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
}
