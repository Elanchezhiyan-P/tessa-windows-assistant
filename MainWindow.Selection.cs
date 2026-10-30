using System.Windows;
using System.Windows.Controls;
using WinCompanion.Services;

namespace WinCompanion;

// Ctrl+Alt+A: select text in any app, press the key, and ask her to explain, fix, shorten or translate it.
public partial class MainWindow
{
    private const string DefaultPlaceholder = "Ask anything…   (Ctrl+Alt+V to talk)";
    private static readonly TimeSpan SelectionLifetime = TimeSpan.FromMinutes(10);

    private string? _selection;
    private DateTime _selectionTime;

    private bool HasSelection => _selection is not null && DateTime.Now - _selectionTime < SelectionLifetime;

    /// <summary>Reads the selection from the app in front, then shows the choices. The text is read before our window appears.</summary>
    public async void AskAboutSelection()
    {
        string? text = null;
        try { text = await SelectionCapture.GetSelectedTextAsync(); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { }

        ShowAndFocus();
        if (text is null)
        {
            AddMessage("assistant", $"I couldn't read any selected text. Select some text in an app, then press Ctrl+Alt+A.");
            return;
        }

        _selection = text;
        _selectionTime = DateTime.Now;
        AddMessage("assistant", $"You selected {text.Length} characters: “{SelectionPrompts.Snippet(text)}”\nPick what to do with it, or type your own question.");

        Chips.Children.Clear();
        foreach (var action in SelectionPrompts.All) AddSelectionChip(action);
        var done = new Button { Content = "Done", Style = (Style)FindResource("Chip") };
        done.Click += (_, _) => ClearSelection();
        Chips.Children.Add(done);
        Placeholder.Text = "Ask about the selected text…";
        Input.Focus();
    }

    private void AddSelectionChip(SelectionAction action)
    {
        var chip = new Button { Content = action.Label, Style = (Style)FindResource("Chip") };
        chip.Click += async (_, _) => await RunSelectionAsync(action);
        Chips.Children.Add(chip);
    }

    private void ClearSelection()
    {
        _selection = null;
        Placeholder.Text = DefaultPlaceholder;
        RefreshChips();
    }

    /// <summary>
    /// One stateless request with the selected text. It runs without tools, so text that tries to give orders can't make her do anything.
    /// </summary>
    private async Task RunSelectionAsync(SelectionAction action)
    {
        if (!HasSelection)
        {
            ClearSelection();
            AddMessage("assistant", "That selection has expired. Select the text again and press Ctrl+Alt+A.");
            return;
        }
        var selection = _selection!;

        var client = EnsureClient();
        if (client is null) return;
        if (client.IsCloud && !await _ctx.AllowCloudAsync("the text you selected"))
        {
            AddMessage("assistant", "Okay, I won't send the selected text.");
            return;
        }

        AddMessage("user", $"{action.Label}: “{SelectionPrompts.Snippet(selection, 80)}”");
        SetBusy(true);
        try
        {
            var reply = (await client.GenerateAsync(SelectionPrompts.Instruction(action), selection)).Trim();
            if (reply.Length == 0) reply = "(no reply)";
            else if (action.Transforms && TryCopy(reply)) reply += "\n\n_Copied to your clipboard, ready to paste back._";
            AddMessage("assistant", reply);
        }
        catch (LlmException ex) { AddMessage("error", ex.Message); }
        catch (Exception ex) { AddMessage("error", $"Something went wrong: {ex.Message}"); }
        finally { SetBusy(false); }
    }

    private static bool TryCopy(string text)
    {
        try { Clipboard.SetText(text); return true; }
        catch (System.Runtime.InteropServices.COMException) { return false; }
    }
}
