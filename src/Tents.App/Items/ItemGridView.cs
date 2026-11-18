using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tents.Core;
using Tents.Shell.Desktop;
using Tents.Shell.Icons;
using Tents.Shell.Menus;

namespace Tents.App.Items;

/// <summary>What the item view asks its tent to do. Called on the UI thread.</summary>
internal interface IItemViewHost
{
    /// <summary>The user clicked in this view (other tents clear their selection, as the desktop has one selection).</summary>
    void ItemViewClicked();

    void OpenItem(DesktopItem item);

    /// <summary>Right-click on items: the shell's context menu at a physical screen point.</summary>
    void ShowItemMenu(IReadOnlyList<DesktopItem> items, System.Drawing.Point screenPoint, ContextMenuOptions options);

    /// <summary>Right-click on empty space: the tent's own menu.</summary>
    void ShowEmptyAreaMenu();

    /// <summary>The user started dragging these items.</summary>
    void StartDrag(IReadOnlyList<DesktopItem> items);
}

/// <summary>
/// A tent's items, drawn directly: icon and label per cell, only for the cells in view. One element per tent instead of
/// one visual tree per item, so 200 items cost what the visible 20 to 40 cost, and nothing is laid out or drawn while
/// nothing changes.
/// </summary>
/// <remarks>
/// <para><b>Virtualized.</b> <see cref="OnRender"/> draws only the visible range of <see cref="ItemGrid"/>. Scrolling goes
/// through <see cref="IScrollInfo"/>, so a <see cref="ScrollViewer"/> supplies the scroll bar and the mouse wheel.</para>
/// <para><b>Lazy icons.</b> An icon is requested from the shared <see cref="IconCache"/> the first time its cell is drawn,
/// at the exact pixel size for this tent's DPI. Finished loads are collected into one redraw.</para>
/// <para><b>Input.</b> Click selects (Ctrl toggles, Shift extends), double-click opens, right-click shows the shell menu,
/// dragging an item starts an OLE drag. The tent window never takes focus, so there is no keyboard input in M1.</para>
/// <para><b>Accessibility</b> (UI Automation peers, keyboard navigation) is M2 step 7.</para>
/// </remarks>
internal sealed class ItemGridView : FrameworkElement, IScrollInfo
{
    private const double _labelFontSize = 12;
    private const double _labelGap = 4;
    private const double _iconTop = 6;

