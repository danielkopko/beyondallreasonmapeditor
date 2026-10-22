// The checks, by the engine's rules (MapCore.Movement: per 8-unit square, the slope of its 16-unit cell, and the
// water depth) and round every blocking feature's footprint (its footprint units of 16, turned to its facing, and 8
// more round it, so a way through is at least 3 squares wide):
//   every start is reached by vehicles; each lane on its own lets vehicles and bots cross from north to south, and
//   with all three shut the ship walls the halves off; every metal spot is clear of features and flat enough for an
//   extractor; the halves mirror each other.
using System.Globalization;
using MapCore.Heights;
using MapCore.Movement;

static class Check
{
    /// <summary>Footprints (footprint units) and whether each kind of feature blocks: this map's own, and the jungle's.</summary>
    public static Dictionary<string, (int X, int Z, bool Blocks)> Footprints(IEnumerable<FeatureDef> defs)
    {
        var f = defs.ToDictionary(d => d.Name, d => (d.Footprint.X, d.Footprint.Z, d.Blocking));
        return f;
    }

    static (int X, int Z, bool Blocks) Jungle(string name) =>
        name.StartsWith("banyan_", StringComparison.Ordinal) ? (2, 2, true)
        : name.StartsWith("kapok", StringComparison.Ordinal) || name.StartsWith("oak_holly", StringComparison.Ordinal) || name.StartsWith("ad0_senegal", StringComparison.Ordinal) ? (1, 1, true)
        : (1, 1, false);

    public static int Report(HeightMap heights, IEnumerable<string> ops, Dictionary<string, (int X, int Z, bool Blocks)> own, Action<string> log)
    {
        int failures = 0;
        void Expect(bool ok, string what)
        {
            log($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }
        var vehicle = new PassabilityMap(heights, BarMoveClasses.Vehicle);
        var bot = new PassabilityMap(heights, BarMoveClasses.Bot);
        int w = vehicle.Width, h = vehicle.Height;
        var items = new bool[w * h];
        int blocking = 0;
        foreach (var line in ops.Where(l => l.StartsWith("item-add:placer:", StringComparison.Ordinal)))
        {
            var f = line.Split(':');
            var xz = f[3].Split(',');
            double x = double.Parse(xz[0], CultureInfo.InvariantCulture), z = double.Parse(xz[1], CultureInfo.InvariantCulture);
            int facing = (int)Math.Round(double.Parse(f[4], CultureInfo.InvariantCulture) / 16384) % 4;
            var fp = own.TryGetValue(f[2], out var o) ? o : Jungle(f[2]);
            if (!fp.Blocks) continue;
            blocking++;
            double hx = (facing % 2 == 0 ? fp.X : fp.Z) * 8, hz = (facing % 2 == 0 ? fp.Z : fp.X) * 8;
            Mark(items, w, h, x, z, hx + 8, hz + 8);
        }
        log($"{blocking} blocking features, {items.Count(b => b) * 100.0 / items.Length:0.0}% of the map with the room round them.");

        var starts = Layout.Starts.Concat(Layout.Starts.Select(s => new P(s.X, Layout.Size - s.Z))).ToArray();
        bool Has(bool[] reach, P p)
        {
            for (int dz = -3; dz <= 3; dz++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    int x = (int)(p.X / 8) + dx, z = (int)(p.Z / 8) + dz;
                    if (x >= 0 && z >= 0 && x < w && z < h && reach[z * w + x]) return true;
                }
            return false;
        }
        bool[] Shut(Func<double, bool> shut)
        {
            var m = (bool[])items.Clone();
            for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    double gx = x * 8 + 4, gz = z * 8 + 4;
                    if (Math.Abs(gz - Layout.Axis) < 1500 && shut(gx)) m[z * w + x] = true;
                }
            return m;
        }

        log("Vehicles (TANK3: 27 degrees, 20 deep) and bots (BOT3: 54 degrees), round the features:");
        var all = Access.Fill(vehicle, starts[0], items);
        Expect(starts.All(s => Has(all, s)), $"from the north-west start, vehicles reach all four starts ({all.Count(b => b) * 100.0 / all.Length:0.0}% of the map)");
        foreach (var (name, x0, x1) in Layout.Lanes)
        {
            var only = Shut(x => x < x0 || x > x1);
            Expect(Has(Access.Fill(vehicle, starts[0], only), starts[2]), $"by {name} alone, vehicles cross from north to south");
            Expect(Has(Access.Fill(bot, starts[0], only), starts[2]), $"by {name} alone, bots cross from north to south");
        }
        var none = Shut(x => Layout.Lanes.Any(l => x >= l.X0 && x <= l.X1));
        var leakV = Access.Fill(vehicle, starts[0], none);
        var leakB = Access.Fill(bot, starts[0], none);
        bool walled = !Has(leakV, starts[2]) && !Has(leakB, starts[2]);
        Expect(walled, "with the three lanes shut, the ship walls the north off from the south");
        if (!walled)
        {
            int row = (int)(Layout.Axis / 8);
            var open = Enumerable.Range(0, w).Where(x => leakB[row * w + x] || leakV[row * w + x]).Select(x => x * 8 + 4).ToList();
            log($"       crossing the middle line at x = {string.Join(", ", open.Take(12))}{(open.Count > 12 ? " …" : "")}");
        }

        // The hull's sections butt together with no end caps: they must all sit at one height.
        var sections = ops.Where(l => l.StartsWith("item-add:placer:sf_ship_", StringComparison.Ordinal) && !l.Contains("sf_ship_prow"))
            .Select(l => l.Split(':')[3].Split(',')).Select(xz => heights.HeightAt(double.Parse(xz[0], CultureInfo.InvariantCulture), double.Parse(xz[1], CultureInfo.InvariantCulture))).ToList();
        Expect(sections.Count == 5 && sections.Max() - sections.Min() < 0.05,
            $"the ship's five hull sections sit at one height, so they meet without a step ({string.Join(", ", sections.Select(v => v.ToString("0.00", CultureInfo.InvariantCulture)))})");

        log("Metal spots and starts:");
        var spots = Layout.Spots.Select(s => s.At).Concat(Layout.Vents).SelectMany(s => new[] { s, new P(s.X, Layout.Size - s.Z) }).ToList();
        int crowded = spots.Count(s => Enumerable.Range(-5, 11).Any(dz => Enumerable.Range(-5, 11).Any(dx =>
        {
            int x = (int)(s.X / 8) + dx, z = (int)(s.Z / 8) + dz;
            return x >= 0 && z >= 0 && x < w && z < h && items[z * w + x];
        })));
        Expect(crowded == 0, $"no blocking feature within 40 of any of the {spots.Count} metal spots ({crowded} crowded)");
        var bots = Access.Fill(bot, starts[0], items);
        Expect(spots.All(s => Has(bots, s)), "bots reach every metal spot and vent");
        var cut = spots.Where(s => !Has(all, s)).ToList();
        Expect(cut.Count == 0, $"vehicles reach every metal spot{(cut.Count > 0 ? $" (not {string.Join(", ", cut.Select(c => $"({c.X:0}, {c.Z:0})"))})" : "")}");
        double worst = spots.Max(s => Rise(heights, s, 16));
        Expect(worst <= 16, $"every spot and vent is level enough to build on (the most any rises over its 32-unit footprint: {worst:0.0})");
        Expect(starts.All(s => heights.HeightAt(s.X, s.Z) > 4 && Rise(heights, s, 48) < 30), "every start is on dry, nearly level ground");

        var rnd = new Random(1);
        var diffs = new List<double>();
        while (diffs.Count < 200000)
        {
            double x = rnd.NextDouble() * Layout.Size, z = rnd.NextDouble() * Layout.Size;
            diffs.Add(Math.Abs(heights.HeightAt(x, z) - heights.HeightAt(x, Layout.Size - z)));
        }
        diffs.Sort();
        log($"Halves alike (200000 points against their mirror image): median {diffs[diffs.Count / 2]:0.00}, 99% within {diffs[(int)(diffs.Count * 0.99)]:0.00}, worst {diffs[^1]:0.00}");
        return failures;
    }

