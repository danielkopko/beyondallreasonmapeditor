using System.IO.Compression;
using System.Security.Cryptography;
using MapCore.Archives;

namespace MapCore.Tests;

static class ArchiveTests
{
    const string MapInfoCrcHex = "D7D486FE"; // from `7z l -slt` on the original archive

    [Test]
    public static void ListingParserReadsOriginalArchive()
    {
        var entries = TestData.AcidicQuarryListing;
        Assert.Equal(48, entries.Count(e => !e.IsDirectory), "file count");
        Assert.Equal(12, entries.Count(e => e.IsDirectory), "folder count");

        var mapInfo = entries.Single(e => e.Path == "mapinfo.lua");
        Assert.Equal(7256L, mapInfo.Size, "mapinfo.lua size");
        Assert.Equal(MapInfoCrcHex, mapInfo.Crc?.ToString("X8"), "mapinfo.lua CRC");

        var desktopIni = entries.Single(e => e.Path == "mapconfig/featureplacer/desktop.ini");
        Assert.True(desktopIni.Attributes.HasFlag(FileAttributes.Hidden | FileAttributes.System), "desktop.ini should be hidden + system");

        var empty = entries.Single(e => e.Path == "mapconfig/featureplacer/featureplacement_set.lua");
        Assert.Equal(0L, empty.Size, "empty file size");
        Assert.Equal<uint?>(null, empty.Crc, "empty file CRC");
        Assert.True(entries.All(e => !e.Path.Contains('\\')), "paths should use '/'");
    }

    [Test]
    public static void OpenFindsMapInfoAndSmf()
    {
        var package = TestData.AcidicQuarry;
        Assert.True(File.Exists(package.MapInfoPath), "mapinfo.lua extracted");
        Assert.Equal("maps/AcidicQuarry.smf", package.SmfEntry, "SMF entry");
        Assert.True(File.Exists(package.SmfPath), "SMF extracted");
    }

    [Test]
    public static void ExtractKeepsHiddenAndEmptyFiles()
    {
        var root = TestData.AcidicQuarry.Root;
        var files = Directory.EnumerateFiles(root, "*", FileSystemUtil.AllEntries).ToList();
        var folders = Directory.EnumerateDirectories(root, "*", FileSystemUtil.AllEntries).ToList();
        Assert.Equal(TestData.AcidicQuarryListing.Count(e => !e.IsDirectory), files.Count, "files on disk");
        Assert.Equal(TestData.AcidicQuarryListing.Count(e => e.IsDirectory), folders.Count, "folders on disk");

        var desktopIni = Path.Combine(root, "mapconfig", "mapinfo", "desktop.ini");
        Assert.True(File.GetAttributes(desktopIni).HasFlag(FileAttributes.Hidden), "desktop.ini extracted as hidden");
        Assert.Equal(0L, new FileInfo(Path.Combine(root, "mapconfig", "featureplacer", "featureplacement_set.lua")).Length, "empty file");
    }

    [Test]
    public static void SevenZipRepackMatchesOriginal()
    {
        var output = Path.Combine(TestData.NewWorkDir("repack-7z"), "acidicquarry_repack.sd7");
        TestData.AcidicQuarry.SaveAs(output);

        var listing = ArchiveIO.Read(output);
        Assert.Empty(ArchiveComparer.Diff(TestData.AcidicQuarryListing, listing.Entries, compareAttributes: true), "repacked .sd7 vs original");
        Assert.Equal("-", listing.Properties.GetValueOrDefault("Solid"), "solid flag (non-solid expected)");
        Assert.Equal("LZMA2:24", listing.Properties.GetValueOrDefault("Method"), "compression method (16 MB dictionary, as the original)");

        var reopened = MapPackage.Open(output, TestData.NewWorkDir("reopen-7z"));
        Assert.Empty(TreeDifferences(TestData.AcidicQuarry.Root, reopened.Root), "re-extracted .sd7 vs first extraction");
    }

    [Test]
    public static void ZipRepackMatchesOriginal()
    {
        var output = Path.Combine(TestData.NewWorkDir("repack-zip"), "acidicquarry_repack.sdz");
        TestData.AcidicQuarry.SaveAs(output);

        Assert.Empty(ArchiveComparer.Diff(TestData.AcidicQuarryListing, ArchiveIO.List(output), compareAttributes: false), "repacked .sdz vs original");
        using (var zip = ZipFile.OpenRead(output))
        {
            Assert.True(zip.Entries.All(e => !e.FullName.Contains('\\')), "zip entry names should use '/'");
            Assert.True(zip.Entries.Any(e => e.FullName == "mapinfo.lua"), "mapinfo.lua at the archive root");
        }

        var reopened = MapPackage.Open(output, TestData.NewWorkDir("reopen-zip"));
        Assert.Empty(TreeDifferences(TestData.AcidicQuarry.Root, reopened.Root), "re-extracted .sdz vs first extraction");
    }

