using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ReactiveUI;
using NexusMonitor.UI.Messages;
using NexusMonitor.UI.Helpers;
using NexusMonitor.UI.Views;

namespace NexusMonitor.UI.ViewModels;

public partial class ProcessesViewModel : ViewModelBase, IDisposable
{
    private readonly IProcessProvider _processProvider;
    private readonly AppSettings _appSettings;
    private readonly CancellationTokenSource _cts = new();
    // 4E: Per-selection CTS — cancelled each time SelectedProcess changes to abort stale detail loads
    private CancellationTokenSource _detailCts = new();
    private IDisposable? _subscription;

    // Master cache: all live processes keyed by PID.
    // Allows in-place property updates so the DataGrid never loses sort state or selection.
    private readonly Dictionary<int, ProcessRowViewModel> _allRows = new();
    // Pre-allocated working dictionary reused each tick to avoid 300-entry allocs
    private readonly Dictionary<int, ProcessInfo> _liveByPid = new(400);

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
    private bool _isDetailPanelVisible = false;

    [ObservableProperty]
    private bool _isTreeViewActive;

    /// <summary>True when the detail panel should be shown (has selection AND toggle is on).</summary>
    public bool IsDetailPanelShown => SelectedDetails is not null && IsDetailPanelVisible;

    /// <summary>
    /// Tracks the set of PIDs shown in the last tree-mode render so we avoid
    /// clearing + rebuilding the list on every tick when nothing structural changed.
    /// </summary>
    private readonly HashSet<int> _currentTreePids = new();

    [ObservableProperty]
    private IReadOnlyList<ModuleInfo> _processModules = [];

    [ObservableProperty]
    private IReadOnlyList<ThreadInfo> _processThreads = [];

    [ObservableProperty]
    private IReadOnlyList<EnvironmentEntry> _processEnvironment = [];

    [ObservableProperty]
    private IReadOnlyList<HandleInfo> _processHandles = [];

    [ObservableProperty]
    private IReadOnlyList<MemoryRegionInfo> _processMemoryMap = [];

    [ObservableProperty]
    private string _handleCountLabel = "";

