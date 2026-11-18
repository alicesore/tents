using System.Collections.Concurrent;
using Tents.Shell.Desktop;
using Tents.Tests.Persistence;

namespace Tents.Tests.Shell;

/// <summary>The watcher's two stages on a manual clock, fed with synthetic events. No file system, no desktop.</summary>
public sealed class DesktopWatcherTests : IAsyncLifetime, IAsyncDisposable
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);
    private static readonly DesktopWatcherOptions _options = new()
    {
        CoalesceDelay = TimeSpan.FromMilliseconds(100),
        NativeViewLag = TimeSpan.FromSeconds(1.5),
        SettleWindow = TimeSpan.FromSeconds(2),
    };

    private readonly ManualTimeProvider _clock = new();
    private readonly DesktopWatcher _watcher;
    private readonly ConcurrentQueue<DesktopChangeBatch> _batches = new();
    private readonly SemaphoreSlim _batchRaised = new(0);
    private readonly SemaphoreSlim _settledRaised = new(0);
    private int _settledCount;

    public DesktopWatcherTests()
    {
        _watcher = new DesktopWatcher([], _options, _clock);
        _watcher.ChangesReady += (_, batch) =>
        {
            _batches.Enqueue(batch);
            _batchRaised.Release();
        };
        _watcher.NativeViewSettled += (_, _) =>
        {
            Interlocked.Increment(ref _settledCount);
            _settledRaised.Release();
        };
    }

    public Task InitializeAsync() => Task.CompletedTask;

    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    // IAsyncLifetime, not IAsyncDisposable: xUnit 2 does not call IAsyncDisposable on test classes.
    public async Task DisposeAsync()
    {
        await _watcher.DisposeAsync();
        _batchRaised.Dispose();
        _settledRaised.Dispose();
        _clock.Dispose();
    }

    [Fact]
    public void NothingChanging_CreatesNoTimer() => Assert.Equal(0, _clock.CreatedTimerCount);

    [Fact]
    public async Task Burst_IsOneBatch_ThenSettles_ThenNoTimerRemains()
    {
        for (var i = 0; i < 20; i++)
        {
            Created($"file-{i}.txt");
        }

        await WaitForTimersAsync(2); // one per stage, for the whole burst
        Assert.Equal(2, _clock.CreatedTimerCount);

        _clock.Advance(TimeSpan.FromMilliseconds(99));
        Assert.Empty(_batches);
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        await WaitAsync(_batchRaised);
        Assert.Equal(20, Assert.Single(_batches).Added.Count);

        _clock.Advance(TimeSpan.FromMilliseconds(1900));
        await WaitAsync(_settledRaised);
        Assert.Equal(1, _settledCount);

        await WaitForNoScheduledTimersAsync();
        Assert.Equal(2, _clock.CreatedTimerCount);
    }

    [Fact]
    public async Task EventLateInTheSettleWindow_SettlesOneWindowLater()
    {
        Created("first.txt");
        await WaitForTimersAsync(2);
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        await WaitAsync(_batchRaised);

        _clock.Advance(TimeSpan.FromMilliseconds(900)); // t = 1.0 s
        Created("late.txt");
        await WaitForTimersAsync(1); // a new window of the fast stage
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        await WaitAsync(_batchRaised);
        Assert.Equal(2, _batches.Count);

        _clock.Advance(TimeSpan.FromMilliseconds(900)); // t = 2.0 s: the late event is 1.0 s old, the view is not there yet
        await WaitForTimersAsync(1); // the settle stage starts one more window instead of raising
        Assert.Equal(0, _settledCount);

        _clock.Advance(TimeSpan.FromSeconds(2)); // t = 4.0 s
        await WaitAsync(_settledRaised);
        Assert.Equal(1, _settledCount);
        await WaitForNoScheduledTimersAsync();
    }

    [Fact]
    public async Task LongBurst_GivesOneBatchPerWindow_NotOnePerEvent()
    {
        Created("a.txt");
        await WaitForTimersAsync(2);
        _clock.Advance(TimeSpan.FromMilliseconds(50));
        Created("b.txt"); // same window
        _clock.Advance(TimeSpan.FromMilliseconds(50));
        await WaitAsync(_batchRaised);

        Created("c.txt"); // next window
        await WaitForTimersAsync(1);
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        await WaitAsync(_batchRaised);

        Assert.Equal([2, 1], _batches.Select(b => b.Added.Count));
    }

    [Fact]
    public async Task NetZeroBurst_RaisesNoBatch_ButStillSettles()
    {
        Created("tmp.txt");
        _watcher.OnRawChange(new RawChange(RawChangeKind.Deleted, Path("tmp.txt")));
        await WaitForTimersAsync(2);

        _clock.Advance(TimeSpan.FromSeconds(2));
        await WaitAsync(_settledRaised);

        Assert.Empty(_batches);
    }

    [Fact]
    public async Task AfterDispose_NothingIsRaised()
    {
        Created("a.txt");
        await WaitForTimersAsync(2);

        await _watcher.DisposeAsync();
        Created("b.txt");
        _clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Empty(_batches);
        Assert.Equal(0, _settledCount);
    }

    [Fact]
    public async Task ThrowingHandler_DoesNotStopLaterBatches()
    {
        var calls = 0;
        _watcher.ChangesReady += (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                throw new InvalidOperationException("Synthetic handler failure.");
            }
        };

        Created("a.txt");
        await WaitForTimersAsync(2);
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        await WaitAsync(_batchRaised);

        Created("b.txt");
        await WaitForTimersAsync(1);
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        await WaitAsync(_batchRaised);

        Assert.Equal(2, _batches.Count);
        Assert.Single(_batches.Last().Added);
    }

    [Fact]
    public void SettleWindowShorterThanTheLag_IsRejected() =>
        Assert.Throws<ArgumentException>(() => new DesktopWatcher([], new DesktopWatcherOptions { NativeViewLag = TimeSpan.FromSeconds(2), SettleWindow = TimeSpan.FromSeconds(1) }));

    private static string Path(string name) => @"C:\Users\Someone\Desktop\" + name;

    private static async Task WaitAsync(SemaphoreSlim signal)
    {
        if (!await signal.WaitAsync(_timeout))
        {
            throw new TimeoutException("The event was not raised.");
        }
    }

    private void Created(string name) => _watcher.OnRawChange(new RawChange(RawChangeKind.Created, Path(name)));

    private async Task WaitForTimersAsync(int count)
    {
        for (var i = 0; i < count; i++)
        {
            await _clock.WaitForTimerCreatedAsync();
        }
    }

    private Task WaitForNoScheduledTimersAsync()
    {
        Assert.True(SpinWait.SpinUntil(() => _clock.ScheduledTimerCount == 0, _timeout), "A timer is still scheduled.");
        return Task.CompletedTask;
    }
}
