using System.Text.RegularExpressions;

namespace WinCompanion.Services;

/// <summary>
/// Pomodoro and focus mode. A session is just reminders: one when each work period ends ("take a break") and one when each
/// break ends, so the character announces them and they survive a restart. While a work period runs, other reminders are held back
/// and she stays quiet; the window decides what that means (see <see cref="IsWorking"/>).
/// </summary>
public sealed class FocusService
{
    /// <summary>Starts every reminder this service creates, so they can be told apart and cancelled together.</summary>
    public const string Mark = "🍅 ";

    private readonly ReminderStore _reminders;
    private readonly object _gate = new();
    private readonly List<(DateTime Start, DateTime End)> _work = new();

    public FocusService(ReminderStore reminders) => _reminders = reminders;

    /// <summary>True while a work period is running.</summary>
    public bool IsWorking(DateTime now)
    {
        lock (_gate) return _work.Any(w => now >= w.Start && now < w.End);
    }

    /// <summary>When the current work period ends, or null outside one.</summary>
    public DateTime? WorkEnds(DateTime now)
    {
        lock (_gate)
        {
            foreach (var w in _work) if (now >= w.Start && now < w.End) return w.End;
            return null;
        }
    }

    public static bool IsFocusReminder(string message) => message.StartsWith(Mark, StringComparison.Ordinal);

    /// <summary>Starts <paramref name="cycles"/> rounds of work then break. Replaces any session already running.</summary>
    public string Start(int workMinutes, int breakMinutes, int cycles, DateTime? now = null)
    {
        workMinutes = Math.Clamp(workMinutes, 1, 180);
        breakMinutes = Math.Clamp(breakMinutes, 0, 60);
        cycles = Math.Clamp(cycles, 1, 8);
        var t = now ?? DateTime.Now;

        Stop();
        lock (_gate)
        {
            for (var i = 1; i <= cycles; i++)
            {
                var start = t;
                t = t.AddMinutes(workMinutes);
                _work.Add((start, t));
                var last = i == cycles;
                _reminders.Add(last
                    ? Mark + (breakMinutes > 0 ? $"Focus session done. Take a {breakMinutes} minute break." : "Focus session done. Well done!")
                    : Mark + $"Time for a {breakMinutes} minute break ({i} of {cycles} done).", t);
                if (breakMinutes > 0 && !last)
                {
                    t = t.AddMinutes(breakMinutes);
                    _reminders.Add(Mark + "Break over. Back to focus.", t);
                }
            }
        }

        var total = cycles == 1 ? $"{workMinutes} minutes" : $"{cycles} rounds of {workMinutes} minutes with {breakMinutes}-minute breaks";
        return $"Focus mode on for {total}. I'll hold other reminders and keep quiet until each break.";
    }

    /// <summary>Ends the session and removes its reminders. Returns whether one was running.</summary>
    public bool Stop()
    {
        bool had;
        lock (_gate)
        {
            had = _work.Count > 0;
            _work.Clear();
        }
        var removed = _reminders.RemoveWhere(r => IsFocusReminder(r.Message));
        return had || removed > 0;
    }

    private static readonly Regex Minutes = new(@"(?<n>\d{1,3})\s*(?:-|\s)?(?:min(?:ute)?s?|m)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex StopWords = new(@"^\W*(?:please\s+)?(?:stop|end|cancel|turn off|exit)\s+(?:my\s+)?(?:the\s+)?(?:focus(?:\s+(?:mode|session|timer))?|pomodoro)\W*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex StartWords = new(@"^\W*(?:please\s+)?(?:start|begin|set|turn on|enable|let'?s)?\s*(?:a\s+|my\s+)?(?:focus(?:\s+(?:mode|session|timer))?|pomodoro)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Handles "pomodoro", "focus for 45 minutes", "start focus mode", "stop focus". Null when the text is something else.</summary>
    public string? TryHandle(string text)
    {
        var input = text.Trim();
        if (StopWords.IsMatch(input)) return Stop() ? "Focus mode is off." : "Focus mode wasn't on.";
        if (!StartWords.IsMatch(input)) return null;

        if (input.Contains("pomodoro", StringComparison.OrdinalIgnoreCase) && !Minutes.IsMatch(input))
            return Start(25, 5, 4);
        var minutes = Minutes.Match(input);
        return Start(minutes.Success ? int.Parse(minutes.Groups["n"].Value) : 25, 5, 1);
    }
}
