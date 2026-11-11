using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WinCompanion.UI;

namespace WinCompanion.Services;

public sealed record BackupSummary(DateTime Created, string Version, int Settings, int Memories, int Routines, int Reminders, int NoteLines, int Plans = 0)
{
    public string Describe() =>
        $"Backup made {Created:g} by version {Version}: {Settings} settings, {Memories} things I remember, {Routines} routines, " +
        $"{Plans} plans, {Reminders} reminders and {NoteLines} note lines.";
}

public sealed record RestoreReport(int Settings, int Memories, int Routines, int Reminders, int NoteLines, IReadOnlyList<string> Skipped, int Plans = 0)
{
    public string Describe()
    {
        var text = $"Restored {Settings} settings, {Memories} memories, {Routines} routines, {Plans} plans, {Reminders} reminders and {NoteLines} note lines.";
        return Skipped.Count == 0 ? text : text + "\nLeft out: " + string.Join("; ", Skipped.Take(8)) + (Skipped.Count > 8 ? $"; and {Skipped.Count - 8} more." : ".");
    }
}

/// <summary>
/// One zip file holding the settings, memories, routines, reminders and notes, to move to a new PC. It never holds an API key,
/// chat history or clipboard history. Restoring only adds to what is there and never switches on a risky setting.
/// </summary>
public static class Backup
{
    private const string App = "tessa-backup";
    private const int Format = 1;
    private const int MaxEntryBytes = 4 * 1024 * 1024;
    private const int MaxItems = 2000;

    /// <summary>
    /// Settings that are never read back: they switch off a safety question or turn on something that records or runs things, and
    /// they should be chosen again on the new PC. The endpoint of a cloud service is also left out, so a file cannot redirect requests.
    /// </summary>
    public static readonly string[] NeverRestored =
    [
        nameof(AppSettings.PowerShellTrusted), nameof(AppSettings.PowerShellAdmin), nameof(AppSettings.AskBeforeSendingToCloud),
        nameof(AppSettings.ClipboardHistory), nameof(AppSettings.MicrophoneDevice), "OpenAiBaseUrl", nameof(AppSettings.BriefingLastShown)
    ];

    private static readonly Regex SafeName = new(@"^[\p{L}\p{N} _\-]{1,40}$", RegexOptions.CultureInvariant);

    // ---- Create -------------------------------------------------------------------------------------

    /// <summary>What <see cref="Inspect"/> and <see cref="Restore"/> report for a locked backup opened without its password.</summary>
    public const string PasswordRequired = "This backup is locked with a password.";

    private const long MaxBackupBytes = 64L * 1024 * 1024;

    /// <summary>True when the file is a backup locked with a password.</summary>
    public static bool IsLocked(string path) => BackupCrypto.IsEncryptedFile(path);

