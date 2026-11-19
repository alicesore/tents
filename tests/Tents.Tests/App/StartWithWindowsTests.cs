using Tents.App;

namespace Tents.Tests.App;

/// <summary>
/// The Start with Windows toggle against an in-memory Run key. The real Run key is only read, never written.
/// </summary>
public sealed class StartWithWindowsTests
{
    private const string _exe = @"C:\Program Files\Tents\Tents.exe";
    private readonly MemoryRunKey _key = new();

    [Fact]
    public void Off_WhenThereIsNoEntry()
    {
        var startup = new StartWithWindows(_key, _exe);

        Assert.Equal(StartupState.Off, startup.State);
        Assert.False(startup.IsOn);
    }

    [Fact]
    public void TurningOn_WritesTheQuotedPath_UnderTentsOwnName()
    {
        var startup = new StartWithWindows(_key, _exe);

        startup.Set(true);

        Assert.Equal("\"C:\\Program Files\\Tents\\Tents.exe\"", _key.Values[StartWithWindows.ValueName]);
        Assert.Equal(StartupState.On, startup.State);
        Assert.Single(_key.Values);
    }

    [Fact]
    public void TurningOff_DeletesOnlyTentsValue()
    {
        _key.Values["SomethingElse"] = "\"C:\\Other\\other.exe\" --tray";
        var startup = new StartWithWindows(_key, _exe);
        startup.Set(true);

        startup.Set(false);

        Assert.Equal(StartupState.Off, startup.State);
        Assert.Equal(["SomethingElse"], _key.Values.Keys);
    }

    [Fact]
    public void AnEntryForAnotherCopy_IsOnElsewhere_AndTurningOnRewritesIt()
    {
        _key.Values[StartWithWindows.ValueName] = "\"D:\\Old\\Tents.exe\"";
        var startup = new StartWithWindows(_key, _exe);

        Assert.Equal(StartupState.OnElsewhere, startup.State);
        Assert.True(startup.IsOn);
        startup.Set(true);
        Assert.Equal(StartupState.On, startup.State);
    }

    [Fact]
    public void Set_DoesNotWriteWhenNothingChanges()
    {
        var startup = new StartWithWindows(_key, _exe);

        startup.Set(false);
        startup.Set(true);
        startup.Set(true);

        Assert.Equal(1, _key.Writes);
    }

    [Theory]
    [InlineData("\"C:\\A B\\Tents.exe\"", "C:\\A B\\Tents.exe")]
    [InlineData("\"C:\\A B\\Tents.exe\" --minimized", "C:\\A B\\Tents.exe")]
    [InlineData("C:\\Tents\\Tents.exe", "C:\\Tents\\Tents.exe")]
    [InlineData("  \"C:\\x.exe", "C:\\x.exe")]
    public void PathOf_ReadsQuotedAndPlainValues(string value, string path) => Assert.Equal(path, StartWithWindows.PathOf(value));

    [Fact]
    public void TheMenu_ShowsTheToggle()
    {
        var item = Assert.Single(TrayText.Menu(paused: false, startWithWindows: true), i => i.Id == TrayText.StartWithWindowsCommand);

        Assert.True(item.Checked);
        Assert.Equal("Start with &Windows", item.Text);
    }

    [Fact]
    public void TheRealRunKey_CanBeRead() => _ = new UserRunKey().GetValue("Tents.Tests.NoSuchValue." + Guid.NewGuid().ToString("N"));

    private sealed class MemoryRunKey : IRunKey
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int Writes { get; private set; }

        public string? GetValue(string name) => Values.GetValueOrDefault(name);

        public void SetValue(string name, string value)
        {
            Writes++;
            Values[name] = value;
        }

        public void DeleteValue(string name) => Values.Remove(name);
    }
}
