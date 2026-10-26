using System.Text.Json.Nodes;
using static WinCompanion.Tools.ToolSchema;

namespace WinCompanion.Tools;

internal static class NotesTools
{
    private static string NotesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WinCompanion", "notes.md");

    /// <summary>Appends a timestamped line to the notes file. A to-do is a Markdown checkbox, so any Markdown app shows it as a task.</summary>
    public static string Append(string text, bool todo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(NotesPath)!);
        var clean = text.Trim().ReplaceLineEndings(" ");
        File.AppendAllText(NotesPath, $"- {(todo ? "[ ] " : "")}[{DateTime.Now:yyyy-MM-dd HH:mm}] {clean}{Environment.NewLine}");
        return NotesPath;
    }

    /// <summary>Turns what was typed into the quick-capture box into (text, isTodo): "todo: x", "to-do x" or "[] x" are to-dos.</summary>
    public static (string Text, bool Todo) ParseCapture(string input)
    {
        var text = input.Trim();
        foreach (var prefix in new[] { "todo:", "to-do:", "todo ", "to-do ", "[]", "[ ]", "task:" })
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return (text[prefix.Length..].Trim(), true);
        }
        return (text, false);
    }

    public static IEnumerable<ITool> All()
    {
        yield return new DelegateTool("add_note", "Append a quick note to the user's notes file, with a timestamp.",
            new JsonObject { ["text"] = Str("The note text.") }, ["text"], false,
            args => Task.FromResult($"Note saved to {Append(args["text"]!.GetValue<string>(), todo: false)}."));

        yield return new DelegateTool("add_todo", "Add a to-do item (an unchecked task) to the user's notes file.",
            new JsonObject { ["text"] = Str("The task.") }, ["text"], false,
            args => Task.FromResult($"To-do saved to {Append(args["text"]!.GetValue<string>(), todo: true)}."));

        yield return new DelegateTool("read_notes", "Read the most recent notes and to-dos, optionally filtered by a word. Lines starting with '- [ ]' are open to-dos.",
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
