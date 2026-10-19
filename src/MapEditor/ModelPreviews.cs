using System.Globalization;
using MapCore;
using MapCore.Assets;
using MapCore.Editing;
using MapCore.Items;
using MapCore.Structures;

namespace MapEditor;

/// <summary>
/// The features the open map defines, worked out again only when its features/, objects3d/ or unittextures/ change
/// (an asset added or undone): running the definition files is too slow to repeat for every selection.
/// </summary>
sealed class FeatureLookup
{
    static readonly string[] Watched = ["features", "objects3d", "unittextures"];
    string? fingerprint;
    IReadOnlyList<FeatureDefinition> definitions = [];
    Func<string, string?> resolve = _ => null;

    /// <summary>Goes up each time the definitions are read again.</summary>
    public int Version { get; private set; }

    public (IReadOnlyList<FeatureDefinition> Definitions, Func<string, string?> Resolve) Get(MapEditSession session)
    {
        var root = session.Package.Root;
        var now = root + "|" + string.Join("|", Watched.Select(d => Path.Combine(root, d)).Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*", FileSystemUtil.AllEntries))
            .Select(f => new FileInfo(f)).OrderBy(f => f.FullName, StringComparer.Ordinal)
            .Select(f => string.Create(CultureInfo.InvariantCulture, $"{f.FullName}:{f.Length}:{f.LastWriteTimeUtc.Ticks}")));
        if (now != fingerprint)
        {
            (definitions, _) = FeatureCatalog.Load(root, session.Smf);
            resolve = ModelPaths.Resolver(root);
            fingerprint = now;
            Version++;
        }
        return (definitions, resolve);
    }
}

/// <summary>
/// Finds and loads the models previews show: a library asset's (from its folder in the library), a placed item's
/// (from the map, or else from the library), or one of BAR's own features (from the models shipped with the editor),
/// keeping the last few loaded. When there is no model to show it says why: the engine's vent (no model in game
/// either), a feature BAR doesn't define, a unit, missing from its map, or a kind of model the preview doesn't draw.
/// </summary>
sealed class ModelPreviews
{
    const int Kept = 16;
    readonly Dictionary<string, ModelPreview> cache = [];
    readonly Queue<string> order = [];

    public readonly record struct Result(ModelPreview? Model, string Text);

    /// <summary>BAR's own features and their models (null: none shipped).</summary>
    public BarFeatureCatalog? BarFeatures { get; set; }

    public Result ForAsset(AssetLibrary library, AssetEntry asset, AssetFeature? feature)
    {
        if (!asset.HasFiles)
            return ForBar(asset.Name, asset.Kind);
        feature ??= asset.Features.FirstOrDefault();
        var name = feature?.Name ?? asset.Name;
        if (feature?.Model == null)
            return new(null, $"{name}'s model wasn't in its map (BAR may have it), so there's nothing to show.");
        return Load(Path.Combine(library.Root, "files", asset.Id), feature.Model, null, name);
    }

    /// <summary>The model of a placed item called <paramref name="name"/>: the map's own definition first, then the library's.</summary>
    public Result ForPlaced(string name, PlacedItemKind kind, MapEditSession session, FeatureLookup features, AssetLibrary? library)
    {
        if (kind is PlacedItemKind.Unit or PlacedItemKind.Building)
            return ForBar(name, kind == PlacedItemKind.Unit ? AssetKind.Unit : AssetKind.Building);
        var (definitions, resolve) = features.Get(session);
        var def = definitions.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (def?.Model != null)
        {
            var path = ModelPaths.Find(def.Model, resolve);
            return path == null
                ? new(null, $"{name}'s model {def.Model} isn't in this map (BAR may have it), so there's nothing to show.")
                : Load(session.Package.Root, path, resolve, name);
        }
        if (library?.Entries.Where(e => e.HasFiles).SelectMany(e => e.Features.Select(f => (Asset: e, Feature: f)))
                .FirstOrDefault(x => x.Feature.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && x.Feature.Model != null) is { Asset: not null } known)
        {
            var result = ForAsset(library, known.Asset, known.Feature);
            return result with { Text = result.Text + " (from the asset library: this map doesn't define it)" };
        }
        if (def != null && !def.Source.StartsWith("engine default", StringComparison.Ordinal))
            return new(null, $"{name} is defined by this map without a model: there's nothing to show.");
        bool engine = def != null || name.Contains("treetype", StringComparison.OrdinalIgnoreCase) || name.Contains("geovent", StringComparison.OrdinalIgnoreCase);
        var bar = ForBar(name, engine ? AssetKind.EngineFeature : AssetKind.GameFeature);
        return bar.Model != null ? bar with { Text = bar.Text + " (this map doesn't define it: BAR does)" } : bar;
    }

