// THROWAWAY SPIKE CODE for M0 spike 1 (desktop layer). Not production. See spikes/README.md.
//
// One WPF "tent" window, hosted on the desktop by the strategy named in --mode, with every relevant
// window message and WinEvent written to a log so that a script can drive the checklist and read results.
//
//   --mode=plain          control: a plain tool window, no desktop handling
//   --mode=bottom         top-level tool window pinned to HWND_BOTTOM, no Show Desktop handling
//   --mode=bottom-react   top-level tool window kept directly above the shell's desktop window (event-driven)
//   --mode=bottom-react2  unowned, pinned to HWND_BOTTOM, lifted above the desktop window only while it is raised
//   --mode=owned          top-level tool window owned by the shell's desktop window
//   --mode=owned-bottom   owned by the desktop window, and sent to HWND_BOTTOM once
//   --mode=owned-pin      owned by the desktop window, and every z-order change is turned into HWND_BOTTOM
//   --mode=child-progman  re-parented (WS_CHILD) under Progman, above the icon view
//   --mode=child-defview  re-parented under SHELLDLL_DefView (the icon view's parent)
//   --mode=child-workerw  re-parented under the wallpaper WorkerW, behind the icons
//   --layered=none|attr|wpf   none: ordinary window; attr: WS_EX_LAYERED + SetLayeredWindowAttributes; wpf: AllowsTransparency
//   --owner=iconhost|progman  owned modes: which shell window owns the tent (default: the one hosting the icon view)
//   --title=TentsSpike-x  --rect=x,y,w,h (physical px)  --color=RRGGBB  --log=path
//
// Event-driven only: no timers, no polling.
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Tents.Spikes;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var o = Options.Parse(args);
        Log.Open(o.LogPath);
        Log.Write("app", $"start mode={o.Mode} layered={o.Layered} rect={o.X},{o.Y},{o.W},{o.H} pid={Environment.ProcessId}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("app", "UNHANDLED " + e.ExceptionObject);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) => Log.Write("app", "DISPATCHER UNHANDLED " + e.Exception);
        var host = new TentHost(o);
        host.Start();
        var rc = app.Run();
        Log.Write("app", "exit rc=" + rc);
        return rc;
    }
}

internal sealed record Options(string Mode, string Layered, string Title, int X, int Y, int W, int H, Color Color, string LogPath, string Owner)
{
    public static Options Parse(string[] args)
    {
        string Get(string k, string d) => args.FirstOrDefault(a => a.StartsWith("--" + k + "=", StringComparison.Ordinal))?[(k.Length + 3)..] ?? d;
        var mode = Get("mode", "plain");
        var r = Get("rect", "200,200,420,300").Split(',').Select(int.Parse).ToArray();
        var c = Convert.ToInt32(Get("color", "FF00C8"), 16);
        return new Options(mode, Get("layered", "none"), Get("title", "TentsSpike-" + mode), r[0], r[1], r[2], r[3],
            Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c), Get("log", Path.Combine(Path.GetTempPath(), "tents-spike1.log")), Get("owner", "iconhost"));
    }
}

internal static class Log
{
    static StreamWriter? _w;
    public static void Open(string path) { _w = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true }; }
    public static void Write(string src, string msg) => _w?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{src}] {msg}");
}

internal sealed class TentHost
{
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

    readonly Options _o;
    readonly uint _taskbarCreated = N.RegisterWindowMessage("TaskbarCreated");
    HwndSource? _controller;
    Window? _tent;
    IntPtr _tentHwnd;
    N.WinEventProc? _winEventProc; // keep the delegate alive
    IntPtr _hookFg, _hookReorder;
    bool _inReassert;
    bool _desktopFg, _raised, _allowZ; // bottom-react2 state
    int _generation;

    public TentHost(Options o) => _o = o;

    bool IsChildMode => _o.Mode.StartsWith("child-", StringComparison.Ordinal);

