using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace WinCompanion.Services;

/// <summary>
/// Reads the text the user has selected in whatever app is in front. It first asks the app through Windows UI Automation, which
/// leaves the clipboard alone; apps that don't support that get a Ctrl+C, after which the clipboard text is put back.
/// </summary>
public static class SelectionCapture
{
    private const int MaxChars = 8000;

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();

    private const byte VkControl = 0x11, VkMenu = 0x12, VkC = 0x43, VkA = 0x41;
    private const uint KeyUp = 0x0002;

    /// <summary>The selected text, or null when nothing is selected or it can't be read. Call before showing any window of ours.</summary>
    public static async Task<string?> GetSelectedTextAsync()
    {
        await WaitForKeysUpAsync(); // the user is still holding Ctrl+Alt+A

        var text = FromAutomation() ?? await FromCopyAsync();
        if (string.IsNullOrWhiteSpace(text)) return null;
        return Trim(text);
    }

    public static string Trim(string text) => text.Length <= MaxChars ? text : text[..MaxChars];

    private static string? FromAutomation()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused is not null && focused.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
            {
                var ranges = ((TextPattern)pattern).GetSelection();
                if (ranges.Length > 0)
                {
                    var text = ranges[0].GetText(MaxChars);
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or System.ComponentModel.Win32Exception) { }
        return null;
    }

    private static async Task<string?> FromCopyAsync()
    {
        // Never replace a picture or copied files: only text, or an empty clipboard, can be borrowed and given back.
        string? previous = null;
        try
        {
            if (Clipboard.ContainsText()) previous = Clipboard.GetText();
            else if (!IsClipboardEmpty()) return null;
        }
        catch (COMException) { return null; }

        var before = GetClipboardSequenceNumber();
        PressCtrlC();

        for (var i = 0; i < 12 && GetClipboardSequenceNumber() == before; i++) await Task.Delay(40);
        if (GetClipboardSequenceNumber() == before) return null; // nothing was selected

        string? copied = null;
        try { copied = Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch (COMException) { }

        try
        {
            if (previous is not null) Clipboard.SetText(previous);
            else Clipboard.Clear();
        }
        catch (COMException) { /* clipboard busy: the borrowed text stays, which is harmless */ }
        return copied;
    }

    private static bool IsClipboardEmpty() => !Clipboard.ContainsText() && !Clipboard.ContainsImage()
        && !Clipboard.ContainsFileDropList() && !Clipboard.ContainsAudio() && Clipboard.GetDataObject()?.GetFormats().Length is null or 0;

    private static void PressCtrlC()
    {
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        keybd_event(VkC, 0, 0, UIntPtr.Zero);
        keybd_event(VkC, 0, KeyUp, UIntPtr.Zero);
        keybd_event(VkControl, 0, KeyUp, UIntPtr.Zero);
    }

    // Sending Ctrl+C while the shortcut's own Ctrl and Alt are held would arrive as Ctrl+Alt+C.
    private static async Task WaitForKeysUpAsync()
    {
        for (var i = 0; i < 40 && (Down(VkControl) || Down(VkMenu) || Down(VkA)); i++) await Task.Delay(25);

        static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
    }
}
