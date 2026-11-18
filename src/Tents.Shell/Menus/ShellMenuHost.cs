using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tents.Shell.Menus;

/// <summary>
/// A hidden top-level window on the shell UI thread that owns context menus and forwards menu messages to the shell's
/// <c>IContextMenu2</c>/<c>IContextMenu3</c>, which draw the owner-drawn and lazily filled submenus (Send to, Open with,
/// New). Created on first use, kept for the thread's lifetime so dialogs that a command opens keep their owner.
/// </summary>
/// <remarks>
/// Not a message-only window: a menu owner has to be able to become the foreground window, or the menu does not close
/// when the user clicks elsewhere. See the remarks of
/// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-trackpopupmenuex">TrackPopupMenuEx</see>
/// and <see href="https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-icontextmenu3-handlemenumsg2">IContextMenu3::HandleMenuMsg2</see>.
/// </remarks>
internal static unsafe class ShellMenuHost
{
    private const string _windowClassName = "Tents.ShellMenuHost";

    [ThreadStatic]
    private static HWND _window;

    [ThreadStatic]
    private static IContextMenu2* _menu2;

    [ThreadStatic]
    private static IContextMenu3* _menu3;

    /// <summary>The owner window for this thread, created on first use.</summary>
    public static HWND GetWindow()
    {
        if (!_window.IsNull && PInvoke.IsWindow(_window))
        {
            return _window;
        }

        fixed (char* className = _windowClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProc,
                hInstance = (HINSTANCE)PInvoke.GetModuleHandle((char*)null).Value,
                lpszClassName = className,
            };

            // A second registration from another thread fails with ERROR_CLASS_ALREADY_EXISTS, which is fine.
            PInvoke.RegisterClassEx(&wc);
            _window = PInvoke.CreateWindowEx(
                WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
                className,
                null,
                WINDOW_STYLE.WS_POPUP,
                0,
                0,
                0,
                0,
                default,
                default,
                default,
                null);
        }

        if (_window.IsNull)
        {
            throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastPInvokeError()}.");
        }

        return _window;
    }

    /// <summary>Routes menu messages to <paramref name="menu"/> until the returned scope is disposed.</summary>
    public static Scope Track(IContextMenu* menu)
    {
        IContextMenu2* menu2 = null;
        IContextMenu3* menu3 = null;
        _ = menu->QueryInterface(out menu3);
        if (menu3 is null)
        {
            _ = menu->QueryInterface(out menu2);
        }

        _menu2 = menu2;
        _menu3 = menu3;
        return default;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (message is PInvoke.WM_INITMENUPOPUP or PInvoke.WM_DRAWITEM or PInvoke.WM_MEASUREITEM or PInvoke.WM_MENUCHAR)
            {
                if (_menu3 is not null)
                {
                    LRESULT result;
                    try
                    {
                        _menu3->HandleMenuMsg2(message, wParam, lParam, &result);
                        return result;
                    }
                    catch (Exception ex) when (Interop.ComErrors.IsCallFailure(ex))
                    {
                        // Not handled by the menu: fall through to the default.
                    }
                }
                else if (_menu2 is not null && message != PInvoke.WM_MENUCHAR)
                {
                    try
                    {
                        _menu2->HandleMenuMsg(message, wParam, lParam);
                        return default;
                    }
                    catch (Exception ex) when (Interop.ComErrors.IsCallFailure(ex))
                    {
                        // Not handled by the menu: fall through to the default.
                    }
                }
            }

            return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
        }
        catch (Exception ex) when (ShellPlatform.ReportCallbackException(ex))
        {
            return default;
        }
    }

    /// <summary>Stops routing and releases the menu's extra interfaces.</summary>
    internal readonly struct Scope : IDisposable
    {
        public void Dispose()
        {
            var menu2 = _menu2;
            var menu3 = _menu3;
            _menu2 = null;
            _menu3 = null;
            Interop.Com.Release(ref menu2);
            Interop.Com.Release(ref menu3);
        }
    }
}
