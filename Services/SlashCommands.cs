using System.Text;

namespace WinCompanion.Services;

/// <param name="Name">Without the slash, lower case.</param>
/// <param name="Usage">What to type, e.g. "/export [words]".</param>
/// <param name="NeedsArgs">Picking it from the suggestions only fills it in, so the argument can be typed.</param>
public sealed record SlashCommand(string Name, string Usage, string Description, bool NeedsArgs = false);

/// <summary>The "/clear"-style commands typed in the chat box. They run in the app, never reach the model.</summary>
public static class SlashCommands
{
    public static readonly IReadOnlyList<SlashCommand> All =
    [
        new("clear", "/clear", "Wipe the chat and what the assistant remembers of it"),
        new("new", "/new", "Save the chat to a file, then start a fresh one"),
        new("export", "/export [words]", "Save the chat (or only messages about some words) as Markdown"),
        new("copy", "/copy", "Copy the last reply to the clipboard"),
        new("undo", "/undo", "Undo the last file move, rename or folder tidy-up"),
        new("search", "/search <words>", "Search your whole chat history (or press Ctrl+F)", NeedsArgs: true),
        new("nudge", "/nudge [water|posture|eyes|stand|off]", "Gentle repeating reminders for water, posture, eyes and stretching"),
        new("update", "/update", "Check GitHub for a newer version"),
        new("diagnostics", "/diagnostics", "Copy system and error details for a bug report"),
        new("briefing", "/briefing", "Reminders, weather and headlines for today"),
        new("focus", "/focus [minutes|pomodoro|stop]", "Start or stop focus mode (default 25 minutes)"),
        new("routine", "/routine [name]", "Run a saved routine, or list them"),
        new("routines", "/routines", "List the saved routines"),
        new("reminders", "/reminders", "List your pending reminders"),
        new("clipboard", "/clipboard [words|clear]", "Search the clipboard history, or clear it"),
        new("ocr", "/ocr [screen]", "Copy the text on the active window (or the whole screen)"),
        new("note", "/note <text>", "Save a quick note; start with todo: for a task", NeedsArgs: true),
        new("model", "/model [gemini|local]", "Show or switch the AI model"),
        new("voice", "/voice [on|off]", "Turn her spoken replies on or off"),
        new("settings", "/settings", "Open or close the settings"),
        new("system", "/system", "Memory, processor, graphics and disks of this PC, and a model to suit it"),
        new("features", "/features", "What I can do"),
        new("about", "/about", "Who made me, and which model I'm using"),
        new("help", "/help", "List the commands")
    ];

    /// <summary>Splits "/export my topic" into ("export", "my topic"); null when the text is not a slash command.</summary>
    public static (string Name, string Args)? Parse(string text)
    {
        var input = text.Trim();
        if (input.Length < 2 || input[0] != '/' || !char.IsLetter(input[1])) return null;

        var space = input.IndexOfAny([' ', '\t']);
        var name = (space < 0 ? input[1..] : input[1..space]).ToLowerInvariant();
        if (!name.All(char.IsLetter)) return null; // "/etc/hosts" is a path, not a command

        var args = space < 0 ? "" : input[(space + 1)..].Trim();
        return (name, args);
    }

    public static SlashCommand? Find(string name) =>
        All.FirstOrDefault(c => c.Name == name.ToLowerInvariant());

    /// <summary>Commands to suggest while typing: those starting with what follows the slash. Empty once an argument is being typed.</summary>
    public static IReadOnlyList<SlashCommand> Suggest(string text)
    {
        if (text.Length == 0 || text[0] != '/' || text.Any(char.IsWhiteSpace)) return [];
        var prefix = text[1..].ToLowerInvariant();
        return All.Where(c => c.Name.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }

    public static string HelpText()
    {
        var sb = new StringBuilder("Commands:");
        foreach (var command in All) sb.Append("\n• ").Append(command.Usage).Append(" — ").Append(command.Description);
        return sb.ToString();
    }

    public static string Unknown(string name) => $"I don't know /{name}. Type /help to see the commands.";
}
