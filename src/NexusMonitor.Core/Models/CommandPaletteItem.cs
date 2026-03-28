using CommunityToolkit.Mvvm.ComponentModel;

namespace NexusMonitor.Core.Models;

/// <summary>
/// A single entry in the Command Palette — either a navigation action or a mode toggle.
/// </summary>
public partial class CommandPaletteItem : ObservableObject
{
    public string Label { get; }
    public string Icon { get; }          // NexusIcons glyph char
    public string Category { get; }      // "Navigate" | "Toggle" | "Theme"
    public Action Execute { get; }

    [ObservableProperty]
    private string? _stateLabel;         // "ON" / "OFF" / "ACTIVE" / null

    [ObservableProperty]
    private bool _isSelected;

    public CommandPaletteItem(string label, string icon, string category, Action execute, string? stateLabel = null)
    {
        Label = label;
        Icon = icon;
        Category = category;
        Execute = execute;
        _stateLabel = stateLabel;
    }
}
