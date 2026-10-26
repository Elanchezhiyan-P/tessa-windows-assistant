using System.Text.Json.Nodes;
using WinCompanion.Services;

namespace WinCompanion.Tools;

/// <summary>
/// Runs one tool the guarded way: asks the user first when the tool needs approval, runs it, and writes it to the action log.
/// The AI clients and routines both go through here, so a routine step is never less guarded than the same request typed in chat.
/// </summary>
public sealed class ToolExecutor(ToolRegistry tools, Func<string, Task<bool>> confirm, ActionLog log)
{
    public async Task<string> RunAsync(string name, JsonObject args)
    {
        var tool = tools.Find(name);
        if (tool is null)
        {
            var unknown = $"Unknown tool '{name}'.";
            log.Write(name, args, "unknown", unknown);
            return unknown;
        }

        var needsConfirmation = tool.NeedsConfirmation(args);
        // Show a PowerShell command as plain text rather than escaped JSON.
        var detail = args["command"] is JsonValue v && v.TryGetValue<string>(out var command) ? command : args.ToJsonString();
        var approved = !needsConfirmation || await confirm($"{name}\n{detail}");

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

        log.Write(name, args, !needsConfirmation ? "auto" : approved ? "approved" : "declined", result);
        return result;
    }
}
