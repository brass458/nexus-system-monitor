using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;
using Avalonia.Threading;
using ReactiveUI;
using NexusMonitor.UI.Messages;
using NexusMonitor.UI.Helpers;

namespace NexusMonitor.UI.ViewModels;

public partial class ProcessesViewModel : ViewModelBase, IDisposable
{
    private readonly IProcessProvider _processProvider;
    private readonly CancellationTokenSource _cts = new();
    private IDisposable? _subscription;

    // Master cache: all live processes keyed by PID.
    // Allows in-place property updates so the DataGrid never loses sort state or selection.
    private readonly Dictionary<int, ProcessRowViewModel> _allRows = new();

    [ObservableProperty]
    private ObservableCollection<ProcessRowViewModel> _processes = [];

    [ObservableProperty]
    private ProcessRowViewModel? _selectedProcess;

    [ObservableProperty]
    private ProcessDetailViewModel? _selectedDetails;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isSimplifiedView = false;

    [ObservableProperty]
    private int _totalProcessCount;

    [ObservableProperty]
    private int _totalThreadCount;

    [ObservableProperty]
    private double _totalCpuPercent;

    [ObservableProperty]
    private string _lastError = string.Empty;

    [ObservableProperty]
    private bool _isDetailPanelVisible = true;

    /// <summary>True when the detail panel should be shown (has selection AND toggle is on).</summary>
    public bool IsDetailPanelShown => SelectedDetails is not null && IsDetailPanelVisible;

    [ObservableProperty]
    private IReadOnlyList<ModuleInfo> _processModules = [];

    [ObservableProperty]
    private IReadOnlyList<ThreadInfo> _processThreads = [];

    [ObservableProperty]
    private IReadOnlyList<EnvironmentEntry> _processEnvironment = [];

