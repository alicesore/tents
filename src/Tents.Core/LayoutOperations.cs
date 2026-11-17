using System.Collections.Immutable;

namespace Tents.Core;

/// <summary>
/// The operations on a <see cref="Layout"/>. Every operation is pure and deterministic: it returns a new layout
/// and never changes its input, and the same inputs always give the same result.
/// </summary>
/// <remarks>
/// <para>Rules shared by all operations:</para>
/// <list type="bullet">
/// <item>An operation that changes nothing returns the <em>same instance</em>, so callers can skip a save with a
/// reference comparison.</item>
/// <item>An unknown tent id throws <see cref="KeyNotFoundException"/>. Tent ids come from the UI, so an unknown
/// one is a bug or a stale reference, not something to ignore.</item>
/// <item>Item ids are handled leniently, because the item list comes from the shell and can race with the layout:
/// assigning an unknown item adds it, and removing an unknown item does nothing.</item>
/// <item>An item is never in two tents. Assigning an item moves it out of the tent it was in.</item>
/// <item>Items that are not assigned to another tent, and the items of a deleted tent, go to the default tent.
/// The default tent cannot be deleted.</item>
/// </list>
/// </remarks>
public static class LayoutOperations
{
    // ---- Tents ----

    /// <summary>Adds an empty, expanded tent after the existing tents.</summary>
    /// <exception cref="ArgumentException">The id is empty or already used, or the bounds are empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="title"/> is null.</exception>
    public static Layout CreateTent(this Layout layout, TentId id, string title, PixelRect bounds)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var tent = new Tent(id, title, bounds);
        if (layout.ContainsTent(id))
        {
            throw new ArgumentException($"Tent {id} already exists.", nameof(id));
        }

