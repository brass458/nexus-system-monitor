using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NexusMonitor.UI.ViewModels;

namespace NexusMonitor.UI.Views;

public partial class ProcessesView : UserControl
{
    private bool _restoringSort; // guards against RestoreSort→Sorting→OnGridSorting feedback loop

    public ProcessesView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        ProcessGrid.Sorting += OnGridSorting;

        // Restore sort indicator from the VM — survives tab switches (View is recreated, VM is not).
        RestoreSort();

        if (DataContext is ProcessesViewModel vm)
        {
            vm.Processes.CollectionChanged += OnProcessesCollectionChanged;
            vm.PropertyChanged            += OnVmPropertyChanged;
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        ProcessGrid.Sorting -= OnGridSorting;

        if (DataContext is ProcessesViewModel vm)
        {
            vm.Processes.CollectionChanged -= OnProcessesCollectionChanged;
            vm.PropertyChanged            -= OnVmPropertyChanged;
        }
    }

    private void OnGridSorting(object? sender, DataGridColumnEventArgs e)
    {
        if (_restoringSort) return;
        if (e.Column?.SortMemberPath is not { } path) return;
        if (DataContext is not ProcessesViewModel vm) return;

        if (vm.SortMemberPath == path)
        {
            // Toggle: Ascending → Descending → cleared
            if (vm.SortDirection == ListSortDirection.Ascending)
                vm.SortDirection = ListSortDirection.Descending;
            else
                vm.SortMemberPath = null; // third click clears sort
        }
        else
        {
            // New column — starts ascending
            vm.SortMemberPath = path;
            vm.SortDirection  = ListSortDirection.Ascending;
        }
    }

    private void OnProcessesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // After Processes.Clear() (tree-mode rebuild), re-apply the column sort.
        if (e.Action == NotifyCollectionChangedAction.Reset)
            RestoreSort();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // After switching view modes, restore the sort indicator.
        if (e.PropertyName == nameof(ProcessesViewModel.IsTreeViewActive))
            RestoreSort();
    }

    private void RestoreSort()
    {
        if (DataContext is not ProcessesViewModel vm) return;
        if (vm.SortMemberPath is null) return;
        _restoringSort = true;
        try
        {
            foreach (var col in ProcessGrid.Columns)
            {
                if (col.SortMemberPath == vm.SortMemberPath)
                    col.Sort(vm.SortDirection);
                else
                    col.ClearSort();
            }
        }
        finally { _restoringSort = false; }
    }
}
