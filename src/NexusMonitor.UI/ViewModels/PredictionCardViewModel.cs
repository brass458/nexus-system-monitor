using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusMonitor.Core.Health;

namespace NexusMonitor.UI.ViewModels;

public partial class PredictionCardViewModel : ObservableObject
{
    public ResourcePrediction Prediction { get; }

    [ObservableProperty] private bool _isDismissed;

    public string Resource       => Prediction.Resource;
    public string Description    => Prediction.Description;
    public string DepletionLabel => Prediction.DepletionEstimate.HasValue
        ? $"Estimated depletion: {Prediction.DepletionEstimate.Value:MMM d, yyyy HH:mm}"
        : string.Empty;
    public string SeverityLabel  => Prediction.Severity.ToString();
    public bool   IsCritical     => Prediction.Severity == RecommendationSeverity.Critical;
    public bool   IsWarning      => Prediction.Severity == RecommendationSeverity.Warning;

    [RelayCommand]
    private void Dismiss() => IsDismissed = true;

    public PredictionCardViewModel(ResourcePrediction prediction)
    {
        Prediction = prediction;
    }
}
