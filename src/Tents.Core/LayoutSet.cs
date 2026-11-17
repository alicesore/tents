using System.Collections.Immutable;

namespace Tents.Core;

/// <summary>
/// Everything Tents saves: one <see cref="Layout"/> per monitor configuration. This is the root that the layout file
/// stores. Immutable.
/// </summary>
/// <remarks>
/// In M1 each layout has its own tents and item assignments. Whether item assignments should be shared across
/// monitor configurations is part of the M2 multi-monitor work (MILESTONES.md M2 step 3).
/// </remarks>
public sealed class LayoutSet : IEquatable<LayoutSet>
{
    private LayoutSet(ImmutableArray<Layout> layouts) => Layouts = layouts;

    /// <summary>A set with no layouts, for example before the first start.</summary>
    public static LayoutSet Empty { get; } = new([]);

    /// <summary>The layouts, in the order they were added.</summary>
    public ImmutableArray<Layout> Layouts { get; }

    /// <exception cref="InvalidLayoutException">Two layouts have the same monitor configuration, or one is null.</exception>
    public static LayoutSet Create(IEnumerable<Layout> layouts)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        var array = layouts.ToImmutableArray();
        var keys = new HashSet<MonitorConfigKey>(array.Length);
        foreach (var layout in array)
        {
            if (layout is null)
            {
                throw new InvalidLayoutException("A layout set cannot contain a null layout.");
            }

            if (!keys.Add(layout.Monitors))
            {
                throw new InvalidLayoutException("Two layouts have the same monitor configuration.");
            }
        }

        return array.IsEmpty ? Empty : new LayoutSet(array);
    }

    public static bool operator ==(LayoutSet? left, LayoutSet? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(LayoutSet? left, LayoutSet? right) => !(left == right);

    /// <summary>Returns the layout for a monitor configuration, or null.</summary>
    public Layout? Find(MonitorConfigKey monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        foreach (var layout in Layouts)
        {
            if (layout.Monitors.Equals(monitors))
            {
                return layout;
            }
        }

        return null;
    }

    /// <summary>
    /// Adds the layout, or replaces the one with the same monitor configuration. Returns this instance when the
    /// layout is already in the set.
    /// </summary>
    public LayoutSet SetLayout(Layout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        for (var i = 0; i < Layouts.Length; i++)
        {
            if (Layouts[i].Monitors.Equals(layout.Monitors))
            {
                return ReferenceEquals(Layouts[i], layout) ? this : new LayoutSet(Layouts.SetItem(i, layout));
            }
        }

        return new LayoutSet(Layouts.Add(layout));
    }

    /// <summary>Removes the layout for a monitor configuration. Returns this instance when there is none.</summary>
    public LayoutSet RemoveLayout(MonitorConfigKey monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        for (var i = 0; i < Layouts.Length; i++)
        {
            if (Layouts[i].Monitors.Equals(monitors))
            {
                var remaining = Layouts.RemoveAt(i);
                return remaining.IsEmpty ? Empty : new LayoutSet(remaining);
            }
        }

        return this;
    }

    public bool Equals(LayoutSet? other) =>
        other is not null
        && (ReferenceEquals(this, other) || Layouts.AsSpan().SequenceEqual(other.Layouts.AsSpan()));

    public override bool Equals(object? obj) => Equals(obj as LayoutSet);

    public override int GetHashCode() => Layouts.Length;
}
