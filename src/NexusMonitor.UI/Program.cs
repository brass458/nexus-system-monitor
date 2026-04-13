using Avalonia;
using Avalonia.Fonts.Inter;
using Avalonia.ReactiveUI;
using Serilog;

namespace NexusMonitor.UI;

internal sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // ── Cap ThreadPool — default min = ProcessorCount (16 on Ryzen 5700X3D) which
        //    pre-commits 16 thread stacks unnecessarily. The shared multicast Rx pattern
        //    means the app never needs more than 4 concurrent workers at steady state.
        System.Threading.ThreadPool.SetMinThreads(4, 4);
        System.Threading.ThreadPool.SetMaxThreads(32, 16);

        // ── Initialize Serilog before anything else ─────────────────────────
        LoggingBootstrap.Initialize();

        // ── Catch-all for unhandled exceptions on any thread ────────────────
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Log.Fatal(ex, "Unhandled exception (IsTerminating={IsTerminating})", e.IsTerminating);
                CrashLogger.Write(ex,
                    $"AppDomain.UnhandledException (IsTerminating={e.IsTerminating})");
            }
        };

        // ── Catch unobserved Task exceptions (background async failures) ────
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Warning(e.Exception, "Unobserved task exception");
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
            Log.Fatal(ex, "Fatal error during startup");
            CrashLogger.Write(ex, "Startup — BuildAvaloniaApp / StartWithClassicDesktopLifetime");
            throw;   // re-throw so the OS / debugger still sees it
        }
        finally
        {
            LoggingBootstrap.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
                     .UsePlatformDetect()
                     .WithInterFont()
                     .LogToTrace()
                     .UseReactiveUI();
}
