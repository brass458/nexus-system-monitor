using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using NexusMonitor.Core.Abstractions;
using NexusMonitor.UI.Controls;
using NexusMonitor.UI.Helpers;
using NexusMonitor.UI.ViewModels;

namespace NexusMonitor.UI.Views;

public partial class DiskAnalyzerView : UserControl
{
    private readonly IShellContextMenuService? _shellContextMenu;
    private readonly ContextMenu? _fallbackMenu;
    private DataGrid? _folderTree;
    private string _fallbackPath = string.Empty;

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

        _folderTree = this.FindControl<DataGrid>("FolderTree");
        if (_folderTree is not null)
            _folderTree.PointerReleased += OnFolderTreePointerReleased;

        // Build Avalonia fallback context menu for macOS / Linux
        if (_shellContextMenu is not { IsSupported: true })
            _fallbackMenu = BuildFallbackContextMenu();
    }

    private ContextMenu BuildFallbackContextMenu()
    {
        var revealLabel = OperatingSystem.IsMacOS() ? "Reveal in Finder" : "Open in Files";

        var openItem = new MenuItem { Header = "Open" };
        openItem.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_fallbackPath))
                try { Process.Start(new ProcessStartInfo(_fallbackPath) { UseShellExecute = true }); }
                catch { }
        };

        var revealItem = new MenuItem { Header = revealLabel };
        revealItem.Click += (_, _) => ShellHelper.OpenFileLocation(_fallbackPath);

        var copyItem = new MenuItem { Header = "Copy Path" };
        copyItem.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null && !string.IsNullOrEmpty(_fallbackPath))
                await clipboard.SetTextAsync(_fallbackPath);
        };

        var menu = new ContextMenu();
        menu.Items.Add(openItem);
        menu.Items.Add(revealItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(copyItem);
        return menu;
    }

    private void OnFolderTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right) return;
        if (DataContext is not DiskAnalyzerViewModel vm) return;

        var filePath = vm.SelectedFile?.FullPath;
        if (string.IsNullOrEmpty(filePath)) return;

        if (_shellContextMenu is { IsSupported: true })
        {
            var hwnd = TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? nint.Zero;
            if (hwnd == nint.Zero) return;
            _shellContextMenu.ShowContextMenu(filePath, hwnd);
        }
        else if (_fallbackMenu is not null && _folderTree is not null)
        {
            _fallbackPath = filePath;
            _fallbackMenu.Open(_folderTree);
        }
    }
}
