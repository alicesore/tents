using System.Windows;
using Tents.App.Items;
using Tents.Core;
using Tents.Shell.Desktop;
using Tents.Shell.Icons;
using Tents.Shell.Interop;
using Tents.Shell.Menus;

namespace Tents.Tests.App;

/// <summary>The item view without a window: items, selection and scrolling. Never shows anything.</summary>
public sealed class ItemGridViewTests
{
    [Fact]
    public void Scrolling_IsClampedToTheContent() => StaThread.Run(() =>
    {
        using var icons = new IconCache();
        var view = new ItemGridView(icons, new Host());
        view.SetItems(Items(200));
        view.Measure(new Size(350, 300));
        view.Arrange(new Rect(0, 0, 350, 300));

        Assert.Equal(new ItemGrid(350, 200).ExtentHeight, view.ExtentHeight);
        view.SetVerticalOffset(-10);
        Assert.Equal(0, view.VerticalOffset);
        view.SetVerticalOffset(1e9);
        Assert.Equal(view.ExtentHeight - 300, view.VerticalOffset);
        view.MouseWheelUp();
        Assert.Equal(view.ExtentHeight - 300 - ItemGrid.CellHeight, view.VerticalOffset);
    });

    [Fact]
    public void FewerItems_ClampTheOffset() => StaThread.Run(() =>
    {
        using var icons = new IconCache();
        var view = new ItemGridView(icons, new Host());
        view.SetItems(Items(200));
        view.Measure(new Size(350, 300));
        view.Arrange(new Rect(0, 0, 350, 300));
        view.SetVerticalOffset(1e9);

        view.SetItems(Items(4));

        Assert.Equal(0, view.VerticalOffset);
        Assert.Equal(300, view.ExtentHeight); // never less than the viewport
    });

    [Fact]
    public void ItemsThatLeave_LeaveTheSelection() => StaThread.Run(() =>
    {
        using var icons = new IconCache();
        var view = new ItemGridView(icons, new Host());
        view.SetItems(Items(3));

        view.ClearSelection();
        view.SetItems(Items(2));

        Assert.Empty(view.SelectedIds);
    });

    private static List<DesktopItem> Items(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new DesktopItem(new ItemId($"item {i}"), $"Item {i}", true, false, ShellIdList.Empty))];

    private sealed class Host : IItemViewHost
    {
        public void ItemViewClicked()
        {
        }

        public void OpenItem(DesktopItem item)
        {
        }

        public void ShowItemMenu(IReadOnlyList<DesktopItem> items, System.Drawing.Point screenPoint, ContextMenuOptions options)
        {
        }

        public void ShowEmptyAreaMenu()
        {
        }

        public void StartDrag(IReadOnlyList<DesktopItem> items)
        {
        }
    }
}
