using System.Text.Json;
using System.Text.Json.Serialization;
using MapCore.Imaging;
using MapCore.Items;
using MapCore.Structures;

namespace MapCore.Assets;

/// <summary>
/// A feature BAR defines, as tools/MapResearch's barfeatures read it from BAR's repository: its own features/*.lua
/// (the engine's treetype0-15 among them, which BAR draws as its fir trees), and units' wrecks (name_dead, name_heap).
/// </summary>
/// <param name="Name">Its name, as maps place it.</param>
/// <param name="Object">The model BAR's definition names, as a path in BAR (objects3d/...), or null for none.</param>
/// <param name="Model">The model shipped here (a path under the catalog's folder), or null when it isn't (not fetched, or none).</param>
/// <param name="File">The BAR file that defines it.</param>
/// <param name="Author">The model's author, as its definition credits it.</param>
/// <param name="FromUnit">For a wreck, the unit it is left by.</param>
/// <param name="Note">Anything to know about how it is shown here.</param>
public sealed record BarFeature(string Name, string? Object, string? Model, string? Description, string? Category, double Metal, double Energy,
    string File, string? Author, string? FromUnit, string? Note);

/// <summary>
/// One of BAR's units or buildings (the unit catalog's: Armada, Cortex, Legion) with its model. A map places it
/// through its featureplacer: buildings in the buildinglist, facing one of four ways; units in the unitlist, at any
/// heading. The map's featureplacer gadget makes them Gaia's: neutral, always visible.
/// </summary>
/// <param name="Building">It doesn't move (no speed) and doesn't fly.</param>
/// <param name="Object">Its model as a path in BAR (objects3d/Units/...).</param>
/// <param name="Model">The model shipped here, or null.</param>
public sealed record BarUnit(string Name, string? DisplayName, string? Description, string? Faction, bool Building, string? Object, string? Model,
    string File, string? Author)
{
    /// <summary>Its name and what BAR calls it, e.g. "armpw (Pawn)".</summary>
    public string Title => DisplayName is { Length: > 0 } shown && shown != Name ? $"{Name} ({shown})" : Name;
}

/// <summary>
/// The features BAR defines and its units and buildings, with their models shipped beside the editor (BarFeatures:
/// objects3d/ and unittextures/ as BAR has them, textures cut to one smaller mip level): so the asset browser, its
/// previews and the 3D view can offer and draw all of them (the engine's trees as BAR's firs, BAR's rocks, wrecks,
/// every unit and building) instead of placeholders. They stay BAR's: a map names them and BAR supplies them, so they
/// are never copied into a map.
/// </summary>
public sealed class BarFeatureCatalog
{
    public const string FileName = "catalog.json";

    public CatalogSource? Source { get; }
    public IReadOnlyList<BarFeature> Features { get; }
    public IReadOnlyList<BarUnit> Units { get; }

    /// <summary>The folder it was read from: a package-like folder (objects3d/, unittextures/) the models' paths are under.</summary>
    public string Folder { get; }

    readonly Dictionary<string, BarFeature> byName;
    readonly Dictionary<string, BarUnit> unitByName;
    Func<string, string?>? resolve;

