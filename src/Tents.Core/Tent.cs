using System.Collections.Immutable;

namespace Tents.Core;

/// <summary>
/// One tent: a titled container on the desktop that holds desktop items. Immutable; change it with a
/// <c>with</c> expression or, inside a layout, through the layout operations.
/// </summary>
/// <remarks>
/// <see cref="Bounds"/> is the expanded size. Rolling a tent up only sets <see cref="IsRolledUp"/>, so expanding it
/// restores the same size. <see cref="Items"/> is in display order. Rules that span tents (an item in at most one
/// tent, exactly one default tent) are enforced by <see cref="Layout"/>.
/// </remarks>
public sealed record Tent
{
    /// <exception cref="ArgumentException">The id is empty, or the bounds do not have a positive size.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="title"/> is null.</exception>
    public Tent(TentId id, string title, PixelRect bounds, bool isRolledUp = false, ImmutableArray<ItemId> items = default)
    {
        Id = id;
        Title = title;
        Bounds = bounds;
        IsRolledUp = isRolledUp;
        Items = items;
    }

    public TentId Id
    {
        get;
        init => field = value.IsEmpty ? throw new ArgumentException("A tent needs a non-empty id.", nameof(value)) : value;
    }

    /// <summary>The title shown on the tent. May be empty; it is never null.</summary>
    public string Title
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Position and expanded size, in physical pixels. Width and height are always positive.</summary>
    public PixelRect Bounds
    {
        get;
        init => field = value.HasPositiveSize
            ? value
            : throw new ArgumentException("A tent's width and height must be greater than zero.", nameof(value));
    }

    /// <summary>True when the tent is collapsed to its title bar.</summary>
    public bool IsRolledUp { get; init; }

    /// <summary>The items in this tent, in display order. Never default; an empty tent has an empty array.</summary>
    public ImmutableArray<ItemId> Items
    {
        get;
        init => field = value.IsDefault ? [] : value;
    }

    /// <summary>Structural equality, comparing <see cref="Items"/> element by element.</summary>
    public bool Equals(Tent? other) =>
        other is not null
        && (ReferenceEquals(this, other)
            || (Id == other.Id
                && string.Equals(Title, other.Title, StringComparison.Ordinal)
                && Bounds == other.Bounds
                && IsRolledUp == other.IsRolledUp
                && Items.AsSpan().SequenceEqual(other.Items.AsSpan())));

    public override int GetHashCode() => HashCode.Combine(Id, Title, Bounds, IsRolledUp, Items.Length);
}
