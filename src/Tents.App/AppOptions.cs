namespace Tents.App;

/// <summary>Command-line options. Parsed once at start; unknown arguments are reported, not fatal.</summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>--hardware-rendering</c>: use WPF's default GPU rendering instead of software rendering. Software rendering
/// is the M1 default (spike 3: 0.55 s against 2.7 s startup for the typical setup) until the interactive measurement of
/// M1 step 12 confirms or replaces it.</item>
/// <item><c>--grid=N</c>: moving and resizing a tent snaps to an N-pixel grid. Off by default (M1 step 14); a setting
/// in the M2 settings window replaces it.</item>
/// <item><c>--layout-file=PATH</c>: read and save the layout at PATH instead of <c>%APPDATA%\Tents\layout.json</c>
/// (for trying things out without touching the real layout).</item>
/// <item><c>--keep-native-icons</c>: leave the native desktop icons on; tents show next to them and Pause only hides
/// the tents. By default Tents turns the native icons off while it shows the items (MILESTONES.md M1, "Desktop items"),
/// with Pause, Exit and the three crash-safety layers (M1 steps 19 and 21) to bring them back.</item>
/// <item><c>--desktop-folder=PATH</c>: a measurement and test seam (M1 step 22). Tents shows the items of the folder at
/// PATH instead of the desktop's, and never touches the native desktop: it implies <c>--keep-native-icons</c>, skips the
/// icon-layer recovery and the watchdog, and the crash handler leaves the icons alone. Not for everyday use: a file
/// dropped onto a tent still goes to the real Desktop folder, as in normal use.</item>
/// <item><c>--log-file=PATH</c>: write the log to PATH instead of <c>%LOCALAPPDATA%\Tents\tents.log</c>.</item>
/// </list>
/// </remarks>
internal sealed record AppOptions
{
    public const string HardwareRenderingSwitch = "--hardware-rendering";

    public const string GridPrefix = "--grid=";

    public const string LayoutFilePrefix = "--layout-file=";

    public const string KeepNativeIconsSwitch = "--keep-native-icons";

    public const string DesktopFolderPrefix = "--desktop-folder=";

    public const string LogFilePrefix = "--log-file=";

    /// <summary>Largest grid accepted, in pixels.</summary>
    public const int MaxGrid = 200;

    public static AppOptions Default { get; } = new();

    /// <summary>True to keep WPF's hardware rendering. Default false: software rendering (M1 step 12).</summary>
    public bool HardwareRendering { get; init; }

    /// <summary>Snap grid for moving and resizing tents, in physical pixels; 0 (the default) for none.</summary>
    public int SnapGrid { get; init; }

    /// <summary>Turn the native desktop icons off while tents show the items. True unless <c>--keep-native-icons</c>.</summary>
    public bool HideNativeIcons { get; init; } = true;

    /// <summary>The layout file to use, or null for the default.</summary>
    public string? LayoutFile { get; init; }

    /// <summary>A folder whose items Tents shows instead of the desktop's, or null for the desktop (the default).</summary>
    public string? DesktopFolder { get; init; }

    /// <summary>The log file to use, or null for the default.</summary>
    public string? LogFile { get; init; }

    /// <summary>True when Tents may read or change the native desktop's icon layer: always, unless a folder stands in.</summary>
    public bool UsesNativeDesktop => DesktopFolder is null;

    /// <summary>Arguments that were not understood, for the log.</summary>
    public IReadOnlyList<string> Unknown { get; init; } = [];

    public static AppOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var options = new AppOptions();
        var unknown = new List<string>();
        foreach (var arg in args)
        {
            var lower = arg.ToLowerInvariant();
            if (lower == HardwareRenderingSwitch)
            {
                options = options with { HardwareRendering = true };
            }
            else if (lower == KeepNativeIconsSwitch)
            {
                options = options with { HideNativeIcons = false };
            }
            else if (lower.StartsWith(GridPrefix, StringComparison.Ordinal)
                && int.TryParse(lower.AsSpan(GridPrefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var grid)
                && grid is > 1 and <= MaxGrid)
            {
                options = options with { SnapGrid = grid };
            }
            else if (lower.StartsWith(LayoutFilePrefix, StringComparison.Ordinal) && arg.Length > LayoutFilePrefix.Length)
            {
                options = options with { LayoutFile = arg[LayoutFilePrefix.Length..].Trim('"') };
            }
            else if (lower.StartsWith(DesktopFolderPrefix, StringComparison.Ordinal) && arg.Length > DesktopFolderPrefix.Length)
            {
                options = options with { DesktopFolder = Path.GetFullPath(arg[DesktopFolderPrefix.Length..].Trim('"')) };
            }
            else if (lower.StartsWith(LogFilePrefix, StringComparison.Ordinal) && arg.Length > LogFilePrefix.Length)
            {
                options = options with { LogFile = arg[LogFilePrefix.Length..].Trim('"') };
            }
            else
            {
                unknown.Add(arg);
            }
        }

        // A stand-in folder never touches the native desktop, so the icons stay on.
        if (options.DesktopFolder is not null)
        {
            options = options with { HideNativeIcons = false };
        }

        return options with { Unknown = unknown };
    }
}
