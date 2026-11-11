using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WinCompanion.Tools;

namespace WinCompanion.Services;

/// <summary>Where an OpenAI-compatible client sends its requests.</summary>
/// <param name="Cloud">True for a hosted service (the key is sent, requests leave this PC); false for a server on this PC.</param>
public sealed record OpenAiEndpoint(string BaseUrl, string Model, string VisionModel, string? ApiKey, bool Cloud, string Label);

/// <summary>
/// A model behind any OpenAI-compatible /chat/completions server with tool calling: a local one (Ollama, LM Studio,
/// llama.cpp, vLLM; nothing leaves the machine) or a hosted one (OpenAI, OpenRouter, Groq and others; needs an API key).
/// </summary>
public sealed class LocalLlmClient : LlmClientBase
{
    private static readonly Regex Thinking = new(@"<think>.*?</think>", RegexOptions.Singleline | RegexOptions.Compiled);

    // Local models can take a while to load on the first request.
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly JsonArray _history = new();
    private readonly string _baseUrl, _model, _visionModel, _apiKey, _label;
    private readonly bool _cloud;

    public LocalLlmClient(AppSettings settings, ToolRegistry tools, Func<string, Task<bool>> confirm, ActionLog log,
        MemoryStore memory, Func<ActiveAppInfo?> activeApp)
        : this(ForLocal(settings), tools, confirm, log, memory, activeApp) { }

    public LocalLlmClient(OpenAiEndpoint endpoint, ToolRegistry tools, Func<string, Task<bool>> confirm, ActionLog log,
        MemoryStore memory, Func<ActiveAppInfo?> activeApp)
        : base(tools, confirm, log, memory, activeApp)
    {
        _baseUrl = endpoint.BaseUrl.TrimEnd('/');
        _model = endpoint.Model.Trim();
        _visionModel = string.IsNullOrWhiteSpace(endpoint.VisionModel) ? _model : endpoint.VisionModel.Trim();
        _apiKey = endpoint.ApiKey ?? "";
        _cloud = endpoint.Cloud;
        _label = endpoint.Label;
    }

    public static OpenAiEndpoint ForLocal(AppSettings settings) =>
        new(settings.LocalBaseUrl, settings.LocalModel, settings.LocalVisionModel, null, false, "the local model server");

    public static OpenAiEndpoint ForOpenAi(AppSettings settings, string apiKey) =>
        new(settings.OpenAiBaseUrl, settings.OpenAiModel, "", apiKey, true,
            Uri.TryCreate(settings.OpenAiBaseUrl, UriKind.Absolute, out var uri) ? uri.Host : "the OpenAI-compatible service");

