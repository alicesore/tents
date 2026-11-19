using Tents.App;
using Tents.Core;
using Tents.Shell.Hosting;

namespace Tents.Tests.App;

public sealed class TentPlacementTests
{
    [Fact]
    public void ForFirstTent_SitsNearTheTopRightOfTheWorkArea()
    {
        var primary = new MonitorInfo("P", new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1392), 96, true);

        var bounds = TentPlacement.ForFirstTent(primary);

        Assert.Equal(new PixelRect(2560 - 380 - 24, 24, 380, 460), bounds);
    }

    [Fact]
    public void ForFirstTent_ScalesWithTheMonitorDpi()
    {
        var primary = new MonitorInfo("P", new PixelRect(0, 0, 3840, 2160), new PixelRect(0, 0, 3840, 2100), 192, true);

        var bounds = TentPlacement.ForFirstTent(primary);

        Assert.Equal(new PixelRect(3840 - 760 - 48, 48, 760, 920), bounds);
    }

    [Fact]
    public void ForFirstTent_StaysInsideAWorkAreaWithAnOffsetOrigin()
    {
        // Taskbar on the left and top: the work area does not start at 0.
        var primary = new MonitorInfo("P", new PixelRect(0, 0, 1920, 1080), new PixelRect(60, 40, 1860, 1040), 96, true);

        var bounds = TentPlacement.ForFirstTent(primary);

        Assert.True(bounds.X >= 60 && bounds.X + bounds.Width <= 1920);
        Assert.True(bounds.Y >= 40 && bounds.Y + bounds.Height <= 1080);
    }

    [Fact]
    public void ForFirstTent_ShrinksToATinyWorkArea()
    {
        var primary = new MonitorInfo("P", new PixelRect(0, 0, 300, 200), new PixelRect(0, 0, 300, 200), 96, true);

        var bounds = TentPlacement.ForFirstTent(primary);

        Assert.Equal(new PixelRect(0, 0, 300, 200), bounds);
    }

    [Fact]
    public void ForFirstTent_WithoutMonitors_UsesTheFallback() =>
        Assert.Equal(TentPlacement.Fallback, TentPlacement.ForFirstTent(null));
}
