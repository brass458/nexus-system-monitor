using Avalonia.Controls;
using Avalonia.Interactivity;
using NexusMonitor.Core.Alerts;
using NexusMonitor.UI.ViewModels;

namespace NexusMonitor.UI.Views;

public partial class AlertsView : UserControl
{
    public AlertsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Clicking anywhere on a rule row selects that rule and opens the editor.
    /// </summary>
    private void OnRuleRowClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AlertsViewModel vm) return;

        // Walk up the visual tree to find the AlertRule bound to this row
        var btn  = sender as Button;
        if (btn?.DataContext is AlertRule rule)
        {
            vm.SelectedRule = rule;
        }
    }
}
