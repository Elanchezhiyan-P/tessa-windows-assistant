using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace WinCompanion.Services;

public enum SpeechEngineKind { Windows, Gemini, OpenAi }

/// <summary>Chooses who turns her recording of your voice into text.</summary>
public static class SpeechRouting
{
    public static readonly string[] Choices = ["auto", "gemini", "openai", "windows"];

    public static string Normalize(string? choice) =>
        Choices.FirstOrDefault(c => c.Equals(choice?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "auto";

    /// <summary>
    /// "Auto" follows the chosen AI model when that service can listen (Gemini, or an OpenAI-compatible service); Claude and local models
    /// cannot, so Windows' own recogniser is used unless a service is picked on purpose. The audio goes only to the service named here.
    /// </summary>
    public static SpeechEngineKind Resolve(AppSettings settings, bool hasGeminiKey, bool openAiUsable)
    {
        if (!settings.SpeechViaGemini) return SpeechEngineKind.Windows;
        var wanted = Normalize(settings.SpeechEngine);
        if (wanted == "auto")
            wanted = settings.Provider switch { "gemini" => "gemini", "openai" => "openai", _ => "windows" };
        return wanted switch
        {
            "gemini" when hasGeminiKey => SpeechEngineKind.Gemini,
            "openai" when openAiUsable => SpeechEngineKind.OpenAi,
            _ => SpeechEngineKind.Windows
        };
    }

    /// <summary>The model to ask for: the one set in the settings, else the usual speech model of the service.</summary>
    public static string ModelFor(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.SpeechModel)) return settings.SpeechModel.Trim();
        var host = Uri.TryCreate(settings.OpenAiBaseUrl, UriKind.Absolute, out var uri) ? uri.Host : "";
        return host.Contains("groq", StringComparison.OrdinalIgnoreCase) ? "whisper-large-v3-turbo" : "whisper-1";
    }

    /// <summary>Who gets the audio, in words, for the settings page.</summary>
    public static string Describe(SpeechEngineKind kind, AppSettings settings) => kind switch
    {
        SpeechEngineKind.Gemini => "Your voice goes to Google's Gemini to be turned into text.",
        SpeechEngineKind.OpenAi => $"Your voice goes to {settings.CloudLabelFor("openai")} ({ModelFor(settings)}) to be turned into text.",
        _ => "Your voice is understood on this PC by Windows' own recogniser (which may use Microsoft's online service, depending on your Windows settings)."
    };
}

/// <summary>Turns a recorded phrase into text with an OpenAI-compatible service (the standard /audio/transcriptions request).</summary>
public static class OpenAiSpeech
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private const string Prompt = "A short command or question to a PC assistant.";

    /// <summary>"en-IN" becomes "en": the service wants the two-letter language, and an empty tag means "work it out".</summary>
    internal static string LanguageOf(string? tag)
    {
        var t = (tag ?? "").Trim();
        var dash = t.IndexOf('-');
        var language = (dash > 0 ? t[..dash] : t).ToLowerInvariant();
        return language.Length is 2 or 3 && language.All(char.IsAsciiLetter) ? language : "";
    }

    public static async Task<string> TranscribeAsync(string baseUrl, string apiKey, string model, byte[] wav, string languageTag = "")
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(model), "model" },
            { new StringContent("json"), "response_format" },
            { new StringContent(Prompt), "prompt" },
            { new StringContent("0"), "temperature" }
        };
        if (LanguageOf(languageTag) is { Length: > 0 } language) form.Add(new StringContent(language), "language");
        var file = new ByteArrayContent(wav);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(file, "file", "speech.wav");

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/audio/transcriptions") { Content = form };
        if (apiKey.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage response;
        try { response = await Http.SendAsync(request); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new LlmException("couldn't reach the speech service");
        }
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
                return (JsonNode.Parse(body)?["text"]?.GetValue<string>() ?? "").Trim();
            throw new LlmException(Describe(response.StatusCode, body));
        }
    }

    internal static string Describe(HttpStatusCode status, string body) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "the service rejected the API key",
        HttpStatusCode.NotFound => "this service has no speech model by that name (see Settings, You and her)",
        HttpStatusCode.TooManyRequests => "the service is limiting requests or has no credit left",
        >= HttpStatusCode.InternalServerError => "the service is busy",
        _ => $"error {(int)status}: {(body.Length > 150 ? body[..150] : body)}"
    };
}
