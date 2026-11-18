using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.System.Ole;

namespace Tents.Shell.Interop;

/// <summary>
/// Exposes managed objects as COM objects without built-in COM marshaling (which is off: <c>allowMarshaling: false</c>).
/// Today only <c>IDropTarget</c>. Vtables come from CsWin32 (<c>IDropTarget.PopulateVTable</c>); IUnknown comes from
/// <see cref="ComWrappers.GetIUnknownImpl"/>.
/// </summary>
/// <remarks>
/// Source: <see href="https://learn.microsoft.com/dotnet/standard/native-interop/tutorial-comwrappers">Tutorial: Use the
/// ComWrappers API</see>.
/// </remarks>
internal sealed unsafe class ShellComWrappers : ComWrappers
{
    private static readonly ComInterfaceEntry* _dropTargetEntry = CreateEntry<IDropTarget>(IDropTarget.IID_Guid);

    public static ShellComWrappers Instance { get; } = new();

    /// <summary>A new COM reference to <paramref name="target"/> as <c>IDropTarget*</c>. The caller releases it.</summary>
    public IDropTarget* GetDropTarget(IDropTarget.Interface target)
    {
        var unknown = GetOrCreateComInterfaceForObject(target, CreateComInterfaceFlags.None);
        try
        {
            var iid = IDropTarget.IID_Guid;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, in iid, out var result));
            return (IDropTarget*)result;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    protected override ComInterfaceEntry* ComputeVtables(object obj, CreateComInterfaceFlags flags, out int count)
    {
        if (obj is IDropTarget.Interface)
        {
            count = 1;
            return _dropTargetEntry;
        }

        count = 0;
        return null;
    }

    protected override object CreateObject(nint externalComObject, CreateObjectFlags flags) =>
        throw new NotSupportedException("Tents does not wrap COM objects as managed objects.");

    protected override void ReleaseObjects(IEnumerable objects) =>
        throw new NotSupportedException("Tents does not use reference tracking.");

    private static ComInterfaceEntry* CreateEntry<T>(Guid iid)
        where T : unmanaged, IVTable
    {
        var entry = (ComInterfaceEntry*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(ShellComWrappers), sizeof(ComInterfaceEntry));
        entry->IID = iid;
        entry->Vtable = (nint)T.VTable;
        return entry;
    }
}
