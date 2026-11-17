# Layout file format

Tents saves its layouts to `%APPDATA%\Tents\layout.json`. The file is UTF-8 JSON without a byte order mark, written by `Tents.Core.Persistence.LayoutSerializer` with System.Text.Json source generation. This page describes schema version 1.

## Example

```json
{
  "schemaVersion": 1,
  "layouts": [
    {
      "monitors": [
        { "deviceId": "MONITOR-A", "bounds": { "x": 0, "y": 0, "width": 2560, "height": 1440 } }
      ],
      "defaultTentId": "3f2c9a4e-0d7b-4c1e-9a55-1b2c3d4e5f60",
      "tents": [
        {
          "id": "3f2c9a4e-0d7b-4c1e-9a55-1b2c3d4e5f60",
          "title": "Desktop",
          "bounds": { "x": 40, "y": 40, "width": 420, "height": 300 },
          "rolledUp": false,
          "items": [ "C:\\Users\\Example\\Desktop\\notes.txt" ]
        }
      ]
    }
  ]
}
```

## Fields

| Field | Meaning |
|---|---|
| `schemaVersion` | Integer, always the first property. Tents reads the version before anything else. |
| `layouts` | One entry per monitor configuration. No two entries may have the same `monitors`. |
| `monitors` | The monitor configuration: each monitor's device id and bounds. Order does not matter. |
| `defaultTentId` | The tent that holds every item not put into another tent. It must be one of `tents`. |
| `tents[].id` | A GUID, unique within the layout. |
| `tents[].title` | Required; may be empty. |
| `tents[].bounds` | Position and expanded size in physical pixels, virtual-screen coordinates. Width and height are positive. |
| `tents[].rolledUp` | True when the tent is collapsed to its title bar. `bounds` still holds the expanded size. |
| `tents[].items` | Shell parsing names of the items, in display order. Optional (empty when missing). An item may appear only once in the whole layout; names are compared case-insensitively. |

Unknown properties are ignored. A file that breaks any rule above is rejected as a whole, not partly loaded.

## Files next to it

| File | Purpose |
|---|---|
| `layout.json.bak` | The previous version. Every save keeps one. |
| `layout.json.tmp` | A save in progress. Never read. |
| `layout.json.corrupt` | The last main file that could not be read. |
| `layout.json.v{N}` | A main file from a newer Tents (schema version N), moved aside untouched. |

## Loading and saving

- **Save** writes `layout.json.tmp`, flushes it to disk, and swaps it in with `File.Replace` (Win32 `ReplaceFileW`), which turns the old file into `layout.json.bak`. The first save moves the temp file into place.
- **Load** reads `layout.json`. If it is missing, Tents uses the backup. If it is corrupt, it is moved to `layout.json.corrupt` and Tents uses the backup. If it has a newer schema version, it is moved to `layout.json.v{N}` and Tents uses the backup if that is readable. Moving a bad file aside keeps the next save from turning it into the backup. If no file is usable, Tents starts with no layouts.

## Schema changes

Raise `LayoutSerializer.CurrentSchemaVersion`, change the DTOs in `LayoutFileDto.cs`, and add an `ILayoutMigration` for the old version to `LayoutSerializer.DefaultMigrations`. A migration rewrites the raw JSON tree from version N to N+1, and the serializer chains them. An older Tents never reads a newer file: it moves it aside as described above.
