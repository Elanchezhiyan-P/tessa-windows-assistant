using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace WinCompanion.UI;

/// <param name="Accent">Buttons, highlights and the sparkle in the header.</param>
/// <param name="Tint">The dark colour laid over the window's blurred background.</param>
public sealed record AppTheme(string Key, string Name, Color Accent, Color Tint);

/// <summary>The window colour themes, and the code that applies one to the running app.</summary>
public static class ThemeCatalog
{
    public static readonly IReadOnlyList<AppTheme> All =
    [
        new("midnight", "Midnight", Color.FromRgb(0x6B, 0x8B, 0xFF), Color.FromRgb(0x12, 0x13, 0x19)),
        new("violet", "Violet", Color.FromRgb(0xA9, 0x8B, 0xFF), Color.FromRgb(0x17, 0x12, 0x1F)),
        new("ocean", "Ocean", Color.FromRgb(0x4F, 0xC3, 0xD9), Color.FromRgb(0x0F, 0x18, 0x1D)),
        new("forest", "Forest", Color.FromRgb(0x6B, 0xCB, 0x77), Color.FromRgb(0x0F, 0x17, 0x12)),
        new("sunset", "Sunset", Color.FromRgb(0xFF, 0x9F, 0x5A), Color.FromRgb(0x1C, 0x13, 0x10)),
        new("rose", "Rose", Color.FromRgb(0xFF, 0x7A, 0x9C), Color.FromRgb(0x1D, 0x11, 0x15)),
        new("graphite", "Graphite", Color.FromRgb(0xB0, 0xB6, 0xC4), Color.FromRgb(0x14, 0x14, 0x16))
    ];

