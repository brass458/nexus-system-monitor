using Avalonia.Controls;
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
        if (DataContext is RulesViewModel vm && sender is Avalonia.Controls.Button btn)
        {
            if (btn.DataContext is NexusMonitor.Core.Rules.ProcessRule rule)
                vm.SelectedRule = rule;
        }
    }
}
