using CommunityToolkit.Mvvm.ComponentModel;
using NexusMonitor.Core.Models;
#if WINDOWS
using NexusMonitor.Platform.Windows;
#endif

namespace NexusMonitor.UI.ViewModels;

public partial class SystemInfoViewModel : ViewModelBase
{
    [ObservableProperty] private SystemHardwareInfo? _info;
    [ObservableProperty] private bool   _isLoading = true;
    [ObservableProperty] private string _loadError  = "";

#if WINDOWS
    public SystemInfoViewModel(WindowsHardwareInfoProvider provider)
        => _ = LoadAsync(provider);

    private async Task LoadAsync(WindowsHardwareInfoProvider provider)
    {
        try   { Info = await provider.QueryAsync(); }
        catch (Exception ex) { LoadError = $"Failed to read hardware info: {ex.Message}"; }
        finally { IsLoading = false; }
    }
#else
    public SystemInfoViewModel()
    {
        LoadError = "Hardware info is only available on Windows.";
        IsLoading = false;
    }
#endif
}
