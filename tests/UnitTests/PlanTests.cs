using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WinCompanion.Services;
using WinCompanion.UI;
using Xunit;

namespace WinCompanion.UnitTests;

public class PlanStoreTests : DatabaseTestBase
{
    private static readonly string[] Steps = ["Open Downloads", "Sort by type", "Report what is big"];

    [Fact]
    public void ASavedPlanComesBackWithItsStepsInOrder()
    {
        var store = new PlanStore();
        Assert.StartsWith("Saved", store.Save("Tidy downloads", "tidy my downloads", Steps));
        var plan = store.Get("tidy DOWNLOADS")!;
        Assert.Equal("tidy my downloads", plan.Goal);
        Assert.Equal(Steps, plan.Steps);
    }

    [Fact]
    public void SavingAgainWithTheSameNameReplacesIt()
    {
        var store = new PlanStore();
        store.Save("Tidy", "one", ["a"]);
        store.Save("tidy", "two", ["b", "c"]);
        var plan = Assert.Single(store.List());
        Assert.Equal("two", plan.Goal);
        Assert.Equal(["b", "c"], plan.Steps);
    }

    [Theory]
    [InlineData("", new[] { "a" }, "name")]
    [InlineData("x", new string[0], "at least one")]
    [InlineData("x", new[] { "   " }, "at least one")]
    public void BadPlansAreRefused(string name, string[] steps, string expected) =>
        Assert.Contains(expected, new PlanStore().Save(name, "goal", steps));

    [Fact]
    public void TooManyOrTooLongStepsAreRefused()
    {
        var store = new PlanStore();
        Assert.Contains("at most", store.Save("x", "g", Enumerable.Range(1, 9).Select(i => $"step {i}").ToList()));
        Assert.Contains("at most", store.Save("x", "g", [new string('a', 201)]));
        Assert.Empty(store.List());
    }

    [Fact]
    public void ThereIsALimitOnHowManyPlansAreKept()
    {
        var store = new PlanStore();
        for (var i = 0; i < PlanStore.MaxPlans; i++) Assert.StartsWith("Saved", store.Save($"plan {i}", "g", ["s"]));
        Assert.Contains("up to", store.Save("one too many", "g", ["s"]));
        Assert.StartsWith("Saved", store.Save("plan 3", "changed", ["s"])); // replacing one is still fine
    }

    [Fact]
    public void DeleteRemovesIt()
    {
        var store = new PlanStore();
        store.Save("a", "g", ["s"]);
        Assert.True(store.Delete("A"));
        Assert.False(store.Delete("a"));
        Assert.Empty(store.List());
    }

    [Fact]
    public void ANameWithSpacesIsFoundAtTheStartOfCommandText()
    {
        var store = new PlanStore();
        store.Save("tidy", "g", ["s"]);
        store.Save("tidy downloads", "g", ["s"]);

        var (plan, rest) = store.FindByPrefix("Tidy Downloads every weekday at 9am")!.Value;
        Assert.Equal("tidy downloads", plan.Name);
        Assert.Equal("every weekday at 9am", rest);
        Assert.Equal("tidy", store.FindByPrefix("tidy at 8")!.Value.Plan.Name);
        Assert.Null(store.FindByPrefix("tidying every day")); // a longer word is not the name
        Assert.Null(store.FindByPrefix("nothing like it"));
    }

    [Theory]
    [InlineData("Tidy", "Tidy 2")]
    [InlineData("tidy", "tidy 2")]
    [InlineData("Fresh", "Fresh")]
    public void UniqueNamesAvoidClashes(string wanted, string expected) =>
        Assert.Equal(expected, PlanStore.UniqueName(wanted, ["Tidy"]));

    [Fact]
    public void LongOrMessyNamesAreCleaned()
    {
        Assert.Equal("a b", PlanStore.CleanName("  a\u0007   b \n"));
        Assert.True(PlanStore.CleanName(new string('x', 100)).Length <= PlanStore.MaxNameLength);
        Assert.Equal("Plan", PlanStore.UniqueName("   ", []));
    }

