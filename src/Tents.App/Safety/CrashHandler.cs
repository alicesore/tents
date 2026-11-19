using System.Globalization;
using System.Text;
using Tents.Shell.Desktop;
using Tents.Shell.IconLayer;

namespace Tents.App.Safety;

/// <summary>
/// The first layer of crash safety (M1 step 21): when an exception is about to end the process, turn the native
/// desktop icons back on before it goes, and leave a short crash note.
/// </summary>
/// <remarks>
/// <para><b>Why not only <c>finally</c>.</b> An unhandled exception ends the process without running outer
/// <c>finally</c> blocks reliably (spike 2), so the restore runs in <see cref="AppDomain.UnhandledException"/>, which
/// sees exceptions from every thread, including the UI thread's dispatcher.</para>
/// <para><b>Independent of what crashed.</b> The restore uses a fresh desktop view on a fresh shell thread, so it works
/// even when the crash came from the app's own shell thread. It is bounded (a few seconds); if it cannot finish, the
/// watchdog and the next start still find the state file.</para>
/// <para><b>Privacy.</b> The crash note has the exception types and stack traces, not the messages, which can contain
/// file names from the desktop.</para>
/// </remarks>
internal static class CrashHandler
{
    public static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(3);

    private static int _handled;
    private static bool _restoreIcons = true;

    /// <summary>The crash note, next to the log.</summary>
    public static string CrashNotePath => Path.Combine(Path.GetDirectoryName(AppLog.DefaultFilePath)!, "crash.txt");

    /// <summary>Installs the handler for this process. Call once, first thing in Main.</summary>
    /// <param name="restoreIcons">False when this run never touches the native desktop (<c>--desktop-folder</c>).</param>
    public static void Install(bool restoreIcons = true)
    {
        _restoreIcons = restoreIcons;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => OnUnhandled(e.ExceptionObject as Exception);
    }

    /// <summary>Restores the icons (if Tents turned them off) and writes the crash note. Runs at most once.</summary>
    internal static void OnUnhandled(Exception? exception)
    {
        if (Interlocked.Exchange(ref _handled, 1) == 1)
        {
            return;
        }

        try
        {
            if (_restoreIcons)
            {
                RestoreIcons(new IconLayerStateFile(), static source => new DesktopIconSwitch(source));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Nothing more to do here: the watchdog and the next start cover it.
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashNotePath)!);
            File.WriteAllText(CrashNotePath, Describe(exception, DateTimeOffset.Now), new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Turns the icons back on if the state file says Tents turned them off. Returns true when the state file is gone
    /// afterwards (restored, or nothing to do).
    /// </summary>
    internal static bool RestoreIcons(IconLayerStateFile stateFile, Func<DesktopItemSource, IDesktopIconSwitch> createSwitch)
    {
        ArgumentNullException.ThrowIfNull(stateFile);
        ArgumentNullException.ThrowIfNull(createSwitch);
        if (!stateFile.Exists)
        {
            return true;
        }

        using var source = new DesktopItemSource();
        using var guard = new IconLayerGuard(createSwitch(source), stateFile);
        return guard.TurnIconsOnAsync().Wait(RestoreTimeout) && !stateFile.Exists;
    }

    /// <summary>The crash note: time, exception types and stack traces, no messages. Pure.</summary>
    internal static string Describe(Exception? exception, DateTimeOffset at)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"Tents crashed at {at:yyyy-MM-dd HH:mm:ss zzz}.").AppendLine();
        text.AppendLine("Exception messages are left out because they can contain file names.");
        for (var current = exception; current is not null; current = current.InnerException)
        {
            text.AppendLine();
            text.Append(CultureInfo.InvariantCulture, $"{current.GetType().FullName} (HResult 0x{current.HResult:X8})").AppendLine();
            text.AppendLine(current.StackTrace ?? "(no stack trace)");
        }

        return text.ToString();
    }
}
