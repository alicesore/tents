namespace Tents.Core;

/// <summary>
/// Stable identity of a tent. It is generated once when the tent is created and persisted with it.
/// <c>default(TentId)</c> is the empty id and is never a valid tent id.
/// </summary>
public readonly record struct TentId
{
    /// <summary>Wraps an existing id, for example one read from the layout file.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is <see cref="Guid.Empty"/>.</exception>
    public TentId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A tent id cannot be the empty GUID.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    /// <summary>True for <c>default(TentId)</c>, which no tent may use.</summary>
    public bool IsEmpty => Value == Guid.Empty;

    /// <summary>
    /// Creates a new random id. This is the only non-deterministic step in the model: the layout operations
    /// take ids as input, so callers create the id first and the operations stay pure.
    /// </summary>
    public static TentId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}
