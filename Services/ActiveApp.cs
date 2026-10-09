using System.Diagnostics;
using System.Text;
using WinCompanion.Tools;

namespace WinCompanion.Services;

public sealed record ActiveAppInfo(string Process, string Title);

public sealed record Suggestion(string Label, string Prompt);

/// <summary>Notices which app the user was in and offers relevant one-click help.</summary>
public static class ActiveApp
{
    /// <summary>The foreground window, or null if it is WinCompanion itself or nothing useful.</summary>
    public static ActiveAppInfo? Capture()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return null;

        try
        {
            using var process = Process.GetProcessById((int)pid);
            var length = NativeMethods.GetWindowTextLength(hwnd);
            var title = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(hwnd, title, title.Capacity);
            return new ActiveAppInfo(process.ProcessName, title.ToString());
        }
        catch (ArgumentException) { return null; } // process exited
        catch (InvalidOperationException) { return null; }
    }

    public static string FriendlyName(ActiveAppInfo app) => app.Process.ToLowerInvariant() switch
    {
        "chrome" => "Chrome",
        "msedge" => "Edge",
        "firefox" => "Firefox",
        "code" => "VS Code",
        "devenv" => "Visual Studio",
        "winword" => "Word",
        "excel" => "Excel",
        "powerpnt" => "PowerPoint",
        "outlook" => "Outlook",
        "windowsterminal" => "Terminal",
        "explorer" => "File Explorer",
        _ => app.Process
    };

    public static IReadOnlyList<Suggestion> Suggestions(ActiveAppInfo? app)
    {
        const string look = "Take a screenshot and ";
        var process = app?.Process.ToLowerInvariant() ?? "";

        return process switch
        {
            "chrome" or "msedge" or "firefox" or "brave" or "opera" =>
            [
                new("Summarize this page", look + "summarize the web page I'm looking at."),
                new("Explain what's on screen", look + "explain what I'm looking at.")
            ],
            "code" or "devenv" or "rider64" or "idea64" or "pycharm64" or "notepad++" or "sublime_text" =>
            [
                new("Explain this error", look + "explain any error or problem visible and suggest a fix."),
                new("Review this code", look + "briefly review the code visible.")
            ],
            "windowsterminal" or "powershell" or "pwsh" or "cmd" =>
            [
                new("Explain this output", look + "explain the terminal output and any errors.")
            ],
            "winword" or "excel" or "powerpnt" or "outlook" =>
            [
                new("Help with this", look + "suggest improvements or next steps for what I'm working on."),
                new("Summarize clipboard", "Summarize my clipboard text.")
            ],
            "explorer" =>
            [
                new("Organize Downloads", "Organize my Downloads folder by file type."),
                new("What's on my screen?", look + "tell me what I'm looking at.")
            ],
            _ =>
            [
                new("What's on my screen?", look + "tell me what I'm looking at."),
                new("Summarize clipboard", "Summarize my clipboard text.")
            ]
        };
    }

    /// <summary>One line for the model's system prompt.</summary>
    public static string PromptLine(ActiveAppInfo? app) =>
        app is null
            ? ""
            : $"\nThe user was just using the app '{app.Process}' (window title: \"{app.Title}\"). " +
              "Use this to tailor your help, but only mention it when relevant.";
}
