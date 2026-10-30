using System.Text;

namespace WinCompanion.Services;

/// <param name="Key">What the user types: "water", "posture", "eyes", "stand".</param>
public sealed record NudgeKind(string Key, string Emoji, string Message, int DefaultMinutes);

/// <summary>
/// Gentle repeating reminders (water, posture, eye rest, standing up). They are ordinary repeating reminders that start with an
/// emoji, so they survive restarts, work offline, and are held back during a focus work period like any other reminder.
/// </summary>
public sealed class NudgeService(ReminderStore reminders)
{
    public static readonly IReadOnlyList<NudgeKind> Kinds =
    [
        new("water", "💧", "Time for a glass of water.", 60),
        new("posture", "🧍", "Posture check: sit back and drop your shoulders.", 45),
        new("eyes", "👀", "Rest your eyes: look at something 20 feet away for 20 seconds.", 20),
        new("stand", "🚶", "Stand up and stretch for a minute.", 60)
    ];

    private const int MinMinutes = 5;
    private const int MaxMinutes = 240;

    public static NudgeKind? Find(string key) =>
        Kinds.FirstOrDefault(k => k.Key.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsNudge(string message) => KindOf(message) is not null;

    private static NudgeKind? KindOf(string message) =>
        Kinds.FirstOrDefault(k => message.StartsWith(k.Emoji, StringComparison.Ordinal));

    /// <summary>Starts (or restarts) one kind of nudge, repeating every <paramref name="minutes"/> (its default when null).</summary>
    public string Start(NudgeKind kind, int? minutes = null, DateTime? now = null)
    {
        var every = Math.Clamp(minutes ?? kind.DefaultMinutes, MinMinutes, MaxMinutes);
        reminders.RemoveWhere(r => KindOf(r.Message) == kind);
        var added = reminders.Add($"{kind.Emoji} {kind.Message}", (now ?? DateTime.Now).AddMinutes(every), $"every:{every}");
        return $"Done: I'll nudge you {added.RepeatText}: {kind.Message} During a focus session I hold them until your break.";
    }

    /// <summary>Stops one kind, or all of them when <paramref name="key"/> is empty.</summary>
    public string Stop(string? key)
    {
        NudgeKind? kind = null;
        if (!string.IsNullOrWhiteSpace(key) && !key.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            kind = Find(key);
            if (kind is null) return $"I don't know a nudge called \"{key}\". Try {KeyList()}.";
        }

        var removed = reminders.RemoveWhere(r => kind is null ? IsNudge(r.Message) : KindOf(r.Message) == kind);
        return removed == 0 ? "No nudges were on." : kind is null ? "All nudges are off." : $"The {kind.Key} nudge is off.";
    }

    public string Status()
    {
        var active = reminders.Pending().Where(r => IsNudge(r.Message)).OrderBy(r => r.Message).ToList();
        var sb = new StringBuilder();
        if (active.Count == 0) sb.Append("No nudges are on.");
        else
        {
            sb.Append("Nudges on:");
            foreach (var r in active) sb.Append($"\n• {KindOf(r.Message)!.Key}: {r.RepeatText ?? "once"}, next {ReminderStore.FormatWhen(r.Due, DateTime.Now)}");
        }
        sb.Append($"\nStart one with /nudge {KeyList()}, optionally with minutes (for example /nudge water 45). Stop with /nudge off.");
        return sb.ToString();
    }

    public static string KeyList() => string.Join(", ", Kinds.Select(k => k.Key));
}
