using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WinCompanion.Services;
using WinCompanion.UI;
using Xunit;

namespace WinCompanion.UnitTests;

/// <summary>Drives the plan card the way a user would, by clicking its buttons, without needing the running app.</summary>
// WPF starts up once per process, and two UI threads starting it together can deadlock: tests that build WPF objects run one at a time.
[Collection("wpf")]
public class AgentPlanCardTests
{
    private static object Resource(string key) => key.EndsWith("Button", StringComparison.Ordinal) ? null! : Brushes.Gray;

    private static List<AgentStep> Steps() =>
        [new(1, "Organize the Downloads folder"), new(2, "Find the biggest files"), new(3, "Tell me what is big")];

    private static T OnStaThread<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() => { try { result = work(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("failed on the UI thread", error);
        return result;
    }

    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var deeper in All<T>(child)) yield return deeper;
        }
    }

    private static Button ButtonNamed(AgentPlanCard card, string content) =>
        All<Button>(card.Root).Single(b => (string)b.Content == content);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static List<string> Lines(AgentPlanCard card) => All<TextBlock>(card.Root).Select(t => t.Text).ToList();

    [Fact]
    public void StartsWithThePlanAndAskingForApproval()
    {
        var (lines, run, cancel, stop) = OnStaThread(() =>
        {
            var card = new AgentPlanCard("tidy my downloads", Steps(), Resource);
            return (Lines(card), ButtonNamed(card, "Run plan").Visibility, ButtonNamed(card, "Cancel").Visibility,
                ButtonNamed(card, "Stop after this step").Visibility);
        });

        Assert.Contains("Plan: tidy my downloads", lines);
        Assert.Contains("○  1. Organize the Downloads folder", lines);
        Assert.Contains("○  3. Tell me what is big", lines);
        Assert.Contains(lines, l => l.Contains("Anything risky still asks you first"));
        Assert.Equal(Visibility.Visible, run);
        Assert.Equal(Visibility.Visible, cancel);
        Assert.Equal(Visibility.Collapsed, stop);
    }

    [Fact]
    public void RunPlanApprovesAndSwitchesToStop()
    {
        var (decided, result, run, cancel, stop, status) = OnStaThread(() =>
        {
            var card = new AgentPlanCard("g", Steps(), Resource);
            var decision = card.WaitForDecisionAsync();
            Click(ButtonNamed(card, "Run plan"));
            return (decision.IsCompleted, decision.Result, ButtonNamed(card, "Run plan").Visibility, ButtonNamed(card, "Cancel").Visibility,
                ButtonNamed(card, "Stop after this step").Visibility, Lines(card).First(l => l.StartsWith("Running")));
        });

        Assert.True(decided);
        Assert.True(result);
        Assert.Equal(Visibility.Collapsed, run);
        Assert.Equal(Visibility.Collapsed, cancel);
        Assert.Equal(Visibility.Visible, stop);
        Assert.Contains("Stop", status);
    }

    [Fact]
    public void CancelAnswersNoAndNothingRuns()
    {
        var result = OnStaThread(() =>
        {
            var card = new AgentPlanCard("g", Steps(), Resource);
            var decision = card.WaitForDecisionAsync();
            Click(ButtonNamed(card, "Cancel"));
            return decision.Result;
        });
        Assert.False(result);
    }

    [Fact]
    public void ClearingTheChatCancelsAPendingPlanButNotARunningOne()
    {
        var (pending, running) = OnStaThread(() =>
        {
            var waiting = new AgentPlanCard("g", Steps(), Resource);
            var waitingDecision = waiting.WaitForDecisionAsync();
            waiting.Cancel();

            var started = new AgentPlanCard("g", Steps(), Resource);
            var startedDecision = started.WaitForDecisionAsync();
            Click(ButtonNamed(started, "Run plan"));
            started.Cancel(); // too late: the user already said yes
            return (waitingDecision.Result, startedDecision.Result);
        });

        Assert.False(pending);
        Assert.True(running);
    }

    [Fact]
    public void ProgressShowsEachStepsState()
    {
        var (lines, failedBrush, runningWeight) = OnStaThread(() =>
        {
            var steps = Steps();
            var card = new AgentPlanCard("g", steps, Resource);
            steps[0].Status = StepStatus.Done;
            steps[1].Status = StepStatus.Running;
            steps[2].Status = StepStatus.Pending;
            card.Refresh();
            var runningRow = All<TextBlock>(card.Root).First(t => t.Text.StartsWith("▶"));

            steps[1].Status = StepStatus.Failed;
            steps[2].Status = StepStatus.Skipped;
            card.Refresh();
            var failedRow = All<TextBlock>(card.Root).First(t => t.Text.StartsWith("✗"));
            return (Lines(card), failedRow.Foreground, runningRow.FontWeight);
        });

        Assert.Contains("✓  1. Organize the Downloads folder", lines);
        Assert.Contains("✗  2. Find the biggest files", lines);
        Assert.Contains("–  3. Tell me what is big", lines);
        Assert.Same(Brushes.IndianRed, failedBrush);
        Assert.Equal(FontWeights.SemiBold, runningWeight);
    }

    [Fact]
    public void StopRaisesTheEventOnceAndShowsItIsStopping()
    {
        var (raised, enabled, status) = OnStaThread(() =>
        {
            var card = new AgentPlanCard("g", Steps(), Resource);
            var count = 0;
            card.StopRequested += () => count++;
            Click(ButtonNamed(card, "Run plan"));
            var stop = ButtonNamed(card, "Stop after this step");
            Click(stop);
            var enabledAfter = stop.IsEnabled;
            Click(stop); // a second press must not raise it again... it can't, the button is off
            return (count, enabledAfter, Lines(card).First(l => l.Contains("Stopping")));
        });

        Assert.True(raised >= 1);
        Assert.False(enabled);
        Assert.Contains("current step", status);
    }

    [Fact]
    public void FinishingHidesTheButtonsAndSaysHowItEnded()
    {
        var (buttons, lines) = OnStaThread(() =>
        {
            var card = new AgentPlanCard("g", Steps(), Resource);
            Click(ButtonNamed(card, "Run plan"));
            card.ShowFinished("Stopped. The remaining steps were skipped.");
            var panel = (StackPanel)ButtonNamed(card, "Stop after this step").Parent;
            return (panel.Visibility, Lines(card));
        });

        Assert.Equal(Visibility.Collapsed, buttons);
        Assert.Contains("Stopped. The remaining steps were skipped.", lines);
    }
}
