using System.Text.RegularExpressions;
using MapCore.Items;
using MapCore.Smf;

namespace MapCore.Tests;

static class ItemTests
{
    static readonly Regex RockLine = new(@"^\{ name = ""[^""]+"", x = -?\d+, z = -?\d+, rot = -?\d+ \},$", RegexOptions.Multiline);

    static FeaturePlacerConfig LoadPlacer() => FeaturePlacerConfig.Load(TestData.AcidicQuarry.Root)!;

    [Test]
    public static void ReadsAcidicQuarryRocks()
    {
        var placer = LoadPlacer();
        Assert.Equal("mapconfig/featureplacer/set.lua", placer.Entry, "config.lua's VFS.Include is followed");
        var items = placer.Items;
        Assert.Equal(56, items.Count, "rocks");
        Assert.True(items.All(i => i.Kind == PlacedItemKind.Feature), "all in objectlist");
        Assert.Equal(new PlacedItem(PlacedItemKind.Feature, 1, "agorm_rock5", 5809, 3562, 28119), items[0], "first rock");
        Assert.Equal(new PlacedItem(PlacedItemKind.Feature, 56, "agorm_rock1", 4848, 1089, -19204), items[^1], "last rock");
    }

    [Test]
    public static void UnchangedSaveIsByteIdentical()
    {
        var path = Path.Combine(TestData.AcidicQuarry.Root, "mapconfig", "featureplacer", "set.lua");
        Assert.True(LoadPlacer().Document.ToBytes().AsSpan().SequenceEqual(File.ReadAllBytes(path)), "set.lua differs after load/save");
    }

    [Test]
    public static void AddedRockIsWrittenInTheFilesOwnStyle()
    {
        var placer = LoadPlacer();
        var original = placer.Document.Text;
        var added = placer.Add(PlacedItemKind.Feature, "agorm_rock2", 1000, 2000, 16384);
        var text = placer.Document.Text;

        Assert.Equal(57, added.Index, "position of the new rock");
        const string newLine = "{ name = \"agorm_rock2\", x = 1000, z = 2000, rot = 16384 },";
        string lastOriginal = "{ name = \"agorm_rock1\", x = 4848, z = 1089, rot = -19204 },";
        string newlineChars = original.Contains("\r\n") ? "\r\n" : "\n";
        Assert.True(text.Contains(lastOriginal + newlineChars + newLine + newlineChars), "new line follows the last rock, same layout");
        Assert.Equal(57, RockLine.Matches(text.Replace("\r", "")).Count, "every rock line has the same format");
        Assert.Equal(original.Length + newLine.Length + newlineChars.Length, text.Length, "nothing else changed");

        var reread = FeaturePlacerConfig.Load(TestData.AcidicQuarry.Root)!; // proves nothing was written to disk
        Assert.Equal(56, reread.Items.Count, "file on disk untouched (edits are in memory until saved)");
        Assert.Equal(added, placer.Items[^1], "new rock reads back");
    }

    [Test]
    public static void MoveAndRotateChangeOnlyThoseNumbers()
    {
        var placer = LoadPlacer();
        var item = placer.Items[2];
        placer.Move(item, 100, 200);
        placer.SetRotation(item, -5);
        var text = placer.Document.Text;
        Assert.True(text.Contains("{ name = \"agorm_rock5\", x = 100, z = 200, rot = -5 },"), "edited line");
        Assert.Equal(56, RockLine.Matches(text.Replace("\r", "")).Count, "all lines keep the format");
        Assert.Equal(item with { X = 100, Z = 200, Rotation = -5 }, placer.Items[2], "reads back");
    }

    [Test]
    public static void RemoveDeletesTheWholeLine()
    {
        var placer = LoadPlacer();
        var original = placer.Document.Text;
        var second = placer.Items[1];
        var third = placer.Items[2];
        placer.Remove(second);
        Assert.Equal(55, placer.Items.Count, "one fewer rock");
        Assert.Equal(third with { Index = 2 }, placer.Items[1], "later rocks move up");
        Assert.True(!placer.Document.Text.Contains("x = 5080, z = 4454"), "removed rock's line is gone");
        Assert.Equal(55, RockLine.Matches(placer.Document.Text.Replace("\r", "")).Count, "remaining lines intact");
        string newlineChars = original.Contains("\r\n") ? "\r\n" : "\n";
        Assert.Equal(original.Length - "{ name = \"agorm_rock2\", x = 5080, z = 4454, rot = 17158 },".Length - newlineChars.Length,
            placer.Document.Text.Length, "only that line removed");
    }

