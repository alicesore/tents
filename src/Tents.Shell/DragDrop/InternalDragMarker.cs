using Tents.Shell.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Memory;
using Windows.Win32.System.Ole;

namespace Tents.Shell.DragDrop;

/// <summary>
/// A private clipboard format added to the data object of a drag that starts in a tent, holding this process's id.
/// A tent drop target that finds it knows the drag is a move between tents (<see cref="DropRoute.Reassign"/>), not
/// a file transfer. Another program never sees a meaning in it; another process's marker does not count.
/// </summary>
internal static unsafe class InternalDragMarker
{
    private const string _formatName = "Tents.InternalDrag";

    private static readonly ushort _format = (ushort)PInvoke.RegisterClipboardFormat(_formatName);

    /// <summary>Adds the marker. Returns false when the data object does not take it (the drag still works).</summary>
    public static bool Set(IDataObject* data)
    {
        var memory = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, sizeof(int));
        if (memory.IsNull)
        {
            return false;
        }

        var p = PInvoke.GlobalLock(memory);
        *(int*)p = Environment.ProcessId;
        PInvoke.GlobalUnlock(memory);

        var format = Format();
        var medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL };
        medium.u.hGlobal = memory;
        try
        {
            data->SetData(&format, &medium, true); // the data object now owns the memory
            return true;
        }
        catch (Exception ex) when (ComErrors.IsCallFailure(ex))
        {
            PInvoke.GlobalFree(memory);
            return false;
        }
    }

    /// <summary>True when <paramref name="data"/> carries this process's marker.</summary>
    public static bool IsPresent(IDataObject* data)
    {
        if (data is null)
        {
            return false;
        }

        var format = Format();
        if (data->QueryGetData(&format) != HRESULT.S_OK)
        {
            return false;
        }

        STGMEDIUM medium = default;
        try
        {
            data->GetData(&format, &medium);
        }
        catch (Exception ex) when (ComErrors.IsCallFailure(ex))
        {
            return false;
        }

        try
        {
            if (medium.tymed != TYMED.TYMED_HGLOBAL || PInvoke.GlobalSize(medium.u.hGlobal) < sizeof(int))
            {
                return false;
            }

            var p = PInvoke.GlobalLock(medium.u.hGlobal);
            try
            {
                return p is not null && *(int*)p == Environment.ProcessId;
            }
            finally
            {
                PInvoke.GlobalUnlock(medium.u.hGlobal);
            }
        }
        finally
        {
            PInvoke.ReleaseStgMedium(&medium);
        }
    }

    private static FORMATETC Format() => new()
    {
        cfFormat = _format,
        dwAspect = (uint)DVASPECT.DVASPECT_CONTENT,
        lindex = -1,
        tymed = (uint)TYMED.TYMED_HGLOBAL,
    };
}
