using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Data.Sqlite;
using WinCompanion.Services;
using WinCompanion.UI;
using Xunit;

namespace WinCompanion.UnitTests;

public class ConversationTests : DatabaseTestBase
{
    private static ChatEntry Say(string role, string text) => new(role, text, DateTime.Now);

    [Fact]
    public void AFreshDatabaseHasOneEmptyUnnamedChat()
    {
        var store = new ChatStore();
        Assert.Empty(store.Entries);
        var chat = Assert.Single(store.Conversations());
        Assert.Equal(Database.DefaultChatTitle, chat.Title);
    }

    [Fact]
    public void TheFirstThingYouTypeNamesTheChat()
    {
        var store = new ChatStore();
        store.Append(Say("user", "Plan a three day trip to Kochi with a small budget please"));
        store.Append(Say("assistant", "Sure"));
        store.Append(Say("user", "second question must not rename it"));
        Assert.Equal("Plan a three day trip to Kochi with a…", store.ActiveTitle);
    }

    [Theory]
    [InlineData("/startup", "")]
    [InlineData("   \n\n  hello world  \nsecond line", "hello world")]
    [InlineData("tab\u0007bell", "tabbell")]
    public void TitlesComeFromTheFirstLineAndNeverFromACommand(string text, string expected) =>
        Assert.Equal(expected, ChatStore.TitleFrom(text));

    [Fact]
    public void ASlashCommandDoesNotNameTheChat()
    {
        var store = new ChatStore();
        store.Append(Say("user", "/startup"));
        Assert.Equal(Database.DefaultChatTitle, store.ActiveTitle);
        store.Append(Say("user", "what is my IP address"));
        Assert.Equal("what is my IP address", store.ActiveTitle);
    }

    [Fact]
    public void ANewChatKeepsTheOldOneAndStartsEmpty()
    {
        var store = new ChatStore();
        store.Append(Say("user", "first chat message"));
        var first = store.ActiveId;

        var second = store.StartNew();
        Assert.NotEqual(first, second);
        Assert.Empty(store.Entries);
        store.Append(Say("user", "second chat message"));

        Assert.Equal(2, store.Conversations().Count);
        Assert.True(store.Switch(first));
        Assert.Equal("first chat message", store.Entries.Single().Text);
        Assert.True(store.Switch(second));
        Assert.Equal("second chat message", store.Entries.Single().Text);
    }

    [Fact]
    public void StartingNewWhileTheChatIsEmptyDoesNotPileUpEmptyChats()
    {
        var store = new ChatStore();
        var id = store.StartNew();
        Assert.Equal(id, store.StartNew());
        Assert.Single(store.Conversations());
    }

    [Fact]
    public void TheLastUsedChatOpensAfterARestart()
    {
        var store = new ChatStore();
        store.Append(Say("user", "alpha"));
        var alpha = store.ActiveId;
        store.StartNew();
        store.Append(Say("user", "beta"));
        store.Switch(alpha);

        var restarted = new ChatStore();
        Assert.Equal(alpha, restarted.ActiveId);
        Assert.Equal("alpha", restarted.Entries.Single().Text);
    }

    [Fact]
    public void RenameAndDeleteWork()
    {
        var store = new ChatStore();
        store.Append(Say("user", "alpha"));
        var alpha = store.ActiveId;
        var beta = store.StartNew();
        store.Append(Say("user", "beta"));

        Assert.True(store.Rename(alpha, "  Holiday plans\u0007 "));
        Assert.Equal("Holiday plans", store.Conversations().Single(c => c.Id == alpha).Title);
        Assert.False(store.Rename(alpha, "   "));

        store.Delete(beta); // the open one: the most recent other chat opens
        Assert.Equal(alpha, store.ActiveId);
        Assert.Single(store.Conversations());
        Assert.Empty(ChatStore.Search("beta")); // its messages are gone from search too
    }

    [Fact]
    public void DeletingTheOnlyChatLeavesAFreshOne()
    {
        var store = new ChatStore();
        store.Append(Say("user", "secret plans"));
        store.Delete(store.ActiveId);
        Assert.Empty(store.Entries);
        Assert.Single(store.Conversations());
        Assert.Empty(ChatStore.Search("secret"));
    }

    [Fact]
    public void ClearEmptiesOnlyTheOpenChat()
    {
        var store = new ChatStore();
        store.Append(Say("user", "keep me"));
        var keep = store.ActiveId;
        store.StartNew();
        store.Append(Say("user", "remove me"));
        store.Clear();

        Assert.Empty(store.Entries);
        Assert.Equal(Database.DefaultChatTitle, store.ActiveTitle);
        store.Switch(keep);
        Assert.Equal("keep me", store.Entries.Single().Text);
    }

