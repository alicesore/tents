using System.Windows;
using Tents.App.Items;

namespace Tents.Tests.App;

public sealed class ItemGridTests
{
    // 4 columns: 6 + 4 * 84 + 6 = 348.
    private const double _width = 350;

    [Fact]
    public void Columns_FitTheWidth_AtLeastOne()
    {
        Assert.Equal(4, new ItemGrid(_width, 10).Columns);
        Assert.Equal(1, new ItemGrid(40, 10).Columns);
    }

    [Fact]
    public void RowsAndExtent()
    {
        var grid = new ItemGrid(_width, 10);

        Assert.Equal(3, grid.Rows);
        Assert.Equal((3 * ItemGrid.CellHeight) + (2 * ItemGrid.Padding), grid.ExtentHeight);
        Assert.Equal(0, new ItemGrid(_width, 0).ExtentHeight);
    }

    [Fact]
    public void CellRect_FillsRowsLeftToRight()
    {
        var grid = new ItemGrid(_width, 10);

        Assert.Equal(new Rect(ItemGrid.Padding, ItemGrid.Padding, ItemGrid.CellWidth, ItemGrid.CellHeight), grid.CellRect(0));
        Assert.Equal(new Rect(ItemGrid.Padding + (3 * ItemGrid.CellWidth), ItemGrid.Padding, ItemGrid.CellWidth, ItemGrid.CellHeight), grid.CellRect(3));
        Assert.Equal(new Rect(ItemGrid.Padding, ItemGrid.Padding + ItemGrid.CellHeight, ItemGrid.CellWidth, ItemGrid.CellHeight), grid.CellRect(4));
    }

    [Fact]
    public void VisibleRange_CoversOnlyTheRowsInView()
    {
        var grid = new ItemGrid(_width, 200); // 50 rows

        Assert.Equal((0, 8), grid.VisibleRange(0, ItemGrid.CellHeight * 1.5)); // rows 0 and 1
        var (first, count) = grid.VisibleRange(ItemGrid.CellHeight * 10, ItemGrid.CellHeight * 2);
        Assert.InRange(first, 36, 40);
        Assert.InRange(count, 8, 16);
        Assert.Equal((0, 0), new ItemGrid(_width, 0).VisibleRange(0, 100));
    }

    [Fact]
    public void VisibleRange_DoesNotRunPastTheLastItem()
    {
        var grid = new ItemGrid(_width, 6);

        Assert.Equal((0, 6), grid.VisibleRange(0, 10_000));
    }

    [Fact]
    public void IndexAt_HitsCells_AndMissesEmptySpace()
    {
        var grid = new ItemGrid(_width, 6);

        Assert.Equal(0, grid.IndexAt(new Point(10, 10), 0));
        Assert.Equal(5, grid.IndexAt(new Point(ItemGrid.Padding + ItemGrid.CellWidth + 1, ItemGrid.Padding + ItemGrid.CellHeight + 1), 0));
        Assert.Equal(-1, grid.IndexAt(new Point(ItemGrid.Padding + (2 * ItemGrid.CellWidth) + 1, ItemGrid.Padding + ItemGrid.CellHeight + 1), 0)); // after the last item
        Assert.Equal(-1, grid.IndexAt(new Point(2, 2), 0)); // in the padding
        Assert.Equal(-1, grid.IndexAt(new Point(_width - 1, 10), 0)); // right of the last column
    }

    [Fact]
    public void IndexAt_AccountsForTheScrollOffset()
    {
        var grid = new ItemGrid(_width, 40);

        Assert.Equal(4, grid.IndexAt(new Point(10, 10), ItemGrid.CellHeight));
    }

    [Fact]
    public void InsertionIndexAt_BeforeOrAfterTheCellUnderThePoint()
    {
        var grid = new ItemGrid(_width, 6);
        var y = ItemGrid.Padding + 10;

        Assert.Equal(0, grid.InsertionIndexAt(new Point(ItemGrid.Padding + 10, y), 0));
        Assert.Equal(1, grid.InsertionIndexAt(new Point(ItemGrid.Padding + ItemGrid.CellWidth - 10, y), 0));
        Assert.Equal(6, grid.InsertionIndexAt(new Point(10, ItemGrid.Padding + (5 * ItemGrid.CellHeight)), 0)); // below the last row
        Assert.Equal(6, grid.InsertionIndexAt(new Point(ItemGrid.Padding + (3 * ItemGrid.CellWidth) + 60, ItemGrid.Padding + ItemGrid.CellHeight + 5), 0));
        Assert.Equal(0, new ItemGrid(_width, 0).InsertionIndexAt(new Point(100, 100), 0));
    }

    [Fact]
    public void ClampOffset_StaysInsideTheContent()
    {
        var grid = new ItemGrid(_width, 40); // 10 rows

        Assert.Equal(0, grid.ClampOffset(-50, 300));
        Assert.Equal(grid.ExtentHeight - 300, grid.ClampOffset(1e6, 300));
        Assert.Equal(0, grid.ClampOffset(100, 10_000));
    }

    [Theory]
    [InlineData(1.0, 48)]
    [InlineData(1.25, 60)]
    [InlineData(1.5, 72)]
    [InlineData(2.0, 96)]
    public void IconPixelSize_IsExactForTheDpi(double scale, int pixels) => Assert.Equal(pixels, ItemGrid.IconPixelSize(scale));
}
