using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tents.Core;
using Tents.Core.Persistence;
using static Tents.Tests.Persistence.PersistenceTestData;

namespace Tents.Tests.Persistence;

public class LayoutSerializerTests
{
    [Fact]
    public void RoundTrip_PreservesEverything()
    {
        var original = Sample();

        var bytes = LayoutSerializer.SerializeToUtf8Bytes(original);
        var read = LayoutSerializer.Default.Deserialize(bytes);

        Assert.Equal(original, read);
    }

    [Fact]
    public void RoundTrip_ThroughStream_MatchesBytes()
    {
        using var stream = new MemoryStream();
        LayoutSerializer.Serialize(stream, Sample());

        Assert.Equal(LayoutSerializer.SerializeToUtf8Bytes(Sample()), stream.ToArray());
    }

    [Fact]
    public void RoundTrip_EmptySet() =>
        Assert.Same(LayoutSet.Empty, LayoutSerializer.Default.Deserialize(LayoutSerializer.SerializeToUtf8Bytes(LayoutSet.Empty)));

    [Fact]
    public void Output_HasTheDocumentedSchemaShape()
    {
        var bytes = LayoutSerializer.SerializeToUtf8Bytes(Sample());
        Assert.False(bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]), "No byte order mark.");

        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        Assert.Equal(["schemaVersion", "layouts"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());

        var layout = root.GetProperty("layouts")[0];
        Assert.Equal(["monitors", "defaultTentId", "tents"], layout.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["deviceId", "bounds"], layout.GetProperty("monitors")[0].EnumerateObject().Select(p => p.Name));

        var tent = layout.GetProperty("tents")[1];
        Assert.Equal(["id", "title", "bounds", "rolledUp", "items"], tent.EnumerateObject().Select(p => p.Name));
        Assert.Equal(["x", "y", "width", "height"], tent.GetProperty("bounds").EnumerateObject().Select(p => p.Name));
        Assert.True(tent.GetProperty("rolledUp").GetBoolean());
        Assert.Equal(@"X:\TestDesktop\c", tent.GetProperty("items")[0].GetString());
    }

    [Fact]
    public void Deserialize_AcceptsByteOrderMarkAndUnknownProperties()
    {
        var json = MinimalJson(1).Replace("\"rolledUp\"", "\"futureField\": [1, 2], \"rolledUp\"", StringComparison.Ordinal);
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Utf8(json)];

        var set = LayoutSerializer.Default.Deserialize(bytes);

        Assert.Equal("Desktop", Assert.Single(set.Layouts).DefaultTent.Title);
    }

    [Fact]
    public void Deserialize_SchemaVersionNeedNotBeFirst()
    {
        var json = """{ "layouts": [], "extra": { "schemaVersion": 99 }, "schemaVersion": 1 }""";

        Assert.Same(LayoutSet.Empty, LayoutSerializer.Default.Deserialize(Utf8(json)));
    }

    public static TheoryData<string> CorruptFiles() =>
    [
        "",
        "not json",
        "[1, 2, 3]",
        "{ \"schemaVersion\": 1, \"layouts\": [",
        "{ \"layouts\": [] }",
        "{ \"schemaVersion\": \"1\", \"layouts\": [] }",
        "{ \"schemaVersion\": 1.5, \"layouts\": [] }",
        "{ \"schemaVersion\": 1 }",
        "{ \"schemaVersion\": 1, \"layouts\": [ null ] }",
        "{ \"schemaVersion\": 1, \"layouts\": [ { \"tents\": [] } ] }",
        "{ \"schemaVersion\": 1, \"layouts\": \"oops\" }",
        MinimalJson(1).Replace("\"title\": \"Desktop\",", "", StringComparison.Ordinal),
        MinimalJson(1).Replace("\"width\": 400", "\"width\": 0", StringComparison.Ordinal),
        MinimalJson(1).Replace("\"defaultTentId\": \"00000001", "\"defaultTentId\": \"00000009", StringComparison.Ordinal),
        MinimalJson(1).Replace("00000001-0000-0000-0000-000000000001", "00000000-0000-0000-0000-000000000000", StringComparison.Ordinal),
        MinimalJson(1, items: """["X:\\TestDesktop\\a", "x:\\testdesktop\\A"]"""),
        MinimalJson(1, items: """["X:\\TestDesktop\\a", null]"""),
        MinimalJson(1, items: """["  "]"""),
    ];

    [Theory]
    [MemberData(nameof(CorruptFiles))]
    public void Deserialize_CorruptFile_ThrowsLayoutFormatException(string json) =>
        Assert.Throws<LayoutFormatException>(() => LayoutSerializer.Default.Deserialize(Utf8(json)));

    [Fact]
    public void Deserialize_CorruptFile_MessageDoesNotContainItemIds()
    {
        var json = MinimalJson(1, items: """["X:\\TestDesktop\\secret.txt", "X:\\TestDesktop\\secret.txt"]""");

        var ex = Assert.Throws<LayoutFormatException>(() => LayoutSerializer.Default.Deserialize(Utf8(json)));

        Assert.DoesNotContain("secret", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_NewerSchemaVersion_ThrowsUnsupportedVersion()
    {
        var ex = Assert.Throws<UnsupportedLayoutVersionException>(
            () => LayoutSerializer.Default.Deserialize(Utf8("""{ "schemaVersion": 2, "somethingNew": {} }""")));

        Assert.Equal(2, ex.FileVersion);
        Assert.Equal(LayoutSerializer.CurrentSchemaVersion, ex.SupportedVersion);
    }

    [Fact]
    public void Deserialize_OlderVersionWithoutMigration_ThrowsLayoutFormatException() =>
        Assert.Throws<LayoutFormatException>(() => LayoutSerializer.Default.Deserialize(Utf8(MinimalJson(0))));

    [Fact]
    public void Deserialize_OlderVersion_RunsTheMigrationHook()
    {
        // A made-up version 0 that stored a single layout under "layout" instead of a "layouts" array.
        var v1 = JsonNode.Parse(MinimalJson(1))!.AsObject();
        var v0 = new JsonObject { ["schemaVersion"] = 0, ["layout"] = v1["layouts"]![0]!.DeepClone() };
        var migration = new WrapSingleLayoutMigration();
        var serializer = new LayoutSerializer([migration]);

        var set = serializer.Deserialize(Utf8(v0.ToJsonString()));

        Assert.Equal(1, migration.Calls);
        Assert.Equal(0, migration.SeenVersion);
        Assert.Equal("Desktop", Assert.Single(set.Layouts).DefaultTent.Title);
        Assert.Throws<LayoutFormatException>(() => LayoutSerializer.Default.Deserialize(Utf8(v0.ToJsonString())));
    }

    [Fact]
    public void Deserialize_FailingMigration_ThrowsLayoutFormatException()
    {
        var serializer = new LayoutSerializer([new WrapSingleLayoutMigration()]);

        // "layout" is a number, so the migration's AsObject() call throws InvalidOperationException.
        Assert.Throws<LayoutFormatException>(() => serializer.Deserialize(Utf8("""{ "schemaVersion": 0, "layout": 5 }""")));
    }

    [Fact]
    public void ReadSchemaVersion_StopsAtTheVersion()
    {
        // Everything after the version is garbage; the probe must not need it.
        var bytes = Encoding.UTF8.GetBytes("""{ "schemaVersion": 7, "layouts": [ !!!""");

        Assert.Equal(7, LayoutSerializer.ReadSchemaVersion(bytes));
    }

    private sealed class WrapSingleLayoutMigration : ILayoutMigration
    {
        public int Calls { get; private set; }

        public int SeenVersion { get; private set; } = -1;

        public int FromVersion => 0;

        public void Migrate(JsonObject root)
        {
            Calls++;
            SeenVersion = root["schemaVersion"]!.GetValue<int>();
            var layout = root["layout"]!.AsObject();
            root.Remove("layout");
            root["layouts"] = new JsonArray(layout);
        }
    }
}
