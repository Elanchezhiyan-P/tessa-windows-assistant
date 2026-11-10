using System.Windows;
using WinCompanion.Services;

namespace WinCompanion;

// "This PC" in Settings, the model suggestion under the local model box, and /system.
public partial class MainWindow
{
    // Read once and kept until the user presses Check again: the hardware doesn't change while the app runs.
    private SystemSpecs? _specs;

    private SystemSpecs Specs => _specs ??= SystemInfo.Collect();

    /// <summary>Fills the This PC tab (and the hint under the local model) when the user allows it, else waits for the button.</summary>
    private void ShowSystemPanel()
    {
        CheckSystemSwitch.IsChecked = _appSettings.CheckSystem;
        if (_appSettings.CheckSystem || _specs is not null) FillSystemPanel(Specs);
        else
        {
            SystemText.Text = "Not checked yet. Press “Check this PC” to see your memory, processor, graphics and disks.";
            SystemAdvice.Text = "";
            LocalAdvice.Text = "";
        }
    }

    private void FillSystemPanel(SystemSpecs specs)
    {
        var advice = ModelAdvice.Recommend(specs);
        SystemText.Text = SystemInfo.Describe(specs);
        SystemAdvice.Text = advice.Text;
        LocalAdvice.Text = advice.Text;
        SystemRefreshButton.Content = "Check again";
    }

    private void SystemRefresh_Click(object sender, RoutedEventArgs e)
    {
        _specs = SystemInfo.Collect();
        FillSystemPanel(_specs);
    }

    private void SystemCopy_Click(object sender, RoutedEventArgs e)
    {
        var text = $"{_appSettings.AssistantName} {AboutTessa.Version}\n{SystemInfo.Describe(Specs)}";
        try
        {
            Clipboard.SetText(text);
            SettingsNote.Text = "System info copied. Paste it into a bug report.";
        }
        catch (System.Runtime.InteropServices.COMException) { SettingsNote.Text = "The clipboard is busy; try again."; }
    }

    private async void UpdateCheck_Click(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        SettingsNote.Text = "Checking GitHub…";
        try { SettingsNote.Text = await UpdateCheck.CheckAsync(); }
        finally { UpdateButton.IsEnabled = true; }
    }

    private void DiagnosticsCopy_Click(object sender, RoutedEventArgs e) => SettingsNote.Text = CopyDiagnostics();

    // Ctrl+F opens a search of the whole chat history: it fills the box with "/search " ready for the words.
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.F || System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Control) return;
        if (_settingsOpen) return;
        Input.Text = "/search ";
        Input.CaretIndex = Input.Text.Length;
        Input.Focus();
        e.Handled = true;
    }

    private string SystemReport()
    {
        _specs = SystemInfo.Collect(); // a fresh reading: free memory changes
        return SystemInfo.Describe(_specs) + "\n\n" + ModelAdvice.Recommend(_specs).Text;
    }
}
