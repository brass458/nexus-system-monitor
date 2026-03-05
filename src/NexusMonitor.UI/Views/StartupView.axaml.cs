using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NexusMonitor.UI.ViewModels;

namespace NexusMonitor.UI.Views;

public partial class StartupView : UserControl
{
    private bool _restoringSort;

    public StartupView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        StartupGrid.Sorting += OnGridSorting;
        RestoreSort();

        if (DataContext is StartupViewModel vm)
            vm.PropertyChanged += OnVmPropertyChanged;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        StartupGrid.Sorting -= OnGridSorting;

        if (DataContext is StartupViewModel vm)
            vm.PropertyChanged -= OnVmPropertyChanged;
    }

    private void OnGridSorting(object? sender, DataGridColumnEventArgs e)
    {
        if (_restoringSort) return;
        if (e.Column?.SortMemberPath is not { } path) return;
        if (DataContext is not StartupViewModel vm) return;

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

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StartupViewModel.Items))
            RestoreSort();
    }

    private void RestoreSort()
    {
        if (DataContext is not StartupViewModel vm) return;
        if (vm.SortMemberPath is null) return;

        var col = StartupGrid.Columns.FirstOrDefault(c => c.SortMemberPath == vm.SortMemberPath);
        if (col is null) return;

        _restoringSort = true;
        col.Sort(vm.SortDirection);
        Dispatcher.UIThread.Post(() => _restoringSort = false);
    }
}
