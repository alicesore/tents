using Tents.Core;
using static Tents.Tests.TestData;

namespace Tents.Tests;

public class LayoutOperationsTests
{
    // Default tent Id(0) holds a, b; tent 1 holds c, d; tent 2 is empty.
    private static readonly Layout _start = LayoutWith(Tent(0, "a", "b"), Tent(1, "c", "d"), Tent(2));

    /// <summary>Re-checks every invariant on an operation's result (operations skip the check internally).</summary>
    private static Layout Valid(Layout layout)
    {
        var rebuilt = Layout.Create(layout.Monitors, layout.Tents, layout.DefaultTentId);
        Assert.Equal(rebuilt, layout);
        Assert.Equal(layout.DefaultTentId, layout.DefaultTent.Id);
        return layout;
    }

    private static string[] ItemsOf(Layout layout, int tent) =>
        [.. layout.GetTent(Id(tent)).Items.Select(i => i.Value[(i.Value.LastIndexOf('\\') + 1)..])];

    // ---- Create ----

    [Fact]
    public void CreateTent_AppendsAnEmptyExpandedTent()
    {
        var bounds = new PixelRect(10, 20, 300, 200);

        var layout = Valid(_start.CreateTent(Id(5), "New", bounds));

        Assert.Equal(4, layout.Tents.Length);
        var tent = layout.Tents[^1];
        Assert.Equal(Id(5), tent.Id);
        Assert.Equal("New", tent.Title);
        Assert.Equal(bounds, tent.Bounds);
        Assert.False(tent.IsRolledUp);
        Assert.Empty(tent.Items);
        Assert.Equal(3, _start.Tents.Length);
    }

    [Fact]
    public void CreateTent_ExistingId_Throws() =>
        Assert.Throws<ArgumentException>(() => _start.CreateTent(Id(1), "Again", SomeBounds));

    [Fact]
    public void CreateTent_EmptyId_Throws() =>
        Assert.Throws<ArgumentException>(() => _start.CreateTent(default, "t", SomeBounds));

    [Fact]
    public void CreateTent_EmptyBounds_Throws() =>
        Assert.Throws<ArgumentException>(() => _start.CreateTent(Id(5), "t", new PixelRect(0, 0, 0, 0)));

    [Fact]
    public void CreateTent_NullTitle_Throws() =>
        Assert.Throws<ArgumentNullException>(() => _start.CreateTent(Id(5), null!, SomeBounds));

    // ---- Move and resize ----

    [Fact]
    public void MoveTent_ChangesPositionOnly()
    {
        var layout = Valid(_start.MoveTent(Id(1), -50, 700));

        Assert.Equal(new PixelRect(-50, 700, SomeBounds.Width, SomeBounds.Height), layout.GetTent(Id(1)).Bounds);
        Assert.Equal(SomeBounds, _start.GetTent(Id(1)).Bounds);
    }

    [Fact]
    public void MoveTent_SamePosition_ReturnsSameInstance() =>
        Assert.Same(_start, _start.MoveTent(Id(1), SomeBounds.X, SomeBounds.Y));

