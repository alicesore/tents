using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tents.Core.Persistence;

/// <summary>
/// Converts a <see cref="LayoutSet"/> to and from the layout file's JSON, using System.Text.Json source generation
/// (no reflection). Thread-safe.
/// </summary>
/// <remarks>
/// <para>Reading checks <c>schemaVersion</c> first. The current version is deserialized directly. An older version
/// is upgraded through <see cref="ILayoutMigration"/> steps on the JSON tree first. A newer version throws
/// <see cref="UnsupportedLayoutVersionException"/>, so a downgrade never misreads a file.</para>
/// <para>Every value read is validated by building the immutable model, so an invalid file (for example an item in
/// two tents) is rejected as a whole with <see cref="LayoutFormatException"/> rather than partly loaded.</para>
/// <para>Unknown properties are ignored, so a file may carry extra data within the same schema version.</para>
/// </remarks>
public sealed class LayoutSerializer
{
    /// <summary>The schema version this build writes, and the newest it reads.</summary>
    public const int CurrentSchemaVersion = 1;

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    private static ReadOnlySpan<byte> SchemaVersionProperty => "schemaVersion"u8;

    private readonly ImmutableArray<ILayoutMigration> _migrations;

    /// <param name="migrations">Migrations to apply to older files. Defaults to <see cref="DefaultMigrations"/>.</param>
    public LayoutSerializer(IEnumerable<ILayoutMigration>? migrations = null)
    {
        _migrations = migrations is null ? DefaultMigrations : [.. migrations];
    }

    // Declared before Default: static initializers run in declaration order, and Default's constructor reads this.

    /// <summary>The built-in migrations. Empty while version 1 is the only schema.</summary>
    public static ImmutableArray<ILayoutMigration> DefaultMigrations { get; } = [];

    /// <summary>The serializer with the built-in migrations.</summary>
    public static LayoutSerializer Default { get; } = new();

