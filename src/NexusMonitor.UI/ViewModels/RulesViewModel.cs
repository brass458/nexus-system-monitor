using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Rules;

namespace NexusMonitor.UI.ViewModels;

public partial class RulesViewModel : ViewModelBase
{
    private readonly RulesPersistence _persistence;
    private Guid _editingId = Guid.Empty; // Empty = creating a new rule

    // ── Rules list ────────────────────────────────────────────────────────────
    public ObservableCollection<ProcessRule> Rules { get; } = [];
    public ObservableCollection<ProcessRule> FilteredRules { get; } = [];

    [ObservableProperty] private ProcessRule? _selectedRule;
    [ObservableProperty] private string _searchText = string.Empty;

    // ── Editor visibility ─────────────────────────────────────────────────────
    [ObservableProperty] private bool _isEditorVisible;
    [ObservableProperty] private string _editorTitle = "New Rule";

    // ── Edit fields ───────────────────────────────────────────────────────────
    [ObservableProperty] private string _editName    = "";
    [ObservableProperty] private string _editPattern = "";
    [ObservableProperty] private bool   _editEnabled = true;

    // Priority indices: 0 = (none), 1..n = enum values in display order
    [ObservableProperty] private int _editPriorityIndex    = 0;
    [ObservableProperty] private int _editIoPriorityIndex  = 0;
    [ObservableProperty] private int _editMemPriorityIndex = 0;
    [ObservableProperty] private int _editEfficiencyIndex  = 0; // 0=none, 1=enable, 2=disable

    [ObservableProperty] private string _editAffinityHex = "";

    // Watchdog / condition
    [ObservableProperty] private int    _editConditionTypeIndex     = 0; // 0=Always 1=CpuAbove 2=RamAbove
    [ObservableProperty] private double _editConditionCpuThreshold  = 25.0;
    [ObservableProperty] private double _editConditionRamMb         = 512.0;
    [ObservableProperty] private int    _editConditionDurationSecs  = 5;
    [ObservableProperty] private int    _editWatchdogActionIndex    = 0; // None/BelowNormal/Idle/Terminate

    [ObservableProperty] private bool _editDisallowed  = false;
    [ObservableProperty] private bool _editKeepRunning = false;

    // Validation
    [ObservableProperty] private string _validationError = "";

    // ── Visibility helpers ────────────────────────────────────────────────────
    public bool IsConditionEnabled => EditConditionTypeIndex > 0;
    public bool IsWatchdogEnabled  => EditWatchdogActionIndex > 0;

    partial void OnEditConditionTypeIndexChanged(int value)
        => OnPropertyChanged(nameof(IsConditionEnabled));
    partial void OnEditWatchdogActionIndexChanged(int value)
        => OnPropertyChanged(nameof(IsWatchdogEnabled));

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var selected = SelectedRule;
        FilteredRules.Clear();

        var source = string.IsNullOrWhiteSpace(SearchText)
            ? (IEnumerable<ProcessRule>)Rules
            : Rules.Where(r =>
                r.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                r.ProcessNamePattern.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        foreach (var rule in source)
            FilteredRules.Add(rule);

        // Restore selection if it still passes the filter
        if (selected is not null && FilteredRules.Contains(selected))
            SelectedRule = selected;
    }

    // ── Static option lists ───────────────────────────────────────────────────

    public static IReadOnlyList<string> PriorityOptions { get; } =
        ["(none)", "Idle", "Below Normal", "Normal", "Above Normal", "High", "Real Time"];

    public static IReadOnlyList<string> IoPriorityOptions { get; } =
        ["(none)", "Very Low", "Low", "Normal", "High"];

    public static IReadOnlyList<string> MemPriorityOptions { get; } =
        ["(none)", "Very Low", "Low", "Medium", "Normal", "High"];

    public static IReadOnlyList<string> EfficiencyOptions { get; } =
        ["(none)", "Enable", "Disable"];

    public static IReadOnlyList<string> ConditionTypeOptions { get; } =
        ["Always (on launch)", "CPU above threshold", "RAM above threshold"];

    public static IReadOnlyList<string> WatchdogActionOptions { get; } =
        ["None", "Set Below Normal", "Set Idle", "Terminate process"];

    // ── Priority enum helpers (index ↔ nullable enum) ─────────────────────────

