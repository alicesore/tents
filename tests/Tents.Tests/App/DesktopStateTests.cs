using Tents.App.Items;
using Tents.Core;
using Tents.Shell.Desktop;
using Tents.Shell.Interop;

namespace Tents.Tests.App;

/// <summary>Tent membership rules for reads, watcher batches and drops. Synthetic items only.</summary>
public sealed class DesktopStateTests
{
    private static readonly MonitorConfigKey _monitors = MonitorConfigKey.Create(new MonitorDescriptor("D1", new PixelRect(0, 0, 1920, 1080)));
    private static readonly TentId _default = TentId.New();
    private static readonly TentId _work = TentId.New();

    [Fact]
    public void FirstRead_PutsEveryItemInTheDefaultTent_InViewOrder()
    {
        var state = Empty().WithSnapshot(Snapshot("a", "b", "c"), out var ignored);

        Assert.False(ignored);
        Assert.Equal(Ids("a", "b", "c"), state.Layout.DefaultTent.Items);
        Assert.Equal(["a", "b", "c"], state.ItemsOf(state.Layout.DefaultTent).Select(i => i.DisplayName));
    }

    [Fact]
    public void Read_RemovesGoneItems_AndKeepsAssignedOnesInTheirTent()
    {
        var state = Empty().WithSnapshot(Snapshot("a", "b", "c"), out _);
        state = state.WithLayout(state.Layout.AssignItem(Id("b"), _work));

        state = state.WithSnapshot(Snapshot("b", "c", "d"), out _);

        Assert.Equal(Ids("c", "d"), state.Layout.DefaultTent.Items);
        Assert.Equal(Ids("b"), state.Layout.GetTent(_work).Items);
        Assert.False(state.Items.ContainsKey(Id("a")));
    }

    [Fact]
    public void AnEmptyRead_IsIgnoredOnce_SoTentsKeepTheirItems()
    {
        var state = Empty().WithSnapshot(Snapshot("a", "b"), out _);
        state = state.WithLayout(state.Layout.AssignItem(Id("b"), _work));

        var afterEmpty = state.WithSnapshot(DesktopSnapshot.Empty, out var ignored);

        Assert.True(ignored);
        Assert.Equal(Ids("b"), afterEmpty.Layout.GetTent(_work).Items);
        Assert.Equal(2, afterEmpty.Items.Count);

        // The view was only busy: the next read is normal again.
        var recovered = afterEmpty.WithSnapshot(Snapshot("a", "b"), out ignored);
        Assert.False(ignored);
        Assert.Equal(Ids("b"), recovered.Layout.GetTent(_work).Items);
    }

    [Fact]
    public void TwoEmptyReadsInARow_AreTrusted()
    {
        var state = Empty().WithSnapshot(Snapshot("a"), out _);

        state = state.WithSnapshot(DesktopSnapshot.Empty, out var first);
        state = state.WithSnapshot(DesktopSnapshot.Empty, out var second);

        Assert.True(first);
        Assert.False(second);
        Assert.Empty(state.Items);
        Assert.Equal(0, state.Layout.ItemCount);
    }

    [Fact]
    public void AnEmptyFirstRead_IsAcceptedWhenNothingWasKnown()
    {
        Empty().WithSnapshot(DesktopSnapshot.Empty, out var ignored);

        Assert.False(ignored);
    }

    [Fact]
    public void Batch_RemovesDeletedItemsAtOnce()
    {
        var state = Empty().WithSnapshot(Snapshot("a", "b"), out _);

        state = state.WithBatch(Batch(removed: ["a"]), []);

        Assert.Equal(Ids("b"), state.Layout.DefaultTent.Items);
        Assert.False(state.Items.ContainsKey(Id("a")));
    }

    [Fact]
    public void Batch_AddsNewItemsToTheDefaultTent()
    {
        var state = Empty().WithSnapshot(Snapshot("a"), out _);

        state = state.WithBatch(Batch(added: ["n"]), [Item("n")]);

        Assert.Equal(Ids("a", "n"), state.Layout.DefaultTent.Items);
        Assert.True(state.Items.ContainsKey(Id("n")));
    }

    [Fact]
    public void Batch_RenameKeepsTheTentAndPosition()
    {
        var state = Empty().WithSnapshot(Snapshot("a", "b", "c"), out _);
        state = state.WithLayout(state.Layout.AssignItems(Ids("a", "b", "c"), _work));

        state = state.WithBatch(Batch(renamed: [("b", "b2")]), [Item("b2")]);

        Assert.Equal(Ids("a", "b2", "c"), state.Layout.GetTent(_work).Items);
        Assert.False(state.Items.ContainsKey(Id("b")));
        Assert.Equal("b2", state.Items[Id("b2")].DisplayName);
    }

    [Fact]
    public void Batch_RenameWithoutAResolvedItem_StillMovesTheEntry()
    {
        var state = Empty().WithSnapshot(Snapshot("a.txt"), out _);

        state = state.WithBatch(Batch(renamed: [("a.txt", "z.txt")]), []);

        Assert.Equal(Ids("z.txt"), state.Layout.DefaultTent.Items);
        Assert.Equal("z.txt", state.Items[Id("z.txt")].DisplayName);
    }

