using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NexusMonitor.UI.ViewModels;

namespace NexusMonitor.UI.Views;

public partial class ProcessesView : UserControl
{
    // Saved sort state — restored after tab switches and tree-mode rebuilds.
    private string? _sortMemberPath;
    private ListSortDirection _sortDir = ListSortDirection.Ascending;

    public ProcessesView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        ProcessGrid.Sorting += OnGridSorting;

        // Restore sort indicator after tab switch (DataGrid may have lost it).
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
        if (e.Column?.SortMemberPath is not { } path) return;

        if (_sortMemberPath == path)
        {
            // Toggle: Ascending → Descending → cleared
            if (_sortDir == ListSortDirection.Ascending)
                _sortDir = ListSortDirection.Descending;
            else
                _sortMemberPath = null; // third click clears sort
        }
        else
        {
            // New column — starts ascending
            _sortMemberPath = path;
            _sortDir        = ListSortDirection.Ascending;
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
        if (_sortMemberPath is null) return;
        foreach (var col in ProcessGrid.Columns)
        {
            if (col.SortMemberPath == _sortMemberPath)
                col.Sort(_sortDir);
            else
                col.ClearSort();
        }
    }
}
