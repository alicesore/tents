using System.Buffers.Binary;

namespace Tents.Tests.Shell;

/// <summary>
/// Writes <c>.ico</c> files with generated, seed-dependent images, so icon tests have any number of distinct real
/// files for the shell to extract. Each file holds 32-bit images at the given sizes: a disc with a soft (partly
/// transparent) edge and an inner square, colors and proportions from the seed.
/// </summary>
/// <remarks>
/// File layout: <c>ICONDIR</c>, one <c>ICONDIRENTRY</c> per size, then per size a <c>BITMAPINFOHEADER</c> (height
/// doubled), the BGRA pixels bottom-up with straight alpha, and the 1-bit AND mask. Source:
/// <see href="https://learn.microsoft.com/previous-versions/ms997538(v=msdn.10)">Icons (MSDN, 1995)</see>.
/// </remarks>
internal static class SyntheticIcons
{
    public static void WriteIcoFile(string path, int seed, params int[] sizes)
    {
        var images = sizes.Select(size => (Size: size, Data: EncodeImage(size, seed))).ToArray();
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // icon
        writer.Write((ushort)images.Length);
        var offset = 6 + (16 * images.Length);
        foreach (var (size, data) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0); // no palette
            writer.Write((byte)0); // reserved
            writer.Write((ushort)1); // planes
            writer.Write((ushort)32); // bits per pixel
            writer.Write(data.Length);
            writer.Write(offset);
            offset += data.Length;
        }

        foreach (var (_, data) in images)
        {
            writer.Write(data);
        }
    }

    /// <summary>Top-down straight-alpha BGRA pixels of the image for <paramref name="seed"/>.</summary>
    public static byte[] Render(int size, int seed)
    {
        var random = new Random(seed);
        var (b, g, r) = ((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
        var (b2, g2, r2) = ((byte)(255 - b), (byte)(255 - g), (byte)(255 - r));
        var radius = size * (0.30 + (random.NextDouble() * 0.18));
        var square = size * (0.10 + (random.NextDouble() * 0.15));
        var shift = size * ((random.NextDouble() * 0.2) - 0.1);
        var center = (size - 1) / 2.0;

        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var edge = radius - Math.Sqrt((dx * dx) + (dy * dy)); // > 0 inside
                var alpha = (byte)Math.Clamp(Math.Round(edge * 255), 0, 255); // one-pixel soft edge
                var inner = Math.Abs(dx - shift) < square && Math.Abs(dy + shift) < square;
                var i = ((y * size) + x) * 4;
                pixels[i] = inner ? b2 : b;
                pixels[i + 1] = inner ? g2 : g;
                pixels[i + 2] = inner ? r2 : r;
                pixels[i + 3] = alpha;
            }
        }

        return pixels;
    }

    private static byte[] EncodeImage(int size, int seed)
    {
        var topDown = Render(size, seed);
        var maskStride = ((size + 31) / 32) * 4;
        var data = new byte[40 + topDown.Length + (maskStride * size)];
        var header = data.AsSpan(0, 40);
        BinaryPrimitives.WriteInt32LittleEndian(header, 40);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], size);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], size * 2); // XOR image plus AND mask
        BinaryPrimitives.WriteInt16LittleEndian(header[12..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[14..], 32);

        var stride = size * 4;
        var maskStart = 40 + topDown.Length;
        for (var row = 0; row < size; row++)
        {
            var sourceRow = size - 1 - row; // bottom-up
            topDown.AsSpan(sourceRow * stride, stride).CopyTo(data.AsSpan(40 + (row * stride)));
            for (var x = 0; x < size; x++)
            {
                if (topDown[(sourceRow * stride) + (x * 4) + 3] == 0)
                {
                    data[maskStart + (row * maskStride) + (x / 8)] |= (byte)(0x80 >> (x % 8));
                }
            }
        }

        return data;
    }
}
