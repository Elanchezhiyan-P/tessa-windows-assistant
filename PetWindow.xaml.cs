using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using WinCompanion.Services;
using WinCompanion.Tools;
using WinCompanion.UI;

namespace WinCompanion;

/// <summary>
/// A character that comes onto the screen, shows a reminder in a speech bubble, then leaves.
/// Walkers pace along the top of the taskbar; flyers glide across the upper part of the screen.
/// The window is transparent, so only the character and bubble are visible and clickable.
/// </summary>
public partial class PetWindow : Window
{
    private const double WindowWidth = 340, BubbleArea = 190, EdgeMargin = 12;
    private const double WalkSpeed = 150, RunSpeed = 260, FlySpeed = 220; // DIPs per second
    private const double SlideSeconds = 0.45;
    private static readonly TimeSpan PatienceLimit = TimeSpan.FromSeconds(90);

    private enum Phase { Entering, Waiting, Leaving }

    private readonly PetDefinition _pet;
    private readonly Action? _onSnooze, _onDone;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Random _random = new();
    private readonly Rect _area = SystemParameters.WorkArea;

    private Phase _phase = Phase.Entering;
    private double _x;                 // horizontal centre of the character, screen DIPs
    private double _targetX, _direction;
    private double _lastSeconds, _waitingSince, _flightBaseY;
    private double _facing = double.NaN;   // current mirror: 1 as drawn, -1 flipped
    private double _slide, _phaseStarted;  // sitters: how far below their resting place they are, and when the phase began
    private (System.Windows.Media.Imaging.BitmapSource[] Frames, double Fps) _animation;
    private string _animationName = "";
    private int _frameIndex = -1;

    public event Action? Finished;

