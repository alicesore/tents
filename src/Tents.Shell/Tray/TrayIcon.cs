using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tents.Shell.Tray;

/// <summary>One entry of the tray menu.</summary>
/// <param name="Id">Command id returned when chosen; 0 for a separator.</param>
public sealed record TrayMenuItem(int Id, string Text, bool Checked = false, bool Enabled = true)
{
    public static TrayMenuItem Separator { get; } = new(0, string.Empty);

    public bool IsSeparator => Id == 0;
}

/// <summary>What a tray callback message asks for.</summary>
public enum TrayRequest
{
    None,

    /// <summary>Show the menu (right-click, left-click, or keyboard selection of the icon).</summary>
    Menu,
}

/// <summary>
/// Tents' notification-area icon, owned by the controller window: added at start and again after
/// <c>TaskbarCreated</c> (the tray loses its icons when Explorer restarts), removed on exit.
/// </summary>
/// <remarks>
/// <para>Uses <c>NOTIFYICON_VERSION_4</c>: the callback's <c>lParam</c> carries the event, <c>wParam</c> the anchor point
/// for the menu. The menu is a plain Win32 popup menu shown with <c>TPM_RETURNCMD</c> on the controller window, which is
/// made the foreground window first and sent <c>WM_NULL</c> afterwards, as documented for notification-area menus.</para>
/// <para>Idle cost: none. The icon raises callbacks only when the user interacts with it.</para>
/// <para>Sources: <see href="https://learn.microsoft.com/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw">Shell_NotifyIcon</see>,
/// <see href="https://learn.microsoft.com/windows/win32/api/shellapi/ns-shellapi-notifyicondataw">NOTIFYICONDATA</see>,
/// <see href="https://learn.microsoft.com/windows/win32/shell/notification-area">Notifications and the Notification Area</see>,
/// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-trackpopupmenuex">TrackPopupMenuEx</see> (remarks on
/// the foreground window and WM_NULL).</para>
/// </remarks>
public sealed unsafe class TrayIcon : IDisposable
{
    /// <summary>The callback message the controller window receives for the icon (WM_APP + 0x40).</summary>
    public const int CallbackMessage = 0x8000 + 0x40;

    private const uint _iconId = 1;
    private const uint _notifyIconVersion4 = 4;
    private const int _wmContextMenu = 0x007B;
    private const int _ninSelect = 0x0400;
    private const int _ninKeySelect = 0x0401;
    private const int _wmNull = 0x0000;
    private const uint _iconResourceVersion = 0x00030000;

    private readonly nint _window;
    private HICON _icon;
    private string _tooltip;
    private bool _added;

