namespace Tents.App;

/// <summary>Command-line options. Parsed once at start; unknown arguments are reported, not fatal.</summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>--hardware-rendering</c>: use WPF's default GPU rendering instead of software rendering. Software rendering
/// is the M1 default (spike 3: 0.55 s against 2.7 s startup for the typical setup) until the interactive measurement of
/// M1 step 12 confirms or replaces it.</item>
/// </list>
/// </remarks>
internal sealed record AppOptions
{
    public const string HardwareRenderingSwitch = "--hardware-rendering";

    public static AppOptions Default { get; } = new();

    /// <summary>True to keep WPF's hardware rendering. Default false: software rendering (M1 step 12).</summary>
    public bool HardwareRendering { get; init; }

    /// <summary>Arguments that were not understood, for the log.</summary>
    public IReadOnlyList<string> Unknown { get; init; } = [];

    public static AppOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var options = new AppOptions();
        var unknown = new List<string>();
        foreach (var arg in args)
        {
            switch (arg.ToLowerInvariant())
            {
                case HardwareRenderingSwitch:
                    options = options with { HardwareRendering = true };
                    break;
                default:
                    unknown.Add(arg);
                    break;
            }
        }

        return options with { Unknown = unknown };
    }
}
