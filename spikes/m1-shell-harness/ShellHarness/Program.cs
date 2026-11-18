// THROWAWAY HARNESS for the manual checks of M1 steps 9 and 10. Not production code. See ../README.md.
//
// One ordinary window that lists the desktop's items (read from the native desktop view) and drives the production
// Tents.Shell code: double-click opens, right-click shows the shell context menu (Shift for extended verbs), items can be
// dragged out, and the window is a tent drop target. It never changes the desktop by itself; only commands you choose in a
// shell menu, and drops you make, do (exactly as on the desktop).
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Tents.Shell.Desktop;
using Tents.Shell.DragDrop;
using Tents.Shell.Menus;

namespace Tents.Spikes.ShellHarness;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new Application();
        app.DispatcherUnhandledException += (_, e) =>
        {
            // Throwaway: show the failure instead of a silent exit.
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "tents-harness-error.txt"), e.Exception.ToString());
            MessageBox.Show(e.Exception.ToString(), "Tents shell harness");
            e.Handled = true;
        };
        return app.Run(new HarnessWindow());
    }
}

internal sealed class HarnessWindow : Window, ITentDropHandler
{
    private static int _windowCount;

    private readonly DesktopItemSource _source = new();
    private readonly ShellActions _actions = new();
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Extended, FontSize = 14 };
    private readonly TextBlock _status = new() { Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
    private TentDropTarget? _dropTarget;
    private Point? _dragStart;

    public HarnessWindow()
    {
        var number = Interlocked.Increment(ref _windowCount);
        Title = $"Tents shell harness {number} (M1.9 / M1.10)";
        Width = 520;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 80 + ((number - 1) * 540);
        Top = 80;

        var refresh = new Button { Content = "Refresh", Margin = new Thickness(8), Padding = new Thickness(12, 4, 12, 4) };
        refresh.Click += async (_, _) => await RefreshAsync();
        var another = new Button { Content = "Open a second window", Margin = new Thickness(0, 8, 8, 8), Padding = new Thickness(12, 4, 12, 4) };
        another.Click += (_, _) => new HarnessWindow().Show();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(refresh);
        buttons.Children.Add(another);

        var help = new TextBlock
        {
            Margin = new Thickness(8, 0, 8, 8),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.DimGray,
            Text = "Double-click: open. Right-click: shell menu. Shift+right-click: extended menu. " +
                   "Ctrl/Shift+click selects several items; right-click one of them for a multi-item menu. " +
                   "Drag items out (to Explorer, another app, or the second window); drop files from Explorer onto this window.",
        };

        var top = new StackPanel();
        top.Children.Add(buttons);
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
        _list.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(this);
        _list.PreviewMouseLeftButtonUp += (_, _) => _dragStart = null;
        _list.PreviewMouseMove += (_, e) => StartDragIfMoved(e);
        SourceInitialized += (_, _) => _dropTarget = TentDropTarget.Register(new WindowInteropHelper(this).Handle, this);
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) =>
        {
            _dropTarget?.Dispose();
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

    // ITentDropHandler: the item under a physical screen point, or null over empty space.
    public DesktopItem? HitTest(System.Drawing.Point screenPoint)
    {
        var local = _list.PointFromScreen(new Point(screenPoint.X, screenPoint.Y));
        return _list.InputHitTest(local) is DependencyObject hit && ItemsControl.ContainerFromElement(_list, hit) is ListBoxItem { DataContext: Row row }
            ? row.Item
            : null;
    }

    // ITentDropHandler: a finished drop. A real tent would update its membership here.
    public void Dropped(TentDrop drop)
    {
        _status.Text = $"Dropped {drop.Items.Count} item(s): route {drop.Route}, effect {drop.Effect}" +
            (drop.Target is null ? "" : ", onto an item") +
            (drop.Route == DropRoute.Reassign ? " (a tent would only change membership; no file was touched)" : "");
        _ = Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(2000); // throwaway: give the shell and the native view time; the app uses the watcher
            await RefreshAsync();
        });
    }

    private void StartDragIfMoved(MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var delta = e.GetPosition(this) - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStart = null;
        var items = _list.SelectedItems.Cast<Row>().Select(r => r.Item).ToList();
        if (items.Count == 0)
        {
            return;
        }

        var effect = ShellDragSource.DoDragDrop(new WindowInteropHelper(this).Handle, items);
        _status.Text = $"Drag of {items.Count} item(s) ended: {effect}.";
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