    private static readonly Typeface _typeface = new(SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private readonly IconCache _icons;
    private readonly IItemViewHost _host;
    private readonly HashSet<ItemId> _selected = [];
    private readonly Dictionary<ItemId, (string Name, FormattedText Text)> _labels = [];
    private readonly HashSet<IconKey> _loading = [];
    private readonly HashSet<IconKey> _failed = [];
    private IReadOnlyList<DesktopItem> _items = [];
    private Size _viewport;
    private double _offset;
    private double _dpiScale;
    private int _hover = -1;
    private int _anchor = -1;
    private int _pendingSingleSelect = -1;
    private Point? _dragStart;
    private bool _redrawQueued;

    public ItemGridView(IconCache icons, IItemViewHost host)
    {
        ArgumentNullException.ThrowIfNull(icons);
        ArgumentNullException.ThrowIfNull(host);
        _icons = icons;
        _host = host;
        _dpiScale = 1;
        ClipToBounds = true;
        Focusable = false;
    }

    public IReadOnlyList<DesktopItem> Items => _items;

    public IReadOnlyCollection<ItemId> SelectedIds => _selected;

    internal ItemGrid Grid => new(_viewport.Width, _items.Count);

    /// <summary>Shows a new item list. Selection is kept for items that are still there.</summary>
    public void SetItems(IReadOnlyList<DesktopItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items;
        var present = items.Select(i => i.Id).ToHashSet();
        _selected.IntersectWith(present);
        foreach (var gone in _labels.Keys.Where(id => !present.Contains(id)).ToList())
        {
            _labels.Remove(gone);
        }

        _failed.Clear(); // a changed item may have an icon now
        _anchor = Math.Min(_anchor, items.Count - 1);
        _hover = -1;
        _offset = Grid.ClampOffset(_offset, _viewport.Height);
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateVisual();
    }

    public void ClearSelection()
    {
        if (_selected.Count > 0)
        {
            _selected.Clear();
            InvalidateVisual();
        }
    }

    /// <summary>The item under a physical screen point, or null.</summary>
    public DesktopItem? ItemAtScreen(System.Drawing.Point screenPoint)
    {
        var index = Grid.IndexAt(FromScreen(screenPoint), _offset);
        return index >= 0 ? _items[index] : null;
    }

    /// <summary>Where a drop at a physical screen point inserts (index into <see cref="Items"/>).</summary>
    public int InsertionIndexAtScreen(System.Drawing.Point screenPoint) => Grid.InsertionIndexAt(FromScreen(screenPoint), _offset);

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? ItemGrid.CellWidth * 4 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (finalSize != _viewport)
        {
            _viewport = finalSize;
            _offset = Grid.ClampOffset(_offset, _viewport.Height);
            ScrollOwner?.InvalidateScrollInfo();
        }

        return finalSize;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        _dpiScale = newDpi.DpiScaleX;
        _labels.Clear();
        _failed.Clear();
        InvalidateVisual();
        base.OnDpiChanged(oldDpi, newDpi);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        _dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;

        // A transparent background, so clicks on empty space reach the view.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(_viewport));
        var grid = Grid;
        var (first, count) = grid.VisibleRange(_offset, _viewport.Height);
        var pixelSize = ItemGrid.IconPixelSize(_dpiScale);
        for (var i = first; i < first + count; i++)
        {
            var item = _items[i];
            var cell = grid.CellRect(i);
            cell.Offset(0, -_offset);
            var highlight = _selected.Contains(item.Id) ? TentTheme.Selection : (i == _hover ? TentTheme.Hover : null);
            if (highlight is not null)
            {
                drawingContext.DrawRoundedRectangle(highlight, null, Inset(cell, 2), 4, 4);
            }

            if (GetIcon(item, pixelSize) is { } icon)
            {
                var iconRect = new Rect(cell.X + ((cell.Width - ItemGrid.IconSize) / 2), cell.Y + _iconTop, ItemGrid.IconSize, ItemGrid.IconSize);
                drawingContext.DrawImage(icon, iconRect);
            }

            var label = GetLabel(item);
            drawingContext.DrawText(label, new Point(cell.X + _labelGap, cell.Y + _iconTop + ItemGrid.IconSize + _labelGap));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);
        _host.ItemViewClicked();
        var position = e.GetPosition(this);
        var index = Grid.IndexAt(position, _offset);
        _pendingSingleSelect = -1;
        _dragStart = null;

        if (e.ClickCount == 2 && index >= 0)
        {
            SelectOnly(index);
            _host.OpenItem(_items[index]);
            e.Handled = true;
            return;
        }

        var modifiers = Keyboard.Modifiers;
        if (index < 0)
        {
            if (!modifiers.HasFlag(ModifierKeys.Control))
            {
                ClearSelection();
            }

            return;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift) && _anchor >= 0)
        {
            SelectRange(_anchor, index, add: modifiers.HasFlag(ModifierKeys.Control));
        }
        else if (modifiers.HasFlag(ModifierKeys.Control))
        {
            if (!_selected.Remove(_items[index].Id))
            {
                _selected.Add(_items[index].Id);
            }

            _anchor = index;
            InvalidateVisual();
        }
        else if (_selected.Contains(_items[index].Id))
        {
            // Keep a multiple selection for a drag; a click without a drag selects only this item (on button up).
            _pendingSingleSelect = index;
        }
        else
        {
            SelectOnly(index);
        }

