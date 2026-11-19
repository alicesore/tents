using Tents.Core;
using Tents.Shell.Hosting;

namespace Tents.App;

/// <summary>A tent's window, as <see cref="TentWindowSet"/> sees it.</summary>
internal interface ITentWindow
{
    TentId TentId { get; }

    /// <summary>The native window handle, or 0 once it is gone.</summary>
    nint Handle { get; }

    /// <summary>Shows the tent's new state (title, bounds, items).</summary>
    void Update(Tent tent);

    /// <summary>Closes the window. Safe to call on a window that is already gone.</summary>
    void Close();
}

/// <summary>Creates and shows tent windows, attached to the desktop.</summary>
internal interface ITentWindowFactory
{
    ITentWindow Create(Tent tent);
}

/// <summary>What <see cref="TentWindowSet.OnExplorerRestarted"/> did, for the log.</summary>
internal readonly record struct ExplorerRestartSummary(int Reattached, int AlreadyAttached, int Recreated, int WaitingForDesktop);

/// <summary>
/// Keeps exactly one window per tent of the current layout. Runs on the UI thread.
/// </summary>
/// <remarks>
/// <para><b>Explorer restarts</b> (spike 1, failure mode 2). Each window is re-attached to the new desktop window if it
/// still exists, and a new window is created for each tent whose window is gone. This is idempotent: <c>TaskbarCreated</c>
/// also arrives without a restart.</para>
/// <para><b>Windows destroyed from outside</b> (with their owner) report it through <see cref="WindowDestroyed"/>; the
/// tent gets a new window at the next <see cref="OnExplorerRestarted"/> or <see cref="Sync"/>.</para>
/// </remarks>
internal sealed class TentWindowSet
{
    private readonly IDesktopHost _host;
    private readonly ITentWindowFactory _factory;
    private readonly Dictionary<TentId, ITentWindow> _windows = [];

    public TentWindowSet(IDesktopHost host, ITentWindowFactory factory)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(factory);
        _host = host;
        _factory = factory;
    }

    public int Count => _windows.Count;

    public bool TryGet(TentId id, out ITentWindow window) => _windows.TryGetValue(id, out window!);

    public IEnumerable<ITentWindow> Windows => _windows.Values;

    /// <summary>Creates windows for new tents, updates the others, and closes the windows of deleted tents.</summary>
    public void Sync(Layout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        foreach (var id in _windows.Keys.Where(id => !layout.ContainsTent(id)).ToList())
        {
            _windows.Remove(id, out var window);
            window!.Close();
        }

        foreach (var tent in layout.Tents)
        {
            if (_windows.TryGetValue(tent.Id, out var window))
            {
                window.Update(tent);
            }
            else
            {
                _windows[tent.Id] = _factory.Create(tent);
            }
        }
    }

    /// <summary>A window was destroyed without <see cref="Sync"/> or <see cref="CloseAll"/> closing it.</summary>
    public void WindowDestroyed(ITentWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_windows.TryGetValue(window.TentId, out var current) && ReferenceEquals(current, window))
        {
            _windows.Remove(window.TentId);
        }
    }

    /// <summary>Re-attaches every window to the new desktop window, and recreates the ones that are gone.</summary>
    public ExplorerRestartSummary OnExplorerRestarted(Layout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        int reattached = 0, already = 0, recreated = 0, waiting = 0;
        foreach (var tent in layout.Tents)
        {
            var result = _windows.TryGetValue(tent.Id, out var window) ? _host.Reattach(window.Handle) : ReattachResult.WindowGone;
            switch (result)
            {
                case ReattachResult.Reattached:
                    reattached++;
                    break;
                case ReattachResult.AlreadyAttached:
                    already++;
                    break;
                case ReattachResult.NoDesktop:
                    waiting++;
                    break;
                case ReattachResult.WindowGone:
                    window?.Close();
                    _windows[tent.Id] = _factory.Create(tent);
                    recreated++;
                    break;
            }
        }

        return new ExplorerRestartSummary(reattached, already, recreated, waiting);
    }

    public void CloseAll()
    {
        var windows = _windows.Values.ToList();
        _windows.Clear();
        foreach (var window in windows)
        {
            window.Close();
        }
    }
}
