using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WinCompanion.Services;
using WinCompanion.UI;
using Xunit;

namespace WinCompanion.UnitTests;

public class SafeToOpenTests
{
    [Theory]
    [InlineData("a.pdf")]
    [InlineData("A.DOCX")]
    [InlineData("notes.txt")]
    [InlineData("page.html")]
    [InlineData("data.csv")]
    public void DocumentsMayBeOpened(string name) => Assert.True(DocumentText.IsSafeToOpen(@"C:\Docs\" + name));

    // These are readable and searchable, but opening one would run it.
    [Theory]
    [InlineData("run.bat")]
    [InlineData("setup.cmd")]
    [InlineData("script.ps1")]
    [InlineData("thing.js")]
    [InlineData("tool.py")]
    [InlineData("deploy.sh")]
    [InlineData("query.sql")]
    [InlineData("setup.exe")]
    [InlineData("shortcut.lnk")]
    [InlineData("noextension")]
    public void ScriptsAndProgramsAreNeverOpened(string name) => Assert.False(DocumentText.IsSafeToOpen(@"C:\Docs\" + name));

    [Fact]
    public void EveryTypeThatIsSearchableAsCodeIsRefused()
    {
        // The index reads these as text, so a search can find them: make sure none of them is on the "open" list.
        foreach (var ext in new[] { ".cs", ".py", ".js", ".ts", ".java", ".c", ".cpp", ".h", ".go", ".rs", ".sql", ".ps1", ".bat", ".sh", ".css" })
        {
            Assert.True(DocumentText.IsSupported("x" + ext), ext);
            Assert.False(DocumentText.IsSafeToOpen("x" + ext), ext);
        }
    }
}

/// <summary>Clicks the cards' real buttons and switches, without the running app.</summary>
[Collection("wpf")]
public class ResultCardsTests
{
    private static object Resource(string key) => key is "Switch" or "Chip" or "PrimaryButton" or "GhostButton" ? null! : Brushes.Gray;

    private static T OnStaThread<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() => { try { result = work(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("failed on the UI thread", error);
        return result;
    }

    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var deeper in All<T>(child)) yield return deeper;
        }
    }

    private static void Click(ButtonBase button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private static List<string> Lines(FrameworkElement card) => All<TextBlock>(card).Select(t => string.Concat(t.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text))).ToList();

    private static StartupItem Item(string name, bool on, StartupSource source = StartupSource.UserRun) => new(name, @"C:\x\" + name + ".exe", source, on);

    // ---- start-up list ----

    [Fact]
    public void StartupCardListsProgramsWithTheirSwitchesOnesThatAreOnFirst()
    {
        var (names, states, summary) = OnStaThread(() =>
        {
            var card = ResultCards.StartupList([Item("Zoom", false), Item("Spotify", true), Item("Docker Desktop", false), Item("AnyDesk.lnk", true, StartupSource.CommonFolder)],
                (item, on) => (on, ""), Resource);
            var boxes = All<CheckBox>(card).ToList();
            return (boxes.Select(b => (string)b.Content).ToList(), boxes.Select(b => b.IsChecked == true).ToList(), Lines(card));
        });

        Assert.Equal(["AnyDesk · Startup folder", "Spotify", "Docker Desktop", "Zoom"], names);
        Assert.Equal([true, true, false, false], states);
        Assert.Contains("2 on · 2 off", summary);
    }

    [Fact]
    public void ClickingASwitchCallsBackAndShowsTheRealResult()
    {
        var calls = new List<(string Name, bool Enabled)>();
        var (isOn, summary, footer) = OnStaThread(() =>
        {
            var card = ResultCards.StartupList([Item("Spotify", true), Item("Zoom", true)], (item, on) =>
            {
                calls.Add((item.Name, on));
                return (on, $"{item.Name} {(on ? "will" : "won't")} start with Windows.");
            }, Resource);
            var spotify = All<CheckBox>(card).First(b => (string)b.Content == "Spotify");
            spotify.IsChecked = false; // what the user's click does before the handler runs
            Click(spotify);
            return (spotify.IsChecked == true, Lines(card), Lines(card).Last());
        });

        Assert.Equal([("Spotify", false)], calls);
        Assert.False(isOn);
        Assert.Contains("1 on · 1 off", summary);
        Assert.Equal("Spotify won't start with Windows.", footer);
    }

    [Fact]
    public void AFailedChangeSnapsTheSwitchBack()
    {
        var (isOn, footer) = OnStaThread(() =>
        {
            // The change didn't work (for example the registry refused), so the program is still on.
            var card = ResultCards.StartupList([Item("Spotify", true)], (item, on) => (true, "I couldn't change Spotify: access denied."), Resource);
            var box = All<CheckBox>(card).Single();
            box.IsChecked = false;
            Click(box);
            return (box.IsChecked == true, Lines(card).Last());
        });

        Assert.True(isOn);
        Assert.Contains("access denied", footer);
    }

    // ---- search results ----

    private static FileHit Hit(string path, string snippet = "Invoice for **March** 2026.") => new(path, new DateTime(2026, 10, 31), snippet, false);

    [Fact]
    public void SearchCardShowsEachFileAndItsButtons()
    {
        var (buttons, lines) = OnStaThread(() =>
        {
            var card = ResultCards.SearchResults("invoice", [Hit(@"D:\Docs\Bills\electricity.pdf"), Hit(@"D:\Docs\Tools\fix.ps1")],
                _ => { }, _ => { }, DocumentText.IsSafeToOpen, Resource);
            return (All<Button>(card).Select(b => (string)b.Content).ToList(), Lines(card));
        });

        Assert.Equal(["Open", "Show in folder", "Show in folder"], buttons); // a script is shown but can only be revealed, never opened
        Assert.Contains(lines, l => l.StartsWith("2 match(es) for “invoice”"));
        Assert.Contains(lines, l => l.StartsWith("electricity.pdf") && l.Contains("31 Oct 2026"));
        Assert.Contains(lines, l => l.StartsWith("fix.ps1"));
        Assert.Contains(@"D:\Docs\Bills", lines);
    }

    [Fact]
    public void OpenAndShowInFolderPassTheRightFile()
    {
        var opened = new List<string>();
        var revealed = new List<string>();
        OnStaThread(() =>
        {
            var card = ResultCards.SearchResults("x", [Hit(@"D:\a\one.pdf"), Hit(@"D:\b\two.docx")], opened.Add, revealed.Add, _ => true, Resource);
            var buttons = All<Button>(card).ToList();
            Click(buttons.First(b => (string)b.Content == "Open"));                 // the first Open belongs to one.pdf
            Click(buttons.Where(b => (string)b.Content == "Show in folder").Last()); // the last Show belongs to two.docx
            return 0;
        });

        Assert.Equal([@"D:\a\one.pdf"], opened);
        Assert.Equal([@"D:\b\two.docx"], revealed);
    }

    [Fact]
    public void ANameOnlyMatchSaysSoInItalics()
    {
        var italic = OnStaThread(() =>
        {
            var card = ResultCards.SearchResults("washer", [new FileHit(@"D:\Docs\washer.pdf", DateTime.Now, "(the file name matches)", true)], _ => { }, _ => { }, _ => true, Resource);
            return All<TextBlock>(card).SelectMany(t => t.Inlines.OfType<System.Windows.Documents.Italic>()).Select(i => ((System.Windows.Documents.Run)i.Inlines.First()).Text).ToList();
        });
        Assert.Equal(["the file name matches"], italic);
    }

    // ---- junk files ----

    private static CleanupFound Found(string name, int files, long bytes) => new(new CleanupArea(name.ToLowerInvariant(), name, @"C:\x"), files, bytes);

    [Fact]
    public void CleanupNeedsASecondClickBeforeAnythingIsDeleted()
    {
        var ran = 0;
        var (afterFirst, button, status, visible) = OnStaThread(() =>
        {
            var card = ResultCards.Cleanup([Found("Crash dumps", 9, 140_000_000), Found("Temporary files", 0, 0)], () => { ran++; return Task.FromResult("Deleted 9 file(s) and freed 133.5 MB."); }, Resource);
            var clean = All<Button>(card).Single();
            Click(clean);
            var first = ran;
            var label = (string)clean.Content;
            Click(clean);
            return (first, label, Lines(card).Last(), clean.Visibility);
        });

        Assert.Equal(0, afterFirst);          // the first click only asks
        Assert.Contains("Click again", button);
        Assert.Equal(1, ran);                 // the second click does it, once
        Assert.Equal("Deleted 9 file(s) and freed 133.5 MB.", status);
        Assert.Equal(Visibility.Collapsed, visible);
    }

    [Fact]
    public void CleanupListsOnlyAreasWithSomethingInThem()
    {
        var (lines, label) = OnStaThread(() =>
        {
            var card = ResultCards.Cleanup([Found("Crash dumps", 9, 140_000_000), Found("Temporary files", 1, 0)], () => Task.FromResult(""), Resource);
            return (Lines(card), (string)All<Button>(card).Single().Content);
        });

        Assert.Contains(lines, l => l.StartsWith("Crash dumps: 133.5 MB in 9 file(s)"));
        Assert.DoesNotContain(lines, l => l.StartsWith("Temporary files"));
        Assert.Equal("Clean up 133.5 MB", label);
    }

    [Fact]
    public void WithNothingToCleanThereIsNoButton()
    {
        var buttons = OnStaThread(() => All<Button>(ResultCards.Cleanup([Found("Temporary files", 1, 0)], () => Task.FromResult(""), Resource)).Count());
        Assert.Equal(0, buttons);
    }
}

public class InlineTextTests
{
    [Fact]
    public void BoldMarksAreRemovedFromPlainText() =>
        Assert.Equal("• 10 programs start with Windows.\n→ Type /startup", WinCompanion.UI.InlineText.Plain("**• 10 programs start with Windows.**\n→ Type `/startup`"));

    [Fact]
    public void TextWithoutMarksIsUnchanged() => Assert.Equal("a * b", WinCompanion.UI.InlineText.Plain("a * b"));
}
