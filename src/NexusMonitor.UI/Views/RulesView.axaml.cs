using Avalonia.Controls;
using Avalonia.Input;
using NexusMonitor.UI.ViewModels;

namespace NexusMonitor.UI.Views;

public partial class RulesView : UserControl
{
    public RulesView()
    {
        InitializeComponent();
    }

    // Helper to select rule row when Edit button is clicked inside the ItemTemplate
    private void OnRuleRowClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is RulesViewModel vm && sender is Button btn)
        {
            if (btn.DataContext is NexusMonitor.Core.Rules.ProcessRule rule)
                vm.SelectedRule = rule;
        }
    }

    /// <summary>
    /// Right-clicking a rule row selects that rule so context menu
    /// Edit/Delete commands operate on the correct item.
    /// </summary>
    private void OnRuleBorderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsRightButtonPressed) return;
        if (DataContext is RulesViewModel vm && sender is Control ctrl
            && ctrl.DataContext is NexusMonitor.Core.Rules.ProcessRule rule)
        {
            vm.SelectedRule = rule;
        }
    }
}
