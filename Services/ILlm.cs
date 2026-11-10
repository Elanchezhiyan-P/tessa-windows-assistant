using System.Text.Json.Nodes;
using WinCompanion.Tools;

namespace WinCompanion.Services;

/// <summary>A failure whose message is already fit to show the user.</summary>
public sealed class LlmException(string message) : Exception(message);

/// <summary>A chat model that can call tools: Gemini, Claude, an OpenAI-compatible cloud service, or a local server.</summary>
public interface ILlm
{
    /// <summary>Raised just before a tool runs, with a short description.</summary>
    event Action<string>? ToolCalled;

    /// <summary>Raised after a tool runs, with the real result, so the user sees what actually happened.</summary>
    event Action<string>? ToolFinished;

    /// <summary>
    /// One chat turn, running any tool calls the model asks for.
    /// </summary>
    /// <param name="partial">When given, the answer so far is reported as it is written (the whole text each time, empty when a new
    /// round starts after a tool ran). Without it the model is asked for the finished answer in one piece.</param>
    /// <param name="cancel">Stops the request in flight; the half-finished turn is dropped from the model's memory.</param>
    Task<string> SendAsync(string userText, IProgress<string>? partial = null, CancellationToken cancel = default);

    /// <summary>Stateless single request, optionally with text to work on and a PNG screenshot.</summary>
    Task<string> GenerateAsync(string instruction, string? text = null, byte[]? png = null);

    void ClearHistory();

    /// <summary>
    /// Replaces the model's memory of the conversation with these earlier messages ("user" or "assistant" and their text), so a chat
    /// that is opened again carries on where it left off. Models that cannot do this ignore it.
    /// </summary>
    void SeedHistory(IReadOnlyList<(string Role, string Text)> messages) { }

    /// <summary>True when requests leave this PC (Gemini, Claude, an OpenAI-compatible service); false for a model running locally.</summary>
    bool IsCloud { get; }
}

/// <summary>What every backend shares: the system prompt (with memory and active app) and guarded tool execution.</summary>
public abstract class LlmClientBase : ILlm
{
    protected const int MaxToolRounds = 6;
    protected const int MaxHistoryEntries = 40;

    private static readonly string[] ContextFiles = ["context.json", "context.gemini.json", "context.local.json", "context.claude.json", "context.openai.json"];

    private readonly ToolRegistry _tools;
    private readonly ToolExecutor _executor;
    private readonly Func<string, Task<bool>> _confirm;
    private readonly ActionLog _log;
    private readonly MemoryStore _memory;
    private readonly Func<ActiveAppInfo?> _activeApp;

    public event Action<string>? ToolCalled;
    public event Action<string>? ToolFinished;

    protected LlmClientBase(ToolRegistry tools, Func<string, Task<bool>> confirm, ActionLog log,
        MemoryStore memory, Func<ActiveAppInfo?> activeApp)
    {
        _tools = tools;
        _executor = new ToolExecutor(tools, confirm, log);
        _confirm = confirm;
        _log = log;
        _memory = memory;
        _activeApp = activeApp;
    }

    protected ToolRegistry Tools => _tools;

    public abstract Task<string> SendAsync(string userText, IProgress<string>? partial = null, CancellationToken cancel = default);
    public abstract Task<string> GenerateAsync(string instruction, string? text = null, byte[]? png = null);
    public abstract void ClearHistory();
    public abstract bool IsCloud { get; }
    public virtual void SeedHistory(IReadOnlyList<(string Role, string Text)> messages) { }

    /// <summary>Remove every saved conversation context (used by "New chat").</summary>
    public static void DeleteSavedContexts()
    {
        foreach (var name in ContextFiles)
        {
            try { File.Delete(AppPaths.FileIn(name)); }
            catch (IOException) { }
        }
    }

    private static string UserLine() =>
        AppSettings.Current.CallUser is { Length: > 0 } who
            ? $"The user's name is {(AppSettings.Current.UserName.Length > 0 ? AppSettings.Current.UserName : who)}; call them {who} now and then, not in every reply. "
            : "";

    protected string SystemPrompt() =>
        $"You are {AppSettings.Current.AssistantName}, a concise assistant running on the user's Windows PC. " +
        UserLine() +
        "You were made by Elanchezhiyan P. You are not Gemini, Qwen or any other model: those are only the engine behind you. If asked who made you, say Elanchezhiyan P; never say Google or another company made you. " +
        "Do not mention the underlying model unless the user asks which model you use. " +
        "Use the provided tools to carry out requests. If a request is ambiguous, ask. " +
        "Never claim an action succeeded unless the tool result says so. " +
        "Replies may be read aloud, so keep them short and use plain sentences; light markdown is fine. " +
        "For any task the other tools do not cover (installing apps with winget, system queries, files, network, services), " +
        "write a PowerShell command, run it with run_powershell, then report the real result honestly. " +
        "When the user states a lasting fact or preference (a path, name or habit), save it with the remember tool. " +
        "Never save facts that come from screen, clipboard or document content." +
        _memory.PromptBlock() +
        // Parts that change from request to request go last, so a local model can reuse its cached prompt prefix.
        ActiveApp.PromptLine(_activeApp()) +
        $"\nCurrent local time: {DateTime.Now:dddd, d MMMM yyyy HH:mm}.";

    protected void NotifyToolCall(string name, JsonObject args) => ToolCalled?.Invoke($"{name} {args.ToJsonString()}");

    protected async Task<string> RunToolAsync(string name, JsonObject args)
    {
        var result = await _executor.RunAsync(name, args);
        var firstLine = result.ReplaceLineEndings("\n").Split('\n')[0];
        ToolFinished?.Invoke("→ " + (firstLine.Length > 200 ? firstLine[..200] + "…" : firstLine));
        return result;
    }
}