    /// <param name="onSnooze">Null hides the Snooze button (used for the preview).</param>
    public PetWindow(PetDefinition pet, string message, Action? onSnooze, Action? onDone)
    {
        InitializeComponent();
        _pet = pet;
        _onSnooze = onSnooze;
        _onDone = onDone;

        MessageText.Text = message;
        SnoozeButton.Visibility = onSnooze is null ? Visibility.Collapsed : Visibility.Visible;

        Sprite.Width = pet.DisplayWidth;
        Sprite.Height = pet.DisplayHeight;
        RenderOptions.SetBitmapScalingMode(Sprite, pet.Smooth ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
        if (pet.Glow)
            Sprite.Effect = new DropShadowEffect { Color = Colors.White, BlurRadius = 6, ShadowDepth = 0, Opacity = 0.95 };
        Width = WindowWidth;
        Height = BubbleArea + pet.DisplayHeight;

        // Walkers keep the bubble above them; flyers fly high, so theirs hangs below.
        var bubbleRow = pet.IsFlyer ? 1 : 0;
        var spriteRow = pet.IsFlyer ? 0 : 1;
        Layout.RowDefinitions[bubbleRow].Height = new GridLength(BubbleArea);
        Layout.RowDefinitions[spriteRow].Height = GridLength.Auto;
        Grid.SetRow(BubbleHost, bubbleRow);
        Grid.SetRow(Sprite, spriteRow);
        if (pet.IsFlyer)
        {
            BubbleHost.VerticalAlignment = VerticalAlignment.Top;
            Sprite.VerticalAlignment = VerticalAlignment.Top;
            BubbleHost.Children.Clear();
            BubbleHost.Children.Add(Tail);   // tail on top, pointing up at the bird
            BubbleHost.Children.Add(Bubble);
            Tail.Points = new PointCollection { new Point(0, 10), new Point(18, 10), new Point(9, 0) };
        }

        BeginEntrance();
        _timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => { _timer.Stop(); Finished?.Invoke(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Never take focus from whatever the user is typing into; the buttons still work on click.
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GwlExStyle,
            style | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow);
    }

    // ---- Phases ------------------------------------------------------------------------------------

    private void BeginEntrance()
    {
        if (_pet.IsSitter)
        {
            // Appear at a comfortable spot along the bottom and rise into view.
            var margin = WindowWidth / 2 + EdgeMargin;
            _x = _area.Left + margin + _random.NextDouble() * Math.Max(1, _area.Width - 2 * margin);
            _slide = _pet.DisplayHeight + 60;
            SetAnimation("idle");
            Place();
            return;
        }

        var fromLeft = _random.Next(2) == 0;
        _direction = fromLeft ? 1 : -1;
        _x = fromLeft ? _area.Left - _pet.DisplayWidth : _area.Right + _pet.DisplayWidth;

        // Stop somewhere comfortable, far enough from the edges for the whole bubble to fit.
        var half = WindowWidth / 2 + EdgeMargin;
        _targetX = _area.Left + half + _random.NextDouble() * Math.Max(1, _area.Width - 2 * half);
        _flightBaseY = _area.Top + _area.Height * 0.10;

        SetAnimation(_pet.IsFlyer ? "fly" : _pet.Has("run") ? "run" : "walk");
        Place();
    }

    private void Tick()
    {
        var seconds = _clock.Elapsed.TotalSeconds;
        var dt = Math.Min(seconds - _lastSeconds, 0.1); // a stall (sleep, debugger) must not teleport the character
        _lastSeconds = seconds;

        switch (_phase)
        {
            case Phase.Entering:
                if (_pet.IsSitter) { Slide(seconds, rising: true); break; }
                Step(dt, SpeedFor());
                if ((_direction > 0 && _x >= _targetX) || (_direction < 0 && _x <= _targetX)) Arrive(seconds);
                break;

            case Phase.Waiting:
                if (seconds - _waitingSince > PatienceLimit.TotalSeconds) Leave("gave up waiting"); // the reminder stays in the chat history
                break;

            case Phase.Leaving:
                if (_pet.IsSitter) { Slide(seconds, rising: false); break; }
                Step(dt, SpeedFor());
                if (_x < _area.Left - _pet.DisplayWidth - 20 || _x > _area.Right + _pet.DisplayWidth + 20) Close();
                break;
        }

        Animate(seconds);
        Place();
    }

    private void Slide(double seconds, bool rising)
    {
        var t = Math.Clamp((seconds - _phaseStarted) / SlideSeconds, 0, 1);
        var eased = 1 - Math.Pow(1 - t, 3); // ease-out
        var travel = _pet.DisplayHeight + 60;
        _slide = rising ? travel * (1 - eased) : travel * eased;
        if (t < 1) return;
        if (rising) Arrive(seconds);
        else Close();
    }

    private double SpeedFor() =>
        _pet.Speed > 0 ? _pet.Speed : _pet.IsFlyer ? FlySpeed : _pet.Has("run") ? RunSpeed : WalkSpeed;

    private void Step(double dt, double speed)
    {
        _x += _direction * speed * dt;
        SetFacing(_direction);
    }

    private void Arrive(double seconds)
    {
        _phase = Phase.Waiting;
        _waitingSince = seconds;
        SetAnimation(_pet.IsFlyer ? "fly" : _pet.Has("alert") ? "alert" : "idle");

        BubbleHost.Visibility = Visibility.Visible;
        BubbleHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
    }

    private void Leave(string reason)
    {
        if (_phase == Phase.Leaving) return;
        LogLeaving(reason);
        _phase = Phase.Leaving;
        BubbleHost.Visibility = Visibility.Collapsed;

        _phaseStarted = _clock.Elapsed.TotalSeconds;
        if (_pet.IsSitter) return; // it simply sinks back down

        // Head for whichever edge is nearer.
        _direction = _x < _area.Left + _area.Width / 2 ? -1 : 1;
        SetAnimation(_pet.IsFlyer ? "fly" : _pet.Has("run") ? "run" : "walk");
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        _onDone?.Invoke();
        Leave("Done clicked");
    }

    private void Snooze_Click(object sender, RoutedEventArgs e)
    {
        _onSnooze?.Invoke();
        Leave("Snooze clicked");
    }

    // A one-line trail of why the character left, to make "it vanished" reports easy to explain.
    private void LogLeaving(string reason)
    {
        try
        {
            File.AppendAllText(AppPaths.FileIn("pet.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}	{_pet.Id}	{reason}	(waited {_clock.Elapsed.TotalSeconds - _waitingSince:0.0}s){Environment.NewLine}");
        }
        catch (IOException) { }
    }

    // ---- Drawing -------------------------------------------------------------------------------------

    private void SetFacing(double direction)
    {
        // The art faces one way; mirror it when travelling the other way.
        var target = (direction > 0) == _pet.ArtFacesLeft ? -1.0 : 1.0;
        if (target == _facing) return;

        var firstTime = double.IsNaN(_facing);
        _facing = target;
        if (firstTime)
        {
            Flip.ScaleX = target; // entering: already facing the way it walks, no turn needed
            return;
        }

        // Turning round: squeeze through edge-on and out the other side instead of snapping.
        Flip.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        });
    }

    private void SetAnimation(string name)
    {
        if (name == _animationName) return;
        _animationName = name;
        _animation = _pet.Get(name);
        _frameIndex = -1;
    }

    private void Animate(double seconds)
    {
        // A single standing frame gets a slow breathing motion so it doesn't look frozen.
        Breath.ScaleY = _phase == Phase.Waiting && _animation.Frames.Length == 1 ? 1 + 0.012 * Math.Sin(seconds * 2.2) : 1;

        var index = (int)(seconds * _animation.Fps * _pet.Playback) % _animation.Frames.Length;
        if (index == _frameIndex) return;
        _frameIndex = index;
        Sprite.Source = _animation.Frames[index];
    }

    private void Place()
    {
        Left = _x - WindowWidth / 2;

        if (_pet.IsFlyer)
        {
            // Glide on a gentle wave; hover and bob while waiting.
            var t = _clock.Elapsed.TotalSeconds;
            Top = _flightBaseY + Math.Sin(t * (_phase == Phase.Waiting ? 2.2 : 3.0)) * (_phase == Phase.Waiting ? 8 : 22);
        }
        else
        {
            Top = _area.Bottom - Height + _slide; // feet on the top edge of the taskbar (sitters rise from below it)
        }
    }
}
