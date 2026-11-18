using Tents.Core;

namespace Tents.Shell.Desktop;

/// <summary>
/// The desktop's items at one moment, in the native view's order, with at most one item per <see cref="ItemId"/>.
/// Immutable, so it can be handed from the shell thread to a UI thread.
/// </summary>
public sealed class DesktopSnapshot
{
    private readonly Dictionary<ItemId, DesktopItem> _byId;

    /// <summary>Builds a snapshot. A repeated id keeps its first item and is counted in <see cref="DuplicateCount"/>.</summary>
    public DesktopSnapshot(IEnumerable<DesktopItem> items, int skippedCount = 0)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfNegative(skippedCount);
        var list = new List<DesktopItem>();
        _byId = [];
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (item.Id.IsEmpty)
            {
                throw new ArgumentException("A desktop item needs an id.", nameof(items));
            }

            if (_byId.TryAdd(item.Id, item))
            {
                list.Add(item);
            }
            else
            {
                DuplicateCount++;
            }
        }

        Items = list;
        SkippedCount = skippedCount;
    }

    public static DesktopSnapshot Empty { get; } = new([]);

    public IReadOnlyList<DesktopItem> Items { get; }

    public int Count => Items.Count;

    /// <summary>Items that shared an id with an earlier item and were dropped.</summary>
    public int DuplicateCount { get; }

    /// <summary>Items the native view listed but that had no usable identity (no parsing name).</summary>
    public int SkippedCount { get; }

    public bool Contains(ItemId id) => _byId.ContainsKey(id);

    public bool TryGet(ItemId id, out DesktopItem item) => _byId.TryGetValue(id, out item!);

    /// <summary>What changed between <paramref name="previous"/> and this snapshot.</summary>
    public DesktopSnapshotDiff DiffFrom(DesktopSnapshot previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        var added = new List<DesktopItem>();
        var changed = new List<DesktopItem>();
        foreach (var item in Items)
        {
            if (!previous.TryGet(item.Id, out var before))
            {
                added.Add(item);
            }
            else if (before != item)
            {
                changed.Add(item);
            }
        }

        var removed = previous.Items.Where(item => !Contains(item.Id)).ToList();
        return new DesktopSnapshotDiff(added, removed, changed);
    }
}

/// <summary>The result of <see cref="DesktopSnapshot.DiffFrom"/>.</summary>
/// <param name="Added">Items that are new, in the new snapshot's order.</param>
/// <param name="Removed">Items that are gone, in the old snapshot's order.</param>
/// <param name="Changed">Items whose id stayed but whose name, attributes or ID list changed (new values).</param>
public sealed record DesktopSnapshotDiff(
    IReadOnlyList<DesktopItem> Added,
    IReadOnlyList<DesktopItem> Removed,
    IReadOnlyList<DesktopItem> Changed)
{
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;
}
