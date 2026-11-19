using System.Collections.Immutable;
using Tents.Core;
using Tents.Shell.Desktop;

namespace Tents.App.Items;

/// <summary>
/// The layout together with the desktop items it shows. Pure and immutable: every change returns a new state, so the
/// rules (what a re-read, a watcher batch or a drop does to tent membership) are unit-tested without a desktop.
/// </summary>
/// <remarks>
/// <para><b>Two sources.</b> A full read of the native view (<see cref="WithSnapshot"/>) is the reference for which items
/// exist. Watcher batches (<see cref="WithBatch"/>) arrive about 1.3 s earlier and are applied at once, so a new or
/// deleted file shows in its tent immediately; the next full read corrects anything the watcher saw differently.</para>
/// <para><b>An empty read is not trusted</b> when items were known: the native view lists 0 items for about 60 ms after
/// the icon switch flips, while Explorer restarts, and possibly early at sign-in. Reconciling from such a read would throw
/// every item out of its tent, and with a restored layout that loss would be saved. Such a read is ignored.</para>
/// <para><b>Drops from outside</b> (Explorer onto a tent) put files on the desktop through the shell; the new desktop
/// items should land in the tent they were dropped on. <see cref="ExpectItems"/> records the expected ids; they go to
/// that tent when they appear, instead of the default tent.</para>
/// </remarks>
internal sealed class DesktopState
{
    /// <summary>
    /// How many full reads an expected drop survives without its item appearing (a renamed copy, a cancelled
    /// operation). Counted in reads, not time, so nothing needs a timer.
    /// </summary>
    public const int ExpectationLifetime = 3;

    private DesktopState(Layout layout, ImmutableDictionary<ItemId, DesktopItem> items, ImmutableDictionary<ItemId, Expectation> expected, bool lastReadWasEmpty = false)
    {
        Layout = layout;
        Items = items;
        Expected = expected;
        LastReadWasEmpty = lastReadWasEmpty;
    }

    public Layout Layout { get; }

    /// <summary>The desktop items currently known, by id.</summary>
    public ImmutableDictionary<ItemId, DesktopItem> Items { get; }

    /// <summary>The last full read was empty and was ignored.</summary>
    public bool LastReadWasEmpty { get; }

    /// <summary>Ids expected to appear after a drop, with the tent they belong in.</summary>
    internal ImmutableDictionary<ItemId, Expectation> Expected { get; }

    public static DesktopState Create(Layout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return new DesktopState(layout, ImmutableDictionary<ItemId, DesktopItem>.Empty, ImmutableDictionary<ItemId, Expectation>.Empty);
    }

    /// <summary>The items of one tent, in display order, that are on the desktop.</summary>
    public IReadOnlyList<DesktopItem> ItemsOf(Tent tent)
    {
        ArgumentNullException.ThrowIfNull(tent);
        var list = new List<DesktopItem>(tent.Items.Length);
        foreach (var id in tent.Items)
        {
            if (Items.TryGetValue(id, out var item))
            {
                list.Add(item);
            }
        }

        return list;
    }

    public DesktopState WithLayout(Layout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return ReferenceEquals(layout, Layout) ? this : new DesktopState(layout, Items, Expected, LastReadWasEmpty);
    }

    /// <summary>
    /// Applies a full read of the native view. An empty read while items are known (on the desktop or in a restored
    /// layout) is ignored (see remarks). It is trusted only when the read before it was empty too and the desktop folders
    /// have no visible entries (<paramref name="foldersHaveVisibleEntries"/>), so a desktop that really became empty is
    /// shown as empty.
    /// </summary>
    public DesktopState WithSnapshot(DesktopSnapshot snapshot, out bool ignored, bool foldersHaveVisibleEntries = false)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var known = !Items.IsEmpty || Layout.ItemCount > 0;
        ignored = snapshot.Count == 0 && known && (!LastReadWasEmpty || foldersHaveVisibleEntries);
        if (ignored)
        {
            return new DesktopState(Layout, Items, Expected, lastReadWasEmpty: true);
        }

        var items = ImmutableDictionary.CreateRange(snapshot.Items.Select(i => KeyValuePair.Create(i.Id, i)));
        var layout = PlaceExpected(Layout, snapshot.Items.Select(i => i.Id), Expected, out var expected);
        layout = layout.ReconcileItems(snapshot.Items.Select(i => i.Id));

