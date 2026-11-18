using Tents.Core;
using Tents.Shell.Desktop;

namespace Tents.Tests.Shell;

/// <summary>
/// Resolving watcher paths to items before the native view lists them. Uses files in a temporary folder, never the
/// desktop; no desktop view is acquired.
/// </summary>
public sealed class DesktopItemResolveTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("tents-resolve-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void IsHiddenFile_SeesHiddenAndSystemFiles()
    {
        var visible = Create("visible.txt");
        var hidden = Create("hidden.txt", FileAttributes.Hidden);
        var system = Create("desktop.ini", FileAttributes.System | FileAttributes.Hidden);

        Assert.False(DesktopItemSource.IsHiddenFile(visible));
        Assert.True(DesktopItemSource.IsHiddenFile(hidden));
        Assert.True(DesktopItemSource.IsHiddenFile(system));
        Assert.False(DesktopItemSource.IsHiddenFile(Path.Combine(_folder, "gone.txt")));
        Assert.False(DesktopItemSource.IsHiddenFile("::{645FF040-5081-101B-9F08-00AA002F954E}"));
    }

    [Fact]
    public async Task ResolveAsync_ReturnsVisibleExistingFiles_Only()
    {
        var visible = Create("visible.txt");
        var hidden = Create("hidden.txt", FileAttributes.Hidden);
        using var source = new DesktopItemSource();

        var items = await source.ResolveAsync([new ItemId(visible), new ItemId(hidden), new ItemId(Path.Combine(_folder, "gone.txt"))]);

        var item = Assert.Single(items);
        Assert.Equal(new ItemId(visible), item.Id);
        Assert.True(item.IsFileSystem);
        Assert.False(item.IsFolder);
    }

    private string Create(string name, FileAttributes attributes = FileAttributes.Normal)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "x");
        if (attributes != FileAttributes.Normal)
        {
            File.SetAttributes(path, attributes);
        }

        return path;
    }
}
