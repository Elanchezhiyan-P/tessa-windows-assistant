using WinCompanion.Services;
using Xunit;

namespace WinCompanion.UnitTests;

public class ReminderParserTests
{
    private static readonly DateTime Now = new(2026, 10, 26, 10, 0, 0); // a Monday

    [Fact]
    public void InTenMinutes()
    {
        var outcome = ReminderParser.Parse("remind me in 10 minutes to stretch", Now);
        Assert.NotNull(outcome?.Reminder);
        Assert.Equal(Now.AddMinutes(10), outcome!.Reminder!.Due);
        Assert.Contains("stretch", outcome.Reminder.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TimerFor25Minutes()
    {
        var timer = ReminderParser.TryTimer("set a timer for 25 minutes", Now);
        Assert.NotNull(timer);
        Assert.Equal(Now.AddMinutes(25), timer!.Due);
    }

    [Fact]
    public void UnrelatedTextIsNotAReminder() =>
        Assert.Null(ReminderParser.Parse("what is the weather like", Now)?.Reminder);
}
