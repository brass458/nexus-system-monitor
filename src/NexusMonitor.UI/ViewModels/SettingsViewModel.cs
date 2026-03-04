using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using NexusMonitor.Core.Services;
using NexusMonitor.Core.Storage;
using NexusMonitor.Core.Telemetry;
using NexusMonitor.UI.Messages;
using NexusMonitor.UI.Services;
using SkiaSharp;

namespace NexusMonitor.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly SettingsService         _settings;
    private readonly PrometheusExporter      _exporter;
    private readonly AnomalyDetectionService _anomalyService;
    private readonly AnomalyDetectionConfig  _anomalyConfig;
    private readonly GlassAdaptiveService    _glassAdaptive;

    // Luminance-derived min alpha floor (0x80–0xE0); null = feature disabled
    private byte? _luminanceMinAlpha;

    // ── Appearance ────────────────────────────────────────────────────────────
    [ObservableProperty] private int    _themeModeIndex; // 0=System, 1=Dark, 2=Light

    private static readonly string[] _themeModeValues = ["System", "Dark", "Light"];
    public static IReadOnlyList<string> ThemeModeLabels => _themeModeValues;

    private Action? _osThemeCleanup;

    // ── Crystal Glass ──────────────────────────────────────────────────────────
    [ObservableProperty] private bool   _isGlassEnabled;
    [ObservableProperty] private double _glassOpacity    = 0.80;
    [ObservableProperty] private string _backdropBlurMode = "Acrylic";
    [ObservableProperty] private bool   _isSpecularEnabled;
    [ObservableProperty] private double _specularIntensity = 0.55;

    // ── Accent ────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _accentColorHex     = "#0A84FF";
    [ObservableProperty] private string _textAccentColorHex = "";

    // ── Custom surface colors ─────────────────────────────────────────────────
    [ObservableProperty] private string _customWindowBgHex  = "";
    [ObservableProperty] private string _customSurfaceBgHex = "";
    [ObservableProperty] private string _customSidebarBgHex = "";

    // ── Color picker state ────────────────────────────────────────────────────
    // Unified current-picker color — routes to AccentColorHex or TextAccentColorHex based on TextAccentColorPickerActive.
    [ObservableProperty] private Color _pickerCurrentColor       = Color.Parse("#0A84FF");
    [ObservableProperty] private bool  _textAccentColorPickerActive;

    /// <summary>Dynamic title for the color picker window (changes when picking text accent).</summary>
    public string PickerWindowTitle => TextAccentColorPickerActive ? "Custom Text Accent" : "Custom Accent Color";

    /// <summary>Live hex string shown in the color picker window hex readout.</summary>
    public string PickerCurrentHex  => TextAccentColorPickerActive ? TextAccentColorHex : AccentColorHex;

    // Generic picker: shared by all surface pickers; set before opening dialog
    [ObservableProperty] private Color  _pickerSurfaceColor = Color.Parse("#131318");
    // Hex display string updated whenever PickerSurfaceColor changes (consumed by SurfaceColorPickerWindow)
    [ObservableProperty] private string _pickerSurfaceHex   = "#131318";
    private bool _suppressColorSync;

    // ── Typography ────────────────────────────────────────────────────────────
    [ObservableProperty] private string _fontFamily          = "";
    [ObservableProperty] private double _fontSizeMultiplier  = 1.0;

    // ── Performance ───────────────────────────────────────────────────────────
    [ObservableProperty] private int _updateIntervalIndex = 1; // 0=500ms 1=1s 2=2s 3=5s

    // ── Tray / close behaviour ────────────────────────────────────────────────
    /// <summary>Index into <see cref="CloseActionLabels"/>: 0=Ask, 1=Tray, 2=Exit.</summary>
    [ObservableProperty] private int  _closeActionIndex;
    [ObservableProperty] private bool _hideWidgetOnMinimize;

    // ── Other ─────────────────────────────────────────────────────────────────
    [ObservableProperty] private bool   _showOverlayWidget;

    // ── Notifications ─────────────────────────────────────────────────────────
    [ObservableProperty] private bool   _desktopNotificationsEnabled  = true;
    [ObservableProperty] private bool   _anomalyNotificationsEnabled  = true;

    // ── Smart Glass Tint ──────────────────────────────────────────────────────
    [ObservableProperty] private bool _smartTintEnabled;

    // ── Metrics & History ─────────────────────────────────────────────────────
    [ObservableProperty] private bool _metricsEnabled;

    // ── Anomaly Detection ─────────────────────────────────────────────────────
    [ObservableProperty] private bool    _anomalyDetectionEnabled;
    [ObservableProperty] private string  _anomalySensitivity = "Medium";
    [ObservableProperty] private decimal _anomalyCooldownSeconds = 60m;
    [ObservableProperty] private decimal _metricsEventsRetentionDays = 90m;

    public static IReadOnlyList<string> AnomalySensitivities { get; } = ["Low", "Medium", "High"];

    // ── Telemetry ─────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrometheusStatusText))]
    private bool    _prometheusEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrometheusStatusText))]
    [NotifyPropertyChangedFor(nameof(TelegrafConfig))]
    private decimal _prometheusPort = 9182m;

    public string PrometheusStatusText => PrometheusEnabled
        ? $"Active — scrape at http://localhost:{(int)PrometheusPort}/metrics"
        : "Disabled";

    public string TelegrafConfig      => TelegrafConfigGenerator.Generate((int)PrometheusPort);
    public string GrafanaDashboardJson => GrafanaDashboard.Json;

    // ── Static lists ─────────────────────────────────────────────────────────

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

    public static IReadOnlyList<string> UpdateIntervalLabels { get; } =
        ["500 ms", "1 second", "2 seconds", "5 seconds"];

    private static readonly int[] _intervalValues = [500, 1000, 2000, 5000];

    public static IReadOnlyList<string> CloseActionLabels { get; } =
        ["Always Ask", "Minimize to Tray", "Close Application"];

    private static readonly string[] _closeActionValues = ["", "Tray", "Exit"];

    /// <summary>All system font families — enumerated lazily on first access (deferred past startup).</summary>
    private static readonly Lazy<IReadOnlyList<string>> _lazySystemFonts = new(LoadSystemFonts);
    public static IReadOnlyList<string> SystemFonts => _lazySystemFonts.Value;

    private static IReadOnlyList<string> LoadSystemFonts()
    {
        try
        {
            var fonts = SKFontManager.Default.FontFamilies
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
            fonts.Insert(0, "(System Default)");
            return fonts;
        }
        catch { return ["(System Default)"]; }
    }

    // ── Constructor ──────────────────────────────────────────────────────────

    public SettingsViewModel(
        SettingsService         settings,
        PrometheusExporter      exporter,
        AnomalyDetectionService anomalyService,
        AnomalyDetectionConfig  anomalyConfig,
        GlassAdaptiveService    glassAdaptive)
    {
        Title           = "Settings";
        _settings       = settings;
        _exporter       = exporter;
        _anomalyService = anomalyService;
        _anomalyConfig  = anomalyConfig;
        _glassAdaptive  = glassAdaptive;

        // Subscribe to luminance changes from GlassAdaptiveService
        _glassAdaptive.LuminanceChanged += OnLuminanceChanged;

        // Load saved values via backing fields so partial callbacks don't fire during init
        _themeModeIndex     = Array.IndexOf(_themeModeValues, settings.Current.ThemeMode);
        if (_themeModeIndex < 0) _themeModeIndex = 0;
        _isGlassEnabled     = settings.Current.IsGlassEnabled;
        _glassOpacity       = settings.Current.GlassOpacity;
        _backdropBlurMode   = settings.Current.BackdropBlurMode;
        _isSpecularEnabled  = settings.Current.IsSpecularEnabled;
        _specularIntensity  = settings.Current.SpecularIntensity;
        _accentColorHex     = settings.Current.AccentColorHex;
        _textAccentColorHex = settings.Current.TextAccentColorHex;
        // Initialise picker from stored accent
        try
        {
            _pickerCurrentColor = Color.Parse(settings.Current.AccentColorHex);
        }
        catch { _pickerCurrentColor = Color.Parse("#0A84FF"); }
        _customWindowBgHex  = settings.Current.CustomWindowBgHex;
        _customSurfaceBgHex = settings.Current.CustomSurfaceBgHex;
        _customSidebarBgHex = settings.Current.CustomSidebarBgHex;
        _fontFamily              = settings.Current.FontFamily;
        _fontSizeMultiplier      = settings.Current.FontSizeMultiplier;
        _showOverlayWidget             = settings.Current.ShowOverlayWidget;
        _desktopNotificationsEnabled   = settings.Current.DesktopNotificationsEnabled;
        _anomalyNotificationsEnabled   = settings.Current.AnomalyNotificationsEnabled;
        _smartTintEnabled              = settings.Current.SmartTintEnabled;

        // Map stored CloseAction → index
        _closeActionIndex = Array.IndexOf(_closeActionValues, settings.Current.CloseAction);
        if (_closeActionIndex < 0) _closeActionIndex = 0;
        _hideWidgetOnMinimize = settings.Current.HideWidgetOnMinimize;

        // Map stored UpdateIntervalMs → index
        _updateIntervalIndex = Array.IndexOf(_intervalValues, settings.Current.UpdateIntervalMs);
        if (_updateIntervalIndex < 0) _updateIntervalIndex = 1;

        // ── Metrics & History ─────────────────────────────────────────────────
        _metricsEnabled = settings.Current.MetricsEnabled;

        // ── Anomaly Detection ─────────────────────────────────────────────────
        _anomalyDetectionEnabled     = settings.Current.AnomalyDetectionEnabled;
        _anomalySensitivity          = settings.Current.AnomalySensitivity;
        _anomalyCooldownSeconds      = settings.Current.AnomalyCooldownSeconds;
        _metricsEventsRetentionDays  = settings.Current.MetricsEventsRetentionDays;

        // ── Telemetry ────────────────────────────────────────────────────────
        _prometheusEnabled = settings.Current.PrometheusEnabled;
        _prometheusPort    = settings.Current.PrometheusPort;

        // Restore at startup
        ApplyGlass(_isGlassEnabled, _glassOpacity,
            _customWindowBgHex, _customSurfaceBgHex, _customSidebarBgHex);
        ApplySpecular(_isGlassEnabled, _isSpecularEnabled, _specularIntensity);
        ApplyBackdropMode(_isGlassEnabled, _backdropBlurMode);
        ApplyAccentColor(_accentColorHex);
        ApplyTextAccent(_accentColorHex, _textAccentColorHex);
        ApplyFont(_fontFamily, _fontSizeMultiplier);
    }

    // ── Partial callbacks ─────────────────────────────────────────────────────

    partial void OnThemeModeIndexChanged(int value)
    {
        // Cancel any existing OS theme subscription
        _osThemeCleanup?.Invoke();
        _osThemeCleanup = null;

        var mode = _themeModeValues[Math.Clamp(value, 0, _themeModeValues.Length - 1)];
        _settings.Current.ThemeMode = mode;
        _settings.Save();

        ThemeVariant variant;
        if (mode == "System")
        {
            variant = NexusMonitor.UI.App.DetectSystemTheme();
            // Re-follow OS theme changes while in System mode
            if (Application.Current?.PlatformSettings is { } ps)
            {
                void Handler(object? s, PlatformColorValues e)
                {
                    if (Application.Current is not null)
                        Application.Current.RequestedThemeVariant = NexusMonitor.UI.App.DetectSystemTheme();
                    ApplyGlass(IsGlassEnabled, GlassOpacity,
                        CustomWindowBgHex, CustomSurfaceBgHex, CustomSidebarBgHex, _luminanceMinAlpha);
                }
                ps.ColorValuesChanged += Handler;
                _osThemeCleanup = () => ps.ColorValuesChanged -= Handler;
            }
        }
        else
        {
            variant = mode == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        if (Application.Current is not null)
            Application.Current.RequestedThemeVariant = variant;
        // Re-apply glass so background brushes recalculate with the new theme.
        ApplyGlass(IsGlassEnabled, GlassOpacity,
            CustomWindowBgHex, CustomSurfaceBgHex, CustomSidebarBgHex, _luminanceMinAlpha);
    }

    partial void OnIsGlassEnabledChanged(bool value)
    {
        _settings.Current.IsGlassEnabled = value;
        _settings.Save();
        ApplyGlass(value, GlassOpacity, CustomWindowBgHex, CustomSurfaceBgHex, CustomSidebarBgHex, _luminanceMinAlpha);
        ApplySpecular(value, IsSpecularEnabled, SpecularIntensity);
        ApplyBackdropMode(value, BackdropBlurMode);
    }

    partial void OnGlassOpacityChanged(double value)
    {
        _settings.Current.GlassOpacity = value;
        _settings.Save();
        ApplyGlass(IsGlassEnabled, value, CustomWindowBgHex, CustomSurfaceBgHex, CustomSidebarBgHex, _luminanceMinAlpha);
    }

    partial void OnCustomWindowBgHexChanged(string value)
    {
        _settings.Current.CustomWindowBgHex = value;
        _settings.Save();
        ApplyGlass(IsGlassEnabled, GlassOpacity, value, CustomSurfaceBgHex, CustomSidebarBgHex, _luminanceMinAlpha);
    }

    partial void OnCustomSurfaceBgHexChanged(string value)
    {
        _settings.Current.CustomSurfaceBgHex = value;
        _settings.Save();
        ApplyGlass(IsGlassEnabled, GlassOpacity, CustomWindowBgHex, value, CustomSidebarBgHex, _luminanceMinAlpha);
    }

    partial void OnCustomSidebarBgHexChanged(string value)
    {
        _settings.Current.CustomSidebarBgHex = value;
        _settings.Save();
        ApplyGlass(IsGlassEnabled, GlassOpacity, CustomWindowBgHex, CustomSurfaceBgHex, value, _luminanceMinAlpha);
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
        ApplyTextAccent(value, TextAccentColorHex);

        // Keep the color-wheel picker in sync when accent is changed via presets
        if (!_suppressColorSync && !TextAccentColorPickerActive)
        {
            try
            {
                _suppressColorSync = true;
                PickerCurrentColor = Color.Parse(value);
            }
            catch { /* invalid hex — leave picker unchanged */ }
            finally { _suppressColorSync = false; }
        }
        OnPropertyChanged(nameof(PickerCurrentHex));
    }

    partial void OnTextAccentColorHexChanged(string value)
    {
        _settings.Current.TextAccentColorHex = value;
        _settings.Save();
        ApplyTextAccent(AccentColorHex, value);
        if (TextAccentColorPickerActive) OnPropertyChanged(nameof(PickerCurrentHex));
    }

    partial void OnFontFamilyChanged(string value)
    {
        _settings.Current.FontFamily = value;
        _settings.Save();
        ApplyFont(value, FontSizeMultiplier);
    }

    partial void OnFontSizeMultiplierChanged(double value)
    {
        _settings.Current.FontSizeMultiplier = value;
        _settings.Save();
        ApplyFont(FontFamily, value);
    }

    partial void OnCloseActionIndexChanged(int value)
    {
        _settings.Current.CloseAction =
            _closeActionValues[Math.Clamp(value, 0, _closeActionValues.Length - 1)];
        _settings.Save();
    }

    partial void OnHideWidgetOnMinimizeChanged(bool value)
    {
        _settings.Current.HideWidgetOnMinimize = value;
        _settings.Save();
    }

    partial void OnUpdateIntervalIndexChanged(int value)
    {
        var ms = _intervalValues[Math.Clamp(value, 0, _intervalValues.Length - 1)];
        _settings.Current.UpdateIntervalMs = ms;
        _settings.Save();
        WeakReferenceMessenger.Default.Send(
            new MetricsIntervalChangedMessage(TimeSpan.FromMilliseconds(ms)));
    }

    // ── Overlay window ────────────────────────────────────────────────────────

    // Set by App.axaml.cs after the overlay window is created
    private Window? _overlayWindow;
    internal Window? OverlayWindow
    {
        get => _overlayWindow;
        set
        {
            _overlayWindow = value;
            // Apply current glass/backdrop/font settings to the newly assigned overlay
            if (value is not null)
            {
                ApplyBackdropMode(IsGlassEnabled, BackdropBlurMode);
                ApplyFont(FontFamily, FontSizeMultiplier);
            }
        }
    }

    partial void OnShowOverlayWidgetChanged(bool value)
    {
        _settings.Current.ShowOverlayWidget = value;
        _settings.Save();
        if (value) OverlayWindow?.Show();
        else        OverlayWindow?.Hide();
    }

    partial void OnDesktopNotificationsEnabledChanged(bool value)
    {
        _settings.Current.DesktopNotificationsEnabled = value;
        _settings.Save();
    }

    partial void OnAnomalyNotificationsEnabledChanged(bool value)
    {
        _settings.Current.AnomalyNotificationsEnabled = value;
        _settings.Save();
    }

    partial void OnSmartTintEnabledChanged(bool value)
    {
        _settings.Current.SmartTintEnabled = value;
        _settings.Save();
        if (value)
            _glassAdaptive.Start();
        else
        {
            _glassAdaptive.Stop();
            _luminanceMinAlpha = null;
            ApplyGlass(IsGlassEnabled, GlassOpacity, CustomWindowBgHex, CustomSurfaceBgHex, CustomSidebarBgHex);
        }
    }

    private void OnLuminanceChanged(byte minAlpha)
    {
        _luminanceMinAlpha = minAlpha;
        ApplyGlass(IsGlassEnabled, GlassOpacity, CustomWindowBgHex, CustomSurfaceBgHex, CustomSidebarBgHex, minAlpha);
    }

    partial void OnMetricsEnabledChanged(bool value)
    {
        _settings.Current.MetricsEnabled = value;
        _settings.Save();
        var store  = App.Services.GetRequiredService<MetricsStore>();
        var rollup = App.Services.GetRequiredService<MetricsRollupService>();
        if (value)
        {
            store.Start(TimeSpan.FromMilliseconds(_settings.Current.UpdateIntervalMs));
            rollup.Start();
        }
        else
        {
            store.Stop();
            rollup.Stop();
        }
        WeakReferenceMessenger.Default.Send(new MetricsEnabledChangedMessage(value));
    }

    partial void OnAnomalyDetectionEnabledChanged(bool value)
    {
        _settings.Current.AnomalyDetectionEnabled = value;
        _settings.Save();
        _anomalyConfig.Enabled = value;
        if (value)
            _anomalyService.Start();
        else
            _anomalyService.Stop();
    }

    partial void OnAnomalySensitivityChanged(string value)
    {
        _settings.Current.AnomalySensitivity = value;
        _settings.Save();
        _anomalyConfig.ApplySensitivity(value);   // mutates in-place, takes effect next tick
    }

    partial void OnAnomalyCooldownSecondsChanged(decimal value)
    {
        _settings.Current.AnomalyCooldownSeconds = (int)value;
        _settings.Save();
        _anomalyConfig.CooldownSeconds = (int)value;
    }

    partial void OnMetricsEventsRetentionDaysChanged(decimal value)
    {
        _settings.Current.MetricsEventsRetentionDays = (int)value;
        _settings.Save();
    }

    partial void OnPrometheusEnabledChanged(bool value)
    {
        _settings.Current.PrometheusEnabled = value;
        _settings.Save();
        if (value)
            _exporter.Start((int)PrometheusPort);
        else
            _exporter.Stop();
    }

    partial void OnPrometheusPortChanged(decimal value)
    {
        _settings.Current.PrometheusPort = (int)value;
        _settings.Save();
        if (PrometheusEnabled)
        {
            _exporter.Stop();
            _exporter.Start((int)value);
        }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand] private void SetAccentColor(string h)       => AccentColorHex = h;
    [RelayCommand] private void SetTextAccentColor(string? h) => TextAccentColorHex = h ?? "";
    [RelayCommand] private void ResetWindowBg()               => CustomWindowBgHex  = "";
    [RelayCommand] private void ResetSurfaceBg()          => CustomSurfaceBgHex = "";
    [RelayCommand] private void ResetSidebarBg()          => CustomSidebarBgHex = "";

    /// <summary>Called by the ColorWheelControl binding when the user picks a color.
    /// Routes to <see cref="AccentColorHex"/> or <see cref="TextAccentColorHex"/>
    /// depending on <see cref="TextAccentColorPickerActive"/>.</summary>
    partial void OnPickerCurrentColorChanged(Color value)
    {
        if (_suppressColorSync) return;
        var hex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
        _suppressColorSync = true;
        try
        {
            if (TextAccentColorPickerActive)
            {
                if (!string.Equals(hex, TextAccentColorHex, StringComparison.OrdinalIgnoreCase))
                    TextAccentColorHex = hex;
            }
            else
            {
                if (!string.Equals(hex, AccentColorHex, StringComparison.OrdinalIgnoreCase))
                    AccentColorHex = hex;
            }
        }
        finally { _suppressColorSync = false; }
    }

    partial void OnTextAccentColorPickerActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(PickerWindowTitle));
        OnPropertyChanged(nameof(PickerCurrentHex));
    }

    /// <summary>Keeps <see cref="PickerSurfaceHex"/> in sync with the color wheel in SurfaceColorPickerWindow.</summary>
    partial void OnPickerSurfaceColorChanged(Color value)
    {
        PickerSurfaceHex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
    }

    // ── Static resource helpers ───────────────────────────────────────────────
    //   Write directly into Application.Current.Resources so every
    //   {DynamicResource …} binding across the whole app updates instantly.

    /// <summary>
    /// Updates ALL background and glass-surface brushes.
    /// Custom color overrides (from the Settings color pickers) are respected when non-empty.
    /// Empty strings fall back to the built-in dark-theme defaults.
    /// <paramref name="opacity"/> 0 = fully transparent, 1 = fully opaque.
    /// </summary>
    private static void ApplyGlass(
        bool enabled, double opacity,
        string? customWindowBgHex  = null,
        string? customSurfaceBgHex = null,
        string? customSidebarBgHex = null,
        byte?   luminanceMinAlpha  = null)
    {
        if (Application.Current is null) return;

        // Determine current theme so fallback colours match the active palette.
        bool isDark = Application.Current.RequestedThemeVariant != ThemeVariant.Light;

        Color defaultBgBase    = isDark ? Color.Parse("#0A0A12") : Color.Parse("#F5F5FA");
        Color defaultBgSurface = isDark ? Color.Parse("#131318") : Color.Parse("#FFFFFF");

        // Resolve base colors — custom hex wins if valid; else use theme-appropriate defaults.
        Color bgBase    = TryParseColor(customWindowBgHex,  defaultBgBase);
        Color bgSurface = TryParseColor(customSurfaceBgHex, defaultBgSurface);
        Color bgSidebar = TryParseColor(customSidebarBgHex, defaultBgSurface);

        // Derive surface variants: lighten for dark mode, darken for light mode.
        // AdjustBrightness clamps to [0,255] in both directions.
        int secondaryDelta = isDark ?  9 : -15;
        int elevatedDelta  = isDark ? 16 : -23;
        int hoverDelta     = isDark ? 26 : -34;
        Color bgSecondary = AdjustBrightness(bgSurface, secondaryDelta);
        Color bgElevated  = AdjustBrightness(bgSurface, elevatedDelta);
        Color bgHover     = AdjustBrightness(bgSurface, hoverDelta);

        // Window-frame brush can go fully transparent (that IS the glass effect)
        byte bgAlpha = enabled ? (byte)Math.Round(opacity * 255) : (byte)0xFF;
        SetBrush("BgBaseBrush", bgBase, bgAlpha);

        // Content-area brushes: floor raised by wallpaper luminance when Smart Tint is active.
        // Cap at 0xCC (80%) so OS acrylic blur always bleeds through; without it, opacity=1.0
        // yields alpha=255 (fully opaque) and no glass shows in the content area.
        byte floor = luminanceMinAlpha ?? 0xA0;
        byte contentAlpha = enabled
            ? (byte)Math.Min(0xCC, Math.Max(floor, (int)Math.Round(opacity * 255)))
            : (byte)0xFF;
        SetBrush("BgPrimaryBrush",   bgSurface,   contentAlpha);
        SetBrush("BgSecondaryBrush", bgSecondary, contentAlpha);
        SetBrush("BgElevatedBrush",  bgElevated,  contentAlpha);
        SetBrush("BgHoverBrush",     bgHover,     contentAlpha);

        // Sidebar / nav glass layer.
        // Floor at 0xA0 (63%) so the sidebar is never so transparent that text
        // becomes unreadable over bright wallpapers (e.g. pure white desktop).
        byte glassAlpha = enabled
            ? (byte)Math.Max(0xA0, (int)Math.Round(opacity * 0xB2))
            : (byte)0xB2;
        SetBrush("GlassBgBrush", bgSidebar, glassAlpha);

        // Glass border — derive from sidebar base with low alpha; use light or dark tint.
        byte borderAlpha = enabled ? (byte)0x60 : (byte)0x40;
        Application.Current.Resources["GlassBorderBrush"] = isDark
            ? new SolidColorBrush(new Color(borderAlpha,
                (byte)Math.Min(255, bgSidebar.R + 0x20),
                (byte)Math.Min(255, bgSidebar.G + 0x20),
                (byte)Math.Min(255, bgSidebar.B + 0x20)))
            : new SolidColorBrush(new Color(borderAlpha,
                (byte)Math.Max(0, bgSidebar.R - 0x20),
                (byte)Math.Max(0, bgSidebar.G - 0x20),
                (byte)Math.Max(0, bgSidebar.B - 0x20)));

        // Overlay widget: always semi-transparent; use theme-appropriate base colour.
        // Floor is higher in light mode so labels remain legible over bright wallpapers.
        byte overlayFloor = luminanceMinAlpha ?? (isDark ? (byte)0x80 : (byte)0xA0);
        byte overlayAlpha = enabled
            ? (byte)Math.Max(overlayFloor, (int)Math.Round(opacity * 0xCC))
            : (byte)0xCC;
        (byte oR, byte oG, byte oB) = isDark ? ((byte)0x05, (byte)0x05, (byte)0x08)
                                              : ((byte)0xF0, (byte)0xF0, (byte)0xF5);
        Application.Current.Resources["OverlayBgBrush"] =
            new SolidColorBrush(new Color(overlayAlpha, oR, oG, oB));

        // Text readability: dark glow in dark mode, no effect in light mode
        // (light backgrounds don't need an outline to stay legible).
        Application.Current.Resources["GlassTextEffect"] = (enabled && isDark)
            ? (object)new DropShadowDirectionEffect
              {
                  BlurRadius  = 6,
                  Color       = Colors.Black,
                  ShadowDepth = 0,   // centred → expands in all directions = outline
                  Direction   = 315,
                  Opacity     = 1.0,
              }
            : null;
    }

    private static Color TryParseColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try { return Color.Parse(hex); }
        catch { return fallback; }
    }

    /// <summary>
    /// Adds <paramref name="amount"/> to each R, G, B channel, clamped to [0, 255].
    /// Positive values lighten; negative values darken.
    /// </summary>
    private static Color AdjustBrightness(Color c, int amount) =>
        new Color(c.A,
            (byte)Math.Clamp(c.R + amount, 0, 255),
            (byte)Math.Clamp(c.G + amount, 0, 255),
            (byte)Math.Clamp(c.B + amount, 0, 255));

    /// <summary>
    /// Controls the opacity of ALL specular highlight overlays in MainWindow and OverlayWindow.
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
    /// Sets the <see cref="WindowTransparencyLevel"/> hint on BOTH the main window
    /// and the overlay widget so the OS provides the correct backdrop blur type.
    /// </summary>
    private void ApplyBackdropMode(bool glassEnabled, string mode)
    {
        IReadOnlyList<WindowTransparencyLevel> hints = (!glassEnabled || mode == "None")
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

        if (Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is Window main)
        {
            main.TransparencyLevelHint = hints;
            // Gate shimmer timer: run only when glass is visually active
            if (main is NexusMonitor.UI.MainWindow nexusMain)
                nexusMain.SetGlassActive(glassEnabled && mode != "None");
        }

        if (_overlayWindow is not null)
        {
            // Overlay always needs Transparent as last-resort fallback (never None) so
            // the CornerRadius clips correctly — None would give a square opaque window.
            IReadOnlyList<WindowTransparencyLevel> overlayHints = (!glassEnabled || mode == "None")
                ? [WindowTransparencyLevel.Transparent]
                : mode switch
                {
                    "Blur" => [WindowTransparencyLevel.Blur,
                               WindowTransparencyLevel.Transparent],
                    "Mica" => [WindowTransparencyLevel.Mica,
                               WindowTransparencyLevel.AcrylicBlur,
                               WindowTransparencyLevel.Blur,
                               WindowTransparencyLevel.Transparent],
                    _      => [WindowTransparencyLevel.AcrylicBlur,
                               WindowTransparencyLevel.Blur,
                               WindowTransparencyLevel.Transparent],
                };
            _overlayWindow.TransparencyLevelHint = overlayHints;
        }
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

    /// <summary>
    /// Writes <c>TextAccentBrush</c>.  If <paramref name="textHex"/> is empty the
    /// brush derives from the primary accent at 90% opacity for a softer text look.
    /// </summary>
    private static void ApplyTextAccent(string accentHex, string textHex)
    {
        if (Application.Current is null) return;
        try
        {
            Color base_ = Color.Parse(accentHex);
            Color c = string.IsNullOrWhiteSpace(textHex)
                ? new Color(0xE6, base_.R, base_.G, base_.B)  // 90% opacity accent
                : Color.Parse(textHex);
            Application.Current.Resources["TextAccentBrush"] = new SolidColorBrush(c);
        }
        catch { /* ignore invalid hex */ }
    }

    /// <summary>
    /// Applies <paramref name="family"/> as the <c>FontFamily</c> on every open
    /// window.  An empty string or "(System Default)" restores the system default.
    /// <paramref name="multiplier"/> scales the base font size (1.0 = default).
    /// </summary>
    private void ApplyFont(string family, double multiplier = 1.0)
    {
        if (Application.Current?.ApplicationLifetime
                is not IClassicDesktopStyleApplicationLifetime desktop) return;

        var ff = string.IsNullOrWhiteSpace(family) || family == "(System Default)"
            ? Avalonia.Media.FontFamily.Default
            : new Avalonia.Media.FontFamily(family);

        const double BaseFontSize = 15.0; // matches NxFont13 token (bumped from 14→15)
        double fontSize = BaseFontSize * Math.Clamp(multiplier, 0.5, 3.0);
        if (desktop.MainWindow is Window main) { main.FontFamily = ff; main.FontSize = fontSize; }
        if (_overlayWindow      is Window ow)  { ow.FontFamily   = ff; ow.FontSize   = fontSize; }

        // Scale all NxFont* and FontSize* resource tokens so {DynamicResource} bindings update instantly.
        double m = Math.Clamp(multiplier, 0.5, 3.0);
        (string Key, double Base)[] fontTokens =
        [
            ("NxFont10", 12), ("NxFont11", 13), ("NxFont12", 14), ("NxFont13", 15),
            ("NxFont14", 16), ("NxFont16", 18), ("NxFont18", 21), ("NxFont24", 30),
            ("NxFontSm", 13), ("NxFontBase", 14), ("NxFontMd", 15),
            ("FontSizeXS", 12), ("FontSizeSM", 13), ("FontSizeMD", 14),
            ("FontSizeBase", 15), ("FontSizeLG", 17), ("FontSizeXL", 19),
            ("FontSize2XL", 23), ("FontSize3XL", 30),
        ];
        foreach (var (key, baseVal) in fontTokens)
            Application.Current!.Resources[key] = Math.Round(baseVal * m, 1);
    }

    private static void SetBrush(string key, Color baseColor, byte alpha)
    {
        if (Application.Current is null) return;
        Application.Current.Resources[key] =
            new SolidColorBrush(new Color(alpha, baseColor.R, baseColor.G, baseColor.B));
    }

    public void Dispose()
    {
        _osThemeCleanup?.Invoke();
        _glassAdaptive.LuminanceChanged -= OnLuminanceChanged;
    }
}
