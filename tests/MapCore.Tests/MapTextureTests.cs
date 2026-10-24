using System.Diagnostics;
using MapCore.Imaging;
using MapCore.Smf;
using MapCore.Textures;

namespace MapCore.Tests;

static class MapTextureTests
{
    static MapTexture LoadTexture(out SmfFile smf)
    {
        smf = TestData.AcidicQuarry.LoadSmf();
        return TestData.AcidicQuarry.LoadTexture(smf);
    }

    /// <summary>
    /// A texture update stores its pixels opaque by default (an edit never punches holes), but keeps alpha below 128
    /// as DXT1's transparent texels when asked (a voidGround map's holes), at every mip level; opaque pixels stay opaque.
    /// </summary>
    [Test]
    public static void UpdateKeepsTransparencyOnlyWhenAsked()
    {
        foreach (bool keep in new[] { false, true })
        {
            var texture = LoadTexture(out _);
            var image = texture.Decode();
            var hole = new PixelRect(64, 64, 64, 64);
            for (int y = hole.Y; y < hole.Bottom; y++)
                for (int x = hole.X; x < hole.Right; x++)
                {
                    var p = image.GetPixel(x, y);
                    image.SetPixel(x, y, new Rgba(p.R, p.G, p.B, 0));
                }
            texture.Update(image, new PixelRect(32, 32, 128, 128), keepAlpha: keep);
            var tile = texture.DecodeTile(3, 3);               // texels 96-127: inside the hole
            var edge = texture.DecodeTile(1, 1);               // texels 32-63: outside it
            var small = texture.DecodeTile(3, 3, 3);            // its 4x4 mip
            bool holeTransparent = Enumerable.Range(0, 32 * 32).All(i => tile.GetPixel(i % 32, i / 32).A == 0);
            bool holeOpaque = Enumerable.Range(0, 32 * 32).All(i => tile.GetPixel(i % 32, i / 32).A == 255);
            bool edgeOpaque = Enumerable.Range(0, 32 * 32).All(i => edge.GetPixel(i % 32, i / 32).A == 255);
            bool mipTransparent = Enumerable.Range(0, 16).All(i => small.GetPixel(i % 4, i / 4).A == 0);
            Assert.True(keep ? holeTransparent && mipTransparent : holeOpaque, $"keepAlpha {keep}: the hole is {(keep ? "transparent at mip 0 and 3" : "opaque")}");
            Assert.True(edgeOpaque, $"keepAlpha {keep}: opaque pixels stay opaque");
        }
    }

    [Test]
    public static void SmtUnchangedSaveIsByteIdentical()
    {
        var path = Path.Combine(TestData.AcidicQuarry.Root, "maps", "AcidicQuarry.smt");
        var original = File.ReadAllBytes(path);
        var smt = SmtFile.Load(original);
        Assert.Equal(36864, smt.TileCount, "tile count");
        Assert.Empty(smt.Warnings.ToList(), "warnings");
        Assert.True(smt.ToBytes().AsSpan().SequenceEqual(original), "SMT differs after load/save");
    }

    [Test]
    public static void FindsTileFileAndNoTextureOverrides()
    {
        var smf = TestData.AcidicQuarry.LoadSmf();
        Assert.Equal("maps/AcidicQuarry.smt", string.Join(";", TestData.AcidicQuarry.ResolveSmtEntries(smf)), "resolved tile files");
        Assert.Equal(0, TestData.AcidicQuarry.TextureOverrides().Count, "mapinfo texture overrides");
    }

    [Test]
    public static void TileFileLookupFollowsEngineRules()
    {
        // mapinfo names win when there is one per tile file; they are looked up next to the SMF first.
        Assert.Equal("custom/tiles.smt", ResolveIn("""smtFileName0 = "custom/tiles.smt", """, "maps/test.smt", "custom/tiles.smt"), "mapinfo override");
        Assert.Equal("maps/tiles.smt", ResolveIn("""smtFileName0 = "tiles.smt", """, "tiles.smt", "maps/tiles.smt"), "SMF folder first");
        // A count mismatch makes the engine ignore mapinfo's names.
        Assert.Equal("maps/test.smt", ResolveIn("""
            smtFileName0 = "a.smt",
            smtFileName1 = "b.smt",
            """, "maps/test.smt", "a.smt"), "mapinfo ignored on count mismatch");
        Assert.Equal("maps/test.smt", ResolveIn("""--smtFileName0 = "custom/tiles.smt", """, "maps/test.smt", "custom/tiles.smt"), "commented-out line ignored");
        Assert.Throws<InvalidDataException>(() => ResolveIn("", "maps/elsewhere.smt"), "missing tile file");
    }

