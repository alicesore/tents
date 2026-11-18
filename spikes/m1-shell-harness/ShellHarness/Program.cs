// THROWAWAY HARNESS for the manual checks of M1 steps 9 and 10. Not production code. See ../README.md.
//
// One ordinary window that lists the desktop's items (read from the native desktop view) and drives the production
// Tents.Shell code: double-click opens, right-click shows the shell context menu (Shift for extended verbs). It never
// changes the desktop by itself; only commands you choose in a shell menu do (for example Delete).
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Tents.Shell.Desktop;
using Tents.Shell.Menus;

namespace Tents.Spikes.ShellHarness;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new Application();
        return app.Run(new HarnessWindow());
    }
}

internal sealed class HarnessWindow : Window
{
    private readonly DesktopItemSource _source = new();
    private readonly ShellActions _actions = new();
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Extended, FontSize = 14 };
    private readonly TextBlock _status = new() { Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };

    public HarnessWindow()
    {
        Title = "Tents shell harness (M1.9 / M1.10)";
        Width = 520;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var refresh = new Button { Content = "Refresh", Margin = new Thickness(8), Padding = new Thickness(12, 4, 12, 4), HorizontalAlignment = HorizontalAlignment.Left };
        refresh.Click += async (_, _) => await RefreshAsync();

        var help = new TextBlock
        {
            Margin = new Thickness(8, 0, 8, 8),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Text = "Double-click: open. Right-click: shell menu. Shift+right-click: extended menu. " +
                   "Ctrl/Shift+click selects several items; right-click one of them for a multi-item menu.",
        };

        var top = new StackPanel();
        top.Children.Add(refresh);
        top.Children.Add(help);
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(_status);
        root.Children.Add(_list);
        Content = root;

        _list.MouseDoubleClick += async (_, e) => await OpenAsync(e);
        _list.PreviewMouseRightButtonUp += async (_, e) => await ShowMenuAsync(e);
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) =>
        {
            _actions.Dispose();
            _source.Dispose();
        };
    }

    private async Task RefreshAsync()
    {
        try
        {
            var snapshot = await _source.ReadAsync();
            _list.ItemsSource = snapshot.Items.Select(i => new Row(i)).ToList();
            _status.Text = $"{snapshot.Count} desktop items.";
        }
        catch (Exception ex)
        {
            _status.Text = "Read failed: " + ex.Message;
        }
    }

    private async Task OpenAsync(MouseButtonEventArgs e)
    {
        if (RowAt(e) is not { } row)
        {
            return;
        }

        var result = await _actions.OpenAsync(row.Item);
        _status.Text = result.Succeeded ? "Open: started." : $"Open: failed, Win32 error {result.Win32Error}.";
    }

    private async Task ShowMenuAsync(MouseButtonEventArgs e)
    {
        if (RowAt(e) is not { } row)
        {
            return;
        }

        e.Handled = true;
        if (!_list.SelectedItems.Contains(row))
        {
            _list.SelectedItems.Clear();
            _list.SelectedItems.Add(row);
        }

        var items = _list.SelectedItems.Cast<Row>().Select(r => r.Item).ToList();
        var screen = PointToScreen(e.GetPosition(this)); // device pixels
        var options = new ContextMenuOptions(
            ExtendedVerbs: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),
            ControlDown: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));

        var result = await _actions.ShowContextMenuAsync(items, new System.Drawing.Point((int)screen.X, (int)screen.Y), options);
        _status.Text = $"Menu for {items.Count} item(s){(options.ExtendedVerbs ? ", extended" : "")}: {result.Outcome}" +
            (result.Verb is null ? "" : $", verb \"{result.Verb}\"") +
            (result.ItemsDropped > 0 ? $", {result.ItemsDropped} item(s) left out" : "") +
            (result.Outcome == ContextMenuOutcome.HostVerb ? " (Tents would start its own rename here)" : "");
        await RefreshAsync();
    }

    private Row? RowAt(MouseButtonEventArgs e) =>
        (e.OriginalSource as DependencyObject) is { } source && ItemsControl.ContainerFromElement(_list, source) is ListBoxItem container
            ? container.DataContext as Row
            : null;

    private sealed record Row(DesktopItem Item)
    {
        public override string ToString() => Item.IsFileSystem ? Item.DisplayName : Item.DisplayName + "   (virtual item)";
    }
}