    static double Rise(HeightMap hm, P p, double half)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        for (double dz = -half; dz <= half; dz += 8)
            for (double dx = -half; dx <= half; dx += 8)
            {
                double y = hm.HeightAt(p.X + dx, p.Z + dz);
                lo = Math.Min(lo, y); hi = Math.Max(hi, y);
            }
        return hi - lo;
    }

    static void Mark(bool[] m, int w, int h, double x, double z, double hx, double hz)
    {
        for (int sz = Math.Max(0, (int)((z - hz) / 8)); sz <= Math.Min(h - 1, (int)((z + hz) / 8)); sz++)
            for (int sx = Math.Max(0, (int)((x - hx) / 8)); sx <= Math.Min(w - 1, (int)((x + hx) / 8)); sx++)
                if (Math.Abs(sx * 8 + 4 - x) <= hx && Math.Abs(sz * 8 + 4 - z) <= hz) m[sz * w + sx] = true;
    }
}

static class Access
{
    /// <summary>Squares reachable from a point (4-connected), not entering <paramref name="blocked"/>.</summary>
    public static bool[] Fill(PassabilityMap pass, P from, bool[]? blocked)
    {
        int w = pass.Width, h = pass.Height;
        var seen = new bool[w * h];
        var queue = new Queue<int>();
        int sx = (int)(from.X / 8), sz = (int)(from.Z / 8);
        for (int r = 0; r < 8 && queue.Count == 0; r++)
            for (int dz = -r; dz <= r && queue.Count == 0; dz++)
                for (int dx = -r; dx <= r && queue.Count == 0; dx++)
                {
                    int x = sx + dx, z = sz + dz;
                    if (x < 0 || z < 0 || x >= w || z >= h) continue;
                    if (pass[x, z] is Passage.TooSteep or Passage.TooDeep || (blocked?[z * w + x] ?? false)) continue;
                    seen[z * w + x] = true;
                    queue.Enqueue(z * w + x);
                }
        while (queue.Count > 0)
        {
            int c = queue.Dequeue(), cx = c % w, cz = c / w;
            foreach (var (nx, nz) in new[] { (cx + 1, cz), (cx - 1, cz), (cx, cz + 1), (cx, cz - 1) })
            {
                if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                int ni = nz * w + nx;
                if (seen[ni] || (blocked?[ni] ?? false)) continue;
                if (pass[nx, nz] is Passage.TooSteep or Passage.TooDeep) continue;
                seen[ni] = true;
                queue.Enqueue(ni);
            }
        }
        return seen;
    }
}
