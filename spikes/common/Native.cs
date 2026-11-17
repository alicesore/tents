// THROWAWAY SPIKE CODE. Not production. See spikes/README.md.
// Hand-written P/Invoke for the spike prototypes. Production code uses CsWin32 instead.
using System.Runtime.InteropServices;
using System.Text;

namespace Tents.Spikes;

internal static class N
{
    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20, GWLP_HWNDPARENT = -8;
    public const uint WS_CHILD = 0x40000000, WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000, WS_CAPTION = 0x00C00000,
        WS_THICKFRAME = 0x00040000, WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000,
        WS_CLIPCHILDREN = 0x02000000, WS_CLIPSIBLINGS = 0x04000000;
    public const uint WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_APPWINDOW = 0x40000,
        WS_EX_LAYERED = 0x80000, WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20,
        SWP_SHOWWINDOW = 0x40, SWP_HIDEWINDOW = 0x80, SWP_NOOWNERZORDER = 0x200, SWP_NOSENDCHANGING = 0x400, SWP_ASYNCWINDOWPOS = 0x4000;
    public static readonly IntPtr HWND_TOP = IntPtr.Zero, HWND_BOTTOM = new(1), HWND_TOPMOST = new(-1), HWND_NOTOPMOST = new(-2);

    public const uint GW_HWNDFIRST = 0, GW_HWNDLAST = 1, GW_HWNDNEXT = 2, GW_HWNDPREV = 3, GW_OWNER = 4, GW_CHILD = 5;
    public const uint GA_PARENT = 1, GA_ROOT = 2, GA_ROOTOWNER = 3;

    public const int WM_DESTROY = 0x0002, WM_MOVE = 0x0003, WM_SIZE = 0x0005, WM_ACTIVATE = 0x0006, WM_CLOSE = 0x0010,
        WM_QUERYENDSESSION = 0x0011, WM_ENDSESSION = 0x0016, WM_SHOWWINDOW = 0x0018, WM_SETTINGCHANGE = 0x001A,
        WM_MOUSEACTIVATE = 0x0021, WM_WINDOWPOSCHANGING = 0x0046, WM_WINDOWPOSCHANGED = 0x0047, WM_DISPLAYCHANGE = 0x007E,
        WM_NCDESTROY = 0x0082, WM_NCHITTEST = 0x0084, WM_SYSCOMMAND = 0x0112, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202,
        WM_RBUTTONDOWN = 0x0204, WM_PARENTNOTIFY = 0x0210, WM_WTSSESSION_CHANGE = 0x02B1, WM_DPICHANGED = 0x02E0,
        WM_DPICHANGED_AFTERPARENT = 0x02E3, WM_APP = 0x8000, WM_DWMCOMPOSITIONCHANGED = 0x031E, WM_NULL = 0;
    public const int MA_NOACTIVATE = 3;

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_OBJECT_REORDER = 0x8004, EVENT_OBJECT_SHOW = 0x8002,
        EVENT_OBJECT_HIDE = 0x8003, EVENT_OBJECT_LOCATIONCHANGE = 0x800B, EVENT_OBJECT_CLOAKED = 0x8017, EVENT_OBJECT_UNCLOAKED = 0x8018,
        EVENT_SYSTEM_MINIMIZESTART = 0x0016, EVENT_SYSTEM_MINIMIZEEND = 0x0017, EVENT_SYSTEM_DESKTOPSWITCH = 0x0020;
    public const uint WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
    public const int OBJID_WINDOW = 0;

    public const int DWMWA_CLOAKED = 14;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int L, T, R, B; public override readonly string ToString() => $"({L},{T})-({R},{B}) {R - L}x{B - T}"; }
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }

    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    public delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? title);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetTopWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hwnd, ref POINT p);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int idx);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int idx, IntPtr v);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string s);
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr w, IntPtr l, uint flags, uint timeoutMs, out IntPtr result);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc cb, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("wtsapi32.dll", SetLastError = true)] public static extern bool WTSRegisterSessionNotification(IntPtr hwnd, uint flags);
    [DllImport("wtsapi32.dll")] public static extern bool WTSUnRegisterSessionNotification(IntPtr hwnd);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

    public static string Class(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, sb.Capacity); return sb.ToString(); }
    public static string Text(IntPtr h) { var sb = new StringBuilder(256); GetWindowText(h, sb, sb.Capacity); return sb.ToString(); }
    public static uint Pid(IntPtr h) { GetWindowThreadProcessId(h, out var pid); return pid; }
    public static uint Style(IntPtr h) => unchecked((uint)(long)GetWindowLongPtr(h, GWL_STYLE));
    public static uint ExStyle(IntPtr h) => unchecked((uint)(long)GetWindowLongPtr(h, GWL_EXSTYLE));
    public static bool Cloaked(IntPtr h) => DwmGetWindowAttribute(h, DWMWA_CLOAKED, out var v, 4) == 0 && v != 0;
    public static string H(IntPtr h) => "0x" + h.ToInt64().ToString("X");

    /// <summary>The shell's desktop windows, found with documented APIs only.</summary>
    public sealed record DesktopWindows(IntPtr Progman, uint ShellPid, IntPtr DefView, IntPtr DefViewParent, IntPtr ListView, IntPtr[] TopLevelWorkerWs, IntPtr[] ProgmanChildWorkerWs);

    public static DesktopWindows FindDesktop()
    {
        var progman = GetShellWindow();
        var shellPid = progman == IntPtr.Zero ? 0 : Pid(progman);
        var topWorkers = new List<IntPtr>();
        if (progman != IntPtr.Zero)
        {
            EnumWindows((h, _) => { if (Pid(h) == shellPid && Class(h) == "WorkerW") topWorkers.Add(h); return true; }, IntPtr.Zero);
        }
        var childWorkers = new List<IntPtr>();
        for (var c = GetWindow(progman, GW_CHILD); c != IntPtr.Zero; c = GetWindow(c, GW_HWNDNEXT))
            if (Class(c) == "WorkerW") childWorkers.Add(c);

        var defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        var defParent = progman;
        if (defView == IntPtr.Zero)
        {
            foreach (var w in topWorkers)
            {
                defView = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView != IntPtr.Zero) { defParent = w; break; }
            }
        }
        var list = defView == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        return new DesktopWindows(progman, shellPid, defView, defView == IntPtr.Zero ? IntPtr.Zero : defParent, list, topWorkers.ToArray(), childWorkers.ToArray());
    }
}
