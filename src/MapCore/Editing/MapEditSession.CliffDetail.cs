using MapCore.Heights;
using MapCore.Movement;

namespace MapCore.Editing;

/// <summary>
/// Beautify's cliff detail: <c>beautify-heights:&lt;settings&gt;</c> (<see cref="CliffDetailSettings"/>), one undo step.
/// Cliffs get broken rock, ledges that follow the height and gullies running down them; open ground, if asked, a gentle
/// swell. Then it is checked as the game would see it, and taken back where it would change anything that matters:
/// <list type="bullet">
/// <item>Where every BAR unit can go (<see cref="AccessMap"/>): each 16-unit cell's slope stays on the same side of every
/// movement class's limit, and each square's highest corner on the same side of the water and of every depth limit,
/// staying a margin away from each (and what was already within the margin isn't touched), so storing the heights in
/// 16 bits can't tip one over.</item>
/// <item>Where buildings can go: ground flat enough for BAR's flattest buildings (solar and fusion, maxslope 10, 96
/// units across), its labs (15) and its metal extractors (30, 64 units) stays flat enough: the engine lets each
/// footprint square stand at most 40 × tan(maxslope) from the building's height (RecoilEngine UnitDef.cpp,
/// GameHelper.cpp), so a footprint's ground may span twice that.</item>
/// </list>
/// Where a change would alter one of these, it is halved there, again and again, and in the end taken out. Metal spots,
/// structures and start positions are left alone, and heights stay within the map's range. The same settings on the
/// same map give the same heights.
/// </summary>
public sealed partial class MapEditSession
{
    /// <summary>BAR's buildings by how flat they need the ground: maxslope (degrees) and footprint (8-unit squares across).</summary>
    static readonly (double MaxSlope, int Squares)[] BuildingFlatness = [(10, 12), (15, 12), (30, 8)];

    /// <summary>Roughens cliffs (one undo step): see the class summary.</summary>
    public string BeautifyHeights(CliffDetailSettings settings) => Apply("beautify-heights:" + settings.Encode());

    string BeautifyHeightsCore(string[] args)
    {
        Expect(args, 1, "beautify-heights");
        var settings = CliffDetailSettings.Decode(args[0]);
        if (settings.Problem is string problem)
            throw new InvalidOperationException($"Cliff detail can't run: {problem}.");
        var mirror = settings.Mirror is MirrorKind kind ? new MapMirror(kind, MapWidth, MapHeight) : (MapMirror?)null;
        var heights = Heights;
        int w = heights.Width, h = heights.Height;
        var before = (float[])heights.Heights.Clone();
        var delta = CliffDelta(before, w, h, settings, mirror);
        int wanted = delta.Count(d => d != 0);
        if (wanted == 0)
            return "cliff detail: no ground steep enough to change";

        var guard = new AccessGuard(before, w, h);
        var keep = new float[w * h];
        Array.Fill(keep, 1f);
        // Within the heights the map has: a value past them would widen the stored range on saving, and every height
        // would be stored again, each up to half a step off, which could tip ground that sits on a limit.
        float lowest = before.Min(), highest = before.Max();
        var candidate = new float[w * h];
        void Build()
        {
            for (int i = 0; i < candidate.Length; i++)
                candidate[i] = Math.Clamp(before[i] + keep[i] * delta[i], lowest, highest);
        }
        int rounds = 0;
        for (; ; rounds++)
        {
            Build();
            var wrong = guard.Violations(candidate);
            if (wrong.Count == 0)
                break;
            foreach (int v in wrong)
                keep[v] = rounds < 8 ? keep[v] * 0.5f : 0;
            if (rounds > 64)
                throw new InvalidOperationException("Cliff detail couldn't keep where units and buildings go; nothing was changed.");
        }
        int changed = heights.SetAll(candidate);
        int softened = keep.Count(k => k < 1 && k > 0), removed = keep.Count(k => k == 0);
        return $"cliff detail: {changed:N0} heights changed on cliffs{(settings.Ground > 0 ? " and open ground" : "")}; where units and buildings go is as it was " +
               $"({softened:N0} softened and {removed:N0} left as they were to keep it so, in {rounds + 1} passes)" + (mirror is MapMirror m ? $"; noise mirrored {m.Code}" : "");
    }

