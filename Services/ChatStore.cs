using Microsoft.Data.Sqlite;

namespace WinCompanion.Services;

/// <param name="Role">user, assistant, tool or error.</param>
/// <param name="Id">The saved message's number; 0 for one that is not saved (yet).</param>
public sealed record ChatEntry(string Role, string Text, DateTime Time, long Id = 0, bool Pinned = false);

/// <summary>One saved chat.</summary>
public sealed record Conversation(long Id, string Title, DateTime Updated, int Messages, int Pinned);

/// <summary>A pinned message and the chat it is in.</summary>
public sealed record PinnedMessage(long ConversationId, string ConversationTitle, ChatEntry Entry);

/// <summary>
/// The chats, kept in the local database so they survive restarts. One chat is open at a time; only its newest page is loaded
/// into memory, and older messages are fetched on request. Any message can be pinned so it is easy to come back to.
/// </summary>
public sealed class ChatStore
{
    private const int PageSize = 200;
    private const int MaxTitleLength = 40;

    private long _oldestLoadedId = long.MaxValue;

    /// <summary>The messages of the open chat shown now, oldest first.</summary>
    public List<ChatEntry> Entries { get; } = new();

    /// <summary>The open chat.</summary>
    public long ActiveId { get; private set; } = 1;

    public ChatStore()
    {
        try
        {
            ActiveId = MostRecentId() ?? CreateRow();
            TitleFromFirstMessageIfUnnamed(ActiveId);
            LoadActive();
        }
        catch (SqliteException) { /* start with an empty transcript */ }
    }

    public string ActiveTitle => Conversations().FirstOrDefault(c => c.Id == ActiveId)?.Title ?? Database.DefaultChatTitle;

    // ---- Chats --------------------------------------------------------------------------------------

    /// <summary>Every chat, the one used most recently first.</summary>
    public IReadOnlyList<Conversation> Conversations()
    {
        try
        {
            using var db = Database.Open();
            using var command = db.CreateCommand();
            command.CommandText = """
                SELECT c.id, c.title, c.updated,
                       (SELECT COUNT(*) FROM messages m WHERE m.conversation = c.id AND m.role IN ('user', 'assistant')),
                       (SELECT COUNT(*) FROM messages m WHERE m.conversation = c.id AND m.pinned = 1)
                FROM conversations c ORDER BY c.updated DESC, c.id DESC
                """;
            var list = new List<Conversation>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                list.Add(new Conversation(reader.GetInt64(0), reader.GetString(1), Database.FromText(reader.GetString(2)), reader.GetInt32(3), reader.GetInt32(4)));
            return list;
        }
        catch (SqliteException) { return []; }
    }

    /// <summary>Opens a fresh chat. An open chat that has nothing in it yet is reused instead of piling up empty ones.</summary>
    public long StartNew()
    {
        try
        {
            if (Conversations().FirstOrDefault(c => c.Id == ActiveId) is { Messages: 0 } && Entries.All(e => e.Role is not ("user" or "assistant")))
                return ActiveId;
            ActiveId = CreateRow();
            Entries.Clear();
            _oldestLoadedId = long.MaxValue;
        }
        catch (SqliteException) { }
        return ActiveId;
    }

    /// <summary>Opens another chat. Returns false when there is no such chat.</summary>
    public bool Switch(long id)
    {
        try
        {
            if (Conversations().All(c => c.Id != id)) return false;
            ActiveId = id;
            Touch(id);
            LoadActive();
            return true;
        }
        catch (SqliteException) { return false; }
    }

    public bool Rename(long id, string title)
    {
        title = CleanTitle(title);
        if (title.Length == 0) return false;
        try
        {
            using var db = Database.Open();
            using var command = db.CreateCommand();
            command.CommandText = "UPDATE conversations SET title = $title WHERE id = $id";
            command.Parameters.AddWithValue("$title", title);
            command.Parameters.AddWithValue("$id", id);
            return command.ExecuteNonQuery() > 0;
        }
        catch (SqliteException) { return false; }
    }

    /// <summary>Deletes a chat and its messages. When it was the open one, the most recent other chat (or a new one) opens.</summary>
    public void Delete(long id)
    {
        try
        {
            using (var db = Database.Open())
            {
                Delete(db, "DELETE FROM messages WHERE conversation = $id", id);
                Delete(db, "DELETE FROM conversations WHERE id = $id", id);
            }
            if (id != ActiveId) return;
            ActiveId = MostRecentId() ?? CreateRow();
            LoadActive();
        }
        catch (SqliteException) { }
    }

    /// <summary>Deletes every chat.</summary>
    public void DeleteAll()
    {
        try
        {
            using (var db = Database.Open())
            {
                Database.Execute(db, "DELETE FROM messages; DELETE FROM conversations;");
            }
            ActiveId = CreateRow();
            Entries.Clear();
            _oldestLoadedId = long.MaxValue;
        }
        catch (SqliteException) { }
    }

