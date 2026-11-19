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

    [Fact]
    public void NativeIcons_AreSwitchedOffByDefault_WithAnOptOut()
    {
        Assert.True(AppOptions.Parse([]).HideNativeIcons);
        Assert.False(AppOptions.Parse(["--keep-native-icons"]).HideNativeIcons);
        Assert.Equal(["--hide-native-icons"], AppOptions.Parse(["--hide-native-icons"]).Unknown); // the old developer flag is gone
    }

    [Fact]
    public void DesktopFolder_IsAFullPath_AndKeepsTheNativeIcons()
    {
        var plain = AppOptions.Parse([]);
        Assert.Null(plain.DesktopFolder);
        Assert.True(plain.UsesNativeDesktop);

        var options = AppOptions.Parse([@"--desktop-folder=""D:\Bench\Items"""]);

        Assert.Equal(@"D:\Bench\Items", options.DesktopFolder);
        Assert.False(options.UsesNativeDesktop);
        Assert.False(options.HideNativeIcons);
        Assert.Empty(options.Unknown);
    }

    [Fact]
    public void DesktopFolder_WithoutAPath_IsUnknown() => Assert.Equal(["--desktop-folder="], AppOptions.Parse(["--desktop-folder="]).Unknown);

    [Fact]
    public void LogFile_IsTakenAsGiven()
    {
        Assert.Null(AppOptions.Parse([]).LogFile);
        Assert.Equal(@"D:\Bench\tents.log", AppOptions.Parse([@"--LOG-FILE=D:\Bench\tents.log"]).LogFile);
    }
}
