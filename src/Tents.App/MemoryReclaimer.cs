namespace Tents.App;

/// <summary>
/// Gives memory back once activity has stopped: a few seconds after the last burst of work (startup, items arriving,
/// icons loading, a resize), one aggressive garbage collection returns the unused managed heap to the system. Nothing
/// runs and no timer exists while nothing happens.
/// </summary>
/// <remarks>
/// <para><b>Why.</b> The idle check (M1 step 22) found that Tents never collected at all: the runtime sizes the first
/// generation's budget from the CPU's cache (about 47 MB on a CPU with 96 MB of L3), so everything allocated at startup
/// and while resizing stayed committed, and private bytes stayed over the budget in <c>PROJECT_PLAN.md</c> section 7.
/// The live managed heap is about 3 MB. A <see cref="GCCollectionMode.Aggressive"/> collection is the one mode that
/// also decommits the freed memory; it took about 3 ms in a test. It is not working-set trimming: the memory is
/// released, not paged out.</para>
/// <para><b>How.</b> <see cref="Signal"/> only records the time; the first signal after a quiet period starts one
/// background wait. When the wait ends it checks whether a later signal arrived and, if so, waits for the rest of the
/// delay (one one-shot timer at a time, only while signals keep arriving). Then it collects once, on the thread pool.
/// </para>
/// </remarks>
internal sealed class MemoryReclaimer : IDisposable
{
    /// <summary>Quiet time after the last activity before memory is given back.</summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(5);

    private readonly TimeSpan _delay;
    private readonly TimeProvider _time;
    private readonly Action _reclaim;
    private readonly CancellationTokenSource _stop = new();
    private long _lastSignal;
    private int _pending;
    private bool _disposed;

    /// <param name="reclaim">What to run once activity has stopped; <see cref="Collect"/> in the app.</param>
    public MemoryReclaimer(Action reclaim, TimeSpan? delay = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(reclaim);
        _reclaim = reclaim;
        _delay = delay ?? DefaultDelay;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Raised after each reclaim, on the thread pool.</summary>
    public event EventHandler? Reclaimed;

    /// <summary>Something happened that may have allocated. Cheap; call from any thread.</summary>
    public void Signal()
    {
        if (_disposed)
        {
            return;
        }

        Interlocked.Exchange(ref _lastSignal, _time.GetTimestamp());
        if (Interlocked.Exchange(ref _pending, 1) == 0)
        {
            _ = WaitThenReclaimAsync();
        }
    }

    /// <summary>The app's reclaim: one blocking, compacting collection that also decommits the freed memory.</summary>
    public static void Collect() => GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stop.Cancel();
        _stop.Dispose();
    }

    private async Task WaitThenReclaimAsync()
    {
        try
        {
            var wait = _delay;
            while (true)
            {
                await Task.Delay(wait, _time, _stop.Token).ConfigureAwait(false);
                var quiet = _time.GetElapsedTime(Interlocked.Read(ref _lastSignal));
                if (quiet < _delay)
                {
                    wait = _delay - quiet;
                    continue;
                }

                // Let the next signal start a new wait, then check once more for one that came in meanwhile.
                Volatile.Write(ref _pending, 0);
                quiet = _time.GetElapsedTime(Interlocked.Read(ref _lastSignal));
                if (quiet < _delay)
                {
                    if (Interlocked.Exchange(ref _pending, 1) != 0)
                    {
                        return; // that signal started its own wait
                    }

                    wait = _delay - quiet;
                    continue;
                }

                break;
            }

            _reclaim();
            Reclaimed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (ObjectDisposedException)
        {
            // Shutting down.
        }
    }
}
