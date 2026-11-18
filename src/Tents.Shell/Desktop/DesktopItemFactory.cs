using System.Runtime.InteropServices;
using Tents.Shell.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.SystemServices;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;

namespace Tents.Shell.Desktop;

/// <summary>Builds <see cref="DesktopItem"/> values from shell ID lists or parsing names, in this process.</summary>
public static unsafe class DesktopItemFactory
{
    /// <summary>
    /// The item for a parsing name, for example a path reported by the file watcher before the native view lists it,
    /// or null when the shell cannot parse it (the file is already gone). Its ID list is the absolute one for that
    /// path, which is deeper than the desktop view's (see <see cref="ShellSelection"/>). Call on an STA thread.
    /// </summary>
    public static DesktopItem? FromParsingName(string parsingName)
    {
        ArgumentException.ThrowIfNullOrEmpty(parsingName);
        ITEMIDLIST* pidl = null;
        try
        {
            if (PInvoke.SHParseDisplayName(parsingName, null, out pidl, 0).Failed)
            {
                return null;
            }

            return FromIdList(pidl);
        }
        catch (Exception ex) when (ComErrors.IsCallFailure(ex))
        {
            return null;
        }
        finally
        {
            PInvoke.CoTaskMemFree(pidl);
        }
    }

    /// <summary>The item for an absolute ID list, or null when it has no usable parsing name.</summary>
    internal static DesktopItem? FromIdList(ITEMIDLIST* absolute)
    {
        IShellItem* item = null;
        try
        {
            if (PInvoke.SHCreateItemFromIDList(in *absolute, out item).Failed)
            {
                return null;
            }

            var id = DesktopItemIdentity.FromParsingName(GetName(item, SIGDN.SIGDN_DESKTOPABSOLUTEPARSING));
            if (id is null)
            {
                return null;
            }

            const SFGAO_FLAGS wanted = SFGAO_FLAGS.SFGAO_FILESYSTEM | SFGAO_FLAGS.SFGAO_FOLDER;
            item->GetAttributes(wanted, out var attributes);
            var displayName = GetName(item, SIGDN.SIGDN_NORMALDISPLAY) ?? id.Value.Value;
            return new DesktopItem(
                id.Value,
                displayName,
                IsFileSystem: (attributes & SFGAO_FLAGS.SFGAO_FILESYSTEM) != 0,
                IsFolder: (attributes & SFGAO_FLAGS.SFGAO_FOLDER) != 0,
                ShellIdList.FromNative(absolute));
        }
        catch (Exception ex) when (ComErrors.IsCallFailure(ex))
        {
            return null; // This item could not be read.
        }
        finally
        {
            Com.Release(ref item);
        }
    }

    private static string? GetName(IShellItem* item, SIGDN kind)
    {
        PWSTR name = default;
        try
        {
            item->GetDisplayName(kind, &name);
            return name.ToString();
        }
        catch (Exception ex) when (ComErrors.IsCallFailure(ex))
        {
            return null; // The item has no name of this kind.
        }
        finally
        {
            PInvoke.CoTaskMemFree(name.Value);
        }
    }
}
