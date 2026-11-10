using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using WinCompanion.Services;
using Xunit;

namespace WinCompanion.UnitTests;

public class OpenAiStreamTests
{
    private static OpenAiMessageBuilder Feed(IEnumerable<string> chunks, List<string>? seen = null)
    {
        var builder = new OpenAiMessageBuilder();
        foreach (var chunk in chunks) builder.Add(chunk, seen is null ? null : seen.Add);
        return builder;
    }

    [Fact]
    public void BuildsTheAnswerWordByWordAndReportsTheTextSoFar()
    {
        var seen = new List<string>();
        var builder = Feed(
        [
            """{"choices":[{"delta":{"role":"assistant","content":"Hel"}}]}""",
            """{"choices":[{"delta":{"content":"lo"}}]}""",
            """{"choices":[{"delta":{"content":" there"},"finish_reason":"stop"}]}""",
            "[DONE]"
        ], seen);

        Assert.Equal(["Hel", "Hello", "Hello there"], seen);
        var message = builder.Build();
        Assert.Equal("assistant", message["role"]!.GetValue<string>());
        Assert.Equal("Hello there", message["content"]!.GetValue<string>());
        Assert.Null(message["tool_calls"]);
    }

    [Fact]
    public void AssemblesAToolCallFromFragmentsOfItsArguments()
    {
        var message = Feed(
        [
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_7","type":"function","function":{"name":"create_folder","arguments":""}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"path\":"}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"D:\\\\Work\\\\new\"}"}}]},"finish_reason":"tool_calls"}]}""",
            "[DONE]"
        ]).Build();

        var call = Assert.Single(message["tool_calls"]!.AsArray())!;
        Assert.Equal("call_7", call["id"]!.GetValue<string>());
        Assert.Equal("function", call["type"]!.GetValue<string>());
        Assert.Equal("create_folder", call["function"]!["name"]!.GetValue<string>());
        // The pieces join into valid JSON that the rest of the app reads exactly as it reads a one-piece reply.
        var args = JsonNode.Parse(call["function"]!["arguments"]!.GetValue<string>())!;
        Assert.Equal(@"D:\Work\new", args["path"]!.GetValue<string>());
    }

    [Fact]
    public void KeepsSeveralToolCallsApartInOrder()
    {
        var message = Feed(
        [
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"a","function":{"name":"add_note","arguments":"{\"text\":\"one\"}"}},{"index":1,"id":"b","function":{"name":"add_note","arguments":"{\"text\":\"two\"}"}}]}}]}"""
        ]).Build();

        var calls = message["tool_calls"]!.AsArray();
        Assert.Equal(2, calls.Count);
        Assert.Equal(["a", "b"], calls.Select(c => c!["id"]!.GetValue<string>()));
        Assert.Equal("two", JsonNode.Parse(calls[1]!["function"]!["arguments"]!.GetValue<string>())!["text"]!.GetValue<string>());
    }

    [Fact]
    public void ARepeatedNameIsNotDoubled()
    {
        var message = Feed(
        [
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"x","function":{"name":"get_weather","arguments":"{"}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"name":"get_weather","arguments":"}"}}]}}]}"""
        ]).Build();
        Assert.Equal("get_weather", message["tool_calls"]![0]!["function"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void AcceptsArgumentsSentAsAnObjectAndFillsInAMissingId()
    {
        var message = Feed(["""{"choices":[{"delta":{"tool_calls":[{"function":{"name":"set_theme","arguments":{"mode":"toggle"}}}]}}]}"""]).Build();
        var call = message["tool_calls"]![0]!;
        Assert.Equal("call_0", call["id"]!.GetValue<string>());
        Assert.Equal("toggle", JsonNode.Parse(call["function"]!["arguments"]!.GetValue<string>())!["mode"]!.GetValue<string>());
    }

    [Fact]
    public void ACallWithNoArgumentsGetsAnEmptyObject()
    {
        var message = Feed(["""{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"c","function":{"name":"list_reminders"}}]}}]}"""]).Build();
        Assert.Equal("{}", message["tool_calls"]![0]!["function"]!["arguments"]!.GetValue<string>());
    }

    [Fact]
    public void TextAndAToolCallCanComeTogether()
    {
        var message = Feed(
        [
            """{"choices":[{"delta":{"content":"Let me check."}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"c","function":{"name":"get_system_info","arguments":"{}"}}]}}]}"""
        ]).Build();
        Assert.Equal("Let me check.", message["content"]!.GetValue<string>());
        Assert.Single(message["tool_calls"]!.AsArray());
    }

    [Fact]
    public void IgnoresKeepAlivesNullsAndUnknownChunks()
    {
        var message = Feed(
        [
            "not json at all",
            """{"choices":[]}""",
            """{"id":"x","object":"chat.completion.chunk"}""",
            """{"choices":[{"delta":{"content":null}}]}""",
            """{"choices":[{"delta":{"content":"ok"}}]}"""
        ]).Build();
        Assert.Equal("ok", message["content"]!.GetValue<string>());
    }

    [Fact]
    public void AnErrorInTheStreamBecomesAFriendlyException()
    {
        var builder = new OpenAiMessageBuilder();
        var ex = Assert.Throws<LlmException>(() => builder.Add("""{"error":{"message":"model not found"}}"""));
        Assert.Equal("model not found", ex.Message);
    }

    [Theory]
    [InlineData("Hello", "Hello")]
    [InlineData("<think>hmm</think>Answer", "Answer")]
    [InlineData("<think>still thinking about", "")]
    [InlineData("Answer<think>half", "Answer")]
    [InlineData("<think>a</think>One <think>b</think>Two", "One Two")]
    [InlineData("", "")]
    public void ReasoningNeverFlashesUp(string streamed, string shown) => Assert.Equal(shown, OpenAiMessageBuilder.HideThinking(streamed));
}

public class GeminiStreamTests
{
    private static GeminiMessageBuilder Feed(IEnumerable<string> chunks, List<string>? seen = null)
    {
        var builder = new GeminiMessageBuilder();
        foreach (var chunk in chunks) builder.Add(chunk, seen is null ? null : seen.Add);
        return builder;
    }

    [Fact]
    public void JoinsTextPiecesIntoOneTextPart()
    {
        var seen = new List<string>();
        var content = Feed(
        [
            """{"candidates":[{"content":{"role":"model","parts":[{"text":"Hel"}]}}]}""",
            """{"candidates":[{"content":{"role":"model","parts":[{"text":"lo"}]},"finishReason":"STOP"}]}"""
        ], seen).Build();

        Assert.Equal(["Hel", "Hello"], seen);
        Assert.Equal("model", content["role"]!.GetValue<string>());
        var part = Assert.Single(content["parts"]!.AsArray())!;
        Assert.Equal("Hello", part["text"]!.GetValue<string>());
    }

    [Fact]
    public void KeepsAFunctionCallExactlyAsSent()
    {
        var content = Feed(
        [
            """{"candidates":[{"content":{"role":"model","parts":[{"text":"Checking."}]}}]}""",
            """{"candidates":[{"content":{"role":"model","parts":[{"functionCall":{"name":"create_folder","args":{"path":"D:\\x"}},"thoughtSignature":"abc123"}]}}]}"""
        ]).Build();

        var parts = content["parts"]!.AsArray();
        Assert.Equal(2, parts.Count);
        Assert.Equal("Checking.", parts[0]!["text"]!.GetValue<string>());
        Assert.Equal("create_folder", parts[1]!["functionCall"]!["name"]!.GetValue<string>());
        Assert.Equal(@"D:\x", parts[1]!["functionCall"]!["args"]!["path"]!.GetValue<string>());
        Assert.Equal("abc123", parts[1]!["thoughtSignature"]!.GetValue<string>()); // later requests need it back unchanged
    }

    [Fact]
    public void TextAfterAFunctionCallStartsANewPart()
    {
        var content = Feed(
        [
            """{"candidates":[{"content":{"parts":[{"functionCall":{"name":"a","args":{}}}]}}]}""",
            """{"candidates":[{"content":{"parts":[{"text":"Done."}]}}]}"""
        ]).Build();
        Assert.Equal(2, content["parts"]!.AsArray().Count);
        Assert.Equal("Done.", content["parts"]![1]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void AnAnswerWithNothingInItSaysSoClearly()
    {
        var builder = Feed(["""{"promptFeedback":{"blockReason":"SAFETY"}}""", """{"candidates":[]}"""]);
        var ex = Assert.Throws<LlmException>(() => builder.Build());
        Assert.Contains("no answer", ex.Message);
    }

    [Fact]
    public void AnErrorInTheStreamBecomesAFriendlyException() =>
        Assert.Equal("quota", Assert.Throws<LlmException>(() => new GeminiMessageBuilder().Add("""{"error":{"message":"quota"}}""")).Message);

    [Fact]
    public void IgnoresChunksThatAreNotJson()
    {
        var content = Feed(["???", """{"candidates":[{"content":{"parts":[{"text":"ok"}]}}]}"""]).Build();
        Assert.Equal("ok", content["parts"]![0]!["text"]!.GetValue<string>());
    }
}

public class ServerSentEventsTests
{
    private static async Task<List<string>> Read(string body, CancellationToken cancel = default)
    {
        var list = new List<string>();
        await foreach (var data in ServerSentEvents.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(body)), cancel)) list.Add(data);
        return list;
    }

    [Fact]
    public async Task YieldsOnlyTheDataLines()
    {
        var data = await Read(": keep-alive\n\nevent: message\ndata: {\"a\":1}\n\ndata:{\"b\":2}\r\n\nid: 7\ndata: [DONE]\n\n");
        Assert.Equal(["{\"a\":1}", "{\"b\":2}", "[DONE]"], data);
    }

    [Fact]
    public async Task EmptyAndNonSseBodiesGiveNothing()
    {
        Assert.Empty(await Read(""));
        Assert.Empty(await Read("{\"choices\":[]}")); // plain JSON, not a stream
    }

    [Fact]
    public async Task StopsWhenCancelled()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Read("data: x\n", stop.Token));
    }

    // The whole path a streamed reply takes: bytes from the server, to events, to a finished message the chat loop understands.
    [Fact]
    public async Task ARealisticStreamBecomesAFinishedMessage()
    {
        const string body = """
            data: {"id":"c1","choices":[{"index":0,"delta":{"role":"assistant","content":""}}]}

            data: {"id":"c1","choices":[{"index":0,"delta":{"content":"Your biggest file"}}]}

            data: {"id":"c1","choices":[{"index":0,"delta":{"content":" is setup.iso."}}]}

            data: {"id":"c1","choices":[{"index":0,"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            """;
        var builder = new OpenAiMessageBuilder();
        var shown = new List<string>();
        await foreach (var data in ServerSentEvents.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(body))))
            builder.Add(data, shown.Add);

        Assert.Equal(["Your biggest file", "Your biggest file is setup.iso."], shown);
        Assert.Equal("Your biggest file is setup.iso.", builder.Build()["content"]!.GetValue<string>());
    }
}
