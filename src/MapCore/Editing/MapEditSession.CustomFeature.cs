using System.Text;
using MapCore.Assets;
using MapCore.Items;

namespace MapCore.Editing;

/// <summary>
/// A new feature made from another's model: <c>feature-custom:&lt;name&gt;:&lt;base feature&gt;:&lt;settings&gt;</c>
/// writes features/&lt;name&gt;.lua (see <see cref="CustomFeature"/>), so the new feature can be placed like any other.
/// The base is a feature the map defines, or one from the asset library: then its model and the files the model needs
/// (textures, a .lua sidecar, its normal map) are copied in too, but not its definition. One undo step either way.
/// </summary>
public sealed partial class MapEditSession
{
    /// <summary>Makes a new feature from another feature's model (one undo step).</summary>
    public string AddCustomFeature(string name, string baseFeature, CustomFeatureSettings settings) =>
        Apply($"feature-custom:{name}:{baseFeature}:{settings.Encode()}");

    /// <summary>
    /// The new features made with the editor's new-feature tool that this map has, by name: each one's
    /// features/&lt;name&gt;.lua says so on its first line, so ones made before this session are found too. The parts of a
    /// structure kind made from models are written the same way: the kinds' part names are left out.
    /// </summary>
    public IReadOnlyList<MadeFeature> MadeFeatures()
    {
        var folder = Path.Combine(Package.Root, "features");
        if (!Directory.Exists(folder))
            return [];
        var config = Structures;
        var parts = config.Kinds.Values.SelectMany(k => new[] { k.Wall, k.Centre, k.Repair, k.Marker, k.Debris, k.Beam }).OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        Func<string, string?>? resolve = null;
        var made = new List<MadeFeature>();
        foreach (var file in Directory.EnumerateFiles(folder, "*.lua", new EnumerationOptions { AttributesToSkip = 0 }).Order(StringComparer.Ordinal))
        {
            string first;
            using (var reader = new StreamReader(file))
                first = reader.ReadLine() ?? "";
            if (CustomFeature.MadeBy(first) is not { } header || header.Name != Path.GetFileNameWithoutExtension(file) || parts.Contains(header.Name))
                continue;
            var entry = "features/" + Path.GetFileName(file);
            if (FeatureDefs.Read(entry, File.ReadAllText(file)).Features.FirstOrDefault(d => d.Name == header.Name) is not FeatureDef def)
                continue;
            var settings = CustomFeatureSettings.FromDefinition(def) with
            {
                BuildOn = config.BuildOn.TryGetValue(header.Name, out bool noCrossing) ? noCrossing ? 2 : 1 : 0,
            };
            var model = def.Object is string obj ? ModelPaths.Find(obj, resolve ??= ModelPaths.Resolver(Package.Root)) : null;
            made.Add(new MadeFeature(header.Name, header.BaseName, model, settings));
        }
        return made;
    }

    /// <summary>The definition a library asset gives one of its features (read from the library's copy of its file).</summary>
    public static FeatureDef? LibraryDefinition(AssetLibrary library, AssetEntry asset, string feature)
    {
        var source = asset.Files.FirstOrDefault(f => f.Path.Equals(asset.Source, StringComparison.OrdinalIgnoreCase));
        if (source == null)
            return null;
        var file = FeatureDefs.Read(source.Path, File.ReadAllText(library.FilePath(asset, source)));
        return file.Features.FirstOrDefault(f => f.Name.Equals(feature, StringComparison.OrdinalIgnoreCase));
    }

    sealed record CustomPlan(string Name, string BaseName, string Entry, string Text, List<(string From, string Target, bool Present)> Copies,
        List<(string Entry, string Text)> Extra);

