using System.Windows;
using System.Windows.Media;
using Tents.App.Safety;
using Tents.Shell;
using Tents.Shell.Hosting;

namespace Tents.App;

/// <summary>Entry point. No XAML application definition: the app is built in code, which keeps startup small.</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ShellPlatform.EnsureSupported();

        // The watchdog is this same executable started with --watchdog=...; it loads no WPF and shows nothing.
        if (WatchedProcess.TryParse(args, out var watched))
        {
            return Watchdog.Run(watched);
        }

        // Crash safety, first layer: the native icons come back even when an exception ends the process.
        CrashHandler.Install();
        var options = AppOptions.Parse(args);

        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            return 0; // Tents is already running in this session.
        }

        // Spike 3: software rendering starts 2 s faster with 10 tents and uses less memory. Set once, before any window.
        if (!options.HardwareRendering)
        {
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        }

        var log = new AppLog();
        try
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            using var tents = new TentsApp(application, options, log, new OwnedPinHost());
            application.Startup += (_, _) => tents.Start();
            return application.Run();
        }
        finally
        {
            // The UI thread is done; waiting here cannot block a tent.
            log.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }
    }
}
