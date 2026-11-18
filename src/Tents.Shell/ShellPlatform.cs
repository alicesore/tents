using System.Diagnostics;

namespace Tents.Shell;

/// <summary>Process-wide checks for the shell layer.</summary>
public static class ShellPlatform
{
    /// <summary>
    /// Throws when the process is 32-bit. Some generated interop types use the 64-bit layout (see
    /// <c>CsWin32PlatformTarget</c> in <c>Tents.Shell.csproj</c>).
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">The process is not 64-bit.</exception>
    public static void EnsureSupported()
    {
        if (!Environment.Is64BitProcess)
        {
            throw new PlatformNotSupportedException("Tents.Shell requires a 64-bit process.");
        }
    }

    /// <summary>
    /// Last-resort report for an exception caught in a native callback, where it must not unwind into Windows. Always
    /// returns true so it can be used as an exception filter.
    /// </summary>
    internal static bool ReportCallbackException(Exception exception)
    {
        Trace.TraceError("Tents.Shell callback failed: {0}", exception);
        return true;
    }
}
