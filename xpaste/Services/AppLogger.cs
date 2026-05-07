using System.IO;

namespace xpaste.Services;

/// <summary>
/// Lightweight file-based logger that writes timestamped entries to
/// <c>%AppData%\xpaste\xpaste.log</c>.
/// The log file is automatically rotated when it exceeds 1 MB.
/// <para>
/// <b>Privacy:</b> This logger must never record snippet content or passwords.
/// Use <c>[REDACTED]</c> placeholders and only log character counts when needed for diagnostics.
/// </para>
/// </summary>
public static class AppLogger
{
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "xpaste", "xpaste.log");

    private static readonly object _lock = new();
    private const long MaxBytes = 1_000_000; // 1 MB

    /// <summary>Logs an informational message.</summary>
    public static void Info(string message)  => Write("INFO ", message);

    /// <summary>Logs a warning message.</summary>
    public static void Warn(string message)  => Write("WARN ", message);

    /// <summary>Logs an error message, optionally including exception type and message (never a stack trace with sensitive data).</summary>
    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex == null ? message : $"{message} | {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (_lock)
            {
                var dir = Path.GetDirectoryName(LogFile)!;
                Directory.CreateDirectory(dir);

                // Rotate if too large
                if (File.Exists(LogFile) && new FileInfo(LogFile).Length > MaxBytes)
                    File.Move(LogFile, LogFile + ".old", overwrite: true);

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                File.AppendAllText(LogFile, line);
            }
        }
        catch { /* logging must never crash the app */ }
    }
}
