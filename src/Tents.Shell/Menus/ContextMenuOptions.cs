namespace Tents.Shell.Menus;

/// <summary>How to show a shell context menu.</summary>
/// <param name="ExtendedVerbs">
/// Shift was held: include extended verbs (<c>CMF_EXTENDEDVERBS</c>), as Shift+right-click does on the desktop.
/// </param>
/// <param name="ControlDown">Ctrl was held when the command was chosen (passed on to the handler).</param>
public sealed record ContextMenuOptions(bool ExtendedVerbs = false, bool ControlDown = false);

/// <summary>What happened after a context menu closed.</summary>
public enum ContextMenuOutcome
{
    /// <summary>The menu was dismissed without a choice.</summary>
    Dismissed,

    /// <summary>The shell ran the chosen command.</summary>
    Invoked,

    /// <summary>The chosen command is one the host must carry out itself; see <see cref="ContextMenuResult.Verb"/>.</summary>
    HostVerb,

    /// <summary>The shell could not build the menu or run the command.</summary>
    Failed,
}

/// <param name="Outcome">What happened.</param>
/// <param name="Verb">The canonical verb of the chosen command, when the handler names one (for example "delete").</param>
/// <param name="ItemsDropped">Selected items left out because they had another parent folder than the first item.</param>
public sealed record ContextMenuResult(ContextMenuOutcome Outcome, string? Verb = null, int ItemsDropped = 0);

/// <summary>The fixed parts of the context menu protocol. Pure.</summary>
public static class ContextMenuProtocol
{
    /// <summary>First command id passed to <c>QueryContextMenu</c>. 0 means "no command" in <c>TrackPopupMenuEx</c>.</summary>
    public const uint FirstCommandId = 1;

    /// <summary>Last command id passed to <c>QueryContextMenu</c>.</summary>
    public const uint LastCommandId = 0x7FFF;

    /// <summary>CMF_NORMAL.</summary>
    public const uint CmfNormal = 0x0;

    /// <summary>CMF_CANRENAME: the host can rename in place, so handlers add Rename.</summary>
    public const uint CmfCanRename = 0x10;

    /// <summary>CMF_EXTENDEDVERBS: Shift was held.</summary>
    public const uint CmfExtendedVerbs = 0x100;

    /// <summary>
    /// <c>QueryContextMenu</c> flags. Tents edits names itself (M1 step 16), so it always asks for Rename.
    /// Values from <see href="https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-icontextmenu-querycontextmenu">IContextMenu::QueryContextMenu</see>.
    /// </summary>
    public static uint QueryFlags(ContextMenuOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return CmfNormal | CmfCanRename | (options.ExtendedVerbs ? CmfExtendedVerbs : 0);
    }

    /// <summary>
    /// True for verbs the host carries out instead of the shell. "rename" is one: invoked outside a shell view it has
    /// no view to edit in, so the tent starts its own in-place edit.
    /// </summary>
    public static bool IsHostVerb(string? verb) => string.Equals(verb, "rename", StringComparison.OrdinalIgnoreCase);
}
