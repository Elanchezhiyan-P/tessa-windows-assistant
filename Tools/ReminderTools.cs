using System.Globalization;
using System.Text.Json.Nodes;
using WinCompanion.Services;
using static WinCompanion.Tools.ToolSchema;

namespace WinCompanion.Tools;

/// <summary>The same reminders the app handles locally, exposed so the AI model can set them too.</summary>
internal static class ReminderTools
{
    public static IEnumerable<ITool> All(ReminderStore reminders)
    {
        yield return new DelegateTool("set_reminder",
            "Set a reminder that pops up at a time, even if the app is restarted. Give either 'minutes' (from now) or 'at' (local date and time).",
            new JsonObject
            {
                ["message"] = Str("What to remind the user about."),
                ["minutes"] = Int("Minutes from now."),
                ["at"] = Str("Local date and time as 'yyyy-MM-dd HH:mm', e.g. '2026-10-07 17:30'."),
                ["repeat"] = Choice("Optional: repeat at the same time.", "daily", "weekdays", "weekly"),
                ["every_minutes"] = Int("Optional: repeat every this many minutes (the first one fires after that long).")
            }, ["message"], false,
            args =>
            {
                var now = DateTime.Now;
                var message = args["message"]!.GetValue<string>().Trim();
                var repeat = args["repeat"]?.GetValue<string>() is { Length: > 0 } r ? r : null;
                if (args["every_minutes"] is { } every && every.GetValue<int>() >= 1)
                {
                    repeat = $"every:{every.GetValue<int>()}";
                    if (args["minutes"] is null && args["at"] is null) args["minutes"] = every.GetValue<int>();
                }

                DateTime due;
                if (args["minutes"] is { } minutes) due = now.AddMinutes(Math.Clamp(minutes.GetValue<int>(), 1, 60 * 24 * 365));
                else if (args["at"] is { } at && DateTime.TryParse(at.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) due = parsed;
                else return Task.FromResult("I need either 'minutes' or an 'at' time (yyyy-MM-dd HH:mm).");

                if (due <= now) return Task.FromResult("That time has already passed.");
                var saved = reminders.Add(message.Length == 0 ? "Reminder" : message, due, repeat);
                return Task.FromResult($"Reminder set for {ReminderStore.FormatWhen(due, now)}{(saved.RepeatText is { } t ? $", repeating {t}" : "")}: {message}");
            });

        yield return new DelegateTool("list_reminders", "List the pending reminders, numbered.", null, [], false,
            _ => Task.FromResult(reminders.Describe()));

        yield return new DelegateTool("cancel_reminder", "Cancel one reminder by its number from list_reminders, or all of them.",
            new JsonObject { ["which"] = Str("The reminder number, or 'all'.") }, ["which"], false,
            args =>
            {
                var which = args["which"]!.GetValue<string>().Trim();
                if (which.Equals("all", StringComparison.OrdinalIgnoreCase)) return Task.FromResult($"Cancelled {reminders.Clear()} reminder(s).");
                if (!int.TryParse(which, out var number)) return Task.FromResult("Give a reminder number or 'all'.");
                var cancelled = reminders.CancelAt(number);
                return Task.FromResult(cancelled is null ? "No reminder with that number." : $"Cancelled: {cancelled.Message}");
            });
    }
}
