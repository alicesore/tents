using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Tents.App.Chrome;
using Tents.App.Items;
using Tents.Core;
using Tents.Shell.Desktop;
using Tents.Shell.DragDrop;
using Tents.Shell.Hosting;
using Tents.Shell.Icons;
using Tents.Shell.Menus;

namespace Tents.App;

/// <summary>What a tent window asks the app to do. Called on the UI thread.</summary>
internal interface ITentCommands
{
    /// <summary>Exit Tents.</summary>
    void Exit();

    /// <summary>The user clicked into this tent.</summary>
    void TentClicked(TentId tent);

    /// <summary>Run an item's default command (double-click).</summary>
    void Open(DesktopItem item);

    /// <summary>Show the shell's context menu for items.</summary>
    void ShowItemMenu(IReadOnlyList<DesktopItem> items, System.Drawing.Point screenPoint, ContextMenuOptions options);

    /// <summary>A drop on a tent finished; <paramref name="insertionIndex"/> is where in the tent it landed.</summary>
    void Dropped(TentId tent, TentDrop drop, int insertionIndex);

    /// <summary>The user moved or resized the tent; <paramref name="bounds"/> are its new stored bounds.</summary>
    void BoundsChanged(TentId tent, PixelRect bounds);

    /// <summary>The user clicked the title bar without dragging it: roll the tent up or expand it.</summary>
    void TitleClicked(TentId tent);
}

/// <summary>
/// One tent on the desktop: a borderless WPF window attached to the desktop by an <see cref="IDesktopHost"/> and placed
/// in physical pixels, with a title bar and the tent's items.
/// </summary>
/// <remarks>
/// <para><b>Creation order</b> (spike 1): owner set before the handle exists, so it is set at creation; then the host's
/// styles; then the bounds; then shown without activation.</para>
/// <para><b>Never blocks.</b> Everything here is UI work. Shell and file work happens elsewhere and arrives as
/// <see cref="Update"/> calls. The OLE drag loop started from here pumps messages.</para>
/// </remarks>
internal sealed class TentWindow : Window, ITentWindow, IItemViewHost, ITentDropHandler
{
    private readonly IDesktopHost _host;
    private readonly ITentCommands _commands;
    private readonly TextBlock _title;
    private readonly ItemGridView _items;
    private readonly ContextMenu _menu;
    private Tent _tent;
    private nint _handle;
    private PixelRect? _placing;
    private TentDropTarget? _dropTarget;
    private DragSession? _drag;

