using System.Diagnostics;
using System.Globalization;
using MapCore.Editing;
using MapCore.Heights;

namespace MapCore.Tests;

/// <summary>Bending ramps: a ramp through points, its centre line straight runs with each bend rounded (<see cref="RampPath"/>).</summary>
static class RampPathTests
{
    static MapEditSession Synthetic(string name) =>
        MapEditSession.Open(MapPackage.Open(SyntheticMap.Build(TestData.NewWorkDir(name)), TestData.NewWorkDir(name + "-open")));

    static MapEditSession Quarry(string name) =>
        MapEditSession.Open(MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir(name)));

    /// <summary>
    /// A hairpin on the synthetic map, by two corners: up one leg, round a half circle of radius 120 (each corner takes
    /// half the 240 between them), down the other leg, 240 apart.
    /// </summary>
    static readonly (double X, double Z)[] Hairpin = [(300, 900), (300, 330), (540, 330), (540, 900)];

    /// <summary>
    /// A switchback by two corners 150 apart: round a half circle of radius 75 about (1070, 400), its legs 150 apart, so a
    /// ramp 120 wide leaves a gap of only 30 between them, less than the window u is smoothed over.
    /// </summary>
    static readonly (double X, double Z)[] Switchback = [(995, 900), (995, 325), (1145, 325), (1145, 900)];

    /// <summary>World and Local undo each other on and just beyond a bending ramp, by the grid and without it alike.</summary>
    [Test]
    public static void ABendingRampMapsPointsBothWays()
    {
        var ramp = Ramp.Through(Hairpin, 80, 120);
        var random = new Random(5);
        double worst = 0;
        for (int k = 0; k < 4000; k++)
        {
            double u = -60 + random.NextDouble() * (ramp.Length + 120), v = (random.NextDouble() - 0.5) * ramp.Width;
            var (x, z) = ramp.World(u, v);
            var (u2, v2) = ramp.Local(x, z);
            worst = Math.Max(worst, Math.Max(Math.Abs(u2 - u), Math.Abs(v2 - v)));
        }
        Assert.True(worst < 1e-6, $"Local(World(u, v)) is (u, v) (worst {worst:g3})");
        Assert.True(Math.Abs(ramp.Length - (450 + 450 + Math.PI * 120)) < 0.1, $"its length runs along the line, legs and half circle ({ramp.Length:0.###}, {450 + 450 + Math.PI * 120:0.###})");
        var (radius, bx, bz) = ramp.Path!.Tightest();
        Assert.True(Math.Abs(radius - 120) < 1e-9 && Math.Abs(bx - 420 + 120 * Math.Sin(Math.PI / 4)) < 1e-6 && Math.Abs(bz - 450 + 120 * Math.Cos(Math.PI / 4)) < 1e-6,
            $"each corner takes half the run between them: a quarter circle of radius 120 (radius {radius}, middle at ({bx:0.#}, {bz:0.#}))");

        // Far from the curve, Local measures every segment instead of the grid: the same answers either way.
        var near = ramp with { Edge = 400 };
        double differs = 0;
        foreach (var (x, z) in new[] { (420.0, 700.0), (300.0, 1100.0), (700.0, 300.0), (150.0, 450.0), (420.0, 120.0) })
        {
            var (ua, va) = ramp.Local(x, z);
            var (ub, vb) = near.Local(x, z);
            differs = Math.Max(differs, Math.Max(Math.Abs(ua - ub), Math.Abs(va - vb)));
        }
        Assert.True(differs < 1e-6, $"beyond the grid's reach Local agrees with the grid's answer (worst {differs:g3})");
        Assert.True(double.IsPositiveInfinity(ramp.DistanceOutside(1500, 900)) && ramp.Weight(1500, 900) == 0, "far beyond the edge isn't measured, and weighs nothing");
        Assert.True(ramp.Bends.Count == 2 && ramp.Points.Count == 4 && (ramp.X1, ramp.Z1, ramp.X2, ramp.Z2) == (300, 900, 540, 900), "its ends and bends");
    }

    /// <summary>
    /// Local against the true geometry of the switchback, a radius-75 half circle between straight legs: on the ramp
    /// (|v| up to 60, near the bend's centre) u and v are the true distance along the line and from it, to within the
    /// samples' straightness, with no jumps between samples.
    /// </summary>
    [Test]
    public static void ASwitchbackIsMeasuredTrue()
    {
        var ramp = Ramp.Through(Switchback, 120, 96);
        Assert.True(MapEditSession.RampShapeProblem(ramp) == null, $"it can be built ({MapEditSession.RampShapeProblem(ramp)})");
        double legs = 500, arc = 75 * Math.PI;
        Assert.True(Math.Abs(ramp.Length - (2 * legs + arc)) < 0.05, $"its length ({ramp.Length:0.###}, {2 * legs + arc:0.###})");
        double worst = 0;
        int points = 0;
        for (double x = 930; x <= 1210; x += 1.7)
            for (double z = 300; z <= 900; z += 1.3)
            {
                double u, v;
                if (z >= 400)
                {
                    if (Math.Abs(x - 995) <= 60)
                        (u, v) = (900 - z, x - 995);
                    else if (Math.Abs(x - 1145) <= 60)
                        (u, v) = (legs + arc + z - 400, 1145 - x);
                    else
                        continue;
                }
                else
                {
                    double r = Math.Sqrt((x - 1070) * (x - 1070) + (z - 400) * (z - 400));
                    if (r < 15 || r > 135)
                        continue;
                    (u, v) = (legs + 75 * (Math.Atan2(z - 400, x - 1070) + Math.PI), 75 - r);
                }
                var (lu, lv) = ramp.Local(x, z);
                worst = Math.Max(worst, Math.Max(Math.Abs(lu - u), Math.Abs(lv - v)));
                points++;
            }
        Assert.True(worst < 0.05, $"over {points} points on the ramp, Local is the true (u, v) (worst {worst:0.0000})");
    }

    /// <summary>
    /// On a bending ramp every stored height is the straight line from h1 to h2 by the distance along the curve (exactly,
    /// by its nearest point: u isn't smoothed on the ramp itself), even where the legs of a switchback are closer than
    /// the smoothing window; in the edge, heights lie between the old ground and the surface; beyond it nothing moves.
    /// </summary>
    [Test]
    public static void ABendingRampRisesEvenlyAlongItsCurve()
    {
        foreach (var (name, points, width) in new[] { ("hairpin", Hairpin, 160.0), ("switchback", Switchback, 120.0) })
        {
            var s = Synthetic("ramp-path-" + name);
            var before = (float[])s.Heights.Heights.Clone();
            var ramp = Ramp.Through(points, width, 96);
            Assert.True(MapEditSession.RampShapeProblem(ramp) == null, $"{name}: can be built ({MapEditSession.RampShapeProblem(ramp)})");
            string result = s.BuildRamp(ramp, 20, 200, RampLook.None);
            Report.Info(result);
            var heights = s.Heights;
            int on = 0, off = 0, edge = 0, outOfRange = 0, moved = 0;
            double worst = 0;
            for (int z = 0; z < heights.Height; z++)
                for (int x = 0; x < heights.Width; x++)
                {
                    double wx = x * HeightMap.Spacing, wz = z * HeightMap.Spacing;
                    int i = z * heights.Width + x;
                    double d = ramp.DistanceOutside(wx, wz);
                    if (d >= ramp.Edge)
                    {
                        moved += heights.Heights[i] != before[i] ? 1 : 0;
                        continue;
                    }
                    if (d > 0)
                    {
                        edge++;
                        double target = ramp.SurfaceAt(wx, wz, 20, 200);
                        if (heights.Heights[i] < Math.Min(before[i], target) - 1e-3 || heights.Heights[i] > Math.Max(before[i], target) + 1e-3)
                            outOfRange++;
                        continue;
                    }
                    on++;
                    double expected = 20 + 180 * Math.Clamp(ramp.Local(wx, wz).U / ramp.Length, 0, 1);
                    double error = Math.Abs(heights.Heights[i] - expected);
                    worst = Math.Max(worst, error);
                    off += error >= 0.01 ? 1 : 0;
                }
            Assert.True(on > 1000 && off == 0, $"{name}: all {on} vertices on the ramp rise evenly along its curve ({off} off, worst {worst:0.0000})");
            Assert.True(edge > 0 && outOfRange == 0, $"{name}: all {edge} edge vertices lie between the old ground and the ramp ({outOfRange} outside)");
            Assert.Equal(0, moved, $"{name}: vertices beyond the edge that moved");
            Assert.True(result.Contains(" through 2 bends to ", StringComparison.Ordinal),
                $"{name}: the result counts its bends");
            Assert.True(result.Contains("its tightest bend", StringComparison.Ordinal), $"{name}: and gives its tightest bend");
        }
    }

    /// <summary>
    /// Between the legs of a hairpin, beyond their sides, the nearest point jumps from one leg to the other: by it, the
    /// blend's surface would step there by the climb round the bend. Smoothed, it doesn't: sampled at half the spacing,
    /// the largest difference between neighbouring samples halves, as it does for anything without a step (and doesn't,
    /// by the nearest point alone, which this checks too, so the measure can tell).
    /// </summary>
    [Test]
    public static void TheBlendBetweenTheLegsHasNoStep()
    {
        var ramp = Ramp.Through(Hairpin, 80, 200);
        double Smoothed(double x, double z) => ramp.SurfaceAt(x, z, 0, 400);
        double Nearest(double x, double z) => 400 * Math.Clamp(ramp.Local(x, z).U / ramp.Length, 0, 1);
        // Across between the legs, and down the middle from inside the bend.
        (double X, double Z, double Dx, double Dz, double Length)[] lines =
            [(250, 470, 1, 0, 340), (250, 600, 1, 0, 340), (250, 800, 1, 0, 340), (420, 340, 0, 1, 660)];
        double Largest(Func<double, double, double> surface, double step)
        {
            double largest = 0;
            foreach (var (x0, z0, dx, dz, length) in lines)
            {
                double? last = null;
                for (double t = 0; t <= length; t += step)
                {
                    double x = x0 + t * dx, z = z0 + t * dz;
                    if (ramp.Weight(x, z) <= 0)
                    {
                        last = null;
                        continue;
                    }
                    double h = surface(x, z);
                    if (last is double l)
                        largest = Math.Max(largest, Math.Abs(h - l));
                    last = h;
                }
            }
            return largest;
        }
        double smooth2 = Largest(Smoothed, 2), smooth1 = Largest(Smoothed, 1), exact2 = Largest(Nearest, 2), exact1 = Largest(Nearest, 1);
        Report.Info($"largest step between neighbours: smoothed {smooth2:0.##} at 2 units, {smooth1:0.##} at 1; by the nearest point {exact2:0.##}, {exact1:0.##}");
        Assert.True(exact1 > 0.9 * exact2 && exact2 > 50, "by the nearest point alone, the surface steps between the legs (the measure sees it)");
        Assert.True(smooth1 < 0.7 * smooth2, "smoothed, it doesn't step: the largest difference halves with the spacing");
    }

    [Test]
    public static void BadBendingRampsAreRefused()
    {
        var s = Synthetic("ramp-path-bad");
        (string Op, string Says)[] bad =
        [
            ("ramp:350,800;400,300:450,800:200:0,50", "bends too tightly"),
            ("ramp:300,300;600,300:300,300:100:0,50", "turns straight back on itself"),
            ("ramp:300,300;900,300;900,700;300,700:300,390:120:0,50", "come within"),
            ("ramp:300,300;3000,500:600,900:100:0,50", "bend (3000, 500) is off the map"),
            ("ramp:300,300;304,300:600,300:100:0,50", "at least 8 units apart"),
        ];
        foreach (var (op, says) in bad)
        {
            var e = Assert.Throws<InvalidOperationException>(() => s.Apply(op), op);
            Assert.True(e.Message.Contains(says, StringComparison.Ordinal), $"{op}: {e.Message}");
        }
        Assert.True(!s.CanUndo && !s.Heights.HasChanges, "nothing was recorded or changed");
    }

    /// <summary>
    /// The edge's outer line, traced where the blend ends: round a bend tighter than the edge reaches from the line (where
    /// the outline grown by the edge loops), and for a ramp whose ends' edges overlap, each line closed, on the edge, and
    /// going round once.
    /// </summary>
    [Test]
    public static void TheEdgeIsTracedWithoutLoops()
    {
        foreach (var (name, ramp) in new[]
                 {
                     ("a bend tighter than the edge", Ramp.Through([(2600, 2350), (2630, 2830), (2300, 2830)], 140, 306)),
                     ("round the mesa", Ramp.Through([(2210, 2110), (2600, 2350), (2630, 2830), (2360, 3080), (2160, 2790)], 140, 306)),
                 })
        {
            const double step = 12;
            var lines = ramp.EdgeContour(step);
            double off = 0;
            var turnings = new List<double>();
            foreach (var line in lines)
            {
                double turning = 0;
                for (int i = 0; i < line.Length; i++)
                {
                    var (a, b, c) = (line[(i + line.Length - 1) % line.Length], line[i], line[(i + 1) % line.Length]);
                    turning += Math.IEEERemainder(Math.Atan2(c.Z - b.Z, c.X - b.X) - Math.Atan2(b.Z - a.Z, b.X - a.X), 2 * Math.PI);
                    off = Math.Max(off, Math.Abs(ramp.DistanceOutside(b.X, b.Z) - ramp.Edge));
                }
                turnings.Add(Math.Abs(turning) * 180 / Math.PI);
            }
            Assert.True(lines.Count >= 1 && turnings.All(t => Math.Abs(t - 360) < 6) && off < step,
                $"{name}: {lines.Count} line(s), each turning {string.Join(", ", turnings.Select(t => t.ToString("0")))}° in all, within {off:0.#} of the edge");
        }
    }
    /// <summary>A bending ramp's operation carries its bends after its first end; it undoes in one step and replays the same.</summary>
    [Test]
    public static void ABendingRampIsOneOperation()
    {
        var s = Synthetic("ramp-path-op");
        var before = (float[])s.Heights.Heights.Clone();
        var ramp = Ramp.Through(Hairpin, 100.5, 48);
        s.BuildRamp(ramp, 10, 90);
        Assert.Equal("ramp:300,900;300,330;540,330:540,900:100.5:10,90:48:1", s.UndoDescription, "one undo step, named by its operation");
        var built = (float[])s.Heights.Heights.Clone();
        s.Undo();
        Assert.True(s.Heights.Heights.AsSpan().SequenceEqual(before), "undo puts the heights back");
        s.Redo();
        Assert.True(s.Heights.Heights.AsSpan().SequenceEqual(built), "redo builds it again the same");
    }

    /// <summary>
    /// A ramp wrapped three quarters round the south mesa of Acidic Quarry, so the run on beyond its top end points back
    /// over the mesa's north slope towards its start: no neighbouring vertices end up more than 20 steeper than they were.
    /// (Each point used to take its place from the nearest part of the line, run on included: points the end's run on
    /// took, far past its edge, sat beside points the start held, and the ground stepped between them like a comb.)
    /// </summary>
    [Test]
    public static void ARunOnNeverTakesPointsFromANearerPart()
    {
        var s = Quarry("ramp-path-mesa");
        var before = (float[])s.Heights.Heights.Clone();
        const string op = "ramp:2210,2110;2600,2350;2630,2830;2360,3080:2160,2790:140:auto,auto:200:0";
        Report.Info(s.Apply(op));
        var ramp = Ramp.Through([(2210, 2110), (2600, 2350), (2630, 2830), (2360, 3080), (2160, 2790)], 140, 200);
        var h = s.Heights;
        var jumps = new List<(double Jump, int X, int Z, double Was)>();
        for (int z = 0; z < h.Height - 1; z++)
            for (int x = 0; x < h.Width - 1; x++)
            {
                int i = z * h.Width + x;
                foreach (int j in new[] { i + 1, i + h.Width })
                {
                    double now = Math.Abs(h.Heights[i] - h.Heights[j]), was = Math.Abs(before[i] - before[j]);
                    if (now > was + 20)
                        jumps.Add((now, x, z, was));
                }
            }
        foreach (var (jump, x, z, was) in jumps.OrderByDescending(j => j.Jump).Take(10))
        {
            double wx = x * 8, wz = z * 8;
            var (u, v) = ramp.Local(wx, wz);
            Report.Info($"step {jump:0.#} (was {was:0.#}) at ({wx}, {wz}): u {u:0.#}, v {v:0.#}, {ramp.DistanceOutside(wx, wz):0.#} outside, weight {ramp.Weight(wx, wz):0.###}");
        }
        Assert.Equal(0, jumps.Count, "neighbouring vertices more than 20 steeper than before");
    }

    /// <summary>
    /// A long ramp curving round a cliff on Acidic Quarry, rock to dirt: built in a few seconds, the same twice, and
    /// nothing past its edge changes.
    /// </summary>
    [Test]
    public static void ALongBendingRampIsQuickAndExact()
    {
        string Hash(byte[] b) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b));
        string State(MapEditSession s) => Hash(s.Texture.TileFiles[0].ToBytes()) + Hash(s.ResourceBytes(MapEditSession.SplatKey)) +
                                          Hash(s.Heights.Heights.SelectMany(BitConverter.GetBytes).ToArray()) + Hash(s.Smf.Minimap);
        const string op = "ramp:1380,900;1800,980;2080,1250:2180,1700:160:auto,auto:auto:2:0.5:auto:auto";
        var a = Quarry("ramp-path-long-a");
        var before = a.TextureImage.Crop(a.TextureImage.Bounds);
        var timer = Stopwatch.StartNew();
        string result = a.Apply(op);
        timer.Stop();
        Report.Info($"{result} ({timer.ElapsedMilliseconds} ms)");
        var b = Quarry("ramp-path-long-b");
        b.Apply(op);
        Assert.Equal(State(a), State(b), "texture, splat map, heights and minimap, twice");
        Assert.True(timer.ElapsedMilliseconds < 6000, $"built in {timer.ElapsedMilliseconds} ms");

        var first = op.Split(':');
        var points = first[1].Split(';').Append(first[2]).Select(p => p.Split(','))
            .Select(p => (double.Parse(p[0], CultureInfo.InvariantCulture), double.Parse(p[1], CultureInfo.InvariantCulture))).ToList();
        double edge = double.Parse(result[(result.IndexOf("edge ", StringComparison.Ordinal) + 5)..].Split(',', ':')[0], CultureInfo.InvariantCulture);
        var ramp = Ramp.Through(points, 160, edge);
        var after = a.TextureImage;
        double perUnit = after.Width / a.MapWidth;
        int changedBeyond = 0;
        for (int y = 0; y < after.Height; y += 2)
            for (int x = 0; x < after.Width; x += 2)
                if (ramp.DistanceOutside((x + 0.5) / perUnit, (y + 0.5) / perUnit) >= ramp.Edge + 6 / perUnit && after.GetPixel(x, y) != before.GetPixel(x, y))
                    changedBeyond++;
        Assert.Equal(0, changedBeyond, "texture pixels beyond the edge (and its blocks) that changed");
    }
}
