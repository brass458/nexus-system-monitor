namespace NexusMonitor.Core.Models;

public class AppSettings
{
    public bool   IsDarkTheme        { get; set; } = true;
    public bool   IsAeroGlassEnabled { get; set; } = false;
    public double WindowOpacity      { get; set; } = 0.92;
    public string AccentColorHex     { get; set; } = "#0A84FF";
    public bool   ShowOverlayWidget  { get; set; } = false;
}
