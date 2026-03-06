using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusMonitor.Core.Automation;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Rules;
using NexusMonitor.Core.Services;

namespace NexusMonitor.UI.ViewModels;

public partial class AutomationViewModel : ViewModelBase
{
    private readonly AppSettings      _settings;
    private readonly SettingsService  _settingsService;

    // ── Foreground Boost ────────────────────────────────────────────────────
    [ObservableProperty] private bool   _foregroundBoostEnabled;
    [ObservableProperty] private string _foregroundBoostExclusionsText = "";

    // ── IdleSaver ───────────────────────────────────────────────────────────
    [ObservableProperty] private bool   _idleSaverEnabled;
    [ObservableProperty] private double _idleSaverCpuThreshold;
    [ObservableProperty] private int    _idleSaverIdleTicks;
    [ObservableProperty] private bool   _idleSaverUseEfficiencyMode;
    [ObservableProperty] private string _idleSaverExclusionsText = "";

    // ── SmartTrim ───────────────────────────────────────────────────────────
    [ObservableProperty] private bool   _smartTrimEnabled;
    [ObservableProperty] private int    _smartTrimInterval;
    [ObservableProperty] private double _smartTrimPressurePercent;
    [ObservableProperty] private int    _smartTrimMinWorkingSetMb;

    // ── CPU Limiter ─────────────────────────────────────────────────────────
    [ObservableProperty] private bool _cpuLimiterEnabled;
    public ObservableCollection<CpuLimiterRule> CpuLimiterRules { get; } = [];
    [ObservableProperty] private CpuLimiterRule? _selectedCpuLimiterRule;

    // ── Instance Balancer ───────────────────────────────────────────────────
    [ObservableProperty] private bool _instanceBalancerEnabled;
    public ObservableCollection<InstanceBalancerRule> InstanceBalancerRules { get; } = [];
    [ObservableProperty] private InstanceBalancerRule? _selectedInstanceBalancerRule;

    public AutomationViewModel(AppSettings settings, SettingsService settingsService)
    {
        Title             = "Automation";
        _settings         = settings;
        _settingsService  = settingsService;

        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        ForegroundBoostEnabled       = _settings.ForegroundBoostEnabled;
        ForegroundBoostExclusionsText = string.Join(", ", _settings.ForegroundBoostExclusions);

        IdleSaverEnabled           = _settings.IdleSaverEnabled;
        IdleSaverCpuThreshold      = _settings.IdleSaverCpuThreshold;
        IdleSaverIdleTicks         = _settings.IdleSaverIdleTicksRequired;
        IdleSaverUseEfficiencyMode  = _settings.IdleSaverUseEfficiencyMode;
        IdleSaverExclusionsText    = string.Join(", ", _settings.IdleSaverExclusions);

        SmartTrimEnabled         = _settings.SmartTrimEnabled;
        SmartTrimInterval        = _settings.SmartTrimIntervalSeconds;
        SmartTrimPressurePercent = _settings.SmartTrimPressurePercent;
        SmartTrimMinWorkingSetMb = _settings.SmartTrimMinWorkingSetMB;

        CpuLimiterEnabled = _settings.CpuLimiterEnabled;
        CpuLimiterRules.Clear();
        foreach (var r in _settings.CpuLimiterRules)
            CpuLimiterRules.Add(r);

        InstanceBalancerEnabled = _settings.InstanceBalancerEnabled;
        InstanceBalancerRules.Clear();
        foreach (var r in _settings.InstanceBalancerRules)
            InstanceBalancerRules.Add(r);
    }

    [RelayCommand]
    private void Save()
    {
        _settings.ForegroundBoostEnabled    = ForegroundBoostEnabled;
        _settings.ForegroundBoostExclusions = ParseList(ForegroundBoostExclusionsText);

        _settings.IdleSaverEnabled           = IdleSaverEnabled;
        _settings.IdleSaverCpuThreshold      = IdleSaverCpuThreshold;
        _settings.IdleSaverIdleTicksRequired = IdleSaverIdleTicks;
        _settings.IdleSaverUseEfficiencyMode  = IdleSaverUseEfficiencyMode;
        _settings.IdleSaverExclusions        = ParseList(IdleSaverExclusionsText);

        _settings.SmartTrimEnabled         = SmartTrimEnabled;
        _settings.SmartTrimIntervalSeconds = SmartTrimInterval;
        _settings.SmartTrimPressurePercent = SmartTrimPressurePercent;
        _settings.SmartTrimMinWorkingSetMB = SmartTrimMinWorkingSetMb;

        _settings.CpuLimiterEnabled = CpuLimiterEnabled;
        _settings.CpuLimiterRules   = CpuLimiterRules.ToList();

        _settings.InstanceBalancerEnabled = InstanceBalancerEnabled;
        _settings.InstanceBalancerRules   = InstanceBalancerRules.ToList();

        _settingsService.Save();
    }

    // ── CPU Limiter CRUD ────────────────────────────────────────────────────

    [RelayCommand]
    private void AddCpuLimiterRule()
    {
        var rule = new CpuLimiterRule { ProcessNamePattern = "process.exe" };
        CpuLimiterRules.Add(rule);
        _settings.CpuLimiterRules = CpuLimiterRules.ToList();
        _settingsService.Save();
    }

    [RelayCommand]
    private void RemoveCpuLimiterRule(CpuLimiterRule? rule)
    {
        if (rule is null) return;
        CpuLimiterRules.Remove(rule);
        _settings.CpuLimiterRules = CpuLimiterRules.ToList();
        _settingsService.Save();
    }

    // ── Instance Balancer CRUD ──────────────────────────────────────────────

    [RelayCommand]
    private void AddInstanceBalancerRule()
    {
        var rule = new InstanceBalancerRule { ProcessNamePattern = "process.exe" };
        InstanceBalancerRules.Add(rule);
        _settings.InstanceBalancerRules = InstanceBalancerRules.ToList();
        _settingsService.Save();
    }

    [RelayCommand]
    private void RemoveInstanceBalancerRule(InstanceBalancerRule? rule)
    {
        if (rule is null) return;
        InstanceBalancerRules.Remove(rule);
        _settings.InstanceBalancerRules = InstanceBalancerRules.ToList();
        _settingsService.Save();
    }

    private static List<string> ParseList(string text) =>
        text.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

    public static IReadOnlyList<string> BalancerAlgorithmOptions { get; } =
        ["Spread Evenly", "Fixed Core Count"];
}