    /// <summary>Empties the open chat but keeps it (and its name goes back to the default).</summary>
    public void Clear()
    {
        Entries.Clear();
        _oldestLoadedId = long.MaxValue;
        try
        {
            using var db = Database.Open();
            Delete(db, "DELETE FROM messages WHERE conversation = $id", ActiveId);
            using var command = db.CreateCommand();
            command.CommandText = "UPDATE conversations SET title = $title WHERE id = $id";
            command.Parameters.AddWithValue("$title", Database.DefaultChatTitle);
            command.Parameters.AddWithValue("$id", ActiveId);
            command.ExecuteNonQuery();
        }
        catch (SqliteException) { }
    }

    // ---- Messages -----------------------------------------------------------------------------------

    /// <summary>True when the open chat has saved messages older than the ones loaded.</summary>
    public bool HasOlder
    {
        get
        {
            try
            {
                using var db = Database.Open();
                using var command = db.CreateCommand();
                command.CommandText = "SELECT EXISTS(SELECT 1 FROM messages WHERE conversation = $c AND id < $before)";
                command.Parameters.AddWithValue("$c", ActiveId);
                command.Parameters.AddWithValue("$before", _oldestLoadedId);
                return Convert.ToInt64(command.ExecuteScalar()) == 1;
            }
            catch (SqliteException) { return false; }
        }
    }

    /// <summary>Saves a message in the open chat and returns it with its number. The first thing you type names an unnamed chat.</summary>
    public ChatEntry Append(ChatEntry entry)
    {
        try
        {
            using var db = Database.Open();
            using var command = db.CreateCommand();
            command.CommandText = "INSERT INTO messages(role, text, time, conversation) VALUES ($role, $text, $time, $c); SELECT last_insert_rowid()";
            command.Parameters.AddWithValue("$role", entry.Role);
            command.Parameters.AddWithValue("$text", entry.Text);
            command.Parameters.AddWithValue("$time", Database.ToText(entry.Time));
            command.Parameters.AddWithValue("$c", ActiveId);
            var id = Convert.ToInt64(command.ExecuteScalar());
            entry = entry with { Id = id };
            if (_oldestLoadedId == long.MaxValue) _oldestLoadedId = id; // the first message of an empty transcript

            Touch(db, ActiveId);
            if (entry.Role == "user") NameFrom(db, ActiveId, entry.Text);
        }
        catch (SqliteException) { /* history is a convenience */ }
        Entries.Add(entry);
        return entry;
    }

    /// <summary>Loads up to <paramref name="count"/> messages older than those shown, and returns them oldest first.</summary>
    public IReadOnlyList<ChatEntry> LoadOlder(int count = 100)
    {
        try
        {
            var (entries, oldest) = ReadBefore(ActiveId, _oldestLoadedId, count);
            if (entries.Count == 0) return entries;
            Entries.InsertRange(0, entries);
            _oldestLoadedId = oldest;
            return entries;
        }
        catch (SqliteException) { return []; }
    }

    // ---- Pins ---------------------------------------------------------------------------------------

    /// <summary>Pins or unpins a saved message. Returns the new state (false when it was not found).</summary>
    public bool TogglePin(long messageId)
    {
        if (messageId <= 0) return false;
        try
        {
            using var db = Database.Open();
            using var command = db.CreateCommand();
            command.CommandText = "UPDATE messages SET pinned = 1 - pinned WHERE id = $id; SELECT pinned FROM messages WHERE id = $id";
            command.Parameters.AddWithValue("$id", messageId);
            var pinned = command.ExecuteScalar() is { } value && Convert.ToInt64(value) == 1;
            var index = Entries.FindIndex(e => e.Id == messageId);
            if (index >= 0) Entries[index] = Entries[index] with { Pinned = pinned };
            return pinned;
        }
        catch (SqliteException) { return false; }
    }

    /// <summary>Every pinned message in every chat, newest first.</summary>
    public IReadOnlyList<PinnedMessage> Pins()
    {
        try
        {
            using var db = Database.Open();
            using var command = db.CreateCommand();
            command.CommandText = """
                SELECT m.role, m.text, m.time, m.id, m.pinned, c.id, c.title
                FROM messages m JOIN conversations c ON c.id = m.conversation
                WHERE m.pinned = 1 ORDER BY m.id DESC
                """;
            var list = new List<PinnedMessage>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                list.Add(new PinnedMessage(reader.GetInt64(5), reader.GetString(6),
                    new ChatEntry(reader.GetString(0), reader.GetString(1), Database.FromText(reader.GetString(2)), reader.GetInt64(3), true)));
            return list;
        }
        catch (SqliteException) { return []; }
    }

    // ---- Search and export --------------------------------------------------------------------------

