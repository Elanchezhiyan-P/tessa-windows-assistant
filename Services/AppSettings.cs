using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace WinCompanion.Services;

/// <summary>Non-secret preferences: which model backend to use and where the local server is.</summary>
public sealed class AppSettings
{
    /// <summary>"gemini" or "local".</summary>
    public string Provider { get; set; } = "gemini";
    public string LocalBaseUrl { get; set; } = "http://localhost:11434/v1"; // Ollama's default
    public string LocalModel { get; set; } = "qwen2.5:3b";
    /// <summary>Optional separate model for screenshots (e.g. qwen2.5vl). Empty means use the main model.</summary>
    public string LocalVisionModel { get; set; } = "";

    /// <summary>
    /// False: PowerShell commands run unprompted only if read-only; everything else asks.
    /// True: everything runs unprompted except commands matching known dangerous patterns.
    /// </summary>
    public bool PowerShellTrusted { get; set; }
    /// <summary>Lets run_powershell ask Windows for administrator rights (UAC prompt each time). Off by default.</summary>
    public bool PowerShellAdmin { get; set; }

    /// <summary>Folder name of the character that delivers reminders ("realcat", "cat", "pixeldog"...), or "none".</summary>
    public string Pet { get; set; } = "realcat";

    /// <summary>With a cloud model (Gemini), ask before a screenshot, the clipboard text or a file's contents is sent.</summary>
    public bool AskBeforeSendingToCloud { get; set; } = true;

    /// <summary>Tell the AI which app (and window title) the user was in, so it can tailor its help.</summary>
    public bool ShareActiveApp { get; set; }

    /// <summary>What the assistant is called. It is also her wake word ("Hey Tessa").</summary>
    public string AssistantName { get; set; } = "Tessa";
    public string UserName { get; set; } = "";
    /// <summary>The short name she uses for the user.</summary>
    public string UserNickname { get; set; } = "";
    /// <summary>"female" or "male": which kind of Windows voice reads her replies aloud.</summary>
    public string VoiceGender { get; set; } = "female";
    /// <summary>True: she never speaks aloud (replies and reminders are text only).</summary>
    public bool VoiceMuted { get; set; }
    /// <summary>Language she listens in (a tag such as "en-IN" or "en-US"). Empty: whatever Windows is set to.</summary>
    public string SpeechLanguage { get; set; } = "";
    /// <summary>False until the first-run welcome has been completed or skipped.</summary>
    public bool SetupDone { get; set; }

    [JsonIgnore] public bool UseLocal => Provider == "local";
    [JsonIgnore] public string WakePhrase => "hey " + AssistantName.Trim().ToLowerInvariant();
    [JsonIgnore] public string WakeLabel => "Hey " + AssistantName.Trim();
    /// <summary>How the assistant addresses the user: the short name, else the name, else nothing.</summary>
    [JsonIgnore] public string CallUser => UserNickname.Length > 0 ? UserNickname : UserName;

    /// <summary>The settings the running app is using, for code that has no reference to the window (voice, tray, prompts).</summary>
    [JsonIgnore] public static AppSettings Current { get; private set; } = new();

    // The tray tooltip is limited to 63 characters, so names are kept short.
    private const int MaxNameLength = 18;

    public static string CleanName(string? text, string fallback = "")
    {
        var clean = Regex.Replace(text ?? "", @"[^\p{L}\p{N} '\-\.]", "").Trim();
        if (clean.Length > MaxNameLength) clean = clean[..MaxNameLength].Trim();
        return clean.Length == 0 ? fallback : clean;
    }

    public void SetProfile(string? userName, string? nickname, string? assistantName, bool maleVoice)
    {
        UserName = CleanName(userName);
        UserNickname = CleanName(nickname);
        AssistantName = CleanName(assistantName, "Tessa");
        VoiceGender = maleVoice ? "male" : "female";
    }

    private static string FilePath => AppPaths.FileIn("settings.json");

    public static AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            if (File.Exists(FilePath))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
        if (string.IsNullOrWhiteSpace(settings.AssistantName)) settings.AssistantName = "Tessa";
        Current = settings;
        return settings;
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch (IOException) { }
    }
}
