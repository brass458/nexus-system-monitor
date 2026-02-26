using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace NexusMonitor.UI.Views;

public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();

        // TransparencyLevelHint is managed by SettingsViewModel.ApplyBackdropMode.
        // IMPORTANT: always end with Transparent (never None) so that pixels outside
        // the CornerRadius="14" border are genuinely cut out by the compositor.
        // "None" falls back to an opaque rectangular OS window, making corners appear square.
        TransparencyLevelHint =
        [
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Blur,
            WindowTransparencyLevel.Transparent,  // ← per-pixel alpha; clips corners correctly
        ];

        // Position at the bottom-right corner of the primary screen's working area
        // (below taskbar / above dock). We wait for the layout pass so Width/Height are final.
        Opened += (_, _) =>
        {
            var wa = Screens.Primary?.WorkingArea;
            if (wa is not null)
                Position = new PixelPoint(
                    wa.Value.Right  - (int)(Bounds.Width  + 16),
                    wa.Value.Bottom - (int)(Bounds.Height + 16));
        };
    }

    private void OnDrag(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }
}
