using System.Drawing;
using Tents.Core;
using Tents.Shell.Desktop;
using Tents.Shell.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;

namespace Tents.Shell.DragDrop;

/// <summary>A completed drop onto a tent.</summary>
/// <param name="Route">What the drop did (see <see cref="DropRoute"/>).</param>
/// <param name="Items">Ids of the dragged items, as the data object names them.</param>
/// <param name="Effect">
/// The effect the drop ended with. For forwarded drops, what the shell's target reported, which is None after an
/// optimized or asynchronous move even though the files moved; the file watcher is the reference for what changed.
/// </param>
/// <param name="ScreenPoint">Where it was dropped, in physical screen pixels.</param>
/// <param name="Target">The item it was dropped onto, for <see cref="DropRoute.ForwardToItem"/>.</param>
public sealed record TentDrop(DropRoute Route, IReadOnlyList<ItemId> Items, DragEffects Effect, Point ScreenPoint, DesktopItem? Target);

/// <summary>The tent side of a drop target. Called on the tent window's UI thread, during the drag.</summary>
public interface ITentDropHandler
{
    /// <summary>The item under <paramref name="screenPoint"/> (physical pixels), or null over empty tent area.</summary>
    DesktopItem? HitTest(Point screenPoint);

    /// <summary>
    /// A drop finished. For <see cref="DropRoute.Reassign"/> move the items to this tent. For
    /// <see cref="DropRoute.ForwardToDesktop"/> the shell has put the items on the desktop (copied or moved): the
    /// watcher reports the new desktop items next, and the app puts them in this tent.
    /// </summary>
    void Dropped(TentDrop drop);
}

/// <summary>
/// The OLE drop target of one tent window. Desktop items dragged between tents only change tent membership; every
/// other drop is handed to the shell's own drop target (the desktop folder, or the item under the pointer), which
/// does the file operation exactly as a drop on the desktop would. See <see cref="DropPolicy"/>.
/// </summary>
/// <remarks>
/// <para><b>Threading.</b> <see cref="Register"/> and <see cref="Dispose"/> run on the window's thread, which must be an
/// OLE STA (a WPF UI thread is). OLE calls the target on that thread during the drag.</para>
/// <para><b>WPF.</b> A window has one OLE drop target, and WPF registers its own on every window it creates, with or
/// without <c>AllowDrop</c> (observed: <c>RegisterDragDrop</c> on a plain WPF window fails with
/// <c>DRAGDROP_E_ALREADYREGISTERED</c>). <see cref="Register"/> therefore replaces it: the tent window gets no WPF
/// drag and drop events, which it does not use.</para>
/// <para><b>Drag image.</b> Drawn by the shell's <c>IDropTargetHelper</c>, so the image a drag from Explorer shows keeps
/// following the pointer over a tent.</para>
/// <para><b>Known limit.</b> A forwarded drop runs the shell's copy or move on the tent thread for data objects that do
/// not support asynchronous transfer, as a drop on the desktop runs on Explorer's desktop thread. The shell shows its
/// progress dialog and keeps the thread pumping messages meanwhile.</para>
/// </remarks>
public sealed unsafe class TentDropTarget : IDropTarget.Interface, IDisposable
{
    private const int _alreadyRegistered = unchecked((int)0x80040101); // DRAGDROP_E_ALREADYREGISTERED

    private readonly HWND _hwnd;
    private readonly ITentDropHandler _handler;
    private readonly Dictionary<ItemId, bool> _acceptsDrops = [];
    private IDropTargetHelper* _helper;
    private IDataObject* _data;
    private List<ItemId> _items = [];
    private bool _fromTents;
    private DragEffects _allowed;
    private IDropTarget* _forward;
    private ItemId? _forwardItem;
    private DropRoute _route;
    private DesktopItem? _target;
    private bool _registered;

    private TentDropTarget(HWND hwnd, ITentDropHandler handler)
    {
        _hwnd = hwnd;
        _handler = handler;
    }

    /// <summary>
    /// Makes <paramref name="hwnd"/> a drop target, replacing a drop target already registered for it (WPF's). Call on
    /// the window's thread.
    /// </summary>
    /// <exception cref="InvalidOperationException">The thread is not an STA, or OLE refused the registration.</exception>
    public static TentDropTarget Register(nint hwnd, ITentDropHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
        {
            throw new InvalidOperationException("A drop target must be registered on an OLE STA thread (the window's UI thread).");
        }

        var target = new TentDropTarget((HWND)hwnd, handler);
        var com = ShellComWrappers.Instance.GetDropTarget(target);
        try
        {
            var hr = PInvoke.RegisterDragDrop((HWND)hwnd, com);
            if (hr.Value == _alreadyRegistered)
            {
                PInvoke.RevokeDragDrop((HWND)hwnd); // WPF's own target (see remarks)
                hr = PInvoke.RegisterDragDrop((HWND)hwnd, com);
            }

            if (hr.Failed)
            {
                throw new InvalidOperationException($"RegisterDragDrop failed: 0x{hr.Value:X8}.");
            }
        }
        finally
        {
            Com.Release(ref com); // OLE keeps its own reference while registered.
        }

        target._registered = true;
        return target;
    }

