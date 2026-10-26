using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WinCompanion.Services;
using static WinCompanion.Tools.ToolSchema;

namespace WinCompanion.Tools;

/// <summary>Saved sequences of tools. Every step still goes through the normal approval and logging.</summary>
internal static class RoutineTools
{
    public static IEnumerable<ITool> All(ToolContext ctx, RoutineStore routines)
    {
        var stepSchema = new JsonObject
        {
            ["type"] = "array",
            ["description"] = "The steps, in order. Each step is a tool name and that tool's arguments written as a JSON object string.",
            ["items"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["tool"] = Str("The tool to run, for example open_target or change_volume."),
                    ["args_json"] = Str("The tool's arguments as a JSON object string, for example {\"target\":\"outlook\"}. Use {} for none.")
                },
                ["required"] = new JsonArray("tool")
            }
        };

        yield return new DelegateTool("save_routine",
            "Save a named routine: a list of tool steps that run one after another when the user asks for it by name or says its phrase " +
            "(for example 'good morning' opens Outlook, snaps Teams left and sets the volume). Saving again with the same name replaces it.",
            new JsonObject
            {
                ["name"] = Str("Short name, for example 'morning'."),
                ["phrase"] = Str("Optional phrase the user can say to run it, for example 'good morning'."),
                ["steps"] = stepSchema
            }, ["name", "steps"], false,
            args =>
            {
                var steps = new List<RoutineStep>();
                foreach (var item in args["steps"]?.AsArray() ?? new JsonArray())
                {
                    var tool = item?["tool"]?.GetValue<string>() ?? "";
                    JsonObject stepArgs;
                    try { stepArgs = JsonNode.Parse(item?["args_json"]?.GetValue<string>() ?? "{}") as JsonObject ?? new JsonObject(); }
                    catch (JsonException) { return Task.FromResult($"The arguments for '{tool}' are not valid JSON."); }
                    steps.Add(new RoutineStep(tool, stepArgs));
                }
                return Task.FromResult(routines.Save(args["name"]!.GetValue<string>(), args["phrase"]?.GetValue<string>(), steps,
                    name => ctx.Tools?.Find(name) is not null));
            });

        yield return new DelegateTool("list_routines", "List the saved routines and what each one does.", null, [], false,
            _ =>
            {
                var all = routines.List();
                if (all.Count == 0) return Task.FromResult("No routines saved yet.");
                var sb = new StringBuilder();
                foreach (var r in all)
                    sb.AppendLine($"- {r.Name}{(r.Phrase is null ? "" : $" (say \"{r.Phrase}\")")}: {string.Join(", then ", r.Steps.Select(s => s.Tool))}");
                return Task.FromResult(sb.ToString().TrimEnd());
            });

        yield return new DelegateTool("delete_routine", "Delete a saved routine by name.",
            new JsonObject { ["name"] = Str("The routine's name.") }, ["name"], false,
            args => Task.FromResult(routines.Delete(args["name"]!.GetValue<string>()) ? "Routine deleted." : "No routine with that name."));

        yield return new DelegateTool("run_routine", "Run a saved routine by name.",
            new JsonObject { ["name"] = Str("The routine's name.") }, ["name"], false,
            async args =>
            {
                var routine = routines.Get(args["name"]!.GetValue<string>());
                return routine is null ? "No routine with that name." : await Run(ctx, routine);
            });
    }

    /// <summary>Runs every step in order through the guarded executor and summarises what happened.</summary>
    public static async Task<string> Run(ToolContext ctx, Routine routine)
    {
        if (ctx.Executor is null) return "Routines can't run right now.";
        var sb = new StringBuilder($"Ran '{routine.Name}':\n");
        foreach (var step in routine.Steps)
        {
            if (RoutineStore.Forbidden.Contains(step.Tool, StringComparer.OrdinalIgnoreCase)) continue;
            var result = await ctx.Executor.RunAsync(step.Tool, (JsonObject)step.Args.DeepClone());
            var first = result.ReplaceLineEndings("\n").Split('\n')[0];
            sb.AppendLine($"- {step.Tool}: {(first.Length > 120 ? first[..120] + "…" : first)}");
        }
        return sb.ToString().TrimEnd();
    }
}
