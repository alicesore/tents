using System.Collections.Concurrent;
using Tents.Core.Persistence;

namespace Tents.Tests.Persistence;

public sealed class DebouncedSaverTests : IAsyncDisposable
{
    private static readonly TimeSpan _delay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    private readonly ManualTimeProvider _clock = new();
    private readonly ConcurrentQueue<string> _saved = new();
    private readonly SemaphoreSlim _saveCompleted = new(0);
    private readonly DebouncedSaver<string> _saver;

    private Func<string, Task> _onSave = _ => Task.CompletedTask;

    public DebouncedSaverTests()
    {
        _saver = new DebouncedSaver<string>(
            async value =>
            {
                await _onSave(value);
                _saved.Enqueue(value);
                _saveCompleted.Release();
            },
            _delay,
            _clock);
    }

    public async ValueTask DisposeAsync()
    {
        await _saver.DisposeAsync();
        _saveCompleted.Dispose();
        _clock.Dispose();
    }

    private async Task WaitForSaveAsync()
    {
        if (!await _saveCompleted.WaitAsync(_timeout))
        {
            throw new TimeoutException("The save did not happen.");
        }
    }

    /// <summary>
    /// Waits until the background task has ended by itself, without advancing the clock or flushing, so a worker
    /// that started another (empty) window after its save would time out here.
    /// </summary>
    private Task WaitForIdleAsync()
    {
        Assert.True(SpinWait.SpinUntil(() => !_saver.HasPendingChanges, _timeout), "The saver did not become idle.");
        return Task.CompletedTask;
    }

    [Fact]
    public void NoChange_CreatesNoTimer()
    {
        Assert.Equal(0, _clock.CreatedTimerCount);
        Assert.False(_saver.HasPendingChanges);
    }

    [Fact]
    public async Task OneChange_SavesOnceAfterTheDelay_ThenNoTimerRemains()
    {
        _saver.Schedule("a");
        await _clock.WaitForTimerCreatedAsync();

        Assert.Empty(_saved);
        Assert.Equal(1, _clock.ScheduledTimerCount);
        Assert.True(_saver.HasPendingChanges);

        _clock.Advance(_delay - TimeSpan.FromMilliseconds(1));
        Assert.Empty(_saved);

        _clock.Advance(TimeSpan.FromMilliseconds(1));
        await WaitForSaveAsync();
        await WaitForIdleAsync();

        Assert.Equal(["a"], _saved);
        Assert.Equal(0, _clock.ScheduledTimerCount);
        Assert.Equal(1, _clock.CreatedTimerCount);
        Assert.False(_saver.HasPendingChanges);
    }

    [Fact]
    public async Task Burst_IsCoalescedIntoOneSaveOfTheLatestValue()
    {
        _saver.Schedule("a");
        await _clock.WaitForTimerCreatedAsync();
        _saver.Schedule("b");
        _clock.Advance(TimeSpan.FromMilliseconds(500));
        _saver.Schedule("c");

        _clock.Advance(TimeSpan.FromMilliseconds(500));
        await WaitForSaveAsync();
        await WaitForIdleAsync();

        Assert.Equal(["c"], _saved);
        Assert.Equal(1, _clock.CreatedTimerCount);
    }

    [Fact]
    public async Task ChangesDuringTheWindow_DoNotExtendIt()
    {
        _saver.Schedule("a");
        await _clock.WaitForTimerCreatedAsync();
        for (var i = 0; i < 9; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(100));
            _saver.Schedule("change " + i);
        }

        // 900 ms of continuous changes; the save still happens 1 s after the first one.
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        await WaitForSaveAsync();

        Assert.Equal(["change 8"], _saved);
    }

    [Fact]
    public async Task ChangeDuringASave_StartsOneMoreWindow()
    {
        var releaseFirstSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _onSave = value =>
        {
            if (value != "a")
            {
                return Task.CompletedTask;
            }

            firstSaveStarted.TrySetResult();
            return releaseFirstSave.Task;
        };

        _saver.Schedule("a");
        await _clock.WaitForTimerCreatedAsync();
        _clock.Advance(_delay);
        await firstSaveStarted.Task.WaitAsync(_timeout);

        _saver.Schedule("b");
        releaseFirstSave.SetResult();
        await WaitForSaveAsync();
        await _clock.WaitForTimerCreatedAsync();

        Assert.Equal(["a"], _saved);
        _clock.Advance(_delay);
        await WaitForSaveAsync();
        await WaitForIdleAsync();

        Assert.Equal(["a", "b"], _saved);
        Assert.Equal(2, _clock.CreatedTimerCount);
        Assert.Equal(0, _clock.ScheduledTimerCount);
    }

    [Fact]
    public async Task Flush_SavesImmediatelyAndReleasesTheTimer()
    {
        _saver.Schedule("a");
        await _clock.WaitForTimerCreatedAsync();

        await _saver.FlushAsync().WaitAsync(_timeout);

        Assert.Equal(["a"], _saved);
        Assert.Equal(0, _clock.ScheduledTimerCount);
        Assert.False(_saver.HasPendingChanges);
    }

    [Fact]
    public async Task Flush_BeforeTheWorkerStartedItsTimer_StillSaves()
    {
        _saver.Schedule("a");

        await _saver.FlushAsync().WaitAsync(_timeout);

        Assert.Equal(["a"], _saved);
        Assert.Equal(0, _clock.ScheduledTimerCount);
    }

    [Fact]
    public void Flush_WithNothingPending_CompletesAtOnce() => Assert.True(_saver.FlushAsync().IsCompleted);

    [Fact]
    public async Task FailedSave_IsReportedKeptPendingAndNotRetriedOnATimer()
    {
        var failures = new ConcurrentQueue<Exception>();
        var failed = new SemaphoreSlim(0);
        _saver.SaveFailed += (_, e) =>
        {
            failures.Enqueue(e.Exception);
            failed.Release();
        };
        var attempts = 0;
        _onSave = _ => Interlocked.Increment(ref attempts) == 1
            ? Task.FromException(new IOException("disk full"))
            : Task.CompletedTask;

        _saver.Schedule("a");
        await _clock.WaitForTimerCreatedAsync();
        _clock.Advance(_delay);
        Assert.True(await failed.WaitAsync(_timeout));

        Assert.IsType<IOException>(Assert.Single(failures));
        Assert.True(_saver.HasPendingChanges);
        Assert.Empty(_saved);
        Assert.Equal(0, _clock.ScheduledTimerCount);

        await _saver.FlushAsync().WaitAsync(_timeout);

        Assert.Equal(["a"], _saved);
        Assert.False(_saver.HasPendingChanges);
    }

    [Fact]
    public async Task Dispose_FlushesAndRejectsNewChanges()
    {
        _saver.Schedule("a");

        await _saver.DisposeAsync();

        Assert.Equal(["a"], _saved);
        Assert.Throws<ObjectDisposedException>(() => _saver.Schedule("b"));
    }

    [Fact]
    public async Task SystemClock_SavesAfterARealDelay()
    {
        var saved = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var saver = new DebouncedSaver<string>(
            value =>
            {
                saved.TrySetResult(value);
                return ValueTask.CompletedTask;
            },
            TimeSpan.FromMilliseconds(20));

        saver.Schedule("real");

        Assert.Equal("real", await saved.Task.WaitAsync(_timeout));
    }
}
