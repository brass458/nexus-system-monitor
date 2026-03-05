using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using NexusMonitor.Core.Health;
using NexusMonitor.Core.Models;
using ReactiveUI;

namespace NexusMonitor.UI.ViewModels;

public partial class DashboardViewModel : ViewModelBase, IDisposable
{
    private readonly SystemHealthService _healthService;
    private readonly AppSettings         _settings;
    private IDisposable?                 _subscription;

    // ── Overall health ────────────────────────────────────────────────────────

    [ObservableProperty] private double _overallScore = 100;
    [ObservableProperty] private string _overallLabel = "Excellent";
    [ObservableProperty] private string _overallDescription = "Your system is running smoothly.";
    [ObservableProperty] private IBrush _healthRingBrush  = Brushes.Green;
    [ObservableProperty] private string _trendArrow = "→";

    // ── Subsystem cards ───────────────────────────────────────────────────────

    [ObservableProperty] private SubsystemCardViewModel _cpuCard    = new("CPU",    "\ue9d5");
    [ObservableProperty] private SubsystemCardViewModel _memoryCard = new("Memory", "\ue9d6");
    [ObservableProperty] private SubsystemCardViewModel _diskCard   = new("Disk",   "\ue9d7");
    [ObservableProperty] private SubsystemCardViewModel _gpuCard    = new("GPU",    "\ue9d9");

    // ── Top consumers ─────────────────────────────────────────────────────────

    public ObservableCollection<ProcessImpactViewModel> TopConsumers { get; } = new();

    // ── Active automations ────────────────────────────────────────────────────

    [ObservableProperty] private int    _activeAutomations;
    [ObservableProperty] private string _automationStatus = "No automations active";

    // ── Recommendations ───────────────────────────────────────────────────────

    public ObservableCollection<RecommendationViewModel> Recommendations { get; } = new();

    public DashboardViewModel(SystemHealthService healthService, AppSettings settings)
    {
        _healthService = healthService;
        _settings      = settings;

        _subscription = _healthService.HealthStream
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(ApplySnapshot);
    }

    private void ApplySnapshot(SystemHealthSnapshot snapshot)
    {
        OverallScore       = snapshot.OverallScore;
        OverallLabel       = snapshot.OverallHealth.ToString();
        OverallDescription = DescribeHealth(snapshot.OverallHealth, snapshot.OverallScore);
        HealthRingBrush    = HealthLevelToBrush(snapshot.OverallHealth);
        TrendArrow         = snapshot.OverallTrend switch
        {
            TrendDirection.Improving => "↑",
            TrendDirection.Degrading => "↓",
            _                        => "→",
        };

        UpdateCard(CpuCard,    snapshot.Cpu);
        UpdateCard(MemoryCard, snapshot.Memory);
        UpdateCard(DiskCard,   snapshot.Disk);
        UpdateCard(GpuCard,    snapshot.Gpu);

        // Top consumers
        TopConsumers.Clear();
        foreach (var c in snapshot.TopConsumers)
            TopConsumers.Add(new ProcessImpactViewModel(c));

        // Automation status
        ActiveAutomations = snapshot.ActiveAutomations;
        AutomationStatus = snapshot.ActiveAutomations == 0
            ? "No automations active"
            : $"{snapshot.ActiveAutomations} automation{(snapshot.ActiveAutomations > 1 ? "s" : "")} active";

        // Recommendations
        var recs = RecommendationEngine.Evaluate(snapshot, _settings);
        Recommendations.Clear();
        foreach (var r in recs)
            Recommendations.Add(new RecommendationViewModel(r));
    }

    private static void UpdateCard(SubsystemCardViewModel card, SubsystemHealth health)
    {
        card.Score        = health.Score;
        card.Level        = health.Level.ToString();
        card.Summary      = health.Summary;
        card.Value        = health.CurrentValue;
        card.TrendArrow   = health.Trend switch
        {
            TrendDirection.Improving => "↑",
            TrendDirection.Degrading => "↓",
            _                        => "→",
        };
        card.LevelBrush   = HealthLevelToBrush(health.Level);
    }

    private static string DescribeHealth(HealthLevel level, double score) => level switch
    {
        HealthLevel.Excellent => "Your system is running smoothly.",
        HealthLevel.Good      => "Your system is in good shape.",
        HealthLevel.Fair      => "Your system is under moderate load.",
        HealthLevel.Poor      => "Your system is under heavy load. Consider closing unused apps.",
        HealthLevel.Critical  => "Your system is critically stressed. Immediate action recommended.",
        _                     => string.Empty,
    };

    private static IBrush HealthLevelToBrush(HealthLevel level) => level switch
    {
        HealthLevel.Excellent => new SolidColorBrush(Color.Parse("#30D158")), // Apple Green
        HealthLevel.Good      => new SolidColorBrush(Color.Parse("#34C759")),
        HealthLevel.Fair      => new SolidColorBrush(Color.Parse("#FF9F0A")), // Apple Orange
        HealthLevel.Poor      => new SolidColorBrush(Color.Parse("#FF6B35")),
        HealthLevel.Critical  => new SolidColorBrush(Color.Parse("#FF3B30")), // Apple Red
        _                     => Brushes.Gray,
    };

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
    }
}

// ── Sub-ViewModels ─────────────────────────────────────────────────────────────

public partial class SubsystemCardViewModel : ObservableObject
{
    public string Name { get; }
    public string Icon { get; }

    [ObservableProperty] private double _score = 100;
    [ObservableProperty] private string _level = "Excellent";
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private double _value;
    [ObservableProperty] private string _trendArrow = "→";
    [ObservableProperty] private IBrush _levelBrush = Brushes.Green;

    public SubsystemCardViewModel(string name, string icon)
    {
        Name = name;
        Icon = icon;
    }
}

public sealed class ProcessImpactViewModel
{
    public string Name         { get; }
    public double ImpactScore  { get; }
    public double CpuPercent   { get; }
    public string MemoryLabel  { get; }
    public double BarWidth     => Math.Min(ImpactScore, 100);

    public ProcessImpactViewModel(ProcessImpact impact)
    {
        Name        = impact.Name;
        ImpactScore = impact.ImpactScore;
        CpuPercent  = impact.CpuPercent;
        MemoryLabel = FormatBytes(impact.MemoryBytes);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
        if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F0} MB";
        return $"{bytes / 1024.0:F0} KB";
    }
}

public sealed class RecommendationViewModel
{
    public string Title    { get; }
    public string Body     { get; }
    public IBrush IconBrush { get; }
    public string Icon     { get; }

    public RecommendationViewModel(Recommendation rec)
    {
        Title    = rec.Title;
        Body     = rec.Body;
        (Icon, IconBrush) = rec.Severity switch
        {
            RecommendationSeverity.Critical => ("\ue9b2", new SolidColorBrush(Color.Parse("#FF3B30"))),
            RecommendationSeverity.Warning  => ("\ue9b1", new SolidColorBrush(Color.Parse("#FF9F0A"))),
            _                               => ("\ue9b0", new SolidColorBrush(Color.Parse("#30D158"))),
        };
    }
}
