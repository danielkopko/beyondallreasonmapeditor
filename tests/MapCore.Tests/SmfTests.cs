using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using MapCore.Smf;

namespace MapCore.Tests;

static class SmfTests
{
    static string OriginalPath => TestData.AcidicQuarry.SmfPath;

    [Test]
    public static void LoadsAcidicQuarryValues()
    {
        var smf = SmfFile.Load(OriginalPath);
        Assert.Equal(768, smf.MapX, "map width");
        Assert.Equal(768, smf.MapY, "map height");
        Assert.Equal(220, smf.MapId, "map id");
        Assert.Equal(-50f, smf.MinHeight, "header min height");
        Assert.Equal(100f, smf.MaxHeight, "header max height");
        Assert.Equal(1, smf.TileFiles.Count, "tile file count");
        Assert.Equal(new SmtReference("AcidicQuarry.smt", 36864), smf.TileFiles[0], "tile file");
        Assert.Equal(17, smf.FeatureTypes.Count, "feature types");
        Assert.Equal(0, smf.Features.Count, "placed features");
        Assert.True(smf.GrassMap != null && smf.GrassMap.All(b => b == 0), "empty grass map present");
        Assert.Equal(216, smf.MetalMap.Count(b => b == 246), "metal cells at 246");
        Assert.Empty(smf.Warnings.ToList(), "load warnings");
    }

    [Test]
    public static void UnchangedSaveIsByteIdentical()
    {
        var original = File.ReadAllBytes(OriginalPath);
        var smf = SmfFile.Load(original);
        Assert.True(smf.ToBytes().AsSpan().SequenceEqual(original), "ToBytes() differs from the original file");

        var saved = Path.Combine(TestData.NewWorkDir("smf-save"), "copy.smf");
        smf.Save(saved);
        Assert.True(File.ReadAllBytes(saved).AsSpan().SequenceEqual(original), "Save() differs from the original file");
    }

    [Test]
    public static void MatchesPythonReference()
    {
        var py = PythonReference.DescribeSmf(OriginalPath);
        var smf = SmfFile.Load(OriginalPath);
        var problems = new List<string>();
        void Check<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                problems.Add($"{what}: python {expected}, C# {actual}");
        }

        var h = py.GetProperty("header");
        Check("spring map file", h.GetProperty("magic").GetString(), "magic");
        Check(h.GetProperty("version").GetInt32(), smf.Version, "version");
        Check(h.GetProperty("mapid").GetInt32(), smf.MapId, "mapid");
        Check(h.GetProperty("mapx").GetInt32(), smf.MapX, "mapx");
        Check(h.GetProperty("mapy").GetInt32(), smf.MapY, "mapy");
        Check(h.GetProperty("minHeight").GetDouble(), (double)smf.MinHeight, "minHeight");
        Check(h.GetProperty("maxHeight").GetDouble(), (double)smf.MaxHeight, "maxHeight");
        Check(h.GetProperty("numExtraHeaders").GetInt32(), smf.ExtraHeaders.Count, "numExtraHeaders");

        var layout = smf.SourceLayout.ToDictionary(s => s.Section);
        foreach (var (field, section) in new[]
        {
            ("heightmapPtr", SmfSection.Heightmap), ("typeMapPtr", SmfSection.TypeMap), ("tilesPtr", SmfSection.Tiles),
            ("minimapPtr", SmfSection.Minimap), ("metalmapPtr", SmfSection.MetalMap), ("featurePtr", SmfSection.Features),
        })
            Check(h.GetProperty(field).GetInt32(), layout[section].Offset, field);

        var grassHeader = py.GetProperty("extraHeaders")[0];
        Check(SmfExtraHeader.GrassType, grassHeader.GetProperty("type").GetInt32(), "extra header type");
        Check(grassHeader.GetProperty("grassPtr").GetInt32(), layout[SmfSection.Grass].Offset, "grassPtr");

        // Every section: same position, same length, and the C# serialisation hashes to the original bytes.
        var pySections = py.GetProperty("sections").EnumerateArray().ToList();
        Check(pySections.Count, smf.SourceLayout.Count, "section count");
        foreach (var s in pySections)
        {
            var section = Enum.GetValues<SmfSection>().Single(v => v.ToString().ToLowerInvariant() == s.GetProperty("name").GetString());
            Check(s.GetProperty("offset").GetInt32(), layout[section].Offset, $"{section} offset");
            Check(s.GetProperty("length").GetInt32(), layout[section].Length, $"{section} length");
            var hash = Convert.ToHexStringLower(SHA256.HashData(smf.GetSectionBytes(section)));
            Check(s.GetProperty("sha256").GetString(), hash, $"{section} sha256");
        }
        Check(0, py.GetProperty("gaps").GetArrayLength(), "python gaps");
        Check(0, py.GetProperty("overlaps").GetArrayLength(), "python overlaps");

