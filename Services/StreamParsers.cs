using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WinCompanion.Services;

internal static class Json
{
    /// <summary>The first element of a JSON array, or null when it is missing or empty (an indexer on an empty array throws).</summary>
    public static JsonNode? First(JsonNode? array) => array is JsonArray { Count: > 0 } a ? a[0] : null;
}

/// <summary>Reads a server-sent-events body ("data: {...}" lines) and yields each data payload.</summary>
public static class ServerSentEvents
{
    public static async IAsyncEnumerable<string> ReadAsync(Stream body, [EnumeratorCancellation] CancellationToken cancel = default)
    {
        using var reader = new StreamReader(body, Encoding.UTF8);
        while (await reader.ReadLineAsync(cancel) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue; // blank separators, comments and "event:" lines
            var data = line[5..].TrimStart();
            if (data.Length > 0) yield return data;
        }
    }
}

/// <summary>
/// Builds one assistant message out of the chunks an OpenAI-compatible server (Ollama, LM Studio, llama.cpp, vLLM) streams, in the same
/// shape the non-streaming reply has, so the rest of the chat loop doesn't care how it arrived.
/// </summary>
public sealed class OpenAiMessageBuilder
{
    private readonly StringBuilder _text = new();
    private readonly SortedDictionary<int, (string? Id, StringBuilder Name, StringBuilder Arguments)> _calls = new();

    public string Text => _text.ToString();

    /// <summary>Adds one "data:" payload. <paramref name="onText"/> gets the whole answer so far whenever it grows.</summary>
    public void Add(string data, Action<string>? onText = null)
    {
        if (data == "[DONE]") return;

        JsonNode? chunk;
        try { chunk = JsonNode.Parse(data); }
        catch (JsonException) { return; } // a keep-alive or a partial line: ignore it

        if (chunk?["error"] is { } error)
            throw new LlmException(error is JsonObject o ? o["message"]?.GetValue<string>() ?? "The local model reported an error." : error.ToString());

        var delta = Json.First(chunk?["choices"])?["delta"];
        if (delta is null) return;

        if (delta["content"] is JsonValue content && content.TryGetValue<string>(out var piece) && piece.Length > 0)
        {
            _text.Append(piece);
            onText?.Invoke(_text.ToString());
        }

        if (delta["tool_calls"] is JsonArray calls)
        {
            for (var position = 0; position < calls.Count; position++)
            {
                var call = calls[position];
                var index = call?["index"]?.GetValue<int>() ?? position;
                if (!_calls.TryGetValue(index, out var entry)) entry = (null, new StringBuilder(), new StringBuilder());

                var id = call?["id"]?.GetValue<string>() ?? entry.Id;
                var function = call?["function"];
                // The name arrives whole in the first chunk; some servers repeat it, which must not double it.
                if (function?["name"]?.GetValue<string>() is { Length: > 0 } name && entry.Name.Length == 0) entry.Name.Append(name);
                switch (function?["arguments"])
                {
                    case JsonValue v when v.TryGetValue<string>(out var fragment): entry.Arguments.Append(fragment); break;
                    case JsonObject o: entry.Arguments.Append(o.ToJsonString()); break;
                }
                _calls[index] = (id, entry.Name, entry.Arguments);
            }
        }
    }

    /// <summary>The finished message: role, text and any tool calls.</summary>
    public JsonObject Build()
    {
        var message = new JsonObject { ["role"] = "assistant", ["content"] = _text.ToString() };
        if (_calls.Count > 0)
        {
            var array = new JsonArray();
            foreach (var (index, call) in _calls)
            {
                array.Add(new JsonObject
                {
                    ["id"] = call.Id ?? $"call_{index}",
                    ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = call.Name.ToString(), ["arguments"] = call.Arguments.Length > 0 ? call.Arguments.ToString() : "{}" }
                });
            }
            message["tool_calls"] = array;
        }
        return message;
    }

    /// <summary>
    /// Hides a model's reasoning while it streams: whole &lt;think&gt; blocks are removed, and so is an unfinished one at the end
    /// (otherwise its thoughts would flash up and then vanish).
    /// </summary>
    public static string HideThinking(string text)
    {
        text = Regex.Replace(text, @"<think>.*?</think>", "", RegexOptions.Singleline);
        var open = text.IndexOf("<think>", StringComparison.Ordinal);
        return (open >= 0 ? text[..open] : text).TrimStart();
    }
}

/// <summary>Builds Gemini's reply out of the chunks of a streamGenerateContent response, in the shape the one-piece reply has.</summary>
public sealed class GeminiMessageBuilder
{
    private readonly JsonArray _parts = new();
    private readonly StringBuilder _text = new();

    public string Text => _text.ToString();

    public void Add(string data, Action<string>? onText = null)
    {
        JsonNode? chunk;
        try { chunk = JsonNode.Parse(data); }
        catch (JsonException) { return; }

        if (chunk?["error"] is { } error)
            throw new LlmException(error["message"]?.GetValue<string>() ?? "Gemini reported an error.");

        if (Json.First(chunk?["candidates"])?["content"]?["parts"] is not JsonArray parts) return;
        foreach (var part in parts)
        {
            if (part is null) continue;
            if (part["text"] is JsonValue value && value.TryGetValue<string>(out var piece) && part["functionCall"] is null)
            {
                _text.Append(piece);
                // Consecutive text pieces are one text part, as in a reply that wasn't streamed.
                if (_parts.Count > 0 && _parts[^1] is JsonObject last && last["text"] is not null && last["functionCall"] is null)
                    last["text"] = _text.ToString();
                else
                    _parts.Add(new JsonObject { ["text"] = _text.ToString() });
                if (piece.Length > 0) onText?.Invoke(_text.ToString());
            }
            else
            {
                _parts.Add(part.DeepClone()); // a function call (with any signature Gemini attached) is kept exactly as sent
            }
        }
    }

    public JsonObject Build()
    {
        if (_parts.Count == 0) throw new LlmException("Gemini returned no answer (the request may have been blocked).");
        return new JsonObject { ["role"] = "model", ["parts"] = _parts.DeepClone() };
    }
}
