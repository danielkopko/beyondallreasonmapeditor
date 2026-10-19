using System.IO.Compression;

namespace MapCore.Archives;

/// <summary>Reads and writes map archives: .sd7 through 7-Zip, .sdz through the built-in zip support.</summary>
public static class ArchiveIO
{
    public static ArchiveFormat FormatOf(string archivePath) => Path.GetExtension(archivePath).ToLowerInvariant() switch
    {
        ".sd7" or ".7z" => ArchiveFormat.SevenZip,
        ".sdz" or ".zip" => ArchiveFormat.Zip,
        var ext => throw new NotSupportedException($"Unsupported archive type '{ext}'. Map archives are .sd7 or .sdz."),
    };

    /// <summary>Extracts every entry, including hidden and system files, into <paramref name="destDir"/>.</summary>
    public static void Extract(string archivePath, string destDir)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Archive not found.", archivePath);
        // What it unpacks to, and a margin for the edits that follow (the texture is rewritten on save).
        long unpacked = List(archivePath).Sum(e => e.Size);
        FileSystemUtil.EnsureFreeSpace(destDir, unpacked + unpacked / 2 + (64L << 20), $"unpacking {Path.GetFileName(archivePath)}");
        Directory.CreateDirectory(destDir);

        if (FormatOf(archivePath) == ArchiveFormat.SevenZip)
            SevenZip.Extract(archivePath, destDir);
        else
            ZipFile.ExtractToDirectory(archivePath, destDir); // rejects entries that would land outside destDir
    }

    /// <summary>
    /// Packs the contents of <paramref name="sourceDir"/> into a new archive; the format follows the file extension.
    /// The archive is built under a temporary name and moved into place only once complete.
    /// </summary>
    public static void Pack(string sourceDir, string archivePath, bool overwrite = false)
    {
        var root = Path.GetFullPath(sourceDir);
        var target = Path.GetFullPath(archivePath);
        var format = FormatOf(target);

        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Folder to pack not found: {root}");
        if (FileSystemUtil.IsSameOrUnder(target, root))
            throw new IOException("The output archive cannot be inside the folder being packed.");
        if (File.Exists(target) && !overwrite)
            throw new IOException($"{target} already exists.");

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = $"{target}.{Guid.NewGuid():N}.partial";
        try
        {
            if (format == ArchiveFormat.SevenZip)
                SevenZip.Pack(root, temp);
            else
                PackZip(root, temp);
            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    public static IReadOnlyList<ArchiveEntry> List(string archivePath) => Read(archivePath).Entries;

    /// <summary>
    /// One entry's bytes without extracting the archive (null if it has no such entry). The name is matched
    /// without regard to case, as the engine's file system does.
    /// </summary>
    public static byte[]? ReadEntry(string archivePath, string entry)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Archive not found.", archivePath);
        if (FormatOf(archivePath) == ArchiveFormat.SevenZip)
        {
            var name = List(archivePath).FirstOrDefault(e => !e.IsDirectory && e.Path.Equals(entry, StringComparison.OrdinalIgnoreCase))?.Path;
            return name == null ? null : SevenZip.ReadEntry(archivePath, name.Replace('/', Path.DirectorySeparatorChar));
        }
        using var zip = ZipFile.OpenRead(archivePath);
        var found = zip.Entries.FirstOrDefault(e => e.FullName.Replace('\\', '/').Equals(entry, StringComparison.OrdinalIgnoreCase));
        if (found == null)
            return null;
        using var stream = found.Open();
        using var data = new MemoryStream();
        stream.CopyTo(data);
        return data.ToArray();
    }

    /// <summary>Lists the archive's entries without extracting it.</summary>
    public static ArchiveListing Read(string archivePath)
    {
        if (!File.Exists(archivePath))
            throw new FileNotFoundException("Archive not found.", archivePath);
        return FormatOf(archivePath) == ArchiveFormat.SevenZip ? SevenZip.List(archivePath) : ListZip(archivePath);
    }

    static void PackZip(string root, string archivePath)
    {
        using var zip = ZipFile.Open(archivePath, ZipArchiveMode.Create);
        // Folder entries keep empty folders; names always use '/' as the zip format requires.
        foreach (var dir in Directory.EnumerateDirectories(root, "*", FileSystemUtil.AllEntries).Order(StringComparer.Ordinal))
            zip.CreateEntry(FileSystemUtil.EntryPath(root, dir) + "/");
        foreach (var file in Directory.EnumerateFiles(root, "*", FileSystemUtil.AllEntries).Order(StringComparer.Ordinal))
            zip.CreateEntryFromFile(file, FileSystemUtil.EntryPath(root, file), CompressionLevel.Optimal);
    }

    static ArchiveListing ListZip(string archivePath)
    {
        using var zip = ZipFile.OpenRead(archivePath);
        var entries = zip.Entries.Select(e =>
        {
            var name = e.FullName.Replace('\\', '/');
            bool isDir = name.EndsWith('/');
            var attributes = (FileAttributes)(e.ExternalAttributes & 0xFFFF) | (isDir ? FileAttributes.Directory : 0);
            uint? crc = isDir || e.Length == 0 ? null : e.Crc32;
            return new ArchiveEntry(name.TrimEnd('/'), isDir, isDir ? 0 : e.Length, crc, attributes);
        }).ToList();
        return new ArchiveListing(new Dictionary<string, string> { ["Type"] = "zip" }, entries);
    }
}
