using System.IO;
using System.Text.Json.Nodes;
using WinCompanion.Services;
using WinCompanion.Tools;
using Xunit;

namespace WinCompanion.UnitTests;

public class QuickCaptureTests
{
    [Theory]
    [InlineData("buy milk", "buy milk", false)]
    [InlineData("todo: call mom", "call mom", true)]
    [InlineData("To-Do send the invoice", "send the invoice", true)]
    [InlineData("[] water plants", "water plants", true)]
    public void ParsesNotesAndTodos(string input, string text, bool todo) =>
        Assert.Equal((text, todo), NotesTools.ParseCapture(input));
}

public class RoutineNormalizeTests
{
    [Theory]
    [InlineData("Good morning!", "good morning")]
    [InlineData("please run my morning routine", "morning")]
    [InlineData("Start the work routine.", "work")]
    public void StripsPolitenessAndWrappers(string input, string expected) =>
        Assert.Equal(expected, RoutineStore.Normalize(input));
}

public class ClipboardStoreTests : DatabaseTestBase
{
    [Fact]
    public void KeepsTheLastHundredAndSkipsRepeatsAndHugeText()
    {
        Assert.True(ClipboardStore.Add("first"));
        Assert.False(ClipboardStore.Add("first"));
        Assert.False(ClipboardStore.Add("   "));
        Assert.False(ClipboardStore.Add(new string('x', ClipboardStore.MaxLength + 1)));

        for (var i = 0; i < 110; i++) ClipboardStore.Add($"copy {i}");
        Assert.Equal(100, ClipboardStore.Find(null, null, 1000).Count);
        Assert.Equal("copy 109", ClipboardStore.Find(null, null, 1)[0].Text);
    }

    [Fact]
    public void FindsByWordsAndByTime()
    {
        ClipboardStore.Add("https://github.com/example/repo", new DateTime(2026, 10, 26, 8, 30, 0));
        ClipboardStore.Add("shopping list", new DateTime(2026, 10, 26, 14, 0, 0));

        Assert.Single(ClipboardStore.Find("github", null));
        Assert.Single(ClipboardStore.Find(null, new DateTime(2026, 10, 26, 12, 0, 0)));
        Assert.Empty(ClipboardStore.Find("github", new DateTime(2026, 10, 26, 12, 0, 0)));
    }

    [Fact]
    public void ClearEmptiesIt()
    {
        ClipboardStore.Add("a");
        Assert.Equal(1, ClipboardStore.Clear());
        Assert.Empty(ClipboardStore.Find(null, null));
    }
}

public class RoutineStoreTests : DatabaseTestBase
{
    private static RoutineStep Step(string tool, string json = "{}") => new(tool, (JsonObject)JsonNode.Parse(json)!);

    [Fact]
    public void SavesFindsByNameOrPhraseAndDeletes()
    {
        var store = new RoutineStore();
        store.Save("morning", "good morning", [Step("open_target", "{\"target\":\"outlook\"}"), Step("change_volume", "{\"level\":30}")]);

        Assert.Equal(2, store.Find("Good morning!")!.Steps.Count);
        Assert.NotNull(store.Find("run my morning routine"));
        Assert.Equal("outlook", store.Get("MORNING")!.Steps[0].Args["target"]!.GetValue<string>());
        Assert.Null(store.Find("good night"));

        Assert.True(store.Delete("morning"));
        Assert.Empty(store.List());
    }

    [Fact]
    public void SavingAgainReplacesTheRoutine()
    {
        var store = new RoutineStore();
        store.Save("work", null, [Step("a")], _ => true);
        store.Save("work", "start work", [Step("b"), Step("c")], _ => true);
        Assert.Equal(2, store.Get("work")!.Steps.Count);
        Assert.Single(store.List());
    }

    [Fact]
    public void RejectsNestedRoutinesUnknownToolsAndEmptyOnes()
    {
        var store = new RoutineStore();
        Assert.Contains("can't contain", store.Save("x", null, [Step("run_routine")]));
        Assert.Contains("no tool called", store.Save("x", null, [Step("nope")], name => name == "yes"));
        Assert.Contains("at least one", store.Save("x", null, []));
        Assert.Empty(store.List());
    }
}

public class FocusServiceTests : DatabaseTestBase
{
    private static readonly DateTime Now = new(2026, 10, 26, 10, 0, 0);

