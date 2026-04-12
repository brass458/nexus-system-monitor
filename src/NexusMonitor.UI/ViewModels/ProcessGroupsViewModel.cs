using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.Storage;

namespace NexusMonitor.UI.ViewModels;

public partial class ProcessGroupsViewModel : ViewModelBase
{
    private readonly ProcessGroupStore _store;
    private Guid? _editId;

    // ── Group list ─────────────────────────────────────────────────────────────
    public ObservableCollection<ProcessGroup> Groups { get; } = [];

    // ── Editor state ──────────────────────────────────────────────────────────
    [ObservableProperty] private bool   _isEditorVisible;
    [ObservableProperty] private string _editorTitle  = "New Group";
    [ObservableProperty] private string _editName     = "";
    [ObservableProperty] private string _editColor    = "#5B9BD5";
    [ObservableProperty] private string _editPatterns = "";

    // ── Validation ────────────────────────────────────────────────────────────
    [ObservableProperty] private bool   _hasValidationError;
    [ObservableProperty] private string _validationMessage = "";

    // ── Constructor ───────────────────────────────────────────────────────────
    public ProcessGroupsViewModel(ProcessGroupStore store)
    {
        Title  = "Process Groups";
        _store = store;
        LoadGroups();
    }

    // ── List management ───────────────────────────────────────────────────────
    private void LoadGroups()
    {
        Groups.Clear();
        foreach (var g in _store.GetAll())
            Groups.Add(g);
    }

    [RelayCommand]
    private void NewGroup()
    {
        _editId          = null;
        EditorTitle      = "New Group";
        EditName         = "";
        EditColor        = "#5B9BD5";
        EditPatterns     = "";
        HasValidationError = false;
        ValidationMessage  = "";
        IsEditorVisible  = true;
    }

    [RelayCommand]
    private void EditGroup(ProcessGroup group)
    {
        _editId          = group.Id;
        EditorTitle      = "Edit Group";
        EditName         = group.Name;
        EditColor        = group.Color;
        EditPatterns     = string.Join("\n", group.Patterns);
        HasValidationError = false;
        ValidationMessage  = "";
        IsEditorVisible  = true;
    }

    [RelayCommand]
    private void SaveGroup()
    {
        HasValidationError = false;
        ValidationMessage  = "";

        if (string.IsNullOrWhiteSpace(EditName))
        {
            HasValidationError = true;
            ValidationMessage  = "Name is required.";
            return;
        }

        var patterns = EditPatternsToList();
        if (patterns.Count == 0)
        {
            HasValidationError = true;
            ValidationMessage  = "At least one pattern is required.";
            return;
        }

        if (_editId.HasValue)
        {
            // Update existing
            var existing = _store.Get(_editId.Value);
            if (existing is null)
            {
                HasValidationError = true;
                ValidationMessage  = "This group was deleted before saving. Please create a new group.";
                LoadGroups();
                IsEditorVisible = false;
                return;
            }

            existing.Name     = EditName.Trim();
            existing.Color    = string.IsNullOrWhiteSpace(EditColor) ? "#5B9BD5" : EditColor.Trim();
            existing.Patterns = patterns;
            _store.Upsert(existing);
        }
        else
        {
            // Create new
            var group = new ProcessGroup
            {
                Id       = Guid.NewGuid(),
                Name     = EditName.Trim(),
                Color    = string.IsNullOrWhiteSpace(EditColor) ? "#5B9BD5" : EditColor.Trim(),
                Patterns = patterns,
            };
            _store.Upsert(group);
        }

        LoadGroups();
        IsEditorVisible = false;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditorVisible    = false;
        HasValidationError = false;
        ValidationMessage  = "";
    }

    [RelayCommand]
    private void DeleteGroup(ProcessGroup group)
    {
        _store.Delete(group.Id);
        Groups.Remove(group);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    public List<string> EditPatternsToList() =>
        EditPatterns
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();
}
