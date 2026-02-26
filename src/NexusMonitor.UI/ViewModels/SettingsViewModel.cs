using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusMonitor.Core.Services;

namespace NexusMonitor.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;

    [ObservableProperty] private bool   _isDarkTheme;
    [ObservableProperty] private bool   _isAeroGlassEnabled;
    [ObservableProperty] private double _windowOpacity = 0.92;
    [ObservableProperty] private string _accentColorHex = "#0A84FF";
    [ObservableProperty] private bool   _showOverlayWidget;

    public static IReadOnlyList<string> AccentPresets { get; } =
    [
        "#0A84FF", // Blue (default)
        "#BF5AF2", // Purple
        "#FF375F", // Pink
        "#FF9F0A", // Orange
        "#FFD60A", // Yellow
        "#34C759", // Green
        "#5AC8FA", // Teal
        "#FF453A", // Red
    ];

    public SettingsViewModel(SettingsService settings)
    {
        Title     = "Settings";
        _settings = settings;

        // Load saved values via backing fields to avoid triggering partial callbacks during init
        _isDarkTheme         = settings.Current.IsDarkTheme;
        _isAeroGlassEnabled  = settings.Current.IsAeroGlassEnabled;
        _windowOpacity       = settings.Current.WindowOpacity;
        _accentColorHex      = settings.Current.AccentColorHex;
        _showOverlayWidget   = settings.Current.ShowOverlayWidget;

        // Apply on startup
        ApplyGlass(_isAeroGlassEnabled, _windowOpacity);
        ApplyAccentColor(_accentColorHex);
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant =
                value ? ThemeVariant.Dark : ThemeVariant.Light;
        _settings.Current.IsDarkTheme = value;
        _settings.Save();
    }

    partial void OnIsAeroGlassEnabledChanged(bool value)
    {
        _settings.Current.IsAeroGlassEnabled = value;
        _settings.Save();
        ApplyGlass(value, WindowOpacity);
    }

    partial void OnWindowOpacityChanged(double value)
    {
        _settings.Current.WindowOpacity = value;
        _settings.Save();
        ApplyGlass(IsAeroGlassEnabled, value);
    }

    partial void OnAccentColorHexChanged(string value)
    {
        _settings.Current.AccentColorHex = value;
        _settings.Save();
        ApplyAccentColor(value);
    }

    // Set by App.axaml.cs after the overlay window is created
    internal Window? OverlayWindow { private get; set; }

    partial void OnShowOverlayWidgetChanged(bool value)
    {
        _settings.Current.ShowOverlayWidget = value;
        _settings.Save();
        if (value) OverlayWindow?.Show();
        else        OverlayWindow?.Hide();
    }

    [RelayCommand]
    private void SetLightTheme() => IsDarkTheme = false;

    [RelayCommand]
    private void SetAccentColor(string hex) => AccentColorHex = hex;

    // ─── Static helpers — modify Application.Current.Resources directly so DynamicResource
    //     bindings across all views update instantly without a restart.

    private static void ApplyGlass(bool enabled, double opacity)
    {
        byte alpha = enabled ? (byte)Math.Round(opacity * 255) : (byte)0xFF;
        SetBrush("BgBaseBrush",    Color.Parse("#0F0F12"), alpha);
        SetBrush("BgPrimaryBrush", Color.Parse("#1C1C1E"), alpha);
    }

    private static void SetBrush(string key, Color baseColor, byte alpha)
    {
        if (Application.Current is null) return;
        Application.Current.Resources[key] =
            new SolidColorBrush(new Color(alpha, baseColor.R, baseColor.G, baseColor.B));
    }

    private static void ApplyAccentColor(string hex)
    {
        if (Application.Current is null) return;
        try
        {
            var c = Color.Parse(hex);
            Application.Current.Resources["AccentBlueBrush"]      = new SolidColorBrush(c);
            Application.Current.Resources["AccentBlueDimBrush"]   =
                new SolidColorBrush(new Color(0x1A, c.R, c.G, c.B));
            Application.Current.Resources["AccentBlueHoverBrush"] =
                new SolidColorBrush(new Color(c.A,
                    (byte)Math.Min(255, c.R + 25),
                    (byte)Math.Min(255, c.G + 25),
                    (byte)Math.Min(255, c.B + 25)));
        }
        catch { /* ignore invalid hex */ }
    }
}
