using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NexusMonitor.UI.ViewModels;

namespace NexusMonitor.UI;

public partial class MainWindow : Window
{
    // ── Drag-to-reorder state ────────────────────────────────────────────────
    private NavItem? _dragItem;
    private int      _dragFromIndex;
    private Point    _dragStart;
    private bool     _isDragging;

    // Grip zone: the leftmost 20px of each nav item (the ⠿ column)
    private const double GripWidth = 20.0;

    public MainWindow()
    {
        InitializeComponent();

        // Request acrylic/blur transparency — set in code since XAML type conversion
        // differs across Avalonia versions.
        TransparencyLevelHint =
        [
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Blur,
            WindowTransparencyLevel.Mica,
            WindowTransparencyLevel.None
        ];

        // Dispose all cached ViewModels when the window closes.
        Closed += (_, _) => (DataContext as IDisposable)?.Dispose();

        // Hook drag-to-reorder once the visual tree is ready.
        Loaded += (_, _) => SetupNavDrag();
    }

    // ── Window chrome ────────────────────────────────────────────────────────

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void OnMaximize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    // ── Sidebar drag-to-reorder ──────────────────────────────────────────────

    private void SetupNavDrag()
    {
        // Use tunneling so we intercept before the Button children handle the event
        NavItemsControl.AddHandler(
            PointerPressedEvent,
            OnNavPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: false);

        NavItemsControl.AddHandler(
            PointerMovedEvent,
            OnNavPointerMoved,
            RoutingStrategies.Tunnel,
            handledEventsToo: false);

        NavItemsControl.AddHandler(
            PointerReleasedEvent,
            OnNavPointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: false);
    }

    private void OnNavPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(NavItemsControl).Properties.IsLeftButtonPressed) return;

        var vm = DataContext as MainViewModel;
        if (vm is null) return;

        // Find which container was pressed and whether it's in the grip zone
        for (int i = 0; i < vm.NavItems.Count; i++)
        {
            var container = NavItemsControl.ContainerFromIndex(i) as Control;
            if (container is null) continue;

            // Convert pointer position to this container's local coordinates
            var localPt = e.GetCurrentPoint(container).Position;
            if (localPt.Y < 0 || localPt.Y > container.Bounds.Height) continue;

            // Only start drag when pressing in the grip column (left GripWidth px)
            if (localPt.X < 0 || localPt.X >= GripWidth) continue;

            _dragFromIndex = i;
            _dragItem      = vm.NavItems[i];
            _dragStart     = e.GetCurrentPoint(NavItemsControl).Position;
            _isDragging    = false;
            e.Handled      = true;
            break;
        }
    }

    private void OnNavPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragItem is null) return;

        var pos = e.GetCurrentPoint(NavItemsControl).Position;

        // Activate drag mode once the user has moved ≥ 5px vertically
        if (!_isDragging)
        {
            if (Math.Abs(pos.Y - _dragStart.Y) < 5) return;
            _isDragging = true;
        }

        var vm = DataContext as MainViewModel;
        if (vm is null) return;

        int newIndex = GetNavIndexAt(e, vm.NavItems.Count);
        if (newIndex >= 0 && newIndex < vm.NavItems.Count && newIndex != _dragFromIndex)
        {
            vm.NavItems.Move(_dragFromIndex, newIndex);
            _dragFromIndex = newIndex;
        }
    }

    private void OnNavPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragItem is not null && _isDragging)
        {
            // Persist the new order
            (DataContext as MainViewModel)?.SaveNavOrder();
        }

        _dragItem   = null;
        _isDragging = false;
    }

    /// <summary>
    /// Returns the index of the nav container whose midpoint is below the current
    /// pointer Y position — used to determine the drop target during drag.
    /// </summary>
    private int GetNavIndexAt(PointerEventArgs e, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var container = NavItemsControl.ContainerFromIndex(i) as Control;
            if (container is null) continue;

            var localPt = e.GetCurrentPoint(container).Position;
            if (localPt.Y >= 0 && localPt.Y < container.Bounds.Height)
                return i;
        }
        return count - 1;
    }
}
