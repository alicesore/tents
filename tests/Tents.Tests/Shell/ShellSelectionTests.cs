using Tents.Shell.Interop;
using Tents.Shell.Menus;

namespace Tents.Tests.Shell;

public class ShellSelectionTests
{
    // Entries are [size, 0, tag...]; a list ends with [0, 0].
    private static ShellIdList List(params byte[][] entries) =>
        ShellIdList.FromBytes([.. entries.SelectMany(e => new byte[] { (byte)(e.Length + 2), 0 }.Concat(e)), 0, 0]);

    [Fact]
    public void Parent_AndLastItem_SplitTheList()
    {
        var list = List([1], [2, 2], [3]);

        Assert.Equal(List([1], [2, 2]), list.Parent);
        Assert.Equal(List([3]), list.LastItem);
    }

    [Fact]
    public void ChildOfTheRoot_HasTheEmptyParent()
    {
        var list = List([7]);

        Assert.Equal(ShellIdList.Empty, list.Parent);
        Assert.Equal(list, list.LastItem);
    }

    [Fact]
    public void Empty_HasEmptyParentAndLastItem()
    {
        Assert.Equal(ShellIdList.Empty, ShellIdList.Empty.Parent);
        Assert.Equal(ShellIdList.Empty, ShellIdList.Empty.LastItem);
    }

    [Fact]
    public void DesktopItems_AllShareTheDesktopRoot()
    {
        var (parent, children, dropped) = ShellSelection.Group([List([1]), List([2]), List([3])]);

        Assert.Equal(ShellIdList.Empty, parent);
        Assert.Equal([List([1]), List([2]), List([3])], children);
        Assert.Equal(0, dropped);
    }

    [Fact]
    public void MixedParents_KeepTheFirstItemsParent()
    {
        var (parent, children, dropped) = ShellSelection.Group([List([9], [1]), List([2]), List([9], [3])]);

        Assert.Equal(List([9]), parent);
        Assert.Equal([List([1]), List([3])], children);
        Assert.Equal(1, dropped);
    }

    [Fact]
    public void ParentsWithDifferentBytes_MatchWhenTheComparerSaysSo()
    {
        var items = new[] { List([9, 1], [1]), List([9, 2], [2]), List([8], [3]) };

        var (parent, children, dropped) = ShellSelection.Group(items, (a, b) => a.ItemCount == b.ItemCount && b.Bytes[2] == 9);

        Assert.Equal(List([9, 1]), parent);
        Assert.Equal([List([1]), List([2])], children);
        Assert.Equal(1, dropped);
    }

    [Fact]
    public void EmptySelection_IsRejected() => Assert.Throws<ArgumentException>(() => ShellSelection.Group([]));

    [Fact]
    public void DesktopRootInASelection_IsRejected() =>
        Assert.Throws<ArgumentException>(() => ShellSelection.Group([ShellIdList.Empty]));

    [Fact]
    public void QueryFlags_AlwaysAskForRename_AndAddExtendedVerbsOnShift()
    {
        Assert.Equal(0x10u, ContextMenuProtocol.QueryFlags(new ContextMenuOptions()));
        Assert.Equal(0x110u, ContextMenuProtocol.QueryFlags(new ContextMenuOptions(ExtendedVerbs: true)));
    }

    [Theory]
    [InlineData("rename", true)]
    [InlineData("Rename", true)]
    [InlineData("delete", false)]
    [InlineData("open", false)]
    [InlineData(null, false)]
    public void OnlyRename_IsAHostVerb(string? verb, bool expected) => Assert.Equal(expected, ContextMenuProtocol.IsHostVerb(verb));
}
