using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using WinCompanion.Services;
using Xunit;

namespace WinCompanion.UnitTests;

/// <summary>Regression tests for problems an independent code review found.</summary>
public class TempCleanupSafetyTests
{
    private static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // The review's scenario: TEMP set to a folder of real data inside the profile used to be accepted and wiped.
    [Theory]
    [InlineData("Documents")]
    [InlineData("Downloads")]
    [InlineData("Desktop")]
    [InlineData("Pictures")]
    [InlineData(@"Documents\Work")]
    [InlineData("OneDrive")]
    public void NothingInTheProfileThatIsNotThrowawayIsEverASafeRoot(string folder) =>
        Assert.False(TempCleanup.IsSafeRoot(Path.Combine(Profile, folder)), folder);

    [Fact]
    public void TheRealJunkFoldersAreStillAccepted()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.True(TempCleanup.IsSafeRoot(Path.Combine(local, "Microsoft", "Windows", "INetCache")));
        Assert.True(TempCleanup.IsSafeRoot(Path.Combine(local, "CrashDumps")));
        Assert.True(TempCleanup.IsSafeRoot(Path.GetTempPath()));
        Assert.False(TempCleanup.IsSafeRoot(local)); // the whole of LocalAppData is not junk
    }

    [Fact]
    public void ADotDotCannotEscapeAFolderThatIsAllowed()
    {
        var sneaky = Path.Combine(Path.GetTempPath(), "..", "..", "..", "Documents");
        Assert.False(TempCleanup.IsSafeRoot(sneaky));
    }
}

public class IndexFolderSafetyTests : DatabaseTestBase
{
    private static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void TheAppDataFolderItselfAndItsSiblingsAreRefused()
    {
        foreach (var folder in new[] { "AppData", @"AppData\Roaming", @"AppData\Local", @"AppData\LocalLow" })
        {
            var path = Path.Combine(Profile, folder);
            if (!Directory.Exists(path)) continue; // not every machine has every one
            Assert.False(FileIndex.IsAllowedFolder(path, out var reason), folder);
            Assert.Contains("app-data", reason);
        }
    }

    [Fact]
    public void AFolderInsideAppDataIsRefusedWhateverItIsCalled()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var inside = Directory.CreateDirectory(Path.Combine(local, "tessa-index-refuse-" + Guid.NewGuid().ToString("N"))).FullName;
        try { Assert.False(FileIndex.IsAllowedFolder(inside, out _)); }
        finally { Directory.Delete(inside); }
    }

    [Fact]
    public void ShortNamesCannotHideASystemFolder()
    {
        var dir = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "PROGRA~1-" + Guid.NewGuid().ToString("N")[..4])).FullName;
        var shortName = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "ABCDEF~1")).FullName;
        try
        {
            Assert.False(FileIndex.IsAllowedFolder(shortName, out var reason));
            Assert.Contains("short name", reason);
            Assert.True(FileIndex.IsAllowedFolder(dir, out _)); // a long name that merely contains a tilde is fine
        }
        finally { Directory.Delete(dir); Directory.Delete(shortName); }
    }

    [Fact]
    public void DocumentsOfAFolderThatWasRemovedNeverShowUpInASearch()
    {
        var folder = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "tessa-orphan-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllText(Path.Combine(folder, "secret-plan.txt"), "the launch codes are tangerine");
            Assert.StartsWith("Added", FileIndex.AddFolder(folder));
            FileIndex.Update();
            Assert.NotEmpty(FileIndex.Search("tangerine"));

            // The folder leaves the list while rows for its files are still in the tables (what a running update could leave behind).
            using (var db = Database.Open()) Database.Execute(db, "DELETE FROM index_folders");

            Assert.Empty(FileIndex.Search("tangerine"));      // by content
            Assert.Empty(FileIndex.Search("secret plan"));    // and by file name
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void AnUpdateThatWasAlreadyRunningCannotRefillARemovedFolder()
    {
        var folder = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "tessa-refill-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            for (var i = 0; i < 5; i++) File.WriteAllText(Path.Combine(folder, $"note{i}.txt"), "kiwi smoothie recipe " + i);
            FileIndex.AddFolder(folder);

            // Remove the folder after the update has listed its files but before it stores them: here, by cancelling nothing and removing first.
            using var stop = new CancellationTokenSource();
            var progress = new Progress<string>(_ => { });
            var first = true;
            var reporter = new SyncProgress(message =>
            {
                if (first) { first = false; FileIndex.RemoveFolder(folder); } // happens while the first file is being read
            });
            FileIndex.Update(reporter, stop.Token);

            Assert.Empty(FileIndex.Search("kiwi"));
            Assert.Equal(0, FileIndex.Stats().Files);
        }
        finally { Directory.Delete(folder, true); }
    }

    private sealed class SyncProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}

