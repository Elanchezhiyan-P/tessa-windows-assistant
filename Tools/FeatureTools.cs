using System.Text.Json.Nodes;
using WinCompanion.Services;
using static WinCompanion.Tools.ToolSchema;

namespace WinCompanion.Tools;

/// <summary>Focus mode and the daily briefing, so the AI can start them too.</summary>
internal static class FeatureTools
{
    public static IEnumerable<ITool> All(FocusService focus, ReminderStore reminders, AppSettings settings)
    {
        yield return new DelegateTool("start_focus",
            "Start focus mode (a pomodoro timer): work for some minutes, then a break, announced by the reminder character. " +
            "Other reminders are held back and spoken replies are muted while working.",
            new JsonObject
            {
                ["work_minutes"] = Int("Length of each work period (default 25)."),
                ["break_minutes"] = Int("Length of each break (default 5)."),
                ["rounds"] = Int("How many work periods (default 1; a classic pomodoro is 4).")
            }, [], false,
            args => Task.FromResult(focus.Start(
                args["work_minutes"]?.GetValue<int>() ?? 25,
                args["break_minutes"]?.GetValue<int>() ?? 5,
                args["rounds"]?.GetValue<int>() ?? 1)));

        yield return new DelegateTool("stop_focus", "Stop focus mode and cancel its break reminders.", null, [], false,
            _ => Task.FromResult(focus.Stop() ? "Focus mode is off." : "Focus mode wasn't on."));

        yield return new DelegateTool("daily_briefing",
            "Give the user's daily briefing: today's reminders, the weather and headlines for their chosen topics.", null, [], false,
            async _ => await BriefingService.BuildAsync(reminders, settings));
    }
}
