using System.Diagnostics;
using Tents.App.Safety;
using Tents.Shell.Desktop;
using Tents.Shell.IconLayer;

namespace Tents.Tests.App;

/// <summary>
/// Crash safety layers 1 and 2. The icon switch is a fake and the state file lives in a temporary folder, so nothing here
/// touches the desktop; the "Tents" process a watchdog guards is a short-lived <c>cmd.exe</c>.
/// </summary>
public sealed class CrashSafetyTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("tents-crash-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private IconLayerStateFile StateFile => new(Path.Combine(_folder, "icon-layer.json"));

    [Fact]
    public void WatchedProcess_RoundTripsThroughTheCommandLine()
    {
        var watched = new WatchedProcess(4242, new DateTimeOffset(2026, 11, 18, 9, 30, 15, TimeSpan.Zero).AddTicks(1234));

        Assert.True(WatchedProcess.TryParse(["--other", watched.ToArgument()], out var parsed));
        Assert.Equal(watched, parsed);
    }

    [Theory]
    [InlineData("--watchdog=")]
    [InlineData("--watchdog=12")]
    [InlineData("--watchdog=0:5")]
    [InlineData("--watchdog=-3:5")]
    [InlineData("--watchdog=abc:5")]
    [InlineData("--watchdog=12:x")]
    [InlineData("--watchdog=12:99999999999999999999")]
    public void WatchedProcess_RejectsBadArguments(string arg) => Assert.False(WatchedProcess.TryParse([arg], out _));

    [Fact]
    public void WatchedProcess_NotAWatchdogStart() => Assert.False(WatchedProcess.TryParse(["--keep-native-icons"], out _));

    [Fact]
    public void ShouldRecover_OnlyForTheWatchedProcess_OrAnUnreadableFile()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.True(Watchdog.ShouldRecover(new IconLayerState(1, 100, now, now), 100));
        Assert.True(Watchdog.ShouldRecover(new IconLayerState(1, 0, now, now), 100)); // unreadable file: restore
        Assert.False(Watchdog.ShouldRecover(new IconLayerState(1, 200, now, now), 100)); // a newer Tents owns it
        Assert.False(Watchdog.ShouldRecover(null, 100)); // clean exit
    }

    [Fact]
    public void WaitForExit_ReturnsWhenTheProcessEnds()
    {
        using var child = StartShortLivedProcess();
        var watched = new WatchedProcess(child.Id, new DateTimeOffset(child.StartTime.ToUniversalTime()));

        Assert.True(Watchdog.WaitForExit(watched));
        Assert.True(child.HasExited);
    }

    [Fact]
    public void WaitForExit_IgnoresAReusedProcessId()
    {
        using var current = Process.GetCurrentProcess();
        var watched = new WatchedProcess(current.Id, new DateTimeOffset(current.StartTime.ToUniversalTime()).AddMinutes(-10));

        Assert.False(Watchdog.WaitForExit(watched)); // same id, other start time: not ours, and no wait
    }

    [Fact]
    public void WaitForExit_AProcessThatIsGone() =>
        Assert.False(Watchdog.WaitForExit(new WatchedProcess(int.MaxValue - 7, DateTimeOffset.UtcNow)));

    [Fact]
    public async Task Watchdog_AfterTentsWasKilledWithTheIconsOff_TurnsThemBackOn()
    {
        using var child = StartShortLivedProcess();
        var watched = new WatchedProcess(child.Id, new DateTimeOffset(child.StartTime.ToUniversalTime()));
        StateFile.Write(new IconLayerState(IconLayerState.CurrentVersion, watched.ProcessId, watched.StartTime, DateTimeOffset.UtcNow));
        var iconSwitch = new FakeIconSwitch { Shown = false };

        Assert.True(Watchdog.WaitForExit(watched));
        Assert.True(Watchdog.ShouldRecover(StateFile.Read(), watched.ProcessId));
        await Watchdog.RecoverAsync(StateFile, iconSwitch);

        Assert.True(iconSwitch.Shown);
        Assert.False(StateFile.Exists);
    }

    [Fact]
    public void CrashHandler_TurnsTheIconsBackOn_WhenTentsHadThemOff()
    {
        var file = StateFile;
        file.Write(new IconLayerState(IconLayerState.CurrentVersion, Environment.ProcessId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var iconSwitch = new FakeIconSwitch { Shown = false };

        Assert.True(CrashHandler.RestoreIcons(file, _ => iconSwitch));

        Assert.True(iconSwitch.Shown);
        Assert.False(file.Exists);
    }

    [Fact]
    public void CrashHandler_WithoutAStateFile_DoesNothing()
    {
        var iconSwitch = new FakeIconSwitch { Shown = false }; // the user's own setting

        Assert.True(CrashHandler.RestoreIcons(StateFile, _ => iconSwitch));

        Assert.False(iconSwitch.Shown);
        Assert.Equal(0, iconSwitch.SetCalls);
    }

    [Fact]
    public void CrashHandler_AHangingSwitch_IsBounded()
    {
        var file = StateFile;
        file.Write(new IconLayerState(IconLayerState.CurrentVersion, Environment.ProcessId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        var iconSwitch = new FakeIconSwitch { Shown = false, Hang = true };

        var watch = Stopwatch.StartNew();
        Assert.False(CrashHandler.RestoreIcons(file, _ => iconSwitch));

        Assert.InRange(watch.Elapsed, TimeSpan.Zero, CrashHandler.RestoreTimeout + TimeSpan.FromSeconds(2));
        Assert.True(file.Exists); // left for the watchdog and the next start
    }

    [Fact]
    public void CrashNote_HasTypesAndStacks_NotMessages()
    {
        Exception thrown;
        try
        {
            throw new InvalidOperationException("outer", new FileNotFoundException(@"C:\Users\someone\Desktop\secret plans.txt"));
        }
        catch (InvalidOperationException ex)
        {
            thrown = ex;
        }

        var note = CrashHandler.Describe(thrown, new DateTimeOffset(2026, 11, 18, 10, 0, 0, TimeSpan.Zero));

        Assert.Contains("System.InvalidOperationException", note, StringComparison.Ordinal);
        Assert.Contains("System.IO.FileNotFoundException", note, StringComparison.Ordinal);
        Assert.Contains(nameof(CrashNote_HasTypesAndStacks_NotMessages), note, StringComparison.Ordinal);
        Assert.DoesNotContain("secret plans", note, StringComparison.Ordinal);
        Assert.DoesNotContain("outer", note, StringComparison.Ordinal);
    }

    private static Process StartShortLivedProcess() =>
        Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false, CreateNoWindow = true })
        ?? throw new InvalidOperationException("cmd.exe did not start.");
}

/// <summary>A desktop icon switch that only remembers its state.</summary>
internal sealed class FakeIconSwitch : IDesktopIconSwitch
{
    public bool Shown { get; set; } = true;

    public bool Hang { get; init; }

    public int SetCalls { get; private set; }

    public Task<bool> AreIconsShownAsync() => Hang ? new TaskCompletionSource<bool>().Task : Task.FromResult(Shown);

    public Task SetIconsShownAsync(bool shown)
    {
        SetCalls++;
        Shown = shown;
        return Task.CompletedTask;
    }
}
