using Tents.Core;

namespace Tents.Shell.Desktop;

/// <summary>What a file watcher reported, before coalescing.</summary>
public enum RawChangeKind
{
    Created,
    Deleted,
    Changed,
    Renamed,

    /// <summary>The watcher lost events (buffer overflow or error). Only a full re-read is reliable.</summary>
    Overflow,
}

/// <summary>One raw watcher event. Paths are full paths.</summary>
public readonly record struct RawChange(RawChangeKind Kind, string? Path = null, string? OldPath = null);

/// <summary>The net effect of one burst of file system changes on the desktop folders.</summary>
/// <param name="Added">Items that did not exist before the burst and exist after it.</param>
/// <param name="Removed">Items that existed before the burst and no longer exist (under their old id).</param>
/// <param name="Renamed">Items that existed before and now have another path. Carry tent membership from old to new.</param>
/// <param name="Changed">Items whose content or attributes changed (refresh icon and name). Current ids.</param>
/// <param name="RescanNeeded">Events were lost: re-read the native view instead of trusting this batch alone.</param>
/// <remarks>
/// The watcher sees files the desktop does not show (hidden files, <c>desktop.ini</c>). The native view, re-read
/// after <see cref="DesktopWatcher.NativeViewSettled"/>, is the reference for which items exist.
/// </remarks>
public sealed record DesktopChangeBatch(
    IReadOnlyList<ItemId> Added,
    IReadOnlyList<ItemId> Removed,
    IReadOnlyList<(ItemId Old, ItemId New)> Renamed,
    IReadOnlyList<ItemId> Changed,
    bool RescanNeeded)
{
    public bool IsEmpty => !RescanNeeded && Added.Count == 0 && Removed.Count == 0 && Renamed.Count == 0 && Changed.Count == 0;
}

/// <summary>
/// Folds raw watcher events into the net change of a burst. Pure and not thread-safe: the caller locks.
/// </summary>
/// <remarks>
/// Rules, per item, comparing the state before the burst with the state after it:
/// created then deleted is nothing; deleted then created again is a change; a chain of renames is one rename from the
/// first to the last name; a rename of an item created in the same burst is an addition under the last name; a
/// rename then a delete is a removal of the original. Paths compare case-insensitively, like <see cref="ItemId"/>.
/// </remarks>
public sealed class DesktopChangeAccumulator
{
    // Current path -> the path the item had before the burst (null: created during the burst).
    private readonly Dictionary<string, string?> _live = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _removed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _changed = new(StringComparer.OrdinalIgnoreCase);
    private bool _overflow;

    public bool IsEmpty => !_overflow && _live.Count == 0 && _removed.Count == 0;

    public void Add(RawChange change)
    {
        switch (change.Kind)
        {
            case RawChangeKind.Created:
                Created(Require(change.Path));
                break;
            case RawChangeKind.Deleted:
                Deleted(Require(change.Path));
                break;
            case RawChangeKind.Changed:
                var path = Require(change.Path);
                _live.TryAdd(path, path);
                _changed.Add(path);
                break;
            case RawChangeKind.Renamed:
                Renamed(Require(change.OldPath), Require(change.Path));
                break;
            case RawChangeKind.Overflow:
                _overflow = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(change), change.Kind, "Unknown change kind.");
        }
    }

    /// <summary>Returns the net change so far and starts a new burst.</summary>
    public DesktopChangeBatch TakeBatch()
    {
        var added = new List<ItemId>();
        var renamed = new List<(ItemId, ItemId)>();
        var changed = new List<ItemId>();
        foreach (var (path, original) in _live)
        {
            var id = DesktopItemIdentity.FromFileSystemPath(path);
            if (original is null)
            {
                added.Add(id);
                continue;
            }

            if (!string.Equals(original, path, StringComparison.OrdinalIgnoreCase))
            {
                renamed.Add((DesktopItemIdentity.FromFileSystemPath(original), id));
            }

            if (_changed.Contains(path))
            {
                changed.Add(id);
            }
        }

        var batch = new DesktopChangeBatch(
            added,
            [.. _removed.Select(DesktopItemIdentity.FromFileSystemPath)],
            renamed,
            changed,
            _overflow);
        _live.Clear();
        _removed.Clear();
        _changed.Clear();
        _overflow = false;
        return batch;
    }

    private void Created(string path)
    {
        if (_live.ContainsKey(path))
        {
            _changed.Add(path); // A repeated create: treat as a change of what is there.
        }
        else if (_removed.Remove(path))
        {
            _live[path] = path; // Deleted and created again: the same item, changed.
            _changed.Add(path);
        }
        else
        {
            _live[path] = null;
        }
    }

    private void Deleted(string path)
    {
        _changed.Remove(path);
        if (_live.Remove(path, out var original))
        {
            if (original is not null)
            {
                _removed.Add(original);
            }
        }
        else
        {
            _removed.Add(path);
        }
    }

    private void Renamed(string oldPath, string newPath)
    {
        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            // A casing-only rename keeps the id; report a change so the displayed name refreshes.
            var original = _live.Remove(oldPath, out var o) ? o : oldPath;
            _live[newPath] = original;
            _changed.Add(newPath);
            return;
        }

        if (_live.ContainsKey(newPath))
        {
            Deleted(newPath); // The rename replaced an item that was there.
        }

        var from = _live.Remove(oldPath, out var known) ? known : oldPath;
        _live[newPath] = from;
        if (_changed.Remove(oldPath))
        {
            _changed.Add(newPath);
        }
    }

    private static string Require(string? path) =>
        string.IsNullOrEmpty(path) ? throw new ArgumentException("The change has no path.", nameof(path)) : path;
}
