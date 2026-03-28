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

    /// <summary>
    /// Optional delegate called by <see cref="NexusMonitor.Core.ViewModels.CommandPaletteViewModel.RefreshToggleStates"/>
    /// to re-derive the current state badge from live settings.
    /// Returns "ON" / "OFF" / "ACTIVE" / null.
    /// </summary>
    internal Func<string?>? StateRefresher { get; set; }

    public CommandPaletteItem(string label, string icon, string category, Action execute, string? stateLabel = null)
    {
        Label = label;
        Icon = icon;
        Category = category;
        Execute = execute;
        _stateLabel = stateLabel;
    }
}
