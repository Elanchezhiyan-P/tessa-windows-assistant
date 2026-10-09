using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace WinCompanion.Services;

/// <summary>Turns a recorded phrase into text with Gemini. Far more accurate than Windows' built-in recognisers, especially with accents.</summary>
internal static class GeminiSpeech
{
    private const string Model = "gemini-2.5-flash";
    private const string Instruction =
        "Transcribe this audio recording exactly as spoken. The speaker may have an Indian English accent and is giving a short " +
        "command or question to a PC assistant. Reply with only the words spoken, with no quotes and no commentary. " +
        "If there is no clear speech, reply with exactly: [none]";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<string> TranscribeAsync(string apiKey, byte[] wav)
    {
        var body = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(
                    new JsonObject { ["text"] = Instruction },
                    new JsonObject { ["inlineData"] = new JsonObject { ["mimeType"] = "audio/wav", ["data"] = Convert.ToBase64String(wav) } })
            })
        };

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent") { Content = JsonContent.Create(body) };
        request.Headers.Add("x-goog-api-key", apiKey);

        using var response = await Http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Gemini returned {(int)response.StatusCode}");

        var parts = JsonNode.Parse(json)?["candidates"]?[0]?["content"]?["parts"]?.AsArray();
        return string.Concat((parts ?? new JsonArray()).Select(p => p?["text"]?.GetValue<string>())).Trim();
    }
}
