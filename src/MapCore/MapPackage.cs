using MapCore.Archives;
using MapCore.Smf;

namespace MapCore;

/// <summary>
/// A map archive extracted into a workspace folder. Edits happen on the files in <see cref="Root"/>;
/// <see cref="SaveAs"/> packs the folder into a new archive. The source archive is never written to.
/// </summary>
public sealed class MapPackage
{
    public const string MapInfoFileName = "mapinfo.lua";

    /// <summary>The workspace folder holding the extracted map files.</summary>
    public string Root { get; }

    /// <summary>The archive this package was extracted from, if any.</summary>
    public string? SourceArchive { get; }

    /// <summary>Path of the .smf inside the package, relative to <see cref="Root"/>, with '/' separators.</summary>
    public string SmfEntry { get; }

    public string MapInfoPath => Path.Combine(Root, MapInfoFileName);
    public string SmfPath => Path.Combine(Root, SmfEntry);

    MapPackage(string root, string? sourceArchive, string smfEntry)
    {
        Root = root;
        SourceArchive = sourceArchive;
        SmfEntry = smfEntry;
    }

    /// <summary>Extracts <paramref name="archivePath"/> into <paramref name="workspaceDir"/>, which must be new or empty.</summary>
    public static MapPackage Open(string archivePath, string workspaceDir)
    {
        if (!FileSystemUtil.IsMissingOrEmpty(workspaceDir))
            throw new IOException($"Workspace folder is not empty: {workspaceDir}");
        ArchiveIO.Extract(archivePath, workspaceDir);
        return OpenFolder(workspaceDir, Path.GetFullPath(archivePath));
    }

    /// <summary>Opens an already-extracted map folder.</summary>
    public static MapPackage OpenFolder(string root, string? sourceArchive = null)
    {
        root = Path.GetFullPath(root);
        var mapInfo = Path.Combine(root, MapInfoFileName);
        if (!File.Exists(mapInfo))
        {
            // A common packaging mistake: everything zipped inside one extra top-level folder.
            var nested = Directory.EnumerateFiles(root, MapInfoFileName, FileSystemUtil.AllEntries).FirstOrDefault();
            throw new InvalidDataException(nested == null
                ? $"Not a map: no {MapInfoFileName} found."
                : $"{MapInfoFileName} must be at the top of the archive, but was found at '{FileSystemUtil.EntryPath(root, nested)}'.");
        }
        return new MapPackage(root, sourceArchive, FindSmf(root, DeclaredMapFile(mapInfo)));
    }

    /// <summary>mapinfo's <c>mapfile</c>, or null if it has none or cannot be read (opening still works then).</summary>
    static string? DeclaredMapFile(string mapInfoPath)
    {
        try
        {
            return MapInfo.Load(mapInfoPath).MapFile;
        }
        catch (Exception ex) when (ex is Lua.LuaSyntaxException or InvalidDataException)
        {
            return null;
        }
    }

    public SmfFile LoadSmf() => SmfFile.Load(SmfPath);

    /// <exception cref="Lua.LuaSyntaxException">mapinfo.lua is not Lua this reader understands.</exception>
    public MapInfo LoadMapInfo() => MapInfo.Load(MapInfoPath);

    public void SaveMapInfo(MapInfo mapInfo) => mapInfo.Save(MapInfoPath);

    /// <summary>
    /// The package path of a mapinfo resource texture, looked up as the engine does: as given, then under maps/.
    /// Null if neither exists.
    /// </summary>
    public string? ResolveResource(string name)
    {
        name = name.Replace('\\', '/');
        foreach (var candidate in new[] { name, "maps/" + name })
            if (File.Exists(Path.Combine(Root, candidate)))
                return candidate;
        return null;
    }

    /// <summary>The map's placed objects: <paramref name="smf"/>'s features plus the featureplacer lists, if any.</summary>
    public Items.MapItems LoadItems(SmfFile smf) => new(smf, Items.FeaturePlacerConfig.Load(Root));

    /// <summary>Writes both item stores: the SMF (its feature list) and the featureplacer file.</summary>
    public void SaveItems(Items.MapItems items)
    {
        items.Smf.Save(SmfPath);
        items.Placer?.Save(Root);
    }