    /// <param name="password">Locks the file with it (AES-256, see <see cref="BackupCrypto"/>). Empty or null saves a plain zip.</param>
    public static string Create(string zipPath, AppSettings settings, MemoryStore memory, RoutineStore routines, ReminderStore reminders, string notesPath,
        PlanStore? plans = null, string? password = null, int iterations = BackupCrypto.DefaultIterations)
    {
        var temp = zipPath + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        try
        {
            using (var stream = File.Create(temp))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                Add(zip, "manifest.json", new JsonObject
                {
                    ["app"] = App,
                    ["format"] = Format,
                    ["version"] = AboutTessa.Version,
                    ["created"] = DateTime.Now.ToString("o")
                }.ToJsonString());

                var settingsNode = (JsonObject)JsonSerializer.SerializeToNode(settings)!;
                foreach (var key in NeverRestored) settingsNode.Remove(key);
                Add(zip, "settings.json", settingsNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

                Add(zip, "memory.json", new JsonArray(memory.List()
                    .Select(m => (JsonNode)new JsonObject { ["text"] = m.Text, ["added"] = m.Added.ToString("o") }).ToArray()).ToJsonString());

                Add(zip, "routines.json", new JsonArray(routines.List().Select(r => (JsonNode)new JsonObject
                {
                    ["name"] = r.Name,
                    ["phrase"] = r.Phrase,
                    ["steps"] = new JsonArray(r.Steps.Select(s => (JsonNode)new JsonObject { ["tool"] = s.Tool, ["args"] = s.Args.DeepClone() }).ToArray())
                }).ToArray()).ToJsonString());

                Add(zip, "reminders.json", new JsonArray(reminders.Pending()
                    .Where(r => !FocusService.IsFocusReminder(r.Message))
                    .Select(r => (JsonNode)new JsonObject { ["message"] = r.Message, ["due"] = r.Due.ToString("o"), ["repeat"] = r.Repeat }).ToArray()).ToJsonString());

                if (plans is not null)
                    Add(zip, "plans.json", new JsonArray(plans.List().Select(p => (JsonNode)new JsonObject
                    {
                        ["name"] = p.Name,
                        ["goal"] = p.Goal,
                        ["steps"] = new JsonArray(p.Steps.Select(s => (JsonNode)s).ToArray())
                    }).ToArray()).ToJsonString());

                if (File.Exists(notesPath)) Add(zip, "notes.md", File.ReadAllText(notesPath));
            }
            if (!string.IsNullOrEmpty(password))
                File.WriteAllBytes(temp, BackupCrypto.Encrypt(File.ReadAllBytes(temp), password, iterations));
            File.Move(temp, zipPath, overwrite: true);
        }
        finally { try { File.Delete(temp); } catch (IOException) { } }
        return zipPath;
    }