    public ProcessesViewModel(IProcessProvider processProvider)
    {
        _processProvider = processProvider;
        Title = "Processes";
        StartMonitoring();

        WeakReferenceMessenger.Default.Register<NavigateToProcessMessage>(this, (_, msg) =>
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_allRows.TryGetValue(msg.Pid, out var row))
                    SelectedProcess = row;
            }));
    }

    private void StartMonitoring()
    {
        _subscription = _processProvider
            .GetProcessStream(TimeSpan.FromSeconds(1))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(UpdateProcessList);
    }

    // Already on UI thread via ObserveOn(RxApp.MainThreadScheduler) — no inner Post needed.
    private void UpdateProcessList(IReadOnlyList<ProcessInfo> processes)
    {
        var liveByPid = processes.ToDictionary(p => p.Pid);

        // ── 1. Update existing rows in-place; create rows for new PIDs ─────────
        foreach (var (pid, info) in liveByPid)
        {
            if (_allRows.TryGetValue(pid, out var row))
            {
                // Mutate observable properties — DataGrid keeps its sort & selection
                row.CpuPercent         = info.CpuPercent;
                row.WorkingSetBytes    = info.WorkingSetBytes;
                row.IoReadBytesPerSec  = info.IoReadBytesPerSec;
                row.IoWriteBytesPerSec = info.IoWriteBytesPerSec;
                row.ThreadCount        = info.ThreadCount;
                row.HandleCount        = info.HandleCount;
            }
            else
            {
                _allRows[pid] = new ProcessRowViewModel(info);
            }
        }

        // ── 2. Evict dead processes ────────────────────────────────────────────
        var deadPids = _allRows.Keys.Where(pid => !liveByPid.ContainsKey(pid)).ToList();
        foreach (var pid in deadPids)
            _allRows.Remove(pid);

        // ── 3. Update totals ──────────────────────────────────────────────────
        TotalProcessCount = processes.Count;
        TotalThreadCount  = processes.Sum(p => p.ThreadCount);
        TotalCpuPercent   = Math.Round(processes.Sum(p => p.CpuPercent), 1);

        // ── 4. Sync the visible collection with the current filter ────────────
        ApplyFilter();
    }

    /// <summary>
    /// Filters <see cref="_allRows"/> against <see cref="SearchText"/> and syncs
    /// the <see cref="Processes"/> collection in-place, preserving sort order and
    /// the current selection.  Must be called on the UI thread.
    /// </summary>
    private void ApplyFilter()
    {
        int selectedPid = SelectedProcess?.Pid ?? -1;

        var wantPids = new HashSet<int>(
            string.IsNullOrWhiteSpace(SearchText)
                ? _allRows.Keys
                : _allRows.Values
                    .Where(r =>
                        r.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)       ||
                        r.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase)||
                        r.Pid.ToString().Contains(SearchText))
                    .Select(r => r.Pid));

        // Remove rows that left the visible set (iterate backwards to keep indices valid)
        for (int i = Processes.Count - 1; i >= 0; i--)
            if (!wantPids.Contains(Processes[i].Pid))
                Processes.RemoveAt(i);

        // Add rows newly in the visible set
        var currentPids = new HashSet<int>(Processes.Select(r => r.Pid));
        foreach (var pid in wantPids)
            if (!currentPids.Contains(pid) && _allRows.TryGetValue(pid, out var row))
                Processes.Add(row);

        // Restore selection if it was cleared by collection churn
        if (selectedPid >= 0 && SelectedProcess is null)
            SelectedProcess = Processes.FirstOrDefault(r => r.Pid == selectedPid);
    }

    [RelayCommand]
    private async Task KillProcess()
    {
        if (SelectedProcess is null) return;
        try
        {
            LastError = string.Empty;
            await _processProvider.KillProcessAsync(SelectedProcess.Pid, killTree: false, _cts.Token);
        }
        catch (Exception ex) { LastError = $"Kill failed: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task KillProcessTree()
    {
        if (SelectedProcess is null) return;
        try
        {
            LastError = string.Empty;
            await _processProvider.KillProcessAsync(SelectedProcess.Pid, killTree: true, _cts.Token);
        }
        catch (Exception ex) { LastError = $"Kill tree failed: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task SuspendProcess()
    {
        if (SelectedProcess is null) return;
        try
        {
            LastError = string.Empty;
            await _processProvider.SuspendProcessAsync(SelectedProcess.Pid, _cts.Token);
        }
        catch (Exception ex) { LastError = $"Suspend failed: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task ResumeProcess()
    {
        if (SelectedProcess is null) return;
        try
        {
            LastError = string.Empty;
            await _processProvider.ResumeProcessAsync(SelectedProcess.Pid, _cts.Token);
        }
        catch (Exception ex) { LastError = $"Resume failed: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task SetPriority(string priorityName)
    {
        if (SelectedProcess is null) return;
        if (!Enum.TryParse<ProcessPriority>(priorityName, out var priority)) return;
        try
        {
            LastError = string.Empty;
            await _processProvider.SetPriorityAsync(SelectedProcess.Pid, priority, _cts.Token);
        }
        catch (Exception ex) { LastError = $"Set priority failed: {ex.Message}"; }
    }

    [RelayCommand]
    private void OpenFileLocation()
    {
        ShellHelper.OpenFileLocation(SelectedProcess?.ImagePath ?? string.Empty);
    }

    [RelayCommand]
    private void SearchOnline()
    {
        var name = SelectedProcess?.Name ?? string.Empty;
        if (name.Length > 0)
            ShellHelper.OpenUrl($"https://www.google.com/search?q={Uri.EscapeDataString(name + " process")}");
    }

    // Filter from the in-memory cache — no async round-trip to the provider needed.
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnIsDetailPanelVisibleChanged(bool value) =>
        OnPropertyChanged(nameof(IsDetailPanelShown));

    partial void OnSelectedProcessChanged(ProcessRowViewModel? value)
    {
        // Dispose the old detail view model to unsubscribe its PropertyChanged handler
        (SelectedDetails as IDisposable)?.Dispose();
        SelectedDetails = value is null ? null : new ProcessDetailViewModel(value);
        OnPropertyChanged(nameof(IsDetailPanelShown));
        ProcessModules = [];
        ProcessThreads = [];
        ProcessEnvironment = [];
        if (value is not null)
        {
            _ = LoadModulesAsync(value.Pid);
            _ = LoadThreadsAsync(value.Pid);
            _ = LoadEnvironmentAsync(value.Pid);
        }
    }

    private async Task LoadModulesAsync(int pid)
    {
        try
        {
            var modules = await _processProvider.GetModulesAsync(pid, _cts.Token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (SelectedProcess?.Pid == pid)
                    ProcessModules = modules;
            });
        }
        catch (OperationCanceledException) { }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (SelectedProcess?.Pid == pid)
                    ProcessModules = [];
            });
        }
    }

    private async Task LoadThreadsAsync(int pid)
    {
        try
        {
            var threads = await _processProvider.GetThreadsAsync(pid, _cts.Token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (SelectedProcess?.Pid == pid)
                    ProcessThreads = threads;
            });
        }
        catch (OperationCanceledException) { }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (SelectedProcess?.Pid == pid)
                    ProcessThreads = [];
            });
        }
    }

    private async Task LoadEnvironmentAsync(int pid)
    {
        try
        {
            var env = await _processProvider.GetEnvironmentAsync(pid, _cts.Token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (SelectedProcess?.Pid == pid)
                    ProcessEnvironment = env;
            });
        }
        catch (OperationCanceledException) { }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (SelectedProcess?.Pid == pid)
                    ProcessEnvironment = [];
            });
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _subscription?.Dispose();
        (SelectedDetails as IDisposable)?.Dispose();
        _allRows.Clear();
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
}

