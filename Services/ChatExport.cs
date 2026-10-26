using System.Text;

namespace WinCompanion.Services;

/// <summary>Saves a conversation (or the messages matching a search) as a Markdown file.</summary>
public static class ChatExport
{
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WinCompanion", "exports");

    public static string ToMarkdown(IEnumerable<ChatEntry> entries, string title, string assistantName)
    {
        var sb = new StringBuilder();
        sb.Append("# ").AppendLine(title).AppendLine();
        sb.AppendLine($"_Exported {DateTime.Now:d MMM yyyy, HH:mm}_").AppendLine();

        DateTime? day = null;
        foreach (var entry in entries)
        {
            if (day != entry.Time.Date)
            {
                day = entry.Time.Date;
                sb.AppendLine($"## {entry.Time:dddd, d MMMM yyyy}").AppendLine();
            }
            var who = entry.Role == "user" ? "You" : assistantName;
            sb.AppendLine($"**{who}** · {entry.Time:HH:mm}").AppendLine();
            sb.AppendLine(entry.Text.Trim()).AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>Writes the Markdown file and returns its path, or null when there is nothing to export.</summary>
    public static string? Save(IReadOnlyList<ChatEntry> entries, string title, string assistantName, DateTime? now = null)
    {
        if (entries.Count == 0) return null;
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, $"chat-{now ?? DateTime.Now:yyyyMMdd-HHmmss}.md");
        File.WriteAllText(path, ToMarkdown(entries, title, assistantName), new UTF8Encoding(false));
        return path;
    }

    /// <summary>The whole saved conversation, or only the messages containing <paramref name="query"/> when one is given.</summary>
    public static string Export(string? query, string assistantName)
    {
        IReadOnlyList<ChatEntry> entries;
        string title;
        if (string.IsNullOrWhiteSpace(query))
        {
            entries = ChatStore.ReadAll();
            title = $"Conversation with {assistantName}";
        }
        else
        {
            entries = ChatStore.Search(query, 100).Reverse().ToList();
            title = $"Messages about \"{query.Trim()}\"";
        }
        var path = Save(entries, title, assistantName);
        return path is null ? "There is nothing to export yet." : $"Saved {entries.Count} message(s) to {path}";
    }
}
