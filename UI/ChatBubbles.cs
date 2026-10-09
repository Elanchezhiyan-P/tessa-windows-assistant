using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinCompanion.Services;

namespace WinCompanion.UI;

/// <summary>Builds the visual for one chat message.</summary>
internal static class ChatBubbles
{
    private static readonly Brush Text = Frozen("#F0F0F4");
    private static readonly Brush Muted = Frozen("#9A9DAD");
    private static readonly Brush UserBubble = Frozen("#5B7BF5");
    private static readonly Brush AssistantBubble = Frozen("#26FFFFFF");
    private static readonly Brush ErrorText = Frozen("#FFA9A9");
    private static readonly Brush ErrorBubble = Frozen("#40FF5A5A");

    public static FrameworkElement Create(ChatEntry entry, bool animate)
    {
        FrameworkElement element = entry.Role switch
        {
            "user" => Bubble(entry,
                new TextBlock { Text = entry.Text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, FontSize = 14 },
                UserBubble, HorizontalAlignment.Right),
            "assistant" => Bubble(entry, MarkdownRenderer.Render(entry.Text, Text), AssistantBubble, HorizontalAlignment.Left),
            "error" => Bubble(entry,
                new TextBlock { Text = entry.Text, TextWrapping = TextWrapping.Wrap, Foreground = ErrorText, FontSize = 13 },
                ErrorBubble, HorizontalAlignment.Left),
            _ => new TextBlock
            {
                Text = "⚙  " + entry.Text,
                Foreground = Muted,
                FontSize = 11,
                Margin = new Thickness(4, 2, 4, 2),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = entry.Text
            }
        };

        if (animate) FadeIn(element);
        return element;
    }

    // A bubble with a copy button that appears while the pointer is over it.
    private static Border Bubble(ChatEntry entry, UIElement content, Brush background, HorizontalAlignment align)
    {
        var copy = new Button
        {
            Style = (Style)Application.Current.FindResource("IconButton"),
            Content = "",
            Width = 24,
            Height = 24,
            FontSize = 11,
            Opacity = 0,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            ToolTip = "Copy"
        };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(entry.Text); }
            catch (System.Runtime.InteropServices.COMException) { /* clipboard busy */ }
        };

        if (content is FrameworkElement fe) fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top, 22, fe.Margin.Bottom);

        var bubble = new Border
        {
            Child = new Grid { Children = { content, copy } },
            Background = background,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 9, 8, 5),
            Margin = new Thickness(0, 4, 0, 4),
            HorizontalAlignment = align,
            MaxWidth = 500,
            ToolTip = entry.Time.ToString("g")
        };
        bubble.MouseEnter += (_, _) => copy.Opacity = 1;
        bubble.MouseLeave += (_, _) => copy.Opacity = 0;
        return bubble;
    }

    private static void FadeIn(FrameworkElement element)
    {
        var slide = new TranslateTransform(0, 10);
        element.RenderTransform = slide;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