    /// <summary>
    /// A feature a map names without defining it: BAR's model of it when one is shipped, else why there is none (the
    /// engine's vent has no model in game either; a name BAR doesn't define won't appear in game).
    /// </summary>
    public Result ForBar(string name, AssetKind kind)
    {
        if (kind is AssetKind.Unit or AssetKind.Building)
        {
            string what = kind == AssetKind.Unit ? "unit" : "building";
            if (BarFeatures == null)
                return new(null, $"{name} is a BAR {what}: its model is in BAR, not in the library.");
            if (BarFeatures.FindUnit(name) is not BarUnit unit)
                return new(null, $"BAR has no {what} called {name} ({BarFeatures.Origin}): it won't appear in game.");
            if (BarFeatures.UnitModelOf(name) is not string unitModel)
                return new(null, $"{unit.Title}: its model ({unit.Object ?? "none"}) isn't shipped with the editor, so there's nothing to show.");
            var shown = Load(BarFeatures.Folder, unitModel, BarFeatures.Resolve, unit.Title);
            return shown.Model == null ? shown : shown with { Text = shown.Text + $" · BAR's model{(unit.Author is string by ? ", by " + by : "")}" };
        }
        if (name.Contains("geovent", StringComparison.OrdinalIgnoreCase))
            return new(null, $"{name} is the engine's geothermal vent: it has no model, in game either (the map's texture shows the vent).");
        if (BarFeatures?.Find(name) is not BarFeature feature)
        {
            if (BarFeatures == null || kind == AssetKind.EngineFeature)
                return new(null, kind == AssetKind.EngineFeature
                    ? $"{name} is built into the engine: there's no model file to show."
                    : $"{name} is defined by BAR, not by a map: its model is in BAR, not in the library.");
            return new(null, $"BAR doesn't define {name} ({BarFeatures.Origin}): it won't appear in game.");
        }
        if (BarFeatures.ModelOf(name) is not string model)
            return new(null, feature.Note is string note ? $"{name}: {note}."
                : $"{name} is defined by BAR; its model ({feature.Object ?? "none"}) isn't shipped with the editor, so there's nothing to show.");
        var result = Load(BarFeatures.Folder, model, BarFeatures.Resolve, name);
        return result.Model == null ? result : result with { Text = result.Text + $" · BAR's model{(feature.Author is string author ? ", by " + author : "")}" };
    }

    /// <summary>
    /// A door or tunnel as it stands at the start, in its own frame: both walls and the centre (or the repair markers,
    /// if it starts destroyed) as one model when its parts share textures (built models do), else its centre alone.
    /// </summary>
    public Result ForStructure(StructureKind kind, StructureInstance placed, MapEditSession session, FeatureLookup features, AssetLibrary? library)
    {
        var local = placed with { X = 0, Z = 0, Facing = 0, Markers = null }; // markers stand where they were put, apart: the views show them
        var parts = new List<(ModelPreview Model, System.Numerics.Vector3 Offset)>();
        foreach (var part in StructureLayout.AtStart(kind, local))
        {
            var result = ForPlaced(part.Feature, PlacedItemKind.Feature, session, features, library);
            if (result.Model == null)
                return part.Role == "centre" ? result : ForPlaced(kind.Centre, PlacedItemKind.Feature, session, features, library);
            parts.Add((result.Model, new((float)part.X, 0, (float)part.Z)));
        }
        var whole = ModelPreview.Combine($"{kind.Name} (structure)", parts);
        if (whole == null)
        {
            var centre = ForPlaced(kind.Centre, PlacedItemKind.Feature, session, features, library);
            return centre with { Text = centre.Text + " · the centre (its parts use different textures)" };
        }
        return new(whole, string.Create(CultureInfo.CurrentCulture,
            $"{kind.Name} · {StructureKind.TypeName(kind.Type).Replace('_', ' ')}{(StructureLayout.StartsBroken(kind, placed) ? ", starts destroyed" : "")} · {whole.Mesh.TriangleCount:N0} triangles"));
    }

    Result Load(string root, string modelPath, Func<string, string?>? resolve, string name)
    {
        var file = new FileInfo(Path.Combine(root, modelPath.Replace('/', Path.DirectorySeparatorChar)));
        try
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}");
            if (!cache.TryGetValue(key, out var model))
            {
                model = ModelPreview.Load(root, modelPath, resolve);
                cache[key] = model;
                order.Enqueue(key);
                while (order.Count > Kept)
                    cache.Remove(order.Dequeue());
            }
            var text = string.Create(CultureInfo.CurrentCulture, $"{name} · {model.Mesh.TriangleCount:N0} triangles");
            if (model.Notes.Count > 0)
                text += " · " + (model.Notes.Count == 1 ? "drawn without a texture" : $"drawn without {model.Notes.Count} textures");
            return new(model, text);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            return new(null, $"{name}'s model can't be shown: {ex.Message}");
        }
    }
}
