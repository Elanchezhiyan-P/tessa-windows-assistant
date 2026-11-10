using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using WinCompanion.Services;
using WinCompanion.Tools;
using Xunit;

namespace WinCompanion.UnitTests;

/// <summary>A tiny OpenAI-style server on this PC that records what it was sent.</summary>
internal sealed class FakeChatServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Task _loop;
    public string BaseUrl { get; }
    public List<(string? Auth, JsonObject Body)> Requests { get; } = [];
    public int Status { get; set; } = 200;
    public string Reply { get; set; } = """{"choices":[{"message":{"role":"assistant","content":"Hi there"}}]}""";

    public FakeChatServer()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        BaseUrl = $"http://127.0.0.1:{port}/v1";
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _loop = Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }
                using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                var raw = await reader.ReadToEndAsync();
                var body = (raw.Length > 0 ? JsonNode.Parse(raw) as JsonObject : null) ?? new JsonObject();
                lock (Requests) Requests.Add((ctx.Request.Headers["Authorization"], body));
                var bytes = Encoding.UTF8.GetBytes(Reply);
                ctx.Response.StatusCode = Status;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        });
    }

    public void Dispose()
    {
        _listener.Close();
        try { _loop.Wait(1000); } catch (AggregateException) { }
    }
}

public class OpenAiCompatibleTests : DatabaseTestBase
{
    private static LocalLlmClient Client(OpenAiEndpoint endpoint) =>
        new(endpoint, new ToolRegistry(), _ => Task.FromResult(true), new ActionLog(), new MemoryStore(), () => null);

    private static OpenAiEndpoint Cloud(string url, string key, string file) =>
        new(url, "some-model", "", key, true, file, "api.example.com");

    private static string Unique() => $"context.test-{Guid.NewGuid():N}.json";

    private static void Cleanup(string file) { try { File.Delete(AppPaths.FileIn(file)); } catch (IOException) { } }

    [Fact]
    public async Task ACloudServiceGetsTheKeyAndNoTemperature()
    {
        using var server = new FakeChatServer();
        var file = Unique();
        try
        {
            var client = Client(Cloud(server.BaseUrl, "sk-secret-123", file));
            var reply = await client.SendAsync("hello there");

            Assert.Equal("Hi there", reply);
            Assert.True(client.IsCloud);
            var (auth, body) = server.Requests.Single(r => r.Body["tools"] is not null || r.Body["messages"] is not null);
            Assert.Equal("Bearer sk-secret-123", auth);
            Assert.Null(body["temperature"]); // some hosted models reject the field
            Assert.Equal("some-model", body["model"]!.GetValue<string>());
        }
        finally { Cleanup(file); }
    }

    [Fact]
    public async Task ALocalServerGetsNoKeyAndAFixedLowTemperature()
    {
        using var server = new FakeChatServer();
        var file = Unique();
        try
        {
            var client = Client(new OpenAiEndpoint(server.BaseUrl, "qwen", "", null, false, file, "the local model server"));
            await client.SendAsync("hello there");

            var (auth, body) = server.Requests.First();
            Assert.Null(auth);
            Assert.False(client.IsCloud);
            Assert.NotNull(body["temperature"]);
        }
        finally { Cleanup(file); }
    }

    [Theory]
    [InlineData(401, "rejected the API key")]
    [InlineData(403, "rejected the API key")]
    [InlineData(429, "limiting requests")]
    [InlineData(404, "doesn't have a model")]
    [InlineData(503, "busy")]
    public async Task ACloudFailureIsExplainedInPlainWords(int status, string expected)
    {
        using var server = new FakeChatServer { Status = status, Reply = """{"error":{"message":"nope"}}""" };
        var file = Unique();
        try
        {
            var client = Client(Cloud(server.BaseUrl, "sk-x", file));
            var error = await Assert.ThrowsAsync<LlmException>(() => client.SendAsync("hello there"));
            Assert.Contains(expected, error.Message);
            Assert.Contains("api.example.com", error.Message);
            Assert.DoesNotContain("sk-x", error.Message);
        }
        finally { Cleanup(file); }
    }

    [Fact]
    public async Task EachServiceKeepsItsOwnConversationFile()
    {
        using var server = new FakeChatServer();
        var a = Unique();
        var b = Unique();
        try
        {
            await Client(Cloud(server.BaseUrl, "k", a)).SendAsync("first topic");
            var second = Client(Cloud(server.BaseUrl, "k", b));
            await second.SendAsync("other topic");

            var last = server.Requests.Last().Body["messages"]!.AsArray();
            Assert.DoesNotContain(last, m => m?["content"]?.ToString() == "first topic");
            Assert.True(File.Exists(AppPaths.FileIn(a)) && File.Exists(AppPaths.FileIn(b)));
        }
        finally { Cleanup(a); Cleanup(b); }
    }

    [Fact]
    public void LocalDefaultsKeepTheOriginalContextFile()
    {
        var endpoint = LocalLlmClient.ForLocal(new AppSettings());
        Assert.Equal("context.local.json", endpoint.ContextFile);
        Assert.False(endpoint.Cloud);
        Assert.Null(endpoint.ApiKey);
        var cloud = LocalLlmClient.ForOpenAi(new AppSettings { OpenAiBaseUrl = "https://api.groq.com/openai/v1" }, "k");
        Assert.Equal("context.openai.json", cloud.ContextFile);
        Assert.Equal("api.groq.com", cloud.Label);
    }

    [Fact]
    public async Task ModelListingSendsTheKey()
    {
        using var server = new FakeChatServer { Reply = """{"data":[{"id":"m1"},{"id":"m2"}]}""" };
        var models = await LocalLlmClient.ListModelsAsync(server.BaseUrl, "sk-list");
        Assert.Equal(["m1", "m2"], models);
    }
}

