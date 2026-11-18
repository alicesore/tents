using Tents.Shell.Desktop;
using Xunit.Abstractions;

namespace Tents.Tests.Shell;

/// <summary>Watches the real desktop folders without touching them. Prints counts only.</summary>
[Trait(TestCategories.Key, TestCategories.Desktop)]
public class DesktopWatcherDesktopTests(ITestOutputHelper output)
{
    [Fact]
    public async Task WatchesTheUserAndPublicDesktopFolders()
    {
        var folders = DesktopWatcher.GetDesktopFolders();
        await using var watcher = new DesktopWatcher(folders);

        var watched = watcher.Start();

        output.WriteLine($"desktop folders found: {folders.Count}, watched: {watched}");
        Assert.InRange(folders.Count, 1, 2);
        Assert.Equal(folders.Count, watched);
        Assert.Contains(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), folders, StringComparer.OrdinalIgnoreCase);
    }
}
