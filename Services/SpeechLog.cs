namespace WinCompanion.Services;

/// <summary>Diagnostics for speech input, written to speech.log. Starts over when it passes 1 MB, like crash.log.</summary>
internal static class SpeechLog
{
    private const long MaxBytes = 1_000_000;
    private static readonly object Gate = new();

    public static void Write(string line)
    {
        try
        {
            lock (Gate)
            {
                var path = AppPaths.FileIn("speech.log");
                var file = new FileInfo(path);
                if (file.Exists && file.Length > MaxBytes) file.Delete();
                File.AppendAllText(path, $"{DateTime.Now:s}  {line}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
    }
}
