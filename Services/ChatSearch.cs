using System.Text;

namespace WinCompanion.Services;

/// <summary>Searches the whole saved chat history (not just what is on screen) and lists the matches with a snippet.</summary>
public static class ChatSearch
{
    private const int Limit = 15;
    private const int Before = 50;
    private const int After = 110;

    public static string Run(string query, string assistantName) =>
        Format(ChatStore.Search(query, Limit), query, assistantName);

    public static string Format(IReadOnlyList<ChatEntry> matches, string query, string assistantName)
    {
        if (matches.Count == 0) return $"Nothing in your chat history matches \"{query.Trim()}\".";

        var sb = new StringBuilder($"Found {matches.Count}{(matches.Count == Limit ? " or more" : "")} message(s) about \"{query.Trim()}\", newest first:");
        foreach (var match in matches)
        {
            var who = match.Role == "user" ? "You" : assistantName;
            sb.Append($"\n• {match.Time:d MMM yyyy HH:mm} · {who}: {Snippet(match.Text, query)}");
        }
        return sb.ToString();
    }

    /// <summary>A short piece of <paramref name="text"/> around the first of the query's words that appears in it.</summary>
    internal static string Snippet(string text, string query)
    {
        var flat = text.ReplaceLineEndings(" ").Trim();
        var at = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => flat.IndexOf(word, StringComparison.OrdinalIgnoreCase))
            .Where(i => i >= 0)
            .DefaultIfEmpty(0)
            .Min();

        var start = Math.Max(0, at - Before);
        var end = Math.Min(flat.Length, at + After);
        return (start > 0 ? "…" : "") + flat[start..end] + (end < flat.Length ? "…" : "");
    }
}
