using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tents.Shell.Hosting;

/// <summary>
/// The <c>owned-pin</c> strategy from spike 1: a top-level tool window owned by the shell's desktop window, with every
/// z-order change rewritten to <c>HWND_BOTTOM</c> and clicks that never activate it.
/// </summary>
/// <remarks>
/// <para><b>Why it works.</b> For an owned window, "bottom" means "directly above its owner", so the tent sits directly
/// above the desktop and below everything else. When Show Desktop raises the desktop window, the window manager moves
/// its owned windows with it, so the tent stays visible without any code reacting to it. No hooks, no timers.</para>
/// <para><b>Cost of the owner.</b> The owner is on Explorer's thread, which attaches the input queues: a hung tent UI
/// thread also blocks mouse input to the desktop (measured in spike 1). Nothing may block the tent UI thread.</para>
/// <para><b>Explorer restarts.</b> The desktop window is a new window after a restart. <see cref="Reattach"/> sets the new
/// one as owner if the tent still exists (observed in spike 1), or reports <see cref="ReattachResult.WindowGone"/> so the
/// app creates a new tent window (the documented outcome: an owned window is destroyed with its owner).</para>
/// <para>Sources: <see href="https://learn.microsoft.com/windows/win32/winmsg/window-features#owned-windows">Owned
/// Windows</see>, <see href="https://learn.microsoft.com/windows/win32/winmsg/wm-windowposchanging">WM_WINDOWPOSCHANGING</see>,
/// <see href="https://learn.microsoft.com/windows/win32/inputdev/wm-mouseactivate">WM_MOUSEACTIVATE</see>,
/// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getshellwindow">GetShellWindow</see>,
/// <see href="https://learn.microsoft.com/windows/win32/winmsg/extended-window-styles">Extended Window Styles</see>.</para>
/// </remarks>
public sealed unsafe class OwnedPinHost : IDesktopHost
{
    public const int WmWindowPosChanging = 0x0046;
    public const int WmMouseActivate = 0x0021;
    public const nint MaNoActivate = 3;
    public const nint WsExToolWindow = 0x00000080;
    public const nint WsExNoActivate = 0x08000000;

    private readonly Func<nint> _getShellWindow;

    public OwnedPinHost()
        : this(static () => PInvoke.GetShellWindow())
    {
    }

    /// <param name="getShellWindow">The desktop window lookup; tests pass their own window.</param>
    internal OwnedPinHost(Func<nint> getShellWindow)
    {
        ArgumentNullException.ThrowIfNull(getShellWindow);
        _getShellWindow = getShellWindow;
    }

    public string Name => "owned-pin";

    public nint GetOwnerForNewWindow() => _getShellWindow();

    public void Prepare(nint hwnd)
    {
        var style = PInvoke.GetWindowLongPtr((HWND)hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLongPtr((HWND)hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, style | WsExToolWindow | WsExNoActivate);
    }

    public bool ProcessMessage(nint hwnd, int message, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        switch (message)
        {
            case WmWindowPosChanging when lParam != 0:
                ZOrderPin.PinToBottom(ref *(WINDOWPOS*)lParam);
                return false; // The window still processes the (adjusted) change.

            case WmMouseActivate:
                result = MaNoActivate;
                return true;

            default:
                return false;
        }
    }

    public ReattachResult Reattach(nint hwnd)
    {
        if (hwnd == 0 || !PInvoke.IsWindow((HWND)hwnd))
        {
            return ReattachResult.WindowGone;
        }

        var shell = _getShellWindow();
        if (shell == 0)
        {
            return ReattachResult.NoDesktop;
        }

        var result = ReattachResult.AlreadyAttached;
        if (GetOwner(hwnd) != shell)
        {
            // Documented as "ownership cannot be transferred", observed to work in spike 1. The app recreates the window
            // when it is gone, which is the documented path.
            PInvoke.SetWindowLongPtr((HWND)hwnd, WINDOW_LONG_PTR_INDEX.GWLP_HWNDPARENT, shell);
            result = ReattachResult.Reattached;
        }

        PInvoke.SetWindowPos(
            (HWND)hwnd,
            (HWND)ZOrderPin.HwndBottom,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
        return result;
    }

    /// <summary>The window's owner (<c>GW_OWNER</c>), or 0.</summary>
    public static nint GetOwner(nint hwnd) => PInvoke.GetWindow((HWND)hwnd, GET_WINDOW_CMD.GW_OWNER);
}

/// <summary>The z-order rule of <see cref="OwnedPinHost"/>. Pure.</summary>
internal static class ZOrderPin
{
    /// <summary>HWND_BOTTOM.</summary>
    public const nint HwndBottom = 1;

    /// <summary>Rewrites any z-order change in a pending <c>SetWindowPos</c> to "bottom". Other changes pass unchanged.</summary>
    /// <returns>True when the z-order was rewritten.</returns>
    public static bool PinToBottom(ref WINDOWPOS position)
    {
        if ((position.flags & SET_WINDOW_POS_FLAGS.SWP_NOZORDER) != 0)
        {
            return false;
        }

        position.hwndInsertAfter = (HWND)HwndBottom;
        return true;
    }
}
