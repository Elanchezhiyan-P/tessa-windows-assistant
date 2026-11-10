using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using UglyToad.PdfPig;

namespace WinCompanion.Services;

/// <summary>
/// Pulls the plain text out of the documents people keep: text and code files, Word, Excel and PowerPoint files, and PDFs.
/// Everything is read on this PC. A scanned PDF is only a picture, so it has no text to find.
/// </summary>
public static class DocumentText
{
    public const int MaxChars = 1_000_000;
    private const long MaxTextBytes = 8L * 1024 * 1024;
    private const long MaxDocumentBytes = 40L * 1024 * 1024;
    private const int MaxPdfPages = 400;
    // A part of an Office file can be a tiny zip entry that expands to gigabytes. Anything over this is not read.
    private const long MaxEntryBytes = 60L * 1024 * 1024;

    private static readonly HashSet<string> PlainText = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".csv", ".tsv", ".log", ".json", ".xml", ".yml", ".yaml", ".ini", ".cfg", ".toml", ".tex",
        ".cs", ".py", ".js", ".ts", ".java", ".c", ".cpp", ".h", ".go", ".rs", ".sql", ".ps1", ".bat", ".sh", ".css"
    };

    private static readonly HashSet<string> Markup = new(StringComparer.OrdinalIgnoreCase) { ".html", ".htm" };
    private static readonly HashSet<string> Office = new(StringComparer.OrdinalIgnoreCase) { ".docx", ".xlsx", ".pptx" };

    public static bool IsSupported(string path)
    {
        var ext = Path.GetExtension(path);
        return PlainText.Contains(ext) || Markup.Contains(ext) || Office.Contains(ext) || ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }

    // Files that are safe to hand to Windows to open: documents and plain data. Scripts and code (.bat, .ps1, .js, .py ...) are
    // readable and searchable, but "opening" one would RUN it, so they are never opened from a search result.
    private static readonly HashSet<string> Openable = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".xlsx", ".pptx", ".txt", ".md", ".markdown", ".csv", ".tsv", ".log", ".json", ".xml", ".yml", ".yaml",
        ".ini", ".cfg", ".toml", ".tex", ".html", ".htm"
    };

    /// <summary>True for documents that can be opened in their normal app without any risk of running code.</summary>
    public static bool IsSafeToOpen(string path) => Openable.Contains(Path.GetExtension(path));

    public static string SupportedList => "text and code files, HTML, Word (.docx), Excel (.xlsx), PowerPoint (.pptx) and PDF";

    /// <summary>The document's text, or null with <paramref name="problem"/> saying why there is none.</summary>
    public static string? Extract(string path, out string? problem)
    {
        problem = null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) { problem = "The file isn't there."; return null; }
            var ext = info.Extension.ToLowerInvariant();
            var limit = PlainText.Contains(ext) || Markup.Contains(ext) ? MaxTextBytes : MaxDocumentBytes;
            if (info.Length > limit) { problem = $"It is too big to read ({SystemInfo.FormatBytes(info.Length)})."; return null; }

            string? text = ext switch
            {
                ".pdf" => FromPdf(path, out problem),
                ".docx" => FromZip(path, ["word/document.xml"], "t", "p"),
                ".xlsx" => FromZip(path, ["xl/sharedStrings.xml"], "t", "si"),
                ".pptx" => FromPresentation(path),
                ".html" or ".htm" => FromHtml(File.ReadAllText(path)),
                _ => ReadPlain(path)
            };

            if (problem is not null) return null;
            text = Tidy(text);
            if (text.Length == 0)
            {
                problem = ext == ".pdf" ? "There is no text in it (it may be a scan, which is only a picture)." : "There is no text in it.";
                return null;
            }
            return text.Length > MaxChars ? text[..MaxChars] : text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or XmlException or System.Security.SecurityException)
        {
            problem = $"I couldn't open it: {ex.Message}";
            return null;
        }
    }

    // ---- Formats ----------------------------------------------------------------------------------------

    private static string ReadPlain(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[MaxChars];
        var read = reader.ReadBlock(buffer, 0, buffer.Length);
        return new string(buffer, 0, read);
    }

    // Time limits on the patterns: a page full of unclosed <script tags can make a backtracking pattern crawl.
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(2);
    private static readonly Regex ScriptOrStyle = new(@"<(script|style)\b.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled, PatternTimeout);
    private static readonly Regex BlockEnd = new(@"<\s*(br|/p|/div|/li|/h\d|/tr)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled, PatternTimeout);
    private static readonly Regex AnyTag = new("<[^>]+>", RegexOptions.Compiled, PatternTimeout);

    private static string FromHtml(string html)
    {
        try
        {
            html = ScriptOrStyle.Replace(html, " ");
            html = BlockEnd.Replace(html, "\n");
            return WebUtility.HtmlDecode(AnyTag.Replace(html, " "));
        }
        catch (RegexMatchTimeoutException)
        {
            return WebUtility.HtmlDecode(StripTags(html)); // a page built to make the patterns crawl: a plain scan can't be slowed down
        }
    }

    /// <summary>Removes everything between &lt; and &gt; in one pass.</summary>
    internal static string StripTags(string html)
    {
        var sb = new StringBuilder(html.Length);
        var inTag = false;
        foreach (var c in html)
        {
            if (c == '<') { if (!inTag) sb.Append(' '); inTag = true; }
            else if (c == '>' && inTag) inTag = false;
            else if (!inTag) sb.Append(c);
        }
        return sb.ToString();
    }

    private static string? FromPdf(string path, out string? problem)
    {
        problem = null;
        try
        {
            using var document = PdfDocument.Open(path);
            var sb = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                if (page.Number > MaxPdfPages || sb.Length >= MaxChars) break;
                sb.AppendLine(page.Text).AppendLine();
            }
            return sb.ToString();
        }
        catch (Exception ex) when (ex.GetType().Namespace?.StartsWith("UglyToad.PdfPig", StringComparison.Ordinal) == true
                                   || ex is InvalidOperationException or ArgumentException or NotSupportedException or IndexOutOfRangeException)
        {
            problem = ex.GetType().Name.Contains("Encrypt", StringComparison.OrdinalIgnoreCase)
                ? "It is password-protected."
                : "I couldn't read this PDF (it may be damaged or an unusual kind).";
            return null;
        }
    }

    private static string FromPresentation(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var slides = zip.Entries
            .Select(e => (Entry: e, Number: SlideNumber(e.FullName)))
            .Where(s => s.Number > 0)
            .OrderBy(s => s.Number)
            .ToList();

        var sb = new StringBuilder();
        foreach (var (entry, number) in slides)
        {
            if (sb.Length >= MaxChars) break; // enough text: don't keep opening thousands of slides
            if (entry.Length > MaxEntryBytes) throw new InvalidDataException("A part of this file is too large to read.");
            using var stream = entry.Open();
            sb.AppendLine($"Slide {number}:").AppendLine(OfficeXml(stream, "t", "p")).AppendLine();
        }
        return sb.ToString();
    }

    private static int SlideNumber(string name)
    {
        var match = Regex.Match(name, @"^ppt/slides/slide(\d+)\.xml$", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var number) ? number : 0; // a huge number in a crafted name is not a slide
    }

    private static string FromZip(string path, string[] parts, string textElement, string blockElement)
    {
        using var zip = ZipFile.OpenRead(path);
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            var entry = zip.GetEntry(part);
            if (entry is null) continue;
            if (entry.Length > MaxEntryBytes) throw new InvalidDataException("A part of this file is too large to read.");
            using var stream = entry.Open();
            sb.AppendLine(OfficeXml(stream, textElement, blockElement));
        }
        return sb.ToString();
    }

    /// <summary>
    /// The words in an Office XML part: the content of every <paramref name="textElement"/> (w:t, a:t), with a line break at the end of each
    /// <paramref name="blockElement"/> (a paragraph, or a spreadsheet's shared string).
    /// </summary>
    public static string OfficeXml(Stream xml, string textElement, string blockElement)
    {
        var sb = new StringBuilder();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(xml, settings);
        var more = reader.Read();
        while (more && sb.Length < MaxChars)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == textElement && !reader.IsEmptyElement)
            {
                // Reading the text moves the reader past the element's end, onto the next node: handle that node before reading on.
                sb.Append(reader.ReadElementContentAsString());
                continue;
            }

            if (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.LocalName == "tab") sb.Append('\t');
                else if (reader.LocalName == "br") sb.Append('\n');
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == blockElement)
            {
                sb.Append('\n');
            }
            more = reader.Read();
        }
        return sb.ToString();
    }

    /// <summary>Collapses runs of blank lines and spaces so chunks aren't mostly whitespace.</summary>
    public static string Tidy(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace(' ', ' ');
        text = Regex.Replace(text, @"[ \t\f\v]+", " ");
        text = Regex.Replace(text, @" ?\n ?", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }
}
