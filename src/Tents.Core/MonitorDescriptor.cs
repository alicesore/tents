namespace Tents.Core;

/// <summary>One monitor in a monitor configuration: a stable device id and its bounds in physical pixels.</summary>
/// <remarks>
/// <see cref="DeviceId"/> is opaque to Core. The shell layer fills it from the monitor's device identity, and it is
/// compared case-insensitively (ordinal). DPI is deliberately not part of the descriptor in M1; see
/// <see cref="MonitorConfigKey"/>.
/// </remarks>
public readonly struct MonitorDescriptor : IEquatable<MonitorDescriptor>
{
    private readonly string? _deviceId;

    /// <exception cref="ArgumentException">The device id is null or whitespace, or the bounds are empty.</exception>
    public MonitorDescriptor(string deviceId, PixelRect bounds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        if (!bounds.HasPositiveSize)
        {
            throw new ArgumentException("A monitor's width and height must be greater than zero.", nameof(bounds));
        }

        _deviceId = deviceId;
        Bounds = bounds;
    }

    public string DeviceId => _deviceId ?? string.Empty;

    public PixelRect Bounds { get; }

    public static bool operator ==(MonitorDescriptor left, MonitorDescriptor right) => left.Equals(right);

    public static bool operator !=(MonitorDescriptor left, MonitorDescriptor right) => !left.Equals(right);

    public bool Equals(MonitorDescriptor other) =>
        string.Equals(DeviceId, other.DeviceId, StringComparison.OrdinalIgnoreCase) && Bounds == other.Bounds;

    public override bool Equals(object? obj) => obj is MonitorDescriptor other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(DeviceId), Bounds);

    public override string ToString() => $"{DeviceId} @ {Bounds.X},{Bounds.Y} {Bounds.Width}x{Bounds.Height}";
}
