using System.Drawing;
using Tents.Core;
using Tents.Shell.Desktop;
using Tents.Shell.DragDrop;
using Tents.Shell.Interop;
using Tents.Shell.Threading;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.System.Ole;
using Windows.Win32.System.SystemServices;
using Xunit.Abstractions;

namespace Tents.Tests.Shell;

/// <summary>
/// Drop target behavior with real shell data objects, called through the COM vtable as OLE would, without a mouse.
/// Only temporary files are used. Drops are never forwarded to the desktop: that route is checked with
/// DragEnter and DragLeave only, which change nothing.
/// </summary>
[Trait(TestCategories.Key, TestCategories.Desktop)]
public sealed class DragDropShellTests(ITestOutputHelper output) : IDisposable
{
    private const DROPEFFECT _all = DROPEFFECT.DROPEFFECT_COPY | DROPEFFECT.DROPEFFECT_MOVE | DROPEFFECT.DROPEFFECT_LINK;
    private static readonly POINTL _point = new() { x = 100, y = 100 };

    private readonly StaWorker _worker = new("test drag and drop");
    private readonly string _folder = Directory.CreateTempSubdirectory("tents-drop-test-").FullName;

    public void Dispose()
    {
        _worker.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public unsafe Task Register_OnAWindowThatAlreadyHasADropTarget_ReplacesIt() => _worker.InvokeAsync(() =>
    {
        // WPF registers an OLE drop target on every window it creates, so a tent window always has one already.
        // A hidden window of this thread, registered twice, takes the same path (DRAGDROP_E_ALREADYREGISTERED).
        var hwnd = (nint)Tents.Shell.Menus.ShellMenuHost.GetWindow().Value;
        var first = TentDropTarget.Register(hwnd, new Handler(hit: null));
        using var second = TentDropTarget.Register(hwnd, new Handler(hit: null));
        Assert.NotNull(second);
        GC.KeepAlive(first); // revoked by the second registration; disposing it would revoke the second one
    });

    [Fact]
    public unsafe Task InternalMarker_IsFoundOnlyAfterItWasSet() => _worker.InvokeAsync(() =>
    {
        var items = Items(CreateFile("a.txt"));
        var data = DataObject(items);
        try
        {
            Assert.False(InternalDragMarker.IsPresent(data));
            Assert.True(InternalDragMarker.Set(data));
            Assert.True(InternalDragMarker.IsPresent(data));
        }
        finally
        {
            Com.Release(ref data);
        }
    });

    [Fact]
    public unsafe Task DragBetweenTents_Reassigns_AndTouchesNoFile() => _worker.InvokeAsync(() =>
    {
        var paths = new[] { CreateFile("a.txt"), CreateFile("b.txt") };
        var items = Items(paths);
        var data = DataObject(items);
        var handler = new Handler(hit: null);
        using var target = TentDropTarget.CreateUnregistered(handler);
        var com = ShellComWrappers.Instance.GetDropTarget(target);
        try
        {
            InternalDragMarker.Set(data);
            var effect = _all;
            com->DragEnter(data, MODIFIERKEYS_FLAGS.MK_LBUTTON, _point, &effect);
            Assert.Equal(DROPEFFECT.DROPEFFECT_MOVE, effect);

            effect = _all;
            com->Drop(data, 0, _point, &effect);

            var drop = Assert.Single(handler.Drops);
            Assert.Equal(DropRoute.Reassign, drop.Route);
            Assert.Equal(DragEffects.Move, drop.Effect);
            Assert.Equal(paths.Select(p => new ItemId(p)), drop.Items);
            Assert.All(paths, p => Assert.True(File.Exists(p)));
        }
        finally
        {
            com->Release();
            Com.Release(ref data);
        }
    });

    [Fact]
    public unsafe Task DragFromElsewhere_OverEmptyArea_IsHandedToTheDesktop_ButNotDropped() => _worker.InvokeAsync(() =>
    {
        var path = CreateFile("a.txt");
        var data = DataObject(Items(path));
        var handler = new Handler(hit: null);
        using var target = TentDropTarget.CreateUnregistered(handler);
        var com = ShellComWrappers.Instance.GetDropTarget(target);
        try
        {
            var effect = _all;
            com->DragEnter(data, MODIFIERKEYS_FLAGS.MK_LBUTTON, _point, &effect);
            output.WriteLine($"desktop drop target offers: {(DragEffects)effect}");
            Assert.NotEqual(DROPEFFECT.DROPEFFECT_NONE, effect);

            com->DragLeave(); // never Drop here: that would put the file on the real desktop
            Assert.Empty(handler.Drops);
            Assert.True(File.Exists(path));
        }
        finally
        {
            com->Release();
            Com.Release(ref data);
        }
    });

    [Fact]
    public async Task DropOntoAFolderItem_IsDoneByTheShell()
    {
        var file = CreateFile("moved.txt");
        var subfolder = Directory.CreateDirectory(Path.Combine(_folder, "inside")).FullName;
        var handler = await _worker.InvokeAsync(() => DropOntoFolder(file, subfolder));

        var drop = Assert.Single(handler.Drops);
        Assert.Equal(DropRoute.ForwardToItem, drop.Route);
        var moved = Path.Combine(subfolder, "moved.txt");
        Assert.True(SpinWait.SpinUntil(() => File.Exists(moved) && !File.Exists(file), TimeSpan.FromSeconds(10)), "The shell did not move the file.");
    }

    [Fact]
    public unsafe Task DropOntoTheDraggedItemItself_IsAReassign() => _worker.InvokeAsync(() =>
    {
        var subfolder = Directory.CreateDirectory(Path.Combine(_folder, "self")).FullName;
        var folderItem = DesktopItemFactory.FromParsingName(subfolder)!;
        var data = DataObject([folderItem]);
        var handler = new Handler(hit: folderItem);
        using var target = TentDropTarget.CreateUnregistered(handler);
        var com = ShellComWrappers.Instance.GetDropTarget(target);
        try
        {
            InternalDragMarker.Set(data);
            var effect = _all;
            com->DragEnter(data, MODIFIERKEYS_FLAGS.MK_LBUTTON, _point, &effect);
            Assert.Equal(DROPEFFECT.DROPEFFECT_MOVE, effect);
            com->DragLeave();
            Assert.True(Directory.Exists(subfolder));
        }
        finally
        {
            com->Release();
            Com.Release(ref data);
        }
    });

    private static unsafe Handler DropOntoFolder(string file, string subfolder)
    {
        var folderItem = DesktopItemFactory.FromParsingName(subfolder)!;
        var data = DataObject(Items(file));
        var handler = new Handler(hit: folderItem);
        using var target = TentDropTarget.CreateUnregistered(handler);
        var com = ShellComWrappers.Instance.GetDropTarget(target);
        try
        {
            InternalDragMarker.Set(data); // even a drag from a tent goes to the folder when dropped onto it
            var effect = DROPEFFECT.DROPEFFECT_MOVE;
            com->DragEnter(data, MODIFIERKEYS_FLAGS.MK_LBUTTON, _point, &effect);
            Assert.Equal(DROPEFFECT.DROPEFFECT_MOVE, effect);
            effect = DROPEFFECT.DROPEFFECT_MOVE;
            com->Drop(data, 0, _point, &effect);
            return handler;
        }
        finally
        {
            com->Release();
            Com.Release(ref data);
        }
    }

    private static unsafe IDataObject* DataObject(IReadOnlyList<DesktopItem> items)
    {
        var data = (IDataObject*)ShellFolders.GetUIObject([.. items.Select(i => i.IdList)], default, IDataObject.IID_Guid, out var dropped);
        Assert.Equal(0, dropped);
        Assert.True(data is not null, "No shell data object.");
        return data;
    }

    private static List<DesktopItem> Items(params string[] paths) =>
        [.. paths.Select(p => DesktopItemFactory.FromParsingName(p) ?? throw new InvalidOperationException("No shell item."))];

    private string CreateFile(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "Tents test file");
        return path;
    }

    private sealed class Handler(DesktopItem? hit) : ITentDropHandler
    {
        public List<TentDrop> Drops { get; } = [];

        public DesktopItem? HitTest(Point screenPoint) => hit;

        public void Dropped(TentDrop drop) => Drops.Add(drop);
    }
}
