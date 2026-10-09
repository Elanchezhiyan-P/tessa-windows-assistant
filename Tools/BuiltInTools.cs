using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using WinCompanion.Services;

namespace WinCompanion.Tools;

/// <summary>A tool defined by a delegate, so each automation stays a few lines.</summary>
internal sealed class DelegateTool : ITool
{
    private readonly Func<JsonObject, Task<string>> _run;
    private readonly Func<JsonObject, bool> _confirmWhen;

    public DelegateTool(string name, string description, JsonObject? properties, string[] required,
        bool needsConfirmation, Func<JsonObject, Task<string>> run, Func<JsonObject, bool>? confirmWhen = null)
    {
        Name = name; Description = description; Properties = properties; Required = required;
        _confirmWhen = confirmWhen ?? (_ => needsConfirmation); _run = run;
    }

    public string Name { get; }
    public string Description { get; }
    public JsonObject? Properties { get; }
    public string[] Required { get; }
    public bool NeedsConfirmation(JsonObject args) => _confirmWhen(args);
    public Task<string> ExecuteAsync(JsonObject args) => _run(args);
}

internal static class BuiltInTools
{
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    private const byte VkVolumeMute = 0xAD, VkVolumeDown = 0xAE, VkVolumeUp = 0xAF;
    private const uint KeyUp = 0x0002;

