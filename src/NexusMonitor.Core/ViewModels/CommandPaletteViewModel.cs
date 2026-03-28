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
    {
        _allItems.AddRange(items);
    }

    public void Open()
    {
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
