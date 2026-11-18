using System.Runtime.InteropServices;
using Tents.Shell.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Tents.Shell.Icons;

/// <summary>Which image the shell is asked for.</summary>
public enum IconKind
{
    /// <summary>A thumbnail where the item has one (pictures, videos), otherwise its icon. What the desktop shows.</summary>
    IconOrThumbnail,

    /// <summary>Always the icon.</summary>
    IconOnly,
}

/// <summary>
/// Gets an item's image at an exact pixel size through <c>IShellItemImageFactory::GetImage</c>. Call on an STA thread
/// (<see cref="Threading.StaWorker"/>): icon and thumbnail handlers are shell extensions that expect one.
/// </summary>
/// <remarks>
/// The returned <c>HBITMAP</c> is read with <c>GetDIBits</c> as a 32-bit top-down DIB, so the row order of the
/// shell's bitmap does not matter, then converted with <see cref="AlphaConversion.ToPremultiplied"/>. No
/// <c>SIIGBF_BIGGERSIZEOK</c>: the image is never larger than requested, so it is drawn 1:1 without scaling.
/// Source: <see href="https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellitemimagefactory-getimage">IShellItemImageFactory::GetImage</see>,
/// <see href="https://learn.microsoft.com/windows/win32/api/wingdi/nf-wingdi-getdibits">GetDIBits</see>.
/// </remarks>
internal static unsafe class ShellIconExtractor
{
    /// <summary>The image of an item given by its absolute ID list, or null when the shell has none.</summary>
    public static IconImage? Extract(ShellIdList idList, int pixelSize, IconKind kind)
    {
        ArgumentNullException.ThrowIfNull(idList);
        IShellItemImageFactory* factory = null;
        try
        {
            fixed (byte* pidl = idList.Bytes)
            {
                if (PInvoke.SHCreateItemFromIDList(in *(ITEMIDLIST*)pidl, out factory).Failed)
                {
                    return null;
                }
            }

            return Extract(factory, pixelSize, kind);
        }
        finally
        {
            Com.Release(ref factory);
        }
    }

    /// <summary>The image of an item given by its parsing name (for example a file path), or null.</summary>
    public static IconImage? Extract(string parsingName, int pixelSize, IconKind kind)
    {
        ArgumentException.ThrowIfNullOrEmpty(parsingName);
        IShellItemImageFactory* factory = null;
        try
        {
            if (PInvoke.SHCreateItemFromParsingName(parsingName, null, out factory).Failed)
            {
                return null;
            }

            return Extract(factory, pixelSize, kind);
        }
        finally
        {
            Com.Release(ref factory);
        }
    }

    private static IconImage? Extract(IShellItemImageFactory* factory, int pixelSize, IconKind kind)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pixelSize, 2048);
        var flags = kind == IconKind.IconOnly ? SIIGBF.SIIGBF_ICONONLY : SIIGBF.SIIGBF_RESIZETOFIT;
        HBITMAP bitmap = default;
        try
        {
            factory->GetImage(new SIZE(pixelSize, pixelSize), flags, &bitmap);
        }
        catch (COMException)
        {
            return null; // The item has no image of this kind (or its handler failed).
        }

        try
        {
            return Read(bitmap);
        }
        finally
        {
            PInvoke.DeleteObject((HGDIOBJ)bitmap.Value);
        }
    }

    private static IconImage? Read(HBITMAP bitmap)
    {
        BITMAP info;
        if (PInvoke.GetObject((HGDIOBJ)bitmap.Value, sizeof(BITMAP), &info) != sizeof(BITMAP)
            || info.bmWidth <= 0 || info.bmHeight <= 0)
        {
            return null;
        }

        var width = info.bmWidth;
        var height = info.bmHeight;
        var pixels = new byte[width * height * 4];
        var header = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = width,
                biHeight = -height, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0, // BI_RGB
            },
        };

        var dc = PInvoke.CreateCompatibleDC(default);
        try
        {
            fixed (byte* bits = pixels)
            {
                if (PInvoke.GetDIBits(dc, bitmap, 0, (uint)height, bits, &header, DIB_USAGE.DIB_RGB_COLORS) != height)
                {
                    return null;
                }
            }
        }
        finally
        {
            PInvoke.DeleteDC(dc);
        }

        var alpha = AlphaConversion.ToPremultiplied(pixels);
        return new IconImage(width, height, pixels, alpha);
    }
}
