using System.IO.Compression;
using MapCore.Archives;
using MapCore.Imaging;
using MapCore.Smf;

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

    /// <summary>
    /// BAR's lobby shows a map's picture only from its own set or from LuaMenu/Images/Minimaps/&lt;name_version&gt;.jpg in
    /// BAR's data folder: installing a map puts its minimap there, at the map's proportions.
    /// </summary>
    [Test]
    public static void InstallPutsTheMapsPictureWhereTheLobbyLooks()
    {
        Assert.Equal("AcidicQuarry_5.17.jpg", MapInstaller.LobbyPictureName("AcidicQuarry", "5.17"), "as the lobby names its own pictures");
        Assert.Equal("Simmering_Ravine_1.4.jpg", MapInstaller.LobbyPictureName("Simmering Ravine", "1.4"), "spaces made underscores");
        Assert.Equal("Glacier_v2.jpg", MapInstaller.LobbyPictureName("Glacier v2", "v2"), "a version already in the name isn't repeated");
        Assert.Equal("Plain.jpg", MapInstaller.LobbyPictureName("Plain", ""), "no version");

        var data = TestData.NewWorkDir("installer-lobby");
        var maps = Path.Combine(data, "maps");
        var archive = SyntheticMap.Build(TestData.NewWorkDir("installer-lobby-map"));
        MapInstaller.Install(archive, maps);
        var picture = MapInstaller.InstallLobbyPicture(archive, maps);
        Assert.Equal(Path.Combine(data, "LuaMenu", "Images", "Minimaps", "Synthetic_Test_1.jpg"), picture, "beside the maps folder, under the lobby's name for the map");
        var image = PngReader.Read(picture!).ToRgba();
        Assert.Equal((1024, 512), (image.Width, image.Height), "the map's proportions (256 x 128 squares), the longer side 1024");
        var minimap = Minimap.Decode(SmfFile.Load(ArchiveIO.ReadEntry(archive, SyntheticMap.SmfEntry)!)).Resample(1024, 512);
        Assert.Equal(minimap.GetPixel(300, 200), image.GetPixel(300, 200), "the map's own minimap");
        Assert.True(!Directory.EnumerateFiles(Path.GetDirectoryName(picture)!, "*.partial").Any(), "no partial file left behind");
    }
}
