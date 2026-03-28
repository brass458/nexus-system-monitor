using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NexusMonitor.Core.Models;
using NexusMonitor.Core.ViewModels;

namespace NexusMonitor.UI.Controls;

/// <summary>
/// Full-screen glass overlay that renders the Command Palette.
/// Visibility is driven by <see cref="CommandPaletteViewModel.IsOpen"/>.
/// Keyboard routing (Up/Down/Enter/Escape) is handled in code-behind;
/// backdrop click dismisses the palette.
/// </summary>
public partial class CommandPaletteControl : UserControl
{
    public CommandPaletteControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Auto-focus the SearchBox whenever the control becomes visible
    /// so the user can type immediately without an extra click.
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && IsVisible)
        {
            Dispatcher.UIThread.Post(
                () => this.FindControl<TextBox>("SearchBox")?.Focus(),
                DispatcherPriority.Input);
        }
    }

    private void OnBackdropPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        (DataContext as CommandPaletteViewModel)?.Close();
        e.Handled = true;
    }

    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // Stop the pointer event reaching the backdrop border so it doesn't close the palette.
        e.Handled = true;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        var vm = DataContext as CommandPaletteViewModel;
        if (vm is null) return;

        switch (e.Key)
        {
            case Key.Down:
                vm.MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                vm.MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                vm.ExecuteSelected();
                e.Handled = true;
                break;
            case Key.Escape:
                vm.Close();
                e.Handled = true;
                break;
        }
    }

    private void OnItemButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is CommandPaletteItem item)
        {
            item.Execute();
            (DataContext as CommandPaletteViewModel)?.Close();
        }
    }
}
