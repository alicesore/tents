using Tents.Core;

namespace Tents.Tests;

public class CoreTests
{
    [Fact]
    public void CoreInfo_ReportsAssemblyName() => Assert.Equal("Tents.Core", CoreInfo.AssemblyName);
}
