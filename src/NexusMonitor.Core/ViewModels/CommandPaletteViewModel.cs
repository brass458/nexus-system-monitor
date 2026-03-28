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

    /// <summary>Populates FilteredItems from _allItems. Overridden in Task 3 to add filter logic.</summary>
    protected virtual void RefreshFilteredItems()
    {
        FilteredItems.Clear();
        foreach (var item in _allItems)
            FilteredItems.Add(item);
        SelectedIndex = 0;
    }
}
