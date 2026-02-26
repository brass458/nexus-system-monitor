using System.Collections.ObjectModel;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;
using ReactiveUI;

namespace NexusMonitor.UI.ViewModels;

public partial class OptimizationViewModel : ViewModelBase, IDisposable
{
    private readonly IProcessProvider _processProvider;
    private IDisposable? _subscription;
    private readonly CancellationTokenSource _cts = new();

    [ObservableProperty] private ObservableCollection<OptimizationRowViewModel> _topCpu = [];
    [ObservableProperty] private ObservableCollection<OptimizationRowViewModel> _topMemory = [];
    [ObservableProperty] private string _lastAction = string.Empty;

    public OptimizationViewModel(IProcessProvider processProvider)
    {
        Title = "Optimization";
        _processProvider = processProvider;
        _subscription = processProvider
            .GetProcessStream(TimeSpan.FromSeconds(2))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(Update);
    }

    private void Update(IReadOnlyList<ProcessInfo> processes)
    {
        var top5Cpu = processes
            .OrderByDescending(p => p.CpuPercent)
            .Take(5)
            .Select(p => new OptimizationRowViewModel(p.Pid, p.Name, p.CpuPercent, p.WorkingSetBytes))
            .ToList();

        var top5Mem = processes
            .OrderByDescending(p => p.WorkingSetBytes)
            .Take(5)
            .Select(p => new OptimizationRowViewModel(p.Pid, p.Name, p.CpuPercent, p.WorkingSetBytes))
            .ToList();

        SyncCollection(TopCpu, top5Cpu);
        SyncCollection(TopMemory, top5Mem);
    }

    private static void SyncCollection(ObservableCollection<OptimizationRowViewModel> target,
                                        List<OptimizationRowViewModel> source)
    {
        for (int i = target.Count - 1; i >= source.Count; i--) target.RemoveAt(i);
        for (int i = 0; i < source.Count; i++)
        {
            if (i < target.Count) target[i] = source[i];
            else target.Add(source[i]);
        }
    }

    [RelayCommand]
    private async Task NormalizePriorities()
    {
        LastAction = string.Empty;
        int count = 0;
        try
        {
            var processes = await _processProvider.GetProcessesAsync(_cts.Token);
            foreach (var p in processes.Where(p => p.Category != ProcessCategory.SystemKernel))
            {
                try
                {
                    await _processProvider.SetPriorityAsync(p.Pid, ProcessPriority.Normal, _cts.Token);
                    count++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { /* access denied or process exited — skip */ }
            }
            LastAction = $"Normalized priority for {count} processes.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LastAction = $"Failed: {ex.Message}"; }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _subscription?.Dispose();
    }
}

public record OptimizationRowViewModel(int Pid, string Name, double CpuPercent, long WorkingSetBytes)
{
    public string CpuDisplay => CpuPercent < 0.1 ? "< 0.1%" : $"{CpuPercent:F1}%";
    public string MemDisplay => ProcessRowViewModel.FormatBytes(WorkingSetBytes);
}
