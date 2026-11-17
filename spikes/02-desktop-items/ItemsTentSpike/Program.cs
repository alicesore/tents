// THROWAWAY SPIKE CODE for M0 spike 2 (desktop items). Not production. See spikes/README.md.
//
// One tent that draws shell items itself, and optionally takes the matching native desktop icons out of view.
//
// Candidate (a), hide the native icon layer and draw every icon ourselves:
//     --source=desktop --hide=listview     hide the desktop's icon list window (ShowWindow, not persisted by Explorer)
//     --source=desktop --hide=noicons      turn off "Show desktop icons" through IFolderView2 (FWF_NOICONS)
// Candidate (b), reposition native icons:
//     --source=desktop --filter=tents-spike- --park-at=x,y
//                                          draw only the items whose name starts with the filter, and move their
//                                          native icons to x,y (icon list client coordinates, may be off-screen)
// Neutral:
//     --source=<folder path>               draw the items of any folder; touches nothing native
//
// SAFETY: whatever the prototype hides or moves is put back on clean exit, on an unhandled exception, and (with
// --watchdog) by a second process if this one is killed. The pre-run state is also written to a state file.
//
// Event-driven only: FileSystemWatcher and WinEvents. No timers, no polling.
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Tents.Spikes;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--watchdog") return Watchdog.Run(int.Parse(args[1]), args[2]);

        var o = Options.Parse(args);
        Log.Open(o.LogPath);
        Log.Write("app", $"start source={o.Source} hide={o.Hide} filter={o.Filter} parkAt={(o.Park ? $"{o.ParkX},{o.ParkY}" : "-")} watchdog={o.Watchdog} pid={Environment.ProcessId}");
        var guard = new NativeIconGuard(o);
        // Finally blocks are not guaranteed to run for an unhandled exception, so the restore is hooked here as well.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { Log.Write("app", "UNHANDLED " + (e.ExceptionObject as Exception)?.Message); guard.Restore("unhandled exception"); };
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) => { Log.Write("app", "DISPATCHER UNHANDLED " + e.Exception.Message); guard.Restore("dispatcher unhandled exception"); };
        try
        {
            new ItemsTent(o, guard).Start();
            return app.Run();
        }
        finally
        {
            guard.Restore("exit");
            Log.Write("app", "exit");
        }
    }
}

internal sealed record Options(string Source, string Hide, string Filter, bool Park, int ParkX, int ParkY, bool Watchdog, string Title, int X, int Y, int W, int H, string LogPath)
{
    public static Options Parse(string[] args)
    {
        string Get(string k, string d) => args.FirstOrDefault(a => a.StartsWith("--" + k + "=", StringComparison.Ordinal))?[(k.Length + 3)..] ?? d;
        var r = Get("rect", "1500,600,520,360").Split(',').Select(int.Parse).ToArray();
        var park = Get("park-at", "");
        var p = park.Length > 0 ? park.Split(',').Select(int.Parse).ToArray() : [0, 0];
        return new Options(Get("source", "desktop"), Get("hide", "none"), Get("filter", ""), park.Length > 0, p[0], p[1], args.Contains("--watchdog"),
            Get("title", "TentsSpike-items"), r[0], r[1], r[2], r[3], Get("log", Path.Combine(Path.GetTempPath(), "tents-spike2.log")));
    }
}

internal static class Log
{
    static StreamWriter? _w;
    public static void Open(string path) { _w = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true }; }
    public static void Write(string src, string msg) { lock (typeof(Log)) _w?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{src}] {msg}"); }
}

/// <summary>Owns "what did we change on the native desktop, and how do we put it back".</summary>
internal sealed class NativeIconGuard
{
    readonly Options _o;
    readonly DesktopBackup _before;
    readonly string _stateFile;
    int _restored;

