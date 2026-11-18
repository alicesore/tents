using Tents.Core;
using Tents.Shell.Desktop;
using Tents.Shell.Interop;

namespace Tents.Tests.Shell;

public class DesktopSnapshotTests
{
    private static DesktopItem Item(string id, string? name = null, byte tag = 1) =>
        new(new ItemId(id), name ?? id, IsFileSystem: true, IsFolder: false, ShellIdList.FromBytes([3, 0, tag, 0, 0]));

    [Fact]
    public void Items_KeepTheNativeOrder()
    {
        var snapshot = new DesktopSnapshot([Item("b"), Item("a"), Item("c")]);

        Assert.Equal(["b", "a", "c"], snapshot.Items.Select(i => i.Id.Value));
    }

    [Fact]
    public void RepeatedId_KeepsTheFirstItem_AndIsCounted()
    {
        var snapshot = new DesktopSnapshot([Item("a", "first"), Item("A", "second"), Item("b")], skippedCount: 2);

        Assert.Equal(2, snapshot.Count);
        Assert.Equal(1, snapshot.DuplicateCount);
        Assert.Equal(2, snapshot.SkippedCount);
        Assert.True(snapshot.TryGet(new ItemId("a"), out var kept));
        Assert.Equal("first", kept.DisplayName);
    }

    [Fact]
    public void EmptyId_IsRejected() =>
        Assert.Throws<ArgumentException>(() => new DesktopSnapshot([Item("a") with { Id = default }]));

    [Fact]
    public void Diff_FindsAddedRemovedAndChanged()
    {
        var before = new DesktopSnapshot([Item("keep"), Item("rename-me", "Old"), Item("gone"), Item("pidl", tag: 1)]);
        var after = new DesktopSnapshot([Item("new"), Item("keep"), Item("rename-me", "New"), Item("pidl", tag: 2)]);

        var diff = after.DiffFrom(before);

        Assert.Equal(["new"], diff.Added.Select(i => i.Id.Value));
        Assert.Equal(["gone"], diff.Removed.Select(i => i.Id.Value));
        Assert.Equal(["rename-me", "pidl"], diff.Changed.Select(i => i.Id.Value));
        Assert.Equal("New", diff.Changed[0].DisplayName);
        Assert.False(diff.IsEmpty);
    }

    [Fact]
    public void Diff_OfEqualSnapshots_IsEmpty()
    {
        var a = new DesktopSnapshot([Item("x"), Item("y")]);
        var b = new DesktopSnapshot([Item("X", name: "x"), Item("y")]); // same item, id in another casing

        Assert.True(b.DiffFrom(a).IsEmpty);
    }

    [Fact]
    public void Diff_FromEmpty_AddsEverything() =>
        Assert.Equal(2, new DesktopSnapshot([Item("x"), Item("y")]).DiffFrom(DesktopSnapshot.Empty).Added.Count);
}
