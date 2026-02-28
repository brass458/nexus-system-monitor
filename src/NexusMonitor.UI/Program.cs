using Avalonia;
using Avalonia.Fonts.Inter;
using Avalonia.ReactiveUI;

namespace NexusMonitor.UI;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // ── Catch-all for unhandled exceptions on any thread ────────────────
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                CrashLogger.Write(ex,
                    $"AppDomain.UnhandledException (IsTerminating={e.IsTerminating})");
        };

        // ── Catch unobserved Task exceptions (background async failures) ────
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLogger.Write(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved();   // prevents process termination for non-fatal async faults
        };

        // ── Wrap the entire Avalonia lifetime so startup failures are logged ─
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            CrashLogger.Write(ex, "Startup — BuildAvaloniaApp / StartWithClassicDesktopLifetime");
            throw;   // re-throw so the OS / debugger still sees it
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
                     .UsePlatformDetect()
                     .WithInterFont()
                     .LogToTrace()
                     .UseReactiveUI();
}
