using System.Drawing;
using System.Runtime.InteropServices;
using Tents.Shell.Desktop;
using Tents.Shell.Interop;
using Tents.Shell.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Tents.Shell.Menus;

/// <summary>The result of <see cref="ShellActions.OpenAsync"/>.</summary>
/// <param name="Succeeded">The shell started the default command (or the user cancelled a prompt it showed).</param>
/// <param name="Win32Error">The error when it failed, from <c>GetLastError</c>.</param>
public sealed record ShellOpenResult(bool Succeeded, int Win32Error = 0);

/// <summary>
/// Opens desktop items and shows their native shell context menu, on a dedicated STA thread so a slow handler never
/// blocks a tent UI thread (and, through the tent's owner, the desktop).
/// </summary>
/// <remarks>
/// <para><b>Open.</b> <c>ShellExecuteEx</c> with <c>SEE_MASK_INVOKEIDLIST</c> and the item's ID list, so virtual items
/// open too, with the item's default verb (what a double-click on the desktop does). No owner window: an owner on a
/// tent thread would attach that thread's input to this one.</para>
/// <para><b>Context menu.</b> <c>IShellFolder::GetUIObjectOf(IID_IContextMenu)</c> for the selection on its parent
/// folder (the desktop root for desktop items), <c>QueryContextMenu</c>, <c>TrackPopupMenuEx</c> owned by
/// <see cref="ShellMenuHost"/>, then <c>InvokeCommand</c>. "rename" is returned to the host instead of invoked
/// (<see cref="ContextMenuProtocol.IsHostVerb"/>).</para>
/// <para><b>Windows 11 "Show more options".</b> The compact Windows 11 desktop menu has no public API. This shows the
/// full menu through <c>IContextMenu</c>, which is what "Show more options" (or Shift+F10) opens on the desktop, with
/// third-party commands included. Shift+right-click adds extended verbs, as on the desktop.</para>
/// <para><b>Idle cost.</b> None: the thread waits in <c>GetMessage</c> and the hidden owner window gets no messages.</para>
/// </remarks>
public sealed unsafe class ShellActions : IDisposable
{
    private const uint _seeMaskInvokeIdList = 0x0000000C;
    private const uint _seeMaskNoAsync = 0x00000100;
    private const uint _seeMaskFlagLogUsage = 0x04000000;
    private const uint _cmicMaskUnicode = 0x00004000;
    private const uint _cmicMaskShiftDown = 0x10000000;
    private const uint _cmicMaskPtInvoke = 0x20000000;
    private const uint _cmicMaskControlDown = 0x40000000;
    private const uint _gcsVerbW = 0x4;
    private const int _swShowNormal = 1;
    private const int _errorCancelled = 1223;
    private const int _hresultCancelled = unchecked((int)0x800704C7);

    private readonly StaWorker _worker = new("Tents shell UI");

    /// <summary>Runs the default command of <paramref name="item"/>, as a double-click on the desktop does.</summary>
    public Task<ShellOpenResult> OpenAsync(DesktopItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _worker.InvokeAsync(() => Open(item.IdList));
    }

    /// <summary>
    /// Shows the shell context menu of <paramref name="items"/> at a screen position (physical pixels) and runs the
    /// chosen command. Completes when the menu has closed and the command has returned.
    /// </summary>
    public Task<ContextMenuResult> ShowContextMenuAsync(IReadOnlyList<DesktopItem> items, Point screenPoint, ContextMenuOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        var list = items.Select(i => i.IdList).ToList();
        return _worker.InvokeAsync(() => ShowMenu(list, screenPoint, options ?? new ContextMenuOptions()));
    }

    /// <summary>The canonical verbs of the menu that would be shown, without showing it or running anything.</summary>
    internal Task<IReadOnlyList<string>> ReadMenuVerbsAsync(IReadOnlyList<DesktopItem> items, ContextMenuOptions? options = null)
    {
        var list = items.Select(i => i.IdList).ToList();
        return _worker.InvokeAsync<IReadOnlyList<string>>(() => ReadVerbs(list, options ?? new ContextMenuOptions()));
    }

