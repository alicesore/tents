namespace Tents.Shell.DragDrop;

/// <summary>OLE drop effects (<c>DROPEFFECT</c>).</summary>
[Flags]
public enum DragEffects
{
    None = 0,
    Copy = 1,
    Move = 2,
    Link = 4,
}

/// <summary>What a drop onto a tent does.</summary>
public enum DropRoute
{
    /// <summary>Nothing usable is being dragged (for example plain text): the drop is refused.</summary>
    Reject,

    /// <summary>
    /// Desktop items dragged out of a tent onto a tent's empty area: only tent membership changes. No file is touched.
    /// </summary>
    Reassign,

    /// <summary>
    /// Items from elsewhere (Explorer, other apps) onto a tent's empty area: handed to the desktop folder's own drop
    /// target, which copies or moves them onto the desktop exactly as a drop on the desktop would. The new desktop items
    /// then belong to this tent.
    /// </summary>
    ForwardToDesktop,

    /// <summary>
    /// Anything dropped onto an item that accepts drops (a folder, the Recycle Bin, a program): handed to that item's
    /// drop target, as on the desktop.
    /// </summary>
    ForwardToItem,
}

/// <summary>The rules for drops onto a tent. Pure.</summary>
/// <remarks>
/// Tents itself never moves, copies or deletes files. A <see cref="DropRoute.Reassign"/> changes only which tent an item
/// is in. Every file operation is done by the shell's own drop target, because the user dropped there, with the shell's
/// usual rules (same volume moves, Ctrl copies, Alt links), confirmations and undo.
/// </remarks>
public static class DropPolicy
{
    /// <param name="fromTents">The drag started in a tent of this process (it carries the internal marker).</param>
    /// <param name="hasShellItems">The data names shell items (files, folders, virtual items).</param>
    /// <param name="overDropTargetItem">The pointer is over an item that accepts drops and that is not being dragged.</param>
    public static DropRoute Route(bool fromTents, bool hasShellItems, bool overDropTargetItem)
    {
        if (!hasShellItems)
        {
            return DropRoute.Reject;
        }

        if (overDropTargetItem)
        {
            return DropRoute.ForwardToItem;
        }

        return fromTents ? DropRoute.Reassign : DropRoute.ForwardToDesktop;
    }

    /// <summary>
    /// The effect shown for a <see cref="DropRoute.Reassign"/>: Move when the source allows it. An item belongs to one
    /// tent, so Ctrl (copy) does not make a second entry; the drag shows Move either way.
    /// </summary>
    public static DragEffects ReassignEffect(DragEffects allowed) => allowed.HasFlag(DragEffects.Move) ? DragEffects.Move : DragEffects.None;

    /// <summary>What a drag that starts in a tent offers: what the desktop offers for its items.</summary>
    public const DragEffects SourceEffects = DragEffects.Copy | DragEffects.Move | DragEffects.Link;
}
