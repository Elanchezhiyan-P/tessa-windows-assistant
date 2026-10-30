using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WinCompanion.Services;

/// <summary>The geometry and image handling behind "drag a box on the screen and copy its text".</summary>
public static class RegionOcr
{
    /// <summary>A box smaller than this on either side is a click, not a selection.</summary>
    public const int MinSidePixels = 8;

    // Windows' OCR reads small print badly, so a thin strip is enlarged first.
    private const int ComfortableSide = 100;
    private const double MaxEnlarge = 4;
    private const int MaxSide = 4000;

    /// <summary>
    /// Turns two corners dragged on screen (in device-independent units, in any order) into a rectangle of the screenshot's pixels,
    /// kept inside the picture. Null when the box is too small to be a selection.
    /// </summary>
    public static Rectangle? ToPixels(double x1, double y1, double x2, double y2,
        double shownWidth, double shownHeight, int pixelWidth, int pixelHeight)
    {
        if (shownWidth <= 0 || shownHeight <= 0) return null;
        var sx = pixelWidth / shownWidth;
        var sy = pixelHeight / shownHeight;

        var left = (int)Math.Round(Math.Min(x1, x2) * sx);
        var top = (int)Math.Round(Math.Min(y1, y2) * sy);
        var right = (int)Math.Round(Math.Max(x1, x2) * sx);
        var bottom = (int)Math.Round(Math.Max(y1, y2) * sy);

        left = Math.Clamp(left, 0, pixelWidth);
        right = Math.Clamp(right, 0, pixelWidth);
        top = Math.Clamp(top, 0, pixelHeight);
        bottom = Math.Clamp(bottom, 0, pixelHeight);

        var area = Rectangle.FromLTRB(left, top, right, bottom);
        return area.Width < MinSidePixels || area.Height < MinSidePixels ? null : area;
    }

    /// <summary>How much to enlarge a crop before reading it: 1 when it is already big enough, up to 4 for a thin strip.</summary>
    public static double EnlargeFactor(int width, int height)
    {
        var shorter = Math.Min(width, height);
        if (shorter >= ComfortableSide || shorter <= 0) return 1;
        var factor = Math.Min(MaxEnlarge, (double)ComfortableSide / shorter);
        // Never grow past what the OCR engine accepts.
        var capped = Math.Min(factor, (double)MaxSide / Math.Max(width, height));
        return capped > 1 ? capped : 1;
    }

    /// <summary>A screenshot of every monitor, and where it sits on the desktop.</summary>
    public static Bitmap CaptureVirtualScreen(out Rectangle bounds)
    {
        bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        var shot = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(shot);
        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        return shot;
    }

    /// <summary>The chosen part of the screenshot as PNG bytes, enlarged when it is small.</summary>
    public static byte[] CropToPng(Bitmap shot, Rectangle area)
    {
        using var crop = shot.Clone(area, PixelFormat.Format32bppArgb);
        var factor = EnlargeFactor(area.Width, area.Height);

        using var stream = new MemoryStream();
        if (factor > 1)
        {
            using var big = new Bitmap((int)(area.Width * factor), (int)(area.Height * factor));
            using (var g = Graphics.FromImage(big))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(crop, 0, 0, big.Width, big.Height);
            }
            big.Save(stream, ImageFormat.Png);
        }
        else crop.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
