using System.Drawing;
using System.Drawing.Imaging;
using System.Text.Json.Nodes;
using static WinCompanion.Tools.ToolSchema;

namespace WinCompanion.Tools;

internal static class ScreenTools
{
    private const int AnalysisMaxWidth = 1600; // keeps uploads small without hurting readability

    public static IEnumerable<ITool> All(ToolContext ctx)
    {
        yield return new DelegateTool("look_at_screen",
            "Take a screenshot and answer a question about what is visible: explain an error, summarize a page, read text, describe an app. " +
            "The screenshot is sent to the configured AI model (Gemini sends it to Google; a local model keeps it on this PC).",
            new JsonObject
            {
                ["question"] = Str("What to find out about the screen."),
                ["scope"] = Choice("'screen' for all monitors or 'active_window' for just the focused window. Default screen.", "screen", "active_window")
            }, ["question"], false,
            async args =>
            {
                var llm = ctx.Llm ?? throw new InvalidOperationException("AI model is not ready.");
                if (!await ctx.AllowCloudAsync("a screenshot of your screen")) return "The user chose not to send the screenshot.";
                var png = await CaptureAsync(ctx, args["scope"]?.GetValue<string>() ?? "screen", AnalysisMaxWidth);
                return await llm.GenerateAsync(args["question"]!.GetValue<string>() + " Answer concisely.", png: png);
            });

        yield return new DelegateTool("save_screenshot",
            "Take a screenshot and save it as a PNG file (default: the Desktop). Does not send the image anywhere. Returns the saved path.",
            new JsonObject
            {
                ["scope"] = Choice("'screen' for all monitors or 'active_window' for just the focused window. Default screen.", "screen", "active_window"),
                ["folder"] = Str("Where to save: 'Desktop' (default), 'Documents', 'Pictures', 'Downloads' or a full folder path."),
                ["filename"] = Str("Optional file name, with or without .png. Default is a timestamped name.")
            }, [], false,
            async args =>
            {
                var png = await CaptureAsync(ctx, args["scope"]?.GetValue<string>() ?? "screen", maxWidth: null);

                var folder = BuiltInTools.ResolveFolder(args["folder"]?.GetValue<string>() ?? "desktop");
                Directory.CreateDirectory(folder);

                var path = UniquePath(folder, FileName(args["filename"]?.GetValue<string>()));
                await File.WriteAllBytesAsync(path, png);
                return $"Screenshot saved to {path}";
            });
    }

    private static string FileName(string? requested)
    {
        var name = string.IsNullOrWhiteSpace(requested) ? $"Screenshot {DateTime.Now:yyyy-MM-dd HH-mm-ss}" : requested.Trim();
        name = Path.GetFileNameWithoutExtension(name); // drop any folder part the model might include
        foreach (var bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
        return (name.Length == 0 ? "Screenshot" : name) + ".png";
    }

    // Never overwrite an existing file: "name.png", "name (2).png", ...
    private static string UniquePath(string folder, string fileName)
    {
        var path = Path.Combine(folder, fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        for (var n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{stem} ({n}).png");
        return path;
    }

    // Hides our own overlay so it isn't in the picture, captures, then brings it back without stealing focus.
    private static async Task<byte[]> CaptureAsync(ToolContext ctx, string scope, int? maxWidth)
    {
        var window = ctx.Window;
        var wasVisible = window?.IsVisible == true;
        if (wasVisible)
        {
            window!.Hide();
            await Task.Delay(350);
        }

        try { return Capture(scope, maxWidth); }
        finally
        {
            if (wasVisible)
            {
                window!.ShowActivated = false;
                window.Show();
                window.ShowActivated = true;
            }
        }
    }

    private static byte[] Capture(string scope, int? maxWidth)
    {
        Rectangle bounds;
        if (scope == "active_window" && NativeMethods.GetWindowRect(NativeMethods.GetForegroundWindow(), out var r)
            && r.Right > r.Left && r.Bottom > r.Top)
            bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        else
            bounds = System.Windows.Forms.SystemInformation.VirtualScreen;

        using var full = new Bitmap(bounds.Width, bounds.Height);
        using (var g = Graphics.FromImage(full))
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

        using var stream = new MemoryStream();
        if (maxWidth is { } limit && full.Width > limit)
        {
            using var small = new Bitmap(full, limit, full.Height * limit / full.Width);
            small.Save(stream, ImageFormat.Png);
        }
        else
        {
            full.Save(stream, ImageFormat.Png);
        }
        return stream.ToArray();
    }
}