    public void Start()
    {
        // Hidden TOP-LEVEL window. Broadcasts such as TaskbarCreated and WM_DISPLAYCHANGE go to top-level windows only,
        // so a tent that has been re-parented under the desktop would never see them itself.
        _controller = new HwndSource(new HwndSourceParameters(_o.Title + "-controller") { WindowStyle = unchecked((int)N.WS_POPUP), ExtendedWindowStyle = (int)N.WS_EX_TOOLWINDOW, Width = 0, Height = 0 });
        _controller.AddHook(ControllerHook);
        Log.Write("ctl", $"controller hwnd={N.H(_controller.Handle)} wtsRegister={N.WTSRegisterSessionNotification(_controller.Handle, 0)}");

        _winEventProc = OnWinEvent;
        _hookFg = N.SetWinEventHook(N.EVENT_SYSTEM_FOREGROUND, N.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _winEventProc, 0, 0, N.WINEVENT_OUTOFCONTEXT);
        _hookReorder = N.SetWinEventHook(N.EVENT_OBJECT_REORDER, N.EVENT_OBJECT_REORDER, IntPtr.Zero, _winEventProc, 0, 0, N.WINEVENT_OUTOFCONTEXT | N.WINEVENT_SKIPOWNPROCESS);

        CreateTent();
    }

    void CreateTent()
    {
        _generation++;
        var w = new Window
        {
            Title = _o.Title,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            AllowsTransparency = _o.Layered == "wpf",
            Background = new SolidColorBrush(_o.Color),
            Left = 0, Top = 0, Width = 200, Height = 200,
            Content = new TextBlock { Text = $"Tents spike 1\n{_o.Mode} / layered={_o.Layered}\n(throwaway prototype)", Foreground = Brushes.White, FontSize = 14, Margin = new Thickness(8), VerticalAlignment = VerticalAlignment.Top },
        };
        var gen = _generation;
        w.SourceInitialized += (_, _) =>
        {
            _tentHwnd = new WindowInteropHelper(w).Handle;
            HwndSource.FromHwnd(_tentHwnd)!.AddHook(TentHook);
            var ex = N.ExStyle(_tentHwnd) | N.WS_EX_TOOLWINDOW | N.WS_EX_NOACTIVATE;
            if (_o.Layered == "attr") ex |= N.WS_EX_LAYERED;
            N.SetWindowLongPtr(_tentHwnd, N.GWL_EXSTYLE, (IntPtr)ex);
            if (_o.Layered == "attr") Log.Write("tent", "SetLayeredWindowAttributes=" + SetLayeredWindowAttributes(_tentHwnd, 0, 255, 2 /*LWA_ALPHA*/));
            Log.Write("tent", $"gen={gen} created hwnd={N.H(_tentHwnd)} style={N.Style(_tentHwnd):X8} ex={N.ExStyle(_tentHwnd):X8}");
        };
        w.Closed += (_, _) => Log.Write("tent", $"gen={gen} WPF Window.Closed");
        _tent = w;
        w.Show();
        N.SetWindowPos(_tentHwnd, IntPtr.Zero, _o.X, _o.Y, _o.W, _o.H, N.SWP_NOZORDER | N.SWP_NOACTIVATE);
        Attach("create");
    }

    // ---------- hosting strategies ----------

    void Attach(string why)
    {
        var d = N.FindDesktop();
        Log.Write("attach", $"why={why} mode={_o.Mode} progman={N.H(d.Progman)} defView={N.H(d.DefView)} defViewParent={(d.DefViewParent == IntPtr.Zero ? "-" : N.Class(d.DefViewParent))} childWorkerW={d.ProgmanChildWorkerWs.Length}");
        switch (_o.Mode)
        {
            case "plain":
                break;
            case "bottom":
            case "bottom-react2":
                Log.Write("attach", "HWND_BOTTOM ok=" + N.SetWindowPos(_tentHwnd, N.HWND_BOTTOM, 0, 0, 0, 0, N.SWP_NOMOVE | N.SWP_NOSIZE | N.SWP_NOACTIVATE));
                break;
            case "bottom-react":
                Reassert("attach");
                break;
            case "owned":
            case "owned-bottom":
            case "owned-pin":
                {
                    // Own the tent to the top-level shell window that hosts the icon view. On Windows 11 (observed) that is
                    // Progman. On layouts where the icon view lives in a top-level WorkerW (reported for Windows 10, untested
                    // here) it is that WorkerW. --owner=progman forces Progman for comparison.
                    var owner = _o.Owner == "progman" || d.DefViewParent == IntPtr.Zero ? d.Progman : d.DefViewParent;
                    N.SetWindowLongPtr(_tentHwnd, N.GWLP_HWNDPARENT, owner);
                    Log.Write("attach", $"owner set to {N.Class(owner)} err={Marshal.GetLastWin32Error()} ownerNow={N.H(N.GetWindow(_tentHwnd, N.GW_OWNER))}");
                }
                if (_o.Mode != "owned")
                    Log.Write("attach", "HWND_BOTTOM ok=" + N.SetWindowPos(_tentHwnd, N.HWND_BOTTOM, 0, 0, 0, 0, N.SWP_NOMOVE | N.SWP_NOSIZE | N.SWP_NOACTIVATE));
                break;
            case "child-progman":
                Reparent(d.Progman);
                break;
            case "child-defview":
                Reparent(d.DefView);
                break;
            case "child-workerw":
                Reparent(FindWallpaperWorkerW(d));
                break;
        }
    }

