using System.Text;
using Tents.Shell.IconLayer;
using Tents.Tests.Persistence;

namespace Tents.Tests.Shell;

/// <summary>The state file and the icon layer guard, with a fake switch. No desktop is touched.</summary>
public sealed class IconLayerTests : IDisposable
{
    private static readonly DateTimeOffset _ourStart = new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

    private readonly string _folder = Directory.CreateTempSubdirectory("tents-icon-layer-test-").FullName;
    private readonly IconLayerStateFile _file;
    private readonly FakeSwitch _switch = new();
    private readonly FakeProbe _probe = new() { Current = (4242, _ourStart) };
    private readonly ManualTimeProvider _clock = new();

    public IconLayerTests() => _file = new IconLayerStateFile(Path.Combine(_folder, "Tents", "icon-layer.json"));

    public void Dispose()
    {
        _clock.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    // ---- state file ----

    [Fact]
    public void StateFile_RoundTrips_AndLeavesNoTempFile()
    {
        var state = new IconLayerState(1, 4242, _ourStart, _ourStart.AddSeconds(5));

        _file.Write(state);

        Assert.Equal(state, _file.Read());
        Assert.False(File.Exists(_file.FilePath + ".tmp"));
    }

    [Fact]
    public void StateFile_Format_IsVersionedCamelCaseJson()
    {
        _file.Write(new IconLayerState(1, 4242, _ourStart, _ourStart));

        var json = File.ReadAllText(_file.FilePath);

        Assert.Contains("\"version\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"processId\": 4242", json, StringComparison.Ordinal);
        Assert.Contains("\"processStartTime\"", json, StringComparison.Ordinal);
        Assert.Contains("\"turnedOffAt\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void StateFile_Missing_IsNull() => Assert.Null(_file.Read());

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("{ \"version\": 1 }")]
    [InlineData("null")]
    public void StateFile_Unreadable_StillMeansTurnTheIconsOn(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file.FilePath)!);
        File.WriteAllText(_file.FilePath, content, Encoding.UTF8);

        var state = _file.Read();

        Assert.NotNull(state);
        Assert.Equal(0, state.ProcessId);
    }

    [Fact]
    public void StateFile_Delete_IsIdempotent()
    {
        _file.Write(new IconLayerState(1, 1, _ourStart, _ourStart));

        _file.Delete();
        _file.Delete();

        Assert.False(_file.Exists);
    }

    [Fact]
    public void StateFile_DefaultLocation_IsLocalAppData() => Assert.Equal(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tents", "icon-layer.json"),
        IconLayerStateFile.DefaultFilePath);

    // ---- turning off ----

    [Fact]
    public async Task TurnOff_WritesTheStateFileBeforeSwitching()
    {
        using var guard = Guard();
        _switch.OnSet = shown => Assert.True(shown || _file.Exists, "The icons were switched off before the state file existed.");

        Assert.Equal(TurnOffResult.TurnedOff, await guard.TurnIconsOffAsync());

        Assert.False(_switch.Shown);
        Assert.True(guard.OwnsIconsOff);
        var state = _file.Read()!;
        Assert.Equal(4242, state.ProcessId);
        Assert.Equal(_ourStart, state.ProcessStartTime);
        Assert.Equal(_clock.GetUtcNow(), state.TurnedOffAt);
    }

    [Fact]
    public async Task TurnOff_Twice_IsAlreadyOffByTents()
    {
        using var guard = Guard();
        await guard.TurnIconsOffAsync();

        Assert.Equal(TurnOffResult.AlreadyOffByTents, await guard.TurnIconsOffAsync());
        Assert.Equal(1, _switch.SetCalls);
    }

    [Fact]
    public async Task TurnOff_WhenTheUserHadThemOff_TakesNoOwnership()
    {
        _switch.Shown = false;
        using var guard = Guard();

        Assert.Equal(TurnOffResult.AlreadyOffByUser, await guard.TurnIconsOffAsync());

        Assert.False(_file.Exists);
        Assert.False(await guard.TurnIconsOnAsync());
        Assert.False(_switch.Shown); // the user's setting is left alone
    }