    /// <summary>The change cliff detail wants at each vertex, before any is taken back.</summary>
    float[] CliffDelta(float[] before, int w, int h, CliffDetailSettings s, MapMirror? mirror)
    {
        // The slope at each vertex as it is (not smoothed): a ramp's steep banks mustn't reach onto its surface.
        var slope = new float[w * h];
        Parallel.For(0, h, z =>
        {
            for (int x = 0; x < w; x++)
            {
                int x0 = Math.Max(0, x - 1), x1 = Math.Min(w - 1, x + 1), z0 = Math.Max(0, z - 1), z1 = Math.Min(h - 1, z + 1);
                double gx = (before[z * w + x1] - before[z * w + x0]) / ((x1 - x0) * HeightMap.Spacing);
                double gz = (before[z1 * w + x] - before[z0 * w + x]) / ((z1 - z0) * HeightMap.Spacing);
                slope[z * w + x] = (float)(Math.Atan(Math.Sqrt(gx * gx + gz * gz)) * 180 / Math.PI);
            }
        });
        var smooth = BoxBlur(before, w, h, 2); // the gullies run down the slope of the ground at large
        var protect = VertexProtection(w, h);
        var delta = new float[w * h];
        Parallel.For(0, h, z =>
        {
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                double free = 1 - protect[i];
                if (free <= 0)
                    continue;
                double cliff = Smooth(s.CliffFrom, s.CliffFrom + 15, slope[i]) * free;
                // The swell only on nearly level ground: slopes, ramps among them, keep their shape.
                double ground = s.Ground > 0 ? (1 - Smooth(4, 8, slope[i])) * free : 0;
                if (cliff <= 0 && ground <= 0)
                    continue;
                double wx = x * HeightMap.Spacing, wz = z * HeightMap.Spacing;
                var (nx, nz) = Canonical(mirror, wx, wz);
                bool mapped = mirror != null && (nx, nz) != (wx, wz);
                double d = 0;
                if (cliff > 0)
                {
                    // Broken rock: sharp-crested lumps.
                    double rock = 0.6 * (1 - 2 * Math.Abs(TerrainNoise.Sample(nx / 34 + 1.7, nz / 34 + 6.1))) +
                                  0.4 * (1 - 2 * Math.Abs(TerrainNoise.Sample(nx / 13 + 8.3, nz / 13 + 2.9))) - 0.35;
                    // Ledges: the height pulled towards steps, as beds of rock weather back.
                    double period = 24 + 10 * Fbm(nx / 300 + 4.1, nz / 300 + 7.3, 2);
                    double t = (before[i] + 18 * Fbm(nx / 150 + 2.2, nz / 150 + 5.5, 2)) / period;
                    double step = Math.Floor(t) + Smooth(0.35, 0.65, t - Math.Floor(t));
                    double ledge = Math.Clamp((step - t) * 2.5, -1, 1);
                    // Gullies: grooves along the way down (across it narrow, along it long).
                    double gx = (smooth[z * w + Math.Min(w - 1, x + 1)] - smooth[z * w + Math.Max(0, x - 1)]) / (2 * HeightMap.Spacing);
                    double gz = (smooth[Math.Min(h - 1, z + 1) * w + x] - smooth[Math.Max(0, z - 1) * w + x]) / (2 * HeightMap.Spacing);
                    double length = Math.Sqrt(gx * gx + gz * gz), gully = 0;
                    if (length > 1e-6)
                    {
                        var (dx, dz) = (gx / length, gz / length);
                        if (mapped)
                            (dx, dz) = mirror!.Value.MapVector(dx, dz); // read in the frame the noise is read in, so both halves match
                        double across = nx * -dz + nz * dx, along = nx * dx + nz * dz;
                        double n = TerrainNoise.Sample(across / 10 + 3.7, along / 70 + 9.2);
                        gully = -Math.Pow(1 - Math.Abs(n), 4) * 1.6 + 0.25;
                    }
                    d += cliff * s.Amount * (0.55 * rock + 0.9 * s.Strata * ledge + s.Gullies * gully);
                }
                if (ground > 0)
                    d += ground * s.Ground * Fbm(nx / 110 + 6.6, nz / 110 + 3.3, 3);
                delta[i] = (float)d;
            }
        });
        return delta;
    }

    /// <summary>How much to leave each vertex alone (1: leave it): round metal spots, under structures and their markers, and round start positions.</summary>
    float[] VertexProtection(int w, int h)
    {
        var protect = new float[w * h];
        void Stamp(double minX, double minZ, double maxX, double maxZ, Func<double, double, double> value)
        {
            int x0 = Math.Max(0, (int)(minX / HeightMap.Spacing)), x1 = Math.Min(w - 1, (int)Math.Ceiling(maxX / HeightMap.Spacing));
            int z0 = Math.Max(0, (int)(minZ / HeightMap.Spacing)), z1 = Math.Min(h - 1, (int)Math.Ceiling(maxZ / HeightMap.Spacing));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    protect[z * w + x] = Math.Max(protect[z * w + x], (float)value(x * HeightMap.Spacing, z * HeightMap.Spacing));
        }
        void Circle(double cx, double cz, double radius) => Stamp(cx - radius - ProtectFade, cz - radius - ProtectFade, cx + radius + ProtectFade, cz + radius + ProtectFade,
            (x, z) => 1 - Math.Clamp((Math.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)) - radius) / ProtectFade, 0, 1));
        foreach (var spot in Metal.FindSpots())
            Circle(spot.X, spot.Z, SpotProtectRadius);
        foreach (var start in Info.StartPositions)
            Circle(start.X, start.Z, 96);
        var config = Structures;
        foreach (var instance in config.Instances)
        {
            if (config.Kinds.TryGetValue(instance.Kind, out var kind))
                foreach (var part in MapCore.Structures.StructureLayout.Parts(kind, instance))
                    Stamp(part.X1 - 16 - ProtectFade, part.Z1 - 16 - ProtectFade, part.X2 + 16 + ProtectFade, part.Z2 + 16 + ProtectFade, (x, z) =>
                    {
                        double dx = Math.Max(0, Math.Max(part.X1 - 16 - x, x - part.X2 - 16)), dz = Math.Max(0, Math.Max(part.Z1 - 16 - z, z - part.Z2 - 16));
                        return 1 - Math.Clamp(Math.Sqrt(dx * dx + dz * dz) / ProtectFade, 0, 1);
                    });
            foreach (var (mx, mz) in instance.MarkerList)
                Circle(mx, mz, 24);
        }
        return protect;
    }

    /// <summary>
    /// What heights must keep, taken from the heights before: each slope cell's side of every movement class's slope
    /// limit, each square's side of the water and of every depth limit, and the flatness buildings need (see the class
    /// summary). Each with a margin: a value may not come nearer a limit than the margin unless it was already nearer.
    /// </summary>
    sealed class AccessGuard
    {
        const double SlopeMargin = 0.002, HeightMargin = 0.1, SpanMargin = 0.1;
        readonly float[] before;
        readonly int w, h, cellsX, cellsZ;
        readonly double[] slopeLimits, waterLimits;
        readonly double[] cellSlope;
        readonly (double Limit, int Squares, float[] Span)[] buildable;

        public AccessGuard(float[] before, int w, int h)
        {
            (this.before, this.w, this.h) = (before, w, h);
            (cellsX, cellsZ) = ((w - 1) / 2, (h - 1) / 2);
            var classes = AccessMap.DistinctClasses;
            slopeLimits = [.. classes.Select(c => c.MaxSlope).Where(s => s < 1).Distinct().Order()];
            waterLimits = [.. classes.Where(c => c.Kind != MoverKind.Hover && double.IsFinite(c.MaxWaterDepth)).Select(c => -c.MaxWaterDepth).Append(0).Distinct().Order()];
            cellSlope = CellSlopes(before);
            buildable = [.. BuildingFlatness.Select(b => (2 * 40 * Math.Tan(b.MaxSlope * Math.PI / 180), b.Squares, Spans(before, b.Squares)))];
        }

        double[] CellSlopes(float[] heights)
        {
            var slopes = new double[cellsX * cellsZ];
            Parallel.For(0, cellsZ, cz =>
            {
                for (int cx = 0; cx < cellsX; cx++)
                    slopes[cz * cellsX + cx] = TerrainSlope.Cell((x, z) => heights[z * w + x], cx, cz);
            });
            return slopes;
        }

        /// <summary>For each footprint (squares × squares, every 2 squares), how far its ground's highest corner is above its lowest.</summary>
        float[] Spans(float[] heights, int squares)
        {
            int bx = (w - 1 - squares) / 2 + 1, bz = (h - 1 - squares) / 2 + 1;
            var rowMax = new float[w * h];
            var rowMin = new float[w * h];
            Parallel.For(0, h, z =>
            {
                for (int x = 0; x + squares < w; x += 2)
                {
                    float hi = float.MinValue, lo = float.MaxValue;
                    for (int i = 0; i <= squares; i++)
                    {
                        float v = heights[z * w + x + i];
                        hi = Math.Max(hi, v);
                        lo = Math.Min(lo, v);
                    }
                    (rowMax[z * w + x], rowMin[z * w + x]) = (hi, lo);
                }
            });
            var spans = new float[Math.Max(0, bx) * Math.Max(0, bz)];
            Parallel.For(0, Math.Max(0, bz), j =>
            {
                for (int i = 0; i < bx; i++)
                {
                    float hi = float.MinValue, lo = float.MaxValue;
                    for (int k = 0; k <= squares; k++)
                    {
                        int at = (2 * j + k) * w + 2 * i;
                        hi = Math.Max(hi, rowMax[at]);
                        lo = Math.Min(lo, rowMin[at]);
                    }
                    spans[j * bx + i] = hi - lo;
                }
            });
            return spans;
        }

        /// <summary>
        /// Whether a value keeps its side of every limit, staying the margin away from it. One that was already within
        /// the margin of a limit must not change at all: storing the heights in 16 bits could tip it over.
        /// </summary>
        static bool Keeps(double was, double now, double[] limits, double margin)
        {
            foreach (double limit in limits)
            {
                if (Math.Abs(was - limit) < margin)
                {
                    if (now != was)
                        return false;
                }
                else if ((was > limit) != (now > limit) || Math.Abs(now - limit) < margin)
                    return false;
            }
            return true;
        }

        /// <summary>The vertices of every cell, square and footprint the candidate heights change for the worse.</summary>
        /// <summary>What the last check found: slope cells, squares at the water's edge or a depth limit, and building footprints.</summary>
        public (int Slopes, int Water, int Buildings) Found { get; private set; }

        public HashSet<int> Violations(float[] candidate)
        {
            var wrong = new HashSet<int>();
            int slopesFound = 0, waterFound = 0, buildingsFound = 0;
            void Area(int x0, int z0, int size)
            {
                for (int z = z0; z <= Math.Min(h - 1, z0 + size); z++)
                    for (int x = x0; x <= Math.Min(w - 1, x0 + size); x++)
                        wrong.Add(z * w + x);
            }
            var slopes = CellSlopes(candidate);
            for (int c = 0; c < slopes.Length; c++)
                if (!Keeps(cellSlope[c], slopes[c], slopeLimits, SlopeMargin))
                {
                    Area(c % cellsX * 2, c / cellsX * 2, 2);
                    slopesFound++;
                }
            for (int z = 0; z < h - 1; z++)
                for (int x = 0; x < w - 1; x++)
                {
                    int i = z * w + x;
                    double was = Math.Max(Math.Max(before[i], before[i + 1]), Math.Max(before[i + w], before[i + w + 1]));
                    double now = Math.Max(Math.Max(candidate[i], candidate[i + 1]), Math.Max(candidate[i + w], candidate[i + w + 1]));
                    if (was != now && !Keeps(was, now, waterLimits, HeightMargin))
                    {
                        Area(x, z, 1);
                        waterFound++;
                    }
                }
            foreach (var (limit, squares, spanBefore) in buildable)
            {
                var spanNow = Spans(candidate, squares);
                int bx = (w - 1 - squares) / 2 + 1;
                for (int b = 0; b < spanNow.Length; b++)
                    if (spanBefore[b] <= limit && (spanBefore[b] > limit - SpanMargin ? spanNow[b] != spanBefore[b] : spanNow[b] > limit - SpanMargin))
                    {
                        Area(b % bx * 2, b / bx * 2, squares);
                        buildingsFound++;
                    }
            }
            Found = (slopesFound, waterFound, buildingsFound);
            return wrong;
        }
    }
}
