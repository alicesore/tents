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
    private DesktopFolderView? _view; // Owned by the worker thread.
    private int _invalidated;
    private bool _disposed;

    /// <summary>Reads every item the desktop shows.</summary>
    /// <exception cref="DesktopUnavailableException">The shell has no desktop window right now.</exception>
    public Task<DesktopSnapshot> ReadAsync() => InvokeWithViewAsync(view => view.ReadItems());

    /// <summary>
    /// Drops the current view, so the next read acquires the desktop view of the shell that is running then. Call
    /// after <c>TaskbarCreated</c>. Cheap and never blocks.
    /// </summary>
    public void InvalidateView() => Volatile.Write(ref _invalidated, 1);

    /// <summary>Runs <paramref name="work"/> on the shell thread with a live view.</summary>
    internal Task<T> InvokeWithViewAsync<T>(Func<DesktopFolderView, T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _worker.InvokeAsync(() => WithView(work));
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
