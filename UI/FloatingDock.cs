using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinCompanion.Tools;

namespace WinCompanion.UI;

/// <summary>
/// The small floating button she shrinks to when you click another app. It stays out of the way, never takes focus, can be dragged,
/// and one click brings the window back exactly as it was. A dot appears on it when she has something to tell you.
/// </summary>
internal sealed class FloatingDock : Window
{
    private const double Size = 48;
    private readonly Border _dot = new()
    {
        Width = 12, Height = 12, CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed, BorderThickness = new Thickness(2)
    };
    private bool _dragged;

    /// <summary>The user clicked it (not dragged it): bring the window back.</summary>
    public event Action? Clicked;
    /// <summary>The user dragged it somewhere: the new left and top.</summary>
    public event Action<double, double>? Moved;

    public FloatingDock(string name)
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
        Title = name;

        _dot.Background = ThemeCatalog.PaletteBrush("NoteFg");
        _dot.BorderBrush = ThemeCatalog.PaletteBrush("WindowBack");

        // A rounded pill: her icon in a small circle, and her name.
        var badge = new Border
        {
            Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Margin = new Thickness(0, 0, 10, 0),
            Background = ThemeCatalog.PaletteBrush("AccentSoft"),
            Child = AppIcon() is { } icon
                ? new Image { Source = icon, Margin = new Thickness(4), Stretch = Stretch.Uniform, IsHitTestVisible = false }
                : new TextBlock
                {
                    Text = "✦", Foreground = ThemeCatalog.PaletteBrush("Accent"), FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI")
                }
        };
        var face = new Border
        {
            Height = Size, CornerRadius = new CornerRadius(Size / 2), Margin = new Thickness(8),
            Padding = new Thickness(7, 0, 20, 0),
            Background = ThemeCatalog.PaletteBrush("WindowBack"),
            BorderBrush = ThemeCatalog.PaletteBrush("Stroke"),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.45 },
            Cursor = Cursors.Hand,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    badge,
                    new TextBlock
                    {
                        Text = name, Foreground = ThemeCatalog.PaletteBrush("Fg"), FontSize = 13.5, FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false
                    }
                }
            }
        };
        ToolTip = $"{name}: click to open";
        Content = new Grid { Children = { face, _dot } };
        _dot.Margin = new Thickness(0, 4, 4, 0);

        // A press that moves more than a few pixels is a drag; one that doesn't is a click. The window is moved by hand rather than with
        // DragMove, which is unreliable for a window that never takes focus.
        MouseLeftButtonDown += (_, e) =>
        {
            _down = true;
            _dragged = false;
            _pressScreen = ScreenPoint(e);
            _startLeft = Left;
            _startTop = Top;
            CaptureMouse();
            e.Handled = true;
        };
        MouseMove += (_, e) =>
        {
            if (!_down) return;
            var delta = ScreenPoint(e) - _pressScreen;
            if (!_dragged && delta.Length > 4) _dragged = true;
            if (_dragged)
            {
                Left = _startLeft + delta.X;
                Top = _startTop + delta.Y;
            }
        };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!_down) return;
            _down = false;
            ReleaseMouseCapture();
            e.Handled = true;
            if (_dragged) Moved?.Invoke(Left, Top);
            else Clicked?.Invoke();
        };
        LostMouseCapture += (_, _) => _down = false;
    }

    private bool _down;
    private Point _pressScreen;

    // The pointer's place on the screen in device-independent units; it stays true while the window itself moves.
    private Point ScreenPoint(MouseEventArgs e)
    {
        var device = PointToScreen(e.GetPosition(this));
        return PresentationSource.FromVisual(this)?.CompositionTarget is { } target ? target.TransformFromDevice.Transform(device) : device;
    }

    private double _startLeft, _startTop;

    /// <summary>The app's own icon (the largest size in Assets\app.ico), or null if it can't be read.</summary>
    internal static ImageSource? AppIcon()
    {
        try
        {
            var decoder = new IconBitmapDecoder(new Uri("pack://application:,,,/Assets/app.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var best = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
            best.Freeze();
            return best;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or UriFormatException or System.Xml.XmlException)
        {
            return null;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GwlExStyle, style | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow);
    }

    /// <summary>Shows or clears the dot that says she has something to tell you.</summary>
    public void SetAttention(bool on) => _dot.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Puts it at a saved spot if that is still on a screen; otherwise at the right edge, a little above the bottom.</summary>
    public void Place(int? left, int? top)
    {
        UpdateLayout();
        var work = SystemParameters.WorkArea;
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (left is { } l && top is { } t && screen.Contains(new Point(l + 20, t + 20)))
        {
            Left = l;
            Top = t;
            return;
        }
        Left = work.Right - ActualWidth - 12;
        Top = work.Bottom - ActualHeight - 120;
    }
}
