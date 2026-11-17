// THROWAWAY SPIKE CODE for M0 spike 1 (desktop layer). Not production. See spikes/README.md.
//
// A bare Win32 window with a solid background, hosted the same ways as DesktopLayerSpike but with no WPF involved.
// Used to tell "the OS does not support this" apart from "WPF does not support this".
//
//   --mode=plain | owned-bottom | owned-pin | child-progman | child-defview | child-workerw
//   --layered=none | attr      (attr: WS_EX_LAYERED + SetLayeredWindowAttributes, alpha 255)
//   --create=direct | setparent (child modes: create as WS_CHILD directly, or create top-level then SetParent)
//   --title=TentsSpike-x  --rect=x,y,w,h  --color=RRGGBB  --log=path
using System.Runtime.InteropServices;
using Tents.Spikes;

internal static unsafe class Program
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEXW { public uint cbSize, style; public IntPtr lpfnWndProc; public int cbClsExtra, cbWndExtra; public IntPtr hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public IntPtr hIconSm; }
    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int ptX, ptY; }
    delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern ushort RegisterClassExW(ref WNDCLASSEXW c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern int GetMessageW(out MSG m, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessageW(ref MSG m);
    [DllImport("user32.dll")] static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr LoadCursorW(IntPtr inst, IntPtr name);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(uint colorref);
    [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandleW(IntPtr name);

    static StreamWriter? s_log;
    static IntPtr s_tent, s_controller;
    static uint s_taskbarCreated;
    static WndProc? s_proc;
    static string[] s_args = [];
    static bool s_pin;

    static string Arg(string k, string d) => s_args.FirstOrDefault(a => a.StartsWith("--" + k + "=", StringComparison.Ordinal))?[(k.Length + 3)..] ?? d;
    static void Log(string m) => s_log?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [win32] {m}");

    [STAThread]
    static int Main(string[] args)
    {
        s_args = args;
        var mode = Arg("mode", "plain");
        var layered = Arg("layered", "none");
        s_pin = mode == "owned-pin";
        var title = Arg("title", "TentsSpike-" + mode);
        var logPath = Arg("log", "");
        if (logPath.Length > 0) s_log = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        var r = Arg("rect", "200,200,420,300").Split(',').Select(int.Parse).ToArray();
        var rgb = Convert.ToUInt32(Arg("color", "FF00C8"), 16);
        var colorref = ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);
        Log($"start mode={mode} layered={layered} create={Arg("create", "direct")} pid={Environment.ProcessId}");

        s_taskbarCreated = N.RegisterWindowMessage("TaskbarCreated");
        s_proc = Proc;
        var inst = GetModuleHandleW(IntPtr.Zero);
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(s_proc), hInstance = inst,
            hCursor = LoadCursorW(IntPtr.Zero, new IntPtr(32512)), hbrBackground = CreateSolidBrush(colorref), lpszClassName = "TentsSpikeWin32",
        };
        if (RegisterClassExW(ref wc) == 0) { Log("RegisterClassEx failed " + Marshal.GetLastWin32Error()); return 1; }

        // Hidden top-level window: receives WM_CLOSE from the test script and broadcasts such as TaskbarCreated.
        s_controller = CreateWindowExW(N.WS_EX_TOOLWINDOW, "TentsSpikeWin32", title + "-controller", N.WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);

        var d = N.FindDesktop();
        IntPtr parent = mode switch
        {
            "child-progman" => d.Progman,
            "child-defview" => d.DefView,
            "child-workerw" => d.ProgmanChildWorkerWs.Length > 0 ? d.ProgmanChildWorkerWs[0] : IntPtr.Zero,
            _ => IntPtr.Zero,
        };
        var ex = N.WS_EX_TOOLWINDOW | N.WS_EX_NOACTIVATE | (layered == "attr" ? N.WS_EX_LAYERED : 0);
        if (mode.StartsWith("child-", StringComparison.Ordinal))
        {
            if (parent == IntPtr.Zero) { Log("no parent window found for " + mode); return 1; }
            var p = new N.POINT { X = r[0], Y = r[1] };
            N.ScreenToClient(parent, ref p);
            if (Arg("create", "direct") == "direct")
            {
                s_tent = CreateWindowExW(ex & ~N.WS_EX_TOOLWINDOW, "TentsSpikeWin32", title, N.WS_CHILD | N.WS_VISIBLE | N.WS_CLIPSIBLINGS, p.X, p.Y, r[2], r[3], parent, IntPtr.Zero, inst, IntPtr.Zero);
                Log($"CreateWindowEx as child of {N.Class(parent)}: hwnd={N.H(s_tent)} err={Marshal.GetLastWin32Error()}");
            }
            else
            {
                s_tent = CreateWindowExW(ex, "TentsSpikeWin32", title, N.WS_POPUP | N.WS_VISIBLE, r[0], r[1], r[2], r[3], IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
                N.SetWindowLongPtr(s_tent, N.GWL_STYLE, (IntPtr)(N.WS_CHILD | N.WS_VISIBLE | N.WS_CLIPSIBLINGS));
                var prev = N.SetParent(s_tent, parent);
                Log($"SetParent to {N.Class(parent)}: prev={N.H(prev)} err={Marshal.GetLastWin32Error()}");
                N.SetWindowPos(s_tent, N.HWND_TOP, p.X, p.Y, r[2], r[3], N.SWP_NOACTIVATE | N.SWP_SHOWWINDOW | N.SWP_FRAMECHANGED);
            }
            if (s_tent != IntPtr.Zero) N.SetWindowPos(s_tent, N.HWND_TOP, 0, 0, 0, 0, N.SWP_NOMOVE | N.SWP_NOSIZE | N.SWP_NOACTIVATE);
        }
        else
        {
            var owner = mode is "owned-bottom" or "owned-pin" ? d.Progman : IntPtr.Zero;
            s_tent = CreateWindowExW(ex, "TentsSpikeWin32", title, N.WS_POPUP, r[0], r[1], r[2], r[3], owner, IntPtr.Zero, inst, IntPtr.Zero);
            Log($"CreateWindowEx top-level owner={N.H(owner)}: hwnd={N.H(s_tent)} err={Marshal.GetLastWin32Error()}");
            N.SetWindowPos(s_tent, owner != IntPtr.Zero ? N.HWND_BOTTOM : N.HWND_TOP, 0, 0, 0, 0, N.SWP_NOMOVE | N.SWP_NOSIZE | N.SWP_NOACTIVATE | N.SWP_SHOWWINDOW);
        }
        if (s_tent == IntPtr.Zero) return 1;
        if (layered == "attr") Log($"SetLayeredWindowAttributes={SetLayeredWindowAttributes(s_tent, 0, 255, 2)} err={Marshal.GetLastWin32Error()}");
        Log($"tent style={N.Style(s_tent):X8} ex={N.ExStyle(s_tent):X8} parent={N.H(N.GetAncestor(s_tent, N.GA_PARENT))} owner={N.H(N.GetWindow(s_tent, N.GW_OWNER))}");

        while (GetMessageW(out var m, IntPtr.Zero, 0, 0) > 0) { TranslateMessage(ref m); DispatchMessageW(ref m); }
        Log("exit");
        return 0;
    }

    static IntPtr Proc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l)
    {
        if (hwnd == s_controller)
        {
            if (msg == s_taskbarCreated) Log($"TaskbarCreated. tentIsWindow={N.IsWindow(s_tent)}");
            if (msg == N.WM_CLOSE)
            {
                Log("WM_CLOSE: clean shutdown");
                if (N.IsWindow(s_tent)) DestroyWindow(s_tent);
                DestroyWindow(hwnd);
                PostQuitMessage(0);
                return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, w, l);
        }
        switch ((int)msg)
        {
            case N.WM_WINDOWPOSCHANGING:
                if (s_pin)
                {
                    // Pin: turn every z-order change into "go to the bottom", which for an owned window means directly above its owner.
                    var wp = (N.WINDOWPOS*)l;
                    if ((wp->flags & N.SWP_NOZORDER) == 0) wp->hwndInsertAfter = N.HWND_BOTTOM;
                }
                break;
            case N.WM_MOUSEACTIVATE: Log("WM_MOUSEACTIVATE -> MA_NOACTIVATE"); return N.MA_NOACTIVATE;
            case N.WM_LBUTTONDOWN: Log("WM_LBUTTONDOWN"); break;
            case N.WM_ACTIVATE: Log($"WM_ACTIVATE {(int)w & 0xFFFF}"); break;
            case N.WM_SIZE: if ((int)w != 0) Log($"WM_SIZE type={w}"); break;
            case N.WM_DESTROY: Log("WM_DESTROY"); break;
        }
        return DefWindowProcW(hwnd, msg, w, l);
    }
}
