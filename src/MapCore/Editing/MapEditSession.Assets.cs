using System.Security.Cryptography;
using MapCore.Assets;
using MapCore.Items;

namespace MapCore.Editing;

/// <summary>
/// Adding a library asset to the map: <c>asset-add:&lt;id&gt;</c> copies the asset's files into the package at the
/// paths they had in their own map, so its features can be placed here (and BAR loads them from features/).
/// </summary>
public sealed partial class MapEditSession
{
    /// <summary>The library <c>asset-add</c> copies from (set by the editor; without it the operation is refused).</summary>
    public AssetLibrary? Assets { get; set; }

    /// <summary>Copies an asset's files into the map (one undo step).</summary>
    public string AddAsset(AssetEntry asset) => Apply($"asset-add:{asset.Id}");

    /// <summary>Whether the map already has an asset: every one of its files, byte for byte (name-only assets: always).</summary>
    public bool HasAsset(AssetEntry asset)
    {
        if (!asset.HasFiles)
            return true;
        var existing = PackageFiles();
        return asset.Files.All(f => existing.TryGetValue(f.Path, out var entry) && SameBytes(Path.Combine(Package.Root, entry), f.Hash));
    }

    /// <summary>
    /// The assets the map has (see <see cref="HasAsset"/>), worked out together: each package file is hashed at most
    /// once, however many assets share it.
    /// </summary>
    public HashSet<string> AssetsPresent(IEnumerable<AssetEntry> assets)
    {
        var existing = PackageFiles();
        var hashes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string? HashOf(string path)
        {
            if (!hashes.TryGetValue(path, out var hash))
            {
                var full = Path.Combine(Package.Root, path);
                hashes[path] = hash = File.Exists(full) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(full))) : null;
            }
            return hash;
        }
        return assets.Where(a => !a.HasFiles || a.Files.All(f => existing.TryGetValue(f.Path, out var entry) && HashOf(entry) == f.Hash))
            .Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    Dictionary<string, string> PackageFiles() =>
        Directory.EnumerateFiles(Package.Root, "*", FileSystemUtil.AllEntries)
            .Select(f => FileSystemUtil.EntryPath(Package.Root, f))
            .GroupBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    static bool SameBytes(string file, string hash) =>
        File.Exists(file) && Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))) == hash;

    /// <summary>
    /// Where an asset's files would go (an existing file of the same path in another case keeps its spelling), and
    /// why it can't be added, if it can't: a file at the same path with different content (overwriting it would change
    /// this map's own features), or a feature of the same name already defined by another file here.
    /// </summary>
    (AssetEntry Asset, List<(AssetFile File, string Target, bool Present)> Files) PlanAsset(string id)
    {
        var library = Assets ?? throw new InvalidOperationException("No asset library is open.");
        var asset = library.Find(id) ?? throw new InvalidOperationException($"The asset '{id}' isn't in the library (removed?).");
        if (!asset.HasFiles)
            throw new InvalidOperationException($"{asset.Name} is built into {(asset.Kind == AssetKind.EngineFeature ? "the engine" : "BAR")}: there is nothing to copy; place it by name.");
        var existing = PackageFiles();
        var plan = new List<(AssetFile, string, bool)>();
        var clashes = new List<string>();
        foreach (var file in asset.Files)
        {
            if (!File.Exists(library.FilePath(asset, file)))
                throw new InvalidOperationException($"The library is missing {file.Path} of {asset.Name}; collect its map again.");
            if (existing.TryGetValue(file.Path, out var entry))
            {
                if (SameBytes(Path.Combine(Package.Root, entry), file.Hash))
                    plan.Add((file, entry, true));
                else
                    clashes.Add(entry);
            }
            else
                plan.Add((file, file.Path, false));
        }
        if (clashes.Count > 0)
            throw new InvalidOperationException($"{asset.Name} can't be added: this map already has {string.Join(", ", clashes)} with different content, " +
                                                "and replacing it would change this map's own features. Nothing was copied.");
        var ours = asset.Files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var (definitions, _) = FeatureCatalog.Load(Package.Root, Smf);
        var taken = definitions.Where(d => d.Source.StartsWith("features/", StringComparison.OrdinalIgnoreCase) && !ours.Contains(d.Source) &&
                                           asset.Defines.Contains(d.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (taken.Count > 0)
            throw new InvalidOperationException($"{asset.Name} can't be added: this map already defines {string.Join(", ", taken.Select(d => $"{d.Name} (in {d.Source})"))}. " +
                                                "Nothing was copied.");
        return (asset, plan);
    }

    string AssetAddCore(string[] args)
    {
        Expect(args, 1, "asset-add");
        var (asset, plan) = PlanAsset(args[0]);
        if (plan.All(p => p.Present))
            throw new InvalidOperationException($"{asset.Name} is already in this map (all {plan.Count} of its files, byte for byte).");
        int copied = 0;
        foreach (var (file, target, present) in plan)
        {
            if (present)
                continue;
            var path = Path.Combine(Package.Root, target.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(Assets!.FilePath(asset, file), path, overwrite: false);
            File.SetAttributes(path, FileAttributes.Normal);
            copied++;
        }
        return $"{asset.Name} added from the asset library ({asset.Category}): {copied} file{(copied == 1 ? "" : "s")} copied" +
               (copied < plan.Count ? $", {plan.Count - copied} already here" : "") +
               (asset.Defines.Count is > 1 and <= 4 ? $"; defines {string.Join(", ", asset.Defines)}" :
                asset.Defines.Count > 4 ? $"; defines {asset.Defines.Count} features" : "");
    }

    List<Snapshot> AssetSnapshots(string id)
    {
        try
        {
            var (_, plan) = PlanAsset(id);
            return [new PackageFilesSnapshot(this, plan.Where(p => !p.Present).Select(p => p.Target).ToList())];
        }
        catch (InvalidOperationException)
        {
            return []; // the operation itself will refuse, with the reason
        }
    }

    /// <summary>
    /// Undo for files an operation adds to the package: which of them existed (and their bytes), and which folders,
    /// so undo deletes what was added and removes folders it created, leaving the package as it was.
    /// </summary>
    sealed class PackageFilesSnapshot : Snapshot
    {
        readonly MapEditSession session;
        readonly IReadOnlyList<string> paths;
        readonly List<(string Path, byte[]? Bytes)> files = [];
        readonly List<string> missingFolders = [];

        public PackageFilesSnapshot(MapEditSession session, IReadOnlyList<string> paths)
        {
            this.session = session;
            this.paths = paths;
            var root = session.Package.Root;
            foreach (var p in paths)
            {
                var full = Path.Combine(root, p.Replace('/', Path.DirectorySeparatorChar));
                files.Add((full, File.Exists(full) ? File.ReadAllBytes(full) : null));
                for (var dir = Path.GetDirectoryName(full); dir != null && dir.Length > root.Length; dir = Path.GetDirectoryName(dir))
                    if (!Directory.Exists(dir) && !missingFolders.Contains(dir))
                        missingFolders.Add(dir);
            }
        }

        public override MapLayers Layers => MapLayers.Items;
        public override long Bytes => files.Sum(f => f.Bytes?.LongLength ?? 0) + 256;
        public override Snapshot CaptureAgain() => new PackageFilesSnapshot(session, paths);

        public override void Restore()
        {
            foreach (var (path, bytes) in files)
            {
                if (bytes != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!); // redo after an undo that removed the folder
                    FileSystemUtil.WriteAllBytesAtomic(path, bytes);
                }
                else if (File.Exists(path))
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                    File.Delete(path);
                }
            }
            session.structures = null; // the structures' config may be one of these files
            // Deepest first: a folder this created goes once it is empty again.
            foreach (var dir in missingFolders.OrderByDescending(d => d.Length))
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
        }
    }
}
