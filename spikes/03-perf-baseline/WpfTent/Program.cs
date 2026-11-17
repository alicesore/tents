// THROWAWAY SPIKE CODE for M0 spike 3 (performance baseline). Not production. See spikes/README.md.
//
// A minimal WPF tent on .NET 10, hosted as spike 1 recommends (tool window owned by the desktop window, pinned to
// the bottom). It does nothing after startup: no timers, no polling, no logging, no hooks. That is the point.
//
//   --tents=N        number of tent windows (default 1)
//   --items=M        icon + label items per tent (default 0). Icons come from a shared cache of frozen bitmaps.
//   --software       RenderOptions.ProcessRenderMode = SoftwareOnly
//   --transparent    AllowsTransparency = true (layered window, software rendering)
//   --trim           call SetProcessWorkingSetSize(-1, -1) once, after the first frame of the last tent
//   --marker=path    write "ms from process creation to first rendered frame" to this file, once
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Tents.Spikes;

public static unsafe class Program
{
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    static readonly uint s_taskbarCreated = N.RegisterWindowMessage("TaskbarCreated");
    static readonly List<IntPtr> s_tents = [];

    [STAThread]
    public static int Main(string[] args)
    {
        string Get(string k, string d) => args.FirstOrDefault(a => a.StartsWith("--" + k + "=", StringComparison.Ordinal))?[(k.Length + 3)..] ?? d;
        int tents = int.Parse(Get("tents", "1")), items = int.Parse(Get("items", "0"));
        bool transparent = args.Contains("--transparent"), trim = args.Contains("--trim");
        var marker = Get("marker", "");
        if (args.Contains("--software")) RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        // Hidden top-level controller window: TaskbarCreated (re-own after an Explorer restart) and WM_CLOSE (exit).
        var controller = new HwndSource(new HwndSourceParameters("TentsSpike-perf-controller") { WindowStyle = unchecked((int)N.WS_POPUP), ExtendedWindowStyle = (int)N.WS_EX_TOOLWINDOW, Width = 0, Height = 0 });
        controller.AddHook(ControllerHook);

        var icons = items > 0 ? MakeIconCache(20) : [];
        var progman = N.FindDesktop().Progman;
        Window? last = null;
        for (int t = 0; t < tents; t++)
        {
            var panel = new WrapPanel { Margin = new Thickness(6) };
            for (int i = 0; i < items; i++)
            {
                var cell = new StackPanel { Width = 76, Margin = new Thickness(2) };
                cell.Children.Add(new Image { Source = icons[(t * 7 + i) % icons.Length], Width = 48, Height = 48, Stretch = Stretch.None });
                cell.Children.Add(new TextBlock { Text = $"Item {t + 1}-{i + 1}", Foreground = Brushes.White, FontSize = 12, TextAlignment = TextAlignment.Center });
                panel.Children.Add(cell);
            }
            var dock = new DockPanel();
            var title = new TextBlock { Text = $"Tent {t + 1}", Foreground = Brushes.White, FontSize = 13, Margin = new Thickness(8, 5, 8, 3) };
            DockPanel.SetDock(title, Dock.Top);
            dock.Children.Add(title);
            dock.Children.Add(panel);
            var w = new Window
            {
                Title = $"TentsSpike-perf-{t + 1}", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual, AllowsTransparency = transparent,
                Background = new SolidColorBrush(transparent ? Color.FromArgb(0xC0, 0x20, 0x40, 0x60) : Color.FromRgb(0x20, 0x40, 0x60)), Content = dock,
                Left = 0, Top = 0, Width = 300, Height = 200,
            };
            var helper = new WindowInteropHelper(w) { Owner = progman };
            var hwnd = helper.EnsureHandle();
            HwndSource.FromHwnd(hwnd)!.AddHook(TentHook);
            N.SetWindowLongPtr(hwnd, N.GWL_EXSTYLE, (IntPtr)(N.ExStyle(hwnd) | N.WS_EX_TOOLWINDOW | N.WS_EX_NOACTIVATE));
            s_tents.Add(hwnd);
            last = w;
            w.Show();
            // 5 columns x 2 rows of tents on the primary monitor, in physical pixels.
            N.SetWindowPos(hwnd, N.HWND_BOTTOM, 60 + (t % 5) * 440, 80 + (t / 5 % 3) * 420, 420, 400, N.SWP_NOACTIVATE);
        }

        last!.ContentRendered += (_, _) =>
        {
            if (marker.Length > 0)
            {
                var ms = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
                File.WriteAllText(marker, ms.ToString("F0"));
            }
            if (trim) SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, new IntPtr(-1), new IntPtr(-1));
        };
        return app.Run();
    }

    static BitmapSource[] MakeIconCache(int count)
    {
        // Stand-in for the shared icon cache: distinct 48x48 premultiplied BGRA bitmaps, frozen, shared by all tents.
        var result = new BitmapSource[count];
        var rnd = new Random(1234);
        for (int n = 0; n < count; n++)
        {
            var px = new byte[48 * 48 * 4];
            rnd.NextBytes(px);
            for (int i = 3; i < px.Length; i += 4) px[i] = 255;
            var bmp = BitmapSource.Create(48, 48, 96, 96, PixelFormats.Pbgra32, null, px, 48 * 4);
            bmp.Freeze();
            result[n] = bmp;
        }
        return result;
    }

    static IntPtr TentHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == N.WM_WINDOWPOSCHANGING)
        {
            var wp = (N.WINDOWPOS*)lParam;
            if ((wp->flags & N.SWP_NOZORDER) == 0) wp->hwndInsertAfter = N.HWND_BOTTOM;
        }
        else if (msg == N.WM_MOUSEACTIVATE) { handled = true; return N.MA_NOACTIVATE; }
        return IntPtr.Zero;
    }

    static IntPtr ControllerHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == s_taskbarCreated)
        {
            var progman = N.FindDesktop().Progman;
            foreach (var t in s_tents) if (N.IsWindow(t)) N.SetWindowLongPtr(t, N.GWLP_HWNDPARENT, progman);
        }
        else if (msg == N.WM_CLOSE) { handled = true; Application.Current.Shutdown(0); }
        return IntPtr.Zero;
    }
}
