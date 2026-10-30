using System.Text;
using System.Text.RegularExpressions;

namespace WinCompanion.Services;

/// <summary>
/// A block of text for bug reports: version, hardware, a few non-secret settings and the latest errors. It leaves out chat,
/// keys, notes and file names, and hides the Windows user name and PC name.
/// </summary>
public static class Diagnostics
{
    private const int CrashLines = 40;

    public static string Build(SystemSpecs specs, AppSettings settings, string? crashTail)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{settings.AssistantName} {AboutTessa.Version}, .NET {Environment.Version}");
        sb.AppendLine(SystemInfo.Describe(specs));
        sb.AppendLine();
        sb.AppendLine($"Model: {(settings.UseLocal ? $"local ({settings.LocalModel})" : "Gemini")}");
        sb.AppendLine($"PowerShell: {(settings.PowerShellTrusted ? "run freely" : "ask unless read-only")}{(settings.PowerShellAdmin ? ", administrator allowed" : "")}");
        sb.AppendLine($"Voice: {(settings.VoiceMuted ? "muted" : "on")}, speech via Gemini {(settings.SpeechViaGemini ? "on" : "off")}, language \"{settings.SpeechLanguage}\"");
        sb.AppendLine($"Character: {settings.Pet}, clipboard history {(settings.ClipboardHistory ? "on" : "off")}, briefing at start {(settings.BriefingAtStart ? "on" : "off")}");
        sb.AppendLine();
        sb.AppendLine("Recent errors:");
        sb.AppendLine(string.IsNullOrWhiteSpace(crashTail) ? "(none)" : crashTail.TrimEnd());

        return Redact(sb.ToString().TrimEnd(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.UserName, Environment.MachineName);
    }

    /// <summary>Replaces the profile folder, the Windows user name and the PC name, so a pasted report doesn't identify the person.</summary>
    public static string Redact(string text, string? userProfile, string? userName, string? machineName)
    {
        if (!string.IsNullOrEmpty(userProfile))
            text = Regex.Replace(text, Regex.Escape(userProfile), "%USERPROFILE%", RegexOptions.IgnoreCase);
        if (userName is { Length: >= 3 })
            text = Regex.Replace(text, @"(?<![\p{L}\p{N}])" + Regex.Escape(userName) + @"(?![\p{L}\p{N}])", "<user>", RegexOptions.IgnoreCase);
        if (machineName is { Length: >= 3 })
            text = Regex.Replace(text, @"(?<![\p{L}\p{N}])" + Regex.Escape(machineName) + @"(?![\p{L}\p{N}])", "<pc>", RegexOptions.IgnoreCase);
        return text;
    }

    /// <summary>The last lines of crash.log, or null when there is none.</summary>
    public static string? CrashTail(int lines = CrashLines)
    {
        try
        {
            if (!File.Exists(CrashLog.FilePath)) return null;
            return string.Join(Environment.NewLine, File.ReadLines(CrashLog.FilePath).TakeLast(lines));
        }
        catch (IOException) { return null; }
    }
}
