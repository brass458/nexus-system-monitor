using System.Runtime.InteropServices;
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
        _ = LoadCrossPlatformAsync();
    }

    private async Task LoadCrossPlatformAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                var uptime  = TimeSpan.FromMilliseconds(Environment.TickCount64);
                var ramBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;

                var cpu = new CpuHardwareInfo(
                    Name:          RuntimeInformation.OSArchitecture.ToString(),
                    Architecture:  RuntimeInformation.OSArchitecture.ToString(),
                    PhysicalCores: Environment.ProcessorCount,
                    LogicalCores:  Environment.ProcessorCount,
                    L2CacheKB:     0,
                    L3CacheKB:     0,
                    MaxClockMhz:   0,
                    Socket:        string.Empty,
                    Stepping:      string.Empty);

                Info = new SystemHardwareInfo(
                    Hostname:               Environment.MachineName,
                    OsName:                 RuntimeInformation.OSDescription,
                    OsBuild:                Environment.OSVersion.ToString(),
                    OsArchitecture:         RuntimeInformation.OSArchitecture.ToString(),
                    Uptime:                 uptime,
                    BiosVendor:             string.Empty,
                    BiosVersion:            string.Empty,
                    MotherboardManufacturer: string.Empty,
                    MotherboardModel:       string.Empty,
                    Cpu:                    cpu,
                    TotalRamBytes:          ramBytes,
                    RamSlots:               [],
                    Gpus:                   [],
                    Storage:                []);
            }
            catch (Exception ex)
            {
                LoadError = $"Failed to read system info: {ex.Message}";
            }
        });
        IsLoading = false;
    }
#endif
}
