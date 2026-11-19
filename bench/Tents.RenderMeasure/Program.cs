// M1 step 12: interactive cost of drawing tents, per rendering mode. See measure-rendering.ps1 and the step 12 PR.
//
//   Tents.RenderMeasure.exe [--hardware] [--transparent] [--items=200] [--steps=120] [--out=results.jsonl]
//
// Creates one tent with N synthetic items and one "live" tent, hosted exactly as the app hosts them (owned by the
// desktop window, pinned to the bottom, so they sit behind every other window), and drives each scenario step by step
// from code: move, resize, scroll, hover, and a live tent that gains an item and scrolls to it every step. After each
// step it waits until WPF has rendered. It records CPU time, wall time and rendered frames per scenario, plus startup
// and memory, as one JSON line per scenario. The items are empty files in a temporary folder (deleted at the end);
// nothing on the desktop is read or changed, and the native icons are not touched.
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Tents.App;
using Tents.App.Items;
using Tents.Core;
using Tents.Shell.Desktop;
using Tents.Shell.DragDrop;
using Tents.Shell.Hosting;
using Tents.Shell.Icons;
using Tents.Shell.Menus;

namespace Tents.RenderMeasure;

internal static class Program
{
    private static readonly string[] _extensions = [".txt", ".md", ".csv", ".json", ".log", ".xml", ".html", ".ps1", ".zip", ".pdf"];

