using System.Collections.Immutable;
using System.Globalization;
using Tents.Core;

namespace Tents.Benchmarks;

/// <summary>The setup sizes from PROJECT_PLAN.md §7, plus a stress size.</summary>
public enum LayoutSize
{
    /// <summary>The typical setup the budgets are defined for: 10 tents, 200 items.</summary>
    Typical,

    /// <summary>A stress size: 100 tents, 5,000 items.</summary>
    Stress,
}

/// <summary>Builds deterministic, synthetic layouts. Item names are made up and never refer to real files.</summary>
public static class BenchmarkLayouts
{
    public static (int Tents, int Items) Dimensions(LayoutSize size) => size switch
    {
        LayoutSize.Typical => (10, 200),
        LayoutSize.Stress => (100, 5_000),
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, null),
    };

    /// <summary>
    /// One layout on one monitor. Items are spread evenly over the tents, with paths of a realistic length.
    /// The first tent is the default tent; every third tent is rolled up.
    /// </summary>
    public static LayoutSet Create(LayoutSize size)
    {
        var (tentCount, itemCount) = Dimensions(size);
        var monitors = MonitorConfigKey.Create(new MonitorDescriptor(@"\\?\DISPLAY#BENCH0001#5&1a2b3c4d&0&UID4352", new PixelRect(0, 0, 2560, 1440)));
        var extensions = new[] { ".lnk", ".txt", ".pdf", ".url", ".docx", ".png" };

        var tents = new Tent[tentCount];
        var perTent = itemCount / tentCount;
        for (var t = 0; t < tentCount; t++)
        {
            var items = ImmutableArray.CreateBuilder<ItemId>(perTent);
            for (var i = 0; i < perTent; i++)
            {
                var n = (t * perTent) + i;
                var name = string.Create(CultureInfo.InvariantCulture, $@"C:\Users\bench-user\Desktop\Benchmark item {n:D5}{extensions[n % extensions.Length]}");
                items.Add(new ItemId(name));
            }

            tents[t] = new Tent(
                new TentId(new Guid(t + 1, 0x7e57, 0x7e57, [0, 0, 0, 0, 0, 0, 0, 1])),
                string.Create(CultureInfo.InvariantCulture, $"Tent {t + 1}"),
                new PixelRect(40 + (t % 5 * 480), 40 + (t / 5 % 4 * 340), 440, 300),
                isRolledUp: t % 3 == 2,
                items.MoveToImmutable());
        }

        return LayoutSet.Create([Layout.Create(monitors, tents, tents[0].Id)]);
    }
}