    [Fact]
    public void CorruptStepsInTheDatabaseDoNotBreakListing()
    {
        var store = new PlanStore();
        store.Save("good", "g", ["s"]);
        using (var db = Database.Open())
        using (var command = db.CreateCommand())
        {
            command.CommandText = "INSERT INTO agent_plans(name, goal, steps, created) VALUES ('bad', 'g', 'not json', '2026-01-01')";
            command.ExecuteNonQuery();
        }
        Assert.Equal(["good"], store.List().Select(p => p.Name));
    }
}

public class PlanScheduleTests : DatabaseTestBase
{
    private static readonly DateTime Now = new(2026, 11, 10, 8, 0, 0); // a Tuesday

    private static SavedPlan Plan(string name = "tidy") => new(name, "tidy my downloads", ["a"], Now);

    [Fact]
    public void ThePlanReminderIsMarkedAndNamed()
    {
        var message = PlanSchedule.MessageFor("tidy downloads");
        Assert.True(PlanSchedule.IsPlanReminder(message));
        Assert.Equal("tidy downloads", PlanSchedule.NameOf(message));
        Assert.Null(PlanSchedule.NameOf("buy milk"));
        Assert.False(PlanSchedule.IsPlanReminder(FocusService.Mark + "x"));
    }

    [Fact]
    public void ARepeatingScheduleIsARepeatingMarkedReminder()
    {
        var reminders = new ReminderStore();
        var reply = PlanSchedule.Schedule(reminders, Plan(), "every weekday at 9am", Now);

        var added = Assert.Single(reminders.Pending());
        Assert.Equal(PlanSchedule.MessageFor("tidy"), added.Message);
        Assert.Equal("weekdays", added.Repeat);
        Assert.Equal(new DateTime(2026, 11, 10, 9, 0, 0), added.Due);
        Assert.Contains("nothing runs until you press Run plan", reply);
    }

    [Fact]
    public void SchedulingAgainReplacesTheEarlierSchedule()
    {
        var reminders = new ReminderStore();
        PlanSchedule.Schedule(reminders, Plan(), "every day at 9am", Now);
        PlanSchedule.Schedule(reminders, Plan(), "every Friday at 5pm", Now);
        var only = Assert.Single(reminders.Pending());
        Assert.Equal("weekly", only.Repeat);
    }

