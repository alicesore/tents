using System.Text;
using Tents.Core;
using static Tents.Tests.TestData;

namespace Tents.Tests.Persistence;

internal static class PersistenceTestData
{
    /// <summary>Two layouts with every field set to a non-default value somewhere.</summary>
    public static LayoutSet Sample()
    {
        var single = LayoutWith(Tent(0, "a", "b"), Tent(1, "c") with { IsRolledUp = true, Title = "Wörk ✓ \"quoted\"" }, Tent(2));
        var dual = Layout.Create(
            DualMonitor,
            [new Tent(Id(10), "", new PixelRect(-1500, -200, 320, 240), items: Items("d")), Tent(11, "e", "f")],
            Id(11));
        return LayoutSet.Create([single, dual]);
    }

    public static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    /// <summary>A minimal valid file for <paramref name="version"/>, with one layout and one tent.</summary>
    public static string MinimalJson(int version, string items = "[]") => $$"""
        {
          "schemaVersion": {{version}},
          "layouts": [
            {
              "monitors": [ { "deviceId": "M1", "bounds": { "x": 0, "y": 0, "width": 1920, "height": 1080 } } ],
              "defaultTentId": "00000001-0000-0000-0000-000000000001",
              "tents": [
                {
                  "id": "00000001-0000-0000-0000-000000000001",
                  "title": "Desktop",
                  "bounds": { "x": 0, "y": 0, "width": 400, "height": 300 },
                  "rolledUp": false,
                  "items": {{items}}
                }
              ]
            }
          ]
        }
        """;
}
