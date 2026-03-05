using NexusMonitor.Core.Alerts;
using NexusMonitor.Core.Rules;

namespace NexusMonitor.Core.Models;

public class AppSettings
{
    public bool   IsDarkTheme        { get; set; } = true;   // kept for migration only
    /// <summary>"System" | "Dark" | "Light"</summary>
    public string ThemeMode          { get; set; } = "System";

    // Crystal Glass
    public bool   IsGlassEnabled     { get; set; } = true;
    public double GlassOpacity       { get; set; } = 0.80;   // 0 = fully transparent, 1 = fully opaque
    public string BackdropBlurMode   { get; set; } = "Acrylic"; // None | Blur | Acrylic | Mica
    public bool   IsSpecularEnabled  { get; set; } = true;
    public double SpecularIntensity  { get; set; } = 0.55;   // default raised for visibility

    // Accent
    public string AccentColorHex     { get; set; } = "#0A84FF";
    public string TextAccentColorHex { get; set; } = "";     // "" = derive from AccentColorHex

    // Custom surface colors — "" = use built-in theme defaults
    public string CustomWindowBgHex  { get; set; } = "";   // BgBaseBrush  (window chrome)
    public string CustomSurfaceBgHex { get; set; } = "";   // BgPrimary/Secondary/Elevated (cards/panels)
    public string CustomSidebarBgHex { get; set; } = "";   // GlassBgBrush (left sidebar / nav)

    // Typography
    public string FontFamily         { get; set; } = "";     // "" = system default
    public double FontSizeMultiplier { get; set; } = 1.0;   // 1.0 = default size

    // Performance
    public int    UpdateIntervalMs   { get; set; } = 2000;   // 500 | 1000 | 2000 | 5000

    // Tray / window close behaviour
    /// <summary>"" = always ask, "Tray" = minimize to tray, "Exit" = close application.</summary>
    public string CloseAction          { get; set; } = "";
    public bool   HideWidgetOnMinimize { get; set; } = false;

    // Other
    public bool   ShowOverlayWidget  { get; set; } = false;

    // Notifications
    public bool   DesktopNotificationsEnabled  { get; set; } = false;
    public bool   AnomalyNotificationsEnabled  { get; set; } = false;

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

    // Sidebar navigation order — empty = default order
    public List<string> NavOrder { get; set; } = new();

    // Metrics persistence (Phase 11)
    public bool MetricsEnabled           { get; set; } = false;
    public int  MetricsTopNProcesses     { get; set; } = 15;
    public bool MetricsRecordNetwork     { get; set; } = true;
    public int  MetricsRawRetentionHours { get; set; } = 1;
    public int  MetricsRollup1mDays      { get; set; } = 7;
    public int  MetricsRollup5mDays      { get; set; } = 30;
    public int  MetricsRollup1hDays      { get; set; } = 365;

    // Telemetry — Prometheus endpoint (Phase 14)
    public bool PrometheusEnabled { get; set; } = false;
    public int  PrometheusPort    { get; set; } = 9182;

    // Smart Glass Tint (Phase 4 enhancements)
    public bool SmartTintEnabled { get; set; } = false;

    // Dashboard (Phase 1)
    public bool DashboardEnabled { get; set; } = true;

    // Theme Presets
    public string ActiveThemePresetId { get; set; } = "";  // "" = custom/none

    // Anomaly Detection (Phase 13)
    public bool   AnomalyDetectionEnabled     { get; set; } = false;
    /// <summary>"Low", "Medium", or "High" — maps to sigma preset in AnomalyDetectionConfig.</summary>
    public string AnomalySensitivity          { get; set; } = "Low";
    public int    AnomalyCooldownSeconds      { get; set; } = 60;
    public int    AnomalyNewConnGracePeriodSec{ get; set; } = 120;
    public int    MetricsEventsRetentionDays  { get; set; } = 90;
}
