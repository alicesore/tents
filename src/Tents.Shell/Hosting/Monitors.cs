using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Tents.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace Tents.Shell.Hosting;

/// <summary>One display monitor, in physical pixels.</summary>
/// <param name="DeviceName">The GDI device name (for example <c>\\.\DISPLAY1</c>). Stable while the configuration is.</param>
/// <param name="Bounds">The monitor's rectangle in virtual-screen coordinates.</param>
/// <param name="WorkArea">The part not covered by the taskbar and docked bars.</param>
/// <param name="Dpi">Effective DPI (96 at 100%).</param>
/// <param name="IsPrimary">The primary monitor; its top-left corner is (0, 0).</param>
public sealed record MonitorInfo(string DeviceName, PixelRect Bounds, PixelRect WorkArea, int Dpi, bool IsPrimary)
{
    /// <summary>The scale factor (1.0 at 96 DPI).</summary>
    public double Scale => Dpi / 96.0;
}

/// <summary>
/// The display monitors, read when asked (at start and on <c>WM_DISPLAYCHANGE</c>), never polled. Requires per-monitor
/// v2 DPI awareness (M1.13) for physical-pixel values.
/// </summary>
/// <remarks>
/// Sources: <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enumdisplaymonitors">EnumDisplayMonitors</see>,
/// <see href="https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getmonitorinfow">GetMonitorInfo</see>,
/// <see href="https://learn.microsoft.com/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor">GetDpiForMonitor</see>.
/// </remarks>
public static unsafe class Monitors
{
    private const uint _monitorInfoPrimary = 1; // MONITORINFOF_PRIMARY

    /// <summary>Every monitor, primary first, then left to right and top to bottom.</summary>
    public static IReadOnlyList<MonitorInfo> GetAll()
    {
        var list = new List<MonitorInfo>();
        var handle = GCHandle.Alloc(list);
        try
        {
            PInvoke.EnumDisplayMonitors(default, (RECT*)null, &OnMonitor, (LPARAM)GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return Order(list);
    }

    /// <summary>The primary monitor, or null when none is reported (no interactive session).</summary>
    public static MonitorInfo? GetPrimary() => GetAll().FirstOrDefault(m => m.IsPrimary);

    /// <summary>Primary first, then by position. Pure.</summary>
    internal static IReadOnlyList<MonitorInfo> Order(IEnumerable<MonitorInfo> monitors) =>
        [.. monitors.OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y)];

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static BOOL OnMonitor(HMONITOR monitor, HDC hdc, RECT* rect, LPARAM data)
    {
        try
        {
            var list = (List<MonitorInfo>)GCHandle.FromIntPtr(data).Target!;
            var info = new MONITORINFOEXW();
            info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
            if (PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info))
            {
                var dpi = OperatingSystem.IsWindowsVersionAtLeast(8, 1) && PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _).Succeeded ? (int)dpiX : 96;
                list.Add(new MonitorInfo(
                    info.szDevice.ToString(),
                    ToPixelRect(info.monitorInfo.rcMonitor),
                    ToPixelRect(info.monitorInfo.rcWork),
                    dpi,
                    (info.monitorInfo.dwFlags & _monitorInfoPrimary) != 0));
            }
        }
        catch (Exception ex) when (ShellPlatform.ReportCallbackException(ex))
        {
        }

        return true; // continue
    }

    private static PixelRect ToPixelRect(RECT r) => new(r.left, r.top, r.right - r.left, r.bottom - r.top);
}