    IntPtr FindWallpaperWorkerW(N.DesktopWindows d)
    {
        // Windows 11 24H2+ layout (observed): the wallpaper WorkerW is a child of Progman, below SHELLDLL_DefView.
        if (d.ProgmanChildWorkerWs.Length > 0) { Log.Write("attach", "wallpaper WorkerW: existing child of Progman"); return d.ProgmanChildWorkerWs[0]; }
        // Publicly described technique (CodeProject, "Draw Behind Desktop Icons in Windows 8+"): ask Progman to
        // create the WorkerW behind the icons, then find it as the top-level WorkerW after the one holding the icon view.
        N.SendMessageTimeout(d.Progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);
        d = N.FindDesktop();
        if (d.ProgmanChildWorkerWs.Length > 0) { Log.Write("attach", "wallpaper WorkerW: child of Progman after 0x052C"); return d.ProgmanChildWorkerWs[0]; }
        if (d.DefViewParent != IntPtr.Zero && d.DefViewParent != d.Progman)
        {
            var w = N.FindWindowEx(IntPtr.Zero, d.DefViewParent, "WorkerW", null);
            Log.Write("attach", "wallpaper WorkerW: top-level sibling after the icon WorkerW = " + N.H(w));
            return w;
        }
        Log.Write("attach", "wallpaper WorkerW: NOT FOUND");
        return IntPtr.Zero;
    }

    void Reparent(IntPtr parent)
    {
        if (parent == IntPtr.Zero) { Log.Write("attach", "reparent: no parent window, staying top-level"); return; }
        // SetParent docs: clear WS_POPUP and set WS_CHILD before calling SetParent when turning a top-level window into a child.
        var style = (N.Style(_tentHwnd) & ~(N.WS_POPUP | N.WS_CAPTION | N.WS_THICKFRAME | N.WS_SYSMENU | N.WS_MINIMIZEBOX | N.WS_MAXIMIZEBOX)) | N.WS_CHILD | N.WS_CLIPSIBLINGS;
        N.SetWindowLongPtr(_tentHwnd, N.GWL_STYLE, (IntPtr)style);
        var prev = N.SetParent(_tentHwnd, parent);
        var err = Marshal.GetLastWin32Error();
        var p = new N.POINT { X = _o.X, Y = _o.Y };
        N.ScreenToClient(parent, ref p);
        var ok = N.SetWindowPos(_tentHwnd, N.HWND_TOP, p.X, p.Y, _o.W, _o.H, N.SWP_NOACTIVATE | N.SWP_SHOWWINDOW | N.SWP_FRAMECHANGED);
        Log.Write("attach", $"reparent to {N.Class(parent)} {N.H(parent)}: prevParent={N.H(prev)} err={err} setPos={ok} clientPos={p.X},{p.Y} parentNow={N.H(N.GetAncestor(_tentHwnd, N.GA_PARENT))} style={N.Style(_tentHwnd):X8} ex={N.ExStyle(_tentHwnd):X8}");
    }