    /// <summary>Removes the drop target. Call on the window's thread, before the window is destroyed.</summary>
    public void Dispose()
    {
        if (_registered)
        {
            _registered = false;
            PInvoke.RevokeDragDrop(_hwnd);
        }

        EndDrag();
        Com.Release(ref _helper);
    }

    /// <summary>Creates a target that is not registered with OLE, so tests can call it through its vtable.</summary>
    internal static TentDropTarget CreateUnregistered(ITentDropHandler handler) => new(default, handler);

    HRESULT IDropTarget.Interface.DragEnter(IDataObject* pDataObj, MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect) =>
        Guard(pdwEffect, () =>
        {
            EndDrag();
            if (pDataObj is not null)
            {
                pDataObj->AddRef();
                _data = pDataObj;
            }

            _allowed = (DragEffects)(*pdwEffect);
            _fromTents = InternalDragMarker.IsPresent(_data);
            _items = ReadItems(_data);

            var effect = Evaluate(grfKeyState, pt, *pdwEffect);
            var point = new Point(pt.x, pt.y);
            var helper = Helper();
            if (helper is not null && _data is not null)
            {
                helper->DragEnter(_hwnd, _data, &point, effect);
            }

            *pdwEffect = effect;
        });

    HRESULT IDropTarget.Interface.DragOver(MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect) =>
        Guard(pdwEffect, () =>
        {
            var effect = Evaluate(grfKeyState, pt, *pdwEffect);
            var point = new Point(pt.x, pt.y);
            if (_helper is not null)
            {
                _helper->DragOver(&point, effect);
            }

            *pdwEffect = effect;
        });

    HRESULT IDropTarget.Interface.DragLeave() =>
        Guard(null, () =>
        {
            if (_helper is not null)
            {
                _helper->DragLeave();
            }

            EndDrag();
        });

    HRESULT IDropTarget.Interface.Drop(IDataObject* pDataObj, MODIFIERKEYS_FLAGS grfKeyState, POINTL pt, DROPEFFECT* pdwEffect) =>
        Guard(pdwEffect, () =>
        {
            var effect = Evaluate(grfKeyState, pt, *pdwEffect);
            if (_forward is not null && effect != DROPEFFECT.DROPEFFECT_NONE)
            {
                effect = *pdwEffect;
                _forward->Drop(pDataObj, grfKeyState, pt, &effect); // ends the forwarded drag; no DragLeave after it
                Com.Release(ref _forward);
            }

            var point = new Point(pt.x, pt.y);
            if (_helper is not null && pDataObj is not null)
            {
                _helper->Drop(pDataObj, &point, effect);
            }

            var drop = new TentDrop(_route, _items, (DragEffects)effect, point, _target);
            EndDrag();
            *pdwEffect = effect;
            // A forwarded drop is reported even with effect None: after an optimized or asynchronous move the shell's
            // target returns DROPEFFECT_NONE (so the source deletes nothing) although it moves the files. The watcher
            // then reports what actually changed. A reassign is reported only when it shows Move.
            var report = drop.Route switch
            {
                DropRoute.Reassign => effect != DROPEFFECT.DROPEFFECT_NONE,
                DropRoute.ForwardToDesktop or DropRoute.ForwardToItem => true,
                _ => false,
            };
            if (report)
            {
                _handler.Dropped(drop);
            }
        });

    // Decides the route for the pointer position and keeps the forwarded target in step with it.
    private DROPEFFECT Evaluate(MODIFIERKEYS_FLAGS keys, POINTL pt, DROPEFFECT allowed)
    {
        var point = new Point(pt.x, pt.y);
        _target = _data is null ? null : _handler.HitTest(point);
        var overItem = _target is not null && !_items.Contains(_target.Id) && AcceptsDrops(_target);
        _route = DropPolicy.Route(_fromTents, _items.Count > 0, overItem);
        switch (_route)
        {
            case DropRoute.Reassign:
                LeaveForward();
                return (DROPEFFECT)DropPolicy.ReassignEffect((DragEffects)allowed);
            case DropRoute.ForwardToItem:
            case DropRoute.ForwardToDesktop:
                var key = _route == DropRoute.ForwardToItem ? _target!.Id : (ItemId?)null;
                var effect = allowed;
                if (_forward is not null && _forwardItem == key)
                {
                    _forward->DragOver(keys, pt, &effect);
                    return effect;
                }

                LeaveForward();
                _forward = key is null ? DesktopDropTarget() : ItemDropTarget(_target!);
                _forwardItem = key;
                if (_forward is null)
                {
                    return DROPEFFECT.DROPEFFECT_NONE;
                }

                _forward->DragEnter(_data, keys, pt, &effect);
                return effect;
            case DropRoute.Reject:
            default:
                LeaveForward();
                return DROPEFFECT.DROPEFFECT_NONE;
        }
    }

