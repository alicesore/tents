using System.Runtime.InteropServices;
using Tents.Shell.Interop;
using Tents.Shell.Threading;

namespace Tents.Shell.Desktop;

/// <summary>
/// Reads the list of desktop items from the desktop's native view, off the caller's thread. Read-only: it never
/// changes, moves, renames or deletes anything on the desktop.
/// </summary>
/// <remarks>
/// <para><b>Threading.</b> All shell work runs on one dedicated STA thread (<see cref="StaWorker"/>), so a slow or
/// hung Explorer never blocks a tent UI thread. Calls never block the caller. Nothing runs while nobody asks: no
/// timers, no polling.</para>
/// <para><b>Explorer restarts.</b> The view is a proxy into Explorer's process and dies with it. Call
/// <see cref="InvalidateView"/> when <c>TaskbarCreated</c> arrives; the next read acquires the new shell's view. A read
/// that finds the old proxy disconnected also acquires a new view once, by itself.</para>
/// <para><b>Lag.</b> The native view lists a new file about 1.3 s after the file system reports it (spike 2). Use the
/// file watcher (M1.7) for speed and this list as the reference for which items exist.</para>
/// </remarks>
public sealed class DesktopItemSource : IDisposable
{
    private readonly StaWorker _worker = new("Tents desktop view");
    private readonly string? _folder;
    private DesktopFolderView? _view; // Owned by the worker thread.
    private int _invalidated;
    private bool _disposed;

    /// <summary>A source for the desktop's native view.</summary>
    public DesktopItemSource()
    {
    }

    private DesktopItemSource(string folder) => _folder = folder;

    /// <summary>
    /// A stand-in source for measurement and tests: lists the visible entries of <paramref name="folder"/>, sorted by
    /// name, instead of the desktop's view. It never acquires the desktop view, so nothing built on it can read or
    /// change the native desktop.
    /// </summary>
    public static DesktopItemSource ForFolder(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        return new DesktopItemSource(Path.GetFullPath(folder));
    }

    /// <summary>The stand-in folder, or null for the desktop's native view.</summary>
    public string? Folder => _folder;

    /// <summary>Reads every item the desktop (or the stand-in folder) shows.</summary>
    /// <exception cref="DesktopUnavailableException">The shell has no desktop window right now.</exception>
    public Task<DesktopSnapshot> ReadAsync()
    {
        if (_folder is not { } folder)
        {
            return InvokeWithViewAsync(view => view.ReadItems());
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        return _worker.InvokeAsync(() => ReadFolder(folder));
    }

    /// <summary>
    /// Builds items for paths the file watcher reported before the native view lists them (about 1.3 s later), so a
    /// tent can show a new file at once. Hidden and system files are left out, as the desktop does by default; paths the
    /// shell cannot parse (already gone) are skipped. The items' ID lists are absolute, not the view's; the next read of
    /// the native view replaces them.
    /// </summary>
    public Task<IReadOnlyList<DesktopItem>> ResolveAsync(IReadOnlyList<Core.ItemId> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var list = ids.ToList();
        return _worker.InvokeAsync<IReadOnlyList<DesktopItem>>(() =>
        {
            var items = new List<DesktopItem>(list.Count);
            foreach (var id in list)
            {
                if (IsHiddenFile(id.Value))
                {
                    continue;
                }

                if (DesktopItemFactory.FromParsingName(id.Value) is { } item)
                {
                    items.Add(item);
                }
            }

            return items;
        });
    }

    /// <summary>
    /// Drops the current view, so the next read acquires the desktop view of the shell that is running then. Call
    /// after <c>TaskbarCreated</c>. Cheap and never blocks.
    /// </summary>
    public void InvalidateView() => Volatile.Write(ref _invalidated, 1);

    /// <summary>Runs <paramref name="work"/> on the shell thread with a live view.</summary>
    /// <exception cref="InvalidOperationException">This is a stand-in folder source, which has no desktop view.</exception>
    internal Task<T> InvokeWithViewAsync<T>(Func<DesktopFolderView, T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_folder is not null)
        {
            throw new InvalidOperationException("A stand-in folder source never touches the desktop's view.");
        }

        return _worker.InvokeAsync(() => WithView(work));
    }

    /// <summary>The visible entries of a folder as items, sorted by name. Call on an STA thread.</summary>
    internal static DesktopSnapshot ReadFolder(string folder)
    {
        var options = new EnumerationOptions { AttributesToSkip = FileAttributes.Hidden | FileAttributes.System, IgnoreInaccessible = true };
        var items = new List<DesktopItem>();
        var skipped = 0;
        foreach (var path in Directory.EnumerateFileSystemEntries(folder, "*", options).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (DesktopItemFactory.FromParsingName(path) is { } item)
            {
                items.Add(item);
            }
            else
            {
                skipped++;
            }
        }

        return new DesktopSnapshot(items, skipped);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _ = _worker.InvokeAsync(ReleaseView);
        _worker.Dispose();
    }

    private T WithView<T>(Func<DesktopFolderView, T> work)
    {
        if (Interlocked.Exchange(ref _invalidated, 0) == 1)
        {
            ReleaseView();
        }

        _view ??= DesktopFolderView.TryAcquire() ?? throw new DesktopUnavailableException();
        try
        {
            return work(_view);
        }
        catch (Exception ex) when (ComErrors.IsDisconnected(ex.HResult))
        {
            // Explorer restarted since the view was acquired. Try once with the new shell; no retry loop.
            ReleaseView();
            _view = DesktopFolderView.TryAcquire() ?? throw new DesktopUnavailableException(ex);
            return work(_view);
        }
    }

    /// <summary>True for an existing file or folder with the hidden or system attribute.</summary>
    internal static bool IsHiddenFile(string path)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path))
            {
                return false; // a virtual item's ::{CLSID} name
            }

            var attributes = File.GetAttributes(path);
            return (attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false; // gone or unreadable: the parse below decides
        }
    }

    private void ReleaseView()
    {
        _view?.Dispose();
        _view = null;
    }
}

/// <summary>The shell has no desktop window, for example while Explorer restarts. Try again on <c>TaskbarCreated</c>.</summary>
public sealed class DesktopUnavailableException : Exception
{
    public DesktopUnavailableException()
        : base("The desktop's shell view is not available.")
    {
    }

    public DesktopUnavailableException(Exception innerException)
        : base("The desktop's shell view is not available.", innerException)
    {
    }

    public DesktopUnavailableException(string message)
        : base(message)
    {
    }

    public DesktopUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