        return layout.WithTentsUnchecked(layout.Tents.Add(tent));
    }

    /// <summary>Moves a tent's top-left corner to (<paramref name="x"/>, <paramref name="y"/>). Size is unchanged.</summary>
    /// <exception cref="KeyNotFoundException">No tent has this id.</exception>
    public static Layout MoveTent(this Layout layout, TentId id, int x, int y)
    {
        var index = RequireIndex(layout, id);
        return WithBounds(layout, index, layout.Tents[index].Bounds.WithPosition(x, y));
    }

    /// <summary>Changes a tent's expanded size. The top-left corner is unchanged.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The width or height is zero or negative.</exception>
    /// <exception cref="KeyNotFoundException">No tent has this id.</exception>
    public static Layout ResizeTent(this Layout layout, TentId id, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        var index = RequireIndex(layout, id);
        return WithBounds(layout, index, layout.Tents[index].Bounds.WithSize(width, height));
    }

    /// <summary>Moves and resizes a tent in one step, for example when the user drags its top or left edge.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The bounds do not have a positive size.</exception>
    /// <exception cref="KeyNotFoundException">No tent has this id.</exception>
    public static Layout SetTentBounds(this Layout layout, TentId id, PixelRect bounds)
    {
        if (!bounds.HasPositiveSize)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), bounds, "A tent's width and height must be greater than zero.");
        }

        var index = RequireIndex(layout, id);
        return WithBounds(layout, index, bounds);
    }

    /// <summary>Changes a tent's title. Any string is allowed, including an empty one.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="title"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">No tent has this id.</exception>
    public static Layout RenameTent(this Layout layout, TentId id, string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var index = RequireIndex(layout, id);
        var tent = layout.Tents[index];
        return string.Equals(tent.Title, title, StringComparison.Ordinal)
            ? layout
            : layout.WithTentsUnchecked(layout.Tents.SetItem(index, tent with { Title = title }));
    }

    /// <summary>Collapses a tent to its title bar. The expanded size is kept.</summary>
    /// <exception cref="KeyNotFoundException">No tent has this id.</exception>
    public static Layout RollUpTent(this Layout layout, TentId id) => SetTentRolledUp(layout, id, true);

    /// <summary>Expands a rolled-up tent back to its stored size.</summary>
    /// <exception cref="KeyNotFoundException">No tent has this id.</exception>
    public static Layout ExpandTent(this Layout layout, TentId id) => SetTentRolledUp(layout, id, false);

    /// <exception cref="KeyNotFoundException">No tent has this id.</exception>
    public static Layout SetTentRolledUp(this Layout layout, TentId id, bool rolledUp)
    {
        var index = RequireIndex(layout, id);
        var tent = layout.Tents[index];
        return tent.IsRolledUp == rolledUp
            ? layout
            : layout.WithTentsUnchecked(layout.Tents.SetItem(index, tent with { IsRolledUp = rolledUp }));
    }

    /// <summary>
    /// Deletes a tent. Its items are appended to the default tent, in their order. The files themselves are never
    /// touched: only tent membership changes.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tent is the default tent.</exception>
    /// <exception cref="KeyNotFoundException">No tent has this id.</exception>
    public static Layout DeleteTent(this Layout layout, TentId id)
    {
        var index = RequireIndex(layout, id);
        if (id == layout.DefaultTentId)
        {
            throw new InvalidOperationException("The default tent cannot be deleted.");
        }

        var removed = layout.Tents[index];
        var tents = layout.Tents.RemoveAt(index);
        if (!removed.Items.IsEmpty)
        {
            var defaultIndex = IndexOf(tents, layout.DefaultTentId);
            var defaultTent = tents[defaultIndex];
            tents = tents.SetItem(defaultIndex, defaultTent with { Items = defaultTent.Items.AddRange(removed.Items) });
        }

        return layout.WithTentsUnchecked(tents);
    }

    // ---- Items ----

    /// <summary>
    /// Puts an item at the end of a tent, moving it out of the tent it was in. An item that is not in the layout
    /// yet is added. When the item is already in the target tent, nothing changes.
    /// </summary>
    /// <exception cref="ArgumentException">The item id is empty.</exception>
    /// <exception cref="KeyNotFoundException">No tent has the target id.</exception>
    public static Layout AssignItem(this Layout layout, ItemId item, TentId target) =>
        AssignItemsCore(layout, [RequireItem(item)], target, index: null);

    /// <summary>
    /// Puts an item into a tent at <paramref name="index"/>, moving it out of the tent it was in. The index is the
    /// item's position in the target tent afterwards, so this also reorders an item inside its own tent.
    /// </summary>
    /// <exception cref="ArgumentException">The item id is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The index is negative, or greater than the number of other
    /// items in the target tent.</exception>
    /// <exception cref="KeyNotFoundException">No tent has the target id.</exception>
    public static Layout AssignItem(this Layout layout, ItemId item, TentId target, int index) =>
        AssignItemsCore(layout, [RequireItem(item)], target, index);

    /// <summary>
    /// Puts several items at the end of a tent, in the given order, moving each out of the tent it was in. Duplicates
    /// in <paramref name="items"/> are ignored after the first. Items already in the target tent keep their place.
    /// </summary>
    /// <exception cref="ArgumentException">An item id is empty.</exception>
    /// <exception cref="KeyNotFoundException">No tent has the target id.</exception>
    public static Layout AssignItems(this Layout layout, IEnumerable<ItemId> items, TentId target) =>
        AssignItemsCore(layout, Distinct(items), target, index: null);

    /// <summary>
    /// Takes an item out of its tent and puts it at the end of the default tent. An item that is not in the layout
    /// yet is added to the default tent. Nothing changes when the item is already in the default tent.
    /// </summary>
    /// <exception cref="ArgumentException">The item id is empty.</exception>
    public static Layout UnassignItem(this Layout layout, ItemId item)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return AssignItemsCore(layout, [RequireItem(item)], layout.DefaultTentId, index: null);
    }

    /// <summary>
    /// Adds items that are new to the layout at the end of the default tent, in the given order. Items already in
    /// the layout are left where they are.
    /// </summary>
    /// <exception cref="ArgumentException">An item id is empty.</exception>
    public static Layout AddItems(this Layout layout, IEnumerable<ItemId> items)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var known = CollectItems(layout);
        var added = ImmutableArray.CreateBuilder<ItemId>();
        foreach (var item in Distinct(items))
        {
            if (!known.Contains(item))
            {
                added.Add(item);
            }
        }

        return AppendToDefault(layout, layout.Tents, added.ToImmutable());
    }

    /// <summary>
    /// Removes an item from the layout entirely, for example because it no longer exists on the desktop. Unknown
    /// items are ignored.
    /// </summary>
    public static Layout RemoveItem(this Layout layout, ItemId item) => RemoveItems(layout, [item]);

    /// <summary>Removes items from the layout entirely. Unknown items are ignored.</summary>
    public static Layout RemoveItems(this Layout layout, IEnumerable<ItemId> items)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(items);
        var removing = new HashSet<ItemId>(items);
        removing.Remove(default);
        if (removing.Count == 0)
        {
            return layout;
        }

        var tents = RemoveFromTents(layout.Tents, removing.Contains, out var changed);
        return changed ? layout.WithTentsUnchecked(tents) : layout;
    }

    /// <summary>
    /// Makes the layout match the items that exist on the desktop: items that are no longer present are removed,
    /// and present items that are new to the layout are added to the end of the default tent, in the given order.
    /// Items that stay keep their tent and position.
    /// </summary>
    /// <exception cref="ArgumentException">An item id is empty.</exception>
    public static Layout ReconcileItems(this Layout layout, IEnumerable<ItemId> presentItems)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var present = Distinct(presentItems);
        var presentSet = new HashSet<ItemId>(present);
        var tents = RemoveFromTents(layout.Tents, item => !presentSet.Contains(item), out _);

        var known = new HashSet<ItemId>();
        foreach (var tent in tents)
        {
            known.UnionWith(tent.Items);
        }

        var added = ImmutableArray.CreateBuilder<ItemId>();
        foreach (var item in present)
        {
            if (!known.Contains(item))
            {
                added.Add(item);
            }
        }

        return AppendToDefault(layout, tents, added.ToImmutable());
    }

    // ---- Helpers ----

    private static Layout AssignItemsCore(Layout layout, ImmutableArray<ItemId> moving, TentId target, int? index)
    {
        var targetIndex = RequireIndex(layout, target);
        if (moving.IsEmpty)
        {
            return layout;
        }

        var targetTent = layout.Tents[targetIndex];
        if (index is null && moving.All(targetTent.Items.Contains))
        {
            // Appending items that are already in the target tent keeps them where they are.
            return layout;
        }

        var movingSet = new HashSet<ItemId>(moving);
        var tents = RemoveFromTents(layout.Tents, movingSet.Contains, out _);
        var remaining = tents[targetIndex].Items;

        ImmutableArray<ItemId> targetItems;
        if (index is { } at)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(at, nameof(index));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(at, remaining.Length, nameof(index));
            targetItems = remaining.InsertRange(at, moving);
        }
        else
        {
            // Items already in the target keep their place; only the others are appended.
            var appended = moving.RemoveAll(targetTent.Items.Contains);
            targetItems = targetTent.Items.AddRange(appended);
        }

        if (targetItems.AsSpan().SequenceEqual(targetTent.Items.AsSpan()) && OnlyTargetChanged(layout.Tents, tents, targetIndex))
        {
            return layout;
        }

        return layout.WithTentsUnchecked(tents.SetItem(targetIndex, targetTent with { Items = targetItems }));
    }

    private static bool OnlyTargetChanged(ImmutableArray<Tent> before, ImmutableArray<Tent> after, int targetIndex)
    {
        for (var i = 0; i < before.Length; i++)
        {
            if (i != targetIndex && !ReferenceEquals(before[i], after[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Removes matching items from every tent. Tents with no matching item keep their instance.</summary>
    private static ImmutableArray<Tent> RemoveFromTents(ImmutableArray<Tent> tents, Predicate<ItemId> match, out bool changed)
    {
        changed = false;
        ImmutableArray<Tent>.Builder? builder = null;
        for (var i = 0; i < tents.Length; i++)
        {
            var tent = tents[i];
            if (!tent.Items.Any(item => match(item)))
            {
                continue;
            }

            builder ??= tents.ToBuilder();
            builder[i] = tent with { Items = tent.Items.RemoveAll(match) };
            changed = true;
        }

        return builder is null ? tents : builder.MoveToImmutable();
    }

    private static Layout AppendToDefault(Layout layout, ImmutableArray<Tent> tents, ImmutableArray<ItemId> added)
    {
        if (!added.IsEmpty)
        {
            var defaultIndex = IndexOf(tents, layout.DefaultTentId);
            var defaultTent = tents[defaultIndex];
            tents = tents.SetItem(defaultIndex, defaultTent with { Items = defaultTent.Items.AddRange(added) });
        }

        return tents == layout.Tents ? layout : layout.WithTentsUnchecked(tents);
    }

    private static HashSet<ItemId> CollectItems(Layout layout)
    {
        var items = new HashSet<ItemId>(layout.ItemCount);
        foreach (var tent in layout.Tents)
        {
            items.UnionWith(tent.Items);
        }

        return items;
    }

    private static ImmutableArray<ItemId> Distinct(IEnumerable<ItemId> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var seen = new HashSet<ItemId>();
        var result = ImmutableArray.CreateBuilder<ItemId>();
        foreach (var item in items)
        {
            if (seen.Add(RequireItem(item)))
            {
                result.Add(item);
            }
        }

        return result.ToImmutable();
    }

    private static ItemId RequireItem(ItemId item) =>
        item.IsEmpty ? throw new ArgumentException("An item id cannot be empty.", nameof(item)) : item;

    private static Layout WithBounds(Layout layout, int index, PixelRect bounds)
    {
        var tent = layout.Tents[index];
        return tent.Bounds == bounds
            ? layout
            : layout.WithTentsUnchecked(layout.Tents.SetItem(index, tent with { Bounds = bounds }));
    }

    private static int RequireIndex(Layout layout, TentId id)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var index = layout.IndexOf(id);
        return index >= 0 ? index : throw new KeyNotFoundException($"Tent {id} is not in the layout.");
    }

    private static int IndexOf(ImmutableArray<Tent> tents, TentId id)
    {
        for (var i = 0; i < tents.Length; i++)
        {
            if (tents[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }
}