    public NativeIconGuard(Options o)
    {
        _o = o;
        _before = DesktopView.Open().Backup();
        if (o.Park) { _before.ParkX = o.ParkX; _before.ParkY = o.ParkY; }
        _stateFile = Path.Combine(Path.GetTempPath(), $"tents-spike2-state-{Environment.ProcessId}.json");
        if (Mutates)
        {
            DesktopView.Save(_before, _stateFile);
            Log.Write("guard", $"pre-run state saved ({_before.Items.Count} positions, flags=0x{_before.FolderFlags:X}, listViewVisible={_before.ListViewVisible}) to {_stateFile}");
            if (o.Watchdog)
            {
                var wd = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--watchdog {Environment.ProcessId} \"{_stateFile}\"") { UseShellExecute = false });
                Log.Write("guard", "watchdog started pid=" + wd?.Id);
            }
        }
    }

    bool Mutates => _o.Hide != "none" || _o.Park;

    /// <summary>Applies the hide. Called at start and again after an Explorer restart.</summary>
    public void ApplyHide(string why)
    {
        var d = N.FindDesktop();
        if (_o.Hide == "listview")
        {
            var was = N.IsWindowVisible(d.ListView);
            N.ShowWindow(d.ListView, 0 /*SW_HIDE*/);
            Log.Write("guard", $"{why}: icon list window {N.H(d.ListView)} wasVisible={was} nowVisible={N.IsWindowVisible(d.ListView)}");
        }
        else if (_o.Hide == "noicons")
        {
            var v = DesktopView.Open();
            var before = v.Flags;
            v.SetFlags(DesktopView.FWF_NOICONS, DesktopView.FWF_NOICONS);
            Log.Write("guard", $"{why}: FWF_NOICONS flags 0x{before:X} -> 0x{v.Flags:X} listViewVisible={N.IsWindowVisible(N.FindDesktop().ListView)} HideIconsRegistry={DesktopView.HideIconsRegistry()}");
        }
    }

    /// <summary>Remembers where a native icon was before it was parked, including icons that appeared after start.</summary>
    public void RecordParked(DesktopItem original)
    {
        // Only the FIRST position counts. Explorer may move a parked icon again (it snaps off-screen positions back
        // into the view), and a later reading must not overwrite the true original.
        if (_before.Items.Any(i => string.Equals(i.ParsingName, original.ParsingName, StringComparison.OrdinalIgnoreCase))) return;
        _before.Items.Add(original);
        DesktopView.Save(_before, _stateFile); // keep the watchdog's copy current
    }

    public void Restore(string why)
    {
        if (!Mutates || Interlocked.Exchange(ref _restored, 1) != 0) return;
        try
        {
            Log.Write("guard", $"restore ({why}): {DesktopView.Restore(_before)}");
            File.Delete(_stateFile); // tells the watchdog there is nothing left to do
        }
        catch (Exception ex) { Log.Write("guard", "RESTORE FAILED: " + ex.Message); }
    }
}

/// <summary>Second process: waits (blocked on the process handle, no polling) for the prototype to end, then restores if it did not.</summary>
internal static class Watchdog
{
    public static int Run(int pid, string stateFile)
    {
        try { Process.GetProcessById(pid).WaitForExit(); } catch (ArgumentException) { /* already gone */ }
        if (!File.Exists(stateFile)) return 0; // clean exit: the prototype restored and deleted the state file
        var log = stateFile + ".watchdog.log";
        try
        {
            var report = DesktopView.Restore(DesktopView.Load(stateFile));
            File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss.fff} prototype {pid} ended without restoring. Watchdog restore: {report}\n");
            File.Delete(stateFile);
            return 0;
        }
        catch (Exception ex) { File.AppendAllText(log, $"{DateTime.Now:HH:mm:ss.fff} WATCHDOG RESTORE FAILED: {ex}\n"); return 1; }
    }
}

internal sealed record ShellEntry(string ParsingName, string DisplayName, ImageSource? Icon);

