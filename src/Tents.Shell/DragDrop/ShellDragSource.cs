using Tents.Shell.Desktop;
using Tents.Shell.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;

namespace Tents.Shell.DragDrop;

/// <summary>Starts an OLE drag of desktop items, with the shell's own data object and drag image.</summary>
/// <remarks>
/// <para>The data object comes from <c>IShellFolder::GetUIObjectOf(IID_IDataObject)</c> on the items' parent folder
/// (the desktop root), so every drop target sees what a drag from the desktop gives it: shell ID lists, file paths,
/// file contents for virtual items where the shell provides them. <c>SHDoDragDrop</c> supplies the drop source and the
/// drag image. The <see cref="InternalDragMarker"/> is added so a tent target recognizes the drag.</para>
/// <para>Tents never deletes the source files after a Move. A shell target that moves files does so itself (an
/// "optimized move"); anything else leaves the files where they are.</para>
/// <para>Sources: <see href="https://learn.microsoft.com/windows/win32/api/shlobj_core/nf-shlobj_core-shdodragdrop">SHDoDragDrop</see>,
/// <see href="https://learn.microsoft.com/windows/win32/shell/datascenarios">Handling Shell Data Transfer Scenarios</see>.</para>
/// </remarks>
public static unsafe class ShellDragSource
{
    private const int _dragDropDrop = 0x00040100; // DRAGDROP_S_DROP

    /// <summary>
    /// Drags <paramref name="items"/> until the user drops or cancels, and returns the effect the target reported
    /// (<see cref="DragEffects.None"/> when cancelled). Runs a modal loop: call it on the UI thread that received the
    /// mouse-down, which must be an OLE STA (a WPF UI thread is).
    /// </summary>
    /// <param name="owner">A window of the calling thread, used as the owner of any UI the shell shows.</param>
    public static DragEffects DoDragDrop(nint owner, IReadOnlyList<DesktopItem> items, DragEffects allowed = DropPolicy.SourceEffects)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return DragEffects.None;
        }

        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("A drag must start on an OLE STA thread (the UI thread).");
        }

        var data = (IDataObject*)ShellFolders.GetUIObject([.. items.Select(i => i.IdList)], (HWND)owner, IDataObject.IID_Guid, out _);
        if (data is null)
        {
            return DragEffects.None;
        }

        try
        {
            InternalDragMarker.Set(data);
            DROPEFFECT effect = default;
            var hr = PInvoke.SHDoDragDrop((HWND)owner, data, null, (DROPEFFECT)allowed, &effect);
            return hr.Value == _dragDropDrop || hr == HRESULT.S_OK ? (DragEffects)effect : DragEffects.None;
        }
        finally
        {
            Com.Release(ref data);
        }
    }
}