        var hm = py.GetProperty("heightmap");
        Check(hm.GetProperty("rawMin").GetInt32(), (int)smf.Heightmap.Min(), "heightmap raw min");
        Check(hm.GetProperty("rawMax").GetInt32(), (int)smf.Heightmap.Max(), "heightmap raw max");
        CheckHistogram(py.GetProperty("metalmap").GetProperty("histogram"), smf.MetalMap, "metal", problems);
        CheckHistogram(py.GetProperty("typemap").GetProperty("histogram"), smf.TypeMap, "typemap", problems);
        Check(py.GetProperty("grass").GetProperty("nonzero").GetInt32(), smf.GrassMap!.Count(b => b != 0), "grass non-zero");

        var tiles = py.GetProperty("tiles");
        var files = tiles.GetProperty("files").EnumerateArray()
            .Select(f => new SmtReference(f.GetProperty("name").GetString()!, f.GetProperty("numTiles").GetInt32())).ToList();
        Check(string.Join(";", files), string.Join(";", smf.TileFiles), "tile files");
        Check(tiles.GetProperty("numTilesTotal").GetInt32(), smf.TileCount, "total tiles");
        Check(tiles.GetProperty("indexCount").GetInt32(), smf.TileIndex.Length, "tile index length");
        Check(tiles.GetProperty("uniqueIndices").GetInt32(), smf.TileIndex.Distinct().Count(), "distinct tiles");

        var features = py.GetProperty("features");
        Check(string.Join(",", features.GetProperty("types").EnumerateArray().Select(t => t.GetString())),
              string.Join(",", smf.FeatureTypes), "feature types");
        Check(features.GetProperty("count").GetInt32(), smf.Features.Count, "feature count");

