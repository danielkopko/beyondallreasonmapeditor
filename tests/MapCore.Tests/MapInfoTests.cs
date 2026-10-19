using System.Text;
using MapCore.Lua;

namespace MapCore.Tests;

static class MapInfoTests
{
    static string MapInfoPath => TestData.AcidicQuarry.MapInfoPath;
    static MapInfo Load() => MapInfo.Load(MapInfoPath);

    [Test]
    public static void ReadsAcidicQuarryValues()
    {
        var info = Load();
        Assert.Equal("mapinfo", info.TableName, "table variable");
        Assert.Equal(("AcidicQuarry", "5.17", "maps/AcidicQuarry.smf"), (info.Name, info.Version, info.MapFile), "name, version, map file");
        Assert.Equal("BasiC, Beherith, IceXuick", info.Author, "author");
        Assert.Equal((-97.0, 353.0), (info.MinHeight, info.MaxHeight), "smf height override");
        Assert.Equal((1.04, 90.0), (info.MaxMetal, info.ExtractorRadius), "metal settings");
        Assert.Equal("maps/AcidicQuarry.smt", string.Join(";", info.SmtFileNames), "tile files (commented-out lines ignored)");
        Assert.Equal(0, info.TextureOverrides.Count, "texture overrides");
        Assert.Equal("normalmap.dds", info.Resource("detailNormalTex"), "resources texture");
        Assert.Equal("0.62,0.7,0.18", string.Join(",", info.GetNumbers("atmosphere.fogColor")!), "fog colour");
        Assert.Equal(200.0, info.GetNumber("water.damage"), "water damage");
        Assert.True(info.Get("atmosphere.skyBox") == null, "commented-out skyBox is not read");
        Assert.Equal("team 0 (1068, 5026); team 1 (5244, 1047); team 2 (5256, 4907); team 3 (1124, 946)",
            string.Join("; ", info.StartPositions.Select(s => $"team {s.Team} ({s.X}, {s.Z})")), "start positions");
    }

    [Test]
    public static void UnchangedSaveIsByteIdentical()
    {
        var original = File.ReadAllBytes(MapInfoPath);
        Assert.True(MapInfo.FromBytes(original).ToBytes().AsSpan().SequenceEqual(original), "mapinfo.lua differs after load/save");
    }

    [Test]
    public static void ChangingOneValueChangesOnlyThatValue()
    {
        var info = Load();
        var original = info.Text;
        var value = info.Get("smf.minheight")!;
        info.SetNumber("smf.minheight", -120.5);
        Assert.Equal(original[..value.Start] + "-120.5" + original[value.End..], info.Text, "only the value text changed");
        Assert.Equal(-120.5, info.MinHeight, "new value reads back");
    }

    [Test]
    public static void ColourEditKeepsLayout()
    {
        var info = Load();
        var original = info.Text;
        info.SetNumbers("atmosphere.fogColor", [0.2, 0.4, 0.9]);
        Assert.True(info.Text.Contains("fogColor = {0.2,0.4,0.9}"), "numbers replaced in place, original spacing kept");
        Assert.Equal(original.Length - "0.62,0.70,0.18".Length + "0.2,0.4,0.9".Length, info.Text.Length, "nothing else changed length");
        Assert.Equal("0.2,0.4,0.9", string.Join(",", info.GetNumbers("atmosphere.fogColor")!), "new colour reads back");
    }

    [Test]
    public static void StringEditKeepsQuotesAndEscapes()
    {
        var info = Load();
        info.SetString("name", "Acidic \"Quarry\" C:\\edit");
        Assert.True(info.Text.Contains("name        = \"Acidic \\\"Quarry\\\" C:\\\\edit\","), "quotes and backslashes escaped in place");
        Assert.Equal("Acidic \"Quarry\" C:\\edit", MapInfo.Parse(info.Text).Name, "value survives a fresh parse");
        info.SetString("version", "5.18");
        Assert.Equal("5.18", info.Version, "version stays a string");
    }

    [Test]
    public static void StartPositionEdit()
    {
        var info = Load();
        info.SetNumber("teams.2.startPos.x", 5000);
        Assert.Equal(5000.0, info.StartPositions.Single(s => s.Team == 2).X, "team 2 x");
        Assert.True(info.Text.Contains("[2] = {startPos = {x = 5000, z = 4907}},"), "edited in place");
    }

    [Test]
    public static void MissingKeysAreInsertedInTheTablesStyle()
    {
        const string source = """
            local mapinfo = {
                name = "Test",
                atmosphere = { fogStart = 0.5 },
                water = {
                    damage = 0
                }
            }

            return mapinfo
            """;
        var info = MapInfo.Parse(source);
        info.SetNumber("smf.minheight", -50);
        info.SetNumber("atmosphere.fogEnd", 0.9);
        info.SetNumber("water.repeatX", 2);
        info.SetNumbers("lighting.sunDir", [0.1, 0.6, -0.3]);

        Assert.Equal(-50.0, info.MinHeight, "new nested table");
        Assert.Equal(0.9, info.GetNumber("atmosphere.fogEnd"), "inline table");
        Assert.Equal(2.0, info.GetNumber("water.repeatX"), "multi-line table without trailing comma");
        Assert.Equal("0.1,0.6,-0.3", string.Join(",", info.GetNumbers("lighting.sunDir")!), "new number list");
        Assert.True(info.Text.Contains("atmosphere = { fogStart = 0.5, fogEnd = 0.9 },"), "inline insertion keeps the line");
        Assert.True(info.Text.Contains("        damage = 0,\n        repeatX = 2,"), "multi-line insertion uses the table's indentation");
        Assert.True(info.Text.Contains("    name = \"Test\",\n"), "untouched lines unchanged");
        Assert.Equal(0.5, MapInfo.Parse(info.Text).GetNumber("atmosphere.fogStart"), "result is still valid Lua for the reader");
    }

