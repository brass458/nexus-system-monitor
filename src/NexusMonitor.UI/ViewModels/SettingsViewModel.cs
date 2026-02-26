using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusMonitor.Core.Services;

namespace NexusMonitor.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settings;

    // ── Appearance ────────────────────────────────────────────────────────────
    [ObservableProperty] private bool   _isDarkTheme;

    // ── Liquid Glass ──────────────────────────────────────────────────────────
    [ObservableProperty] private bool   _isGlassEnabled;
    [ObservableProperty] private double _glassOpacity    = 0.80;
    [ObservableProperty] private string _backdropBlurMode = "Acrylic";
    [ObservableProperty] private bool   _isSpecularEnabled;
    [ObservableProperty] private double _specularIntensity = 0.35;

    // ── Accent ────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _accentColorHex = "#0A84FF";

    // ── Other ─────────────────────────────────────────────────────────────────
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

    public static IReadOnlyList<string> BackdropModes { get; } =
        ["None", "Blur", "Acrylic", "Mica"];

    public SettingsViewModel(SettingsService settings)
    {
        Title     = "Settings";
        _settings = settings;

        // Load saved values via backing fields so partial callbacks don't fire during init
        _isDarkTheme       = settings.Current.IsDarkTheme;
        _isGlassEnabled    = settings.Current.IsGlassEnabled;
        _glassOpacity      = settings.Current.GlassOpacity;
        _backdropBlurMode  = settings.Current.BackdropBlurMode;
        _isSpecularEnabled = settings.Current.IsSpecularEnabled;
        _specularIntensity = settings.Current.SpecularIntensity;
        _accentColorHex    = settings.Current.AccentColorHex;
        _showOverlayWidget = settings.Current.ShowOverlayWidget;

        // Restore at startup
        ApplyGlass(_isGlassEnabled, _glassOpacity);
        ApplySpecular(_isGlassEnabled, _isSpecularEnabled, _specularIntensity);
        ApplyBackdropMode(_isGlassEnabled, _backdropBlurMode);
        ApplyAccentColor(_accentColorHex);
    }

    // ── Partial callbacks ─────────────────────────────────────────────────────

    partial void OnIsDarkThemeChanged(bool value)
    {
        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant =
                value ? ThemeVariant.Dark : ThemeVariant.Light;
        _settings.Current.IsDarkTheme = value;
        _settings.Save();
    }

    partial void OnIsGlassEnabledChanged(bool value)
    {
        _settings.Current.IsGlassEnabled = value;
        _settings.Save();
        ApplyGlass(value, GlassOpacity);
        ApplySpecular(value, IsSpecularEnabled, SpecularIntensity);
        ApplyBackdropMode(value, BackdropBlurMode);
    }

    partial void OnGlassOpacityChanged(double value)
    {
        _settings.Current.GlassOpacity = value;
        _settings.Save();
        ApplyGlass(IsGlassEnabled, value);
    }

    partial void OnBackdropBlurModeChanged(string value)
    {
        _settings.Current.BackdropBlurMode = value;
        _settings.Save();
        ApplyBackdropMode(IsGlassEnabled, value);
    }

    partial void OnIsSpecularEnabledChanged(bool value)
    {
        _settings.Current.IsSpecularEnabled = value;
        _settings.Save();
        ApplySpecular(IsGlassEnabled, value, SpecularIntensity);
    }

    partial void OnSpecularIntensityChanged(double value)
    {
        _settings.Current.SpecularIntensity = value;
        _settings.Save();
        ApplySpecular(IsGlassEnabled, IsSpecularEnabled, value);
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

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand] private void SetLightTheme()          => IsDarkTheme = false;
    [RelayCommand] private void SetAccentColor(string h) => AccentColorHex = h;

    // ── Static resource helpers ───────────────────────────────────────────────
    //   Write directly into Application.Current.Resources so every
    //   {DynamicResource …} binding across the whole app updates instantly.

    /// <summary>
    /// Updates ALL background and glass-surface brushes.
    /// <paramref name="opacity"/> 0 = fully transparent, 1 = fully opaque.
    /// </summary>
    private static void ApplyGlass(bool enabled, double opacity)
    {
        if (Application.Current is null) return;

        // Solid fill layers: content area, cards, hover cells
        byte bg = enabled ? (byte)Math.Round(opacity * 255) : (byte)0xFF;
        SetBrush("BgBaseBrush",      Color.Parse("#0F0F12"), bg);
        SetBrush("BgPrimaryBrush",   Color.Parse("#1C1C1E"), bg);
        SetBrush("BgSecondaryBrush", Color.Parse("#252528"), bg);
        SetBrush("BgElevatedBrush",  Color.Parse("#2C2C2E"), bg);
        SetBrush("BgHoverBrush",     Color.Parse("#363638"), bg);

        // Glass surface layers (sidebar + titlebar) — keep proportional to
        // their design-intent opacity (0xB2 = 70%) so they read as a separate
        // material layer when partially transparent.
        byte glass = enabled ? (byte)Math.Round(opacity * 0xB2) : (byte)0xB2;
        SetBrush("GlassBgBrush", Color.Parse("#1C1C1E"), glass);

        // Glass border becomes brighter when glass is active so the rim shows
        byte borderAlpha = enabled ? (byte)0x60 : (byte)0x40;
        Application.Current.Resources["GlassBorderBrush"] =
            new SolidColorBrush(new Color(borderAlpha, 0x3A, 0x3A, 0x3C));
    }

    /// <summary>
    /// Controls the opacity of the specular highlight overlays in MainWindow.
    /// Writes the <c>GlassSpecularOpacity</c> double resource consumed by
    /// <c>Opacity="{DynamicResource GlassSpecularOpacity}"</c> in XAML.
    /// </summary>
    private static void ApplySpecular(bool glassEnabled, bool specularEnabled, double intensity)
    {
        if (Application.Current is null) return;
        Application.Current.Resources["GlassSpecularOpacity"] =
            (glassEnabled && specularEnabled) ? Math.Clamp(intensity, 0, 1) : 0.0;
    }

    /// <summary>
    /// Sets the Window's <see cref="WindowTransparencyLevel"/> hint so the OS
    /// provides the correct backdrop blur type.
    /// </summary>
    private static void ApplyBackdropMode(bool glassEnabled, string mode)
    {
        if (Application.Current?.ApplicationLifetime
                is not IClassicDesktopStyleApplicationLifetime desktop) return;
        if (desktop.MainWindow is not Window win) return;

        win.TransparencyLevelHint = (!glassEnabled || mode == "None")
            ? [WindowTransparencyLevel.None]
            : mode switch
            {
                "Blur"   => [WindowTransparencyLevel.Blur,
                             WindowTransparencyLevel.None],
                "Mica"   => [WindowTransparencyLevel.Mica,
                             WindowTransparencyLevel.AcrylicBlur,
                             WindowTransparencyLevel.Blur,
                             WindowTransparencyLevel.None],
                _        => [WindowTransparencyLevel.AcrylicBlur,  // Acrylic (default)
                             WindowTransparencyLevel.Blur,
                             WindowTransparencyLevel.None],
            };
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

    private static void SetBrush(string key, Color baseColor, byte alpha)
    {
        if (Application.Current is null) return;
        Application.Current.Resources[key] =
            new SolidColorBrush(new Color(alpha, baseColor.R, baseColor.G, baseColor.B));
    }
}
