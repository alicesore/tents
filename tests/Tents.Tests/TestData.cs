using System.Collections.Immutable;
using Tents.Core;

namespace Tents.Tests;

/// <summary>Shared builders for model tests. Item ids are synthetic and never refer to real desktop files.</summary>
internal static class TestData
{
    public static readonly MonitorConfigKey SingleMonitor =
        MonitorConfigKey.Create(new MonitorDescriptor("TEST-MONITOR-1", new PixelRect(0, 0, 1920, 1080)));

    public static readonly MonitorConfigKey DualMonitor = MonitorConfigKey.Create(
        new MonitorDescriptor("TEST-MONITOR-1", new PixelRect(0, 0, 1920, 1080)),
        new MonitorDescriptor("TEST-MONITOR-2", new PixelRect(1920, -200, 1080, 1920)));

    public static readonly PixelRect SomeBounds = new(100, 100, 400, 300);

    /// <summary>A deterministic tent id: the same <paramref name="n"/> always gives the same id.</summary>
    public static TentId Id(int n) => new(new Guid(n, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]));

    public static ItemId Item(string name) => new(@"X:\TestDesktop\" + name);

    public static ImmutableArray<ItemId> Items(params string[] names) => [.. names.Select(Item)];

    public static Tent Tent(int n, params string[] items) =>
        new(Id(n), "Tent " + n, SomeBounds, items: Items(items));

    /// <summary>A layout whose default tent is <c>Id(0)</c>, plus one tent per entry in <paramref name="others"/>.</summary>
    public static Layout LayoutWith(Tent defaultTent, params Tent[] others) =>
        Layout.Create(SingleMonitor, [defaultTent, .. others], defaultTent.Id);
}
