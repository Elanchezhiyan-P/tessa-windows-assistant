using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using WinCompanion.UI;
using Xunit;

namespace WinCompanion.UnitTests;

[Collection("wpf")]
public class LightThemeTests
{
    private static T OnStaThread<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() => { try { result = work(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("failed on the UI thread", error);
        return result;
    }

    private static ResourceDictionary LoadTheme()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Themes", "Modern.xaml"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return (ResourceDictionary)XamlReader.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "Themes", "Modern.xaml")));
    }

    private static double Contrast(Color a, Color b)
    {
        var (hi, lo) = ThemeCatalog.Luminance(a) > ThemeCatalog.Luminance(b) ? (a, b) : (b, a);
        return (ThemeCatalog.Luminance(hi) + 0.05) / (ThemeCatalog.Luminance(lo) + 0.05);
    }

    private static Color Brush(ResourceDictionary r, string key) => ((SolidColorBrush)r[key]).Color;

    [Theory]
    [InlineData("dark", false)]
    [InlineData("light", true)]
    [InlineData("LIGHT", true)]
    [InlineData("nonsense", false)]
    [InlineData(null, false)]
    public void ModeMapsToLightOrDark(string? mode, bool light) => Assert.Equal(light, ThemeCatalog.ResolveLight(mode));

    [Fact]
    public void SystemModeFollowsTheWindowsSetting()
    {
        Assert.True(ThemeCatalog.ResolveLight("system", () => true));
        Assert.False(ThemeCatalog.ResolveLight("system", () => false));
    }

    [Theory]
    [InlineData(90, 90)]
    [InlineData(100, 100)]
    [InlineData(112, 115)]
    [InlineData(500, 130)]
    [InlineData(0, 90)]
    public void SizeSnapsToASupportedStep(int asked, int expected) => Assert.Equal(expected, ThemeCatalog.NormalizeSize(asked));

    [Fact]
    public void EveryThemeKeepsWhiteTextReadableOnItsLightAccent()
    {
        foreach (var theme in ThemeCatalog.All)
        {
            var accent = ThemeCatalog.ResolveAccent(theme, null, light: true);
            Assert.True(Contrast(Colors.White, accent) >= 4.5, $"{theme.Name}: white on accent {accent}");
        }
        // A pale custom accent is darkened too.
        Assert.True(Contrast(Colors.White, ThemeCatalog.ResolveAccent(ThemeCatalog.All[0], "#FFFF99", light: true)) >= 4.5);
    }

    [Fact]
    public void LightPaletteHasReadableTextOnTheLightWindow()
    {
        var failures = OnStaThread(() =>
        {
            var resources = LoadTheme();
            var problems = new List<string>();
            try
            {
                foreach (var theme in ThemeCatalog.All)
                {
                    ThemeCatalog.Apply(resources, theme, null, "light");
                    var window = ThemeCatalog.CurrentTint;
                    if (Contrast(Brush(resources, "Fg"), window) < 7) problems.Add($"{theme.Name}: Fg");
                    if (Contrast(Brush(resources, "Muted"), window) < 4.5) problems.Add($"{theme.Name}: Muted");
                    if (Contrast(Brush(resources, "LinkFg"), window) < 4.5) problems.Add($"{theme.Name}: link");
                    if (Contrast(Brush(resources, "ErrorText"), window) < 4.5) problems.Add($"{theme.Name}: error text");
                    if (Contrast(Brush(resources, "NoteFg"), window) < 4.5) problems.Add($"{theme.Name}: note");
                    if (Contrast(Brush(resources, "Accent"), window) < 3) problems.Add($"{theme.Name}: accent on window");
                    if (!ThemeCatalog.IsLight) problems.Add("IsLight not set");
                }
            }
            finally { ThemeCatalog.Apply(resources, ThemeCatalog.All[0], null, "dark"); }
            return problems;
        });
        Assert.Empty(failures);
    }

    [Fact]
    public void DarkPaletteStillHasReadableText()
    {
        var failures = OnStaThread(() =>
        {
            var resources = LoadTheme();
            var problems = new List<string>();
            foreach (var theme in ThemeCatalog.All)
            {
                ThemeCatalog.Apply(resources, theme, null, "dark");
                if (Contrast(Brush(resources, "Fg"), theme.Tint) < 7) problems.Add($"{theme.Name}: Fg");
                if (Contrast(Brush(resources, "Muted"), theme.Tint) < 4.5) problems.Add($"{theme.Name}: Muted");
                if (ThemeCatalog.IsLight) problems.Add("IsLight stuck on");
            }
            return problems;
        });
        Assert.Empty(failures);
    }

    [Fact]
    public void SwitchingModeRecoloursTheSameBrushesInPlace()
    {
        var (sameBrush, dark, light) = OnStaThread(() =>
        {
            var resources = LoadTheme();
            var fg = (SolidColorBrush)resources["Fg"];
            ThemeCatalog.Apply(resources, ThemeCatalog.All[0], null, "dark");
            var d = fg.Color;
            ThemeCatalog.Apply(resources, ThemeCatalog.All[0], null, "light");
            var l = fg.Color;
            var same = ReferenceEquals(fg, resources["Fg"]);
            ThemeCatalog.Apply(resources, ThemeCatalog.All[0], null, "dark");
            return (same, d, l);
        });
        Assert.True(sameBrush);
        Assert.True(ThemeCatalog.Luminance(dark) > 0.8);
        Assert.True(ThemeCatalog.Luminance(light) < 0.05);
    }

    [Fact]
    public void NoLiteralWhiteOrDarkColoursAreLeftInTheWindowXamlOutsideTheStyles()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MainWindow.xaml"))) dir = dir.Parent;
        Assert.NotNull(dir);
        foreach (var file in new[] { "MainWindow.xaml", "SetupWindow.xaml", "TourWindow.xaml", "PetWindow.xaml" })
        {
            var text = File.ReadAllText(Path.Combine(dir!.FullName, file));
            Assert.DoesNotContain("#14FFFFFF", text);
            Assert.DoesNotContain("#FF1E1F26", text);
            Assert.DoesNotContain("#F2191A1E", text);
        }
    }
}
