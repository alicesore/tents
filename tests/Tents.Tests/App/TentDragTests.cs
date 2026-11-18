using System.Windows;
using Tents.App.Chrome;
using Tents.Core;

namespace Tents.Tests.App;

public sealed class TentDragTests
{
    private static readonly Size _size = new(300, 400);
    private static readonly PixelRect _start = new(100, 200, 300, 400);

    [Theory]
    [InlineData(150, 14, DragHandle.Move)]
    [InlineData(150, 200, DragHandle.None)]
    [InlineData(2, 200, DragHandle.Left)]
    [InlineData(297, 200, DragHandle.Right)]
    [InlineData(150, 2, DragHandle.Top)]
    [InlineData(150, 398, DragHandle.Bottom)]
    [InlineData(2, 2, DragHandle.TopLeft)]
    [InlineData(298, 2, DragHandle.TopRight)]
    [InlineData(2, 398, DragHandle.BottomLeft)]
    [InlineData(298, 398, DragHandle.BottomRight)]
    [InlineData(-1, 10, DragHandle.None)]
    [InlineData(300, 10, DragHandle.None)]
    public void HandleAt_CornersThenEdgesThenTitleBar(double x, double y, DragHandle expected) =>
        Assert.Equal(expected, TentDrag.HandleAt(new Point(x, y), _size, titleBarHeight: 28, rolledUp: false));

    [Theory]
    [InlineData(150, 2, DragHandle.Move)] // no top edge
    [InlineData(150, 26, DragHandle.Move)] // no bottom edge
    [InlineData(2, 2, DragHandle.Left)]
    [InlineData(298, 26, DragHandle.Right)]
    public void HandleAt_ARolledUpTent_OnlyResizesSideways(double x, double y, DragHandle expected) =>
        Assert.Equal(expected, TentDrag.HandleAt(new Point(x, y), new Size(300, 28), titleBarHeight: 28, rolledUp: true));

    [Fact]
    public void Move_KeepsTheSize() =>
        Assert.Equal(new PixelRect(130, 150, 300, 400), TentDrag.Apply(_start, DragHandle.Move, 30, -50, 120, 80));

    [Fact]
    public void ResizeRightAndBottom_KeepTheTopLeftCorner() =>
        Assert.Equal(new PixelRect(100, 200, 350, 420), TentDrag.Apply(_start, DragHandle.BottomRight, 50, 20, 120, 80));

    [Fact]
    public void ResizeLeftAndTop_KeepTheBottomRightCorner() =>
        Assert.Equal(new PixelRect(80, 230, 320, 370), TentDrag.Apply(_start, DragHandle.TopLeft, -20, 30, 120, 80));

    [Fact]
    public void Resize_NeverGoesBelowTheMinimum_AndTheOppositeEdgeStays()
    {
        var left = TentDrag.Apply(_start, DragHandle.Left, 1000, 0, 120, 80);
        var top = TentDrag.Apply(_start, DragHandle.Top, 0, 1000, 120, 80);
        var right = TentDrag.Apply(_start, DragHandle.Right, -1000, 0, 120, 80);

        Assert.Equal(new PixelRect(400 - 120, 200, 120, 400), left);
        Assert.Equal(new PixelRect(100, 600 - 80, 300, 80), top);
        Assert.Equal(new PixelRect(100, 200, 120, 400), right);
    }

    [Fact]
    public void SingleEdges_ChangeOnlyTheirAxis()
    {
        Assert.Equal(new PixelRect(100, 200, 300, 450), TentDrag.Apply(_start, DragHandle.Bottom, 77, 50, 120, 80));
        Assert.Equal(new PixelRect(100, 200, 340, 400), TentDrag.Apply(_start, DragHandle.Right, 40, 99, 120, 80));
    }

    [Fact]
    public void Grid_SnapsTheMovedCorner() =>
        Assert.Equal(new PixelRect(140, 180, 300, 400), TentDrag.Apply(_start, DragHandle.Move, 37, -22, 120, 80, grid: 20));

    [Fact]
    public void Grid_SnapsOnlyTheMovingEdges()
    {
        var resized = TentDrag.Apply(new PixelRect(103, 207, 300, 400), DragHandle.BottomRight, 11, 11, 120, 80, grid: 20);

        Assert.Equal(103, resized.X); // the fixed corner is not moved onto the grid
        Assert.Equal(207, resized.Y);
        Assert.Equal(420, resized.X + resized.Width);
        Assert.Equal(620, resized.Y + resized.Height);
    }

    [Fact]
    public void NoGrid_IsPixelExact() =>
        Assert.Equal(new PixelRect(101, 199, 300, 400), TentDrag.Apply(_start, DragHandle.Move, 1, -1, 120, 80, grid: 0));

    [Theory]
    [InlineData(3, 0, false)]
    [InlineData(4, 0, true)]
    [InlineData(0, -4, true)]
    public void IsDrag_UsesTheSystemThreshold(double dx, double dy, bool expected) =>
        Assert.Equal(expected, TentDrag.IsDrag(new Vector(dx, dy), 4, 4));
}
