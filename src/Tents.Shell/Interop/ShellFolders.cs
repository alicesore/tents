using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Tents.Shell.Interop;

/// <summary>Shell folder helpers shared by menus and drag and drop. Call on an STA thread.</summary>
internal static unsafe class ShellFolders
{
    /// <summary>
    /// <c>IShellFolder::GetUIObjectOf</c> for a selection (grouped with <see cref="ShellSelection.Group"/> and
    /// <see cref="SameFolder"/>): an <c>IContextMenu</c>, <c>IDataObject</c> or <c>IDropTarget</c>. Null when the shell
    /// has none. The caller releases the result.
    /// </summary>
    public static void* GetUIObject(IReadOnlyList<ShellIdList> items, HWND owner, Guid iid, out int dropped)
    {
        var (parent, children, droppedCount) = ShellSelection.Group(items, SameFolder);
        dropped = droppedCount;
        IShellFolder* folder = null;
        using var native = new NativeIdLists(children);
        try
        {
            folder = BindToFolder(parent);
            folder->GetUIObjectOf(owner, (uint)children.Count, native.Pointers, iid, out var result);
            return result;
        }
        catch (Exception ex) when (ComErrors.IsCallFailure(ex))
        {
            return null;
        }
        finally
        {
            Com.Release(ref folder);
        }
    }

    /// <summary>The folder an absolute ID list names (the desktop root for the empty list). The caller releases it.</summary>
    public static IShellFolder* BindToFolder(ShellIdList folder)
    {
        IShellFolder* desktop;
        PInvoke.SHGetDesktopFolder(&desktop).ThrowOnFailure();
        if (folder.ItemCount == 0)
        {
            return desktop;
        }

        try
        {
            fixed (byte* pidl = folder.Bytes)
            {
                desktop->BindToObject(in *(ITEMIDLIST*)pidl, null, out IShellFolder* result);
                return result;
            }
        }
        finally
        {
            Com.Release(ref desktop);
        }
    }

    /// <summary>
    /// True when two absolute ID lists name the same folder: <c>IShellFolder::CompareIDs</c> on the desktop root with
    /// <c>SHCIDS_CANONICALONLY</c>, equal when the code part of the result is 0.
    /// </summary>
    public static bool SameFolder(ShellIdList a, ShellIdList b)
    {
        const int canonicalOnly = 0x10000000;
        IShellFolder* desktop = null;
        try
        {
            PInvoke.SHGetDesktopFolder(&desktop).ThrowOnFailure();
            fixed (byte* pa = a.Bytes, pb = b.Bytes)
            {
                var hr = desktop->CompareIDs(new LPARAM(canonicalOnly), in *(ITEMIDLIST*)pa, in *(ITEMIDLIST*)pb);
                return hr.Succeeded && (short)(hr.Value & 0xFFFF) == 0;
            }
        }
        catch (Exception ex) when (ComErrors.IsCallFailure(ex))
        {
            return false;
        }
        finally
        {
            Com.Release(ref desktop);
        }
    }

    /// <summary>Copies ID lists into native memory, so the shell gets stable <c>ITEMIDLIST*</c> pointers.</summary>
    private readonly struct NativeIdLists : IDisposable
    {
        private readonly void* _block;

        public NativeIdLists(IReadOnlyList<ShellIdList> lists)
        {
            var pointerBytes = (nuint)(sizeof(ITEMIDLIST*) * lists.Count);
            var total = pointerBytes + (nuint)lists.Sum(l => l.Length);
            _block = NativeMemory.Alloc(total);
            Pointers = (ITEMIDLIST**)_block;
            var next = (byte*)_block + pointerBytes;
            for (var i = 0; i < lists.Count; i++)
            {
                lists[i].Bytes.CopyTo(new Span<byte>(next, lists[i].Length));
                Pointers[i] = (ITEMIDLIST*)next;
                next += lists[i].Length;
            }
        }

        public ITEMIDLIST** Pointers { get; }

        public void Dispose() => NativeMemory.Free(_block);
    }
}
