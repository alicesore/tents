// THROWAWAY SPIKE CODE. Not production. See spikes/README.md.
//
// spikectl: observes and drives the spike prototypes from a script.
//
// Privacy / clean-room notes:
//  - It prints details only for windows that belong to the shell (explorer.exe that owns the desktop) or to a spike
//    prototype (window title starts with "TentsSpike"). Any other window is reported as "(other)" with no class or title.
//  - Screen checks never save or print an image. They only count pixels of the prototype's marker color.
using System.Diagnostics;
using System.Runtime.InteropServices;
using Tents.Spikes;

internal static class Program
{
    const string Prefix = "TentsSpike";

    static int Main(string[] args)
    {
        N.SetProcessDpiAwarenessContext(new IntPtr(-4)); // per-monitor v2: all coordinates are physical pixels
        if (args.Length == 0) { Console.WriteLine("commands: tree | state <title> [RRGGBB] | zorder | fg | keys <combo> | toggle-desktop | minimize-all | undo-minimize-all | click <x> <y> [right] | colorscan <RRGGBB> | close <title> | post <title> <msgHex>"); return 2; }
        switch (args[0])
        {
            case "tree": Tree(); return 0;
            case "state": return State(args[1], args.Length > 2 ? args[2] : null);
            case "zorder": ZOrder(); return 0;
            case "fg": Console.WriteLine("foreground=" + Describe(N.GetForegroundWindow())); return 0;
            case "keys": Keys(args[1]); return 0;
            case "toggle-desktop": ShellApp("ToggleDesktop"); return 0;
            case "show-desktop": return EnsureDesktop(true, args.Length > 1 && args[1] == "keys");
            case "unshow-desktop": return EnsureDesktop(false, args.Length > 1 && args[1] == "keys");
            case "desktop-shown": Console.WriteLine($"desktopShown={(IsDesktopShown() ? 1 : 0)} appWindowsAboveDesktop={AppWindowsAboveDesktop()}"); return 0;
            case "minimize-all": ShellApp("MinimizeAll"); return 0;
            case "undo-minimize-all": ShellApp("UndoMinimizeALL"); return 0;
            case "click": Click(int.Parse(args[1]), int.Parse(args[2]), args.Length > 3 && args[3] == "right"); return 0;
            case "colorscan": ColorScan(ParseColor(args[1]), true); return 0;
            case "menu-open":
                {
                    // Is a shell popup (the desktop context menu) currently shown? Windows 11 uses a XAML popup rather
                    // than a classic #32768 menu, so report every shown top-level window of the shell that is not a
                    // desktop, taskbar or folder window. Compare against the value before the right-click.
                    var d = N.FindDesktop();
                    var popups = TopLevel().Where(h => IsShown(h) && (N.Class(h) == "#32768" || (N.Pid(h) == d.ShellPid && N.Class(h) is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "CabinetWClass")))).Select(N.Class).ToList();
                    Console.WriteLine($"shellPopups={popups.Count} [{string.Join(",", popups)}]");
                    return 0;
                }
            case "who":
                {
                    // Environment diagnosis: which processes own the shown top-level windows that overlap a rectangle?
                    // Prints process names, minimized/topmost flags and stacking only, never titles or classes.
                    int x = int.Parse(args[1]), y = int.Parse(args[2]), w = int.Parse(args[3]), hh = int.Parse(args[4]);
                    var d = N.FindDesktop();
                    foreach (var h in TopLevel())
                    {
                        if (h == d.Progman) { Console.WriteLine("  -- Progman --"); continue; }
                        if (!IsShown(h)) continue;
                        N.GetWindowRect(h, out var r);
                        if (r.R <= x || r.L >= x + w || r.B <= y || r.T >= y + hh) continue;
                        Console.WriteLine($"  {ProcName(N.Pid(h))} topmost={((N.ExStyle(h) & N.WS_EX_TOPMOST) != 0 ? 1 : 0)} toolwindow={((N.ExStyle(h) & N.WS_EX_TOOLWINDOW) != 0 ? 1 : 0)} minimizeBox={((N.Style(h) & N.WS_MINIMIZEBOX) != 0 ? 1 : 0)} rect={r}");
                    }
                    return 0;
                }
            case "snapshot": Snapshot(args[1]); return 0;
            case "restore": Restore(args[1]); return 0;
            case "minimized": Minimized(args.Length > 1 && args[1] == "restore-all"); return 0;
            case "close": return Post(args[1], N.WM_CLOSE);
            case "post": return Post(args[1], Convert.ToInt32(args[2], 16));
            default: Console.WriteLine("unknown command"); return 2;
        }
    }

    // ---------- identification ----------

    static bool IsOurs(IntPtr h) => N.Text(h).StartsWith(Prefix, StringComparison.Ordinal);

    static string ProcName(uint pid) { try { return Process.GetProcessById((int)pid).ProcessName; } catch { return "?"; } }

    static string Describe(IntPtr h)
    {
        if (h == IntPtr.Zero) return "(none)";
        var d = N.FindDesktop();
        if (N.Pid(h) == d.ShellPid) return $"shell:{N.Class(h)}:{N.H(h)}";
        var root = N.GetAncestor(h, N.GA_ROOT);
        if (IsOurs(h) || IsOurs(root)) return $"spike:\"{N.Text(h)}\":{N.H(h)}";
        return "(other)";
    }

    static IntPtr FindOurs(string title)
    {
        IntPtr found = IntPtr.Zero;
        N.EnumWindows((h, _) => { if (N.Text(h) == title) { found = h; return false; } return true; }, IntPtr.Zero);
        if (found != IntPtr.Zero) return found;
        // A re-parented tent is no longer top-level: look through the shell's desktop windows.
        var d = N.FindDesktop();
        var roots = new List<IntPtr> { d.Progman };
        roots.AddRange(d.TopLevelWorkerWs);
        foreach (var r in roots)
        {
            N.EnumChildWindows(r, (h, _) => { if (N.Text(h) == title) { found = h; return false; } return true; }, IntPtr.Zero);
            if (found != IntPtr.Zero) break;
        }
        return found;
    }

    // ---------- tree ----------

    static void Tree()
    {
        var d = N.FindDesktop();
        Console.WriteLine($"GetShellWindow={N.H(d.Progman)} class={N.Class(d.Progman)} shellPid={d.ShellPid} ({ProcName(d.ShellPid)})");
        Console.WriteLine($"DefView={N.H(d.DefView)} parentClass={(d.DefViewParent == IntPtr.Zero ? "-" : N.Class(d.DefViewParent))} ListView={N.H(d.ListView)}");
        Console.WriteLine($"top-level WorkerW count={d.TopLevelWorkerWs.Length}; WorkerW children of Progman={d.ProgmanChildWorkerWs.Length}");
        Console.WriteLine("--- Progman subtree (children listed in z-order, top first) ---");
        Dump(d.Progman, 0, d.ShellPid);
        foreach (var w in d.TopLevelWorkerWs)
        {
            Console.WriteLine("--- top-level WorkerW ---");
            Dump(w, 0, d.ShellPid);
        }
    }

    static void Dump(IntPtr h, int depth, uint shellPid)
    {
        var pad = new string(' ', depth * 2);
        var pid = N.Pid(h);
        if (pid != shellPid && !IsOurs(h)) { Console.WriteLine($"{pad}(other process window: not inspected)"); return; }
        N.GetWindowRect(h, out var r);
        var title = IsOurs(h) ? $" title=\"{N.Text(h)}\"" : (N.Class(h) is "Progman" or "SysListView32" ? $" title=\"{N.Text(h)}\"" : "");
        Console.WriteLine($"{pad}{N.H(h)} class={N.Class(h)}{title} vis={(N.IsWindowVisible(h) ? 1 : 0)} style={N.Style(h):X8} ex={N.ExStyle(h):X8} rect={r} cloaked={(N.Cloaked(h) ? 1 : 0)}");
        for (var c = N.GetWindow(h, N.GW_CHILD); c != IntPtr.Zero; c = N.GetWindow(c, N.GW_HWNDNEXT)) Dump(c, depth + 1, shellPid);
    }

    // ---------- z-order ----------

    static List<IntPtr> TopLevel()
    {
        var list = new List<IntPtr>();
        for (var h = N.GetTopWindow(IntPtr.Zero); h != IntPtr.Zero; h = N.GetWindow(h, N.GW_HWNDNEXT)) list.Add(h);
        return list;
    }

    static bool IsShown(IntPtr h)
    {
        if (!N.IsWindowVisible(h) || N.IsIconic(h) || N.Cloaked(h)) return false;
        N.GetWindowRect(h, out var r);
        return r.R - r.L > 0 && r.B - r.T > 0;
    }

    static void ZOrder()
    {
        var d = N.FindDesktop();
        var all = TopLevel();
        Console.WriteLine($"top-level windows={all.Count}; listing shown (visible, not minimized, not cloaked) windows top to bottom:");
        int i = 0, others = 0;
        foreach (var h in all)
        {
            if (!IsShown(h)) continue;
            var desc = Describe(h);
            if (desc == "(other)") { others++; continue; }
            if (others > 0) { Console.WriteLine($"   ... {others} other shown window(s)"); others = 0; }
            var top = (N.ExStyle(h) & N.WS_EX_TOPMOST) != 0 ? " TOPMOST" : "";
            Console.WriteLine($"  [{i++}] {desc}{top}");
        }
        if (others > 0) Console.WriteLine($"   ... {others} other shown window(s)");
    }

    // ---------- Show Desktop with verification ----------
    // Show Desktop is a toggle with no documented "is it on?" query, so a test can drift out of phase.
    // These helpers toggle and then check the observable outcome, toggling again if needed.

    static int AppWindowsAboveDesktop()
    {
        var d = N.FindDesktop();
        int n = 0;
        foreach (var h in TopLevel())
        {
            if (h == d.Progman) break;
            if (!IsShown(h) || (N.ExStyle(h) & N.WS_EX_TOPMOST) != 0 || (N.Style(h) & N.WS_MINIMIZEBOX) == 0) continue; // Show Desktop leaves non-minimizable windows alone
            var root = N.GetAncestor(h, N.GA_ROOT);
            if (IsOurs(h) || IsOurs(root)) continue;
            var cls = N.Class(h);
            if (N.Pid(h) == d.ShellPid && cls is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "WorkerW") continue;
            n++;
        }
        return n;
    }

    static bool IsDesktopShown() => AppWindowsAboveDesktop() == 0;

    static int EnsureDesktop(bool shown, bool viaKeys)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (attempt > 0 && IsDesktopShown() == shown) { Console.WriteLine($"desktopShown={(shown ? 1 : 0)} togglesUsed={attempt}"); return 0; }
            if (attempt == 0 && !shown && !IsDesktopShown()) { Console.WriteLine("desktopShown=0 togglesUsed=0"); return 0; }
            if (viaKeys) Keys("win+d"); else ShellApp("ToggleDesktop");
            Thread.Sleep(1500);
        }
        var ok = IsDesktopShown() == shown;
        Console.WriteLine($"desktopShown={(IsDesktopShown() ? 1 : 0)} togglesUsed=4 {(ok ? "" : "FAILED to reach requested state")}");
        return ok ? 0 : 1;
    }

    // ---------- window arrangement safety net ----------
    // Show Desktop toggles can get out of sync during a test and leave the user's windows minimized.
    // snapshot records which top-level windows were shown; restore un-minimizes exactly those again.

    static void Snapshot(string file)
    {
        var shown = TopLevel().Where(IsShown).Select(h => h.ToInt64().ToString()).ToArray();
        File.WriteAllLines(file, shown);
        Console.WriteLine($"snapshot: {shown.Length} shown top-level windows recorded");
    }

    static void Restore(string file)
    {
        int restored = 0;
        foreach (var line in File.ReadAllLines(file).Reverse()) // bottom first, so the original stacking is roughly kept
        {
            var h = new IntPtr(long.Parse(line));
            if (N.IsWindow(h) && N.IsIconic(h)) { N.ShowWindow(h, 4 /*SW_SHOWNOACTIVATE*/); restored++; }
        }
        Console.WriteLine($"restore: {restored} window(s) un-minimized");
    }

    static void Minimized(bool restoreAll)
    {
        var list = TopLevel().Where(h => N.IsWindowVisible(h) && N.IsIconic(h) && N.GetWindow(h, N.GW_OWNER) == IntPtr.Zero && (N.ExStyle(h) & N.WS_EX_TOOLWINDOW) == 0).ToList();
        Console.WriteLine($"minimized application windows: {list.Count}");
        if (restoreAll) { list.Reverse(); foreach (var h in list) N.ShowWindow(h, 4); Console.WriteLine("all un-minimized (no activation)"); }
    }

    // ---------- state ----------

    [ComImport, Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr hwnd, out int onCurrent);
        [PreserveSig] int GetWindowDesktopId(IntPtr hwnd, out Guid id);
        [PreserveSig] int MoveWindowToDesktop(IntPtr hwnd, ref Guid id);
    }

    static int State(string title, string? expectColor)
    {
        var h = FindOurs(title);
        if (h == IntPtr.Zero) { Console.WriteLine("found=0"); return 1; }
        var d = N.FindDesktop();
        var parent = N.GetAncestor(h, N.GA_PARENT);
        var owner = N.GetWindow(h, N.GW_OWNER);
        var root = N.GetAncestor(h, N.GA_ROOT);
        N.GetWindowRect(h, out var r);
        Console.WriteLine($"found=1 hwnd={N.H(h)} visible={(N.IsWindowVisible(h) ? 1 : 0)} iconic={(N.IsIconic(h) ? 1 : 0)} cloaked={(N.Cloaked(h) ? 1 : 0)} style={N.Style(h):X8} ex={N.ExStyle(h):X8}");
        Console.WriteLine($"rect={r} dpi={N.GetDpiForWindow(h)}");
        Console.WriteLine($"parent={(parent == N.GetDesktopWindow() ? "(desktop root: top-level)" : Describe(parent))} owner={Describe(owner)} root={Describe(root)}");

        try
        {
            var vdm = (IVirtualDesktopManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a"))!)!;
            var hr1 = vdm.IsWindowOnCurrentVirtualDesktop(root, out var on);
            var hr2 = vdm.GetWindowDesktopId(root, out var id);
            Console.WriteLine($"virtualDesktop: onCurrent={(hr1 == 0 ? on.ToString() : $"hr=0x{hr1:X8}")} desktopId={(hr2 == 0 ? (id == Guid.Empty ? "(empty: not assigned to a desktop)" : "assigned") : $"hr=0x{hr2:X8}")}");
        }
        catch (Exception ex) { Console.WriteLine("virtualDesktop: error " + ex.Message); }

        // Z position of the root among shown top-level windows.
        var shown = TopLevel().Where(IsShown).ToList();
        int idx = shown.IndexOf(root);
        int idxProg = shown.IndexOf(d.Progman);
        int othersAbove = idx < 0 ? -1 : shown.Take(idx).Count(x => Describe(x) == "(other)");
        int othersBelow = idx < 0 ? -1 : shown.Skip(idx + 1).Count(x => Describe(x) == "(other)");
        Console.WriteLine($"z: shownTopLevel={shown.Count} rootIndex={idx} progmanIndex={idxProg} otherAppWindowsAbove={othersAbove} otherAppWindowsBelow={othersBelow}");
        var raised = shown.Where(x => N.Pid(x) == d.ShellPid && N.Class(x) == "WorkerW").Select(x => $"{N.H(x)}@{shown.IndexOf(x)}");
        Console.WriteLine($"shell WorkerW shown top-level: [{string.Join(", ", raised)}]");
        Console.WriteLine("foreground=" + Describe(N.GetForegroundWindow()));

        var c = new N.POINT { X = (r.L + r.R) / 2, Y = (r.T + r.B) / 2 };
        var hit = N.WindowFromPoint(c);
        Console.WriteLine($"hitTest(center {c.X},{c.Y})={(hit == h || N.IsChild(h, hit) ? "TENT" : Describe(hit))}");
        if (expectColor != null)
        {
            var want = ParseColor(expectColor);
            var (count, box, total) = ColorScan(want, false);
            long area = (long)(r.R - r.L) * (r.B - r.T);
            var px = PixelAt(c.X, c.Y);
            Console.WriteLine($"centerPixel=#{px:X6} centerMatches={(Near(px, want) ? 1 : 0)} markerPixelsOnScreen={count} tentArea={area} coverage={(area == 0 ? 0 : 100.0 * count / area):F1}% markerBounds={box}");
        }
        return 0;
    }

    // ---------- screen color probe (no image is stored or printed) ----------

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER { public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount; public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant; }
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);

    static int[] Capture(out int vx, out int vy, out int vw, out int vh)
    {
        vx = N.GetSystemMetrics(76); vy = N.GetSystemMetrics(77); vw = N.GetSystemMetrics(78); vh = N.GetSystemMetrics(79); // virtual screen
        var screen = N.GetDC(IntPtr.Zero);
        var mem = CreateCompatibleDC(screen);
        var bmi = new BITMAPINFOHEADER { biSize = 40, biWidth = vw, biHeight = -vh, biPlanes = 1, biBitCount = 32 };
        var bmp = CreateDIBSection(screen, ref bmi, 0, out var bits, IntPtr.Zero, 0);
        var old = SelectObject(mem, bmp);
        BitBlt(mem, 0, 0, vw, vh, screen, vx, vy, 0x00CC0020 | 0x40000000); // SRCCOPY | CAPTUREBLT
        var px = new int[vw * vh];
        Marshal.Copy(bits, px, 0, px.Length);
        SelectObject(mem, old); DeleteObject(bmp); DeleteDC(mem); N.ReleaseDC(IntPtr.Zero, screen);
        return px;
    }

    static int ParseColor(string s) => Convert.ToInt32(s.TrimStart('#'), 16) & 0xFFFFFF;
    static bool Near(int a, int b, int tol = 6) =>
        Math.Abs(((a >> 16) & 255) - ((b >> 16) & 255)) <= tol && Math.Abs(((a >> 8) & 255) - ((b >> 8) & 255)) <= tol && Math.Abs((a & 255) - (b & 255)) <= tol;

    static int PixelAt(int x, int y)
    {
        var px = Capture(out var vx, out var vy, out var vw, out var vh);
        int ix = x - vx, iy = y - vy;
        return ix < 0 || iy < 0 || ix >= vw || iy >= vh ? -1 : px[iy * vw + ix] & 0xFFFFFF;
    }

    static (long count, string box, long total) ColorScan(int want, bool print)
    {
        var px = Capture(out var vx, out var vy, out var vw, out var vh);
        long count = 0; int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int y = 0; y < vh; y++)
            for (int x = 0; x < vw; x++)
                if (Near(px[y * vw + x] & 0xFFFFFF, want)) { count++; if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; }
        var box = count == 0 ? "(none)" : $"({minX + vx},{minY + vy})-({maxX + vx + 1},{maxY + vy + 1}) {maxX - minX + 1}x{maxY - minY + 1}";
        if (print) Console.WriteLine($"markerPixels={count} bounds={box} virtualScreen=({vx},{vy}) {vw}x{vh}");
        return (count, box, (long)vw * vh);
    }

    // ---------- input ----------

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public ushort wVk;
        [FieldOffset(10)] public ushort wScan;
        [FieldOffset(12)] public uint kbFlags;
        [FieldOffset(8)] public int dx;
        [FieldOffset(12)] public int dy;
        [FieldOffset(16)] public uint mouseData;
        [FieldOffset(20)] public uint mouseFlags;
    }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);

    static void Keys(string combo)
    {
        var map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["win"] = 0x5B, ["ctrl"] = 0x11, ["shift"] = 0x10, ["alt"] = 0x12, ["tab"] = 0x09, ["esc"] = 0x1B,
            ["f4"] = 0x73, ["left"] = 0x25, ["right"] = 0x27, ["d"] = 0x44, ["m"] = 0x4D, ["enter"] = 0x0D,
        };
        var vks = combo.Split('+').Select(k => map[k]).ToArray();
        var inputs = new List<INPUT>();
        foreach (var vk in vks) inputs.Add(new INPUT { type = 1, wVk = vk, kbFlags = IsExt(vk) ? 1u : 0 });
        foreach (var vk in vks.Reverse()) inputs.Add(new INPUT { type = 1, wVk = vk, kbFlags = 2u | (IsExt(vk) ? 1u : 0) });
        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), 40);
        Console.WriteLine($"keys {combo}: sent {sent}/{inputs.Count}");
        static bool IsExt(ushort vk) => vk is 0x5B or 0x25 or 0x27;
    }

    static void Click(int x, int y, bool right)
    {
        N.GetCursorPos(out var old);
        N.SetCursorPos(x, y);
        Thread.Sleep(80);
        var down = right ? 0x0008u : 0x0002u; var up = right ? 0x0010u : 0x0004u;
        var sent = SendInput(2, new[] { new INPUT { type = 0, mouseFlags = down }, new INPUT { type = 0, mouseFlags = up } }, 40);
        Thread.Sleep(150);
        N.SetCursorPos(old.X, old.Y);
        Console.WriteLine($"click {(right ? "right" : "left")} at {x},{y}: sent {sent}/2 (cursor restored)");
    }

    static void ShellApp(string method)
    {
        // Documented Shell automation object: Shell.ToggleDesktop / MinimizeAll / UndoMinimizeALL.
        var t = Type.GetTypeFromProgID("Shell.Application")!;
        var shell = Activator.CreateInstance(t)!;
        t.InvokeMember(method, System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
        Console.WriteLine("Shell.Application." + method + " called");
    }

    static int Post(string title, int msg)
    {
        var h = FindOurs(title);
        if (h == IntPtr.Zero) { Console.WriteLine("found=0"); return 1; }
        Console.WriteLine($"post 0x{msg:X} to {N.H(h)}: {N.PostMessage(h, (uint)msg, IntPtr.Zero, IntPtr.Zero)}");
        return 0;
    }
}
