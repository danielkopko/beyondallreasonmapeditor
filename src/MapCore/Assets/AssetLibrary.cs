using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MapCore.Items;
using MapCore.Lua;
using MapCore.Smf;

namespace MapCore.Assets;

/// <summary>One file of an asset: its path inside a map package ('/' separators), size and SHA-256.</summary>
public sealed record AssetFile(string Path, long Size, string Hash);

/// <summary>A map an asset was found in.</summary>
public sealed class AssetSighting
{
    public string Map { get; set; } = "";
    public string? Version { get; set; }
    public string? Author { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
}

/// <summary>
/// One asset in the library. For a map feature, the asset is a definition file with everything it needs (the
/// features it defines, their models, the textures those name, files it includes, its wrecks' definitions), kept
/// byte for byte; its identity is the hash of those files. Name-only assets (BAR's own features, units and buildings,
/// and the engine's trees and vents) have no files: they are placed by name on any map.
/// </summary>
public sealed class AssetEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Defines { get; set; } = [];
    public AssetKind Kind { get; set; }
    public string Category { get; set; } = AssetCategories.Other;

    /// <summary>True when the user put it in its category (kept over the automatic choice).</summary>
    public bool CategoryChosen { get; set; }

    public string? Description { get; set; }

    /// <summary>For a map feature: the definition file the asset is, at its path in its map.</summary>
    public string? Source { get; set; }

    /// <summary>The features it defines (one file can make many: a whole set of trees), each with its model.</summary>
    public List<AssetFeature> Features { get; set; } = [];

    public List<AssetFile> Files { get; set; } = [];
    public string Hash { get; set; } = "";

    /// <summary>What the asset relies on that its map didn't contain (BAR may provide it), and similar remarks.</summary>
    public List<string> Notes { get; set; } = [];

    /// <summary>Another asset defines a feature of the same name with different files.</summary>
    public bool Variant { get; set; }

    public List<AssetSighting> SeenIn { get; set; } = [];

    /// <summary>How it was read (<see cref="AssetLibrary.CurrentParse"/>): older entries are brought up to date when seen again.</summary>
    public int ParseVersion { get; set; }

    [JsonIgnore]
    public bool HasFiles => Kind == AssetKind.MapFeature;

    /// <summary>
    /// Listed from BAR's own catalog (<see cref="BarFeatureCatalog.AssetEntries"/>), not collected from a map: shown
    /// and placed like any name-only asset, but not kept in the library (it has nothing to move or remove).
    /// </summary>
    [JsonIgnore]
    public bool BarOnly { get; init; }
}

/// <summary>One feature an asset defines.</summary>
public sealed class AssetFeature
{
    public string Name { get; set; } = "";

    /// <summary>Its model: a path among the asset's files, or null when its map didn't have it (BAR may).</summary>
    public string? Model { get; set; }

    public string? Description { get; set; }
}

/// <summary>What collecting one map added to the library.</summary>
/// <param name="Updated">Assets read again with a better reading of their files than when they were collected.</param>
public sealed record CollectReport(int Added, int Variants, int AlreadyThere, int NewNames, int KnownNames, IReadOnlyList<string> Notes, int Updated = 0)
{
    public override string ToString() =>
        $"{Added} new asset{(Added == 1 ? "" : "s")}" + (Variants > 0 ? $", {Variants} variant{(Variants == 1 ? "" : "s")} of ones already there" : "") +
        $", {AlreadyThere} already in the library" + (Updated > 0 ? $" ({Updated} brought up to date)" : "") +
        $"; {NewNames} new name{(NewNames == 1 ? "" : "s")} built into BAR or the engine ({KnownNames} known)" +
        (Notes.Count > 0 ? $"; {Notes.Count} note{(Notes.Count == 1 ? "" : "s")}" : "");
}

/// <summary>
/// The asset library: features, units and buildings collected from every map opened, kept in a folder
/// (<see cref="DefaultRoot"/> for the editor) as index.json plus each asset's files under files/&lt;id&gt;/, at the
/// paths they had in their map. Collecting a map again adds nothing new: identical assets are only marked as seen.
/// Not thread-safe; the editor uses it from one task at a time.
/// </summary>
public sealed class AssetLibrary
{
    const string IndexName = "index.json";
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    readonly List<AssetEntry> entries;

    public AssetLibrary(string root)
    {
        Root = Path.GetFullPath(root);
        var index = Path.Combine(Root, IndexName);
        try
        {
            entries = File.Exists(index) ? JsonSerializer.Deserialize<List<AssetEntry>>(File.ReadAllText(index), Json) ?? [] : [];
        }
        catch (JsonException ex)
        {
            // A damaged index is set aside, not overwritten: the files are still there to collect again.
            var aside = index + $".damaged-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(index, aside);
            LoadProblem = $"The asset library's index was damaged ({ex.Message}); it was set aside as {Path.GetFileName(aside)} and the library starts empty.";
            entries = [];
        }
    }

