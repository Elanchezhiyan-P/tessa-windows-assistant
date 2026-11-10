using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using WinCompanion.Services;
using WinCompanion.UI;
using Xunit;

namespace WinCompanion.UnitTests;

[Collection("database")]
public class MiniTimerTextTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 9, 0, 0);

    private static Reminder R(string text, TimeSpan from) => new(Guid.NewGuid(), text, Now + from, Now);

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(-5, "00:00")]
    [InlineData(59, "00:59")]
    [InlineData(60, "01:00")]
    [InlineData(25 * 60, "25:00")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(3725, "1:02:05")]
    public void FormatsWholeSeconds(int seconds, string expected) =>
        Assert.Equal(expected, MiniTimerText.Format(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void AFractionOfASecondCountsAsAWholeOne() =>
        Assert.Equal("00:02", MiniTimerText.Format(TimeSpan.FromSeconds(1.2)));

    [Fact]
    public void FocusWorkShowsTheTimeLeftAndHowFarAlong()
    {
        var work = (Now.AddMinutes(-5), Now.AddMinutes(20));
        var state = MiniTimerText.Build(Now, work, []);
        Assert.Equal(MiniTimerKind.Focus, state.Kind);
        Assert.Equal("20:00", state.Time);
        Assert.Equal(0.2, state.Progress, 3);
    }

    [Fact]
    public void FocusBeatsAnyReminder()
    {
        var state = MiniTimerText.Build(Now, (Now, Now.AddMinutes(25)), [R("call mom", TimeSpan.FromMinutes(2))]);
        Assert.Equal(MiniTimerKind.Focus, state.Kind);
    }

    [Fact]
    public void BetweenWorkPeriodsItCountsDownTheBreak()
    {
        var state = MiniTimerText.Build(Now, null,
        [
            R(FocusService.Mark + "Break over. Back to focus.", TimeSpan.FromMinutes(3)),
            R("lunch", TimeSpan.FromMinutes(1))
        ]);
        Assert.Equal(MiniTimerKind.Break, state.Kind);
        Assert.Equal("03:00", state.Time);
        Assert.Equal(-1, state.Progress);
    }

    [Fact]
    public void TheNextReminderShowsWhenItIsCloseAndNotWhenItIsFar()
    {
        var near = MiniTimerText.Build(Now, null, [R("later", TimeSpan.FromHours(5)), R("stand up and stretch for a while now", TimeSpan.FromMinutes(12))]);
        Assert.Equal(MiniTimerKind.Reminder, near.Kind);
        Assert.Equal("12:00", near.Time);
        Assert.True(near.Label.Length <= 24 && near.Label.EndsWith('…'));

        Assert.Equal(MiniTimerKind.None, MiniTimerText.Build(Now, null, [R("tomorrow", TimeSpan.FromHours(5))]).Kind);
        Assert.Equal(MiniTimerKind.Reminder, MiniTimerText.Build(Now, null, [R("exactly an hour", TimeSpan.FromHours(1))]).Kind);
    }

    [Fact]
    public void NothingPendingMeansNothingShown()
    {
        Assert.Equal(MiniTimerKind.None, MiniTimerText.Build(Now, null, []).Kind);
        // An overdue reminder is about to fire, so it is not counted down.
        Assert.Equal(MiniTimerKind.None, MiniTimerText.Build(Now, null, [R("late", TimeSpan.FromMinutes(-1))]).Kind);
        // A finished work period is ignored.
        Assert.Equal(MiniTimerKind.None, MiniTimerText.Build(Now, (Now.AddMinutes(-30), Now.AddMinutes(-5)), []).Kind);
    }

    [Fact]
    public void ReminderListsComeInAnyOrder()
    {
        var state = MiniTimerText.Build(Now, null, [R("second", TimeSpan.FromMinutes(30)), R("first", TimeSpan.FromMinutes(10))]);
        Assert.Equal("first", state.Label);
    }

    [Fact]
    public void AFocusSessionKnowsItsCurrentWork()
    {
        var db = Directory.CreateTempSubdirectory("tessa-timer").FullName;
        var old = Database.Path;
        try
        {
            Database.Path = Path.Combine(db, "t.db");
            var focus = new FocusService(new ReminderStore());
            var start = new DateTime(2026, 11, 10, 9, 0, 0);
            focus.Start(25, 5, 2, start);
            Assert.Equal((start, start.AddMinutes(25)), focus.CurrentWork(start.AddMinutes(3)));
            Assert.Null(focus.CurrentWork(start.AddMinutes(27))); // in the break
            Assert.Equal(start.AddMinutes(30), focus.CurrentWork(start.AddMinutes(31))!.Value.Start);
            focus.Stop();
            Assert.Null(focus.CurrentWork(start.AddMinutes(3)));
        }
        finally
        {
            Database.Path = old;
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(db, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void TheReminderVersionChangesWhenRemindersDo()
    {
        var db = Directory.CreateTempSubdirectory("tessa-timer").FullName;
        var old = Database.Path;
        try
        {
            Database.Path = Path.Combine(db, "v.db");
            var store = new ReminderStore();
            var before = ReminderStore.Version;
            store.Add("x", DateTime.Now.AddHours(1));
            var added = ReminderStore.Version;
            store.Clear();
            Assert.True(added > before);
            Assert.True(ReminderStore.Version > added);
        }
        finally
        {
            Database.Path = old;
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(db, true); } catch (IOException) { }
        }
    }
}
