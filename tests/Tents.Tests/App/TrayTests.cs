using Tents.App;
using Tents.Shell.IconLayer;
using Tents.Shell.Tray;

namespace Tents.Tests.App;

/// <summary>Tray icon, menu and the native icon layer as the app drives it. No real tray, no real desktop.</summary>
public sealed class TrayTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("tents-tray-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void TheAppIcon_IsEmbedded_WithEveryTraySize()
    {
        using var stream = typeof(AppOptions).Assembly.GetManifestResourceStream("Tents.ico");
        Assert.NotNull(stream);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);

        var images = IcoFile.ReadDirectory(copy.ToArray());

        Assert.Equal([16, 20, 24, 32, 40, 48, 64, 256], images.Select(i => i.Size));
        Assert.All(images, i => Assert.Equal(0x89, copy.ToArray()[i.Offset])); // PNG payloads
    }

    [Theory]
    [InlineData(16, 16)]
    [InlineData(18, 20)]
    [InlineData(32, 32)]
    [InlineData(300, 256)]
    public void Pick_TheSmallestImageAtLeastThatLarge(int wanted, int picked)
    {
        IcoImage[] images = [new(16, 0, 1), new(20, 0, 1), new(32, 0, 1), new(256, 0, 1)];

        Assert.Equal(picked, IcoFile.Pick(images, wanted).Size);
    }

    [Fact]
    public void ReadDirectory_RejectsOtherFiles_AndSkipsEntriesOutsideTheFile()
    {
        Assert.Throws<FormatException>(() => IcoFile.ReadDirectory(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0 }));

        // One entry claiming 100 bytes at offset 22 in a 22-byte file.
        var file = new byte[22];
        file[2] = 1;
        file[4] = 1;
        file[6] = 16;
        file[14] = 100;
        file[18] = 22;
        Assert.Empty(IcoFile.ReadDirectory(file));
        Assert.Throws<ArgumentException>(() => IcoFile.Pick([], 16));
    }

    [Theory]
    [InlineData(0x007B, TrayRequest.Menu)] // WM_CONTEXTMENU
    [InlineData(0x0400, TrayRequest.Menu)] // NIN_SELECT (left click)
    [InlineData(0x0401, TrayRequest.Menu)] // NIN_KEYSELECT
    [InlineData(0x0200, TrayRequest.None)] // WM_MOUSEMOVE
    [InlineData(0x0406, TrayRequest.None)] // NIN_POPUPOPEN
    public void Classify_VersionFourCallbacks(int notification, TrayRequest expected)
    {
        var wParam = unchecked((nint)0xFFEC_05DC); // anchor (1500, -20): a monitor above the primary
        var (request, x, y) = TrayIcon.Classify(wParam, notification | (1 << 16));

        Assert.Equal(expected, request);
        Assert.Equal(1500, x);
        Assert.Equal(-20, y);
    }

    [Fact]
    public void Menu_PauseIsACheckboxNamedForWhatItDoes()
    {
        var running = TrayText.Menu(paused: false);
        var paused = TrayText.Menu(paused: true);

        var pause = Assert.Single(running, i => i.Id == TrayText.PauseCommand);
        Assert.Equal("&Show Windows desktop icons", pause.Text);
        Assert.False(pause.Checked);
        Assert.True(Assert.Single(paused, i => i.Id == TrayText.PauseCommand).Checked);
        Assert.False(Assert.Single(paused, i => i.Id == TrayText.NewTentCommand).Enabled);
        Assert.False(Assert.Single(running, i => i.Id == TrayText.SettingsCommand).Enabled);
        Assert.Equal(TrayText.ExitCommand, running[^1].Id);
        Assert.Equal("Tents (paused)", TrayText.Tooltip(paused: true));
    }

    [Fact]
    public async Task IconLayer_WithSwitchingDisabled_NeverTouchesTheIcons()
    {
        var iconSwitch = new FakeSwitch();
        await using var log = new AppLog(Path.Combine(_folder, "log.txt"));
        using var guard = new IconLayerGuard(iconSwitch, new IconLayerStateFile(Path.Combine(_folder, "state.json")));
        var layer = new NativeIconLayer(guard, log, switchingEnabled: false);

        await layer.HideNativeIconsAsync();
        Assert.False(await layer.UserTurnedIconsOnAsync());

        Assert.True(iconSwitch.Shown);
        Assert.Equal(0, iconSwitch.SetCalls);
        Assert.False(layer.OwnsIconsOff);
    }

    [Fact]
    public async Task IconLayer_RecoversALeftoverStateFile_EvenWithSwitchingDisabled()
    {
        var stateFile = new IconLayerStateFile(Path.Combine(_folder, "state.json"));
        stateFile.Write(new IconLayerState(IconLayerState.CurrentVersion, 0, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch));
        var iconSwitch = new FakeSwitch { Shown = false };
        await using var log = new AppLog(Path.Combine(_folder, "log.txt"));
        using var guard = new IconLayerGuard(iconSwitch, stateFile);

        await new NativeIconLayer(guard, log, switchingEnabled: false).RecoverAsync();

        Assert.True(iconSwitch.Shown);
        Assert.False(stateFile.Exists);
    }

    [Fact]
    public async Task IconLayer_OffForTents_OnForPause_AndTheUsersSwitchMeansPause()
    {
        var iconSwitch = new FakeSwitch();
        await using var log = new AppLog(Path.Combine(_folder, "log.txt"));
        using var guard = new IconLayerGuard(iconSwitch, new IconLayerStateFile(Path.Combine(_folder, "state.json")));
        var layer = new NativeIconLayer(guard, log, switchingEnabled: true);

        await layer.HideNativeIconsAsync();
        Assert.False(iconSwitch.Shown);
        Assert.True(layer.OwnsIconsOff);
        Assert.False(await layer.UserTurnedIconsOnAsync());

        await layer.ShowNativeIconsAsync();
        Assert.True(iconSwitch.Shown);
        Assert.False(layer.OwnsIconsOff);

        await layer.HideNativeIconsAsync();
        iconSwitch.Shown = true; // the user ticks View > Show desktop icons
        Assert.True(await layer.UserTurnedIconsOnAsync());
        Assert.False(layer.OwnsIconsOff);
    }

    [Fact]
    public async Task IconLayer_AFailingSwitch_IsLoggedNotThrown()
    {
        var iconSwitch = new FakeSwitch { Fail = true };
        await using var log = new AppLog(Path.Combine(_folder, "log.txt"));
        using var guard = new IconLayerGuard(iconSwitch, new IconLayerStateFile(Path.Combine(_folder, "state.json")));
        var layer = new NativeIconLayer(guard, log, switchingEnabled: true);

        await layer.HideNativeIconsAsync();
        await layer.ShowNativeIconsAsync();
        Assert.False(await layer.UserTurnedIconsOnAsync());
    }

    private sealed class FakeSwitch : IDesktopIconSwitch
    {
        public bool Shown { get; set; } = true;

        public bool Fail { get; init; }

        public int SetCalls { get; private set; }

        public Task<bool> AreIconsShownAsync() =>
            Fail ? Task.FromException<bool>(new InvalidOperationException("No desktop view.")) : Task.FromResult(Shown);

        public Task SetIconsShownAsync(bool shown)
        {
            SetCalls++;
            Shown = shown;
            return Task.CompletedTask;
        }
    }
}
