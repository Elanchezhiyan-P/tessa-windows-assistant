using Microsoft.Data.Sqlite;

namespace WinCompanion.Services;

public sealed record ClipboardItem(string Text, DateTime Time);

/// <summary>The last 100 pieces of text you copied, kept only in the local database and only when you turn it on in Settings.</summary>
public static class ClipboardStore
{
    public const int MaxItems = 100;
    public const int MaxLength = 5000;

    /// <summary>Saves a copied text. Skips empty text, over-long text and a repeat of the latest item. Returns whether it was saved.</summary>
    public static bool Add(string text, DateTime? now = null)
    {
        var clean = text.Trim();
        if (clean.Length == 0 || clean.Length > MaxLength) return false;
        try
        {
            using var db = Database.Open();
            using (var latest = db.CreateCommand())
            {
                latest.CommandText = "SELECT text FROM clipboard_history ORDER BY id DESC LIMIT 1";
                if (latest.ExecuteScalar() is string last && last == clean) return false;
            }

            using var insert = db.CreateCommand();
            insert.CommandText = "INSERT INTO clipboard_history(text, time) VALUES ($text, $time)";
            insert.Parameters.AddWithValue("$text", clean);
            insert.Parameters.AddWithValue("$time", Database.ToText(now ?? DateTime.Now));
            insert.ExecuteNonQuery();

            using var trim = db.CreateCommand();
            trim.CommandText = "DELETE FROM clipboard_history WHERE id NOT IN (SELECT id FROM clipboard_history ORDER BY id DESC LIMIT $max)";
            trim.Parameters.AddWithValue("$max", MaxItems);
            trim.ExecuteNonQuery();
            return true;
        }
        catch (SqliteException) { return false; }
    }

    /// <summary>Newest first. <paramref name="query"/> filters by words; <paramref name="since"/> by time.</summary>
    public static IReadOnlyList<ClipboardItem> Find(string? query, DateTime? since, int limit = 10)
    {
        var match = string.IsNullOrWhiteSpace(query) ? "" : ChatStore.ToMatchQuery(query);
        try
        {
            using var db = Database.Open();
            using var command = db.CreateCommand();
            var filters = new List<string>();
            var join = "";
            if (match.Length > 0)
            {
                join = "JOIN clipboard_fts ON clipboard_fts.rowid = c.id";
                filters.Add("clipboard_fts MATCH $match");
                command.Parameters.AddWithValue("$match", match);
            }
            if (since is { } from)
            {
                filters.Add("c.time >= $since");
                command.Parameters.AddWithValue("$since", Database.ToText(from));
            }
            command.CommandText = $"SELECT c.text, c.time FROM clipboard_history c {join} " +
                                  (filters.Count > 0 ? "WHERE " + string.Join(" AND ", filters) : "") +
                                  " ORDER BY c.id DESC LIMIT $limit";
            command.Parameters.AddWithValue("$limit", limit);

            var list = new List<ClipboardItem>();
            using var reader = command.ExecuteReader();
            while (reader.Read()) list.Add(new ClipboardItem(reader.GetString(0), Database.FromText(reader.GetString(1))));
            return list;
        }
        catch (SqliteException) { return []; }
    }

    public static int Clear()
    {
        try
        {
            using var db = Database.Open();
            using var command = db.CreateCommand();
            command.CommandText = "DELETE FROM clipboard_history";
            return command.ExecuteNonQuery();
        }
        catch (SqliteException) { return 0; }
    }
}
