using System.Diagnostics;
using Tents.Shell.Desktop;
using Tents.Shell.IconLayer;
using Xunit.Abstractions;

namespace Tents.Tests.Shell;

/// <summary>Reads the real "Show desktop icons" state. Read-only.</summary>
[Trait(TestCategories.Key, TestCategories.Desktop)]
public sealed class DesktopIconStateTests(ITestOutputHelper output) : IDisposable
{
    private readonly DesktopItemSource _source = new();

    public void Dispose() => _source.Dispose();

    [Fact]
    public async Task Switch_AgreesWithExplorersOwnRecord()
    {
        var shown = await new DesktopIconSwitch(_source).AreIconsShownAsync();
        var hideIcons = DesktopIconSettingWatcher.ReadHideIcons();

        output.WriteLine($"icons shown: {shown}; HideIcons value: {hideIcons?.ToString() ?? "absent"}");
        if (hideIcons is { } hidden)
        {
            Assert.Equal(!hidden, shown);
        }
    }
}

/// <summary>
/// Switches the real desktop icons off and on. Changes the user's desktop for about a second per test, so these run
/// only when TENTS_ALLOW_DESKTOP_CHANGES=1 is set, and never in CI. Every test turns the icons back on in finally.
/// If a run is killed while they are off: right-click the desktop > View > Show desktop icons.
/// </summary>
[Trait(TestCategories.Key, TestCategories.DesktopChanges)]
public sealed class DesktopIconToggleTests(ITestOutputHelper output) : IDisposable
{
    private readonly DesktopItemSource _source = new();
    private readonly string _folder = Directory.CreateTempSubdirectory("tents-icon-toggle-test-").FullName;

    public void Dispose()
    {
        _source.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [DesktopChangesFact]
    public async Task TurnOffAndOn_RestoresTheIcons_AndTheItemListComesBack()
    {
        var iconSwitch = new DesktopIconSwitch(_source);
        Assert.True(await iconSwitch.AreIconsShownAsync(), "This test expects the desktop icons to be on when it starts.");
        var items = await ItemCountAsync();
        var file = new IconLayerStateFile(Path.Combine(_folder, "icon-layer.json"));
        using var guard = new IconLayerGuard(iconSwitch, file);
        try
        {
            var clock = Stopwatch.StartNew();
            Assert.Equal(TurnOffResult.TurnedOff, await guard.TurnIconsOffAsync());
            var offTook = clock.ElapsedMilliseconds;
            Assert.False(await iconSwitch.AreIconsShownAsync());
            Assert.True(file.Exists);
            var hiddenRecord = DesktopIconSettingWatcher.ReadHideIcons();
            var whileOff = await ItemCountAsync();

            clock.Restart();
            Assert.True(await guard.TurnIconsOnAsync());
            var onTook = clock.ElapsedMilliseconds;
            Assert.True(await iconSwitch.AreIconsShownAsync());
            Assert.False(file.Exists);

            // Spike 2: right after switching on the view lists 0 items for a moment. Measure how long (test only).
            clock.Restart();
            var firstRead = await ItemCountAsync();
            var back = firstRead;
            while (back != items && clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(50);
                back = await ItemCountAsync();
            }

            output.WriteLine($"items: {items}; while off: {whileOff}; first read after on: {firstRead}; back to {back} after {clock.ElapsedMilliseconds} ms");
            output.WriteLine($"switch off took {offTook} ms, on took {onTook} ms; HideIcons while off: {hiddenRecord?.ToString() ?? "absent"}, after: {DesktopIconSettingWatcher.ReadHideIcons()?.ToString() ?? "absent"}");
            Assert.Equal(items, whileOff);
            Assert.Equal(items, back);
        }
        finally
        {
            await EnsureIconsOnAsync(iconSwitch);
        }
    }

    [DesktopChangesFact]
    public async Task CrashWhileOff_IsRepairedAtTheNextStart()
    {
        var iconSwitch = new DesktopIconSwitch(_source);
        Assert.True(await iconSwitch.AreIconsShownAsync(), "This test expects the desktop icons to be on when it starts.");
        var file = new IconLayerStateFile(Path.Combine(_folder, "icon-layer.json"));
        try
        {
            // What a killed Tents leaves: the state file of a process that is gone, and the icons off.
            file.Write(new IconLayerState(1, int.MaxValue - 1, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow));
            await iconSwitch.SetIconsShownAsync(false);
            Assert.False(await iconSwitch.AreIconsShownAsync());

            using var nextStart = new IconLayerGuard(iconSwitch, file);
            Assert.Equal(StartupRecovery.RestoredIcons, await nextStart.RecoverAsync());

            Assert.True(await iconSwitch.AreIconsShownAsync());
            Assert.False(file.Exists);
        }
        finally
        {
            await EnsureIconsOnAsync(iconSwitch);
        }
    }

    [DesktopChangesFact]
    public async Task UserTurningIconsOnWhileTentsRuns_IsNoticed()
    {
        var iconSwitch = new DesktopIconSwitch(_source);
        Assert.True(await iconSwitch.AreIconsShownAsync(), "This test expects the desktop icons to be on when it starts.");
        var file = new IconLayerStateFile(Path.Combine(_folder, "icon-layer.json"));
        using var guard = new IconLayerGuard(iconSwitch, file);
        using var watcher = new DesktopIconSettingWatcher();
        using var changed = new SemaphoreSlim(0);
        try
        {
            await guard.TurnIconsOffAsync();
            watcher.Changed += (_, _) => changed.Release();
            Assert.True(watcher.Start());

            // The same flag the desktop's View > Show desktop icons sets.
            var clock = Stopwatch.StartNew();
            await iconSwitch.SetIconsShownAsync(true);
            Assert.True(await changed.WaitAsync(TimeSpan.FromSeconds(10)), "The setting change was not noticed.");
            output.WriteLine($"setting change noticed after {clock.ElapsedMilliseconds} ms");

            Assert.True(await guard.HandleUserTurnedIconsOnAsync());
            Assert.False(file.Exists);
        }
        finally
        {
            await EnsureIconsOnAsync(iconSwitch);
        }
    }

    private async Task<int> ItemCountAsync() => await _source.InvokeWithViewAsync(view => view.ItemCount());

    private static async Task EnsureIconsOnAsync(DesktopIconSwitch iconSwitch)
    {
        if (!await iconSwitch.AreIconsShownAsync())
        {
            await iconSwitch.SetIconsShownAsync(true);
        }
    }
}

/// <summary>A fact that is skipped unless TENTS_ALLOW_DESKTOP_CHANGES=1, so a desktop-changing test never runs by accident.</summary>
internal sealed class DesktopChangesFactAttribute : FactAttribute
{
    public const string Variable = "TENTS_ALLOW_DESKTOP_CHANGES";

    public DesktopChangesFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
        {
            Skip = $"Changes the desktop icons. Set {Variable}=1 to run (see CONTRIBUTING.md).";
        }
    }
}