public class ProviderSettingsTests
{
    [Theory]
    [InlineData("gemini", "Google's Gemini")]
    [InlineData("claude", "Anthropic's Claude")]
    [InlineData("local", "")]
    public void EachProviderHasAnAccurateCloudLabel(string provider, string label) =>
        Assert.Equal(label, new AppSettings { Provider = provider }.CloudLabel);

    [Fact]
    public void AnOpenAiCompatibleServiceIsNamedByItsHost()
    {
        var settings = new AppSettings { Provider = "openai", OpenAiBaseUrl = "https://openrouter.ai/api/v1", OpenAiModel = "m" };
        Assert.Equal("openrouter.ai", settings.CloudLabel);
        Assert.Contains("OpenAI-compatible", settings.BrainDescription);
        Assert.Equal("m", settings.ModelBadge);
    }

    [Theory]
    [InlineData("claude", "claude-opus-5-5")]
    [InlineData("claude claude-sonnet-5-5", "claude-sonnet-5-5")]
    public void ModelCommandSwitchesProviderAndModel(string args, string expectedModel)
    {
        var settings = new AppSettings();
        var (provider, model) = ModelSwitch.Parse(args);
        Assert.Equal("claude", provider);
        var (reply, changed) = ModelSwitch.Apply(settings, provider!, model);
        Assert.True(changed);
        Assert.Equal("claude", settings.Provider);
        Assert.Equal(expectedModel, settings.ClaudeModel);
        Assert.Contains("Claude", reply);
    }

    [Theory]
    [InlineData("chatgpt", "openai")]
    [InlineData("anthropic", "claude")]
    [InlineData("LOCAL", "local")]
    [InlineData("bard", null)]
    [InlineData("", null)]
    public void ProviderNamesAreUnderstood(string args, string? expected) => Assert.Equal(expected, ModelSwitch.Parse(args).Provider);

    [Fact]
    public void ABadModelNameIsRefusedAndNothingChanges()
    {
        var settings = new AppSettings();
        var (reply, changed) = ModelSwitch.Apply(settings, "claude", "bad name\u0007");
        Assert.False(changed);
        Assert.Equal("gemini", settings.Provider);
        Assert.Contains("model name", reply);
        Assert.False(ModelSwitch.Apply(settings, "gemini", "gemini-9").Changed);
    }

    [Fact]
    public void SwitchingToTheSameModelSaysSo()
    {
        var settings = new AppSettings { Provider = "claude" };
        Assert.Equal("Already using that model.", ModelSwitch.Apply(settings, "claude", null).Reply);
    }

    [Fact]
    public void BackupNeverCarriesTheServiceAddressOrAKey()
    {
        Assert.Contains("OpenAiBaseUrl", Backup.NeverRestored);
        var names = typeof(AppSettings).GetProperties().Select(p => p.Name);
        Assert.DoesNotContain(names, n => n.Contains("Key", StringComparison.OrdinalIgnoreCase) && n != "MicrophoneDevice");
    }
}

public class ClaudeClientTests
{
    private static JsonArray History() => new(
        new JsonObject { ["role"] = "user", ["content"] = "open notepad" },
        new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = new JsonArray(
                new JsonObject { ["type"] = "thinking", ["thinking"] = "hmm", ["signature"] = "sig123" },
                new JsonObject { ["type"] = "text", ["text"] = "Opening it." },
                new JsonObject { ["type"] = "tool_use", ["id"] = "tu_1", ["name"] = "open_app", ["input"] = new JsonObject { ["name"] = "notepad" } })
        },
        new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = "tu_1", ["content"] = "Opened Notepad." })
        });

    [Fact]
    public void ToolsAreConvertedWithTheirSchema()
    {
        var openAi = new JsonArray(new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = "set_volume",
                ["description"] = "Set the volume",
                ["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject { ["level"] = new JsonObject { ["type"] = "integer" } },
                    ["required"] = new JsonArray("level")
                }
            }
        }, new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["description"] = "no name" } });

        var tools = ClaudeClient.ToolDefinitions(openAi);
        Assert.Single(tools); // the nameless one is dropped
        Assert.True(tools[0].TryPickTool(out var tool));
        Assert.Equal("set_volume", tool.Name);
        Assert.Equal("Set the volume", tool.Description);
        Assert.Contains("level", tool.InputSchema.Properties!.Keys);
        Assert.Equal(["level"], tool.InputSchema.Required);
    }

    [Fact]
    public void HistoryRoundTripsIntoTheSdkMessages()
    {
        var messages = ClaudeClient.ToMessages(History());
        Assert.Equal(3, messages.Count);
        Assert.True(messages[0].TryPickUser(out _));
        Assert.True(messages[1].TryPickAssistant(out var assistant));
        Assert.True(messages[2].TryPickUser(out _));
        // The text, the thinking block (with its signature) and the tool call all survive.
        var json = System.Text.Json.JsonSerializer.Serialize(assistant);
        Assert.Contains("sig123", json);
        Assert.Contains("tu_1", json);
        Assert.Contains("Opening it.", json);
    }

    [Fact]
    public void AToolWithNoArgumentsGetsAnEmptyObject()
    {
        Assert.Empty(ClaudeClient.ParseInput(""));
        Assert.Empty(ClaudeClient.ParseInput("not json"));
        Assert.Equal("x", ClaudeClient.ParseInput("{\"a\":\"x\"}")["a"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(401, "rejected the API key")]
    [InlineData(404, "model name")]
    [InlineData(429, "limiting requests")]
    [InlineData(529, "busy")]
    [InlineData(400, "error (400)")]
    public void FailuresAreExplained(int status, string expected) =>
        Assert.Contains(expected, ClaudeClient.Describe(status, "bad things"));
}