    private static void Add(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    // ---- Read ---------------------------------------------------------------------------------------

    private sealed record Contents(DateTime Created, string Version, JsonObject Settings, JsonArray Memories, JsonArray Routines, JsonArray Reminders, string Notes,
        JsonArray Plans);

    private static Contents? Read(string zipPath, out string error, string? password = null)
    {
        error = "";
        try
        {
            if (new FileInfo(zipPath).Length > MaxBackupBytes) { error = "That file is too big to be one of my backups."; return null; }
            var bytes = File.ReadAllBytes(zipPath);
            if (BackupCrypto.IsEncrypted(bytes))
            {
                if (string.IsNullOrEmpty(password)) { error = PasswordRequired; return null; }
                try { bytes = BackupCrypto.Decrypt(bytes, password); }
                catch (InvalidDataException ex) { error = ex.Message; return null; }
            }
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            if (zip.Entries.Count > 20) { error = "This does not look like a backup of mine."; return null; }

            var manifest = Parse(zip, "manifest.json") as JsonObject;
            if (manifest?["app"]?.GetValue<string>() != App) { error = "This is not a backup made by me."; return null; }
            if ((manifest["format"]?.GetValue<int>() ?? 0) > Format) { error = "This backup is from a newer version. Update me first."; return null; }

            DateTime.TryParse(manifest["created"]?.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var created);
            return new Contents(created, manifest["version"]?.GetValue<string>() ?? "?",
                Parse(zip, "settings.json") as JsonObject ?? new JsonObject(),
                Parse(zip, "memory.json") as JsonArray ?? [],
                Parse(zip, "routines.json") as JsonArray ?? [],
                Parse(zip, "reminders.json") as JsonArray ?? [],
                ReadText(zip, "notes.md") ?? "",
                Parse(zip, "plans.json") as JsonArray ?? []);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException or InvalidOperationException or FormatException or UnauthorizedAccessException)
        {
            error = "I couldn't read that file as a backup.";
            return null;
        }
    }

    private static string? ReadText(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        if (entry is null) return null;
        if (entry.Length > MaxEntryBytes) throw new InvalidDataException("entry too large");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var buffer = new char[MaxEntryBytes + 1];
        var n = reader.ReadBlock(buffer, 0, buffer.Length);
        if (n > MaxEntryBytes) throw new InvalidDataException("entry too large");
        return new string(buffer, 0, n);
    }

    private static JsonNode? Parse(ZipArchive zip, string name) => ReadText(zip, name) is { } text ? JsonNode.Parse(text) : null;

    public static BackupSummary? Inspect(string zipPath, out string error, string? password = null)
    {
        var contents = Read(zipPath, out error, password);
        if (contents is null) return null;
        return new BackupSummary(contents.Created, contents.Version, RestorableSettings(contents.Settings).Count,
            contents.Memories.Count, contents.Routines.Count, contents.Reminders.Count,
            contents.Notes.Split('\n').Count(l => l.Trim().Length > 0), contents.Plans.Count);
    }

    // ---- Restore ------------------------------------------------------------------------------------

    private static PropertyInfo[] SettingProperties { get; } = typeof(AppSettings).GetProperties()
        .Where(p => p.CanWrite && p.GetCustomAttribute<JsonIgnoreAttribute>() is null && !NeverRestored.Contains(p.Name)).ToArray();

    private static Dictionary<PropertyInfo, JsonNode> RestorableSettings(JsonObject saved)
    {
        var result = new Dictionary<PropertyInfo, JsonNode>();
        foreach (var property in SettingProperties)
            if (saved[property.Name] is { } node) result[property] = node;
        return result;
    }

    public static RestoreReport Restore(string zipPath, AppSettings settings, MemoryStore memory, RoutineStore routines,
        ReminderStore reminders, string notesPath, Func<string, bool>? toolExists = null, PlanStore? plans = null, string? password = null)
    {
        var contents = Read(zipPath, out var error, password) ?? throw new InvalidDataException(error);
        var skipped = new List<string>();

        // Settings: only known names, each checked.
        var applied = 0;
        foreach (var (property, node) in RestorableSettings(contents.Settings))
        {
            try
            {
                var value = JsonSerializer.Deserialize(node, property.PropertyType);
                if (!Accept(property.Name, ref value, skipped)) continue;
                property.SetValue(settings, value);
                applied++;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                skipped.Add($"setting {property.Name}");
            }
        }

        var memories = 0;
        foreach (var item in contents.Memories.Take(MaxItems).OfType<JsonObject>())
            if (item["text"]?.GetValue<string>() is { Length: > 0 } text && memory.Remember(text).StartsWith("Remembered", StringComparison.Ordinal)) memories++;

        var routineCount = 0;
        foreach (var item in contents.Routines.Take(MaxItems).OfType<JsonObject>())
        {
            var name = item["name"]?.GetValue<string>() ?? "";
            var steps = (item["steps"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Select(s => new RoutineStep(s["tool"]?.GetValue<string>() ?? "", s["args"] as JsonObject ?? new JsonObject()))
                .ToList();
            var result = routines.Save(name, item["phrase"]?.GetValue<string>(), steps, toolExists);
            if (result.StartsWith("Saved", StringComparison.Ordinal)) routineCount++;
            else skipped.Add($"routine {name} ({result.TrimEnd('.')})");
        }

        // Plans are added, never replaced: one with the same name that already exists is left as it is.
        var planCount = 0;
        if (plans is not null)
        {
            foreach (var item in contents.Plans.Take(MaxItems).OfType<JsonObject>())
            {
                var name = PlanStore.CleanName(item["name"]?.GetValue<string>());
                var steps = (item["steps"] as JsonArray ?? []).Select(s => s?.GetValue<string>()).OfType<string>().ToList();
                if (name.Length == 0 || plans.Get(name) is not null) continue;
                if (plans.Save(name, item["goal"]?.GetValue<string>() ?? "", steps).StartsWith("Saved", StringComparison.Ordinal)) planCount++;
                else skipped.Add($"plan {name}");
            }
        }

        var reminderCount = 0;
        var existing = reminders.Pending();
        foreach (var item in contents.Reminders.Take(MaxItems).OfType<JsonObject>())
        {
            var message = item["message"]?.GetValue<string>() ?? "";
            if (message.Length == 0 || FocusService.IsFocusReminder(message)) continue;
            if (!DateTime.TryParse(item["due"]?.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var due)) continue;
            var repeat = item["repeat"]?.GetValue<string>();
            var next = due;
            if (next <= DateTime.Now)
            {
                if (string.IsNullOrEmpty(repeat)) { skipped.Add($"reminder \"{Trim(message)}\" (already past)"); continue; }
                next = new Reminder(Guid.NewGuid(), message, due, due, repeat).NextAfter(DateTime.Now);
            }
            if (existing.Any(r => r.Message == message && r.Due == next)) continue;
            reminders.Add(message, next, string.IsNullOrEmpty(repeat) ? null : repeat);
            reminderCount++;
        }

        var noteLines = MergeNotes(contents.Notes, notesPath);
        return new RestoreReport(applied, memories, routineCount, reminderCount, noteLines, skipped, planCount);
    }

    private static string Trim(string text) => text.Length > 30 ? text[..30] + "…" : text;

    // Appends the note lines that are not already in the file.
    private static int MergeNotes(string backupNotes, string notesPath)
    {
        var lines = backupNotes.Replace("\r", "").Split('\n').Where(l => l.Trim().Length > 0).ToList();
        if (lines.Count == 0) return 0;
        var have = File.Exists(notesPath) ? File.ReadAllLines(notesPath).Select(l => l.Trim()).ToHashSet() : [];
        var add = lines.Where(l => !have.Contains(l.Trim())).ToList();
        if (add.Count == 0) return 0;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(notesPath))!);
        File.AppendAllText(notesPath, string.Join(Environment.NewLine, add) + Environment.NewLine);
        return add.Count;
    }

    /// <summary>Checks one restored value. Returns false (and says why) when it is not acceptable.</summary>
    private static bool Accept(string name, ref object? value, List<string> skipped)
    {
        switch (name)
        {
            case nameof(AppSettings.Provider):
                if (value is string p && AppSettings.Providers.Contains(p)) return true;
                skipped.Add("AI model choice");
                return false;
            case nameof(AppSettings.LocalBaseUrl):
                // Only a server on this PC is trusted from a file; any other address is entered again by hand.
                if (value is string url && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.IsLoopback && uri.Scheme is "http" or "https") return true;
                skipped.Add("local server address (not on this PC)");
                return false;
            case nameof(AppSettings.Pet):
                if (value is string pet && (pet == "none" || SafeName.IsMatch(pet))) return true;
                skipped.Add("character");
                return false;
            case nameof(AppSettings.Theme):
                if (value is string theme) { value = ThemeCatalog.Find(theme).Key; return true; }
                return false;
            case nameof(AppSettings.ColorMode):
                value = ThemeCatalog.NormalizeMode(value as string);
                return true;
            case nameof(AppSettings.InterfaceSize):
                value = ThemeCatalog.NormalizeSize(value is int size ? size : 100);
                return true;
            case nameof(AppSettings.VoiceRate):
                value = Math.Clamp(value is int rate ? rate : 0, -6, 6);
                return true;
            case nameof(AppSettings.QuietStart):
            case nameof(AppSettings.QuietEnd):
                if (value is string time && QuietHours.TryParseTime(time, out _)) return true;
                skipped.Add("quiet hours time");
                return false;
            case nameof(AppSettings.AccentColor):
                if (value is string accent && (accent.Length == 0 || ThemeCatalog.TryParseAccent(accent, out _))) return true;
                skipped.Add("accent colour");
                return false;
            case nameof(AppSettings.AssistantName):
            case nameof(AppSettings.UserName):
            case nameof(AppSettings.UserNickname):
                value = AppSettings.CleanName(value as string, name == nameof(AppSettings.AssistantName) ? "Tessa" : "");
                return true;
        }
        // Any other text is length-limited and must not hold control characters.
        if (value is string text)
        {
            if (text.Length > 400 || text.Any(char.IsControl)) { skipped.Add($"setting {name}"); return false; }
        }
        return true;
    }
}
