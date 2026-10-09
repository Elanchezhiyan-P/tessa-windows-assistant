using System.Diagnostics;
using System.Windows;
using WinCompanion.Services;
using WinCompanion.UI;

namespace WinCompanion;

/// <summary>The tray icon's right-click menu, styled like the main window instead of a classic context menu.</summary>
public partial class TrayFlyout : Window
{
    private readonly MainWindow _main;
    private bool _backdropApplied;

    public TrayFlyout(MainWindow main)
    {
        InitializeComponent();
        _main = main;
    }

    /// <param name="cursorPixels">Cursor position in device pixels; the flyout opens above and left of it.</param>
    public void ShowAt(System.Drawing.Point cursorPixels)
    {
        if (!_backdropApplied)
        {
            Backdrop.Apply(this, (System.Windows.Controls.Border)Surface);
            _backdropApplied = true;
        }

        var name = AppSettings.Current.AssistantName;
        Title = $"{name} menu";
        OpenItem.Content = $"Open {name}";
        WakeSwitch.Content = $"Wake word “{AppSettings.Current.WakeLabel}”";

        Sync(() =>
        {
            WakeSwitch.IsChecked = _main.WakeWordEnabled;
            StartupSwitch.IsChecked = StartupManager.IsEnabled;
        });

        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        var area = SystemParameters.WorkArea;
        Show();
        UpdateLayout();

        Left = Math.Clamp(cursorPixels.X / dpi.DpiScaleX - Width + 12, area.Left + 8, area.Right - Width - 8);
        Top = Math.Max(area.Top + 8, area.Bottom - ActualHeight - 12);
        Activate();
    }

    private void Window_Deactivated(object? sender, EventArgs e) => Hide();

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _main.ShowAndFocus();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _main.OpenSettings();
    }

    // The switches react to any change (mouse, keyboard, or a screen reader flipping them), not just a mouse click.
    // _syncing marks changes the program makes itself, so setting a switch from code doesn't trigger its action again.
    private bool _syncing;
    private void Sync(Action change)
    {
        _syncing = true;
        try { change(); }
        finally { _syncing = false; }
    }

    private void Wake_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        if (!_main.SetWakeWord(WakeSwitch.IsChecked == true)) Sync(() => WakeSwitch.IsChecked = false);
    }

    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        try { StartupManager.SetEnabled(StartupSwitch.IsChecked == true); }
        catch (Exception ex)
        {
            Sync(() => StartupSwitch.IsChecked = StartupManager.IsEnabled);
            MessageBox.Show(ex.Message, AppSettings.Current.AssistantName);
        }
    }

    private void Log_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        ActionLog.EnsureExists();
        Process.Start(new ProcessStartInfo(ActionLog.FilePath) { UseShellExecute = true });
    }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        Process.Start(new ProcessStartInfo(AppPaths.Dir) { UseShellExecute = true });
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
