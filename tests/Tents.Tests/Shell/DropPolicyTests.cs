using System.Drawing;
using Tents.Shell.Desktop;
using Tents.Shell.DragDrop;
using Windows.Win32.Foundation;
using Windows.Win32.System.Ole;
using Windows.Win32.System.SystemServices;

namespace Tents.Tests.Shell;

public class DropPolicyTests
{
    [Theory]
    [InlineData(true, true, false, DropRoute.Reassign)]
    [InlineData(false, true, false, DropRoute.ForwardToDesktop)]
    [InlineData(true, true, true, DropRoute.ForwardToItem)]
    [InlineData(false, true, true, DropRoute.ForwardToItem)]
    [InlineData(true, false, false, DropRoute.Reject)]
    [InlineData(false, false, true, DropRoute.Reject)]
    public void Route(bool fromTents, bool hasShellItems, bool overItem, DropRoute expected) =>
        Assert.Equal(expected, DropPolicy.Route(fromTents, hasShellItems, overItem));

    [Theory]
    [InlineData(DragEffects.Copy | DragEffects.Move | DragEffects.Link, DragEffects.Move)]
    [InlineData(DragEffects.Move, DragEffects.Move)]
    [InlineData(DragEffects.Copy, DragEffects.None)]
    [InlineData(DragEffects.None, DragEffects.None)]
    public void Reassign_ShowsMoveOrNothing(DragEffects allowed, DragEffects expected) =>
        Assert.Equal(expected, DropPolicy.ReassignEffect(allowed));

    [Fact]
    public void DragEffects_MatchDropEffect()
    {
        Assert.Equal((int)DROPEFFECT.DROPEFFECT_COPY, (int)DragEffects.Copy);
        Assert.Equal((int)DROPEFFECT.DROPEFFECT_MOVE, (int)DragEffects.Move);
        Assert.Equal((int)DROPEFFECT.DROPEFFECT_LINK, (int)DragEffects.Link);
    }

    [Fact]
    public unsafe void DropTarget_ThroughItsComVtable_RefusesADropWithoutData()
    {
        // Exercises the ComWrappers vtable (IUnknown from the runtime, IDropTarget from CsWin32) without OLE or a shell.
        var handler = new RecordingHandler();
        using var target = TentDropTarget.CreateUnregistered(handler);
        var com = Tents.Shell.Interop.ShellComWrappers.Instance.GetDropTarget(target);
        try
        {
            var effect = DROPEFFECT.DROPEFFECT_COPY | DROPEFFECT.DROPEFFECT_MOVE;
            com->DragEnter(null, MODIFIERKEYS_FLAGS.MK_LBUTTON, new POINTL { x = 10, y = 20 }, &effect);
            Assert.Equal(DROPEFFECT.DROPEFFECT_NONE, effect);

            effect = DROPEFFECT.DROPEFFECT_MOVE;
            com->DragOver(MODIFIERKEYS_FLAGS.MK_LBUTTON, new POINTL { x = 11, y = 21 }, &effect);
            Assert.Equal(DROPEFFECT.DROPEFFECT_NONE, effect);

            effect = DROPEFFECT.DROPEFFECT_MOVE;
            com->Drop(null, 0, new POINTL { x = 12, y = 22 }, &effect);
            Assert.Equal(DROPEFFECT.DROPEFFECT_NONE, effect);
            com->DragLeave();

            Assert.Equal(2u, com->AddRef());
            Assert.Equal(1u, com->Release());
            Assert.Empty(handler.Drops);
        }
        finally
        {
            com->Release();
        }
    }

    private sealed class RecordingHandler : ITentDropHandler
    {
        public List<TentDrop> Drops { get; } = [];

        public DesktopItem? HitTest(Point screenPoint) => null;

        public void Dropped(TentDrop drop) => Drops.Add(drop);
    }
}
