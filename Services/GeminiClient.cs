using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using WinCompanion.Tools;

namespace WinCompanion.Services;

/// <summary>Gemini over REST: a function-calling chat loop plus one-shot generation.</summary>
public sealed class GeminiClient : LlmClientBase
{
    private const string Model = "gemini-2.5-flash";
    private const int MaxRetries = 2;
    private static readonly TimeSpan MaxAutoWait = TimeSpan.FromSeconds(20);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly JsonArray _history = new();

    private static string ContextPath => AppPaths.FileIn("context.gemini.json");

    public GeminiClient(string apiKey, ToolRegistry tools, Func<string, Task<bool>> confirm, ActionLog log,
        MemoryStore memory, Func<ActiveAppInfo?> activeApp)
        : base(tools, confirm, log, memory, activeApp)
    {
        _http.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);
        LoadHistory();
    }

    // ---- Chat with tools -------------------------------------------------------------------

    public override async Task<string> SendAsync(string userText)
    {
        var startCount = _history.Count;
        _history.Add(new JsonObject
        {
            ["role"] = "user",
            ["parts"] = new JsonArray(new JsonObject { ["text"] = userText })
        });

        try
        {
            for (var round = 0; round < MaxToolRounds; round++)
            {
                var content = await PostAsync(new JsonObject
                {
                    ["systemInstruction"] = new JsonObject
                    {
                        ["parts"] = new JsonArray(new JsonObject { ["text"] = SystemPrompt() })
                    },
                    ["contents"] = _history.DeepClone(),
                    ["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = Tools.Declarations() })
                });
                _history.Add(content.DeepClone());

                var parts = content["parts"]?.AsArray() ?? new JsonArray();
                var calls = parts.Select(p => p?["functionCall"]).Where(c => c is not null).ToList();

                if (calls.Count == 0)
                    return string.Concat(parts.Select(p => p?["text"]?.GetValue<string>())).Trim();

                var responses = new JsonArray();
                foreach (var call in calls)
                {
                    var name = call!["name"]!.GetValue<string>();
                    var args = call["args"]?.AsObject() ?? new JsonObject();
                    NotifyToolCall(name, args);
                    var result = await RunToolAsync(name, args);
                    responses.Add(new JsonObject
                    {
                        ["functionResponse"] = new JsonObject
                        {
                            ["name"] = name,
                            ["response"] = new JsonObject { ["result"] = result }
                        }
                    });
                }
                _history.Add(new JsonObject { ["role"] = "user", ["parts"] = responses });
            }
            return "Stopped after too many tool steps.";
        }
        catch
        {
            // Drop the half-finished turn so a dangling tool call never poisons later requests.
            while (_history.Count > startCount) _history.RemoveAt(_history.Count - 1);
            throw;
        }
        finally
        {
            TrimHistory();
            SaveHistory();
        }
    }

    // ---- One-shot generation (no tools, no chat history) -------------------------------------

    public override async Task<string> GenerateAsync(string instruction, string? text = null, byte[]? png = null)
    {
        var parts = new JsonArray(new JsonObject { ["text"] = text is null ? instruction : $"{instruction}\n\n{text}" });
        if (png is not null)
        {
            parts.Add(new JsonObject
            {
                ["inlineData"] = new JsonObject { ["mimeType"] = "image/png", ["data"] = Convert.ToBase64String(png) }
            });
        }

        var content = await PostAsync(new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject { ["role"] = "user", ["parts"] = parts })
        });
        return string.Concat((content["parts"]?.AsArray() ?? new JsonArray()).Select(p => p?["text"]?.GetValue<string>())).Trim();
    }

    // ---- HTTP, retries and friendly errors ----------------------------------------------------

    private async Task<JsonObject> PostAsync(JsonObject body)
    {
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent";

        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage resp;
            string text;
            try
            {
                resp = await _http.PostAsJsonAsync(url, body);
                text = await resp.Content.ReadAsStringAsync();
            }
            catch (TaskCanceledException)
            {
                throw new LlmException("Gemini took too long to answer. Try again.");
            }
            catch (HttpRequestException ex)
            {
                throw new LlmException($"Couldn't reach Gemini (offline?): {ex.Message}");
            }

            using (resp)
            {
                if (resp.IsSuccessStatusCode)
                {
                    var node = JsonNode.Parse(text)!;
                    return node["candidates"]?[0]?["content"]?.AsObject()
                        ?? throw new LlmException("Gemini returned no answer (the request may have been blocked).");
                }

                var retryAfter = RetryDelay(text);
                var transient = resp.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;
                var dailyQuota = text.Contains("PerDay", StringComparison.OrdinalIgnoreCase);
                var wait = retryAfter ?? TimeSpan.FromSeconds(3);

                if (transient && !dailyQuota && attempt < MaxRetries && wait <= MaxAutoWait)
                {
                    await Task.Delay(wait + TimeSpan.FromMilliseconds(500));
                    continue;
                }
                throw new LlmException(Describe(resp.StatusCode, text, retryAfter));
            }
        }
    }

    private static string Describe(HttpStatusCode status, string body, TimeSpan? retryAfter)
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

    private static string ErrorMessage(string body)
    {
        try { return JsonNode.Parse(body)?["error"]?["message"]?.GetValue<string>() ?? body; }
        catch (JsonException) { return body; }
    }

    // Gemini reports "retry in N seconds" as an error detail of type RetryInfo, e.g. "retryDelay": "12s".
    private static TimeSpan? RetryDelay(string body)
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

    // ---- Persisted conversation context ---------------------------------------------------------

    public override bool IsCloud => true;

    public override void ClearHistory()
    {
        _history.Clear();
        SaveHistory();
    }

    private void TrimHistory()
    {
        while (_history.Count > MaxHistoryEntries) _history.RemoveAt(0);
        // The window must start on a plain user message, never mid tool-call.
        while (_history.Count > 0 && !IsUserText(_history[0])) _history.RemoveAt(0);
    }

    private static bool IsUserText(JsonNode? entry) =>
        entry?["role"]?.GetValue<string>() == "user" && entry["parts"]?[0]?["text"] is not null;

    private void LoadHistory()
    {
        try
        {
            if (File.Exists(ContextPath) && JsonNode.Parse(File.ReadAllText(ContextPath)) is JsonArray saved)
                foreach (var entry in saved) _history.Add(entry!.DeepClone());
            TrimHistory();
        }
        catch (Exception ex) when (ex is IOException or JsonException) { _history.Clear(); }
    }

    private void SaveHistory()
    {
        try { File.WriteAllText(ContextPath, _history.ToJsonString()); }
        catch (IOException) { }
    }
}