    [Fact]
    public async Task TurnOff_WhenTheStateFileCannotBeWritten_DoesNotSwitch()
    {
        File.WriteAllText(Path.Combine(_folder, "blocker"), "a file where the folder should be");
        using var guard = new IconLayerGuard(_switch, new IconLayerStateFile(Path.Combine(_folder, "blocker", "icon-layer.json")), _probe, _clock);

        await Assert.ThrowsAnyAsync<IOException>(guard.TurnIconsOffAsync);

        Assert.True(_switch.Shown);
        Assert.Equal(0, _switch.SetCalls);
    }

    [Fact]
    public async Task TurnOff_WhenTheSwitchFailsAndIconsStayOn_LeavesNoStateFile()
    {
        _switch.FailNextSet = true;
        using var guard = Guard();

        await Assert.ThrowsAsync<InvalidOperationException>(guard.TurnIconsOffAsync);

        Assert.True(_switch.Shown);
        Assert.False(_file.Exists);
    }

    [Fact]
    public async Task TurnOff_WhenTheSwitchFailsButIconsWentOff_KeepsTheStateFile()
    {
        _switch.FailNextSet = true;
        _switch.ApplyBeforeFailing = true;
        using var guard = Guard();

        await Assert.ThrowsAsync<InvalidOperationException>(guard.TurnIconsOffAsync);

        Assert.False(_switch.Shown);
        Assert.True(_file.Exists); // still recoverable at the next start
    }

    // ---- turning on ----

    [Fact]
    public async Task TurnOn_SwitchesBeforeDeletingTheStateFile()
    {
        using var guard = Guard();
        await guard.TurnIconsOffAsync();
        _switch.OnSet = shown => Assert.True(!shown || _file.Exists, "The state file was deleted before the icons were on.");

        Assert.True(await guard.TurnIconsOnAsync());

        Assert.True(_switch.Shown);
        Assert.False(_file.Exists);
    }

    [Fact]
    public async Task TurnOn_WhenTheSwitchFails_KeepsTheStateFile()
    {
        using var guard = Guard();
        await guard.TurnIconsOffAsync();
        _switch.FailNextSet = true;

        await Assert.ThrowsAsync<InvalidOperationException>(guard.TurnIconsOnAsync);

        Assert.False(_switch.Shown);
        Assert.True(_file.Exists);
    }

    [Fact]
    public async Task TurnOn_WhenIconsAreAlreadyOn_OnlyDropsOwnership()
    {
        using var guard = Guard();
        await guard.TurnIconsOffAsync();
        _switch.Shown = true; // for example after an Explorer restart with the setting reset

        Assert.False(await guard.TurnIconsOnAsync());

        Assert.False(_file.Exists);
        Assert.Equal(1, _switch.SetCalls);
    }

    // ---- the user turns icons back on ----

    [Fact]
    public async Task UserTurningIconsOn_EndsOwnership_AndMeansPause()
    {
        using var guard = Guard();
        await guard.TurnIconsOffAsync();
        _switch.Shown = true; // View > Show desktop icons

        Assert.True(await guard.HandleUserTurnedIconsOnAsync());

        Assert.False(_file.Exists);
        Assert.False(await guard.TurnIconsOnAsync()); // Exit after that changes nothing
    }

    [Fact]
    public async Task OurOwnSwitchOff_IsNotMistakenForTheUser()
    {
        using var guard = Guard();
        await guard.TurnIconsOffAsync();

        Assert.False(await guard.HandleUserTurnedIconsOnAsync());
        Assert.True(_file.Exists);
    }

    [Fact]
    public async Task SettingChange_WithoutOwnership_IsIgnored()
    {
        using var guard = Guard();

        Assert.False(await guard.HandleUserTurnedIconsOnAsync());
    }

    // ---- recovery at startup ----

    [Fact]
    public async Task Recover_WithoutStateFile_DoesNothing()
    {
        _switch.Shown = false; // the user's own choice
        using var guard = Guard();

        Assert.Equal(StartupRecovery.NothingToDo, await guard.RecoverAsync());
        Assert.False(_switch.Shown);
        Assert.Equal(0, _switch.SetCalls);
    }

