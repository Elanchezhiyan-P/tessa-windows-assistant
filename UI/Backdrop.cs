using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace WinCompanion.UI;

/// <summary>Gives a borderless window the Windows 11 look: acrylic backdrop, rounded corners, dark frame.</summary>
internal static class Backdrop
{
    private const int DwmaUseImmersiveDarkMode = 20;
    private const int DwmaWindowCornerPreference = 33;
    private const int DwmaSystemBackdropType = 38;
    private const int Windows11Build22H2 = 22621;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <param name="surface">The root border; it gets a tint over the acrylic (or a solid colour on older Windows).</param>
    public static void Apply(Window window, Border surface)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();

        SetFrame(hwnd);

        // WINCOMPANION_LEGACY_UI=1 forces the plain Windows 10 look, which lets that path be tested on Windows 11.
        if (Environment.OSVersion.Version.Build >= Windows11Build22H2 && Environment.GetEnvironmentVariable("WINCOMPANION_LEGACY_UI") != "1")
        {
            var rounded = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, DwmaWindowCornerPreference, ref rounded, sizeof(int));
            var acrylic = 3; // DWMSBT_TRANSIENTWINDOW
            DwmSetWindowAttribute(hwnd, DwmaSystemBackdropType, ref acrylic, sizeof(int));

            // The WPF surface must be transparent for the backdrop to show through.
            if (HwndSource.FromHwnd(hwnd) is { } source) source.CompositionTarget.BackgroundColor = Colors.Transparent;
            surface.Background = new SolidColorBrush(Color.FromArgb(0xB0, ThemeCatalog.CurrentTint.R, ThemeCatalog.CurrentTint.G, ThemeCatalog.CurrentTint.B));
        }
        else
        {
            surface.Background = new SolidColorBrush(Opaque(ThemeCatalog.CurrentTint));
        }
    }

    // The title-bar frame and menus follow the dark or light look.
    private static void SetFrame(IntPtr hwnd)
    {
        var dark = ThemeCatalog.IsLight ? 0 : 1;
        DwmSetWindowAttribute(hwnd, DwmaUseImmersiveDarkMode, ref dark, sizeof(int));
    }

    /// <summary>Colours the window background again after the theme changed.</summary>
    public static void Retint(Border surface)
    {
        if (Window.GetWindow(surface) is { } window && new WindowInteropHelper(window).Handle is var hwnd && hwnd != IntPtr.Zero) SetFrame(hwnd);
        if (surface.Background is not SolidColorBrush current) return;
        var t = ThemeCatalog.CurrentTint;
        surface.Background = new SolidColorBrush(current.Color.A == 0xFF ? Opaque(t) : Color.FromArgb(current.Color.A, t.R, t.G, t.B));
    }

    // Without the blurred background the tint is lightened a little, as the original look was.
    private static Color Opaque(Color tint) =>
        ThemeCatalog.IsLight ? tint : Color.FromRgb((byte)(tint.R + 9), (byte)(tint.G + 9), (byte)(tint.B + 9));
}