    /// <summary>bottom-react: keep the tent directly above the shell's desktop window in the top-level z-order.</summary>
    void Reassert(string why)
    {
        if (_inReassert || !N.IsWindow(_tentHwnd)) return;
        var d = N.FindDesktop();
        var target = TopmostShownDesktopWindow(d);
        if (target == IntPtr.Zero) { Log.Write("react", $"{why}: no desktop window"); return; }
        var above = N.GetWindow(target, N.GW_HWNDPREV);
        if (above == _tentHwnd) return; // already directly above the desktop window
        _inReassert = true;
        // Insert the tent after the window that currently sits directly above the desktop window.
        var ok = N.SetWindowPos(_tentHwnd, above == IntPtr.Zero ? N.HWND_TOP : above, 0, 0, 0, 0, N.SWP_NOMOVE | N.SWP_NOSIZE | N.SWP_NOACTIVATE);
        _inReassert = false;
        Log.Write("react", $"{why}: desktopWindow={N.Class(target)} {N.H(target)} insertedAfter={N.H(above)} ok={ok} nowDirectlyAbove={(N.GetWindow(target, N.GW_HWNDPREV) == _tentHwnd)} topmost={(N.ExStyle(_tentHwnd) & N.WS_EX_TOPMOST) != 0}");
    }

    static IntPtr TopmostShownDesktopWindow(N.DesktopWindows d)
    {
        // Walk the z-order from the top and return the first shell desktop window: Progman, or (older layout)
        // the visible top-level WorkerW that hosts the icon view.
        for (var h = N.GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = N.GetWindow(h, N.GW_HWNDNEXT))
        {
            if (h == d.Progman) return h;
            if (h == d.DefViewParent && N.IsWindowVisible(h)) return h;
        }
        return d.Progman;
    }

    // ---------- bottom-react2: unowned, pinned to the bottom, lifted only while the desktop is raised ----------
    // No owner and no parent, so no input-queue attachment to the shell. The cost is two WinEvent hooks:
    // EVENT_SYSTEM_FOREGROUND always, and EVENT_OBJECT_REORDER which production code would install only while
    // the desktop is the foreground window (this prototype keeps it installed for logging and gates on _desktopFg).

    void React2Foreground(IntPtr fg, N.DesktopWindows d)
    {
        var root = N.GetAncestor(fg, N.GA_ROOT);
        _desktopFg = root == d.Progman || (d.DefViewParent != IntPtr.Zero && root == d.DefViewParent);
        if (_desktopFg) { React2Check(d, "foreground"); return; }
        if (_raised)
        {
            _raised = false;
            var ok = N.SetWindowPos(_tentHwnd, N.HWND_BOTTOM, 0, 0, 0, 0, N.SWP_NOMOVE | N.SWP_NOSIZE | N.SWP_NOACTIVATE);
            Log.Write("react2", "desktop no longer foreground: back to HWND_BOTTOM ok=" + ok);
        }
    }

    void React2Check(N.DesktopWindows d, string why)
    {
        if (_allowZ || !N.IsWindow(_tentHwnd)) return;
        // Is a shell desktop window above the tent in the z-order? That is what Show Desktop does.
        IntPtr desktopAbove = IntPtr.Zero;
        for (var h = N.GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = N.GetWindow(h, N.GW_HWNDNEXT))
        {
            if (h == _tentHwnd) break;
            if (h == d.Progman || (h == d.DefViewParent && N.IsWindowVisible(h))) { desktopAbove = h; break; }
        }
        if (desktopAbove == IntPtr.Zero) return;
        var above = N.GetWindow(desktopAbove, N.GW_HWNDPREV);
        _allowZ = true;
        var ok = N.SetWindowPos(_tentHwnd, above == IntPtr.Zero ? N.HWND_TOP : above, 0, 0, 0, 0, N.SWP_NOMOVE | N.SWP_NOSIZE | N.SWP_NOACTIVATE);
        _allowZ = false;
        _raised = true;
        Log.Write("react2", $"{why}: desktop window {N.Class(desktopAbove)} was above the tent: lifted tent above it ok={ok}");
    }

    // ---------- events ----------

