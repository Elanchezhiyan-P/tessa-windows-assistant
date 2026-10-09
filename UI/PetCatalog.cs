using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media.Imaging;
using WinCompanion.Services;

namespace WinCompanion.UI;

public sealed class PetAnimation
{
    public int Row { get; set; }
    public int Frames { get; set; } = 1;
    public double Fps { get; set; } = 8;
}

/// <summary>
/// One character: a sprite sheet (one animation per row, equal-sized cells) plus pet.json describing it.
/// Built-in characters live in Assets\Pets; users can add their own under %AppData%\WinCompanion\pets.
/// </summary>
public sealed class PetDefinition
{
    public string Name { get; set; } = "";
    /// <summary>"walker" paces along the bottom of the screen; "flyer" glides across the upper part.</summary>
    public string Kind { get; set; } = "walker";
    /// <summary>The direction the art faces; it is mirrored when the character moves the other way.</summary>
    public string Facing { get; set; } = "left";
    public int FrameWidth { get; set; }
    public int FrameHeight { get; set; }
    public double Scale { get; set; } = 3;
    /// <summary>True for smooth cartoon art; false (the default) keeps hard pixel edges when scaled up.</summary>
    public bool Smooth { get; set; }
    /// <summary>Adds a soft light outline so dark pixel art stays visible on any background.</summary>
    public bool Glow { get; set; }
    /// <summary>Travel speed in DIPs per second; 0 uses the default for the kind. Match it to the stride to avoid foot sliding.</summary>
    public double Speed { get; set; }
    /// <summary>Multiplier on the animation frame rate (e.g. 0.5 plays a fast gallop at half speed).</summary>
    public double Playback { get; set; } = 1;
    /// <summary>Position in the picker; lower comes first.</summary>
    public int Sort { get; set; } = 100;
    public string Sheet { get; set; } = "sheet.png";
    public string Credit { get; set; } = "";
    /// <summary>walk (walkers), fly (flyers), idle, and optional run / alert.</summary>
    public Dictionary<string, PetAnimation> Animations { get; set; } = new();

    [JsonIgnore] public string Id { get; set; } = "";
    [JsonIgnore] public string Folder { get; set; } = "";
    [JsonIgnore] public bool IsFlyer => Kind.Equals("flyer", StringComparison.OrdinalIgnoreCase);
    /// <summary>A sitter stays put: it rises into view from the bottom of the screen, loops its idle animation, then sinks away.</summary>
    [JsonIgnore] public bool IsSitter => Kind.Equals("sitter", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool ArtFacesLeft => !Facing.Equals("right", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public double DisplayWidth => FrameWidth * Scale;
    [JsonIgnore] public double DisplayHeight => FrameHeight * Scale;

    private BitmapSource? _sheet;
    private readonly Dictionary<string, BitmapSource[]> _frames = new();

    /// <summary>Frames of an animation; falls back to idle, then walk/fly, if the requested one doesn't exist.</summary>
    public (BitmapSource[] Frames, double Fps) Get(params string[] names)
    {
        foreach (var name in names.Concat(["idle", "walk", "fly"]))
        {
            if (!Animations.TryGetValue(name, out var animation)) continue;
            if (!_frames.TryGetValue(name, out var frames)) _frames[name] = frames = Slice(animation);
            return (frames, animation.Fps);
        }
        throw new InvalidOperationException($"Pet '{Name}' has no animations.");
    }

    public bool Has(string animation) => Animations.ContainsKey(animation);

    private BitmapSource[] Slice(PetAnimation animation)
    {
        _sheet ??= LoadSheet();
        var frames = new BitmapSource[animation.Frames];
        for (var i = 0; i < frames.Length; i++)
        {
            var cell = new CroppedBitmap(_sheet, new Int32Rect(i * FrameWidth, animation.Row * FrameHeight, FrameWidth, FrameHeight));
            cell.Freeze();
            frames[i] = cell;
        }
        return frames;
    }

    private BitmapSource LoadSheet()
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(Path.Combine(Folder, Sheet));
        image.CacheOption = BitmapCacheOption.OnLoad; // read fully now so the file isn't locked
        image.EndInit();
        image.Freeze();
        return image;
    }
}

public static class PetCatalog
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static List<PetDefinition>? _cache;

    public static string UserFolder => Path.Combine(AppPaths.Dir, "pets");
    private static string BuiltInFolder => Path.Combine(AppContext.BaseDirectory, "Assets", "Pets");

    public static IReadOnlyList<PetDefinition> All() => _cache ??= Load();

    public static PetDefinition? Find(string id) =>
        All().FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static void Reload() => _cache = null;

    private static List<PetDefinition> Load()
    {
        var byId = new Dictionary<string, PetDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { BuiltInFolder, UserFolder }) // user packs override built-ins with the same id
        {
            if (!Directory.Exists(root)) continue;
            foreach (var folder in Directory.GetDirectories(root))
            {
                if (TryRead(folder) is { } pet) byId[pet.Id] = pet;
            }
        }
        return byId.Values.OrderBy(p => p.Sort).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static PetDefinition? TryRead(string folder)
    {
        try
        {
            var json = Path.Combine(folder, "pet.json");
            if (!File.Exists(json)) return null;

            var pet = JsonSerializer.Deserialize<PetDefinition>(File.ReadAllText(json), Json);
            if (pet is null || pet.FrameWidth <= 0 || pet.FrameHeight <= 0 || pet.Animations.Count == 0) return null;
            if (!File.Exists(Path.Combine(folder, pet.Sheet))) return null;

            pet.Id = Path.GetFileName(folder);
            pet.Folder = folder;
            if (string.IsNullOrWhiteSpace(pet.Name)) pet.Name = pet.Id;
            return pet;
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return null; } // a broken pack is skipped, not fatal
    }
}
