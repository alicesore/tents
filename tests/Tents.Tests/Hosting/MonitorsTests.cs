using Tents.Core;
using Tents.Shell.Hosting;
using Tents.Tests.Shell;

namespace Tents.Tests.Hosting;

public sealed class MonitorsTests
{
    [Fact]
    public void Order_PutsThePrimaryFirst_ThenLeftToRight()
    {
        var right = Monitor("B", 2560, 0, primary: false);
        var left = Monitor("C", -1920, 0, primary: false);
        var primary = Monitor("A", 0, 0, primary: true);

        var ordered = Monitors.Order([right, left, primary]);

        Assert.Equal(["A", "C", "B"], ordered.Select(m => m.DeviceName));
    }

    [Fact]
    public void Scale_IsDpiOver96() => Assert.Equal(1.5, Monitor("A", 0, 0, primary: true, dpi: 144).Scale);

    [Fact]
    [Trait(TestCategories.Key, TestCategories.Desktop)]
    public void GetAll_ReportsOnePrimaryMonitorAtTheOrigin()
    {
        var monitors = Monitors.GetAll();

        var primary = Assert.Single(monitors, m => m.IsPrimary);
        Assert.Same(primary, monitors[0]);
        Assert.Equal(0, primary.Bounds.X);
        Assert.Equal(0, primary.Bounds.Y);
        Assert.All(monitors, m => Assert.True(m.Bounds.HasPositiveSize && m.WorkArea.HasPositiveSize && m.Dpi >= 96));
    }

    internal static MonitorInfo Monitor(string name, int x, int y, bool primary, int dpi = 96) =>
        new(name, new PixelRect(x, y, 1920, 1080), new PixelRect(x, y, 1920, 1040), dpi, primary);
}
