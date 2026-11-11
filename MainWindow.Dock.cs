using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WinCompanion.Tools;
using WinCompanion.UI;

namespace WinCompanion;

// Clicking another app shrinks the window to a small floating button, so she never blocks the screen; one click brings her back.
public partial class MainWindow
{
    private FloatingDock? _dock;
    private DispatcherTimer? _collapseTimer;
    private bool _collapsedByFocus;

    private void InitDock()
    {
        Deactivated += (_, _) => StartCollapseCheck();
        Activated += (_, _) => _collapseTimer?.Stop();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _collapsedByFocus = false;
                _dock?.Hide();
                _dock?.SetAttention(false);
            }
        };
    }

    // Focus often moves for a moment (a pop-up, a dialog, one of her own windows), so look again shortly before collapsing.
    private void StartCollapseCheck()
    {
        if (!_appSettings.FloatWhenInactive) return;
        _collapseTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _collapseTimer.Tick -= CollapseTick;
        _collapseTimer.Tick += CollapseTick;
        _collapseTimer.Start();
    }

    private void CollapseTick(object? sender, EventArgs e)
    {
        _collapseTimer?.Stop();
        if (ShouldCollapse()) CollapseToDock();
    }

    private bool ShouldCollapse()
    {
        if (!_appSettings.FloatWhenInactive || !IsVisible || IsActive) return false;
        if (ChatsPopup.IsOpen || _listeningActive) return false;
        // One of her own windows (setup, tour, the snip box, a character) took the focus: not another app.
        if (Application.Current.Windows.OfType<Window>().Any(w => w != this && w != _dock && w != _miniTimer && w.IsActive)) return false;
        // A file or folder dialog is open over her: the window is disabled while it is.
        var hwnd = new WindowInteropHelper(this).Handle;
        return hwnd == IntPtr.Zero || NativeMethods.IsWindowEnabled(hwnd);
    }

    private void CollapseToDock()
    {
        if (_dock is null)
        {
            _dock = new FloatingDock(_appSettings.AssistantName);
            _dock.Clicked += () => { _dock.SetAttention(false); ShowAndFocus(); };
            _dock.Moved += (left, top) =>
            {
                _appSettings.DockLeft = (int)left;
                _appSettings.DockTop = (int)top;
                _appSettings.Save();
            };
            _dock.Show();
            _dock.Place(_appSettings.DockLeft, _appSettings.DockTop);
        }
        _collapsedByFocus = true;
        // A hidden window keeps everything as it is: the chat, the scroll position, an open settings page.
        Hide();
        UpdateDock();
    }

    // The dock stays while she is collapsed, and steps aside for a full-screen app or game.
    private void UpdateDock()
    {
        if (_dock is null) return;
        var show = _collapsedByFocus && !IsVisible && _appSettings.FloatWhenInactive && !PetController.IsFullScreenAppRunning();
        if (show && !_dock.IsVisible) _dock.Show();
        else if (!show && _dock.IsVisible) _dock.Hide();
    }

    // Something arrived while she is a button: a dot says so.
    private void NoteForDock()
    {
        if (_collapsedByFocus && !IsVisible) _dock?.SetAttention(true);
    }

    private string FloatCommand(string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "":
                return _appSettings.FloatWhenInactive
                    ? "I shrink to a small floating button when you click another app. /float off keeps the window where it is; /float reset moves the button back to the edge."
                    : "I stay as a window when you click another app. Type /float on to shrink to a floating button instead.";
            case "on":
                _appSettings.FloatWhenInactive = true;
                break;
            case "off":
                _appSettings.FloatWhenInactive = false;
                _collapsedByFocus = false;
                UpdateDock();
                break;
            case "reset":
                _appSettings.DockLeft = null;
                _appSettings.DockTop = null;
                _dock?.Place(null, null);
                _appSettings.Save();
                return "Moved the floating button back to the right edge.";
            default:
                return "Use /float on, /float off or /float reset.";
        }
        _appSettings.Save();
        return _appSettings.FloatWhenInactive ? "I will shrink to a floating button when you click another app." : "I will stay a window when you click another app.";
    }
}