    void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (evt == N.EVENT_SYSTEM_FOREGROUND)
        {
            var d = N.FindDesktop();
            var isShell = N.Pid(hwnd) == d.ShellPid;
            Log.Write("evt", $"FOREGROUND {(hwnd == _tentHwnd ? "TENT" : isShell ? "shell:" + N.Class(hwnd) : "(other)")} {ZSummary(d)}");
            if (_o.Mode == "bottom-react") Reassert("foreground");
            if (_o.Mode == "bottom-react2") React2Foreground(hwnd, d);
        }
        else if (evt == N.EVENT_OBJECT_REORDER && hwnd == N.GetDesktopWindow())
        {
            var d = N.FindDesktop();
            Log.Write("evt", $"REORDER(top-level) {ZSummary(d)}");
            if (_o.Mode == "bottom-react") Reassert("reorder");
            if (_o.Mode == "bottom-react2" && _desktopFg) React2Check(d, "reorder");
        }
    }

    string ZSummary(N.DesktopWindows d)
    {
        // Positions among top-level windows that are visible and not minimized. Other apps are only counted.
        int i = 0, tent = -1, prog = -1, icons = -1;
        for (var h = N.GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = N.GetWindow(h, N.GW_HWNDNEXT))
        {
            if (!N.IsWindowVisible(h) || N.IsIconic(h) || N.Cloaked(h)) continue;
            if (h == _tentHwnd) tent = i;
            if (h == d.Progman) prog = i;
            if (h == d.DefViewParent && h != d.Progman) icons = i;
            i++;
        }
        return $"z[tent={tent} progman={prog} iconWorkerW={icons} shown={i}]";
    }

    IntPtr ControllerHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == _taskbarCreated)
        {
            Log.Write("ctl", $"TaskbarCreated. tentIsWindow={N.IsWindow(_tentHwnd)} tentVisible={N.IsWindowVisible(_tentHwnd)}");
            if (!N.IsWindow(_tentHwnd)) { Log.Write("ctl", "tent window is gone: recreating"); CreateTent(); }
            else Attach("TaskbarCreated");
            return IntPtr.Zero;
        }
        switch (msg)
        {
            case N.WM_DISPLAYCHANGE:
                Log.Write("ctl", $"WM_DISPLAYCHANGE bpp={wParam} size={(int)lParam & 0xFFFF}x{((int)lParam >> 16) & 0xFFFF} tent: isWindow={N.IsWindow(_tentHwnd)} visible={N.IsWindowVisible(_tentHwnd)} rect={Rect()}");
                break;
            case N.WM_WTSSESSION_CHANGE:
                Log.Write("ctl", $"WM_WTSSESSION_CHANGE {(int)wParam switch { 7 => "LOCK", 8 => "UNLOCK", 1 => "CONSOLE_CONNECT", 2 => "CONSOLE_DISCONNECT", var v => v.ToString() }} tent: isWindow={N.IsWindow(_tentHwnd)} visible={N.IsWindowVisible(_tentHwnd)} parent={N.H(N.GetAncestor(_tentHwnd, N.GA_PARENT))}");
                break;
            case N.WM_SETTINGCHANGE:
                Log.Write("ctl", $"WM_SETTINGCHANGE wParam={wParam} area={(lParam == IntPtr.Zero ? "" : Marshal.PtrToStringUni(lParam))}");
                break;
            case N.WM_CLOSE:
                Log.Write("ctl", "WM_CLOSE: clean shutdown");
                Shutdown();
                handled = true;
                break;
            case N.WM_APP + 2:
                // Hang test: block the UI thread. With a cross-process owner or parent, the input queues of this
                // thread and the shell's desktop thread are attached, so the question is what the desktop does meanwhile.
                Log.Write("ctl", "WM_APP+2: UI thread sleeping 8 s (hang test)");
                Thread.Sleep(8000);
                Log.Write("ctl", "hang test: UI thread awake again");
                break;
            case N.WM_APP + 1:
                Log.Write("ctl", "WM_APP+1: deliberate unhandled exception for the crash-path test");
                throw new InvalidOperationException("Deliberate spike crash");
        }
        return IntPtr.Zero;
    }

    string Rect() { N.GetWindowRect(_tentHwnd, out var r); return r.ToString(); }

    void Shutdown()
    {
        N.UnhookWinEvent(_hookFg); N.UnhookWinEvent(_hookReorder);
        if (_controller != null) N.WTSUnRegisterSessionNotification(_controller.Handle);
        if (N.IsWindow(_tentHwnd) && IsChildMode)
        {
            // Detach from the shell's window tree before closing, so no child of ours is left inside Explorer.
            N.ShowWindow(_tentHwnd, 0);
            N.SetParent(_tentHwnd, IntPtr.Zero);
            Log.Write("ctl", "detached tent from desktop parent");
        }
        try { _tent?.Close(); } catch (Exception ex) { Log.Write("ctl", "close: " + ex.Message); }
        Application.Current.Shutdown(0);
    }

    IntPtr TentHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case N.WM_WINDOWPOSCHANGING:
                if (_o.Mode is "bottom" or "owned-pin" || (_o.Mode == "bottom-react2" && !_allowZ))
                {
                    // Pin to the bottom: whatever z-order change is requested, turn it into "go to the bottom".
                    var wp = Marshal.PtrToStructure<N.WINDOWPOS>(lParam);
                    if ((wp.flags & N.SWP_NOZORDER) == 0 && wp.hwndInsertAfter != N.HWND_BOTTOM)
                    {
                        wp.hwndInsertAfter = N.HWND_BOTTOM;
                        Marshal.StructureToPtr(wp, lParam, false);
                    }
                }
                break;
            case N.WM_WINDOWPOSCHANGED:
                {
                    var wp = Marshal.PtrToStructure<N.WINDOWPOS>(lParam);
                    var f = wp.flags;
                    var parts = new List<string>();
                    if ((f & N.SWP_NOZORDER) == 0) parts.Add("Z(after=" + N.H(wp.hwndInsertAfter) + ")");
                    if ((f & N.SWP_NOMOVE) == 0) parts.Add($"MOVE({wp.x},{wp.y})");
                    if ((f & N.SWP_NOSIZE) == 0) parts.Add($"SIZE({wp.cx}x{wp.cy})");
                    if ((f & N.SWP_SHOWWINDOW) != 0) parts.Add("SHOW");
                    if ((f & N.SWP_HIDEWINDOW) != 0) parts.Add("HIDE");
                    if (parts.Count > 0) Log.Write("tent", "WM_WINDOWPOSCHANGED " + string.Join(" ", parts));
                    break;
                }
            case N.WM_SHOWWINDOW: Log.Write("tent", $"WM_SHOWWINDOW show={wParam} status={lParam}"); break;
            case N.WM_SIZE: if ((int)wParam != 0) Log.Write("tent", $"WM_SIZE type={wParam} (1=minimized 2=maximized)"); break;
            case N.WM_SYSCOMMAND: Log.Write("tent", $"WM_SYSCOMMAND 0x{(long)wParam:X}"); break;
            case N.WM_ACTIVATE: Log.Write("tent", $"WM_ACTIVATE {(int)wParam & 0xFFFF}"); break;
            case N.WM_MOUSEACTIVATE: Log.Write("tent", "WM_MOUSEACTIVATE -> MA_NOACTIVATE"); handled = true; return N.MA_NOACTIVATE;
            case N.WM_LBUTTONDOWN: Log.Write("tent", $"WM_LBUTTONDOWN at {(short)((long)lParam & 0xFFFF)},{(short)(((long)lParam >> 16) & 0xFFFF)}"); break;
            case N.WM_RBUTTONDOWN: Log.Write("tent", "WM_RBUTTONDOWN"); break;
            case N.WM_DISPLAYCHANGE: Log.Write("tent", "WM_DISPLAYCHANGE (received by the tent window itself)"); break;
            case N.WM_DPICHANGED: Log.Write("tent", $"WM_DPICHANGED dpi={(int)wParam & 0xFFFF}"); break;
            case N.WM_DPICHANGED_AFTERPARENT: Log.Write("tent", "WM_DPICHANGED_AFTERPARENT"); break;
            case N.WM_DESTROY: Log.Write("tent", $"WM_DESTROY (parent={N.H(N.GetAncestor(hwnd, N.GA_PARENT))})"); break;
            case N.WM_NCDESTROY: Log.Write("tent", "WM_NCDESTROY"); break;
            default:
                if ((uint)msg == _taskbarCreated) Log.Write("tent", "TaskbarCreated (received by the tent window itself)");
                break;
        }
        return IntPtr.Zero;
    }
}
