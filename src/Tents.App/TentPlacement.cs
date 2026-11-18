using Tents.Core;
using Tents.Shell.Hosting;

namespace Tents.App;

/// <summary>Where new tents go and how big they are. Pure.</summary>
internal static class TentPlacement
{
    /// <summary>Size of a new tent at 100% scaling, in pixels.</summary>
    public const int DefaultWidth = 380;

    public const int DefaultHeight = 460;

    /// <summary>Gap between a new tent and the work area's edge at 100% scaling.</summary>
    public const int Margin = 24;

    /// <summary>The bounds when nothing is known about the monitors (no interactive session).</summary>
    public static PixelRect Fallback { get; } = new(100, 100, DefaultWidth, DefaultHeight);

    /// <summary>
    /// The first tent: near the top-right corner of the primary monitor's work area, where it covers the fewest of the
    /// places Windows puts desktop icons (they fill from the top-left).
    /// </summary>
    public static PixelRect ForFirstTent(MonitorInfo? primary)
    {
        if (primary is null)
        {
            return Fallback;
        }

        var work = primary.WorkArea;
        var scale = primary.Scale;
        var width = Math.Min(Scaled(DefaultWidth, scale), Math.Max(1, work.Width));
        var height = Math.Min(Scaled(DefaultHeight, scale), Math.Max(1, work.Height));
        var margin = Scaled(Margin, scale);
        var x = Math.Max(work.X, work.X + work.Width - width - margin);
        var y = Math.Min(work.Y + margin, Math.Max(work.Y, work.Y + work.Height - height));
        return new PixelRect(x, y, width, height);
    }

    private static int Scaled(int value, double scale) => (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);
}
