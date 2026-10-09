using System.Text.Json.Nodes;
using static WinCompanion.Tools.ToolSchema;

namespace WinCompanion.Tools;

internal static class NotesTools
{
    private static string NotesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WinCompanion", "notes.md");

    public static IEnumerable<ITool> All()
    {
        yield return new DelegateTool("add_note", "Append a quick note to the user's notes file, with a timestamp.",
            new JsonObject { ["text"] = Str("The note text.") }, ["text"], false,
            args =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(NotesPath)!);
                var text = args["text"]!.GetValue<string>().Trim().ReplaceLineEndings(" ");
                File.AppendAllText(NotesPath, $"- [{DateTime.Now:yyyy-MM-dd HH:mm}] {text}{Environment.NewLine}");
                return Task.FromResult($"Note saved to {NotesPath}.");
            });

        yield return new DelegateTool("read_notes", "Read the most recent notes, optionally filtered by a word.",
            new JsonObject
            {
                ["count"] = Int("How many recent notes to return (default 20)."),
                ["search"] = Str("Only return notes containing this text.")
            }, [], false,
            args =>
            {
                if (!File.Exists(NotesPath)) return Task.FromResult("No notes yet.");

                var lines = File.ReadAllLines(NotesPath).Where(l => l.Length > 0);
                var search = args["search"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(search))
                    lines = lines.Where(l => l.Contains(search, StringComparison.OrdinalIgnoreCase));

                var count = Math.Clamp(args["count"]?.GetValue<int>() ?? 20, 1, 200);
                var result = lines.TakeLast(count).ToList();
                return Task.FromResult(result.Count == 0 ? "No matching notes." : string.Join('\n', result));
            });
    }
}
