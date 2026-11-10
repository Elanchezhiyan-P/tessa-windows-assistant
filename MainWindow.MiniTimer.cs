using System.Windows.Threading;
using WinCompanion.Services;
using WinCompanion.UI;

namespace WinCompanion;

// The small floating countdown for focus sessions and reminders that are close.
public partial class MainWindow
{
    private MiniTimerWindow? _miniTimer;
    private DispatcherTimer? _miniTick;
    private IReadOnlyList<Reminder> _miniPending = [];
    private int _miniVersion = -1;
    private DateTime _miniLoaded = DateTime.MinValue;

    private void StartMiniTimer()
    {
        _miniTick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _miniTick.Tick += (_, _) => UpdateMiniTimer();
        _miniTick.Start();
    }

    private void UpdateMiniTimer()
    {
        if (_miniTick is null) return;
        var now = DateTime.Now;

        var state = MiniTimerState.Hidden;
        if (_appSettings.MiniTimer && !PetController.IsFullScreenAppRunning())
        {
            // Look at the reminder list again only when it changed (or now and then, in case a reminder fired).
            if (_miniVersion != ReminderStore.Version || now - _miniLoaded > TimeSpan.FromSeconds(30))
            {
                _miniPending = _reminders.Pending();
                _miniVersion = ReminderStore.Version;
                _miniLoaded = now;
            }
            state = MiniTimerText.Build(now, _focus.CurrentWork(now), _miniPending);
        }

        if (state.Kind == MiniTimerKind.None)
        {
            _miniTimer?.Hide();
            return;
        }

        if (_miniTimer is null)
        {
            _miniTimer = new MiniTimerWindow();
            _miniTimer.Moved += (left, top) =>
            {
                _appSettings.MiniTimerLeft = (int)left;
                _appSettings.MiniTimerTop = (int)top;
                _appSettings.Save();
            };
            _miniTimer.StopFocusRequested += () =>
            {
                _focus.Stop();
                _miniVersion = -1;
                UpdateMiniTimer();
            };
            _miniTimer.HideRequested += () =>
            {
                _appSettings.MiniTimer = false;
                _appSettings.Save();
                UpdateMiniTimer();
            };
            _miniTimer.SetState(state);
            _miniTimer.Place(_appSettings.MiniTimerLeft, _appSettings.MiniTimerTop);
        }

        _miniTimer.SetState(state);
        if (!_miniTimer.IsVisible) _miniTimer.Show();
    }

    private string TimerCommand(string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "":
                return _appSettings.MiniTimer
                    ? "The floating timer is on. It shows while a focus session runs or a reminder is less than an hour away. /timer off hides it; /timer reset moves it back to the corner."
                    : "The floating timer is off. Type /timer on to show it.";
            case "on":
                _appSettings.MiniTimer = true;
                break;
            case "off":
                _appSettings.MiniTimer = false;
                break;
            case "reset":
                _appSettings.MiniTimerLeft = null;
                _appSettings.MiniTimerTop = null;
                _miniTimer?.Place(null, null);
                _appSettings.Save();
                return "Moved the floating timer back to the bottom right.";
            default:
                return "Use /timer on, /timer off or /timer reset.";
        }
        _appSettings.Save();
        _miniVersion = -1;
        UpdateMiniTimer();
        return _appSettings.MiniTimer ? "The floating timer is on." : "The floating timer is off.";
    }
}
