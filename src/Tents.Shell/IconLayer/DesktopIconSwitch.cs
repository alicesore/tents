using Tents.Shell.Desktop;

namespace Tents.Shell.IconLayer;

/// <summary>
/// The desktop's "Show desktop icons" switch through <c>IFolderView2::SetCurrentFolderFlags(FWF_NOICONS)</c>, the
/// documented flag behind the desktop's own View > Show desktop icons (spike 2). Runs on the desktop view's STA thread.
/// </summary>
/// <remarks>
/// <para>Switching on and off survives an Explorer restart with no duplicate icons, and the user can always undo it
/// with View > Show desktop icons. After switching on, the native view lists 0 items for a moment.</para>
/// <para>Sources: <see href="https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifolderview2-setcurrentfolderflags">IFolderView2::SetCurrentFolderFlags</see>,
/// <see href="https://learn.microsoft.com/windows/win32/api/shobjidl_core/ne-shobjidl_core-folderflags">FOLDERFLAGS</see>
/// (<c>FWF_NOICONS</c>: "The view should not display icons").</para>
/// </remarks>
public sealed class DesktopIconSwitch(DesktopItemSource source) : IDesktopIconSwitch
{
    /// <summary>FWF_NOICONS.</summary>
    public const uint NoIconsFlag = 0x1000;

    private readonly DesktopItemSource _source = source ?? throw new ArgumentNullException(nameof(source));

    public Task<bool> AreIconsShownAsync() => _source.InvokeWithViewAsync(view => (view.GetFolderFlags() & NoIconsFlag) == 0);

    public Task SetIconsShownAsync(bool shown) => _source.InvokeWithViewAsync(view =>
    {
        view.SetFolderFlags(NoIconsFlag, shown ? 0 : NoIconsFlag);
        return true;
    });
}
