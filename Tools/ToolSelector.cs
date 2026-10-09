using System.Text.RegularExpressions;

namespace WinCompanion.Tools;

/// <summary>
/// Picks the few tools that could matter for a request. Small local models read every tool definition on every request
/// (35 of them is ~3,800 tokens, which overflows Ollama's 4,096-token default and takes minutes on a CPU), so they
/// get a short, relevant list instead. Cloud models are fast enough to receive everything.
/// </summary>
internal static class ToolSelector
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;

    /// <summary>Always offered: the general-purpose escape hatches.</summary>
    private static readonly string[] Always = ["run_powershell", "open_target"];

    private static readonly (Regex When, string[] Tools)[] Groups =
    [
        (new(@"remind|alarm|snooze|timer", Opts),
            ["set_reminder", "list_reminders", "cancel_reminder"]),
        (new(@"screen|screenshot|look at|what.s (on|this)|see this|on my display", Opts),
            ["look_at_screen", "save_screenshot"]),
        (new(@"clipboard|copied|paste|rewrite|translate|summari[sz]e|grammar|polite|formal|casual|shorter|proofread", Opts),
            ["get_clipboard", "set_clipboard", "transform_clipboard"]),
        (new(@"window|minimi[sz]e|maximi[sz]e|snap|restore|switch to|bring .{0,20}front|focus|close (the )?(app|window)", Opts),
            ["list_windows", "window_action", "close_window"]),
        (new(@"cpu|\bram\b|memory|battery|disk|storage|space|wi-?fi|network|slow|performance|uptime|processes|system info", Opts),
            ["get_system_info"]),
        (new(@"volume|mute|louder|quieter|music|song|track|\bplay\b|pause|skip|\bnext\b|previous|media", Opts),
            ["change_volume", "media_control"]),
        (new(@"file|folder|director|download|find|recent|\bmove\b|rename|delete|recycle|\bread\b|create|organi[sz]e|\bsort\b", Opts),
            ["find_files", "list_recent_files", "read_text_file", "create_folder", "move_or_rename", "delete_to_recycle_bin", "organize_folder"]),
        (new(@"\bnotes?\b|jot", Opts),
            ["add_note", "read_notes"]),
        (new(@"remember|forget|memor(y|ies)|\bmy [\w ]{0,30}\b(is|are)\b|\bi('m| am| live| work| prefer| like| use)\b", Opts),
            ["remember", "forget", "list_memories"]),
        (new(@"weather|forecast|temperature|google|web search|look up|search the web", Opts),
            ["web_search", "web_lookup", "get_weather"]),
        (new(@"latest|news|who (is|was)|what (is|are|was) the|price of|how (much|many|old|tall)|current(ly)?|today.s|search for|find out|capital of|score", Opts),
            ["web_lookup"]),
        (new(@"dark mode|light mode|theme|brightness|sleep|shut ?down|restart|reboot|sign out|log ?off|\block\b", Opts),
            ["set_theme", "set_brightness", "power_action", "lock_screen"]),
        (new(@"\bkill\b|force quit|end task|not responding|stop (the )?process", Opts),
            ["kill_process"]),
    ];

    /// <param name="request">The user's message (and the one before it, so a short "yes, do it" still has context).</param>
    /// <param name="recentlyUsed">Tools used in the last few turns; a follow-up probably needs them again.</param>
    public static HashSet<string> Select(string request, IEnumerable<string> recentlyUsed)
    {
        var selected = new HashSet<string>(Always);
        foreach (var (when, tools) in Groups)
            if (when.IsMatch(request)) selected.UnionWith(tools);
        selected.UnionWith(recentlyUsed);
        return selected;
    }
}
