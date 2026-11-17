namespace Tents.Tests.Persistence;

/// <summary>
/// A clock that only moves when the test calls <see cref="Advance"/>, and that counts its timers, so tests can
/// check that no timer exists while nothing changes. Timer callbacks run on the thread that calls Advance.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider, IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly SemaphoreSlim _created = new(0);
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private int _createdCount;

    /// <summary>Timers that exist and are scheduled to fire.</summary>
    public int ScheduledTimerCount
    {
        get
        {
            lock (_gate)
            {
                return _timers.Count(t => t.DueAt is not null);
            }
        }
    }

    public int CreatedTimerCount => Volatile.Read(ref _createdCount);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
            timer.ChangeLocked(dueTime, period);
        }

        Interlocked.Increment(ref _createdCount);
        _created.Release();
        return timer;
    }

    /// <summary>Waits until the code under test has created its next timer.</summary>
    public async Task WaitForTimerCreatedAsync()
    {
        if (!await _created.WaitAsync(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("No timer was created.");
        }
    }

    public void Dispose() => _created.Dispose();

    public void Advance(TimeSpan by)
    {
        List<ManualTimer> due;
        lock (_gate)
        {
            _now += by;
            due = [.. _timers.Where(t => t.DueAt <= _now)];
            foreach (var timer in due)
            {
                timer.DueAt = timer.Period is { } period ? _now + period : null;
            }
        }

        foreach (var timer in due)
        {
            timer.Fire();
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? DueAt { get; set; }

        public TimeSpan? Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                ChangeLocked(dueTime, period);
            }

            return true;
        }

        public void ChangeLocked(TimeSpan dueTime, TimeSpan period)
        {
            DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
            Period = period == Timeout.InfiniteTimeSpan || period == TimeSpan.Zero ? null : period;
        }

        public void Fire() => callback(state);

        public void Dispose()
        {
            lock (owner._gate)
            {
                owner._timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
