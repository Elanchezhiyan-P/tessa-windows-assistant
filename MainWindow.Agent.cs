using System.Windows;
using System.Windows.Threading;
using WinCompanion.Services;
using WinCompanion.UI;

namespace WinCompanion;

// /agent <goal>: she writes a plan, you approve it once, and she works through it step by step with a Stop button.
public partial class MainWindow
{
    private bool _agentRunning;
    private AgentPlanCard? _agentCard;
    private CancellationTokenSource? _agentStop;
    private bool _agentAbandoned;

    private void AgentSay(string role, string text)
    {
        if (!_agentAbandoned) AddMessage(role, text);
    }

    /// <summary>Ends a plan that is waiting for approval or running (it finishes the step it is on). Used when the chat is cleared.</summary>
    private void StopAgent()
    {
        _agentAbandoned = _agentRunning; // anything the plan says from now on belongs to a chat that no longer exists
        _agentCard?.Cancel();
        try { _agentStop?.Cancel(); }
        catch (ObjectDisposedException) { }
        if (_agentRunning) ResolveConfirm(false);
    }

    private const string AgentExamples = "Try something like /agent organise my Downloads and tell me what's biggest.";

    /// <summary>Plans the goal, waits for the user's approval, then runs the steps. Every step is a normal request to the assistant.</summary>
    private async Task RunAgentAsync(string goal)
    {
        if (_agentRunning)
        {
            AddMessage("assistant", "A plan is already running. Press Stop on it first.");
            return;
        }

        var client = EnsureClient();
        if (client is null) return;

        _agentRunning = true;
        _agentAbandoned = false;
        AddMessage("user", $"/agent {goal}");
        try
        {
            // 1. Ask for a plan. Only the goal is sent, no files or screen.
            IReadOnlyList<string> texts;
            SetBusy(true);
            try { texts = AgentPlanner.Parse(await client.GenerateAsync(AgentPlanner.Instruction(_tools.Names()), goal)); }
            catch (LlmException ex)
            {
                AgentSay("error", ex.Message);
                return;
            }
            finally { SetBusy(false); }

            // "New chat" while the plan was being written: the chat it was for is gone, so drop the plan instead of showing it.
            if (_agentAbandoned) return;

            if (texts.Count == 0)
            {
                AddMessage("assistant", "I couldn't turn that into a plan. Describe the goal with what to do and where. " + AgentExamples);
                return;
            }

            // 2. Show it and wait for a yes.
            var steps = texts.Select((text, i) => new AgentStep(i + 1, text)).ToList();
            var card = new AgentPlanCard(goal, steps);
            _agentCard = card;
            Messages.Children.Add(card.Root);
            _historyOpen = true;
            _settingsOpen = false;
            RefreshLayout();

            if (!await card.WaitForDecisionAsync())
            {
                card.ShowFinished("Cancelled. Nothing was done.");
                AgentSay("assistant", "Plan cancelled. Nothing was done.");
                return;
            }

            // 3. Run it. Stop works before the next step, and also turns away an approval that is waiting.
            var stop = new CancellationTokenSource();
            _agentStop = stop;
            card.StopRequested += () =>
            {
                stop.Cancel();
                ResolveConfirm(false);
            };

            SetBusy(true);
            AgentOutcome outcome;
            try
            {
                outcome = await new AgentRunner().RunAsync(steps, async step =>
                {
                    var reply = await client.SendAsync(AgentPlanner.StepPrompt(goal, step, steps.Count));
                    AgentSay("assistant", $"**Step {step.Number}:** {(reply.Length > 0 ? reply : "(done)")}");
                    return reply;
                }, _ => card.Refresh(), stop.Token);
            }
            finally
            {
                SetBusy(false);
                _agentStop = null;
                stop.Dispose();
            }

            card.ShowFinished(outcome switch
            {
                AgentOutcome.Completed => "Done.",
                AgentOutcome.Stopped => "Stopped. The remaining steps were skipped.",
                _ => "Stopped after a problem."
            });
            AgentSay("assistant", AgentPlanner.Summary(goal, steps, outcome));
        }
        finally
        {
            _agentCard = null;
            _agentRunning = false;
        }
    }
}
