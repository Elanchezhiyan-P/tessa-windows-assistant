using System.Windows.Threading;

namespace WinCompanion.Services;

/// <summary>
/// Watches the clock and raises <see cref="Due"/> when a reminder's time arrives. It compares against the wall
/// clock every second, so reminders still fire correctly after sleep, and ones that came due while the app
/// was closed fire at startup (marked as missed).
/// </summary>
public sealed class ReminderService
{
    private static readonly TimeSpan MissedAfter = TimeSpan.FromMinutes(2);

    private readonly ReminderStore _store;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>The reminder, and whether it is late (it came due while the app wasn't running or the PC slept).</summary>
    public event Action<Reminder, bool>? Due;

    public ReminderService(ReminderStore store)
    {
        _store = store;
        _timer.Tick += (_, _) => Check();
    }

    public void Start()
    {
        Check();
        _timer.Start();
    }

    private void Check()
    {
        var now = DateTime.Now;
        foreach (var reminder in _store.TakeDue(now))
            Due?.Invoke(reminder, now - reminder.Due > MissedAfter);
    }
}
