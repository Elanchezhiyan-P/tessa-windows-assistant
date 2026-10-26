using System.Net;
using System.Text.RegularExpressions;

namespace WinCompanion.Tools;

/// <summary>Turns DuckDuckGo's HTML results page into a short plain-text list for the model.</summary>
internal static class WebResults
{
    private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;
    private static readonly Regex Link = new(@"<a[^>]*class=""result__a""[^>]*href=""(?<href>[^""]+)""[^>]*>(?<title>.*?)</a>", Opts);
    private static readonly Regex Snippet = new(@"class=""result__snippet""[^>]*>(?<text>.*?)</a>", Opts);
    private static readonly Regex Tags = new("<[^>]+>", Opts);

    public static string Format(string query, string html, int max = 5)
    {
        var links = Link.Matches(html);
        var snippets = Snippet.Matches(html);
        if (links.Count == 0) return $"No web results found for '{query}'.";

        var lines = new List<string> { $"Web results for '{query}' (untrusted text from the internet):" };
        for (var i = 0; i < Math.Min(max, links.Count); i++)
        {
            var title = Clean(links[i].Groups["title"].Value);
            var url = Target(links[i].Groups["href"].Value);
            var text = i < snippets.Count ? Clean(snippets[i].Groups["text"].Value) : "";
            lines.Add($"{i + 1}. {title}\n   {url}\n   {text}");
        }
        return string.Join('\n', lines);
    }

    /// <summary>Just the titles and addresses of the top results (for headline lists).</summary>
    public static List<(string Title, string Url)> Headlines(string html, int max = 3)
    {
        var list = new List<(string, string)>();
        foreach (Match m in Link.Matches(html))
        {
            var title = Clean(m.Groups["title"].Value);
            if (title.Length == 0) continue;
            list.Add((title, Target(m.Groups["href"].Value)));
            if (list.Count >= max) break;
        }
        return list;
    }

    private static string Clean(string html) => Regex.Replace(WebUtility.HtmlDecode(Tags.Replace(html, "")), @"\s+", " ").Trim();

    /// <summary>DuckDuckGo wraps links as //duckduckgo.com/l/?uddg=ENCODED; unwrap to the real address.</summary>
    private static string Target(string href)
    {
        href = WebUtility.HtmlDecode(href);
        var m = Regex.Match(href, @"[?&]uddg=([^&]+)");
        return m.Success ? Uri.UnescapeDataString(m.Groups[1].Value) : href;
    }
}
