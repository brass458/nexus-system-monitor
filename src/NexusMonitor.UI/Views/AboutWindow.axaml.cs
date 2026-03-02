using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NexusMonitor.UI.Helpers;

namespace NexusMonitor.UI.Views;

public partial class AboutWindow : Window
{
    // Parameterless constructor required by Avalonia's XAML runtime loader (AVLN3001)
    public AboutWindow()
    {
        InitializeComponent();

        var asm = Assembly.GetExecutingAssembly();
        var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                         ?.InformationalVersion
                      ?? asm.GetName().Version?.ToString(3)
                      ?? "0.1.0";

        // Strip any build metadata suffix (e.g. "0.1.0+abc123" → "0.1.0")
        var plusIndex = version.IndexOf('+');
        if (plusIndex >= 0)
            version = version[..plusIndex];

        VersionText.Text = $"Version {version}";
    }

    private void OnGitHubClick(object? sender, RoutedEventArgs e) =>
        ShellHelper.Launch("https://github.com/brass458/nexus-system-monitor");

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
