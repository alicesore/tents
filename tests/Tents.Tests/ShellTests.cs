using Tents.Shell;

namespace Tents.Tests;

public class ShellTests
{
    [Fact]
    public void GetMonitorCount_CallsIntoWin32() => Assert.True(SystemMetrics.GetMonitorCount() >= 0);
}
