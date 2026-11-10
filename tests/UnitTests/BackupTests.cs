using System.IO;
using System.IO.Compression;
using System.Text.Json.Nodes;
using WinCompanion.Services;
using Xunit;

namespace WinCompanion.UnitTests;

public class BackupTests : DatabaseTestBase
{
    private readonly string _folder = Directory.CreateTempSubdirectory("tessa-backup").FullName;
    private string Zip => Path.Combine(_folder, "b.zip");
    private string Notes => Path.Combine(_folder, "notes.md");

    private static readonly RoutineStep OpenNotepad = new("open_app", new JsonObject { ["name"] = "notepad" });

    private (MemoryStore, RoutineStore, ReminderStore) Stores() => (new MemoryStore(), new RoutineStore(), new ReminderStore());

    private void MakeBackup(AppSettings settings)
    {
        var (memory, routines, reminders) = Stores();
        memory.Remember("My dog is called Biscuit");
        routines.Save("morning", "good morning", [OpenNotepad, new RoutineStep("fly_to_moon", new JsonObject())]);
        reminders.Add("call the bank", DateTime.Now.AddDays(2));
        reminders.Add("pay rent", DateTime.Now.AddDays(3), "weekly");
        reminders.Add(FocusService.Mark + "break", DateTime.Now.AddMinutes(20));
        File.WriteAllText(Notes, "- [ ] buy milk\n- idea: a robot\n");
        Backup.Create(Zip, settings, memory, routines, reminders, Notes);
    }

    private void ClearData()
    {
        Database.Path = Path.Combine(_folder, "fresh.db");
        File.Delete(Notes);
    }

    [Fact]
    public void RoundTripBringsBackTheDataOnAFreshPc()
    {
        MakeBackup(new AppSettings { UserName = "Sam", Theme = "forest", VoiceRate = 3, QuietHours = true });
        ClearData();

        var (memory, routines, reminders) = Stores();
        var restored = new AppSettings();
        var report = Backup.Restore(Zip, restored, memory, routines, reminders, Notes, _ => true);

        Assert.Equal("Sam", restored.UserName);
        Assert.Equal("forest", restored.Theme);
        Assert.Equal(3, restored.VoiceRate);
        Assert.True(restored.QuietHours);
        Assert.Single(memory.List());
        Assert.Equal("morning", routines.List().Single().Name);
        Assert.Equal(2, routines.List().Single().Steps.Count);
        Assert.Equal(2, reminders.Pending().Count);                // the focus-session reminder is not backed up
        Assert.Contains("buy milk", File.ReadAllText(Notes));
        Assert.Equal(2, report.NoteLines);
    }

    [Fact]
    public void RiskySettingsAndKeysAreNeverInTheFileOrRestored()
    {
        MakeBackup(new AppSettings { PowerShellTrusted = true, PowerShellAdmin = true, AskBeforeSendingToCloud = false, ClipboardHistory = true });

        using (var zip = ZipFile.OpenRead(Zip))
        {
            var text = new StreamReader(zip.GetEntry("settings.json")!.Open()).ReadToEnd();
            foreach (var name in Backup.NeverRestored) Assert.DoesNotContain($"\"{name}\"", text);
            Assert.DoesNotContain("apikey", string.Join(' ', zip.Entries.Select(e => e.Name)), StringComparison.OrdinalIgnoreCase);
        }

        // A hand-edited file that puts them back is ignored too.
        var tampered = Path.Combine(_folder, "t.zip");
        File.Copy(Zip, tampered);
        using (var zip = ZipFile.Open(tampered, ZipArchiveMode.Update))
        {
            zip.GetEntry("settings.json")!.Delete();
            using var w = new StreamWriter(zip.CreateEntry("settings.json").Open());
            w.Write("{\"PowerShellTrusted\":true,\"PowerShellAdmin\":true,\"AskBeforeSendingToCloud\":false,\"ClipboardHistory\":true,\"UserName\":\"Kim\"}");
        }
        var (memory, routines, reminders) = Stores();
        var fresh = new AppSettings();
        Backup.Restore(tampered, fresh, memory, routines, reminders, Notes);
        Assert.False(fresh.PowerShellTrusted);
        Assert.False(fresh.PowerShellAdmin);
        Assert.True(fresh.AskBeforeSendingToCloud);
        Assert.False(fresh.ClipboardHistory);
        Assert.Equal("Kim", fresh.UserName);
    }

    [Fact]
    public void ARestoredLocalServerMustBeOnThisPc()
    {
        MakeBackup(new AppSettings { LocalBaseUrl = "http://evil.example.com/v1", Provider = "local" });
        var (memory, routines, reminders) = Stores();
        var fresh = new AppSettings();
        var report = Backup.Restore(Zip, fresh, memory, routines, reminders, Notes);

        Assert.Equal("http://localhost:11434/v1", fresh.LocalBaseUrl);
        Assert.Contains(report.Skipped, s => s.Contains("local server"));
        Assert.Equal("local", fresh.Provider);
    }

    [Theory]
    [InlineData("http://localhost:1234/v1", true)]
    [InlineData("http://127.0.0.1:11434/v1", true)]
    [InlineData("https://api.example.com/v1", false)]
    [InlineData("file:///c:/x", false)]
    public void OnlyLoopbackAddressesAreTrustedFromAFile(string url, bool accepted)
    {
        MakeBackup(new AppSettings { LocalBaseUrl = url });
        var (memory, routines, reminders) = Stores();
        var fresh = new AppSettings();
        Backup.Restore(Zip, fresh, memory, routines, reminders, Notes);
        Assert.Equal(accepted, fresh.LocalBaseUrl == url);
    }

