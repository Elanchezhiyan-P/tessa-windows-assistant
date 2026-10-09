using System.Reflection;
using System.Text;

namespace WinCompanion.Services;

/// <summary>
/// Writes unexpected errors to %AppData%\WinCompanion\crash.log so a failure leaves a trail instead of vanishing.
/// Only the error and basic environment details are written, never your chat or settings.
/// </summary>
public static class CrashLog
{
    private const long MaxBytes = 1_000_000; // start over rather than grow forever
    private static readonly object Gate = new();

    public static string FilePath => AppPaths.FileIn("crash.log");

    public static void Write(string where, Exception? error)
    {
        try
        {
            var text = new StringBuilder()
                .AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where}")
                .AppendLine($"Tessa {Assembly.GetExecutingAssembly().GetName().Version} on {Environment.OSVersion}, .NET {Environment.Version}")
                .AppendLine(error?.ToString() ?? "(no exception object)")
                .AppendLine()
                .ToString();

            lock (Gate)
            {
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes) file.Delete();
                File.AppendAllText(FilePath, text);
            }
        }
        catch (Exception) { /* reporting a failure must never cause another one */ }
    }
}
