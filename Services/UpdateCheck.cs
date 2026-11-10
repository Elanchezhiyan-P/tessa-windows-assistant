using System.Net;
using System.Reflection;
using System.Text.Json;

namespace WinCompanion.Services;

/// <summary>
/// Asks GitHub whether a newer release exists. Only runs when the user asks (/update or the Settings button): nothing is sent
/// except the request itself, and nothing is downloaded or installed.
/// </summary>
public static class UpdateCheck
{
    public const string LatestUrl = "https://api.github.com/repos/Elanchezhiyan-P/tessa-windows-assistant/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Tessa/{AboutTessa.Version}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    public static Version Current =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) : new Version(2, 0, 0);

    public static async Task<string> CheckAsync(HttpClient? http = null)
    {
        try
        {
            using var response = await (http ?? Http).GetAsync(LatestUrl);
            if (response.StatusCode == HttpStatusCode.NotFound) return "No releases are published on GitHub yet, so you have the newest version.";
            if (!response.IsSuccessStatusCode) return $"GitHub didn't answer properly ({(int)response.StatusCode}). Try again later.";
            return Describe(Current, await response.Content.ReadAsStringAsync());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return "I couldn't reach GitHub to check for updates. Are you online?";
        }
    }

    /// <summary>Turns GitHub's "latest release" JSON into a sentence for the user.</summary>
    public static string Describe(Version current, string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            var url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() : null;
            if (!TryParseTag(tag, out var latest)) return "I couldn't read the latest version from GitHub.";

            var shown = $"{latest.Major}.{latest.Minor}.{Math.Max(latest.Build, 0)}";
            return latest > current
                ? $"Version {shown} is available (you have {current.ToString(3)}). Download it here: {url ?? "https://github.com/Elanchezhiyan-P/tessa-windows-assistant/releases"}"
                : $"You're up to date: version {current.ToString(3)} is the latest.";
        }
        catch (JsonException) { return "I couldn't read the latest version from GitHub."; }
    }

    /// <summary>Reads tags such as "v2.1.0", "2.1" or "v2.1.0-beta" as a version (a pre-release suffix is ignored).</summary>
    public static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;
        var core = tag.Trim().TrimStart('v', 'V');
        var cut = core.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) core = core[..cut];
        if (!Version.TryParse(core, out var parsed)) return false;
        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        return true;
    }
}
