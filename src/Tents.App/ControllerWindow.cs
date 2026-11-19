using System.Windows.Interop;
using System.Windows.Threading;
using Tents.Shell.Hosting;
using Tents.Shell.Tray;

namespace Tents.App;

/// <summary>
/// The hidden top-level window that receives Tents' system notifications: <c>TaskbarCreated</c>,
/// <c>WM_DISPLAYCHANGE</c>, session lock and unlock, resume, end of session, and <c>WM_CLOSE</c>. One per process, on the
/// UI thread.
/// </summary>
/// <remarks>
/// <para>Hidden (never shown), zero-sized, a tool window, so it is in no taskbar or Alt+Tab list. A message-only window
/// would not get the broadcasts. It wakes only when Windows sends it one of these messages.</para>
/// <para><b>Delivery.</b> Events are raised after the message returns (queued on the dispatcher), so a slow reaction never
/// holds up the sender's broadcast. <see cref="ControllerEvent.SessionEnding"/> is the exception: it is raised inside
/// <c>WM_ENDSESSION</c>, because the process may be ended as soon as that message returns.</para>
/// <para><b>Session notifications early at sign-in.</b> <c>WTSRegisterSessionNotification</c> fails while Terminal Services
/// is starting. Then the window waits on <c>Global\TermSrvReadyEvent</c> (a thread-pool wait on a kernel event, no
/// polling) and registers once it is set.</para>
/// </remarks>
internal sealed class ControllerWindow : IDisposable
{
    private const int _wsPopup = unchecked((int)0x80000000);
    private const int _wsExToolWindow = 0x00000080;

    private readonly HwndSource _source;
    private readonly ControllerMessages _messages;
    private readonly Dispatcher _dispatcher;
    private readonly AppLog _log;
    private RegisteredWaitHandle? _readyWait;
    private WaitHandle? _readyEvent;
    private bool _sessionRegistered;
    private bool _disposed;

    public ControllerWindow(AppLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _messages = ControllerMessages.Register();
        _source = new HwndSource(new HwndSourceParameters("Tents controller")
        {
            WindowStyle = _wsPopup, // no WS_VISIBLE: never shown
            ExtendedWindowStyle = _wsExToolWindow,
            Width = 0,
            Height = 0,
        });
        _source.AddHook(Hook);
        RegisterSessionNotifications();
    }

    /// <summary>A notification arrived. Raised on the UI thread.</summary>
    public event EventHandler<ControllerEvent>? EventReceived;

    /// <summary>The user asked for the tray menu, at this screen point (physical pixels). Raised on the UI thread.</summary>
    public event Action<int, int>? TrayMenuRequested;

    public nint Handle => _source.Handle;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _readyWait?.Unregister(null);
        _readyEvent?.Dispose();
        if (_sessionRegistered)
        {
            SessionNotifications.Unregister(_source.Handle);
        }

        _source.RemoveHook(Hook);
        _source.Dispose();
    }

    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == TrayIcon.CallbackMessage)
        {
            handled = true;
            var (request, x, y) = TrayIcon.Classify(wParam, lParam);
            if (request == TrayRequest.Menu)
            {
                _dispatcher.BeginInvoke(() =>
                {
                    if (!_disposed)
                    {
                        TrayMenuRequested?.Invoke(x, y);
                    }
                });
            }

            return 0;
        }

        var e = _messages.Classify(msg, wParam);
        switch (e)
        {
            case ControllerEvent.None:
                return 0;

            case ControllerEvent.SessionEnding:
                Raise(e);
                return 0;

            case ControllerEvent.CloseRequested:
                handled = true; // Do not let the default procedure destroy the controller.
                break;
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Normal, () => Raise(e));
        return 0;
    }

    private void Raise(ControllerEvent e)
    {
        if (!_disposed)
        {
            EventReceived?.Invoke(this, e);
        }
    }

    private void RegisterSessionNotifications()
    {
        if (_disposed || _sessionRegistered)
        {
            return;
        }

        var error = SessionNotifications.Register(_source.Handle);
        if (error == 0)
        {
            _sessionRegistered = true;
            return;
        }

        _log.Write($"Session notifications not registered yet (error {error}).");
        if (error != SessionNotifications.NotReadyError || _readyWait is not null || SessionNotifications.OpenReadyEvent() is not { } ready)
        {
            return;
        }

        _readyEvent = ready;
        _readyWait = ThreadPool.RegisterWaitForSingleObject(
            ready,
            (_, _) => _dispatcher.BeginInvoke(() =>
            {
                _log.Write("Terminal Services ready; registering session notifications.");
                RegisterSessionNotifications();
            }),
            null,
            Timeout.Infinite,
            executeOnlyOnce: true);
    }
}