    [Fact]
    public void DeleteAllRemovesEverythingIncludingSearch()
    {
        var store = new ChatStore();
        store.Append(Say("user", "needle one"));
        store.StartNew();
        store.Append(Say("user", "needle two"));
        store.DeleteAll();
        Assert.Empty(ChatStore.Search("needle"));
        Assert.Single(store.Conversations());
    }

    [Fact]
    public void PinningSurvivesARestartAndListsAcrossChats()
    {
        var store = new ChatStore();
        var a = store.Append(Say("assistant", "the wifi password is on the fridge"));
        var firstChat = store.ActiveId;
        store.Append(Say("user", "unpinned"));
        store.StartNew();
        var b = store.Append(Say("assistant", "tax deadline is 31 July"));

        Assert.True(store.TogglePin(a.Id));
        Assert.True(store.TogglePin(b.Id));
        Assert.True(store.Entries.Single(e => e.Id == b.Id).Pinned);

        var pins = new ChatStore().Pins();
        Assert.Equal(["tax deadline is 31 July", "the wifi password is on the fridge"], pins.Select(p => p.Entry.Text));
        Assert.Equal(firstChat, pins[1].ConversationId);

        Assert.False(store.TogglePin(a.Id)); // a second press unpins
        Assert.Single(store.Pins());
        Assert.Equal(1, store.Conversations().Single(c => c.Id == store.ActiveId).Pinned);
    }

    [Fact]
    public void PinningAnUnsavedOrUnknownMessageDoesNothing()
    {
        var store = new ChatStore();
        Assert.False(store.TogglePin(0));
        Assert.False(store.TogglePin(99999));
    }

    [Fact]
    public void OlderMessagesLoadOnlyFromTheOpenChat()
    {
        var store = new ChatStore();
        for (var i = 0; i < 250; i++) store.Append(Say("user", $"first-{i}"));
        var first = store.ActiveId;
        store.StartNew();
        for (var i = 0; i < 5; i++) store.Append(Say("user", $"second-{i}"));

        Assert.False(store.HasOlder);
        store.Switch(first);
        Assert.Equal(200, store.Entries.Count);
        Assert.True(store.HasOlder);
        Assert.All(store.LoadOlder(100), e => Assert.StartsWith("first-", e.Text));
        Assert.Equal(250, store.Entries.Count);
    }

    [Fact]
    public void ExportCoversOnlyTheNamedChat()
    {
        var store = new ChatStore();
        store.Append(Say("user", "alpha topic"));
        var alpha = store.ActiveId;
        store.StartNew();
        store.Append(Say("user", "beta topic"));

        var only = ChatStore.ReadAll(alpha);
        Assert.Equal("alpha topic", only.Single().Text);
        Assert.Equal(2, ChatStore.ReadAll().Count);
    }

    [Fact]
    public void SearchStillFindsMessagesInEveryChat()
    {
        var store = new ChatStore();
        store.Append(Say("user", "invoice for march"));
        store.StartNew();
        store.Append(Say("user", "invoice for april"));
        Assert.Equal(2, ChatStore.Search("invoice").Count);
    }

