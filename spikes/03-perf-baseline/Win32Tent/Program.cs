// THROWAWAY SPIKE CODE for M0 spike 3 (performance baseline). Not production. See spikes/README.md.
//
// The comparison point for WPF: bare Win32 windows painted with GDI, hosted the same way (owned by the desktop
// window, pinned to the bottom). Written with blittable P/Invoke only, so it also compiles with NativeAOT.
//
//   --tents=N  --items=M  --trim  --marker=path     (same meaning as WpfTent)
using System.Runtime.InteropServices;

internal static unsafe class Program
{
    [StructLayout(LayoutKind.Sequential)]
    struct WNDCLASSEXW { public uint cbSize, style; public delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr> lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public char* lpszMenuName, lpszClassName; public IntPtr hIconSm; }
    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }
    [StructLayout(LayoutKind.Sequential)]
    struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }
    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)]
    struct PAINTSTRUCT { public IntPtr hdc; public int fErase; public RECT rcPaint; public int fRestore, fIncUpdate; public fixed byte reserved[32]; }

    [DllImport("user32.dll")] static extern ushort RegisterClassExW(WNDCLASSEXW* c);
    [DllImport("user32.dll")] static extern IntPtr CreateWindowExW(uint ex, char* cls, char* title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern int GetMessageW(MSG* m, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern int TranslateMessage(MSG* m);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessageW(MSG* m);
    [DllImport("user32.dll")] static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] static extern IntPtr LoadCursorW(IntPtr inst, IntPtr name);
    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern int SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern uint RegisterWindowMessageW(char* s);
    [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int idx, IntPtr v);
    [DllImport("user32.dll")] static extern int IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr BeginPaint(IntPtr hwnd, PAINTSTRUCT* ps);
    [DllImport("user32.dll")] static extern int EndPaint(IntPtr hwnd, PAINTSTRUCT* ps);
    [DllImport("user32.dll")] static extern int FillRect(IntPtr dc, RECT* r, IntPtr brush);
    [DllImport("user32.dll")] static extern int DrawTextW(IntPtr dc, char* text, int len, RECT* r, uint format);
    [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(uint colorref);
    [DllImport("gdi32.dll")] static extern int SetBkMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] static extern uint SetTextColor(IntPtr dc, uint color);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleW(IntPtr name);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern int GetProcessTimes(IntPtr p, long* creation, long* exit, long* kernel, long* user);
    [DllImport("kernel32.dll")] static extern void GetSystemTimeAsFileTime(long* t);
    [DllImport("kernel32.dll")] static extern int SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    const uint WS_POPUP = 0x80000000, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40, SWP_NOZORDER = 4;
    const int WM_CLOSE = 0x10, WM_PAINT = 0xF, WM_MOUSEACTIVATE = 0x21, WM_WINDOWPOSCHANGING = 0x46, GWLP_HWNDPARENT = -8;

    static IntPtr s_controller, s_itemBrush, s_lastTent;
    static IntPtr[] s_tents = [];
    static uint s_taskbarCreated;
    static int s_items;
    static bool s_trim, s_markerWritten;
    static string s_marker = "";

    [STAThread]
    static int Main(string[] args)
    {
        string Get(string k, string d) => args.FirstOrDefault(a => a.StartsWith("--" + k + "=", StringComparison.Ordinal))?[(k.Length + 3)..] ?? d;
        int tents = int.Parse(Get("tents", "1"));
        s_items = int.Parse(Get("items", "0"));
        s_trim = args.Contains("--trim");
        s_marker = Get("marker", "");

        var inst = GetModuleHandleW(IntPtr.Zero);
        s_itemBrush = CreateSolidBrush(0x00C08040);
        fixed (char* cls = "TentsSpikePerfWin32", tc = "TaskbarCreated", ctlTitle = "TentsSpike-perf-controller", tentTitle = "TentsSpike-perf")
        {
            s_taskbarCreated = RegisterWindowMessageW(tc);
            var wc = new WNDCLASSEXW { cbSize = (uint)sizeof(WNDCLASSEXW), lpfnWndProc = &WndProc, hInstance = inst, hCursor = LoadCursorW(IntPtr.Zero, 32512), hbrBackground = CreateSolidBrush(0x00604020), lpszClassName = cls };
            if (RegisterClassExW(&wc) == 0) return 1;
            s_controller = CreateWindowExW(WS_EX_TOOLWINDOW, cls, ctlTitle, WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
            var owner = GetShellWindow();
            s_tents = new IntPtr[tents];
            for (int t = 0; t < tents; t++)
            {
                var h = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE, cls, tentTitle, WS_POPUP, 60 + (t % 5) * 440, 80 + (t / 5 % 3) * 420, 420, 400, owner, IntPtr.Zero, inst, IntPtr.Zero);
                s_tents[t] = h;
                s_lastTent = h;
                SetWindowPos(h, 1 /*HWND_BOTTOM*/, 0, 0, 0, 0, 1 | 2 | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
        }
        MSG m;
        while (GetMessageW(&m, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(&m); DispatchMessageW(&m); }
        return 0;
    }

    [UnmanagedCallersOnly]
    static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
    {
        if (hwnd == s_controller)
        {
            if (msg == s_taskbarCreated && s_taskbarCreated != 0)
            {
                var owner = GetShellWindow();
                foreach (var t in s_tents) if (IsWindow(t) != 0) SetWindowLongPtrW(t, GWLP_HWNDPARENT, owner);
            }
            else if (msg == WM_CLOSE) { PostQuitMessage(0); return IntPtr.Zero; }
            return DefWindowProcW(hwnd, msg, w, l);
        }
        switch ((int)msg)
        {
            case WM_WINDOWPOSCHANGING:
                {
                    var wp = (WINDOWPOS*)l;
                    if ((wp->flags & SWP_NOZORDER) == 0) wp->hwndInsertAfter = 1; // HWND_BOTTOM
                    break;
                }
            case WM_MOUSEACTIVATE: return 3; // MA_NOACTIVATE
            case WM_PAINT:
                {
                    PAINTSTRUCT ps;
                    var dc = BeginPaint(hwnd, &ps);
                    SetBkMode(dc, 1); SetTextColor(dc, 0x00FFFFFF);
                    var title = new RECT { L = 8, T = 5, R = 400, B = 24 };
                    fixed (char* s = "Tent (Win32)") DrawTextW(dc, s, -1, &title, 0);
                    for (int i = 0; i < s_items; i++)
                    {
                        int x = 8 + (i % 5) * 80, y = 30 + (i / 5) * 72;
                        var icon = new RECT { L = x + 14, T = y, R = x + 62, B = y + 48 };
                        FillRect(dc, &icon, s_itemBrush);
                        var label = new RECT { L = x, T = y + 50, R = x + 76, B = y + 68 };
                        fixed (char* s = "Item") DrawTextW(dc, s, -1, &label, 1 /*DT_CENTER*/);
                    }
                    EndPaint(hwnd, &ps);
                    if (hwnd == s_lastTent && !s_markerWritten)
                    {
                        s_markerWritten = true;
                        if (s_marker.Length > 0)
                        {
                            long creation, exit, kernel, user, now;
                            GetProcessTimes(GetCurrentProcess(), &creation, &exit, &kernel, &user);
                            GetSystemTimeAsFileTime(&now);
                            File.WriteAllText(s_marker, ((now - creation) / 10000).ToString());
                        }
                        if (s_trim) SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
                    }
                    return IntPtr.Zero;
                }
        }
        return DefWindowProcW(hwnd, msg, w, l);
    }
}
