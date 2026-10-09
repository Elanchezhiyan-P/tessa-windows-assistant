using System.Text.Json;

namespace WinCompanion.Services;

/// <param name="Role">user, assistant, tool or error.</param>
public sealed record ChatEntry(string Role, string Text, DateTime Time);

/// <summary>The visible transcript, saved to disk so it survives restarts.</summary>
public sealed class ChatStore
{
    private const int MaxEntries = 200;
    private static string FilePath => AppPaths.FileIn("transcript.json");

    public List<ChatEntry> Entries { get; } = Load();

    public void Append(ChatEntry entry)
    {
        Entries.Add(entry);
        if (Entries.Count > MaxEntries) Entries.RemoveRange(0, Entries.Count - MaxEntries);
        Save();
    }

    public void Clear()
    {
        Entries.Clear();
        Save();
    }

    private static List<ChatEntry> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<List<ChatEntry>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException) { /* start fresh */ }
        return new();
    }

    private void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Entries)); }
        catch (IOException) { /* history is a convenience */ }
    }
}
