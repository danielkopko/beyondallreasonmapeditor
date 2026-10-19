namespace MapCore.Archives;

public enum ArchiveFormat { SevenZip, Zip }

/// <summary>One file or folder inside a map archive.</summary>
/// <param name="Path">Relative path with '/' separators and no trailing slash.</param>
/// <param name="Crc">CRC-32 of the contents; null for folders and empty files.</param>
public sealed record ArchiveEntry(string Path, bool IsDirectory, long Size, uint? Crc, FileAttributes Attributes);

/// <param name="Properties">Archive-level properties reported by the reader (for 7z: Type, Solid, Blocks, ...).</param>
public sealed record ArchiveListing(IReadOnlyDictionary<string, string> Properties, IReadOnlyList<ArchiveEntry> Entries);

public static class ArchiveComparer
{
    // The archive bit is set or cleared by whatever tool last touched a file; it carries no meaning for maps.
    const FileAttributes SignificantAttributes =
        FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly;

    /// <summary>Human-readable differences between two listings; empty when they match.</summary>
    public static List<string> Diff(IReadOnlyList<ArchiveEntry> expected, IReadOnlyList<ArchiveEntry> actual, bool compareAttributes)
    {
        var diffs = new List<string>();
        var actualByPath = actual.ToDictionary(e => e.Path, StringComparer.Ordinal);

        foreach (var e in expected)
        {
            if (!actualByPath.Remove(e.Path, out var a))
            {
                diffs.Add($"missing: {e.Path}");
                continue;
            }
            if (e.IsDirectory != a.IsDirectory)
                diffs.Add($"changed: {e.Path} (folder {e.IsDirectory} -> {a.IsDirectory})");
            else if (e.Size != a.Size || e.Crc != a.Crc)
                diffs.Add($"changed: {e.Path} (size {e.Size} -> {a.Size}, crc {e.Crc:X8} -> {a.Crc:X8})");
            else if (compareAttributes && (e.Attributes & SignificantAttributes) != (a.Attributes & SignificantAttributes))
                diffs.Add($"changed: {e.Path} (attributes {e.Attributes} -> {a.Attributes})");
        }
        foreach (var path in actualByPath.Keys.Order(StringComparer.Ordinal))
            diffs.Add($"extra: {path}");
        return diffs;
    }
}
