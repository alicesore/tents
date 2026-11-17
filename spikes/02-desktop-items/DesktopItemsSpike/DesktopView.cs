// THROWAWAY SPIKE CODE for M0 spike 2 (desktop items). Not production. See spikes/README.md.
//
// Access to the desktop's shell view through documented COM interfaces, following the publicly described route:
// IShellWindows::FindWindowSW(CSIDL_DESKTOP, SWC_DESKTOP) -> IServiceProvider -> SID_STopLevelBrowser ->
// IShellBrowser::QueryActiveShellView -> IFolderView2.
// Source: Raymond Chen, "Manipulating the positions of desktop icons" (The Old New Thing, 2013-03-18).
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using ComServiceProvider = Windows.Win32.System.Com.IServiceProvider;

namespace Tents.Spikes;

internal sealed record DesktopItem(int Index, string ParsingName, string DisplayName, int X, int Y);

internal sealed class DesktopBackup
{
    public string TakenAt { get; set; } = "";
    public uint FolderFlags { get; set; }
    public int ViewMode { get; set; }
    public int IconSize { get; set; }
    public int? HideIconsRegistry { get; set; }
    public bool ListViewVisible { get; set; }
    /// <summary>Set by the parking prototype: where it parks native icons. Restore sweeps anything still left there.</summary>
    public int? ParkX { get; set; }
    public int? ParkY { get; set; }
    public List<DesktopItem> Items { get; set; } = [];
}

internal sealed unsafe class DesktopView
{
    public const uint FWF_AUTOARRANGE = 0x1, FWF_SNAPTOGRID = 0x4, FWF_NOICONS = 0x1000;

    public readonly IFolderView2 View;
    DesktopView(IFolderView2 view) => View = view;

    public static DesktopView Open()
    {
        var shellWindows = (IShellWindows)new ShellWindows();
        object loc = 0; // CSIDL_DESKTOP
        object root = null!;
        var dispatch = shellWindows.FindWindowSW(in loc, in root, ShellWindowTypeConstants.SWC_DESKTOP, out _, ShellWindowFindWindowOptions.SWFO_NEEDDISPATCH);
        var sp = (ComServiceProvider)dispatch;
        Guid sid = PInvoke.SID_STopLevelBrowser, iid = typeof(IShellBrowser).GUID;
        sp.QueryService(&sid, &iid, out void* ppv);
        var browser = (IShellBrowser)Marshal.GetObjectForIUnknown((IntPtr)ppv);
        Marshal.Release((IntPtr)ppv);
        browser.QueryActiveShellView(out var shellView);
        return new DesktopView((IFolderView2)shellView);
    }

    public uint Flags { get { View.GetCurrentFolderFlags(out var f); return f; } }
    public void SetFlags(uint mask, uint flags) => View.SetCurrentFolderFlags(mask, flags);

    public (int mode, int iconSize) ViewMode()
    {
        FOLDERVIEWMODE mode;
        View.GetViewModeAndIconSize(&mode, out var size);
        return ((int)mode, size);
    }

    public Point Spacing() { Point p; View.GetSpacing(&p); return p; }

    public int Count { get { View.ItemCount(_SVGIO.SVGIO_ALLVIEW, out var n); return n; } }

    public List<DesktopItem> Items()
    {
        var list = new List<DesktopItem>();
        int n = Count;
        for (int i = 0; i < n; i++)
        {
            ITEMIDLIST* pidl = null;
            View.Item(i, &pidl);
            try
            {
                Point pt;
                View.GetItemPosition(pidl, &pt);
                var iid = typeof(IShellItem).GUID;
                View.GetItem(i, &iid, out void* ppv);
                var item = (IShellItem)Marshal.GetObjectForIUnknown((IntPtr)ppv);
                Marshal.Release((IntPtr)ppv);
                list.Add(new DesktopItem(i, Name(item, SIGDN.SIGDN_DESKTOPABSOLUTEPARSING), Name(item, SIGDN.SIGDN_NORMALDISPLAY), pt.X, pt.Y));
            }
            finally { PInvoke.CoTaskMemFree(pidl); }
        }
        return list;
    }

