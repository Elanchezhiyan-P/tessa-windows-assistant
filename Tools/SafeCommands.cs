namespace WinCompanion.Tools;

/// <summary>
/// Decides whether a PowerShell command is read-only enough to run without asking.
/// Deliberately strict: anything it can't prove harmless goes to the user for approval.
/// </summary>
internal static class SafeCommands
{
    private static readonly HashSet<string> Cmdlets = new(StringComparer.OrdinalIgnoreCase)
    {
        "Get-Date", "Get-Process", "Get-Service", "Get-ChildItem", "Get-Item", "Get-Location", "Get-Command",
        "Get-ComputerInfo", "Get-NetIPAddress", "Get-NetAdapter", "Get-Volume", "Get-PSDrive", "Get-CimInstance",
        "Test-Path", "Select-Object", "Where-Object", "Sort-Object", "Measure-Object",
        "Format-Table", "Format-List", "Out-String"
    };

    // Native tools that are only harmless with no arguments (ipconfig /release changes the network).
    private static readonly HashSet<string> BareNatives = new(StringComparer.OrdinalIgnoreCase)
    {
        "hostname", "whoami", "ipconfig", "systeminfo"
    };

    // Statement separators, redirection, sub-expressions and script blocks can all smuggle in a second command.
    private static readonly char[] Forbidden = [';', '&', '>', '<', '`', '$', '(', ')', '{', '}', '\r', '\n'];

    // Things that delete, overwrite, reconfigure the system, download-and-run, install software or end processes.
    // Used in "trusted" mode, where everything else runs without asking. A pattern list can't be complete,
    // so it is a safety net, not a sandbox.
    private static readonly System.Text.RegularExpressions.Regex Dangerous = new(
        @"\b(Remove|Clear|Disable|Uninstall|Stop|Restart|Reset|Format|Initialize)-\w+" +
        @"|\b(rm|ri|del|erase|rd|rmdir|taskkill|shutdown|format|diskpart|bcdedit|cipher|vssadmin|wevtutil)\b" +
        @"|\b(Set-ExecutionPolicy|Invoke-Expression|iex|Invoke-WebRequest|iwr|Invoke-RestMethod|irm|curl|wget|Start-BitsTransfer)\b" +
        @"|DownloadString|DownloadFile|-EncodedCommand|-enc\b|FromBase64String" +
        @"|\b(winget|choco|scoop|msiexec|pip|npm)\s+(install|uninstall|remove|upgrade|update)\b" +
        @"|\bSet-Content\b|\bOut-File\b|\s>>?\s?|\s-Force\b|-Recurse\b.*\b(Move|Copy)-Item" +
        @"|\breg(\.exe)?\s+(add|delete|import|load)\b|\bnetsh\b|\bnet\s+(user|localgroup|stop|use)\b|\bsc(\.exe)?\s+(config|delete|stop)\b" +
        @"|HKLM|HKEY_LOCAL_MACHINE|-Verb\s+RunAs|\bNew-LocalUser\b|\bAdd-LocalGroupMember\b|\b(Set|Add)-MpPreference\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <param name="trusted">When true, only commands matching a dangerous pattern ask first.</param>
    public static bool RequiresApproval(string command, bool trusted)
    {
        if (IsPowerShellSafe(command)) return false; // read-only: never worth asking about
        return !trusted || Dangerous.IsMatch(command);
    }

    // The one script block we recognise as harmless: a calculated column that only rounds a property into GB/MB/KB,
    // as in  Select-Object Name, @{n='FreeGB';e={[math]::Round($_.Free/1GB,1)}}.  It must match exactly; anything
    // extra inside the braces (a second command, a different method) doesn't match and so still needs approval.
    private static readonly System.Text.RegularExpressions.Regex SafeRounding = new(
        @"@\{\s*(?:n|name|l|label)\s*=\s*'[\w ]{1,30}'\s*;\s*(?:e|expression)\s*=\s*\{\s*" +
        @"\[math\]::Round\(\s*\$_\.\w{1,40}\s*/\s*1(?:GB|MB|KB)\s*(?:,\s*\d\s*)?\)\s*\}\s*\}",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    public static bool IsPowerShellSafe(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        command = SafeRounding.Replace(command, "calculated");
        if (command.IndexOfAny(Forbidden) >= 0) return false;
        if (command.Contains(@"\\")) return false; // UNC paths would make Windows send credentials to another machine

        foreach (var segment in command.Split('|'))
        {
            var pieces = segment.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length == 0) return false;

            var rest = pieces.Length > 1 ? pieces[1].Trim() : "";
            if (BareNatives.Contains(pieces[0]))
            {
                if (rest.Length != 0 && !rest.Equals("/all", StringComparison.OrdinalIgnoreCase)) return false;
            }
            else if (!Cmdlets.Contains(pieces[0]))
            {
                return false;
            }
        }
        return true;
    }
}