    /// <summary>Writes the layout set as UTF-8 JSON (no byte order mark).</summary>
    public static void Serialize(Stream utf8Json, LayoutSet layouts)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        JsonSerializer.Serialize(utf8Json, ToDto(layouts), LayoutJsonContext.Default.LayoutFileDto);
    }

    /// <summary>Returns the layout set as UTF-8 JSON (no byte order mark).</summary>
    public static byte[] SerializeToUtf8Bytes(LayoutSet layouts) =>
        JsonSerializer.SerializeToUtf8Bytes(ToDto(layouts), LayoutJsonContext.Default.LayoutFileDto);

    /// <summary>Reads a layout set from UTF-8 JSON. A leading byte order mark is allowed.</summary>
    /// <exception cref="LayoutFormatException">The JSON is invalid, has the wrong shape, breaks a model rule, or
    /// is an older version with no migration path.</exception>
    /// <exception cref="UnsupportedLayoutVersionException">The file has a newer schema version.</exception>
    public LayoutSet Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.StartsWith(Utf8Bom))
        {
            utf8Json = utf8Json[Utf8Bom.Length..];
        }

        var version = ReadSchemaVersion(utf8Json);
        if (version > CurrentSchemaVersion)
        {
            throw new UnsupportedLayoutVersionException(version, CurrentSchemaVersion);
        }

        LayoutFileDto? dto;
        try
        {
            dto = version == CurrentSchemaVersion
                ? JsonSerializer.Deserialize(utf8Json, LayoutJsonContext.Default.LayoutFileDto)
                : Migrate(utf8Json, version);
        }
        catch (JsonException ex)
        {
            throw new LayoutFormatException($"The layout file is not valid JSON for schema version {CurrentSchemaVersion}.", ex);
        }

        return ToModel(dto);
    }

    /// <summary>
    /// Reads <c>schemaVersion</c> from the top-level object without deserializing the rest. Tents writes it first,
    /// so this normally stops after a few tokens.
    /// </summary>
    /// <exception cref="LayoutFormatException">Not a JSON object, or no integer <c>schemaVersion</c>.</exception>
    public static int ReadSchemaVersion(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8Json);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                throw new LayoutFormatException("The layout file is not a JSON object.");
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isVersion = reader.ValueTextEquals(SchemaVersionProperty);
                if (!reader.Read())
                {
                    break;
                }

                if (isVersion)
                {
                    return reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var version)
                        ? version
                        : throw new LayoutFormatException("The layout file's schemaVersion is not an integer.");
                }

                reader.Skip();
            }
        }
        catch (JsonException ex)
        {
            throw new LayoutFormatException("The layout file is not valid JSON.", ex);
        }

        throw new LayoutFormatException("The layout file has no schemaVersion.");
    }

    private LayoutFileDto? Migrate(ReadOnlySpan<byte> utf8Json, int version)
    {
        if (JsonNode.Parse(utf8Json) is not JsonObject root)
        {
            throw new LayoutFormatException("The layout file is not a JSON object.");
        }

        for (var from = version; from < CurrentSchemaVersion; from++)
        {
            var migration = FindMigration(from)
                ?? throw new LayoutFormatException($"There is no migration from layout schema version {from}.");
            try
            {
                migration.Migrate(root);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                throw new LayoutFormatException($"Migrating the layout file from schema version {from} failed.", ex);
            }

            root["schemaVersion"] = from + 1;
        }

        return root.Deserialize(LayoutJsonContext.Default.LayoutFileDto);
    }

    private ILayoutMigration? FindMigration(int fromVersion)
    {
        foreach (var migration in _migrations)
        {
            if (migration.FromVersion == fromVersion)
            {
                return migration;
            }
        }

        return null;
    }

    // ---- Model <-> DTO ----

    private static LayoutFileDto ToDto(LayoutSet layouts)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        var result = new List<LayoutDto>(layouts.Layouts.Length);
        foreach (var layout in layouts.Layouts)
        {
            var monitors = new List<MonitorDto>(layout.Monitors.Monitors.Length);
            foreach (var monitor in layout.Monitors.Monitors)
            {
                monitors.Add(new MonitorDto { DeviceId = monitor.DeviceId, Bounds = ToDto(monitor.Bounds) });
            }

            var tents = new List<TentDto>(layout.Tents.Length);
            foreach (var tent in layout.Tents)
            {
                var items = new List<string>(tent.Items.Length);
                foreach (var item in tent.Items)
                {
                    items.Add(item.Value);
                }

                tents.Add(new TentDto
                {
                    Id = tent.Id.Value,
                    Title = tent.Title,
                    Bounds = ToDto(tent.Bounds),
                    RolledUp = tent.IsRolledUp,
                    Items = items,
                });
            }

            result.Add(new LayoutDto { Monitors = monitors, DefaultTentId = layout.DefaultTentId.Value, Tents = tents });
        }

        return new LayoutFileDto { SchemaVersion = CurrentSchemaVersion, Layouts = result };
    }

    private static RectDto ToDto(PixelRect rect) =>
        new() { X = rect.X, Y = rect.Y, Width = rect.Width, Height = rect.Height };

    private static LayoutSet ToModel(LayoutFileDto? dto)
    {
        if (dto?.Layouts is null)
        {
            throw new LayoutFormatException("The layout file has no layouts array.");
        }

        try
        {
            var layouts = new List<Layout>(dto.Layouts.Count);
            for (var i = 0; i < dto.Layouts.Count; i++)
            {
                layouts.Add(ToModel(dto.Layouts[i], i));
            }

            return LayoutSet.Create(layouts);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidLayoutException)
        {
            throw new LayoutFormatException($"The layout file breaks a layout rule: {ex.Message}", ex);
        }
    }

    private static Layout ToModel(LayoutDto? dto, int index)
    {
        if (dto?.Monitors is null || dto.Tents is null)
        {
            throw new LayoutFormatException($"Layout {index} needs both a monitors and a tents array.");
        }

        var monitors = new List<MonitorDescriptor>(dto.Monitors.Count);
        foreach (var monitor in dto.Monitors)
        {
            if (monitor?.DeviceId is null || monitor.Bounds is null)
            {
                throw new LayoutFormatException($"A monitor in layout {index} needs a deviceId and bounds.");
            }

            monitors.Add(new MonitorDescriptor(monitor.DeviceId, ToModel(monitor.Bounds)));
        }

        var tents = new List<Tent>(dto.Tents.Count);
        foreach (var tent in dto.Tents)
        {
            if (tent?.Title is null || tent.Bounds is null)
            {
                throw new LayoutFormatException($"A tent in layout {index} needs a title and bounds.");
            }

            var items = ImmutableArray.CreateBuilder<ItemId>(tent.Items?.Count ?? 0);
            foreach (var item in tent.Items ?? [])
            {
                items.Add(new ItemId(item));
            }

            tents.Add(new Tent(new TentId(tent.Id), tent.Title, ToModel(tent.Bounds), tent.RolledUp, items.MoveToImmutable()));
        }

        return Layout.Create(MonitorConfigKey.Create(monitors), tents, new TentId(dto.DefaultTentId));
    }

    private static PixelRect ToModel(RectDto rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}
