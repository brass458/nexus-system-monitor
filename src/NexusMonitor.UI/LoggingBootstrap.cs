using Serilog;
using Serilog.Events;

namespace NexusMonitor.UI;

/// <summary>
/// Configures Serilog with a rolling file sink before the Avalonia app starts.
/// Log files are written to %AppData%\NexusMonitor\logs\nexus-.log (daily roll, 10 MB cap).
/// </summary>
internal static class LoggingBootstrap
{
    private static readonly string LogDirectory =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NexusMonitor", "logs");

    /// <summary>
    /// Initializes Serilog. Must be called before DI is built so that the static
    /// <see cref="Log.Logger"/> is available to any code that resolves ILogger&lt;T&gt;.
    /// Call <see cref="CloseAndFlush"/> on application shutdown.
    /// </summary>
    public static void Initialize()
    {
        Directory.CreateDirectory(LogDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Avalonia",  LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                path:             Path.Combine(LogDirectory, "nexus-.log"),
                rollingInterval:  RollingInterval.Day,
                fileSizeLimitBytes: 10 * 1024 * 1024,   // 10 MB per file
                retainedFileCountLimit: 7,               // keep 1 week
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("Nexus System Monitor starting — logs at {LogDirectory}", LogDirectory);
    }

    /// <summary>Flushes and closes Serilog on application exit.</summary>
    public static void CloseAndFlush() => Log.CloseAndFlush();
}
