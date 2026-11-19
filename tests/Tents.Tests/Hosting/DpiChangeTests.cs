using Tents.Core;
using Tents.Shell.Hosting;
using Windows.Win32.Foundation;

namespace Tents.Tests.Hosting;

public sealed unsafe class DpiChangeTests
{
    [Fact]
    public void WhilePlacing_TheStoredPhysicalBoundsReplaceTheSuggestion()
    {
        // A new window created on a 100% monitor and placed on a 200% monitor: Windows suggests doubling it.
        var suggested = new RECT { left = 3000, top = 100, right = 3000 + 760, bottom = 100 + 920 };
        var target = new PixelRect(3000, 100, 380, 460);

        var result = DpiChange.Apply((nint)(&suggested), target);

        Assert.Equal(target, result);
        Assert.Equal(new RECT { left = 3000, top = 100, right = 3380, bottom = 560 }, suggested);
    }

    [Fact]
    public void OtherwiseTheSuggestionIsKept()
    {
        // The user dragged the tent to a monitor with another scale: keep its apparent size.
        var suggested = new RECT { left = 10, top = 20, right = 10 + 570, bottom = 20 + 690 };

        var result = DpiChange.Apply((nint)(&suggested), placing: null);

        Assert.Equal(new PixelRect(10, 20, 570, 690), result);
        Assert.Equal(570, suggested.right - suggested.left);
    }

    [Theory]
    [InlineData(0x0060_0060, 96)]
    [InlineData(0x00C0_00C0, 192)]
    [InlineData(0x0090_0090, 144)]
    public void DpiFromWParam_ReadsTheLowWord(int wParam, int dpi) => Assert.Equal(dpi, DpiChange.DpiFromWParam(wParam));
}
