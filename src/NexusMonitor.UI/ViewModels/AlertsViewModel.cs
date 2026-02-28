using System.Collections.ObjectModel;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusMonitor.Core.Alerts;
using NexusMonitor.Core.Services;
using ReactiveUI;

namespace NexusMonitor.UI.ViewModels;

public partial class AlertsViewModel : ViewModelBase, IDisposable
{
    private readonly AlertsService  _alertsService;
    private readonly SettingsService _settings;
    private IDisposable? _sub;
    private const int MaxLogEntries = 500;

    // Guid.Empty = creating new rule
    private Guid _editingId = Guid.Empty;

    // ── Rules list ────────────────────────────────────────────────────────────
    public ObservableCollection<AlertRule> Rules { get; } = [];

    [ObservableProperty] private AlertRule? _selectedRule;

    // ── Editor visibility ─────────────────────────────────────────────────────
    [ObservableProperty] private bool   _isEditorVisible;
    [ObservableProperty] private string _editorTitle = "New Alert";

    // ── Edit fields ───────────────────────────────────────────────────────────
    [ObservableProperty] private string _editName        = "New Alert";
    [ObservableProperty] private bool   _editEnabled     = true;
    [ObservableProperty] private int    _editMetricIndex = 0;
    [ObservableProperty] private double _editThreshold   = 90.0;
    [ObservableProperty] private int    _editSeverityIndex = 1; // Warning by default
    [ObservableProperty] private int    _editSustainSec  = 5;
    [ObservableProperty] private int    _editCooldownSec = 60;

    // ── Event log ─────────────────────────────────────────────────────────────
    public ObservableCollection<AlertEvent> EventLog { get; } = [];

    // ── Session stats ─────────────────────────────────────────────────────────
    [ObservableProperty] private int _alertCount    = 0;
    [ObservableProperty] private int _criticalCount = 0;
    [ObservableProperty] private int _warningCount  = 0;

    // ── Static option lists ───────────────────────────────────────────────────
    public static IReadOnlyList<string> MetricOptions { get; } =
        ["CPU %", "RAM %", "Disk Activity %", "GPU %", "CPU Temperature"];

    public static IReadOnlyList<string> SeverityOptions { get; } =
        ["Info", "Warning", "Critical"];

    // ── Constructor ───────────────────────────────────────────────────────────

    public AlertsViewModel(AlertsService alertsService, SettingsService settings)
    {
        Title          = "Alerts";
        _alertsService = alertsService;
        _settings      = settings;

        // Load persisted rules
        foreach (var rule in settings.Current.AlertRules)
            Rules.Add(rule);

        // Subscribe to alert events on the UI thread
        _sub = alertsService.Events
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(OnAlertFired);

        // Start the service
        alertsService.Start();
    }

    // ── Event handler ─────────────────────────────────────────────────────────

