using Tents.Core;
using Tents.Shell.Desktop;
using Tents.Shell.Interop;
using Tents.Shell.Menus;
using Xunit.Abstractions;

namespace Tents.Tests.Shell;

/// <summary>
/// Builds real shell context menus without showing them and without running any command. Uses a temporary file for
/// verb checks; desktop items are only counted. Nothing is opened, so nothing changes.
/// </summary>
[Trait(TestCategories.Key, TestCategories.Desktop)]
public sealed class ShellActionsTests(ITestOutputHelper output) : IDisposable
{
    private readonly ShellActions _actions = new();
    private readonly string _folder = Directory.CreateTempSubdirectory("tents-menu-test-").FullName;

    public void Dispose()
    {
        _actions.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task ItemFromPath_HasThePathAsId()
    {
        var path = CreateFile("note.txt");

        var item = await _actions.InvokeAsync(() => DesktopItemFactory.FromParsingName(path));

        Assert.NotNull(item);
        Assert.Equal(new ItemId(path), item.Id);
        Assert.True(item.IsFileSystem);
        Assert.False(item.IsFolder);
        Assert.Equal("note.txt", Path.GetFileName(item.Id.Value));
    }

    [Fact]
    public async Task ItemFromAMissingPath_IsNull() =>
        Assert.Null(await _actions.InvokeAsync(() => DesktopItemFactory.FromParsingName(Path.Combine(_folder, "missing.txt"))));

    [Fact]
    public async Task FileMenu_HasTheStandardVerbs_AndRename()
    {
        var item = await ItemAsync(CreateFile("note.txt"));

        var verbs = await _actions.ReadMenuVerbsAsync([item]);

        output.WriteLine("verbs: " + string.Join(", ", verbs));
        Assert.Contains("properties", verbs, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("delete", verbs, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("copy", verbs, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rename", verbs, StringComparer.OrdinalIgnoreCase); // because of CMF_CANRENAME
    }

    [Fact]
    public async Task ExtendedMenu_HasAtLeastTheNormalVerbs()
    {
        var item = await ItemAsync(CreateFile("note.txt"));

        var normal = await _actions.ReadMenuVerbsAsync([item]);
        var extended = await _actions.ReadMenuVerbsAsync([item], new ContextMenuOptions(ExtendedVerbs: true));

        output.WriteLine($"normal: {normal.Count} verbs, extended: {extended.Count}; only in extended: {string.Join(", ", extended.Except(normal, StringComparer.OrdinalIgnoreCase))}");
        Assert.True(extended.Count >= normal.Count);
    }

    [Fact]
    public async Task MultiSelection_InOneFolder_BuildsOneMenu()
    {
        var a = await ItemAsync(CreateFile("a.txt"));
        var b = await ItemAsync(CreateFile("b.txt"));

        // Parsed one after the other: the folder entry in the two ID lists differs (its date changed), so the shell decides.
        var (_, children, dropped) = await _actions.InvokeAsync(() => ShellSelection.Group([a.IdList, b.IdList], ShellActions.ContextMenuHandle.SameFolder));
        var verbs = await _actions.ReadMenuVerbsAsync([a, b]);

        Assert.Equal(2, children.Count);
        Assert.Equal(0, dropped);
        Assert.Contains("delete", verbs, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EveryDesktopItem_HasAMenu_AndAllOfThemTogetherToo()
    {
        using var source = new DesktopItemSource();
        var snapshot = await source.ReadAsync();

        var perItem = new List<int>();
        foreach (var item in snapshot.Items)
        {
            perItem.Add((await _actions.ReadMenuVerbsAsync([item])).Count);
        }

        var (_, _, dropped) = ShellSelection.Group([.. snapshot.Items.Select(i => i.IdList)]);
        var together = snapshot.Count > 0 ? (await _actions.ReadMenuVerbsAsync(snapshot.Items)).Count : 0;

        output.WriteLine($"{snapshot.Count} desktop items; verbs per item: {string.Join(", ", perItem)}; all selected: {together} verbs, {dropped} dropped");
        Assert.All(perItem, count => Assert.True(count > 0));
        Assert.Equal(0, dropped);
    }

    private string CreateFile(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "Tents test file");
        return path;
    }

    private async Task<DesktopItem> ItemAsync(string path) =>
        await _actions.InvokeAsync(() => DesktopItemFactory.FromParsingName(path)) ?? throw new InvalidOperationException("No shell item.");
}
