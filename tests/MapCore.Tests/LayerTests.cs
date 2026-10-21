using MapCore.Editing;
using MapCore.Heights;
using MapCore.Imaging;
using MapCore.Smf;

namespace MapCore.Tests;

static class LayerTests
{
    static readonly string[] Layers = ["heights", "texture", "metal", "grass", "types", "resource:splatDistrTex", "resource:specularTex"];

    static MapEditSession FreshSession(string name, out MapPackage package)
    {
        package = MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir(name));
        return MapEditSession.Open(package);
    }

    [Test]
    public static void ExportThenImportChangesNothing()
    {
        var session = FreshSession("layers-rt", out var package);
        var dir = TestData.NewWorkDir("layers-png");
        foreach (var layer in Layers)
            session.ExportLayer(layer, Path.Combine(dir, layer.Replace(':', '_') + ".png"));
        var originals = Directory.EnumerateFiles(package.Root, "*", FileSystemUtil.AllEntries)
            .ToDictionary(f => f, File.ReadAllBytes);

        var results = Layers.Select(l => session.ImportLayer(l, Path.Combine(dir, l.Replace(':', '_') + ".png"))).ToList();
        Report.Info(string.Join(" | ", results));
        Assert.True(!session.Heights.HasChanges, "heights unchanged");
        session.Save();

        var changed = originals.Where(kv => !File.ReadAllBytes(kv.Key).AsSpan().SequenceEqual(kv.Value)).Select(kv => FileSystemUtil.EntryPath(package.Root, kv.Key)).ToList();
        Assert.Empty(changed, "files changed by re-importing unedited exports");
    }

    /// <summary>The terrain type map: exported as 8-bit grey (each value a type number), and an edited image imported as one undo step.</summary>
    [Test]
    public static void TerrainTypesExportAndImport()
    {
        var session = FreshSession("layers-types", out _);
        var png = Path.Combine(TestData.NewWorkDir("layers-types-png"), "types.png");
        Report.Info(session.ExportLayer("types", png));
        var image = PngReader.Read(png);
        Assert.True(image.Width == session.Smf.MetalMapWidth && image.Height == session.Smf.MetalMapHeight, "one pixel a 16-unit square");
        var before = (byte[])session.Smf.TypeMap.Clone();
        var values = image.ToGray8();
        values[10 * image.Width + 20] = (byte)(values[10 * image.Width + 20] == 3 ? 4 : 3);
        PngWriter.WriteGray8(png, image.Width, image.Height, values);
        Assert.True(session.ImportLayer("types", png).StartsWith("terrain types: 1 squares changed", StringComparison.Ordinal), "the one square edited changes");
        Assert.True(session.Smf.TypeMap[10 * image.Width + 20] == values[10 * image.Width + 20], "to the value in the image");
        session.Undo();
        Assert.True(session.Smf.TypeMap.AsSpan().SequenceEqual(before), "undo puts it back");
    }

    [Test]
    public static void EditedImportsChangeOnlyWhatWasEdited()
    {
        var session = FreshSession("layers-edit", out var package);
        var dir = TestData.NewWorkDir("layers-edit-png");

        // Heights: raise a 10x10 block of vertices by 1000 raw steps.
        var heightsPng = Path.Combine(dir, "heights.png");
        session.ExportLayer("heights", heightsPng);
        var png = PngReader.Read(heightsPng);
        var raw = png.ToGray16();
        for (int z = 380; z < 390; z++)
            for (int x = 380; x < 390; x++)
                raw[z * png.Width + x] += 1000;
        PngWriter.WriteGray16(heightsPng, png.Width, png.Height, raw, png.Text);
        Assert.True(session.ImportLayer("heights", heightsPng).StartsWith("heights: 100 vertices changed"), "100 vertices");
        Assert.Equal(new PixelRect(380, 380, 10, 10), session.Heights.ChangedRegion, "changed region");

        // Texture: paint a 20x20 square magenta in the exported PNG.
        var texturePng = Path.Combine(dir, "texture.png");
        session.ExportLayer("texture", texturePng);
        var texture = PngReader.Read(texturePng).ToRgba();
        texture.Fill(new PixelRect(4000, 2000, 20, 20), new Rgba(255, 0, 255, 255));
        PngWriter.WriteRgba(texturePng, texture);
        var textureResult = session.ImportLayer("texture", texturePng);
        Assert.True(textureResult.Contains("changed area 20x20 at (4000, 2000)"), textureResult);

        // Splat: change a patch, given in map orientation; the file stores it upside down.
        var splatPng = Path.Combine(dir, "splat.png");
        session.ExportLayer("resource:splatDistrTex", splatPng);
        var splat = PngReader.Read(splatPng).ToRgba();
        splat.Fill(new PixelRect(100, 40, 8, 8), new Rgba(0, 0, 255, 255));
        PngWriter.WriteRgba(splatPng, splat);
        Assert.True(session.ImportLayer("resource:splatDistrTex", splatPng).Contains("8x8"), "splat blocks re-encoded");

        // Metal: clear one spot's cells in the image.
        var metalPng = Path.Combine(dir, "metal.png");
        session.ExportLayer("metal", metalPng);
        var metal = PngReader.Read(metalPng).ToGray8();
        var spot = session.Metal.SpotNear(1000, 872)!;
        foreach (var c in spot.Cells)
            metal[c.Z * session.Smf.MetalMapWidth + c.X] = 0;
        PngWriter.WriteGray8(metalPng, session.Smf.MetalMapWidth, session.Smf.MetalMapHeight, metal);
        Assert.True(session.ImportLayer("metal", metalPng).Contains("9 cells changed; 23 spots"), "one spot removed");

        session.Save();
        var reopened = MapEditSession.Open(MapPackage.OpenFolder(package.Root));
        Assert.Equal((byte)255, reopened.TextureImage.GetPixel(4010, 2010).R, "texture patch saved");
        Assert.True(reopened.TextureImage.GetPixel(4010, 2010).G < 10, "texture patch is magenta");
        Assert.Equal(23, reopened.Metal.FindSpots().Count, "metal saved");
        Assert.True(Math.Abs(reopened.Heights[385, 385] - session.Heights[385, 385]) <= reopened.Heights.Step, "heights saved");
        var splatSaved = Textures.DdsFile.Load(Path.Combine(package.Root, "maps", "splat_distr.dds")).Decode();
        splatSaved.FlipVertical();
        Assert.True(splatSaved.GetPixel(104, 44).B > 240, "splat patch at its map position");
    }

    [Test]
    public static void HeightExportKeepsUnsavedEditsBeyondTheRange()
    {
        var session = MapEditSession.Open(MapPackage.OpenFolder(TestData.AcidicQuarry.Root)); // read-only use: never saved
        session.Apply("height-raise:1100,950:300:80:0.6");
        float raised = session.Heights.HeightAt(1100, 950);
        var path = Path.Combine(TestData.NewWorkDir("height-export"), "heights.png");
        Assert.True(session.ExportLayer("heights", path).Contains("range widened"), "export notes the wider range");

        var fresh = FreshSession("height-export-import", out _);
        fresh.ImportLayer("heights", path);
        Assert.True(Math.Abs(fresh.Heights.HeightAt(1100, 950) - raised) < 0.02, $"raised height {raised} survives export/import, got {fresh.Heights.HeightAt(1100, 950)}");
        Assert.True(Math.Abs(fresh.Heights.HeightAt(3072, 3072) - session.Heights.HeightAt(3072, 3072)) < 0.02, "untouched terrain survives too");

        // Importing it back into the session it came from changes nothing: every value rounds to the current height.
        var note = session.ImportLayer("heights", path);
        Assert.True(note.StartsWith("heights: 0 vertices changed", StringComparison.Ordinal), $"re-import with unsaved edits is a no-op: {note}");
    }

    [Test]
    public static void TexturePaintChangesTheBrushAreaAndMinimap()
    {
        var session = FreshSession("paint", out _);
        var minimapBefore = (byte[])session.Smf.Minimap.Clone();
        var before = session.TextureImage.Crop(new PixelRect(0, 0, 6144, 6144));
        session.Apply("texture-paint:1100,950:60:20,200,255:0.7");

        var after = session.TextureImage;
        var centre = after.GetPixel(1100, 950);
        Assert.True(centre.R < 40 && centre.G > 180 && centre.B > 230, $"painted centre {centre}");
        Assert.Equal(before.GetPixel(1100 + 80, 950), after.GetPixel(1100 + 80, 950), "outside the brush unchanged");
        Assert.Equal(before.GetPixel(2000, 3000), after.GetPixel(2000, 3000), "far away unchanged");
        Assert.True(!session.Smf.Minimap.AsSpan().SequenceEqual(minimapBefore), "minimap redrawn there");
        Assert.Throws<InvalidOperationException>(() => session.Apply("texture-paint:1,1:10:300,0,0"), "colour out of range");
    }

    /// <summary>
    /// A whole new texture imported: the minimap shows it (it was drawn from the texture as it was before the import,
    /// in a session that had decoded it already, as the editor's always has), in the style fitted from the map as it
    /// was. A session's first edit (MapTool's way: no window primes the session) comes out exactly as in a primed one;
    /// fitted after the import instead, a minimap of the old layout against the new texture came out a flat wash.
    /// </summary>
    [Test]
    public static void TextureImportRedrawsTheMinimapAsAPrimedSessionDoes()
    {
        var source = FreshSession("minimap-style-source", out _).TextureImage;
        var mirrored = new RgbaImage(source.Width, source.Height); // another layout in the same colours
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
                mirrored.SetPixel(x, y, source.GetPixel(source.Width - 1 - x, y));
        var path = Path.Combine(TestData.NewWorkDir("minimap-style-png"), "mirrored.png");
        PngWriter.WriteRgba(path, mirrored);

        var primed = FreshSession("minimap-style-primed", out _);
        primed.Prime();
        primed.ImportLayer("texture", path);
        var plain = FreshSession("minimap-style-plain", out _);
        plain.ImportLayer("texture", path);
        Assert.True(plain.Smf.Minimap.AsSpan().SequenceEqual(primed.Smf.Minimap), "the minimap redrawn as a primed session redraws it");

        var minimap = Minimap.Decode(plain.Smf);
        double toNew = WorldMachineTests.Likeness(minimap, mirrored.Resample(Minimap.Size, Minimap.Size));
        double toOld = WorldMachineTests.Likeness(minimap, source.Resample(Minimap.Size, Minimap.Size));
        Assert.True(toNew > toOld + 0.3, $"the minimap shows the new texture (likeness {toNew:0.00}), not the old ({toOld:0.00})");
    }

    /// <summary>A minimap that isn't a picture of the texture leaves the land's colours as they are; one that is, fits.</summary>
    [Test]
    public static void MinimapStyleFitsOnlyAMinimapOfItsTexture()
    {
        var texture = new RgbaImage(64, 64);
        var noise = new RgbaImage(64, 64);
        var darker = new RgbaImage(64, 64);
        var halfNoise = new RgbaImage(64, 64);
        var random = new Random(7);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                var t = new Rgba((byte)(40 + x * 3), (byte)(60 + y * 2), (byte)(30 + x + y), 255);
                texture.SetPixel(x, y, t);
                noise.SetPixel(x, y, new Rgba((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255));
                darker.SetPixel(x, y, new Rgba((byte)(t.R * 0.8 + 10), (byte)(t.G * 0.8 + 10), (byte)(t.B * 0.8 + 10), 255));
                halfNoise.SetPixel(x, y, new Rgba((byte)(t.R * 0.8 + 10), (byte)(t.G * 0.8 + 10), (byte)random.Next(256), 255));
            }
        var probe = new Rgba(100, 150, 200, 255);
        Assert.Equal(probe, MinimapStyle.Fit(noise, texture, (_, _) => false).Apply(probe, false), "another picture: the land as it is");
        Assert.Equal(probe, MinimapStyle.Fit(halfNoise, texture, (_, _) => false).Apply(probe, false), "unlike in one channel: all three as they are (no colour cast)");
        var fitted = MinimapStyle.Fit(darker, texture, (_, _) => false).Apply(probe, false);
        Assert.True(Math.Abs(fitted.R - 90) <= 1 && Math.Abs(fitted.G - 130) <= 1 && Math.Abs(fitted.B - 170) <= 1, $"its own picture, darker: fitted ({fitted})");
    }

    [Test]
    public static void ImportRejectsWrongSizes()
    {
        var session = MapEditSession.Open(MapPackage.OpenFolder(TestData.AcidicQuarry.Root));
        var path = Path.Combine(TestData.NewWorkDir("layers-size"), "small.png");
        PngWriter.WriteGray16(path, 10, 10, new ushort[100]);
        var ex = Assert.Throws<InvalidOperationException>(() => session.ImportLayer("heights", path), "wrong height image size");
        Assert.True(ex.Message.Contains("769x769"), ex.Message);
        Assert.Throws<InvalidOperationException>(() => session.ImportLayer("weather", path), "unknown layer");
        Assert.Throws<InvalidOperationException>(() => session.ImportLayer("resource:skyBox", path), "unset resource");
    }
}
