using Tents.App;

namespace Tents.Tests.App;

public sealed class AppOptionsTests
{
    [Fact]
    public void Default_IsSoftwareRendering()
    {
        var options = AppOptions.Parse([]);

        Assert.False(options.HardwareRendering);
        Assert.Empty(options.Unknown);
    }

    [Fact]
    public void HardwareRendering_IsAnOptIn() => Assert.True(AppOptions.Parse(["--HARDWARE-rendering"]).HardwareRendering);

    [Fact]
    public void UnknownArguments_AreKeptForTheLog_NotFatal()
    {
        var options = AppOptions.Parse(["--nope", "x"]);

        Assert.Equal(["--nope", "x"], options.Unknown);
    }

    [Fact]
    public void Grid_IsOffByDefault() => Assert.Equal(0, AppOptions.Parse([]).SnapGrid);

    [Theory]
    [InlineData("--grid=16", 16)]
    [InlineData("--GRID=200", 200)]
    public void Grid_TakesAPixelSize(string arg, int grid) => Assert.Equal(grid, AppOptions.Parse([arg]).SnapGrid);

    [Theory]
    [InlineData("--grid=1")]
    [InlineData("--grid=0")]
    [InlineData("--grid=-5")]
    [InlineData("--grid=201")]
    [InlineData("--grid=abc")]
    [InlineData("--grid=")]
    public void Grid_RejectsNonsense(string arg)
    {
        var options = AppOptions.Parse([arg]);

        Assert.Equal(0, options.SnapGrid);
        Assert.Equal([arg], options.Unknown);
    }

    [Fact]
    public void LayoutFile_IsTakenAsGiven()
    {
        Assert.Null(AppOptions.Parse([]).LayoutFile);
        Assert.Equal(@"D:\Try\Layout.json", AppOptions.Parse([@"--layout-file=""D:\Try\Layout.json"""]).LayoutFile);
    }
}
