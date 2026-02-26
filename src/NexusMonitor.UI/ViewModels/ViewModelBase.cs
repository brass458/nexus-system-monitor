using CommunityToolkit.Mvvm.ComponentModel;

namespace NexusMonitor.UI.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;
}
