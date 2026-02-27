using NexusMonitor.Core.Alerts;
using NexusMonitor.Core.Rules;

namespace NexusMonitor.Core.Models;

public class AppSettings
{
    public bool   IsDarkTheme        { get; set; } = true;

    // Liquid Glass
    public bool   IsGlassEnabled     { get; set; } = false;
    public double GlassOpacity       { get; set; } = 0.80;   // 0 = fully transparent, 1 = fully opaque
    public string BackdropBlurMode   { get; set; } = "Acrylic"; // None | Blur | Acrylic | Mica
    public bool   IsSpecularEnabled  { get; set; } = true;
    public double SpecularIntensity  { get; set; } = 0.55;   // default raised for visibility

    // Accent
    public string AccentColorHex     { get; set; } = "#0A84FF";
    public string TextAccentColorHex { get; set; } = "";     // "" = derive from AccentColorHex

    // Typography
    public string FontFamily         { get; set; } = "";     // "" = system default

    // Performance
    public int    UpdateIntervalMs   { get; set; } = 1000;   // 500 | 1000 | 2000 | 5000

    // Tray / window close behaviour
    /// <summary>"" = always ask, "Tray" = minimize to tray, "Exit" = close application.</summary>
    public string CloseAction          { get; set; } = "";
    public bool   HideWidgetOnMinimize { get; set; } = false;

    // Other
    public bool   ShowOverlayWidget  { get; set; } = false;

    // ProBalance
    public bool          ProBalanceEnabled      { get; set; } = false;
    public double        ProBalanceCpuThreshold { get; set; } = 80.0;
    public List<string>  ProBalanceExclusions   { get; set; } = new();

    // Rules
    public List<ProcessRule> Rules { get; set; } = new();

    // Gaming Mode
    public bool          GamingModeEnabled     { get; set; } = false;
    public string        GamingModeGameProcess { get; set; } = "";
    public List<string>  GamingModeExclusions  { get; set; } = new();

    // Alerts
    public List<AlertRule> AlertRules { get; set; } = new();
}
