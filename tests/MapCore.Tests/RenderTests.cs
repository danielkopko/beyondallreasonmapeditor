using System.Diagnostics;
using MapCore.Editing;
using MapCore.Imaging;
using MapCore.Rendering;

namespace MapCore.Tests;

static class RenderTests
{
    static MapEditSession Fresh(string name) =>
        MapEditSession.Open(MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir(name)));

    /// <summary>One unedited session shared by the tests that only look.</summary>
    static readonly Lazy<MapEditSession> shared = new(() => Fresh("render-shared"));

    static MapEditSession Shared => shared.Value;

    [Test]
    public static void ViewTransformRoundTrips()
    {
        var view = new ViewTransform(-123.5, 456.25, 3.7);
        var (x, z) = view.ToWorld(17.5, 230.25);
        var (ix, iy) = view.ToImage(x, z);
        Assert.True(Math.Abs(ix - 17.5) < 1e-9 && Math.Abs(iy - 230.25) < 1e-9, "image -> world -> image");

        var zoomed = view.ZoomAbout(40, 60, 1.6);
        var (zx, zz) = zoomed.ToWorld(40, 60);
        var (vx, vz) = view.ToWorld(40, 60);
        Assert.True(Math.Abs(zx - vx) < 1e-9 && Math.Abs(zz - vz) < 1e-9, "zoom keeps the point under the cursor");
        Assert.True(Math.Abs(zoomed.UnitsPerPixel - 3.7 / 1.6) < 1e-12, "zoom in shrinks units per pixel");

        var fit = ViewTransform.Fit(6144, 6144, 800, 600);
        var (left, top) = fit.ToImage(0, 0);
        var (right, bottom) = fit.ToImage(6144, 6144);
        Assert.True(left >= 0 && top >= 0 && right <= 800 && bottom <= 600, $"whole map fits: ({left:0.#}, {top:0.#}) - ({right:0.#}, {bottom:0.#})");
        Assert.True(Math.Abs((left + right) / 2 - 400) < 1e-6 && Math.Abs((top + bottom) / 2 - 300) < 1e-6, "map centred");

        var panned = fit.Pan(10, -20);
        var (px, pz) = panned.ToWorld(110, 80);
        var (fx, fz) = fit.ToWorld(100, 100);
        Assert.True(Math.Abs(px - fx) < 1e-9 && Math.Abs(pz - fz) < 1e-9, "the map follows a drag");
    }

    [Test]
    public static void TextureAtOneToOneIsTheTextureItself()
    {
        var s = Shared;
        using var renderer = new MapRenderer(s);
        var target = new RgbaImage(96, 64);
        renderer.Render(target, new ViewTransform(1000, 800, 1), MapViewLayer.Texture, MapOverlays.None);
        var problems = new List<string>();
        for (int y = 0; y < target.Height; y++)
            for (int x = 0; x < target.Width; x++)
                if (target.GetPixel(x, y) != s.TextureImage.GetPixel(1000 + x, 800 + y))
                    problems.Add($"({x}, {y}): {target.GetPixel(x, y)} vs {s.TextureImage.GetPixel(1000 + x, 800 + y)}");
        Assert.Empty(problems, "rendered pixels that differ from the texture at the same world position");
    }

    [Test]
    public static void ZoomedOutViewMatchesAnAreaAverage()
    {
        var s = Shared;
        using var renderer = new MapRenderer(s);
        var target = new RgbaImage(768, 768);
        renderer.Render(target, new ViewTransform(0, 0, 8), MapViewLayer.Texture, MapOverlays.None);
        var expected = s.TextureImage.Resample(768, 768);
        long total = 0;
        int worst = 0;
        for (int i = 0; i < target.Pixels.Length; i += 4)
            for (int c = 0; c < 3; c++)
            {
                int d = Math.Abs(target.Pixels[i + c] - expected.Pixels[i + c]);
                total += d;
                worst = Math.Max(worst, d);
            }
        double mean = total / (768.0 * 768 * 3);
        Report.Info($"8 units per pixel vs independent area average: mean difference {mean:0.000}, worst {worst}");
        Assert.True(mean < 0.75 && worst <= 4, "the pyramid level matches an area average (only rounding differs)");
    }

    [Test]
    public static void SplatAndNormalViewsAreInMapOrientation()
    {
        var s = Shared;
        using var renderer = new MapRenderer(s);
        var dir = TestData.NewWorkDir("render-orient");
        foreach (var (layer, key) in new[] { (MapViewLayer.Splat, "splatDistrTex"), (MapViewLayer.Normals, "detailNormalTex") })
        {
            var png = Path.Combine(dir, key + ".png");
            s.ExportLayer("resource:" + key, png);
            var export = PngReader.Read(png).ToRgba();
            double units = s.MapWidth / export.Width;
            // One crop at the top (north) and one at the bottom (south): a flipped view fails at least one.
            foreach (int top in new[] { 0, export.Height - 128 })
            {
                var target = new RgbaImage(128, 128);
                renderer.Render(target, new ViewTransform(64 * units, top * units, units), layer, MapOverlays.None);
                int differing = 0;
                for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++)
                    {
                        var a = target.GetPixel(x, y);
                        var b = export.GetPixel(64 + x, top + y);
                        if ((a.R, a.G, a.B) != (b.R, b.G, b.B))
                            differing++;
                    }
                Assert.Equal(0, differing, $"{layer} pixels differing from the map-orientation export at row {top}");
            }
        }
    }

    [Test]
    public static void HeightsViewShowsWaterInBlue()
    {
        var s = Shared;
        using var renderer = new MapRenderer(s);
        var h = s.Heights;
        int deep = Array.IndexOf(h.Heights, h.Heights.Min()), high = Array.IndexOf(h.Heights, h.Heights.Max());
        foreach (var (index, water) in new[] { (deep, true), (high, false) })
        {
            double x = index % h.Width * 8.0, z = index / h.Width * 8.0;
            var target = new RgbaImage(9, 9);
            renderer.Render(target, new ViewTransform(x - 4.5 * 2, z - 4.5 * 2, 2), MapViewLayer.Heights, MapOverlays.None);
            var p = target.GetPixel(4, 4);
            Assert.True(water ? p.B > p.R + 40 : p.R >= p.B, $"height {h.HeightAt(x, z):0} at ({x}, {z}) is drawn {p}");
        }
    }

    [Test]
    public static void MetalSpotMarkersSitOnTheirCells()
    {
        var s = Shared;
        using var renderer = new MapRenderer(s);
        var spots = s.Metal.FindSpots();
        var full = new RgbaImage(800, 800);
        var result = renderer.Render(full, ViewTransform.Fit(s.MapWidth, s.MapHeight, 800, 800), MapViewLayer.Texture, MapOverlays.MetalSpots);
        Assert.Equal(spots.Count, result.Labels.Count, "one income label per spot");
        Assert.True(result.Labels.All(l => l.Text == "2.30"), "Acidic Quarry's spots earn 2.30 metal/s each: " + string.Join(", ", result.Labels.Select(l => l.Text).Distinct()));

        // In the metal view, each spot centre lands on metal: there it differs from the same dimmed hillshade
        // without metal (the grass view; this map has no grass), and away from metal the two are identical.
        var problems = new List<string>();
        foreach (var spot in spots)
        {
            var view = new ViewTransform(spot.X - 32 * 2, spot.Z - 32 * 2, 2);
            var metal = new RgbaImage(64, 64);
            var plain = new RgbaImage(64, 64);
            renderer.Render(metal, view, MapViewLayer.Metal, MapOverlays.None);
            renderer.Render(plain, view, MapViewLayer.Grass, MapOverlays.None);
            if (metal.GetPixel(32, 32) == plain.GetPixel(32, 32))
                problems.Add($"spot ({spot.X}, {spot.Z}): no metal drawn at its centre");
            if (s.Probe(spot.X, spot.Z).MetalRaw == 0)
                problems.Add($"spot ({spot.X}, {spot.Z}): no metal in the cell under its centre");
            if (metal.GetPixel(0, 0) != plain.GetPixel(0, 0) && s.Probe(view.Left + 1, view.Top + 1).MetalRaw == 0)
                problems.Add($"spot ({spot.X}, {spot.Z}): metal drawn where there is none");
        }
        Assert.Empty(problems, "metal view vs spot centres");
    }

    [Test]
    public static void PyramidsAfterEditsAndUndoMatchAFreshBuild()
    {
        var s = Fresh("render-edit");
        using var renderer = new MapRenderer(s);
        var target = new RgbaImage(320, 200);
        var view = new ViewTransform(900, 700, 1.7);
        foreach (var layer in new[] { MapViewLayer.Texture, MapViewLayer.Heights })
            renderer.Render(target, view, layer, MapOverlays.None);
        var before = Snapshot(renderer);

        s.Apply("texture-paint:1150,800:60:20,200,255:0.7");
        s.Apply("height-raise:1100,900:150:40");
        foreach (var layer in new[] { MapViewLayer.Texture, MapViewLayer.Heights })
            renderer.Render(target, view, layer, MapOverlays.None);
        var edited = Snapshot(renderer);
        Assert.True(!Same(before, edited), "the edits changed the layer images");
        using (var fresh = new MapRenderer(s))
            Assert.True(Same(edited, Snapshot(fresh)), "after the edits, every level equals a fresh build");

        s.Undo();
        s.Undo();
        foreach (var layer in new[] { MapViewLayer.Texture, MapViewLayer.Heights })
            renderer.Render(target, view, layer, MapOverlays.None);
        Assert.True(Same(before, Snapshot(renderer)), "after undo, every level equals the original");

        static List<byte[]> Snapshot(MapRenderer r) =>
            [.. r.LevelsOf(MapViewLayer.Texture)!.Concat(r.LevelsOf(MapViewLayer.Heights)!).Select(l => (byte[])l.Pixels.Clone())];

        static bool Same(List<byte[]> a, List<byte[]> b) => a.Count == b.Count && a.Zip(b).All(p => p.First.AsSpan().SequenceEqual(p.Second));
    }

    [Test]
    public static void SaveRefreshesTheNormalsView()
    {
        var s = Fresh("render-save");
        using var renderer = new MapRenderer(s);
        var target = new RgbaImage(64, 64);
        var view = new ViewTransform(3072 - 64 * 4, 3072 - 64 * 4, 8);
        renderer.Render(target, view, MapViewLayer.Normals, MapOverlays.None);
        var before = (byte[])target.Pixels.Clone();

        s.Apply("height-raise:3072,3072:200:60");
        var note = renderer.Render(target, view, MapViewLayer.Normals, MapOverlays.None).Note;
        Assert.True(note != null && note.Contains("saved"), "the normals view says height edits show after saving");
        Assert.True(target.Pixels.AsSpan().SequenceEqual(before), "unsaved height edits leave the normals view alone");

        s.Save();
        renderer.Render(target, view, MapViewLayer.Normals, MapOverlays.None);
        Assert.True(!target.Pixels.AsSpan().SequenceEqual(before), "after saving, the normals view shows the rotated normal map");
        var fresh = new RgbaImage(64, 64);
        using (var other = new MapRenderer(s))
            other.Render(fresh, view, MapViewLayer.Normals, MapOverlays.None);
        Assert.True(target.Pixels.AsSpan().SequenceEqual(fresh.Pixels), "and equals a fresh render");
    }

    [Test]
    public static void SeaLevelPreviewChangesOnlyTheView()
    {
        var s = Fresh("render-sealevel");
        using var renderer = new MapRenderer(s);
        var target = new RgbaImage(400, 400);
        var view = ViewTransform.Fit(s.MapWidth, s.MapHeight, 400, 400);
        renderer.Render(target, view, MapViewLayer.Heights, MapOverlays.WaterLine);
        var before = (byte[])target.Pixels.Clone();
        int Blue(RgbaImage image) => Enumerable.Range(0, image.Width * image.Height).Count(i => image.Pixels[i * 4 + 2] > image.Pixels[i * 4] + 40);
        int blueBefore = Blue(target);

        renderer.WaterLevel = 60;
        var result = renderer.Render(target, view, MapViewLayer.Heights, MapOverlays.WaterLine);
        int blueAfter = Blue(target);
        Report.Info($"blue (under water) pixels: {blueBefore} at sea level 0, {blueAfter} at 60");
        Assert.True(blueAfter > blueBefore * 1.05, "a higher sea level puts more ground under water");
        Assert.True(result.Note?.Contains("-60") == true, $"the view says what applying would do: {result.Note}");
        Assert.True(!s.HasUnsavedChanges && !s.CanUndo, "the preview changes nothing in the map");

        renderer.WaterLevel = 0;
        renderer.Render(target, view, MapViewLayer.Heights, MapOverlays.WaterLine);
        Assert.True(target.Pixels.AsSpan().SequenceEqual(before), "back at 0, the view is exactly as before");
    }

    [Test]
    public static void ProbeReportsHeightMetalAndIncome()
    {
        var s = Shared;
        var spot = s.Metal.FindSpots()[0];
        var p = s.Probe(spot.X, spot.Z);
        Assert.True(p.OnMap && p.MetalRaw > 0, "metal under a spot centre");
        Assert.True(Math.Abs(p.ExtractorIncome - 2.30) < 0.01, $"extractor income at a spot centre: {p.ExtractorIncome:0.###}");
        Assert.Equal(s.Heights.HeightAt(spot.X, spot.Z), p.Height, "height at the spot");
        Assert.True(!s.Probe(-1, 100).OnMap && !s.Probe(100, s.MapHeight).OnMap, "positions off the map");
    }

    [Test]
    public static void FullWindowRenderIsQuick()
    {
        var s = Shared;
        using var renderer = new MapRenderer(s);
        var target = new RgbaImage(1600, 1000);
        var all = MapOverlays.WaterLine | MapOverlays.MetalSpots | MapOverlays.Items | MapOverlays.StartPositions | MapOverlays.Grid;
        foreach (var layer in new[] { MapViewLayer.Texture, MapViewLayer.Heights })
        {
            var fit = ViewTransform.Fit(s.MapWidth, s.MapHeight, target.Width, target.Height);
            renderer.Render(target, fit, layer, all); // builds the layer
            var sw = Stopwatch.StartNew();
            const int frames = 5;
            for (int i = 0; i < frames; i++)
                renderer.Render(target, fit.ZoomAbout(800, 500, 1 + i * 0.7), layer, all);
            double ms = sw.Elapsed.TotalMilliseconds / frames;
            Report.Info($"{layer}: {ms:0} ms per 1600x1000 frame with every overlay");
            Assert.True(ms < 400, $"{layer} renders fast enough to pan smoothly");
        }
    }
}
