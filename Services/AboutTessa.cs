using System.Reflection;
using System.Text.RegularExpressions;

namespace WinCompanion.Services;

/// <summary>
/// Fixed answers to "who made you?" and "what can you do?". The app answers these itself, so a Gemini or local model never
/// describes itself as the assistant, and the answer is the same whichever model is selected.
/// </summary>
public static class AboutTessa
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;

    // Only short messages: "who created the file report.docx" must still reach the model.
    private const int MaxQuestionLength = 90;

    private static readonly Regex AboutQuestion = new(
        @"\b(?:who|what)\b.*\b(?:made|created|built|develop(?:ed)?|designed|programmed|coded|trained|owns?|invented)\b.*\b(?:you|yourself)\b" +
        @"|\bwho\s+(?:is|are)\s+(?:you|your\s+(?:creator|developer|maker|author|owner|builder))\b" +
        @"|\bwho\s+are\s+you\b|\bwhat\s+are\s+you\b|\bwhat(?:'s|\s+is)\s+your\s+name\b" +
        @"|\b(?:tell|talk)\s+(?:me\s+)?about\s+(?:you|yourself)\b|\bintroduce\s+yourself\b|\babout\s+(?:you|yourself)\b" +
        @"|\b(?:which|what)\s+(?:llm|model|ai|language\s+model)\b.*\b(?:you|your|powers?|behind)\b" +
        @"|\bare\s+you\s+(?:gemini|chat\s*gpt|gpt|claude|qwen|llama|google|openai|a\s+bot|an?\s+ai)\b" +
        @"|\bwho\s+(?:is|are)\s+behind\s+you\b|\bwho\s+(?:made|created|built)\s+this\s+(?:app|assistant|bot)\b", Opts);

    private static readonly Regex FeaturesQuestion = new(
        @"\bwhat\s+(?:all\s+)?(?:can|could)\s+you\s+(?:do|help)\b|\bwhat\s+do\s+you\s+do\b|\bhow\s+can\s+you\s+help\b" +
        @"|\bwhat\s+(?:are\s+)?(?:your|the)\s+(?:features|abilities|capabilities|skills|powers|functions)\b" +
        @"|\bwhat\s+(?:features|abilities|capabilities|skills)\b.*\b(?:you|have|there)\b" +
        @"|\b(?:list|show|tell\s+me)\s+(?:me\s+)?(?:all\s+)?(?:your\s+|the\s+)?(?:features|abilities|capabilities|skills)\b" +
        @"|\bwhat\s+(?:can|should)\s+i\s+(?:ask|say|do\s+with)\b|^\W*(?:help|features|capabilities)\W*$", Opts);

    public static bool IsAboutQuestion(string text) => text.Length <= MaxQuestionLength && AboutQuestion.IsMatch(text);

    public static bool IsFeaturesQuestion(string text) => text.Length <= MaxQuestionLength && FeaturesQuestion.IsMatch(text);

    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "2.0.0";

    /// <param name="brain">How the current model is described, for example "Google Gemini" or "a local model (qwen2.5:3b)".</param>
    public static string About(string assistantName, string brain) =>
        $"I'm {assistantName}, a desktop assistant for Windows, made by Elanchezhiyan P (version {Version}). " +
        $"I live on your PC: I can open apps, set reminders, look at your screen, take notes, run PowerShell and more. " +
        $"Right now I use {brain} to understand what you type, but {assistantName} is the app itself, not that model. " +
        "Type /features to see what I can do, or /help for the commands.";

    public static string Features(string assistantName) => $"""
        Here is what {assistantName} can do:
        • Your PC: ask in plain words ("how much space is free?") and I run PowerShell and explain the result. Anything risky asks you first.
        • Reminders and timers: "remind me at 5pm to call mom", "every weekday at 9", "timer for 25 minutes". They work offline.
        • Open and control: apps, files, folders and web addresses; snap, minimise or close windows; volume, brightness, dark mode, sleep.
        • Screen: take screenshots, ask what's on screen, or copy the text on it (/ocr).
        • Clipboard: summarise, translate, fix or shorten what you copied; optional history you can search (/clipboard).
        • Notes and to-dos: /note or Ctrl+Alt+N, saved as Markdown.
        • Daily briefing (/briefing), focus mode and pomodoro (/focus), and routines you teach me (/routine).
        • This PC: memory, processor, graphics and disks, with a local model that suits them (/system, or Settings, This PC).
        • Files: find, read, move and rename, with nothing deleted permanently.
        • Web lookup for current facts, memory for things you tell me to remember, and chat export (/export).
        • Voice: push-to-talk with Ctrl+Alt+V, or say "Hey {assistantName}".
        Type /help to see every command.
        """;
}
