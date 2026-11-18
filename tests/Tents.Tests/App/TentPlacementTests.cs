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

    private static readonly MonitorInfo _left = new("L", new PixelRect(-1920, 0, 1920, 1080), new PixelRect(-1920, 0, 1920, 1040), 96, false);
    private static readonly MonitorInfo _main = new("P", new PixelRect(0, 0, 2560, 1440), new PixelRect(0, 0, 2560, 1392), 144, true);

    [Fact]
    public void ForNewTent_CascadesFromTheSource_AtTheMonitorsScale()
    {
        var bounds = TentPlacement.ForNewTent(new PixelRect(100, 100, 400, 500), [_left, _main]);

        Assert.Equal(new PixelRect(148, 148, 570, 690), bounds);
    }

    [Fact]
    public void ForNewTent_StaysOnTheSourcesMonitor()
    {
        var bounds = TentPlacement.ForNewTent(new PixelRect(-1800, 50, 380, 460), [_left, _main]);

        Assert.Equal(new PixelRect(-1768, 82, 380, 460), bounds);
    }

    [Fact]
    public void ForNewTent_WouldLeaveTheWorkArea_GoesToItsTopLeft()
    {
        var bounds = TentPlacement.ForNewTent(new PixelRect(2100, 900, 400, 400), [_left, _main]);

        Assert.Equal(new PixelRect(36, 36, 570, 690), bounds);
    }

    [Fact]
    public void ForNewTent_WithoutMonitors_Cascades() =>
        Assert.Equal(new PixelRect(132, 132, 380, 460), TentPlacement.ForNewTent(new PixelRect(100, 100, 10, 10), []));

    [Fact]
    public void MonitorOf_UsesTheCentre_ThenThePrimary()
    {
        Assert.Same(_left, TentPlacement.MonitorOf(new PixelRect(-300, 0, 400, 100), [_left, _main])); // centre at x = -100
        Assert.Same(_main, TentPlacement.MonitorOf(new PixelRect(-100, 0, 400, 100), [_left, _main])); // centre at x = 100
        Assert.Same(_main, TentPlacement.MonitorOf(new PixelRect(9000, 9000, 10, 10), [_left, _main])); // off every monitor
        Assert.Null(TentPlacement.MonitorOf(new PixelRect(0, 0, 10, 10), []));
    }
}
