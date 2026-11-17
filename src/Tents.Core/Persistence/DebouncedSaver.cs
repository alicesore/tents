namespace Tents.Core.Persistence;

/// <summary>
/// Coalesces bursts of changes into one save, without any timer while nothing changes.
/// </summary>
/// <remarks>
/// <para><b>How it works.</b> The first <see cref="Schedule"/> after a quiet period starts one background task that
/// waits <c>delay</c> once (a one-shot <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/>), then
/// saves the latest value. Changes that arrive during the wait only replace the pending value. Changes that arrive
/// during a save start one more wait after it. When nothing is pending the task ends, so while idle there is no
/// timer, no task and no thread-pool work (PROJECT_PLAN.md §7: <c>dotnet.timer.count</c> 0, no work items).</para>
/// <para><b>Why the window is not reset on every change.</b> A classic debounce restarts the wait on each change,
/// so a long drag would postpone the save indefinitely and create a new timer for every mouse move. A fixed
/// window that starts at the first change bounds the time to disk to about <c>delay</c> plus one save, and uses one
/// timer per burst.</para>
/// <para><b>Threading.</b> <see cref="Schedule"/> only takes a short lock and never blocks, so it is safe on the
/// tent UI thread. The save callback always runs on the thread pool, never on the caller's thread, and saves never
/// overlap. Values are assumed immutable (a <see cref="LayoutSet"/>), so they are shared without copying.</para>
/// <para><b>Errors.</b> A failed save raises <see cref="SaveFailed"/> and keeps the value pending. It is not retried
/// on a timer; the next <see cref="Schedule"/> or <see cref="FlushAsync"/> tries again.</para>
/// </remarks>
/// <typeparam name="T">The value to save.</typeparam>
public sealed class DebouncedSaver<T> : IAsyncDisposable
    where T : class
{
    private readonly Func<T, ValueTask> _save;
    private readonly TimeSpan _delay;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();

    private T? _pending;
    private Task? _worker;
    private TaskCompletionSource? _flushSignal;
    private bool _disposed;

    /// <param name="save">Saves one value. Called on the thread pool, one call at a time.</param>
    /// <param name="delay">How long to wait after the first change of a burst before saving.</param>
    /// <param name="timeProvider">Defaults to <see cref="TimeProvider.System"/>; tests pass a manual clock.</param>
    public DebouncedSaver(Func<T, ValueTask> save, TimeSpan delay, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentOutOfRangeException.ThrowIfLessThan(delay, TimeSpan.Zero);
        _save = save;
        _delay = delay;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Raised on the thread pool when a save throws. The value stays pending.</summary>
    public event EventHandler<SaveFailedEventArgs>? SaveFailed;

    /// <summary>True while a value is waiting to be saved or a save is running.</summary>
    public bool HasPendingChanges
    {
        get
        {
            lock (_gate)
            {
                return _pending is not null || _worker is not null;
            }
        }
    }

    /// <summary>Marks <paramref name="value"/> as the latest state to save. Never blocks.</summary>
    /// <exception cref="ObjectDisposedException">The saver was disposed.</exception>
    public void Schedule(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _pending = value;
            _worker ??= StartWorker();
        }
    }

    /// <summary>
    /// Saves the pending value now, skipping the rest of the wait, and completes when nothing is pending or the
    /// save failed (check <see cref="HasPendingChanges"/>). Use on exit and on Pause.
    /// </summary>
    public Task FlushAsync()
    {
        TaskCompletionSource signal;
        Task worker;
        lock (_gate)
        {
            if (_worker is null)
            {
                if (_pending is null)
                {
                    return Task.CompletedTask;
                }

                _worker = StartWorker();
            }

            signal = _flushSignal!;
            worker = _worker;
        }

        // Outside the lock. The signal runs its continuations asynchronously, so the save never runs on this thread.
        signal.TrySetResult();
        return worker;
    }

    /// <summary>Flushes, then rejects further <see cref="Schedule"/> calls.</summary>
    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        await FlushAsync().ConfigureAwait(false);
    }

    // Called under _gate when no worker is running.
    private Task StartWorker()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _flushSignal = signal;
        return Task.Run(() => RunAsync(signal));
    }

    private async Task RunAsync(TaskCompletionSource flushSignal)
    {
        while (true)
        {
            if (!flushSignal.Task.IsCompleted)
            {
                using var stopDelay = new CancellationTokenSource();
                var delay = Task.Delay(_delay, _timeProvider, stopDelay.Token);
                await Task.WhenAny(delay, flushSignal.Task).ConfigureAwait(false);

                // If the flush won, release the one-shot timer now instead of when it would have fired.
                await stopDelay.CancelAsync().ConfigureAwait(false);
            }

            T? value;
            lock (_gate)
            {
                value = _pending;
                _pending = null;
                if (value is null)
                {
                    _worker = null;
                    _flushSignal = null;
                    return;
                }
            }

            try
            {
                await _save(value).ConfigureAwait(false);
            }
            catch (Exception ex) // Report any save failure instead of faulting the task; the value stays pending.
            {
                lock (_gate)
                {
                    _pending ??= value;
                    _worker = null;
                    _flushSignal = null;
                }

                SaveFailed?.Invoke(this, new SaveFailedEventArgs(ex));
                return;
            }

            // Check before waiting again: when nothing arrived during the save, end now rather than run an empty
            // window, which would cost one more timer and one more wakeup after every save.
            lock (_gate)
            {
                if (_pending is null)
                {
                    _worker = null;
                    _flushSignal = null;
                    return;
                }
            }
        }
    }
}

/// <summary>Data for <see cref="DebouncedSaver{T}.SaveFailed"/>.</summary>
public sealed class SaveFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}
