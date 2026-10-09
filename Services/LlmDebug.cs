namespace WinCompanion.Services;

/// <summary>
/// Opt-in diagnostics: set the environment variable WINCOMPANION_DEBUG_LLM=1 and every request to, and reply from,
/// the AI model is appended to %AppData%\WinCompanion\llm-debug.log. Off by default because the log contains
/// your conversation (and any screenshot data is NOT logged, only its presence).
/// </summary>
internal static class LlmDebug
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("WINCOMPANION_DEBUG_LLM") == "1";
    private static readonly object Gate = new();

    public static void Write(string label, string text)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
                File.AppendAllText(AppPaths.FileIn("llm-debug.log"), $"[{DateTime.Now:HH:mm:ss}] {label}\n{text}\n\n");
        }
        catch (IOException) { }
    }
}
