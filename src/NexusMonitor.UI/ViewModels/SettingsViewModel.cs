using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using NexusMonitor.Core.Services;

namespace NexusMonitor.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;

    [ObservableProperty] private bool _isDarkTheme;

    public SettingsViewModel(SettingsService settings)
    {
        Title     = "Settings";
        _settings = settings;
        _isDarkTheme = settings.Current.IsDarkTheme;
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        // Apply immediately
        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant =
                value ? ThemeVariant.Dark : ThemeVariant.Light;

        // Persist
        _settings.Current.IsDarkTheme = value;
        _settings.Save();
    }
}