    [Test]
    public static void DecodedTilesMatchPythonReference()
    {
        var texture = LoadTexture(out _);
        foreach (var (column, row) in new[] { (0, 0), (191, 191), (57, 113), (100, 3) })
        {
            var expected = PythonReference.DecodeTile(TestData.AcidicQuarry.SmfPath, column, row);
            Assert.True(texture.DecodeTile(column, row).Pixels.AsSpan().SequenceEqual(expected), $"tile ({column},{row}) differs from python");
        }
        var full = texture.Decode();
        Assert.True(full.Crop(new PixelRect(57 * 32, 113 * 32, 32, 32)).Pixels.AsSpan().SequenceEqual(texture.DecodeTile(57, 113).Pixels),
            "full decode agrees with single-tile decode");
    }

    [Test]
    public static void EditingOneTileChangesOnlyThatTile()
    {
        var texture = LoadTexture(out var smf);
        var smtBefore = texture.TileFiles[0].ToBytes();
        var smfBefore = smf.ToBytes();
        var neighbourBefore = texture.DecodeTile(58, 113);

        var image = texture.Decode();
        var region = new PixelRect(57 * 32 + 5, 113 * 32 + 9, 12, 7);
        image.Fill(region, new Rgba(255, 0, 255, 255));
        Assert.Equal(new TextureUpdate(1, 0), texture.Update(image, region), "tiles rewritten / added");

        int tile = smf.TileIndex[113 * smf.TileColumns + 57];
        int start = SmtFile.HeaderSize + tile * SmtFile.TileBytes, end = start + SmtFile.TileBytes;
        var smtAfter = texture.TileFiles[0].ToBytes();
        var changed = Enumerable.Range(0, smtAfter.Length).Where(i => smtAfter[i] != smtBefore[i]).ToList();
        Assert.True(changed.Count > 0, "the edited tile changed");
        int firstOutside = changed.FirstOrDefault(i => i < start || i >= end, -1);
        Assert.Equal(-1, firstOutside, $"first changed byte outside tile {tile}");
        Assert.True(smf.ToBytes().AsSpan().SequenceEqual(smfBefore), "SMF (tile index) unchanged for an unshared tile");

        var painted = texture.DecodeTile(57, 113).GetPixel(5 + 6, 9 + 3);
        Assert.True(painted.R > 240 && painted.G < 15 && painted.B > 240 && painted.A == 255, $"painted pixel decodes as {painted}");
        Assert.True(texture.DecodeTile(58, 113).Pixels.AsSpan().SequenceEqual(neighbourBefore.Pixels), "neighbouring tile unchanged");
    }

    [Test]
    public static void EditedTextureSurvivesSavingToDisk()
    {
        // A private copy of the map: the shared extraction is read-only.
        var package = MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir("texture-save"));
        var smf = package.LoadSmf();
        var texture = package.LoadTexture(smf);
        var image = texture.Decode();
        var region = new PixelRect(20 * 32, 30 * 32, 64, 40); // spans two tiles across
        var cyan = new Rgba(0, 255, 255, 255);
        image.Fill(region, cyan);
        texture.Update(image, region);
        Minimap.Rebuild(smf, image, region);
        package.SaveTexture(texture);

