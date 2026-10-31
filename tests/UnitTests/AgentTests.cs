using WinCompanion.Services;
using Xunit;

namespace WinCompanion.UnitTests;

public class AgentPlanParsingTests
{
    [Fact]
    public void ReadsAJsonArrayOfStrings()
    {
        var steps = AgentPlanner.Parse("[\"Organize the Downloads folder by type\", \"List the five biggest files\", \"Tell me what is big\"]");
        Assert.Equal(["Organize the Downloads folder by type", "List the five biggest files", "Tell me what is big"], steps);
    }

    [Fact]
    public void ReadsAReplyWrappedInProseAndCodeFences()
    {
        var reply = "Sure! Here is the plan:\n```json\n[\"Find large files\", \"Report them\"]\n```\nLet me know.";
        Assert.Equal(["Find large files", "Report them"], AgentPlanner.Parse(reply));
    }

    [Fact]
    public void ReadsObjectsWithAStepTitleOrText()
    {
        var reply = "[{\"step\": \"Scan the folder\"}, {\"title\": \"Sort it\", \"detail\": \"x\"}, {\"text\": \"Report\"}, {\"nonsense\": 1}]";
        Assert.Equal(["Scan the folder", "Sort it", "Report"], AgentPlanner.Parse(reply));
    }

    [Fact]
    public void ReadsANumberedOrBulletedList()
    {
        Assert.Equal(["Open the folder", "Sort the files"], AgentPlanner.Parse("1. Open the folder\n2) Sort the files"));
        Assert.Equal(["First thing", "Second thing"], AgentPlanner.Parse("Plan:\n- First thing\n* Second thing\nThat's all."));
    }

    [Fact]
    public void CleansUpEachStep()
    {
        var steps = AgentPlanner.Parse("[\"1. Step one text\", \"  Step 2: spaced   out   text  \", \"'quoted'\"]");
        Assert.Equal(["Step one text", "spaced out text", "quoted"], steps);
    }

    [Fact]
    public void DropsEmptyAndRepeatedSteps() =>
        Assert.Equal(["Do it"], AgentPlanner.Parse("[\"Do it\", \"\", \"  \", \"do it\"]"));

    [Fact]
    public void KeepsAtMostEightStepsAndShortensLongOnes()
    {
        var many = "[" + string.Join(",", Enumerable.Range(1, 15).Select(i => $"\"Step number {i}\"")) + "]";
        Assert.Equal(AgentPlanner.MaxSteps, AgentPlanner.Parse(many).Count);

        var long_ = AgentPlanner.Parse($"[\"{new string('a', 600)}\"]").Single();
        Assert.True(long_.Length <= 201);
        Assert.EndsWith("…", long_);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("I'm sorry, I can't help with that.")]
    [InlineData("[this is not json")]
    [InlineData("[]")]
    [InlineData("[1, 2, 3]")]
    public void GarbageGivesNoPlanRatherThanAnError(string? reply) => Assert.Empty(AgentPlanner.Parse(reply));

