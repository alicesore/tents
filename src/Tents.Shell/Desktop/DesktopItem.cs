using Tents.Core;
using Tents.Shell.Interop;

namespace Tents.Shell.Desktop;

/// <summary>One item that the desktop shows, as listed by the desktop's native view.</summary>
/// <param name="Id">
/// Stable identity: the shell parsing name (<c>SIGDN_DESKTOPABSOLUTEPARSING</c>). A full path for a file or folder
/// in the user's or the public Desktop folder, a <c>::{CLSID}</c> name for a virtual item. See
/// <see cref="DesktopItemIdentity"/>.
/// </param>
/// <param name="DisplayName">The name the desktop shows (<c>SIGDN_NORMALDISPLAY</c>). Not unique, not stable.</param>
/// <param name="IsFileSystem">The item is a file or folder on disk (<c>SFGAO_FILESYSTEM</c>).</param>
/// <param name="IsFolder">The item can be browsed into (<c>SFGAO_FOLDER</c>).</param>
/// <param name="IdList">
/// The item's ID list relative to the desktop root, as the native view gives it. Used for icons, menus and drag and
/// drop, so the shell sees the same item the desktop does.
/// </param>
public sealed record DesktopItem(ItemId Id, string DisplayName, bool IsFileSystem, bool IsFolder, ShellIdList IdList);
