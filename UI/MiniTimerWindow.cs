using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinCompanion.Services;
using WinCompanion.Tools;

namespace WinCompanion.UI;

/// <summary>
/// A small always-on-top pill with a countdown for the focus session or the next reminder. It never takes focus from what you
/// are typing into, can be dragged anywhere, and its position is remembered.
/// </summary>
internal sealed class MiniTimerWindow : Window
{
    private readonly TextBlock _icon = new() { FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _time = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _label = new()
    {
        FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 1, 0, 0), MaxWidth = 140, TextTrimming = TextTrimming.CharacterEllipsis
    };
    private readonly Border _bar = new() { Height = 3, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly Border _track = new() { Height = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 7, 0, 0) };
    private readonly MenuItem _stopFocus = new() { Header = "Stop the focus session" };

    /// <summary>Raised after the user drags it: the new left and top.</summary>
    public event Action<double, double>? Moved;
    public event Action? StopFocusRequested;
    public event Action? HideRequested;

    private const double BarWidth = 150;

    public MiniTimerWindow()
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
        Title = "Timer";
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");

        _icon.Foreground = ThemeCatalog.PaletteBrush("Fg");
        _time.Foreground = ThemeCatalog.PaletteBrush("Fg");
        _label.Foreground = ThemeCatalog.PaletteBrush("Muted");
        _track.Background = ThemeCatalog.PaletteBrush("SegmentBack");
        _bar.Background = ThemeCatalog.PaletteBrush("Accent");
        _track.Child = _bar;
        _track.Width = BarWidth;

        var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { _icon, _time, _label } };
        var menu = new ContextMenu { Items = { _stopFocus, HideItem() } };
        _stopFocus.Click += (_, _) => StopFocusRequested?.Invoke();

        Content = new Border
        {
            Margin = new Thickness(10),
            Padding = new Thickness(14, 9, 14, 9),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Background = ThemeCatalog.PaletteBrush("WindowBack"),
            BorderBrush = ThemeCatalog.PaletteBrush("Stroke"),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.4 },
            ContextMenu = menu,
            ToolTip = "Drag to move. Right-click for options.",
            Child = new StackPanel { Children = { row, _track } }
        };

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            try { DragMove(); }
            catch (InvalidOperationException) { return; }
            Moved?.Invoke(Left, Top);
        };
    }

    private MenuItem HideItem()
    {
        var hide = new MenuItem { Header = "Hide the timer (turn it back on with /timer on)" };
        hide.Click += (_, _) => HideRequested?.Invoke();
        return hide;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Never take focus from whatever the user is typing into.
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GwlExStyle, style | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow);
    }

    public void SetState(MiniTimerState state)
    {
        _icon.Text = state.Kind switch { MiniTimerKind.Focus => "🍅", MiniTimerKind.Break => "☕", _ => "⏰" };
        _time.Text = state.Time;
        _label.Text = state.Label;
        _track.Visibility = state.Progress >= 0 ? Visibility.Visible : Visibility.Collapsed;
        _bar.Width = state.Progress >= 0 ? BarWidth * Math.Clamp(state.Progress, 0, 1) : 0;
        _stopFocus.Visibility = state.Kind is MiniTimerKind.Focus or MiniTimerKind.Break ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Puts the window at a saved spot if it is still on a screen, otherwise at the bottom right above the taskbar.</summary>
    public void Place(int? left, int? top)
    {
        UpdateLayout();
        var work = SystemParameters.WorkArea;
        var virtualScreen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (left is { } l && top is { } t && virtualScreen.Contains(new Point(l + 20, t + 20)))
        {
            Left = l;
            Top = t;
            return;
        }
        Left = work.Right - ActualWidth - 16;
        Top = work.Bottom - ActualHeight - 16;
    }
}
