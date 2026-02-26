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

    // Other
    public bool   ShowOverlayWidget  { get; set; } = false;
}