    /// <param name="window">The controller window that receives <see cref="CallbackMessage"/>.</param>
    /// <param name="icoFile">The bytes of an <c>.ico</c> file.</param>
    /// <param name="tooltip">Text shown when the pointer rests on the icon (up to 127 characters).</param>
    public TrayIcon(nint window, byte[] icoFile, string tooltip)
    {
        ArgumentNullException.ThrowIfNull(icoFile);
        ArgumentNullException.ThrowIfNull(tooltip);
        _window = window;
        _tooltip = tooltip;
        _icon = CreateIcon(icoFile, PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON));
    }

    public bool IsAdded => _added;

    /// <summary>
    /// Adds the icon (or adds it again after an Explorer restart). Returns false while there is no taskbar to add it to;
    /// try again on the next <c>TaskbarCreated</c>.
    /// </summary>
    public bool Add()
    {
        var data = Data(NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE | NOTIFY_ICON_DATA_FLAGS.NIF_ICON | NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP);
        PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, &data); // after a restart the old entry is gone; harmless otherwise
        _added = PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, &data);
        if (_added)
        {
            data.Anonymous.uVersion = _notifyIconVersion4;
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, &data);
        }

        return _added;
    }

    /// <summary>Changes the tooltip text.</summary>
    public void SetTooltip(string tooltip)
    {
        ArgumentNullException.ThrowIfNull(tooltip);
        _tooltip = tooltip;
        if (_added)
        {
            var data = Data(NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP);
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, &data);
        }
    }

    /// <summary>Removes the icon from the notification area.</summary>
    public void Remove()
    {
        if (_added)
        {
            var data = Data(0);
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, &data);
            _added = false;
        }
    }

    public void Dispose()
    {
        Remove();
        if (!_icon.IsNull)
        {
            PInvoke.DestroyIcon(_icon);
            _icon = default;
        }
    }

    /// <summary>What a <see cref="CallbackMessage"/> asks for, and the anchor point (physical screen pixels). Pure.</summary>
    public static (TrayRequest Request, int X, int Y) Classify(nint wParam, nint lParam)
    {
        var e = (int)(lParam & 0xFFFF);
        var x = (short)(wParam & 0xFFFF);
        var y = (short)((wParam >> 16) & 0xFFFF);
        return e is _wmContextMenu or _ninSelect or _ninKeySelect ? (TrayRequest.Menu, x, y) : (TrayRequest.None, x, y);
    }

    /// <summary>
    /// Shows a popup menu at a screen point and returns the chosen item's id, or 0 when dismissed. Runs a modal menu
    /// loop that keeps pumping messages.
    /// </summary>
    public static int ShowMenu(nint owner, IReadOnlyList<TrayMenuItem> items, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(items);
        var menu = PInvoke.CreatePopupMenu();
        try
        {
            foreach (var item in items)
            {
                if (item.IsSeparator)
                {
                    PInvoke.AppendMenu(menu, MENU_ITEM_FLAGS.MF_SEPARATOR, 0, default(PCWSTR));
                    continue;
                }

                var flags = MENU_ITEM_FLAGS.MF_STRING
                    | (item.Checked ? MENU_ITEM_FLAGS.MF_CHECKED : MENU_ITEM_FLAGS.MF_UNCHECKED)
                    | (item.Enabled ? MENU_ITEM_FLAGS.MF_ENABLED : MENU_ITEM_FLAGS.MF_GRAYED);
                fixed (char* text = item.Text)
                {
                    PInvoke.AppendMenu(menu, flags, (nuint)item.Id, text);
                }
            }

            PInvoke.SetForegroundWindow((HWND)owner);
            var chosen = PInvoke.TrackPopupMenuEx(
                menu,
                (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON | TRACK_POPUP_MENU_FLAGS.TPM_BOTTOMALIGN),
                x,
                y,
                (HWND)owner,
                null).Value;
            PInvoke.PostMessage((HWND)owner, _wmNull, default, default);
            return chosen;
        }
        finally
        {
            PInvoke.DestroyMenu(menu);
        }
    }

    private static HICON CreateIcon(byte[] icoFile, int pixelSize)
    {
        var image = IcoFile.Pick(IcoFile.ReadDirectory(icoFile), pixelSize);
        fixed (byte* bits = &icoFile[image.Offset])
        {
            var icon = PInvoke.CreateIconFromResourceEx(bits, (uint)image.Length, true, _iconResourceVersion, pixelSize, pixelSize, IMAGE_FLAGS.LR_DEFAULTCOLOR);
            return icon.IsNull ? throw new InvalidOperationException($"CreateIconFromResourceEx failed ({Marshal.GetLastPInvokeError()}).") : icon;
        }
    }

    private NOTIFYICONDATAW Data(NOTIFY_ICON_DATA_FLAGS flags)
    {
        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = (HWND)_window,
            uID = _iconId,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = _icon,
        };
        var tip = _tooltip.AsSpan(0, Math.Min(_tooltip.Length, 127));
        tip.CopyTo(data.szTip.AsSpan());
        data.szTip.AsSpan()[tip.Length] = '\0';
        return data;
    }
}
