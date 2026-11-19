using Tents.Core;

namespace Tents.App.Chrome;

/// <summary>
/// The window rectangle of a tent, and the stored bounds that go back into the layout. Pure.
/// </summary>
/// <remarks>
/// A rolled-up tent's window is only as tall as its title bar, but the layout keeps the expanded size
/// (<see cref="Tent.Bounds"/>), so expanding restores it. Moving or widening a rolled-up tent changes the stored
/// position and width, never the stored height.
/// </remarks>
internal static class TentGeometry
{
    /// <summary>Height of a rolled-up tent in device-independent units: the title bar plus the window border.</summary>
    public const double RolledUpHeight = TentTheme.TitleBarHeight + 2;

    /// <summary>The window's rectangle for a tent at a DPI scale.</summary>
    public static PixelRect WindowBounds(Tent tent, double scale)
    {
        ArgumentNullException.ThrowIfNull(tent);
        if (!tent.IsRolledUp)
        {
            return tent.Bounds;
        }

        var height = Math.Min(tent.Bounds.Height, Math.Max(1, (int)Math.Round(RolledUpHeight * scale, MidpointRounding.AwayFromZero)));
        return tent.Bounds.WithSize(tent.Bounds.Width, height);
    }

    /// <summary>The bounds to store after the user moved or resized the window to <paramref name="window"/>.</summary>
    public static PixelRect StoredBounds(Tent tent, PixelRect window)
    {
        ArgumentNullException.ThrowIfNull(tent);
        return tent.IsRolledUp ? window.WithSize(window.Width, tent.Bounds.Height) : window;
    }

    /// <summary>The title text: the tent's title, plus how many items it hides while rolled up.</summary>
    public static string TitleText(Tent tent, int itemCount)
    {
        ArgumentNullException.ThrowIfNull(tent);
        return tent.IsRolledUp && itemCount > 0
            ? string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{tent.Title} ({itemCount})")
            : tent.Title;
    }
}
