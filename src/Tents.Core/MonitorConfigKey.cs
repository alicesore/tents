using System.Collections.Immutable;

namespace Tents.Core;

/// <summary>
/// Identifies a monitor configuration: the set of connected monitors with their positions and resolutions.
/// Each <see cref="Layout"/> belongs to one configuration (PROJECT_PLAN.md §6, Persistence).
/// </summary>
/// <remarks>
/// Equality ignores the order in which monitors are reported: <see cref="Create"/> sorts them into a canonical
/// order. DPI is not part of the key in M1, so changing the display scale keeps the same layout; whether scale
/// should select a different layout is an M2 decision (MILESTONES.md M2 steps 3 and 4).
/// </remarks>
public sealed class MonitorConfigKey : IEquatable<MonitorConfigKey>
{
    private readonly int _hashCode;

    private MonitorConfigKey(ImmutableArray<MonitorDescriptor> monitors)
    {
        Monitors = monitors;
        var hash = new HashCode();
        foreach (var monitor in monitors)
        {
            hash.Add(monitor);
        }

        _hashCode = hash.ToHashCode();
    }

    /// <summary>The monitors in canonical order (device id, then position, then size).</summary>
    public ImmutableArray<MonitorDescriptor> Monitors { get; }

    /// <exception cref="ArgumentException">No monitors, or a monitor is <c>default</c>.</exception>
    public static MonitorConfigKey Create(IEnumerable<MonitorDescriptor> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        var builder = monitors.ToImmutableArray().ToBuilder();
        if (builder.Count == 0)
        {
            throw new ArgumentException("A monitor configuration needs at least one monitor.", nameof(monitors));
        }

        if (builder.Any(m => m.DeviceId.Length == 0))
        {
            throw new ArgumentException("A monitor configuration cannot contain a default monitor descriptor.", nameof(monitors));
        }

        builder.Sort(CompareCanonical);
        return new MonitorConfigKey(builder.ToImmutable());
    }

    /// <summary>Convenience overload of <see cref="Create(IEnumerable{MonitorDescriptor})"/>.</summary>
    /// <remarks>
    /// The cast matters: with C# 14 span conversions an array argument binds to this span overload again.
    /// </remarks>
    public static MonitorConfigKey Create(params ReadOnlySpan<MonitorDescriptor> monitors) =>
        Create((IEnumerable<MonitorDescriptor>)monitors.ToArray());

    public static bool operator ==(MonitorConfigKey? left, MonitorConfigKey? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(MonitorConfigKey? left, MonitorConfigKey? right) => !(left == right);

    public bool Equals(MonitorConfigKey? other) =>
        other is not null
        && (ReferenceEquals(this, other)
            || (_hashCode == other._hashCode && Monitors.AsSpan().SequenceEqual(other.Monitors.AsSpan())));

    public override bool Equals(object? obj) => Equals(obj as MonitorConfigKey);

    public override int GetHashCode() => _hashCode;

    public override string ToString() => string.Join("; ", Monitors);

    private static int CompareCanonical(MonitorDescriptor a, MonitorDescriptor b)
    {
        var result = StringComparer.OrdinalIgnoreCase.Compare(a.DeviceId, b.DeviceId);
        if (result == 0)
        {
            result = a.Bounds.X.CompareTo(b.Bounds.X);
        }

        if (result == 0)
        {
            result = a.Bounds.Y.CompareTo(b.Bounds.Y);
        }

        if (result == 0)
        {
            result = a.Bounds.Width.CompareTo(b.Bounds.Width);
        }

        if (result == 0)
        {
            result = a.Bounds.Height.CompareTo(b.Bounds.Height);
        }

        return result;
    }
}
