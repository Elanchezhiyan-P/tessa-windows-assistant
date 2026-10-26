namespace WinCompanion.Services;

/// <summary>Writes a file so a crash or power loss halfway through can never leave it truncated.</summary>
internal static class AtomicFile
{
    private static readonly object Gate = new();

    /// <summary>Writes to a temporary file, then swaps it in. The previous version is kept as <c>name.bak</c>.</summary>
    public static void WriteAllText(string path, string text)
    {
        lock (Gate)
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, text);
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
    }

    public static bool IsJson(string text)
    {
        try { System.Text.Json.JsonDocument.Parse(text).Dispose(); return true; }
        catch (System.Text.Json.JsonException) { return false; }
    }

    /// <summary>Reads a file, falling back to its <c>.bak</c> copy if the main file is missing or unreadable. Null if neither exists.</summary>
    public static string? ReadAllText(string path, Func<string, bool>? isValid = null)
    {
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                var text = File.ReadAllText(candidate);
                if (isValid is null || isValid(text)) return text;
            }
            catch (IOException) { }
        }
        return null;
    }
}
