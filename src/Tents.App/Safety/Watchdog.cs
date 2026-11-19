using System.Diagnostics;
using System.Globalization;
using Tents.Shell.Desktop;
using Tents.Shell.IconLayer;

namespace Tents.App.Safety;

/// <summary>The process a watchdog guards: its id and start time, so a reused process id is never mistaken for it.</summary>
internal readonly record struct WatchedProcess(int ProcessId, DateTimeOffset StartTime)
{
    public const string Switch = "--watchdog=";

    public static WatchedProcess Current
    {
        get
        {
            using var process = Process.GetCurrentProcess();
            return new WatchedProcess(process.Id, new DateTimeOffset(process.StartTime.ToUniversalTime()));
        }
    }

    /// <summary>The command-line argument that starts a watchdog for this process.</summary>
    public string ToArgument() => string.Create(CultureInfo.InvariantCulture, $"{Switch}{ProcessId}:{StartTime.UtcTicks}");

    /// <summary>Finds a watchdog argument. Pure.</summary>
    public static bool TryParse(IReadOnlyList<string> args, out WatchedProcess watched)
    {
        ArgumentNullException.ThrowIfNull(args);
        foreach (var arg in args)
        {
            if (!arg.StartsWith(Switch, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = arg[Switch.Length..].Split(':');
            if (parts.Length == 2
                && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
                && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks >= DateTimeOffset.MinValue.UtcTicks && ticks <= DateTimeOffset.MaxValue.UtcTicks)
            {
                watched = new WatchedProcess(id, new DateTimeOffset(ticks, TimeSpan.Zero));
                return true;
            }
        }

        watched = default;
        return false;
    }
}

/// <summary>
/// The second layer of crash safety (M1 step 21): a small process (<c>Tents.exe --watchdog=...</c>) that waits on the
/// main process's handle and, if the main process ends while its state file says the native icons are off, turns them
/// back on. Covers a kill (Task Manager, <c>taskkill /f</c>), which no handler inside the main process can.
/// </summary>
/// <remarks>
/// <para><b>Idle cost.</b> One thread blocked on a process handle (<c>WaitForExit</c>): no polling, no timer, no
/// window. It loads no WPF.</para>
/// <para><b>When it acts.</b> Only if the state file exists <i>and</i> names the watched process: after a clean exit
/// the file is gone, and a newer Tents that started meanwhile owns its own file. Recovery itself is part B's
/// <see cref="IconLayerGuard.RecoverAsync"/>, which re-checks that the owner is not running.</para>
/// <para><b>Lifetime.</b> It ends when the main process ends, after at most one recovery. If the watchdog itself is
/// killed, the third layer (the state file checked at the next start) still covers the user.</para>
/// </remarks>
internal static class Watchdog
{
    /// <summary>Starts a watchdog for the current process. Returns false (and logs) when it could not be started.</summary>
    public static bool Launch(AppLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            log.Write("Watchdog not started: the executable path is unknown.");
            return false;
        }

        try
        {
            var info = new ProcessStartInfo(exe, WatchedProcess.Current.ToArgument())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(info);
            log.Write(process is null ? "Watchdog not started." : "Watchdog started.");
            return process is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            log.Write($"Watchdog not started ({ex.GetType().Name}); the state file still covers a crash at the next start.");
            return false;
        }
    }

    /// <summary>The watchdog process's entry point: waits for the main process, then recovers if needed.</summary>
    public static int Run(WatchedProcess watched)
    {
        var log = new AppLog(Path.Combine(Path.GetDirectoryName(AppLog.DefaultFilePath)!, "watchdog.log"));
        try
        {
            if (!WaitForExit(watched))
            {
                log.Write("Watchdog: the watched process is not running; nothing to guard.");
            }

            var stateFile = new IconLayerStateFile();
            if (!ShouldRecover(stateFile.Read(), watched.ProcessId))
            {
                return 0;
            }

            using var source = new DesktopItemSource();
            var result = RecoverAsync(stateFile, new DesktopIconSwitch(source)).Wait(TimeSpan.FromSeconds(10));
            log.Write(result ? "Watchdog: Tents ended with the native icons off; they were turned back on." : "Watchdog: recovery did not finish; the next start retries.");
            return result ? 0 : 1;
        }
        catch (Exception ex)
        {
            log.Write($"Watchdog failed ({ex.GetType().Name}); the next start of Tents restores the icons.");
            return 1;
        }
        finally
        {
            log.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>True when the state file says the watched process turned the icons off. Pure.</summary>
    internal static bool ShouldRecover(IconLayerState? state, int watchedProcessId) =>
        state is not null && (state.ProcessId == watchedProcessId || state.ProcessId == 0);

    /// <summary>Recovery after the watched process ended: part B's startup recovery, run by the watchdog.</summary>
    internal static async Task RecoverAsync(IconLayerStateFile stateFile, IDesktopIconSwitch iconSwitch)
    {
        using var guard = new IconLayerGuard(iconSwitch, stateFile);
        await guard.RecoverAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Blocks until the watched process has ended. Returns false when it was not running (or was another process with
    /// the same id).
    /// </summary>
    internal static bool WaitForExit(WatchedProcess watched)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(watched.ProcessId);
        }
        catch (ArgumentException)
        {
            return false;
        }

        using (process)
        {
            try
            {
                if (Math.Abs((new DateTimeOffset(process.StartTime.ToUniversalTime()) - watched.StartTime).TotalSeconds) >= 1)
                {
                    return false; // the id was reused
                }

                process.WaitForExit(); // a wait on the process handle: no polling
                return true;
            }
            catch (InvalidOperationException)
            {
                return false; // it ended between the lookup and the wait
            }
        }
    }
}
