using System.IO;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using WinCompanion.Services;
using WinCompanion.Tools;
using Xunit;

namespace WinCompanion.UnitTests;

/// <summary>Uses a throwaway key under HKEY_CURRENT_USER, never the real start-up list.</summary>
public sealed class StartupAppsTests : IDisposable
{
    private readonly string _root = @"Software\WinCompanionTests\" + Guid.NewGuid().ToString("N");
    private readonly string _folder = Directory.CreateTempSubdirectory("tessa-startup").FullName;

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    private StartupApps Apps(bool twoRunKeys = false)
    {
        var sources = new List<StartupApps.Source>
        {
            new(StartupSource.UserRun, Registry.CurrentUser, _root + @"\Run", null, _root + @"\Approved\Run")
        };
        // Like the real 32-bit list, this one keeps its own flags (the user and all-users lists share one flag key in Windows).
        if (twoRunKeys)
            sources.Add(new(StartupSource.MachineRun32, Registry.CurrentUser, _root + @"\MachineRun", null, _root + @"\Approved\Run32"));
        sources.Add(new(StartupSource.UserFolder, null, null, _folder, _root + @"\Approved\Folder"));
        return new StartupApps(sources);
    }

    private void AddRunValue(string key, string name, string command)
    {
        using var run = Registry.CurrentUser.CreateSubKey(_root + @"\" + key);
        run.SetValue(name, command);
    }

    private byte[]? Flag(string approved, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_root + @"\Approved\" + approved);
        return key?.GetValue(name) as byte[];
    }

    [Fact]
    public void FlagBytesFollowTaskManagersConvention()
    {
        Assert.True(StartupApps.IsEnabled(null));
        Assert.True(StartupApps.IsEnabled([2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.True(StartupApps.IsEnabled([6, 0, 0, 0]));
        Assert.False(StartupApps.IsEnabled([3, 0, 0, 0]));
        Assert.False(StartupApps.IsEnabled([7, 0, 0, 0]));

        var off = StartupApps.ApprovalData(false, new DateTime(2026, 10, 31, 12, 0, 0));
        Assert.Equal(12, off.Length);
        Assert.Equal(3, off[0]);
        Assert.Equal(new DateTime(2026, 10, 31, 12, 0, 0).ToFileTime(), BitConverter.ToInt64(off, 4));

        var on = StartupApps.ApprovalData(true, DateTime.Now);
        Assert.Equal(2, on[0]);
        Assert.All(on.Skip(1), b => Assert.Equal(0, b));
    }

    [Fact]
    public void ListsRunValuesAndStartupFolderFiles()
    {
        AddRunValue("Run", "Spotify", @"C:\Spotify\Spotify.exe --autostart");
        AddRunValue("Run", "OneDrive", @"C:\OneDrive\OneDrive.exe /background");
        File.WriteAllText(Path.Combine(_folder, "Notes.lnk"), "x");
        File.WriteAllText(Path.Combine(_folder, "desktop.ini"), "x");

        var items = Apps().List();
        Assert.Equal(["Notes.lnk", "OneDrive", "Spotify"], items.Select(i => i.Name));
        Assert.All(items, i => Assert.True(i.Enabled));
        Assert.Equal(StartupSource.UserFolder, items.First(i => i.Name == "Notes.lnk").Source);
        Assert.Contains("--autostart", items.First(i => i.Name == "Spotify").Command);
    }

    [Fact]
    public void SwitchingOffWritesTheFlagAndLeavesTheEntryAlone()
    {
        AddRunValue("Run", "Spotify", "spotify.exe");
        var apps = Apps();

        var reply = apps.SetEnabled("spotify", false);
        Assert.Contains("won't start", reply);
        Assert.Equal(3, Flag("Run", "Spotify")![0]);
        Assert.False(apps.List().Single().Enabled);

        using (var run = Registry.CurrentUser.OpenSubKey(_root + @"\Run"))
            Assert.Equal("spotify.exe", run!.GetValue("Spotify")); // never deleted

        Assert.Contains("start with Windows again", apps.SetEnabled("Spotify", true));
        Assert.Equal(2, Flag("Run", "Spotify")![0]);
        Assert.True(apps.List().Single().Enabled);
    }

    [Fact]
    public void ARepeatedChangeIsHarmless()
    {
        AddRunValue("Run", "Spotify", "spotify.exe");
        var apps = Apps();
        Assert.Contains("already starts", apps.SetEnabled("Spotify", true));
        apps.SetEnabled("Spotify", false);
        Assert.Contains("doesn't start", apps.SetEnabled("Spotify", false));
    }

    [Fact]
    public void RefusesAmbiguousOrUnknownNames()
    {
        AddRunValue("Run", "Adobe Updater", "a.exe");
        AddRunValue("Run", "Adobe Sync", "b.exe");
        var apps = Apps();

        Assert.Contains("more than one", apps.SetEnabled("adobe", false));
        Assert.True(apps.List().All(i => i.Enabled)); // nothing was changed
        Assert.Contains("No startup app matches", apps.SetEnabled("zzz", false));
        Assert.Contains("Say which", apps.SetEnabled("  ", false));
    }

    [Fact]
    public void AnExactNameWinsOverPartialMatches()
    {
        AddRunValue("Run", "Teams", "t.exe");
        AddRunValue("Run", "Teams Updater", "u.exe");
        Apps().SetEnabled("Teams", false);

        var items = Apps().List();
        Assert.False(items.Single(i => i.Name == "Teams").Enabled);
        Assert.True(items.Single(i => i.Name == "Teams Updater").Enabled);
    }

    [Fact]
    public void TheSameProgramListedTwiceIsSwitchedTogetherOrOneAtATime()
    {
        AddRunValue("Run", "Dropbox", "d.exe");
        AddRunValue("MachineRun", "Dropbox", "d.exe");
        var apps = Apps(twoRunKeys: true);

        apps.SetEnabled("Dropbox", false);
        Assert.All(apps.List(), i => Assert.False(i.Enabled));

        apps.SetEnabled("Dropbox", true, only: StartupSource.UserRun);
        var after = apps.List();
        Assert.True(after.Single(i => i.Source == StartupSource.UserRun).Enabled);
        Assert.False(after.Single(i => i.Source == StartupSource.MachineRun32).Enabled);
    }

    [Fact]
    public void ReadsTheRealListWithoutChangingAnything()
    {
        var before = new StartupApps().List();
        Assert.All(before, i => Assert.False(string.IsNullOrWhiteSpace(i.Name)));
        Assert.Equal(before.Count, new StartupApps().List().Count);
        Assert.Contains("start", StartupApps.Describe(before), StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class TempCleanupTests : IDisposable
{
    // Under the real temp folder, which is the only kind of place a cleanup is allowed to work in.
    private readonly string _dir = Directory.CreateTempSubdirectory("tessa-cleanup").FullName;
    private readonly List<string> _extra = new();

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
        foreach (var d in _extra) try { Directory.Delete(d, true); } catch (IOException) { }
    }

    private CleanupArea Area => new("temp", "Temporary files", _dir);

    private string File_(string relative, int ageDays, int bytes = 1000)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        var when = DateTime.Now.AddDays(-ageDays);
        File.SetLastWriteTime(path, when);
        File.SetCreationTime(path, when);
        return path;
    }

    [Fact]
    public void RemovesOldFilesAndKeepsRecentOnes()
    {
        var old1 = File_("a.tmp", 3);
        var old2 = File_(@"sub\deep\b.tmp", 10, 2500);
        var fresh = File_("today.tmp", 0);
        foreach (var folder in new[] { @"sub\deep", "sub" })
            Directory.SetCreationTime(Path.Combine(_dir, folder), DateTime.Now.AddDays(-10)); // made long ago, like the files

        var result = TempCleanup.Clean([Area]);

        Assert.Equal(2, result.Deleted);
        Assert.Equal(3500, result.Bytes);
        Assert.False(File.Exists(old1));
        Assert.False(File.Exists(old2));
        Assert.True(File.Exists(fresh)); // too new: a program may be using it
        Assert.False(Directory.Exists(Path.Combine(_dir, "sub"))); // the folders it left empty went too
        Assert.True(Directory.Exists(_dir));
    }

    [Fact]
    public void ScanReportsWithoutDeleting()
    {
        var old = File_("a.tmp", 5, 4000);
        File_("new.tmp", 0);

        var found = Assert.Single(TempCleanup.Scan([Area]));
        Assert.Equal(1, found.Files);
        Assert.Equal(4000, found.Bytes);
        Assert.True(File.Exists(old));
        Assert.Contains("Temporary files", TempCleanup.Describe([found]));
    }

    [Fact]
    public void LeavesAFileInUseAndCountsIt()
    {
        var busy = File_("busy.tmp", 4);
        File_("free.tmp", 4);
        using var hold = new FileStream(busy, FileMode.Open, FileAccess.Read, FileShare.None);

        var result = TempCleanup.Clean([Area]);

        Assert.Equal(1, result.Deleted);
        Assert.Equal(1, result.Failed);
        Assert.True(File.Exists(busy));
    }

    [Fact]
    public void RemovesReadOnlyFilesToo()
    {
        var path = File_("ro.tmp", 4);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        Assert.Equal(1, TempCleanup.Clean([Area]).Deleted);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void NeverFollowsALinkOutOfTheFolder()
    {
        var victim = Directory.CreateTempSubdirectory("tessa-victim").FullName;
        _extra.Add(victim);
        var precious = Path.Combine(victim, "precious.docx");
        File.WriteAllText(precious, "keep me");
        File.SetLastWriteTime(precious, DateTime.Now.AddDays(-30));
        File.SetCreationTime(precious, DateTime.Now.AddDays(-30));

        try { Directory.CreateSymbolicLink(Path.Combine(_dir, "link"), victim); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return; } // this account may not create links

        File_("old.tmp", 5);
        TempCleanup.Clean([Area]);

        Assert.True(File.Exists(precious));
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Program Files")]
    public void RefusesPlacesThatAreNotJunkFolders(string path) => Assert.False(TempCleanup.IsSafeRoot(path));

    [Fact]
    public void RefusesTheProfileItself() =>
        Assert.False(TempCleanup.IsSafeRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));

    [Fact]
    public void AnUnsafeAreaIsSkippedNotCleaned()
    {
        var unsafeArea = new CleanupArea("x", "Windows", @"C:\Windows");
        Assert.Equal(0, TempCleanup.Scan([unsafeArea]).Single().Files);
        Assert.Equal(0, TempCleanup.Clean([unsafeArea]).Deleted);
    }

    [Fact]
    public void TheDefaultAreasAreAllSafeAndDistinct()
    {
        var areas = TempCleanup.DefaultAreas();
        Assert.All(areas, a => Assert.True(TempCleanup.IsSafeRoot(a.Path), a.Path));
        Assert.Equal(areas.Count, areas.Select(a => a.Key).Distinct().Count());
    }

    [Fact]
    public void ChoosesAreasByName()
    {
        Assert.Equal(["temp", "cache"], TempCleanup.Pick(["Temp", " cache "]).Select(a => a.Key));
        Assert.Equal(TempCleanup.DefaultAreas().Count, TempCleanup.Pick(null).Count);
        Assert.Equal(TempCleanup.DefaultAreas().Count, TempCleanup.Pick([]).Count);
        Assert.Empty(TempCleanup.Pick(["windows"])); // unknown names choose nothing, never something else
    }

    [Fact]
    public void SaysSoWhenThereIsNothing() => Assert.Contains("Nothing to clean", TempCleanup.Describe([new CleanupFound(Area, 0, 0)]));
}

public class SlowdownAnalysisTests
{
    private const long Gb = 1024L * 1024 * 1024;

    private static SlowdownInputs Calm(
        double cpu = 10, int ram = 40, IReadOnlyList<ProcessLoad>? topCpu = null, IReadOnlyList<DriveSpec>? drives = null,
        int startup = 4, double uptimeDays = 1, long temp = 0) =>
        new(cpu, ram, topCpu ?? [new("chrome", 5)], [new("chrome", 2 * Gb), new("code", Gb)],
            drives ?? [new DriveSpec("C:", 500 * Gb, 200 * Gb)], startup, TimeSpan.FromDays(uptimeDays), temp);

    [Fact]
    public void NothingStandsOutOnAHealthyPc()
    {
        var found = Assert.Single(SlowdownAnalysis.Analyze(Calm()));
        Assert.Equal(1, found.Severity);
        Assert.Contains("Nothing stands out", found.Text);
    }

    [Fact]
    public void NamesAProgramHoggingTheProcessor()
    {
        var found = SlowdownAnalysis.Analyze(Calm(cpu: 92, topCpu: [new("handbrake", 70), new("chrome", 8)]));
        var top = found[0];
        Assert.Equal(3, top.Severity);
        Assert.Contains("handbrake", top.Text);
        Assert.Contains("70%", top.Text);
    }

    [Fact]
    public void FullMemoryNamesTheBiggestUsers()
    {
        var f = Assert.Single(SlowdownAnalysis.Analyze(Calm(ram: 95)));
        Assert.Equal(3, f.Severity);
        Assert.Contains("95%", f.Text);
        Assert.Contains("chrome", f.Text);
    }

    [Fact]
    public void ANearlyFullSystemDriveIsAProblem()
    {
        var found = SlowdownAnalysis.Analyze(Calm(drives: [new DriveSpec("C:", 456 * Gb, 8 * Gb), new DriveSpec("D:", 931 * Gb, 400 * Gb)]));
        var drive = Assert.Single(found);
        Assert.Equal(3, drive.Severity);
        Assert.Contains("C:", drive.Text);
        Assert.Contains("/cleanup", drive.Fix);
    }

    [Fact]
    public void ManyStartupProgramsLongUptimeAndJunkAreNoticed()
    {
        var found = SlowdownAnalysis.Analyze(Calm(startup: 18, uptimeDays: 12, temp: 3 * Gb));
        Assert.Contains(found, f => f.Text.Contains("18 programs start") && f.Severity == 3);
        Assert.Contains(found, f => f.Text.Contains("hasn't restarted for 12 days"));
        Assert.Contains(found, f => f.Text.Contains("3 GB of temporary files"));
    }

    [Fact]
    public void WorstProblemsComeFirstAndEachHasAFix()
    {
        var found = SlowdownAnalysis.Analyze(Calm(ram: 96, uptimeDays: 9, startup: 11));
        Assert.Equal(found.OrderByDescending(f => f.Severity).Select(f => f.Text), found.Select(f => f.Text));
        Assert.All(found.Where(f => f.Severity >= 2), f => Assert.False(string.IsNullOrWhiteSpace(f.Fix)));
    }

    [Fact]
    public void DescribeSummarisesTheMachineAndListsEveryFinding()
    {
        var inputs = Calm(cpu: 55, ram: 96);
        var text = SlowdownAnalysis.Describe(inputs, SlowdownAnalysis.Analyze(inputs));
        Assert.Contains("processor 55%", text);
        Assert.Contains("memory 96% used", text);
        Assert.Contains("→", text);
    }

    [Fact]
    public void FormatsUptime()
    {
        Assert.Equal("1 h 30 min", SlowdownAnalysis.FormatUptime(TimeSpan.FromMinutes(90)));
        Assert.Equal("3 day(s) 12 h", SlowdownAnalysis.FormatUptime(new TimeSpan(3, 12, 0, 0)));
    }

    [Fact]
    public void ARecentlyMadeEmptyFolderIsLeftAlone()
    {
        // A program may have just made it to use in a moment.
        var dir = Directory.CreateTempSubdirectory("tessa-cleanup-fresh").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "just-made"));
            TempCleanup.Clean([new CleanupArea("temp", "t", dir)]);
            Assert.True(Directory.Exists(Path.Combine(dir, "just-made")));
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class PcCareToolTests
{
    private static Dictionary<string, ITool> Tools() => PcCareTools.All().ToDictionary(t => t.Name);

    [Fact]
    public void AnythingThatChangesSomethingAsksFirst()
    {
        var tools = Tools();
        var none = new JsonObject();
        Assert.True(tools["set_startup_app"].NeedsConfirmation(none));
        Assert.True(tools["clean_temp_files"].NeedsConfirmation(none));
        Assert.False(tools["list_startup_apps"].NeedsConfirmation(none));
        Assert.False(tools["scan_temp_files"].NeedsConfirmation(none));
        Assert.False(tools["why_is_my_pc_slow"].NeedsConfirmation(none));
    }

    [Fact]
    public void TheApprovalCardSaysWhatWillHappen()
    {
        var tools = Tools();
        var off = tools["set_startup_app"].ConfirmationDetail(new JsonObject { ["name"] = "Spotify", ["enabled"] = false });
        Assert.Contains("Stop \"Spotify\" from starting", off);
        Assert.Contains("switch it back", off);

        var on = tools["set_startup_app"].ConfirmationDetail(new JsonObject { ["name"] = "Spotify", ["enabled"] = true });
        Assert.Contains("Let \"Spotify\" start", on);

        var clean = tools["clean_temp_files"].ConfirmationDetail(new JsonObject());
        Assert.True(clean!.Contains("Permanently delete") || clean.Contains("nothing to clean"), clean);
    }

    [Fact]
    public void ToolsWithoutADescriptionShowTheirArguments() =>
        Assert.Null(Tools()["scan_temp_files"].ConfirmationDetail(new JsonObject()));

    [Fact]
    public async Task ListingAndScanningWorkOnThisPc()
    {
        var tools = Tools();
        Assert.Contains("start", await tools["list_startup_apps"].ExecuteAsync(new JsonObject()), StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(await tools["scan_temp_files"].ExecuteAsync(new JsonObject())));
    }

    [Fact]
    public async Task TheSlowdownCheckProducesAReport()
    {
        var report = await Tools()["why_is_my_pc_slow"].ExecuteAsync(new JsonObject());
        Assert.Contains("Right now: processor", report);
        Assert.Contains("memory", report);
    }

    [Fact]
    public void LocalModelsAreOfferedTheRightToolsForTheRequest()
    {
        var care = ToolSelector.Select("my pc is so slow, can you clean up the temp files?", []);
        Assert.Contains("why_is_my_pc_slow", care);
        Assert.Contains("clean_temp_files", care);

        var startup = ToolSelector.Select("stop spotify from starting with windows", []);
        Assert.Contains("set_startup_app", startup);

        var docs = ToolSelector.Select("what does the contract pdf say about notice?", []);
        Assert.Contains("read_document", docs);
        Assert.Contains("search_my_files", docs);

        Assert.DoesNotContain("clean_temp_files", ToolSelector.Select("remind me to call mom", []));
    }

    [Fact]
    public void NewCommandsAreRegistered()
    {
        foreach (var name in new[] { "agent", "slow", "startup", "cleanup", "find", "index" })
            Assert.NotNull(SlashCommands.Find(name));
        Assert.True(SlashCommands.Find("agent")!.NeedsArgs);
        Assert.True(SlashCommands.Find("find")!.NeedsArgs);
    }
}
