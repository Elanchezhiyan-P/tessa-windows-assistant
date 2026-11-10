using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinCompanion.Services;

namespace WinCompanion.UI;

/// <summary>Builds the visual for one chat message.</summary>
internal static class ChatBubbles
{
    private static Brush Text => ThemeCatalog.PaletteBrush("Fg");
    private static Brush Muted => ThemeCatalog.PaletteBrush("Muted");
    // The user's bubbles follow the accent colour, so a new theme shows up in the next message.
    private static Brush UserBubble
    {
        get
        {
            var brush = new SolidColorBrush(ThemeCatalog.UserBubble(ThemeCatalog.CurrentAccent));
            brush.Freeze();
            return brush;
        }
    }
    private static Brush AssistantBubble => ThemeCatalog.PaletteBrush("AssistantBubble");
    private static Brush ErrorText => ThemeCatalog.PaletteBrush("ErrorText");
    private static Brush ErrorBubble => ThemeCatalog.PaletteBrush("ErrorBubble");

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

    /// <summary>Set by the window: pins or unpins a saved message and returns whether it is now pinned.</summary>
    public static Func<long, bool>? TogglePin { get; set; }

    // A bubble with a copy and a pin button, shown while the pointer is over it (the pin stays visible once set).
    private static Border Bubble(ChatEntry entry, UIElement content, Brush background, HorizontalAlignment align)
    {
        var copy = new Button
        {
            Style = (Style)Application.Current.FindResource("IconButton"),
            Content = "\uE8C8", // Copy
            Width = 24,
            Height = 24,
            FontSize = 11,
            ToolTip = "Copy"
        };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(entry.Text); }
            catch (System.Runtime.InteropServices.COMException) { /* clipboard busy */ }
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            Opacity = 0
        };

        Button? pin = null;
        var pinned = entry.Pinned;
        if (entry.Id > 0)
        {
            pin = new Button
            {
                Style = (Style)Application.Current.FindResource("IconButton"),
                Width = 24,
                Height = 24,
                FontSize = 11
            };
            void ShowPin()
            {
                pin.Content = pinned ? "\uE77A" : "\uE718"; // unpin / pin
                pin.ToolTip = pinned ? "Unpin" : "Pin this message";
                pin.Foreground = pinned ? ThemeCatalog.PaletteBrush("Accent") : ThemeCatalog.PaletteBrush("Muted");
            }
            ShowPin();
            pin.Click += (_, _) =>
            {
                pinned = TogglePin?.Invoke(entry.Id) ?? pinned;
                ShowPin();
            };
            buttons.Children.Add(pin);
        }
        buttons.Children.Add(copy);

        if (content is FrameworkElement fe) fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top, pin is null ? 22 : 46, fe.Margin.Bottom);

        var bubble = new Border
        {
            Child = new Grid { Children = { content, buttons } },
            Background = background,
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14, 9, 8, 5),
            Margin = new Thickness(0, 4, 0, 4),
            HorizontalAlignment = align,
            MaxWidth = 500,
            ToolTip = entry.Time.ToString("g"),
            Tag = entry.Id
        };
        buttons.Opacity = pinned ? 1 : 0;
        bubble.MouseEnter += (_, _) => buttons.Opacity = 1;
        bubble.MouseLeave += (_, _) => buttons.Opacity = pinned ? 1 : 0;
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
}