    [Fact]
    public void ASessionSchedulesBreakReminders()
    {
        var reminders = new ReminderStore();
        var focus = new FocusService(reminders);
        focus.Start(25, 5, 2, Now);

        var pending = reminders.Pending();
        Assert.Equal(3, pending.Count); // break after round 1, back to focus, done after round 2
        Assert.Equal(Now.AddMinutes(25), pending[0].Due);
        Assert.All(pending, r => Assert.True(FocusService.IsFocusReminder(r.Message)));

        Assert.True(focus.IsWorking(Now.AddMinutes(10)));
        Assert.False(focus.IsWorking(Now.AddMinutes(27))); // on a break
        Assert.True(focus.IsWorking(Now.AddMinutes(31)));
        Assert.Equal(Now.AddMinutes(25), focus.WorkEnds(Now.AddMinutes(10)));
    }

    [Fact]
    public void StopRemovesOnlyFocusReminders()
    {
        var reminders = new ReminderStore();
        var focus = new FocusService(reminders);
        reminders.Add("call mom", Now.AddHours(1));
        focus.Start(25, 5, 1, Now);

        Assert.True(focus.Stop());
        Assert.Equal(["call mom"], reminders.Pending().Select(r => r.Message));
        Assert.False(focus.IsWorking(Now.AddMinutes(5)));
        Assert.False(focus.Stop());
    }

    [Theory]
    [InlineData("pomodoro", true)]
    [InlineData("start focus mode", true)]
    [InlineData("focus for 45 minutes", true)]
    [InlineData("what is the weather", false)]
    public void RecognisesFocusPhrases(string text, bool handled)
    {
        var focus = new FocusService(new ReminderStore());
        Assert.Equal(handled, focus.TryHandle(text) is not null);
    }

    [Fact]
    public void StopPhraseTurnsItOff()
    {
        var focus = new FocusService(new ReminderStore());
        focus.TryHandle("focus 20 minutes");
        Assert.Equal("Focus mode is off.", focus.TryHandle("stop focus"));
    }
}

public class BriefingTests
{
    private static readonly DateTime Morning = new(2026, 10, 26, 8, 0, 0);

    [Theory]
    [InlineData(8, "Good morning, Ela")]
    [InlineData(14, "Good afternoon, Ela")]
    [InlineData(20, "Good evening, Ela")]
    public void GreetsByTimeOfDay(int hour, string expected) =>
        Assert.Equal(expected, BriefingService.Greeting(new DateTime(2026, 10, 26, hour, 0, 0), "Ela"));

    [Fact]
    public void ComposesRemindersWeatherAndHeadlines()
    {
        var reminders = new[] { new Reminder(Guid.NewGuid(), "stand-up", Morning.AddHours(1), Morning, "weekdays") };
        var news = new List<(string, List<(string, string)>)> { ("cricket", [("Match report", "https://example.com/a")]) };

        var text = BriefingService.Compose(Morning, "Ela", reminders, "Chennai: Sunny, 31°C", news);

        Assert.Contains("Good morning, Ela", text);
        Assert.Contains("9:00 AM: stand-up (repeats every weekday)", text);
        Assert.Contains("Chennai: Sunny", text);
        Assert.Contains("[Match report](https://example.com/a)", text);
    }

    [Fact]
    public void SaysWhenNothingIsScheduled()
    {
        var text = BriefingService.Compose(Morning, null, [], null, []);
        Assert.Contains("Nothing scheduled", text);
        Assert.DoesNotContain("Weather", text);
    }
}

public class ChatExportTests : DatabaseTestBase
{
    [Fact]
    public void MarkdownGroupsByDayAndNamesTheSpeakers()
    {
        var entries = new[]
        {
            new ChatEntry("user", "hello", new DateTime(2026, 10, 25, 9, 0, 0)),
            new ChatEntry("assistant", "hi!", new DateTime(2026, 10, 26, 9, 1, 0))
        };
        var md = ChatExport.ToMarkdown(entries, "Chat", "Tessa");

        Assert.StartsWith("# Chat", md);
        Assert.Contains("## Sunday, 25 October 2026", md);
        Assert.Contains("## Monday, 26 October 2026", md);
        Assert.Contains("**You** · 09:00", md);
        Assert.Contains("**Tessa** · 09:01", md);
    }

    [Fact]
    public void NothingToExportGivesNoFile()
    {
        Assert.Null(ChatExport.Save([], "x", "Tessa"));
        Assert.Equal("There is nothing to export yet.", ChatExport.Export(null, "Tessa"));
    }
}