    BarFeatureCatalog(CatalogSource? source, IReadOnlyList<BarFeature> features, IReadOnlyList<BarUnit> units, string folder)
    {
        Source = source;
        Features = features;
        Units = units;
        Folder = folder;
        byName = features.GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        unitByName = units.GroupBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    static readonly JsonSerializerOptions options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    sealed record FileShape(CatalogSource? Source, List<BarFeature>? Features, List<BarUnit>? Units);

    public static BarFeatureCatalog Load(string path)
    {
        var file = JsonSerializer.Deserialize<FileShape>(System.IO.File.ReadAllText(path), options)
                   ?? throw new InvalidDataException($"{path} isn't a BAR feature catalog");
        var features = (file.Features ?? []).Where(f => !string.IsNullOrEmpty(f.Name)).Select(f => f with { File = f.File ?? "" }).ToList();
        var units = (file.Units ?? []).Where(u => !string.IsNullOrEmpty(u.Name)).Select(u => u with { File = u.File ?? "" }).ToList();
        return new BarFeatureCatalog(file.Source, features, units, Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
    }

    /// <summary>The catalog shipped beside a program (BarFeatures/catalog.json under its folder), or null when it isn't there.</summary>
    public static BarFeatureCatalog? Shipped(string programFolder)
    {
        string path = Path.Combine(programFolder, "BarFeatures", FileName);
        return System.IO.File.Exists(path) ? Load(path) : null;
    }

    public BarFeature? Find(string name) => byName.TryGetValue(name, out var feature) ? feature : null;

    /// <summary>The shipped model of a feature, as a path under <see cref="Folder"/>, or null when there is none here.</summary>
    public string? ModelOf(string name) => Find(name)?.Model is string model && Resolve(model) is string found ? found : null;

    /// <summary>Looks paths up in the catalog's folder, ignoring case (as <see cref="ModelPaths.Resolver"/>).</summary>
    public string? Resolve(string path) => (resolve ??= ModelPaths.Resolver(Folder))(path);

    /// <summary>
    /// A feature's model with its textures, or null when none is shipped. Throws as <see cref="ModelPreview.Load"/>
    /// does for a model that can't be read. <paramref name="textures"/> keeps textures read for the next model.
    /// </summary>
    public ModelPreview? LoadModel(string name, Dictionary<string, RgbaImage>? textures = null) =>
        ModelOf(name) is string model ? ModelPreview.Load(Folder, model, Resolve, textures) : null;

    public BarUnit? FindUnit(string name) => unitByName.TryGetValue(name, out var unit) ? unit : null;

    /// <summary>The shipped model of a unit or building, as a path under <see cref="Folder"/>, or null when there is none here.</summary>
    public string? UnitModelOf(string name) => FindUnit(name)?.Model is string model && Resolve(model) is string found ? found : null;

    /// <summary>A unit's or building's model with its textures, or null when none is shipped (throws as <see cref="LoadModel"/> does).</summary>
    public ModelPreview? LoadUnitModel(string name, Dictionary<string, RgbaImage>? textures = null) =>
        UnitModelOf(name) is string model ? ModelPreview.Load(Folder, model, Resolve, textures) : null;

    /// <summary>
    /// Everything BAR offers a map, as name-only library entries: its features (not those BAR drops for want of a
    /// model; the engine's trees as engine features), units and buildings. Ids are as the library gives name-only
    /// entries, so a name a map has used is the library's own entry instead. Marked <see cref="AssetEntry.BarOnly"/>.
    /// </summary>
    public IReadOnlyList<AssetEntry> AssetEntries()
    {
        var list = new List<AssetEntry>(Features.Count + Units.Count);
        foreach (var f in Features.Where(f => f.Note == null))
        {
            var kind = f.Name.Contains("treetype", StringComparison.OrdinalIgnoreCase) ? AssetKind.EngineFeature : AssetKind.GameFeature;
            list.Add(Entry(f.Name, kind, AssetCategories.Classify(f.Name, kind, f.Category, f.Description, f.Metal, f.Energy), f.Description));
        }
        foreach (var u in Units)
        {
            var kind = u.Building ? AssetKind.Building : AssetKind.Unit;
            list.Add(Entry(u.Name, kind, AssetCategories.Classify(u.Name, kind), u.DisplayName is { Length: > 0 } shown ? $"{shown}: {u.Description}" : u.Description));
        }
        return list;

        static AssetEntry Entry(string name, AssetKind kind, string category, string? description)
        {
            var id = $"{kind.ToString().ToLowerInvariant()}:{name.ToLowerInvariant()}";
            return new AssetEntry
            {
                Id = id, Name = name, Defines = [name], Kind = kind, Category = category, Description = description, Hash = id,
                ParseVersion = AssetLibrary.CurrentParse, BarOnly = true,
            };
        }
    }

    /// <summary>Where the models came from, for a caption: "BAR at 9d7bdf319ba0".</summary>
    public string Origin => Source is { Commit.Length: >= 12 } s ? $"BAR at {s.Commit[..12]}" : "BAR";
}