    [Test]
    public static void SetFromTextKeepsEachValuesType()
    {
        var info = Load();
        info.SetFromText("version", "6");
        Assert.True(info.Get("version") is LuaString { Value: "6" }, "version stays text even when the input looks numeric");
        info.SetFromText("name", "\"Quoted Name\"");
        Assert.Equal("Quoted Name", info.Name, "surrounding quotes are optional");
        info.SetFromText("maxMetal", "2.5");
        Assert.Equal(2.5, info.MaxMetal, "number field");
        Assert.Throws<InvalidOperationException>(() => info.SetFromText("maxMetal", "lots"), "number field rejects text");
        Assert.Throws<InvalidOperationException>(() => info.SetFromText("maxMetal", "1,2"), "number field rejects a list");
        info.SetFromText("atmosphere.fogColor", "0.2, 0.4, 0.9");
        Assert.Equal("0.2,0.4,0.9", string.Join(",", info.GetNumbers("atmosphere.fogColor")!), "colour list");

        info.SetFromText("custom.amount", "3");
        info.SetFromText("custom.colour", "1,0.5,0");
        info.SetFromText("custom.label", "hello");
        info.SetFromText("custom.code", "'42'");
        Assert.True(info.Get("custom.amount") is LuaNumber { Value: 3 }, "missing key, numeric input -> number");
        Assert.Equal("1,0.5,0", string.Join(",", info.GetNumbers("custom.colour")!), "missing key, list input -> list");
        Assert.Equal("hello", info.GetString("custom.label"), "missing key, text input -> text");
        Assert.Equal("42", info.GetString("custom.code"), "missing key, quoted number -> text");

        var expression = MapInfo.Parse("local m = { scale = 1 / 3 }\nreturn m");
        Assert.Throws<InvalidOperationException>(() => expression.SetFromText("scale", "0.5"), "expressions are not overwritten");
    }

    [Test]
    public static void SettingInsideANonTableFails()
    {
        var info = Load();
        Assert.Throws<InvalidOperationException>(() => info.SetNumber("name.x", 1), "name is a string, not a table");
    }

    [Test]
    public static void FindsOverridesInMapconfig()
    {
        var info = Load();
        var overrides = info.FindOverrides(TestData.AcidicQuarry.Root);
        var paths = overrides.Select(o => o.Path.ToLowerInvariant()).ToHashSet();
        foreach (var expected in new[] { "water.damage", "water.absorb", "water.basecolor", "water.mincolor", "water.surfacecolor", "smf.minheight", "smf.maxheight" })
            Assert.True(paths.Contains(expected), $"override of {expected} found (found: {string.Join(", ", paths)})");
        Assert.True(overrides.All(o => o.File == "mapconfig/mapinfo/0_apply_options.lua"), "all from 0_apply_options.lua");
        Assert.True(MapInfo.Affecting(overrides, "water.damage").Any(), "water.damage affected");
        Assert.True(MapInfo.Affecting(overrides, "terrainTypes.0.moveSpeeds.tank").Any(), "computed keys ([terrainIndex]) match any key");
        Assert.True(!MapInfo.Affecting(overrides, "maxMetal").Any(), "maxMetal not affected");
    }

    [Test]
    public static void KeepsEncodingAndByteOrderMark()
    {
        var utf8WithBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("local m = { author = \"Zoë\" }\nreturn m\n")).ToArray();
        var info = MapInfo.FromBytes(utf8WithBom);
        Assert.Equal("Zoë", info.Author, "UTF-8 text");
        Assert.True(info.ToBytes().AsSpan().SequenceEqual(utf8WithBom), "UTF-8 with BOM round-trips");

        var latin1 = Encoding.Latin1.GetBytes("return { author = \"Zoë\" }\n"); // not valid UTF-8
        var legacy = MapInfo.FromBytes(latin1);
        Assert.Equal("Zoë", legacy.Author, "Latin-1 text");
        Assert.True(legacy.ToBytes().AsSpan().SequenceEqual(latin1), "Latin-1 round-trips");
    }

    [Test]
    public static void RejectsFilesWithoutAReturnedTable()
    {
        Assert.Throws<InvalidDataException>(() => MapInfo.Parse("local x = 1"), "no return");
        Assert.Throws<InvalidDataException>(() => MapInfo.Parse("return mapinfo"), "returned name never assigned");
        Assert.Throws<LuaSyntaxException>(() => MapInfo.Parse("local m = { a = \"broken }\nreturn m"), "syntax error");
    }
}
