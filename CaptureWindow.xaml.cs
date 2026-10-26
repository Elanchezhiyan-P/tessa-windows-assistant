using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using WinCompanion.Tools;
using WinCompanion.UI;

namespace WinCompanion;

/// <summary>A small box opened by a global hotkey that saves a note or a to-do to the notes file without opening the chat.</summary>
public partial class CaptureWindow : Window
{
    private bool _backdropApplied;

    public CaptureWindow()
    {
        InitializeComponent();
        Deactivated += (_, _) => { if (IsVisible) Hide(); };
    }

    /// <summary>Shows the box in the middle of the screen's upper part, ready to type.</summary>
    public void Open()
    {
        if (!_backdropApplied) { Backdrop.Apply(this, Surface); _backdropApplied = true; }
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Top + area.Height * 0.22;
        Box.Clear();
        Result.Text = "";
        Show();
        Activate();
        Box.Focus();
    }

    private void Box_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Hide(); return; }
        if (e.Key != Key.Enter) return;

        var (text, todo) = NotesTools.ParseCapture(Box.Text);
        if (text.Length == 0) return;
        try
        {
            NotesTools.Append(text, todo);
            Result.Text = todo ? "Saved to your to-dos." : "Saved to your notes.";
            Box.Clear();
            // Close shortly after, so the confirmation is seen.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            timer.Tick += (_, _) => { timer.Stop(); Hide(); };
            timer.Start();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Result.Text = "Couldn't save: " + ex.Message;
        }
    }
}
