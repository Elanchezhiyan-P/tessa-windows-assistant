using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WinCompanion.Services;

public enum StepStatus { Pending, Running, Done, Failed, Skipped }

public sealed class AgentStep(int number, string text)
{
    public int Number { get; } = number;
    public string Text { get; } = text;
    public StepStatus Status { get; set; } = StepStatus.Pending;
    public string? Result { get; set; }
}

public enum AgentOutcome { Completed, Stopped, Failed }

/// <summary>Asking the model for a plan, and reading what it sends back.</summary>
public static class AgentPlanner
{
    public const int MaxSteps = 8;
    private const int MaxStepLength = 200;

    /// <param name="toolNames">What she can do, so the plan only uses real abilities.</param>
    public static string Instruction(IEnumerable<string> toolNames) =>
        "You plan tasks for a Windows PC assistant. The user's goal follows. Reply with ONLY a JSON array of 2 to 6 short strings, one per step, in order. " +
        $"Each step must be something the assistant can do with these abilities: {string.Join(", ", toolNames)}. " +
        "Say what to do and to what, not how (for example \"Organize the Downloads folder by file type\"). " +
        "If the user asked a question, make the last step report the answer. " +
        "Never plan to delete anything permanently, and never plan a step that needs information you don't have.";

    /// <summary>What each step sends to the assistant. It runs with all her tools, so every action is approved and logged as usual.</summary>
    public static string StepPrompt(string goal, AgentStep step, int total) =>
        $"This is step {step.Number} of {total} of a plan the user approved. The overall goal: {goal}\n" +
        $"Do only this step now: {step.Text}\n" +
        "Use your tools. When it is done, reply in one or two sentences saying what happened. If you can't do it, say why.";

    /// <summary>The steps in a model's reply: a JSON array of strings or of objects, or else a numbered or bulleted list.</summary>
    public static IReadOnlyList<string> Parse(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return [];
        var text = Regex.Replace(reply, @"```[a-zA-Z]*", "").Trim();

        var steps = FromJson(text) ?? FromLines(text);
        return steps
            .Select(Clean)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxSteps)
            .ToList();
    }

    private static List<string>? FromJson(string text)
    {
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end <= start) return null;

        try
        {
            if (JsonNode.Parse(text[start..(end + 1)]) is not JsonArray array) return null;
            var steps = new List<string>();
            foreach (var item in array)
            {
                if (item is JsonValue value && value.TryGetValue<string>(out var s)) steps.Add(s);
                else if (item is JsonObject obj)
                {
                    var found = new[] { "step", "title", "text", "action", "description", "task" }
                        .Select(k => obj[k] is JsonValue v && v.TryGetValue<string>(out var str) ? str : null)
                        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
                    if (found is not null) steps.Add(found);
                }
            }
            return steps.Count > 0 ? steps : null;
        }
        catch (JsonException) { return null; }
    }

    private static List<string> FromLines(string text) =>
        text.Split('\n')
            .Select(l => Regex.Match(l, @"^\s*(?:\d+[.)]|[-*•])\s+(.+)$"))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .ToList();

    private static string Clean(string step)
    {
        var s = Regex.Replace(step, @"\s+", " ").Trim().Trim('"', '\'', '`', ',');
        s = Regex.Replace(s, @"^\s*(?:step\s*)?\d+\s*[.):-]\s*", "", RegexOptions.IgnoreCase); // "1. Do it" or "Step 2: Do it"
        s = s.Trim();
        return s.Length > MaxStepLength ? s[..MaxStepLength].TrimEnd() + "…" : s;
    }

    public static string Glyph(StepStatus status) => status switch
    {
        StepStatus.Running => "▶",
        StepStatus.Done => "✓",
        StepStatus.Failed => "✗",
        StepStatus.Skipped => "–",
        _ => "○"
    };

    /// <summary>The record kept in the chat once the plan is over.</summary>
    public static string Summary(string goal, IReadOnlyList<AgentStep> steps, AgentOutcome outcome)
    {
        var done = steps.Count(s => s.Status == StepStatus.Done);
        var sb = new StringBuilder($"**Plan: {goal}**");
        foreach (var step in steps) sb.Append($"\n{Glyph(step.Status)} {step.Number}. {step.Text}");
        sb.Append("\n\n").Append(outcome switch
        {
            AgentOutcome.Completed => $"Finished: {done} of {steps.Count} steps done.",
            AgentOutcome.Stopped => $"Stopped by you: {done} of {steps.Count} steps done, the rest were skipped.",
            _ => $"Stopped after a problem: {done} of {steps.Count} steps done." +
                 (steps.FirstOrDefault(s => s.Status == StepStatus.Failed)?.Result is { Length: > 0 } why ? $" {why}" : "")
        });
        return sb.ToString();
    }
}

/// <summary>
/// Runs a plan one step at a time. Stopping takes effect before the next step, and a failed step ends the plan. The step itself is a
/// delegate, so the real thing goes through the model's normal tool loop, with its approvals and log.
/// </summary>
public sealed class AgentRunner
{
    public async Task<AgentOutcome> RunAsync(IReadOnlyList<AgentStep> steps, Func<AgentStep, Task<string>> run,
        Action<AgentStep>? changed, CancellationToken stop)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            if (stop.IsCancellationRequested)
            {
                SkipFrom(steps, i, changed);
                return AgentOutcome.Stopped;
            }

            var step = steps[i];
            step.Status = StepStatus.Running;
            changed?.Invoke(step);
            try
            {
                step.Result = await run(step);
                step.Status = StepStatus.Done;
                changed?.Invoke(step);
            }
            catch (Exception ex)
            {
                step.Status = StepStatus.Failed;
                step.Result = ex.Message;
                changed?.Invoke(step);
                SkipFrom(steps, i + 1, changed);
                return AgentOutcome.Failed;
            }
        }
        return AgentOutcome.Completed;
    }

    private static void SkipFrom(IReadOnlyList<AgentStep> steps, int from, Action<AgentStep>? changed)
    {
        for (var i = from; i < steps.Count; i++)
        {
            steps[i].Status = StepStatus.Skipped;
            changed?.Invoke(steps[i]);
        }
    }
}
