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

    /// <summary>How far a new tent is offset from the tent it was created from, at 100% scaling.</summary>
    public const int CascadeOffset = 32;

    /// <summary>
    /// A new tent created from <paramref name="source"/>'s menu: default size, cascaded down and to the right of the
    /// source on the source's monitor. If that would leave the monitor's work area, it goes to the work area's top-left
    /// corner instead.
    /// </summary>
    public static PixelRect ForNewTent(PixelRect source, IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        var monitor = MonitorOf(source, monitors);
        if (monitor is null)
        {
            return source.WithPosition(source.X + CascadeOffset, source.Y + CascadeOffset).WithSize(DefaultWidth, DefaultHeight);
        }

        var scale = monitor.Scale;
        var work = monitor.WorkArea;
        var width = Math.Min(Scaled(DefaultWidth, scale), work.Width);
        var height = Math.Min(Scaled(DefaultHeight, scale), work.Height);
        var offset = Scaled(CascadeOffset, scale);
        var x = source.X + offset;
        var y = source.Y + offset;
        if (x < work.X || y < work.Y || x + width > work.X + work.Width || y + height > work.Y + work.Height)
        {
            var margin = Scaled(Margin, scale);
            x = work.X + Math.Min(margin, work.Width - width);
            y = work.Y + Math.Min(margin, work.Height - height);
        }

        return new PixelRect(x, y, width, height);
    }

    /// <summary>The monitor that contains the centre of <paramref name="bounds"/>, else the primary, else the first.</summary>
    public static MonitorInfo? MonitorOf(PixelRect bounds, IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        var cx = bounds.X + (bounds.Width / 2);
        var cy = bounds.Y + (bounds.Height / 2);
        return monitors.FirstOrDefault(m => cx >= m.Bounds.X && cx < m.Bounds.X + m.Bounds.Width && cy >= m.Bounds.Y && cy < m.Bounds.Y + m.Bounds.Height)
            ?? monitors.FirstOrDefault(m => m.IsPrimary)
            ?? (monitors.Count > 0 ? monitors[0] : null);
    }

    private static int Scaled(int value, double scale) => (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);
}