    [Fact]
    public async Task Recover_AfterACrashWithIconsOff_TurnsThemOn()
    {
        using (var crashed = Guard(new FakeProbe { Current = (1111, _ourStart.AddHours(-1)) }))
        {
            await crashed.TurnIconsOffAsync(); // and then the process dies without TurnIconsOnAsync
        }

        using var next = Guard(); // the probe says process 1111 is not running
        Assert.Equal(StartupRecovery.RestoredIcons, await next.RecoverAsync());

        Assert.True(_switch.Shown);
        Assert.False(_file.Exists);
    }

    [Fact]
    public async Task Recover_AfterACrash_WithIconsAlreadyOn_OnlyCleansUp()
    {
        _file.Write(new IconLayerState(1, 1111, _ourStart, _ourStart));
        using var guard = Guard();

        Assert.Equal(StartupRecovery.IconsWereAlreadyOn, await guard.RecoverAsync());

        Assert.Equal(0, _switch.SetCalls);
        Assert.False(_file.Exists);
    }

    [Fact]
    public async Task Recover_WhileTheOwnerStillRuns_ChangesNothing()
    {
        _file.Write(new IconLayerState(1, 1111, _ourStart, _ourStart));
        _switch.Shown = false;
        _probe.Running.Add((1111, _ourStart));
        using var guard = Guard();

        Assert.Equal(StartupRecovery.OwnerStillRunning, await guard.RecoverAsync());

        Assert.False(_switch.Shown);
        Assert.True(_file.Exists);
    }

    [Fact]
    public async Task Recover_WithAReusedProcessId_TurnsTheIconsOn()
    {
        _file.Write(new IconLayerState(1, 1111, _ourStart, _ourStart));
        _switch.Shown = false;
        _probe.Running.Add((1111, _ourStart.AddMinutes(30))); // same id, another process
        using var guard = Guard();

        Assert.Equal(StartupRecovery.RestoredIcons, await guard.RecoverAsync());
        Assert.True(_switch.Shown);
    }

    [Fact]
    public async Task Recover_WithAnUnreadableStateFile_TurnsTheIconsOn()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file.FilePath)!);
        File.WriteAllText(_file.FilePath, "garbage");
        _switch.Shown = false;
        using var guard = Guard();

        Assert.Equal(StartupRecovery.RestoredIcons, await guard.RecoverAsync());
        Assert.True(_switch.Shown);
        Assert.False(_file.Exists);
    }

    [Fact]
    public void ProcessProbe_SeesThisProcess_AndNotAMadeUpOne()
    {
        var probe = new SystemProcessProbe();

        Assert.Equal(Environment.ProcessId, probe.Current.ProcessId);
        Assert.True(probe.IsRunning(probe.Current.ProcessId, probe.Current.StartTime));
        Assert.False(probe.IsRunning(probe.Current.ProcessId, probe.Current.StartTime.AddMinutes(-5)));
        Assert.False(probe.IsRunning(int.MaxValue - 1, probe.Current.StartTime));
    }

    private IconLayerGuard Guard(FakeProbe? probe = null) => new(_switch, _file, probe ?? _probe, _clock);

    private sealed class FakeSwitch : IDesktopIconSwitch
    {
        public bool Shown { get; set; } = true;

        public int SetCalls { get; private set; }

        public bool FailNextSet { get; set; }

        public bool ApplyBeforeFailing { get; set; }

        public Action<bool>? OnSet { get; set; }

        public Task<bool> AreIconsShownAsync() => Task.FromResult(Shown);

        public Task SetIconsShownAsync(bool shown)
        {
            SetCalls++;
            OnSet?.Invoke(shown);
            if (FailNextSet)
            {
                FailNextSet = false;
                if (ApplyBeforeFailing)
                {
                    Shown = shown;
                }

                throw new InvalidOperationException("Synthetic switch failure.");
            }

            Shown = shown;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProbe : IProcessProbe
    {
        public HashSet<(int, DateTimeOffset)> Running { get; } = [];

        public (int ProcessId, DateTimeOffset StartTime) Current { get; init; } = (4242, _ourStart);

        public bool IsRunning(int processId, DateTimeOffset startTime) => Running.Contains((processId, startTime));
    }
}