    private void OnAlertFired(AlertEvent e)
    {
        EventLog.Insert(0, e);
        while (EventLog.Count > MaxLogEntries)
            EventLog.RemoveAt(EventLog.Count - 1);

        AlertCount++;
        switch (e.Rule.Severity)
        {
            case AlertSeverity.Critical: CriticalCount++; break;
            case AlertSeverity.Warning:  WarningCount++;  break;
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void AddRule()
    {
        _editingId  = Guid.Empty;
        EditorTitle = "New Alert Rule";
        SetEditorDefaults();
        IsEditorVisible = true;
    }

    [RelayCommand]
    private void EditRule()
    {
        if (SelectedRule is null) return;
        _editingId  = SelectedRule.Id;
        EditorTitle = $"Edit Rule — {SelectedRule.Name}";
        LoadRuleIntoEditor(SelectedRule);
        IsEditorVisible = true;
    }

    [RelayCommand]
    private void DeleteRule()
    {
        if (SelectedRule is null) return;
        var rule = SelectedRule;
        Rules.Remove(rule);
        _settings.Current.AlertRules.Remove(
            _settings.Current.AlertRules.FirstOrDefault(r => r.Id == rule.Id)!);
        _settings.Save();
        SelectedRule = null;
    }

    [RelayCommand]
    private void SaveEdit()
    {
        var rule = _editingId == Guid.Empty
            ? new AlertRule { Id = Guid.NewGuid() }
            : _settings.Current.AlertRules.FirstOrDefault(r => r.Id == _editingId)
              ?? new AlertRule { Id = _editingId };

        rule.Name        = string.IsNullOrWhiteSpace(EditName) ? "New Alert" : EditName.Trim();
        rule.IsEnabled   = EditEnabled;
        rule.Metric      = IndexToMetric(EditMetricIndex);
        rule.Threshold   = EditThreshold;
        rule.Severity    = IndexToSeverity(EditSeverityIndex);
        rule.SustainSec  = Math.Max(0, EditSustainSec);
        rule.CooldownSec = Math.Max(0, EditCooldownSec);

        if (_editingId == Guid.Empty)
        {
            // New rule
            _settings.Current.AlertRules.Add(rule);
            Rules.Add(rule);
        }
        else
        {
            // Update existing
            var existingSettings = _settings.Current.AlertRules
                .FirstOrDefault(r => r.Id == rule.Id);
            if (existingSettings != null)
            {
                var si = _settings.Current.AlertRules.IndexOf(existingSettings);
                _settings.Current.AlertRules[si] = rule;
            }
            var ui = Rules.FirstOrDefault(r => r.Id == rule.Id);
            if (ui != null)
            {
                var idx = Rules.IndexOf(ui);
                Rules.RemoveAt(idx);
                Rules.Insert(idx, rule);
            }
        }

        _settings.Save();
        IsEditorVisible = false;
    }

    [RelayCommand]
    private void CancelEdit() => IsEditorVisible = false;

    [RelayCommand]
    private void ClearLog()
    {
        EventLog.Clear();
        AlertCount    = 0;
        CriticalCount = 0;
        WarningCount  = 0;
    }

    [RelayCommand]
    private void ToggleEnabled(AlertRule? rule)
    {
        if (rule is null) return;
        rule.IsEnabled = !rule.IsEnabled;
        // Sync to settings
        var sr = _settings.Current.AlertRules.FirstOrDefault(r => r.Id == rule.Id);
        if (sr != null) sr.IsEnabled = rule.IsEnabled;
        _settings.Save();
        // Refresh list to trigger UI update
        var idx = Rules.IndexOf(rule);
        if (idx >= 0) { Rules.RemoveAt(idx); Rules.Insert(idx, rule); }
    }

    // ── Enum ↔ index helpers ──────────────────────────────────────────────────

    private static AlertMetric IndexToMetric(int i) => i switch
    {
        1 => AlertMetric.RamPercent,
        2 => AlertMetric.DiskPercent,
        3 => AlertMetric.GpuPercent,
        4 => AlertMetric.CpuTemperature,
        _ => AlertMetric.CpuPercent
    };

    private static int MetricToIndex(AlertMetric m) => m switch
    {
        AlertMetric.RamPercent     => 1,
        AlertMetric.DiskPercent    => 2,
        AlertMetric.GpuPercent     => 3,
        AlertMetric.CpuTemperature => 4,
        _                          => 0
    };

    private static AlertSeverity IndexToSeverity(int i) => i switch
    {
        2 => AlertSeverity.Critical,
        1 => AlertSeverity.Warning,
        _ => AlertSeverity.Info
    };

    private static int SeverityToIndex(AlertSeverity s) => s switch
    {
        AlertSeverity.Critical => 2,
        AlertSeverity.Warning  => 1,
        _                      => 0
    };

    // ── Editor helpers ────────────────────────────────────────────────────────

    private void SetEditorDefaults()
    {
        EditName          = "New Alert";
        EditEnabled       = true;
        EditMetricIndex   = 0;
        EditThreshold     = 90.0;
        EditSeverityIndex = 1;
        EditSustainSec    = 5;
        EditCooldownSec   = 60;
    }

    private void LoadRuleIntoEditor(AlertRule rule)
    {
        EditName          = rule.Name;
        EditEnabled       = rule.IsEnabled;
        EditMetricIndex   = MetricToIndex(rule.Metric);
        EditThreshold     = rule.Threshold;
        EditSeverityIndex = SeverityToIndex(rule.Severity);
        EditSustainSec    = rule.SustainSec;
        EditCooldownSec   = rule.CooldownSec;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _sub?.Dispose();
        _alertsService.Stop();
    }
}
