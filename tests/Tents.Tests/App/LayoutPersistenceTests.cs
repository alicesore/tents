using Tents.App;
using Tents.Core;
using Tents.Core.Persistence;
using Tents.Shell.Hosting;

namespace Tents.Tests.App;

/// <summary>Layout restore and saving. Uses layout files in a temporary folder, never the real one.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA1001", Justification = "The log is disposed in IAsyncLifetime.DisposeAsync.")]
public sealed class LayoutPersistenceTests : IAsyncLifetime
{
    private static readonly MonitorInfo _primary = new(@"\\.\DISPLAY1", new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1040), 96, true);
    private static readonly MonitorInfo _second = new(@"\\.\DISPLAY2", new PixelRect(1920, 0, 1920, 1080), new PixelRect(1920, 0, 1920, 1040), 96, false);

    private readonly string _folder = Directory.CreateTempSubdirectory("tents-layout-").FullName;
    private readonly AppLog _log;

    public LayoutPersistenceTests() => _log = new AppLog(Path.Combine(_folder, "test.log"));

    private string LayoutPath => Path.Combine(_folder, "layout.json");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _log.DisposeAsync();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Choose_TheSavedLayoutForTheseMonitors()
    {
        var both = Key(_primary, _second);
        var saved = LayoutSet.Create([Sample(Key(_primary)), Sample(both)]);

        var layout = LayoutPersistence.Choose(saved, both, () => throw new InvalidOperationException(), out var choice);

        Assert.Equal(LayoutChoice.Saved, choice);
        Assert.Same(saved.Layouts[1], layout);
    }

    [Fact]
    public void Choose_NothingSaved_IsANewLayout()
    {
        var fresh = Sample(Key(_primary));

        var layout = LayoutPersistence.Choose(LayoutSet.Empty, Key(_primary), () => fresh, out var choice);

        Assert.Equal(LayoutChoice.New, choice);
        Assert.Same(fresh, layout);
    }

    [Fact]
    public void Choose_OtherMonitors_AdoptsTheClosestLayoutUnderTheNewKey()
    {
        var onlyOther = Sample(Key(new MonitorInfo(@"\\.\DISPLAY9", new PixelRect(0, 0, 800, 600), new PixelRect(0, 0, 800, 600), 96, true)));
        var withPrimary = Sample(Key(_primary, _second));
        var saved = LayoutSet.Create([onlyOther, withPrimary]);
        var current = Key(_primary); // the second monitor was unplugged

        var layout = LayoutPersistence.Choose(saved, current, () => throw new InvalidOperationException(), out var choice);

        Assert.Equal(LayoutChoice.Adopted, choice);
        Assert.Equal(current, layout.Monitors);
        Assert.Equal(withPrimary.Tents, layout.Tents);
        Assert.Equal(withPrimary.DefaultTentId, layout.DefaultTentId);
    }

    [Fact]
    public void BringOnScreen_LeavesVisibleTentsAlone()
    {
        var sample = Sample(Key(_primary));
        var layout = sample.SetTentBounds(sample.DefaultTentId, new PixelRect(1800, 1000, 400, 400)); // partly visible

        var result = LayoutPersistence.BringOnScreen(layout, [_primary], out var moved);

        Assert.Same(layout, result);
        Assert.Equal(0, moved);
    }

    [Fact]
    public void BringOnScreen_MovesTentsNoMonitorShows_CascadedOnThePrimary()
    {
        var a = TentId.New();
        var b = TentId.New();
        var layout = Layout.CreateWithDefaultTent(Key(_primary), new Tent(a, "A", new PixelRect(2500, 100, 300, 300)))
            .CreateTent(b, "B", new PixelRect(3000, 500, 300, 300));

        var result = LayoutPersistence.BringOnScreen(layout, [_primary], out var moved);

        Assert.Equal(2, moved);
        Assert.Equal(new PixelRect(24, 24, 300, 300), result.GetTent(a).Bounds);
        Assert.Equal(new PixelRect(56, 56, 300, 300), result.GetTent(b).Bounds);
    }

    [Fact]
    public void BringOnScreen_WithoutMonitors_ChangesNothing()
    {
        var layout = Sample(Key(_primary));

        Assert.Same(layout, LayoutPersistence.BringOnScreen(layout, [], out _));
    }

    [Fact]
    public async Task Schedule_SavesTheLayout_AndTheNextStartRestoresIt()
    {
        var layout = Sample(Key(_primary));
        await using (var persistence = new LayoutPersistence(new LayoutFileStore(LayoutPath), _log))
        {
            Assert.True((await persistence.LoadAsync()).Layouts.IsEmpty);
            persistence.Schedule(layout);
            await persistence.FlushAsync();
        }

        await using var next = new LayoutPersistence(new LayoutFileStore(LayoutPath), _log);
        var restored = await next.LoadAsync();

        Assert.Equal(layout, Assert.Single(restored.Layouts));
    }

    [Fact]
    public async Task Schedule_KeepsTheLayoutsOfOtherMonitorSets()
    {
        var first = Sample(Key(_primary));
        var second = Sample(Key(_primary, _second));
        await using (var persistence = new LayoutPersistence(new LayoutFileStore(LayoutPath), _log))
        {
            await persistence.LoadAsync();
            persistence.Schedule(first);
            persistence.Schedule(second);
            persistence.Schedule(second.RenameTent(second.DefaultTentId, "Renamed"));
            await persistence.FlushAsync();
        }

        var restored = new LayoutFileStore(LayoutPath).Load().Layouts;

        Assert.Equal(2, restored.Layouts.Length);
        Assert.Equal("Renamed", restored.Find(second.Monitors)!.DefaultTent.Title);
    }

    [Fact]
    public async Task AFileThatCannotBeRead_IsNeverOverwritten()
    {
        await File.WriteAllTextAsync(LayoutPath, "{ \"precious\": true }");
        var before = await File.ReadAllTextAsync(LayoutPath);
        await using var persistence = new LayoutPersistence(new LayoutFileStore(LayoutPath), _log);

        using (new FileStream(LayoutPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var loaded = await persistence.LoadAsync();
            Assert.True(loaded.Layouts.IsEmpty);
        }

        Assert.True(persistence.IsReadOnly);
        persistence.Schedule(Sample(Key(_primary)));
        await persistence.FlushAsync();
        Assert.Equal(before, await File.ReadAllTextAsync(LayoutPath));
    }

    private static MonitorConfigKey Key(params MonitorInfo[] monitors) => TentsAppKey(monitors);

    private static MonitorConfigKey TentsAppKey(IReadOnlyList<MonitorInfo> monitors) => TentsApp.MonitorKey(monitors);

    private static Layout Sample(MonitorConfigKey key)
    {
        var id = TentId.New();
        return Layout.CreateWithDefaultTent(key, new Tent(id, "Desktop", new PixelRect(100, 100, 300, 300), items: [new ItemId(@"C:\synthetic\a.txt")]))
            .CreateTent(TentId.New(), "Work", new PixelRect(500, 100, 300, 300));
    }
}