    [Test]
    public static void ConfigCanHoldTheListsDirectly()
    {
        var root = TestData.NewWorkDir("placer");
        var folder = Directory.CreateDirectory(Path.Combine(root, "mapconfig", "featureplacer")).FullName;
        File.WriteAllText(Path.Combine(folder, "config.lua"), """
            local cfg = {
                unitlist = {},
                objectlist = {
                },
            }
            return cfg
            """);
        var placer = FeaturePlacerConfig.Load(root)!;
        Assert.Equal(FeaturePlacerConfig.ConfigEntry, placer.Entry, "lists read from config.lua itself");
        placer.Add(PlacedItemKind.Feature, "rock", 1, 2, 3);
        placer.Add(PlacedItemKind.Unit, "critter", 4, 5, 6);
        placer.Add(PlacedItemKind.Building, "tower", 7, 8, 1);
        placer.Save(root);

        var reloaded = FeaturePlacerConfig.Load(root)!;
        Assert.Equal("Feature rock 1,2,3; Unit critter 4,5,6; Building tower 7,8,1",
            string.Join("; ", reloaded.Items.Select(i => $"{i.Kind} {i.Name} {i.X},{i.Z},{i.Rotation}")), "all three lists, incl. a new buildinglist");
        Assert.True(FeaturePlacerConfig.Load(TestData.NewWorkDir("no-placer")) == null, "no config -> null");
    }

    [Test]
    public static void SmfFeaturesThroughMapItems()
    {
        var smf = TestData.AcidicQuarry.LoadSmf();
        var items = new MapItems(smf, LoadPlacer());
        Assert.Equal(56, items.All.Count, "SMF has no features; 56 placer rocks");

        var tree = items.Add(ItemSource.Smf, PlacedItemKind.Feature, "TreeType3", 1000, 2000, 8192);
        var rock = items.Add(ItemSource.Smf, PlacedItemKind.Feature, "agorm_rock1", 3000, 4000, 0);
        Assert.Equal(18, smf.FeatureTypes.Count, "TreeType3 reused, agorm_rock1 added to the 17 types");
        Assert.Equal(3, smf.Features[0].TypeIndex, "TreeType3 index");
        items.Move(rock, 3100, 4100);
        items.SetRotation(tree, -16384);
        Assert.Throws<InvalidOperationException>(() => items.Add(ItemSource.Smf, PlacedItemKind.Unit, "x", 0, 0, 0), "SMF holds features only");

        var reloaded = SmfFile.Load(smf.ToBytes());
        var after = new MapItems(reloaded, null).All;
        Assert.Equal("TreeType3 1000,2000 -16384; agorm_rock1 3100,4100 0", string.Join("; ", after.Select(i => $"{i.Name} {i.X},{i.Z} {i.Rotation}")), "SMF features after save/reload");

        var withoutTree = new MapItems(reloaded, null);
        withoutTree.Remove(withoutTree.All[0]);
        Assert.Equal("agorm_rock1", withoutTree.All.Single().Name, "removed");
        Assert.Throws<InvalidOperationException>(() => withoutTree.Add(ItemSource.FeaturePlacer, PlacedItemKind.Feature, "x", 0, 0, 0), "no placer config");
    }

