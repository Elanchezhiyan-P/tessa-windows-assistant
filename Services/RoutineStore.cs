using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace WinCompanion.Services;

public sealed record RoutineStep(string Tool, JsonObject Args);

/// <param name="Phrase">What the user can say to run it, for example "good morning". Null: only by name.</param>
public sealed record Routine(string Name, string? Phrase, IReadOnlyList<RoutineStep> Steps);

/// <summary>Named sequences of tool calls ("good morning": open Outlook, snap Teams left, volume 30%), saved in the local database.</summary>
public sealed class RoutineStore
{
    /// <summary>Tools a routine may not contain: no nesting, and no editing routines from inside one.</summary>
    public static readonly string[] Forbidden = ["run_routine", "save_routine", "delete_routine"];

    private const int MaxSteps = 20;
    private readonly object _gate = new();

    public string Save(string name, string? phrase, IReadOnlyList<RoutineStep> steps, Func<string, bool>? toolExists = null)
    {
        name = name.Trim();
        if (name.Length == 0) return "A routine needs a name.";
        if (steps.Count == 0) return "A routine needs at least one step.";
        if (steps.Count > MaxSteps) return $"A routine can have at most {MaxSteps} steps.";
        foreach (var step in steps)
        {
            if (Forbidden.Contains(step.Tool, StringComparer.OrdinalIgnoreCase)) return $"A routine can't contain '{step.Tool}'.";
            if (toolExists is not null && !toolExists(step.Tool)) return $"There is no tool called '{step.Tool}'.";
        }

        var array = new JsonArray(steps.Select(s => (JsonNode)new JsonObject { ["tool"] = s.Tool, ["args"] = s.Args.DeepClone() }).ToArray());
        lock (_gate)
        {
            using var db = Database.Open();
            using var command = db.CreateCommand();
            command.CommandText = """
                INSERT INTO routines(name, phrase, steps, created) VALUES ($name, $phrase, $steps, $created)
                ON CONFLICT(name) DO UPDATE SET phrase = excluded.phrase, steps = excluded.steps
                """;
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$phrase", string.IsNullOrWhiteSpace(phrase) ? DBNull.Value : phrase.Trim());
            command.Parameters.AddWithValue("$steps", array.ToJsonString());
            command.Parameters.AddWithValue("$created", Database.ToText(DateTime.Now));
            command.ExecuteNonQuery();
        }
        return $"Saved the routine '{name}' with {steps.Count} step(s).";
    }

    public IReadOnlyList<Routine> List()
    {
        lock (_gate)
        {
            try
            {
                using var db = Database.Open();
                using var command = db.CreateCommand();
                command.CommandText = "SELECT name, phrase, steps FROM routines ORDER BY name";
                var list = new List<Routine>();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var steps = ParseSteps(reader.GetString(2));
                    if (steps.Count > 0) list.Add(new Routine(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), steps));
                }
                return list;
            }
            catch (SqliteException) { return []; }
        }
    }

    public Routine? Get(string name) =>
        List().FirstOrDefault(r => r.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    public bool Delete(string name)
    {
        lock (_gate)
        {
            using var db = Database.Open();
            using var command = db.CreateCommand();
            command.CommandText = "DELETE FROM routines WHERE name = $name";
            command.Parameters.AddWithValue("$name", name.Trim());
            return command.ExecuteNonQuery() > 0;
        }
    }

    /// <summary>The routine the user's message asks for, or null. Matches its name or phrase, with or without "run my ... routine".</summary>
    public Routine? Find(string text)
    {
        var wanted = Normalize(text);
        if (wanted.Length == 0) return null;
        return List().FirstOrDefault(r => Normalize(r.Name) == wanted || (r.Phrase is not null && Normalize(r.Phrase) == wanted));
    }

    private static readonly Regex Wrapper = new(
        @"^(?:please\s+)?(?:(?:run|start|do|begin|execute|trigger)\s+)?(?:(?:my|the)\s+)?(?<core>.+?)(?:\s+routine)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Lower-case, no punctuation, and without "please", "run my" or "routine" around it.</summary>
    internal static string Normalize(string text)
    {
        var clean = Regex.Replace(text.ToLowerInvariant(), @"[^\p{L}\p{N} ]", " ");
        clean = Regex.Replace(clean, @"\s+", " ").Trim();
        var match = Wrapper.Match(clean);
        return match.Success ? match.Groups["core"].Value.Trim() : clean;
    }

    internal static List<RoutineStep> ParseSteps(string json)
    {
        var steps = new List<RoutineStep>();
        try
        {
            if (JsonNode.Parse(json) is not JsonArray array) return steps;
            foreach (var item in array)
            {
                var tool = item?["tool"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(tool)) continue;
                steps.Add(new RoutineStep(tool, item?["args"] as JsonObject ?? new JsonObject()));
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        return steps;
    }
}
