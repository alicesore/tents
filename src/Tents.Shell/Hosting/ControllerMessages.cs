using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace Tents.Shell.Hosting;

/// <summary>A system notification the controller window acts on.</summary>
public enum ControllerEvent
{
    /// <summary>Not a message the controller handles.</summary>
    None,

    /// <summary>
    /// <c>TaskbarCreated</c>: Explorer (re)started, so the desktop window, the desktop view and the tray are new. Windows 10
    /// also sends it when the primary display's DPI changes, so every reaction must be idempotent.
    /// </summary>
    ExplorerRestarted,

    /// <summary><c>WM_DISPLAYCHANGE</c>: resolution or monitor set changed.</summary>
    DisplayChanged,

    /// <summary><c>WM_WTSSESSION_CHANGE</c> with <c>WTS_SESSION_LOCK</c>.</summary>
    SessionLocked,

    /// <summary><c>WM_WTSSESSION_CHANGE</c> with <c>WTS_SESSION_UNLOCK</c>.</summary>
    SessionUnlocked,

    /// <summary><c>WM_POWERBROADCAST</c> with <c>PBT_APMRESUMEAUTOMATIC</c>: the machine woke from sleep or hibernation.</summary>
    Resumed,

    /// <summary><c>WM_ENDSESSION</c> with <c>wParam</c> TRUE: the session is ending (sign-out, restart, shutdown).</summary>
    SessionEnding,

    /// <summary><c>WM_CLOSE</c> sent to the controller window: exit, as Exit in the tray does.</summary>
    CloseRequested,
}

/// <summary>
/// Turns the controller window's messages into <see cref="ControllerEvent"/> values. Pure apart from registering the
/// <c>TaskbarCreated</c> message once.
/// </summary>
/// <remarks>
/// <para>The controller is a hidden top-level window, not a message-only window: <c>TaskbarCreated</c>,
/// <c>WM_DISPLAYCHANGE</c> and <c>WM_POWERBROADCAST</c> are broadcast to top-level windows only.</para>
/// <para>Sources: <see href="https://learn.microsoft.com/windows/win32/shell/taskbar#taskbar-creation-notification">Taskbar
/// Creation Notification</see>, <see href="https://learn.microsoft.com/windows/win32/gdi/wm-displaychange">WM_DISPLAYCHANGE</see>,
/// <see href="https://learn.microsoft.com/windows/win32/termserv/wm-wtssession-change">WM_WTSSESSION_CHANGE</see>,
/// <see href="https://learn.microsoft.com/windows/win32/power/wm-powerbroadcast">WM_POWERBROADCAST</see>,
/// <see href="https://learn.microsoft.com/windows/win32/power/pbt-apmresumeautomatic">PBT_APMRESUMEAUTOMATIC</see>
/// (sent on every resume; <c>PBT_APMRESUMESUSPEND</c> only follows when the user is present, so it is not used, which
/// avoids reacting twice), <see href="https://learn.microsoft.com/windows/win32/shutdown/wm-endsession">WM_ENDSESSION</see>.</para>
/// </remarks>
public sealed class ControllerMessages
{
    public const int WmClose = 0x0010;
    public const int WmEndSession = 0x0016;
    public const int WmDisplayChange = 0x007E;
    public const int WmPowerBroadcast = 0x0218;
    public const int WmWtsSessionChange = 0x02B1;
    public const int PbtApmResumeAutomatic = 0x0012;
    public const int WtsSessionLock = 0x7;
    public const int WtsSessionUnlock = 0x8;

    /// <param name="taskbarCreatedMessage">The registered <c>TaskbarCreated</c> message id.</param>
    public ControllerMessages(int taskbarCreatedMessage)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(taskbarCreatedMessage);
        TaskbarCreatedMessage = taskbarCreatedMessage;
    }

    /// <summary>The registered <c>TaskbarCreated</c> message id (0xC000 to 0xFFFF).</summary>
    public int TaskbarCreatedMessage { get; }

    /// <summary>Registers <c>TaskbarCreated</c> (the same id in every process of the session).</summary>
    /// <exception cref="InvalidOperationException">Registration failed.</exception>
    public static ControllerMessages Register()
    {
        var id = (int)PInvoke.RegisterWindowMessage("TaskbarCreated");
        return id != 0 ? new ControllerMessages(id) : throw new InvalidOperationException("RegisterWindowMessage(TaskbarCreated) failed.");
    }

    public ControllerEvent Classify(int message, nint wParam)
    {
        if (message == TaskbarCreatedMessage)
        {
            return ControllerEvent.ExplorerRestarted;
        }

        return message switch
        {
            WmDisplayChange => ControllerEvent.DisplayChanged,
            WmWtsSessionChange => (int)wParam switch
            {
                WtsSessionLock => ControllerEvent.SessionLocked,
                WtsSessionUnlock => ControllerEvent.SessionUnlocked,
                _ => ControllerEvent.None,
            },
            WmPowerBroadcast when (int)wParam == PbtApmResumeAutomatic => ControllerEvent.Resumed,
            WmEndSession when wParam != 0 => ControllerEvent.SessionEnding,
            WmClose => ControllerEvent.CloseRequested,
            _ => ControllerEvent.None,
        };
    }
}

/// <summary>Session lock and unlock notifications (<c>WTSRegisterSessionNotification</c>) for one window.</summary>
public static class SessionNotifications
{
    /// <summary>NOTIFY_FOR_THIS_SESSION.</summary>
    private const uint _notifyForThisSession = 0;

    /// <summary>SYNCHRONIZE access right.</summary>
    private const uint _synchronize = 0x00100000;

    /// <summary>RPC_S_INVALID_BINDING: Terminal Services is not ready yet (early at sign-in).</summary>
    public const int NotReadyError = 1702;

    /// <summary>
    /// The event Windows sets once session notifications can be registered. Wait on it after
    /// <see cref="NotReadyError"/>, then register again.
    /// </summary>
    public const string ReadyEventName = @"Global\TermSrvReadyEvent";

    /// <summary>
    /// Registers <paramref name="hwnd"/> for this session's notifications. Returns 0, or the Win32 error
    /// (<see cref="NotReadyError"/> early at sign-in). Source:
    /// <see href="https://learn.microsoft.com/windows/win32/api/wtsapi32/nf-wtsapi32-wtsregistersessionnotification">WTSRegisterSessionNotification</see>.
    /// </summary>
    public static int Register(nint hwnd) =>
        PInvoke.WTSRegisterSessionNotification((HWND)hwnd, _notifyForThisSession)
            ? 0
            : System.Runtime.InteropServices.Marshal.GetLastPInvokeError();

    /// <summary>
    /// Opens <see cref="ReadyEventName"/> for waiting only (<c>SYNCHRONIZE</c>), or returns null when it does not exist.
    /// </summary>
    public static WaitHandle? OpenReadyEvent()
    {
        using var handle = PInvoke.OpenEvent((SYNCHRONIZATION_ACCESS_RIGHTS)_synchronize, false, ReadyEventName);
        if (handle.IsInvalid)
        {
            return null;
        }

        // Hand the handle to a WaitHandle, which closes it on dispose.
        var owned = new SafeWaitHandle(handle.DangerousGetHandle(), ownsHandle: true);
        handle.SetHandleAsInvalid();
        return new ManualResetEvent(false) { SafeWaitHandle = owned };
    }

    /// <summary>Removes the registration. Call before the window is destroyed.</summary>
    public static void Unregister(nint hwnd) => PInvoke.WTSUnRegisterSessionNotification((HWND)hwnd);
}
