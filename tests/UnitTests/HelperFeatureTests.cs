using System.IO;
using System.Net;
using System.Net.Http;
using WinCompanion.Services;
using Xunit;

namespace WinCompanion.UnitTests;

public class UndoLogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("tessa-undo").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string P(string name) => Path.Combine(_dir, name);

    [Fact]
    public void PutsAMovedFileBack()
    {
        File.WriteAllText(P("a.txt"), "x");
        File.Move(P("a.txt"), P("b.txt"));
        var log = new UndoLog();
        log.Record("move a.txt", [new UndoMove(P("a.txt"), P("b.txt"))]);

        Assert.Contains("put 1 item(s) back", log.Undo());
        Assert.True(File.Exists(P("a.txt")));
        Assert.False(File.Exists(P("b.txt")));
        Assert.Equal(0, log.Count);
    }

    [Fact]
    public void UndoesATidyUpAndRemovesTheFoldersItMade()
    {
        File.WriteAllText(P("one.png"), "1");
        File.WriteAllText(P("two.png"), "2");
        Directory.CreateDirectory(P("Images"));
        File.Move(P("one.png"), P("Images\\one.png"));
        File.Move(P("two.png"), P("Images\\two.png"));
        var log = new UndoLog();
        log.Record("tidy", [new UndoMove(P("one.png"), P("Images\\one.png")), new UndoMove(P("two.png"), P("Images\\two.png"))], [P("Images")]);

        log.Undo();
        Assert.True(File.Exists(P("one.png")) && File.Exists(P("two.png")));
        Assert.False(Directory.Exists(P("Images")));
    }

    [Fact]
    public void UndoesTheNewestActionFirst()
    {
        File.WriteAllText(P("a.txt"), "x");
        File.Move(P("a.txt"), P("b.txt"));
        File.Move(P("b.txt"), P("c.txt"));
        var log = new UndoLog();
        log.Record("first", [new UndoMove(P("a.txt"), P("b.txt"))]);
        log.Record("second", [new UndoMove(P("b.txt"), P("c.txt"))]);

        log.Undo();
        Assert.True(File.Exists(P("b.txt")));
        log.Undo();
        Assert.True(File.Exists(P("a.txt")));
    }

    [Fact]
    public void NeverOverwritesAndExplainsWhy()
    {
        File.WriteAllText(P("a.txt"), "new file in the old place");
        File.WriteAllText(P("b.txt"), "moved file");
        var log = new UndoLog();
        log.Record("move", [new UndoMove(P("a.txt"), P("b.txt"))]);

        var reply = log.Undo();
        Assert.Contains("already exists", reply);
        Assert.Equal("moved file", File.ReadAllText(P("b.txt")));
        Assert.Equal("new file in the old place", File.ReadAllText(P("a.txt")));
    }

    [Fact]
    public void SaysSoWhenThereIsNothingToUndo() => Assert.Contains("nothing to undo", new UndoLog().Undo());
}

public class NudgeTests : DatabaseTestBase
{
    private static readonly DateTime Now = new(2026, 10, 30, 10, 0, 0);

    [Fact]
    public void StartsARepeatingReminderThatCanBeRecognised()
    {
        var reminders = new ReminderStore();
        new NudgeService(reminders).Start(NudgeService.Find("water")!, 45, Now);

        var reminder = Assert.Single(reminders.Pending());
        Assert.True(NudgeService.IsNudge(reminder.Message));
        Assert.Equal("every:45", reminder.Repeat);
        Assert.Equal(Now.AddMinutes(45), reminder.Due);
    }

    [Fact]
    public void RestartingReplacesTheOldOne()
    {
        var reminders = new ReminderStore();
        var nudges = new NudgeService(reminders);
        nudges.Start(NudgeService.Find("eyes")!, 20, Now);
        nudges.Start(NudgeService.Find("eyes")!, 30, Now);

        Assert.Equal("every:30", Assert.Single(reminders.Pending()).Repeat);
    }

    [Fact]
    public void StopsOneKindOrAll()
    {
        var reminders = new ReminderStore();
        var nudges = new NudgeService(reminders);
        nudges.Start(NudgeService.Find("water")!, null, Now);
        nudges.Start(NudgeService.Find("posture")!, null, Now);
        reminders.Add("call mom", Now.AddHours(1));

        Assert.Contains("water nudge is off", nudges.Stop("water"));
        Assert.Equal(2, reminders.Pending().Count);

        Assert.Contains("All nudges are off", nudges.Stop(null));
        Assert.Equal("call mom", Assert.Single(reminders.Pending()).Message); // other reminders are untouched
    }

    [Fact]
    public void KeepsTheIntervalSensible()
    {
        var reminders = new ReminderStore();
        new NudgeService(reminders).Start(NudgeService.Find("stand")!, 1, Now);
        Assert.Equal("every:5", Assert.Single(reminders.Pending()).Repeat);
    }

    [Fact]
    public void RejectsUnknownNames()
    {
        Assert.Null(NudgeService.Find("coffee"));
        Assert.Contains("don't know", new NudgeService(new ReminderStore()).Stop("coffee"));
    }

