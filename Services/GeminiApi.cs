using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WinCompanion.Services;

/// <summary>What the chat client and the voice transcriber share: the model and the friendly error messages.</summary>
internal static class GeminiApi
{
    /// <summary>The Gemini model used for chat and for transcribing speech.</summary>
    public const string Model = "gemini-2.5-flash";

    public static string GenerateUrl => $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent";

    public static string Describe(HttpStatusCode status, string body, TimeSpan? retryAfter)
    {
        var message = ErrorMessage(body);
        switch ((int)status)
        {
            case 429:
                if (body.Contains("PerDay", StringComparison.OrdinalIgnoreCase))
                    return "You've used today's free Gemini quota. It resets daily, so try again tomorrow, or switch to a local model in Settings.";
                var wait = retryAfter is { } r ? $"about {Math.Ceiling(r.TotalSeconds)} seconds" : "a minute";
                return $"Slow down: the free tier only allows a few requests per minute. Try again in {wait}.";
            case 503:
                return "Gemini is overloaded right now. Try again in a moment.";
            case 400 when message.Contains("API key", StringComparison.OrdinalIgnoreCase):
            case 401:
            case 403:
                return "Gemini rejected the API key. Open Settings to update it.";
            default:
                return $"Gemini error {(int)status}: {message}";
        }
    }

    public static string ErrorMessage(string body)
    {
        try { return JsonNode.Parse(body)?["error"]?["message"]?.GetValue<string>() ?? body; }
        catch (JsonException) { return body; }
    }

    // Gemini reports "retry in N seconds" as an error detail of type RetryInfo, e.g. "retryDelay": "12s".
    public static TimeSpan? RetryDelay(string body)
    {
        try
        {
            var details = JsonNode.Parse(body)?["error"]?["details"]?.AsArray();
            var raw = details?.FirstOrDefault(d => d?["retryDelay"] is not null)?["retryDelay"]?.GetValue<string>();
            if (raw is not null && double.TryParse(raw.TrimEnd('s'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                return TimeSpan.FromSeconds(seconds);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        return null;
    }
}
