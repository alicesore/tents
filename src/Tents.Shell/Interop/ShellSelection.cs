namespace Tents.Shell.Interop;

/// <summary>
/// Groups selected items under one parent folder, as <c>IShellFolder::GetUIObjectOf</c> requires (one folder, child
/// ID lists). Pure.
/// </summary>
/// <remarks>
/// Items listed by the desktop's native view are all direct children of the desktop root, including items from the
/// public Desktop folder, so a desktop selection always has one parent, exactly as on the desktop itself. Items made
/// from a path (a file watcher event, before the view lists them) have deeper ID lists. When a selection mixes
/// parents, only the items under the first item's parent are kept, so the menu still acts on the item the user
/// clicked.
/// </remarks>
public static class ShellSelection
{
    /// <summary>The first item's parent, and the children of the items that share it, in selection order.</summary>
    /// <param name="items">Absolute ID lists.</param>
    /// <param name="sameFolder">
    /// Decides whether two parent ID lists that differ in bytes name the same folder. Byte equality is not enough for
    /// lists built from paths: a file system entry carries the folder's attributes and dates, so the same folder
    /// parsed before and after a change gives different bytes. The shell's <c>IShellFolder::CompareIDs</c> answers it;
    /// without a comparer only identical bytes match.
    /// </param>
    /// <exception cref="ArgumentException">The selection is empty or contains the desktop root itself.</exception>
    public static (ShellIdList Parent, IReadOnlyList<ShellIdList> Children, int Dropped) Group(
        IReadOnlyList<ShellIdList> items,
        Func<ShellIdList, ShellIdList, bool>? sameFolder = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("The selection is empty.", nameof(items));
        }

        if (items.Any(i => i.ItemCount == 0))
        {
            throw new ArgumentException("The desktop root itself cannot be part of a selection.", nameof(items));
        }

        var parent = items[0].Parent;
        var children = new List<ShellIdList>();
        foreach (var item in items)
        {
            var itemParent = item.Parent;
            if (itemParent.Equals(parent) || (sameFolder?.Invoke(parent, itemParent) ?? false))
            {
                children.Add(item.LastItem);
            }
        }

        return (parent, children, items.Count - children.Count);
    }
}
