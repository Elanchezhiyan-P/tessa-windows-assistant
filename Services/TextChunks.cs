using System.Text;

namespace WinCompanion.Services;

/// <summary>Cuts a document into pieces that are small enough to search and to hand to a model, and picks the pieces that answer a question.</summary>
public static class TextChunks
{
    public const int TargetSize = 1000;
    private const int Overlap = 120;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "from", "that", "this", "what", "which", "who", "whom", "how", "when", "where", "why",
        "are", "was", "were", "has", "have", "had", "does", "did", "can", "could", "would", "should", "about", "into", "than",
        "then", "them", "they", "you", "your", "our", "find", "show", "tell", "give", "get", "any", "all", "its", "not",
        "file", "files", "document", "documents", "pdf", "word", "say", "says", "said"
    };

    /// <summary>Splits on paragraph breaks into pieces of about <paramref name="size"/> characters, each starting with a little of the one before.</summary>
    public static List<string> Split(string text, int size = TargetSize, int overlap = Overlap)
    {
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return chunks;

        var current = new StringBuilder();
        void Flush()
        {
            var piece = current.ToString().Trim();
            if (piece.Length == 0) return;
            chunks.Add(piece);
            current.Clear();
            // A new piece starts with the end of the last one, so a sentence cut at the boundary is still whole in one of them.
            var tail = Tail(piece, overlap);
            if (tail.Length > 0) current.Append(tail).Append(' ');
        }

        foreach (var paragraph in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var p = paragraph.Trim();
            if (p.Length == 0) continue;

            if (p.Length > size)
            {
                // A very long paragraph is cut at spaces.
                Flush();
                foreach (var part in HardSplit(p, size)) { current.Append(part); Flush(); }
                continue;
            }
            if (current.Length + p.Length + 2 > size && current.Length > overlap + 1) Flush();
            current.Append(p).Append("\n\n");
        }

        var rest = current.ToString().Trim();
        if (rest.Length > 0 && (chunks.Count == 0 || rest.Length > overlap + 2)) chunks.Add(rest);
        return chunks;
    }

    private static string Tail(string piece, int overlap)
    {
        if (piece.Length <= overlap) return "";
        var start = piece.Length - overlap;
        var space = piece.IndexOf(' ', start);
        return space < 0 ? "" : piece[(space + 1)..];
    }

    private static IEnumerable<string> HardSplit(string text, int size)
    {
        var start = 0;
        while (start < text.Length)
        {
            var end = Math.Min(text.Length, start + size);
            if (end < text.Length)
            {
                var space = text.LastIndexOf(' ', end - 1, Math.Min(200, end - start));
                if (space > start) end = space;
            }
            yield return text[start..end].Trim();
            start = end;
        }
    }

    /// <summary>The words worth searching for in a question: no short words and no "find", "the", "document" and the like.</summary>
    public static List<string> Keywords(string? question) =>
        (question ?? "")
            .Split([' ', '\t', '\n', ',', '.', ';', ':', '?', '!', '"', '\'', '(', ')', '-', '/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.ToLowerInvariant())
            .Where(w => w.Length >= 3 && !StopWords.Contains(w))
            .Distinct()
            .ToList();

    /// <summary>
    /// The pieces of a document that best match <paramref name="question"/>, in document order, up to <paramref name="maxChars"/>.
    /// With no usable words in the question, it is simply the start of the document.
    /// </summary>
    public static string Best(IReadOnlyList<string> chunks, string? question, int maxChars)
    {
        if (chunks.Count == 0) return "";
        var words = Keywords(question);

        var scored = chunks
            .Select((text, index) => (Index: index, Text: text, Score: Score(text, words)))
            .ToList();

        var chosen = words.Count > 0 && scored.Any(s => s.Score > 0)
            ? scored.Where(s => s.Score > 0).OrderByDescending(s => s.Score).ThenBy(s => s.Index)
            : scored.AsEnumerable();

        var picked = new List<(int Index, string Text)>();
        var used = 0;
        foreach (var piece in chosen)
        {
            if (picked.Count > 0 && used + piece.Text.Length > maxChars) continue;
            picked.Add((piece.Index, piece.Text));
            used += piece.Text.Length;
            if (used >= maxChars) break;
        }

        var sb = new StringBuilder();
        var previous = -2;
        foreach (var (index, text) in picked.OrderBy(p => p.Index))
        {
            if (index != previous + 1) sb.Append(sb.Length == 0 ? "" : "\n[…]\n");
            else sb.Append('\n');
            sb.Append(text);
            previous = index;
        }
        return sb.ToString();
    }

    /// <summary>More matching words score higher, and a word that repeats counts a little more each time.</summary>
    private static double Score(string text, List<string> words)
    {
        double score = 0;
        foreach (var word in words)
        {
            var count = 0;
            for (var at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase); at >= 0 && count < 20;
                 at = text.IndexOf(word, at + word.Length, StringComparison.OrdinalIgnoreCase))
                count++;
            if (count > 0) score += 1 + Math.Log(count);
        }
        return score;
    }
}
