using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace WinCompanion.UI;

/// <summary>
/// A glowing orb that breathes while she waits for you to speak and swells with the loudness of your voice,
/// followed by a short label ("Listening…", "Understanding…").
/// </summary>
internal sealed class ListeningIndicator : StackPanel
{
    private readonly ScaleTransform _glowScale = new(1, 1), _orbScale = new(1, 1);
    private readonly TextBlock _label;
    private double _smoothed;

    public ListeningIndicator()
    {
        Orientation = Orientation.Horizontal;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;

        var glow = new Ellipse
        {
            Width = 64, Height = 64, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _glowScale,
            Fill = new RadialGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(0x90, 0x7B, 0x6B, 0xFF), 0.0),
                new(Color.FromArgb(0x40, 0x4F, 0xB0, 0xFF), 0.55),
                new(Color.FromArgb(0x00, 0x4F, 0xB0, 0xFF), 1.0),
            })
        };
        var orb = new Ellipse
        {
            Width = 34, Height = 34, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = _orbScale,
            Fill = new RadialGradientBrush(new GradientStopCollection
            {
                new(Color.FromRgb(0xF2, 0xF5, 0xFF), 0.0),
                new(Color.FromRgb(0x8F, 0xA6, 0xFF), 0.35),
                new(Color.FromRgb(0x6B, 0x5B, 0xFF), 0.75),
                new(Color.FromRgb(0xB2, 0x6B, 0xFF), 1.0),
            }) { GradientOrigin = new Point(0.35, 0.3) }
        };
        // Breathing, so it looks alive even when no loudness is being measured.
        _orbScale.BeginAnimation(ScaleTransform.ScaleXProperty, Breathe());
        _orbScale.BeginAnimation(ScaleTransform.ScaleYProperty, Breathe());

        var holder = new Grid { Width = 64, Height = 64 };
        holder.Children.Add(glow);
        holder.Children.Add(orb);
        Children.Add(holder);

        _label = new TextBlock
        {
            Text = "Listening…", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14, Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xCC, 0xDA))
        };
        Children.Add(_label);
    }

    public string Label
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    /// <summary>Feed the current voice level (RMS, about 0 to 0.3). The orb eases toward it instead of jumping.</summary>
    public void SetLevel(double rms)
    {
        var target = Math.Clamp(rms * 7, 0, 1);
        _smoothed = target > _smoothed ? target : _smoothed * 0.6 + target * 0.4; // quick to rise, gentle to fall
        var scale = 1 + _smoothed * 0.75;
        var tween = new DoubleAnimation(scale, TimeSpan.FromMilliseconds(110)) { EasingFunction = new SineEase() };
        _glowScale.BeginAnimation(ScaleTransform.ScaleXProperty, tween);
        _glowScale.BeginAnimation(ScaleTransform.ScaleYProperty, tween);
    }

    private static DoubleAnimation Breathe() => new(1.0, 1.1, TimeSpan.FromMilliseconds(950))
    {
        AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase()
    };
}
