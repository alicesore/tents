using System.Text.Json.Nodes;

namespace Tents.Core.Persistence;

/// <summary>
/// Upgrades a layout file from one schema version to the next. Migrations work on the raw JSON tree, so they can
/// handle shapes the current DTOs no longer describe.
/// </summary>
/// <remarks>
/// When the schema changes: bump <see cref="LayoutSerializer.CurrentSchemaVersion"/>, change the DTOs, and add a
/// migration whose <see cref="FromVersion"/> is the old version to <see cref="LayoutSerializer.DefaultMigrations"/>.
/// The serializer applies migrations in order and sets <c>schemaVersion</c> after each step; a migration only
/// changes the shape.
/// </remarks>
public interface ILayoutMigration
{
    /// <summary>The version this migration reads. It produces <c>FromVersion + 1</c>.</summary>
    int FromVersion { get; }

    /// <summary>Rewrites <paramref name="root"/> (the whole file) in place.</summary>
    void Migrate(JsonObject root);
}
