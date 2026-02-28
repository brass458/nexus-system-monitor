using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace NexusMonitor.UI.Views;

public partial class FindWindowOverlay : Window
{
    private readonly DispatcherTimer _pollTimer;
    private int _hoveredPid;
    private string _hoveredProcessName = "";

    /// <summary>The PID the user selected (0 if cancelled).</summary>
    public int SelectedPid { get; private set; }

    public FindWindowOverlay()
    {
        InitializeComponent();

        // Poll the mouse position at 60 Hz to identify the window under the cursor
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _pollTimer.Tick += OnPollTick;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _pollTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _pollTimer.Stop();
        base.OnClosed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SelectedPid = 0;
            Close(0);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_hoveredPid > 0)
        {
            SelectedPid = _hoveredPid;
            Close(_hoveredPid);
        }
        else
        {
            SelectedPid = 0;
            Close(0);
        }
    }

    private void OnPollTick(object? sender, EventArgs e)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        try
        {
            if (!GetCursorPos(out var pt)) return;

            // Temporarily hide ourselves so WindowFromPoint hits the real window behind
            nint hwnd = WindowFromPoint(pt);
            if (hwnd == nint.Zero)
            {
                UpdateInfo(0, "");
                return;
            }

            // Get the root owner window for better identification
            nint root = GetAncestor(hwnd, 3 /* GA_ROOTOWNER */);
            if (root != nint.Zero) hwnd = root;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == (uint)Environment.ProcessId)
            {
                UpdateInfo(0, "(Nexus Monitor)");
                return;
            }

            string name = "";
            try
            {
                var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                name = proc.ProcessName;
            }
            catch { }

            UpdateInfo((int)pid, name);
        }
        catch { }
    }

    private void UpdateInfo(int pid, string processName)
    {
        if (pid == _hoveredPid) return;
        _hoveredPid = pid;
        _hoveredProcessName = processName;

        if (pid > 0)
        {
            InfoText.Text = $"{processName}";
            PidText.Text = $"PID {pid} — click to select";
        }
        else
        {
            InfoText.Text = "Click on any window to identify its process";
            PidText.Text = processName;
        }
    }

    // ── P/Invoke ─────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(POINT pt);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint gaFlags);
}
