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
        sb.AppendLine($"Model: {(settings.Provider switch { "local" => $"local ({settings.LocalModel})", "claude" => $"Claude ({settings.ClaudeModel})", "openai" => $"OpenAI-compatible ({settings.CloudLabel}, {settings.OpenAiModel})", _ => "Gemini" })}");
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
        text = Hide(text, userName, "<user>");
        text = Hide(text, machineName, "<pc>");
        return text;
    }

    // A name of three or more letters is replaced wherever it stands alone as a word. A one- or two-letter name ("Al", "J") would also
    // match ordinary words, so it is replaced only where it is clearly a name: as a folder in a path, or after "user", "for", "by".
    private static string Hide(string text, string? name, string replacement)
    {
        if (string.IsNullOrEmpty(name)) return text;
        var escaped = Regex.Escape(name);
        if (name.Length >= 3)
            return Regex.Replace(text, @"(?<![\p{L}\p{N}])" + escaped + @"(?![\p{L}\p{N}])", replacement, RegexOptions.IgnoreCase);

        text = Regex.Replace(text, @"(?<=[\\/])" + escaped + @"(?=[\\/:\s""'.,;)]|$)", replacement, RegexOptions.IgnoreCase);
        return Regex.Replace(text, @"(?<=\b(?:user|users|for|by|as|account|name)[\s:=]+)" + escaped + @"(?![\p{L}\p{N}])", replacement, RegexOptions.IgnoreCase);
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