    /// <summary>Runs work on the shell UI thread (tests build items there).</summary>
    internal Task<T> InvokeAsync<T>(Func<T> work) => _worker.InvokeAsync(work);

    public void Dispose() => _worker.Dispose();

    private static ShellOpenResult Open(ShellIdList idList)
    {
        fixed (byte* pidl = idList.Bytes)
        {
            var info = new SHELLEXECUTEINFOW
            {
                cbSize = (uint)sizeof(SHELLEXECUTEINFOW),
                fMask = _seeMaskInvokeIdList | _seeMaskNoAsync | _seeMaskFlagLogUsage,
                lpIDList = pidl,
                nShow = _swShowNormal,
            };
            if (PInvoke.ShellExecuteEx(&info))
            {
                return new ShellOpenResult(true);
            }

            var error = Marshal.GetLastPInvokeError();
            return new ShellOpenResult(error == _errorCancelled, error);
        }
    }

    private static ContextMenuResult ShowMenu(List<ShellIdList> items, Point point, ContextMenuOptions options)
    {
        var owner = ShellMenuHost.GetWindow();
        using var menu = ContextMenuHandle.Create(items, owner, out var dropped);
        if (menu.Menu is null)
        {
            return new ContextMenuResult(ContextMenuOutcome.Failed, ItemsDropped: dropped);
        }

        var popup = PInvoke.CreatePopupMenu();
        try
        {
            if (menu.Menu->QueryContextMenu(popup, 0, ContextMenuProtocol.FirstCommandId, ContextMenuProtocol.LastCommandId, ContextMenuProtocol.QueryFlags(options)).Failed)
            {
                return new ContextMenuResult(ContextMenuOutcome.Failed, ItemsDropped: dropped);
            }

            int command;
            using (ShellMenuHost.Track(menu.Menu))
            {
                // The owner must be the foreground window, or the menu does not close on an outside click.
                PInvoke.SetForegroundWindow(owner);
                command = PInvoke.TrackPopupMenuEx(
                    popup,
                    (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON),
                    point.X,
                    point.Y,
                    owner,
                    null).Value;
                PInvoke.PostMessage(owner, PInvoke.WM_NULL, default, default);
            }

            if (command < ContextMenuProtocol.FirstCommandId)
            {
                return new ContextMenuResult(ContextMenuOutcome.Dismissed, ItemsDropped: dropped);
            }

            var offset = (uint)command - ContextMenuProtocol.FirstCommandId;
            var verb = GetVerb(menu.Menu, offset);
            if (ContextMenuProtocol.IsHostVerb(verb))
            {
                return new ContextMenuResult(ContextMenuOutcome.HostVerb, verb, dropped);
            }

            return new ContextMenuResult(Invoke(menu.Menu, offset, owner, point, options), verb, dropped);
        }
        finally
        {
            PInvoke.DestroyMenu(popup);
        }
    }

    private static List<string> ReadVerbs(List<ShellIdList> items, ContextMenuOptions options)
    {
        using var menu = ContextMenuHandle.Create(items, ShellMenuHost.GetWindow(), out _);
        if (menu.Menu is null)
        {
            return [];
        }

        var popup = PInvoke.CreatePopupMenu();
        try
        {
            menu.Menu->QueryContextMenu(popup, 0, ContextMenuProtocol.FirstCommandId, ContextMenuProtocol.LastCommandId, ContextMenuProtocol.QueryFlags(options)).ThrowOnFailure();
            var verbs = new List<string>();
            var count = PInvoke.GetMenuItemCount(popup);
            for (var i = 0; i < count; i++)
            {
                var id = PInvoke.GetMenuItemID(popup, i);
                if (id >= ContextMenuProtocol.FirstCommandId && id <= ContextMenuProtocol.LastCommandId
                    && GetVerb(menu.Menu, id - ContextMenuProtocol.FirstCommandId) is { } verb)
                {
                    verbs.Add(verb);
                }
            }

            return verbs;
        }
        finally
        {
            PInvoke.DestroyMenu(popup);
        }
    }