public class DocumentReaderSafetyTests : IDisposable
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "tessa-safety-" + Guid.NewGuid().ToString("N"))).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string Zip(string name, params (string Entry, string Xml)[] parts)
    {
        var path = Path.Combine(_dir, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, xml) in parts)
        {
            using var writer = new StreamWriter(archive.CreateEntry(entry).Open(), new UTF8Encoding(false));
            writer.Write(xml);
        }
        return path;
    }

    private static string Slide(string text) =>
        "<p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
        $"<p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>{text}</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:sld>";

    [Fact]
    public void AHugeNumberInASlideNameDoesNotCrashTheReader()
    {
        // int.Parse on this overflowed and aborted indexing at that file for good.
        var path = Zip("crafted.pptx", ("ppt/slides/slide99999999999999.xml", Slide("ignored")), ("ppt/slides/slide1.xml", Slide("Real slide")));
        var text = DocumentText.Extract(path, out var problem);
        Assert.Null(problem);
        Assert.Contains("Real slide", text);
        Assert.DoesNotContain("ignored", text);
    }

    [Fact]
    public void ManySlidesStopAtTheTextLimitInsteadOfFillingMemory()
    {
        var big = new string('x', 100_000);
        var parts = Enumerable.Range(1, 60).Select(i => ($"ppt/slides/slide{i}.xml", Slide(big))).ToArray();
        var text = DocumentText.Extract(Zip("many.pptx", parts), out _);
        Assert.NotNull(text);
        Assert.True(text!.Length <= DocumentText.MaxChars, $"{text.Length} characters");
    }

    [Fact]
    public void APageBuiltToSlowThePatternsStillReadsQuickly()
    {
        // Tens of thousands of unclosed <script tags make a backtracking pattern quadratic: it must time out and fall back.
        var html = string.Concat(Enumerable.Repeat("<script ", 40_000)) + "> visible words after the traps";
        var path = Path.Combine(_dir, "trap.html");
        File.WriteAllText(path, html);

        var clock = Stopwatch.StartNew();
        var text = DocumentText.Extract(path, out var problem);
        clock.Stop();

        Assert.Null(problem);
        Assert.Contains("visible words after the traps", text);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), $"took {clock.Elapsed}");
    }

    [Fact]
    public void StripTagsRemovesTagsInOnePass()
    {
        Assert.Equal(" Hello   world ", DocumentText.StripTags("<p>Hello</p> <b>world</b>"));
        Assert.DoesNotContain("<", DocumentText.StripTags("<a href=\"x\">link</a><<script>>text"));
        Assert.Contains("text", DocumentText.StripTags("<<script>>text"));
        Assert.Equal("plain", DocumentText.StripTags("plain"));
    }
}

public class PrivateClipboardTests
{
    [Fact]
    public void PlainTextOrNothingMayBeBorrowed()
    {
        Assert.True(SelectionCapture.SafeToBorrow([]));
        Assert.True(SelectionCapture.SafeToBorrow(["UnicodeText", "Text", "System.String", "OEMText", "Locale"]));
        Assert.True(SelectionCapture.SafeToBorrow(["unicodetext"])); // names are not case sensitive
    }

    [Theory]
    [InlineData("ExcludeClipboardContentFromMonitorProcessing")]   // what password managers set
    [InlineData("CanIncludeInClipboardHistory")]
    [InlineData("HTML Format")]                                    // rich text would be lost
    [InlineData("Rich Text Format")]
    [InlineData("Bitmap")]
    [InlineData("FileDrop")]
    public void PrivateOrRichClipboardsAreLeftAlone(string format) =>
        Assert.False(SelectionCapture.SafeToBorrow(["UnicodeText", format]));
}

public class ShortUserNameRedactionTests
{
    [Fact]
    public void AShortNameIsHiddenInPathsAndAfterUserWords()
    {
        var text = @"Crash in C:\Users\Al\AppData\x.log, opened by Al; account Al. Also D:/Al/notes and 'Al'.";
        var clean = Diagnostics.Redact(text, null, "Al", "PC");
        Assert.DoesNotContain(@"\Al\", clean);
        Assert.DoesNotContain("/Al/", clean);
        Assert.DoesNotContain("by Al", clean);
        Assert.DoesNotContain("account Al", clean);
        Assert.Contains("<user>", clean);
    }

    [Fact]
    public void AShortNameDoesNotDamageOrdinaryWords()
    {
        var clean = Diagnostics.Redact("Alert: the album is all about Alabama and Al-Qaeda news", null, "Al", "PC");
        Assert.Contains("Alert", clean);
        Assert.Contains("album", clean);
        Assert.Contains("all about Alabama", clean);
    }

    [Theory]
    [InlineData("J")]
    [InlineData("Jo")]
    public void OneAndTwoLetterNamesAreHiddenInPaths(string name) =>
        Assert.DoesNotContain(name + @"\", Diagnostics.Redact($@"C:\Users\{name}\Desktop\a.txt", null, name, "X"));

    [Fact]
    public void LongerNamesStillWorkAsBefore() =>
        Assert.Equal("hello <user> and <pc>", Diagnostics.Redact("hello Elan and DESKTOP-ABC", null, "Elan", "DESKTOP-ABC"));
}
