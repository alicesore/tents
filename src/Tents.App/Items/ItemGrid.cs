using System.Windows;

namespace Tents.App.Items;

/// <summary>
/// Where each item of a tent goes: a grid of equal cells, filled left to right and top to bottom, scrolled vertically.
/// Pure geometry in device-independent units, so hit testing, visible ranges and drop positions are unit-tested.
/// </summary>
/// <param name="ViewportWidth">Width available for cells (the tent's content area).</param>
/// <param name="ItemCount">Number of items.</param>
internal readonly record struct ItemGrid(double ViewportWidth, int ItemCount)
{
    /// <summary>Cell size. 48 px icon plus two lines of label, close to the desktop's medium icon spacing.</summary>
    public const double CellWidth = 84;

    public const double CellHeight = 92;

    /// <summary>Space around the grid.</summary>
    public const double Padding = 6;

    public const double IconSize = 48;

    /// <summary>Columns that fit; at least one, so a very narrow tent still shows its items.</summary>
    public int Columns => Math.Max(1, (int)Math.Floor((ViewportWidth - (2 * Padding)) / CellWidth));

    public int Rows => ItemCount == 0 ? 0 : ((ItemCount - 1) / Columns) + 1;

    /// <summary>Total height of the content, for the scroll extent.</summary>
    public double ExtentHeight => ItemCount == 0 ? 0 : (Rows * CellHeight) + (2 * Padding);

    /// <summary>The largest useful scroll offset for a viewport height.</summary>
    public double MaxOffset(double viewportHeight) => Math.Max(0, ExtentHeight - viewportHeight);

    /// <summary>Clamps a scroll offset to the content.</summary>
    public double ClampOffset(double offset, double viewportHeight) => Math.Clamp(offset, 0, MaxOffset(viewportHeight));

    /// <summary>A cell's rectangle in content coordinates (before scrolling).</summary>
    public Rect CellRect(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        var row = index / Columns;
        var column = index % Columns;
        return new Rect(Padding + (column * CellWidth), Padding + (row * CellHeight), CellWidth, CellHeight);
    }

    /// <summary>The items whose cells intersect the viewport: [first, first + count).</summary>
    public (int First, int Count) VisibleRange(double offset, double viewportHeight)
    {
        if (ItemCount == 0 || viewportHeight <= 0)
        {
            return (0, 0);
        }

        var firstRow = Math.Max(0, (int)Math.Floor((offset - Padding) / CellHeight));
        var lastRow = Math.Max(firstRow, (int)Math.Floor((offset + viewportHeight - Padding) / CellHeight));
        var first = Math.Min(ItemCount, firstRow * Columns);
        var end = Math.Min(ItemCount, (lastRow + 1) * Columns);
        return (first, end - first);
    }

    /// <summary>The item under a point in viewport coordinates, or -1 over empty space.</summary>
    public int IndexAt(Point viewportPoint, double offset)
    {
        var x = viewportPoint.X - Padding;
        var y = viewportPoint.Y + offset - Padding;
        if (x < 0 || y < 0)
        {
            return -1;
        }

        var column = (int)(x / CellWidth);
        var row = (int)(y / CellHeight);
        if (column >= Columns)
        {
            return -1;
        }

        var index = (row * Columns) + column;
        return index < ItemCount ? index : -1;
    }

    /// <summary>
    /// Where a drop at a point inserts, as an index into the current item list (0 to <see cref="ItemCount"/>): before
    /// the item under the point if the point is in the left half of its cell, after it otherwise; at the end over empty
    /// space after the last item.
    /// </summary>
    public int InsertionIndexAt(Point viewportPoint, double offset)
    {
        if (ItemCount == 0)
        {
            return 0;
        }

        var x = Math.Max(0, viewportPoint.X - Padding);
        var y = Math.Max(0, viewportPoint.Y + offset - Padding);
        var row = (int)(y / CellHeight);
        var column = Math.Min(Columns - 1, (int)(x / CellWidth));
        if (row >= Rows)
        {
            return ItemCount;
        }

        var index = (row * Columns) + column;
        var after = x - (column * CellWidth) >= CellWidth / 2;
        return Math.Min(ItemCount, index + (after ? 1 : 0));
    }

    /// <summary>The exact pixel size to load an icon at for a DPI scale (48 px at 100%, 96 px at 200%).</summary>
    public static int IconPixelSize(double dpiScale) => Math.Max(16, (int)Math.Round(IconSize * dpiScale, MidpointRounding.AwayFromZero));
}
