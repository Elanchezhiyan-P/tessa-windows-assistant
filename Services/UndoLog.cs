using System.Text;

namespace WinCompanion.Services;

public sealed record UndoMove(string From, string To);

/// <summary>
/// Remembers the file moves and renames she made, so "/undo" can put them back. Kept in memory only: it covers what happened
/// since she started, and nothing is ever deleted by an undo.
/// </summary>
public sealed class UndoLog
{
    public static UndoLog Shared { get; } = new();

    private const int MaxEntries = 20;

    private sealed record Entry(string Description, IReadOnlyList<UndoMove> Moves, IReadOnlyList<string> CreatedFolders);

    private readonly object _gate = new();
    private readonly List<Entry> _entries = new();

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    /// <param name="createdFolders">Folders made just for this action; they are removed again if the undo leaves them empty.</param>
    public void Record(string description, IEnumerable<UndoMove> moves, IEnumerable<string>? createdFolders = null)
    {
        var list = moves.ToList();
        if (list.Count == 0) return;
        lock (_gate)
        {
            _entries.Add(new Entry(description, list, (createdFolders ?? []).ToList()));
            if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
        }
    }

    /// <summary>Puts the most recent action back and says what happened.</summary>
    public string Undo()
    {
        Entry entry;
        lock (_gate)
        {
            if (_entries.Count == 0)
                return "There is nothing to undo. I can undo file moves, renames and folder tidy-ups made since I started.";
            entry = _entries[^1];
            _entries.RemoveAt(_entries.Count - 1);
        }

        int restored = 0;
        var problems = new List<string>();
        foreach (var move in entry.Moves.Reverse())
        {
            var isFolder = Directory.Exists(move.To);
            if (!isFolder && !File.Exists(move.To)) { problems.Add($"{move.To} is no longer there."); continue; }
            if (File.Exists(move.From) || Directory.Exists(move.From)) { problems.Add($"{move.From} already exists, so I left {move.To} alone."); continue; }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(move.From)!);
                if (isFolder) Directory.Move(move.To, move.From);
                else File.Move(move.To, move.From);
                restored++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"Couldn't move {move.To} back: {ex.Message}");
            }
        }

        foreach (var folder in entry.CreatedFolders)
        {
            try
            {
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        var sb = new StringBuilder($"Undid \"{entry.Description}\": put {restored} item(s) back.");
        foreach (var problem in problems.Take(5)) sb.Append("\n• ").Append(problem);
        if (problems.Count > 5) sb.Append($"\n• …and {problems.Count - 5} more.");
        return sb.ToString();
    }
}
