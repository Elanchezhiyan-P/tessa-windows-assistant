using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace WinCompanion.Services;

public sealed record FileHit(string Path, DateTime Modified, string Snippet, bool NameMatch);

/// <param name="Problems">Files that were left out because they have no readable text or couldn't be opened.</param>
public sealed record IndexSummary(int Added, int Updated, int Removed, int Unchanged, int Problems)
{
    public bool Changed => Added + Updated + Removed > 0;
}

public sealed record IndexStats(int Folders, int Files, int Chunks, int Problems);

/// <summary>
/// A search index of the documents in folders the user chose, kept in the local database. Only folders the user adds are read, only
/// documents are indexed, and the text stays on this PC. Searching is by words (not by meaning), and also matches file names.
/// </summary>
public static class FileIndex
{
    // Folders that are never worth indexing, and a few the user must not index by accident.
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "bin", "obj", "packages", "venv", ".venv", "__pycache__", "$RECYCLE.BIN", "System Volume Information",
        "AppData", "Windows", "Program Files", "Program Files (x86)", "ProgramData"
    };

    private const int MaxChunksPerFile = 600;

    // ---- Folders ---------------------------------------------------------------------------------------

    /// <summary>Whether a folder may be indexed. Drive roots and system folders are refused.</summary>
    public static bool IsAllowedFolder(string path, out string reason)
    {
        reason = "";
        string full;
        try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            reason = "That isn't a valid folder path.";
            return false;
        }

        if (!Directory.Exists(full)) { reason = "That folder doesn't exist."; return false; }
        if (full.Length <= 3) { reason = "Pick a folder, not a whole drive."; return false; }

        foreach (var special in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                     Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                 })
        {
            if (special.Length > 3 && Inside(full, special))
            {
                reason = "That is a system or app-data folder. Choose a folder with your own documents.";
                return false;
            }
        }
        return true;
    }

    private static bool Inside(string path, string parent)
    {
        parent = Path.TrimEndingDirectorySeparator(parent);
        return path.Equals(parent, StringComparison.OrdinalIgnoreCase)
               || path.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static string AddFolder(string path)
    {
        if (!IsAllowedFolder(path, out var reason)) return reason;
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        using var db = Database.Open();
        if (Folders(db).Any(f => Inside(full, f) || Inside(f, full)))
            return "That folder is already covered by one in the list (or contains one). Remove the other first if you want to change it.";

        using var command = db.CreateCommand();
        command.CommandText = "INSERT INTO index_folders(path, added) VALUES ($path, $added)";
        command.Parameters.AddWithValue("$path", full);
        command.Parameters.AddWithValue("$added", Database.ToText(DateTime.Now));
        command.ExecuteNonQuery();
        return $"Added {full}.";
    }

    public static string RemoveFolder(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(path.Trim().Trim('"'));
        using var db = Database.Open();
        var match = Folders(db).FirstOrDefault(f => f.Equals(full, StringComparison.OrdinalIgnoreCase))
                    ?? Folders(db).FirstOrDefault(f => f.EndsWith(Path.DirectorySeparatorChar + full, StringComparison.OrdinalIgnoreCase)
                                                       || Path.GetFileName(f).Equals(full, StringComparison.OrdinalIgnoreCase));
        if (match is null) return $"\"{path.Trim()}\" isn't one of the indexed folders.";

        using var transaction = db.BeginTransaction();
        DeleteFolderFiles(db, transaction, match);
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM index_folders WHERE path = $path";
        command.Parameters.AddWithValue("$path", match);
        command.ExecuteNonQuery();
        transaction.Commit();
        return $"Removed {match} and forgot its documents.";
    }

    public static IReadOnlyList<string> Folders()
    {
        using var db = Database.Open();
        return Folders(db);
    }

    private static List<string> Folders(SqliteConnection db)
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT path FROM index_folders ORDER BY path";
        var list = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) list.Add(reader.GetString(0));
        return list;
    }

    /// <summary>Forgets every indexed document and folder.</summary>
    public static void Clear()
    {
        using var db = Database.Open();
        using var transaction = db.BeginTransaction();
        foreach (var sql in new[] { "DELETE FROM index_chunks", "DELETE FROM index_files", "DELETE FROM index_folders" })
        {
            using var command = db.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public static IndexStats Stats()
    {
        using var db = Database.Open();
        return new IndexStats(
            Folders(db).Count,
            Count(db, "SELECT COUNT(*) FROM index_files WHERE chunks > 0"),
            Count(db, "SELECT COUNT(*) FROM index_chunks"),
            Count(db, "SELECT COUNT(*) FROM index_files WHERE chunks = 0"));
    }

    private static int Count(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    // ---- Indexing --------------------------------------------------------------------------------------

    /// <summary>
    /// Brings the index up to date with the chosen folders: new and changed documents are read, deleted ones are dropped, and
    /// unchanged ones are skipped. Call it from a background thread.
    /// </summary>
    public static IndexSummary Update(IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        int added = 0, updated = 0, removed = 0, unchanged = 0, problems = 0;
        using var db = Database.Open();

        foreach (var folder in Folders(db))
        {
            if (cancel.IsCancellationRequested) break;
            if (!Directory.Exists(folder)) continue; // a drive that isn't plugged in: keep what is indexed

            var known = KnownFiles(db, folder);
            var current = Enumerate(folder).ToList();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var done = 0;
            foreach (var file in current)
            {
                if (cancel.IsCancellationRequested) break;
                seen.Add(file.FullName);
                done++;

                var modified = Database.ToText(file.LastWriteTime);
                if (known.TryGetValue(file.FullName, out var old) && old.Size == file.Length && old.Modified == modified)
                {
                    unchanged++;
                    continue;
                }

                progress?.Report($"Reading {done} of {current.Count}: {file.Name}");
                var text = DocumentText.Extract(file.FullName, out var problem);
                var chunks = text is null ? [] : TextChunks.Split(text).Take(MaxChunksPerFile).ToList();
                Save(db, folder, file, modified, chunks, problem, old.Id);

                if (chunks.Count == 0) problems++;
                if (old.Id == 0) added++; else updated++;
            }

            if (cancel.IsCancellationRequested) break;
            foreach (var (path, info) in known.Where(k => !seen.Contains(k.Key)))
            {
                Delete(db, info.Id);
                removed++;
            }
        }
        return new IndexSummary(added, updated, removed, unchanged, problems);
    }

    private static Dictionary<string, (long Id, long Size, string Modified)> KnownFiles(SqliteConnection db, string folder)
    {
        using var command = db.CreateCommand();
        command.CommandText = "SELECT path, id, size, modified FROM index_files WHERE folder = $folder";
        command.Parameters.AddWithValue("$folder", folder);
        var known = new Dictionary<string, (long, long, string)>(StringComparer.OrdinalIgnoreCase);
        using var reader = command.ExecuteReader();
        while (reader.Read()) known[reader.GetString(0)] = (reader.GetInt64(1), reader.GetInt64(2), reader.GetString(3));
        return known;
    }

    /// <summary>The documents under a folder, skipping hidden and system files, links, build folders and anything that isn't a document.</summary>
    public static IEnumerable<FileInfo> Enumerate(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            List<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(folder).ToList(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                FileAttributes attributes;
                try { attributes = File.GetAttributes(entry); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

                if (attributes.HasFlag(FileAttributes.ReparsePoint) || attributes.HasFlag(FileAttributes.Hidden) || attributes.HasFlag(FileAttributes.System))
                    continue;

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    if (!name.StartsWith('.') && !SkippedFolders.Contains(name)) pending.Push(entry);
                }
                else if (!name.StartsWith("~$") && DocumentText.IsSupported(entry))
                {
                    yield return new FileInfo(entry);
                }
            }
        }
    }

    private static void Save(SqliteConnection db, string folder, FileInfo file, string modified, List<string> chunks, string? problem, long oldId)
    {
        using var transaction = db.BeginTransaction();
        if (oldId != 0) DeleteChunks(db, transaction, oldId);

        long id = oldId;
        using (var upsert = db.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO index_files(path, folder, size, modified, indexed, chunks, note)
                VALUES ($path, $folder, $size, $modified, $indexed, $chunks, $note)
                ON CONFLICT(path) DO UPDATE SET size = excluded.size, modified = excluded.modified, indexed = excluded.indexed,
                                                chunks = excluded.chunks, note = excluded.note
                RETURNING id
                """;
            upsert.Parameters.AddWithValue("$path", file.FullName);
            upsert.Parameters.AddWithValue("$folder", folder);
            upsert.Parameters.AddWithValue("$size", file.Length);
            upsert.Parameters.AddWithValue("$modified", modified);
            upsert.Parameters.AddWithValue("$indexed", Database.ToText(DateTime.Now));
            upsert.Parameters.AddWithValue("$chunks", chunks.Count);
            upsert.Parameters.AddWithValue("$note", (object?)problem ?? DBNull.Value);
            id = Convert.ToInt64(upsert.ExecuteScalar());
        }

        for (var i = 0; i < chunks.Count; i++)
        {
            using var insert = db.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO index_chunks(file_id, ordinal, text) VALUES ($file, $ordinal, $text)";
            insert.Parameters.AddWithValue("$file", id);
            insert.Parameters.AddWithValue("$ordinal", i);
            insert.Parameters.AddWithValue("$text", chunks[i]);
            insert.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private static void Delete(SqliteConnection db, long id)
    {
        using var transaction = db.BeginTransaction();
        DeleteChunks(db, transaction, id);
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM index_files WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void DeleteChunks(SqliteConnection db, SqliteTransaction transaction, long fileId)
    {
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM index_chunks WHERE file_id = $id";
        command.Parameters.AddWithValue("$id", fileId);
        command.ExecuteNonQuery();
    }

    private static void DeleteFolderFiles(SqliteConnection db, SqliteTransaction transaction, string folder)
    {
        using var chunks = db.CreateCommand();
        chunks.Transaction = transaction;
        chunks.CommandText = "DELETE FROM index_chunks WHERE file_id IN (SELECT id FROM index_files WHERE folder = $folder)";
        chunks.Parameters.AddWithValue("$folder", folder);
        chunks.ExecuteNonQuery();

        using var files = db.CreateCommand();
        files.Transaction = transaction;
        files.CommandText = "DELETE FROM index_files WHERE folder = $folder";
        files.Parameters.AddWithValue("$folder", folder);
        files.ExecuteNonQuery();
    }

    // ---- Searching -------------------------------------------------------------------------------------

    /// <summary>
    /// The FTS5 queries for some words: one needing every word, and a looser one needing any. Short and filler words
    /// ("find", "the", "from") are dropped, so a natural request still works.
    /// </summary>
    public static (string All, string Any) BuildQueries(string? text)
    {
        var words = TextChunks.Keywords(text)
            .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()))
            .Where(w => w.Length > 0)
            .Distinct()
            .ToList();
        var quoted = words.Select(w => $"\"{w}\"").ToList();
        return (string.Join(' ', quoted), string.Join(" OR ", quoted));
    }

    /// <summary>Splits "invoice electricity after:2026-03 before:2026-04" into the words and the two dates.</summary>
    public static (string Query, string? After, string? Before) ParseFindArgs(string args)
    {
        string? after = null, before = null;
        var words = new List<string>();
        foreach (var token in args.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith("after:", StringComparison.OrdinalIgnoreCase)) after = token[6..];
            else if (token.StartsWith("before:", StringComparison.OrdinalIgnoreCase)) before = token[7..];
            else words.Add(token);
        }
        return (string.Join(' ', words), after, before);
    }

    /// <summary>"2026-03-15" or "2026-03" as a sortable date; null if it isn't one.</summary>
    public static string? NormalizeDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var formats = new[] { "yyyy-MM-dd", "yyyy-M-d", "yyyy-MM", "yyyy-M", "yyyy" };
        return DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>Files whose name or text matches, best first. <paramref name="after"/> and <paramref name="before"/> limit by last-changed date.</summary>
    public static IReadOnlyList<FileHit> Search(string query, string? after = null, string? before = null, int limit = 8)
    {
        var (all, any) = BuildQueries(query);
        if (all.Length == 0) return [];

        using var db = Database.Open();
        var hits = new List<FileHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var hit in NameMatches(db, TextChunks.Keywords(query), after, before, limit))
            if (seen.Add(hit.Path)) hits.Add(hit);

        foreach (var match in new[] { all, any }.Distinct())
        {
            if (hits.Count >= limit) break;
            foreach (var hit in ContentMatches(db, match, after, before, limit * 6))
            {
                if (seen.Add(hit.Path))
                {
                    if (hits.Count < limit) hits.Add(hit);
                    continue;
                }
                // Already listed because of its name: show what the text says rather than only "the name matches".
                var at = hits.FindIndex(h => h.NameMatch && h.Path.Equals(hit.Path, StringComparison.OrdinalIgnoreCase));
                if (at >= 0 && hits[at].Snippet.StartsWith("(the file name", StringComparison.Ordinal)) hits[at] = hits[at] with { Snippet = hit.Snippet };
            }
            if (hits.Count > 0 && match == all) break; // a result needing every word is better than looser ones
        }
        return hits;
    }

    private static IEnumerable<FileHit> NameMatches(SqliteConnection db, List<string> words, string? after, string? before, int limit)
    {
        if (words.Count == 0) yield break;
        using var command = db.CreateCommand();
        var sb = new StringBuilder("SELECT path, modified FROM index_files WHERE 1 = 1");
        for (var i = 0; i < words.Count; i++)
        {
            sb.Append($" AND instr(lower(path), $w{i}) > 0");
            command.Parameters.AddWithValue($"$w{i}", words[i]);
        }
        AppendDates(sb, command, after, before, "modified");
        sb.Append(" ORDER BY modified DESC LIMIT $limit");
        command.CommandText = sb.ToString();
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        while (reader.Read())
            yield return new FileHit(reader.GetString(0), Database.FromText(reader.GetString(1)), "(the file name matches)", true);
    }

    private static IEnumerable<FileHit> ContentMatches(SqliteConnection db, string match, string? after, string? before, int limit)
    {
        using var command = db.CreateCommand();
        var sb = new StringBuilder("""
            SELECT f.path, f.modified, snippet(index_fts, 0, '**', '**', '…', 22)
            FROM index_fts
            JOIN index_chunks c ON c.id = index_fts.rowid
            JOIN index_files f ON f.id = c.file_id
            WHERE index_fts MATCH $match
            """);
        command.Parameters.AddWithValue("$match", match);
        AppendDates(sb, command, after, before, "f.modified");
        sb.Append(" ORDER BY bm25(index_fts) LIMIT $limit");
        command.CommandText = sb.ToString();
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        while (reader.Read())
            yield return new FileHit(reader.GetString(0), Database.FromText(reader.GetString(1)),
                reader.GetString(2).ReplaceLineEndings(" "), false);
    }

    private static void AppendDates(StringBuilder sb, SqliteCommand command, string? after, string? before, string column)
    {
        if (NormalizeDate(after) is { } from)
        {
            sb.Append($" AND {column} >= $after");
            command.Parameters.AddWithValue("$after", from);
        }
        if (NormalizeDate(before) is { } to)
        {
            sb.Append($" AND {column} < $before");
            command.Parameters.AddWithValue("$before", to);
        }
    }

    public static string Describe(IReadOnlyList<FileHit> hits, string query)
    {
        query = query.Trim().Trim('"');
        if (hits.Count == 0) return $"Nothing in the indexed folders matches \"{query}\".";
        var sb = new StringBuilder($"{hits.Count} match(es) for \"{query}\":");
        foreach (var hit in hits)
        {
            // One short block per file: its name and date, where it is, and what it says.
            var folder = Path.GetDirectoryName(hit.Path) ?? "";
            var snippet = hit.Snippet.StartsWith("(the file name", StringComparison.Ordinal) ? "*the file name matches*" : hit.Snippet;
            sb.Append($"\n\n**{Path.GetFileName(hit.Path)}** · {hit.Modified:d MMM yyyy}\nin {folder}\n{snippet}");
        }
        return sb.ToString();
    }

    public static string DescribeStats(IndexStats stats) =>
        stats.Folders == 0
            ? "No folders are indexed. Add one with /index add <folder>, and I can search the documents in it."
            : $"Indexed {stats.Files:N0} document(s) ({stats.Chunks:N0} searchable pieces) in {stats.Folders} folder(s)" +
              (stats.Problems > 0 ? $"; {stats.Problems:N0} file(s) had no readable text." : ".");

    public static string Describe(IndexSummary s) =>
        s.Changed
            ? $"Index updated: {s.Added:N0} new, {s.Updated:N0} changed, {s.Removed:N0} removed" + (s.Problems > 0 ? $" ({s.Problems:N0} had no readable text)." : ".")
            : "The index is already up to date.";
}
