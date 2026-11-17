namespace Tents.Core;

/// <summary>
/// A rectangle in physical pixels, in virtual-screen coordinates (the origin is the top-left corner of the primary
/// monitor, so other monitors can have negative coordinates).
/// </summary>
/// <remarks>
/// Physical pixels rather than device-independent units, because a layout is stored per monitor configuration and
/// the desktop host positions tent windows in physical pixels (per-monitor v2 DPI awareness). Mixed-DPI handling
/// is M2 work (MILESTONES.md M2 steps 3 and 4).
/// </remarks>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    /// <summary>True when both <see cref="Width"/> and <see cref="Height"/> are greater than zero.</summary>
    public bool HasPositiveSize => Width > 0 && Height > 0;

    public PixelRect WithPosition(int x, int y) => this with { X = x, Y = y };

    public PixelRect WithSize(int width, int height) => this with { Width = width, Height = height };
}
