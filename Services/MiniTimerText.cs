namespace WinCompanion.Services;

public enum MiniTimerKind { None, Focus, Break, Reminder }

/// <param name="Progress">0 to 1 of the way through a focus period, or -1 when there is no bar to show.</param>
public sealed record MiniTimerState(MiniTimerKind Kind, string Label, string Time, double Progress)
{
    public static readonly MiniTimerState Hidden = new(MiniTimerKind.None, "", "", -1);
}

/// <summary>What the small floating timer should say right now. Pure, so it can be tested without a window.</summary>
public static class MiniTimerText
{
    /// <summary>A reminder further away than this is not worth a countdown on screen.</summary>
    public static readonly TimeSpan ReminderWindow = TimeSpan.FromMinutes(60);

    private const int MaxLabel = 24;

    /// <summary>"04:07" under an hour, "1:04:07" from an hour. Never negative; a part of a second counts as a whole one.</summary>
    public static string Format(TimeSpan remaining)
    {
        var seconds = (long)Math.Ceiling(Math.Max(0, remaining.TotalSeconds));
        var h = seconds / 3600;
        var m = seconds % 3600 / 60;
        var s = seconds % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
    }

    /// <param name="work">The focus work period that is running now, if any.</param>
    /// <param name="pending">The reminders waiting (any order).</param>
    public static MiniTimerState Build(DateTime now, (DateTime Start, DateTime End)? work, IReadOnlyList<Reminder> pending)
    {
        if (work is { } w && w.End > now)
        {
            var total = (w.End - w.Start).TotalSeconds;
            var done = total <= 0 ? 1 : Math.Clamp((now - w.Start).TotalSeconds / total, 0, 1);
            return new MiniTimerState(MiniTimerKind.Focus, "Focus", Format(w.End - now), done);
        }

        // Between two work periods the next focus reminder is the end of the break.
        var focus = pending.Where(r => FocusService.IsFocusReminder(r.Message) && r.Due > now).OrderBy(r => r.Due).FirstOrDefault();
        if (focus is not null && focus.Message.Contains("Break over", StringComparison.Ordinal))
            return new MiniTimerState(MiniTimerKind.Break, "Break", Format(focus.Due - now), -1);

        var next = pending.Where(r => !FocusService.IsFocusReminder(r.Message) && r.Due > now && r.Due - now <= ReminderWindow)
            .OrderBy(r => r.Due).FirstOrDefault();
        if (next is null) return MiniTimerState.Hidden;
        return new MiniTimerState(MiniTimerKind.Reminder, Shorten(next.Message), Format(next.Due - now), -1);
    }

    internal static string Shorten(string message)
    {
        var line = message.ReplaceLineEndings(" ").Trim();
        return line.Length <= MaxLabel ? line : line[..(MaxLabel - 1)].TrimEnd() + "…";
    }
}
