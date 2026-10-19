using System.Globalization;
using MapCore.Editing;
using MapCore.Heights;
using MapCore.Imaging;

namespace MapCore.Tests;

static class RampTests
{
    static MapEditSession Fresh(string name) =>
        MapEditSession.Open(MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir(name)));

    static string Op(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    static string RampOp(Ramp r, string heights, string edge = "auto", int fill = 1) =>
        Op($"ramp:{r.X1},{r.Z1}:{r.X2},{r.Z2}:{r.Width}:{heights}:{edge}:{fill}");

    /// <summary>
    /// Checks a built ramp against the heights before it: on the ramp the ground is the straight line from h1 to h2
    /// (bilinear sampling of a plane is exact well inside it), in the edge it lies between the old ground and the
    /// ramp, and beyond the edge no vertex has moved at all.
    /// </summary>
    static void CheckHeights(MapEditSession s, float[] before, Ramp ramp, double h1, double h2, string what)
    {
        var heights = s.Heights;
        double worst = 0;
        for (double u = 12; u <= ramp.Length - 12; u += 7)
            for (double v = -ramp.Width / 2 + 12; v <= ramp.Width / 2 - 12; v += 9)
            {
                var (x, z) = ramp.World(u, v);
                worst = Math.Max(worst, Math.Abs(heights.HeightAt(x, z) - (h1 + (h2 - h1) * u / ramp.Length)));
            }
        Assert.True(worst < 0.01, $"{what}: the ramp is a straight slope from {h1} to {h2} (worst {worst:0.0000})");

        int moved = 0, outOfRange = 0, edge = 0;
        for (int z = 0; z < heights.Height; z++)
            for (int x = 0; x < heights.Width; x++)
            {
                int i = z * heights.Width + x;
                double d = ramp.DistanceOutside(x * HeightMap.Spacing, z * HeightMap.Spacing);
                if (d >= ramp.Edge)
                {
                    moved += heights.Heights[i] != before[i] ? 1 : 0;
                    continue;
                }
                if (d <= 0)
                    continue;
                edge++;
                double target = ramp.SurfaceAt(x * HeightMap.Spacing, z * HeightMap.Spacing, h1, h2);
                if (heights.Heights[i] < Math.Min(before[i], target) - 1e-3 || heights.Heights[i] > Math.Max(before[i], target) + 1e-3)
                    outOfRange++;
            }
        Assert.Equal(0, moved, $"{what}: vertices beyond the edge that moved");
        Assert.True(edge > 0 && outOfRange == 0, $"{what}: all {edge} edge vertices lie between the old ground and the ramp ({outOfRange} outside)");
    }

    [Test]
    public static void HeightsFollowTheRampAndNothingBeyondTheEdgeMoves()
    {
        var s = Fresh("ramp-heights");
        var before = (float[])s.Heights.Heights.Clone();
        var ramp = new Ramp(2000, 2600, 2400, 2600, 120, 40);
        s.Apply(RampOp(ramp, "-20,150", "40"));
        CheckHeights(s, before, ramp, -20, 150, "axis-aligned");
        Assert.Equal("ramp:2000,2600:2400,2600:120:-20,150:40:1", s.UndoDescription, "one undo step, named by its operation");
    }

    [Test]
    public static void ARotatedRampRunsAlongItsOwnAxis()
    {
        var s = Fresh("ramp-rotated");
        var before = (float[])s.Heights.Heights.Clone();
        double a = 30 * Math.PI / 180;
        var ramp = new Ramp(3000, 3000, 3000 + 400 * Math.Cos(a), 3000 + 400 * Math.Sin(a), 100, 48);
        s.Apply(RampOp(ramp, "10,90", "48"));
        CheckHeights(s, before, ramp, 10, 90, "30°");
        double last = double.MinValue;
        for (double u = 0; u <= ramp.Length; u += 20)
        {
            var (x, z) = ramp.World(u, 0);
            double h = s.Heights.HeightAt(x, z);
            Assert.True(h >= last - 1e-3, $"rises along its axis ({h:0.##} at u = {u})");
            last = h;
        }
        Assert.True(Math.Abs(ramp.SlopeDegrees(10, 90) - Math.Atan2(80, 400) * 180 / Math.PI) < 1e-9, "slope in degrees");
    }

    [Test]
    public static void AutoMeetsTheGroundAtBothEnds()
    {
        var s = Fresh("ramp-auto");
        var ramp = new Ramp(1500, 1500, 1500, 1850, 140, 0);
        var (g1, g2) = s.RampEndHeights(ramp);
        double edge = s.RampAutoEdge(ramp, g1, g2);
        string result = s.Apply(RampOp(ramp, "auto,auto"));
        Report.Info(result);
        Assert.True(result.Contains(string.Create(CultureInfo.InvariantCulture, $"at {g1:0.#} to"), StringComparison.Ordinal), "the result gives the heights it used");
        Assert.True(result.Contains(string.Create(CultureInfo.InvariantCulture, $"edge {edge:0}"), StringComparison.Ordinal), $"and the auto edge ({edge})");
        // Just inside each end (where every vertex sampled is on the ramp), the slope is the line between those heights.
        foreach (double u in new[] { 12.0, ramp.Length - 12 })
        {
            var (x, z) = ramp.World(u, 0);
            double expected = g1 + (g2 - g1) * u / ramp.Length;
            Assert.True(Math.Abs(s.Heights.HeightAt(x, z) - expected) < 0.01,
                $"at {u:0} along, the ramp is on the line between the ground's heights at its ends ({s.Heights.HeightAt(x, z):0.##} vs {expected:0.##})");
        }
        Assert.True(edge >= MapEditSession.MinAutoEdge && edge <= MapEditSession.MaxRampEdge, "auto edge within its limits");
    }

    [Test]
    public static void TheAutoEdgeGrowsWithTheCut()
    {
        var s = Fresh("ramp-edge");
        var ramp = new Ramp(2000, 2600, 2400, 2600, 120, 0);
        double ground = s.Heights.HeightAt(2200, 2600);
        double shallow = s.RampAutoEdge(ramp, ground, ground + 5), deep = s.RampAutoEdge(ramp, ground + 200, ground + 300);
        Assert.True(deep > shallow && deep >= 300, $"a ramp far above the ground gets a wider edge ({shallow} vs {deep})");
    }

    static (double R, double G, double B) Mean(RgbaImage image, Ramp ramp, double u0, double u1, double perUnit)
    {
        double r = 0, g = 0, b = 0;
        int n = 0;
        for (double u = u0; u <= u1; u += 2)
            for (double v = -ramp.Width / 2 + 4; v <= ramp.Width / 2 - 4; v += 2)
            {
                var (x, z) = ramp.World(u, v);
                var p = image.GetPixel((int)(x * perUnit), (int)(z * perUnit));
                (r, g, b, n) = (r + p.R, g + p.G, b + p.B, n + 1);
            }
        return (r / n, g / n, b / n);
    }

    static double Distance((double R, double G, double B) a, (double R, double G, double B) b) =>
        Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    /// <summary>
    /// The texture along the ramp comes from the ground beyond its ends: each end of the ramp looks like the ground
    /// beyond it, the fill fades out just past the sides (the edge's slopes keep their own texture), and nothing
    /// beyond that changes.
    /// </summary>
    [Test]
    public static void TheTextureIsFilledFromBeyondTheEnds()
    {
        var s = Fresh("ramp-texture");
        var ramp = new Ramp(4300, 3000, 4300, 3500, 120, 32);
        var before = s.TextureImage.Crop(s.TextureImage.Bounds);
        double perUnit = before.Width / s.MapWidth;
        var beyondFirst = Mean(before, ramp, -40, -4, perUnit);
        var beyondSecond = Mean(before, ramp, ramp.Length + 4, ramp.Length + 40, perUnit);
        s.Apply(RampOp(ramp, "auto,auto", "32"));
        var after = s.TextureImage;
        var firstEnd = Mean(after, ramp, 4, 40, perUnit);
        var secondEnd = Mean(after, ramp, ramp.Length - 40, ramp.Length - 4, perUnit);
        Report.Info($"beyond the first end {beyondFirst}, the ramp's first part {firstEnd}; beyond the second {beyondSecond}, its last part {secondEnd}");
        Assert.True(Distance(firstEnd, beyondFirst) < 8, "the ramp's first end looks like the ground beyond it");
        Assert.True(Distance(secondEnd, beyondSecond) < 8, "the ramp's second end looks like the ground beyond it");

        // DXT re-encodes whole 4x4 blocks, so pixels in a block the edge reaches may shift a little; beyond those, nothing.
        int changedBeyond = 0;
        double blockReach = 6 / perUnit;
        for (int y = 0; y < after.Height; y += 2)
            for (int x = 0; x < after.Width; x += 2)
                if (ramp.DistanceOutside((x + 0.5) / perUnit, (y + 0.5) / perUnit) >= Math.Min(ramp.Edge, MapEditSession.RampFillFeather) + blockReach &&
                    after.GetPixel(x, y) != before.GetPixel(x, y))
                    changedBeyond++;
        Assert.Equal(0, changedBeyond, "texture pixels beyond the fill's feather (and its blocks) that changed");
    }

    [Test]
    public static void WithoutFillOnlyTheHeightsChange()
    {
        var s = Fresh("ramp-nofill");
        string Hash(byte[] b) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b));
        string tiles = Hash(s.Texture.TileFiles[0].ToBytes()), splat = Hash(s.ResourceBytes(MapEditSession.SplatKey)), minimap = Hash(s.Smf.Minimap);
        s.Apply(RampOp(new Ramp(4300, 3000, 4300, 3500, 120, 32), "auto,auto", "32", fill: 0));
        Assert.True(s.Heights.HasChanges, "the heights changed");
        Assert.Equal(tiles, Hash(s.Texture.TileFiles[0].ToBytes()), "tiles unchanged");
        Assert.Equal(splat, Hash(s.ResourceBytes(MapEditSession.SplatKey)), "splat map unchanged");
        Assert.Equal(minimap, Hash(s.Smf.Minimap), "minimap unchanged");
    }

    [Test]
    public static void TheSplatMapIsFilledToo()
    {
        var s = Fresh("ramp-splat");
        var ramp = new Ramp(4300, 3000, 4300, 3500, 120, 32);
        var (w, h) = s.SplatSize;
        var before = new RgbaImage(w, h);
        s.ReadSplat(before, before.Bounds);
        double perUnit = w / s.MapWidth;
        var beyond = Mean(before, ramp, -40, -4, perUnit);
        s.Apply(RampOp(ramp, "auto,auto", "32"));
        var after = new RgbaImage(w, h);
        s.ReadSplat(after, after.Bounds);
        var first = Mean(after, ramp, 4, 40, perUnit);
        Assert.True(Distance(first, beyond) < 12, $"the splat weights at the ramp's first end match the ground beyond it ({first} vs {beyond})");
        Assert.True(!after.Pixels.AsSpan().SequenceEqual(before.Pixels), "the splat map changed");
    }

    /// <summary>
    /// Rough sides bend the blend, never the bounds: the surface stays exact, nothing beyond the edge moves, every edge
    /// vertex stays between the old ground and the ramp; the sides really are rougher, and the ends stay smooth.
    /// </summary>
    [Test]
    public static void RoughSidesStayInTheirBand()
    {
        var smooth = Fresh("ramp-smooth");
        var rough = Fresh("ramp-rough");
        var before = (float[])rough.Heights.Heights.Clone();
        var ramp = new Ramp(2000, 2600, 2500, 2600, 120, 120);
        smooth.Apply(RampOp(ramp, "-20,150", "120", fill: 0));
        rough.Apply(Op($"ramp:{ramp.X1},{ramp.Z1}:{ramp.X2},{ramp.Z2}:{ramp.Width}:-20,150:120:0:1"));
        CheckHeights(rough, before, ramp, -20, 150, "roughness 1");
        int sides = 0, roughened = 0, ends = 0, endsChanged = 0;
        var h = rough.Heights;
        for (int z = 0; z < h.Height; z++)
            for (int x = 0; x < h.Width; x++)
            {
                double wx = x * HeightMap.Spacing, wz = z * HeightMap.Spacing;
                var (u, v) = ramp.Local(wx, wz);
                double d = ramp.DistanceOutside(wx, wz);
                if (d <= 0 || d >= ramp.Edge)
                    continue;
                int i = z * h.Width + x;
                if (u > ramp.Edge && u < ramp.Length - ramp.Edge && Math.Abs(v) > ramp.Width / 2)
                {
                    sides++;
                    roughened += Math.Abs(h.Heights[i] - smooth.Heights.Heights[i]) > 1 ? 1 : 0;
                }
                else if ((u < 0 || u > ramp.Length) && Math.Abs(v) < ramp.Width / 2)
                {
                    ends++;
                    endsChanged += h.Heights[i] != smooth.Heights.Heights[i] ? 1 : 0;
                }
            }
        Assert.True(roughened > sides / 3, $"{roughened} of {sides} side vertices differ from the smooth ramp by over a unit");
        Assert.True(ends > 50 && endsChanged == 0, $"beyond the ends ({ends} vertices) the rough ramp is exactly the smooth one");
    }

    /// <summary>A ramp off the north-west plateau's east rim, from its flat top down into the acid.</summary>
    static readonly Ramp RimRamp = new(1380, 900, 2180, 900, 200, 0);

    /// <summary>
    /// Rock to dirt takes its rock and dirt from the map: above water, clear of the ramp, rock steep and dirt gentle
    /// (over 64 units); the upper ramp takes the rock's colour, the foot the ground's beyond it; nothing past the edge
    /// changes.
    /// </summary>
    [Test]
    public static void RockToDirtTakesTheMapsOwnColours()
    {
        var s = Fresh("ramp-rock");
        var (g1, g2) = s.RampEndHeights(RimRamp);
        var ramp = RimRamp with { Edge = s.RampAutoEdge(RimRamp, g1, g2) };
        var (rock, dirt) = s.RampMaterialSources(ramp, g1, g2);
        Assert.True(rock != null && dirt != null, "rock and dirt are found near the ramp");
        double Steepness((double X, double Z) p)
        {
            double gx = (s.Heights.HeightAt(p.X + 32, p.Z) - s.Heights.HeightAt(p.X - 32, p.Z)) / 64;
            double gz = (s.Heights.HeightAt(p.X, p.Z + 32) - s.Heights.HeightAt(p.X, p.Z - 32)) / 64;
            return Math.Atan(Math.Sqrt(gx * gx + gz * gz)) * 180 / Math.PI;
        }
        foreach (var (name, p, lo, hi) in new[] { ("rock", rock!.Value, MapEditSession.RockDegrees, 90.0), ("dirt", dirt!.Value, MapEditSession.DirtMinDegrees, MapEditSession.DirtMaxDegrees) })
        {
            double deg = Steepness(p);
            Assert.True(deg >= lo && deg <= hi && s.Heights.HeightAt(p.X, p.Z) > 0 && ramp.DistanceOutside(p.X, p.Z) >= ramp.Edge + 32,
                $"{name} at ({p.X:0}, {p.Z:0}): {deg:0.#} degrees over 64 units, above water, clear of the ramp");
        }

        var before = s.TextureImage.Crop(s.TextureImage.Bounds);
        double perUnit = before.Width / s.MapWidth;
        (double R, double G, double B) MeanAround((double X, double Z) p, RgbaImage img)
        {
            double r = 0, g = 0, b = 0;
            int n = 0;
            for (double dz = -32; dz < 32; dz += 4)
                for (double dx = -32; dx < 32; dx += 4)
                {
                    var c = img.GetPixel((int)((p.X + dx) * perUnit), (int)((p.Z + dz) * perUnit));
                    (r, g, b, n) = (r + c.R, g + c.G, b + c.B, n + 1);
                }
            return (r / n, g / n, b / n);
        }
        var rockColour = MeanAround(rock.Value, before);
        var footGround = Mean(before, ramp, ramp.Length + 8, ramp.Length + 60, perUnit);
        string result = s.Apply(Op($"ramp:{ramp.X1},{ramp.Z1}:{ramp.X2},{ramp.Z2}:{ramp.Width}:auto,auto:auto:2:0.5:auto:auto"));
        Report.Info(result);
        Assert.True(result.Contains("textured rock to dirt", StringComparison.Ordinal) &&
                    result.Contains(Op($"rock from ({rock.Value.X:0}, {rock.Value.Z:0})"), StringComparison.Ordinal),
            "the result says where the rock came from");
        var after = s.TextureImage;
        var upper = Mean(after, ramp, ramp.Length * 0.12, ramp.Length * 0.25, perUnit); // the top is the first end here
        var foot = Mean(after, ramp, ramp.Length - 30, ramp.Length - 4, perUnit);
        Report.Info($"rock {rockColour}, upper ramp {upper}; ground beyond the foot {footGround}, foot of the ramp {foot}");
        Assert.True(Distance(upper, rockColour) < Distance(upper, footGround), "the upper ramp is nearer the rock's colour than the foot ground's");
        Assert.True(Distance(foot, footGround) < Distance(foot, rockColour), "the foot is nearer the ground beyond it than the rock");

        int changedBeyond = 0;
        for (int y = 0; y < after.Height; y += 2)
            for (int x = 0; x < after.Width; x += 2)
                if (ramp.DistanceOutside((x + 0.5) / perUnit, (y + 0.5) / perUnit) >= ramp.Edge + 6 / perUnit && after.GetPixel(x, y) != before.GetPixel(x, y))
                    changedBeyond++;
        Assert.Equal(0, changedBeyond, "texture pixels beyond the edge (and its blocks) that changed");
    }

    /// <summary>The same rock-to-dirt ramp twice comes out byte for byte the same (the fill runs in parallel).</summary>
    [Test]
    public static void TheSameRampGivesTheSameBytes()
    {
        string Hash(byte[] b) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b));
        string State(MapEditSession s) => Hash(s.Texture.TileFiles[0].ToBytes()) + Hash(s.ResourceBytes(MapEditSession.SplatKey)) +
                                          Hash(s.Heights.Heights.SelectMany(BitConverter.GetBytes).ToArray()) + Hash(s.Smf.Minimap);
        const string op = "ramp:1380,900:2180,900:200:auto,auto:auto:2:0.6:auto:auto";
        var a = Fresh("ramp-same-a");
        var b = Fresh("ramp-same-b");
        a.Apply(op);
        b.Apply(op);
        Assert.Equal(State(a), State(b), "texture, splat map, heights and minimap");
    }

    /// <summary>
    /// Straight ramps come out byte for byte as they did before ramps could bend (hashes taken from the code of
    /// 2026-10-15, before paths): journals of straight ramps replay exactly.
    /// </summary>
    [Test]
    public static void StraightRampsComeOutAsBefore()
    {
        string Hash(byte[] b) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b))[..16];
        string State(MapEditSession s) => Hash(s.Texture.TileFiles[0].ToBytes()) + " " + Hash(s.ResourceBytes(MapEditSession.SplatKey)) + " " +
                                          Hash(s.Heights.Heights.SelectMany(BitConverter.GetBytes).ToArray()) + " " + Hash(s.Smf.Minimap);
        (string Op, string Expected)[] cases =
        [
            ("ramp:3000,3000:3346.4,3200:100:10,90:48:1:0.4", "C34047A2F43E204A 8776A1F70F1C2049 AD460DC384D75732 AA1A3B363A6127D6"),
            ("ramp:1380,900:2180,900:200:auto,auto:auto:2:0.6:auto:auto", "85244CE078BA3E71 C8D1EABE4F4AA462 F6F1CE51A90780AE 9A5097D473AB72B8"),
        ];
        foreach (var (op, expected) in cases)
        {
            var s = Fresh("ramp-straight-" + Math.Abs(op.GetHashCode()));
            s.Apply(op);
            string state = State(s);
            Report.Info($"{op}: {state}");
            Assert.Equal(expected, state, $"{op}: texture, splat map, heights and minimap");
        }
    }

    [Test]
    public static void RockToDirtFallsBackWhereItCant()
    {
        var level = Fresh("ramp-level");
        string flat = level.Apply("ramp:1000,1000:1300,1000:120:50,52:40:2:0.3:auto:auto");
        Assert.True(flat.Contains("level, so no rock to dirt: filled from the ends", StringComparison.Ordinal), flat);

        var path = SyntheticMap.Build(TestData.NewWorkDir("ramp-norock"));
        var synthetic = MapEditSession.Open(MapPackage.Open(path, TestData.NewWorkDir("ramp-norock-open")));
        string noRock = synthetic.Apply("ramp:300,100:300,400:90:-30,120:40:2:0.5:auto:auto");
        Assert.True(noRock.Contains("no cliff near it to take rock from: filled from the ends", StringComparison.Ordinal), noRock);

        var picked = Fresh("ramp-picked");
        string chosen = picked.Apply("ramp:1380,900:2180,900:200:auto,auto:auto:2:0.5:1200,700:2400,1300");
        Assert.True(chosen.Contains("rock from (1200, 700)", StringComparison.Ordinal) && chosen.Contains("dirt from (2400, 1300)", StringComparison.Ordinal),
            $"picked sources are used as given: {chosen}");
    }

    [Test]
    public static void BadRampsAreRefused()
    {
        var s = Fresh("ramp-bad");
        string[] bad =
        [
            "ramp:1000,1000:1000,1000:100:0,50",          // no length
            "ramp:1000,1000:1400,1000:4:0,50",            // too narrow
            "ramp:1000,1000:7000,1000:100:0,50",          // an end off the map
            "ramp:1000,1000:1400,1000:100:0,50:900",      // edge too wide
            "ramp:1000,1000:1400,1000:100:0,50:40:3",     // no such look
            "ramp:1000,1000:1400,1000:100:0,50:40:1:1.5", // roughness over 1
            "ramp:1000,1000:1400,1000:100:0",             // one height
            "ramp:1000,1000:1400,1000:100",               // no heights
        ];
        foreach (var op in bad)
            Assert.Throws<InvalidOperationException>(() => s.Apply(op), op);
        Assert.True(!s.CanUndo && !s.Heights.HasChanges, "nothing was recorded or changed");
    }

    [Test]
    public static void ARampOnAMapWithoutASplatMap()
    {
        var path = SyntheticMap.Build(TestData.NewWorkDir("ramp-synthetic"));
        var s = MapEditSession.Open(MapPackage.Open(path, TestData.NewWorkDir("ramp-synthetic-open")));
        var before = (float[])s.Heights.Heights.Clone();
        var ramp = new Ramp(300, 100, 300, 400, 90, 40);
        string result = s.Apply(RampOp(ramp, "-30,120", "40"));
        Assert.True(!result.Contains("splat", StringComparison.OrdinalIgnoreCase), $"no splat map to fill: {result}");
        CheckHeights(s, before, ramp, -30, 120, "synthetic map");
        s.Undo();
        Assert.True(s.Heights.Heights.AsSpan().SequenceEqual(before), "undo puts the heights back");
    }
}