    /// <summary>Finds saved messages (in every chat) containing all the words in <paramref name="query"/>, newest first.</summary>
    public static IReadOnlyList<ChatEntry> Search(string query, int limit = 10)
    {
        var match = ToMatchQuery(query);
        if (match.Length == 0) return [];
        try
        {
            using var db = Database.Open();
            using var command = db.CreateCommand();
            command.CommandText = """
                SELECT m.role, m.text, m.time, m.id, m.pinned FROM messages_fts
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

    /// <summary>Every saved question and answer, oldest first (tool lines are left out): of one chat, or of all when none is named.</summary>
    public static IReadOnlyList<ChatEntry> ReadAll(long? conversation = null)
    {
        try
        {
            using var db = Database.Open();
            using var command = db.CreateCommand();
            command.CommandText = "SELECT role, text, time, id, pinned FROM messages WHERE role IN ('user', 'assistant')" +
                                  (conversation is null ? "" : " AND conversation = $c") + " ORDER BY id";
            if (conversation is not null) command.Parameters.AddWithValue("$c", conversation.Value);
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

    // ---- Names --------------------------------------------------------------------------------------

    /// <summary>A short name for a chat from what was typed first: one line, no commands, cut at a word.</summary>
    public static string TitleFrom(string text)
    {
        var line = text.ReplaceLineEndings("\n").Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        if (line.StartsWith('/')) return ""; // a slash command says nothing about the topic
        return CleanTitle(line);
    }

    private static string CleanTitle(string text)
    {
        var clean = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (clean.Length <= MaxTitleLength) return clean;
        var cut = clean[..MaxTitleLength];
        var space = cut.LastIndexOf(' ');
        return (space > 15 ? cut[..space] : cut).TrimEnd(' ', ',', '.', ';', ':') + "…";
    }

    // ---- Internals ----------------------------------------------------------------------------------

    private void LoadActive()
    {
        Entries.Clear();
        _oldestLoadedId = long.MaxValue;
        var (entries, oldest) = ReadBefore(ActiveId, long.MaxValue, PageSize);
        Entries.AddRange(entries);
        if (entries.Count > 0) _oldestLoadedId = oldest;
    }

    private static long? MostRecentId()
    {
        using var db = Database.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT id FROM conversations ORDER BY updated DESC, id DESC LIMIT 1";
        return command.ExecuteScalar() is { } value ? Convert.ToInt64(value) : null;
    }

    private static long CreateRow()
    {
        using var db = Database.Open();
        using var command = db.CreateCommand();
        command.CommandText = "INSERT INTO conversations(title, created, updated) VALUES ($title, $now, $now); SELECT last_insert_rowid()";
        command.Parameters.AddWithValue("$title", Database.DefaultChatTitle);
        command.Parameters.AddWithValue("$now", Database.ToText(DateTime.Now));
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void Touch(long id)
    {
        using var db = Database.Open();
        Touch(db, id);
    }

    private static void Touch(SqliteConnection db, long id)
    {
        using var command = db.CreateCommand();
        command.CommandText = "UPDATE conversations SET updated = $now WHERE id = $id";
        command.Parameters.AddWithValue("$now", Database.ToText(DateTime.Now));
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static void NameFrom(SqliteConnection db, long id, string firstText)
    {
        var title = TitleFrom(firstText);
        if (title.Length == 0) return;
        using var command = db.CreateCommand();
        command.CommandText = "UPDATE conversations SET title = $title WHERE id = $id AND title = $default";
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$default", Database.DefaultChatTitle);
        command.ExecuteNonQuery();
    }

    // Chats that existed before names did take their name from the first thing typed in them.
    private static void TitleFromFirstMessageIfUnnamed(long id)
    {
        using var db = Database.Open();
        using var command = db.CreateCommand();
        command.CommandText = "SELECT text FROM messages WHERE conversation = $id AND role = 'user' ORDER BY id LIMIT 1";
        command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteScalar() is string text) NameFrom(db, id, text);
    }

    private static void Delete(SqliteConnection db, string sql, long id)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static (List<ChatEntry> Entries, long OldestId) ReadBefore(long conversation, long beforeId, int count)
    {
        using var db = Database.Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            SELECT role, text, time, id, pinned FROM (
                SELECT role, text, time, id, pinned FROM messages WHERE conversation = $c AND id < $before ORDER BY id DESC LIMIT $count
            ) ORDER BY id
            """;
        command.Parameters.AddWithValue("$c", conversation);
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
        while (reader.Read())
        {
            var id = reader.GetInt64(3);
            entries.Add(new ChatEntry(reader.GetString(0), reader.GetString(1), Database.FromText(reader.GetString(2)), id, reader.GetInt64(4) == 1));
            oldestId = Math.Min(oldestId, id);
        }
        return entries;
    }
}
