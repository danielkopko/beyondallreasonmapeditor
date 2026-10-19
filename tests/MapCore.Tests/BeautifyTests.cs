using MapCore.Editing;
using MapCore.Heights;
using MapCore.Imaging;
using MapCore.Movement;

namespace MapCore.Tests;

/// <summary>
/// Beautify (MapEditSession.Beautify.cs and .CliffDetail.cs): the texture painted by the terrain and the cliffs
/// roughened, leaving metal, water, where every unit and building can go, and what is placed as they were; each one
/// undo step, the same every time, and alike on both halves of a symmetric map.
/// </summary>
static class BeautifyTests
{
    static MapEditSession Open(string name, string? archive = null) =>
        MapEditSession.Open(MapPackage.Open(archive ?? TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir(name)));

    /// <summary>For each footprint of BAR's flattest buildings (96 units, every 16), whether its ground spans at most 2 × 40 × tan 10°.</summary>
    static bool[] FlatEnough(MapEditSession s)
    {
        var h = s.Heights;
        double limit = 2 * 40 * Math.Tan(10 * Math.PI / 180);
        int squares = 12, bx = (h.Width - 1 - squares) / 2 + 1, bz = (h.Height - 1 - squares) / 2 + 1;
        var flat = new bool[bx * bz];
        Parallel.For(0, bz, j =>
        {
            for (int i = 0; i < bx; i++)
            {
                float hi = float.MinValue, lo = float.MaxValue;
                for (int z = 2 * j; z <= 2 * j + squares; z++)
                    for (int x = 2 * i; x <= 2 * i + squares; x++)
                        (hi, lo) = (Math.Max(hi, h[x, z]), Math.Min(lo, h[x, z]));
                flat[j * bx + i] = hi - lo <= limit;
            }
        });
        return flat;
    }

    [Test]
    public static void TheTextureIsPaintedLeavingMetalHeightsAndSpotsAlone()
    {
        var s = Open("beautify-texture");
        var heights = (float[])s.Heights.Heights.Clone();
        var metal = (byte[])s.Smf.MetalMap.Clone();
        var before = (byte[])s.TextureImage.Pixels.Clone();
        var settings = s.SuggestBeautify(new BeautifySettings());
        Report.Info(settings.Encode());
        Assert.True(settings.Rock != new BeautifySettings().Rock && settings.Ground != new BeautifySettings().Ground, "the map's own rock and ground colours are read from it");
        Report.Info(s.BeautifyTexture(settings));
        var image = s.TextureImage;
        int changed = 0;
        for (int i = 0; i < before.Length; i += 4)
            if (before[i] != image.Pixels[i] || before[i + 1] != image.Pixels[i + 1] || before[i + 2] != image.Pixels[i + 2])
                changed++;
        Assert.True(changed > before.Length / 4 / 2, $"most of the texture is painted ({changed:N0} pixels)");
        Assert.True(s.Heights.Heights.AsSpan().SequenceEqual(heights) && s.Smf.MetalMap.AsSpan().SequenceEqual(metal), "heights and metal untouched");
        double perUnit = image.Width / s.MapWidth;
        int spotPixels = 0, spotChanged = 0;
        foreach (var spot in s.Metal.FindSpots())
            for (int dy = -40; dy <= 40; dy += 4)
                for (int dx = -40; dx <= 40; dx += 4)
                {
                    int x = (int)((spot.X + dx) * perUnit), y = (int)((spot.Z + dy) * perUnit), i = (y * image.Width + x) * 4;
                    if (x < 0 || y < 0 || x >= image.Width || y >= image.Height || dx * dx + dy * dy > 40 * 40)
                        continue;
                    spotPixels++;
                    if (before[i] != image.Pixels[i] || before[i + 1] != image.Pixels[i + 1] || before[i + 2] != image.Pixels[i + 2])
                        spotChanged++;
                }
        Assert.True(spotPixels > 500 && spotChanged == 0, $"the texture round metal spots (their look) is left as it was ({spotChanged} of {spotPixels} changed)");
        Assert.True(s.UndoDescription!.StartsWith("beautify-texture:", StringComparison.Ordinal), "one undo step");
        s.Undo();
        Assert.True(s.TextureImage.Pixels.AsSpan().SequenceEqual(before), "undo puts the texture back exactly");

        var again = Open("beautify-texture-again");
        again.BeautifyTexture(settings);
        s.Redo();
        Assert.True(again.TextureImage.Pixels.AsSpan().SequenceEqual(s.TextureImage.Pixels), "the same settings on the same map give the same texture");
        Assert.Throws<InvalidOperationException>(() => s.Apply("beautify-texture:rockamount=2"), "an amount over 1 is refused");
        Assert.Throws<InvalidOperationException>(() => s.Apply("beautify-texture:nodgun=1"), "an unknown setting is refused");
    }

