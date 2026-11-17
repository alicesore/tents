using Tents.Core;
using static Tents.Tests.TestData;

namespace Tents.Tests;

public class LayoutTests
{
    [Fact]
    public void CreateNew_HoldsOnlyTheDefaultTent()
    {
        var layout = Layout.CreateWithDefaultTent(SingleMonitor, Tent(0, "a"));

        Assert.Single(layout.Tents);
        Assert.Equal(Id(0), layout.DefaultTentId);
        Assert.Same(layout.Tents[0], layout.DefaultTent);
        Assert.Equal(1, layout.ItemCount);
    }

    [Fact]
    public void Create_KeepsTentOrderAndFindsTheDefaultTentAnywhere()
    {
        var layout = Layout.Create(SingleMonitor, [Tent(1), Tent(2), Tent(0)], Id(0));

        Assert.Equal([Id(1), Id(2), Id(0)], layout.Tents.Select(t => t.Id));
        Assert.Equal(Id(0), layout.DefaultTent.Id);
    }

    [Fact]
    public void Create_RejectsNoTents() =>
        Assert.Throws<InvalidLayoutException>(() => Layout.Create(SingleMonitor, [], Id(0)));

    [Fact]
    public void Create_RejectsEmptyDefaultTentId() =>
        Assert.Throws<InvalidLayoutException>(() => Layout.Create(SingleMonitor, [Tent(0)], default));

    [Fact]
    public void Create_RejectsDefaultTentThatIsNotInTheLayout() =>
        Assert.Throws<InvalidLayoutException>(() => Layout.Create(SingleMonitor, [Tent(1)], Id(0)));

    [Fact]
    public void Create_RejectsDuplicateTentIds() =>
        Assert.Throws<InvalidLayoutException>(() => Layout.Create(SingleMonitor, [Tent(0), Tent(0)], Id(0)));

    [Fact]
    public void Create_RejectsNullTent() =>
        Assert.Throws<InvalidLayoutException>(() => Layout.Create(SingleMonitor, [Tent(0), null!], Id(0)));

    [Fact]
    public void Create_RejectsAnItemInTwoTents()
    {
        var ex = Assert.Throws<InvalidLayoutException>(() => LayoutWith(Tent(0, "a"), Tent(1, "b", "a")));

        Assert.DoesNotContain("TestDesktop", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RejectsAnItemTwiceInOneTent() =>
        Assert.Throws<InvalidLayoutException>(() => LayoutWith(Tent(0, "a", "b", "a")));

    [Fact]
    public void Create_TreatsItemIdsThatDifferOnlyInCaseAsTheSameItem() =>
        Assert.Throws<InvalidLayoutException>(() => LayoutWith(Tent(0, "notes.txt"), Tent(1, "NOTES.TXT")));

    [Fact]
    public void Create_RejectsEmptyItemId()
    {
        var tent = new Tent(Id(0), "t", SomeBounds, items: [default]);

        Assert.Throws<InvalidLayoutException>(() => Layout.CreateWithDefaultTent(SingleMonitor, tent));
    }

    [Fact]
    public void FindTentOf_ReturnsOwnerOrNull()
    {
        var layout = LayoutWith(Tent(0, "a"), Tent(1, "b"));

        Assert.Equal(Id(0), layout.FindTentOf(Item("a")));
        Assert.Equal(Id(1), layout.FindTentOf(Item("B")));
        Assert.Null(layout.FindTentOf(Item("missing")));
        Assert.True(layout.ContainsItem(Item("b")));
        Assert.False(layout.ContainsItem(Item("missing")));
    }

    [Fact]
    public void GetTent_UnknownId_Throws()
    {
        var layout = LayoutWith(Tent(0));

        Assert.Throws<KeyNotFoundException>(() => layout.GetTent(Id(9)));
        Assert.False(layout.TryGetTent(Id(9), out _));
        Assert.Equal(-1, layout.IndexOf(Id(9)));
    }

    [Fact]
    public void Equality_IsStructural()
    {
        var a = LayoutWith(Tent(0, "a"), Tent(1, "b"));
        var b = LayoutWith(Tent(0, "a"), Tent(1, "b"));
        var c = LayoutWith(Tent(0, "a"), Tent(1, "c"));

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.NotEqual(a, c);
        Assert.NotEqual(a, Layout.Create(DualMonitor, a.Tents, a.DefaultTentId));
    }
}

public class LayoutSetTests
{
    [Fact]
    public void Empty_HasNoLayouts()
    {
        Assert.Empty(LayoutSet.Empty.Layouts);
        Assert.Null(LayoutSet.Empty.Find(SingleMonitor));
        Assert.Same(LayoutSet.Empty, LayoutSet.Create([]));
    }

    [Fact]
    public void Create_RejectsTwoLayoutsForTheSameMonitors() =>
        Assert.Throws<InvalidLayoutException>(() => LayoutSet.Create([LayoutWith(Tent(0)), LayoutWith(Tent(1))]));

    [Fact]
    public void Find_MatchesByMonitorConfiguration()
    {
        var single = LayoutWith(Tent(0));
        var dual = Layout.CreateWithDefaultTent(DualMonitor, Tent(1));
        var set = LayoutSet.Create([single, dual]);

        Assert.Same(single, set.Find(SingleMonitor));
        Assert.Same(dual, set.Find(MonitorConfigKey.Create(DualMonitor.Monitors.Reverse())));
    }

    [Fact]
    public void SetLayout_AppendsOrReplaces()
    {
        var first = LayoutWith(Tent(0));
        var replacement = LayoutWith(Tent(0, "a"));
        var dual = Layout.CreateWithDefaultTent(DualMonitor, Tent(1));

        var set = LayoutSet.Empty.SetLayout(first).SetLayout(dual).SetLayout(replacement);

        Assert.Equal(2, set.Layouts.Length);
        Assert.Same(replacement, set.Layouts[0]);
        Assert.Same(dual, set.Layouts[1]);
    }

    [Fact]
    public void SetLayout_SameInstance_ReturnsSameSet()
    {
        var set = LayoutSet.Empty.SetLayout(LayoutWith(Tent(0)));

        Assert.Same(set, set.SetLayout(set.Layouts[0]));
    }

    [Fact]
    public void RemoveLayout_RemovesOrReturnsSameSet()
    {
        var set = LayoutSet.Create([LayoutWith(Tent(0))]);

        Assert.Same(set, set.RemoveLayout(DualMonitor));
        Assert.Same(LayoutSet.Empty, set.RemoveLayout(SingleMonitor));
    }
}