        // Expectations that were not met by this read age by one.
        expected = expected
            .Where(e => e.Value.ReadsLeft > 1)
            .ToImmutableDictionary(e => e.Key, e => e.Value with { ReadsLeft = e.Value.ReadsLeft - 1 });
        return new DesktopState(layout, items, expected);
    }

    /// <summary>
    /// Applies a watcher batch: removed items leave their tents, renamed items keep their tent and position, and
    /// <paramref name="added"/> (the batch's new paths, resolved to items) join an expected tent or the default tent.
    /// </summary>
    public DesktopState WithBatch(DesktopChangeBatch batch, IReadOnlyList<DesktopItem> added)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(added);
        var items = Items.ToBuilder();
        var layout = Layout;

        foreach (var (oldId, newId) in batch.Renamed)
        {
            if (items.Remove(oldId, out var old))
            {
                items[newId] = old with { Id = newId, DisplayName = Path.GetFileName(newId.Value) is { Length: > 0 } name ? DisplayNameFor(old, name) : old.DisplayName };
            }

            layout = RenameItem(layout, oldId, newId);
        }

        foreach (var id in batch.Removed)
        {
            items.Remove(id);
        }

        layout = layout.RemoveItems(batch.Removed);

        var expected = Expected;
        foreach (var item in added)
        {
            items[item.Id] = item;
        }

        layout = PlaceExpected(layout, added.Select(i => i.Id), expected, out expected);
        layout = layout.AddItems(added.Select(i => i.Id));
        return new DesktopState(layout, items.ToImmutable(), expected);
    }

    /// <summary>Records that these ids should join <paramref name="tent"/> when they appear (a drop from outside).</summary>
    public DesktopState ExpectItems(IEnumerable<ItemId> ids, TentId tent)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var expected = Expected.ToBuilder();
        foreach (var id in ids.Where(i => !i.IsEmpty))
        {
            expected[id] = new Expectation(tent, ExpectationLifetime);
        }

        return new DesktopState(Layout, Items, expected.ToImmutable(), LastReadWasEmpty);
    }

    /// <summary>
    /// Moves items into a tent, inserted at <paramref name="insertionIndex"/> of that tent's current item list (as the
    /// item view computes it from the drop point, counting the moving items too). Items already in the tent are
    /// reordered. Only membership changes; no file is touched.
    /// </summary>
    public DesktopState Reassign(IReadOnlyList<ItemId> moving, TentId tent, int insertionIndex)
    {
        ArgumentNullException.ThrowIfNull(moving);
        var target = Layout.GetTent(tent);
        var movingSet = moving.Where(id => !id.IsEmpty && Items.ContainsKey(id)).ToHashSet();
        if (movingSet.Count == 0)
        {
            return this;
        }

        // The index among the items that stay, which is where the first moving item ends up.
        var at = 0;
        for (var i = 0; i < Math.Min(insertionIndex, target.Items.Length); i++)
        {
            if (!movingSet.Contains(target.Items[i]))
            {
                at++;
            }
        }

        var layout = Layout;
        foreach (var id in moving.Where(movingSet.Contains).Distinct())
        {
            layout = layout.AssignItem(id, tent, at++);
        }

        return WithLayout(layout);
    }

    private static Layout PlaceExpected(Layout layout, IEnumerable<ItemId> present, ImmutableDictionary<ItemId, Expectation> expected, out ImmutableDictionary<ItemId, Expectation> remaining)
    {
        remaining = expected;
        foreach (var id in present)
        {
            if (remaining.TryGetValue(id, out var expectation))
            {
                remaining = remaining.Remove(id);
                if (layout.ContainsTent(expectation.Tent))
                {
                    layout = layout.AssignItem(id, expectation.Tent);
                }
            }
        }

        return layout;
    }

    private static Layout RenameItem(Layout layout, ItemId oldId, ItemId newId)
    {
        if (layout.FindTentOf(oldId) is not { } tentId)
        {
            return layout;
        }

        var index = layout.GetTent(tentId).Items.IndexOf(oldId);
        layout = layout.RemoveItem(oldId);
        if (layout.ContainsItem(newId))
        {
            return layout;
        }

        return layout.AssignItem(newId, tentId, Math.Min(index, layout.GetTent(tentId).Items.Length));
    }

    // A renamed file shows its new name until the next read gives the shell's display name (which may hide the extension).
    private static string DisplayNameFor(DesktopItem old, string newFileName)
    {
        var oldFileName = Path.GetFileName(old.Id.Value);
        var extensionHidden = !string.Equals(old.DisplayName, oldFileName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(old.DisplayName, Path.GetFileNameWithoutExtension(oldFileName), StringComparison.OrdinalIgnoreCase);
        return extensionHidden ? Path.GetFileNameWithoutExtension(newFileName) : newFileName;
    }

    internal readonly record struct Expectation(TentId Tent, int ReadsLeft);
}