    /// <summary>Ask the server which models it has (GET /models). Used by Settings to detect Qwen or to check a cloud key.</summary>
    public static async Task<List<string>> ListModelsAsync(string baseUrl, string? apiKey = null)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        if (!string.IsNullOrEmpty(apiKey)) http.DefaultRequestHeaders.Authorization = new("Bearer", apiKey);
        try
        {
            var text = await http.GetStringAsync(baseUrl.TrimEnd('/') + "/models");
            var data = JsonNode.Parse(text)?["data"]?.AsArray();
            return data?.Select(m => m?["id"]?.GetValue<string>()).OfType<string>().ToList() ?? new();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new LlmException(string.IsNullOrEmpty(apiKey)
                ? $"Couldn't reach {baseUrl}. Is Ollama or LM Studio running?"
                : $"Couldn't list models at {baseUrl}. Check the address and the key.");
        }
    }

    // ---- Chat with tools -------------------------------------------------------------------

    public override async Task<string> SendAsync(string userText, IProgress<string>? partial = null, CancellationToken cancel = default)
    {
        var startCount = _history.Count;
        _history.Add(new JsonObject { ["role"] = "user", ["content"] = userText });

        try
        {
            // A read-only question about this PC: skip tool selection and go straight to "write the command, run it".
            if (SystemQuestions.Matches(userText) && await AskForCommandAndRunAsync(userText) is { } answer)
            {
                _history.Add(new JsonObject { ["role"] = "assistant", ["content"] = answer });
                return answer;
            }

            // Small models choke on dozens of tool definitions, so offer only the ones this request could need.
            var offered = ToolSelector.Select(RecentUserText(), RecentToolNames());

            string? lastToolResult = null;
            for (var round = 0; round < MaxToolRounds; round++)
            {
                cancel.ThrowIfCancellationRequested();
                partial?.Report(""); // a new round: whatever was shown before belongs to the step that just ended

                var messages = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = SystemPrompt() });
                foreach (var entry in _history) messages.Add(entry!.DeepClone());

                var message = await ChatAsync(_model, messages, Tools.OpenAiDeclarations(offered), partial, cancel);

                var calls = message["tool_calls"]?.AsArray();
                if (calls is null || calls.Count == 0)
                {
                    var text = Clean(message["content"]?.GetValue<string>());
                    if (text.Length > 0)
                    {
                        _history.Add(message.DeepClone());
                        return text;
                    }

                    // Small models sometimes answer with nothing at all. After a tool ran, show what it found; before
                    // any tool ran, ask the model for just a PowerShell command (plain text is far more reliable than
                    // tool calling for a small model) and run that.
                    var recovered = lastToolResult ?? await AskForCommandAndRunAsync(userText);
                    if (recovered is null)
                        return "I couldn't work out how to do that. Try rephrasing it, or use a larger model.";
                    _history.Add(new JsonObject { ["role"] = "assistant", ["content"] = recovered });
                    return recovered;
                }

                _history.Add(message.DeepClone());
                foreach (var call in calls)
                {
                    var id = call?["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N");
                    var function = call?["function"];
                    var name = function?["name"]?.GetValue<string>() ?? "";
                    var args = ParseArguments(function?["arguments"]);

                    NotifyToolCall(name, args);
                    var result = await RunToolAsync(name, args);
                    lastToolResult = result;
                    _history.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = id, ["content"] = result });
                }
            }
            return "Stopped after too many tool steps.";
        }
        catch
        {
            while (_history.Count > startCount) _history.RemoveAt(_history.Count - 1);
            throw;
        }
        finally
        {
            TrimHistory();
        }
    }

    /// <summary>
    /// The fallback for when tool calling fails: ask the model for the PowerShell command as plain text, run it through
    /// the normal run_powershell path (so the same approval rules apply), then have the model explain the output.
    /// </summary>
    private async Task<string?> AskForCommandAndRunAsync(string request)
    {
        var reply = await ChatAsync(_model, new JsonArray(
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = "You write Windows PowerShell. Reply with ONLY the command (one line if possible), " +
                              "with no explanation and no markdown. It must run without administrator rights. " +
                              "Keep the output short: at most 15 rows (Select-Object -First 15) and only the useful columns. " +
                              "Examples of correct commands:\n" +
                              "free disk space: Get-PSDrive -PSProvider FileSystem | Select-Object Name, @{n='UsedGB';e={[math]::Round($_.Used/1GB,1)}}, @{n='FreeGB';e={[math]::Round($_.Free/1GB,1)}}\n" +
                              "free memory: Get-CimInstance Win32_OperatingSystem | Select-Object @{n='FreeGB';e={[math]::Round($_.FreePhysicalMemory/1MB,1)}}, @{n='TotalGB';e={[math]::Round($_.TotalVisibleMemorySize/1MB,1)}}\n" +
                              "biggest processes: Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 5 Name, @{n='MB';e={[math]::Round($_.WorkingSet64/1MB)}}\n" +
                              "ip address: Get-NetIPAddress -AddressFamily IPv4 | Select-Object InterfaceAlias, IPAddress\n" +
                              "last boot: Get-CimInstance Win32_OperatingSystem | Select-Object LastBootUpTime"
            },
            new JsonObject { ["role"] = "user", ["content"] = request }), tools: null);

        var command = ExtractCommand(reply["content"]?.GetValue<string>());
        if (command is null) return null;

        var args = new JsonObject { ["command"] = command };
        NotifyToolCall("run_powershell", args);
        var output = await RunToolAsync("run_powershell", args);
        if (output.StartsWith("The user declined", StringComparison.Ordinal)) return output;

        // Raw byte counts are unreadable (and small models misread them), so show sizes the way people say them,
        // and never dump more than a screenful.
        var lines = Regex.Replace(output, @"\b\d{8,}\b", m => FormatSize(double.Parse(m.Value))).ReplaceLineEndings("\n").Split('\n');
        var shown = string.Join('\n', lines.Take(25)).TrimEnd();
        if (lines.Length > 25) shown += $"\n...({lines.Length - 25} more lines)";

        // Let the model put the output into a plain answer; if it can't, the output itself is still the answer.
        var summary = await ChatAsync(_model, new JsonArray(
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = "Answer the user's question in one to three short plain sentences, using only the command output. " +
                              "Copy numbers and names exactly; do not guess or estimate."
            },
            new JsonObject { ["role"] = "user", ["content"] = $"Question: {request}\n\nCommand output:\n{shown}" }), tools: null);

        var text = Clean(summary["content"]?.GetValue<string>());
        return text.Length > 0 ? $"{text}\n\n{shown}" : shown;
    }

    private static string FormatSize(double bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (1L << 30):0.0} GB",
        _ => $"{bytes / (1L << 20):0} MB"
    };

    private static string? ExtractCommand(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return null;
        var fenced = Regex.Match(reply, @"```(?:powershell|pwsh|ps1)?\s*\n?(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var command = (fenced.Success ? fenced.Groups[1].Value : reply).Trim().Trim('`').Trim();
        return command.Length is > 0 and < 1500 ? command : null;
    }

    // The last two things the user said, so a short follow-up like "yes, do it" still selects the right tools.
    private string RecentUserText() => string.Join(' ', _history
        .Where(m => m?["role"]?.GetValue<string>() == "user")
        .TakeLast(2)
        .Select(m => m?["content"]?.GetValue<string>() ?? ""));

    private IEnumerable<string> RecentToolNames() => _history
        .TakeLast(10)
        .SelectMany(m => m?["tool_calls"]?.AsArray() ?? new JsonArray())
        .Select(call => call?["function"]?["name"]?.GetValue<string>())
        .OfType<string>();

    // ---- One-shot generation (no tools, no chat history) -------------------------------------

    public override async Task<string> GenerateAsync(string instruction, string? text = null, byte[]? png = null)
    {
        var prompt = text is null ? instruction : $"{instruction}\n\n{text}";
        JsonNode content = prompt;
        if (png is not null)
        {
            content = new JsonArray(
                new JsonObject { ["type"] = "text", ["text"] = prompt },
                new JsonObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JsonObject { ["url"] = "data:image/png;base64," + Convert.ToBase64String(png) }
                });
        }

        var messages = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = content });
        var reply = await ChatAsync(png is null ? _model : _visionModel, messages, tools: null);
        return Clean(reply["content"]?.GetValue<string>());
    }

    // ---- HTTP and friendly errors -------------------------------------------------------------

    private async Task<JsonObject> ChatAsync(string model, JsonArray messages, JsonArray? tools,
        IProgress<string>? partial = null, CancellationToken cancel = default)
    {
        var streaming = partial is not null;
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages,
            ["stream"] = streaming
        };
        // Local models do best with a low temperature for tool calls; hosted models choose their own (some reject the field).
        if (!_cloud) body["temperature"] = tools is null ? 0.3 : 0.1;
        if (tools is not null) body["tools"] = tools;
        LlmDebug.Write($"local request ({tools?.Count ?? 0} tools, {(streaming ? "streaming" : "one piece")})", Regex.Replace(body.ToJsonString(), "base64,[A-Za-z0-9+/=]{100,}", "base64,<image omitted>"));

        HttpStatusCode status;
        string text;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions") { Content = JsonContent.Create(body) };
            if (_apiKey.Length > 0) request.Headers.Authorization = new("Bearer", _apiKey);
            using var resp = await _http.SendAsync(request, streaming ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead, cancel);
            status = resp.StatusCode;

            // A server that ignores "stream" answers with plain JSON, which is read below as before.
            if (status == HttpStatusCode.OK && streaming && resp.Content.Headers.ContentType?.MediaType == "text/event-stream")
            {
                var builder = new OpenAiMessageBuilder();
                await foreach (var data in ServerSentEvents.ReadAsync(await resp.Content.ReadAsStreamAsync(cancel), cancel))
                    builder.Add(data, soFar => partial!.Report(OpenAiMessageBuilder.HideThinking(soFar)));
                LlmDebug.Write("local response 200 (streamed)", builder.Text);
                return builder.Build();
            }

            text = await resp.Content.ReadAsStringAsync(cancel);
            LlmDebug.Write($"local response {(int)status}", text);
        }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new LlmException(_cloud
                ? $"{_label} took too long to answer. Try again."
                : "The local model took too long to answer (the first request can be slow while the model loads). Try again.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new LlmException(_cloud
                ? $"Can't reach {_label}. Check your internet connection and the address in Settings."
                : $"Can't reach the local model server at {_baseUrl}. Is Ollama or LM Studio running?");
        }

        if (status == HttpStatusCode.OK)
        {
            return JsonNode.Parse(text)?["choices"]?[0]?["message"]?.AsObject()
                ?? throw new LlmException("The local model returned no answer.");
        }
        throw new LlmException(Describe(status, text, model, _cloud ? _label : null));
    }

    internal static string Describe(HttpStatusCode status, string body, string model, string? cloudLabel = null)
    {
        var message = ErrorMessage(body);
        if (cloudLabel is not null)
        {
            if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return $"{cloudLabel} rejected the API key. Check it in Settings, AI model.";
            if (status == HttpStatusCode.TooManyRequests)
                return $"{cloudLabel} is limiting requests (rate limit or no credit left). Wait a moment, or check your account.";
            if (status == HttpStatusCode.NotFound || message.Contains("model", StringComparison.OrdinalIgnoreCase) && message.Contains("not", StringComparison.OrdinalIgnoreCase))
                return $"{cloudLabel} doesn't have a model called '{model}', or the key can't use it. Check the model name in Settings.";
            if ((int)status >= 500) return $"{cloudLabel} is busy or having trouble right now. Try again in a moment.";
            return $"{cloudLabel} error {(int)status}: {(message.Length > 300 ? message[..300] + "…" : message)}";
        }
        if (message.Contains("support tools", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("tool", StringComparison.OrdinalIgnoreCase) && message.Contains("not support", StringComparison.OrdinalIgnoreCase))
            return $"'{model}' doesn't support tool calling, so it can't run automations. Use a Qwen build with tool support (qwen2.5 or qwen3).";
        if (message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return $"The server doesn't have a model called '{model}'. Open Settings and press Detect to see what's installed.";
        if (message.Contains("image", StringComparison.OrdinalIgnoreCase))
            return "This local model can't read images. Set a vision model (for example qwen2.5vl) in Settings, or use Gemini for screenshots.";
        return $"Local model error {(int)status}: {message}";
    }

    private static string ErrorMessage(string body)
    {
        try
        {
            var error = JsonNode.Parse(body)?["error"];
            return error switch
            {
                JsonObject o => o["message"]?.GetValue<string>() ?? body,
                null => body,
                _ => error.GetValue<string>()
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return body; }
    }

    // OpenAI servers send arguments as a JSON string; some servers send an object.
    private static JsonObject ParseArguments(JsonNode? node)
    {
        try
        {
            return node switch
            {
                JsonObject o => (JsonObject)o.DeepClone(),
                JsonValue v when v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s)
                    => JsonNode.Parse(s)?.AsObject() ?? new JsonObject(),
                _ => new JsonObject()
            };
        }
        catch (JsonException) { return new JsonObject(); }
    }

    // Qwen3 may include its reasoning in <think> tags; the user only wants the answer.
    private static string Clean(string? content) => Thinking.Replace(content ?? "", "").Trim();

    // ---- Persisted conversation context ---------------------------------------------------------

    public override bool IsCloud => _cloud;

    public override void ClearHistory()
    {
        _history.Clear();
    }

    public override void SeedHistory(IReadOnlyList<(string Role, string Text)> messages)
    {
        _history.Clear();
        foreach (var (role, text) in messages) _history.Add(new JsonObject { ["role"] = role, ["content"] = text });
        TrimHistory();
    }

    private void TrimHistory()
    {
        while (_history.Count > MaxHistoryEntries) _history.RemoveAt(0);
        // Start on a plain user message so a tool result never appears without its call.
        while (_history.Count > 0 && _history[0]?["role"]?.GetValue<string>() != "user") _history.RemoveAt(0);
    }


}
