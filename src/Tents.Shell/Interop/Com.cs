using Windows.Win32.System.Com;

namespace Tents.Shell.Interop;

/// <summary>Reference counting helpers for raw COM pointers (CsWin32 with marshaling off).</summary>
internal static unsafe class Com
{
    /// <summary>Releases <paramref name="pointer"/> if it is set, and clears it.</summary>
    public static void Release<T>(ref T* pointer)
        where T : unmanaged
    {
        var p = pointer;
        pointer = null;
        if (p is not null)
        {
            ((IUnknown*)p)->Release();
        }
    }
}
