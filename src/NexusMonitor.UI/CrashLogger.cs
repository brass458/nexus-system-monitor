using System.Text;

namespace NexusMonitor.UI;

/// <summary>
/// Appends structured crash reports to %AppData%\NexusMonitor\crash.log.
/// All methods are non-throwing — if logging itself fails, the error is silently swallowed
/// so that crash-handler code never masks the original exception.
/// </summary>
internal static class CrashLogger
{
    private static readonly string LogDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NexusMonitor");

    public static string LogPath { get; } = Path.Combine(LogDirectory, "crash.log");

    // Keep the file under ~200 KB; trim aggressively when exceeded
    private const long MaxLogBytes = 200 * 1024;

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Appends a crash report for <paramref name="ex"/> to crash.log.</summary>
    /// <param name="ex">The exception that was thrown.</param>
    /// <param name="context">
    /// Short label describing where the exception originated, e.g.
    /// "Startup", "AppDomain.UnhandledException", "UI Thread".
    /// </param>
    public static void Write(Exception ex, string context = "Runtime")
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            TrimIfNeeded();

            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine($"  Nexus Monitor — Crash Report");
            sb.AppendLine($"  Timestamp : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz}");
            sb.AppendLine($"  Context   : {context}");
            sb.AppendLine($"  OS        : {Environment.OSVersion}");
            sb.AppendLine($"  CLR       : {Environment.Version}");
            sb.AppendLine($"  Platform  : {(Environment.Is64BitProcess ? "x64" : "x86")}");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            AppendException(sb, ex, depth: 0);
            sb.AppendLine();

            File.AppendAllText(LogPath, sb.ToString());
        }
        catch
        {
            // Never throw from a crash handler
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void AppendException(StringBuilder sb, Exception ex, int depth)
    {
        string pad = new(' ', depth * 2);

        sb.AppendLine($"{pad}Type    : {ex.GetType().FullName}");
        sb.AppendLine($"{pad}Message : {ex.Message}");

        if (!string.IsNullOrWhiteSpace(ex.StackTrace))
        {
            sb.AppendLine($"{pad}Stack Trace:");
            foreach (var line in ex.StackTrace.Split('\n'))
            {
                string trimmed = line.TrimEnd();
                if (!string.IsNullOrEmpty(trimmed))
                    sb.AppendLine($"{pad}  {trimmed}");
            }
        }

        // AggregateException: unroll inner list first so each sub-exception is labelled
        if (ex is AggregateException agg)
        {
            for (int i = 0; i < agg.InnerExceptions.Count; i++)
            {
                sb.AppendLine($"{pad}--- AggregateException inner [{i}] ---");
                AppendException(sb, agg.InnerExceptions[i], depth + 1);
            }
        }
        else if (ex.InnerException is not null)
        {
            sb.AppendLine($"{pad}--- Inner Exception ---");
            AppendException(sb, ex.InnerException, depth + 1);
        }
    }

    /// <summary>
    /// If the log file exceeds <see cref="MaxLogBytes"/>, discards the older half so the
    /// file never grows unbounded while still keeping recent history.
    /// </summary>
    private static void TrimIfNeeded()
    {
        try
        {
            var fi = new FileInfo(LogPath);
            if (!fi.Exists || fi.Length < MaxLogBytes) return;

            string content = File.ReadAllText(LogPath);
            // Drop the first half — crude but avoids loading large files twice
            File.WriteAllText(LogPath, content[(content.Length / 2)..]);
        }
        catch { }
    }
}
