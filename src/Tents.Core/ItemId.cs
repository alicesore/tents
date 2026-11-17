namespace Tents.Core;

/// <summary>
/// Identity of a desktop item: its shell parsing name (MILESTONES.md M1 step 5), for example a file system path
/// or a <c>::{CLSID}</c> name for a virtual item. Core treats the value as opaque.
/// </summary>
/// <remarks>
/// Ids compare case-insensitively (ordinal), because desktop items live on case-insensitive file systems and the
/// shell does not promise a stable casing for a parsing name. <c>default(ItemId)</c> is the empty id and is never
/// valid inside a layout.
/// </remarks>
public readonly struct ItemId : IEquatable<ItemId>
{
    private readonly string? _value;

    /// <exception cref="ArgumentException"><paramref name="value"/> is null, empty or whitespace.</exception>
    public ItemId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        _value = value;
    }

    public string Value => _value ?? string.Empty;

    /// <summary>True for <c>default(ItemId)</c>.</summary>
    public bool IsEmpty => _value is null;

    public static bool operator ==(ItemId left, ItemId right) => left.Equals(right);

    public static bool operator !=(ItemId left, ItemId right) => !left.Equals(right);

    public bool Equals(ItemId other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is ItemId other && Equals(other);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public override string ToString() => Value;
}