internal sealed unsafe class ItemsTent
{
    readonly Options _o;
    readonly NativeIconGuard _guard;
    readonly uint _taskbarCreated = N.RegisterWindowMessage("TaskbarCreated");
    readonly Dictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    readonly List<FileSystemWatcher> _watchers = [];
    HwndSource? _controller;
    Window? _window;
    IntPtr _hwnd;
    WrapPanel? _panel;
    TextBlock? _header;
    List<ShellEntry> _model = [];
    int _refreshPending, _refreshCount;
    N.WinEventProc? _winEventProc;
    IntPtr _hook;
    IntPtr _listView;

    public ItemsTent(Options o, NativeIconGuard guard) { _o = o; _guard = guard; }

    public void Start()
    {
        _controller = new HwndSource(new HwndSourceParameters(_o.Title + "-controller") { WindowStyle = unchecked((int)N.WS_POPUP), ExtendedWindowStyle = (int)N.WS_EX_TOOLWINDOW, Width = 0, Height = 0 });
        _controller.AddHook(ControllerHook);

        _guard.ApplyHide("start");
        CreateWindow();
        StartWatching();
        HookListView();
        Refresh("start");
    }

    // ---------- window (hosting as recommended by spike 1: owned by the desktop window, pinned to the bottom) ----------

    void CreateWindow()
    {
        _header = new TextBlock { Foreground = Brushes.White, FontSize = 13, Margin = new Thickness(8, 6, 8, 4) };
        _panel = new WrapPanel { Margin = new Thickness(6) };
        var dock = new DockPanel();
        DockPanel.SetDock(_header, Dock.Top);
        dock.Children.Add(_header);
        dock.Children.Add(new ScrollViewer { Content = _panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var w = new Window
        {
            Title = _o.Title, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Background = new SolidColorBrush(Color.FromRgb(0x20, 0x40, 0x60)), Content = dock, Left = 0, Top = 0, Width = 300, Height = 200,
        };
        var helper = new WindowInteropHelper(w) { Owner = N.FindDesktop().Progman }; // owner set before the window exists
        _hwnd = helper.EnsureHandle();
        HwndSource.FromHwnd(_hwnd)!.AddHook(TentHook);
        N.SetWindowLongPtr(_hwnd, N.GWL_EXSTYLE, (IntPtr)(N.ExStyle(_hwnd) | N.WS_EX_TOOLWINDOW | N.WS_EX_NOACTIVATE));
        _window = w;
        w.Show();
        N.SetWindowPos(_hwnd, N.HWND_BOTTOM, _o.X, _o.Y, _o.W, _o.H, N.SWP_NOACTIVATE);
        Log.Write("tent", $"window {N.H(_hwnd)} owner={N.H(N.GetWindow(_hwnd, N.GW_OWNER))}");
    }

    IntPtr TentHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == N.WM_WINDOWPOSCHANGING)
        {
            var wp = (N.WINDOWPOS*)lParam;
            if ((wp->flags & N.SWP_NOZORDER) == 0) wp->hwndInsertAfter = N.HWND_BOTTOM;
        }
        else if (msg == N.WM_MOUSEACTIVATE) { handled = true; return N.MA_NOACTIVATE; }
        return IntPtr.Zero;
    }

