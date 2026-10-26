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

            CREATE TABLE IF NOT EXISTS reminders (
                id      TEXT PRIMARY KEY,
                message TEXT NOT NULL,
                due     TEXT NOT NULL,
                created TEXT NOT NULL,
                repeat  TEXT
            );
            """);
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
