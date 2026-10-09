using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace WinCompanion.UI;

/// <summary>
/// Makes a ScrollViewer glide instead of jumping: each mouse-wheel notch animates the offset with an ease-out curve,
/// and rapid notches build on the destination already in flight.
/// </summary>
internal static class SmoothScroll
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, OnEnabledChanged));

    private static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached(
        "Offset", typeof(double), typeof(SmoothScroll),
        new PropertyMetadata(0.0, (d, e) => ((ScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue)));

    private static readonly DependencyProperty TargetProperty = DependencyProperty.RegisterAttached(
        "Target", typeof(double), typeof(SmoothScroll), new PropertyMetadata(0.0));

    private static readonly DependencyProperty LastWheelProperty = DependencyProperty.RegisterAttached(
        "LastWheel", typeof(DateTime), typeof(SmoothScroll), new PropertyMetadata(DateTime.MinValue));

    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject o, bool value) => o.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer viewer) return;
        viewer.PreviewMouseWheel -= OnWheel;
        if ((bool)e.NewValue) viewer.PreviewMouseWheel += OnWheel;
    }

    private static void OnWheel(object sender, MouseWheelEventArgs e)
    {
        var viewer = (ScrollViewer)sender;
        if (viewer.ScrollableHeight <= 0) return;
        e.Handled = true;

        var inFlight = DateTime.UtcNow - (DateTime)viewer.GetValue(LastWheelProperty) < TimeSpan.FromMilliseconds(350);
        var from = inFlight ? (double)viewer.GetValue(TargetProperty) : viewer.VerticalOffset;
        AnimateTo(viewer, from - e.Delta * 0.9, 260);
        viewer.SetValue(LastWheelProperty, DateTime.UtcNow);
    }

    /// <summary>Glides to the bottom (used when a new message arrives).</summary>
    public static void ScrollToEnd(ScrollViewer viewer) => AnimateTo(viewer, viewer.ScrollableHeight, 320);

    private static void AnimateTo(ScrollViewer viewer, double target, int milliseconds)
    {
        target = Math.Clamp(target, 0, viewer.ScrollableHeight);
        viewer.SetValue(TargetProperty, target);
        viewer.BeginAnimation(OffsetProperty, new DoubleAnimation(viewer.VerticalOffset, target, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }
}
