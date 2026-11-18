namespace Tents.Shell.Icons;

/// <summary>What <see cref="AlphaConversion.ToPremultiplied"/> found in a bitmap.</summary>
public enum AlphaKind
{
    /// <summary>Every alpha byte was 0: the bitmap has no alpha channel. It was made opaque.</summary>
    NoAlpha,

    /// <summary>No color channel exceeded its alpha: already premultiplied. Left unchanged.</summary>
    Premultiplied,

    /// <summary>Some color channel exceeded its alpha: straight (unassociated) alpha. It was premultiplied.</summary>
    Straight,
}

/// <summary>
/// Turns a 32-bit BGRA bitmap from the shell into premultiplied BGRA (WPF <c>Pbgra32</c>). Pure, so it is unit-tested
/// on synthetic bitmaps.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> A shell bitmap can arrive in three states: premultiplied alpha (what GDI's <c>AlphaBlend</c>
/// expects with <c>AC_SRC_ALPHA</c>), straight alpha, or no alpha at all (alpha bytes 0, colors opaque). Treating
/// every bitmap as <c>Bgr32</c> loses transparency; treating every bitmap as <c>Bgra32</c> gives dark fringes on
/// premultiplied data; treating every bitmap as <c>Pbgra32</c> makes an alpha-less bitmap invisible.</para>
/// <para><b>Detection.</b> In valid premultiplied data no color channel can exceed its alpha. So: if every alpha is 0
/// the bitmap has no alpha channel; else if any channel exceeds its alpha it is straight; else it is treated as
/// premultiplied. Straight data in which no channel happens to exceed alpha (for example a fully opaque bitmap) is
/// identical in both interpretations, so the rule never mis-renders a pixel by more than rounding.</para>
/// <para>Sources: <see href="https://learn.microsoft.com/windows/win32/api/wingdi/ns-wingdi-blendfunction">BLENDFUNCTION</see>
/// (premultiplied source for <c>AC_SRC_ALPHA</c>),
/// <see href="https://learn.microsoft.com/dotnet/api/system.windows.media.pixelformats.pbgra32">PixelFormats.Pbgra32</see>.</para>
/// </remarks>
public static class AlphaConversion
{
    private const int _bytesPerPixel = 4;

    /// <summary>Classifies <paramref name="bgra"/> without changing it.</summary>
    /// <exception cref="ArgumentException">The length is not a whole number of pixels.</exception>
    public static AlphaKind Classify(ReadOnlySpan<byte> bgra)
    {
        CheckLength(bgra.Length);
        var anyAlpha = false;
        for (var i = 0; i < bgra.Length; i += _bytesPerPixel)
        {
            var a = bgra[i + 3];
            if (a != 0)
            {
                anyAlpha = true;
            }

            if (bgra[i] > a || bgra[i + 1] > a || bgra[i + 2] > a)
            {
                if (anyAlpha)
                {
                    return AlphaKind.Straight;
                }

                // A colored pixel with alpha 0 is straight data, unless the whole bitmap has no alpha. Keep scanning.
                return HasAnyAlpha(bgra[(i + _bytesPerPixel)..]) ? AlphaKind.Straight : AlphaKind.NoAlpha;
            }
        }

        return anyAlpha ? AlphaKind.Premultiplied : AlphaKind.NoAlpha;
    }

    /// <summary>Converts <paramref name="bgra"/> in place to premultiplied BGRA and reports what it found.</summary>
    /// <exception cref="ArgumentException">The length is not a whole number of pixels.</exception>
    public static AlphaKind ToPremultiplied(Span<byte> bgra)
    {
        var kind = Classify(bgra);
        switch (kind)
        {
            case AlphaKind.NoAlpha:
                for (var i = 3; i < bgra.Length; i += _bytesPerPixel)
                {
                    bgra[i] = 255;
                }

                break;
            case AlphaKind.Straight:
                for (var i = 0; i < bgra.Length; i += _bytesPerPixel)
                {
                    var a = bgra[i + 3];
                    bgra[i] = Premultiply(bgra[i], a);
                    bgra[i + 1] = Premultiply(bgra[i + 1], a);
                    bgra[i + 2] = Premultiply(bgra[i + 2], a);
                }

                break;
            case AlphaKind.Premultiplied:
            default:
                break;
        }

        return kind;
    }

    /// <summary>Reverses the row order of a bitmap in place (bottom-up to top-down and back).</summary>
    public static void FlipRows(Span<byte> pixels, int stride, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stride);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        if ((long)stride * height != pixels.Length)
        {
            throw new ArgumentException("The buffer is not stride * height bytes.", nameof(pixels));
        }

        Span<byte> row = stride <= 1024 ? stackalloc byte[stride] : new byte[stride];
        for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
        {
            var a = pixels.Slice(top * stride, stride);
            var b = pixels.Slice(bottom * stride, stride);
            a.CopyTo(row);
            b.CopyTo(a);
            row.CopyTo(b);
        }
    }

    /// <summary><c>round(c * a / 255)</c> in integers.</summary>
    internal static byte Premultiply(byte c, byte a) => (byte)(((c * a) + 127) / 255);

    private static bool HasAnyAlpha(ReadOnlySpan<byte> bgra)
    {
        for (var i = 3; i < bgra.Length; i += _bytesPerPixel)
        {
            if (bgra[i] != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void CheckLength(int length)
    {
        if (length % _bytesPerPixel != 0)
        {
            throw new ArgumentException("A BGRA buffer holds whole 4-byte pixels.", nameof(length));
        }
    }
}
