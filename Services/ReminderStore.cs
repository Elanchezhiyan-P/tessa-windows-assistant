using System.Globalization;

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

/// <summary>Pending reminders, saved in the local database so they survive restarts and reboots.</summary>
public sealed class ReminderStore
{
    private readonly object _gate = new();

    public Reminder Add(string message, DateTime due, string? repeat = null)
    {
        var reminder = new Reminder(Guid.NewGuid(), message, due, DateTime.Now, repeat);
        lock (_gate)
        {
            using var db = Database.Open();
            using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO reminders(id, message, due, created, repeat) VALUES ($id, $message, $due, $created, $repeat)";
            command.Parameters.AddWithValue("$id", reminder.Id.ToString());
            command.Parameters.AddWithValue("$message", reminder.Message);
            command.Parameters.AddWithValue("$due", Database.ToText(reminder.Due));
            command.Parameters.AddWithValue("$created", Database.ToText(reminder.Created));
            command.Parameters.AddWithValue("$repeat", (object?)reminder.Repeat ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
        return reminder;
    }

    public IReadOnlyList<Reminder> Pending()
    {
        lock (_gate)
        {
            using var db = Database.Open();
            return ReadAll(db);
        }
    }

    /// <summary>Removes and returns every reminder that is due at or before <paramref name="now"/>.</summary>
    public List<Reminder> TakeDue(DateTime now)
    {
        lock (_gate)
        {
            using var db = Database.Open();
            using var transaction = db.BeginTransaction();
            var due = ReadAll(db).Where(r => r.Due <= now).ToList();
            foreach (var reminder in due)
            {
                using var command = db.CreateCommand();
                command.Transaction = transaction;
                // A repeating reminder isn't consumed: it moves on to its next occurrence.
                if (reminder.Repeat is not null)
                {
                    command.CommandText = "UPDATE reminders SET due = $due WHERE id = $id";
                    command.Parameters.AddWithValue("$due", Database.ToText(reminder.NextAfter(now)));
                }
                else
                {
                    command.CommandText = "DELETE FROM reminders WHERE id = $id";
                }
                command.Parameters.AddWithValue("$id", reminder.Id.ToString());
                command.ExecuteNonQuery();
            }
            transaction.Commit();
            return due;
        }
    }

    /// <summary>Cancel by its 1-based position in <see cref="Pending"/>.</summary>
    public Reminder? CancelAt(int number)
    {
        lock (_gate)
        {
            using var db = Database.Open();
            var ordered = ReadAll(db);
            if (number < 1 || number > ordered.Count) return null;
            var target = ordered[number - 1];
            using var command = db.CreateCommand();
            command.CommandText = "DELETE FROM reminders WHERE id = $id";
            command.Parameters.AddWithValue("$id", target.Id.ToString());
            command.ExecuteNonQuery();
            return target;
        }
    }

    public int Clear()
    {
        lock (_gate)
        {
            using var db = Database.Open();
            using var command = db.CreateCommand();
            command.CommandText = "DELETE FROM reminders";
            return command.ExecuteNonQuery();
        }
    }

    private static List<Reminder> ReadAll(Microsoft.Data.Sqlite.SqliteConnection db)
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT id, message, due, created, repeat FROM reminders ORDER BY due, created";
        var list = new List<Reminder>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            list.Add(new Reminder(Guid.Parse(reader.GetString(0)), reader.GetString(1), Database.FromText(reader.GetString(2)),
                Database.FromText(reader.GetString(3)), reader.IsDBNull(4) ? null : reader.GetString(4)));
        return list;
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
}
