using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Threading;
using WinCompanion.Services;
using WinCompanion.Tools;
using Xunit;

namespace WinCompanion.UnitTests;

public class RegionOcrTests
{
    [Fact]
    public void MapsADragToPixelsWhateverTheScale()
    {
        // The overlay is 800x600 units on a screenshot of 1600x1200 pixels (a 200% display).
        var area = RegionOcr.ToPixels(100, 50, 300, 250, 800, 600, 1600, 1200);
        Assert.Equal(new Rectangle(200, 100, 400, 400), area);
    }

    [Fact]
    public void ADragInAnyDirectionGivesTheSameBox() =>
        Assert.Equal(RegionOcr.ToPixels(100, 50, 300, 250, 800, 600, 800, 600), RegionOcr.ToPixels(300, 250, 100, 50, 800, 600, 800, 600));

    [Fact]
    public void StaysInsideThePicture()
    {
        var area = RegionOcr.ToPixels(-50, -50, 900, 700, 800, 600, 800, 600);
        Assert.Equal(new Rectangle(0, 0, 800, 600), area);
    }

    [Theory]
    [InlineData(100, 100, 103, 103)] // a click or a twitch
    [InlineData(100, 100, 400, 104)] // a line, not a box
    public void TooSmallABoxIsNotASelection(double x1, double y1, double x2, double y2) =>
        Assert.Null(RegionOcr.ToPixels(x1, y1, x2, y2, 800, 600, 800, 600));

    [Fact]
    public void BadSizesDoNotCrash() => Assert.Null(RegionOcr.ToPixels(0, 0, 10, 10, 0, 0, 800, 600));

    [Theory]
    [InlineData(400, 300, 1.0)]    // big enough already
    [InlineData(400, 100, 1.0)]
    [InlineData(300, 50, 2.0)]     // a thin strip is doubled
    [InlineData(300, 10, 4.0)]     // and never more than four times
    public void EnlargesSmallCrops(int width, int height, double expected) =>
        Assert.Equal(expected, RegionOcr.EnlargeFactor(width, height), 3);

    [Fact]
    public void NeverEnlargesBeyondWhatOcrAccepts() =>
        Assert.True(RegionOcr.EnlargeFactor(3900, 20) * 3900 <= 4000.0001);

    private static Bitmap PageWithText(string text, int fontSize)
    {
        var page = new Bitmap(900, 500);
        using var g = Graphics.FromImage(page);
        g.Clear(Color.White);
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using var font = new Font("Segoe UI", fontSize);
        g.DrawString(text, font, Brushes.Black, 220, 180);
        return page;
    }

    // Real end to end: draw words, cut out a box, enlarge it, and read it with Windows' own OCR.
    [Theory]
    [InlineData(40)]
    [InlineData(14)] // small print goes through the enlarging path
    public async Task ReadsTextFromACroppedBox(int fontSize)
    {
        using var page = PageWithText("Hello Tessa 2026", fontSize);
        // The words sit at (220, 180); the box is a little larger than them, like a hand-drawn selection.
        var area = new Rectangle(180, 165, 700, (int)(fontSize * 2.6) + 30); // wide: text renders larger on a scaled display
        var png = RegionOcr.CropToPng(page, area);

        using (var decoded = new Bitmap(new MemoryStream(png)))
            Assert.True(decoded.Width >= area.Width); // enlarged, never shrunk

        // A PC with no Windows OCR language installed has nothing to check.
        if (Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages() is null) return;

        var text = await ScreenText.ReadAsync(png);
        Assert.True(text.Contains("Hello", StringComparison.OrdinalIgnoreCase), $"OCR read [{text}] from {area}");
        Assert.True(text.Contains("2026"), $"OCR read [{text}] from {area}");
    }

    [Fact]
    public void ABlankBoxGivesNoText()
    {
        using var page = new Bitmap(400, 200);
        using (var g = Graphics.FromImage(page)) g.Clear(Color.White);
        var text = ScreenText.ReadAsync(RegionOcr.CropToPng(page, new Rectangle(10, 10, 300, 100))).GetAwaiter().GetResult();
        Assert.Equal("", text);
    }

    [Fact]
    public void TheOverlayOpensAndClosesWithoutASelection()
    {
        Exception? error = null;
        Rectangle? selection = new Rectangle(1, 1, 1, 1);
        var thread = new Thread(() =>
        {
            try
            {
                using var shot = new Bitmap(800, 600);
                var window = new RegionSnipWindow(shot, new Rectangle(0, 0, 800, 600));
                window.Show();
                // Let the window finish loading, as it would on screen.
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                selection = window.Selection;
                window.Close();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
        Assert.Null(selection);
    }

    [Fact]
    public void TheCommandAndTourKnowAboutIt()
    {
        Assert.NotNull(SlashCommands.Find("snip"));
        Assert.Contains("Ctrl+Alt+T", TourSteps.Build("Tessa")[0].Rows.Select(r => r.Keys));
    }
}