    /// <summary>
    /// Soften hard edges on its own: a concrete plate painted on grainy grass gets a dirty blend along its border; the
    /// grass well away from it, grain and all, is left alone (grain isn't an edge).
    /// </summary>
    [Test]
    public static void HardEdgesAreSoftenedAndGrainIsNot()
    {
        var s = Open("beautify-soften", SyntheticMap.Build(TestData.NewWorkDir("beautify-soften-map")));
        var random = new Random(1);
        var painted = new RgbaImage(s.TextureImage.Width, s.TextureImage.Height);
        (int X1, int Y1, int X2, int Y2) plate = (1200, 150, 1500, 400);
        bool OnPlate(int x, int y) => x >= plate.X1 && x < plate.X2 && y >= plate.Y1 && y < plate.Y2;
        for (int y = 0; y < painted.Height; y++)
            for (int x = 0; x < painted.Width; x++)
            {
                int grain = random.Next(-14, 15);
                painted.SetPixel(x, y, OnPlate(x, y) ? new Rgba((byte)(150 + grain), (byte)(150 + grain), (byte)(148 + grain), 255)
                    : new Rgba((byte)(88 + grain), (byte)(108 + grain), (byte)(58 + grain), 255));
            }
        var png = Path.Combine(TestData.NewWorkDir("beautify-soften-png"), "plate.png");
        PngWriter.WriteRgba(png, painted);
        s.Apply("import:texture=" + png);
        var image = s.TextureImage;
        var before = (byte[])image.Pixels.Clone();
        var settings = new BeautifySettings
        {
            Strength = 1, SoftenEdges = 1, Crevices = 0, Detail = 0, Breakup = 0,
            RockAmount = 0, DirtAmount = 0, SandAmount = 0, MudAmount = 0, SeabedAmount = 0, GroundAmount = 0,
        };
        Report.Info(s.BeautifyTexture(settings));
        bool Changed(int x, int y)
        {
            int i = (y * image.Width + x) * 4;
            return Math.Abs(image.Pixels[i] - before[i]) + Math.Abs(image.Pixels[i + 1] - before[i + 1]) + Math.Abs(image.Pixels[i + 2] - before[i + 2]) > 6;
        }
        int border = 0, borderChanged = 0, grass = 0, grassChanged = 0;
        for (int x = plate.X1 - 4; x <= plate.X2 + 4; x += 2)
            foreach (int y in new[] { plate.Y1 - 3, plate.Y1 + 2, plate.Y2 - 3, plate.Y2 + 2 })
            {
                border++;
                if (Changed(x, y))
                    borderChanged++;
            }
        for (int y = 640; y < 1000; y += 5)
            for (int x = 100; x < 900; x += 5)
            {
                grass++;
                if (Changed(x, y))
                    grassChanged++;
            }
        Report.Info($"{borderChanged} of {border} pixels along the plate's border changed; {grassChanged} of {grass} on the grass away from it");
        Assert.True(borderChanged > border / 3, "the plate's hard border is blended");
        Assert.True(grassChanged == 0, "grainy grass away from any edge is left alone");

        var aq = Open("beautify-soften-aq");
        var aqBefore = (byte[])aq.TextureImage.Pixels.Clone();
        aq.BeautifyTexture(settings);
        int aqChanged = 0;
        for (int i = 0; i < aqBefore.Length; i += 4 * 7)
            if (Math.Abs(aq.TextureImage.Pixels[i] - aqBefore[i]) + Math.Abs(aq.TextureImage.Pixels[i + 1] - aqBefore[i + 1]) + Math.Abs(aq.TextureImage.Pixels[i + 2] - aqBefore[i + 2]) > 6)
                aqChanged++;
        double share = aqChanged / (aqBefore.Length / (4.0 * 7));
        Report.Info($"on Acidic Quarry, {share:P1} of the texture changes by more than a little (its region borders: crater rims, plates)");
        Assert.True(share < 0.25, "on a finished map, only the borders between regions change, not its grain everywhere");
    }

