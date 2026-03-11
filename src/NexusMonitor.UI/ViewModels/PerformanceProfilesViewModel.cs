using System.Collections.ObjectModel;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.Core.Automation;
using NexusMonitor.Core.Gaming;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Services;
using ReactiveUI;

namespace NexusMonitor.UI.ViewModels;

public partial class PerformanceProfilesViewModel : ViewModelBase, IDisposable
{
    private readonly PerformanceProfileService _profileService;
    private readonly SettingsService           _settings;
    private readonly IPowerPlanProvider        _powerPlanProvider;
    private IDisposable? _statusSub;

    /// <summary>Exposes platform capability flags for binding in the View.</summary>
    public IPlatformCapabilities Platform { get; }

    public ObservableCollection<PerformanceProfile> Profiles { get; } = new();

    [ObservableProperty] private PerformanceProfile? _selectedProfile;
    [ObservableProperty] private bool                _isEditing;
    [ObservableProperty] private string              _statusText = "";

    // ── Edit form fields (bound to the selected profile while IsEditing) ──────
    [ObservableProperty] private string _editName        = "";
    [ObservableProperty] private string _editDescription = "";
    [ObservableProperty] private bool   _editChangePowerPlan;
    [ObservableProperty] private string _editPowerPlanName = "";

    public ObservableCollection<ProfileProcessRule> EditRules { get; } = new();

    // ── Active profile display ────────────────────────────────────────────────
    [ObservableProperty] private string _activeProfileName = "";

    public ObservableCollection<string> AvailablePowerPlans { get; } = new();

    public static IReadOnlyList<string> PriorityOptions { get; } =
        ["(unchanged)", "Idle", "BelowNormal", "Normal", "AboveNormal", "High", "RealTime"];

    public PerformanceProfilesViewModel(
        PerformanceProfileService profileService,
        SettingsService           settings,
        IPowerPlanProvider        powerPlanProvider,
        IPlatformCapabilities?    platformCapabilities = null)
    {
        Title             = "Performance Profiles";
        _profileService   = profileService;
        _settings         = settings;
        _powerPlanProvider= powerPlanProvider;
        Platform          = platformCapabilities ?? new MockPlatformCapabilities();

        // Load saved profiles
        foreach (var p in _settings.Current.PerformanceProfiles)
            Profiles.Add(p);

        // Track active profile name
        ActiveProfileName = profileService.ActiveProfileName;
        _statusSub = profileService.ProfileChanged
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(name => ActiveProfileName = name ?? "");

        // Load available power plans
        LoadPowerPlans();
    }

    private void LoadPowerPlans()
    {
        try
        {
            var plans = _powerPlanProvider.GetPowerPlans();
            foreach (var p in plans)
                AvailablePowerPlans.Add(p.Name);
        }
        catch { /* non-fatal */ }
    }

    // ── Profile list commands ─────────────────────────────────────────────────

    [RelayCommand]
    private void CreateProfile()
    {
        var profile = new PerformanceProfile { Name = "New Profile" };
        Profiles.Add(profile);
        _settings.Current.PerformanceProfiles.Add(profile);
        _settings.Save();
        SelectedProfile = profile;
        BeginEdit();
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile is null) return;
        if (_profileService.ActiveProfileId == SelectedProfile.Id)
            _profileService.DeactivateProfile();

        _settings.Current.PerformanceProfiles.Remove(SelectedProfile);
        _settings.Save();
        Profiles.Remove(SelectedProfile);
        SelectedProfile = null;
        IsEditing = false;
    }

    [RelayCommand]
    private void ActivateProfile()
    {
        if (SelectedProfile is null) return;
        _profileService.ActivateProfile(SelectedProfile.Id);
        ActiveProfileName = _profileService.ActiveProfileName;
        _settings.Current.ActiveProfileId = SelectedProfile.Id;
        _settings.Save();
        OnPropertyChanged(nameof(IsSelectedProfileActive));
    }

    [RelayCommand]
    private void DeactivateProfile()
    {
        _profileService.DeactivateProfile();
        _settings.Current.ActiveProfileId = null;
        _settings.Save();
        OnPropertyChanged(nameof(IsSelectedProfileActive));
    }

    public bool IsSelectedProfileActive =>
        SelectedProfile is not null &&
        _profileService.ActiveProfileId == SelectedProfile.Id;

    partial void OnSelectedProfileChanged(PerformanceProfile? value)
    {
        IsEditing = false;
        OnPropertyChanged(nameof(IsSelectedProfileActive));
    }

    // ── Edit commands ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void EditProfile()
    {
        if (SelectedProfile is null) return;
        BeginEdit();
    }

    private void BeginEdit()
    {
        if (SelectedProfile is null) return;
        EditName         = SelectedProfile.Name;
        EditDescription  = SelectedProfile.Description;
        EditChangePowerPlan = SelectedProfile.ChangePowerPlan;
        EditPowerPlanName   = SelectedProfile.PowerPlanName;
        EditRules.Clear();
        foreach (var r in SelectedProfile.ProcessRules)
            EditRules.Add(r);
        IsEditing = true;
    }

    [RelayCommand]
    private void SaveProfile()
    {
        if (SelectedProfile is null) return;
        SelectedProfile.Name          = EditName;
        SelectedProfile.Description   = EditDescription;
        SelectedProfile.ChangePowerPlan = EditChangePowerPlan;
        SelectedProfile.PowerPlanName = EditPowerPlanName;
        SelectedProfile.ProcessRules  = EditRules.ToList();

        // Refresh in Profiles list (name may have changed)
        var idx = Profiles.IndexOf(SelectedProfile);
        if (idx >= 0)
        {
            Profiles.RemoveAt(idx);
            Profiles.Insert(idx, SelectedProfile);
            SelectedProfile = Profiles[idx];
        }

        _settings.Save();
        IsEditing = false;
        StatusText = "Profile saved.";
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
    }

    [RelayCommand]
    private void AddProcessRule()
    {
        EditRules.Add(new ProfileProcessRule());
    }

    [RelayCommand]
    private void RemoveProcessRule(ProfileProcessRule rule)
    {
        EditRules.Remove(rule);
    }

    public void Dispose()
    {
        _statusSub?.Dispose();
    }
}
