using System.ComponentModel;
using Avalonia.Controls;
using NexusMonitor.UI.ViewModels;

namespace NexusMonitor.UI.Views;

public partial class NetworkView : UserControl
{
    public NetworkView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is NetworkViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            ApplyThroughputVisibility(vm.ShowThroughputColumns);
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
