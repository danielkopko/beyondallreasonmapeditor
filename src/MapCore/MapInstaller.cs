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
