using System.Text.RegularExpressions;

namespace WinCompanion.Services;

/// <summary>
/// Requests the app can answer on its own, with no AI model and no internet: setting, listing and cancelling
/// reminders. Checked before anything is sent to Gemini or a local model, so they work offline and cost no quota.
/// </summary>
public static class LocalCommands
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly Regex List = new(
        @"^\W*(?:please\s+)?(?:(?:show|list|view|see|check|display)\s+)?(?:me\s+)?(?:(?:my|all|the)\s+)?(?:(?:upcoming|pending|active)\s+)?reminders\W*$" +
        @"|^\W*(?:what|which)\s+reminders\b.*$|^\W*(?:do\s+i\s+have\s+)?any\s+reminders\b.*$|^\W*what(?:'s|\s+is)\s+(?:on\s+)?my\s+reminders?\b.*$", Opts);

    private static readonly Regex CancelAll = new(
        @"^\W*(?:please\s+)?(?:cancel|delete|remove|clear)\s+(?:all\s+)?(?:(?:my|the)\s+)?(?:all\s+)?reminders\W*$", Opts);

    private static readonly Regex CancelOne = new(
        @"^\W*(?:please\s+)?(?:cancel|delete|remove)\s+(?:(?:my|the)\s+)?reminder\s+(?:number\s+|no\.?\s*|#\s*)?(?<n>\d+)\W*$", Opts);

    /// <summary>Returns the reply when the text was handled here, or null to pass it on to the AI.</summary>
    public static string? TryHandle(string text, ReminderStore reminders)
    {
        var input = text.Trim();

        if (List.IsMatch(input)) return reminders.Describe();

        if (CancelAll.IsMatch(input))
        {
            var count = reminders.Clear();
            return count == 0 ? "You have no reminders to cancel." : $"Cancelled {count} reminder(s).";
        }

        if (CancelOne.Match(input) is { Success: true } one)
        {
            var cancelled = reminders.CancelAt(int.Parse(one.Groups["n"].Value));
            return cancelled is null
                ? "I couldn't find that reminder number. Say “show my reminders” to see the list."
                : $"Cancelled: {cancelled.Message}";
        }

        if (ReminderParser.TryTimer(input, DateTime.Now) is { } timer)
        {
            reminders.Add(timer.Message, timer.Due);
            return $"Timer set for **{ReminderStore.FormatWhen(timer.Due, DateTime.Now)}**.";
        }

        var outcome = ReminderParser.Parse(input, DateTime.Now);
        if (outcome is null) return null;
        if (outcome.Problem is not null) return outcome.Problem;

        var parsed = outcome.Reminder!;
        var saved = reminders.Add(parsed.Message, parsed.Due, parsed.Repeat);
        var again = saved.RepeatText is { } t ? $", repeating {t}" : "";
        return $"Reminder set for **{ReminderStore.FormatWhen(parsed.Due, DateTime.Now)}**{again}: {parsed.Message}";
    }
}
