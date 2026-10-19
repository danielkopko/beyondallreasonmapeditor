using System.IO.Compression;
using MapCore.Archives;

namespace MapCore.Tests;

static class InstallerTests
{
    [Test]
    public static void ReadsOneEntryFromEitherFormat()
    {
        var sd7 = TestData.PathOf(TestData.AcidicQuarryArchive);
        var fromSd7 = ArchiveIO.ReadEntry(sd7, "MAPINFO.LUA");
        var extracted = File.ReadAllBytes(TestData.AcidicQuarry.MapInfoPath);
        Assert.True(fromSd7 != null && fromSd7.AsSpan().SequenceEqual(extracted), "mapinfo.lua read from the .sd7 (any case) equals the extracted file");
        Assert.True(ArchiveIO.ReadEntry(sd7, "no/such/file.lua") == null, "a missing entry reads as null");

        var sdz = Path.Combine(TestData.NewWorkDir("installer-sdz"), "small.sdz");
        using (var zip = ZipFile.Open(sdz, ZipArchiveMode.Create))
            zip.CreateEntryFromFile(TestData.AcidicQuarry.MapInfoPath, "mapinfo.lua");
        Assert.True(ArchiveIO.ReadEntry(sdz, "mapinfo.lua")!.AsSpan().SequenceEqual(extracted), "and from an .sdz");
        Assert.Equal(("AcidicQuarry", "5.17"), MapInstaller.ReadIdentity(sdz)!.Value, "the name and version it declares");
    }

    [Test]
    public static void FindsSameNamedMapsAndNeverOverwritesUnasked()
    {
        var maps = TestData.NewWorkDir("installer-maps");
        var original = TestData.PathOf(TestData.AcidicQuarryArchive);
        File.Copy(original, Path.Combine(maps, "someone_elses_copy.sd7"));
        File.WriteAllText(Path.Combine(maps, "not_a_map.sd7"), "junk");

        var same = MapInstaller.FindSameMap(maps, "acidicquarry", "5.17");
        Assert.Equal(1, same.Count, "archives in the folder that claim \"AcidicQuarry 5.17\" (any case; junk is skipped)");
        Assert.Equal(0, MapInstaller.FindSameMap(maps, "AcidicQuarry", "5.18").Count, "another version is a different map");
        Assert.Equal(0, MapInstaller.FindSameMap(maps, "AcidicQuarry", "5.17", except: same[0]).Count, "the archive being replaced doesn't count");

        var target = MapInstaller.Install(original, maps);
        Assert.True(File.Exists(target) && new FileInfo(target).Length == new FileInfo(original).Length, "copied in under its own name");
        Assert.Throws<IOException>(() => MapInstaller.Install(original, maps), "a second copy is refused without overwrite");
        MapInstaller.Install(original, maps, overwrite: true);
        Assert.True(!Directory.EnumerateFiles(maps, "*.partial").Any(), "no partial file left behind");
    }
}