    private static string? GetVerb(IContextMenu* menu, uint offset)
    {
        const int capacity = 256;
        var buffer = stackalloc char[capacity];
        buffer[0] = '\0';
        try
        {
            menu->GetCommandString(offset, _gcsVerbW, null, (PSTR)(byte*)buffer, capacity);
        }
        catch (Exception ex) when (ComErrors.IsCallFailure(ex))
        {
            return null; // The handler names no verb for this command.
        }

        var verb = new string(buffer);
        return verb.Length == 0 ? null : verb;
    }

    private static ContextMenuOutcome Invoke(IContextMenu* menu, uint offset, HWND owner, Point point, ContextMenuOptions options)
    {
        var info = new CMINVOKECOMMANDINFOEX
        {
            cbSize = (uint)sizeof(CMINVOKECOMMANDINFOEX),
            fMask = _cmicMaskUnicode | _cmicMaskPtInvoke
                | (options.ExtendedVerbs ? _cmicMaskShiftDown : 0)
                | (options.ControlDown ? _cmicMaskControlDown : 0),
            hwnd = owner,
            lpVerb = new PCSTR((byte*)(nint)offset), // MAKEINTRESOURCE(offset)
            lpVerbW = new PCWSTR((char*)(nint)offset),
            nShow = _swShowNormal,
            ptInvoke = point,
        };
        try
        {
            menu->InvokeCommand((CMINVOKECOMMANDINFO*)&info);
            return ContextMenuOutcome.Invoked;
        }
        catch (Exception ex) when (ex.HResult == _hresultCancelled)
        {
            return ContextMenuOutcome.Invoked; // The user cancelled a prompt the command showed.
        }
        catch (Exception ex) when (ComErrors.IsCallFailure(ex))
        {
            return ContextMenuOutcome.Failed;
        }
    }

    /// <summary>The <c>IContextMenu</c> of a selection, with the native memory it needs. Released on dispose.</summary>
    internal struct ContextMenuHandle : IDisposable
    {
        public IContextMenu* Menu;

        public static ContextMenuHandle Create(List<ShellIdList> items, HWND owner, out int dropped)
        {
            var (parent, children, droppedCount) = ShellSelection.Group(items, ContextMenuHandle.SameFolder);
            dropped = droppedCount;
            IShellFolder* folder = null;
            var native = new NativeIdLists(children);
            try
            {
                folder = BindToFolder(parent);
                void* menu;
                folder->GetUIObjectOf(owner, (uint)children.Count, native.Pointers, IContextMenu.IID_Guid, out menu);
                return new ContextMenuHandle { Menu = (IContextMenu*)menu };
            }
            catch (Exception ex) when (ComErrors.IsCallFailure(ex))
            {
                return default;
            }
            finally
            {
                native.Dispose();
                Com.Release(ref folder);
            }
        }

        public void Dispose() => Com.Release(ref Menu);

        /// <summary>IShellFolder::CompareIDs on the desktop root (SHCIDS_CANONICALONLY): equal when the code part is 0.</summary>
        internal static bool SameFolder(ShellIdList a, ShellIdList b)
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

        private static IShellFolder* BindToFolder(ShellIdList parent)
        {
            IShellFolder* desktop;
            PInvoke.SHGetDesktopFolder(&desktop).ThrowOnFailure();
            if (parent.ItemCount == 0)
            {
                return desktop;
            }

            try
            {
                fixed (byte* pidl = parent.Bytes)
                {
                    desktop->BindToObject(in *(ITEMIDLIST*)pidl, null, out IShellFolder* folder);
                    return folder;
                }
            }
            finally
            {
                Com.Release(ref desktop);
            }
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