    [STAThread]
    private static int Main(string[] args)
    {
        var options = MeasureOptions.Parse(args);
        if (!options.Hardware)
        {
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var exitCode = 1;
        app.Startup += async (_, _) =>
        {
            try
            {
                await RunAsync(options);
                exitCode = 0;
            }
            catch (Exception ex)
            {
                await File.AppendAllTextAsync(options.Output + ".error.txt", ex.ToString());
            }
            finally
            {
                app.Shutdown(exitCode);
            }
        };
        app.Run();
        return exitCode;
    }

    private static async Task RunAsync(MeasureOptions options)
    {
        var folder = Directory.CreateTempSubdirectory("tents-render-measure-").FullName;
        try
        {
            var paths = Enumerable.Range(1, options.Items + options.Steps)
                .Select(i => Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"Item {i:000}{_extensions[i % _extensions.Length]}")))
                .ToList();
            foreach (var path in paths)
            {
                await File.WriteAllBytesAsync(path, []);
            }

            using var source = new DesktopItemSource();
            var resolved = await source.ResolveAsync([.. paths.Select(p => new ItemId(p))]);
            var items = resolved.Take(options.Items).ToList();
            var extra = resolved.Skip(options.Items).ToList();

            using var icons = new IconCache();
            var host = new OwnedPinHost();
            var commands = new NoCommands();
            var primary = Monitors.GetPrimary();
            var scale = primary?.Scale ?? 1.0;
            var work = primary?.WorkArea ?? new PixelRect(0, 0, 1920, 1080);
            int Px(double dip) => (int)Math.Round(dip * scale);

            var startup = Stopwatch.StartNew();
            var tent = new Tent(TentId.New(), "Measure", new PixelRect(work.X + Px(80), work.Y + Px(80), Px(420), Px(600)));
            var window = new TentWindow(tent, items, host, icons, commands, transparent: options.Transparent);
            var firstFrame = new TaskCompletionSource();
            window.ContentRendered += (_, _) => firstFrame.TrySetResult();
            window.Attach();
            await firstFrame.Task;
            var firstFrameMs = startup.Elapsed.TotalMilliseconds;
            await SettleAsync(TimeSpan.FromSeconds(3)); // visible icons load
            var view = window.ItemView;

            var live = new Tent(TentId.New(), "Live", new PixelRect(work.X + Px(540), work.Y + Px(80), Px(320), Px(400)));
            var liveItems = new List<DesktopItem>(items.Take(20));
            var liveWindow = new TentWindow(live, liveItems.ToList(), host, icons, commands, transparent: options.Transparent);
            liveWindow.Attach();
            await SettleAsync(TimeSpan.FromSeconds(1));

            var results = new List<ScenarioResult>
            {
                Result(options, "startup", 1, firstFrameMs, 0, 1),
            };

            var start = tent.Bounds;
            results.Add(await MeasureAsync(options, "drag", options.Steps, step =>
                NativeWindow.SetBounds(window.Handle, start.WithPosition(start.X + (Px(4) * step), start.Y + (Px(2) * step)))));
            NativeWindow.SetBounds(window.Handle, start);

            results.Add(await MeasureAsync(options, "resize", options.Steps, step =>
                NativeWindow.SetBounds(window.Handle, start.WithSize(start.Width + (Px(3) * step), start.Height + Px(2) * step))));
            NativeWindow.SetBounds(window.Handle, start);
            await SettleAsync(TimeSpan.FromMilliseconds(500));

            var maxOffset = Math.Max(1, view.ExtentHeight - view.ViewportHeight);
            results.Add(await MeasureAsync(options, "scroll", options.Steps, step =>
                view.SetVerticalOffset(maxOffset * step / options.Steps)));
            view.SetVerticalOffset(0);
            await SettleAsync(TimeSpan.FromMilliseconds(500));

            var (first, count) = view.Grid.VisibleRange(0, view.ViewportHeight);
            results.Add(await MeasureAsync(options, "hover", options.Steps, step =>
                view.SetHoverForMeasurement(first + (step % Math.Max(1, count)))));
            view.SetHoverForMeasurement(-1);

            var liveView = liveWindow.ItemView;
            results.Add(await MeasureAsync(options, "live", Math.Min(options.Steps, extra.Count), step =>
            {
                liveItems.Add(extra[step - 1]);
                liveView.SetItems(liveItems.ToList());
                liveView.SetVerticalOffset(double.MaxValue);
            }));

            await SettleAsync(TimeSpan.FromSeconds(1));
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var memory = new { privateBytesMB = process.PrivateMemorySize64 / 1048576.0, workingSetMB = process.WorkingSet64 / 1048576.0 };
            results.Add(Result(options, "memory", 0, 0, 0, 0) with { PrivateBytesMB = memory.privateBytesMB, WorkingSetMB = memory.workingSetMB });

            await File.AppendAllLinesAsync(options.Output, results.Select(r => JsonSerializer.Serialize(r)));
            liveWindow.Close();
            window.Close();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Runs one scenario: a step, then a wait until WPF has rendered it, counting frames and CPU time.</summary>
    private static async Task<ScenarioResult> MeasureAsync(MeasureOptions options, string name, int steps, Action<int> step)
    {
        await SettleAsync(TimeSpan.FromMilliseconds(300));
        var frames = 0;
        void OnRendering(object? sender, EventArgs e) => frames++;
        CompositionTarget.Rendering += OnRendering;
        using var process = Process.GetCurrentProcess();
        var cpuBefore = process.TotalProcessorTime;
        var wall = Stopwatch.StartNew();
        for (var i = 1; i <= steps; i++)
        {
            step(i);
            await Dispatcher.CurrentDispatcher.InvokeAsync(static () => { }, DispatcherPriority.ContextIdle); // after layout and render
        }

        wall.Stop();
        process.Refresh();
        var cpu = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
        CompositionTarget.Rendering -= OnRendering;
        return Result(options, name, steps, wall.Elapsed.TotalMilliseconds, cpu, frames);
    }

    // Lets pending work (icon loads, renders) finish. Measurement only; the app never waits like this.
    private static Task SettleAsync(TimeSpan time) => Task.Delay(time);

    private static ScenarioResult Result(MeasureOptions options, string scenario, int steps, double wallMs, double cpuMs, int frames)
    {
        var dpi = Monitors.GetPrimary()?.Dpi ?? 96;
        return new ScenarioResult(
            options.Hardware ? "hardware" : "software",
            options.Transparent,
            dpi,
            options.Items,
            scenario,
            steps,
            Math.Round(wallMs, 1),
            Math.Round(cpuMs, 1),
            frames,
            frames > 0 ? Math.Round(cpuMs / frames, 2) : 0,
            0,
            0);
    }

    private sealed class NoCommands : ITentCommands
    {
        public void Exit()
        {
        }

        public void TentClicked(TentId tent)
        {
        }

        public void Open(DesktopItem item)
        {
        }

        public void ShowItemMenu(IReadOnlyList<DesktopItem> items, System.Drawing.Point screenPoint, ContextMenuOptions options)
        {
        }

        public void Dropped(TentId tent, TentDrop drop, int insertionIndex)
        {
        }

        public void BoundsChanged(TentId tent, PixelRect bounds)
        {
        }

        public void TitleClicked(TentId tent)
        {
        }

        public void NewTent(TentId from)
        {
        }

        public void RenameTent(TentId tent, string title)
        {
        }

        public void DeleteTent(TentId tent)
        {
        }

        public bool CanDelete(TentId tent) => false;

        public void Activity()
        {
        }
    }
}

internal sealed record MeasureOptions(bool Hardware, bool Transparent, int Items, int Steps, string Output)
{
    public static MeasureOptions Parse(string[] args)
    {
        string Value(string name, string fallback) =>
            args.FirstOrDefault(a => a.StartsWith(name, StringComparison.OrdinalIgnoreCase))?[name.Length..] ?? fallback;

        return new MeasureOptions(
            args.Contains("--hardware", StringComparer.OrdinalIgnoreCase),
            args.Contains("--transparent", StringComparer.OrdinalIgnoreCase),
            int.Parse(Value("--items=", "200"), CultureInfo.InvariantCulture),
            int.Parse(Value("--steps=", "120"), CultureInfo.InvariantCulture),
            Path.GetFullPath(Value("--out=", "render-results.jsonl")));
    }
}

internal sealed record ScenarioResult(
    string Mode,
    bool Transparent,
    int Dpi,
    int Items,
    string Scenario,
    int Steps,
    double WallMs,
    double CpuMs,
    int Frames,
    double CpuMsPerFrame,
    double PrivateBytesMB,
    double WorkingSetMB);