    /// <param name="snapGrid">Grid in physical pixels that moves and resizes snap to; 0 for none (the default).</param>
    public TentWindow(Tent tent, IReadOnlyList<DesktopItem> items, IDesktopHost host, IconCache icons, ITentCommands commands, int snapGrid = 0)
    {
        ArgumentNullException.ThrowIfNull(tent);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(commands);
        _tent = tent;
        _host = host;
        _commands = commands;
        SnapGrid = Math.Max(0, snapGrid);

        Title = "Tents";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = TentTheme.Background;
        BorderBrush = TentTheme.Border;
        BorderThickness = new Thickness(1);

        _title = new TextBlock
        {
            Foreground = TentTheme.Text,
            FontSize = 13,
            Margin = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        _menu = BuildTentMenu(commands);
        var titleBar = new Border { Height = TentTheme.TitleBarHeight, Background = TentTheme.TitleBar, Child = _title, ContextMenu = _menu };

        _items = new ItemGridView(icons, this);
        _items.SetItems(items);
        var scroller = new ScrollViewer
        {
            CanContentScroll = true, // the view scrolls itself (IScrollInfo)
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Content = _items,
        };

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(titleBar, Dock.Top);
        root.Children.Add(titleBar);
        root.Children.Add(scroller);
        Content = root;
        ShowTent(tent);
    }

    public TentId TentId => _tent.Id;

    public int SnapGrid { get; }

    public nint Handle => _handle;

    /// <summary>Creates the native window attached to the desktop, places it and shows it without activating it.</summary>
    public void Attach()
    {
        var helper = new WindowInteropHelper(this) { Owner = _host.GetOwnerForNewWindow() };
        _handle = helper.EnsureHandle();
        _host.Prepare(_handle);
        HwndSource.FromHwnd(_handle)!.AddHook(Hook);
        Place(WindowBounds(_tent));
        Show();
        Place(WindowBounds(_tent)); // WPF may re-apply its own size on the first show.
        _dropTarget = TentDropTarget.Register(_handle, this);
    }

    public void Update(Tent tent, IReadOnlyList<DesktopItem> items)
    {
        ArgumentNullException.ThrowIfNull(tent);
        ArgumentNullException.ThrowIfNull(items);
        var previous = _tent;
        _tent = tent;
        // A re-read that changed nothing for this tent must not redraw it (nothing renders unless something changed).
        if (!items.SequenceEqual(_items.Items))
        {
            _items.SetItems(items);
        }

        ShowTent(tent);
        if (_handle != 0 && (previous.Bounds != tent.Bounds || previous.IsRolledUp != tent.IsRolledUp))
        {
            Place(WindowBounds(tent));
        }
    }

    public void ClearSelection() => _items.ClearSelection();

    void ITentWindow.Close()
    {
        if (_handle != 0)
        {
            Close();
        }
    }

    // ---- IItemViewHost ----

    public void ItemViewClicked() => _commands.TentClicked(_tent.Id);

    public void OpenItem(DesktopItem item) => _commands.Open(item);

    public void ShowItemMenu(IReadOnlyList<DesktopItem> items, System.Drawing.Point screenPoint, ContextMenuOptions options) =>
        _commands.ShowItemMenu(items, screenPoint, options);

    public void ShowEmptyAreaMenu()
    {
        _menu.PlacementTarget = this;
        _menu.IsOpen = true;
    }

    public void StartDrag(IReadOnlyList<DesktopItem> items)
    {
        if (_handle != 0)
        {
            ShellDragSource.DoDragDrop(_handle, items); // modal OLE loop; it pumps messages
        }
    }

    // ---- ITentDropHandler ----

    DesktopItem? ITentDropHandler.HitTest(System.Drawing.Point screenPoint) => _items.ItemAtScreen(screenPoint);

    void ITentDropHandler.Dropped(TentDrop drop) => _commands.Dropped(_tent.Id, drop, _items.InsertionIndexAtScreen(drop.ScreenPoint));

    // ---- Move and resize (M1 step 14) ----
    //
    // The title bar moves the tent and the edges resize it. A press on a handle captures the mouse; the window follows
    // the pointer in physical pixels; releasing reports the final bounds once. While nothing is dragged, these handlers
    // only update the cursor over the edges.

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var position = e.GetPosition(this);
        var handle = HandleAt(position);
        if (handle == DragHandle.None || _handle == 0 || NativeWindow.GetBounds(_handle) is not { } bounds)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            return;
        }

        _commands.TentClicked(_tent.Id);
        _drag = new DragSession(handle, PointToScreen(position), bounds);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var position = e.GetPosition(this);
        if (_drag is not { } drag)
        {
            Cursor = CursorFor(HandleAt(position));
            base.OnPreviewMouseMove(e);
            return;
        }

        e.Handled = true;
        var delta = PointToScreen(position) - drag.StartScreen; // physical pixels
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (!drag.Moved && !TentDrag.IsDrag(delta, SystemParameters.MinimumHorizontalDragDistance * scale, SystemParameters.MinimumVerticalDragDistance * scale))
        {
            return;
        }

        drag.Moved = true;
        var start = drag.StartBounds;
        if (drag.Handle == DragHandle.Move && NativeWindow.GetBounds(_handle) is { } current)
        {
            // Crossing to a monitor with another scale rescales the window (WM_DPICHANGED); keep the new size.
            start = start.WithSize(current.Width, current.Height);
        }

        var next = TentDrag.Apply(
            start,
            drag.Handle,
            (int)Math.Round(delta.X),
            (int)Math.Round(delta.Y),
            (int)Math.Round(TentDrag.MinWidth * scale),
            (int)Math.Round(MinimumHeight(scale)),
            SnapGrid);
        NativeWindow.SetBounds(_handle, next);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (_drag is not null)
        {
            EndDrag();
            e.Handled = true;
            return;
        }

        base.OnPreviewMouseLeftButtonUp(e);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        EndDrag(); // capture taken away (another window, a menu): keep where the tent got to
        base.OnLostMouseCapture(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_drag is null)
        {
            Cursor = null;
        }

