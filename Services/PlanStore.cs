using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace WinCompanion.Services;

public sealed record SavedPlan(string Name, string Goal, IReadOnlyList<string> Steps, DateTime Created);

/// <summary>Plans (a goal and its steps) kept so they can be run again, edited, or scheduled.</summary>
public sealed class PlanStore
{
    public const int MaxPlans = 50;
    public const int MaxNameLength = 40;
    public const int MaxGoalLength = 300;
    public const int MaxStepLength = 200;

    private readonly object _gate = new();

    /// <summary>A name fit to type after "/plan run": one line, no control characters, at most 40 characters.</summary>
    public static string CleanName(string? text)
    {
        var clean = Regex.Replace(new string((text ?? "").Where(c => !char.IsControl(c)).ToArray()), @"\s+", " ").Trim();
        if (clean.Length <= MaxNameLength) return clean;
        var cut = clean[..MaxNameLength];
        var space = cut.LastIndexOf(' ');
        return (space > 15 ? cut[..space] : cut).TrimEnd(' ', ',', '.', ';', ':');
    }

    /// <summary>"Tidy downloads", or "Tidy downloads 2" when that name is taken.</summary>
    public static string UniqueName(string wanted, IEnumerable<string> existing)
    {
        var taken = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = CleanName(wanted);
        if (name.Length == 0) name = "Plan";
        if (!taken.Contains(name)) return name;
        for (var i = 2; i < 1000; i++)
        {
            var suffix = $" {i}";
            var candidate = (name.Length + suffix.Length > MaxNameLength ? name[..(MaxNameLength - suffix.Length)] : name) + suffix;
            if (!taken.Contains(candidate)) return candidate;
        }
        return name;
    }

    /// <summary>Saves (or replaces) a plan. The reply says what happened; it starts with "Saved" on success.</summary>
    public string Save(string name, string goal, IReadOnlyList<string> steps)
    {
        name = CleanName(name);
        if (name.Length == 0) return "A plan needs a name.";
        var clean = steps.Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (clean.Count == 0) return "A plan needs at least one step.";
        if (clean.Count > AgentPlanner.MaxSteps) return $"A plan can have at most {AgentPlanner.MaxSteps} steps.";
        if (clean.Any(s => s.Length > MaxStepLength)) return $"A step can be at most {MaxStepLength} characters.";
        goal = goal.Trim();
        if (goal.Length > MaxGoalLength) goal = goal[..MaxGoalLength];

        lock (_gate)
        {
            try
            {
                using var db = Database.Open();
                if (!Exists(db, name) && Count(db) >= MaxPlans) return $"You can keep up to {MaxPlans} plans. Delete one with /plan delete first.";
                using var command = db.CreateCommand();
                command.CommandText = """
                    INSERT INTO agent_plans(name, goal, steps, created) VALUES ($name, $goal, $steps, $created)
                    ON CONFLICT(name) DO UPDATE SET goal = excluded.goal, steps = excluded.steps
                    """;
                command.Parameters.AddWithValue("$name", name);
                command.Parameters.AddWithValue("$goal", goal);
                command.Parameters.AddWithValue("$steps", new JsonArray(clean.Select(s => (JsonNode)s).ToArray()).ToJsonString());
                command.Parameters.AddWithValue("$created", Database.ToText(DateTime.Now));
                command.ExecuteNonQuery();
            }
            catch (SqliteException) { return "I couldn't save the plan just now."; }
        }
        return $"Saved the plan “{name}” with {clean.Count} step(s).";
    }

