using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace WinCompanion.UI;

/// <summary>Draws the tray icon (a gradient disc with a sparkle) so no image asset is needed.</summary>
internal static class TrayIcon
{
    public static Icon Create()
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using var fill = new LinearGradientBrush(new Rectangle(0, 0, size, size),
                Color.FromArgb(107, 139, 255), Color.FromArgb(168, 102, 255), 45f);
            g.FillEllipse(fill, 1, 1, size - 2, size - 2);

            using var font = new Font("Segoe UI Symbol", 17, FontStyle.Bold, GraphicsUnit.Pixel);
            var text = g.MeasureString("✦", font);
            g.DrawString("✦", font, Brushes.White, (size - text.Width) / 2, (size - text.Height) / 2);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }
}
