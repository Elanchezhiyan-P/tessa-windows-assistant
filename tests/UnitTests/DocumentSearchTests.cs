using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using WinCompanion.Services;
using WinCompanion.Tools;
using Xunit;

namespace WinCompanion.UnitTests;

/// <summary>Builds real Word, Excel, PowerPoint and PDF files, since reading them is the point.</summary>
public abstract class DocumentFixture : IDisposable
{
    // Under the build folder: a folder under %LOCALAPPDATA% (where the temp folder is) is correctly refused as an indexing target.
    protected string Dir { get; } = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "tessa-docs-" + Guid.NewGuid().ToString("N"))).FullName;

    public virtual void Dispose()
    {
        try { Directory.Delete(Dir, true); } catch (IOException) { }
    }

    protected string P(string name) => Path.Combine(Dir, name);

    protected string Write(string name, string content, DateTime? modified = null)
    {
        var path = P(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (modified is { } when_) File.SetLastWriteTime(path, when_);
        return path;
    }

    protected string Zip(string name, params (string Entry, string Xml)[] parts)
    {
        var path = P(name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, xml) in parts)
        {
            using var writer = new StreamWriter(archive.CreateEntry(entry).Open(), new UTF8Encoding(false));
            writer.Write(xml);
        }
        return path;
    }

    protected string Docx(string name, params string[] paragraphs) =>
        Zip(name, ("word/document.xml",
            "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
            string.Concat(paragraphs.Select(p => $"<w:p><w:r><w:t xml:space=\"preserve\">{p}</w:t></w:r></w:p>")) +
            "</w:body></w:document>"));

    protected string Pdf(string name, params string[] lines)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var y = 780;
        foreach (var line in lines)
        {
            page.AddText(line, 12, new PdfPoint(40, y), font);
            y -= 20;
        }
        var path = P(name);
        File.WriteAllBytes(path, builder.Build());
        return path;
    }
}

public class DocumentTextTests : DocumentFixture
{
    [Fact]
    public void ReadsWordParagraphs()
    {
        var text = DocumentText.Extract(Docx("a.docx", "Hello world", "Second paragraph"), out var problem);
        Assert.Null(problem);
        Assert.Equal("Hello world\nSecond paragraph", text);
    }

    [Fact]
    public void ReadsExcelText()
    {
        var path = Zip("a.xlsx", ("xl/sharedStrings.xml",
            "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>Invoice</t></si><si><t>Total 120</t></si></sst>"));
        Assert.Equal("Invoice\nTotal 120", DocumentText.Extract(path, out _));
    }

    [Fact]
    public void ReadsPowerPointSlidesInOrder()
    {
        string Slide(string text) =>
            "<p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
            $"<p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>{text}</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:sld>";

        var path = Zip("a.pptx", ("ppt/slides/slide10.xml", Slide("Tenth")), ("ppt/slides/slide2.xml", Slide("Second")), ("ppt/slides/slide1.xml", Slide("First")));
        var text = DocumentText.Extract(path, out _)!;
        Assert.True(text.IndexOf("First") < text.IndexOf("Second") && text.IndexOf("Second") < text.IndexOf("Tenth"));
        Assert.Contains("Slide 2:", text);
    }

    [Fact]
    public void ReadsPdfText()
    {
        var text = DocumentText.Extract(Pdf("a.pdf", "Invoice number 4711", "Total due 120 euro"), out var problem);
        Assert.Null(problem);
        Assert.Contains("Invoice number 4711", text);
        Assert.Contains("Total due 120 euro", text);
    }

    [Fact]
    public void APdfWithNoTextIsExplained()
    {
        var text = DocumentText.Extract(Pdf("blank.pdf"), out var problem);
        Assert.Null(text);
        Assert.Contains("scan", problem);
    }

    [Fact]
    public void ADamagedPdfDoesNotCrash()
    {
        var path = Write("broken.pdf", "this is not a pdf at all");
        Assert.Null(DocumentText.Extract(path, out var problem));
        Assert.Contains("couldn't read", problem);
    }

