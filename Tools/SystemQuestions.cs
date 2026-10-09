using System.Text.RegularExpressions;

namespace WinCompanion.Tools;

/// <summary>
/// Recognises read-only questions about this PC ("how much disk space is left?", "which apps use the most memory?").
/// Small local models are unreliable at choosing a tool, so these go straight to: model writes PowerShell, app runs it,
/// model explains the output.
/// </summary>
internal static class SystemQuestions
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static readonly Regex Asking = new(
        @"^\W*(what|what's|whats|how much|how many|how long|how big|which|who|is|are|do i|does|did|show|list|tell me|check|give me|display|find out|get)\b|\?\s*$",
        Opts);

    private static readonly Regex AboutThisPc = new(
        @"disk|drive|storage|free space|space (left|free|used)|\bram\b|memory|\bcpu\b|processor|\bgpu\b|graphics card|process(es)?\b|battery|ip address|\bip\b|network|wi-?fi|" +
        @"uptime|boot|installed|apps?\b|programs?\b|software|services?\b|\bports?\b|windows version|os version|hostname|computer name|user ?name|" +
        @"temperature|startup|folder size|file size|largest files|biggest files|environment variable|printers?|monitors?|resolution|bios|serial number|" +
        @"specs|hardware|updates|firewall|bluetooth|usb|devices?\b|drivers?\b|event log|mac address|dns|gateway",
        Opts);

    // A request to *change* something is for the tools (with their approval rules), not for a quick read-only lookup.
    private static readonly Regex Action = new(
        @"\b(open|close|delete|remove|move|rename|install|uninstall|kill|stop|restart|shut ?down|turn|change|enable|disable|create|launch|" +
        @"remind|save|screenshot|play|pause|mute|lock|sleep|copy|paste|translate|rewrite|summari[sz]e|snap|minimi[sz]e|maximi[sz]e)\b",
        Opts);

    public static bool Matches(string request) =>
        request.Length < 300 && Asking.IsMatch(request) && AboutThisPc.IsMatch(request) && !Action.IsMatch(request);
}