    private static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };
    private static JsonObject Int(string description) => new() { ["type"] = "integer", ["description"] = description };

    public static IEnumerable<ITool> All(ToolContext ctx, MemoryStore memory, AppSettings settings, ReminderStore reminders) => Core()
        .Concat(WindowTools.All())
        .Concat(SystemTools.All())
        .Concat(ScreenTools.All(ctx))
        .Concat(NotesTools.All())
        .Concat(TextTools.All(ctx))
        .Concat(MemoryTools.All(memory))
        .Concat(EverydayTools.All(ctx))
        .Concat(PowerShellTool.All(settings))
        .Concat(ReminderTools.All(reminders));

    /// <summary>Finds an installed app by (part of) its name in the Start menu list. Null when nothing matches.</summary>
    private static (string Name, string Id)? FindStartApp(string name)
    {
        var clean = System.Text.RegularExpressions.Regex.Replace(name, @"[^\p{L}\p{N} .\-]", "").Trim();
        if (clean.Length == 0) return null;
        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };
        foreach (var a in new[]
        {
            "-NoProfile", "-NonInteractive", "-Command",
            $"Get-StartApps | Where-Object {{ $_.Name -like '*{clean}*' }} | Sort-Object {{ $_.Name.Length }} | Select-Object -First 1 | ForEach-Object {{ $_.Name + '|' + $_.AppID }}"
        }) psi.ArgumentList.Add(a);
        try
        {
            using var process = Process.Start(psi)!;
            var line = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(15000)) { process.Kill(true); return null; }
            var parts = line.Split('|', 2);
            return parts.Length == 2 && parts[1].Length > 0 ? (parts[0], parts[1]) : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { return null; }
    }

    private static IEnumerable<ITool> Core()
    {
        yield return new DelegateTool("open_target",
            "Open an app, file, folder, URL or Windows settings page, e.g. 'notepad', 'https://example.com', 'ms-settings:bluetooth'.",
            new JsonObject { ["target"] = Str("App name, path or URL to open.") }, ["target"], false,
            async args =>
            {
                var target = args["target"]!.GetValue<string>().Trim();
                try
                {
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                    return $"Opened {target}.";
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Not a program on the PATH, a file or a URL: look for an installed app by name (Store apps, Spotify, WhatsApp...).
                    var app = await Task.Run(() => FindStartApp(target));
                    if (app is null) return $"I couldn't find an app, file or address called '{target}'.";
                    Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + app.Value.Id) { UseShellExecute = true });
                    return $"Opened {app.Value.Name}.";
                }
            });

        yield return new DelegateTool("get_clipboard", "Read the current text on the clipboard.", null, [], false,
            _ => Task.FromResult(System.Windows.Clipboard.ContainsText()
                ? System.Windows.Clipboard.GetText()
                : "(clipboard has no text)"));

        yield return new DelegateTool("set_clipboard", "Replace the clipboard text, e.g. with a rewritten or translated version.",
            new JsonObject { ["text"] = Str("Text to place on the clipboard.") }, ["text"], false,
            args =>
            {
                System.Windows.Clipboard.SetText(args["text"]!.GetValue<string>());
                return Task.FromResult("Clipboard updated.");
            });

        yield return new DelegateTool("change_volume",
            "Change system volume. Each step is about 2%.",
            new JsonObject
            {
                ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("up", "down", "mute_toggle") },
                ["steps"] = Int("How many steps for up/down (default 5).")
            }, ["action"], false,
            args =>
            {
                var action = args["action"]!.GetValue<string>();
                var steps = Math.Clamp(args["steps"]?.GetValue<int>() ?? 5, 1, 50);
                var key = action switch { "up" => VkVolumeUp, "down" => VkVolumeDown, _ => VkVolumeMute };
                for (var i = 0; i < (action == "mute_toggle" ? 1 : steps); i++)
                {
                    keybd_event(key, 0, 0, UIntPtr.Zero);
                    keybd_event(key, 0, KeyUp, UIntPtr.Zero);
                }
                return Task.FromResult($"Volume {action} done.");
            });

        yield return new DelegateTool("lock_screen", "Lock the Windows session.", null, [], true,
            _ =>
            {
                Process.Start("rundll32.exe", "user32.dll,LockWorkStation");
                return Task.FromResult("Locked.");
            });

        yield return new DelegateTool("find_files",
            "Search for files by name pattern under a folder. Returns up to 25 matches.",
            new JsonObject
            {
                ["folder"] = Str("Folder to search, e.g. 'C:\\Users\\me\\Documents'. Use 'Downloads' or 'Documents' or 'Desktop' as shortcuts."),
                ["pattern"] = Str("Wildcard pattern such as '*invoice*.pdf'.")
            }, ["folder", "pattern"], false,
            args =>
            {
                var folder = ResolveFolder(args["folder"]!.GetValue<string>());
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
                var hits = Directory.EnumerateFiles(folder, args["pattern"]!.GetValue<string>(), options).Take(25).ToList();
                return Task.FromResult(hits.Count == 0 ? "No matches." : string.Join('\n', hits));
            });

        yield return new DelegateTool("organize_folder",
            "Sort the files directly inside a folder into subfolders by type (Images, Documents, Videos, Audio, Archives, Installers, Other). Files are moved, never deleted.",
            new JsonObject { ["folder"] = Str("Folder to organize, e.g. 'Downloads'.") }, ["folder"], true,
            args =>
            {
                var folder = ResolveFolder(args["folder"]!.GetValue<string>());
                var moved = 0;
                foreach (var file in Directory.GetFiles(folder))
                {
                    var dest = Path.Combine(folder, CategoryFor(Path.GetExtension(file)));
                    Directory.CreateDirectory(dest);
                    var target = Path.Combine(dest, Path.GetFileName(file));
                    if (File.Exists(target)) continue; // never overwrite
                    File.Move(file, target);
                    moved++;
                }
                return Task.FromResult($"Moved {moved} file(s) in {folder}.");
            });

    }

    /// <summary>
    /// Turns what the model typed into a real absolute path: known-folder names ("Desktop", "Downloads\\x"),
    /// "~", environment variables, and plain paths. A relative path is placed under the user's profile,
    /// never under the app's own folder.
    /// </summary>
    internal static string ResolveFolder(string input)
    {
        var path = Environment.ExpandEnvironmentVariables(input.Trim().Trim('"'));
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path == "~" || path.StartsWith("~\\") || path.StartsWith("~/")) path = profile + path[1..];

        var separator = path.IndexOfAny(['\\', '/']);
        var first = separator < 0 ? path : path[..separator];
        var known = first.ToLowerInvariant() switch
        {
            "downloads" => Path.Combine(profile, "Downloads"),
            "documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "desktop" => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            _ => null
        };

        if (known is not null)
            path = separator < 0 ? known : Path.Combine(known, path[(separator + 1)..].TrimStart('\\', '/'));
        else if (!Path.IsPathRooted(path))
            path = Path.Combine(profile, path);

        return Path.GetFullPath(path);
    }

    private static string CategoryFor(string ext) => ext.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".svg" => "Images",
        ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".csv" => "Documents",
        ".mp4" or ".mkv" or ".avi" or ".mov" => "Videos",
        ".mp3" or ".wav" or ".flac" or ".m4a" => "Audio",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "Archives",
        ".exe" or ".msi" => "Installers",
        _ => "Other"
    };
}