    private static ProcessPriority? IndexToPriority(int index) => index switch
    {
        1 => ProcessPriority.Idle,
        2 => ProcessPriority.BelowNormal,
        3 => ProcessPriority.Normal,
        4 => ProcessPriority.AboveNormal,
        5 => ProcessPriority.High,
        6 => ProcessPriority.RealTime,
        _ => null
    };

    private static int PriorityToIndex(ProcessPriority? p) => p switch
    {
        ProcessPriority.Idle        => 1,
        ProcessPriority.BelowNormal => 2,
        ProcessPriority.Normal      => 3,
        ProcessPriority.AboveNormal => 4,
        ProcessPriority.High        => 5,
        ProcessPriority.RealTime    => 6,
        _                           => 0
    };

    private static IoPriority? IndexToIoPriority(int index) => index switch
    {
        1 => IoPriority.VeryLow,
        2 => IoPriority.Low,
        3 => IoPriority.Normal,
        4 => IoPriority.High,
        _ => null
    };

    private static int IoPriorityToIndex(IoPriority? p) => p switch
    {
        IoPriority.VeryLow => 1,
        IoPriority.Low     => 2,
        IoPriority.Normal  => 3,
        IoPriority.High    => 4,
        _                  => 0
    };

    private static MemoryPriority? IndexToMemPriority(int index) => index switch
    {
        1 => MemoryPriority.VeryLow,
        2 => MemoryPriority.Low,
        3 => MemoryPriority.Medium,
        4 => MemoryPriority.Normal,
        5 => MemoryPriority.High,
        _ => null
    };

    private static int MemPriorityToIndex(MemoryPriority? p) => p switch
    {
        MemoryPriority.VeryLow => 1,
        MemoryPriority.Low     => 2,
        MemoryPriority.Medium  => 3,
        MemoryPriority.Normal  => 4,
        MemoryPriority.High    => 5,
        _                      => 0
    };

    private static bool? IndexToEfficiency(int index) => index switch
    {
        1 => true,
        2 => false,
        _ => null
    };

    private static int EfficiencyToIndex(bool? v) => v switch
    {
        true  => 1,
        false => 2,
        _     => 0
    };

    private static WatchdogAction IndexToWatchdog(int index) => index switch
    {
        1 => WatchdogAction.SetBelowNormal,
        2 => WatchdogAction.SetIdle,
        3 => WatchdogAction.Terminate,
        _ => WatchdogAction.None
    };

    private static int WatchdogToIndex(WatchdogAction a) => a switch
    {
        WatchdogAction.SetBelowNormal => 1,
        WatchdogAction.SetIdle        => 2,
        WatchdogAction.Terminate      => 3,
        _                             => 0
    };

    private static ConditionType IndexToConditionType(int index) => index switch
    {
        1 => ConditionType.CpuAbove,
        2 => ConditionType.RamAbove,
        _ => ConditionType.Always
    };

    private static int ConditionTypeToIndex(ConditionType t) => t switch
    {
        ConditionType.CpuAbove => 1,
        ConditionType.RamAbove => 2,
        _                      => 0
    };

    // ── Constructor ───────────────────────────────────────────────────────────

    public RulesViewModel(RulesPersistence persistence)
    {
        Title        = "Rules";
        _persistence = persistence;
        LoadRules();
    }

    // ── List management ───────────────────────────────────────────────────────

    private void LoadRules()
    {
        Rules.Clear();
        foreach (var r in _persistence.GetAll())
            Rules.Add(r);
        ApplyFilter();
    }

    [RelayCommand]
    private void AddRule()
    {
        _editingId = Guid.Empty;
        EditorTitle = "New Rule";
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
        _persistence.Remove(SelectedRule.Id);
        Rules.Remove(SelectedRule);
        SelectedRule = null;
        ApplyFilter();
    }

    [RelayCommand]
    private void ToggleEnabled(ProcessRule? rule)
    {
        if (rule is null) return;
        rule.IsEnabled = !rule.IsEnabled;
        _persistence.Update(rule);
        // Trigger list refresh for the toggle icon
        var idx = Rules.IndexOf(rule);
        if (idx >= 0) { Rules.RemoveAt(idx); Rules.Insert(idx, rule); }
        SelectedRule = rule;
        ApplyFilter();
    }