    [Fact]
    public void OrdinaryRemindersAreNotNudges()
    {
        Assert.False(NudgeService.IsNudge("call mom"));
        Assert.False(NudgeService.IsNudge(FocusService.Mark + "Break over"));
    }
}

public class ChatSearchTests
{
    [Fact]
    public void ListsMatchesWithASnippetAroundTheWord()
    {
        var text = string.Concat(Enumerable.Repeat("filler ", 40)) + "the invoice is due Friday " + string.Concat(Enumerable.Repeat("more ", 60));
        var reply = ChatSearch.Format([new ChatEntry("user", text, new DateTime(2026, 10, 1, 9, 5, 0))], "invoice", "Tessa");

        Assert.Contains("1 message(s)", reply);
        Assert.Contains("1 Oct 2026 09:05 · You:", reply);
        Assert.Contains("invoice is due Friday", reply);
        Assert.Contains("…", reply);
        Assert.True(reply.Length < text.Length);
    }

    [Fact]
    public void SaysSoWhenNothingMatches() =>
        Assert.Contains("Nothing in your chat history matches \"zebra\"", ChatSearch.Format([], " zebra ", "Tessa"));

    [Fact]
    public void NamesTheAssistantNotTheModel() =>
        Assert.Contains("Tessa:", ChatSearch.Format([new ChatEntry("assistant", "hello", DateTime.Now)], "hello", "Tessa"));
}

public class UpdateCheckTests
{
    private static readonly Version Current = new(2, 0, 0);

    [Theory]
    [InlineData("v2.1.0", 2, 1, 0)]
    [InlineData("2.1", 2, 1, 0)]
    [InlineData("v3.0.2-beta.1", 3, 0, 2)]
    public void ReadsTags(string tag, int major, int minor, int build)
    {
        Assert.True(UpdateCheck.TryParseTag(tag, out var version));
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData(null)]
    public void RejectsBadTags(string? tag) => Assert.False(UpdateCheck.TryParseTag(tag, out _));

    [Fact]
    public void AnnouncesANewerRelease()
    {
        var reply = UpdateCheck.Describe(Current, """{"tag_name":"v2.1.0","html_url":"https://example.test/r"}""");
        Assert.Contains("Version 2.1.0 is available", reply);
        Assert.Contains("https://example.test/r", reply);
    }

    [Theory]
    [InlineData("v2.0.0")]
    [InlineData("v1.9.9")]
    public void SaysUpToDateOtherwise(string tag) =>
        Assert.Contains("up to date", UpdateCheck.Describe(Current, $$"""{"tag_name":"{{tag}}"}"""));

    [Fact]
    public void SurvivesJunk()
    {
        Assert.Contains("couldn't read", UpdateCheck.Describe(Current, "not json"));
        Assert.Contains("couldn't read", UpdateCheck.Describe(Current, "{}"));
    }

    private sealed class FakeHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    [Fact]
    public async Task ChecksOverHttp()
    {
        using var http = new HttpClient(new FakeHandler(HttpStatusCode.OK, """{"tag_name":"v99.0.0","html_url":"https://example.test"}"""));
        Assert.Contains("Version 99.0.0 is available", await UpdateCheck.CheckAsync(http));
    }

    [Fact]
    public async Task ExplainsWhenThereAreNoReleases()
    {
        using var http = new HttpClient(new FakeHandler(HttpStatusCode.NotFound, ""));
        Assert.Contains("No releases", await UpdateCheck.CheckAsync(http));
    }
}

public class DiagnosticsTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static SystemSpecs Specs() => new("Windows 11", "CPU", 4, 8, 0, 0, 0, 16 * Gb, 8 * Gb, [], []);

    [Fact]
    public void HidesTheUserAndPcNames()
    {
        var text = Diagnostics.Redact(@"at C:\Users\DELL\Documents\x.cs on DELLPC by dell", @"C:\Users\DELL", "DELL", "DELLPC");
        Assert.DoesNotContain("DELL", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%USERPROFILE%", text);
        Assert.Contains("<pc>", text);
        Assert.Contains("<user>", text);
    }

    [Fact]
    public void LeavesWordsThatMerelyContainTheName() =>
        Assert.Equal("Dellwood", Diagnostics.Redact("Dellwood", null, "Dell", null));

    [Fact]
    public void IncludesTheEssentialsAndNoSecrets()
    {
        var settings = new AppSettings { Provider = "local", LocalModel = "qwen2.5:3b", UserName = "Secret Person", BriefingCity = "Hidden City" };
        var text = Diagnostics.Build(Specs(), settings, "boom at line 3");

        Assert.Contains("Tessa", text);
        Assert.Contains("local (qwen2.5:3b)", text);
        Assert.Contains("16 GB total", text);
        Assert.Contains("boom at line 3", text);
        Assert.DoesNotContain("Secret Person", text);
        Assert.DoesNotContain("Hidden City", text);
    }

    [Fact]
    public void SaysWhenThereAreNoErrors() =>
        Assert.Contains("(none)", Diagnostics.Build(Specs(), new AppSettings(), null));
}
