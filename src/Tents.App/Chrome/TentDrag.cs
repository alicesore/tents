using System.Windows;
using Tents.Core;

namespace Tents.App.Chrome;

/// <summary>Which part of a tent a drag moves.</summary>
public enum DragHandle
{
    None,
    Move,
    Left,
    Top,
    Right,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// Moving and resizing a tent by dragging its title bar or edges. Pure: hit testing in device-independent units, bounds
/// in physical pixels, so every rule is unit-tested.
/// </summary>
internal static class TentDrag
{
    /// <summary>How close to an edge (device-independent units) a press starts a resize.</summary>
    public const double ResizeBorder = 6;

    /// <summary>Smallest tent at 100% scaling, in pixels.</summary>
    public const int MinWidth = 120;

    public const int MinHeight = 80;

    /// <summary>
    /// The handle under a point of the window (device-independent units, origin top-left). Corners win over edges, edges
    /// over the title bar. A rolled-up tent can only be made wider or narrower.
    /// </summary>
    public static DragHandle HandleAt(Point point, Size size, double titleBarHeight, bool rolledUp)
    {
        if (point.X < 0 || point.Y < 0 || point.X >= size.Width || point.Y >= size.Height)
        {
            return DragHandle.None;
        }

        var left = point.X < ResizeBorder;
        var right = point.X >= size.Width - ResizeBorder;
        var top = !rolledUp && point.Y < ResizeBorder;
        var bottom = !rolledUp && point.Y >= size.Height - ResizeBorder;

        return (left, right, top, bottom) switch
        {
            (true, _, true, _) => DragHandle.TopLeft,
            (_, true, true, _) => DragHandle.TopRight,
            (true, _, _, true) => DragHandle.BottomLeft,
            (_, true, _, true) => DragHandle.BottomRight,
            (true, _, _, _) => DragHandle.Left,
            (_, true, _, _) => DragHandle.Right,
            (_, _, true, _) => DragHandle.Top,
            (_, _, _, true) => DragHandle.Bottom,
            _ when point.Y < titleBarHeight => DragHandle.Move,
            _ => DragHandle.None,
        };
    }

    /// <summary>
    /// The bounds after dragging <paramref name="handle"/> by (<paramref name="dx"/>, <paramref name="dy"/>) physical
    /// pixels from <paramref name="start"/>. The edge opposite the handle stays put; the size never goes below the
    /// minimum. With <paramref name="grid"/> greater than 1, the moving edges (or, for a move, the top-left corner) snap
    /// to multiples of it in screen coordinates.
    /// </summary>
    public static PixelRect Apply(PixelRect start, DragHandle handle, int dx, int dy, int minWidth, int minHeight, int grid = 0)
    {
        var l = start.X;
        var t = start.Y;
        var r = start.X + start.Width;
        var b = start.Y + start.Height;

        if (handle == DragHandle.Move)
        {
            var x = Snap(l + dx, grid);
            var y = Snap(t + dy, grid);
            return new PixelRect(x, y, start.Width, start.Height);
        }

        if (handle is DragHandle.Left or DragHandle.TopLeft or DragHandle.BottomLeft)
        {
            l = Math.Min(Snap(l + dx, grid), r - minWidth);
        }

        if (handle is DragHandle.Right or DragHandle.TopRight or DragHandle.BottomRight)
        {
            r = Math.Max(Snap(r + dx, grid), l + minWidth);
        }

        if (handle is DragHandle.Top or DragHandle.TopLeft or DragHandle.TopRight)
        {
            t = Math.Min(Snap(t + dy, grid), b - minHeight);
        }

        if (handle is DragHandle.Bottom or DragHandle.BottomLeft or DragHandle.BottomRight)
        {
            b = Math.Max(Snap(b + dy, grid), t + minHeight);
        }

        return new PixelRect(l, t, r - l, b - t);
    }

    /// <summary>True when the pointer moved far enough from the press to be a drag rather than a click.</summary>
    public static bool IsDrag(Vector delta, double minimumHorizontal, double minimumVertical) =>
        Math.Abs(delta.X) >= minimumHorizontal || Math.Abs(delta.Y) >= minimumVertical;

    private static int Snap(int value, int grid) =>
        grid > 1 ? (int)Math.Round(value / (double)grid, MidpointRounding.AwayFromZero) * grid : value;
}
