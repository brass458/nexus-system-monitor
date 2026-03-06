using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.UI.Controls;
using NexusMonitor.UI.ViewModels;

namespace NexusMonitor.UI.Views;

public partial class DiskAnalyzerView : UserControl
{
    private readonly IShellContextMenuService? _shellContextMenu;

    public DiskAnalyzerView()
    {
        InitializeComponent();

        _shellContextMenu = App.Services.GetService<IShellContextMenuService>();

        var treemap = this.FindControl<TreemapControl>("TheTreemap");
        if (treemap is not null)
        {
            treemap.NodeClicked += node =>
            {
                if (DataContext is DiskAnalyzerViewModel vm)
                    vm.TreemapNodeClickedCommand.Execute(node);
            };
        }

        var folderTree = this.FindControl<DataGrid>("FolderTree");
        if (folderTree is not null)
        {
            folderTree.PointerReleased += OnFolderTreePointerReleased;
        }
    }

    private void OnFolderTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right) return;
        if (_shellContextMenu is not { IsSupported: true }) return;
        if (DataContext is not DiskAnalyzerViewModel vm) return;

        var filePath = vm.SelectedFile?.FullPath;
        if (string.IsNullOrEmpty(filePath)) return;

        var hwnd = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (hwnd == nint.Zero) return;

        _shellContextMenu.ShowContextMenu(filePath, hwnd);
    }
}
