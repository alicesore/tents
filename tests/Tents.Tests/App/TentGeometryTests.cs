using Tents.App.Chrome;
using Tents.Core;

namespace Tents.Tests.App;

public sealed class TentGeometryTests
{
    private static readonly Tent _tent = new(TentId.New(), "Work", new PixelRect(100, 200, 300, 400));

    [Fact]
    public void AnExpandedTent_UsesItsBounds() => Assert.Equal(_tent.Bounds, TentGeometry.WindowBounds(_tent, 1.5));

    [Theory]
    [InlineData(1.0, 30)]
    [InlineData(1.5, 45)]
    [InlineData(2.0, 60)]
    public void ARolledUpTent_IsAsTallAsItsTitleBar(double scale, int height)
    {
        var window = TentGeometry.WindowBounds(_tent with { IsRolledUp = true }, scale);

        Assert.Equal(new PixelRect(100, 200, 300, height), window);
    }

    [Fact]
    public void ARolledUpTent_NeverGrowsPastItsStoredHeight()
    {
        var tiny = new Tent(TentId.New(), "t", new PixelRect(0, 0, 200, 20), isRolledUp: true);

        Assert.Equal(20, TentGeometry.WindowBounds(tiny, 2.0).Height);
    }

    [Fact]
    public void MovingARolledUpTent_KeepsItsExpandedHeight()
    {
        var rolled = _tent with { IsRolledUp = true };

        var stored = TentGeometry.StoredBounds(rolled, new PixelRect(500, 50, 350, 30));

        Assert.Equal(new PixelRect(500, 50, 350, 400), stored);
    }

    [Fact]
    public void MovingAnExpandedTent_StoresTheWindow() =>
        Assert.Equal(new PixelRect(1, 2, 3, 4), TentGeometry.StoredBounds(_tent, new PixelRect(1, 2, 3, 4)));

    [Fact]
    public void RollUpAndExpand_RestoreTheSameWindow()
    {
        var rolled = TentGeometry.WindowBounds(_tent with { IsRolledUp = true }, 1.0);
        var stored = TentGeometry.StoredBounds(_tent with { IsRolledUp = true }, rolled);

        Assert.Equal(_tent.Bounds, TentGeometry.WindowBounds(_tent with { Bounds = stored }, 1.0));
    }

    [Fact]
    public void TitleText_ShowsTheHiddenItemCountWhileRolledUp()
    {
        Assert.Equal("Work", TentGeometry.TitleText(_tent, 5));
        Assert.Equal("Work (5)", TentGeometry.TitleText(_tent with { IsRolledUp = true }, 5));
        Assert.Equal("Work", TentGeometry.TitleText(_tent with { IsRolledUp = true }, 0));
    }
}