    [Test]
    public static void CliffsAreRoughenedKeepingWhereUnitsAndBuildingsGo()
    {
        var s = Open("beautify-cliffs");
        var access = new AccessMap(s.Heights);
        var flat = FlatEnough(s);
        var before = (float[])s.Heights.Heights.Clone();
        var metal = (byte[])s.Smf.MetalMap.Clone();
        Report.Info(s.BeautifyHeights(new CliffDetailSettings { Amount = 14, Ground = 3 }));
        int changed = s.Heights.Heights.Zip(before).Count(p => p.First != p.Second);
        double most = s.Heights.Heights.Zip(before).Max(p => Math.Abs(p.First - p.Second));
        Assert.True(changed > 50_000 && most > 5, $"cliffs and open ground are reshaped ({changed:N0} heights, up to {most:0.#})");
        var differences = access.Differences(new AccessMap(s.Heights), 10);
        Assert.True(differences.Count == 0, $"every movement class goes where it went: {string.Join("; ", differences)}");
        var flatNow = FlatEnough(s);
        int lost = flat.Zip(flatNow).Count(p => p.First && !p.Second);
        Assert.True(lost == 0, $"ground flat enough for a fusion reactor stays so ({lost} footprints lost it)");
        Assert.True(s.Smf.MetalMap.AsSpan().SequenceEqual(metal), "metal untouched");
        foreach (var spot in s.Metal.FindSpots())
            Assert.True(Math.Abs(s.Heights.HeightAt(spot.X, spot.Z) - before[(int)Math.Round(spot.Z / 8) * s.Heights.Width + (int)Math.Round(spot.X / 8)]) < 1e-3 ||
                        s.Heights.HeightAt(spot.X, spot.Z) == HeightAtCopy(before, s, spot.X, spot.Z), $"the ground under the metal spot at ({spot.X}, {spot.Z}) is as it was");
        Assert.True(s.Heights.Heights.Min() >= before.Min() && s.Heights.Heights.Max() <= before.Max(), "within the map's own heights");

        // Saved and opened again: the 16-bit heights the game reads keep the same access.
        var target = Path.Combine(TestData.NewWorkDir("beautify-cliffs-saved"), "cliffs.sd7");
        s.Save();
        s.Package.SaveAs(target, overwrite: true);
        var reopened = Open("beautify-cliffs-reopened", target);
        var afterSave = access.Differences(new AccessMap(reopened.Heights), 10);
        Assert.True(afterSave.Count == 0, $"and so after saving and opening again: {string.Join("; ", afterSave)}");
        s.Undo();
        Assert.True(s.Heights.Heights.AsSpan().SequenceEqual(before), "undo puts the heights back exactly");
    }

    static float HeightAtCopy(float[] heights, MapEditSession s, double x, double z)
    {
        int w = s.Heights.Width, h = s.Heights.Height;
        double gx = Math.Clamp(x / 8, 0, w - 1), gz = Math.Clamp(z / 8, 0, h - 1);
        int x0 = Math.Min((int)gx, w - 2), z0 = Math.Min((int)gz, h - 2);
        double fx = gx - x0, fz = gz - z0;
        double top = heights[z0 * w + x0] * (1 - fx) + heights[z0 * w + x0 + 1] * fx, bottom = heights[(z0 + 1) * w + x0] * (1 - fx) + heights[(z0 + 1) * w + x0 + 1] * fx;
        return (float)(top * (1 - fz) + bottom * fz);
    }

