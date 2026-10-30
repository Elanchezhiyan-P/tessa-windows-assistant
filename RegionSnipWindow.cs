using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinCompanion.Services;
using WinCompanion.UI;

namespace WinCompanion;

/// <summary>
/// A full-screen, dimmed copy of the desktop on which the user drags a box. The picture is a screenshot taken a moment earlier,
/// so nothing moves while they choose. <see cref="Selection"/> is the box in the screenshot's pixels, or null if they cancelled.
/// </summary>
public sealed class RegionSnipWindow : Window
{
    private readonly Bitmap _shot;
    private readonly Canvas _canvas = new();
    private readonly System.Windows.Shapes.Path _dim = new() { IsHitTestVisible = false, Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x99, 0, 0, 0)) };
    private readonly Border _box = new() { IsHitTestVisible = false, BorderThickness = new Thickness(2), Visibility = Visibility.Collapsed };
    private System.Windows.Point? _start;

    public System.Drawing.Rectangle? Selection { get; private set; }

    public RegionSnipWindow(Bitmap shot, System.Drawing.Rectangle screenBounds)
    {
        _shot = shot;

        // Windows measures a system-DPI-aware app in units of 1/96 inch, so the screen's pixels are divided by the scale.
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        var scale = g.DpiX / 96.0;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = screenBounds.X / scale;
        Top = screenBounds.Y / scale;
        Width = screenBounds.Width / scale;
        Height = screenBounds.Height / scale;
        Background = System.Windows.Media.Brushes.Black;
        Cursor = Cursors.Cross;

        _box.BorderBrush = new SolidColorBrush(ThemeCatalog.CurrentAccent);
        var hint = new TextBlock
        {
            Text = "Drag a box around the text. Esc or right-click to cancel.",
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 15,
            Padding = new Thickness(14, 8, 14, 8),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xCC, 0x12, 0x13, 0x19)),
            IsHitTestVisible = false
        };

        _canvas.Children.Add(new System.Windows.Controls.Image { Source = ToImageSource(shot), Stretch = Stretch.Fill, IsHitTestVisible = false });
        _canvas.Children.Add(_dim);
        _canvas.Children.Add(_box);
        _canvas.Children.Add(hint);
        Canvas.SetLeft(hint, 24);
        Canvas.SetTop(hint, 24);
        Content = _canvas;
        SizeChanged += (_, _) => RedrawDim(null);

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        MouseRightButtonDown += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            // Match the screenshot to the window exactly, so a drag lines up with the pixels beneath it.
            _canvas.Children[0].SetValue(WidthProperty, ActualWidth);
            _canvas.Children[0].SetValue(HeightProperty, ActualHeight);
            RedrawDim(null);
        };
    }

    private static BitmapSource ToImageSource(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(_canvas);
        CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_start is not { } start) return;
        var now = e.GetPosition(_canvas);
        var rect = new Rect(start, now);
        _box.Visibility = Visibility.Visible;
        Canvas.SetLeft(_box, rect.X);
        Canvas.SetTop(_box, rect.Y);
        _box.Width = rect.Width;
        _box.Height = rect.Height;
        RedrawDim(rect);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_start is not { } start) return;
        ReleaseMouseCapture();
        var end = e.GetPosition(_canvas);
        _start = null;

        Selection = RegionOcr.ToPixels(start.X, start.Y, end.X, end.Y, _canvas.ActualWidth, _canvas.ActualHeight, _shot.Width, _shot.Height);
        if (Selection is null)
        {
            // Just a click: let them try again.
            _box.Visibility = Visibility.Collapsed;
            RedrawDim(null);
            return;
        }
        Close();
    }

    // The screen is dimmed everywhere except inside the chosen box.
    private void RedrawDim(Rect? hole)
    {
        var all = new RectangleGeometry(new Rect(0, 0, _canvas.ActualWidth, _canvas.ActualHeight));
        _dim.Data = hole is { } h
            ? new CombinedGeometry(GeometryCombineMode.Exclude, all, new RectangleGeometry(h))
            : all;
    }
}