    public IReadOnlyList<SavedPlan> List()
    {
        lock (_gate)
        {
            try
            {
                using var db = Database.Open();
                using var command = db.CreateCommand();
                command.CommandText = "SELECT name, goal, steps, created FROM agent_plans ORDER BY name";
                var list = new List<SavedPlan>();
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var steps = ParseSteps(reader.GetString(2));
                    if (steps.Count > 0) list.Add(new SavedPlan(reader.GetString(0), reader.GetString(1), steps, Database.FromText(reader.GetString(3))));
                }
                return list;
            }
            catch (SqliteException) { return []; }
        }
    }

    public SavedPlan? Get(string name) =>
        List().FirstOrDefault(p => p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The saved plan whose name the text starts with (the longest such name), and what comes after it.</summary>
    public (SavedPlan Plan, string Remainder)? FindByPrefix(string text)
    {
        text = text.Trim();
        foreach (var plan in List().OrderByDescending(p => p.Name.Length))
        {
            if (!text.StartsWith(plan.Name, StringComparison.OrdinalIgnoreCase)) continue;
            if (text.Length > plan.Name.Length && !char.IsWhiteSpace(text[plan.Name.Length])) continue;
            return (plan, text[plan.Name.Length..].Trim());
        }
        return null;
    }

    public bool Delete(string name)
    {
        lock (_gate)
        {
            try
            {
                using var db = Database.Open();
                using var command = db.CreateCommand();
                command.CommandText = "DELETE FROM agent_plans WHERE name = $name";
                command.Parameters.AddWithValue("$name", name.Trim());
                return command.ExecuteNonQuery() > 0;
            }
            catch (SqliteException) { return false; }
        }
    }

    private static bool Exists(SqliteConnection db, string name)
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT EXISTS(SELECT 1 FROM agent_plans WHERE name = $name)";
        command.Parameters.AddWithValue("$name", name);
        return Convert.ToInt64(command.ExecuteScalar()) == 1;
    }

    private static long Count(SqliteConnection db)
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM agent_plans";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static List<string> ParseSteps(string json)
    {
        try
        {
            return (JsonNode.Parse(json) as JsonArray)?.Select(n => n?.GetValue<string>()).OfType<string>()
                .Where(s => s.Length > 0).Take(AgentPlanner.MaxSteps).ToList() ?? [];
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return []; }
    }
}

/// <summary>
/// Plans that come up on a schedule. A scheduled plan is an ordinary repeating reminder with a marked message; when it is due the
/// plan appears for approval, and nothing runs until the user presses Run plan.
/// </summary>
public static class PlanSchedule
{
    public const string Mark = "📋 Plan: ";

    public static bool IsPlanReminder(string message) => message.StartsWith(Mark, StringComparison.Ordinal);

    public static string? NameOf(string message) => IsPlanReminder(message) ? message[Mark.Length..].Trim() : null;

    public static string MessageFor(string planName) => Mark + planName;

    /// <summary>Schedules a saved plan from words like "every weekday at 9am" or "tomorrow at 8". Replaces an earlier schedule of the same plan.</summary>
    public static string Schedule(ReminderStore reminders, SavedPlan plan, string when, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(when)) return $"When? For example /plan schedule {plan.Name} every weekday at 9am.";
        var outcome = ReminderParser.Parse("remind me " + when, now);
        if (outcome?.Reminder is not { } parsed)
            return outcome?.Problem ?? "I couldn't tell when that is. Try “every weekday at 9am”, “tomorrow at 8” or “every Monday at 10:30”.";

        reminders.RemoveWhere(r => NameOf(r.Message)?.Equals(plan.Name, StringComparison.OrdinalIgnoreCase) == true);
        var added = reminders.Add(MessageFor(plan.Name), parsed.Due, parsed.Repeat);
        var repeat = added.RepeatText is { } text ? $", {text}" : "";
        return $"Scheduled “{plan.Name}” for {ReminderStore.FormatWhen(added.Due, now)}{repeat}. " +
               "When it is time I'll bring up the plan and wait: nothing runs until you press Run plan.";
    }

    public static bool Unschedule(ReminderStore reminders, string planName) =>
        reminders.RemoveWhere(r => NameOf(r.Message)?.Equals(planName, StringComparison.OrdinalIgnoreCase) == true) > 0;

    /// <summary>"next Mon 9 Nov, 09:00, every weekday", or null when the plan has no schedule.</summary>
    public static string? Describe(IReadOnlyList<Reminder> pending, string planName, DateTime now)
    {
        var next = pending.Where(r => NameOf(r.Message)?.Equals(planName, StringComparison.OrdinalIgnoreCase) == true).OrderBy(r => r.Due).FirstOrDefault();
        if (next is null) return null;
        return ReminderStore.FormatWhen(next.Due, now) + (next.RepeatText is { } repeat ? $", {repeat}" : "");
    }
}