    /// <summary>A ramp built and then the map beautified: the ramp's surface is as it was, and who can climb it.</summary>
    [Test]
    public static void ARampIsLeftAsItWas()
    {
        var s = Open("beautify-ramp");
        var ramp = new Ramp(2300, 2150, 2600, 2400, 96, 0);
        s.BuildRamp(ramp, 40, 180);
        var surface = new List<(double X, double Z, float H)>();
        for (double u = 8; u < ramp.Length - 8; u += 16)
            for (double v = -32; v <= 32; v += 16) // the drivable middle (a vertex in from each side, where the banks begin)
            {
                var (x, z) = ramp.World(u, v);
                surface.Add((x, z, s.Heights.HeightAt(x, z)));
            }
        var access = new AccessMap(s.Heights);
        s.BeautifyHeights(new CliffDetailSettings { Amount = 16, Ground = 4 });
        int moved = surface.Count(p => Math.Abs(s.Heights.HeightAt(p.X, p.Z) - p.H) > 0.6);
        Report.Info($"{moved} of {surface.Count} points on the ramp moved more than 0.6");
        Assert.True(access.Differences(new AccessMap(s.Heights), 1).Count == 0, "who can go where, the ramp included, is as it was");
        Assert.True(moved <= surface.Count / 20, $"the ramp's surface is as it was ({moved} of {surface.Count} points moved over 0.6)");
    }

    /// <summary>On a map whose halves mirror each other, a beautify with the mirror set comes out alike on both halves.</summary>
    [Test]
    public static void ASymmetricMapStaysSymmetric()
    {
        var s = Open("beautify-mirror", SyntheticMap.Build(TestData.NewWorkDir("beautify-mirror-map")));
        var h = s.Heights;
        int w = h.Width;
        var mirrored = (float[])h.Heights.Clone();
        for (int z = 0; z < h.Height; z++)
            for (int x = w / 2 + 1; x < w; x++)
                mirrored[z * w + x] = mirrored[z * w + (w - 1 - x)];
        h.SetAll(mirrored);
        var plain = new RgbaImage(s.TextureImage.Width, s.TextureImage.Height);
        for (int y = 0; y < plain.Height; y++)
            for (int x = 0; x < plain.Width; x++)
                plain.SetPixel(x, y, new Rgba(110, 104, 80, 255));
        var png = Path.Combine(TestData.NewWorkDir("beautify-mirror-png"), "plain.png");
        PngWriter.WriteRgba(png, plain);
        s.Apply("import:texture=" + png);
        s.BeautifyHeights(new CliffDetailSettings { Mirror = MirrorKind.LeftRight, CliffFrom = 20, Amount = 12, Ground = 2 });
        s.BeautifyTexture(new BeautifySettings { Mirror = MirrorKind.LeftRight, Strength = 1, RockFrom = 20, RockTo = 32 }.WithPreset("Desert"));
        // Away from what is protected one-sidedly (the synthetic map's metal spots and starts aren't symmetric).
        var keepOut = s.Metal.FindSpots().Select(p => (p.X, p.Z)).Concat(s.Info.StartPositions.Select(p => (p.X, p.Z))).ToList();
        bool Clear(double x, double z) => keepOut.All(p => Math.Min(Math.Abs(p.X - x), Math.Abs(p.X - (s.MapWidth - x))) > 200 || Math.Abs(p.Z - z) > 200);
        double worstHeight = 0;
        for (int z = 0; z < h.Height; z++)
            for (int x = 0; x < w / 2; x++)
                if (Clear(x * 8, z * 8))
                    worstHeight = Math.Max(worstHeight, Math.Abs(h[x, z] - h[w - 1 - x, z]));
        var image = s.TextureImage;
        int worstColour = 0, compared = 0;
        for (int y = 0; y < image.Height; y += 3)
            for (int x = 0; x < image.Width / 2; x += 3)
            {
                if (!Clear(x / (image.Width / s.MapWidth), y / (image.Width / s.MapWidth)))
                    continue;
                var (a, b) = (image.GetPixel(x, y), image.GetPixel(image.Width - 1 - x, y));
                worstColour = Math.Max(worstColour, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))));
                compared++;
            }
        Report.Info($"worst height difference {worstHeight:0.####}, worst colour difference {worstColour} over {compared:N0} pixels");
        Assert.True(worstHeight < 0.01 && worstColour <= 12, "both halves come out alike (colours within the compression's rounding)");
    }
}