    [Test]
    public static void SaveAsRefusesSourceArchive()
    {
        var source = TestData.PathOf(TestData.AcidicQuarryArchive);
        var before = File.GetLastWriteTimeUtc(source);
        Assert.Throws<IOException>(() => TestData.AcidicQuarry.SaveAs(source, overwrite: true), "saving over the source");
        Assert.Equal(before, File.GetLastWriteTimeUtc(source), "source archive timestamp");
    }

    [Test]
    public static void SaveAsRefusesExistingFileUnlessOverwrite()
    {
        var existing = Path.Combine(TestData.NewWorkDir("existing"), "taken.sdz");
        File.WriteAllText(existing, "keep me");
        Assert.Throws<IOException>(() => TestData.AcidicQuarry.SaveAs(existing), "saving over an existing file");
        Assert.Equal("keep me", File.ReadAllText(existing), "existing file contents");
    }

    [Test]
    public static void SaveAsRefusesOutputInsideWorkspace()
    {
        var inside = Path.Combine(TestData.AcidicQuarry.Root, "nested.sdz");
        Assert.Throws<IOException>(() => TestData.AcidicQuarry.SaveAs(inside), "saving inside the workspace");
        Assert.True(!File.Exists(inside), "no archive left inside the workspace");
    }

    [Test]
    public static void OpenRefusesNonEmptyWorkspace()
    {
        var workspace = TestData.NewWorkDir("busy");
        File.WriteAllText(Path.Combine(workspace, "user-file.txt"), "x");
        Assert.Throws<IOException>(() => MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), workspace), "opening into a non-empty folder");
    }

    [Test]
    public static void OpenFolderExplainsExtraTopLevelFolder()
    {
        var root = TestData.NewWorkDir("nested");
        Directory.CreateDirectory(Path.Combine(root, "inner"));
        File.WriteAllText(Path.Combine(root, "inner", "mapinfo.lua"), "return {}");
        var ex = Assert.Throws<InvalidDataException>(() => MapPackage.OpenFolder(root), "nested mapinfo.lua");
        Assert.True(ex.Message.Contains("inner/mapinfo.lua"), $"message should name the nested file: {ex.Message}");
    }

    [Test]
    public static void DiffReportsEachKindOfDifference()
    {
        var a = new ArchiveEntry("a.txt", false, 3, 0x11111111, FileAttributes.Archive);
        var b = new ArchiveEntry("b.txt", false, 3, 0x22222222, FileAttributes.Hidden);
        var c = new ArchiveEntry("c.txt", false, 3, 0x33333333, FileAttributes.Archive);
        var expected = new[] { a, b, c };
        var actual = new[] { a with { Crc = 0x99999999 }, b with { Attributes = FileAttributes.Archive }, new ArchiveEntry("d.txt", false, 1, 1, 0) };

        var withAttributes = ArchiveComparer.Diff(expected, actual, compareAttributes: true);
        Assert.Equal(4, withAttributes.Count, "differences with attributes: " + string.Join("; ", withAttributes));
        Assert.True(withAttributes.Any(d => d.StartsWith("changed: a.txt")), "content change reported");
        Assert.True(withAttributes.Any(d => d.StartsWith("changed: b.txt")), "attribute change reported");
        Assert.True(withAttributes.Contains("missing: c.txt"), "missing file reported");
        Assert.True(withAttributes.Contains("extra: d.txt"), "extra file reported");

        Assert.Equal(3, ArchiveComparer.Diff(expected, actual, compareAttributes: false).Count, "differences without attributes");
        Assert.Equal(0, ArchiveComparer.Diff(expected, expected, compareAttributes: true).Count, "identical listings");
    }

    /// <summary>Differences between two folder trees: missing/extra paths and files whose bytes differ.</summary>
    static List<string> TreeDifferences(string expectedRoot, string actualRoot)
    {
        static Dictionary<string, string> Snapshot(string root) =>
            Directory.EnumerateFileSystemEntries(root, "*", FileSystemUtil.AllEntries).ToDictionary(
                p => FileSystemUtil.EntryPath(root, p),
                p => Directory.Exists(p) ? "<dir>" : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))),
                StringComparer.Ordinal);

        var expected = Snapshot(expectedRoot);
        var actual = Snapshot(actualRoot);
        var diffs = new List<string>();
        foreach (var (path, hash) in expected)
        {
            if (!actual.Remove(path, out var other))
                diffs.Add($"missing: {path}");
            else if (other != hash)
                diffs.Add($"content differs: {path}");
        }
        diffs.AddRange(actual.Keys.Select(p => $"extra: {p}"));
        return diffs;
    }
}
