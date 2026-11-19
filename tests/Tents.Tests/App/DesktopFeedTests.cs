using Tents.App.Items;

namespace Tents.Tests.App;

/// <summary>The folder check behind trusting an empty read. Temporary folders only.</summary>
public sealed class DesktopFeedTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("tents-feed-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void HasVisibleEntry_IgnoresHiddenAndSystemFiles()
    {
        Assert.False(DesktopFeed.HasVisibleEntry(_folder));

        var ini = Path.Combine(_folder, "desktop.ini");
        File.WriteAllText(ini, "x");
        File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
        Assert.False(DesktopFeed.HasVisibleEntry(_folder));

        Directory.CreateDirectory(Path.Combine(_folder, "Folder"));
        Assert.True(DesktopFeed.HasVisibleEntry(_folder));
    }

    [Fact]
    public void HasVisibleEntry_AMissingFolder_CannotBeTrusted() =>
        Assert.True(DesktopFeed.HasVisibleEntry(Path.Combine(_folder, "missing")));
}
