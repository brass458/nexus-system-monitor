using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusMonitor.Core.Health;

namespace NexusMonitor.UI.ViewModels;

public partial class PredictionCardViewModel : ObservableObject
{
    public ResourcePrediction Prediction { get; }

    [ObservableProperty] private bool _isDismissed;

    private readonly HashSet<string>? _dismissedResources;

    public string Resource       => Prediction.Resource;
    public string Description    => Prediction.Description;
    public string DepletionLabel => Prediction.DepletionEstimate.HasValue
        ? $"Estimated depletion: {Prediction.DepletionEstimate.Value:MMM d, yyyy HH:mm}"
        : string.Empty;
    public string SeverityLabel  => Prediction.Severity.ToString();

    [RelayCommand]
    private void Dismiss()
    {
        IsDismissed = true;
        _dismissedResources?.Add(Prediction.Resource);
    }

    public PredictionCardViewModel(ResourcePrediction prediction, HashSet<string>? dismissedResources = null)
    {
        Prediction          = prediction;
        _dismissedResources = dismissedResources;
        // Restore dismissed state if this resource was previously dismissed
        _isDismissed = dismissedResources?.Contains(prediction.Resource) ?? false;
    }
}
