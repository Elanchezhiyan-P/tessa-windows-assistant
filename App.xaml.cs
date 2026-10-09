using System.Windows;
using System.Windows.Interop;
using WinCompanion.Services;
using WinCompanion.UI;

namespace WinCompanion;

public partial class App : System.Windows.Application
{
    private System.Windows.Forms.NotifyIcon? _tray;
    private MainWindow? _window;
    private TrayFlyout? _flyout;
    private HotKey? _hotKey, _talkKey;
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        InstallCrashHandlers();

        // Only one copy may run: a second one would fight over the hotkeys and tray icon.
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\WinCompanion.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        // First run: ask what to call the user and what to call her, before anything else appears.
        var firstRun = false;
        var saved = AppSettings.Load();
        if (!saved.SetupDone && Environment.GetEnvironmentVariable("WINCOMPANION_SKIP_SETUP") != "1")
        {
            new SetupWindow(saved).ShowDialog();
            firstRun = true;
        }

        _window = new MainWindow(new SettingsStore());
        _flyout = new TrayFlyout(_window);

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = TrayIcon.Create(),
            Text = TrayText(),
            Visible = true
        };
        _window.IdentityChanged += () => _tray.Text = TrayText();
        _tray.MouseUp += (_, args) =>
        {
            if (args.Button == System.Windows.Forms.MouseButtons.Left) ToggleWindow();
            else if (args.Button == System.Windows.Forms.MouseButtons.Right)
                _flyout.ShowAt(System.Windows.Forms.Cursor.Position);
        };

        // Reminders pop a balloon from the tray icon too, in case the window is hidden.
        _window.ReminderFired += (title, text) =>
            _tray?.ShowBalloonTip(10000, title, text, System.Windows.Forms.ToolTipIcon.Info);

        // The hotkeys need a window handle, so create one without showing the window.
        var helper = new WindowInteropHelper(_window);
        helper.EnsureHandle();
        _hotKey = TryRegister(helper.Handle, System.Windows.Forms.Keys.Space, ToggleWindow, "Ctrl+Alt+Space");

        // Push-to-talk: press, then speak your request.
        _talkKey = TryRegister(helper.Handle, System.Windows.Forms.Keys.V, _window.StartListening, "Ctrl+Alt+V");

        if (firstRun) _window.Welcome();

        // Handy for testing and for launching straight into a view.
        if (e.Args.Contains("--settings")) _window.OpenSettings();
        else if (e.Args.Contains("--show")) _window.ShowAndFocus();

        if (e.Args.Contains("--flyout"))
            _flyout.ShowAt(new System.Drawing.Point(System.Windows.Forms.Screen.PrimaryScreen!.Bounds.Width / 2, 0));

        var pet = Array.IndexOf(e.Args, "--pet");
        if (pet >= 0 && pet + 1 < e.Args.Length) _window.PreviewPet(e.Args[pet + 1]);
    }

    private static string TrayText() => $"{AppSettings.Current.AssistantName}  (Ctrl+Alt+Space, Ctrl+Alt+V to talk)";

    // Another program may already own a shortcut. Say so, and carry on: the tray icon still works.
    private HotKey? TryRegister(IntPtr handle, System.Windows.Forms.Keys key, Action pressed, string label)
    {
        try
        {
            var hotKey = new HotKey(handle, HotKey.Modifiers.Ctrl | HotKey.Modifiers.Alt, key);
            hotKey.Pressed += pressed;
            return hotKey;
        }
        catch (InvalidOperationException)
        {
            _tray?.ShowBalloonTip(8000, AppSettings.Current.AssistantName, $"{label} is already used by another program. Use the tray icon instead.",
                System.Windows.Forms.ToolTipIcon.Warning);
            return null;
        }
    }

    // Anything unexpected is written to crash.log; the app keeps running rather than disappearing.
    private bool _crashShown;
    private void InstallCrashHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLog.Write("UI thread", args.Exception);
            args.Handled = true;
            NotifyOnce();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => CrashLog.Write("Unhandled (fatal)", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLog.Write("Background task", args.Exception);
            args.SetObserved();
        };
    }

    private void NotifyOnce()
    {
        if (_crashShown) return;
        _crashShown = true;
        _tray?.ShowBalloonTip(10000, $"{AppSettings.Current.AssistantName} hit a problem",
            $"It kept running. Details are in {CrashLog.FilePath}", System.Windows.Forms.ToolTipIcon.Warning);
    }

    private void ToggleWindow()
    {
        if (_window is null) return;
        if (_window.IsVisible && _window.IsActive) _window.Hide();
        else _window.ShowAndFocus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotKey?.Dispose();
        _talkKey?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
