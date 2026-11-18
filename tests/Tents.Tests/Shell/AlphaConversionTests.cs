using Tents.Shell.Icons;

namespace Tents.Tests.Shell;

public class AlphaConversionTests
{
    [Fact]
    public void NoAlphaChannel_BecomesOpaque_ColorsUnchanged()
    {
        byte[] pixels = [10, 20, 30, 0, 200, 100, 50, 0];

        Assert.Equal(AlphaKind.NoAlpha, AlphaConversion.ToPremultiplied(pixels));

        Assert.Equal([10, 20, 30, 255, 200, 100, 50, 255], pixels);
    }

    [Fact]
    public void Premultiplied_IsLeftAlone()
    {
        byte[] pixels = [0, 0, 0, 0, 64, 32, 16, 128, 255, 255, 255, 255, 10, 10, 10, 10];
        var before = (byte[])pixels.Clone();

        Assert.Equal(AlphaKind.Premultiplied, AlphaConversion.ToPremultiplied(pixels));

        Assert.Equal(before, pixels);
    }

    [Fact]
    public void Straight_IsPremultipliedWithRounding()
    {
        // 200 * 128 / 255 = 100.4, 100 * 128 / 255 = 50.2, 50 * 128 / 255 = 25.1, 255 * 1 / 255 = 1.
        byte[] pixels = [200, 100, 50, 128, 255, 255, 255, 1, 7, 8, 9, 255];

        Assert.Equal(AlphaKind.Straight, AlphaConversion.ToPremultiplied(pixels));

        Assert.Equal([100, 50, 25, 128, 1, 1, 1, 1, 7, 8, 9, 255], pixels);
    }

    [Fact]
    public void Straight_IsFoundAfterPixelsThatLookPremultiplied()
    {
        byte[] pixels = [1, 1, 1, 255, 0, 0, 0, 0, 2, 2, 2, 2, 90, 0, 0, 60];

        Assert.Equal(AlphaKind.Straight, AlphaConversion.Classify(pixels));
    }

    [Fact]
    public void ColorUnderZeroAlpha_WithAlphaElsewhere_IsStraight_AndBecomesTransparent()
    {
        // A straight-alpha edge: the color under a fully transparent pixel must disappear after conversion.
        byte[] pixels = [255, 255, 255, 0, 40, 40, 40, 255];

        Assert.Equal(AlphaKind.Straight, AlphaConversion.ToPremultiplied(pixels));

        Assert.Equal([0, 0, 0, 0, 40, 40, 40, 255], pixels);
    }

    [Fact]
    public void AllZero_IsTreatedAsNoAlpha()
    {
        // Ambiguous (transparent, or opaque black without alpha). The no-alpha reading is chosen and documented.
        byte[] pixels = new byte[16];

        Assert.Equal(AlphaKind.NoAlpha, AlphaConversion.ToPremultiplied(pixels));

        Assert.All(pixels.Chunk(4), p => Assert.Equal(255, p[3]));
    }

    [Fact]
    public void Premultiply_RoundsToNearest_AndNeverExceedsAlpha()
    {
        for (var c = 0; c <= 255; c++)
        {
            for (var a = 0; a <= 255; a++)
            {
                var result = AlphaConversion.Premultiply((byte)c, (byte)a);
                Assert.Equal((int)Math.Round(c * a / 255.0, MidpointRounding.AwayFromZero), result);
                Assert.True(result <= a);
            }
        }
    }

    [Fact]
    public void ConvertedStraight_ClassifiesAsPremultiplied()
    {
        var random = new Random(1234);
        var pixels = new byte[64 * 64 * 4];
        random.NextBytes(pixels);

        AlphaConversion.ToPremultiplied(pixels);

        Assert.Equal(AlphaKind.Premultiplied, AlphaConversion.Classify(pixels));
    }

    [Fact]
    public void FlipRows_ReversesRowOrder()
    {
        byte[] pixels = [1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3];

        AlphaConversion.FlipRows(pixels, stride: 4, height: 3);

        Assert.Equal([3, 3, 3, 3, 2, 2, 2, 2, 1, 1, 1, 1], pixels);
    }

    [Fact]
    public void PartialPixel_IsRejected() =>
        Assert.Throws<ArgumentException>(() => AlphaConversion.Classify(new byte[7]));

    [Fact]
    public void FlipRows_RejectsWrongSize() =>
        Assert.Throws<ArgumentException>(() => AlphaConversion.FlipRows(new byte[10], stride: 4, height: 3));
}