public partial class ProcessRowViewModel : ObservableObject
{
    public int Pid { get; }
    public int ParentPid { get; }
    public string Name { get; }
    public string Description { get; }
    public string UserName { get; }
    public string ImagePath { get; }
    public string CommandLine { get; }
    public DateTime StartTime { get; }
    public bool IsElevated { get; }
    public ProcessCategory Category { get; }
    public ProcessState State { get; }
    public bool IsAccessDenied { get; }
    public long PrivateBytes { get; }
    public long VirtualBytes { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CpuDisplay))]
    private double _cpuPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemDisplay))]
    private long _workingSetBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IoDisplay))]
    private long _ioReadBytesPerSec;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IoDisplay))]
    private long _ioWriteBytesPerSec;

    [ObservableProperty] private int _threadCount;
    [ObservableProperty] private int _handleCount;

    public string CpuDisplay => CpuPercent < 0.1 ? "" : $"{CpuPercent:F1}%";
    public string MemDisplay => FormatBytes(WorkingSetBytes);
    public string IoDisplay  => IoReadBytesPerSec + IoWriteBytesPerSec > 0
        ? $"R:{FormatBytes(IoReadBytesPerSec)}/s W:{FormatBytes(IoWriteBytesPerSec)}/s" : "";

    public ProcessRowViewModel(ProcessInfo p)
    {
        Pid                = p.Pid;
        ParentPid          = p.ParentPid;
        Name               = p.Name;
        Description        = p.Description;
        UserName           = p.UserName;
        ImagePath          = p.ImagePath;
        CommandLine        = p.CommandLine;
        StartTime          = p.StartTime;
        IsElevated         = p.IsElevated;
        Category           = p.Category;
        State              = p.State;
        IsAccessDenied     = p.AccessDenied;
        PrivateBytes       = p.PrivateBytesBytes;
        VirtualBytes       = p.VirtualBytesBytes;
        _cpuPercent        = p.CpuPercent;
        _workingSetBytes   = p.WorkingSetBytes;
        _ioReadBytesPerSec = p.IoReadBytesPerSec;
        _ioWriteBytesPerSec= p.IoWriteBytesPerSec;
        _threadCount       = p.ThreadCount;
        _handleCount       = p.HandleCount;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F1} MB";
        if (bytes >= 1_024)         return $"{bytes / 1_024.0:F0} KB";
        return $"{bytes} B";
    }
}

/// <summary>
/// Wraps a <see cref="ProcessRowViewModel"/> and forwards its PropertyChanged
/// events so the details side panel stays live without requiring a re-selection.
/// Uses null/"" property name to tell Avalonia "re-read every binding".
/// Implements IDisposable to cleanly unsubscribe and prevent memory leaks.
/// </summary>
public class ProcessDetailViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ProcessRowViewModel _row;
    private readonly PropertyChangedEventHandler _handler;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ProcessDetailViewModel(ProcessRowViewModel row)
    {
        _row = row;
        // Store the handler so we can unsubscribe it in Dispose()
        _handler = (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        _row.PropertyChanged += _handler;
    }

    /// <summary>Unsubscribes from the source row's PropertyChanged event.</summary>
    public void Dispose() => _row.PropertyChanged -= _handler;

    public string Name        => _row.Name;
    public int    Pid         => _row.Pid;
    public int    ParentPid   => _row.ParentPid;
    public string User        => _row.UserName.Length > 0 ? _row.UserName : "—";
    public string State       => _row.State.ToString();
    public string Category    => _row.Category.ToString();
    public bool   IsElevated  => _row.IsElevated;

    public string CpuPercent  => $"{_row.CpuPercent:F1}%";
    public int    Threads     => _row.ThreadCount;
    public int    Handles     => _row.HandleCount;

    public string WorkingSet  => ProcessRowViewModel.FormatBytes(_row.WorkingSetBytes);
    public string PrivateBytes=> ProcessRowViewModel.FormatBytes(_row.PrivateBytes);
    public string VirtualBytes=> ProcessRowViewModel.FormatBytes(_row.VirtualBytes);

    public string ReadRate    => ProcessRowViewModel.FormatBytes(_row.IoReadBytesPerSec)  + "/s";
    public string WriteRate   => ProcessRowViewModel.FormatBytes(_row.IoWriteBytesPerSec) + "/s";

    public string StartTime   => _row.StartTime == default ? "—" : _row.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string ImagePath   => _row.ImagePath.Length   > 0 ? _row.ImagePath   : "—";
    public string CommandLine => _row.CommandLine.Length > 0 ? _row.CommandLine : "—";
    public string Description => _row.Description.Length > 0 ? _row.Description : "—";
}
