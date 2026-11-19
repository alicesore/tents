using System.Windows;
using Tents.App.Items;
using Tents.Core;
using Tents.Shell.Desktop;
using Tents.Shell.DragDrop;
using Tents.Shell.Hosting;
using Tents.Shell.Icons;
using Tents.Shell.Menus;

namespace Tents.App;

/// <summary>
/// The composition root: owns the desktop state, the tent windows, the shell services and the controller window, and
/// reacts to system notifications. Runs on the UI thread; nothing here may block it (spike 1, failure mode 1).
/// </summary>
internal sealed class TentsApp : ITentCommands, ITentWindowFactory, IDisposable
{
    private readonly Application _application;
    private readonly AppOptions _options;
    private readonly AppLog _log;
    private readonly IDesktopHost _host;
    private readonly DesktopItemSource _source = new();
    private readonly IconCache _icons = new();
    private readonly ShellActions _actions = new();
    private DesktopFeed? _feed;
    private TentWindowSet? _windows;
    private ControllerWindow? _controller;
    private DesktopState? _state;
    private bool _shuttingDown;
    private bool _disposed;

    public TentsApp(Application application, AppOptions options, AppLog log, IDesktopHost host)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(host);
        _application = application;
        _options = options;
        _log = log;
        _host = host;
    }

    /// <summary>Creates the controller and the tent windows. Call once, on the UI thread, from <c>Application.Startup</c>.</summary>
    public void Start()
    {
        _log.Write($"Start: host {_host.Name}, {(_options.HardwareRendering ? "hardware" : "software")} rendering.");
        if (_options.Unknown.Count > 0)
        {
            _log.Write($"Ignored {_options.Unknown.Count} unknown argument(s).");
        }

        _controller = new ControllerWindow(_log);
        _controller.EventReceived += OnControllerEvent;

        var monitors = Monitors.GetAll();
        _state = DesktopState.Create(CreateFirstLayout(monitors));
        _windows = new TentWindowSet(_host, this);
        SyncWindows();
        _log.Write($"Showing {_windows.Count} tent(s) on {monitors.Count} monitor(s).");

        // Items arrive asynchronously; the tents are on screen before the first read finishes.
        _feed = new DesktopFeed(_source, _log);
        _feed.SnapshotRead += OnSnapshot;
        _feed.BatchReady += OnBatch;
        _feed.Start();
    }

    public void Exit() => Shutdown("Exit");

    /// <summary>Releases what <see cref="Start"/> created, if <see cref="Exit"/> did not already.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shuttingDown = true;
        _windows?.CloseAll();
        _controller?.Dispose();
        var feed = _feed;
        _feed = null;
        if (feed is not null)
        {
            _ = feed.DisposeAsync().AsTask();
        }

        _actions.Dispose();
        _icons.Dispose();
        _source.Dispose();
    }

    // ---- ITentCommands ----

    public void TentClicked(TentId tent)
    {
        foreach (var window in _windows?.Windows ?? [])
        {
            if (window.TentId != tent)
            {
                window.ClearSelection();
            }
        }
    }

    public async void Open(DesktopItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        try
        {
            var result = await _actions.OpenAsync(item);
            if (!result.Succeeded)
            {
                _log.Write($"Open failed: Win32 error {result.Win32Error}.");
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async void ShowItemMenu(IReadOnlyList<DesktopItem> items, System.Drawing.Point screenPoint, ContextMenuOptions options)
    {
        try
        {
            var result = await _actions.ShowContextMenuAsync(items, screenPoint, options);
            if (result.Outcome == ContextMenuOutcome.HostVerb)
            {
                // Renaming in place is not part of M1 step 16 (see the PR); the shell's other verbs all work.
                _log.Write("Rename chosen; in-place rename is not available yet.");
            }
            else if (result.Outcome == ContextMenuOutcome.Failed)
            {
                _log.Write($"Context menu failed for {items.Count} item(s).");
            }
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dropped(TentId tent, TentDrop drop, int insertionIndex)
    {
        ArgumentNullException.ThrowIfNull(drop);
        if (_state is null || !_state.Layout.ContainsTent(tent))
        {
            return;
        }

        switch (drop.Route)
        {
            case DropRoute.Reassign:
                // Only membership changes; no file is touched.
                _state = _state.Reassign(drop.Items, tent, insertionIndex);
                SyncWindows();
                break;

            case DropRoute.ForwardToDesktop:
                // The shell put the files on the desktop; the watcher reports them next.
                _state = _state.ExpectItems(DesktopFeed.ExpectedDesktopIds(drop.Items), tent);
                break;
        }

        _log.Write($"Drop of {drop.Items.Count} item(s): {drop.Route}, {drop.Effect}.");
    }

    public void BoundsChanged(TentId tent, PixelRect bounds)
    {
        if (_state is null || !_state.Layout.ContainsTent(tent) || !bounds.HasPositiveSize)
        {
            return;
        }

        _state = _state.WithLayout(_state.Layout.SetTentBounds(tent, bounds));
        SyncWindows();
    }

    ITentWindow ITentWindowFactory.Create(Tent tent, IReadOnlyList<DesktopItem> items)
    {
        var window = new TentWindow(tent, items, _host, _icons, this, _options.SnapGrid);
        window.Closed += (_, _) =>
        {
            if (!_shuttingDown)
            {
                _log.Write("A tent window was destroyed from outside; it is recreated on the next TaskbarCreated.");
                _windows?.WindowDestroyed(window);
            }
        };
        window.Attach();
        return window;
    }

    /// <summary>The layout before any is saved: the default tent on the primary monitor. Persistence is M1 step 18.</summary>
    internal static Layout CreateFirstLayout(IReadOnlyList<MonitorInfo> monitors)
    {
        var key = MonitorConfigKey.Create(monitors.Select(m => new MonitorDescriptor(m.DeviceName, m.Bounds)));
        var bounds = TentPlacement.ForFirstTent(monitors.FirstOrDefault(m => m.IsPrimary));
        return Layout.CreateWithDefaultTent(key, new Tent(TentId.New(), "Desktop", bounds));
    }

    private void OnSnapshot(DesktopSnapshot snapshot)
    {
        if (_shuttingDown || _state is null)
        {
            return;
        }

        _state = _state.WithSnapshot(snapshot, out var ignored);
        if (ignored)
        {
            _log.Write("Ignored an empty read of the desktop view (Explorer busy); the next read decides.");
            return;
        }

        _icons.RemoveAllExcept(_state.Items.Keys.ToHashSet());
        SyncWindows();
        _log.Write($"Desktop read: {snapshot.Count} item(s) in {_state.Layout.Tents.Length} tent(s).");
    }

    private void OnBatch(DesktopChangeBatch batch, IReadOnlyList<DesktopItem> added)
    {
        if (_shuttingDown || _state is null)
        {
            return;
        }

        foreach (var id in batch.Changed.Concat(batch.Renamed.Select(r => r.Old)).Concat(batch.Removed))
        {
            _icons.Invalidate(id);
        }

        _state = _state.WithBatch(batch, added);
        SyncWindows();
    }

    private void SyncWindows()
    {
        if (_state is { } state && _windows is not null)
        {
            _windows.Sync(state.Layout, state.ItemsOf);
        }
    }

    private void OnControllerEvent(object? sender, ControllerEvent e)
    {
        if (_shuttingDown || _state is null || _windows is null)
        {
            return;
        }

        switch (e)
        {
            case ControllerEvent.ExplorerRestarted:
                var summary = _windows.OnExplorerRestarted(_state.Layout, _state.ItemsOf);
                _log.Write($"TaskbarCreated: {summary.Reattached} re-attached, {summary.AlreadyAttached} already attached, " +
                    $"{summary.Recreated} recreated, {summary.WaitingForDesktop} waiting for a desktop window.");
                _feed?.OnExplorerRestarted();
                break;

            case ControllerEvent.DisplayChanged:
                // Layouts per monitor configuration are M2 step 3; M1 keeps the tents where they are.
                _log.Write($"Display changed: {Monitors.GetAll().Count} monitor(s).");
                break;

            case ControllerEvent.SessionLocked:
            case ControllerEvent.SessionUnlocked:
            case ControllerEvent.Resumed:
                _log.Write($"{e}.");
                break;

            case ControllerEvent.SessionEnding:
                _log.Write("Session ending.");
                break;

            case ControllerEvent.CloseRequested:
                Shutdown("WM_CLOSE");
                break;
        }
    }

    private void Shutdown(string reason)
    {
        if (_shuttingDown)
        {
            return;
        }

        _log.Write($"Shutdown ({reason}).");
        Dispose();
        _application.Shutdown(0);
    }
}