    private static readonly Regex HexColor = new(@"^#?(?<rgb>[0-9a-f]{6})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The accent and tint now in use, for things drawn in code (chat bubbles, the window background).</summary>
    public static Color CurrentAccent { get; private set; } = All[0].Accent;
    public static Color CurrentTint { get; private set; } = All[0].Tint;
    /// <summary>True while the light look is on.</summary>
    public static bool IsLight { get; private set; }

    public static readonly int[] Sizes = [90, 100, 115, 130];

    /// <summary>The nearest supported interface size, in percent.</summary>
    public static int NormalizeSize(int percent) => Sizes.OrderBy(s => Math.Abs(s - percent)).First();

    public static readonly string[] Modes = ["dark", "light", "system"];

    /// <summary>"dark", "light" or "system"; anything else is "dark".</summary>
    public static string NormalizeMode(string? mode) =>
        Modes.FirstOrDefault(m => m.Equals(mode?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "dark";

    /// <summary>Whether the light look applies for a mode; "system" follows the Windows "app mode" setting.</summary>
    public static bool ResolveLight(string? mode, Func<bool>? systemIsLight = null) => NormalizeMode(mode) switch
    {
        "light" => true,
        "system" => (systemIsLight ?? SystemIsLight)(),
        _ => false
    };

    public static bool SystemIsLight()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>The window colour for the light look: the theme's tint, almost white.</summary>
    public static Color LightTint(Color tint) =>
        Color.FromRgb(Lift(tint.R), Lift(tint.G), Lift(tint.B));

    private static byte Lift(byte c) => (byte)(c + (255 - c) * 0.93);

    /// <summary>Darkens an accent until white text on it, and the accent as text on a light window, are readable.</summary>
    public static Color EnsureReadableOnLight(Color color)
    {
        for (var i = 0; i < 12 && Luminance(color) > 0.18; i++)
            color = Color.FromRgb((byte)(color.R * 0.9), (byte)(color.G * 0.9), (byte)(color.B * 0.9));
        return color;
    }

    public static AppTheme Find(string? key) =>
        All.FirstOrDefault(t => t.Key.Equals(key?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public static bool TryParseAccent(string? text, out Color color)
    {
        color = default;
        var match = HexColor.Match(text?.Trim() ?? "");
        if (!match.Success) return false;
        var rgb = Convert.ToInt32(match.Groups["rgb"].Value, 16);
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    /// <summary>Lightens a colour that would be hard to read on the dark window.</summary>
    public static Color EnsureVisible(Color color)
    {
        for (var i = 0; i < 10 && Luminance(color) < 0.22; i++)
            color = Color.FromRgb(Mix(color.R, 255), Mix(color.G, 255), Mix(color.B, 255));
        return color;

        static byte Mix(byte c, byte target) => (byte)(c + (target - c) * 0.2);
    }

    /// <summary>Relative luminance, 0 (black) to 1 (white).</summary>
    public static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary>The theme's accent, or the user's own colour when it is valid.</summary>
    public static Color ResolveAccent(AppTheme theme, string? accentOverride, bool light = false)
    {
        var accent = TryParseAccent(accentOverride, out var custom) ? custom : theme.Accent;
        return light ? EnsureReadableOnLight(accent) : TryParseAccent(accentOverride, out _) ? EnsureVisible(accent) : accent;
    }

    /// <summary>The background for the user's own chat messages: the accent, slightly deeper so white text reads well.</summary>
    public static Color UserBubble(Color accent) => Color.FromRgb((byte)(accent.R * 0.9), (byte)(accent.G * 0.9), (byte)(accent.B * 0.9));

    /// <summary>
    /// Recolours the shared brushes in place, so every open window changes at once. Returns false when a brush could not be
    /// changed (it is frozen), in which case the new colour appears the next time the app starts.
    /// </summary>
    public static bool Apply(ResourceDictionary resources, AppTheme theme, string? accentOverride, string? mode = null)
    {
        var light = ResolveLight(mode);
        IsLight = light;
        var accent = ResolveAccent(theme, accentOverride, light);
        CurrentAccent = accent;
        CurrentTint = light ? LightTint(theme.Tint) : theme.Tint;

        var live = Recolor(resources, "Accent", accent);
        live &= Recolor(resources, "AccentSoft", Color.FromArgb(0x40, accent.R, accent.G, accent.B));
        foreach (var (key, dark, bright) in Palette)
            if (resources[key] is not null) live &= Recolor(resources, key, light ? bright : dark);
        return live;
    }

    // Brushes that differ between the two looks: key, dark value, light value.
    private static readonly (string Key, Color Dark, Color Light)[] Palette =
    [
        ("Fg", Hex("#F4F4F7"), Hex("#1C1D24")),
        ("Muted", Hex("#A3A6B5"), Hex("#5A5E6E")),
        ("Surface", Hex("#1FFFFFFF"), Hex("#12000000")),
        ("SurfaceHover", Hex("#2EFFFFFF"), Hex("#1C000000")),
        ("Stroke", Hex("#1FFFFFFF"), Hex("#22000000")),
        ("SegmentBack", Hex("#14FFFFFF"), Hex("#0D000000")),
        ("TrackBack", Hex("#33FFFFFF"), Hex("#2E000000")),
        ("ThumbBack", Hex("#55FFFFFF"), Hex("#55000000")),
        ("AssistantBubble", Hex("#26FFFFFF"), Hex("#10000000")),
        ("ErrorText", Hex("#FFA9A9"), Hex("#B3261E")),
        ("ErrorBubble", Hex("#40FF5A5A"), Hex("#22D32F2F")),
        ("CodeBack", Hex("#33000000"), Hex("#12000000")),
        ("InlineCodeBack", Hex("#33FFFFFF"), Hex("#14000000")),
        ("LinkFg", Hex("#8FB0FF"), Hex("#1D4ED8")),
        ("WindowBack", Hex("#FF1E1F26"), Hex("#FFF3F4F8")),
        ("NoteFg", Hex("#FFC27A"), Hex("#9A5B00")),
        ("PopupBackground", Hex("#FF2B2C35"), Hex("#FFFAFAFD"))
    ];

    /// <summary>A shared palette brush (it changes in place with the theme), or a fixed one when the app has no resources, as in tests.</summary>
    public static Brush PaletteBrush(string key)
    {
        if (Application.Current?.Resources[key] is Brush live) return live;
        var fallback = new SolidColorBrush(Palette.First(p => p.Key == key).Dark);
        fallback.Freeze();
        return fallback;
    }

    private static Color Hex(string text) => (Color)ColorConverter.ConvertFromString(text);

    private static bool Recolor(ResourceDictionary resources, string key, Color color)
    {
        if (resources[key] is not SolidColorBrush brush) return false;
        if (brush.IsFrozen)
        {
            resources[key] = new SolidColorBrush(color); // only elements created from now on see it
            return false;
        }
        brush.Color = color;
        return true;
    }
}