        base.OnMouseLeave(e);
    }

    private static Cursor? CursorFor(DragHandle handle) => handle switch
    {
        DragHandle.Left or DragHandle.Right => Cursors.SizeWE,
        DragHandle.Top or DragHandle.Bottom => Cursors.SizeNS,
        DragHandle.TopLeft or DragHandle.BottomRight => Cursors.SizeNWSE,
        DragHandle.TopRight or DragHandle.BottomLeft => Cursors.SizeNESW,
        _ => null,
    };

    private DragHandle HandleAt(Point position) =>
        TentDrag.HandleAt(position, new Size(ActualWidth, ActualHeight), TentTheme.TitleBarHeight, _tent.IsRolledUp);

    private static double MinimumHeight(double scale) => Math.Max(TentDrag.MinHeight, TentTheme.TitleBarHeight + 2) * scale;

    private void EndDrag()
    {
        if (_drag is not { } drag)
        {
            return;
        }

        _drag = null; // before releasing: the release raises LostMouseCapture
        ReleaseMouseCapture();
        if (drag.Moved && _handle != 0 && NativeWindow.GetBounds(_handle) is { } window)
        {
            var bounds = TentGeometry.StoredBounds(_tent, window);
            _tent = _tent with { Bounds = bounds };
            _commands.BoundsChanged(_tent.Id, bounds);
        }
        else if (!drag.Moved && drag.Handle == DragHandle.Move)
        {
            _commands.TitleClicked(_tent.Id);
        }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        // A rolled-up tent is as tall as its title bar at the monitor's scale; once a placement or drag has finished,
        // fit it to the new scale.
        if (_tent.IsRolledUp && _handle != 0)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_tent.IsRolledUp && _handle != 0 && _drag is null && _placing is null && NativeWindow.GetBounds(_handle) is { } window)
                {
                    var fitted = TentGeometry.WindowBounds(_tent with { Bounds = TentGeometry.StoredBounds(_tent, window) }, newDpi.DpiScaleX);
                    Place(fitted);
                }
            });
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _dropTarget?.Dispose();
        _dropTarget = null;
        _handle = 0;
        base.OnClosed(e);
    }

    private static ContextMenu BuildTentMenu(ITentCommands commands)
    {
        var exit = new MenuItem { Header = "E_xit Tents" };
        exit.Click += (_, _) => commands.Exit();
        var menu = new ContextMenu();
        menu.Items.Add(exit);
        return menu;
    }

    /// <summary>
    /// Moves the window to physical-pixel bounds. A DPI change caused by the move keeps these bounds instead of
    /// Windows' rescaled suggestion (<see cref="DpiChange"/>).
    /// </summary>
    private void Place(PixelRect bounds)
    {
        _placing = bounds;
        try
        {
            NativeWindow.SetBounds(_handle, bounds);

            // Belt and braces: if the DPI change was applied before the rewrite above could take effect (WPF's own
            // handling runs in its HwndTarget hook), the window is now on the target monitor, so a second move does not
            // change the DPI again and sticks.
            if (NativeWindow.GetBounds(_handle) is { } actual && actual != bounds)
            {
                NativeWindow.SetBounds(_handle, bounds);
            }
        }
        finally
        {
            _placing = null;
        }
    }

    private void ShowTent(Tent tent) => _title.Text = TentGeometry.TitleText(tent, _items.Items.Count);

    private PixelRect WindowBounds(Tent tent) => TentGeometry.WindowBounds(tent, VisualTreeHelper.GetDpi(this).DpiScaleX);

    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == DpiChange.WmDpiChanged)
        {
            // Not handled: WPF rescales the content and applies the (possibly rewritten) rectangle.
            DpiChange.Apply(lParam, _placing);
            return 0;
        }

        if (_host.ProcessMessage(hwnd, msg, wParam, lParam, out var result))
        {
            handled = true;
            return result;
        }

        return 0;
    }
}

/// <summary>One press-and-drag on a tent's title bar or edge.</summary>
internal sealed class DragSession(DragHandle handle, Point startScreen, PixelRect startBounds)
{
    public DragHandle Handle { get; } = handle;

    /// <summary>Where the press was, in physical screen pixels.</summary>
    public Point StartScreen { get; } = startScreen;

    public PixelRect StartBounds { get; } = startBounds;

    /// <summary>The pointer has moved past the drag threshold.</summary>
    public bool Moved { get; set; }
}

/// <summary>Colors and sizes shared by every tent. Frozen brushes, created once.</summary>
internal static class TentTheme
{
    public const double TitleBarHeight = 28;

    public static Brush Background { get; } = Frozen(Color.FromRgb(0x1E, 0x24, 0x2C));

    public static Brush TitleBar { get; } = Frozen(Color.FromRgb(0x2A, 0x32, 0x3C));

    public static Brush Border { get; } = Frozen(Color.FromRgb(0x3A, 0x44, 0x50));

    public static Brush Text { get; } = Frozen(Color.FromRgb(0xF2, 0xF4, 0xF6));

    public static Brush Hover { get; } = Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));

    public static Brush Selection { get; } = Frozen(Color.FromArgb(0x70, 0x4A, 0x90, 0xD9));

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
