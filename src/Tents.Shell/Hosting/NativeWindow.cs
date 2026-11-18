using Tents.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tents.Shell.Hosting;

/// <summary>Window position in physical pixels, for tent windows (per-monitor v2 DPI aware).</summary>
public static class NativeWindow
{
    /// <summary>
    /// Moves and sizes a window without activating it and without changing its z-order (the desktop host keeps that).
    /// </summary>
    public static bool SetBounds(nint hwnd, PixelRect bounds) =>
        PInvoke.SetWindowPos(
            (HWND)hwnd,
            default,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);

    /// <summary>The window's rectangle in physical screen pixels, or null when the window does not exist.</summary>
    public static PixelRect? GetBounds(nint hwnd) =>
        PInvoke.GetWindowRect((HWND)hwnd, out var rect)
            ? new PixelRect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top)
            : null;

    public static bool Exists(nint hwnd) => hwnd != 0 && PInvoke.IsWindow((HWND)hwnd);
}
