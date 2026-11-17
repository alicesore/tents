using System.Text.Json.Serialization;

namespace Tents.Core.Persistence;

// The on-disk shape of layout.json, schema version 1. These types exist only for (de)serialization: they mirror
// the JSON exactly and are mapped to and from the immutable model in LayoutSerializer, which also validates them.
// Changing a property name or meaning here is a schema change: bump LayoutSerializer.CurrentSchemaVersion and
// add an ILayoutMigration.

internal sealed class LayoutFileDto
{
    // Written first so the version can be read without parsing the rest of the file.
    [JsonPropertyOrder(-1)]
    public int SchemaVersion { get; set; }

    public List<LayoutDto>? Layouts { get; set; }
}

internal sealed class LayoutDto
{
    public List<MonitorDto>? Monitors { get; set; }

    public Guid DefaultTentId { get; set; }

    public List<TentDto>? Tents { get; set; }
}

internal sealed class MonitorDto
{
    public string? DeviceId { get; set; }

    public RectDto? Bounds { get; set; }
}

internal sealed class TentDto
{
    public Guid Id { get; set; }

    public string? Title { get; set; }

    public RectDto? Bounds { get; set; }

    public bool RolledUp { get; set; }

    public List<string>? Items { get; set; }
}

internal sealed class RectDto
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(LayoutFileDto))]
internal sealed partial class LayoutJsonContext : JsonSerializerContext;
