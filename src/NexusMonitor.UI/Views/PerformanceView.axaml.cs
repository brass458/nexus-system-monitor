using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using NexusMonitor.UI.ViewModels;

namespace NexusMonitor.UI.Views;

public partial class PerformanceView : UserControl
{
    // ── Drag-to-reorder state ────────────────────────────────────────────────
    private PerfDeviceViewModel? _dragItem;
    private int    _dragFromIndex;
    private int    _dropTargetIndex;
    private Point  _dragStart;
    private bool   _isDragging;
    private double _itemHeight;
    private Control? _dragGrid;
    private const double GripWidth = 16.0;

    public PerformanceView()
    {
        InitializeComponent();
        Loaded += (_, _) => SetupDrag();
    }

    // ── Click-to-select on the item body ─────────────────────────────────────

    private void OnDeviceItemClick(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(null).Properties.IsLeftButtonPressed &&
            sender is Control ctrl && ctrl.DataContext is PerfDeviceViewModel device)
        {
            var vm = DataContext as PerformanceViewModel;
            if (vm is not null) vm.SelectedDevice = device;
        }
    }

    // ── Drag-to-reorder setup ────────────────────────────────────────────────

    private void SetupDrag()
    {
        PerfDeviceList.AddHandler(
            PointerPressedEvent, OnDevicePointerPressed,
            Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: false);
        PerfDeviceList.AddHandler(
            PointerMovedEvent, OnDevicePointerMoved,
            Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: false);
        PerfDeviceList.AddHandler(
            PointerReleasedEvent, OnDevicePointerReleased,
            Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: false);
    }

    private void OnDevicePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(PerfDeviceList).Properties.IsLeftButtonPressed) return;

        var vm = DataContext as PerformanceViewModel;
        if (vm is null) return;

        for (int i = 0; i < vm.Devices.Count; i++)
        {
            var container = PerfDeviceList.ContainerFromIndex(i) as Control;
            if (container is null) continue;

            var localPt = e.GetCurrentPoint(container).Position;
            if (localPt.Y < 0 || localPt.Y > container.Bounds.Height) continue;
            if (localPt.X < 0 || localPt.X >= GripWidth) continue;

            _dragFromIndex   = i;
            _dropTargetIndex = i;
            _dragItem        = vm.Devices[i];
            _dragStart       = e.GetCurrentPoint(PerfDeviceList).Position;
            _isDragging      = false;
            e.Handled        = true;

            // Also select this device
            vm.SelectedDevice = _dragItem;
            break;
        }
    }

    private void OnDevicePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragItem is null) return;
        var pos = e.GetCurrentPoint(PerfDeviceList).Position;

        if (!_isDragging)
        {
            if (Math.Abs(pos.Y - _dragStart.Y) < 5) return;
            _isDragging = true;
            _dragItem.IsDragging = true;

            var c0 = PerfDeviceList.ContainerFromIndex(0) as Control;
            _itemHeight = c0?.Bounds.Height ?? 72;

            var dragContainer = PerfDeviceList.ContainerFromIndex(_dragFromIndex) as Control;
            _dragGrid = FindDeviceRowGrid(dragContainer);
            _dragGrid?.SetValue(Animatable.TransitionsProperty, new Transitions());
        }

        var vm = DataContext as PerformanceViewModel;
        if (vm is null) return;

        _dropTargetIndex = GetDeviceIndexAt(e, vm.Devices.Count);
        _dropTargetIndex = Math.Clamp(_dropTargetIndex, 0, vm.Devices.Count - 1);

        double deltaY = pos.Y - _dragStart.Y;
        int dy = (int)Math.Round(deltaY);
        int h  = (int)Math.Round(_itemHeight);

        for (int i = 0; i < vm.Devices.Count; i++)
        {
            var container = PerfDeviceList.ContainerFromIndex(i) as Control;
            var grid = FindDeviceRowGrid(container);
            if (grid is null) continue;

            if (i == _dragFromIndex)
            {
                grid.RenderTransform = TransformOperations.Parse(
                    $"translate(0px, {dy}px) scale(1.01)");
            }
            else
            {
                int shiftY = 0;
                if (_dragFromIndex < _dropTargetIndex &&
                    i > _dragFromIndex && i <= _dropTargetIndex)
                    shiftY = -h;
                else if (_dragFromIndex > _dropTargetIndex &&
                         i >= _dropTargetIndex && i < _dragFromIndex)
                    shiftY = h;

                grid.RenderTransform = shiftY != 0
                    ? TransformOperations.Parse($"translate(0px, {shiftY}px)")
                    : TransformOperations.Parse("scale(1.0)");
            }
        }
    }

    private void OnDevicePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var wasDragging = _isDragging;
        var draggedItem = _dragItem;
        int dropIndex   = _dropTargetIndex;

        if (draggedItem is not null)
            draggedItem.IsDragging = false;

        _dragGrid?.ClearValue(Animatable.TransitionsProperty);

        if (draggedItem is not null && wasDragging)
        {
            var vm = DataContext as PerformanceViewModel;
            if (vm is not null)
            {
                for (int i = 0; i < vm.Devices.Count; i++)
                {
                    var container = PerfDeviceList.ContainerFromIndex(i) as Control;
                    var grid = FindDeviceRowGrid(container);
                    if (grid is not null)
                        grid.RenderTransform = TransformOperations.Parse("scale(1.0)");
                }

                if (dropIndex != _dragFromIndex)
                    vm.Devices.Move(_dragFromIndex, dropIndex);
            }

            DispatcherTimer.RunOnce(() =>
            {
                var settleContainer = PerfDeviceList.ContainerFromIndex(dropIndex) as Control;
                var settleGrid = FindDeviceRowGrid(settleContainer);
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
        else if (draggedItem is not null && !wasDragging)
        {
            // It was just a click on the grip area, not a drag -- select the item
            var vm = DataContext as PerformanceViewModel;
            if (vm is not null)
                vm.SelectedDevice = draggedItem;
        }

        _dragItem   = null;
        _dragGrid   = null;
        _isDragging = false;
    }

    private static Control? FindDeviceRowGrid(Control? container)
    {
        if (container is ContentPresenter cp && cp.Child is Control child)
            return child;
        return null;
    }

    private int GetDeviceIndexAt(PointerEventArgs e, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var container = PerfDeviceList.ContainerFromIndex(i) as Control;
            if (container is null) continue;
            var localPt = e.GetCurrentPoint(container).Position;
            if (localPt.Y >= 0 && localPt.Y < container.Bounds.Height)
                return i;
        }
        return count - 1;
    }
}
