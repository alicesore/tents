using Tents.Core;
using Tents.Core.Persistence;
using Tents.Shell.Hosting;

namespace Tents.App;

/// <summary>How the layout for this start was chosen, for the log.</summary>
internal enum LayoutChoice
{
    /// <summary>A saved layout for exactly these monitors.</summary>
    Saved,

    /// <summary>No saved layout for these monitors; the closest saved one was taken over for them.</summary>
    Adopted,

    /// <summary>Nothing saved: the first-start layout.</summary>
    New,
}

/// <summary>
/// Loads the layouts at start and saves the current one whenever it changes, coalesced. File work runs on the thread
/// pool, never on the UI thread.
/// </summary>
/// <remarks>
/// <para><b>Choosing a layout</b> (<see cref="Choose"/>): the saved layout for the current monitor configuration; if there
/// is none (a monitor was added or removed), the saved layout that shares the most monitors is taken over under the new
/// configuration, so the user's tents do not vanish; otherwise the first-start layout. Proper per-configuration layouts
/// are M2 step 3.</para>
/// <para><b>Off-screen tents</b> (<see cref="BringOnScreen"/>): a tent that no monitor shows any more would be
/// unreachable, so it is moved onto the primary monitor. Tents that are partly visible stay where they are.</para>
/// <para><b>Saving</b>: through <see cref="DebouncedSaver{T}"/> (one save per burst of changes, no timer while idle).
/// If the file could not be read at start (locked, access denied), nothing is saved during this run, so a good file is
/// never overwritten with a fresh layout.</para>
/// </remarks>
internal sealed class LayoutPersistence : IAsyncDisposable
{
    /// <summary>Time from the first change of a burst to the save.</summary>
    public static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly LayoutFileStore _store;
    private readonly AppLog _log;
    private readonly DebouncedSaver<LayoutSet> _saver;
    private LayoutSet _layouts = LayoutSet.Empty;
    private bool _readOnly;

    public LayoutPersistence(LayoutFileStore store, AppLog log, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(log);
        _store = store;
        _log = log;
        _saver = new DebouncedSaver<LayoutSet>(Save, SaveDelay, timeProvider);
        _saver.SaveFailed += (_, e) => _log.Write($"Saving the layout failed: {e.Exception.GetType().Name}; the next change tries again.");
    }

    public string FilePath => _store.FilePath;

    /// <summary>True when the file could not be read at start, so this run never writes it.</summary>
    public bool IsReadOnly => _readOnly;

    /// <summary>Reads the layout file on the thread pool. Never throws for a bad file.</summary>
    public async Task<LayoutSet> LoadAsync()
    {
        try
        {
            var result = await Task.Run(_store.Load).ConfigureAwait(true);
            _layouts = result.Layouts;
            _log.Write($"Layout file: {result.Status}, from {result.Source}, {_layouts.Layouts.Length} layout(s)." +
                (result.PreservedPath is null ? string.Empty : " The unreadable file was kept next to it."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _readOnly = true;
            _layouts = LayoutSet.Empty;
            _log.Write($"Layout file could not be read ({ex.GetType().Name}); starting fresh and not saving during this run.");
        }

        return _layouts;
    }

    /// <summary>Records the current layout and saves it soon. Never blocks.</summary>
    public void Schedule(Layout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _layouts = _layouts.SetLayout(layout);
        if (!_readOnly)
        {
            _saver.Schedule(_layouts);
        }
    }

    /// <summary>Saves anything pending now. Use on exit and on Pause.</summary>
    public Task FlushAsync() => _saver.FlushAsync();

    public ValueTask DisposeAsync() => _saver.DisposeAsync();

    /// <summary>The layout to start with. Pure.</summary>
    public static Layout Choose(LayoutSet saved, MonitorConfigKey current, Func<Layout> createNew, out LayoutChoice choice)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(createNew);
        if (saved.Find(current) is { } exact)
        {
            choice = LayoutChoice.Saved;
            return exact;
        }

        if (saved.Layouts.IsEmpty)
        {
            choice = LayoutChoice.New;
            return createNew();
        }

        var currentIds = current.Monitors.Select(m => m.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var closest = saved.Layouts
            .Select((layout, index) => (layout, index, shared: layout.Monitors.Monitors.Count(m => currentIds.Contains(m.DeviceId))))
            .OrderByDescending(x => x.shared)
            .ThenBy(x => x.index)
            .First()
            .layout;
        choice = LayoutChoice.Adopted;
        return Layout.Create(current, closest.Tents, closest.DefaultTentId);
    }

    /// <summary>
    /// Moves every tent that no monitor shows onto the primary monitor's work area, cascaded. Returns the same layout when
    /// every tent is at least partly on screen. Pure.
    /// </summary>
    public static Layout BringOnScreen(Layout layout, IReadOnlyList<MonitorInfo> monitors, out int moved)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(monitors);
        moved = 0;
        if (monitors.Count == 0)
        {
            return layout;
        }

        var target = TentPlacement.MonitorOf(default, monitors)!; // the primary (or first) monitor
        foreach (var tent in layout.Tents)
        {
            if (monitors.Any(m => Intersects(tent.Bounds, m.Bounds)))
            {
                continue;
            }

            var offset = (int)Math.Round(TentPlacement.CascadeOffset * target.Scale * moved);
            var margin = (int)Math.Round(TentPlacement.Margin * target.Scale);
            var work = target.WorkArea;
            var width = Math.Min(tent.Bounds.Width, work.Width);
            var height = Math.Min(tent.Bounds.Height, work.Height);
            var x = work.X + Math.Min(margin + offset, Math.Max(0, work.Width - width));
            var y = work.Y + Math.Min(margin + offset, Math.Max(0, work.Height - height));
            layout = layout.SetTentBounds(tent.Id, new PixelRect(x, y, width, height));
            moved++;
        }

        return layout;
    }

    private static bool Intersects(PixelRect a, PixelRect b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    private ValueTask Save(LayoutSet layouts)
    {
        _store.Save(layouts);
        return default;
    }
}
