using System.Text.Json.Nodes;

namespace WinCompanion.Services;

/// <summary>Append-only record of every tool call: when, what, whether it was approved, and the outcome.</summary>
public sealed class ActionLog
{
    private readonly object _gate = new();

    public static string FilePath => AppPaths.FileIn("actions.log");

    public void Write(string tool, JsonObject args, string decision, string result)
    {
        var line = string.Join('\t',
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            decision,
            tool,
            args.ToJsonString(),
            Truncate(result));
        try
        {
            lock (_gate) File.AppendAllText(FilePath, line + Environment.NewLine);
        }
        catch (IOException) { /* logging must never break an action */ }
    }

    /// <summary>Make sure the file exists so it can be opened from the tray.</summary>
    public static void EnsureExists()
    {
        if (!File.Exists(FilePath)) File.WriteAllText(FilePath, "time\tdecision\ttool\targuments\tresult" + Environment.NewLine);
    }

    private static string Truncate(string text)
    {
        var flat = text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        return flat.Length > 300 ? flat[..300] + "..." : flat;
    }
}
