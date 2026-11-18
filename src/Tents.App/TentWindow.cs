using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Tents.Core;
using Tents.Shell.Hosting;

namespace Tents.App;

/// <summary>What a tent window asks the app to do. Called on the UI thread.</summary>
internal interface ITentCommands
{
    /// <summary>Exit Tents.</summary>
    void Exit();
}

/// <summary>
/// One tent on the desktop: a borderless WPF window attached to the desktop by an <see cref="IDesktopHost"/> and placed
/// in physical pixels.
/// </summary>
/// <remarks>
/// <para><b>Creation order</b> (spike 1): owner set before the handle exists, so it is set at creation; then the host's
/// styles; then the bounds; then shown without activation.</para>
/// <para><b>Never blocks.</b> Everything here is UI work. Shell and file work happens elsewhere and arrives as
/// <see cref="Update"/> calls.</para>
/// </remarks>
internal sealed class TentWindow : Window, ITentWindow
{
    private readonly IDesktopHost _host;
    private readonly TextBlock _title;
    private Tent _tent;
    private nint _handle;
    private PixelRect? _placing;

    public TentWindow(Tent tent, IDesktopHost host, ITentCommands commands)
    {
        ArgumentNullException.ThrowIfNull(tent);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(commands);
        _tent = tent;
        _host = host;

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
        var titleBar = new Border { Height = TentTheme.TitleBarHeight, Background = TentTheme.TitleBar, Child = _title };
        titleBar.ContextMenu = BuildTitleMenu(commands);

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(titleBar, Dock.Top);
        root.Children.Add(titleBar);
        root.Children.Add(new Border());
        Content = root;
        ShowTent(tent);
    }

    public TentId TentId => _tent.Id;

    public nint Handle => _handle;

    /// <summary>Creates the native window attached to the desktop, places it and shows it without activating it.</summary>
    public void Attach()
    {
        var helper = new WindowInteropHelper(this) { Owner = _host.GetOwnerForNewWindow() };
        _handle = helper.EnsureHandle();
        _host.Prepare(_handle);
        HwndSource.FromHwnd(_handle)!.AddHook(Hook);
        Place(_tent.Bounds);
        Show();
        Place(_tent.Bounds); // WPF may re-apply its own size on the first show.
    }

    public void Update(Tent tent)
    {
        ArgumentNullException.ThrowIfNull(tent);
        var previous = _tent;
        _tent = tent;
        ShowTent(tent);
        if (_handle != 0 && previous.Bounds != tent.Bounds)
        {
            Place(tent.Bounds);
        }
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

    void ITentWindow.Close()
    {
        if (_handle != 0)
        {
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _handle = 0;
        base.OnClosed(e);
    }

    private static ContextMenu BuildTitleMenu(ITentCommands commands)
    {
        var exit = new MenuItem { Header = "E_xit Tents" };
        exit.Click += (_, _) => commands.Exit();
        var menu = new ContextMenu();
        menu.Items.Add(exit);
        return menu;
    }

    private void ShowTent(Tent tent) => _title.Text = tent.Title;

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

/// <summary>Colors and sizes shared by every tent. Frozen brushes, created once.</summary>
internal static class TentTheme
{
    public const double TitleBarHeight = 28;

    public static Brush Background { get; } = Frozen(Color.FromRgb(0x1E, 0x24, 0x2C));

    public static Brush TitleBar { get; } = Frozen(Color.FromRgb(0x2A, 0x32, 0x3C));

    public static Brush Border { get; } = Frozen(Color.FromRgb(0x3A, 0x44, 0x50));

    public static Brush Text { get; } = Frozen(Color.FromRgb(0xF2, 0xF4, 0xF6));

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