        var reopened = MapPackage.OpenFolder(package.Root);
        var reloaded = reopened.LoadTexture(reopened.LoadSmf());
        foreach (var column in new[] { 20, 21 })
        {
            var tile = reloaded.DecodeTile(column, 30);
            var pixel = tile.GetPixel(10, 10);
            Assert.True(pixel.R < 15 && pixel.G > 240 && pixel.B > 240, $"tile ({column},30) after reload decodes as {pixel}");
            Assert.True(PythonReference.DecodeTile(reopened.SmfPath, column, 30).AsSpan().SequenceEqual(tile.Pixels),
                $"python decodes the saved tile ({column},30) the same way");
        }
        var minimapPixel = Minimap.Decode(reopened.LoadSmf()).GetPixel((20 * 32 + 32) * 1024 / 6144, (30 * 32 + 20) * 1024 / 6144);
        Assert.True(minimapPixel.R < 40 && minimapPixel.G > 200 && minimapPixel.B > 200, $"saved minimap shows the patch: {minimapPixel}");
    }

    [Test]
    public static void SharedTileIsCopiedBeforeEditing()
    {
        // Synthetic map where all 32x32 positions share tile 0.
        var smf = new SmfFile(128, 128);
        smf.TileFiles.Add(new("shared.smt", 1));
        var smt = new SmtFile();
        var seed = new RgbaImage(32, 32);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
                seed.SetPixel(x, y, new Rgba((byte)(x * 8), (byte)(y * 8), 90, 255));
        var tileBytes = new byte[SmtFile.TileBytes];
        BlockCompression.Encode(BlockFormat.Dxt1, seed, tileBytes.AsMemory(0, 512));
        smt.AddTile(tileBytes);
        var texture = new MapTexture(smf, [smt]);
        var originalTile0 = smt.Tile(0).ToArray();

        var image = texture.Decode();
        var region = new PixelRect(3 * 32 + 4, 4 * 32 + 4, 8, 8);
        image.Fill(region, new Rgba(0, 0, 0, 255));
        Assert.Equal(new TextureUpdate(1, 1), texture.Update(image, region), "one tile rewritten, as a new copy");

        int position = 4 * smf.TileColumns + 3;
        Assert.Equal(1, smf.TileIndex[position], "edited position points at the new tile");
        Assert.Equal(smf.TileIndex.Length - 1, smf.TileIndex.Count(t => t == 0), "every other position still uses tile 0");
        Assert.Equal((2, 2), (smt.TileCount, smf.TileFiles[0].TileCount), "tile counts in SMT and SMF");
        Assert.True(smt.Tile(0).Span.SequenceEqual(originalTile0), "shared tile 0 untouched");

        // Untouched blocks of the copy keep the shared tile's bytes; only the painted 8x8 area (four blocks) differs at mip 0.
        var copy = smt.Tile(1).ToArray();
        int differingBlocks = Enumerable.Range(0, 64).Count(b => !copy.AsSpan(b * 8, 8).SequenceEqual(originalTile0.AsSpan(b * 8, 8)));
        Assert.Equal(4, differingBlocks, "mip-0 blocks rewritten in the copied tile");

        // Both files survive saving and reloading together.
        var reloaded = new MapTexture(SmfFile.Load(smf.ToBytes()), [SmtFile.Load(smt.ToBytes())]);
        Assert.Equal(new Rgba(0, 0, 0, 255), reloaded.DecodeTile(3, 4).GetPixel(8, 8), "painted pixel after reload");
        Assert.True(reloaded.DecodeTile(0, 0).Pixels.AsSpan().SequenceEqual(texture.DecodeTile(0, 0).Pixels), "other position after reload");
    }

    [Test]
    public static void FullReencodeKeepsQualityAndNoTransparency()
    {
        var texture = LoadTexture(out _);
        var decoded = texture.Decode();
        int transparent = 0;
        for (int i = 3; i < decoded.Pixels.Length; i += 4)
            if (decoded.Pixels[i] < 128)
                transparent++;

        var sw = Stopwatch.StartNew();
        var result = texture.Update(decoded, texture.Bounds);
        long ms = sw.ElapsedMilliseconds;
        double psnr = ImageMetrics.Psnr(decoded, texture.Decode());
        Report.Info($"6144x6144 texture: {result.TilesRewritten} tiles re-encoded (all mips) in {ms} ms, PSNR {psnr:F2} dB, {transparent} transparent pixels in the original tiles");
        Assert.Equal(0, transparent, "transparent pixels in the original tiles");
        Assert.Equal(new TextureUpdate(36864, 0), result, "tiles rewritten / added");
        Assert.True(psnr >= 40, $"re-encode PSNR {psnr:F2} dB");
    }

    [Test]
    public static void PaintedPixelsAreStoredOpaque()
    {
        var texture = LoadTexture(out _);
        var image = texture.Decode();
        var region = new PixelRect(0, 0, 16, 16);
        image.Fill(region, new Rgba(200, 50, 50, 0));
        texture.Update(image, region);
        Assert.Equal((byte)255, texture.DecodeTile(0, 0).GetPixel(5, 5).A, "alpha of a pixel painted with alpha 0");
    }

    [Test]
    public static void MinimapRegionRebuildOnlyTouchesMatchingBlocks()
    {
        var texture = LoadTexture(out var smf);
        var before = (byte[])smf.Minimap.Clone();
        var image = texture.Decode();
        var region = new PixelRect(600, 900, 300, 200);
        image.Fill(region, new Rgba(255, 255, 0, 255));
        Minimap.Rebuild(smf, image, region);

        var dirty = Minimap.ToMinimapRegion(region, image.Width, image.Height);
        int offset = 0;
        for (int m = 0; m < Minimap.LevelCount; m++)
        {
            int size = Minimap.Size >> m;
            if (m > 0)
                dirty = dirty.ToNextMip(size, size);
            var blocks = dirty.ToBlockBounds(size, size);
            int levelBytes = BlockCompression.LevelBytes(BlockFormat.Dxt1, size, size), across = size / 4;
            int outside = 0, inside = 0;
            for (int b = 0; b < levelBytes / 8; b++)
            {
                if (before.AsSpan(offset + b * 8, 8).SequenceEqual(smf.Minimap.AsSpan(offset + b * 8, 8)))
                    continue;
                int px = b % across * 4, py = b / across * 4;
                if (px >= blocks.X && px < blocks.Right && py >= blocks.Y && py < blocks.Bottom)
                    inside++;
                else
                    outside++;
            }
            Assert.Equal(0, outside, $"minimap mip {m}: blocks changed outside the edited area");
            if (m == 0)
                Assert.True(inside > 0, "minimap mip 0: the edited area changed");
            offset += levelBytes;
        }

        var painted = Minimap.Decode(smf).GetPixel((600 + 150) * 1024 / 6144, (900 + 100) * 1024 / 6144);
        Assert.True(painted.R > 240 && painted.G > 240 && painted.B < 15, $"painted minimap pixel decodes as {painted}");
    }

    [Test]
    public static void FullMinimapRebuildResemblesOriginal()
    {
        var texture = LoadTexture(out var smf);
        var original = Minimap.Decode(smf);
        Minimap.Rebuild(smf, texture.Decode());
        double psnr = ImageMetrics.Psnr(original, Minimap.Decode(smf));
        Report.Info($"minimap rebuilt from the texture vs the map's own minimap: PSNR {psnr:F2} dB");
        Assert.True(psnr > 20, "a rebuilt minimap should broadly match the original (same orientation and colours)");
    }

    /// <summary>
    /// Builds a throwaway package with maps/test.smf naming "test.smt", the given mapinfo lines inside
    /// its smf table, and empty-but-valid tile files at <paramref name="tileFiles"/>; returns the resolved entry.
    /// </summary>
    static string ResolveIn(string smfTableLines, params string[] tileFiles)
    {
        var root = TestData.NewWorkDir("resolve");
        Directory.CreateDirectory(Path.Combine(root, "maps"));
        File.WriteAllText(Path.Combine(root, "mapinfo.lua"), $$"""
            local mapinfo = {
                mapfile = "maps/test.smf",
                smf = {
                    {{smfTableLines}}
                },
            }
            return mapinfo
            """);
        var smf = new SmfFile(128, 128);
        smf.TileFiles.Add(new("test.smt", 1));
        smf.Save(Path.Combine(root, "maps", "test.smf"));
        var smt = new SmtFile();
        smt.AddTile(new byte[SmtFile.TileBytes]);
        foreach (var file in tileFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, file))!);
            smt.Save(Path.Combine(root, file));
        }
        var package = MapPackage.OpenFolder(root);
        return string.Join(";", package.ResolveSmtEntries(package.LoadSmf()));
    }
}
