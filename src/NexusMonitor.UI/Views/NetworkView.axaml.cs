using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NexusMonitor.UI.ViewModels;

namespace NexusMonitor.UI.Views;

public partial class NetworkView : UserControl
{
    private bool _restoringSort;
    private NetworkViewModel? _previousVm;

    public NetworkView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_previousVm is not null)
            _previousVm.PropertyChanged -= OnViewModelPropertyChanged;

        if (DataContext is NetworkViewModel vm)
        {
            _previousVm = vm;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            ApplyThroughputVisibility(vm.ShowThroughputColumns);
        }
        else
        {
            _previousVm = null;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        ConnectionGrid.Sorting += OnGridSorting;
        RestoreSort();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        ConnectionGrid.Sorting -= OnGridSorting;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NetworkViewModel.ShowThroughputColumns))
            ApplyThroughputVisibility(((NetworkViewModel)DataContext!).ShowThroughputColumns);
    }

    private void ApplyThroughputVisibility(bool show)
    {
        foreach (var col in ConnectionGrid.Columns)
        {
            if (col.Header is string h && (h == "\u2191 Send" || h == "\u2193 Recv"))
                col.IsVisible = show;
        }
    }

    private void OnGridSorting(object? sender, DataGridColumnEventArgs e)
    {
        if (_restoringSort) return;
        if (e.Column?.SortMemberPath is not { } path) return;
        if (DataContext is not NetworkViewModel vm) return;

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

    private void RestoreSort()
    {
        if (DataContext is not NetworkViewModel vm) return;
        if (vm.SortMemberPath is null) return;

        var col = ConnectionGrid.Columns.FirstOrDefault(c => c.SortMemberPath == vm.SortMemberPath);
        if (col is null) return;

        _restoringSort = true;
        col.Sort(vm.SortDirection);
        Dispatcher.UIThread.Post(() => _restoringSort = false);
    }
}
