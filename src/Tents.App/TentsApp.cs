using System.Windows;
using Tents.App.Items;
using Tents.Core;
using Tents.Core.Persistence;
using Tents.Shell.Desktop;
using Tents.Shell.DragDrop;
using Tents.Shell.Hosting;
using Tents.Shell.IconLayer;
using Tents.Shell.Icons;
using Tents.Shell.Menus;
using Tents.Shell.Tray;

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
    private LayoutPersistence? _persistence;
    private NativeIconLayer? _iconLayer;
    private IconLayerGuard? _guard;
    private TrayIcon? _tray;
    private bool _paused;
    private bool _iconsHiddenOnce;
    private Layout? _savedLayout;
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

    /// <summary>
    /// Creates the controller, restores the layout and shows the tent windows. Call once, on the UI thread, from
    /// <c>Application.Startup</c>. The layout file is read on the thread pool; the UI thread only awaits it.
    /// </summary>
    public async void Start()
    {
        _log.Write($"Start: host {_host.Name}, {(_options.HardwareRendering ? "hardware" : "software")} rendering.");
        if (_options.Unknown.Count > 0)
        {
            _log.Write($"Ignored {_options.Unknown.Count} unknown argument(s).");
        }

        _controller = new ControllerWindow(_log);
        _controller.EventReceived += OnControllerEvent;
        _controller.TrayMenuRequested += ShowTrayMenu;
        _tray = new TrayIcon(_controller.Handle, ReadIcon(), TrayText.Tooltip(paused: false));
        if (!_tray.Add())
        {
            _log.Write("No taskbar yet; the tray icon is added on TaskbarCreated.");
        }

        // An earlier run that died with the native icons off is cleaned up first, whatever this run does.
        _guard = new IconLayerGuard(new DesktopIconSwitch(_source), new IconLayerStateFile());
        _iconLayer = new NativeIconLayer(_guard, _log, _options.HideNativeIcons);
        await _iconLayer.RecoverAsync();

        _persistence = new LayoutPersistence(new LayoutFileStore(_options.LayoutFile ?? LayoutFileStore.DefaultFilePath), _log);
        var saved = await _persistence.LoadAsync();
        if (_shuttingDown)
        {
            return;
        }

        var monitors = Monitors.GetAll();
        var layout = LayoutPersistence.Choose(saved, MonitorKey(monitors), () => CreateFirstLayout(monitors), out var choice);
        layout = LayoutPersistence.BringOnScreen(layout, monitors, out var moved);
        _log.Write($"Layout: {choice}, {layout.Tents.Length} tent(s), {layout.ItemCount} item(s); {moved} tent(s) moved on screen.");
        _state = DesktopState.Create(layout);
        _savedLayout = choice == LayoutChoice.Saved && moved == 0 ? layout : null; // anything else is saved once
        _windows = new TentWindowSet(_host, this);
        SyncWindows();
        _log.Write($"Showing {_windows.Count} tent(s) on {monitors.Count} monitor(s); native icons {(_iconLayer.SwitchingEnabled ? "switched off once the items are read" : "left on")}.");

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

        _tray?.Dispose();
        _tray = null;

        // Only when the app ends without Shutdown: icons back on before the desktop view goes away (off the UI thread,
        // bounded).
        if (_iconLayer is { OwnsIconsOff: true } iconLayer)
        {
            iconLayer.ShowNativeIconsAsync().Wait(TimeSpan.FromSeconds(3));
        }

        _guard?.Dispose();
        _actions.Dispose();
        _icons.Dispose();
        _source.Dispose();

        // Only when the app ends without Shutdown (Application.Run returned on its own): the UI thread is done, so the
        // bounded wait for the last save cannot block a tent.
        var persistence = _persistence;
        _persistence = null;
        persistence?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
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

    public void TitleClicked(TentId tent)
    {
        if (_state is null || !_state.Layout.TryGetTent(tent, out var current))
        {
            return;
        }

        _state = _state.WithLayout(_state.Layout.SetTentRolledUp(tent, !current.IsRolledUp));
        SyncWindows();
    }

    public void NewTent(TentId from)
    {
        if (_state is null || !_state.Layout.TryGetTent(from, out var source))
        {
            return;
        }

        var id = TentId.New();
        var bounds = TentPlacement.ForNewTent(source.Bounds, Monitors.GetAll());
        _state = _state.WithLayout(_state.Layout.CreateTent(id, TentText.NewTentTitle, bounds));
        SyncWindows();
        _log.Write($"New tent; {_state.Layout.Tents.Length} tent(s).");
        if (_windows?.TryGet(id, out var window) == true && window is TentWindow tentWindow)
        {
            tentWindow.BeginRename();
        }
    }

    public void RenameTent(TentId tent, string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (_state is null || !_state.Layout.ContainsTent(tent))
        {
            return;
        }

        _state = _state.WithLayout(_state.Layout.RenameTent(tent, title));
        SyncWindows();
    }

    public bool CanDelete(TentId tent) => _state is not null && tent != _state.Layout.DefaultTentId && _state.Layout.ContainsTent(tent);

    public void DeleteTent(TentId tent)
    {
        if (_state is null || !CanDelete(tent))
        {
            return;
        }

        var target = _state.Layout.GetTent(tent);
        var question = TentText.DeleteQuestion(target.Title, _state.ItemsOf(target).Count, _state.Layout.DefaultTent.Title);

        // A modal message box pumps messages, so the tents and the desktop stay responsive while it is open.
        var answer = MessageBox.Show(question, TentText.DeleteCaption, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes || _state is null || !CanDelete(tent))
        {
            return;
        }

        _state = _state.WithLayout(_state.Layout.DeleteTent(tent));
        SyncWindows();
        _log.Write($"Deleted a tent; {_state.Layout.Tents.Length} tent(s).");
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

    /// <summary>The layout before any is saved: the default tent on the primary monitor.</summary>
    internal static Layout CreateFirstLayout(IReadOnlyList<MonitorInfo> monitors)
    {
        var bounds = TentPlacement.ForFirstTent(monitors.FirstOrDefault(m => m.IsPrimary));
        return Layout.CreateWithDefaultTent(MonitorKey(monitors), new Tent(TentId.New(), "Desktop", bounds));
    }

    /// <summary>The layout key for a set of monitors: device names and bounds.</summary>
    internal static MonitorConfigKey MonitorKey(IReadOnlyList<MonitorInfo> monitors) =>
        MonitorConfigKey.Create(monitors.Select(m => new MonitorDescriptor(m.DeviceName, m.Bounds)));

    private void OnSnapshot(DesktopSnapshot snapshot, bool foldersHaveVisibleEntries)
    {
        if (_shuttingDown || _state is null)
        {
            return;
        }

        _state = _state.WithSnapshot(snapshot, out var ignored, foldersHaveVisibleEntries);
        if (ignored)
        {
            _log.Write("Ignored an empty read of the desktop view (Explorer busy); the next read decides.");
            return;
        }

        _icons.RemoveAllExcept(_state.Items.Keys.ToHashSet());
        SyncWindows();
        _log.Write($"Desktop read: {snapshot.Count} item(s) in {_state.Layout.Tents.Length} tent(s).");

        // The native icons go off only after the tents have the items: right after the switch the view lists nothing
        // for a moment, and a first read then would find an empty desktop.
        if (!_iconsHiddenOnce && !_paused && _iconLayer is not null)
        {
            _iconsHiddenOnce = true;
            _ = _iconLayer.HideNativeIconsAsync();
        }
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

    /// <summary>Shows the current state in the windows and saves the layout if it changed.</summary>
    private void SyncWindows()
    {
        if (_state is not { } state)
        {
            return;
        }

        _windows?.Sync(state.Layout, state.ItemsOf);
        if (!ReferenceEquals(state.Layout, _savedLayout) && _persistence is not null)
        {
            _savedLayout = state.Layout;
            _persistence.Schedule(state.Layout);
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
                _tray?.Add(); // the new taskbar has no icons
                RecheckIconLayer(e);
                break;

            case ControllerEvent.DisplayChanged:
                // Layouts per monitor configuration are M2 step 3; M1 keeps the tents where they are.
                _log.Write($"Display changed: {Monitors.GetAll().Count} monitor(s).");
                break;

            case ControllerEvent.SessionLocked:
                _log.Write($"{e}.");
                break;

            case ControllerEvent.SessionUnlocked:
            case ControllerEvent.Resumed:
                _log.Write($"{e}.");
                RecheckIconLayer(e);
                break;

            case ControllerEvent.SessionEnding:
                // Raised inside WM_ENDSESSION: the process may end when it returns, so this is the one place that waits.
                // Both run off the UI thread (desktop view thread, thread pool), so waiting cannot deadlock; both are
                // bounded. The icons come back so a sign-in without Tents does not find an empty desktop.
                _log.Write("Session ending; native icons back on, saving the layout.");
                _iconLayer?.ShowNativeIconsAsync().Wait(TimeSpan.FromSeconds(3));
                _persistence?.FlushAsync().Wait(TimeSpan.FromSeconds(3));
                break;

            case ControllerEvent.CloseRequested:
                Shutdown("WM_CLOSE");
                break;
        }
    }

    // ---- Tray and Pause (M1 step 19) ----

    /// <summary>
    /// Pause: the native icons come back and every tent is hidden, so the plain desktop is back. Unpause: the tents
    /// show again, then the native icons go off. The order means the desktop is never empty in between, at most briefly
    /// doubled.
    /// </summary>
    public async void SetPaused(bool paused, string reason)
    {
        if (_shuttingDown || paused == _paused || _windows is null || _iconLayer is null)
        {
            return;
        }

        _paused = paused;
        _log.Write($"{(paused ? "Paused" : "Resumed")} ({reason}).");
        _tray?.SetTooltip(TrayText.Tooltip(paused));
        if (paused)
        {
            await _iconLayer.ShowNativeIconsAsync();
            if (_paused && !_shuttingDown)
            {
                _windows.SetVisible(false);
            }

            if (_persistence is not null)
            {
                await _persistence.FlushAsync();
            }
        }
        else
        {
            _windows.SetVisible(true);
            await _iconLayer.HideNativeIconsAsync();
        }
    }

    private void ShowTrayMenu(int x, int y)
    {
        if (_shuttingDown || _controller is null || _state is null)
        {
            return;
        }

        var chosen = TrayIcon.ShowMenu(_controller.Handle, TrayText.Menu(_paused), x, y);
        switch (chosen)
        {
            case TrayText.NewTentCommand:
                NewTent(_state.Layout.DefaultTentId);
                break;
            case TrayText.PauseCommand:
                SetPaused(!_paused, "tray");
                break;
            case TrayText.ExitCommand:
                Exit();
                break;
        }
    }

    /// <summary>Part B step 8: if the user turned Show desktop icons back on, that is Pause.</summary>
    private async void RecheckIconLayer(ControllerEvent reason)
    {
        if (_paused || _iconLayer is null || !await _iconLayer.UserTurnedIconsOnAsync())
        {
            return;
        }

        SetPaused(true, $"Show desktop icons was turned on, noticed at {reason}");
    }

    private static byte[] ReadIcon()
    {
        using var stream = typeof(TentsApp).Assembly.GetManifestResourceStream("Tents.ico")
            ?? throw new InvalidOperationException("The Tents.ico resource is missing.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private async void Shutdown(string reason)
    {
        if (_shuttingDown)
        {
            return;
        }

        _log.Write($"Shutdown ({reason}).");
        if (_iconLayer is not null)
        {
            try
            {
                // First, before anything is torn down: the plain desktop is back whatever happens next.
                await _iconLayer.ShowNativeIconsAsync().WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (TimeoutException)
            {
                _log.Write("Turning the native icons on did not finish in time; the state file stays for the next start.");
            }
        }

        var persistence = _persistence;
        _persistence = null; // saved below, without blocking the UI thread
        Dispose();
        if (persistence is not null)
        {
            try
            {
                // Awaited, not waited on: the UI thread keeps pumping while the last save runs.
                await persistence.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (TimeoutException)
            {
                _log.Write("The last layout save did not finish in time.");
            }
        }

        _application.Shutdown(0);
    }
}
