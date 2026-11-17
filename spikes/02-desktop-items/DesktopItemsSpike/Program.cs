// THROWAWAY SPIKE CODE for M0 spike 2 (desktop items). Not production. See spikes/README.md.
//
// desktopitems: command-line driver for the spike 2 experiments.
//
//   info                     desktop view state (flags, view mode, item count). Read-only. Prints no item names.
//   items <namePrefix>       positions of the native icons whose name starts with the prefix. Read-only.
//   backup <file>            save icon visibility, arrange flags and every icon position. Read-only on the desktop.
//   restore <file>           put back what backup saved.
//
// The backup file contains the names of the items on the desktop, so keep it out of the repository.
using Tents.Spikes;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 0) { Console.WriteLine("commands: info | items <namePrefix> | backup <file> | restore <file>"); return 2; }
        try
        {
            switch (args[0])
            {
                case "info": Info(); return 0;
                case "items":
                    {
                        // Positions of the items whose display name starts with the prefix (use the scratch prefix, so no private names are printed).
                        var all = DesktopView.Open().Items();
                        var hit = all.Where(i => i.DisplayName.StartsWith(args[1], StringComparison.OrdinalIgnoreCase)).ToList();
                        Console.WriteLine($"total={all.Count} matching={hit.Count} " + string.Join(" ", hit.Select(i => $"{i.DisplayName}=({i.X},{i.Y})")));
                        return 0;
                    }
                case "backup":
                    {
                        var b = DesktopView.Open().Backup();
                        DesktopView.Save(b, args[1]);
                        Console.WriteLine($"backup written: {b.Items.Count} item positions, folderFlags=0x{b.FolderFlags:X}, listViewVisible={b.ListViewVisible}, HideIcons={b.HideIconsRegistry?.ToString() ?? "(not set)"}");
                        return 0;
                    }
                case "restore":
                    Console.WriteLine("restore: " + DesktopView.Restore(DesktopView.Load(args[1])));
                    return 0;
                default:
                    Console.WriteLine("unknown command");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR: " + ex);
            return 1;
        }
    }

    static void Info()
    {
        var v = DesktopView.Open();
        var f = v.Flags;
        var (mode, size) = v.ViewMode();
        var d = N.FindDesktop();
        var sp = v.Spacing();
        Console.WriteLine($"items={v.Count} folderFlags=0x{f:X} autoArrange={(f & DesktopView.FWF_AUTOARRANGE) != 0} snapToGrid={(f & DesktopView.FWF_SNAPTOGRID) != 0} noIcons={(f & DesktopView.FWF_NOICONS) != 0}");
        Console.WriteLine($"viewMode={mode} iconSize={size} spacing={sp.X}x{sp.Y} listViewVisible={N.IsWindowVisible(d.ListView)} HideIconsRegistry={DesktopView.HideIconsRegistry()?.ToString() ?? "(not set)"}");
        var items = v.Items();
        Console.WriteLine("positions (no names): " + string.Join(" ", items.Select(i => $"({i.X},{i.Y})")));
    }
}
