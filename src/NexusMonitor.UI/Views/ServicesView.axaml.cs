using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NexusMonitor.UI.ViewModels;

namespace NexusMonitor.UI.Views;

public partial class ServicesView : UserControl
{
    private bool _restoringSort;

    public ServicesView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        ServicesGrid.Sorting += OnGridSorting;
        RestoreSort();

        if (DataContext is ServicesViewModel vm)
            vm.Services.CollectionChanged += OnServicesCollectionChanged;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        ServicesGrid.Sorting -= OnGridSorting;

        if (DataContext is ServicesViewModel vm)
            vm.Services.CollectionChanged -= OnServicesCollectionChanged;
    }

    private void OnGridSorting(object? sender, DataGridColumnEventArgs e)
    {
        if (_restoringSort) return;
        if (e.Column?.SortMemberPath is not { } path) return;
        if (DataContext is not ServicesViewModel vm) return;

        if (vm.SortMemberPath == path)
        {
            if (vm.SortDirection == ListSortDirection.Ascending)
                vm.SortDirection = ListSortDirection.Descending;
            else
                vm.SortMemberPath = null;
        }
        else
        {
            vm.SortMemberPath = path;
            vm.SortDirection  = ListSortDirection.Ascending;
        }
    }

    private void OnServicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
            RestoreSort();
    }

    private void RestoreSort()
    {
        if (DataContext is not ServicesViewModel vm) return;
        if (vm.SortMemberPath is null) return;

        var col = ServicesGrid.Columns.FirstOrDefault(c => c.SortMemberPath == vm.SortMemberPath);
        if (col is null) return;

        // col.Sort() posts ProcessSort asynchronously (same dispatcher priority).
        // Keep _restoringSort=true until all those callbacks have fired so OnGridSorting
        // ignores them. Posting the reset at the same priority guarantees FIFO ordering.
        _restoringSort = true;
        col.Sort(vm.SortDirection);
        Dispatcher.UIThread.Post(() => _restoringSort = false);
    }
}
