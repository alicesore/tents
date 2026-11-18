using Tents.Core.Persistence;

namespace Tents.Shell.Desktop;

/// <summary>Timing of <see cref="DesktopWatcher"/>.</summary>
public sealed record DesktopWatcherOptions
{
    /// <summary>
    /// How long after the first event of a burst <see cref="DesktopWatcher.ChangesReady"/> fires. Events in this window
    /// are delivered as one batch. Default 100 ms: one update for a typical copy or save, still immediate to a user.
    /// </summary>
    public TimeSpan CoalesceDelay { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// How far the desktop's native view lags behind the file system. Spike 2 measured 1.27 s and 1.34 s; the default
    /// adds a margin.
    /// </summary>
    public TimeSpan NativeViewLag { get; init; } = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// The window of the settle stage. <see cref="DesktopWatcher.NativeViewSettled"/> fires at the end of the first
    /// window after which no event is younger than <see cref="NativeViewLag"/>. Must not be shorter than the lag.
    /// </summary>
    public TimeSpan SettleWindow { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>The watcher's kernel buffer per folder. Overflow is reported as a rescan, never lost silently.</summary>
    public int InternalBufferSize { get; init; } = 32 * 1024;
}

/// <summary>
/// Watches the desktop folders and reports each burst of changes once, without any timer while nothing changes.
/// </summary>
/// <remarks>
/// <para><b>Two stages.</b> <see cref="ChangesReady"/> delivers the net change of a burst after
/// <see cref="DesktopWatcherOptions.CoalesceDelay"/>, straight from the file system: fast, by path. The native view
/// lists the same change about 1.3 s later (spike 2), so <see cref="NativeViewSettled"/> fires once the view has
/// caught up with the last event of the burst. Re-read the native view then (<see cref="DesktopItemSource"/>): it is the
/// reference for which items exist, including virtual items and the hidden files the watcher also reports.</para>
/// <para><b>No idle timer.</b> Both stages use <see cref="DebouncedSaver{T}"/>: a one-shot delay that exists only
/// from the first event of a burst until its callback, then nothing. The window is not extended by later events, so
/// a long burst gives one update per window rather than none until it ends. <c>FileSystemWatcher</c> itself waits on
/// an overlapped read: no polling.</para>
/// <para><b>Threading.</b> Events are raised on the thread pool, never on the caller's thread, one at a time per stage.</para>
/// <para><b>Read-only.</b> The watcher never touches the files.</para>
/// </remarks>
public sealed class DesktopWatcher : IAsyncDisposable
{
    private static readonly object _token = new();

    private readonly IReadOnlyList<string> _folders;
    private readonly DesktopWatcherOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private readonly DesktopChangeAccumulator _accumulator = new();
    private readonly DebouncedSaver<object> _changes;
    private readonly DebouncedSaver<object> _settle;
    private readonly List<FileSystemWatcher> _watchers = [];
    private DateTimeOffset _lastEventAt;
    private bool _disposed;

    /// <param name="folders">Folders to watch, non-recursively. Missing folders are skipped.</param>
    /// <param name="options">Timing; defaults are documented on <see cref="DesktopWatcherOptions"/>.</param>
    /// <param name="timeProvider">Defaults to <see cref="TimeProvider.System"/>; tests pass a manual clock.</param>
    public DesktopWatcher(IEnumerable<string> folders, DesktopWatcherOptions? options = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(folders);
        _options = options ?? new DesktopWatcherOptions();
        if (_options.CoalesceDelay < TimeSpan.Zero || _options.NativeViewLag < TimeSpan.Zero || _options.SettleWindow < _options.NativeViewLag)
        {
            throw new ArgumentException("Delays must be positive and the settle window at least the native view lag.", nameof(options));
        }

        _folders = [.. folders.Distinct(StringComparer.OrdinalIgnoreCase)];
        _timeProvider = timeProvider ?? TimeProvider.System;
        _changes = new DebouncedSaver<object>(_ => DeliverChanges(), _options.CoalesceDelay, _timeProvider);
        _settle = new DebouncedSaver<object>(_ => CheckSettled(), _options.SettleWindow, _timeProvider);
    }

    /// <summary>The net change of one burst. Raised on the thread pool.</summary>
    public event EventHandler<DesktopChangeBatch>? ChangesReady;

    /// <summary>The native view has had time to catch up with the last burst. Raised on the thread pool.</summary>
    public event EventHandler? NativeViewSettled;

    /// <summary>The folders being watched.</summary>
    public IReadOnlyList<string> Folders => _folders;

    /// <summary>
    /// The user's Desktop folder and the public Desktop folder, without duplicates or missing folders. Their union is
    /// what the desktop shows.
    /// </summary>
    public static IReadOnlyList<string> GetDesktopFolders() =>
    [
        .. new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        }
        .Where(f => !string.IsNullOrEmpty(f) && Directory.Exists(f))
        .Distinct(StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>Starts watching. Returns the number of folders watched.</summary>
    public int Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_watchers.Count > 0)
            {
                return _watchers.Count;
            }

            foreach (var folder in _folders.Where(Directory.Exists))
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Attributes,
                    InternalBufferSize = _options.InternalBufferSize,
                };
                watcher.Created += (_, e) => OnRawChange(new RawChange(RawChangeKind.Created, e.FullPath));
                watcher.Deleted += (_, e) => OnRawChange(new RawChange(RawChangeKind.Deleted, e.FullPath));
                watcher.Changed += (_, e) => OnRawChange(new RawChange(RawChangeKind.Changed, e.FullPath));
                watcher.Renamed += (_, e) => OnRawChange(new RawChange(RawChangeKind.Renamed, e.FullPath, e.OldFullPath));
                watcher.Error += (_, _) => OnRawChange(new RawChange(RawChangeKind.Overflow));
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }

            return _watchers.Count;
        }
    }

    /// <summary>Stops watching and drops anything not yet delivered. No event is raised after this completes.</summary>
    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var watcher in _watchers)
            {
                watcher.Dispose();
            }

            _watchers.Clear();
        }

        // DisposeAsync flushes; the callbacks see _disposed and raise nothing.
        await _changes.DisposeAsync().ConfigureAwait(false);
        await _settle.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Feeds one raw event. Called by the file watchers; tests call it directly.</summary>
    internal void OnRawChange(RawChange change)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _accumulator.Add(change);
            _lastEventAt = _timeProvider.GetUtcNow();
        }

        _changes.Schedule(_token);
        _settle.Schedule(_token);
    }

    private ValueTask DeliverChanges()
    {
        DesktopChangeBatch batch;
        lock (_gate)
        {
            if (_disposed)
            {
                return default;
            }

            batch = _accumulator.TakeBatch();
        }

        if (!batch.IsEmpty)
        {
            Raise(() => ChangesReady?.Invoke(this, batch));
        }

        return default;
    }

    private ValueTask CheckSettled()
    {
        TimeSpan quiet;
        lock (_gate)
        {
            if (_disposed)
            {
                return default;
            }

            quiet = _timeProvider.GetUtcNow() - _lastEventAt;
        }

        if (quiet < _options.NativeViewLag)
        {
            // An event late in the window is not in the view yet: one more window. Ends once the burst ends.
            _settle.Schedule(_token);
            return default;
        }

        Raise(() => NativeViewSettled?.Invoke(this, EventArgs.Empty));
        return default;
    }

    // A throwing handler must not leave the stage pending (it would be retried on the next event) or fault the saver.
    private static void Raise(Action raise)
    {
        try
        {
            raise();
        }
        catch (Exception ex) when (ShellPlatform.ReportCallbackException(ex))
        {
        }
    }
}