    static string Name(IShellItem item, SIGDN kind)
    {
        PWSTR p;
        item.GetDisplayName(kind, &p);
        try { return p.ToString(); } finally { PInvoke.CoTaskMemFree(p.Value); }
    }

    /// <summary>Moves one native desktop icon. Documented: IFolderView::SelectAndPositionItems with SVSI_POSITIONITEM.</summary>
    public bool SetPosition(string parsingName, int x, int y)
    {
        var item = Items().FirstOrDefault(i => string.Equals(i.ParsingName, parsingName, StringComparison.OrdinalIgnoreCase));
        if (item == null) return false;
        ITEMIDLIST* pidl = null;
        View.Item(item.Index, &pidl);
        try
        {
            var pt = new Point(x, y);
            View.SelectAndPositionItems(1, &pidl, &pt, (uint)_SVSIF.SVSI_POSITIONITEM);
            return true;
        }
        finally { PInvoke.CoTaskMemFree(pidl); }
    }

    // ---------- backup / restore ----------

    public static int? HideIconsRegistry()
        => Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideIcons", null) as int?;

    public DesktopBackup Backup()
    {
        var (mode, size) = ViewMode();
        var d = N.FindDesktop();
        return new DesktopBackup
        {
            TakenAt = DateTime.Now.ToString("o"), FolderFlags = Flags, ViewMode = mode, IconSize = size,
            HideIconsRegistry = HideIconsRegistry(), ListViewVisible = d.ListView != IntPtr.Zero && N.IsWindowVisible(d.ListView), Items = Items(),
        };
    }

    static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };
    public static void Save(DesktopBackup b, string file) => File.WriteAllText(file, JsonSerializer.Serialize(b, s_json));
    public static DesktopBackup Load(string file) => JsonSerializer.Deserialize<DesktopBackup>(File.ReadAllText(file))!;

    /// <summary>Puts back icon visibility, the arrange flags and every saved icon position. Returns a human-readable report.</summary>
    public static string Restore(DesktopBackup b)
    {
        var report = new List<string>();
        var d = N.FindDesktop();
        if (b.ListViewVisible && d.ListView != IntPtr.Zero && !N.IsWindowVisible(d.ListView))
        {
            N.ShowWindow(d.ListView, 5 /*SW_SHOW*/);
            report.Add("icon list window shown again");
        }
        var view = Open();
        const uint mask = FWF_AUTOARRANGE | FWF_SNAPTOGRID | FWF_NOICONS;
        var now = view.Flags;
        if ((now & mask) != (b.FolderFlags & mask))
        {
            view.SetFlags(mask, b.FolderFlags & mask);
            report.Add($"folder flags restored: 0x{now & mask:X} -> 0x{b.FolderFlags & mask:X}");
        }
        var current = view.Items().ToDictionary(i => i.ParsingName, StringComparer.OrdinalIgnoreCase);
        int moved = 0, missing = 0;
        foreach (var saved in b.Items)
        {
            if (!current.TryGetValue(saved.ParsingName, out var cur)) { missing++; continue; }
            if (cur.X == saved.X && cur.Y == saved.Y) continue;
            if (view.SetPosition(saved.ParsingName, saved.X, saved.Y)) moved++;
        }
        report.Add($"icon positions: {moved} moved back, {b.Items.Count - moved - missing} already in place, {missing} no longer on the desktop");
        if (b.ParkX is int px && b.ParkY is int py)
        {
            // Anything still parked has no recorded original position (for example it was renamed while parked).
            // Never leave an icon off-screen: put it somewhere visible.
            int k = 0;
            foreach (var stray in view.Items().Where(i => i.X == px && i.Y == py))
                view.SetPosition(stray.ParsingName, 200 + 100 * k++, 200);
            if (k > 0) report.Add($"{k} icon(s) without a recorded position moved from the park position to a visible fallback position");
        }
        return string.Join("; ", report);
    }
}
