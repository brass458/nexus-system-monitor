using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NexusMonitor.UI.ViewModels;

namespace NexusMonitor.UI;

public partial class MainWindow : Window
{
    // ── Drag-to-reorder state ────────────────────────────────────────────────
    private NavItem? _dragItem;
    private int      _dragFromIndex;
    private int      _dropTargetIndex;
    private Point    _dragStart;
    private bool     _isDragging;
    private double   _itemHeight;
    private Control? _dragGrid;    // the dragged item's Grid (transition disabled)

    // Grip zone: the leftmost 18px of each nav item (the grip column)
    private const double GripWidth = 18.0;

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

    // ── Global keyboard shortcuts ──────────────────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;

        var vm = DataContext as MainViewModel;
        if (vm is null) return;

        bool ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;

        switch (e.Key)
        {
            // Ctrl+Tab / Ctrl+Shift+Tab — cycle sidebar tabs
            case Key.Tab when ctrl:
                CycleTab(vm, shift: (e.KeyModifiers & KeyModifiers.Shift) != 0);
                e.Handled = true;
                break;

            // Ctrl+F — focus search box on current tab
            case Key.F when ctrl:
                FocusCurrentSearch();
                e.Handled = true;
                break;

            // Ctrl+Q — quit application
            case Key.Q when ctrl:
                Close();
                e.Handled = true;
                break;

            // Ctrl+, — open Settings
            case Key.OemComma when ctrl:
                var settingsNav = vm.NavItems.FirstOrDefault(n => n.Label == "Settings");
                if (settingsNav is not null) vm.Navigate(settingsNav);
                e.Handled = true;
                break;
        }
    }

    private static void CycleTab(MainViewModel vm, bool shift)
    {
        int count   = vm.NavItems.Count;
        int current = vm.NavItems.IndexOf(vm.SelectedNavItem!);
        int next    = shift
            ? (current - 1 + count) % count
            : (current + 1) % count;
        vm.Navigate(vm.NavItems[next]);
    }

    /// <summary>
    /// Walks the visual tree to find a TextBox named "SearchBox" in the
    /// current page and focuses it.
    /// </summary>
    private void FocusCurrentSearch()
    {
        var searchBox = FindDescendant<TextBox>(this, "SearchBox");
        if (searchBox is not null)
        {
            searchBox.Focus();
            searchBox.SelectAll();
        }
    }

    private static T? FindDescendant<T>(Visual parent, string name) where T : Control
    {
        foreach (var child in parent.GetVisualChildren())
        {
            if (child is T control && control.Name == name)
                return control;
            if (child is Visual visual)
            {
                var found = FindDescendant<T>(visual, name);
                if (found is not null) return found;
            }
        }
        return null;
    }

    // ── Sidebar drag-to-reorder (iOS-style gap animation) ──────────────────

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

            var localPt = e.GetCurrentPoint(container).Position;
            if (localPt.Y < 0 || localPt.Y > container.Bounds.Height) continue;

            // Only start drag when pressing in the grip column (left GripWidth px)
            if (localPt.X < 0 || localPt.X >= GripWidth) continue;

            _dragFromIndex   = i;
            _dropTargetIndex = i;
            _dragItem        = vm.NavItems[i];
            _dragStart       = e.GetCurrentPoint(NavItemsControl).Position;
            _isDragging      = false;
            e.Handled        = true;
            break;
        }
    }

    private void OnNavPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragItem is null) return;

        var pos = e.GetCurrentPoint(NavItemsControl).Position;

        // Activate drag mode once the user has moved >= 5px vertically
        if (!_isDragging)
        {
            if (Math.Abs(pos.Y - _dragStart.Y) < 5) return;
            _isDragging = true;
            _dragItem.IsDragging = true;

            // Cache item height from first container
            var c0 = NavItemsControl.ContainerFromIndex(0) as Control;
            _itemHeight = c0?.Bounds.Height ?? 42;

            // Disable transition on dragged item so it tracks the cursor instantly
            var dragContainer = NavItemsControl.ContainerFromIndex(_dragFromIndex) as Control;
            _dragGrid = FindNavRowGrid(dragContainer);
            _dragGrid?.SetValue(Animatable.TransitionsProperty, new Transitions());
        }

        var vm = DataContext as MainViewModel;
        if (vm is null) return;

        // Calculate drop target from pointer position
        _dropTargetIndex = GetNavIndexAt(e, vm.NavItems.Count);
        _dropTargetIndex = Math.Clamp(_dropTargetIndex, 0, vm.NavItems.Count - 1);

        double deltaY = pos.Y - _dragStart.Y;
        int dy = (int)Math.Round(deltaY);
        int h  = (int)Math.Round(_itemHeight);

        // Apply visual transforms — NO Move() during drag, just visual displacement
        for (int i = 0; i < vm.NavItems.Count; i++)
        {
            var container = NavItemsControl.ContainerFromIndex(i) as Control;
            var grid = FindNavRowGrid(container);
            if (grid is null) continue;

            if (i == _dragFromIndex)
            {
                // Dragged item follows cursor directly
                grid.RenderTransform = TransformOperations.Parse(
                    $"translate(0px, {dy}px) scale(1.01)");
            }
            else
            {
                int shiftY = 0;
                if (_dragFromIndex < _dropTargetIndex &&
                    i > _dragFromIndex && i <= _dropTargetIndex)
                {
                    shiftY = -h;   // items between drag→drop shift UP
                }
                else if (_dragFromIndex > _dropTargetIndex &&
                         i >= _dropTargetIndex && i < _dragFromIndex)
                {
                    shiftY = h;    // items between drop→drag shift DOWN
                }

                grid.RenderTransform = shiftY != 0
                    ? TransformOperations.Parse($"translate(0px, {shiftY}px)")
                    : TransformOperations.Parse("scale(1.0)");
            }
        }
    }

    private void OnNavPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var wasDragging = _isDragging;
        var draggedItem = _dragItem;
        int dropIndex   = _dropTargetIndex;

        if (draggedItem is not null)
            draggedItem.IsDragging = false;

        // Restore spring transition on the dragged item
        _dragGrid?.ClearValue(Animatable.TransitionsProperty);

        if (draggedItem is not null && wasDragging)
        {
            var vm = DataContext as MainViewModel;
            if (vm is not null)
            {
                // Reset all transforms to identity — spring transition animates the settle
                for (int i = 0; i < vm.NavItems.Count; i++)
                {
                    var container = NavItemsControl.ContainerFromIndex(i) as Control;
                    var grid = FindNavRowGrid(container);
                    if (grid is not null)
                        grid.RenderTransform = TransformOperations.Parse("scale(1.0)");
                }

                // Perform the actual collection move
                if (dropIndex != _dragFromIndex)
                    vm.NavItems.Move(_dragFromIndex, dropIndex);

                vm.SaveNavOrder();
            }

            // Gentle settle: subtle pulse on the dropped item
            DispatcherTimer.RunOnce(() =>
            {
                var settleContainer = NavItemsControl.ContainerFromIndex(dropIndex) as Control;
                var settleGrid = FindNavRowGrid(settleContainer);
                if (settleGrid is not null)
                {
                    settleGrid.RenderTransform = TransformOperations.Parse("scale(1.015)");
                    DispatcherTimer.RunOnce(() =>
                    {
                        settleGrid.RenderTransform = TransformOperations.Parse("scale(1.0)");
                    }, TimeSpan.FromMilliseconds(150));
                }
            }, TimeSpan.FromMilliseconds(30));
        }

        _dragItem   = null;
        _dragGrid   = null;
        _isDragging = false;
    }

    /// <summary>Finds the Grid.nav-row inside a nav ItemsControl container.</summary>
    private static Control? FindNavRowGrid(Control? container)
    {
        if (container is ContentPresenter cp && cp.Child is Control child)
            return child;  // The Grid is the direct child of the ContentPresenter
        return null;
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
