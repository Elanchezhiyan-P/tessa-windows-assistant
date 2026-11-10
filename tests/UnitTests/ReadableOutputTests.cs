using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinCompanion.Services;
using WinCompanion.UI;
using Xunit;

namespace WinCompanion.UnitTests;

/// <summary>Replies that list things must stay short and scannable: names, not file paths and registry noise.</summary>
public class ReadableOutputTests
{
    private static StartupItem Item(string name, StartupSource source, bool enabled, string command = @"C:\Some\Very\Long\Path\To\The\Program.exe --with --many --arguments") =>
        new(name, command, source, enabled);

    [Theory]
    [InlineData("AnyDesk.lnk", "AnyDesk")]
    [InlineData("Send to OneNote.LNK", "Send to OneNote")]
    [InlineData("Spotify.url", "Spotify")]
    [InlineData("MicrosoftEdgeAutoLaunch_6B770857D9B81538FA9524CBB2D560C5", "MicrosoftEdgeAutoLaunch")]
    [InlineData("OneDrive", "OneDrive")]
    [InlineData("Microsoft.Lists", "Microsoft.Lists")]
    [InlineData("Docker Desktop", "Docker Desktop")]
    public void StartupNamesReadLikeAPersonWouldSayThem(string raw, string expected) => Assert.Equal(expected, StartupApps.DisplayName(raw));

    [Fact]
    public void ANameThatIsAllHexIsNotErasedToNothing() => Assert.Equal("ABCDEF0123456789ABCD", StartupApps.DisplayName("ABCDEF0123456789ABCD"));

    [Fact]
    public void TheStartupListIsGroupedByNameWithNoPathsOrArguments()
    {
        var text = StartupApps.Describe(
        [
            Item("Spotify", StartupSource.UserRun, enabled: true),
            Item("AnyDesk.lnk", StartupSource.CommonFolder, enabled: true),
            Item("iTunesHelper", StartupSource.MachineRun, enabled: true),
            Item("Docker Desktop", StartupSource.UserRun, enabled: false),
            Item("MicrosoftEdgeAutoLaunch_6B770857D9B81538FA9524CBB2D560C5", StartupSource.UserRun, enabled: false)
        ]);

        Assert.StartsWith("**Start with Windows:** 3 on, 2 off", text);
        Assert.True(text.IndexOf("**On**") < text.IndexOf("• Spotify") && text.IndexOf("• Spotify") < text.IndexOf("**Off**"));
        Assert.Contains("• AnyDesk · Startup folder", text);
        Assert.Contains("• iTunesHelper · all users", text);
        Assert.Contains("• Spotify\n", text); // a program that only starts for you needs no tag
        Assert.Contains("• MicrosoftEdgeAutoLaunch\n", text);
        Assert.DoesNotContain("C:\\", text);
        Assert.DoesNotContain("--arguments", text);
        Assert.EndsWith("/startup off <name> or /startup on <name>.", text);
        Assert.All(text.Split('\n').Where(l => l.StartsWith("• ")), l => Assert.True(l.Length < 40, l));
    }

    [Fact]
    public void AnEmptyGroupIsLeftOut()
    {
        var text = StartupApps.Describe([Item("Spotify", StartupSource.UserRun, enabled: true)]);
        Assert.Contains("**On**", text);
        Assert.DoesNotContain("**Off**", text);
    }

    [Fact]
    public void SearchResultsShowTheFileNotAFullPath()
    {
        var hits = new[]
        {
            new FileHit(@"D:\Docs\Bills\electricity-bill.txt", new DateTime(2026, 10, 31), "Invoice for **March** 2026.", false),
            new FileHit(@"D:\Docs\Warranty\washer.pdf", new DateTime(2026, 3, 2), "(the file name matches)", true)
        };

        var text = FileIndex.Describe(hits, "invoice");

        Assert.StartsWith("2 match(es) for \"invoice\":", text);
        Assert.Contains("**electricity-bill.txt** · 31 Oct 2026\nin D:\\Docs\\Bills\nInvoice for **March** 2026.", text);
        Assert.Contains("**washer.pdf** · 2 Mar 2026\nin D:\\Docs\\Warranty\n*the file name matches*", text);
        Assert.DoesNotContain("(the file name matches)", text);
        Assert.DoesNotContain("`", text);
    }

    [Fact]
    public void TheSlowCheckGivesEachCauseItsOwnBlock()
    {
        const long gb = 1024L * 1024 * 1024;
        var inputs = new SlowdownInputs(43, 96, [new("chrome", 30)], [new("chrome", 2 * gb)], [new DriveSpec("C:", 456 * gb, 30 * gb)], 12, TimeSpan.FromDays(9), 0);
        var text = SlowdownAnalysis.Describe(inputs, SlowdownAnalysis.Analyze(inputs));

        Assert.Contains("\n\n**⚠ Memory is 96% full.", text);
        Assert.Contains("**\n→ Close programs", text);
        // After the one-line summary, every finding is its own block: a bold cause, then its fix on the next line.
        Assert.All(text.Split("\n\n").Skip(1), block =>
        {
            Assert.StartsWith("**", block);
            Assert.Contains("\n→ ", block);
        });
        Assert.DoesNotContain("   →", text);
    }

    [Fact]
    public void TheCleanupListSkipsAreasWithNothingWorthMentioning()
    {
        var area = new CleanupArea("temp", "Temporary files", @"C:\x");
        var text = TempCleanup.Describe([new CleanupFound(area, 1, 0), new CleanupFound(new CleanupArea("dumps", "Crash dumps", @"C:\y"), 9, 140_000_000)]);
        Assert.Contains("Crash dumps", text);
        Assert.DoesNotContain("Temporary files", text);
        Assert.Contains("133.5 MB", text);
    }
}

// Builds WPF objects, so it runs with the other tests that do.
[Collection("wpf")]
public class MarkdownLayoutTests
{
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

    [Fact]
    public void TextAfterAListGetsSpaceAboveItAndItemsStayTight()
    {
        var (kinds, after, itemGap, headingAfterText, headingAfterList) = OnStaThread(() =>
        {
            var panel = MarkdownRenderer.Render("**On**\n• one\n• two\n\nSwitch one with /startup off.\n\n## Off\n• three", Brushes.White);
            var children = panel.Children.Cast<FrameworkElement>().ToList();

            var listThenHeading = MarkdownRenderer.Render("• a\n## Next", Brushes.White).Children.Cast<FrameworkElement>().ToList();
            return (children.Select(c => c.GetType().Name).ToList(),
                    children[3].Margin.Top,
                    children[1].Margin.Bottom,
                    children[4].Margin.Top,
                    listThenHeading[1].Margin.Top);
        });

        Assert.Equal(["TextBlock", "Grid", "Grid", "TextBlock", "TextBlock", "Grid"], kinds);
        Assert.Equal(8, after);             // the sentence after the list
        Assert.Equal(1, itemGap);           // items sit close together
        Assert.Equal(4, headingAfterText);  // a heading after a sentence needs no extra room
        Assert.Equal(10, headingAfterList); // but straight after a list it gets the most
    }
}