    [Fact]
    public void ExpectedItems_JoinTheTentTheyWereDroppedOn()
    {
        var state = Empty().WithSnapshot(Snapshot("a"), out _).ExpectItems(Ids("dropped"), _work);

        state = state.WithBatch(Batch(added: ["dropped"]), [Item("dropped")]);

        Assert.Equal(Ids("dropped"), state.Layout.GetTent(_work).Items);
        Assert.Empty(state.Expected);
    }

    [Fact]
    public void ExpectedItems_AlsoWorkWhenOnlyTheReadSeesThem()
    {
        var state = Empty().WithSnapshot(Snapshot("a"), out _).ExpectItems(Ids("dropped"), _work);

        state = state.WithSnapshot(Snapshot("a", "dropped"), out _);

        Assert.Equal(Ids("dropped"), state.Layout.GetTent(_work).Items);
    }

    [Fact]
    public void ExpectedItems_ExpireAfterAFewReads()
    {
        var state = Empty().WithSnapshot(Snapshot("a"), out _).ExpectItems(Ids("never"), _work);

        for (var i = 0; i < DesktopState.ExpectationLifetime; i++)
        {
            state = state.WithSnapshot(Snapshot("a"), out _);
        }

        Assert.Empty(state.Expected);
        state = state.WithSnapshot(Snapshot("a", "never"), out _);
        Assert.Equal(Ids("a", "never"), state.Layout.DefaultTent.Items);
    }

    [Fact]
    public void ExpectedItems_ForADeletedTent_GoToTheDefaultTent()
    {
        var state = Empty().WithSnapshot(Snapshot("a"), out _).ExpectItems(Ids("dropped"), _work);
        state = state.WithLayout(state.Layout.DeleteTent(_work));

        state = state.WithBatch(Batch(added: ["dropped"]), [Item("dropped")]);

        Assert.Equal(Ids("a", "dropped"), state.Layout.DefaultTent.Items);
    }

    [Fact]
    public void Reassign_MovesItemsBetweenTents_AtTheDropPosition()
    {
        var state = Empty().WithSnapshot(Snapshot("a", "b", "c", "x", "y"), out _);
        state = state.WithLayout(state.Layout.AssignItems(Ids("x", "y"), _work));

        state = state.Reassign(Ids("a", "c"), _work, insertionIndex: 1); // between x and y

        Assert.Equal(Ids("x", "a", "c", "y"), state.Layout.GetTent(_work).Items);
        Assert.Equal(Ids("b"), state.Layout.DefaultTent.Items);
    }

    [Fact]
    public void Reassign_ReordersInsideATent()
    {
        var state = Empty().WithSnapshot(Snapshot("a", "b", "c", "d"), out _);

        // Drag "a" to just before "d" (index 3 of the current list, which still contains "a").
        state = state.Reassign(Ids("a"), _default, insertionIndex: 3);

        Assert.Equal(Ids("b", "c", "a", "d"), state.Layout.DefaultTent.Items);
    }

    [Fact]
    public void Reassign_ToTheEnd_AndIgnoresUnknownItems()
    {
        var state = Empty().WithSnapshot(Snapshot("a", "b"), out _);

        state = state.Reassign([Id("a"), Id("not-on-desktop")], _work, insertionIndex: 99);

        Assert.Equal(Ids("a"), state.Layout.GetTent(_work).Items);
        Assert.False(state.Layout.ContainsItem(Id("not-on-desktop")));
    }

    [Fact]
    public void ExpectedDesktopIds_AreTheFileNamesInTheDesktopFolder()
    {
        var ids = DesktopFeed.ExpectedIds([new ItemId(@"D:\Downloads\report.pdf"), new ItemId(@"D:\Photos\"), new ItemId("::{645FF040-5081-101B-9F08-00AA002F954E}")], @"C:\Users\u\Desktop");

        Assert.Equal([new ItemId(@"C:\Users\u\Desktop\report.pdf"), new ItemId(@"C:\Users\u\Desktop\Photos")], ids);
    }

    private static DesktopState Empty() =>
        DesktopState.Create(Layout.CreateWithDefaultTent(_monitors, new Tent(_default, "Desktop", new PixelRect(0, 0, 300, 300)))
            .CreateTent(_work, "Work", new PixelRect(400, 0, 300, 300)));

    private static ItemId Id(string name) => new(name);

    private static ItemId[] Ids(params string[] names) => [.. names.Select(Id)];

    private static DesktopItem Item(string name) => new(Id(name), name, true, false, ShellIdList.Empty);

    private static DesktopSnapshot Snapshot(params string[] names) => new(names.Select(Item));

    private static DesktopChangeBatch Batch(string[]? added = null, string[]? removed = null, (string Old, string New)[]? renamed = null) =>
        new(
            Ids(added ?? []),
            Ids(removed ?? []),
            [.. (renamed ?? []).Select(r => (Id(r.Old), Id(r.New)))],
            [],
            false);
}
