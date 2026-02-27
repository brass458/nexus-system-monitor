using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using NexusMonitor.Core.Services;
using NexusMonitor.UI.Messages;
using SkiaSharp;

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
    [ObservableProperty] private double _specularIntensity = 0.55;

    // ── Accent ────────────────────────────────────────────────────────────────
    [ObservableProperty] private string _accentColorHex     = "#0A84FF";
    [ObservableProperty] private string _textAccentColorHex = "";

    // ── Color picker state ────────────────────────────────────────────────────
    [ObservableProperty] private Color  _pickerAccentColor  = Color.Parse("#0A84FF");
    private bool _suppressColorSync;

    // ── Typography ────────────────────────────────────────────────────────────
    [ObservableProperty] private string _fontFamily  = "";
    [ObservableProperty] private double _fontScale   = 1.0;

    // ── Performance ───────────────────────────────────────────────────────────
    [ObservableProperty] private int _updateIntervalIndex = 1; // 0=500ms 1=1s 2=2s 3=5s

    // ── Tray / close behaviour ────────────────────────────────────────────────
    /// <summary>Index into <see cref="CloseActionLabels"/>: 0=Ask, 1=Tray, 2=Exit.</summary>
    [ObservableProperty] private int  _closeActionIndex;
    [ObservableProperty] private bool _hideWidgetOnMinimize;

    // ── Other ─────────────────────────────────────────────────────────────────
    [ObservableProperty] private bool   _showOverlayWidget;

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

    /// <summary>All system font families enumerated once via SkiaSharp (sorted).</summary>
    public static IReadOnlyList<string> SystemFonts { get; } = LoadSystemFonts();

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

    public SettingsViewModel(SettingsService settings)
    {
        Title     = "Settings";
        _settings = settings;

        // Load saved values via backing fields so partial callbacks don't fire during init
        _isDarkTheme        = settings.Current.IsDarkTheme;
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
            _pickerAccentColor = Color.Parse(settings.Current.AccentColorHex);
        }
        catch { _pickerAccentColor = Color.Parse("#0A84FF"); }
        _fontFamily         = settings.Current.FontFamily;
        _fontScale          = settings.Current.FontScale;
        _showOverlayWidget  = settings.Current.ShowOverlayWidget;

        // Map stored CloseAction → index
        _closeActionIndex = Array.IndexOf(_closeActionValues, settings.Current.CloseAction);
        if (_closeActionIndex < 0) _closeActionIndex = 0;
        _hideWidgetOnMinimize = settings.Current.HideWidgetOnMinimize;

        // Map stored UpdateIntervalMs → index
        _updateIntervalIndex = Array.IndexOf(_intervalValues, settings.Current.UpdateIntervalMs);
        if (_updateIntervalIndex < 0) _updateIntervalIndex = 1;

        // Restore at startup
        ApplyGlass(_isGlassEnabled, _glassOpacity);
        ApplySpecular(_isGlassEnabled, _isSpecularEnabled, _specularIntensity);
        ApplyBackdropMode(_isGlassEnabled, _backdropBlurMode);
        ApplyAccentColor(_accentColorHex);
        ApplyTextAccent(_accentColorHex, _textAccentColorHex);
        ApplyFont(_fontFamily);
        ApplyFontScale(_fontScale);
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
        ApplyTextAccent(value, TextAccentColorHex);

        // Keep the color-wheel picker in sync when accent is changed via presets
        if (!_suppressColorSync)
        {
            try
            {
                _suppressColorSync = true;
                PickerAccentColor  = Color.Parse(value);
            }
            catch { /* invalid hex — leave picker unchanged */ }
            finally { _suppressColorSync = false; }
        }
    }

    partial void OnTextAccentColorHexChanged(string value)
    {
        _settings.Current.TextAccentColorHex = value;
        _settings.Save();
        ApplyTextAccent(AccentColorHex, value);
    }

    partial void OnFontFamilyChanged(string value)
    {
        _settings.Current.FontFamily = value;
        _settings.Save();
        ApplyFont(value);
    }

    partial void OnFontScaleChanged(double value)
    {
        _settings.Current.FontScale = value;
        _settings.Save();
        ApplyFontScale(value);
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
                ApplyFont(FontFamily);
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

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand] private void SetLightTheme()           => IsDarkTheme = false;
    [RelayCommand] private void SetAccentColor(string h)  => AccentColorHex = h;

    /// <summary>Called by the ColorWheelControl binding when the user picks a color.</summary>
    partial void OnPickerAccentColorChanged(Color value)
    {
        if (_suppressColorSync) return;
        var hex = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
        if (!string.Equals(hex, AccentColorHex, StringComparison.OrdinalIgnoreCase))
            AccentColorHex = hex;
    }

    // ── Static resource helpers ───────────────────────────────────────────────
    //   Write directly into Application.Current.Resources so every
    //   {DynamicResource …} binding across the whole app updates instantly.

    /// <summary>
    /// Updates ALL background and glass-surface brushes.
    /// Also updates OverlayBgBrush with a 50% opacity floor so the widget
    /// stays readable even at full transparency.
    /// <paramref name="opacity"/> 0 = fully transparent, 1 = fully opaque.
    /// </summary>
    private static void ApplyGlass(bool enabled, double opacity)
    {
        if (Application.Current is null) return;

        // Window-frame brush can go fully transparent (that IS the glass effect)
        byte bg = enabled ? (byte)Math.Round(opacity * 255) : (byte)0xFF;
        SetBrush("BgBaseBrush", Color.Parse("#0F0F12"), bg);

        // Content-area brushes: floor at 0xA0 (~63%) so text stays readable
        // at any transparency level (light text on near-transparent = invisible on white BG)
        byte contentBg = enabled
            ? (byte)Math.Max(0xA0, (int)Math.Round(opacity * 255))
            : (byte)0xFF;
        SetBrush("BgPrimaryBrush",   Color.Parse("#1C1C1E"), contentBg);
        SetBrush("BgSecondaryBrush", Color.Parse("#252528"), contentBg);
        SetBrush("BgElevatedBrush",  Color.Parse("#2C2C2E"), contentBg);
        SetBrush("BgHoverBrush",     Color.Parse("#363638"), contentBg);

        // Glass surface layers (sidebar + titlebar) — keep proportional to
        // their design-intent opacity (0xB2 = 70%) so they read as a separate
        // material layer when partially transparent.
        byte glass = enabled ? (byte)Math.Round(opacity * 0xB2) : (byte)0xB2;
        SetBrush("GlassBgBrush", Color.Parse("#1C1C1E"), glass);

        // Glass border becomes brighter when glass is active so the rim shows
        byte borderAlpha = enabled ? (byte)0x60 : (byte)0x40;
        Application.Current.Resources["GlassBorderBrush"] =
            new SolidColorBrush(new Color(borderAlpha, 0x3A, 0x3A, 0x3C));

        // Overlay widget: always semi-transparent; floor at alpha=0x80 (50%)
        byte overlayAlpha = enabled
            ? (byte)Math.Max(0x80, (int)Math.Round(opacity * 0xCC))
            : (byte)0xCC;
        Application.Current.Resources["OverlayBgBrush"] =
            new SolidColorBrush(new Color(overlayAlpha, 0x05, 0x05, 0x08));
    }

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
            main.TransparencyLevelHint = hints;

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
    /// </summary>
    private void ApplyFont(string family)
    {
        if (Application.Current?.ApplicationLifetime
                is not IClassicDesktopStyleApplicationLifetime desktop) return;

        var ff = string.IsNullOrWhiteSpace(family) || family == "(System Default)"
            ? Avalonia.Media.FontFamily.Default
            : new Avalonia.Media.FontFamily(family);

        if (desktop.MainWindow is Window main) main.FontFamily = ff;
        if (_overlayWindow      is Window ow)  ow.FontFamily   = ff;
    }

    /// <summary>
    /// Scales the base font size (14pt) of all open windows.
    /// Range: 0.75 (compact) → 1.5 (large).
    /// Also updates the <c>NxFontSm/Base/Md</c> DynamicResource tokens so every
    /// styled control (Button, TextBox, DataGrid, etc.) scales in real time.
    /// </summary>
    private void ApplyFontScale(double scale)
    {
        // ── 1. Update global font-size resource tokens ────────────────────────
        // Controls.axaml uses {DynamicResource NxFontSm/Base/Md} so writing here
        // cascades instantly to every styled control — no MainWindow needed.
        if (Application.Current is not null)
        {
            Application.Current.Resources["NxFontSm"]   = Math.Round(11.0 * scale, 1);
            Application.Current.Resources["NxFontBase"]  = Math.Round(12.0 * scale, 1);
            Application.Current.Resources["NxFontMd"]    = Math.Round(13.0 * scale, 1);
            // Per-size tokens used by all TextBlocks with explicit FontSize="X" in views —
            // replacing those with {DynamicResource NxFontX} makes them scale in real time.
            Application.Current.Resources["NxFont10"]    = Math.Round(10.0 * scale, 1);
            Application.Current.Resources["NxFont11"]    = Math.Round(11.0 * scale, 1);
            Application.Current.Resources["NxFont12"]    = Math.Round(12.0 * scale, 1);
            Application.Current.Resources["NxFont13"]    = Math.Round(13.0 * scale, 1);
            Application.Current.Resources["NxFont14"]    = Math.Round(14.0 * scale, 1);
            Application.Current.Resources["NxFont16"]    = Math.Round(16.0 * scale, 1);
        }

        // ── 2. Also set Window.FontSize for unstyled / inherited text ─────────
        if (Application.Current?.ApplicationLifetime
                is not IClassicDesktopStyleApplicationLifetime desktop) return;
        double sz = Math.Round(14.0 * scale, 1);
        if (desktop.MainWindow is Window main) main.FontSize = sz;
        if (_overlayWindow is Window ow)       ow.FontSize   = sz;
    }

    private static void SetBrush(string key, Color baseColor, byte alpha)
    {
        if (Application.Current is null) return;
        Application.Current.Resources[key] =
            new SolidColorBrush(new Color(alpha, baseColor.R, baseColor.G, baseColor.B));
    }
}