    [Test]
    public static void RotationsAreKeptInRange()
    {
        Assert.Equal(-25536.0, Rotation.Normalize(PlacedItemKind.Feature, 40000), "40000 wraps to -25536");
        Assert.Equal(32767.0, Rotation.Normalize(PlacedItemKind.Unit, -32769), "-32769 wraps to 32767");
        Assert.Equal(-32768.0, Rotation.Normalize(PlacedItemKind.Feature, 32768), "32768 wraps to -32768");
        Assert.Equal(100.0, Rotation.Normalize(PlacedItemKind.Feature, 100 + 3 * 65536), "whole turns removed");
        Assert.Equal(1.0, Rotation.Normalize(PlacedItemKind.Building, 5), "facing 5 wraps to 1");
        Assert.Equal(3.0, Rotation.Normalize(PlacedItemKind.Building, -1), "facing -1 wraps to 3");

        var smf = TestData.AcidicQuarry.LoadSmf();
        var items = new MapItems(smf, LoadPlacer());
        items.Add(ItemSource.Smf, PlacedItemKind.Feature, "agorm_rock1", 10, 20, 70000);
        Assert.Equal(4464f, smf.Features[0].Rotation, "SMF feature heading wrapped on add");
        items.SetRotation(items.All.First(i => i.Source == ItemSource.FeaturePlacer), -40000);
        Assert.Equal(25536.0, items.Placer!.Items[0].Rotation, "featureplacer heading wrapped on set");
    }

    [Test]
    public static void CatalogListsMapFeaturesAndEngineDefaults()
    {
        var (definitions, warnings) = FeatureCatalog.Load(TestData.AcidicQuarry.Root, TestData.AcidicQuarry.LoadSmf());
        for (int n = 1; n <= 5; n++)
        {
            var rock = definitions.Single(d => d.Name == $"agorm_rock{n}");
            Assert.Equal($"features/agorm_rock{n}.lua", rock.Source, $"rock {n} source");
            Assert.True(rock.ModelFound == true, $"rock {n} model {rock.Model} found under objects3d/");
        }
        Assert.Equal(16, definitions.Count(d => d.Source == "engine default tree"), "TreeType0-15");
        Assert.True(definitions.Any(d => d.Name == "GeoVent" && d.Source == "engine default geothermal vent"), "GeoVent");
        Assert.True(warnings.Any(w => w.StartsWith("features/agorm_rocks.lua")), "the stray HTML file is reported, not read");
    }

    [Test]
    public static void GrassPainting()
    {
        var smf = TestData.AcidicQuarry.LoadSmf();
        Assert.Equal(0, GrassLayer.CellsWithGrass(smf), "no grass to begin with");
        int changed = GrassLayer.Paint(smf, 1000, 1000, 64, 128);
        Assert.True(changed >= 12 && changed <= 13, $"a 64-unit circle covers about 12 cells of 32x32 (changed {changed})");
        Assert.Equal((byte)128, GrassLayer.ValueAt(smf, 1000, 1000), "painted value");
        GrassLayer.Paint(smf, 1000, 1000, 20, 255); // the cell's centre (1008, 1008) is 11.3 units away
        Assert.Equal(GrassLayer.MaxValue, GrassLayer.ValueAt(smf, 1000, 1000), "255 clamps to 254");
        GrassLayer.Paint(smf, 1000, 1000, 64, 0);
        Assert.Equal(0, GrassLayer.CellsWithGrass(smf), "painting 0 removes grass");

        smf.GrassMap = null;
        Assert.Equal(0, GrassLayer.Paint(smf, 500, 500, 100, 0), "erasing without a grass map does nothing");
        Assert.True(GrassLayer.Paint(smf, 500, 500, 100, 1) > 0 && smf.GrassMap != null, "painting creates the grass map");
        Assert.Equal(GrassLayer.CellsWithGrass(smf), SmfFile.Load(smf.ToBytes()).GrassMap!.Count(v => v != 0), "grass survives save/reload");
    }

    [Test]
    public static void StartPositionsCanBeMovedAddedAndRemoved()
    {
        var info = MapInfo.Load(TestData.AcidicQuarry.MapInfoPath);
        var original = info.Text;
        info.SetStartPosition(2, 5000, 4000);
        Assert.True(info.Text.Contains("[2] = {startPos = {x = 5000, z = 4000}},"), "moved in place");

        info.SetStartPosition(4, 3072, 3072);
        Assert.Equal(5, info.StartPositions.Count, "team 4 added");
        Assert.True(info.Text.Contains("[4] = { startPos = { x = 3072, z = 3072 } },"), $"new team written in Lua table syntax");
        info.RemoveStartPosition(4);
        info.SetStartPosition(2, 5256, 4907);
        Assert.Equal(original, info.Text, "adding and removing a team, and moving one back, restores the file exactly");
    }
}
