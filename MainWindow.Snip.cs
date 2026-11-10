using WinCompanion.Services;
using WinCompanion.Tools;

namespace WinCompanion;

// Ctrl+Alt+T or /snip: drag a box on the screen and the text in it goes to the clipboard.
public partial class MainWindow
{
    private bool _snipping;

    /// <summary>
    /// Dims the screen so the user can drag a box, reads the text in it with Windows' built-in text recognition (on this PC, offline)
    /// and copies it to the clipboard.
    /// </summary>
    public async void SnipText()
    {
        if (_snipping) return;
        _snipping = true;
        var wasVisible = IsVisible;
        try
        {
            // Our own window must not be in the picture.
            if (wasVisible)
            {
                Hide();
                await Task.Delay(300);
            }

            using var shot = RegionOcr.CaptureVirtualScreen(out var bounds);
            var overlay = new RegionSnipWindow(shot, bounds);
            overlay.ShowDialog();

            if (overlay.Selection is not { } area) return;

            var text = await ScreenText.ReadAsync(RegionOcr.CropToPng(shot, area));
            if (text.Length == 0)
            {
                Report("I couldn't find any text in that box. Try a slightly bigger box, or check that your Windows language has text recognition installed.", wasVisible);
                return;
            }

            try { System.Windows.Clipboard.SetText(text); }
            catch (System.Runtime.InteropServices.COMException)
            {
                Report("I read the text but the clipboard was busy. Try again.", wasVisible);
                return;
            }
            Report($"Copied {text.Length} character(s) to the clipboard: “{SelectionPrompts.Snippet(text, 60)}”", wasVisible);
            ReminderFired?.Invoke($"{_appSettings.AssistantName}: text copied", $"{text.Length} characters are on your clipboard.");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or IOException)
        {
            Report($"I couldn't read the screen: {ex.Message}", wasVisible);
        }
        finally
        {
            if (wasVisible) ShowAndFocus();
            _snipping = false;
        }
    }

    private void Report(string message, bool windowWasOpen)
    {
        AddMessage("assistant", message);
        if (!windowWasOpen) ReminderFired?.Invoke(_appSettings.AssistantName, message);
    }
}
