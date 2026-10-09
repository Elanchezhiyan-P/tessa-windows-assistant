using System.Text.Json.Nodes;
using WinCompanion.Services;

namespace WinCompanion.Tools;

public interface ITool
{
    string Name { get; }
    string Description { get; }
    /// <summary>JSON-schema "properties" for the arguments, or null if the tool takes none.</summary>
    JsonObject? Properties { get; }
    string[] Required { get; }
    /// <summary>True if the user must approve this call (anything destructive or arbitrary).</summary>
    bool NeedsConfirmation(JsonObject args);
    Task<string> ExecuteAsync(JsonObject args);
}

public sealed class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new();

    public static ToolRegistry CreateDefault(ToolContext ctx, MemoryStore memory, AppSettings settings, ReminderStore reminders)
    {
        var registry = new ToolRegistry();
        foreach (var tool in BuiltInTools.All(ctx, memory, settings, reminders)) registry._tools[tool.Name] = tool;
        return registry;
    }

    public ITool? Find(string name) => _tools.GetValueOrDefault(name);

    public JsonArray Declarations()
    {
        var array = new JsonArray();
        foreach (var t in _tools.Values)
        {
            var decl = new JsonObject { ["name"] = t.Name, ["description"] = t.Description };
            if (t.Properties is not null)
            {
                decl["parameters"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = t.Properties.DeepClone(),
                    ["required"] = new JsonArray(t.Required.Select(r => (JsonNode)r).ToArray())
                };
            }
            array.Add(decl);
        }
        return array;
    }

    /// <summary>The same tools in OpenAI "tools" format, used by local servers (Ollama, LM Studio...).</summary>
    /// <param name="only">When given, just these tools (in their usual order, so a model's prompt cache stays valid).</param>
    public JsonArray OpenAiDeclarations(IReadOnlySet<string>? only = null)
    {
        var array = new JsonArray();
        foreach (var t in _tools.Values)
        {
            if (only is not null && !only.Contains(t.Name)) continue;
            array.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = t.Properties?.DeepClone() ?? new JsonObject(),
                        ["required"] = new JsonArray(t.Required.Select(r => (JsonNode)r).ToArray())
                    }
                }
            });
        }
        return array;
    }
}
