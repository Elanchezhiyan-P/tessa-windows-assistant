using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WinCompanion.Services;

/// <summary>What a plain request turned into: a tool to run (with its arguments), or a finished reply.</summary>
public sealed record BasicCall(string? Tool, JsonObject? Args, string? Reply)
{
    public static BasicCall Run(string tool, JsonObject args) => new(tool, args, null);
    public static BasicCall Say(string reply) => new(null, null, reply);
}

/// <summary>
/// The minimum she can do with no AI model at all (no key, no local server): short, clear requests such as "open notepad",
/// "volume up", "take a screenshot" or "minimize chrome" are understood here and run as the same tools the model uses, with the
/// same approvals and log. Anything else needs a model. Only whole, short requests match, so a real question is never hijacked.
/// </summary>
public static class BasicCommands
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;
    private const int MaxLength = 90;
    private const int MaxTargetWords = 4;

    private static readonly Regex Time = new(@"^\W*(?:what(?:'s|\s+is)\s+(?:the\s+)?(?:current\s+)?time(?:\s+now)?|what\s+time\s+is\s+it(?:\s+now)?|(?:current\s+)?time(?:\s+now)?)\W*$", Opts);
    private static readonly Regex Date = new(@"^\W*(?:what(?:'s|\s+is)\s+(?:the\s+|today'?s\s+)?(?:date|day)(?:\s+today)?|what\s+day\s+is\s+(?:it|today)|today'?s\s+date)\W*$", Opts);

    private static readonly Regex Open = new(@"^\W*(?:please\s+)?(?:open|launch|go\s+to|show\s+me)\s+(?:up\s+)?(?:the\s+|my\s+)?(?<t>[^\n]+?)\W*$", Opts);
    private static readonly Regex Start = new(@"^\W*(?:please\s+)?start\s+(?:the\s+|my\s+)?(?<t>[^\n]+?)\W*$", Opts);

    private static readonly Regex VolumeUp = new(@"^\W*(?:please\s+)?(?:turn\s+(?:the\s+)?(?:volume\s+)?up|turn\s+it\s+up|volume\s+up|increase\s+(?:the\s+)?volume|raise\s+(?:the\s+)?volume|louder)(?:\s+(?:by\s+)?(?<n>\d{1,3})\s*%?)?\W*$", Opts);
    private static readonly Regex VolumeDown = new(@"^\W*(?:please\s+)?(?:turn\s+(?:the\s+)?(?:volume\s+)?down|turn\s+it\s+down|volume\s+down|decrease\s+(?:the\s+)?volume|lower\s+(?:the\s+)?volume|quieter|softer)(?:\s+(?:by\s+)?(?<n>\d{1,3})\s*%?)?\W*$", Opts);
    private static readonly Regex Mute = new(@"^\W*(?:please\s+)?(?:un)?mute(?:\s+(?:the\s+)?(?:sound|volume|audio|pc|computer))?\W*$", Opts);

    private static readonly Regex Screenshot = new(@"^\W*(?:please\s+)?(?:take\s+(?:a\s+)?screenshot|screenshot|capture\s+(?:the\s+)?screen|screen\s*shot)(?<w>\s+of\s+(?:this|the\s+(?:active|current))\s+window)?\W*$", Opts);
    private static readonly Regex Lock = new(@"^\W*(?:please\s+)?lock\s+(?:my\s+|the\s+)?(?:screen|pc|computer|laptop|workstation)\W*$", Opts);
    private static readonly Regex WindowVerb = new(@"^\W*(?:please\s+)?(?<v>minimi[sz]e|maximi[sz]e|restore|focus|switch\s+to|bring\s+up)\s+(?:the\s+)?(?<t>[^\n]+?)(?:\s+window)?\W*$", Opts);
    private static readonly Regex Snap = new(@"^\W*(?:please\s+)?snap\s+(?:the\s+)?(?<t>[^\n]+?)\s+(?:to\s+(?:the\s+)?)?(?<s>left|right)\W*$", Opts);
    private static readonly Regex Close = new(@"^\W*(?:please\s+)?close\s+(?:the\s+)?(?<t>[^\n]+?)(?:\s+window)?\W*$", Opts);
    private static readonly Regex ListWindows = new(@"^\W*(?:what(?:'s|\s+is)\s+open|(?:list|show)\s+(?:my\s+)?(?:open\s+)?windows|which\s+windows\s+are\s+open)\W*$", Opts);
    private static readonly Regex Clipboard = new(@"^\W*(?:what(?:'s|\s+is)\s+on\s+my\s+clipboard|(?:show|read)\s+(?:me\s+)?(?:my\s+)?clipboard|what\s+did\s+i\s+copy)\W*$", Opts);
    private static readonly Regex Weather = new(@"^\W*(?:what(?:'s|\s+is)\s+(?:the\s+)?)?weather(?:\s+(?:like\s+)?(?:in|for|at)\s+(?<c>[\p{L} .'-]{2,40}?))?(?:\s+(?:today|now|right\s+now))?\W*$", Opts);
    private static readonly Regex Brightness = new(@"^\W*(?:please\s+)?(?:set\s+)?(?:the\s+)?(?:screen\s+)?brightness\s+(?:to\s+)?(?<n>\d{1,3})\s*%?\W*$", Opts);
    private static readonly Regex Media = new(@"^\W*(?:please\s+)?(?:(?<pp>play|pause|resume)(?:\s+(?:the\s+)?(?:music|media|song|video|playback))?|(?<n>next|skip)(?:\s+(?:song|track))?|(?<p>previous|prev|back)(?:\s+(?:song|track))?|(?<s>stop)\s+(?:the\s+)?(?:music|media|playback))\W*$", Opts);
    private static readonly Regex Theme = new(@"^\W*(?:please\s+)?(?:switch\s+to\s+|turn\s+on\s+|enable\s+|toggle\s+)?(?<m>dark|light)\s+mode\W*$", Opts);
    private static readonly Regex Slow = new(@"^\W*(?:why\s+is\s+my\s+(?:pc|computer|laptop)\s+slow|(?:system|pc|computer)\s+(?:info|status|report|health))\W*$", Opts);
    private static readonly Regex Search = new(@"^\W*(?:please\s+)?(?:search(?:\s+(?:the\s+)?(?:web|internet|google))?\s+for|google|look\s+up|search)\s+(?<q>[^\n]+?)\W*$", Opts);

    // Names people say that are not file or program names.
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["settings"] = "ms-settings:", ["windows settings"] = "ms-settings:", ["task manager"] = "taskmgr", ["calculator"] = "calc",
        ["control panel"] = "control", ["command prompt"] = "cmd", ["cmd"] = "cmd", ["file explorer"] = "explorer", ["explorer"] = "explorer",
        ["this pc"] = "explorer", ["notepad"] = "notepad", ["paint"] = "mspaint", ["downloads"] = "shell:Downloads",
        ["downloads folder"] = "shell:Downloads", ["documents"] = "shell:Personal", ["documents folder"] = "shell:Personal",
        ["desktop folder"] = "shell:Desktop", ["pictures"] = "shell:My Pictures", ["pictures folder"] = "shell:My Pictures",
        ["recycle bin"] = "shell:RecycleBinFolder", ["snipping tool"] = "snippingtool", ["device manager"] = "devmgmt.msc",
        ["bluetooth settings"] = "ms-settings:bluetooth", ["wifi settings"] = "ms-settings:network-wifi"
    };

    // Words that mean the rest of the sentence is a real request for the model, not the name of something to open.
    private static readonly Regex NotATarget = new(
        @"\b(?:and|then|because|so|that|which|for\s+me|how|why|what|when|where|who|please|about|with|using|timer|focus|pomodoro|reminder|chat|conversation|plan|agent|new\s+chat)\b|[?]", Opts);

    /// <param name="hasModel">True when an AI model is set up. Then requests that a model answers better (web searches) are left to it.</param>
    public static BasicCall? Parse(string text, DateTime now, bool hasModel)
    {
        var input = (text ?? "").Trim();
        if (input.Length == 0 || input.Length > MaxLength || input.StartsWith('/')) return null;

        if (Time.IsMatch(input)) return BasicCall.Say($"It is {now:h:mm tt}.");
        if (Date.IsMatch(input)) return BasicCall.Say($"Today is {now:dddd, d MMMM yyyy}.");

        if (Mute.IsMatch(input)) return BasicCall.Run("change_volume", new JsonObject { ["action"] = "mute_toggle" });
        if (VolumeUp.Match(input) is { Success: true } up) return BasicCall.Run("change_volume", Volume("up", up));
        if (VolumeDown.Match(input) is { Success: true } down) return BasicCall.Run("change_volume", Volume("down", down));

        if (Screenshot.Match(input) is { Success: true } shot)
            return BasicCall.Run("save_screenshot", new JsonObject { ["scope"] = shot.Groups["w"].Success ? "active_window" : "screen" });
        if (Lock.IsMatch(input)) return BasicCall.Run("lock_screen", new JsonObject());
        if (ListWindows.IsMatch(input)) return BasicCall.Run("list_windows", new JsonObject());
        if (Clipboard.IsMatch(input)) return BasicCall.Run("get_clipboard", new JsonObject());
        if (Slow.IsMatch(input)) return BasicCall.Run("get_system_info", new JsonObject());

        if (Brightness.Match(input) is { Success: true } bright && int.TryParse(bright.Groups["n"].Value, out var percent) && percent <= 100)
            return BasicCall.Run("set_brightness", new JsonObject { ["percent"] = percent });

        if (Media.Match(input) is { Success: true } media)
        {
            var action = media.Groups["pp"].Success ? "play_pause" : media.Groups["n"].Success ? "next" : media.Groups["p"].Success ? "previous" : "stop";
            return BasicCall.Run("media_control", new JsonObject { ["action"] = action });
        }
        if (Theme.Match(input) is { Success: true } theme)
            return BasicCall.Run("set_theme", new JsonObject { ["mode"] = theme.Groups["m"].Value.ToLowerInvariant() });

        if (Snap.Match(input) is { Success: true } snap && Plausible(snap.Groups["t"].Value))
            return BasicCall.Run("window_action", new JsonObject { ["target"] = snap.Groups["t"].Value.Trim(), ["action"] = "snap_" + snap.Groups["s"].Value.ToLowerInvariant() });
        if (WindowVerb.Match(input) is { Success: true } verb && Plausible(verb.Groups["t"].Value))
        {
            var v = verb.Groups["v"].Value.ToLowerInvariant();
            var action = v.StartsWith("min", StringComparison.Ordinal) ? "minimize" : v.StartsWith("max", StringComparison.Ordinal) ? "maximize"
                : v == "restore" ? "restore" : "focus";
            return BasicCall.Run("window_action", new JsonObject { ["target"] = verb.Groups["t"].Value.Trim(), ["action"] = action });
        }
        if (Close.Match(input) is { Success: true } close && Plausible(close.Groups["t"].Value))
            return BasicCall.Run("close_window", new JsonObject { ["target"] = close.Groups["t"].Value.Trim() });

        if (Weather.Match(input) is { Success: true } weather)
        {
            var args = new JsonObject();
            if (weather.Groups["c"].Success) args["city"] = weather.Groups["c"].Value.Trim();
            return BasicCall.Run("get_weather", args);
        }

        if (Open.Match(input) is { Success: true } open && TargetOf(open.Groups["t"].Value) is { } target)
            return BasicCall.Run("open_target", new JsonObject { ["target"] = target });
        // "start" is too common a word to treat as a command when a model could understand the sentence.
        if (!hasModel && Start.Match(input) is { Success: true } start && TargetOf(start.Groups["t"].Value) is { } startTarget)
            return BasicCall.Run("open_target", new JsonObject { ["target"] = startTarget });

        if (!hasModel && Search.Match(input) is { Success: true } search)
            return BasicCall.Run("web_search", new JsonObject { ["query"] = search.Groups["q"].Value.Trim() });

        return null;
    }

    private static JsonObject Volume(string direction, Match match)
    {
        var args = new JsonObject { ["action"] = direction };
        // Each step is about 2%, so "volume up 10" is five steps.
        if (match.Groups["n"].Success && int.TryParse(match.Groups["n"].Value, out var percent)) args["steps"] = Math.Clamp(percent / 2, 1, 50);
        return args;
    }

    private static bool Plausible(string target)
    {
        var t = target.Trim();
        return t.Length is > 0 and <= 40 && t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= MaxTargetWords && !NotATarget.IsMatch(t);
    }

    /// <summary>What to open for "open X", or null when X doesn't look like the name of an app, folder or site.</summary>
    internal static string? TargetOf(string said)
    {
        var t = said.Trim().Trim('"', '\'');
        if (!Plausible(t)) return null;
        if (Known.TryGetValue(t, out var known)) return known;

        // A site: "open example.com" or a full address.
        if (Regex.IsMatch(t, @"^https?://\S+$", Opts)) return t;
        if (Regex.IsMatch(t, @"^(?:www\.)?[a-z0-9-]+(?:\.[a-z0-9-]+)*\.(?:com|org|net|in|io|dev|co|edu|gov|app|ai)(?:/\S*)?$", Opts)) return "https://" + t;

        // A path, a program name ("notepad", "chrome") or up to four words such as "visual studio code".
        return t;
    }

    /// <summary>What she says when there is no model and the request is not one of the basic ones.</summary>
    public static string OfflineHelp(string assistantName) =>
        $"I can't answer that yet: {assistantName} needs an AI model for questions and bigger jobs. Add a key, or a local model, in Settings, AI model (type /settings).\n\n" +
        "Until then I can still do these, just type them:\n" +
        "• Open things: “open notepad”, “open downloads”, “open example.com”, “open settings”\n" +
        "• Sound and screen: “volume up”, “volume down 10”, “mute”, “brightness 60”, “dark mode”, “take a screenshot”\n" +
        "• Windows: “minimize chrome”, “maximize notepad”, “snap chrome left”, “close notepad”, “lock my screen”\n" +
        "• Media: “pause”, “next”, “previous”\n" +
        "• Reminders and timers: “remind me in 10 minutes to stretch”, “timer for 25 minutes”, “show my reminders”\n" +
        "• Focus, notes and plans: /focus, /note, /plans, /routine\n" +
        "• Info: “what time is it”, “weather in Chennai”, “what's on my clipboard”, “why is my PC slow”, “search for pizza near me”\n" +
        "• Everything with a slash: type / to see them, for example /startup, /cleanup, /find, /backup and /theme.";
}
