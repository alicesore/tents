using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tents.Core;
using Tents.Shell.Desktop;
using Tents.Shell.Threading;

namespace Tents.Shell.Icons;

/// <summary>
/// Desktop item icons as frozen WPF bitmaps, loaded once per item and pixel size and shared by every tent.
/// </summary>
/// <remarks>
/// <para><b>Keys.</b> An entry is keyed by item id and exact pixel size (<see cref="IconKey"/>). Each monitor's DPI
/// gives its own pixel size (48 px at 100%, 96 px at 200%), so nothing is scaled at render time.</para>
/// <para><b>Sharing.</b> Many items have the same icon (every <c>.txt</c> file, for example). After extraction the
/// pixels are hashed (SHA-256, width, height) and identical images share one frozen bitmap. The entry stays keyed by
/// item, so invalidating one item never affects another.</para>
/// <para><b>Threading.</b> <see cref="GetAsync"/> never blocks. Extraction and bitmap creation run on one dedicated
/// STA thread: shell icon handlers expect an STA, and creating the bitmaps there keeps WPF from creating a dispatcher
/// on thread-pool threads. Bitmaps are frozen, so any UI thread can draw them. Nothing runs while nobody asks.</para>
/// <para><b>Failures.</b> An item without an image is cached as null, so it is not asked for again until
/// <see cref="Invalidate"/>.</para>
/// <para><b>Eviction.</b> None by size: the desktop's items are a bounded set. The app removes items that are gone
/// (<see cref="RemoveAllExcept"/>) and invalidates items whose file changed (M1.7).</para>
/// </remarks>
public sealed class IconCache : IDisposable
{
    private readonly IIconSource _source;
    private readonly StaWorker _worker;
    private readonly Lock _gate = new();
    private readonly Dictionary<IconKey, Entry> _entries = [];
    private readonly Dictionary<ContentKey, SharedBitmap> _shared = [];
    private bool _disposed;

    /// <param name="kind">Thumbnails where available (as the desktop shows), or icons only.</param>
    public IconCache(IconKind kind = IconKind.IconOrThumbnail)
        : this(new ShellIconSource(kind))
    {
    }

    internal IconCache(IIconSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _worker = new StaWorker("Tents icons");
    }

