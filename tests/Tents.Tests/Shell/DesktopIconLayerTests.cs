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
        var hideIcons = ExplorerRecord.ReadHideIcons();

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
            var hiddenRecord = ExplorerRecord.ReadHideIcons();

            // The first run saw 0 items 7 ms after switching off. Spike 2 saw the list kept while hidden, so measure
            // whether it comes back while the icons stay off (test only; the guard never reads items).
            var (offFirst, offBack, offBackAfter) = await WaitForItemCountAsync(items);

            clock.Restart();
            Assert.True(await guard.TurnIconsOnAsync());
            var onTook = clock.ElapsedMilliseconds;
            Assert.True(await iconSwitch.AreIconsShownAsync());
            Assert.False(file.Exists);

            // Spike 2: right after switching on the view lists 0 items for a moment. Measure how long (test only).
            var (onFirst, onBack, onBackAfter) = await WaitForItemCountAsync(items);

            output.WriteLine($"items: {items}");
            output.WriteLine($"while off: first read {offFirst}, then {offBack} after {offBackAfter} ms");
            output.WriteLine($"after on: first read {onFirst}, then {onBack} after {onBackAfter} ms");
            output.WriteLine($"switch off took {offTook} ms, on took {onTook} ms; HideIcons while off: {hiddenRecord?.ToString() ?? "absent"}, after: {ExplorerRecord.ReadHideIcons()?.ToString() ?? "absent"}");
            Assert.Equal(items, offBack);
            Assert.Equal(items, onBack);
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
    public async Task UserTurningIconsOnWhileTentsRuns_IsFoundByTheRecheck()
    {
        var iconSwitch = new DesktopIconSwitch(_source);
        Assert.True(await iconSwitch.AreIconsShownAsync(), "This test expects the desktop icons to be on when it starts.");
        var file = new IconLayerStateFile(Path.Combine(_folder, "icon-layer.json"));
        using var guard = new IconLayerGuard(iconSwitch, file);
        try
        {
            Assert.Equal(TurnOffResult.TurnedOff, await guard.TurnIconsOffAsync());
            Assert.False(await guard.RecheckAsync()); // Tents' own switch-off is not the user
            Assert.True(file.Exists);

            // The same flag the desktop's View > Show desktop icons sets.
            await iconSwitch.SetIconsShownAsync(true);

            Assert.True(await guard.RecheckAsync());
            Assert.False(file.Exists);
            Assert.True(await iconSwitch.AreIconsShownAsync());
        }
        finally
        {
            await EnsureIconsOnAsync(iconSwitch);
        }
    }

    private async Task<int> ItemCountAsync() => await _source.InvokeWithViewAsync(view => view.ItemCount());

    // Test-only measurement: re-reads every 50 ms for up to 10 s until the view lists the expected count again.
    private async Task<(int First, int Last, long AfterMs)> WaitForItemCountAsync(int expected)
    {
        var clock = Stopwatch.StartNew();
        var first = await ItemCountAsync();
        var last = first;
        while (last != expected && clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(50);
            last = await ItemCountAsync();
        }

        return (first, last, clock.ElapsedMilliseconds);
    }

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

/// <summary>
/// Explorer's own record of "Show desktop icons" (<c>HideIcons</c>, undocumented). Test diagnostics only: the desktop
/// view is the source of truth, and Explorer's switch raises no registry change notification on this value.
/// </summary>
internal static class ExplorerRecord
{
    private const string _advancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    public static bool? ReadHideIcons()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(_advancedKey);
        return key?.GetValue("HideIcons") is int value ? value != 0 : null;
    }
}
