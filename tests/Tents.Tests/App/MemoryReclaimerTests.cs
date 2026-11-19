using Tents.App;
using Tents.Tests.Persistence;

namespace Tents.Tests.App;

public sealed class MemoryReclaimerTests : IDisposable
{
    private static readonly TimeSpan _delay = TimeSpan.FromSeconds(5);
    private readonly ManualTimeProvider _clock = new();
    private readonly SemaphoreSlim _reclaimed = new(0);
    private readonly MemoryReclaimer _reclaimer;
    private int _count;

    public MemoryReclaimerTests()
    {
        _reclaimer = new MemoryReclaimer(
            () =>
            {
                Interlocked.Increment(ref _count);
                _reclaimed.Release();
            },
            _delay,
            _clock);
    }

    public void Dispose()
    {
        _reclaimer.Dispose();
        _reclaimed.Dispose();
        _clock.Dispose();
    }

    [Fact]
    public void NoActivity_CreatesNoTimer()
    {
        Assert.Equal(0, _clock.CreatedTimerCount);
        Assert.Equal(0, _count);
    }

    [Fact]
    public async Task OneBurst_ReclaimsOnce_ThenNoTimerIsLeft()
    {
        _reclaimer.Signal();
        _reclaimer.Signal();
        _reclaimer.Signal();
        await _clock.WaitForTimerCreatedAsync();
        Assert.Equal(1, _clock.CreatedTimerCount); // one wait for the whole burst

        _clock.Advance(_delay);

        Assert.True(await _reclaimed.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, _count);
        Assert.Equal(0, _clock.ScheduledTimerCount);
    }

    [Fact]
    public async Task ActivityDuringTheWait_PushesTheReclaimBack()
    {
        _reclaimer.Signal();
        await _clock.WaitForTimerCreatedAsync();
        _clock.Advance(TimeSpan.FromSeconds(3));
        _reclaimer.Signal(); // still busy: the quiet period starts again here

        _clock.Advance(TimeSpan.FromSeconds(2)); // the first wait ends, 2 s after the last activity
        await _clock.WaitForTimerCreatedAsync(); // waits the remaining 3 s
        Assert.Equal(0, _count);

        _clock.Advance(TimeSpan.FromSeconds(3));
        Assert.True(await _reclaimed.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(1, _count);
    }

    [Fact]
    public async Task ANewBurstAfterAReclaim_ReclaimsAgain()
    {
        _reclaimer.Signal();
        await _clock.WaitForTimerCreatedAsync();
        _clock.Advance(_delay);
        Assert.True(await _reclaimed.WaitAsync(TimeSpan.FromSeconds(10)));

        _reclaimer.Signal();
        await _clock.WaitForTimerCreatedAsync();
        _clock.Advance(_delay);
        Assert.True(await _reclaimed.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(2, _count);
    }

    [Fact]
    public async Task Dispose_CancelsAPendingReclaim()
    {
        _reclaimer.Signal();
        await _clock.WaitForTimerCreatedAsync();

        _reclaimer.Dispose();
        _clock.Advance(_delay);
        _reclaimer.Signal();

        Assert.False(await _reclaimed.WaitAsync(TimeSpan.FromMilliseconds(200)));
        Assert.Equal(0, _count);
    }

    [Fact]
    public void Collect_RunsAnAggressiveCollection()
    {
        var before = GC.CollectionCount(GC.MaxGeneration);

        MemoryReclaimer.Collect();

        Assert.True(GC.CollectionCount(GC.MaxGeneration) > before);
    }
}
