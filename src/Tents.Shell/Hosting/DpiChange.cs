using Tents.Core;
using Windows.Win32.Foundation;

namespace Tents.Shell.Hosting;

/// <summary>
/// The <c>WM_DPICHANGED</c> rule for tent windows (per-monitor v2). Windows suggests a new rectangle that keeps the
/// window's size in device-independent units; WPF applies whatever rectangle the message carries.
/// </summary>
/// <remarks>
/// <para><b>While Tents places a tent</b> (at creation, at restore, after a layout change) the stored bounds are the
/// truth: they are physical pixels on the monitor the tent belongs to. Moving a new window there from the monitor it was
/// created on changes its DPI, and the suggested rectangle would rescale it. The rule replaces the suggestion with the
/// bounds being placed.</para>
/// <para><b>Otherwise</b> (the user dragged the tent to a monitor with another scale, or changed a monitor's scale) the
/// suggestion is kept: the tent keeps its apparent size, and the new physical bounds are saved like any other move.</para>
/// <para>Source: <see href="https://learn.microsoft.com/windows/win32/hidpi/wm-dpichanged">WM_DPICHANGED</see>.</para>
/// </remarks>
public static unsafe class DpiChange
{
    public const int WmDpiChanged = 0x02E0;

    /// <summary>The new DPI from the message's <c>wParam</c> (the low word is the X-axis DPI).</summary>
    public static int DpiFromWParam(nint wParam) => (int)(wParam & 0xFFFF);

    /// <summary>
    /// Rewrites the suggested rectangle in <paramref name="lParam"/> (a <c>RECT*</c>) when a placement is in progress.
    /// </summary>
    /// <returns>The rectangle the window will get.</returns>
    public static PixelRect Apply(nint lParam, PixelRect? placing)
    {
        if (lParam == 0)
        {
            return placing ?? default;
        }

        ref var rect = ref *(RECT*)lParam;
        if (placing is { } target)
        {
            rect = new RECT { left = target.X, top = target.Y, right = target.X + target.Width, bottom = target.Y + target.Height };
        }

        return new PixelRect(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
    }
}