    [Fact]
    public void EachPlanHasItsOwnSchedule()
    {
        var reminders = new ReminderStore();
        PlanSchedule.Schedule(reminders, Plan("a"), "tomorrow at 8", Now);
        PlanSchedule.Schedule(reminders, Plan("b"), "tomorrow at 9", Now);
        Assert.True(PlanSchedule.Unschedule(reminders, "A"));
        Assert.Equal(PlanSchedule.MessageFor("b"), Assert.Single(reminders.Pending()).Message);
        Assert.False(PlanSchedule.Unschedule(reminders, "a"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("whenever")]
    public void WithoutAUsableTimeNothingIsScheduled(string when)
    {
        var reminders = new ReminderStore();
        var reply = PlanSchedule.Schedule(reminders, Plan(), when, Now);
        Assert.Empty(reminders.Pending());
        Assert.NotEmpty(reply);
    }

    [Fact]
    public void ADescriptionTellsWhenAndHowOften()
    {
        var reminders = new ReminderStore();
        Assert.Null(PlanSchedule.Describe(reminders.Pending(), "tidy", Now));
        PlanSchedule.Schedule(reminders, Plan(), "every weekday at 9am", Now);
        var text = PlanSchedule.Describe(reminders.Pending(), "tidy", Now)!;
        Assert.Contains("every weekday", text);
    }
}

public class PlanBackupTests : DatabaseTestBase
{
    [Fact]
    public void PlansTravelInABackupAndAreNeverOverwrittenOnRestore()
    {
        var folder = Directory.CreateTempSubdirectory("tessa-planbackup").FullName;
        var zip = Path.Combine(folder, "b.zip");
        var notes = Path.Combine(folder, "n.md");
        var plans = new PlanStore();
        plans.Save("tidy", "tidy my downloads", ["a", "b"]);
        plans.Save("report", "weekly report", ["x"]);
        Backup.Create(zip, new AppSettings(), new MemoryStore(), new RoutineStore(), new ReminderStore(), notes, plans);

        Assert.Equal(2, Backup.Inspect(zip, out _)!.Plans);

        Database.Path = Path.Combine(folder, "fresh.db");
        var fresh = new PlanStore();
        fresh.Save("tidy", "MY OWN VERSION", ["keep me"]);
        var report = Backup.Restore(zip, new AppSettings(), new MemoryStore(), new RoutineStore(), new ReminderStore(), notes, null, fresh);

        Assert.Equal(1, report.Plans); // "report" came back; "tidy" was left alone
        Assert.Equal(["keep me"], fresh.Get("tidy")!.Steps);
        Assert.Equal(["x"], fresh.Get("report")!.Steps);
    }

    [Fact]
    public void ATamperedPlanWithTooManyStepsIsSkipped()
    {
        var folder = Directory.CreateTempSubdirectory("tessa-planbackup").FullName;
        var zip = Path.Combine(folder, "b.zip");
        var notes = Path.Combine(folder, "n.md");
        Backup.Create(zip, new AppSettings(), new MemoryStore(), new RoutineStore(), new ReminderStore(), notes, new PlanStore());
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            archive.GetEntry("plans.json")?.Delete();
            using var w = new StreamWriter(archive.CreateEntry("plans.json").Open());
            w.Write("[{\"name\":\"big\",\"goal\":\"g\",\"steps\":[\"1\",\"2\",\"3\",\"4\",\"5\",\"6\",\"7\",\"8\",\"9\"]}]");
        }
        var plans = new PlanStore();
        var report = Backup.Restore(zip, new AppSettings(), new MemoryStore(), new RoutineStore(), new ReminderStore(), notes, null, plans);
        Assert.Equal(0, report.Plans);
        Assert.Contains(report.Skipped, s => s.Contains("big"));
        Assert.Empty(plans.List());
    }
}

[Collection("wpf")]
public class EditablePlanCardTests
{
    private static object Res(string key) => key switch
    {
        "PrimaryButton" or "GhostButton" or "IconButton" or "Field" => null!,
        _ => Brushes.Gray
    };

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

    private static List<AgentStep> Steps() => [new(1, "First"), new(2, "Second"), new(3, "Third")];

    private static void Click(AgentPlanCard card, string tip, int nth = 0) =>
        All<Button>(card.Root).Where(b => (string?)b.ToolTip == tip && b.IsEnabled).ElementAt(nth).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static void Click(AgentPlanCard card, string content) =>
        All<Button>(card.Root).Single(b => (b.Content as string) == content).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static string Order(AgentPlanCard card) => string.Join("|", card.Steps.Select(s => $"{s.Number}:{s.Text}"));

    [Fact]
    public void StepsCanBeMovedUpAndDown()
    {
        var (afterDown, afterUp) = OnStaThread(() =>
        {
            var card = new AgentPlanCard("g", Steps(), Res);
            Click(card, "Move down", 0); // the first step's down button
            var down = Order(card);
            Click(card, "Move up", 0);   // the first enabled up button belongs to the second row
            return (down, Order(card));
        });
        Assert.Equal("1:Second|2:First|3:Third", afterDown);
        Assert.Equal("1:First|2:Second|3:Third", afterUp);
    }

    [Fact]
    public void AStepCanBeRemovedButNotTheLastOne()
    {
        var (after, last) = OnStaThread(() =>
        {
            var card = new AgentPlanCard("g", Steps(), Res);
            Click(card, "Remove this step", 1);
            var first = Order(card);
            Click(card, "Remove this step", 0);
            var remaining = card.Steps.Count;
            return (first, remaining + ":" + All<Button>(card.Root).Count(b => (string?)b.ToolTip == "Remove this step" && b.IsEnabled));
        });
        Assert.Equal("1:First|2:Third", after);
        Assert.Equal("1:0", last); // one step left and its remove button is off
    }