    [Fact]
    public void AnOldDatabaseWithoutChatsIsUpgradedAndKeepsEveryMessage()
    {
        var dir = Directory.CreateTempSubdirectory("tessa-legacy").FullName;
        var path = Path.Combine(dir, "old.db");
        using (var old = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            old.Open();
            using var create = old.CreateCommand();
            var when = Database.ToText(new DateTime(2026, 1, 2, 10, 0, 0));
            create.CommandText = $"""
                CREATE TABLE messages (id INTEGER PRIMARY KEY AUTOINCREMENT, role TEXT NOT NULL, text TEXT NOT NULL, time TEXT NOT NULL);
                INSERT INTO messages(role, text, time) VALUES ('user', 'my old question about backups', '{when}');
                INSERT INTO messages(role, text, time) VALUES ('assistant', 'my old answer', '{when}');
                """;
            create.ExecuteNonQuery();
        }
        try
        {
            Database.Path = path;
            var store = new ChatStore();
            Assert.Equal(["my old question about backups", "my old answer"], store.Entries.Select(e => e.Text));
            Assert.Equal("my old question about backups", store.ActiveTitle); // named from its first message
            Assert.Single(store.Conversations());

            // And a second start is not confused by the columns already being there.
            Assert.Equal(2, new ChatStore().Entries.Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}

[Collection("wpf")]
public class ChatListViewTests
{
    private static object Res(string key) => key switch
    {
        "Segment" or "Field" or "GhostButton" or "IconButton" => null!,
        "UiFont" => new FontFamily("Segoe UI"),
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

    private static IReadOnlyList<Conversation> Chats() =>
    [
        new(1, "Trip to Kochi", DateTime.Now, 6, 1),
        new(2, "Taxes", DateTime.Now.AddDays(-1), 1, 0)
    ];

    private static PinnedMessage Pin() => new(1, "Trip to Kochi", new ChatEntry("assistant", "Book the train by Friday", DateTime.Now, 7, true));

    [Fact]
    public void EachChatIsARowAndTheOpenOneIsHighlighted()
    {
        var (texts, rows) = OnStaThread(() =>
        {
            var view = new ChatListView(Res);
            view.Show(Chats(), [Pin()], 1);
            return (All<TextBlock>(view.Element).Select(t => t.Text).ToList(), All<Border>(view.Element).Count(b => b.Tag is long));
        });
        Assert.Equal(2, rows);
        Assert.Contains("Trip to Kochi", texts);
        Assert.Contains("6 messages · today " + DateTime.Now.ToString("HH:mm") + " · 1 pinned", texts.Select(t => t.Replace(DateTime.Now.ToString("HH:mm"), DateTime.Now.ToString("HH:mm"))));
        Assert.Contains("1 message · yesterday " + DateTime.Now.AddDays(-1).ToString("HH:mm"), texts);
    }

    [Fact]
    public void ClickingARowOpensThatChat()
    {
        var opened = OnStaThread(() =>
        {
            long? id = null;
            var view = new ChatListView(Res);
            view.OpenChat += c => id = c;
            view.Show(Chats(), [], 1);
            var row = All<Border>(view.Element).Single(b => b.Tag is long t && t == 2);
            row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
            return id;
        });
        Assert.Equal(2, opened);
    }

    [Fact]
    public void DeletingNeedsASecondClick()
    {
        var (afterFirst, afterSecond) = OnStaThread(() =>
        {
            var deleted = new List<long>();
            var view = new ChatListView(Res);
            view.DeleteChat += deleted.Add;
            view.Show(Chats(), [], 1);
            var button = All<Button>(view.Element).First(b => (string?)b.ToolTip == "Delete this chat");
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var first = deleted.Count;
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            return (first, deleted.ToList());
        });
        Assert.Equal(0, afterFirst);
        Assert.Single(afterSecond);
    }

    [Fact]
    public void RenamingUsesATextBoxAndEnterSaves()
    {
        var renamed = OnStaThread(() =>
        {
            (long, string)? result = null;
            var view = new ChatListView(Res);
            view.RenameChat += (id, title) => result = (id, title);
            view.Show(Chats(), [], 1);
            All<Button>(view.Element).First(b => (string?)b.ToolTip == "Rename").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var box = All<TextBox>(view.Element).Single();
            Assert.Equal("Trip to Kochi", box.Text);
            box.Text = "Kerala trip";
            box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new FakeKeySource(), 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            return result;
        });
        Assert.Equal((1L, "Kerala trip"), renamed);
    }

    [Fact]
    public void PinnedTabListsMessagesWithTheirChatAndCanUnpin()
    {
        var (texts, unpinned) = OnStaThread(() =>
        {
            PinnedMessage? gone = null;
            var view = new ChatListView(Res);
            view.Unpin += p => gone = p;
            view.Show(Chats(), [Pin()], 1, showPins: true);
            var lines = All<TextBlock>(view.Element).Select(t => t.Text).ToList();
            All<Button>(view.Element).Single(b => (string?)b.ToolTip == "Unpin").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            return (lines, gone);
        });
        Assert.Contains("Book the train by Friday", texts);
        Assert.Contains("Answer · in Trip to Kochi", texts);
        Assert.Equal(7, unpinned!.Entry.Id);
    }

    [Fact]
    public void AskingForPinsWhenThereAreNoneStaysOnTheChatList()
    {
        var texts = OnStaThread(() =>
        {
            var view = new ChatListView(Res);
            view.Show(Chats(), [], 1, showPins: true);
            return All<TextBlock>(view.Element).Select(t => t.Text).ToList();
        });
        Assert.Contains("Trip to Kochi", texts);
        Assert.Contains(texts, t => t.Contains("Pin a message"));
    }

    [Theory]
    [InlineData(0, "today")]
    [InlineData(1, "yesterday")]
    public void FriendlyTimes(int daysAgo, string prefix)
    {
        var now = new DateTime(2026, 11, 10, 15, 0, 0);
        Assert.StartsWith(prefix, ChatListView.When(now.AddDays(-daysAgo), now));
        Assert.Equal("3 Jan", ChatListView.When(new DateTime(2026, 1, 3), now));
        Assert.Equal("3 Jan 2025", ChatListView.When(new DateTime(2025, 1, 3), now));
    }

    // A key press needs a source; this one has no window behind it.
    private sealed class FakeKeySource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
