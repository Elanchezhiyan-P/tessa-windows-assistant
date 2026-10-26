using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WinCompanion.Services;

/// <summary>
/// Notices when text is copied and records it in <see cref="ClipboardStore"/>, but only while the setting is on.
/// Content that password managers mark as private ("exclude from history") is never recorded.
/// </summary>
public sealed class ClipboardWatcher : IDisposable
{
    private const int WmClipboardUpdate = 0x031D;

    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private readonly Func<bool> _enabled;
    private HwndSource? _source;
    private IntPtr _handle;

    public ClipboardWatcher(Func<bool> enabled) => _enabled = enabled;

    public void Start(Window window)
    {
        _handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(Hook);
        AddClipboardFormatListener(_handle);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmClipboardUpdate && _enabled()) Capture();
        return IntPtr.Zero;
    }

    private static void Capture()
    {
        // The clipboard is often busy for a moment right after a copy, so try a few times.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!Clipboard.ContainsText()) return;
                if (IsPrivate()) return;
                ClipboardStore.Add(Clipboard.GetText());
                return;
            }
            catch (COMException) { Thread.Sleep(40); }
            catch (ExternalException) { Thread.Sleep(40); }
        }
    }

    // Password managers set these to ask that the copy is not recorded or synced.
    private static bool IsPrivate()
    {
        if (Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing")) return true;
        if (Clipboard.GetData("CanIncludeInClipboardHistory") is byte[] { Length: >= 4 } flag && BitConverter.ToInt32(flag, 0) == 0) return true;
        return false;
    }

    public void Dispose()
    {
        _source?.RemoveHook(Hook);
        if (_handle != IntPtr.Zero) RemoveClipboardFormatListener(_handle);
    }
}