    [Fact]
    public void ResizeTent_ChangesSizeOnly()
    {
        var layout = Valid(_start.ResizeTent(Id(1), 640, 480));

        Assert.Equal(new PixelRect(SomeBounds.X, SomeBounds.Y, 640, 480), layout.GetTent(Id(1)).Bounds);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-1, 100)]
    public void ResizeTent_NonPositiveSize_Throws(int width, int height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _start.ResizeTent(Id(1), width, height));

    [Fact]
    public void ResizeTent_SameSize_ReturnsSameInstance() =>
        Assert.Same(_start, _start.ResizeTent(Id(1), SomeBounds.Width, SomeBounds.Height));

    [Fact]
    public void SetTentBounds_MovesAndResizes()
    {
        var bounds = new PixelRect(1, 2, 3, 4);

        Assert.Equal(bounds, Valid(_start.SetTentBounds(Id(2), bounds)).GetTent(Id(2)).Bounds);
        Assert.Same(_start, _start.SetTentBounds(Id(2), SomeBounds));
        Assert.Throws<ArgumentOutOfRangeException>(() => _start.SetTentBounds(Id(2), new PixelRect(1, 2, 0, 4)));
    }

    // ---- Rename ----

    [Fact]
    public void RenameTent_ChangesTitle()
    {
        var layout = Valid(_start.RenameTent(Id(1), "Projects"));

        Assert.Equal("Projects", layout.GetTent(Id(1)).Title);
        Assert.Equal("Tent 1", _start.GetTent(Id(1)).Title);
    }

    [Fact]
    public void RenameTent_AllowsEmptyTitle() => Assert.Equal(string.Empty, _start.RenameTent(Id(1), "").GetTent(Id(1)).Title);

    [Fact]
    public void RenameTent_SameTitle_ReturnsSameInstance() => Assert.Same(_start, _start.RenameTent(Id(1), "Tent 1"));

    [Fact]
    public void RenameTent_NullTitle_Throws() =>
        Assert.Throws<ArgumentNullException>(() => _start.RenameTent(Id(1), null!));

    // ---- Roll up and expand ----

    [Fact]
    public void RollUpAndExpand_ToggleTheFlagAndKeepTheBounds()
    {
        var rolled = Valid(_start.RollUpTent(Id(1)));
        var expanded = Valid(rolled.ExpandTent(Id(1)));

        Assert.True(rolled.GetTent(Id(1)).IsRolledUp);
        Assert.Equal(SomeBounds, rolled.GetTent(Id(1)).Bounds);
        Assert.False(expanded.GetTent(Id(1)).IsRolledUp);
        Assert.Equal(_start, expanded);
    }

    [Fact]
    public void RollUpAndExpand_AreIdempotent()
    {
        var rolled = _start.RollUpTent(Id(1));

        Assert.Same(rolled, rolled.RollUpTent(Id(1)));
        Assert.Same(_start, _start.ExpandTent(Id(1)));
        Assert.Same(rolled, rolled.SetTentRolledUp(Id(1), true));
    }

    [Fact]
    public void RollUp_WorksOnTheDefaultTent() => Assert.True(Valid(_start.RollUpTent(Id(0))).DefaultTent.IsRolledUp);

    // ---- Delete ----

    [Fact]
    public void DeleteTent_MovesItsItemsToTheEndOfTheDefaultTent()
    {
        var layout = Valid(_start.DeleteTent(Id(1)));

        Assert.False(layout.ContainsTent(Id(1)));
        Assert.Equal(["a", "b", "c", "d"], ItemsOf(layout, 0));
        Assert.Equal(_start.ItemCount, layout.ItemCount);
    }

    [Fact]
    public void DeleteTent_EmptyTent_RemovesIt()
    {
        var layout = Valid(_start.DeleteTent(Id(2)));

        Assert.Equal([Id(0), Id(1)], layout.Tents.Select(t => t.Id));
        Assert.Equal(_start.DefaultTent, layout.DefaultTent);
    }

    [Fact]
    public void DeleteTent_DefaultTentNotFirst_StillReceivesTheItems()
    {
        var start = Layout.Create(SingleMonitor, [Tent(1, "c"), Tent(0, "a")], Id(0));

        var layout = Valid(start.DeleteTent(Id(1)));

        Assert.Equal(["a", "c"], ItemsOf(layout, 0));
    }

    [Fact]
    public void DeleteTent_DefaultTent_ThrowsAndLeavesTheLayoutUnchanged()
    {
        var before = LayoutWith(Tent(0, "a", "b"), Tent(1, "c", "d"), Tent(2));

        Assert.Throws<InvalidOperationException>(() => _start.DeleteTent(Id(0)));
        Assert.Equal(before, _start);
    }

    [Fact]
    public void DeleteTent_OnlyTheDefaultTentLeft_Throws() =>
        Assert.Throws<InvalidOperationException>(() => LayoutWith(Tent(0)).DeleteTent(Id(0)));

    // ---- Unknown tent ids ----

    public static TheoryData<string, Func<Layout, Layout>> TentOperations() => new()
    {
        { "move", l => l.MoveTent(Id(9), 0, 0) },
        { "resize", l => l.ResizeTent(Id(9), 10, 10) },
        { "bounds", l => l.SetTentBounds(Id(9), SomeBounds) },
        { "rename", l => l.RenameTent(Id(9), "x") },
        { "rollup", l => l.RollUpTent(Id(9)) },
        { "expand", l => l.ExpandTent(Id(9)) },
        { "delete", l => l.DeleteTent(Id(9)) },
        { "assign", l => l.AssignItem(Item("a"), Id(9)) },
        { "assign-at", l => l.AssignItem(Item("a"), Id(9), 0) },
        { "assign-many", l => l.AssignItems([Item("a")], Id(9)) },
    };

    [Theory]
    [MemberData(nameof(TentOperations))]
    public void UnknownTentId_Throws(string name, Func<Layout, Layout> operation)
    {
        Assert.NotNull(name);
        Assert.Throws<KeyNotFoundException>(() => operation(_start));
    }

    // ---- Assign ----

    [Fact]
    public void AssignItem_MovesTheItemOutOfItsTent()
    {
        var layout = Valid(_start.AssignItem(Item("a"), Id(1)));

        Assert.Equal(["b"], ItemsOf(layout, 0));
        Assert.Equal(["c", "d", "a"], ItemsOf(layout, 1));
        Assert.Equal(Id(1), layout.FindTentOf(Item("a")));
    }

    [Fact]
    public void AssignItem_NeverLeavesAnItemInTwoTents()
    {
        var layout = _start;
        foreach (var target in new[] { 1, 2, 0, 2, 1 })
        {
            layout = Valid(layout.AssignItem(Item("a"), Id(target)));
            Assert.Single(layout.Tents, t => t.Items.Contains(Item("a")));
        }
    }

    [Fact]
    public void AssignItem_MatchesItemsCaseInsensitively()
    {
        var layout = Valid(_start.AssignItem(Item("A"), Id(2)));

        Assert.Equal(["b"], ItemsOf(layout, 0));
        Assert.Single(layout.GetTent(Id(2)).Items);
        Assert.Equal(_start.ItemCount, layout.ItemCount);
    }

    [Fact]
    public void AssignItem_UnknownItem_IsAddedToTheTarget()
    {
        var layout = Valid(_start.AssignItem(Item("new"), Id(2)));

        Assert.Equal(["new"], ItemsOf(layout, 2));
        Assert.Equal(_start.ItemCount + 1, layout.ItemCount);
    }

    [Fact]
    public void AssignItem_AlreadyInTarget_ReturnsSameInstance() => Assert.Same(_start, _start.AssignItem(Item("c"), Id(1)));

    [Fact]
    public void AssignItem_EmptyItemId_Throws() =>
        Assert.Throws<ArgumentException>(() => _start.AssignItem(default, Id(1)));

    [Fact]
    public void AssignItem_UnknownTarget_DoesNotAddTheItem()
    {
        Assert.Throws<KeyNotFoundException>(() => _start.AssignItem(Item("new"), Id(9)));
        Assert.False(_start.ContainsItem(Item("new")));
    }

    [Theory]
    [InlineData(0, new[] { "a", "c", "d" })]
    [InlineData(1, new[] { "c", "a", "d" })]
    [InlineData(2, new[] { "c", "d", "a" })]
    public void AssignItem_AtIndex_InsertsAtThatPosition(int index, string[] expected) =>
        Assert.Equal(expected, ItemsOf(Valid(_start.AssignItem(Item("a"), Id(1), index)), 1));

    [Fact]
    public void AssignItem_AtIndex_ReordersInsideTheSameTent()
    {
        var layout = Valid(_start.AssignItem(Item("d"), Id(1), 0));

        Assert.Equal(["d", "c"], ItemsOf(layout, 1));
        Assert.Same(_start, _start.AssignItem(Item("c"), Id(1), 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void AssignItem_IndexOutOfRange_Throws(int index) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => _start.AssignItem(Item("a"), Id(1), index));

    [Fact]
    public void AssignItem_IndexCountsTheTargetWithoutTheMovedItem()
    {
        // Tent 1 has two items; moving one of them leaves one other, so 2 is out of range but 1 is the end.
        Assert.Throws<ArgumentOutOfRangeException>(() => _start.AssignItem(Item("c"), Id(1), 2));
        Assert.Equal(["d", "c"], ItemsOf(_start.AssignItem(Item("c"), Id(1), 1), 1));
    }

    [Fact]
    public void AssignItems_MovesSeveralItemsInOrderAndIgnoresDuplicates()
    {
        var layout = Valid(_start.AssignItems([Item("d"), Item("a"), Item("D"), Item("new")], Id(2)));

        Assert.Equal(["b"], ItemsOf(layout, 0));
        Assert.Equal(["c"], ItemsOf(layout, 1));
        Assert.Equal(["d", "a", "new"], ItemsOf(layout, 2));
    }

    [Fact]
    public void AssignItems_ItemsAlreadyInTargetKeepTheirPlace()
    {
        var layout = Valid(_start.AssignItems([Item("c"), Item("a")], Id(1)));

        Assert.Equal(["c", "d", "a"], ItemsOf(layout, 1));
    }

    [Fact]
    public void AssignItems_Empty_ReturnsSameInstance() => Assert.Same(_start, _start.AssignItems([], Id(1)));

    // ---- Unassign ----

    [Fact]
    public void UnassignItem_MovesTheItemToTheEndOfTheDefaultTent()
    {
        var layout = Valid(_start.UnassignItem(Item("c")));

        Assert.Equal(["a", "b", "c"], ItemsOf(layout, 0));
        Assert.Equal(["d"], ItemsOf(layout, 1));
    }

    [Fact]
    public void UnassignItem_AlreadyInDefault_ReturnsSameInstance() => Assert.Same(_start, _start.UnassignItem(Item("a")));

    [Fact]
    public void UnassignItem_UnknownItem_IsAddedToTheDefaultTent() =>
        Assert.Equal(["a", "b", "new"], ItemsOf(Valid(_start.UnassignItem(Item("new"))), 0));

    // ---- Add and remove ----

    [Fact]
    public void AddItems_PutsNewItemsInTheDefaultTentAndLeavesKnownOnes()
    {
        var layout = Valid(_start.AddItems([Item("x"), Item("c"), Item("y"), Item("X")]));

        Assert.Equal(["a", "b", "x", "y"], ItemsOf(layout, 0));
        Assert.Equal(["c", "d"], ItemsOf(layout, 1));
    }

    [Fact]
    public void AddItems_NothingNew_ReturnsSameInstance() => Assert.Same(_start, _start.AddItems([Item("a"), Item("d")]));

    [Fact]
    public void AddItems_EmptyItemId_Throws() => Assert.Throws<ArgumentException>(() => _start.AddItems([default]));

    [Fact]
    public void RemoveItem_RemovesItFromTheLayout()
    {
        var layout = Valid(_start.RemoveItem(Item("c")));

        Assert.Equal(["d"], ItemsOf(layout, 1));
        Assert.False(layout.ContainsItem(Item("c")));
        Assert.Equal(_start.ItemCount - 1, layout.ItemCount);
    }

    [Fact]
    public void RemoveItem_UnknownItem_ReturnsSameInstance()
    {
        Assert.Same(_start, _start.RemoveItem(Item("missing")));
        Assert.Same(_start, _start.RemoveItem(default));
    }

    [Fact]
    public void RemoveItems_RemovesFromSeveralTents()
    {
        var layout = Valid(_start.RemoveItems([Item("b"), Item("d"), Item("missing")]));

        Assert.Equal(["a"], ItemsOf(layout, 0));
        Assert.Equal(["c"], ItemsOf(layout, 1));
        Assert.Same(_start.GetTent(Id(2)), layout.GetTent(Id(2)));
    }

    // ---- Reconcile ----

    [Fact]
    public void ReconcileItems_RemovesMissingAndAddsNewItems()
    {
        var layout = Valid(_start.ReconcileItems([Item("new2"), Item("d"), Item("a"), Item("new1")]));

        Assert.Equal(["a", "new2", "new1"], ItemsOf(layout, 0));
        Assert.Equal(["d"], ItemsOf(layout, 1));
    }

    [Fact]
    public void ReconcileItems_NoChange_ReturnsSameInstance() =>
        Assert.Same(_start, _start.ReconcileItems([Item("d"), Item("c"), Item("b"), Item("a")]));

    [Fact]
    public void ReconcileItems_NothingPresent_EmptiesEveryTentButKeepsThem()
    {
        var layout = Valid(_start.ReconcileItems([]));

        Assert.Equal(0, layout.ItemCount);
        Assert.Equal(3, layout.Tents.Length);
    }

    // ---- Purity ----

    [Fact]
    public void Operations_AreDeterministicAndDoNotChangeTheirInput()
    {
        static Layout Run(Layout start) => start
            .CreateTent(Id(3), "Docs", new PixelRect(0, 0, 200, 200))
            .AssignItems([Item("a"), Item("c")], Id(3))
            .MoveTent(Id(3), 40, 40)
            .RollUpTent(Id(3))
            .DeleteTent(Id(1))
            .AddItems([Item("z")])
            .RemoveItem(Item("b"));

        var before = LayoutWith(Tent(0, "a", "b"), Tent(1, "c", "d"), Tent(2));

        var first = Valid(Run(_start));
        var second = Valid(Run(_start));

        Assert.Equal(first, second);
        Assert.Equal(before, _start);
        Assert.Equal(["d", "z"], ItemsOf(first, 0));
        Assert.Equal(["a", "c"], ItemsOf(first, 3));
    }
}