    [Fact]
    public void UnknownToolsInARoutineAreSkippedWithAReason()
    {
        MakeBackup(new AppSettings());
        ClearData();
        var (memory, routines, reminders) = Stores();
        var report = Backup.Restore(Zip, new AppSettings(), memory, routines, reminders, Notes, name => name == "open_app");
        Assert.Empty(routines.List());
        Assert.Contains(report.Skipped, s => s.Contains("fly_to_moon"));
    }

    [Fact]
    public void RestoringTwiceDoesNotDuplicate()
    {
        MakeBackup(new AppSettings());
        var (memory, routines, reminders) = Stores();
        Backup.Restore(Zip, new AppSettings(), memory, routines, reminders, Notes);
        var before = (memory.List().Count, reminders.Pending().Count, File.ReadAllLines(Notes).Length);
        Backup.Restore(Zip, new AppSettings(), memory, routines, reminders, Notes);
        Assert.Equal(before, (memory.List().Count, reminders.Pending().Count, File.ReadAllLines(Notes).Length));
    }

    [Fact]
    public void APastOneTimeReminderIsSkippedAndARepeatingOneMovesForward()
    {
        MakeBackup(new AppSettings());
        var old = Path.Combine(_folder, "old.zip");
        File.Copy(Zip, old);
        using (var zip = ZipFile.Open(old, ZipArchiveMode.Update))
        {
            zip.GetEntry("reminders.json")!.Delete();
            using var w = new StreamWriter(zip.CreateEntry("reminders.json").Open());
            w.Write($"[{{\"message\":\"gone\",\"due\":\"{DateTime.Now.AddDays(-9):o}\"}},{{\"message\":\"weekly\",\"due\":\"{DateTime.Now.AddDays(-9):o}\",\"repeat\":\"weekly\"}}]");
        }
        ClearData();
        var (memory, routines, reminders) = Stores();
        Backup.Restore(old, new AppSettings(), memory, routines, reminders, Notes);
        var pending = reminders.Pending();
        Assert.Equal("weekly", pending.Single().Message);
        Assert.True(pending.Single().Due > DateTime.Now);
    }

    [Theory]
    [InlineData("not a zip at all")]
    [InlineData("")]
    public void ANonBackupFileIsRefusedPolitely(string content)
    {
        File.WriteAllText(Zip, content);
        Assert.Null(Backup.Inspect(Zip, out var error));
        Assert.NotEmpty(error);
        var (memory, routines, reminders) = Stores();
        Assert.Throws<InvalidDataException>(() => Backup.Restore(Zip, new AppSettings(), memory, routines, reminders, Notes));
    }

    [Fact]
    public void AZipFromSomethingElseOrANewerFormatIsRefused()
    {
        using (var zip = ZipFile.Open(Zip, ZipArchiveMode.Create))
        {
            using var w = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            w.Write("{\"app\":\"something-else\",\"format\":1}");
        }
        Assert.Null(Backup.Inspect(Zip, out var error));
        Assert.Contains("not a backup", error);

        File.Delete(Zip);
        using (var zip = ZipFile.Open(Zip, ZipArchiveMode.Create))
        {
            using var w = new StreamWriter(zip.CreateEntry("manifest.json").Open());
            w.Write("{\"app\":\"tessa-backup\",\"format\":99}");
        }
        Assert.Null(Backup.Inspect(Zip, out error));
        Assert.Contains("newer", error);
    }

    [Fact]
    public void AnOversizedEntryIsRefusedInsteadOfReadIntoMemory()
    {
        MakeBackup(new AppSettings());
        using (var zip = ZipFile.Open(Zip, ZipArchiveMode.Update))
        {
            zip.GetEntry("notes.md")!.Delete();
            using var w = new StreamWriter(zip.CreateEntry("notes.md").Open());
            w.Write(new string('x', 5 * 1024 * 1024));
        }
        Assert.Null(Backup.Inspect(Zip, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void InspectDescribesWhatIsInside()
    {
        MakeBackup(new AppSettings());
        var summary = Backup.Inspect(Zip, out _)!;
        Assert.Equal(1, summary.Memories);
        Assert.Equal(1, summary.Routines);
        Assert.Equal(2, summary.Reminders);
        Assert.Equal(2, summary.NoteLines);
        Assert.Contains("1 routines", summary.Describe());
    }

    [Fact]
    public void BadValuesInSettingsAreCleanedOrDropped()
    {
        MakeBackup(new AppSettings());
        var odd = Path.Combine(_folder, "odd.zip");
        File.Copy(Zip, odd);
        using (var zip = ZipFile.Open(odd, ZipArchiveMode.Update))
        {
            zip.GetEntry("settings.json")!.Delete();
            using var w = new StreamWriter(zip.CreateEntry("settings.json").Open());
            w.Write("{\"Pet\":\"..\\\\..\\\\x\",\"Provider\":\"hacked\",\"VoiceRate\":99,\"InterfaceSize\":500,\"ColorMode\":\"neon\",\"QuietStart\":\"soon\",\"BriefingCity\":\"a\\u0007b\"}");
        }
        var (memory, routines, reminders) = Stores();
        var s = new AppSettings();
        Backup.Restore(odd, s, memory, routines, reminders, Notes);

        Assert.Equal("realcat", s.Pet);
        Assert.Equal("gemini", s.Provider);
        Assert.Equal(6, s.VoiceRate);
        Assert.Equal(130, s.InterfaceSize);
        Assert.Equal("dark", s.ColorMode);
        Assert.Equal("22:00", s.QuietStart);
        Assert.Equal("", s.BriefingCity);
    }
}