    private bool AcceptsDrops(DesktopItem item)
    {
        if (!_acceptsDrops.TryGetValue(item.Id, out var accepts))
        {
            var target = ItemDropTarget(item);
            accepts = target is not null;
            Com.Release(ref target);
            _acceptsDrops[item.Id] = accepts;
        }

        return accepts;
    }

    private IDropTarget* ItemDropTarget(DesktopItem item) =>
        (IDropTarget*)ShellFolders.GetUIObject([item.IdList], _hwnd, IDropTarget.IID_Guid, out _);

    // The desktop root's drop target, which drops into the user's Desktop folder, as a drop on the desktop does.
    private IDropTarget* DesktopDropTarget()
    {
        IShellFolder* desktop = null;
        try
        {
            PInvoke.SHGetDesktopFolder(&desktop).ThrowOnFailure();
            var iid = IDropTarget.IID_Guid;
            void* target;
            desktop->CreateViewObject(_hwnd, &iid, &target);
            return (IDropTarget*)target;
        }
        catch (Exception ex) when (ComErrors.IsCallFailure(ex))
        {
            return null;
        }
        finally
        {
            Com.Release(ref desktop);
        }
    }

    private void LeaveForward()
    {
        if (_forward is not null)
        {
            _forward->DragLeave();
            Com.Release(ref _forward);
        }

        _forwardItem = null;
    }

    private void EndDrag()
    {
        LeaveForward();
        Com.Release(ref _data);
        _items = [];
        _acceptsDrops.Clear();
        _fromTents = false;
        _target = null;
        _route = DropRoute.Reject;
    }

    private IDropTargetHelper* Helper()
    {
        if (_helper is null && !_hwnd.IsNull)
        {
            var clsid = PInvoke.CLSID_DragDropHelper;
            var iid = IDropTargetHelper.IID_Guid;
            void* helper;
            if (PInvoke.CoCreateInstance(&clsid, null, CLSCTX.CLSCTX_INPROC_SERVER, &iid, &helper).Succeeded)
            {
                _helper = (IDropTargetHelper*)helper;
            }
        }

        return _helper;
    }

    // The ids of the shell items the data object names (empty when it names none).
    private static List<ItemId> ReadItems(IDataObject* data)
    {
        var ids = new List<ItemId>();
        if (data is null)
        {
            return ids;
        }

        IShellItemArray* array = null;
        try
        {
            if (PInvoke.SHCreateShellItemArrayFromDataObject(data, out array).Failed)
            {
                return ids;
            }

            array->GetCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                IShellItem* item = null;
                try
                {
                    array->GetItemAt(i, &item);
                    PWSTR name = default;
                    item->GetDisplayName(SIGDN.SIGDN_DESKTOPABSOLUTEPARSING, &name);
                    try
                    {
                        if (DesktopItemIdentity.FromParsingName(name.ToString()) is { } id)
                        {
                            ids.Add(id);
                        }
                    }
                    finally
                    {
                        PInvoke.CoTaskMemFree(name.Value);
                    }
                }
                catch (Exception ex) when (ComErrors.IsCallFailure(ex))
                {
                    // An item without a parsing name is skipped.
                }
                finally
                {
                    Com.Release(ref item);
                }
            }
        }
        catch (Exception ex) when (ComErrors.IsCallFailure(ex))
        {
            // Not shell data.
        }
        finally
        {
            Com.Release(ref array);
        }

        return ids;
    }

    // OLE must get an HRESULT, never an exception; on failure the drop is refused and the drag state reset.
    private HRESULT Guard(DROPEFFECT* effect, Action body)
    {
        try
        {
            body();
            return HRESULT.S_OK;
        }
        catch (Exception ex) when (ShellPlatform.ReportCallbackException(ex))
        {
            if (effect is not null)
            {
                *effect = DROPEFFECT.DROPEFFECT_NONE;
            }

            EndDrag();
            return (HRESULT)unchecked((int)0x8000FFFF); // E_UNEXPECTED
        }
    }
}
