using System.Windows;
using Tents.Core;
using Tents.Shell.Hosting;

namespace Tents.App;

/// <summary>
/// The composition root: owns the layout, the tent windows and the controller window, and reacts to system
/// notifications. Runs on the UI thread; nothing here may block it (spike 1, failure mode 1).
/// </summary>
internal sealed class TentsApp : ITentCommands, ITentWindowFactory, IDisposable
{
    private readonly Application _application;
    private readonly AppOptions _options;
    private readonly AppLog _log;
    private readonly IDesktopHost _host;
    private TentWindowSet? _windows;
    private ControllerWindow? _controller;
    private Layout? _layout;
    private bool _shuttingDown;

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
        _layout = CreateFirstLayout(monitors);
        _windows = new TentWindowSet(_host, this);
        _windows.Sync(_layout);
        _log.Write($"Showing {_windows.Count} tent(s) on {monitors.Count} monitor(s).");
    }

    public void Exit() => Shutdown("Exit");

    /// <summary>Releases what <see cref="Start"/> created, if <see cref="Exit"/> did not already.</summary>
    public void Dispose()
    {
        _shuttingDown = true;
        _windows?.CloseAll();
        _controller?.Dispose();
    }

    ITentWindow ITentWindowFactory.Create(Tent tent)
    {
        var window = new TentWindow(tent, _host, this);
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

    private void OnControllerEvent(object? sender, ControllerEvent e)
    {
        if (_shuttingDown || _layout is null || _windows is null)
        {
            return;
        }

        switch (e)
        {
            case ControllerEvent.ExplorerRestarted:
                var summary = _windows.OnExplorerRestarted(_layout);
                _log.Write($"TaskbarCreated: {summary.Reattached} re-attached, {summary.AlreadyAttached} already attached, " +
                    $"{summary.Recreated} recreated, {summary.WaitingForDesktop} waiting for a desktop window.");
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

        _shuttingDown = true;
        _log.Write($"Shutdown ({reason}).");
        _windows?.CloseAll();
        _controller?.Dispose();
        _application.Shutdown(0);
    }
}