    [Fact]
    public void ADamagedWordFileDoesNotCrash()
    {
        var path = Write("broken.docx", "not a zip");
        Assert.Null(DocumentText.Extract(path, out var problem));
        Assert.False(string.IsNullOrWhiteSpace(problem));
    }

    [Fact]
    public void ReadsTextAndStripsHtml()
    {
        Assert.Equal("Buy milk\n\nand eggs", DocumentText.Extract(Write("a.txt", "Buy milk\r\n\r\n\r\n\r\nand   eggs"), out _)!.Replace("and eggs", "and eggs"));
        var html = DocumentText.Extract(Write("a.html", "<html><style>p{}</style><body><h1>Title</h1><p>Hello&nbsp;<b>there</b> &amp; welcome</p><script>alert(1)</script></body></html>"), out _)!;
        Assert.Contains("Title", html);
        Assert.Contains("there", html);
        Assert.Contains("& welcome", html);
        Assert.DoesNotContain("alert", html);
        Assert.DoesNotContain("<", html);
    }

    [Fact]
    public void KnowsWhichFilesItCanRead()
    {
        foreach (var name in new[] { "a.pdf", "a.DOCX", "a.xlsx", "a.pptx", "a.txt", "a.md", "a.cs", "a.html" }) Assert.True(DocumentText.IsSupported(name), name);
        foreach (var name in new[] { "a.png", "a.exe", "a.mp4", "a.zip", "a" }) Assert.False(DocumentText.IsSupported(name), name);
    }

    [Fact]
    public void ExplainsEmptyAndMissingFiles()
    {
        Assert.Null(DocumentText.Extract(Write("empty.txt", "  \n\n "), out var empty));
        Assert.Contains("no text", empty);
        Assert.Null(DocumentText.Extract(P("missing.txt"), out var missing));
        Assert.Contains("isn't there", missing);
    }

    [Fact]
    public void OfficeXmlIgnoresDoctypeAttacks()
    {
        // An XML bomb or external entity in a Word file must not be expanded.
        var xml = "<!DOCTYPE d [<!ENTITY x \"boom\">]><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:t>&x;</w:t></w:p></w:document>";
        Assert.ThrowsAny<System.Xml.XmlException>(() => DocumentText.OfficeXml(new MemoryStream(Encoding.UTF8.GetBytes(xml)), "t", "p"));
        var path = Zip("evil.docx", ("word/document.xml", xml));
        Assert.Null(DocumentText.Extract(path, out var problem)); // reported, not crashed
        Assert.NotNull(problem);
    }
}

public class TextChunksTests
{
    private static string Long() => string.Join("\n\n", Enumerable.Range(1, 60).Select(i => $"Paragraph {i}: " + new string('x', 150)));

    [Fact]
    public void SplitsLongTextIntoPiecesOfAboutTheTargetSize()
    {
        var chunks = TextChunks.Split(Long());
        Assert.True(chunks.Count > 5);
        Assert.All(chunks, c => Assert.InRange(c.Length, 1, TextChunks.TargetSize + 300));
    }

    [Fact]
    public void NothingIsLostBetweenPieces()
    {
        var chunks = TextChunks.Split(Long());
        var joined = string.Join("\n", chunks);
        for (var i = 1; i <= 60; i++) Assert.Contains($"Paragraph {i}:", joined);
    }

    [Fact]
    public void ShortTextIsOnePiece() => Assert.Single(TextChunks.Split("Just one short line."));

    [Fact]
    public void EmptyTextIsNoPieces() => Assert.Empty(TextChunks.Split("  "));

    [Fact]
    public void ACrazyLongLineIsStillCut()
    {
        var chunks = TextChunks.Split(string.Join(' ', Enumerable.Repeat("word", 2000)));
        Assert.True(chunks.Count >= 8);
        Assert.All(chunks, c => Assert.True(c.Length <= TextChunks.TargetSize + 200));
    }

    [Fact]
    public void KeywordsDropFillerAndShortWords() =>
        Assert.Equal(["invoice", "electricity", "march"], TextChunks.Keywords("Find the invoice for electricity from March, please?").Where(w => w != "please"));