    [Fact]
    public void TheInstructionNamesTheAbilitiesAndForbidsPermanentDeletion()
    {
        var instruction = AgentPlanner.Instruction(["organize_folder", "find_files"]);
        Assert.Contains("organize_folder, find_files", instruction);
        Assert.Contains("JSON array", instruction);
        Assert.Contains("never plan to delete anything permanently", instruction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EachStepPromptCarriesTheGoalTheStepAndItsPlace()
    {
        var prompt = AgentPlanner.StepPrompt("tidy my downloads", new AgentStep(2, "Sort the files"), 5);
        Assert.Contains("step 2 of 5", prompt);
        Assert.Contains("tidy my downloads", prompt);
        Assert.Contains("Sort the files", prompt);
    }

    [Fact]
    public void SummaryListsEveryStepAndTheOutcome()
    {
        var steps = new List<AgentStep> { new(1, "Scan") { Status = StepStatus.Done }, new(2, "Sort") { Status = StepStatus.Failed, Result = "No access." }, new(3, "Report") { Status = StepStatus.Skipped } };

        var failed = AgentPlanner.Summary("tidy up", steps, AgentOutcome.Failed);
        Assert.Contains("✓ 1. Scan", failed);
        Assert.Contains("✗ 2. Sort", failed);
        Assert.Contains("– 3. Report", failed);
        Assert.Contains("1 of 3 steps done", failed);
        Assert.Contains("No access.", failed);

        Assert.Contains("Stopped by you", AgentPlanner.Summary("tidy up", steps, AgentOutcome.Stopped));
        Assert.Contains("Finished", AgentPlanner.Summary("tidy up", steps, AgentOutcome.Completed));
    }
}

public class AgentRunnerTests
{
    private static List<AgentStep> Steps(int count) => Enumerable.Range(1, count).Select(i => new AgentStep(i, $"step {i}")).ToList();

    [Fact]
    public async Task RunsEveryStepInOrderAndReportsProgress()
    {
        var steps = Steps(3);
        var ran = new List<int>();
        var seen = new List<(int Number, StepStatus Status)>();

        var outcome = await new AgentRunner().RunAsync(steps, s => { ran.Add(s.Number); return Task.FromResult($"did {s.Number}"); },
            s => seen.Add((s.Number, s.Status)), CancellationToken.None);

        Assert.Equal(AgentOutcome.Completed, outcome);
        Assert.Equal([1, 2, 3], ran);
        Assert.All(steps, s => Assert.Equal(StepStatus.Done, s.Status));
        Assert.Equal("did 2", steps[1].Result);
        Assert.Equal((1, StepStatus.Running), seen[0]);
        Assert.Equal((1, StepStatus.Done), seen[1]);
        Assert.Equal(6, seen.Count);
    }

    [Fact]
    public async Task AFailedStepEndsThePlanAndSkipsTheRest()
    {
        var steps = Steps(4);
        var outcome = await new AgentRunner().RunAsync(steps,
            s => s.Number == 2 ? throw new LlmException("The model is unavailable.") : Task.FromResult("ok"), null, CancellationToken.None);

        Assert.Equal(AgentOutcome.Failed, outcome);
        Assert.Equal([StepStatus.Done, StepStatus.Failed, StepStatus.Skipped, StepStatus.Skipped], steps.Select(s => s.Status));
        Assert.Equal("The model is unavailable.", steps[1].Result);
    }

    [Fact]
    public async Task StoppingLetsTheCurrentStepFinishThenSkipsTheRest()
    {
        var steps = Steps(4);
        using var stop = new CancellationTokenSource();

        var outcome = await new AgentRunner().RunAsync(steps, s =>
        {
            if (s.Number == 2) stop.Cancel(); // the user presses Stop while step 2 is running
            return Task.FromResult("ok");
        }, null, stop.Token);

        Assert.Equal(AgentOutcome.Stopped, outcome);
        Assert.Equal([StepStatus.Done, StepStatus.Done, StepStatus.Skipped, StepStatus.Skipped], steps.Select(s => s.Status));
    }

    [Fact]
    public async Task StoppingBeforeTheStartDoesNothing()
    {
        var steps = Steps(2);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var ran = false;

        var outcome = await new AgentRunner().RunAsync(steps, _ => { ran = true; return Task.FromResult(""); }, null, stop.Token);

        Assert.Equal(AgentOutcome.Stopped, outcome);
        Assert.False(ran);
        Assert.All(steps, s => Assert.Equal(StepStatus.Skipped, s.Status));
    }

    [Fact]
    public async Task StoppingDuringTheLastStepStillCountsAsFinished()
    {
        var steps = Steps(2);
        using var stop = new CancellationTokenSource();
        var outcome = await new AgentRunner().RunAsync(steps, s => { if (s.Number == 2) stop.Cancel(); return Task.FromResult("ok"); }, null, stop.Token);
        Assert.Equal(AgentOutcome.Completed, outcome);
    }

    [Fact]
    public async Task ARunNeverReusesStateFromAnEarlierOne()
    {
        var first = Steps(2);
        await new AgentRunner().RunAsync(first, _ => Task.FromResult("a"), null, CancellationToken.None);
        var second = Steps(2);
        Assert.All(second, s => Assert.Equal(StepStatus.Pending, s.Status));
    }

    // The whole path the window takes, with a fake model: ask for a plan, read it, then send each step as an ordinary request.
    [Fact]
    public async Task PlanThenRunEndToEndWithAFakeModel()
    {
        var model = new ScriptedModel(
            plan: "```json\n[\"Organize the Downloads folder\", \"Find the biggest files\", \"Tell the user what is big\"]\n```",
            replies: ["Moved 12 files.", "The biggest is setup.iso (4 GB).", "Your biggest file is setup.iso at 4 GB."]);

        var goal = "organise my Downloads and tell me what's big";
        var texts = AgentPlanner.Parse(await model.GenerateAsync(AgentPlanner.Instruction(["organize_folder", "find_files"]), goal));
        var steps = texts.Select((t, i) => new AgentStep(i + 1, t)).ToList();
        Assert.Equal(3, steps.Count);
        Assert.Equal(goal, model.PlanGoal);

        var outcome = await new AgentRunner().RunAsync(steps, step => model.SendAsync(AgentPlanner.StepPrompt(goal, step, steps.Count)), null, CancellationToken.None);

        Assert.Equal(AgentOutcome.Completed, outcome);
        Assert.Equal(3, model.Sent.Count);
        Assert.Contains("Organize the Downloads folder", model.Sent[0]);
        Assert.Contains("step 3 of 3", model.Sent[2]);
        Assert.All(model.Sent, p => Assert.Contains(goal, p));
        Assert.Equal("Your biggest file is setup.iso at 4 GB.", steps[2].Result);
        Assert.Contains("Finished: 3 of 3", AgentPlanner.Summary(goal, steps, outcome));
    }

    private sealed class ScriptedModel(string plan, string[] replies) : ILlm
    {
        private int _next;
        public List<string> Sent { get; } = [];
        public string? PlanGoal { get; private set; }

        public event Action<string>? ToolCalled { add { } remove { } }
        public event Action<string>? ToolFinished { add { } remove { } }

        public Task<string> GenerateAsync(string instruction, string? text = null, byte[]? png = null)
        {
            PlanGoal = text;
            return Task.FromResult(plan);
        }

        public Task<string> SendAsync(string userText)
        {
            Sent.Add(userText);
            return Task.FromResult(replies[_next++]);
        }

        public void ClearHistory() { }
        public bool IsCloud => false;
    }
}
