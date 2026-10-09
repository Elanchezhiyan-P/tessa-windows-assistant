using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WinCompanion.UI;

/// <summary>The three softly pulsing dots shown while she is working on a reply.</summary>
internal static class TypingIndicator
{
    public static FrameworkElement Create()
    {
        var dots = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < 3; i++)
        {
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 7, Height = 7, Margin = new Thickness(i == 0 ? 0 : 5, 0, 0, 0),
                Fill = new SolidColorBrush(Color.FromRgb(0xC9, 0xCC, 0xDA)), Opacity = 0.35
            };
            dot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(520))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(i * 170),
                EasingFunction = new SineEase()
            });
            dots.Children.Add(dot);
        }

        var bubble = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(16, 13, 16, 13),
            Margin = new Thickness(0, 4, 40, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = dots
        };
        bubble.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        return bubble;
    }
}
