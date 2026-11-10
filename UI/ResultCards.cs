using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinCompanion.Services;

namespace WinCompanion.UI;

/// <summary>
/// Answers that are better as something to use than something to read: start-up programs with a switch each, search results with
/// Open and Show-in-folder buttons, and a junk-file summary with a Clean up button. They are built from plain data and delegates,
/// so the window decides what a click does (and logs it) and the cards can be tested on their own.
/// </summary>
internal static class ResultCards
{
    private static Func<string, object> Lookup(Func<string, object>? resources) =>
        resources ?? (key => Application.Current.FindResource(key));

    private static Border Frame(Func<string, object> res, params UIElement[] children)
    {
        var stack = new StackPanel();
        foreach (var child in children) stack.Children.Add(child);
        return new Border
        {
            Background = (Brush)res("Surface"),
            BorderBrush = (Brush)res("Stroke"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 4, 0, 8),
            MaxWidth = 540, // like the chat bubbles: never edge to edge
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = stack
        };
    }

    private static TextBlock Text(Func<string, object> res, string text, string brush, double size, FontWeight? weight = null, Thickness? margin = null) => new()
    {
        Text = text,
        Foreground = (Brush)res(brush),
        FontSize = size,
        FontWeight = weight ?? FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap,
        Margin = margin ?? new Thickness(0)
    };

    // ---- Start-up programs ------------------------------------------------------------------------------

    /// <param name="toggle">Switches one program on or off and returns what it is now and a message to show. The card shows the real state.</param>
    public static FrameworkElement StartupList(IReadOnlyList<StartupItem> items,
        Func<StartupItem, bool, (bool Enabled, string Message)> toggle, Func<string, object>? resources = null)
    {
        var res = Lookup(resources);
        var state = items.ToDictionary(i => i, i => i.Enabled);

        var summary = Text(res, "", "Muted", 12, margin: new Thickness(0, 2, 0, 8));
        var footer = Text(res, "Changes apply from your next sign-in. Nothing is uninstalled, and you can switch a program back on here.", "Muted", 11.5, margin: new Thickness(0, 8, 0, 0));
        void UpdateSummary() => summary.Text = $"{state.Values.Count(on => on)} on · {state.Values.Count(on => !on)} off";
        UpdateSummary();

        var list = new StackPanel();
        // Programs that are on come first; the order stays put while switches are used, so nothing jumps under the pointer.
        foreach (var item in items.OrderByDescending(i => i.Enabled).ThenBy(i => i.Label, StringComparer.OrdinalIgnoreCase))
        {
            var box = new CheckBox
            {
                Style = (Style)res("Switch"),
                Content = item.Label + item.Tag,
                IsChecked = item.Enabled,
                Margin = new Thickness(0, 5, 0, 0),
                ToolTip = item.Command
            };
            var captured = item;
            box.Click += (_, _) =>
            {
                var (enabled, message) = toggle(captured, box.IsChecked == true);
                box.IsChecked = enabled; // whatever really happened, not what was clicked
                state[captured] = enabled;
                footer.Text = message;
                UpdateSummary();
            };
            list.Children.Add(box);
        }

        var scroll = new ScrollViewer { MaxHeight = 280, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list };
        return Frame(res, Text(res, "Start with Windows", "Fg", 13.5, FontWeights.SemiBold), summary, scroll, footer);
    }

    // ---- Search results -----------------------------------------------------------------------------------

    /// <param name="open">Opens a document in its own app. Only offered for files where that can't run code (see <paramref name="canOpen"/>).</param>
    /// <param name="reveal">Shows the file in Explorer.</param>
    public static FrameworkElement SearchResults(string query, IReadOnlyList<FileHit> hits, Action<string> open, Action<string> reveal,
        Func<string, bool> canOpen, Func<string, object>? resources = null)
    {
        var res = Lookup(resources);
        var children = new List<UIElement>
        {
            Text(res, $"{hits.Count} match(es) for “{query.Trim().Trim('"')}”", "Fg", 13.5, FontWeights.SemiBold, new Thickness(0, 0, 0, 4))
        };

        foreach (var hit in hits)
        {
            var details = new StackPanel();
            var title = Text(res, Path.GetFileName(hit.Path), "Fg", 13, FontWeights.SemiBold);
            title.Inlines.Add(new System.Windows.Documents.Run($"  ·  {hit.Modified:d MMM yyyy}")
            { FontWeight = FontWeights.Normal, Foreground = (Brush)res("Muted"), FontSize = 12 });
            details.Children.Add(title);

            var folder = Text(res, Path.GetDirectoryName(hit.Path) ?? "", "Muted", 11.5);
            folder.TextTrimming = TextTrimming.CharacterEllipsis;
            folder.TextWrapping = TextWrapping.NoWrap;
            folder.ToolTip = hit.Path;
            details.Children.Add(folder);

            var snippet = hit.Snippet.StartsWith("(the file name", StringComparison.Ordinal) ? "*the file name matches*" : hit.Snippet;
            var body = MarkdownRenderer.Render(snippet, (Brush)res("Fg"));
            body.Margin = new Thickness(0, 3, 0, 0);
            details.Children.Add(body);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            var path = hit.Path;
            if (canOpen(path))
            {
                var openButton = new Button { Content = "Open", Style = (Style)res("Chip") };
                openButton.Click += (_, _) => open(path);
                buttons.Children.Add(openButton);
            }
            var folderButton = new Button { Content = "Show in folder", Style = (Style)res("Chip") };
            folderButton.Click += (_, _) => reveal(path);
            buttons.Children.Add(folderButton);
            details.Children.Add(buttons);

            children.Add(new Border
            {
                BorderBrush = (Brush)res("Stroke"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 8, 0, 0),
                Margin = new Thickness(0, 8, 0, 0),
                Child = details
            });
        }
        return Frame(res, children.ToArray());
    }

    // ---- Junk files ----------------------------------------------------------------------------------------

    /// <param name="clean">Does the deleting and returns what happened. It is only called after a second click, because it can't be undone.</param>
    public static FrameworkElement Cleanup(IReadOnlyList<CleanupFound> found, Func<Task<string>> clean, Func<string, object>? resources = null)
    {
        var res = Lookup(resources);
        var total = found.Sum(f => f.Bytes);
        var children = new List<UIElement> { Text(res, "Junk files you can clean up", "Fg", 13.5, FontWeights.SemiBold, new Thickness(0, 0, 0, 4)) };

        foreach (var area in found.Where(f => f.Bytes > 0))
            children.Add(Text(res, $"{area.Area.Name}: {SystemInfo.FormatBytes(area.Bytes)} in {area.Files:N0} file(s)", "Fg", 12.5, margin: new Thickness(0, 2, 0, 0)));

        var status = Text(res, "Only files older than a day, in the temp folder, internet cache, crash dumps and error reports. They are deleted for good.",
            "Muted", 11.5, margin: new Thickness(0, 8, 0, 0));
        children.Add(status);

        if (total > 0)
        {
            var button = new Button
            {
                Content = $"Clean up {SystemInfo.FormatBytes(total)}",
                Style = (Style)res("PrimaryButton"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 10, 0, 0)
            };
            var armed = false;
            button.Click += async (_, _) =>
            {
                if (!armed)
                {
                    armed = true;
                    button.Content = "Click again to delete them for good";
                    return;
                }
                button.IsEnabled = false;
                status.Text = "Cleaning…";
                status.Text = await clean();
                button.Visibility = Visibility.Collapsed;
            };
            children.Add(button);
        }
        return Frame(res, children.ToArray());
    }
}
