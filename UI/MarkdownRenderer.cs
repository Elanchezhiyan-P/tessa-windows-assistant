using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WinCompanion.UI;

/// <summary>
/// Small markdown renderer for chat replies: headings, bullet and numbered lists, fenced code,
/// and inline bold, italic, `code` and links. Anything else renders as plain text.
/// </summary>
internal static class MarkdownRenderer
{
    private static readonly Regex InlinePattern = new(
        @"\*\*(?<b>.+?)\*\*|`(?<c>[^`]+)`|\*(?<i>[^*\n]+)\*|\[(?<lt>[^\]]+)\]\((?<lu>https?://[^)\s]+)\)",
        RegexOptions.Compiled);
    private static readonly Regex HeadingPattern = new(@"^(#{1,3})\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex ListPattern = new(@"^\s*(?<m>[-*•]|\d+\.)\s+(?<t>.*)$", RegexOptions.Compiled);

    private static readonly Brush CodeBackground = Frozen("#33000000");
    private static readonly Brush InlineCodeBackground = Frozen("#33FFFFFF");
    private static readonly Brush LinkBrush = Frozen("#8FB0FF");
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public static StackPanel Render(string markdown, Brush foreground)
    {
        var panel = new StackPanel();
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var paragraph = new List<string>();

        void Flush()
        {
            if (paragraph.Count == 0) return;
            panel.Children.Add(TextBlockFor(string.Join("\n", paragraph), foreground, 14, FontWeights.Normal, new Thickness(0, 0, 0, 6)));
            paragraph.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (line.TrimStart().StartsWith("```"))
            {
                Flush();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith("```"); i++) code.Add(lines[i]);
                panel.Children.Add(CodeBlock(string.Join("\n", code), foreground));
                continue;
            }

            var heading = HeadingPattern.Match(line);
            if (heading.Success)
            {
                Flush();
                var size = heading.Groups[1].Length switch { 1 => 18.0, 2 => 16.0, _ => 15.0 };
                panel.Children.Add(TextBlockFor(heading.Groups[2].Value, foreground, size, FontWeights.SemiBold, new Thickness(0, 4, 0, 4)));
                continue;
            }

            var item = ListPattern.Match(line);
            if (item.Success)
            {
                Flush();
                var marker = char.IsDigit(item.Groups["m"].Value[0]) ? item.Groups["m"].Value : "•";
                panel.Children.Add(ListItem(marker, item.Groups["t"].Value, foreground));
                continue;
            }

            if (string.IsNullOrWhiteSpace(line)) Flush();
            else paragraph.Add(line);
        }
        Flush();
        return panel;
    }

    private static TextBlock TextBlockFor(string text, Brush foreground, double size, FontWeight weight, Thickness margin)
    {
        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = foreground,
            FontSize = size,
            FontWeight = weight,
            Margin = margin
        };
        AddInlines(block.Inlines, text);
        return block;
    }

    private static UIElement ListItem(string marker, string text, Brush foreground)
    {
        var grid = new Grid { Margin = new Thickness(6, 0, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var bullet = new TextBlock { Text = marker, Foreground = foreground, FontSize = 14, Margin = new Thickness(0, 0, 8, 0) };
        var body = TextBlockFor(text, foreground, 14, FontWeights.Normal, new Thickness());
        Grid.SetColumn(body, 1);
        grid.Children.Add(bullet);
        grid.Children.Add(body);
        return grid;
    }

    private static UIElement CodeBlock(string code, Brush foreground) => new Border
    {
        Background = CodeBackground,
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(10, 8, 10, 8),
        Margin = new Thickness(0, 2, 0, 8),
        Child = new TextBlock
        {
            Text = code,
            FontFamily = Mono,
            FontSize = 12.5,
            Foreground = foreground,
            TextWrapping = TextWrapping.Wrap
        }
    };

    private static void AddInlines(InlineCollection inlines, string text)
    {
        var position = 0;
        foreach (Match m in InlinePattern.Matches(text))
        {
            if (m.Index > position) inlines.Add(new Run(text[position..m.Index]));

            if (m.Groups["b"].Success) inlines.Add(new Bold(new Run(m.Groups["b"].Value)));
            else if (m.Groups["i"].Success) inlines.Add(new Italic(new Run(m.Groups["i"].Value)));
            else if (m.Groups["c"].Success)
                inlines.Add(new Run(m.Groups["c"].Value) { FontFamily = Mono, Background = InlineCodeBackground });
            else
            {
                var link = new Hyperlink(new Run(m.Groups["lt"].Value))
                {
                    NavigateUri = new Uri(m.Groups["lu"].Value),
                    Foreground = LinkBrush
                };
                link.RequestNavigate += (_, e) =>
                {
                    Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                    e.Handled = true;
                };
                inlines.Add(link);
            }
            position = m.Index + m.Length;
        }
        if (position < text.Length) inlines.Add(new Run(text[position..]));
    }

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
