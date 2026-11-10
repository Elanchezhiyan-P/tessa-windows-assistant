using WinCompanion.Services;
using WinCompanion.UI;

namespace WinCompanion;

// Saved agent plans: keep one, run it again, and put it on a schedule. A scheduled plan only ever appears and waits for Run plan.
public partial class MainWindow
{
    private readonly PlanStore _plans = new();

    // The plan card most recently shown, so "/plan save <name>" can keep it (with any edits made to it).
    private (string Goal, AgentPlanCard Card)? _lastPlan;

    private const string PlanUsage =
        "Plans:\n" +
        "• /agent <goal>: I write a plan; you can reword, move or remove steps, then Save plan or Run plan.\n" +
        "• /plan save <name>: keep the plan I just made.\n" +
        "• /plan run <name>: show it again and run it when you press Run plan.\n" +
        "• /plan schedule <name> every weekday at 9am: bring it up on a schedule (it never runs by itself).\n" +
        "• /plan unschedule <name>, /plan delete <name>, and /plans to see them all.";

    private string PlanListText()
    {
        var plans = _plans.List();
        if (plans.Count == 0) return "You have no saved plans yet. Make one with /agent <goal>, then press Save plan or type /plan save <name>.";

        var pending = _reminders.Pending();
        var now = DateTime.Now;
        return "Your plans:\n" + string.Join('\n', plans.Select(p =>
        {
            var when = PlanSchedule.Describe(pending, p.Name, now);
            return $"• **{p.Name}**: {p.Steps.Count} step(s) for “{p.Goal}”{(when is null ? "" : $". Scheduled: {when}")}";
        })) + "\n\n/plan run <name> to run one, /plan schedule <name> <when> to schedule it.";
    }

    private async Task<string> PlanCommandAsync(string args)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return PlanUsage;
        var verb = parts[0].ToLowerInvariant();
        var rest = parts.Length > 1 ? parts[1] : "";

        switch (verb)
        {
            case "save":
            {
                if (_lastPlan is not { } last) return "Make a plan first with /agent <goal>, then type /plan save <name>.";
                var name = PlanStore.CleanName(rest);
                if (name.Length == 0) return "Give it a name, for example /plan save tidy downloads.";
                var saved = _plans.Save(name, last.Goal, last.Card.Steps.Select(s => s.Text).ToList());
                return saved.StartsWith("Saved", StringComparison.Ordinal)
                    ? $"{saved} Run it with /plan run {name}, or /plan schedule {name} every weekday at 9am."
                    : saved;
            }
            case "run":
            {
                if (_plans.Get(rest) is not { } plan) return NoSuchPlan(rest);
                await RunAgentAsync(plan.Goal.Length > 0 ? plan.Goal : plan.Name, plan);
                return "";
            }
            case "delete" or "remove":
            {
                if (_plans.Get(rest) is not { } plan) return NoSuchPlan(rest);
                PlanSchedule.Unschedule(_reminders, plan.Name);
                _plans.Delete(plan.Name);
                return $"Deleted the plan “{plan.Name}” and its schedule.";
            }
            case "schedule":
            {
                if (_plans.FindByPrefix(rest) is not { } match) return rest.Length == 0 ? "Which plan, and when? For example /plan schedule tidy downloads every weekday at 9am." : NoSuchPlan(rest);
                return PlanSchedule.Schedule(_reminders, match.Plan, match.Remainder, DateTime.Now);
            }
            case "unschedule":
            {
                if (_plans.Get(rest) is not { } plan) return NoSuchPlan(rest);
                return PlanSchedule.Unschedule(_reminders, plan.Name) ? $"“{plan.Name}” is no longer scheduled." : $"“{plan.Name}” wasn't scheduled.";
            }
            default:
                return PlanUsage;
        }
    }

    private string NoSuchPlan(string name) =>
        _plans.List().Count == 0
            ? "You have no saved plans yet. Make one with /agent <goal>."
            : $"I have no plan called “{name}”. Type /plans to see them.";

    // A scheduled plan has come round. It is shown with Run plan and Cancel, like any plan: nothing starts without a click.
    private void OnScheduledPlanDue(string planName, Reminder reminder, bool missed)
    {
        var silent = QuietNow && _appSettings.QuietOwnReminders;
        var plan = _plans.Get(planName);
        if (plan is null)
        {
            PlanSchedule.Unschedule(_reminders, planName);
            AddMessage("assistant", $"A scheduled plan “{planName}” came due, but there is no plan with that name any more, so I removed its schedule.");
            return;
        }

        var text = $"📋 **Scheduled plan:** “{plan.Name}” is ready. Check the steps below, then press **Run plan**. Nothing runs until you do.";
        if (missed) text += $"\n\n(it was due {reminder.Due:ddd d MMM, h:mm tt})";
        if (!silent) System.Media.SystemSounds.Exclamation.Play();
        AddMessage("assistant", text);

        if (_agentRunning)
        {
            AddMessage("assistant", $"Another plan is running. When it ends, type /plan run {plan.Name}.");
            return;
        }

        if (!silent)
        {
            var delivered = _pets.Deliver($"Your plan “{plan.Name}” is ready to run.", snooze: () => Snooze(reminder.Message), done: null);
            if (!delivered && !PetController.IsFullScreenAppRunning()) ShowAndFocus();
            Speak($"Your plan {plan.Name} is ready.");
            ReminderFired?.Invoke($"{_appSettings.AssistantName} plan", $"“{plan.Name}” is ready to run.");
        }

        _ = RunScheduledPlanAsync(plan);
    }

    private async Task RunScheduledPlanAsync(SavedPlan plan)
    {
        try { await RunAgentAsync(plan.Goal.Length > 0 ? plan.Goal : plan.Name, plan); }
        catch (Exception ex) { CrashLog.Write("Scheduled plan failed", ex); }
    }
}
