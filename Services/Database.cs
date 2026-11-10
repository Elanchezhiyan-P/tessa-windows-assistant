using System.Globalization;
using Microsoft.Data.Sqlite;

namespace WinCompanion.Services;

/// <summary>
/// The local SQLite database (wincompanion.db in the data folder) that holds the chat transcript, remembered facts and reminders.
/// Everything stays on this PC. SQLite makes each change atomic, so a crash can never leave a half-written file.
/// </summary>
internal static class Database
{
    private static readonly object Gate = new();
    private static string? _initialised;

    /// <summary>Where the database lives. Tests point this at a temporary file.</summary>
    public static string Path { get; set; } = AppPaths.FileIn("wincompanion.db");

    /// <summary>Opens a connection, creating the database and its tables the first time.</summary>
    public static SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        connection.Open();
        Execute(connection, "PRAGMA busy_timeout = 5000;");

        lock (Gate)
        {
            if (_initialised != Path)
            {
                CreateSchema(connection);
                _initialised = Path;
            }
        }
        return connection;
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        Execute(connection, "PRAGMA journal_mode = WAL;");
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS messages (
                id   INTEGER PRIMARY KEY AUTOINCREMENT,
                role TEXT NOT NULL,
                text TEXT NOT NULL,
                time TEXT NOT NULL
            );
            CREATE VIRTUAL TABLE IF NOT EXISTS messages_fts USING fts5(text, content='messages', content_rowid='id');
            CREATE TRIGGER IF NOT EXISTS messages_ai AFTER INSERT ON messages BEGIN
                INSERT INTO messages_fts(rowid, text) VALUES (new.id, new.text);
            END;
            CREATE TRIGGER IF NOT EXISTS messages_ad AFTER DELETE ON messages BEGIN
                INSERT INTO messages_fts(messages_fts, rowid, text) VALUES ('delete', old.id, old.text);
            END;

            CREATE TABLE IF NOT EXISTS memories (
                id    INTEGER PRIMARY KEY AUTOINCREMENT,
                text  TEXT NOT NULL,
                added TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS clipboard_history (
                id   INTEGER PRIMARY KEY AUTOINCREMENT,
                text TEXT NOT NULL,
                time TEXT NOT NULL
            );
            CREATE VIRTUAL TABLE IF NOT EXISTS clipboard_fts USING fts5(text, content='clipboard_history', content_rowid='id');
            CREATE TRIGGER IF NOT EXISTS clipboard_ai AFTER INSERT ON clipboard_history BEGIN
                INSERT INTO clipboard_fts(rowid, text) VALUES (new.id, new.text);
            END;
            CREATE TRIGGER IF NOT EXISTS clipboard_ad AFTER DELETE ON clipboard_history BEGIN
                INSERT INTO clipboard_fts(clipboard_fts, rowid, text) VALUES ('delete', old.id, old.text);
            END;

            CREATE TABLE IF NOT EXISTS routines (
                name    TEXT PRIMARY KEY COLLATE NOCASE,
                phrase  TEXT,
                steps   TEXT NOT NULL,
                created TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS reminders (
                id      TEXT PRIMARY KEY,
                message TEXT NOT NULL,
                due     TEXT NOT NULL,
                created TEXT NOT NULL,
                repeat  TEXT
            );

            CREATE TABLE IF NOT EXISTS index_folders (
                path  TEXT PRIMARY KEY COLLATE NOCASE,
                added TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS index_files (
                id       INTEGER PRIMARY KEY AUTOINCREMENT,
                path     TEXT NOT NULL UNIQUE COLLATE NOCASE,
                folder   TEXT NOT NULL COLLATE NOCASE,
                size     INTEGER NOT NULL,
                modified TEXT NOT NULL,
                indexed  TEXT NOT NULL,
                chunks   INTEGER NOT NULL,
                note     TEXT
            );
            CREATE INDEX IF NOT EXISTS index_files_folder ON index_files(folder);
            CREATE TABLE IF NOT EXISTS index_chunks (
                id      INTEGER PRIMARY KEY AUTOINCREMENT,
                file_id INTEGER NOT NULL,
                ordinal INTEGER NOT NULL,
                text    TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS index_chunks_file ON index_chunks(file_id);
            CREATE TABLE IF NOT EXISTS agent_plans (
                name    TEXT PRIMARY KEY COLLATE NOCASE,
                goal    TEXT NOT NULL,
                steps   TEXT NOT NULL,
                created TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS conversations (
                id      INTEGER PRIMARY KEY AUTOINCREMENT,
                title   TEXT NOT NULL,
                created TEXT NOT NULL,
                updated TEXT NOT NULL
            );
            CREATE VIRTUAL TABLE IF NOT EXISTS index_fts USING fts5(text, content='index_chunks', content_rowid='id');
            CREATE TRIGGER IF NOT EXISTS index_chunks_ai AFTER INSERT ON index_chunks BEGIN
                INSERT INTO index_fts(rowid, text) VALUES (new.id, new.text);
            END;
            CREATE TRIGGER IF NOT EXISTS index_chunks_ad AFTER DELETE ON index_chunks BEGIN
                INSERT INTO index_fts(index_fts, rowid, text) VALUES ('delete', old.id, old.text);
            END;
            """);

        // Named chats and pinned messages (added in 2.2): existing messages become the first chat.
        AddColumnIfMissing(connection, "messages", "conversation", "INTEGER NOT NULL DEFAULT 1");
        AddColumnIfMissing(connection, "messages", "pinned", "INTEGER NOT NULL DEFAULT 0");
        using (var first = connection.CreateCommand())
        {
            first.CommandText = "INSERT OR IGNORE INTO conversations(id, title, created, updated) VALUES (1, $title, $now, $now)";
            first.Parameters.AddWithValue("$title", DefaultChatTitle);
            first.Parameters.AddWithValue("$now", ToText(DateTime.Now));
            first.ExecuteNonQuery();
        }
        Execute(connection, "CREATE INDEX IF NOT EXISTS messages_conversation ON messages(conversation, id);");
    }

    public const string DefaultChatTitle = "New chat";

    private static void AddColumnIfMissing(SqliteConnection connection, string table, string column, string definition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}'";
        if (Convert.ToInt64(check.ExecuteScalar()) == 0) Execute(connection, $"ALTER TABLE {table} ADD COLUMN {column} {definition};");
    }

    public static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Dates are stored as sortable local-time text, for example 2026-10-26T14:30:00.000.</summary>
    public static string ToText(DateTime value) => value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture);

    public static DateTime FromText(string value) =>
        DateTime.ParseExact(value, "yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None);
}
