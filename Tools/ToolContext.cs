using System.Text.Json.Nodes;
using WinCompanion.Services;

namespace WinCompanion.Tools;

/// <summary>What tools can reach: the active model (for AI-powered tools) and the main window.</summary>
public sealed class ToolContext
{
    public ILlm? Llm { get; set; }
    public System.Windows.Window? Window { get; set; }
    public AppSettings Settings { get; set; } = new();

    /// <summary>Shows the approval card and returns the user's answer.</summary>
    public Func<string, Task<bool>> Confirm { get; set; } = _ => Task.FromResult(false);

    /// <summary>
    /// Call before handing private content (a screenshot, clipboard text, a file) to the AI. With a cloud model and the
    /// privacy setting on, the user is asked first; a local model keeps everything on this PC, so it never asks.
    /// </summary>
    public async Task<bool> AllowCloudAsync(string what)
    {
        if (Llm is not { IsCloud: true } || !Settings.AskBeforeSendingToCloud) return true;
        return await Confirm($"Send {what} to Google's Gemini?\nIt leaves this PC. You can turn this question off in Settings > Privacy.");
    }
}

/// <summary>Helpers for declaring tool argument schemas.</summary>
internal static class ToolSchema
{
    public static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };
    public static JsonObject Bool(string description) => new() { ["type"] = "boolean", ["description"] = description };
    public static JsonObject Int(string description) => new() { ["type"] = "integer", ["description"] = description };

    public static JsonObject Choice(string description, params string[] options) => new()
    {
        ["type"] = "string",
        ["description"] = description,
        ["enum"] = new JsonArray(options.Select(o => (JsonNode)o).ToArray())
    };
}
