using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using WinCompanion.Tools;

namespace WinCompanion.Services;

/// <summary>
/// Claude through the official Anthropic SDK: the same tool-calling chat as the other models, with the reply streamed as it is
/// written. The conversation is kept in the API's own message format so it survives a restart.
/// </summary>
public sealed class ClaudeClient : LlmClientBase
{
    private const int MaxOutputTokens = 16000;

    private readonly AnthropicClient _api;
    private readonly string _model;
    private readonly JsonArray _history = new();


    public ClaudeClient(string apiKey, AppSettings settings, ToolRegistry tools, Func<string, Task<bool>> confirm, ActionLog log,
        MemoryStore memory, Func<ActiveAppInfo?> activeApp)
        : base(tools, confirm, log, memory, activeApp)
    {
        _api = new AnthropicClient { ApiKey = apiKey };
        _model = string.IsNullOrWhiteSpace(settings.ClaudeModel) ? AppSettings.DefaultClaudeModel : settings.ClaudeModel.Trim();
    }

    public override bool IsCloud => true;

    // ---- Chat with tools ----------------------------------------------------------------------------

    public override async Task<string> SendAsync(string userText, IProgress<string>? partial = null, CancellationToken cancel = default)
    {
        var startCount = _history.Count;
        _history.Add(new JsonObject { ["role"] = "user", ["content"] = userText });

        try
        {
            var declarations = ToolDefinitions(Tools.OpenAiDeclarations());
            for (var round = 0; round < MaxToolRounds; round++)
            {
                cancel.ThrowIfCancellationRequested();
                partial?.Report(""); // a new round: whatever was shown belongs to the step that just ended

                var blocks = await StreamOneAsync(new MessageCreateParams
                {
                    Model = _model,
                    MaxTokens = MaxOutputTokens,
                    System = SystemPrompt(),
                    Tools = declarations,
                    Messages = ToMessages(_history)
                }, partial, cancel);

                var content = new JsonArray(blocks.Select(b => (JsonNode)b).ToArray());
                var text = string.Concat(blocks.Where(b => b["type"]?.GetValue<string>() == "text").Select(b => b["text"]?.GetValue<string>()));
                var calls = blocks.Where(b => b["type"]?.GetValue<string>() == "tool_use").ToList();

                if (content.Count == 0)
                    throw new LlmException("Claude sent back an empty answer. Try again.");
                _history.Add(new JsonObject { ["role"] = "assistant", ["content"] = content });

                if (calls.Count == 0) return text.Trim().Length > 0 ? text.Trim() : "I have nothing to add.";

                var results = new JsonArray();
                foreach (var call in calls)
                {
                    var name = call["name"]?.GetValue<string>() ?? "";
                    var args = call["input"] as JsonObject ?? new JsonObject();
                    NotifyToolCall(name, args);
                    var result = await RunToolAsync(name, (JsonObject)args.DeepClone());
                    results.Add(new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = call["id"]?.GetValue<string>(),
                        ["content"] = result.Length == 0 ? "(no output)" : result
                    });
                }
                _history.Add(new JsonObject { ["role"] = "user", ["content"] = results });
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

    // One request, streamed. Returns the assistant's content blocks in the API's wire format.
    private async Task<List<JsonObject>> StreamOneAsync(MessageCreateParams request, IProgress<string>? partial, CancellationToken cancel)
    {
        var blocks = new SortedDictionary<long, Block>();
        try
        {
            await foreach (var e in _api.Messages.CreateStreaming(request, cancel))
            {
                if (e.TryPickContentBlockStart(out var start))
                {
                    var b = new Block();
                    if (start.ContentBlock.TryPickText(out var text)) { b.Type = "text"; b.Text.Append(text.Text); }
                    else if (start.ContentBlock.TryPickToolUse(out var use)) { b.Type = "tool_use"; b.Id = use.ID; b.Name = use.Name; }
                    else if (start.ContentBlock.TryPickThinking(out var thought)) { b.Type = "thinking"; b.Text.Append(thought.Thinking); b.Signature = thought.Signature; }
                    else if (start.ContentBlock.TryPickRedactedThinking(out var hidden)) { b.Type = "redacted_thinking"; b.Id = hidden.Data; }
                    else continue; // a server tool or other block this assistant never asks for
                    blocks[start.Index] = b;
                }
                else if (e.TryPickContentBlockDelta(out var delta) && blocks.TryGetValue(delta.Index, out var block))
                {
                    if (delta.Delta.TryPickText(out var t))
                    {
                        block.Text.Append(t.Text);
                        partial?.Report(AnswerSoFar(blocks.Values));
                    }
                    else if (delta.Delta.TryPickInputJson(out var json)) block.Json.Append(json.PartialJson);
                    else if (delta.Delta.TryPickThinking(out var th)) block.Text.Append(th.Thinking);
                    else if (delta.Delta.TryPickSignature(out var sig)) block.Signature += sig.Signature;
                }
            }
        }
        catch (AnthropicApiException ex) { throw new LlmException(Describe(ex)); }
        catch (TaskCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new LlmException("Claude took too long to answer. Try again.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new LlmException("Can't reach Claude. Check your internet connection.");
        }

        return blocks.Values.Select(b => b.ToWire()).OfType<JsonObject>().ToList();
    }

    private static string AnswerSoFar(IEnumerable<Block> blocks) =>
        string.Concat(blocks.Where(b => b.Type == "text").Select(b => b.Text.ToString()));

    private sealed class Block
    {
        public string Type = "";
        public string Id = "", Name = "", Signature = "";
        public readonly StringBuilder Text = new(), Json = new();

        public JsonObject? ToWire() => Type switch
        {
            "text" when Text.Length > 0 => new JsonObject { ["type"] = "text", ["text"] = Text.ToString() },
            "thinking" => new JsonObject { ["type"] = "thinking", ["thinking"] = Text.ToString(), ["signature"] = Signature },
            "redacted_thinking" => new JsonObject { ["type"] = "redacted_thinking", ["data"] = Id },
            "tool_use" => new JsonObject { ["type"] = "tool_use", ["id"] = Id, ["name"] = Name, ["input"] = ParseInput(Json.ToString()) },
            _ => null
        };
    }

    internal static JsonObject ParseInput(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        try { return JsonNode.Parse(json) as JsonObject ?? new JsonObject(); }
        catch (JsonException) { return new JsonObject(); }
    }

    // ---- Converting to and from the SDK's types -------------------------------------------------------

    /// <summary>The tools in the API's shape: a name, a description and a JSON schema.</summary>
    internal static List<ToolUnion> ToolDefinitions(JsonArray openAiTools)
    {
        var list = new List<ToolUnion>();
        foreach (var node in openAiTools)
        {
            var function = node?["function"];
            var parameters = function?["parameters"] as JsonObject;
            if (function?["name"]?.GetValue<string>() is not { Length: > 0 } name) continue;

            var properties = new Dictionary<string, JsonElement>();
            if (parameters?["properties"] is JsonObject props)
                foreach (var (key, value) in props) properties[key] = JsonSerializer.SerializeToElement(value);

            var required = (parameters?["required"] as JsonArray)?.Select(r => r?.GetValue<string>()).OfType<string>().ToList();
            list.Add(new Tool
            {
                Name = name,
                Description = function["description"]?.GetValue<string>() ?? "",
                InputSchema = new() { Properties = properties, Required = required is { Count: > 0 } ? required : null }
            });
        }
        return list;
    }

    internal static List<MessageParam> ToMessages(JsonArray history)
    {
        var messages = new List<MessageParam>();
        foreach (var entry in history.OfType<JsonObject>())
        {
            var role = entry["role"]?.GetValue<string>();
            if (entry["content"] is JsonValue plain && plain.TryGetValue<string>(out var text))
            {
                messages.Add(role == "assistant"
                    ? new AssistantMessageParam { Content = text }
                    : new UserMessageParam { Content = text });
                continue;
            }
            if (entry["content"] is not JsonArray array) continue;

            var blocks = array.OfType<JsonObject>().Select(ToBlock).OfType<ContentBlockParam>().ToList();
            if (blocks.Count == 0) continue;
            messages.Add(role == "assistant"
                ? new AssistantMessageParam { Content = blocks }
                : new UserMessageParam { Content = blocks });
        }
        return messages;
    }

    private static ContentBlockParam? ToBlock(JsonObject block) => block["type"]?.GetValue<string>() switch
    {
        "text" => new TextBlockParam { Text = block["text"]?.GetValue<string>() ?? "" },
        "thinking" => new ThinkingBlockParam
        {
            Thinking = block["thinking"]?.GetValue<string>() ?? "",
            Signature = block["signature"]?.GetValue<string>() ?? ""
        },
        "redacted_thinking" => new RedactedThinkingBlockParam { Data = block["data"]?.GetValue<string>() ?? "" },
        "tool_use" => new ToolUseBlockParam
        {
            ID = block["id"]?.GetValue<string>() ?? "",
            Name = block["name"]?.GetValue<string>() ?? "",
            Input = ((block["input"] as JsonObject) ?? new JsonObject())
                .ToDictionary(kv => kv.Key, kv => JsonSerializer.SerializeToElement(kv.Value))
        },
        "tool_result" => new ToolResultBlockParam
        {
            ToolUseID = block["tool_use_id"]?.GetValue<string>() ?? "",
            Content = block["content"]?.GetValue<string>() ?? ""
        },
        _ => null
    };

    // ---- One-shot generation (no tools, no chat history) ------------------------------------------------

    public override async Task<string> GenerateAsync(string instruction, string? text = null, byte[]? png = null)
    {
        var prompt = text is null ? instruction : $"{instruction}\n\n{text}";
        MessageParam message;
        if (png is null)
        {
            message = new UserMessageParam { Content = prompt };
        }
        else
        {
            List<ContentBlockParam> parts =
            [
                new ImageBlockParam { Source = new Base64ImageSource { Data = Convert.ToBase64String(png), MediaType = MediaType.ImagePng } },
                new TextBlockParam { Text = prompt }
            ];
            message = new UserMessageParam { Content = parts };
        }

        try
        {
            var response = await _api.Messages.Create(new MessageCreateParams
            {
                Model = _model,
                MaxTokens = MaxOutputTokens,
                Messages = [message]
            });
            return string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)).Trim();
        }
        catch (AnthropicApiException ex) { throw new LlmException(Describe(ex)); }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new LlmException("Can't reach Claude. Check your internet connection.");
        }
    }

    // ---- Friendly errors ---------------------------------------------------------------------------------------

    internal static string Describe(AnthropicApiException ex) => Describe((int)ex.StatusCode, ex.Message);

    internal static string Describe(int status, string message) => status switch
    {
        401 => "Claude rejected the API key. Check it in Settings, AI model.",
        403 => "This API key isn't allowed to use that model or feature. Check your Anthropic account.",
        404 => "Claude doesn't know that model name. Check the model in Settings, AI model.",
        429 => "Claude is limiting requests (rate limit or usage limit). Wait a moment and try again.",
        >= 500 => "Claude is busy or having trouble right now. Try again in a moment.",
        _ => $"Claude returned an error ({status}): {(message.Length > 300 ? message[..300] + "…" : message)}"
    };

    // ---- Persisted conversation context -------------------------------------------------------------------------

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
        // Start on what the user typed, so a tool result never appears without the call that asked for it.
        while (_history.Count > 0 && !IsUserText(_history[0])) _history.RemoveAt(0);
    }

    private static bool IsUserText(JsonNode? entry) =>
        entry?["role"]?.GetValue<string>() == "user" && entry["content"] is JsonValue;


}