    IntPtr ControllerHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == _taskbarCreated)
        {
            // What state is the NEW shell in before we touch it? This is the "after an Explorer restart" observation.
            var d = N.FindDesktop();
            var v = DesktopView.Open();
            Log.Write("ctl", $"TaskbarCreated: new shell state: listViewVisible={N.IsWindowVisible(d.ListView)} flags=0x{v.Flags:X} noIcons={(v.Flags & DesktopView.FWF_NOICONS) != 0} HideIconsRegistry={DesktopView.HideIconsRegistry()} nativeItems={v.Count} tentIsWindow={N.IsWindow(_hwnd)}");
            LogFiltered("TaskbarCreated: native positions of filtered items", v);
            if (N.IsWindow(_hwnd)) N.SetWindowLongPtr(_hwnd, N.GWLP_HWNDPARENT, d.Progman);
            _guard.ApplyHide("TaskbarCreated");
            HookListView();
            Refresh("TaskbarCreated");
            return IntPtr.Zero;
        }
        switch (msg)
        {
            case N.WM_CLOSE:
                Log.Write("ctl", "WM_CLOSE: clean shutdown");
                _guard.Restore("clean shutdown");
                handled = true;
                Application.Current.Shutdown(0);
                break;
            case N.WM_APP + 1:
                Log.Write("ctl", "WM_APP+1: deliberate unhandled exception (crash-path test)");
                throw new InvalidOperationException("Deliberate spike crash");
            case N.WM_APP + 4:
                {
                    // Recovery-path observation: what does the user's "View > Show desktop icons" toggle do while the
                    // icon list window is hidden with ShowWindow? Simulated with the documented flag: off, then on.
                    var v = DesktopView.Open();
                    v.SetFlags(DesktopView.FWF_NOICONS, DesktopView.FWF_NOICONS);
                    Log.Write("ctl", $"toggle test: after 'Show desktop icons' OFF: listViewVisible={N.IsWindowVisible(N.FindDesktop().ListView)}");
                    v.SetFlags(DesktopView.FWF_NOICONS, 0);
                    Log.Write("ctl", $"toggle test: after 'Show desktop icons' ON again: listViewVisible={N.IsWindowVisible(N.FindDesktop().ListView)}");
                    break;
                }
            case N.WM_APP + 3:
                Refresh("dump requested");
                break;
        }
        return IntPtr.Zero;
    }

    // ---------- change tracking ----------

    void StartWatching()
    {
        var folders = _o.Source == "desktop"
            ? new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) }
            : [_o.Source];
        foreach (var f in folders)
        {
            var w = new FileSystemWatcher(f) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName, IncludeSubdirectories = false };
            w.Created += (_, e) => OnFsEvent("created", e.Name);
            w.Deleted += (_, e) => OnFsEvent("deleted", e.Name);
            w.Renamed += (_, e) => OnFsEvent("renamed", e.OldName + " -> " + e.Name);
            w.EnableRaisingEvents = true;
            _watchers.Add(w);
        }
    }

    void OnFsEvent(string kind, string? name)
    {
        // Log names only for scratch items, so the log never contains the user's own file names.
        var shown = name != null && name.Contains("tents-spike-", StringComparison.OrdinalIgnoreCase) ? name : "(item)";
        Log.Write("fsw", $"{kind} {shown}");
        ScheduleRefresh("file system event");
    }

    /// <summary>Coalesces bursts without a timer: at most one refresh is queued on the dispatcher at any time.</summary>
    void ScheduleRefresh(string why)
    {
        if (Interlocked.Exchange(ref _refreshPending, 1) != 0) return;
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { Interlocked.Exchange(ref _refreshPending, 0); Refresh(why); });
    }

    void HookListView()
    {
        // Explorer's icon list raises accessibility events when items are added, removed or renamed. Scoped to the
        // shell process, they tell us when the NATIVE view has caught up with a file system change.
        if (_hook != IntPtr.Zero) N.UnhookWinEvent(_hook);
        var d = N.FindDesktop();
        _listView = d.ListView;
        _winEventProc = (hook, evt, hwnd, idObject, idChild, thread, time) =>
        {
            if (hwnd != _listView || idObject != -4 /*OBJID_CLIENT*/ || idChild == 0) return;
            var name = evt switch { 0x8000 => "CREATE", 0x8001 => "DESTROY", 0x8004 => "REORDER", 0x800C => "NAMECHANGE", _ => null };
            if (name == null) return;
            Log.Write("lv", $"icon list event {name} child={idChild}");
            ScheduleRefresh("icon list event");
        };
        _hook = N.SetWinEventHook(0x8000, 0x800C, IntPtr.Zero, _winEventProc, d.ShellPid, 0, N.WINEVENT_OUTOFCONTEXT);
    }

    // ---------- model ----------

    void Refresh(string why)
    {
        var sw = Stopwatch.StartNew();
        var entries = Enumerate();
        var before = _model.Select(e => e.ParsingName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var after = entries.Select(e => e.ParsingName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int added = after.Count(n => !before.Contains(n)), removed = before.Count(n => !after.Contains(n));
        _model = entries;
        _refreshCount++;

        string native = "";
        if (_o.Source == "desktop")
        {
            // Compare with what the native view lists (it still enumerates while hidden).
            var v = DesktopView.Open();
            var nat = v.Items();
            var natNames = nat.Select(i => i.ParsingName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var all = _o.Filter.Length > 0 ? EnumerateAllNames() : after;
            native = $" native={nat.Count} oursUnfiltered={all.Count} onlyNative={natNames.Count(n => !all.Contains(n))} onlyOurs={all.Count(n => !natNames.Contains(n))}";
            if (_refreshCount == 1)
            {
                // What are the namespace items the native desktop does not show? Categories only, no names.
                int virt = 0, hidden = 0, fsVisible = 0;
                foreach (var it in EnumerateItems())
                {
                    if (natNames.Contains(Name(it, SIGDN.SIGDN_DESKTOPABSOLUTEPARSING))) continue;
                    const uint SFGAO_FILESYSTEM = 0x40000000, SFGAO_HIDDEN = 0x00080000;
                    Windows.Win32.System.SystemServices.SFGAO_FLAGS attrs;
                    it.GetAttributes((Windows.Win32.System.SystemServices.SFGAO_FLAGS)(SFGAO_FILESYSTEM | SFGAO_HIDDEN), &attrs);
                    if (((uint)attrs & SFGAO_FILESYSTEM) == 0) virt++;
                    else if (((uint)attrs & SFGAO_HIDDEN) != 0) hidden++;
                    else fsVisible++;
                }
                Log.Write("model", $"namespace items the native desktop does not list: virtual(non-filesystem)={virt} hiddenFiles={hidden} otherFiles={fsVisible}");
            }
            if (_o.Park) ParkFiltered(v, nat);
        }
        Log.Write("model", $"refresh #{_refreshCount} ({why}): items={entries.Count} +{added} -{removed}{native} scratch=[{string.Join("|", entries.Where(e => IsScratch(e.DisplayName)).Select(e => e.DisplayName))}] iconsLoaded={entries.Count(e => e.Icon != null)} in {sw.ElapsedMilliseconds} ms");
        Render();
    }

    static bool IsScratch(string name) => name.StartsWith("tents-spike-", StringComparison.OrdinalIgnoreCase);

    void ParkFiltered(DesktopView v, List<DesktopItem> nat)
    {
        foreach (var e in _model)
        {
            var n = nat.FirstOrDefault(i => string.Equals(i.ParsingName, e.ParsingName, StringComparison.OrdinalIgnoreCase));
            if (n == null) { Log.Write("park", $"{e.DisplayName}: not in the native view yet"); continue; }
            if (n.X == _o.ParkX && n.Y == _o.ParkY) continue;
            _guard.RecordParked(n);
            v.SetPosition(e.ParsingName, _o.ParkX, _o.ParkY);
            var now = v.Items().FirstOrDefault(i => string.Equals(i.ParsingName, e.ParsingName, StringComparison.OrdinalIgnoreCase));
            Log.Write("park", $"{e.DisplayName}: native icon ({n.X},{n.Y}) -> requested ({_o.ParkX},{_o.ParkY}) -> read back ({now?.X},{now?.Y})");
        }
    }

    void LogFiltered(string what, DesktopView v)
    {
        if (_o.Filter.Length == 0) return;
        var items = v.Items().Where(i => i.DisplayName.StartsWith(_o.Filter, StringComparison.OrdinalIgnoreCase));
        Log.Write("park", $"{what}: {string.Join(" ", items.Select(i => $"{i.DisplayName}=({i.X},{i.Y})"))}");
    }

    IShellItem SourceItem()
    {
        var iid = typeof(IShellItem).GUID;
        object o;
        if (_o.Source == "desktop")
        {
            // The desktop namespace root: the merged view Explorer shows (user Desktop, Public Desktop, virtual items).
            var folderId = PInvoke.FOLDERID_Desktop;
            PInvoke.SHGetKnownFolderItem(&folderId, KNOWN_FOLDER_FLAG.KF_FLAG_DEFAULT, default, &iid, out o).ThrowOnFailure();
        }
        else
        {
            fixed (char* p = _o.Source) PInvoke.SHCreateItemFromParsingName(p, null, &iid, out o).ThrowOnFailure();
        }
        return (IShellItem)o;
    }

    List<IShellItem> EnumerateItems()
    {
        var result = new List<IShellItem>();
        Guid bhid = PInvoke.BHID_EnumItems, iid = typeof(IEnumShellItems).GUID;
        SourceItem().BindToHandler(null, &bhid, &iid, out void* ppv);
        var en = (IEnumShellItems)Marshal.GetObjectForIUnknown((IntPtr)ppv);
        Marshal.Release((IntPtr)ppv);
        var one = new IShellItem[1];
        while (true)
        {
            uint fetched = 0;
            en.Next(1, one, &fetched);
            if (fetched == 0) break;
            result.Add(one[0]);
        }
        return result;
    }

    static string Name(IShellItem item, SIGDN kind)
    {
        PWSTR p;
        item.GetDisplayName(kind, &p);
        try { return p.ToString(); } finally { PInvoke.CoTaskMemFree(p.Value); }
    }

    HashSet<string> EnumerateAllNames() => EnumerateItems().Select(i => Name(i, SIGDN.SIGDN_DESKTOPABSOLUTEPARSING)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    List<ShellEntry> Enumerate()
    {
        var list = new List<ShellEntry>();
        foreach (var item in EnumerateItems())
        {
            var display = Name(item, SIGDN.SIGDN_NORMALDISPLAY);
            if (_o.Filter.Length > 0 && !display.StartsWith(_o.Filter, StringComparison.OrdinalIgnoreCase)) continue;
            var parsing = Name(item, SIGDN.SIGDN_DESKTOPABSOLUTEPARSING);
            if (!_iconCache.TryGetValue(parsing, out var icon)) _iconCache[parsing] = icon = LoadIcon(item);
            list.Add(new ShellEntry(parsing, display, icon));
        }
        return list;
    }

    static ImageSource? LoadIcon(IShellItem item)
    {
        try
        {
            // One frozen bitmap per item at the exact pixel size shown. (Alpha handling is not addressed in this spike.)
            ((IShellItemImageFactory)item).GetImage(new SIZE(48, 48), SIIGBF.SIIGBF_ICONONLY, out var hbm);
            using (hbm)
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(hbm.DangerousGetHandle(), IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
        }
        catch (Exception ex) { Log.Write("icon", "failed: " + ex.Message); return null; }
    }

    void Render()
    {
        _header!.Text = $"Tents spike 2 (throwaway)  source={(_o.Source == "desktop" ? "desktop" : "folder")} hide={_o.Hide} park={_o.Park}  items={_model.Count}";
        _panel!.Children.Clear();
        foreach (var e in _model)
        {
            var cell = new StackPanel { Width = 92, Margin = new Thickness(4) };
            cell.Children.Add(new Image { Source = e.Icon, Width = 48, Height = 48, Stretch = Stretch.None });
            cell.Children.Add(new TextBlock { Text = e.DisplayName, Foreground = Brushes.White, FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxHeight = 34 });
            _panel.Children.Add(cell);
        }
    }
}
