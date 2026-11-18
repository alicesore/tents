using Tents.Shell.Desktop;
using Xunit.Abstractions;

namespace Tents.Tests.Shell;

/// <summary>
/// Reads the real desktop through Explorer. Read-only. Output is counts only, never the names of the user's items.
/// </summary>
[Trait(TestCategories.Key, TestCategories.Desktop)]
public sealed class DesktopItemSourceTests(ITestOutputHelper output) : IDisposable
{
    private readonly DesktopItemSource _source = new();

    public void Dispose() => _source.Dispose();

    [Fact]
    public async Task Read_ListsExactlyTheItemsOfTheNativeView()
    {
        var nativeCount = await _source.InvokeWithViewAsync(view => view.ItemCount());

        var snapshot = await _source.ReadAsync();

        output.WriteLine($"native view items: {nativeCount}, listed: {snapshot.Count}, duplicates: {snapshot.DuplicateCount}, skipped: {snapshot.SkippedCount}, virtual: {snapshot.Items.Count(i => !i.IsFileSystem)}");
        Assert.Equal(nativeCount, snapshot.Count + snapshot.DuplicateCount + snapshot.SkippedCount);
        Assert.Equal(0, snapshot.DuplicateCount);
        Assert.Equal(0, snapshot.SkippedCount);
    }

    [Fact]
    public async Task Read_GivesChildrenOfTheDesktopRoot_WithFileItemsInTheDesktopFolders()
    {
        var snapshot = await _source.ReadAsync();
        string[] desktopFolders =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        ];

        var outside = 0;
        foreach (var item in snapshot.Items)
        {
            Assert.Equal(1, item.IdList.ItemCount);
            if (item.IsFileSystem)
            {
                Assert.True(Path.IsPathFullyQualified(item.Id.Value));
                var parent = Path.GetDirectoryName(item.Id.Value);
                if (!desktopFolders.Any(f => string.Equals(f, parent, StringComparison.OrdinalIgnoreCase)))
                {
                    outside++;
                }
            }
            else
            {
                Assert.True(DesktopItemIdentity.IsVirtual(item.Id));
            }
        }

        output.WriteLine($"file system items outside the two Desktop folders: {outside}");
        Assert.Equal(0, outside);
    }

    [Fact]
    public async Task Read_IsStable_AndChangesNothing()
    {
        var flagsBefore = await _source.InvokeWithViewAsync(view => view.GetFolderFlags());

        var first = await _source.ReadAsync();
        var second = await _source.ReadAsync();

        var flagsAfter = await _source.InvokeWithViewAsync(view => view.GetFolderFlags());
        Assert.Equal(flagsBefore, flagsAfter);
        Assert.True(second.DiffFrom(first).IsEmpty);
    }

    [Fact]
    public async Task InvalidateView_AcquiresTheViewAgain()
    {
        var first = await _source.ReadAsync();

        _source.InvalidateView();
        var second = await _source.ReadAsync();

        Assert.True(second.DiffFrom(first).IsEmpty);
    }

    [Fact]
    public async Task Read_DoesNotRunOnTheCallersThread()
    {
        var caller = Environment.CurrentManagedThreadId;

        var worker = await _source.InvokeWithViewAsync(_ => Environment.CurrentManagedThreadId);

        Assert.NotEqual(caller, worker);
    }
}
