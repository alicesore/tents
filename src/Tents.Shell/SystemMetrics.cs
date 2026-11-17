using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tents.Shell;

/// <summary>Smoke test for the CsWin32 setup: one trivial, allocation-free Win32 call.</summary>
public static class SystemMetrics
{
    /// <summary>Returns the number of display monitors on the desktop (SM_CMONITORS).</summary>
    public static int GetMonitorCount() => PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CMONITORS);
}