        _dragStart = position;
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_pendingSingleSelect >= 0 && _pendingSingleSelect < _items.Count)
        {
            SelectOnly(_pendingSingleSelect);
        }

        _pendingSingleSelect = -1;
        _dragStart = null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        var position = e.GetPosition(this);
        SetHover(Grid.IndexAt(position, _offset));

        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var delta = position - start;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStart = null;
        _pendingSingleSelect = -1;
        var dragged = SelectedItems();
        if (dragged.Count > 0)
        {
            _host.StartDrag(dragged);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        SetHover(-1);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseRightButtonUp(e);
        _host.ItemViewClicked();
        var position = e.GetPosition(this);
        var index = Grid.IndexAt(position, _offset);
        e.Handled = true;
        if (index < 0)
        {
            ClearSelection();
            _host.ShowEmptyAreaMenu();
            return;
        }

        if (!_selected.Contains(_items[index].Id))
        {
            SelectOnly(index);
        }

        var screen = PointToScreen(position); // physical pixels
        var modifiers = Keyboard.Modifiers;
        _host.ShowItemMenu(
            SelectedItems(),
            new System.Drawing.Point((int)screen.X, (int)screen.Y),
            new ContextMenuOptions(ExtendedVerbs: modifiers.HasFlag(ModifierKeys.Shift), ControlDown: modifiers.HasFlag(ModifierKeys.Control)));
    }

    // ---- IScrollInfo: vertical scrolling only ----

    public bool CanVerticallyScroll { get; set; }

    public bool CanHorizontallyScroll { get; set; }

    public double ExtentWidth => _viewport.Width;

    public double ExtentHeight => Math.Max(Grid.ExtentHeight, _viewport.Height);

    public double ViewportWidth => _viewport.Width;

    public double ViewportHeight => _viewport.Height;

    public double HorizontalOffset => 0;

    public double VerticalOffset => _offset;

    public ScrollViewer? ScrollOwner { get; set; }

    public void LineUp() => SetVerticalOffset(_offset - (ItemGrid.CellHeight / 3));

    public void LineDown() => SetVerticalOffset(_offset + (ItemGrid.CellHeight / 3));

    public void PageUp() => SetVerticalOffset(_offset - _viewport.Height);

    public void PageDown() => SetVerticalOffset(_offset + _viewport.Height);

    public void MouseWheelUp() => SetVerticalOffset(_offset - ItemGrid.CellHeight);

    public void MouseWheelDown() => SetVerticalOffset(_offset + ItemGrid.CellHeight);

    public void LineLeft()
    {
    }

    public void LineRight()
    {
    }

    public void PageLeft()
    {
    }

    public void PageRight()
    {
    }

    public void MouseWheelLeft()
    {
    }

    public void MouseWheelRight()
    {
    }

    public void SetHorizontalOffset(double offset)
    {
    }

    public void SetVerticalOffset(double offset)
    {
        var clamped = Grid.ClampOffset(offset, _viewport.Height);
        if (clamped.Equals(_offset))
        {
            return;
        }

        _offset = clamped;
        _hover = -1;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateVisual();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;

    private static Rect Inset(Rect rect, double by) => new(rect.X + by, rect.Y + by, Math.Max(0, rect.Width - (2 * by)), Math.Max(0, rect.Height - (2 * by)));

    private Point FromScreen(System.Drawing.Point screenPoint) =>
        PresentationSource.FromVisual(this) is null ? new Point(-1, -1) : PointFromScreen(new Point(screenPoint.X, screenPoint.Y));

    private List<DesktopItem> SelectedItems() => [.. _items.Where(i => _selected.Contains(i.Id))];

    private void SelectOnly(int index)
    {
        _selected.Clear();
        _selected.Add(_items[index].Id);
        _anchor = index;
        InvalidateVisual();
    }

    private void SelectRange(int from, int to, bool add)
    {
        if (!add)
        {
            _selected.Clear();
        }

        for (var i = Math.Min(from, to); i <= Math.Max(from, to) && i < _items.Count; i++)
        {
            _selected.Add(_items[i].Id);
        }

        InvalidateVisual();
    }

    private void SetHover(int index)
    {
        if (index != _hover)
        {
            _hover = index;
            InvalidateVisual();
        }
    }

    private BitmapSource? GetIcon(DesktopItem item, int pixelSize)
    {
        if (_icons.TryGetCached(item.Id, pixelSize, out var icon))
        {
            return icon;
        }

        var key = new IconKey(item.Id, pixelSize);
        if (!_failed.Contains(key) && _loading.Add(key))
        {
            _ = LoadIconAsync(item, key);
        }

        return null;
    }

    private async Task LoadIconAsync(DesktopItem item, IconKey key)
    {
        try
        {
            await _icons.GetAsync(item, key.PixelSize); // resumes on the UI thread
        }
        catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            _failed.Add(key); // not asked again until the item changes
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        finally
        {
            _loading.Remove(key);
        }

        QueueRedraw();
    }

    // Many icons finish close together; draw once for all of them.
    private void QueueRedraw()
    {
        if (_redrawQueued)
        {
            return;
        }

        _redrawQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _redrawQueued = false;
            InvalidateVisual();
        });
    }

    private FormattedText GetLabel(DesktopItem item)
    {
        if (_labels.TryGetValue(item.Id, out var cached) && string.Equals(cached.Name, item.DisplayName, StringComparison.Ordinal))
        {
            return cached.Text;
        }

        var text = new FormattedText(item.DisplayName, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, _typeface, _labelFontSize, TentTheme.Text, _dpiScale)
        {
            MaxTextWidth = ItemGrid.CellWidth - (2 * _labelGap),
            MaxTextHeight = Math.Ceiling(_labelFontSize * 1.4 * 2),
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
        };
        _labels[item.Id] = (item.DisplayName, text);
        return text;
    }
}
