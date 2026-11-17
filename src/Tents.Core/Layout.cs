using System.Collections.Immutable;

namespace Tents.Core;

/// <summary>
/// The tents for one monitor configuration. Immutable: every change produces a new layout.
/// </summary>
/// <remarks>
/// Invariants, checked by <see cref="Create"/>:
/// <list type="bullet">
/// <item>there is at least one tent, and tent ids are unique;</item>
/// <item>exactly one tent is the default tent (<see cref="DefaultTentId"/>), which holds every item the user has
/// not put into another tent (MILESTONES.md M1, "Desktop items");</item>
/// <item>every item id is non-empty and appears at most once in the whole layout.</item>
/// </list>
/// <see cref="Tents"/> keeps the order the tents were created in.
/// </remarks>
public sealed class Layout : IEquatable<Layout>
{
    private readonly int _defaultIndex;

    private Layout(MonitorConfigKey monitors, ImmutableArray<Tent> tents, TentId defaultTentId, int defaultIndex)
    {
        Monitors = monitors;
        Tents = tents;
        DefaultTentId = defaultTentId;
        _defaultIndex = defaultIndex;
    }

    /// <summary>The monitor configuration this layout belongs to.</summary>
    public MonitorConfigKey Monitors { get; }

    /// <summary>All tents, including the default tent, in creation order.</summary>
    public ImmutableArray<Tent> Tents { get; }

    /// <summary>The id of the default tent, which cannot be deleted.</summary>
    public TentId DefaultTentId { get; }

    public Tent DefaultTent => Tents[_defaultIndex];

    /// <summary>Total number of items across all tents.</summary>
    public int ItemCount
    {
        get
        {
            var count = 0;
            foreach (var tent in Tents)
            {
                count += tent.Items.Length;
            }

            return count;
        }
    }

    /// <summary>Creates a layout and checks every invariant.</summary>
    /// <exception cref="InvalidLayoutException">An invariant does not hold.</exception>
    public static Layout Create(MonitorConfigKey monitors, IEnumerable<Tent> tents, TentId defaultTentId)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(tents);
        var array = tents.ToImmutableArray();
        var defaultIndex = Validate(array, defaultTentId);
        return new Layout(monitors, array, defaultTentId, defaultIndex);
    }

    /// <summary>Creates a layout that holds only its default tent.</summary>
    /// <exception cref="InvalidLayoutException">The tent's items break an invariant (for example a duplicate item).</exception>
    public static Layout CreateWithDefaultTent(MonitorConfigKey monitors, Tent defaultTent)
    {
        ArgumentNullException.ThrowIfNull(defaultTent);
        return Create(monitors, [defaultTent], defaultTent.Id);
    }

    public static bool operator ==(Layout? left, Layout? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(Layout? left, Layout? right) => !(left == right);

    /// <summary>Returns the index of the tent in <see cref="Tents"/>, or -1.</summary>
    public int IndexOf(TentId id) => FindIndex(Tents.AsSpan(), id);

    public bool ContainsTent(TentId id) => IndexOf(id) >= 0;

    public bool TryGetTent(TentId id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Tent? tent)
    {
        var index = IndexOf(id);
        tent = index >= 0 ? Tents[index] : null;
        return tent is not null;
    }

    /// <exception cref="KeyNotFoundException">No tent has this id.</exception>
    public Tent GetTent(TentId id) =>
        TryGetTent(id, out var tent) ? tent : throw new KeyNotFoundException($"Tent {id} is not in the layout.");

    /// <summary>Returns the tent that holds the item, or null when the item is not in this layout.</summary>
    public TentId? FindTentOf(ItemId item)
    {
        foreach (var tent in Tents)
        {
            if (tent.Items.Contains(item))
            {
                return tent.Id;
            }
        }

        return null;
    }

    public bool ContainsItem(ItemId item) => FindTentOf(item) is not null;

    public bool Equals(Layout? other) =>
        other is not null
        && (ReferenceEquals(this, other)
            || (Monitors.Equals(other.Monitors)
                && DefaultTentId == other.DefaultTentId
                && Tents.AsSpan().SequenceEqual(other.Tents.AsSpan())));

    public override bool Equals(object? obj) => Equals(obj as Layout);

    public override int GetHashCode() => HashCode.Combine(Monitors, DefaultTentId, Tents.Length);

    public override string ToString() => $"Layout with {Tents.Length} tents and {ItemCount} items ({Monitors})";

    /// <summary>
    /// Returns a layout with the same monitors and default tent but different tents, without re-checking the
    /// invariants. Only for <see cref="LayoutOperations"/>, which preserve them by construction; the tests re-check
    /// every result with <see cref="Create"/>.
    /// </summary>
    internal Layout WithTentsUnchecked(ImmutableArray<Tent> tents)
    {
        var defaultIndex = _defaultIndex < tents.Length && tents[_defaultIndex].Id == DefaultTentId
            ? _defaultIndex
            : FindIndex(tents.AsSpan(), DefaultTentId);
        return new Layout(Monitors, tents, DefaultTentId, defaultIndex);
    }

    private static int Validate(ImmutableArray<Tent> tents, TentId defaultTentId)
    {
        if (tents.IsEmpty)
        {
            throw new InvalidLayoutException("A layout needs at least one tent (the default tent).");
        }

        if (defaultTentId.IsEmpty)
        {
            throw new InvalidLayoutException("The default tent id is empty.");
        }

        var tentIds = new HashSet<TentId>(tents.Length);
        var itemCount = 0;
        foreach (var tent in tents)
        {
            if (tent is null)
            {
                throw new InvalidLayoutException("A layout cannot contain a null tent.");
            }

            if (!tentIds.Add(tent.Id))
            {
                throw new InvalidLayoutException($"Tent {tent.Id} appears more than once.");
            }

            itemCount += tent.Items.Length;
        }

        var defaultIndex = FindIndex(tents.AsSpan(), defaultTentId);
        if (defaultIndex < 0)
        {
            throw new InvalidLayoutException($"The default tent {defaultTentId} is not in the layout.");
        }

        var owners = new Dictionary<ItemId, TentId>(itemCount);
        foreach (var tent in tents)
        {
            foreach (var item in tent.Items)
            {
                if (item.IsEmpty)
                {
                    throw new InvalidLayoutException($"Tent {tent.Id} contains an empty item id.");
                }

                if (!owners.TryAdd(item, tent.Id))
                {
                    var first = owners[item];
                    throw new InvalidLayoutException(first == tent.Id
                        ? $"An item appears more than once in tent {tent.Id}."
                        : $"An item appears in more than one tent ({first} and {tent.Id}).");
                }
            }
        }

        return defaultIndex;
    }

    private static int FindIndex(ReadOnlySpan<Tent> tents, TentId id)
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
