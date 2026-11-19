using System.Buffers.Binary;

namespace Tents.Shell.Tray;

/// <summary>One image in an <c>.ico</c> file: its size and where its data (PNG or DIB) is.</summary>
public readonly record struct IcoImage(int Size, int Offset, int Length);

/// <summary>
/// Reads the directory of an <c>.ico</c> file and picks the image for a pixel size. Pure.
/// </summary>
/// <remarks>Format: <see href="https://learn.microsoft.com/previous-versions/ms997538(v=msdn.10)">Icons</see>
/// (ICONDIR, ICONDIRENTRY; a width of 0 means 256).</remarks>
public static class IcoFile
{
    private const int _headerSize = 6;
    private const int _entrySize = 16;

    /// <summary>The images listed in the file. Entries that point outside the file are left out.</summary>
    /// <exception cref="FormatException">Not an icon file.</exception>
    public static IReadOnlyList<IcoImage> ReadDirectory(ReadOnlySpan<byte> file)
    {
        if (file.Length < _headerSize
            || BinaryPrimitives.ReadUInt16LittleEndian(file) != 0
            || BinaryPrimitives.ReadUInt16LittleEndian(file[2..]) != 1)
        {
            throw new FormatException("Not an icon file.");
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(file[4..]);
        var images = new List<IcoImage>(count);
        for (var i = 0; i < count; i++)
        {
            var at = _headerSize + (i * _entrySize);
            if (at + _entrySize > file.Length)
            {
                break;
            }

            var entry = file.Slice(at, _entrySize);
            var size = entry[0] == 0 ? 256 : entry[0];
            var length = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
            if (length > 0 && offset >= 0 && (long)offset + length <= file.Length)
            {
                images.Add(new IcoImage(size, offset, length));
            }
        }

        return images;
    }

    /// <summary>
    /// The image to draw at <paramref name="pixelSize"/>: the smallest one at least that large (scaled down, it stays
    /// sharp), or else the largest.
    /// </summary>
    /// <exception cref="ArgumentException">There are no images.</exception>
    public static IcoImage Pick(IReadOnlyList<IcoImage> images, int pixelSize)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0)
        {
            throw new ArgumentException("The icon has no images.", nameof(images));
        }

        return images.Where(i => i.Size >= pixelSize).OrderBy(i => i.Size).FirstOrDefault() is { Length: > 0 } fit
            ? fit
            : images.OrderByDescending(i => i.Size).First();
    }
}