    /// <summary>The map texture: <paramref name="smf"/>'s tile index plus the tile files the engine would load.</summary>
    public MapTexture LoadTexture(SmfFile smf)
    {
        var entries = ResolveSmtEntries(smf);
        return new MapTexture(smf, entries.Select(e => SmtFile.Load(Path.Combine(Root, e))).ToList(), entries);
    }

    /// <summary>
    /// Writes the texture's tile files and its SMF together. They must always be saved as a pair: editing a
    /// shared tile adds a tile to the .smt and changes the SMF's tile index and tile counts.
    /// </summary>
    public void SaveTexture(MapTexture texture)
    {
        texture.SaveTileFiles(Root);
        texture.Smf.Save(SmfPath);
    }

    /// <summary>
    /// Package paths of the tile files the engine loads, in SMF order. Mirrors SMFGroundTextures::LoadTiles:
    /// mapinfo's smf.smtFileName0..N replace the SMF's names only when there is exactly one per tile file;
    /// each name is looked up in the SMF's folder first, then from the package root.
    /// </summary>
    /// <exception cref="InvalidDataException">A tile file is missing (the engine would draw its tiles red).</exception>
    public IReadOnlyList<string> ResolveSmtEntries(SmfFile smf)
    {
        var mapInfoNames = LoadMapInfo().SmtFileNames;
        bool useMapInfo = mapInfoNames.Count > 0 && mapInfoNames.Count == smf.TileFiles.Count;
        var smfDir = SmfEntry.Contains('/') ? SmfEntry[..(SmfEntry.LastIndexOf('/') + 1)] : "";

        var files = Directory.EnumerateFiles(Root, "*", FileSystemUtil.AllEntries)
            .Select(f => FileSystemUtil.EntryPath(Root, f))
            .GroupBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var entries = new List<string>();
        for (int i = 0; i < smf.TileFiles.Count; i++)
        {
            var name = (useMapInfo ? mapInfoNames[i] : smf.TileFiles[i].FileName).Replace('\\', '/');
            if (files.TryGetValue(smfDir + name, out var entry) || files.TryGetValue(name, out entry))
                entries.Add(entry);
            else
                throw new InvalidDataException(
                    $"Tile file {i} ('{name}') was not found in the package; the engine would draw its tiles red.");
        }
        return entries;
    }

    /// <summary>
    /// SMF sections that mapinfo.lua replaces with an image file (minimapTex, metalmapTex, typemapTex,
    /// grassmapTex). Editing the SMF's own copy of those has no effect in game.
    /// </summary>
    public IReadOnlyDictionary<string, string> TextureOverrides() => LoadMapInfo().TextureOverrides;

    /// <summary>Packs the workspace into a new archive (.sd7 or .sdz, from the extension).</summary>
    public void SaveAs(string archivePath, bool overwrite = false)
    {
        if (SourceArchive != null && FileSystemUtil.SamePath(archivePath, SourceArchive))
            throw new IOException("Refusing to overwrite the source archive. Save under a new name.");
        if (!File.Exists(MapInfoPath))
            throw new InvalidDataException($"{MapInfoFileName} is missing from the workspace.");
        ArchiveIO.Pack(Root, archivePath, overwrite);
    }

    /// <summary>
    /// The .smf named by mapinfo's <c>mapfile</c> if it exists, otherwise the only .smf in the package.
    /// </summary>
    static string FindSmf(string root, string? declared)
    {
        var smfFiles = Directory.EnumerateFiles(root, "*.smf", FileSystemUtil.AllEntries)
            .Select(f => FileSystemUtil.EntryPath(root, f))
            .ToList();

        if (declared != null)
        {
            var wanted = declared.Replace('\\', '/');
            var match = smfFiles.FirstOrDefault(f => f.Equals(wanted, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match;
        }

        return smfFiles.Count switch
        {
            1 => smfFiles[0],
            0 => throw new InvalidDataException("Not a map: the archive contains no .smf file."),
            _ => throw new InvalidDataException(
                $"Several .smf files found ({string.Join(", ", smfFiles)}) and mapinfo.lua does not name one that exists."),
        };
    }
}