    // ── Editor ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void SaveEdit()
    {
        ValidationError = "";

        if (string.IsNullOrWhiteSpace(EditPattern))
        {
            ValidationError = "Process name pattern is required.";
            return;
        }
        if (string.IsNullOrWhiteSpace(EditName))
            EditName = EditPattern;

        var rule = _editingId == Guid.Empty
            ? new ProcessRule { Id = Guid.NewGuid() }
            : _persistence.GetAll().FirstOrDefault(r => r.Id == _editingId)
              ?? new ProcessRule { Id = _editingId };

        rule.Name               = EditName.Trim();
        rule.ProcessNamePattern = EditPattern.Trim();
        rule.IsEnabled          = EditEnabled;
        rule.Priority           = IndexToPriority(EditPriorityIndex);
        rule.IoPriority         = IndexToIoPriority(EditIoPriorityIndex);
        rule.MemoryPriority     = IndexToMemPriority(EditMemPriorityIndex);
        rule.EfficiencyMode     = IndexToEfficiency(EditEfficiencyIndex);
        rule.Disallowed         = EditDisallowed;
        rule.KeepRunning        = EditKeepRunning;
        rule.WatchdogAction     = IndexToWatchdog(EditWatchdogActionIndex);

        // Affinity mask
        if (!string.IsNullOrWhiteSpace(EditAffinityHex))
        {
            var hex = EditAffinityHex.TrimStart('0', 'x');
            if (long.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                              null, out long mask))
                rule.AffinityMask = mask;
        }
        else
        {
            rule.AffinityMask = null;
        }

        // Condition
        var condType = IndexToConditionType(EditConditionTypeIndex);
        if (condType != ConditionType.Always || EditWatchdogActionIndex > 0)
        {
            rule.Condition = new RuleCondition
            {
                Type                = condType,
                CpuThresholdPercent = EditConditionCpuThreshold,
                RamThresholdBytes   = (long)(EditConditionRamMb * 1024 * 1024),
                DurationSeconds     = EditConditionDurationSecs
            };
        }
        else
        {
            rule.Condition = null;
        }

        if (_editingId == Guid.Empty)
        {
            _persistence.Add(rule);
            Rules.Add(rule);
        }
        else
        {
            _persistence.Update(rule);
            var idx = Rules.IndexOf(Rules.FirstOrDefault(r => r.Id == rule.Id)!);
            if (idx >= 0) { Rules.RemoveAt(idx); Rules.Insert(idx, rule); }
        }

        ApplyFilter();
        IsEditorVisible = false;
    }

    [RelayCommand]
    private void CancelEdit() => IsEditorVisible = false;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetEditorDefaults()
    {
        EditName                   = "";
        EditPattern                = "";
        EditEnabled                = true;
        EditPriorityIndex          = 0;
        EditIoPriorityIndex        = 0;
        EditMemPriorityIndex       = 0;
        EditEfficiencyIndex        = 0;
        EditAffinityHex            = "";
        EditConditionTypeIndex     = 0;
        EditConditionCpuThreshold  = 25.0;
        EditConditionRamMb         = 512.0;
        EditConditionDurationSecs  = 5;
        EditWatchdogActionIndex    = 0;
        EditDisallowed             = false;
        EditKeepRunning            = false;
        ValidationError            = "";
    }

    private void LoadRuleIntoEditor(ProcessRule rule)
    {
        EditName                  = rule.Name;
        EditPattern               = rule.ProcessNamePattern;
        EditEnabled               = rule.IsEnabled;
        EditPriorityIndex         = PriorityToIndex(rule.Priority);
        EditIoPriorityIndex       = IoPriorityToIndex(rule.IoPriority);
        EditMemPriorityIndex      = MemPriorityToIndex(rule.MemoryPriority);
        EditEfficiencyIndex       = EfficiencyToIndex(rule.EfficiencyMode);
        EditAffinityHex           = rule.AffinityMask.HasValue
                                        ? $"0x{rule.AffinityMask.Value:X}"
                                        : "";
        EditConditionTypeIndex    = ConditionTypeToIndex(rule.Condition?.Type ?? ConditionType.Always);
        EditConditionCpuThreshold = rule.Condition?.CpuThresholdPercent ?? 25.0;
        EditConditionRamMb        = rule.Condition?.RamThresholdBytes / 1024.0 / 1024.0 ?? 512.0;
        EditConditionDurationSecs = rule.Condition?.DurationSeconds ?? 5;
        EditWatchdogActionIndex   = WatchdogToIndex(rule.WatchdogAction);
        EditDisallowed            = rule.Disallowed;
        EditKeepRunning           = rule.KeepRunning;
        ValidationError           = "";
    }
}
