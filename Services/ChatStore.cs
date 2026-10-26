using Microsoft.Data.Sqlite;

namespace WinCompanion.Services;

/// <param name="Role">user, assistant, tool or error.</param>
public sealed record ChatEntry(string Role, string Text, DateTime Time);

/// <summary>
/// The chat transcript, kept in the local database so it survives restarts. Only the newest page is loaded into memory;
/// older messages are fetched on request and can be searched.
/// </summary>
public sealed class ChatStore
{
    private const int PageSize = 200;

    private long _oldestLoadedId = long.MaxValue;

    /// <summary>The messages currently shown, oldest first.</summary>
    public List<ChatEntry> Entries { get; } = new();

    public ChatStore()
    {
        try
        {
            var (entries, oldest) = ReadBefore(long.MaxValue, PageSize);
            Entries.AddRange(entries);
            if (entries.Count > 0) _oldestLoadedId = oldest;
        }
        catch (SqliteException) { /* start with an empty transcript */ }
    }

    /// <summary>True when there are saved messages older than the ones loaded.</summary>
    public bool HasOlder
    {
        get
        {
            try
            {
                using var db = Database.Open();
                using var command = db.CreateCommand();
                command.CommandText = "SELECT EXISTS(SELECT 1 FROM messages WHERE id < $before)";
                command.Parameters.AddWithValue("$before", _oldestLoadedId);
                return Convert.ToInt64(command.ExecuteScalar()) == 1;
            }
            catch (SqliteException) { return false; }
        }
    }

    public void Append(ChatEntry entry)
    {
        Entries.Add(entry);
        try
        {
            using var db = Database.Open();
            using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO messages(role, text, time) VALUES ($role, $text, $time)";
            command.Parameters.AddWithValue("$role", entry.Role);
            command.Parameters.AddWithValue("$text", entry.Text);
            command.Parameters.AddWithValue("$time", Database.ToText(entry.Time));
            command.ExecuteNonQuery();
            if (_oldestLoadedId == long.MaxValue) // the first message of an empty transcript
            {
                command.CommandText = "SELECT last_insert_rowid()";
                _oldestLoadedId = Convert.ToInt64(command.ExecuteScalar());
            }
        }
        catch (SqliteException) { /* history is a convenience */ }
    }

    /// <summary>Loads up to <paramref name="count"/> messages older than those shown, and returns them oldest first.</summary>
    public IReadOnlyList<ChatEntry> LoadOlder(int count = 100)
    {
        try
        {
            var (entries, oldest) = ReadBefore(_oldestLoadedId, count);
            if (entries.Count == 0) return entries;
            Entries.InsertRange(0, entries);
            _oldestLoadedId = oldest;
            return entries;
        }
        catch (SqliteException) { return []; }
    }

    public void Clear()
    {
        Entries.Clear();
        _oldestLoadedId = long.MaxValue;
        try
        {
            using var db = Database.Open();
            Database.Execute(db, "DELETE FROM messages;");
        }
        catch (SqliteException) { }
    }

    /// <summary>Finds saved messages containing all the words in <paramref name="query"/>, newest first.</summary>
    public static IReadOnlyList<ChatEntry> Search(string query, int limit = 10)
    {
        var match = ToMatchQuery(query);
        if (match.Length == 0) return [];
        try
        {
            using var db = Database.Open();
            using var command = db.CreateCommand();
            command.CommandText = """
                SELECT m.role, m.text, m.time FROM messages_fts
                JOIN messages m ON m.id = messages_fts.rowid
                WHERE messages_fts MATCH $match AND m.role IN ('user', 'assistant')
                ORDER BY m.id DESC LIMIT $limit
                """;
            command.Parameters.AddWithValue("$match", match);
            command.Parameters.AddWithValue("$limit", limit);
            return ReadEntries(command, out _);
        }
        catch (SqliteException) { return []; }
    }

    /// <summary>Every saved question and answer, oldest first (tool lines are left out).</summary>
    public static IReadOnlyList<ChatEntry> ReadAll()
    {
        try
        {
            using var db = Database.Open();
            using var command = db.CreateCommand();
            command.CommandText = "SELECT role, text, time FROM messages WHERE role IN ('user', 'assistant') ORDER BY id";
            return ReadEntries(command, out _);
        }
        catch (SqliteException) { return []; }
    }

    /// <summary>Turns free text into a safe FTS5 query: every word quoted, all of them required.</summary>
    internal static string ToMatchQuery(string text)
    {
        var words = text.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()))
            .Where(w => w.Length > 0);
        return string.Join(' ', words.Select(w => $"\"{w}\""));
    }

    private static (List<ChatEntry> Entries, long OldestId) ReadBefore(long beforeId, int count)
    {
        using var db = Database.Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            SELECT role, text, time, id FROM (
                SELECT role, text, time, id FROM messages WHERE id < $before ORDER BY id DESC LIMIT $count
            ) ORDER BY id
            """;
        command.Parameters.AddWithValue("$before", beforeId);
        command.Parameters.AddWithValue("$count", count);
        var entries = ReadEntries(command, out var oldest);
        return (entries, oldest);
    }

    private static List<ChatEntry> ReadEntries(SqliteCommand command, out long oldestId)
    {
        var entries = new List<ChatEntry>();
        oldestId = long.MaxValue;
        using var reader = command.ExecuteReader();
        var hasId = reader.FieldCount > 3;
        while (reader.Read())
        {
            entries.Add(new ChatEntry(reader.GetString(0), reader.GetString(1), Database.FromText(reader.GetString(2))));
            if (hasId) oldestId = Math.Min(oldestId, reader.GetInt64(3));
        }
        return entries;
    }
}
