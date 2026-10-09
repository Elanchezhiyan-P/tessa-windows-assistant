using System.Globalization;
using System.Text.Json;

namespace WinCompanion.Services;

/// <param name="Repeat">null for a one-off; "daily", "weekdays", "weekly", or "every:N" (N minutes).</param>
public sealed record Reminder(Guid Id, string Message, DateTime Due, DateTime Created, string? Repeat = null)
{
    /// <summary>The first occurrence strictly after <paramref name="now"/> (skips any that were missed).</summary>
    public DateTime NextAfter(DateTime now)
    {
        var next = Due;
        for (var i = 0; i < 100000 && next <= now; i++)
        {
            next = Repeat switch
            {
                "weekly" => next.AddDays(7),
                "weekdays" => NextWeekday(next),
                var r when r is not null && r.StartsWith("every:") => next.AddMinutes(Math.Max(1, int.Parse(r[6..]))),
                _ => next.AddDays(1)
            };
        }
        return next;
    }

    private static DateTime NextWeekday(DateTime from)
    {
        var next = from.AddDays(1);
        while (next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) next = next.AddDays(1);
        return next;
    }

    public string? RepeatText => Repeat switch
    {
        null => null,
        "daily" => "every day",
        "weekdays" => "every weekday",
        "weekly" => "every week",
        var r when r.StartsWith("every:") && int.TryParse(r[6..], out var m) =>
            m % 60 == 0 ? (m == 60 ? "every hour" : $"every {m / 60} hours") : $"every {m} minutes",
        _ => null
    };
}

/// <summary>Pending reminders, saved to disk so they survive restarts and reboots.</summary>
public sealed class ReminderStore
{
    private readonly object _gate = new();
    private readonly List<Reminder> _items;

    private static string FilePath => AppPaths.FileIn("reminders.json");

    public ReminderStore() => _items = Load();

    public Reminder Add(string message, DateTime due, string? repeat = null)
    {
        var reminder = new Reminder(Guid.NewGuid(), message, due, DateTime.Now, repeat);
        lock (_gate)
        {
            _items.Add(reminder);
            Save();
        }
        return reminder;
    }

    public IReadOnlyList<Reminder> Pending()
    {
        lock (_gate) return _items.OrderBy(r => r.Due).ToList();
    }

    /// <summary>Removes and returns every reminder that is due at or before <paramref name="now"/>.</summary>
    public List<Reminder> TakeDue(DateTime now)
    {
        lock (_gate)
        {
            var due = _items.Where(r => r.Due <= now).OrderBy(r => r.Due).ToList();
            if (due.Count == 0) return due;
            // A repeating reminder isn't consumed: it moves on to its next occurrence.
            for (var i = 0; i < _items.Count; i++)
                if (_items[i].Due <= now && _items[i].Repeat is not null)
                    _items[i] = _items[i] with { Due = _items[i].NextAfter(now) };
            _items.RemoveAll(r => r.Due <= now);
            Save();
            return due;
        }
    }

    /// <summary>Cancel by its 1-based position in <see cref="Pending"/>.</summary>
    public Reminder? CancelAt(int number)
    {
        lock (_gate)
        {
            var ordered = _items.OrderBy(r => r.Due).ToList();
            if (number < 1 || number > ordered.Count) return null;
            var target = ordered[number - 1];
            _items.Remove(target);
            Save();
            return target;
        }
    }

    public int Clear()
    {
        lock (_gate)
        {
            var count = _items.Count;
            _items.Clear();
            Save();
            return count;
        }
    }

    public string Describe()
    {
        var pending = Pending();
        if (pending.Count == 0) return "You have no reminders set.";
        var now = DateTime.Now;
        return "Your reminders:\n" + string.Join('\n', pending.Select((r, i) => $"{i + 1}. {FormatWhen(r.Due, now)}: {r.Message}{(r.RepeatText is { } t ? $" (repeats {t})" : "")}"));
    }

    /// <summary>"today at 5:00 PM", "tomorrow at 9:00 AM", "Friday at 2:30 PM", "12 Oct at 8:00 AM", or "in 10 min (5:10 PM)".</summary>
    public static string FormatWhen(DateTime due, DateTime now)
    {
        var clock = due.ToString("h:mm tt", CultureInfo.InvariantCulture);
        var days = (due.Date - now.Date).Days;
        var minutes = (due - now).TotalMinutes;

        if (days == 0 && minutes < 60)
        {
            var rounded = (int)Math.Round(minutes);
            return rounded < 1 ? "in under a minute" : $"in {rounded} min ({clock})"; // "in 1 minute" is said a moment before it is exactly 60s
        }
        return days switch
        {
            0 => $"today at {clock}",
            1 => $"tomorrow at {clock}",
            > 1 and < 7 => $"{due.ToString("dddd", CultureInfo.InvariantCulture)} at {clock}",
            _ => $"{due.ToString("d MMM", CultureInfo.InvariantCulture)} at {clock}"
        };
    }

    private static List<Reminder> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<List<Reminder>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException) { }
        return new();
    }

    private void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(_items)); }
        catch (IOException) { }
    }
}
