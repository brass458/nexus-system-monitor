using System.Collections.ObjectModel;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusMonitor.DiskAnalyzer.Analysis;
using NexusMonitor.DiskAnalyzer.Models;
using NexusMonitor.DiskAnalyzer.Scanning;

namespace NexusMonitor.UI.ViewModels;

public partial class DiskAnalyzerViewModel : ViewModelBase, IDisposable
{
    private CancellationTokenSource _cts = new();

    [ObservableProperty] private string _selectedPath = GetDefaultPath();
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _hasScanResult;
    [ObservableProperty] private string _scanStatus = "Choose a drive or folder to scan.";
    [ObservableProperty] private double _scanProgressValue;  // 0–100
    [ObservableProperty] private string _scanProgressText = string.Empty;

    // Scan result data
    [ObservableProperty] private DiskNode? _rootNode;
    [ObservableProperty] private DiskNode? _selectedNode;   // currently navigated-into folder
    [ObservableProperty] private DiskNode? _selectedFile;   // selected row in file list
    [ObservableProperty] private string _summaryText = string.Empty;

    // File list (flat view of SelectedNode's children, sorted by size)
    [ObservableProperty] private ObservableCollection<DiskNode> _fileRows = [];

    // Breadcrumb navigation
    [ObservableProperty] private ObservableCollection<DiskNode> _breadcrumb = [];

    // Duplicate finder
    [ObservableProperty] private bool _isDuplicateScanning;
    [ObservableProperty] private ObservableCollection<DuplicateGroup> _duplicates = [];
    [ObservableProperty] private string _duplicateSummary = string.Empty;

    // Available drives
    public IReadOnlyList<string> AvailableDrives { get; } = GetAvailableDrives();

    public DiskAnalyzerViewModel() { Title = "Disk Analyzer"; }

    // ── Scan ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task StartScan()
    {
        if (string.IsNullOrWhiteSpace(SelectedPath)) return;
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();

        IsScanning         = true;
        HasScanResult      = false;
        ScanProgressValue  = 0;
        ScanStatus         = $"Scanning {SelectedPath}\u2026";
        Duplicates.Clear();
        DuplicateSummary   = string.Empty;

        var progress = new Progress<ScanProgress>(p =>
        {
            // Throttle UI updates: only refresh every 100 files (or the first 5)
            // to prevent flooding the UI thread and causing button reflow/flicker.
            if (p.FilesScanned > 5 && p.FilesScanned % 100 != 0) return;
            ScanProgressText = $"{p.FilesScanned:N0} files \u2014 {DiskNode.FormatSize(p.BytesCounted)}";
            ScanStatus = $"Scanning: {Path.GetFileName(p.CurrentPath)}";
        });

        try
        {
            // MftScanner reads the NTFS Master File Table directly for near-instant results
            // on NTFS drives. Falls back to RecursiveScanner on non-NTFS / non-Windows.
            var scanner = new MftScanner();
            var result  = await scanner.ScanAsync(SelectedPath, new ScanOptions(), progress, _cts.Token);

            RootNode      = result.Root;
            SelectedNode  = result.Root;
            HasScanResult = true;
            BuildBreadcrumb(result.Root);
            UpdateFileRows(result.Root);
            SummaryText = $"{result.TotalFiles:N0} files, {result.TotalFolders:N0} folders \u2014 " +
                          $"{DiskNode.FormatSize(result.TotalSize)} in {result.Duration.TotalSeconds:F1}s";
            ScanStatus  = SummaryText;
        }
        catch (OperationCanceledException)
        {
            ScanStatus = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            ScanStatus = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning        = false;
            ScanProgressValue = 100;
        }
    }

    [RelayCommand]
    private void CancelScan() => _cts.Cancel();

    // ── Navigation ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void NavigateInto(DiskNode? node)
    {
        if (node is null || !node.IsDirectory) return;
        SelectedNode = node;
        BuildBreadcrumb(node);
        UpdateFileRows(node);
    }

    [RelayCommand]
    private void NavigateToBreadcrumb(DiskNode? node)
    {
        if (node is null) return;
        NavigateInto(node);
    }

    [RelayCommand]
    private void NavigateUp()
    {
        if (SelectedNode?.Parent is { } parent)
            NavigateInto(parent);
    }

    private void BuildBreadcrumb(DiskNode node)
    {
        var crumbs = new List<DiskNode>();
        var current = node;
        while (current is not null) { crumbs.Insert(0, current); current = current.Parent; }
        Breadcrumb.Clear();
        foreach (var c in crumbs) Breadcrumb.Add(c);
    }

    private void UpdateFileRows(DiskNode node)
    {
        FileRows.Clear();
        foreach (var child in node.Children.OrderByDescending(c => c.Size))
            FileRows.Add(child);
        SelectedFile = null;
    }

    // ── Treemap node click ────────────────────────────────────────────────────

    [RelayCommand]
    private void TreemapNodeClicked(DiskNode? node)
    {
        if (node is null) return;
        if (node.IsDirectory) NavigateInto(node);
        else SelectedFile = node;
    }

    // ── Duplicate finder ──────────────────────────────────────────────────────

    [RelayCommand]
    private async Task FindDuplicates()
    {
        if (RootNode is null) return;
        IsDuplicateScanning = true;
        DuplicateSummary    = "Scanning for duplicates\u2026";
        Duplicates.Clear();

        try
        {
            var finder = new DuplicateFinder();
            var dupes  = await finder.FindDuplicatesAsync(RootNode, null, _cts.Token);
            foreach (var g in dupes) Duplicates.Add(g);
            long wasted = dupes.Sum(g => g.WastedBytes);
            DuplicateSummary = dupes.Count == 0
                ? "No duplicates found."
                : $"Found {dupes.Count} duplicate group{(dupes.Count == 1 ? "" : "s")} \u2014 {DiskNode.FormatSize(wasted)} wasted";
        }
        catch (OperationCanceledException) { DuplicateSummary = "Cancelled."; }
        catch (Exception ex)               { DuplicateSummary = $"Error: {ex.Message}"; }
        finally                            { IsDuplicateScanning = false; }
    }

    // ── File actions ──────────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenFileLocation(DiskNode? node)
    {
        if (node is null) return;
        var path = node.IsDirectory ? node.FullPath : Path.GetDirectoryName(node.FullPath) ?? node.FullPath;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName       = path,
                UseShellExecute = true,
            });
        }
        catch { }
    }

    [RelayCommand]
    private void CopyPath(DiskNode? node)
    {
        if (node is null) return;
        try
        {
            var lifetime = Avalonia.Application.Current?.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime;
            _ = lifetime?.MainWindow?.Clipboard?.SetTextAsync(node.FullPath);
        }
        catch { }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetDefaultPath()
    {
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
            return "C:\\";
        return "/";
    }

    private static IReadOnlyList<string> GetAvailableDrives()
    {
        try { return DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => d.RootDirectory.FullName).ToList(); }
        catch { return [GetDefaultPath()]; }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
