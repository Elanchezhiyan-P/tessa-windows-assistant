namespace WinCompanion.Services;

/// <param name="Keys">What to press or type.</param>
public sealed record TourRow(string Keys, string Text);

public sealed record TourStep(string Title, string Intro, IReadOnlyList<TourRow> Rows);

/// <summary>The three screens of the first-run tour. Kept as data so the wording is easy to change and to test.</summary>
public static class TourSteps
{
    public static IReadOnlyList<TourStep> Build(string assistantName) =>
    [
        new("Reach me from anywhere", $"These shortcuts work in any app, even when {assistantName}'s window is hidden.",
        [
            new("Ctrl+Alt+Space", "Show or hide the chat window"),
            new("Ctrl+Alt+V", "Talk to me (push-to-talk)"),
            new("Ctrl+Alt+N", "Save a quick note or to-do"),
            new("Ctrl+Alt+A", "Select text in any app, then ask me to explain, fix or translate it"),
            new("Ctrl+Alt+T", "Drag a box on the screen and copy the text inside it"),
            new($"Hey {assistantName}", "Hands-free, once you switch the wake word on in the tray menu")
        ]),
        new("Type / for shortcuts", "Commands start with a slash. They run on your PC, work without an AI model, and show up as you type.",
        [
            new("/features", "What I can do"),
            new("/nudge water", "Gentle repeating reminders"),
            new("/focus 25", "A focus timer that keeps me quiet"),
            new("/search invoice", "Find anything in your chat history (or press Ctrl+F)"),
            new("/undo", "Put back the last file move"),
            new("/help", "Every command")
        ]),
        new("Make me yours", "Open Settings with the gear in the window's corner, or type /settings.",
        [
            new("AI model", "Gemini (free key) or a local model that never leaves your PC"),
            new("Look and alerts", "Colours, quiet hours, and warnings for low disk or battery"),
            new("You and her", "Her name, voice and speed, and what she remembers about you"),
            new("Safety", "She always asks before anything risky; you can see everything she did in the action log")
        ])
    ];
}