        Assert.Empty(problems, "C# vs python reference");
    }

    [Test]
    public static void EditsSurviveSaveAndReload()
    {
        var original = SmfFile.Load(OriginalPath);
        var smf = SmfFile.Load(OriginalPath);
        smf.Heightmap[12_345] = 999;
        smf.MetalMap[100] = 77;
        smf.TypeMap[200] = 3;
        smf.Minimap[5] ^= 0xFF;
        smf.GrassMap![300] = 128;
        (smf.TileIndex[0], smf.TileIndex[1]) = (smf.TileIndex[1], smf.TileIndex[0]);
        smf.MinHeight = -97;
        smf.MaxHeight = 353;
        smf.Features.Add(new SmfFeature(3, 1000, 50, 2000, 1.5f, 1));

        var reloaded = SmfFile.Load(smf.ToBytes());
        Assert.Equal((ushort)999, reloaded.Heightmap[12_345], "edited height");
        Assert.Equal((byte)77, reloaded.MetalMap[100], "edited metal");
        Assert.Equal((byte)3, reloaded.TypeMap[200], "edited type");
        Assert.Equal((byte)(original.Minimap[5] ^ 0xFF), reloaded.Minimap[5], "edited minimap byte");
        Assert.Equal((byte)128, reloaded.GrassMap![300], "edited grass");
        Assert.Equal(original.TileIndex[1], reloaded.TileIndex[0], "swapped tile index");
        Assert.Equal(-97f, reloaded.MinHeight, "min height");
        Assert.Equal(353f, reloaded.MaxHeight, "max height");
        Assert.Equal(new SmfFeature(3, 1000, 50, 2000, 1.5f, 1), reloaded.Features.Single(), "added feature");
        Assert.Equal("TreeType3", reloaded.FeatureTypes[reloaded.Features[0].TypeIndex], "feature type name");
        Assert.Empty(reloaded.Warnings.ToList(), "reload warnings");

        // Everything not edited is unchanged.
        Assert.True(original.Heightmap.AsSpan(0, 12_345).SequenceEqual(reloaded.Heightmap.AsSpan(0, 12_345)), "heights before the edit");
        Assert.True(original.Heightmap.AsSpan(12_346).SequenceEqual(reloaded.Heightmap.AsSpan(12_346)), "heights after the edit");
        Assert.True(original.TileIndex.AsSpan(2).SequenceEqual(reloaded.TileIndex.AsSpan(2)), "rest of the tile index");
    }

    [Test]
    public static void ResizedSectionsGetNewOffsets()
    {
        var smf = SmfFile.Load(OriginalPath);
        var originalLayout = smf.SourceLayout.ToDictionary(s => s.Section);
        smf.TileFiles[0] = smf.TileFiles[0] with { FileName = "AcidicQuarry_edited_with_a_longer_name.smt" };
        smf.Features.Add(new SmfFeature(0, 1000, 0, 2000, 0, 1));
        smf.Features.Add(new SmfFeature(16, 3000, 0, 4000, 0, 1));

        var path = Path.Combine(TestData.NewWorkDir("smf-resize"), "edited.smf");
        smf.Save(path);

        var reloaded = SmfFile.Load(path);
        var layout = reloaded.SourceLayout.ToDictionary(s => s.Section);
        int growth = "AcidicQuarry_edited_with_a_longer_name.smt".Length - "AcidicQuarry.smt".Length;
        Assert.Empty(reloaded.Warnings.ToList(), "reload warnings");
        Assert.Equal(originalLayout[SmfSection.Heightmap].Offset, layout[SmfSection.Heightmap].Offset, "heightmap offset unchanged");
        Assert.Equal(originalLayout[SmfSection.Tiles].Length + growth, layout[SmfSection.Tiles].Length, "tiles section length");
        Assert.Equal(originalLayout[SmfSection.Features].Offset + growth, layout[SmfSection.Features].Offset, "features section moved");
        Assert.Equal(2, reloaded.Features.Count, "features after reload");

        // The independent parser agrees about the rebuilt file.
        var py = PythonReference.DescribeSmf(path);
        Assert.Equal(0, py.GetProperty("gaps").GetArrayLength(), "python gaps");
        Assert.Equal(0, py.GetProperty("overlaps").GetArrayLength(), "python overlaps");
        Assert.Equal("AcidicQuarry_edited_with_a_longer_name.smt",
            py.GetProperty("tiles").GetProperty("files")[0].GetProperty("name").GetString(), "python tile file name");
        var first = py.GetProperty("features").GetProperty("first");
        Assert.Equal("TreeType0", first[0].GetProperty("type").GetString(), "python feature 0 type");
        Assert.Equal("GeoVent", first[1].GetProperty("type").GetString(), "python feature 1 type");
        Assert.Equal(3000.0, first[1].GetProperty("x").GetDouble(), "python feature 1 x");
    }

    [Test]
    public static void GrassMapCanBeRemovedAndAdded()
    {
        var smf = SmfFile.Load(OriginalPath);
        int originalSize = smf.ToBytes().Length;

        smf.GrassMap = null;
        var withoutGrass = smf.ToBytes();
        var reloaded = SmfFile.Load(withoutGrass);
        Assert.True(reloaded.GrassMap == null, "grass map removed");
        Assert.Equal(0, reloaded.ExtraHeaders.Count, "extra headers without grass");
        Assert.Equal(originalSize - SmfExtraHeader.GrassHeaderSize - 192 * 192, withoutGrass.Length, "file size without grass");

        reloaded.GrassMap = new byte[reloaded.GrassMapWidth * reloaded.GrassMapHeight];
        reloaded.GrassMap[42] = 200;
        var again = SmfFile.Load(reloaded.ToBytes());
        Assert.Equal((byte)200, again.GrassMap![42], "grass value after adding the map back");
        Assert.Equal(1, again.ExtraHeaders.Count, "grass header restored");
    }

    [Test]
    public static void UnknownExtraHeaderIsKeptAsIs()
    {
        var smf = new SmfFile(128, 128);
        smf.TileFiles.Add(new("blank.smt", 1));
        var raw = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(raw, 16);
        BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(4), 7);
        raw[12] = 0xAB;
        smf.ExtraHeaders.Add(new(7, raw));

        var reloaded = SmfFile.Load(smf.ToBytes());
        Assert.True(reloaded.ExtraHeaders.Single().Raw!.AsSpan().SequenceEqual(raw), "unknown header bytes preserved");
        Assert.True(reloaded.Warnings.Any(w => w.Contains("type 7")), "warning about the unknown header");
    }

    [Test]
    public static void RejectsDamagedFiles()
    {
        var smf = new SmfFile(128, 128);
        smf.TileFiles.Add(new("blank.smt", 1));
        var good = smf.ToBytes();
        Assert.True(SmfFile.Load(good).MapX == 128, "the undamaged synthetic map loads");

        var badMagic = (byte[])good.Clone();
        badMagic[0] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => SmfFile.Load(badMagic), "wrong magic");

        Assert.Throws<InvalidDataException>(() => SmfFile.Load(good[..1000]), "truncated file");

        var badPointer = (byte[])good.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(badPointer.AsSpan(52), int.MaxValue - 10); // heightmap pointer
        Assert.Throws<InvalidDataException>(() => SmfFile.Load(badPointer), "heightmap pointer past the end");

        var badTileSize = (byte[])good.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(badTileSize.AsSpan(40), 64);
        Assert.Throws<InvalidDataException>(() => SmfFile.Load(badTileSize), "unsupported tile size");
    }

    [Test]
    public static void RefusesToSaveInvalidContent()
    {
        var smf = new SmfFile(128, 128);
        smf.TileFiles.Add(new("blank.smt", 1));
        smf.Features.Add(new SmfFeature(5, 0, 0, 0, 0, 1)); // no feature types defined
        Assert.Throws<InvalidOperationException>(() => smf.ToBytes(), "feature with an undefined type");
    }

    static void CheckHistogram(JsonElement pyHistogram, byte[] values, string what, List<string> problems)
    {
        var expected = pyHistogram.EnumerateObject().ToDictionary(p => int.Parse(p.Name), p => p.Value.GetInt32());
        var actual = values.GroupBy(v => (int)v).ToDictionary(g => g.Key, g => g.Count());
        if (expected.Count != actual.Count || expected.Any(kv => actual.GetValueOrDefault(kv.Key) != kv.Value))
            problems.Add($"{what} histogram: python {string.Join(",", expected)} vs C# {string.Join(",", actual)}");
    }
}
