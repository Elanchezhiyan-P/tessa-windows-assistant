using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace WinCompanion.Tools;

/// <summary>Reads the text in a screenshot with Windows' built-in OCR. Nothing leaves the PC.</summary>
internal static class ScreenText
{
    /// <summary>The text found in a PNG image, or an empty string when there is none or no OCR language is installed.</summary>
    public static async Task<string> ReadAsync(byte[] png)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null) return "";

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync();
            writer.DetachStream();
        }

        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync();
        var result = await engine.RecognizeAsync(bitmap);
        return string.Join('\n', result.Lines.Select(l => l.Text)).Trim();
    }
}
