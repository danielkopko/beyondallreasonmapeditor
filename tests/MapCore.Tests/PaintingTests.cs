using MapCore.Editing;
using MapCore.Heights;
using MapCore.Imaging;
using MapCore.Rendering;

namespace MapCore.Tests;

static class PaintingTests
{
    static MapEditSession Fresh(string name) =>
        MapEditSession.Open(MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir(name)));

    static double Luma(Rgba c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

    /// <summary>Mean colour of a square of texture pixels around a world point (1 pixel per unit on this map).</summary>
    static (double R, double G, double B) Mean(RgbaImage image, int cx, int cy, int half)
    {
        double r = 0, g = 0, b = 0;
        int n = 0;
        for (int y = cy - half; y <= cy + half; y++)
            for (int x = cx - half; x <= cx + half; x++, n++)
            {
                var p = image.GetPixel(x, y);
                (r, g, b) = (r + p.R, g + p.G, b + p.B);
            }
        return (r / n, g / n, b / n);
    }

    [Test]
    public static void TintKeepsBrightnessAndTakesTheColour()
    {
        var s = Fresh("paint-tint");
        var before = Mean(s.TextureImage, 1100, 950, 6);
        s.TintTexture(new Brush(1100, 950, 60, 0.8), new Rgba(220, 40, 40, 255));
        var after = Mean(s.TextureImage, 1100, 950, 6);
        double lumaBefore = 0.299 * before.R + 0.587 * before.G + 0.114 * before.B;
        double lumaAfter = 0.299 * after.R + 0.587 * after.G + 0.114 * after.B;
        Report.Info($"tint: mean {before} -> {after}");
        Assert.True(Math.Abs(lumaAfter - lumaBefore) < 6, $"brightness kept ({lumaBefore:0.#} -> {lumaAfter:0.#})");
        Assert.True(after.R > after.G + 20 && after.R > after.B + 20, "the area turned red");
    }

    [Test]
    public static void AdjustChangesBrightnessSaturationAndHue()
    {
        var s = Fresh("paint-adjust");
        var image = s.TextureImage;
        var a0 = Mean(image, 1100, 950, 6);
        s.AdjustTexture(new Brush(1100, 950, 60, 0.8), 1.4, 1, 0);
        var a1 = Mean(image, 1100, 950, 6);
        Assert.True(a1.R > a0.R * 1.25 && a1.G > a0.G * 1.25, $"brighter: {a0} -> {a1}");

        var b0 = Mean(image, 2000, 3000, 6);
        s.AdjustTexture(new Brush(2000, 3000, 60, 0.8), 1, 0, 0);
        var b1 = Mean(image, 2000, 3000, 6);
        Assert.True(Math.Max(b1.R, Math.Max(b1.G, b1.B)) - Math.Min(b1.R, Math.Min(b1.G, b1.B)) < 6, $"saturation 0 makes it grey: {b0} -> {b1}");

        var c0 = Mean(image, 3000, 2000, 6); // the acid-green sea: green well above blue
        s.AdjustTexture(new Brush(3000, 2000, 60, 0.8), 1, 1, 180);
        var c1 = Mean(image, 3000, 2000, 6);
        Assert.True(c0.G > c0.B + 15 && c1.B > c1.G, $"a half turn of hue swaps green for blue-ish: {c0} -> {c1}");
    }

    [Test]
    public static void CloneCopiesTheSource()
    {
        var s = Fresh("paint-clone");
        var image = s.TextureImage;
        var source = image.Crop(new PixelRect(1100 - 8, 950 - 8, 17, 17));
        s.CloneTexture(new Brush(2600, 3400, 50, 0.8), 1100 - 2600, 950 - 3400);
        double total = 0;
        for (int y = 0; y < 17; y++)
            for (int x = 0; x < 17; x++)
            {
                var a = source.GetPixel(x, y);
                var b = image.GetPixel(2600 - 8 + x, 3400 - 8 + y);
                total += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
            }
        double mean = total / (17 * 17 * 3);
        Report.Info($"clone: mean difference from the source {mean:0.0} (DXT1 re-encoding)");
        Assert.True(mean < 6, "the brush centre now shows the source");
        Assert.Throws<InvalidOperationException>(() => s.CloneTexture(new Brush(100, 100, 50), 0, 0), "a source equal to the brush is refused");
    }

    /// <summary>
    /// The splat file is stored upside down (rule 7). Painting in the north must show in the north of the
    /// map-orientation export, survive saving and reloading there, and leave the south untouched.
    /// </summary>
    [Test]
    public static void SplatPaintLandsWhereItIsPainted()
    {
        var s = Fresh("splat-paint");
        var dir = TestData.NewWorkDir("splat-paint-png");
        var beforePng = Path.Combine(dir, "before.png");
        s.ExportLayer("resource:" + MapEditSession.SplatKey, beforePng);
        var before = PngReader.Read(beforePng).ToRgba();
        var originalBytes = s.ResourceBytes(MapEditSession.SplatKey);
        double units = s.MapWidth / before.Width;
        const double x = 1500, z = 900;
        int px = (int)(x / units), pyNorth = (int)(z / units), pySouth = (int)((s.MapHeight - z) / units);

        s.PaintSplat(new Brush(x, z, 120, 0.8), new Rgba(0, 255, 0, 0));
        var afterPng = Path.Combine(dir, "after.png");
        s.ExportLayer("resource:" + MapEditSession.SplatKey, afterPng);
        var after = PngReader.Read(afterPng).ToRgba();
        var north = after.GetPixel(px, pyNorth);
        Assert.True(north.G > 240 && north.R < 15 && north.B < 15 && north.A < 15, $"painted weights at the brush centre: {north}");
        Assert.Equal(before.GetPixel(px, pySouth), after.GetPixel(px, pySouth), "the mirror point in the south is untouched");

        s.Save();
        var reopened = MapEditSession.Open(MapPackage.OpenFolder(s.Package.Root));
        var savedPng = Path.Combine(dir, "saved.png");
        reopened.ExportLayer("resource:" + MapEditSession.SplatKey, savedPng);
        Assert.Equal(north, PngReader.Read(savedPng).ToRgba().GetPixel(px, pyNorth), "the saved file has the paint in the same place");

        s.Undo();
        Assert.True(s.ResourceBytes(MapEditSession.SplatKey).AsSpan().SequenceEqual(originalBytes), "undo restores the splat file exactly");
    }

    [Test]
    public static void SplatViewFollowsPaintingAndUndo()
    {
        var s = Fresh("splat-view");
        using var renderer = new MapRenderer(s);
        var target = new RgbaImage(200, 200);
        var view = ViewTransform.Fit(s.MapWidth, s.MapHeight, 200, 200);
        renderer.Render(target, view, MapViewLayer.Splat, MapOverlays.None);
        var before = renderer.LevelsOf(MapViewLayer.Splat)!.Select(l => (byte[])l.Pixels.Clone()).ToList();

        s.BeginGroup("splat stroke");
        for (int i = 0; i < 6; i++)
            s.PaintSplat(new Brush(2000 + i * 60, 4000, 150, 0.5), new Rgba(255, 0, 0, 0), 0.7);
        s.EndGroup();
        renderer.Render(target, view, MapViewLayer.Splat, MapOverlays.None);
        var painted = renderer.LevelsOf(MapViewLayer.Splat)!.Select(l => (byte[])l.Pixels.Clone()).ToList();
        using (var fresh = new MapRenderer(s))
        {
            var rebuilt = fresh.LevelsOf(MapViewLayer.Splat)!;
            Assert.True(painted.Zip(rebuilt).All(p => p.First.AsSpan().SequenceEqual(p.Second.Pixels)), "after painting, the splat view equals a fresh build");
        }
        Assert.True(!painted[0].AsSpan().SequenceEqual(before[0]), "the painting shows in the splat view");

        s.Undo();
        renderer.Render(target, view, MapViewLayer.Splat, MapOverlays.None);
        var undone = renderer.LevelsOf(MapViewLayer.Splat)!;
        Assert.True(undone.Zip(before).All(p => p.First.Pixels.AsSpan().SequenceEqual(p.Second)), "after undo, the splat view is as before");
    }
}
