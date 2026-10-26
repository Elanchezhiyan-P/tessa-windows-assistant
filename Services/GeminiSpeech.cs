using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace WinCompanion.Services;

/// <summary>Turns a recorded phrase into text with Gemini. Far more accurate than Windows' built-in recognisers, especially with accents.</summary>
internal static class GeminiSpeech
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>The instruction sent with the audio. <paramref name="languageTag"/> is a tag such as "en-IN", or empty for no hint.</summary>
    internal static string BuildInstruction(string languageTag)
    {
        var language = languageTag.Trim();
        var hint = language.Length > 0 ? $" The speaker is expected to be speaking the language with tag \"{language}\"." : "";
        return "Transcribe this audio recording exactly as spoken. It is a short command or question to a PC assistant." + hint +
               " Reply with only the words spoken, with no quotes and no commentary. If there is no clear speech, reply with exactly: [none]";
    }

    public static async Task<string> TranscribeAsync(string apiKey, byte[] wav, string languageTag = "")
    {
        var body = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(
                    new JsonObject { ["text"] = BuildInstruction(languageTag) },
                    new JsonObject { ["inlineData"] = new JsonObject { ["mimeType"] = "audio/wav", ["data"] = Convert.ToBase64String(wav) } })
            })
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, GeminiApi.GenerateUrl) { Content = JsonContent.Create(body) };
        request.Headers.Add("x-goog-api-key", apiKey);

        using var response = await Http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new LlmException(GeminiApi.Describe(response.StatusCode, json, GeminiApi.RetryDelay(json)));

        var parts = JsonNode.Parse(json)?["candidates"]?[0]?["content"]?["parts"]?.AsArray();
        return string.Concat((parts ?? new JsonArray()).Select(p => p?["text"]?.GetValue<string>())).Trim();
    }
}
