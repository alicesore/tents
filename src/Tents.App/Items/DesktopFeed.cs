using System.Windows.Threading;
using Tents.Core;
using Tents.Shell.Desktop;

namespace Tents.App.Items;

/// <summary>
/// Delivers the desktop's items to the UI thread: full reads of the native view, and the watcher's faster batches. All
/// shell and file work runs on the shell layer's own threads; the UI thread only receives finished results.
/// </summary>
/// <remarks>
/// <para><b>When it reads.</b> At start, when the watcher says the native view has caught up with a burst, when a batch
/// lost events, and after an Explorer restart. Never on a timer. Reads are coalesced: a request while a read runs causes
/// exactly one more read after it.</para>
/// <para><b>Read-only.</b> Nothing here changes the desktop.</para>
/// </remarks>
internal sealed class DesktopFeed : IAsyncDisposable
{
    private readonly DesktopItemSource _source;
    private readonly DesktopWatcher _watcher;
    private readonly Dispatcher _dispatcher;
    private readonly AppLog _log;
    private bool _reading;
    private bool _readAgain;
    private bool _disposed;

    public DesktopFeed(DesktopItemSource source, AppLog log)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(log);
        _source = source;
        _log = log;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _watcher = new DesktopWatcher(DesktopWatcher.GetDesktopFolders());
        _watcher.ChangesReady += (_, batch) => _dispatcher.BeginInvoke(() => OnBatch(batch));
        _watcher.NativeViewSettled += (_, _) => _dispatcher.BeginInvoke(RequestRead);
    }

    /// <summary>
    /// A full read of the native view finished. Raised on the UI thread. For an empty read, the flag says whether the
    /// desktop folders still have visible entries (then the read is not to be trusted); it is false otherwise.
    /// </summary>
    public event Action<DesktopSnapshot, bool>? SnapshotRead;

    /// <summary>A watcher batch, with its new paths resolved to items. Raised on the UI thread.</summary>
    public event Action<DesktopChangeBatch, IReadOnlyList<DesktopItem>>? BatchReady;

    /// <summary>Starts watching and reads the desktop once.</summary>
    public void Start()
    {
        var folders = _watcher.Start();
        _log.Write($"Watching {folders} desktop folder(s).");
        RequestRead();
    }

    /// <summary>Explorer restarted: the next read uses the new shell's view.</summary>
    public void OnExplorerRestarted()
    {
        _source.InvalidateView();
        RequestRead();
    }

    /// <summary>Reads the native view, now or right after the read in progress.</summary>
    public async void RequestRead()
    {
        if (_disposed)
        {
            return;
        }

        if (_reading)
        {
            _readAgain = true;
            return;
        }

        _reading = true;
        try
        {
            do
            {
                _readAgain = false;
                DesktopSnapshot snapshot;
                try
                {
                    snapshot = await _source.ReadAsync(); // resumes on the UI thread
                }
                catch (DesktopUnavailableException)
                {
                    _log.Write("Desktop view not available; waiting for TaskbarCreated.");
                    break;
                }
                catch (Exception ex) when (ex is not ObjectDisposedException)
                {
                    _log.Write($"Desktop read failed: {ex.GetType().Name} 0x{ex.HResult:X8}.");
                    break;
                }

                var foldersHaveVisibleEntries = snapshot.Count == 0
                    && await Task.Run(() => DesktopWatcher.GetDesktopFolders().Any(HasVisibleEntry));

                if (_disposed)
                {
                    return;
                }

                SnapshotRead?.Invoke(snapshot, foldersHaveVisibleEntries);
            }
            while (_readAgain && !_disposed);
        }
        catch (ObjectDisposedException)
        {
            // Shutting down.
        }
        finally
        {
            _reading = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await _watcher.DisposeAsync().ConfigureAwait(false);
    }

    private async void OnBatch(DesktopChangeBatch batch)
    {
        if (_disposed)
        {
            return;
        }

        if (batch.RescanNeeded)
        {
            RequestRead();
        }

        IReadOnlyList<DesktopItem> added = [];
        var paths = batch.Added.Concat(batch.Renamed.Select(r => r.New)).ToList();
        if (paths.Count > 0)
        {
            try
            {
                added = await _source.ResolveAsync(paths);
            }
            catch (Exception ex) when (ex is not ObjectDisposedException)
            {
                _log.Write($"Resolving {paths.Count} new item(s) failed: {ex.GetType().Name}; the next read adds them.");
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }

        if (!_disposed)
        {
            BatchReady?.Invoke(batch, added);
        }
    }

    /// <summary>True when a folder has an entry that is neither hidden nor a system file. Runs on the thread pool.</summary>
    internal static bool HasVisibleEntry(string folder)
    {
        try
        {
            var options = new EnumerationOptions { AttributesToSkip = FileAttributes.Hidden | FileAttributes.System, IgnoreInaccessible = true };
            return Directory.EnumerateFileSystemEntries(folder, "*", options).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true; // cannot tell: do not trust an empty read
        }
    }

    /// <summary>The ids a drop of <paramref name="sources"/> creates on the user's Desktop folder (same file names).</summary>
    public static IReadOnlyList<ItemId> ExpectedDesktopIds(IEnumerable<ItemId> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return ExpectedIds(sources, desktop);
    }

    /// <summary>Pure part of <see cref="ExpectedDesktopIds"/>.</summary>
    internal static IReadOnlyList<ItemId> ExpectedIds(IEnumerable<ItemId> sources, string desktopFolder)
    {
        if (string.IsNullOrEmpty(desktopFolder))
        {
            return [];
        }

        var ids = new List<ItemId>();
        foreach (var source in sources)
        {
            if (Path.IsPathFullyQualified(source.Value) && Path.GetFileName(source.Value.TrimEnd('\\', '/')) is { Length: > 0 } name)
            {
                ids.Add(new ItemId(Path.Combine(desktopFolder, name)));
            }
        }

        return ids;
    }
}
