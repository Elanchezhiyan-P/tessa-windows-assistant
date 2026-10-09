using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WinCompanion.Services;

/// <summary>Registers a system-wide hotkey against a window handle.</summary>
public sealed class HotKey : IDisposable
{
    [Flags]
    public enum Modifiers { Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

    private const int WmHotKey = 0x0312;
    private static int _nextId = 1;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly IntPtr _handle;
    private readonly int _id = _nextId++;

    public event Action? Pressed;

    public HotKey(IntPtr handle, Modifiers mods, System.Windows.Forms.Keys key)
    {
        _handle = handle;
        if (!RegisterHotKey(handle, _id, (uint)mods, (uint)key))
            throw new InvalidOperationException("Could not register hotkey (already in use?).");
        ComponentDispatcher.ThreadPreprocessMessage += OnMessage;
    }

    private void OnMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message == WmHotKey && (int)msg.wParam == _id)
        {
            Pressed?.Invoke();
            handled = true;
        }
    }

    public void Dispose()
    {
        ComponentDispatcher.ThreadPreprocessMessage -= OnMessage;
        UnregisterHotKey(_handle, _id);
    }
}