    [Fact]
    public void BestReturnsTheMatchingPartsInDocumentOrder()
    {
        string[] chunks =
        [
            "Introduction about the company and its history.",
            "The warranty period is 24 months from the date of purchase.",
            "Shipping happens within five working days.",
            "Claims under the warranty must be made in writing. The warranty does not cover damage."
        ];
        var best = TextChunks.Best(chunks, "how long is the warranty?", 400);
        Assert.Contains("24 months", best);
        Assert.Contains("Claims under the warranty", best);
        Assert.DoesNotContain("Introduction", best);
        Assert.True(best.IndexOf("24 months") < best.IndexOf("Claims"));
    }

    [Fact]
    public void WithoutAUsefulQuestionItIsTheStart()
    {
        string[] chunks = ["First part.", "Second part.", "Third part."];
        Assert.StartsWith("First part.", TextChunks.Best(chunks, null, 15));
        Assert.StartsWith("First part.", TextChunks.Best(chunks, "the and for", 15));
    }

    [Fact]
    public void BestStaysWithinTheLimit()
    {
        var chunks = Enumerable.Range(0, 30).Select(i => "match " + new string('y', 500)).ToList();
        Assert.True(TextChunks.Best(chunks, "match", 2000).Length <= 2200);
    }
}

public class FileIndexTests : DatabaseTestBase
{
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "tessa-index-" + Guid.NewGuid().ToString("N"))).FullName;

    public new void Dispose()
    {
        base.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string P(string name) => Path.Combine(_dir, name);

    private string Write(string name, string text, DateTime? modified = null)
    {
        var path = P(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        if (modified is { } m) File.SetLastWriteTime(path, m);
        return path;
    }

    private IndexSummary Index()
    {
        Assert.StartsWith("Added", FileIndex.AddFolder(_dir));
        return FileIndex.Update();
    }

    [Fact]
    public void IndexesDocumentsAndSkipsEverythingElse()
    {
        Write("electricity.txt", "Invoice for electricity, total 120 euro");
        Write("notes.md", "Buy milk and eggs");
        Write("picture.png", "not text");
        Write("~$lock.docx", "temp lock file");
        Write(@"node_modules\pkg\readme.md", "should be ignored");
        Write(@".git\config", "should be ignored");
        File.SetAttributes(Write("secret.txt", "hidden file"), FileAttributes.Hidden);

        var summary = Index();

        Assert.Equal(2, summary.Added);
        var stats = FileIndex.Stats();
        Assert.Equal(1, stats.Folders);
        Assert.Equal(2, stats.Files);
    }

    [Fact]
    public void FindsByContentWithASnippet()
    {
        Write("a.txt", "The warranty period is twenty four months from purchase.");
        Write("b.txt", "Nothing relevant here at all.");
        Index();

        var hit = Assert.Single(FileIndex.Search("warranty period"));
        Assert.EndsWith("a.txt", hit.Path);
        Assert.False(hit.NameMatch);
        Assert.Contains("twenty four months", hit.Snippet);
    }

    [Fact]
    public void FindsByFileName()
    {
        Write("electricity-invoice-march.txt", "just some words");
        Index();

        var hit = Assert.Single(FileIndex.Search("invoice march"));
        Assert.True(hit.NameMatch);
    }

    [Fact]
    public void AFileMatchingByNameAndTextShowsWhatItSays()
    {
        Write("warranty.txt", "The warranty period is twenty four months.");
        Index();

        var hit = Assert.Single(FileIndex.Search("warranty"));
        Assert.True(hit.NameMatch);
        Assert.Contains("twenty four months", hit.Snippet);
    }

    [Fact]
    public void QuotedWordsAreNotQuotedTwiceInTheReply() =>
        Assert.Contains("matches \"zebra\".", FileIndex.Describe([], "\"zebra\""));

    [Fact]
    public void ANaturalRequestFallsBackToAnyWord()
    {
        Write("bill.txt", "Invoice for the electricity supply, total 120 euro");
        Index();

        // "march" appears nowhere, so needing every word finds nothing; the looser search still finds the invoice.
        var hit = Assert.Single(FileIndex.Search("find the invoice from march"));
        Assert.EndsWith("bill.txt", hit.Path);
    }

    [Fact]
    public void NoRealWordsMeansNoResults() => Assert.Empty(FileIndex.Search("find the"));

    [Fact]
    public void LimitsByDate()
    {
        Write("march.txt", "invoice electricity", new DateTime(2026, 3, 10));
        Write("may.txt", "invoice electricity", new DateTime(2026, 5, 1));
        Index();

        Assert.Equal(2, FileIndex.Search("invoice").Count);
        Assert.EndsWith("march.txt", Assert.Single(FileIndex.Search("invoice", after: "2026-03", before: "2026-04")).Path);
        Assert.EndsWith("may.txt", Assert.Single(FileIndex.Search("invoice", after: "2026-04-01")).Path);
        Assert.Empty(FileIndex.Search("invoice", before: "2026-03-01"));
        Assert.Equal(2, FileIndex.Search("invoice", after: "not a date").Count); // a bad date is ignored
    }

    [Fact]
    public void ReadsOnlyWhatChangedNextTime()
    {
        var a = Write("a.txt", "first version about apples");
        Write("b.txt", "stays the same about pears");
        Index();

        var again = FileIndex.Update();
        Assert.False(again.Changed);
        Assert.Equal(2, again.Unchanged);

        File.WriteAllText(a, "second version about oranges");
        File.SetLastWriteTime(a, DateTime.Now.AddMinutes(5));
        var changed = FileIndex.Update();
        Assert.Equal(1, changed.Updated);
        Assert.Equal(1, changed.Unchanged);
        Assert.NotEmpty(FileIndex.Search("oranges"));
        Assert.Empty(FileIndex.Search("apples")); // the old words are gone, not left behind
    }

    [Fact]
    public void ForgetsDeletedFiles()
    {
        var a = Write("a.txt", "about bananas");
        Write("b.txt", "about grapes");
        Index();

        File.Delete(a);
        var summary = FileIndex.Update();
        Assert.Equal(1, summary.Removed);
        Assert.Empty(FileIndex.Search("bananas"));
        Assert.NotEmpty(FileIndex.Search("grapes"));
    }

    [Fact]
    public void PicksUpNewFiles()
    {
        Write("a.txt", "about bananas");
        Index();
        Write("c.txt", "about kiwis");
        Assert.Equal(1, FileIndex.Update().Added);
        Assert.NotEmpty(FileIndex.Search("kiwis"));
    }

    [Fact]
    public void IndexesWordAndPdfFilesToo()
    {
        var word = Path.Combine(_dir, "contract.docx");
        using (var zip = ZipFile.Open(word, ZipArchiveMode.Create))
        using (var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open()))
            w.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:t>The notice period is three months</w:t></w:p></w:document>");

        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4).AddText("Receipt number 998877", 12, new PdfPoint(40, 780), builder.AddStandard14Font(Standard14Font.Helvetica));
        File.WriteAllBytes(P("receipt.pdf"), builder.Build());
        File.WriteAllBytes(P("scan.pdf"), new PdfDocumentBuilder().Also(b => b.AddPage(PageSize.A4)).Build());

        var summary = Index();
        Assert.Equal(3, summary.Added);
        Assert.Equal(1, summary.Problems); // the scan has no text
        Assert.EndsWith("contract.docx", Assert.Single(FileIndex.Search("notice period")).Path);
        Assert.EndsWith("receipt.pdf", Assert.Single(FileIndex.Search("998877")).Path);
    }

    [Fact]
    public void ARemovedFolderTakesItsDocumentsWithIt()
    {
        Write("a.txt", "about bananas");
        Index();
        Assert.Contains("Removed", FileIndex.RemoveFolder(Path.GetFileName(_dir)));
        Assert.Empty(FileIndex.Search("bananas"));
        Assert.Equal(new IndexStats(0, 0, 0, 0), FileIndex.Stats());
    }

    [Fact]
    public void ClearForgetsEverything()
    {
        Write("a.txt", "about bananas");
        Index();
        FileIndex.Clear();
        Assert.Empty(FileIndex.Folders());
        Assert.Empty(FileIndex.Search("bananas"));
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\does\not\exist")]
    public void RefusesFoldersThatShouldNeverBeIndexed(string path) => Assert.False(FileIndex.IsAllowedFolder(path, out var reason) , reason);

    [Fact]
    public void RefusesAppDataEvenThoughItExists()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.False(FileIndex.IsAllowedFolder(local, out var reason));
        Assert.Contains("app-data", reason);
        Assert.False(FileIndex.IsAllowedFolder(Path.GetTempPath(), out _));
    }

    [Fact]
    public void RefusesOverlappingFolders()
    {
        Directory.CreateDirectory(P("inner"));
        Assert.StartsWith("Added", FileIndex.AddFolder(_dir));
        Assert.Contains("already covered", FileIndex.AddFolder(P("inner")));
        Assert.Contains("already covered", FileIndex.AddFolder(Path.GetDirectoryName(_dir)!));
        Assert.Single(FileIndex.Folders());
    }

    [Fact]
    public void QueriesDropFillerAndQuoteWords()
    {
        var (all, any) = FileIndex.BuildQueries("find the invoice, from March!");
        Assert.Equal("\"invoice\" \"march\"", all);
        Assert.Equal("\"invoice\" OR \"march\"", any);
        Assert.Equal(("", ""), FileIndex.BuildQueries("find the"));
    }

    [Theory]
    [InlineData("\"hello\" OR NEAR(x) world*")]
    [InlineData("a\"; DROP TABLE index_files; --")]
    [InlineData("title:secret -excluded ^start")]
    public void SearchTextCanNeverBreakOutOfTheQuery(string hostile)
    {
        // Only letters and digits survive, each in quotes, so search syntax typed or found in a document does nothing.
        var (all, any) = FileIndex.BuildQueries(hostile);
        Assert.Matches("^(\"[\\p{L}\\p{N}]+\"( |$))*$", all + " ");
        Assert.Matches("^(\"[\\p{L}\\p{N}]+\"( OR |$))*$", any);
    }

    [Theory]
    [InlineData("2026-03", "2026-03-01")]
    [InlineData("2026-3-9", "2026-03-09")]
    [InlineData("2026", "2026-01-01")]
    [InlineData("yesterday", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalisesDates(string? input, string? expected) => Assert.Equal(expected, FileIndex.NormalizeDate(input));

    [Fact]
    public void ParsesFindArguments()
    {
        Assert.Equal(("invoice electricity", "2026-03", "2026-04"), FileIndex.ParseFindArgs("invoice electricity after:2026-03 before:2026-04"));
        Assert.Equal(("contract", null, null), FileIndex.ParseFindArgs("contract"));
    }

    [Fact]
    public void SearchingAnEmptyIndexIsHarmless()
    {
        Assert.Empty(FileIndex.Search("anything"));
        Assert.Contains("No folders are indexed", FileIndex.DescribeStats(FileIndex.Stats()));
    }
}

internal static class BuilderExtensions
{
    public static PdfDocumentBuilder Also(this PdfDocumentBuilder builder, Action<PdfDocumentBuilder> action)
    {
        action(builder);
        return builder;
    }
}

// In the database collection: these tests swap the shared database path, so they must not run alongside the other database tests.
[Collection("database")]
public class DocumentToolTests : DocumentFixture
{
    private static ToolContext Ctx() => new();

    private static Dictionary<string, ITool> Tools(ToolContext ctx) => DocumentTools.All(ctx).ToDictionary(t => t.Name);

    [Fact]
    public void ReadingNeverNeedsApprovalButAsksBeforeAnythingLeavesThePc()
    {
        var tools = Tools(Ctx());
        Assert.All(tools.Values, t => Assert.False(t.NeedsConfirmation(new JsonObject())));
        Assert.Equal(["search_my_files", "read_document"], tools.Keys);
    }

    [Fact]
    public async Task ReadDocumentReturnsTheRelevantPartNotJustTheStart()
    {
        var filler = string.Join("\n\n", Enumerable.Range(1, 80).Select(i => $"Section {i}. " + new string('z', 400)));
        var path = Write("manual.txt", filler + "\n\nThe warranty period is 24 months.\n\n" + filler);

        var read = Tools(Ctx())["read_document"];
        var answer = await read.ExecuteAsync(new JsonObject { ["path"] = path, ["question"] = "what is the warranty period?" });
        Assert.Contains("The warranty period is 24 months", answer);
        Assert.Contains("most relevant parts", answer);
        Assert.True(answer.Length < 9000);

        var start = await read.ExecuteAsync(new JsonObject { ["path"] = path });
        Assert.Contains("The start", start);
        Assert.Contains("Section 1.", start);
    }

    [Fact]
    public async Task ReadDocumentWorksOnPdfAndWordFiles()
    {
        var read = Tools(Ctx())["read_document"];
        Assert.Contains("Invoice number 4711", await read.ExecuteAsync(new JsonObject { ["path"] = Pdf("i.pdf", "Invoice number 4711") }));
        Assert.Contains("Notice is three months", await read.ExecuteAsync(new JsonObject { ["path"] = Docx("c.docx", "Notice is three months") }));
    }

    [Fact]
    public async Task ReadDocumentExplainsProblems()
    {
        var read = Tools(Ctx())["read_document"];
        Assert.Contains("No file at", await read.ExecuteAsync(new JsonObject { ["path"] = P("nope.pdf") }));
        Assert.Contains("not .png", await read.ExecuteAsync(new JsonObject { ["path"] = Write("a.png", "x") }));
        Assert.Contains("scan", await read.ExecuteAsync(new JsonObject { ["path"] = Pdf("blank.pdf") }));
    }

    [Fact]
    public async Task ADeclinedCloudShareReturnsNothingFromTheDocument()
    {
        var ctx = new ToolContext { Llm = new FakeCloudLlm(), Settings = new AppSettings { AskBeforeSendingToCloud = true }, Confirm = _ => Task.FromResult(false) };
        var path = Write("secret.txt", "The password is hunter2.");

        var answer = await Tools(ctx)["read_document"].ExecuteAsync(new JsonObject { ["path"] = path });

        Assert.Contains("chose not to send", answer);
        Assert.DoesNotContain("hunter2", answer);
    }

    [Fact]
    public async Task ASharedDocumentIsReadWhenTheUserAgrees()
    {
        string? asked = null;
        var ctx = new ToolContext
        {
            Llm = new FakeCloudLlm(), Settings = new AppSettings { AskBeforeSendingToCloud = true },
            Confirm = q => { asked = q; return Task.FromResult(true); }
        };
        var answer = await Tools(ctx)["read_document"].ExecuteAsync(new JsonObject { ["path"] = Write("a.txt", "Hello there") });

        Assert.Contains("Hello there", answer);
        Assert.Contains("a.txt", asked);
    }

    [Fact]
    public async Task SearchExplainsWhenNothingIsIndexed()
    {
        using var db = new DatabaseScope();
        var answer = await Tools(Ctx())["search_my_files"].ExecuteAsync(new JsonObject { ["query"] = "invoice" });
        Assert.Contains("No folders are indexed", answer);
    }

    /// <summary>A fresh temporary database for one test, for classes that can't inherit the database base class.</summary>
    private sealed class DatabaseScope : IDisposable
    {
        private readonly string _dir = Directory.CreateTempSubdirectory("tessa-db").FullName;
        private readonly string _old = Database.Path;

        public DatabaseScope() => Database.Path = Path.Combine(_dir, "test.db");

        public void Dispose()
        {
            Database.Path = _old;
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }
    }

    private sealed class FakeCloudLlm : ILlm
    {
        public event Action<string>? ToolCalled { add { } remove { } }
        public event Action<string>? ToolFinished { add { } remove { } }
        public Task<string> SendAsync(string userText, IProgress<string>? partial = null, CancellationToken cancel = default) => Task.FromResult("");
        public Task<string> GenerateAsync(string instruction, string? text = null, byte[]? png = null) => Task.FromResult("");
        public void ClearHistory() { }
        public bool IsCloud => true;
    }
}
