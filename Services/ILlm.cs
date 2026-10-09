using System.Text.Json.Nodes;
using WinCompanion.Tools;

namespace WinCompanion.Services;

/// <summary>A failure whose message is already fit to show the user.</summary>
public sealed class LlmException(string message) : Exception(message);

/// <summary>A chat model that can call tools. Implemented for Gemini and for local OpenAI-compatible servers.</summary>
public interface ILlm
{
    /// <summary>Raised just before a tool runs, with a short description.</summary>
    event Action<string>? ToolCalled;

    /// <summary>Raised after a tool runs, with the real result, so the user sees what actually happened.</summary>
    event Action<string>? ToolFinished;

    /// <summary>One chat turn, running any tool calls the model asks for.</summary>
    Task<string> SendAsync(string userText);

    /// <summary>Stateless single request, optionally with text to work on and a PNG screenshot.</summary>
    Task<string> GenerateAsync(string instruction, string? text = null, byte[]? png = null);

    void ClearHistory();

    /// <summary>True when requests leave this PC (Gemini); false for a model running locally.</summary>
    bool IsCloud { get; }
}

/// <summary>What every backend shares: the system prompt (with memory and active app) and guarded tool execution.</summary>
public abstract class LlmClientBase : ILlm
{
    protected const int MaxToolRounds = 6;
    protected const int MaxHistoryEntries = 40;

    private static readonly string[] ContextFiles = ["context.json", "context.gemini.json", "context.local.json"];

    private readonly ToolRegistry _tools;
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
        _confirm = confirm;
        _log = log;
        _memory = memory;
        _activeApp = activeApp;
    }

    protected ToolRegistry Tools => _tools;

    public abstract Task<string> SendAsync(string userText);
    public abstract Task<string> GenerateAsync(string instruction, string? text = null, byte[]? png = null);
    public abstract void ClearHistory();
    public abstract bool IsCloud { get; }

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
        var tool = _tools.Find(name);
        if (tool is null)
        {
            var unknown = $"Unknown tool '{name}'.";
            _log.Write(name, args, "unknown", unknown);
            return unknown;
        }

        var needsConfirmation = tool.NeedsConfirmation(args);
        // Show a PowerShell command as plain text rather than escaped JSON.
        var detail = args["command"] is JsonValue v && v.TryGetValue<string>(out var command) ? command : args.ToJsonString();
        var approved = !needsConfirmation || await _confirm($"{name}\n{detail}");

        string result;
        if (!approved)
        {
            result = "The user declined this action.";
        }
        else
        {
            try { result = await tool.ExecuteAsync(args); }
            catch (Exception ex) { result = $"Error: {ex.Message}"; }
        }

        _log.Write(name, args, !needsConfirmation ? "auto" : approved ? "approved" : "declined", result);

        var firstLine = result.ReplaceLineEndings("\n").Split('\n')[0];
        ToolFinished?.Invoke("→ " + (firstLine.Length > 200 ? firstLine[..200] + "…" : firstLine));
        return result;
    }
}
