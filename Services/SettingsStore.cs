using System.Security.Cryptography;
using System.Text;

namespace WinCompanion.Services;

/// <summary>
/// Stores the Gemini API key encrypted with DPAPI (current user only).
/// The GEMINI_API_KEY environment variable takes precedence if set.
/// </summary>
public sealed class SettingsStore
{
    private static readonly string KeyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinCompanion", "apikey.bin");

    public string? LoadApiKey()
    {
        var env = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        if (!File.Exists(KeyPath)) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(KeyPath), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (CryptographicException) { return null; }
    }

    /// <summary>
    /// A safe-to-display version of a key: first 4 and last 4 characters with ** between, e.g. "AIza**9xYz".
    /// Keys too short to hide safely are fully masked.
    /// </summary>
    public static string Mask(string key) => key.Length < 12 ? "********" : $"{key[..4]}**{key[^4..]}";

    /// <summary>Where the active key comes from and how to show it, or null if there is none.</summary>
    public (string Masked, string Source)? DescribeKey()
    {
        var key = LoadApiKey();
        if (string.IsNullOrWhiteSpace(key)) return null;
        var fromEnvironment = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY"));
        return (Mask(key), fromEnvironment ? "GEMINI_API_KEY environment variable" : "encrypted on this PC");
    }

    /// <summary>Deletes the saved key. A GEMINI_API_KEY environment variable is outside the app's control and is left alone.</summary>
    public void RemoveApiKey()
    {
        try { if (File.Exists(KeyPath)) File.Delete(KeyPath); }
        catch (IOException) { }
    }

    public void SaveApiKey(string key)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(KeyPath, bytes);
    }
}
