using System.Collections.Concurrent;
using Tents.Core;
using Tents.Shell.Desktop;

namespace Tents.Tests.Shell;

/// <summary>
/// A real FileSystemWatcher on a temporary folder (never the desktop) with the real clock. Runs in CI. Assertions
/// allow a burst to split into two windows on a slow machine, but never into one batch per file.
/// </summary>
public sealed class DesktopWatcherFileSystemTests : IAsyncLifetime, IAsyncDisposable
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(15);

    private readonly string _folder = Directory.CreateTempSubdirectory("tents-watch-test-").FullName;
    private readonly DesktopWatcher _watcher;
    private readonly ConcurrentQueue<DesktopChangeBatch> _batches = new();
    private readonly SemaphoreSlim _settled = new(0);

    public DesktopWatcherFileSystemTests()
    {
        _watcher = new DesktopWatcher(
            [_folder],
            new DesktopWatcherOptions
            {
                CoalesceDelay = TimeSpan.FromMilliseconds(300),
                NativeViewLag = TimeSpan.FromMilliseconds(300),
                SettleWindow = TimeSpan.FromMilliseconds(600),
            });
        _watcher.ChangesReady += (_, batch) => _batches.Enqueue(batch);
        _watcher.NativeViewSettled += (_, _) => _settled.Release();
        Assert.Equal(1, _watcher.Start());
    }

    public Task InitializeAsync() => Task.CompletedTask;

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    // IAsyncLifetime, not IAsyncDisposable: xUnit 2 does not call IAsyncDisposable on test classes.
    public async Task DisposeAsync()
    {
        await _watcher.DisposeAsync();
        _settled.Dispose();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task TwentyNewFiles_ArriveInAtMostTwoBatches()
    {
        for (var i = 0; i < 20; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(_folder, $"file-{i:D2}.txt"), "x");
        }

        await WaitForSettledAsync();

        Assert.InRange(_batches.Count, 1, 2);
        var added = _batches.SelectMany(b => b.Added).ToHashSet();
        Assert.Equal(20, added.Count);
        Assert.All(Enumerable.Range(0, 20), i => Assert.Contains(new ItemId(Path.Combine(_folder, $"file-{i:D2}.txt")), added));
    }

    [Fact]
    public async Task RenameAndDelete_AreReportedWithIds()
    {
        var keep = Path.Combine(_folder, "keep.txt");
        var gone = Path.Combine(_folder, "gone.txt");
        await File.WriteAllTextAsync(keep, "x");
        await File.WriteAllTextAsync(gone, "x");
        await WaitForSettledAsync();
        _batches.Clear();

        var renamed = Path.Combine(_folder, "renamed.txt");
        File.Move(keep, renamed);
        File.Delete(gone);
        await WaitForSettledAsync();

        var renames = _batches.SelectMany(b => b.Renamed).ToList();
        Assert.Contains((new ItemId(keep), new ItemId(renamed)), renames);
        Assert.Contains(new ItemId(gone), _batches.SelectMany(b => b.Removed));
    }

    [Fact]
    public async Task ContentChange_IsAChange()
    {
        var file = Path.Combine(_folder, "doc.txt");
        await File.WriteAllTextAsync(file, "x");
        await WaitForSettledAsync();
        _batches.Clear();

        await File.AppendAllTextAsync(file, "more");
        await WaitForSettledAsync();

        Assert.Contains(new ItemId(file), _batches.SelectMany(b => b.Changed));
    }

    private async Task WaitForSettledAsync()
    {
        if (!await _settled.WaitAsync(_timeout))
        {
            throw new TimeoutException("The watcher did not settle.");
        }
    }
}
