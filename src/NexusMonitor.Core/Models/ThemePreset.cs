namespace NexusMonitor.Core.Models;

public class ThemePreset
{
    public string Id          { get; set; } = "";
    public string Name        { get; set; } = "";
    public bool   IsBuiltIn   { get; set; } = true;

    public string ThemeMode          { get; set; } = "Dark";
    public string AccentColorHex     { get; set; } = "#0A84FF";
    public string TextAccentColorHex { get; set; } = "";
    public string CustomWindowBgHex  { get; set; } = "";
    public string CustomSurfaceBgHex { get; set; } = "";
    public string CustomSidebarBgHex { get; set; } = "";
    public bool   IsGlassEnabled     { get; set; } = true;
    public double GlassOpacity       { get; set; } = 0.80;
    public string BackdropBlurMode   { get; set; } = "Acrylic";
    public bool   IsSpecularEnabled  { get; set; } = true;
    public double SpecularIntensity  { get; set; } = 0.55;
    public string FontFamily         { get; set; } = "";
    public double FontSizeMultiplier { get; set; } = 1.0;
}
