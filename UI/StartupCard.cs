using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WinCompanion.Tools;

namespace WinCompanion.UI;

/// <summary>
/// The small card shown in the bottom-right corner (next to the tray) while she starts. It never takes focus and covers
/// nothing important, fades in and out, and stays just long enough to be read.
/// </summary>
internal sealed class StartupCard : Window
{
    private static readonly TimeSpan MinimumVisible = TimeSpan.FromMilliseconds(1400);
    private readonly DateTime _shown = DateTime.UtcNow;
    private bool _closing;

    public StartupCard(string name)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = false;
        IsHitTestVisible = false;
        Title = name;
        Opacity = 0;
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");

        var icon = FloatingDock.AppIcon();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(icon is null
            ? new TextBlock { Text = "✦", FontSize = 26, Foreground = ThemeCatalog.PaletteBrush("Accent"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) }
            : new Image { Source = icon, Width = 40, Height = 40, Margin = new Thickness(0, 0, 14, 0) });
        row.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = name, FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = ThemeCatalog.PaletteBrush("Fg") },
                new TextBlock { Text = "Starting…", FontSize = 12, Foreground = ThemeCatalog.PaletteBrush("Muted"), Margin = new Thickness(0, 1, 0, 0) }
            }
        });

        Content = new Border
        {
            Margin = new Thickness(12),
            Padding = new Thickness(16, 12, 22, 12),
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Background = ThemeCatalog.PaletteBrush("WindowBack"),
            BorderBrush = ThemeCatalog.PaletteBrush("Stroke"),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 3, Opacity = 0.4 },
            Child = row
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        // Click-through as well as non-activating: it must never get in the way.
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GwlExStyle, style | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow | 0x20);
    }

    /// <summary>Puts it in the bottom-right corner above the taskbar and fades it in.</summary>
    public void ShowInCorner()
    {
        Show();
        UpdateLayout();
        var work = SystemParameters.WorkArea;
        Left = work.Right - ActualWidth - 8;
        Top = work.Bottom - ActualHeight - 8;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
    }

    /// <summary>Fades it out once she is ready, after it has been up long enough to read.</summary>
    public void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        var wait = MinimumVisible - (DateTime.UtcNow - _shown);
        var timer = new DispatcherTimer { Interval = wait > TimeSpan.Zero ? wait : TimeSpan.FromMilliseconds(1) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(260));
            fade.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fade);
        };
        timer.Start();
    }
}
