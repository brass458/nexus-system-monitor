using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NexusMonitor.Core.Models;

namespace NexusMonitor.Core.ViewModels;

/// <summary>
/// ViewModel for the Command Palette overlay.
/// Pure C# — no Avalonia dependencies. Testable from NexusMonitor.Core.Tests.
/// </summary>
public partial class CommandPaletteViewModel : ObservableObject
{
    protected readonly List<CommandPaletteItem> _allItems = new();

    private readonly AppSettings? _settings;
    private readonly Action? _onSave;
    private readonly Action<string>? _onThemeChanged;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _selectedIndex;

    [ObservableProperty]
    private bool _isOpen;

    public ObservableCollection<CommandPaletteItem> FilteredItems { get; } = new();

    /// <summary>
    /// Constructs the ViewModel with a pre-built list of command items.
    /// Items are built by the UI layer (MainWindow) from NavItems + toggle definitions.
    /// </summary>
    public CommandPaletteViewModel(IReadOnlyList<CommandPaletteItem> items)
        : this(items, settings: null, onSave: null, onThemeChanged: null)
    {
    }

    /// <summary>
    /// Constructs the ViewModel with settings wired in for toggle and theme commands.
    /// </summary>
    /// <param name="items">Pre-built palette items (Navigate / Toggle / Theme).</param>
    /// <param name="settings">Live AppSettings instance; null = no toggle/theme state tracking.</param>
    /// <param name="onSave">Called after each toggle/theme execute (e.g. settingsService.Save()).</param>
    /// <param name="onThemeChanged">Called with the new ThemeMode string when a theme item is executed.</param>
    public CommandPaletteViewModel(
        IReadOnlyList<CommandPaletteItem> items,
        AppSettings? settings,
        Action? onSave,
        Action<string>? onThemeChanged)
    {
        _settings = settings;
        _onSave = onSave;
        _onThemeChanged = onThemeChanged;
        _allItems.AddRange(items);
    }

    public void Open()
    {
        RefreshToggleStates();
        SearchText = string.Empty;
        RefreshFilteredItems();
        SelectedIndex = 0;
        IsOpen = true;
    }

    public void Close()
    {
        IsOpen = false;
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    /// <summary>
    /// Re-derives StateLabel for every item that has a StateRefresher delegate.
    /// Called at the start of Open() so badges always reflect current settings.
    /// </summary>
    public void RefreshToggleStates()
    {
        foreach (var item in _allItems.Where(i => i.StateRefresher != null))
            item.StateLabel = item.StateRefresher!();
    }

    /// <summary>
    /// Factory helper — creates a Toggle palette item wired to a bool on AppSettings.
    /// </summary>
    /// <param name="label">Display name (e.g. "Gaming Mode").</param>
    /// <param name="icon">NexusIcons glyph char.</param>
    /// <param name="getState">Reads current bool value from settings.</param>
    /// <param name="setState">Writes new bool value to settings.</param>
    public CommandPaletteItem MakeToggle(
        string label,
        string icon,
        Func<bool> getState,
        Action<bool> setState)
        => MakeToggle(label, icon, getState, setState, _onSave);

    /// <summary>
    /// Static factory helper — creates a Toggle palette item wired to a bool getter/setter.
    /// Can be called before a CommandPaletteViewModel instance is created.
    /// </summary>
    public static CommandPaletteItem MakeToggle(
        string label,
        string icon,
        Func<bool> getState,
        Action<bool> setState,
        Action? onSave)
    {
        var item = new CommandPaletteItem(
            label, icon, "Toggle",
            execute: () =>
            {
                setState(!getState());
                onSave?.Invoke();
            },
            stateLabel: getState() ? "ON" : "OFF");

        item.StateRefresher = () => getState() ? "ON" : "OFF";
        return item;
    }

    /// <summary>
    /// Factory helper — creates a Theme palette item for a specific ThemeMode value.
    /// </summary>
    /// <param name="label">Display name (e.g. "Dark Theme").</param>
    /// <param name="icon">NexusIcons glyph char.</param>
    /// <param name="modeValue">"Dark" | "Light" | "System"</param>
    public CommandPaletteItem MakeTheme(
        string label,
        string icon,
        string modeValue)
        => MakeTheme(label, icon, modeValue, _settings, _onSave, _onThemeChanged);

    /// <summary>
    /// Static factory helper — creates a Theme palette item for a specific ThemeMode value.
    /// Can be called before a CommandPaletteViewModel instance is created.
    /// </summary>
    public static CommandPaletteItem MakeTheme(
        string label,
        string icon,
        string modeValue,
        AppSettings? settings,
        Action? onSave,
        Action<string>? onThemeChanged)
    {
        var item = new CommandPaletteItem(
            label, icon, "Theme",
            execute: () =>
            {
                if (settings != null)
                    settings.ThemeMode = modeValue;
                onSave?.Invoke();
                onThemeChanged?.Invoke(modeValue);
            },
            stateLabel: null);

        item.StateRefresher = () =>
            settings != null
            && string.Equals(settings.ThemeMode, modeValue, StringComparison.OrdinalIgnoreCase)
                ? "ACTIVE"
                : null;

        return item;
    }

    /// <summary>Populates FilteredItems from _allItems based on SearchText filter.</summary>
    protected virtual void RefreshFilteredItems()
    {
        // Reset IsSelected on all items before clearing
        foreach (var item in _allItems)
            item.IsSelected = false;

        FilteredItems.Clear();
        var term = SearchText?.Trim() ?? string.Empty;
        foreach (var item in _allItems)
        {
            if (term.Length == 0
                || item.Label.Contains(term, StringComparison.OrdinalIgnoreCase)
                || item.Category.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                FilteredItems.Add(item);
            }
        }
        SelectedIndex = 0;
        // Ensure first item is marked as selected
        if (FilteredItems.Count > 0)
            FilteredItems[0].IsSelected = true;
    }

    /// <summary>Moves selection by delta, clamping to valid range.</summary>
    public void MoveSelection(int delta)
    {
        if (FilteredItems.Count == 0) return;
        var newIndex = SelectedIndex + delta;
        SelectedIndex = Math.Clamp(newIndex, 0, FilteredItems.Count - 1);
    }

    /// <summary>Executes the selected item action and closes the palette.</summary>
    public void ExecuteSelected()
    {
        if (SelectedIndex >= 0 && SelectedIndex < FilteredItems.Count)
        {
            FilteredItems[SelectedIndex].Execute();
            Close();
        }
    }

    /// <summary>Called by CommunityToolkit.Mvvm when SelectedIndex changes.</summary>
    partial void OnSelectedIndexChanged(int oldValue, int newValue)
    {
        // Update IsSelected on items for visual highlight
        if (oldValue >= 0 && oldValue < FilteredItems.Count)
            FilteredItems[oldValue].IsSelected = false;
        if (newValue >= 0 && newValue < FilteredItems.Count)
            FilteredItems[newValue].IsSelected = true;
    }

    /// <summary>Called by CommunityToolkit.Mvvm when SearchText changes.</summary>
    partial void OnSearchTextChanged(string value) => RefreshFilteredItems();
}
