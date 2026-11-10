using System.Security.Cryptography;
using System.Text;

namespace WinCompanion.Services;

/// <summary>
/// Stores the Gemini API key encrypted with DPAPI (current user only).
/// The GEMINI_API_KEY environment variable takes precedence if set.
/// </summary>
public sealed class SettingsStore
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinCompanion");

    /// <summary>"gemini" (the original file name, kept so saved keys still work), "claude" or "openai".</summary>
    private static string KeyPath(string provider) =>
        Path.Combine(Folder, provider == "gemini" ? "apikey.bin" : $"apikey.{provider}.bin");

    private static string EnvironmentVariable(string provider) => provider switch
    {
        "claude" => "ANTHROPIC_API_KEY",
        "openai" => "OPENAI_API_KEY",
        _ => "GEMINI_API_KEY"
    };

    public string? LoadApiKey(string provider = "gemini")
    {
        var env = Environment.GetEnvironmentVariable(EnvironmentVariable(provider));
        if (!string.IsNullOrWhiteSpace(env)) return env;
        var path = KeyPath(provider);
        if (!File.Exists(path)) return null;
        try
        {
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
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
    public (string Masked, string Source)? DescribeKey(string provider = "gemini")
    {
        var key = LoadApiKey(provider);
        if (string.IsNullOrWhiteSpace(key)) return null;
        var variable = EnvironmentVariable(provider);
        var fromEnvironment = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable));
        return (Mask(key), fromEnvironment ? $"{variable} environment variable" : "encrypted on this PC");
    }

    /// <summary>Deletes the saved key. An environment variable is outside the app's control and is left alone.</summary>
    public void RemoveApiKey(string provider = "gemini")
    {
        try { if (File.Exists(KeyPath(provider))) File.Delete(KeyPath(provider)); }
        catch (IOException) { }
    }

    public void SaveApiKey(string key, string provider = "gemini")
    {
        Directory.CreateDirectory(Folder);
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(KeyPath(provider), bytes);
    }
}
