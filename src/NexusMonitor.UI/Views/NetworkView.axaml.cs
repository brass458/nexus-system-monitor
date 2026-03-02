using System.ComponentModel;
using Avalonia.Controls;
using NexusMonitor.UI.ViewModels;

namespace NexusMonitor.UI.Views;

public partial class NetworkView : UserControl
{
    private NetworkViewModel? _previousVm;

    public NetworkView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // 4D: Unsubscribe from the previous VM to prevent accumulating duplicate handlers
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
}
