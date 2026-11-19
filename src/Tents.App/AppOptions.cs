namespace Tents.App;

/// <summary>Command-line options. Parsed once at start; unknown arguments are reported, not fatal.</summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>--hardware-rendering</c>: use WPF's default GPU rendering instead of software rendering. Software rendering
/// is the M1 default (spike 3: 0.55 s against 2.7 s startup for the typical setup) until the interactive measurement of
/// M1 step 12 confirms or replaces it.</item>
/// <item><c>--grid=N</c>: moving and resizing a tent snaps to an N-pixel grid. Off by default (M1 step 14); a setting
/// in the M2 settings window replaces it.</item>
/// </list>
/// </remarks>
internal sealed record AppOptions
{
    public const string HardwareRenderingSwitch = "--hardware-rendering";

    public const string GridPrefix = "--grid=";

    /// <summary>Largest grid accepted, in pixels.</summary>
    public const int MaxGrid = 200;

    public static AppOptions Default { get; } = new();

    /// <summary>True to keep WPF's hardware rendering. Default false: software rendering (M1 step 12).</summary>
    public bool HardwareRendering { get; init; }

    /// <summary>Snap grid for moving and resizing tents, in physical pixels; 0 (the default) for none.</summary>
    public int SnapGrid { get; init; }

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
            else if (lower.StartsWith(GridPrefix, StringComparison.Ordinal)
                && int.TryParse(lower.AsSpan(GridPrefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var grid)
                && grid is > 1 and <= MaxGrid)
            {
                options = options with { SnapGrid = grid };
            }
            else
            {
                unknown.Add(arg);
            }
        }

        return options with { Unknown = unknown };
    }
}