    /// <summary>
    /// The icon of <paramref name="item"/> at <paramref name="pixelSize"/> pixels, or null when it has none. Requests
    /// for the same key share one load.
    /// </summary>
    public Task<BitmapSource?> GetAsync(DesktopItem item, int pixelSize)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize);
        var key = new IconKey(item.Id, pixelSize);
        Entry entry;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.TryGetValue(key, out var existing))
            {
                return existing.Completion.Task;
            }

            entry = new Entry();
            _entries.Add(key, entry);
        }

        _ = _worker.InvokeAsync(() => Load(item, key, entry));
        return entry.Completion.Task;
    }

    /// <summary>The icon if it has finished loading. Never starts a load.</summary>
    public bool TryGetCached(ItemId id, int pixelSize, out BitmapSource? icon)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(new IconKey(id, pixelSize), out var entry) && entry.Completion.Task.IsCompletedSuccessfully)
            {
                icon = entry.Completion.Task.Result;
                return true;
            }
        }

        icon = null;
        return false;
    }

    /// <summary>Forgets every size of one item, so the next request loads it again (its file changed).</summary>
    public void Invalidate(ItemId id)
    {
        lock (_gate)
        {
            foreach (var key in _entries.Keys.Where(k => k.Id == id).ToList())
            {
                RemoveLocked(key);
            }
        }
    }

    /// <summary>Forgets every item not in <paramref name="keep"/> (items no longer on the desktop).</summary>
    public void RemoveAllExcept(IReadOnlySet<ItemId> keep)
    {
        ArgumentNullException.ThrowIfNull(keep);
        lock (_gate)
        {
            foreach (var key in _entries.Keys.Where(k => !keep.Contains(k.Id)).ToList())
            {
                RemoveLocked(key);
            }
        }
    }

    /// <summary>Forgets everything, for example after a theme change.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _shared.Clear();
        }
    }

    /// <summary>Counts for diagnostics and the memory budget.</summary>
    /// <summary>
    /// Unloads the in-process shell handlers (icon and thumbnail providers) that the icon thread loaded and no longer
    /// uses, so their code leaves the working set until an icon needs them again. COM unloads a server only when it
    /// reports that none of its objects are alive (<c>DllCanUnloadNow</c>), which is what Explorer relies on too. Runs on
    /// the icon thread, which loaded them. Source:
    /// <see href="https://learn.microsoft.com/windows/win32/api/combaseapi/nf-combaseapi-cofreeunusedlibrariesex">CoFreeUnusedLibrariesEx</see>.
    /// </summary>
    public Task ReleaseUnusedHandlersAsync()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        return _worker.InvokeAsync(static () =>
        {
            Windows.Win32.PInvoke.CoFreeUnusedLibrariesEx(0, 0);
            return true;
        });
    }

    public IconCacheStatistics GetStatistics()
    {
        lock (_gate)
        {
            return new IconCacheStatistics(
                _entries.Count,
                _shared.Count,
                _shared.Values.Sum(s => (long)s.Bitmap.PixelWidth * s.Bitmap.PixelHeight * 4));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _entries.Clear();
            _shared.Clear();
        }

        _worker.Dispose();
        _source.Dispose();
    }

    /// <summary>A frozen <c>Pbgra32</c> bitmap of <paramref name="image"/>.</summary>
    internal static BitmapSource CreateFrozenBitmap(IconImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Pbgra32, null, image.Pixels, image.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    // Worker thread.
    private void Load(DesktopItem item, IconKey key, Entry entry)
    {
        try
        {
            var image = _source.Extract(item, key.PixelSize);
            entry.Completion.TrySetResult(image is null ? null : Share(key, entry, image));
        }
        catch (Exception ex) // A broken icon handler must not take the cache down; the caller sees the failure.
        {
            lock (_gate)
            {
                // Let a later request try again.
                if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                {
                    _entries.Remove(key);
                }
            }

            entry.Completion.TrySetException(ex);
        }
    }

    // Worker thread. Bitmaps are created only here, so the check-then-create below cannot race with itself.
    private BitmapSource Share(IconKey key, Entry entry, IconImage image)
    {
        var content = ContentKey.Of(image);
        lock (_gate)
        {
            var stillWanted = _entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry);
            if (_shared.TryGetValue(content, out var shared))
            {
                if (stillWanted)
                {
                    shared.RefCount++;
                    entry.Content = content;
                }

                return shared.Bitmap;
            }
        }

        var bitmap = CreateFrozenBitmap(image);
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry) && !_disposed)
            {
                _shared[content] = new SharedBitmap(bitmap) { RefCount = 1 };
                entry.Content = content;
            }

            // Otherwise the item was invalidated during the load: hand the bitmap to this caller, keep nothing.
        }

        return bitmap;
    }

    private void RemoveLocked(IconKey key)
    {
        if (!_entries.Remove(key, out var entry) || entry.Content is not { } content)
        {
            return;
        }

        if (_shared.TryGetValue(content, out var shared) && --shared.RefCount == 0)
        {
            _shared.Remove(content);
        }
    }

    private sealed class Entry
    {
        public TaskCompletionSource<BitmapSource?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ContentKey? Content { get; set; }
    }

    private sealed class SharedBitmap(BitmapSource bitmap)
    {
        public BitmapSource Bitmap { get; } = bitmap;

        public int RefCount { get; set; }
    }

    private readonly record struct ContentKey(int Width, int Height, Guid Hash)
    {
        public static ContentKey Of(IconImage image)
        {
            Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
            SHA256.HashData(image.Pixels, hash);
            return new ContentKey(image.Width, image.Height, new Guid(hash[..16]));
        }
    }
}

/// <summary>An icon cache key: one item at one exact pixel size.</summary>
public readonly record struct IconKey(ItemId Id, int PixelSize);

/// <summary>Icon cache counts.</summary>
/// <param name="Entries">Item and size pairs requested and not removed.</param>
/// <param name="DistinctBitmaps">Frozen bitmaps held; identical images count once.</param>
/// <param name="PixelBytes">Pixel memory of those bitmaps (4 bytes per pixel).</param>
public sealed record IconCacheStatistics(int Entries, int DistinctBitmaps, long PixelBytes);

/// <summary>Where the cache gets images. Called on the cache's STA thread, one call at a time.</summary>
internal interface IIconSource : IDisposable
{
    IconImage? Extract(DesktopItem item, int pixelSize);
}

/// <summary>Images from the shell (<see cref="ShellIconExtractor"/>).</summary>
internal sealed class ShellIconSource(IconKind kind) : IIconSource
{
    public IconImage? Extract(DesktopItem item, int pixelSize) => ShellIconExtractor.Extract(item.IdList, pixelSize, kind);

    public void Dispose()
    {
    }
}
