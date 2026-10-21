using MapCore.Archives;

namespace MapCore;

/// <summary>
/// Copies a finished map archive into BAR's maps folder. BAR knows a map by its mapinfo name and version, so before
/// copying the folder is searched for another archive that claims the same pair: the game would list two maps under
/// one name. Nothing is ever overwritten unless asked.
/// </summary>
public static class MapInstaller
{
    /// <summary>The usual places of BAR's maps folder on Windows that exist on this machine.</summary>
    public static IReadOnlyList<string> CandidateFolders()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string[] candidates =
        [
            Path.Combine(local, "Programs", "Beyond-All-Reason", "data", "maps"),
            Path.Combine(documents, "Beyond All Reason", "maps"),
            Path.Combine(documents, "My Games", "Spring", "maps"),
            @"C:\Program Files\Beyond-All-Reason\data\maps",
        ];
        return candidates.Where(Directory.Exists).ToList();
    }

    /// <summary>The name and version an archive's mapinfo.lua declares (null if it can't be read).</summary>
    public static (string? Name, string? Version)? ReadIdentity(string archive)
    {
        try
        {
            if (ArchiveIO.ReadEntry(archive, MapPackage.MapInfoFileName) is not byte[] bytes)
                return null;
            var info = MapInfo.FromBytes(bytes);
            return (info.Name, info.Version);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or NotSupportedException or Lua.LuaSyntaxException or UnauthorizedAccessException)
        {
            return null; // not a map, or unreadable: it can't clash
        }
    }

    /// <summary>
    /// Archives in <paramref name="folder"/> (not searching subfolders) whose mapinfo declares this name and version,
    /// ignoring case, apart from <paramref name="except"/>.
    /// </summary>
    public static IReadOnlyList<string> FindSameMap(string folder, string? name, string? version, string? except = null, IProgress<string>? progress = null)
    {
        if (!Directory.Exists(folder))
            return [];
        var archives = Directory.EnumerateFiles(folder)
            .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".sd7" or ".sdz")
            .Where(f => except == null || !FileSystemUtil.SamePath(f, except))
            .ToList();
        var same = new List<string>();
        for (int i = 0; i < archives.Count; i++)
        {
            progress?.Report($"Checking the maps folder ({i + 1} of {archives.Count})…");
            if (ReadIdentity(archives[i]) is (var n, var v) &&
                string.Equals(n?.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(v?.Trim() ?? "", version?.Trim() ?? "", StringComparison.OrdinalIgnoreCase))
                same.Add(archives[i]);
        }
        return same;
    }

    /// <summary>
    /// The file BAR's lobby looks for to show a map's picture (on the setup screen and in the map list) when the lobby
    /// has none of its own (it ships pictures only for the maps it knows; it never reads one out of a map archive):
    /// the map's name and version as the engine joins them, spaces made underscores, ".jpg". The lobby reads it from
    /// LuaMenu/Images/Minimaps in BAR's data folder; the engine tells an image's kind by its contents, so a PNG
    /// under that name shows too.
    /// </summary>
    public static string LobbyPictureName(string name, string? version)
    {
        name = name.Trim();
        version = version?.Trim();
        // As the engine names a map: its name, and its version after a space unless the name already has it.
        var full = string.IsNullOrEmpty(version) || name.Contains(version, StringComparison.Ordinal) ? name : $"{name} {version}";
        return full.Replace(' ', '_') + ".jpg";
    }

    /// <summary>
    /// Writes the archive's picture for BAR's lobby (see <see cref="LobbyPictureName"/>) into the data folder that holds
    /// <paramref name="mapsFolder"/>: the map's own minimap, drawn at the map's proportions with its longer side 1024
    /// pixels, as the lobby's own pictures are. Replaces an older picture of the same map. Returns where it went, or
    /// null if the archive's name, .smf or minimap can't be read.
    /// </summary>
    public static string? InstallLobbyPicture(string archive, string mapsFolder)
    {
        if (ReadIdentity(archive) is not (string name, var version) || string.IsNullOrWhiteSpace(name))
            return null;
        var smfEntries = ArchiveIO.List(archive).Where(e => !e.IsDirectory && e.Path.EndsWith(".smf", StringComparison.OrdinalIgnoreCase)).ToList();
        string? declared = null;
        try
        {
            declared = ArchiveIO.ReadEntry(archive, MapPackage.MapInfoFileName) is byte[] info ? MapInfo.FromBytes(info).MapFile?.Replace('\\', '/') : null;
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or Lua.LuaSyntaxException)
        {
        }
        var entry = smfEntries.FirstOrDefault(e => declared != null && e.Path.Equals(declared, StringComparison.OrdinalIgnoreCase))
                    ?? (smfEntries.Count == 1 ? smfEntries[0] : null);
        if (entry == null || ArchiveIO.ReadEntry(archive, entry.Path) is not byte[] bytes)
            return null;
        var smf = Smf.SmfFile.Load(bytes);
        double longer = Math.Max(smf.MapX, smf.MapY);
        int width = Math.Max(1, (int)Math.Round(Smf.Minimap.Size * smf.MapX / longer));
        int height = Math.Max(1, (int)Math.Round(Smf.Minimap.Size * smf.MapY / longer));
        var picture = Smf.Minimap.Decode(smf).Resample(width, height);
        var dataFolder = Path.GetDirectoryName(Path.GetFullPath(mapsFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                         ?? throw new IOException($"{mapsFolder} has no parent folder for BAR's lobby pictures.");
        var folder = Path.Combine(dataFolder, "LuaMenu", "Images", "Minimaps");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, LobbyPictureName(name, version));
        var temp = target + ".partial";
        Imaging.PngWriter.WriteRgba(temp, picture);
        File.Move(temp, target, overwrite: true);
        return target;
    }

    /// <summary>Copies the archive into the folder under its own file name; returns where it went.</summary>
    /// <exception cref="IOException">A file of that name is already there and <paramref name="overwrite"/> is false.</exception>
    public static string Install(string archive, string folder, bool overwrite = false)
    {
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, Path.GetFileName(archive));
        if (FileSystemUtil.SamePath(target, archive))
            return target; // already there
        if (File.Exists(target) && !overwrite)
            throw new IOException($"{target} already exists.");
        var temp = target + ".partial";
        File.Copy(archive, temp, overwrite: true);
        File.Move(temp, target, overwrite: true);
        return target;
    }
}