    CustomPlan PlanCustomFeature(string[] args)
    {
        Expect(args, 3, "feature-custom");
        var (name, baseName) = (args[0], args[1].ToLowerInvariant());
        if (!CustomFeature.NamePattern.IsMatch(name))
            throw new InvalidOperationException($"'{name}' can't be a feature name: use lower-case letters, digits and '_', starting with a letter (64 at most).");
        if (name.Contains("treetype", StringComparison.Ordinal) || name.Contains("geovent", StringComparison.Ordinal))
            throw new InvalidOperationException($"'{name}' would be taken for one of the engine's own trees or vents.");
        var settings = CustomFeatureSettings.Decode(args[2]);
        if (settings.Problem is string problem)
            throw new InvalidOperationException($"{name} can't be made: {problem}.");
        var defined = FeatureDefs.Load(Package.Root).SelectMany(f => f.Features).ToList();
        if (defined.Any(d => d.Name == name))
            throw new InvalidOperationException($"This map already defines a feature called {name}.");
        var existing = PackageFiles();
        var entry = $"features/{name}.lua";
        if (existing.ContainsKey(entry))
            throw new InvalidOperationException($"This map already has {existing[entry]}.");
        if (Assets?.Entries.FirstOrDefault(e => !e.HasFiles && e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is AssetEntry builtIn)
            throw new InvalidOperationException($"{name} is the name of a feature built into {(builtIn.Kind == AssetKind.EngineFeature ? "the engine" : "BAR")}; choose another.");

        var copies = new List<(string From, string Target, bool Present)>();
        var baseDef = ModelSource(baseName, name, defined, existing, copies);
        var category = baseDef.Table?.GetIgnoreCase("category") as string;
        // Something to build on is written blocking (shots hit it, and it blocks if the gadget isn't running);
        // the structures gadget takes it off the blocking map.
        var text = CustomFeature.Lua(name, baseName, baseDef.Object!, baseDef.Table, category,
            settings.BuildOn > 0 ? settings with { Blocking = true } : settings);
        var extra = new List<(string Entry, string Text)>();
        if (settings.BuildOn > 0)
        {
            var config = MapCore.Structures.StructureConfig.Load(Package.Root);
            config.BuildOn[name] = settings.BuildOn == 2;
            if (settings.BuildOn == 2)
                AssignTerrainType(config);
            extra.Add((MapCore.Structures.StructureConfig.Entry, config.ToLua()));
            extra.AddRange(GadgetFiles(existing));
        }
        return new CustomPlan(name, baseName, entry, text, copies, extra);
    }

    /// <summary>
    /// The definition whose model a new feature uses: one the map defines (its model must be in the map), or one from
    /// the asset library, whose model and the files it needs (textures, a .lua sidecar, its normal map) are added to
    /// <paramref name="copies"/> (not its definition). A file of the same path with other content refuses.
    /// </summary>
    FeatureDef ModelSource(string baseName, string forName, IReadOnlyList<FeatureDef> defined, Dictionary<string, string> existing,
        List<(string From, string Target, bool Present)> copies)
    {
        var baseDef = defined.FirstOrDefault(d => d.Name == baseName);
        if (baseDef != null)
        {
            if (baseDef.Object == null)
                throw new InvalidOperationException($"{baseName} names no model to use.");
            if (ModelPaths.Find(baseDef.Object, p => existing.TryGetValue(p.Replace('\\', '/').TrimStart('/'), out var e) ? e : null) == null)
                throw new InvalidOperationException($"{baseName}'s model {baseDef.Object} isn't in this map (BAR may have it): a feature without its model is dropped by BAR.");
            return baseDef;
        }
        var library = Assets ?? throw new InvalidOperationException($"This map doesn't define {baseName}, and no asset library is open to take it from.");
        var (asset, feature) = library.Entries.Where(e => e.HasFiles)
            .SelectMany(e => e.Features.Select(f => (Asset: e, Feature: f)))
            .FirstOrDefault(x => x.Feature.Name.Equals(baseName, StringComparison.OrdinalIgnoreCase) && x.Feature.Model != null);
        if (asset == null)
            throw new InvalidOperationException($"Neither this map nor the asset library has a model for {baseName}.");
        baseDef = LibraryDefinition(library, asset, feature.Name)
                  ?? throw new InvalidOperationException($"The library's copy of {asset.Source} doesn't define {baseName}; collect its map again.");
        if (baseDef.Object == null)
            throw new InvalidOperationException($"{baseName} names no model to use.");
        // The model, and what it needs: its textures, a .lua sidecar, its normal map. Not the base's definition.
        var files = asset.Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        var needed = new List<string> { feature.Model! };
        var modelFile = library.FilePath(asset, files[feature.Model!]);
        if (feature.Model!.EndsWith(".s3o", StringComparison.OrdinalIgnoreCase) && S3oModel.ReadTextures(File.ReadAllBytes(modelFile)) is var (t1, t2))
            foreach (var t in new[] { t1, t2 })
                if (t != null && ModelPreview.TexturePath(t, p => files.TryGetValue(p, out var f) ? f.Path : null) is string texture)
                    needed.Add(texture);
        var sidecar = Path.ChangeExtension(feature.Model!, ".lua");
        if (files.ContainsKey(sidecar))
            needed.Add(files[sidecar].Path);
        if (baseDef.Table?.GetIgnoreCase("customparams") is Lua.LuaMap custom && custom.GetIgnoreCase("normaltex") is string normal &&
            files.TryGetValue(normal.Replace('\\', '/').TrimStart('/'), out var normalFile))
            needed.Add(normalFile.Path);
        var clashes = new List<string>();
        foreach (var path in needed.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var file = files[path];
            var from = library.FilePath(asset, file);
            if (!File.Exists(from))
                throw new InvalidOperationException($"The library is missing {file.Path} of {asset.Name}; collect its map again.");
            if (copies.Any(c => c.Target.Equals(file.Path, StringComparison.OrdinalIgnoreCase)))
                continue; // another part of the same job brings it already
            if (existing.TryGetValue(file.Path, out var here))
            {
                if (SameBytes(Path.Combine(Package.Root, here), file.Hash))
                    copies.Add((from, here, true));
                else
                    clashes.Add(here);
            }
            else
                copies.Add((from, file.Path, false));
        }
        if (clashes.Count > 0)
            throw new InvalidOperationException($"{forName} can't be made: this map already has {string.Join(", ", clashes)} with different content, " +
                                                "and replacing it would change this map's own features. Nothing was changed.");
        return baseDef;
    }

    string CustomFeatureCore(string[] args)
    {
        var plan = PlanCustomFeature(args);
        int copied = 0;
        foreach (var (from, target, present) in plan.Copies)
        {
            if (present)
                continue;
            var path = Path.Combine(Package.Root, target.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(from, path, overwrite: false);
            File.SetAttributes(path, FileAttributes.Normal);
            copied++;
        }
        foreach (var (entry, text) in plan.Extra.Prepend((plan.Entry, plan.Text)))
        {
            var path = Path.Combine(Package.Root, entry.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false)); // Lua 5.1 won't read a byte-order mark
        }
        structures = null;
        var settings = CustomFeatureSettings.Decode(args[2]);
        return $"{plan.Name}: a new feature from {plan.BaseName}'s model, written as {plan.Entry}" +
               (copied > 0 ? $" ({copied} file(s) for its model copied from the asset library)" : "") +
               $"; health {settings.Health:N0}, reclaim time {settings.ReclaimTime:N0}{(settings.Reclaimable ? "" : " (not reclaimable)")}" +
               (settings.BuildOn == 0 ? "" : settings.BuildOn == 1 ? "; buildings can go on it" : "; buildings can go on it, units can't cross it");
    }

    List<Snapshot> CustomFeatureSnapshots(string[] args)
    {
        try
        {
            var plan = PlanCustomFeature(args);
            return [new PackageFilesSnapshot(this, [plan.Entry, .. plan.Extra.Select(e => e.Entry), .. plan.Copies.Where(c => !c.Present).Select(c => c.Target)])];
        }
        catch (InvalidOperationException)
        {
            return []; // the operation itself will refuse, with the reason
        }
    }
}
