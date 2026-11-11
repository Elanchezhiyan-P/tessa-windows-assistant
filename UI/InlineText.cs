using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace WinCompanion.UI;

/// <summary>Shows text that uses Markdown bold (**like this**) and `code` in a plain text block: bold becomes bold, and no stray marks are left.</summary>
internal static class InlineText
{
    private static readonly Regex Bold = new(@"\*\*(?<b>.+?)\*\*", RegexOptions.Singleline | RegexOptions.Compiled);

    public static void Set(TextBlock block, string? text)
    {
        block.Inlines.Clear();
        text = (text ?? "").Replace("`", "");
        var position = 0;
        foreach (Match m in Bold.Matches(text))
        {
            if (m.Index > position) block.Inlines.Add(new Run(text[position..m.Index]));
            block.Inlines.Add(new Run(m.Groups["b"].Value) { FontWeight = FontWeights.SemiBold });
            position = m.Index + m.Length;
        }
        if (position < text.Length) block.Inlines.Add(new Run(text[position..]));
    }

    /// <summary>The same text with the marks removed, for places that cannot show bold.</summary>
    public static string Plain(string? text) => Bold.Replace((text ?? "").Replace("`", ""), "${b}");
}