    public ProcessesViewModel(IProcessProvider processProvider, AppSettings appSettings)
    {
        _processProvider = processProvider;
        _appSettings     = appSettings;
        Title = "Processes";
        StartMonitoring(_appSettings.UpdateIntervalMs);

        WeakReferenceMessenger.Default.Register<NavigateToProcessMessage>(this, (_, msg) =>
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_allRows.TryGetValue(msg.Pid, out var row))
                    SelectedProcess = row;
            }));

        // Restart process stream when the global update interval changes
        WeakReferenceMessenger.Default.Register<MetricsIntervalChangedMessage>(this, (_, msg) =>
            Dispatcher.UIThread.InvokeAsync(() => StartMonitoring((int)msg.Interval.TotalMilliseconds)));
    }

    private void StartMonitoring(int intervalMs)
    {
        _subscription?.Dispose();
        _subscription = _processProvider
            .GetProcessStream(TimeSpan.FromMilliseconds(intervalMs))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(UpdateProcessList);
    }

    // Already on UI thread via ObserveOn(RxApp.MainThreadScheduler) — no inner Post needed.
    private void UpdateProcessList(IReadOnlyList<ProcessInfo> processes)
    {
        // Reuse pre-allocated dictionary to avoid 300-entry heap alloc each tick
        _liveByPid.Clear();
        foreach (var p in processes) _liveByPid[p.Pid] = p;
        var liveByPid = _liveByPid;

        // ── 1. Update existing rows in-place; create rows for new PIDs ─────────
        foreach (var (pid, info) in liveByPid)
        {
            if (_allRows.TryGetValue(pid, out var row))
            {
                // Mutate observable properties — DataGrid keeps its sort & selection
                row.CpuPercent      = info.CpuPercent;
                row.WorkingSetBytes = info.WorkingSetBytes;
                row.SetIoRates(info.IoReadBytesPerSec, info.IoWriteBytesPerSec);
                row.ThreadCount     = info.ThreadCount;
                row.HandleCount     = info.HandleCount;
                row.PushCpuHistory(info.CpuPercent);
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
    /// the <see cref="Processes"/> collection.  Must be called on the UI thread.
    /// In flat mode: in-place add/remove preserves DataGrid sort state.
    /// In tree mode: rebuilds only when the PID set changes, keeping row order stable.
    /// </summary>
    private void ApplyFilter()
    {
        int selectedPid = SelectedProcess?.Pid ?? -1;

        var wantPids = new HashSet<int>(
            string.IsNullOrWhiteSpace(SearchText)
                ? _allRows.Keys
                : _allRows.Values
                    .Where(r =>
                        r.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)        ||
                        r.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                        r.Pid.ToString().Contains(SearchText))
                    .Select(r => r.Pid));

        if (IsTreeViewActive)
            ApplyTreeFilter(wantPids, selectedPid);
        else
            ApplyFlatFilter(wantPids, selectedPid);
    }

    /// <summary>Standard flat-list filter: add / remove rows in-place preserving DataGrid sort.</summary>
    private void ApplyFlatFilter(HashSet<int> wantPids, int selectedPid)
    {
        // Reset tree depths so indents disappear when leaving tree mode
        foreach (var r in _allRows.Values) r.TreeDepth = 0;

        // Remove rows that left the visible set
        for (int i = Processes.Count - 1; i >= 0; i--)
            if (!wantPids.Contains(Processes[i].Pid))
                Processes.RemoveAt(i);

        // Add rows newly in the visible set
        var currentPids = new HashSet<int>(Processes.Select(r => r.Pid));
        foreach (var pid in wantPids)
            if (!currentPids.Contains(pid) && _allRows.TryGetValue(pid, out var row))
                Processes.Add(row);

        if (selectedPid >= 0 && SelectedProcess is null)
            SelectedProcess = Processes.FirstOrDefault(r => r.Pid == selectedPid);
    }

    /// <summary>
    /// Tree-ordered filter: processes appear in depth-first parent→child order with
    /// <see cref="ProcessRowViewModel.TreeDepth"/> set for visual indentation.
    /// Only rebuilds the collection when the set of visible PIDs changes to avoid
    /// scroll-position disruption on every 1-second tick.
    /// </summary>
    private void ApplyTreeFilter(HashSet<int> wantPids, int selectedPid)
    {
        // Structural rebuild only when the visible PID set actually changed
        if (_currentTreePids.SetEquals(wantPids)) return;
        _currentTreePids.Clear();
        foreach (var p in wantPids) _currentTreePids.Add(p);

        // Build parent → children map from the full live set
        var allPids    = new HashSet<int>(_allRows.Keys);
        var childrenOf = new Dictionary<int, List<ProcessRowViewModel>>();
        foreach (var row in _allRows.Values)
        {
            if (!childrenOf.TryGetValue(row.ParentPid, out var list))
                childrenOf[row.ParentPid] = list = new List<ProcessRowViewModel>();
            list.Add(row);
        }

        // Roots: processes whose parent is not a live process
        var roots = _allRows.Values
            .Where(r => !allPids.Contains(r.ParentPid))
            .OrderBy(r => r.Name)
            .ToList();

        // Depth-first traversal with cycle guard and depth limit
        var ordered = new List<ProcessRowViewModel>();
        var visited = new HashSet<int>();
        void Traverse(ProcessRowViewModel row, int depth)
        {
            if (!visited.Add(row.Pid)) return;  // cycle guard
            if (depth > 64) return;              // depth limit safety net
            if (wantPids.Contains(row.Pid))
            {
                row.TreeDepth = depth;
                ordered.Add(row);
            }
            if (childrenOf.TryGetValue(row.Pid, out var kids))
                foreach (var kid in kids.OrderBy(k => k.Name))
                    Traverse(kid, depth + 1);
        }
        foreach (var root in roots) Traverse(root, 0);

        // Rebuild collection (only happens on structural change, not every tick)
        Processes.Clear();
        foreach (var row in ordered) Processes.Add(row);

        if (selectedPid >= 0)
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

    [RelayCommand]
    private async Task CreateDumpFile()
    {
        if (SelectedProcess is null) return;
        try
        {
            LastError = string.Empty;
            var mainWin = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (mainWin is null) return;

            var sp = mainWin.StorageProvider;
            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = $"Save dump file for {SelectedProcess.Name}",
                SuggestedFileName = $"{SelectedProcess.Name}_{SelectedProcess.Pid}.dmp",
                FileTypeChoices =
                [
                    new FilePickerFileType("Dump files") { Patterns = ["*.dmp"] },
                    new FilePickerFileType("All files")  { Patterns = ["*"] },
                ],
            });
            if (file is null) return;

            var path = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) return;

            await _processProvider.CreateDumpFileAsync(SelectedProcess.Pid, path, _cts.Token);
            LastError = $"Dump saved: {path}";
        }
        catch (Exception ex) { LastError = $"Dump failed: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task SetAffinity()
    {
        if (SelectedProcess is null) return;
        try
        {
            LastError = string.Empty;
            var (procMask, sysMask) = await _processProvider.GetAffinityMasksAsync(SelectedProcess.Pid, _cts.Token);

            var mainWin = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (mainWin is null) return;

            var dialog = new AffinityDialog(
                $"{SelectedProcess.Name} (PID {SelectedProcess.Pid})",
                procMask, sysMask);

            var result = await dialog.ShowDialog<long?>(mainWin);
            if (result is long newMask && newMask > 0)
            {
                await _processProvider.SetAffinityAsync(SelectedProcess.Pid, newMask, _cts.Token);
            }
        }
        catch (Exception ex) { LastError = $"Set affinity failed: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task FindWindow()
    {
        try
        {
            LastError = string.Empty;
            var overlay = new FindWindowOverlay();

            var mainWin = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

            var result = await overlay.ShowDialog<int?>(mainWin!);
            int pid = result ?? 0;
            if (pid > 0)
            {
                // Navigate to that PID in the process list
                if (_allRows.TryGetValue(pid, out var row))
                    SelectedProcess = row;
                else
                    LastError = $"PID {pid} not found in process list";
            }
        }
        catch (Exception ex) { LastError = $"Find window failed: {ex.Message}"; }
    }

    [RelayCommand]
    private void ShowProperties()
    {
        if (SelectedProcess is null) return;
        // Ensure the detail panel is visible and selected process is focused
        IsDetailPanelVisible = true;
    }

    // Filter from the in-memory cache — no async round-trip to the provider needed.
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnIsDetailPanelVisibleChanged(bool value) =>
        OnPropertyChanged(nameof(IsDetailPanelShown));

    partial void OnIsTreeViewActiveChanged(bool value)
    {
        // When leaving tree mode, reset depths and force a full rebuild on next filter pass
        if (!value) foreach (var r in _allRows.Values) r.TreeDepth = 0;
        _currentTreePids.Clear();
        ApplyFilter();
    }

    partial void OnSelectedProcessChanged(ProcessRowViewModel? value)
    {
        // Dispose the old detail view model to unsubscribe its PropertyChanged handler
        (SelectedDetails as IDisposable)?.Dispose();
        SelectedDetails = value is null ? null : new ProcessDetailViewModel(value);
        OnPropertyChanged(nameof(IsDetailPanelShown));
        ProcessModules     = [];
        ProcessThreads     = [];
        ProcessEnvironment = [];
        ProcessHandles     = [];
        ProcessMemoryMap   = [];
        HandleCountLabel   = "";

        // 4E: Cancel any in-flight detail loads from the previous selection
        _detailCts.Cancel();
        _detailCts.Dispose();
        _detailCts = new CancellationTokenSource();

        if (value is not null)
        {
            _ = LoadModulesAsync(value.Pid, _detailCts.Token);
            _ = LoadThreadsAsync(value.Pid, _detailCts.Token);
            _ = LoadEnvironmentAsync(value.Pid, _detailCts.Token);
            _ = LoadHandlesAsync(value.Pid, _detailCts.Token);
            _ = LoadMemoryMapAsync(value.Pid, _detailCts.Token);
        }
    }

    private async Task LoadModulesAsync(int pid, CancellationToken ct)
    {
        try
        {
            var modules = await _processProvider.GetModulesAsync(pid, ct);
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

    private async Task LoadThreadsAsync(int pid, CancellationToken ct)
    {
        try
        {
            var threads = await _processProvider.GetThreadsAsync(pid, ct);
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

    private async Task LoadEnvironmentAsync(int pid, CancellationToken ct)
    {
        try
        {
            var env = await _processProvider.GetEnvironmentAsync(pid, ct);
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

    private async Task LoadHandlesAsync(int pid, CancellationToken ct)
    {
        try
        {
            var handles = await _processProvider.GetHandlesAsync(pid, ct);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (SelectedProcess?.Pid == pid)
                {
                    ProcessHandles   = handles;
                    HandleCountLabel = $"{handles.Count} handles";
                }
            });
        }
        catch (OperationCanceledException) { }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (SelectedProcess?.Pid == pid)
                    ProcessHandles = [];
            });
        }
    }

    private async Task LoadMemoryMapAsync(int pid, CancellationToken ct)
    {
        try
        {
            var regions = await _processProvider.GetMemoryMapAsync(pid, ct);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (SelectedProcess?.Pid == pid)
                    ProcessMemoryMap = regions;
            });
        }
        catch (OperationCanceledException) { }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (SelectedProcess?.Pid == pid)
                    ProcessMemoryMap = [];
            });
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _detailCts.Cancel();
        _detailCts.Dispose();
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

    // No [NotifyPropertyChangedFor(nameof(IoDisplay))] here — batched via SetIoRates()
    // to avoid firing IoDisplay PropertyChanged twice per tick.
    private long _ioReadBytesPerSec;
    private long _ioWriteBytesPerSec;

    public long IoReadBytesPerSec
    {
        get => _ioReadBytesPerSec;
        set => SetProperty(ref _ioReadBytesPerSec, value);
    }

    public long IoWriteBytesPerSec
    {
        get => _ioWriteBytesPerSec;
        set => SetProperty(ref _ioWriteBytesPerSec, value);
    }

    /// <summary>
    /// Updates both I/O rate fields and fires <see cref="IoDisplay"/> only once.
    /// Avoids double PropertyChanged notification from the two backing fields.
    /// </summary>
    public void SetIoRates(long readPerSec, long writePerSec)
    {
        bool changed = SetProperty(ref _ioReadBytesPerSec,  readPerSec,  nameof(IoReadBytesPerSec))
                     | SetProperty(ref _ioWriteBytesPerSec, writePerSec, nameof(IoWriteBytesPerSec));
        if (changed) OnPropertyChanged(nameof(IoDisplay));
    }

    [ObservableProperty] private int _threadCount;
    [ObservableProperty] private int _handleCount;

    /// <summary>Depth in the process tree (0 = root). Used for indentation in tree-view mode.</summary>
    [ObservableProperty] private int _treeDepth;

    // ── CPU history for the detail-panel sparkline ───────────────────────────
    private readonly double[] _cpuHistory = new double[60];
    private int _histIdx; // monotonically increasing write cursor

    /// <summary>Appends the latest CPU sample to the ring buffer (called every tick).</summary>
    public void PushCpuHistory(double cpu)
    {
        _cpuHistory[_histIdx % 60] = cpu;
        _histIdx++;
    }

    /// <summary>Returns up to 60 CPU samples ordered oldest-first.</summary>
    public double[] GetCpuHistory()
    {
        int filled = Math.Min(_histIdx, 60);
        if (filled == 0) return [];
        var result = new double[filled];
        int start  = _histIdx > 60 ? _histIdx % 60 : 0;
        for (int i = 0; i < filled; i++)
            result[i] = _cpuHistory[(start + i) % 60];
        return result;
    }

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

    // Maps ProcessRowViewModel property names → ProcessDetailViewModel property names
    // Only includes properties that update at runtime (static ones like Name/ImagePath excluded)
    private static readonly Dictionary<string, string[]> _propMap = new()
    {
        [nameof(ProcessRowViewModel.CpuPercent)]        = [nameof(CpuPercent)],
        [nameof(ProcessRowViewModel.WorkingSetBytes)]   = [nameof(WorkingSet)],
        [nameof(ProcessRowViewModel.IoReadBytesPerSec)] = [nameof(ReadRate)],
        [nameof(ProcessRowViewModel.IoWriteBytesPerSec)]= [nameof(WriteRate)],
        // IoDisplay fires once (batched via SetIoRates) — map to both read/write display
        [nameof(ProcessRowViewModel.IoDisplay)]         = [nameof(ReadRate), nameof(WriteRate)],
        [nameof(ProcessRowViewModel.ThreadCount)]       = [nameof(Threads)],
        [nameof(ProcessRowViewModel.HandleCount)]       = [nameof(Handles)],
        [nameof(ProcessRowViewModel.PrivateBytes)]      = [nameof(PrivateBytes)],
        [nameof(ProcessRowViewModel.VirtualBytes)]      = [nameof(VirtualBytes)],
    };

    public ProcessDetailViewModel(ProcessRowViewModel row)
    {
        _row = row;
        // Forward only the properties that actually changed rather than re-evaluating all 20+
        _handler = (_, e) =>
        {
            if (e.PropertyName is null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
                return;
            }
            if (_propMap.TryGetValue(e.PropertyName, out var targets))
            {
                foreach (var t in targets)
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(t));
            }
            // Other properties (Name, ImagePath, etc.) don't change for a running process
        };
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

    /// <summary>
    /// Polyline points for the CPU history sparkline.
    /// Re-evaluated each time the source row fires PropertyChanged (which happens every tick).
    /// Coordinates are normalised to a 240 × 44 canvas (Viewbox scales to fit the container).
    /// </summary>
    public IList<Avalonia.Point> CpuSparkPoints
    {
        get
        {
            var history = _row.GetCpuHistory();
            var pts     = new List<Avalonia.Point>();
            int n = history.Length;
            if (n < 2) return pts;
            const double W = 240, H = 44;
            for (int i = 0; i < n; i++)
            {
                double x = (double)i / (n - 1) * W;
                double y = H - Math.Clamp(history[i] / 100.0, 0, 1) * H;
                pts.Add(new Avalonia.Point(x, y));
            }
            return pts;
        }
    }
}
