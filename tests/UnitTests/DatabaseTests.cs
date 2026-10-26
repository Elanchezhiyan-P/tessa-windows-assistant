using System.IO;
using WinCompanion.Services;
using Xunit;

namespace WinCompanion.UnitTests;

/// <summary>Each test gets a fresh temporary database. They share one static path, so these classes never run in parallel.</summary>
[Collection("database")]
public abstract class DatabaseTestBase : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("tessa-db").FullName;

    protected DatabaseTestBase() => Database.Path = Path.Combine(_dir, "test.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}

public class ChatStoreTests : DatabaseTestBase
{
    private static ChatEntry Entry(string role, string text, int minute = 0) =>
        new(role, text, new DateTime(2026, 10, 26, 9, minute % 60, 0));

    [Fact]
    public void MessagesSurviveARestart()
    {
        var first = new ChatStore();
        first.Append(Entry("user", "hello"));
        first.Append(Entry("assistant", "hi there"));

        var second = new ChatStore();
        Assert.Equal(["hello", "hi there"], second.Entries.Select(e => e.Text));
    }

    [Fact]
    public void OnlyTheNewestPageIsLoadedAndOlderOnesCanBeFetched()
    {
        var store = new ChatStore();
        for (var i = 0; i < 250; i++) store.Append(Entry("user", $"message {i}", i));

        var reopened = new ChatStore();
        Assert.Equal(200, reopened.Entries.Count);
        Assert.Equal("message 50", reopened.Entries[0].Text);
        Assert.True(reopened.HasOlder);

        var older = reopened.LoadOlder(100);
        Assert.Equal(50, older.Count);
        Assert.Equal("message 0", reopened.Entries[0].Text);
        Assert.False(reopened.HasOlder);
    }

    [Fact]
    public void SearchFindsWordsInOldMessagesNewestFirst()
    {
        var store = new ChatStore();
        store.Append(Entry("user", "please pay the electricity invoice"));
        store.Append(Entry("assistant", "Which invoice number?"));
        store.Append(Entry("user", "what is the weather"));

        var found = ChatStore.Search("invoice");
        Assert.Equal(2, found.Count);
        Assert.Equal("Which invoice number?", found[0].Text);
    }

    [Fact]
    public void SearchIgnoresToolNoiseAndPunctuation()
    {
        var store = new ChatStore();
        store.Append(Entry("tool", "run_powershell invoice"));
        Assert.Empty(ChatStore.Search("invoice"));
        Assert.Empty(ChatStore.Search("\"AND OR ("));
    }

    [Fact]
    public void ClearRemovesEverything()
    {
        var store = new ChatStore();
        store.Append(Entry("user", "secret"));
        store.Clear();
        Assert.Empty(new ChatStore().Entries);
        Assert.Empty(ChatStore.Search("secret"));
    }
}

public class MemoryStoreTests : DatabaseTestBase
{
    [Fact]
    public void RemembersAndFormsAPromptBlock()
    {
        var store = new MemoryStore();
        Assert.StartsWith("Remembered", store.Remember("The user's folder is D:/Work"));
        Assert.Contains("D:/Work", new MemoryStore().PromptBlock());
    }

    [Fact]
    public void DuplicatesAreIgnoredAndForgetWorks()
    {
        var store = new MemoryStore();
        store.Remember("likes tea");
        Assert.Equal("I already know that.", store.Remember("Likes TEA"));
        Assert.Contains("Forgot 1", store.Forget("tea"));
        Assert.Equal("(nothing remembered yet)", store.Describe());
    }

    [Fact]
    public void KeepsOnlyTheNewestHundredFacts()
    {
        var store = new MemoryStore();
        for (var i = 0; i < 105; i++) store.Remember($"fact {i}");
        var text = store.Describe();
        Assert.DoesNotContain("fact 0\n", text + "\n");
        Assert.Contains("fact 104", text);
        Assert.Equal(100, text.Split('\n').Length);
    }
}

public class ReminderStoreTests : DatabaseTestBase
{
    private static readonly DateTime Now = new(2026, 10, 26, 10, 0, 0);

    [Fact]
    public void RemindersSurviveARestartInDueOrder()
    {
        var store = new ReminderStore();
        store.Add("later", Now.AddHours(2));
        store.Add("sooner", Now.AddMinutes(5));

        Assert.Equal(["sooner", "later"], new ReminderStore().Pending().Select(r => r.Message));
    }

    [Fact]
    public void OneOffRemindersAreConsumedWhenDue()
    {
        var store = new ReminderStore();
        store.Add("stretch", Now.AddMinutes(10));

        Assert.Empty(store.TakeDue(Now));
        Assert.Single(store.TakeDue(Now.AddMinutes(11)));
        Assert.Empty(store.Pending());
    }

    [Fact]
    public void RepeatingRemindersMoveToTheirNextTime()
    {
        var store = new ReminderStore();
        store.Add("standup", Now.AddMinutes(-1), "daily");

        Assert.Single(store.TakeDue(Now));
        var next = Assert.Single(store.Pending());
        Assert.Equal(Now.AddMinutes(-1).AddDays(1), next.Due);
    }

    [Fact]
    public void CancelAndClear()
    {
        var store = new ReminderStore();
        store.Add("a", Now.AddMinutes(1));
        store.Add("b", Now.AddMinutes(2));

        Assert.Equal("a", store.CancelAt(1)?.Message);
        Assert.Null(store.CancelAt(5));
        Assert.Equal(1, store.Clear());
    }
}
