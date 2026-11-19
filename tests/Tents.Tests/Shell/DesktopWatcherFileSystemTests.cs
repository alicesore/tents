using System.Collections.Concurrent;
using System.Diagnostics;
using Tents.Core;
using Tents.Shell.Desktop;

namespace Tents.Tests.Shell;

/// <summary>
/// A real FileSystemWatcher on a temporary folder (never the desktop) with the real clock. Runs in CI. Assertions
/// allow a burst to split into two windows on a slow machine, but never into one batch per file.
/// </summary>
/// <remarks>
/// <para>Files a test needs before its burst are written before the watcher starts, so their creation is never part of
/// the burst under test (where a rename or change would fold into it) and never raises a settle of its own (which would
/// end the next wait early).</para>
/// <para>A settle alone does not mean the batches have arrived. It fires once no event is younger than
/// <see cref="DesktopWatcherOptions.NativeViewLag"/>, and the batch with that last event is due up to
/// <see cref="DesktopWatcherOptions.CoalesceDelay"/> after it: here both are 300 ms, so an event that opens a new
/// coalesce window can settle and be batched at the same moment. Tests therefore also wait for the batches they assert
/// on (<see cref="WaitForBatchesMentioningAsync"/>).</para>
/// </remarks>
public sealed class DesktopWatcherFileSystemTests : IAsyncLifetime, IAsyncDisposable
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(15);

    private readonly string _folder = Directory.CreateTempSubdirectory("tents-watch-test-").FullName;
    private readonly DesktopWatcher _watcher;
    private readonly ConcurrentQueue<DesktopChangeBatch> _batches = new();
    private readonly SemaphoreSlim _settled = new(0);
    private readonly SemaphoreSlim _batchArrived = new(0);

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
        _watcher.ChangesReady += (_, batch) =>
        {
            _batches.Enqueue(batch);
            _batchArrived.Release();
        };
        _watcher.NativeViewSettled += (_, _) => _settled.Release();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    // IAsyncLifetime, not IAsyncDisposable: xUnit 2 does not call IAsyncDisposable on test classes.
    public async Task DisposeAsync()
    {
        await _watcher.DisposeAsync();
        _settled.Dispose();
        _batchArrived.Dispose();
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task TwentyNewFiles_ArriveInAtMostTwoBatches()
    {
        StartWatching();
        var files = Enumerable.Range(0, 20).Select(i => Path.Combine(_folder, $"file-{i:D2}.txt")).ToList();
        foreach (var file in files)
        {
            await File.WriteAllTextAsync(file, "x");
        }

        await WaitForSettledAsync();
        await WaitForBatchesMentioningAsync([.. files]);

        Assert.InRange(_batches.Count, 1, 2);
        var added = _batches.SelectMany(b => b.Added).ToHashSet();
        Assert.Equal(20, added.Count);
        Assert.All(files, f => Assert.Contains(new ItemId(f), added));
    }

    [Fact]
    public async Task RenameAndDelete_AreReportedWithIds()
    {
        var keep = Path.Combine(_folder, "keep.txt");
        var gone = Path.Combine(_folder, "gone.txt");
        await File.WriteAllTextAsync(keep, "x");
        await File.WriteAllTextAsync(gone, "x");
        StartWatching();

        var renamed = Path.Combine(_folder, "renamed.txt");
        File.Move(keep, renamed);
        File.Delete(gone);
        await WaitForSettledAsync();
        await WaitForBatchesMentioningAsync(renamed, gone);

        var renames = _batches.SelectMany(b => b.Renamed).ToList();
        Assert.Contains((new ItemId(keep), new ItemId(renamed)), renames);
        Assert.Contains(new ItemId(gone), _batches.SelectMany(b => b.Removed));
    }

    [Fact]
    public async Task ContentChange_IsAChange()
    {
        var file = Path.Combine(_folder, "doc.txt");
        await File.WriteAllTextAsync(file, "x");
        StartWatching();

        await File.AppendAllTextAsync(file, "more");
        await WaitForSettledAsync();
        await WaitForBatchesMentioningAsync(file);

        Assert.Contains(new ItemId(file), _batches.SelectMany(b => b.Changed));
    }

    private void StartWatching() => Assert.Equal(1, _watcher.Start());

    private async Task WaitForSettledAsync()
    {
        if (!await _settled.WaitAsync(_timeout))
        {
            throw new TimeoutException("The watcher did not settle.");
        }
    }

    /// <summary>
    /// Waits until the batches mention every path, in any role (added, removed, changed, or either side of a rename),
    /// so the assertions that follow see those batches and report what they got. Woken by each batch; no polling.
    /// </summary>
    private async Task WaitForBatchesMentioningAsync(params string[] paths)
    {
        var start = Stopwatch.GetTimestamp();
        while (true)
        {
            var mentioned = _batches
                .SelectMany(b => b.Added.Concat(b.Removed).Concat(b.Changed).Concat(b.Renamed.SelectMany(r => new[] { r.Old, r.New })))
                .ToHashSet();
            var missing = paths.Where(p => !mentioned.Contains(new ItemId(p))).ToList();
            if (missing.Count == 0)
            {
                return;
            }

            var left = _timeout - Stopwatch.GetElapsedTime(start);
            if (left <= TimeSpan.Zero || !await _batchArrived.WaitAsync(left))
            {
                throw new TimeoutException($"No batch mentioned {string.Join(", ", missing)}.");
            }
        }
    }
}