    [Fact]
    public void AStepCanBeReworded()
    {
        var result = OnStaThread(() =>
        {
            var card = new AgentPlanCard("g", Steps(), Res);
            Click(card, "Reword this step", 1);
            var box = All<TextBox>(card.Root).Single();
            Assert.Equal("Second", box.Text);
            box.Text = "  Sort   the files  by date ";
            box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new FakeSource(), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            return Order(card);
        });
        Assert.Equal("1:First|2:Sort the files by date|3:Third", result);
    }

    [Fact]
    public void EscapeKeepsTheOldWordingAndAnEmptyStepIsDropped()
    {
        var (escaped, emptied) = OnStaThread(() =>
        {
            var card = new AgentPlanCard("g", Steps(), Res);
            Click(card, "Reword this step", 0);
            var box = All<TextBox>(card.Root).Single();
            box.Text = "changed my mind";
            box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new FakeSource(), 0, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
            var first = Order(card);

            Click(card, "Reword this step", 2);
            All<TextBox>(card.Root).Single().Text = "   ";
            All<TextBox>(card.Root).Single().RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new FakeSource(), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            return (first, Order(card));
        });
        Assert.Equal("1:First|2:Second|3:Third", escaped);
        Assert.Equal("1:First|2:Second", emptied);
    }

    [Fact]
    public void AddingAStepOpensAnEditorAndStopsAtTheLimit()
    {
        var (afterAdd, hidden) = OnStaThread(() =>
        {
            var card = new AgentPlanCard("g", Steps(), Res);
            Click(card, "＋ Add a step");
            var box = All<TextBox>(card.Root).Single();
            box.Text = "Fourth";
            box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new FakeSource(), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            var added = Order(card);

            for (var i = card.Steps.Count; i < AgentPlanner.MaxSteps; i++)
            {
                Click(card, "＋ Add a step");
                All<TextBox>(card.Root).Single().Text = $"step {i + 1}";
                All<TextBox>(card.Root).Single().RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new FakeSource(), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            }
            var add = All<Button>(card.Root).Single(b => (b.Content as string) == "＋ Add a step");
            return (added, add.Visibility == Visibility.Collapsed && card.Steps.Count == AgentPlanner.MaxSteps);
        });
        Assert.Equal("1:First|2:Second|3:Third|4:Fourth", afterAdd);
        Assert.True(hidden);
    }

    [Fact]
    public void RunUsesTheEditedStepsAndTheEditingControlsGoAway()
    {
        var (steps, controls, decision) = OnStaThread(() =>
        {
            var card = new AgentPlanCard("g", Steps(), Res);
            Click(card, "Remove this step", 0);
            Click(card, "Run plan");
            var left = All<Button>(card.Root).Count(b => (string?)b.ToolTip is "Move up" or "Move down" or "Reword this step" or "Remove this step");
            return (Order(card), left, card.WaitForDecisionAsync().IsCompletedSuccessfully && card.WaitForDecisionAsync().Result);
        });
        Assert.Equal("1:Second|2:Third", steps);
        Assert.Equal(0, controls);
        Assert.True(decision);
    }

    [Fact]
    public void SavePlanOnlyAppearsWhenSavingIsPossibleAndReportsBack()
    {
        var (without, texts, status) = OnStaThread(() =>
        {
            var plain = new AgentPlanCard("g", Steps(), Res);
            var noSave = All<Button>(plain.Root).Single(b => (b.Content as string) == "Save plan").Visibility == Visibility.Collapsed;

            IReadOnlyList<string>? saved = null;
            var card = new AgentPlanCard("g", Steps(), Res) { SavePlan = t => { saved = t; return "Saved it."; } };
            card.Refresh();
            Click(card, "Reword this step", 0);
            var box = All<TextBox>(card.Root).Single();
            box.Text = "Edited first";
            Click(card, "Save plan"); // an open edit is kept when saving
            return (noSave, saved, All<TextBlock>(card.Root).Select(t => t.Text).ToList());
        });
        Assert.True(without);
        Assert.Equal(["Edited first", "Second", "Third"], texts);
        Assert.Contains("Saved it.", status);
    }

    private sealed class FakeSource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