    /// <summary>Why the library started empty although an index was there (it was damaged), if so.</summary>
    public string? LoadProblem { get; }

    public static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BarMapEditor", "assets");

    public string Root { get; }
    public IReadOnlyList<AssetEntry> Entries => entries;

    public AssetEntry? Find(string id) => entries.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Where one of an asset's files is kept in the library.</summary>
    public string FilePath(AssetEntry asset, AssetFile file) =>
        Path.Combine(Root, "files", asset.Id, file.Path.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// Where the thumbnail of one of an asset's features is kept: thumbs/&lt;id&gt;/. An asset's files never change
    /// (its id names their content), so a thumbnail made once stays right.
    /// </summary>
    public string ThumbnailPath(AssetEntry asset, string feature) =>
        Path.Combine(Root, "thumbs", asset.Id, Slug(feature) + ".png");

    /// <summary>
    /// A thumbnail of one of BAR's own features (or, with <paramref name="unit"/>, units and buildings), kept apart
    /// for each BAR commit its models came from.
    /// </summary>
    public string ThumbnailPath(BarFeatureCatalog bar, string name, bool unit = false) =>
        Path.Combine(Root, "thumbs", "bar-" + (bar.Source?.Commit is { Length: >= 12 } commit ? commit[..12] : "local"), unit ? "units" : "features", Slug(name) + ".png");

    /// <summary>Puts an asset in a category of the user's choosing (kept when the asset is found again).</summary>
    public void SetCategory(AssetEntry asset, string category)
    {
        asset.Category = category;
        asset.CategoryChosen = true;
        Save();
    }

    /// <summary>Takes an asset out of the library, with its files.</summary>
    public void Remove(AssetEntry asset)
    {
        entries.Remove(asset);
        foreach (var folder in new[] { Path.Combine(Root, "files", asset.Id), Path.Combine(Root, "thumbs", asset.Id) })
            if (Directory.Exists(folder))
                FileSystemUtil.DeleteTree(folder);
        foreach (var other in entries.Where(e => e.HasFiles))
            other.Variant = entries.Any(e => e != other && e.HasFiles && Shares(e, other));
        Save();
    }

    static bool Shares(AssetEntry a, AssetEntry b) => a.Defines.Intersect(b.Defines, StringComparer.OrdinalIgnoreCase).Any();

    void Save()
    {
        Directory.CreateDirectory(Root);
        FileSystemUtil.WriteAllBytesAtomic(Path.Combine(Root, IndexName), JsonSerializer.SerializeToUtf8Bytes(entries, Json));
    }

    /// <summary>
    /// How assets are read from maps now. Entries read an older way are brought up to date when seen again. 3: the
    /// Lua sandbox has string.format, so definition files that call it run instead of being read from their text.
    /// </summary>
    public const int CurrentParse = 3;

    /// <summary>Folders an asset's files can come from: what a feature uses, never the map's own settings.</summary>
    static readonly string[] AssetFolders = ["features/", "objects3d/", "unittextures/", "bitmaps/", "sounds/"];

    /// <summary>A feature definition file as the game runs it, with every text it holds (to find the files it names).</summary>
    sealed record Definition(FeatureDefFile File, List<string> Dead, List<string> Strings)
    {
        public IEnumerable<string> Names => File.Features.Select(f => f.Name);
    }

    /// <summary>
    /// Adds a map's assets to the library, reading its package folder without changing it: every features/**/*.lua,
    /// run as the game runs it (<see cref="FeatureDefs"/>; files that aren't Lua, or define nothing, are skipped with
    /// a note), and the names its SMF features and featureplacer use that the map doesn't define itself (BAR's own
    /// and the engine's).
    /// </summary>
    public CollectReport Collect(string packageRoot, SmfFile smf, MapInfo info, FeaturePlacerConfig? placer, DateTime? now = null)
    {
        var when = now ?? DateTime.UtcNow;
        var notes = new List<string>();
        int added = 0, variants = 0, already = 0, updated = 0, newNames = 0, knownNames = 0;
        var resolve = ModelPaths.Resolver(packageRoot);
        string Full(string entry) => Path.Combine(packageRoot, entry.Replace('/', Path.DirectorySeparatorChar));
        var map = info.Name ?? "unnamed map";

        var definitions = new List<Definition>();
        foreach (var file in FeatureDefs.Load(packageRoot))
        {
            if (file.Features.Count == 0)
            {
                notes.Add($"{file.Entry} {file.Problem ?? "defines no features"}; skipped");
                continue;
            }
            if (file.Problem != null)
                notes.Add($"{file.Entry} {file.Problem}");
            var strings = file.Features.SelectMany(f => f.Strings).ToList();
            try
            {
                strings.AddRange(LuaLexer.Tokenize(File.ReadAllText(Full(file.Entry))).Where(t => t.Kind == LuaTokenKind.String).Select(t => (string)t.Value!));
            }
            catch (LuaSyntaxException)
            {
            }
            definitions.Add(new Definition(file, file.Features.SelectMany(f => f.Dead).Distinct().ToList(), strings.Distinct().ToList()));
        }
        var byName = new Dictionary<string, Definition>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in definitions)
            foreach (var n in d.Names)
                byName.TryAdd(n, d);

        foreach (var definition in definitions)
        {
            var needed = new SortedSet<string>(StringComparer.Ordinal);
            var assetNotes = new List<string>();
            var models = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<Definition>();
            void Need(Definition d)
            {
                if (!visited.Add(d))
                    return;
                needed.Add(d.File.Entry);
                foreach (var s in d.Strings)
                    if (s.Length > 0 && resolve(s) is string named && named != d.File.Entry &&
                        AssetFolders.Any(f => named.StartsWith(f, StringComparison.OrdinalIgnoreCase)))
                        needed.Add(named); // an included file, a normal map in customparams, ...
                foreach (var f in d.File.Features)
                    if (f.Object != null)
                        NeedModel(f.Object);
                foreach (var dead in d.Dead)
                    if (byName.TryGetValue(dead, out var wreck))
                        Need(wreck);
            }
            string? NeedModel(string obj)
            {
                if (models.TryGetValue(obj, out var known))
                    return known;
                var model = models[obj] = ModelPaths.Find(obj, resolve);
                if (model == null)
                {
                    assetNotes.Add($"model {obj} isn't in the map (BAR may have it)");
                    return null;
                }
                needed.Add(model);
                bool texturesKnown = false;
                if (resolve(Path.ChangeExtension(model, ".lua")) is string sidecar && sidecar != model)
                {
                    needed.Add(sidecar);
                    texturesKnown = true;
                    try
                    {
                        foreach (var t in LuaLexer.Tokenize(File.ReadAllText(Full(sidecar))).Where(t => t.Kind == LuaTokenKind.String))
                            if ((resolve((string)t.Value!) ?? resolve("unittextures/" + (string)t.Value!)) is string tex)
                                needed.Add(tex);
                    }
                    catch (LuaSyntaxException)
                    {
                        assetNotes.Add($"{sidecar} isn't readable Lua; its textures weren't looked for");
                    }
                }
                if (model.EndsWith(".s3o", StringComparison.OrdinalIgnoreCase))
                {
                    if (S3oModel.ReadTextures(File.ReadAllBytes(Full(model))) is not var (t1, t2))
                    {
                        assetNotes.Add($"{model} isn't a readable S3O model; its textures weren't looked for");
                        return model;
                    }
                    texturesKnown = true;
                    foreach (var t in new[] { t1, t2 })
                        if (t != null)
                        {
                            if (resolve("unittextures/" + t) is string tex)
                                needed.Add(tex);
                            else
                                assetNotes.Add($"texture {t} isn't in the map (BAR may have it)");
                        }
                }
                if (!texturesKnown)
                    assetNotes.Add($"{model}: the textures of this kind of model aren't known; only the model is copied");
                return model;
            }
            Need(definition);

            var assetFiles = needed.Select(p =>
            {
                var bytes = File.ReadAllBytes(Full(p));
                return new AssetFile(p, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)));
            }).ToList();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                string.Concat(assetFiles.Select(f => f.Path.ToLowerInvariant() + "\0" + f.Hash + "\n")))));
            var fresh = Describe(definition, assetFiles, hash, assetNotes.Distinct().ToList(), obj => models.GetValueOrDefault(obj));

            if (entries.FirstOrDefault(e => e.HasFiles && e.Hash == hash) is AssetEntry known)
            {
                if (known.ParseVersion < CurrentParse)
                {
                    Refresh(known, fresh);
                    updated++;
                }
                Sight(known);
                already++;
                continue;
            }
            // Read before with an older reading that found other files: the same definition file, from the same map.
            var sourceHash = assetFiles.First(f => f.Path == definition.File.Entry).Hash;
            if (entries.FirstOrDefault(e => e.HasFiles && e.ParseVersion < CurrentParse && e.SeenIn.Any(s => s.Map == map) &&
                                            e.Files.Any(f => f.Path.Equals(definition.File.Entry, StringComparison.OrdinalIgnoreCase) && f.Hash == sourceHash)) is AssetEntry stale)
            {
                fresh.SeenIn = stale.SeenIn;
                if (stale.CategoryChosen)
                    (fresh.Category, fresh.CategoryChosen) = (stale.Category, true);
                entries.Remove(stale);
                var folder = Path.Combine(Root, "files", stale.Id);
                if (Directory.Exists(folder))
                    FileSystemUtil.DeleteTree(folder);
                updated++;
                already++;
            }
            else
            {
                var namesakes = entries.Where(e => e.HasFiles && Shares(e, fresh)).ToList();
                if (namesakes.Count > 0)
                {
                    fresh.Variant = true;
                    foreach (var other in namesakes)
                        other.Variant = true;
                    variants++;
                }
                else
                    added++;
            }
            foreach (var file in assetFiles)
            {
                var target = FilePath(fresh, file);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Full(file.Path), target, overwrite: true);
                File.SetAttributes(target, FileAttributes.Normal);
            }
            Sight(fresh);
            entries.Add(fresh);
        }

        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void NameOnly(string name, AssetKind kind)
        {
            if (byName.ContainsKey(name) || !done.Add($"{kind}:{name}"))
                return; // the map defines it (collected with its files above), or it was counted already
            var id = $"{kind.ToString().ToLowerInvariant()}:{name.ToLowerInvariant()}";
            if (Find(id) is AssetEntry known)
            {
                Sight(known);
                knownNames++;
                return;
            }
            var asset = new AssetEntry
            {
                Id = id, Name = name, Defines = [name], Kind = kind, Category = AssetCategories.Classify(name, kind), Hash = id, ParseVersion = CurrentParse,
            };
            Sight(asset);
            entries.Add(asset);
            newNames++;
        }
        foreach (var type in smf.FeatureTypes)
            NameOnly(type, type.Contains("treetype", StringComparison.OrdinalIgnoreCase) || type.Contains("geovent", StringComparison.OrdinalIgnoreCase)
                ? AssetKind.EngineFeature : AssetKind.GameFeature);
        if (placer != null)
            foreach (var item in placer.Items)
                NameOnly(item.Name, item.Kind switch
                {
                    PlacedItemKind.Unit => AssetKind.Unit,
                    PlacedItemKind.Building => AssetKind.Building,
                    _ when item.Name.Contains("treetype", StringComparison.OrdinalIgnoreCase) || item.Name.Contains("geovent", StringComparison.OrdinalIgnoreCase)
                        => AssetKind.EngineFeature, // the engine's own, whichever list names them
                    _ => AssetKind.GameFeature,
                });
        Save();
        return new CollectReport(added, variants, already, newNames, knownNames, notes, updated);

        void Sight(AssetEntry asset)
        {
            var seen = asset.SeenIn.FirstOrDefault(s => s.Map == map && s.Version == info.Version);
            if (seen == null)
                asset.SeenIn.Add(new AssetSighting { Map = map, Version = info.Version, Author = info.Author, FirstSeen = when, LastSeen = when });
            else
                seen.LastSeen = when;
        }
    }

    /// <summary>
    /// A new library entry for a definition file: named after its feature (or after the file, when it makes several),
    /// in the category most of its features fall in.
    /// </summary>
    static AssetEntry Describe(Definition definition, List<AssetFile> files, string hash, List<string> notes, Func<string, string?> modelOf)
    {
        var features = definition.File.Features;
        var name = features.Count == 1 ? features[0].Name : Path.GetFileNameWithoutExtension(definition.File.Entry);
        var category = features
            .Select(f => AssetCategories.Classify(f.Name, AssetKind.MapFeature, f.Category, f.Description, f.Metal, f.Energy))
            .GroupBy(c => c).OrderByDescending(g => g.Count()).ThenBy(g => AssetCategories.All.ToList().IndexOf(g.Key)).First().Key;
        var description = features.Select(f => f.Description).Where(d => !string.IsNullOrWhiteSpace(d))
            .GroupBy(d => d).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        return new AssetEntry
        {
            Id = $"{Slug(name)}-{hash[..8].ToLowerInvariant()}",
            Name = name,
            Defines = features.Select(f => f.Name).ToList(),
            Kind = AssetKind.MapFeature,
            Category = category,
            Description = description,
            Source = definition.File.Entry,
            Features = features.Select(f => new AssetFeature { Name = f.Name, Model = f.Object == null ? null : modelOf(f.Object), Description = f.Description }).ToList(),
            Files = files,
            Hash = hash,
            Notes = notes,
            ParseVersion = CurrentParse,
        };
    }

    /// <summary>Brings an entry read an older way up to date (same files): its names, features, notes and automatic category.</summary>
    static void Refresh(AssetEntry entry, AssetEntry fresh)
    {
        entry.Name = fresh.Name;
        entry.Defines = fresh.Defines;
        entry.Features = fresh.Features;
        entry.Description = fresh.Description;
        entry.Source = fresh.Source;
        entry.Notes = fresh.Notes;
        if (!entry.CategoryChosen)
            entry.Category = fresh.Category;
        entry.ParseVersion = CurrentParse;
    }

    static string Slug(string name) =>
        new string(name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray());
}
