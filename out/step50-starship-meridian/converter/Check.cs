// The checks, by the engine's rules (MapCore.Movement: per 8-unit square, the slope of its 16-unit cell) and round
// every blocking feature's footprint (16-unit footprint units, turned to its facing, and 8 more round it):
//   the halves match exactly, heights and features; every start, spot and vent is reached; each of the five crossings
//   alone lets vehicles across, and with all five shut no ground unit crosses but spiders (their route is logged); no
//   ground class reaches space; every door is wide and clear of features; every start room keeps enough level ground
//   free; every spot is level and clear; every machine and prop stands level. The fronts' driving distance is logged.
using System.Globalization;
using MapCore.Heights;
using MapCore.Movement;

static class Check
{
    public static Dictionary<string, (int X, int Z, bool Blocks)> Footprints(IEnumerable<FeatureDef> defs) =>
        defs.ToDictionary(d => d.Name, d => (d.Footprint.X, d.Footprint.Z, d.Blocking));

    public static int Report(HeightMap heights, Terrain.Result t, IEnumerable<string> ops, Dictionary<string, (int X, int Z, bool Blocks)> own, Action<string> log)
    {
        int failures = 0;
        void Expect(bool ok, string what)
        {
            log($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }
        string List(IEnumerable<string> bad) => bad.Any() ? ": " + string.Join("; ", bad.Take(12)) + (bad.Count() > 12 ? $"; and {bad.Count() - 12} more" : "") : "";

        var vehicle = new PassabilityMap(heights, BarMoveClasses.Vehicle);
        var bot = new PassabilityMap(heights, BarMoveClasses.Bot);
        var hover = new PassabilityMap(heights, BarMoveClasses.Hover);
        var amphibious = new PassabilityMap(heights, BarMoveClasses.All.Single(c => c.Name == "ATANK3"));
        var spider = new PassabilityMap(heights, BarMoveClasses.All.Single(c => c.Name == "TBOT3"));
        int w = vehicle.Width, h = vehicle.Height;

        // The features: their footprints marked (and 8 round them), and the list for the mirror check.
        var items = new bool[w * h];
        var laid = new List<(string Name, P At, int Heading)>();
        foreach (var line in ops.Where(l => l.StartsWith("item-add:placer:", StringComparison.Ordinal)))
        {
            var f = line.Split(':');
            var xz = f[3].Split(',');
            var at = new P(double.Parse(xz[0], CultureInfo.InvariantCulture), double.Parse(xz[1], CultureInfo.InvariantCulture));
            int heading = int.Parse(f[4], CultureInfo.InvariantCulture);
            laid.Add((f[2], at, heading));
            if (!own.TryGetValue(f[2], out var fp) || !fp.Blocks) continue;
            int facing = (int)Math.Round(heading / 16384.0) % 4;
            double hx = (facing % 2 == 0 ? fp.X : fp.Z) * 8, hz = (facing % 2 == 0 ? fp.Z : fp.X) * 8;
            Mark(items, w, h, at.X, at.Z, hx + 8, hz + 8);
        }
        log($"{laid.Count} features, {items.Count(b => b) * 100.0 / items.Length:0.0}% of the map blocked with the room round them.");

        P SquareCentre(int i) => new((i % w) * 8 + 4, (i / w) * 8 + 4);
        int CellAt(P p) => Terrain.Nearest(t.Cell, t.NX, t.NZ, p);
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
        bool[] Shut(Func<P, bool> shut)
        {
            var m = (bool[])items.Clone();
            Parallel.For(0, h, z => { for (int x = 0; x < w; x++) if (shut(new P(x * 8 + 4, z * 8 + 4))) m[z * w + x] = true; });
            return m;
        }

        log("The halves:");
        {
            int nx = t.NX, worst = 0; double most = 0;
            for (int z = 0; z < t.NZ; z++)
                for (int x = 0; x < nx / 2; x++)
                {
                    double a = heights.HeightAt(x * 8, z * 8), b = heights.HeightAt(Layout.W - x * 8, z * 8);
                    if (Math.Abs(a - b) > most) { most = Math.Abs(a - b); worst = z * nx + x; }
                }
            Expect(most < 0.01, $"the heights match their mirror image at every vertex (the most apart {most:0.000})");
            var unmatched = new List<string>();
            var index = laid.GroupBy(l => (Math.Round(l.At.X), Math.Round(l.At.Z))).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var (name, at, heading) in laid)
            {
                if (Math.Abs(at.X - Layout.CX) < 1) continue;
                var key = (Math.Round(Layout.W - at.X), Math.Round(at.Z));
                string twin = name.EndsWith("_m", StringComparison.Ordinal) ? name[..^2] : name + "_m";
                int want = ((32768 - heading) % 65536 + 65536) % 65536;
                bool found = index.TryGetValue(key, out var there) && there.Any(o =>
                    (o.Name == name || o.Name == twin) && Math.Min(Math.Abs(o.Heading - want), 65536 - Math.Abs(o.Heading - want)) <= 2);
                if (!found) unmatched.Add($"{name} at ({at.X:0}, {at.Z:0})");
            }
            Expect(unmatched.Count == 0, $"every feature has its mirror image (the same model, or its twin, at the mirrored place and heading){List(unmatched)}");
        }

        log("Getting about (vehicles: TANK3, 27 degrees; bots: BOT3, 54 degrees; hovers 33; amphibious ATANK3; spiders TBOT3), round the features:");
        var starts = Layout.Starts.Select(s => s.At).ToArray();
        var all = Access.Fill(vehicle, starts[0], items);
        Expect(starts.All(s => Has(all, s)), $"vehicles reach all eight starts ({all.Count(b => b) * 100.0 / all.Length:0.0}% of the map)");
        var cut = Layout.Spots.Where(s => !Has(all, s.At)).Select(s => $"({s.At.X:0}, {s.At.Z:0})").Concat(Layout.Vents.Where(v => !Has(all, v.At)).Select(v => $"vent ({v.At.X:0}, {v.At.Z:0})")).ToList();
        Expect(cut.Count == 0, $"vehicles reach every metal spot and vent (the reactor's vents by the well's ramps){List(cut)}");
        var bots = Access.Fill(bot, starts[0], items);
        Expect(starts.All(s => Has(bots, s)) && Layout.Spots.All(s => Has(bots, s.At)), "bots reach every start and spot");

        log("The crossings (each team's half joins the other only through the shared rooms on the centreline):");
        int Shared(string name) => Array.FindIndex(Layout.Cells, c => c.Name == name && c.Side == "centre");
        var lanes = Layout.Lanes.Select(Shared).ToArray();
        P port = starts[1], starboard = starts[5];
        foreach (var k in lanes)
        {
            var others = lanes.Where(o => o != k).ToHashSet();
            var one = Shut(p => others.Contains(CellAt(p)));
            Expect(Has(Access.Fill(vehicle, port, one), starboard), $"the {Layout.Cells[k].Name} alone lets vehicles across");
        }
        var none = Shut(p => lanes.Contains(CellAt(p)));
        foreach (var (name, map) in new[] { ("vehicles", vehicle), ("bots", bot), ("hovers", hover), ("amphibious units", amphibious) })
            Expect(!Has(Access.Fill(map, port, none), starboard), $"with all five shut, {name} can't cross");
        var spiders = Access.Fill(spider, port, none);
        log($"       spiders, all five shut: {(Has(spiders, starboard) ? "they cross, over the bulkheads (all-terrain bots climb any wall; intended)" : "they don't cross")}");

        log("Space:");
        foreach (var (name, map) in new[] { ("vehicles", vehicle), ("bots", bot), ("hovers", hover), ("amphibious units", amphibious) })
        {
            var reach = Access.Fill(map, port, items);
            int inSpace = 0;
            for (int i = 0; i < reach.Length; i++) if (reach[i] && CellAt(SquareCentre(i)) == Terrain.Space) inSpace++;
            Expect(inSpace == 0, $"{name} don't reach space ({inSpace} squares)");
        }
        {
            var reach = Access.Fill(spider, port, items);
            int inSpace = 0;
            for (int i = 0; i < reach.Length; i++) if (reach[i] && CellAt(SquareCentre(i)) == Terrain.Space) inSpace++;
            log($"       spiders reach {inSpace} squares of space (over the rim; it's too rough for anything else, and never drawn)");
        }
        {
            int flat = 0, total = 0;
            for (int z = 0; z < h; z += 2)
                for (int x = 0; x < w; x += 2)
                {
                    var p = new P(x * 8 + 4, z * 8 + 4);
                    if (CellAt(p) != Terrain.Space || Terrain.Sample(t.Outside, t.NX, t.NZ, p) < 80) continue;
                    total++;
                    if (vehicle[x, z] is not Passage.TooSteep) flat++;
                }
            Expect(flat < total * 0.02, $"space is too rough to build on or drive over ({flat * 100.0 / Math.Max(1, total):0.0}% of it isn't too steep for vehicles)");
        }

        log("Doors and rooms:");
        var narrow = Layout.Doors.Where(d => d.Kind != Layout.DoorKind.Stairs && Math.Max(d.X1 - d.X0, d.Z1 - d.Z0) < 500).Select(d => $"({d.X0:0}, {d.Z0:0})").ToList();
        Expect(narrow.Count == 0, $"every door is at least 500 wide{List(narrow)}");
        var blockedDoors = Layout.Doors.Where(d =>
        {
            for (double z = d.Z0; z <= d.Z1; z += 8)
                for (double x = d.X0; x <= d.X1; x += 8)
                {
                    int sx = (int)(x / 8), sz = (int)(z / 8);
                    if (sx >= 0 && sz >= 0 && sx < w && sz < h && items[sz * w + sx]) return true;
                }
            return false;
        }).Select(d => $"({d.X0:0}, {d.Z0:0})").ToList();
        Expect(blockedDoors.Count == 0, $"no feature stands in a doorway{List(blockedDoors)}");
        foreach (var (room, at) in Layout.Starts.Take(4))
        {
            int c = CellAt(at);
            int level = 0, total = 0;
            for (double z = 0; z < Layout.H; z += 16)
                for (double x = 0; x < Layout.CX; x += 16)
                {
                    var p = new P(x + 8, z + 8);
                    if (CellAt(p) != c) continue;
                    total++;
                    int sx = (int)(p.X / 8), sz = (int)(p.Z / 8);
                    if (items[sz * w + sx]) continue;
                    double rise = Math.Abs(heights.HeightAt(p.X + 8, p.Z) - heights.HeightAt(p.X - 8, p.Z)) + Math.Abs(heights.HeightAt(p.X, p.Z + 8) - heights.HeightAt(p.X, p.Z - 8));
                    if (rise < 2) level++;
                }
            Expect(level >= total * 0.6, $"the {room} keeps {level * 100.0 / Math.Max(1, total):0}% of its floor ({level * 256 / 1e6:0.00} M square units) level and free to build on (at least 60%)");
        }

        log("Standing true:");
        {
            // Every feature stands on level ground under its footprint (but the viewport, on the bridge's forward wall;
            // the engines, hanging off their housings over space; the pipe runs, which block nothing).
            string[] skip = ["sm_viewport", "sm_enginebell", "sm_pipes"];
            var tilted = new List<string>();
            double most = 0;
            foreach (var (name, at, heading) in laid)
            {
                if (skip.Any(n => name.StartsWith(n, StringComparison.Ordinal)) || !own.TryGetValue(name, out var fp)) continue;
                int facing = (int)Math.Round(heading / 16384.0) % 4;
                double hx = (facing % 2 == 0 ? fp.X : fp.Z) * 8, hz = (facing % 2 == 0 ? fp.Z : fp.X) * 8;
                double lo = double.MaxValue, hi = double.MinValue;
                for (double dz = -hz; dz <= hz; dz += 8)
                    for (double dx = -hx; dx <= hx; dx += 8)
                    {
                        double y = heights.HeightAt(at.X + dx, at.Z + dz);
                        lo = Math.Min(lo, y); hi = Math.Max(hi, y);
                    }
                most = Math.Max(most, hi - lo);
                if (hi - lo > 4) tilted.Add($"{name} at ({at.X:0}, {at.Z:0}) ({hi - lo:0})");
            }
            Expect(tilted.Count == 0, $"every feature stands level: the ground under its footprint rises no more than 4 (the most {most:0.0}){List(tilted)}");
        }

        log("Metal spots, vents and starts:");
        int crowded = Layout.Spots.Count(s => Enumerable.Range(-5, 11).Any(dz => Enumerable.Range(-5, 11).Any(dx =>
        {
            int x = (int)(s.At.X / 8) + dx, z = (int)(s.At.Z / 8) + dz;
            return x >= 0 && z >= 0 && x < w && z < h && items[z * w + x];
        })));
        Expect(crowded == 0, $"no blocking feature within 40 of any of the {Layout.Spots.Length} metal spots ({crowded} crowded)");
        double worstRise = Layout.Spots.Select(s => s.At).Concat(Layout.Vents.Select(v => v.At)).Max(p => Rise(heights, p, 48));
        Expect(worstRise < 1, $"every spot and vent is level (the most any rises over its footprint: {worstRise:0.0})");
        Expect(starts.All(s => Rise(heights, s, 200) < 1), "every start is on level deck");
        double teamMetal = Layout.Spots.Where(s => s.Side == "port").Sum(s => s.Income);
        Expect(Math.Abs(teamMetal - Layout.Spots.Where(s => s.Side == "starboard").Sum(s => s.Income)) < 0.01,
            $"each team's own metal: {teamMetal:0.0} metal/s, and {Layout.Spots.Where(s => s.Side == "shared").Sum(s => s.Income):0.0} shared on the centreline; {Layout.Vents.Length} vents");

        // The fronts: how far the two teams' front starts drive to each other (not a check).
        var d = Steps(vehicle, [starts[1]], items);
        log($"       the fronts: crew deck to crew deck {At(d, w, h, starts[5]):0}, crew deck to the enemy's cargo hold {At(d, w, h, starts[7]):0} (driving)");
        return failures;
    }

    static double At(double[] d, int w, int h, P p)
    {
        double best = double.MaxValue;
        for (int dz = -3; dz <= 3; dz++)
            for (int dx = -3; dx <= 3; dx++)
            {
                int x = (int)(p.X / 8) + dx, z = (int)(p.Z / 8) + dz;
                if (x >= 0 && z >= 0 && x < w && z < h && d[z * w + x] >= 0) best = Math.Min(best, d[z * w + x]);
            }
        return best == double.MaxValue ? double.NaN : best * 8;
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

    /// <summary>Driving distance (in 8-unit squares, 8-connected, a diagonal √2) from the nearest of the starts to every square; -1 where unreached.</summary>
    static double[] Steps(PassabilityMap pass, P[] from, bool[] blocked)
    {
        int w = pass.Width, h = pass.Height;
        var d = Enumerable.Repeat(-1.0, w * h).ToArray();
        var done = new bool[w * h];
        var queue = new PriorityQueue<int, double>();
        bool open(int x, int z) => x >= 0 && z >= 0 && x < w && z < h && !blocked[z * w + x] && pass[x, z] is not (Passage.TooSteep or Passage.TooDeep);
        foreach (var s in from)
        {
            int i = (int)(s.Z / 8) * w + (int)(s.X / 8);
            d[i] = 0; queue.Enqueue(i, 0);
        }
        while (queue.TryDequeue(out int c, out double dc))
        {
            if (done[c]) continue;
            done[c] = true;
            int cx = c % w, cz = c / w;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = cx + dx, nz = cz + dz;
                    if (!open(nx, nz)) continue;
                    if (dx != 0 && dz != 0 && !(open(cx + dx, cz) && open(cx, cz + dz))) continue;
                    int ni = nz * w + nx;
                    double nd = dc + (dx != 0 && dz != 0 ? Math.Sqrt(2) : 1);
                    if (done[ni] || (d[ni] >= 0 && d[ni] <= nd)) continue;
                    d[ni] = nd;
                    queue.Enqueue(ni, nd);
                }
        }
        return d;
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
