using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WinCompanion.Services;

public enum StartupSource { UserRun, MachineRun, MachineRun32, UserFolder, CommonFolder }

/// <param name="Command">The program (and arguments) that runs, or the shortcut's path for the Startup folders.</param>
public sealed record StartupItem(string Name, string Command, StartupSource Source, bool Enabled)
{
    public string Where => Source switch
    {
        StartupSource.UserRun => "your account",
        StartupSource.MachineRun or StartupSource.MachineRun32 => "all accounts",
        _ => "Startup folder"
    };

    /// <summary>The name as a person would say it: no ".lnk", no long hex suffix.</summary>
    public string Label => StartupApps.DisplayName(Name);

    /// <summary>A short note for the list, or empty for the ordinary case of a program that only starts for you.</summary>
    public string Tag => Source switch
    {
        StartupSource.MachineRun or StartupSource.MachineRun32 => " · all users",
        StartupSource.UserFolder or StartupSource.CommonFolder => " · Startup folder",
        _ => ""
    };
}

/// <summary>
/// The programs that start with Windows, and turning them on or off the way Task Manager does: a per-user "approved" flag. Nothing is
/// deleted or edited, and it needs no administrator rights, so an item that was switched off can always be switched back on.
/// </summary>
public sealed class StartupApps
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    /// <param name="RunHive">Where the list of programs is read: the registry hive, or null for a folder.</param>
    /// <param name="ApprovedSubKey">Under HKEY_CURRENT_USER, where the on/off flag for these items is kept.</param>
    public sealed record Source(StartupSource Kind, RegistryKey? RunHive, string? RunSubKey, string? Folder, string ApprovedSubKey);

    private readonly IReadOnlyList<Source> _sources;

    public StartupApps(IReadOnlyList<Source>? sources = null) => _sources = sources ?? DefaultSources();

    public static IReadOnlyList<Source> DefaultSources() =>
    [
        new(StartupSource.UserRun, Registry.CurrentUser, Run, null, Approved + @"\Run"),
        new(StartupSource.MachineRun, Registry.LocalMachine, Run, null, Approved + @"\Run"),
        new(StartupSource.MachineRun32, Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", null, Approved + @"\Run32"),
        new(StartupSource.UserFolder, null, null, Environment.GetFolderPath(Environment.SpecialFolder.Startup), Approved + @"\StartupFolder"),
        new(StartupSource.CommonFolder, null, null, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Approved + @"\StartupFolder")
    ];

    // ---- Reading ----------------------------------------------------------------------------------------

    public IReadOnlyList<StartupItem> List()
    {
        var items = new List<StartupItem>();
        foreach (var source in _sources)
        {
            using var flags = Registry.CurrentUser.OpenSubKey(source.ApprovedSubKey);
            foreach (var (name, command) in Entries(source))
                items.Add(new StartupItem(name, command, source.Kind, IsEnabled(flags?.GetValue(name) as byte[])));
        }
        return items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<(string Name, string Command)> Entries(Source source)
    {
        if (source.RunHive is not null && source.RunSubKey is not null)
        {
            RegistryKey? key = null;
            try { key = source.RunHive.OpenSubKey(source.RunSubKey); }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException) { }
            using (key)
            {
                foreach (var name in key?.GetValueNames() ?? [])
                    if (name.Length > 0 && key!.GetValue(name) is string command) yield return (name, command);
            }
        }
        else if (source.Folder is { Length: > 0 } folder && Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var name = Path.GetFileName(file);
                if (!name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) yield return (name, file);
            }
        }
    }

    /// <summary>The flag is 12 bytes. An even first byte (02, 06) means on and an odd one (03, 07) off; no flag at all means on.</summary>
    public static bool IsEnabled(byte[]? flag) => flag is not { Length: > 0 } || (flag[0] & 1) == 0;

    /// <summary>The flag Task Manager writes: 02 for on; 03 and the time it was switched off for off.</summary>
    public static byte[] ApprovalData(bool enabled, DateTime now)
    {
        var data = new byte[12];
        data[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled) BitConverter.GetBytes(now.ToFileTime()).CopyTo(data, 4);
        return data;
    }

    // ---- Changing ---------------------------------------------------------------------------------------

    /// <summary>Switches the one item matching <paramref name="name"/> on or off, and says what happened.</summary>
    /// <param name="only">Restrict to one place (the Settings list passes the row's own), when the same name is listed in more than one.</param>
    public string SetEnabled(string name, bool enabled, DateTime? now = null, StartupSource? only = null)
    {
        name = name.Trim();
        if (name.Length == 0) return "Say which startup app, for example \"Spotify\".";

        var all = List().Where(i => only is null || i.Source == only).ToList();
        var matches = all.Where(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0) matches = all.Where(i => i.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();

        if (matches.Count == 0) return $"No startup app matches \"{name}\". Try /startup to see the list.";
        if (matches.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            return $"\"{name}\" matches more than one: {string.Join(", ", matches.Select(m => m.Name).Distinct())}. Be more specific.";

        // The same program can be listed for your account and for all accounts: they are switched together.
        var item = matches[0];
        var changes = matches.Where(m => m.Enabled != enabled).ToList();
        if (changes.Count == 0) return $"{item.Name} already {(enabled ? "starts" : "doesn't start")} with Windows.";

        foreach (var change in changes)
        {
            var source = _sources.First(s => s.Kind == change.Source);
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(source.ApprovedSubKey);
                key.SetValue(change.Name, ApprovalData(enabled, now ?? DateTime.Now), RegistryValueKind.Binary);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                return $"I couldn't change {item.Name}: {ex.Message}";
            }
        }

        return enabled
            ? $"{item.Name} will start with Windows again."
            : $"{item.Name} won't start with Windows from the next sign-in. It isn't removed: /startup on \"{item.Name}\" brings it back.";
    }

    // ---- Text -------------------------------------------------------------------------------------------

    /// <summary>"AnyDesk.lnk" becomes "AnyDesk"; "MicrosoftEdgeAutoLaunch_6B77…" loses its long hex suffix.</summary>
    public static string DisplayName(string name)
    {
        var clean = Regex.Replace(name.Trim(), @"\.(lnk|url)$", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"[_\- ]?[0-9A-Fa-f]{16,}$", "").Trim();
        return clean.Length > 0 ? clean : name;
    }

    /// <summary>A short list in two groups (on, then off), by name only. The full commands stay out of the chat.</summary>
    public static string Describe(IReadOnlyList<StartupItem> items)
    {
        if (items.Count == 0) return "No programs are set to start with Windows.";

        var on = items.Where(i => i.Enabled).ToList();
        var off = items.Where(i => !i.Enabled).ToList();

        var sb = new StringBuilder($"**Start with Windows:** {on.Count} on, {off.Count} off");
        void Group(string title, List<StartupItem> group)
        {
            if (group.Count == 0) return;
            sb.Append("\n\n**").Append(title).Append("**");
            foreach (var item in group) sb.Append("\n• ").Append(item.Label).Append(item.Tag);
        }
        Group("On", on);
        Group("Off", off);
        sb.Append("\n\nSwitch one with /startup off <name> or /startup on <name>.");
        return sb.ToString();
    }
}
