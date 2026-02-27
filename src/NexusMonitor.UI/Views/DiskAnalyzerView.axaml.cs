using Avalonia.Controls;
using NexusMonitor.UI.Controls;
using NexusMonitor.UI.ViewModels;

namespace NexusMonitor.UI.Views;

public partial class DiskAnalyzerView : UserControl
{
    public DiskAnalyzerView()
    {
        InitializeComponent();

        var treemap = this.FindControl<TreemapControl>("TheTreemap");
        if (treemap is not null)
        {
            treemap.NodeClicked += node =>
            {
                if (DataContext is DiskAnalyzerViewModel vm)
                    vm.TreemapNodeClickedCommand.Execute(node);
            };
        }
    }
}
