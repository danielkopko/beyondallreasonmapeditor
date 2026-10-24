// The checks, by the engine's rules (MapCore.Movement: per 8-unit square, the slope of its 16-unit cell, and the
// water depth) and round every blocking feature's footprint (16-unit footprint units, turned to its facing, and 8
// more round it). The map isn't symmetric, so its fairness is measured:
//   every start and spot is reached; each of the four ramps alone takes vehicles to the floor; with all four shut
//   vehicles can't get down but bots can, by the slide; with the slide shut too, nothing does; the pit runs to the
//   south edge, so nothing crosses south of it;
//   the two teams' ways (driving distance from their starts) to the pit floor's metal, to their own ramp's metal and
//   to each other match within 10%; their metal and the machines they can reclaim on their side match; so does the
//   level ground round their starts to build on; and every spot is clear of features and level.
using System.Globalization;
using MapCore.Heights;
using MapCore.Movement;

static class Check
{
    public static Dictionary<string, (int X, int Z, bool Blocks)> Footprints(IEnumerable<FeatureDef> defs) =>
        defs.ToDictionary(d => d.Name, d => (d.Footprint.X, d.Footprint.Z, d.Blocking));

    public static int Report(HeightMap heights, Terrain.Result t, IEnumerable<string> ops, Dictionary<string, (int X, int Z, bool Blocks)> own,
        Dictionary<string, int> metal, Action<string> log)
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
        var placed = new List<(string Name, P At)>();
        var laid = new List<(string Name, P At, int Facing)>();
        foreach (var line in ops.Where(l => l.StartsWith("item-add:placer:", StringComparison.Ordinal)))
        {
            var f = line.Split(':');
            var xz = f[3].Split(',');
            var at = new P(double.Parse(xz[0], CultureInfo.InvariantCulture), double.Parse(xz[1], CultureInfo.InvariantCulture));
            int facing = (int)Math.Round(double.Parse(f[4], CultureInfo.InvariantCulture) / 16384) % 4;
            placed.Add((f[2], at));
            laid.Add((f[2], at, facing));
            if (!own.TryGetValue(f[2], out var fp) || !fp.Blocks) continue;
            double hx = (facing % 2 == 0 ? fp.X : fp.Z) * 8, hz = (facing % 2 == 0 ? fp.Z : fp.X) * 8;
            Mark(items, w, h, at.X, at.Z, hx + 8, hz + 8);
        }
        log($"{placed.Count} features, {items.Count(b => b) * 100.0 / items.Length:0.0}% of the map blocked with the room round them.");

        var starts = Layout.StartsA.Concat(Layout.StartsB).ToArray();
        var spots = Layout.Spots.Select(s => (s.At, s.Income, s.Side)).Concat(t.PitSpots).ToList();
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

        log("Getting about (vehicles: TANK3, 27 degrees; bots: BOT3, 54 degrees), round the features:");
        var all = Access.Fill(vehicle, starts[0], items);
        Expect(starts.All(s => Has(all, s)), $"vehicles reach all four starts ({all.Count(b => b) * 100.0 / all.Length:0.0}% of the map)");
        var cut = spots.Where(s => !Has(all, s.At)).ToList();
        Expect(cut.Count == 0 && Has(all, t.Vent), $"vehicles reach every metal spot and the vent{(cut.Count > 0 ? $" (not {string.Join(", ", cut.Select(c => $"({c.At.X:0}, {c.At.Z:0})"))})" : "")}");
        var floor = t.PitSpots.Where(s => s.Side == "pit").Select(s => s.At).ToList();
        bool Near(P p, int k) => Geo.ToPolyline(p, t.Ramps[k], out _) < Layout.RampHalf + 60;
        int ramps = t.Ramps.Length;
        bool NearAny(P p, Func<int, bool> which) => Enumerable.Range(0, ramps).Any(j => which(j) && Near(p, j));
        for (int k = 0; k < ramps; k++)
        {
            int kk = k;
            var other = Shut(p => NearAny(p, j => j != kk));
            var reach = Access.Fill(vehicle, starts[0], other);
            bool down = floor.All(f => Has(reach, f));
            Expect(down, $"by the {Terrain.RampNames[k]} ramp alone, vehicles reach the pit floor (all six spots)");
            if (!down)
            {
                var line = t.Ramps[k];
                int last = -1;
                for (int i = 0; i < line.Count; i++) if (Has(reach, line[i])) last = i; else if (last >= 0) break;
                var stop = line[Math.Clamp(last + 1, 0, line.Count - 1)];
                log($"       the ramp is reached {last + 1} of {line.Count} points down; it stops near ({stop.X:0}, {stop.Z:0}), height {heights.HeightAt(stop.X, stop.Z):0}");
            }
        }
        var noRamps = Shut(p => NearAny(p, _ => true));
        Expect(!floor.Any(f => Has(Access.Fill(vehicle, starts[0], noRamps), f)), "with all four ramps shut, vehicles can't get down the benches");
        Expect(floor.All(f => Has(Access.Fill(bot, starts[0], noRamps), f)), "with all four ramps shut, bots get down by the slide");
        bool InSlide(P p) => Terrain.Sample(t.Depth, t.N, p) > 0 && p.Z < Layout.PitCentre.Z && Math.Abs(Terrain.Angle(p) - (Layout.SlideFrom + Layout.SlideTo) / 2) < (Layout.SlideTo - Layout.SlideFrom) / 2 + 6;
        var noSlide = Shut(p => NearAny(p, _ => true) || InSlide(p));
        Expect(!floor.Any(f => Has(Access.Fill(bot, starts[0], noSlide), f)), "with the ramps and the slide shut, nothing gets down the benches");

        log("Fair shares (driving distance from each team's starts, round the features):");
        var distA = Steps(vehicle, Layout.StartsA, items);
        var distB = Steps(vehicle, Layout.StartsB, items);
        double At(double[] d, P p, int reach = 3)
        {
            double best = double.MaxValue;
            for (int dz = -reach; dz <= reach; dz++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int x = (int)(p.X / 8) + dx, z = (int)(p.Z / 8) + dz;
                    if (x >= 0 && z >= 0 && x < w && z < h && d[z * w + x] >= 0) best = Math.Min(best, d[z * w + x]);
                }
            return best == double.MaxValue ? double.NaN : best * 8.0;
        }
        void Alike(string what, double a, double b)
        {
            double ratio = Math.Max(a, b) / Math.Min(a, b);
            Expect(!double.IsNaN(ratio) && ratio <= 1.10, $"{what}: west {a:0}, east {b:0} ({(ratio - 1) * 100:0}% apart)");
        }
        Alike("to the nearest pit-floor spot", floor.Min(f => At(distA, f)), floor.Min(f => At(distB, f)));
        Alike("to all six pit-floor spots, on average", floor.Average(f => At(distA, f)), floor.Average(f => At(distB, f)));
        var rampA = t.PitSpots.Where(s => s.Side == "A").Select(s => s.At).ToList();
        var rampB = t.PitSpots.Where(s => s.Side == "B").Select(s => s.At).ToList();
        Alike("to their own first ramp's spot (the old pit's)", At(distA, rampA[0]), At(distB, rampB[0]));
        Alike("to their own second ramp's spot (the extension's)", At(distA, rampA[1]), At(distB, rampB[1]));
        Alike("to the enemy's nearest start", Layout.StartsB.Min(s => At(distA, s)), Layout.StartsA.Min(s => At(distB, s)));
        double metalA = spots.Where(s => s.Side == "A").Sum(s => s.Income), metalB = spots.Where(s => s.Side == "B").Sum(s => s.Income);
        Expect(Math.Abs(metalA - metalB) < 0.01, $"their own metal: west {metalA:0.0}, east {metalB:0.0} metal/s, and {spots.Where(s => s.Side == "pit").Sum(s => s.Income):0.0} shared on the floor");
        double reclaimA = 0, reclaimB = 0;
        foreach (var (name, at) in placed)
        {
            if (!metal.TryGetValue(name, out int m) || m < 100) continue;
            double a = At(distA, at, 24), b = At(distB, at, 24);
            if (double.IsNaN(a) || double.IsNaN(b) || Terrain.Sample(t.Depth, t.N, at) > 0) continue;   // the pit's machines are shared
            if (a < b) reclaimA += m; else reclaimB += m;
        }
        Alike("machines to reclaim on their side (metal)", reclaimA, reclaimB);
        double room(P[] s) => s.Sum(st => Flat(heights, items, w, h, st, 900));
        Alike("level ground to build on within 900 of their starts (squares)", room(Layout.StartsA), room(Layout.StartsB));

        log("The north lane, across the railway (the pit and all south of its middle shut, so only the north lane joins the sides):");
        bool Lane(P p) => p.Z > Layout.PitCentre.Z || Terrain.Sample(t.Depth, t.N, p) > 0;
        double trainEnd = placed.Where(q => q.Name is "oc_wagon" or "oc_wagon_loaded" or "oc_loco" && Math.Abs(q.At.X - Layout.RailX) < 1 && q.At.Z < t.CrossingZ)
            .Max(q => q.At.Z + (q.Name == "oc_loco" ? 128 : 56));
        var gaps = new (string Name, double Z0, double Z1)[]
        {
            ("gap at the north edge", 0, Layout.LocoFront),
            ("level crossing on road 7", trainEnd, t.ShedNorth),
            ("gap between the shed and the rim", t.ShedSouth, t.RimZ),
        };
        bool InGap(P p, int g) => p.X > Layout.RailX - 80 && p.X < Layout.SidingX + 80 && p.Z >= gaps[g].Z0 - 16 && p.Z <= (g == 2 ? gaps[g].Z1 + 300 : gaps[g].Z1 + 16);
        P a0 = Layout.StartsA[0], b0 = Layout.StartsB[0];
        var lane = Shut(Lane);
        Expect(Has(Access.Fill(vehicle, a0, lane), b0) && Has(Access.Fill(bot, a0, lane), b0), "vehicles and bots cross the north lane, through the train's gaps");
        var walled = Shut(p => Lane(p) || Enumerable.Range(0, gaps.Length).Any(g => InGap(p, g)));
        Expect(!Has(Access.Fill(vehicle, a0, walled), b0) && !Has(Access.Fill(bot, a0, walled), b0), "with its gaps shut, the train, the loadout and the shed wall the lane off: nothing crosses");
        for (int g = 0; g < gaps.Length; g++)
        {
            int k = g;
            var one = Shut(p => Lane(p) || Enumerable.Range(0, gaps.Length).Any(o => o != k && InGap(p, o)));
            Expect(Has(Access.Fill(vehicle, a0, one), b0), $"the {gaps[k].Name} alone lets vehicles across ({gaps[k].Z1 - gaps[k].Z0:0} clear)");
        }
        // The south: the pit runs to the map's edge, so with the pit and all north of its middle shut, nothing crosses.
        var south = Shut(p => p.Z < Layout.PitCentre.Z || Terrain.Sample(t.Depth, t.N, p) > 0);
        Expect(!Has(Access.Fill(vehicle, Layout.StartsA[1], south), Layout.StartsB[1]) && !Has(Access.Fill(bot, Layout.StartsA[1], south), Layout.StartsB[1]),
            "the pit runs to the south edge: with it and the north shut, nothing crosses round its south end");
        var crossing = new P(Layout.RailX, (trainEnd + t.ShedNorth) / 2);
        log($"       driving to the level crossing (not a check; the railway is the north's, not the sides'): west {At(distA, crossing):0}, east {At(distB, crossing):0}");

        // Roads: their paint only where a road would be, never under a building, tank, structure or the clutter round
        // them, nor over a stockpile, a paddock-dumped heap, the tailings dam's walls, a breakaway or the magazine's
        // bund. Vehicles may stand on a road (they drove there), and the track crosses road 7 on purpose.
        log("Roads:");
        var roadLines = Terrain.RoadLines.Concat(t.Ramps).Concat(Terrain.DumpRampLines).ToArray();
        bool Painted(P p) => Terrain.RoadShown(p) > 0.05 && Terrain.ToLines(p, roadLines) < Terrain.RoadEdge;
        string[] mayStand = ["oc_truck", "oc_excavator", "oc_ute", "oc_watercart", "oc_grader", "oc_dozer", "oc_loader", "oc_drill", "oc_servicetruck", "oc_bus", "oc_track", "oc_spinifex", "oc_saltbush"];
        var under = new List<string>();
        foreach (var (name, at, facing) in laid)
        {
            if (mayStand.Any(n => name.StartsWith(n, StringComparison.Ordinal)) || !own.TryGetValue(name, out var fp)) continue;
            double hx = (facing % 2 == 0 ? fp.X : fp.Z) * 8, hz = (facing % 2 == 0 ? fp.Z : fp.X) * 8;
            bool hit = false;
            for (double dz = -hz; dz <= hz && !hit; dz += 8)
                for (double dx = -hx; dx <= hx && !hit; dx += 8)
                    hit = Painted(at + new P(dx, dz));
            if (hit) under.Add($"{name} at ({at.X:0}, {at.Z:0})");
        }
        IEnumerable<P> Grid(double x0, double z0, double x1, double z1)
        {
            for (double z = z0; z <= z1; z += 16) for (double x = x0; x <= x1; x += 16) yield return new P(x, z);
        }
        void Ground(string what, IEnumerable<P> points)
        {
            foreach (var p in points) if (Painted(p)) { under.Add($"{what} at ({p.X:0}, {p.Z:0})"); return; }
        }
        foreach (var s in Layout.Stockpiles) Ground("a stockpile", Grid(s.X - 190, s.Z - 190, s.X + 190, s.Z + 190).Where(p => (p - s).Length < 190));
        foreach (var (at, a, _, _) in Layout.Dumps) Ground("a paddock-dumped heap", Grid(at.X - a, at.Z - a, at.X + a, at.Z + a).Where(p => Terrain.Heap(p, out _) > 0.05));
        double outer = Layout.TsfWall / Math.Tan(22 * Math.PI / 180);
        Ground("the tailings dam's wall", Grid(Layout.TsfX0 - outer, Layout.TsfZ0 - outer, Layout.TsfX1 + outer, Layout.TsfZ1 + outer).Where(p =>
        {
            double e = Math.Max(Math.Max(Layout.TsfX0 - p.X, p.X - Layout.TsfX1), Math.Max(Layout.TsfZ0 - p.Z, p.Z - Layout.TsfZ1));
            return e < outer && e > -Layout.TsfCrest - 60;
        }));
        for (int k = 0; k < Layout.Breakaways.Length; k++)
        {
            int kk = k;
            var (at, a, _, _) = Layout.Breakaways[k];
            Ground("a breakaway", Grid(at.X - a - 150, at.Z - a - 150, at.X + a + 150, at.Z + a + 150).Where(p => Terrain.BreakawayRho(p, kk) < 1.05));
        }
        var mag = Layout.Magazine;
        Ground("the magazine's bund", Grid(mag.X - 170, mag.Z - 170, mag.X + 170, mag.Z + 170).Where(p => Math.Abs((p - mag).Length - 130) < 40));
        Expect(under.Count == 0, "no road is painted under a building, tank or structure, nor over a stockpile, a heap, the dam's walls, a breakaway or the magazine's bund"
            + (under.Count > 0 ? ": " + string.Join("; ", under.Take(14)) + (under.Count > 14 ? $"; and {under.Count - 14} more" : "") : ""));
        var survey = Roads.Survey(t, p => heights.HeightAt(p.X, p.Z));
        string List(IEnumerable<string> bad) => bad.Any() ? ": " + string.Join("; ", bad) : "";
        var loose = survey.SelectMany(r => new[] { (r.Name, "start", r.StartsAt), (r.Name, "end", r.EndsAt) }).Where(e => e.Item3 == "nothing").Select(e => $"{e.Item1}'s {e.Item2}").ToList();
        Expect(loose.Count == 0, $"every road leads somewhere: each end meets a start, a pad, a hardstand, a ramp's top, a dump's top, the dam or another road{List(loose)}");
        var legs = Roads.All();
        var unmet = Enumerable.Range(0, t.Ramps.Length).Where(k => legs.All(l => Geo.ToPolyline(t.Ramps[k][0], l.Line, out _) > 130)).Select(k => $"the {Terrain.RampNames[k]} ramp").ToList();
        Expect(unmet.Count == 0, $"a road meets every haul ramp's top{List(unmet)}");
        var inPit = survey.Where(r => r.InPit > 0).Select(r => $"{r.Name} ({r.InPit:0})").ToList();
        Expect(inPit.Count == 0, $"no road runs into the pit (its edges kept 64 out from the rim){List(inPit)}");
        var creeks = survey.Where(r => r.AlongCreek > 450 * Math.Max(1, r.CreekCrossings)).Select(r => $"{r.Name} ({r.AlongCreek:0} in creek beds)").ToList();
        Expect(creeks.Count == 0, $"no road runs along a creek's bed, only across it{List(creeks)}");
        double grade = survey.Max(r => r.MaxGrade), lean = survey.Max(r => r.MaxLean);
        var steep = survey.Where(r => r.MaxGrade > 7 || r.MaxLean > 3).Select(r => $"{r.Name} (grade {r.MaxGrade:0.0} at ({r.SteepestAt.X:0}, {r.SteepestAt.Z:0}), lean {r.MaxLean:0.0} at ({r.LeaningAt.X:0}, {r.LeaningAt.Z:0}))").ToList();
        Expect(steep.Count == 0, $"every road is graded: none climbs more than 7 degrees (the steepest {grade:0.0}) or leans more than 3 across (the most {lean:0.0}){List(steep)}");

        log("Standing true:");
        // Every machine, building, tank and piece of site gear stands on near-level ground: the most the ground rises
        // under its footprint (the natural ground's mounds and washes keep away from them). Not the tipping trucks,
        // which back their tails onto a stockpile or over a dump's edge.
        string[] natural = ["oc_spinifex", "oc_saltbush", "oc_gum", "oc_deadtree", "oc_termites", "oc_boulder", "oc_track", "oc_powerpole", "oc_bucketwheel", "oc_truck_tipping"];
        var tilted = new List<string>();
        double tilt = 0;
        foreach (var (name, at, facing) in laid)
        {
            if (natural.Any(n => name.StartsWith(n, StringComparison.Ordinal)) || !own.TryGetValue(name, out var fp)) continue;
            double hx = (facing % 2 == 0 ? fp.X : fp.Z) * 8, hz = (facing % 2 == 0 ? fp.Z : fp.X) * 8;
            double lo = double.MaxValue, hi = double.MinValue;
            for (double dz = -hz; dz <= hz; dz += 8)
                for (double dx = -hx; dx <= hx; dx += 8)
                {
                    double y = heights.HeightAt(at.X + dx, at.Z + dz);
                    lo = Math.Min(lo, y); hi = Math.Max(hi, y);
                }
            tilt = Math.Max(tilt, hi - lo);
            if (hi - lo > 12) tilted.Add($"{name} at ({at.X:0}, {at.Z:0}) ({hi - lo:0})");
        }
        Expect(tilted.Count == 0, $"every machine, building, tank and piece of site gear stands near level (but the tipping trucks): the ground rises no more than 12 under its footprint (the most {tilt:0.0}){List(tilted)}");

        log("Metal spots and starts:");
        int crowded = spots.Count(s => Enumerable.Range(-5, 11).Any(dz => Enumerable.Range(-5, 11).Any(dx =>
        {
            int x = (int)(s.At.X / 8) + dx, z = (int)(s.At.Z / 8) + dz;
            return x >= 0 && z >= 0 && x < w && z < h && items[z * w + x];
        })));
        Expect(crowded == 0, $"no blocking feature within 40 of any of the {spots.Count} metal spots ({crowded} crowded)");
        double worst = spots.Select(s => s.At).Append(t.Vent).Max(s => Rise(heights, s, 16));
        Expect(worst <= 16, $"every spot and the vent is level enough to build on (the most any rises over its footprint: {worst:0.0})");
        Expect(starts.All(s => heights.HeightAt(s.X, s.Z) > 4 && Rise(heights, s, 48) < 30), "every start is on dry, nearly level ground");
        return failures;
    }

    /// <summary>Squares within a radius of a point that are nearly level and free of features.</summary>
    static double Flat(HeightMap hm, bool[] items, int w, int h, P c, double radius)
    {
        int count = 0;
        for (double z = c.Z - radius; z <= c.Z + radius; z += 16)
            for (double x = c.X - radius; x <= c.X + radius; x += 16)
            {
                if ((new P(x, z) - c).Length > radius || x < 8 || z < 8 || x > Layout.Size - 8 || z > Layout.Size - 8) continue;
                int sx = (int)(x / 8), sz = (int)(z / 8);
                if (items[Math.Clamp(sz, 0, h - 1) * w + Math.Clamp(sx, 0, w - 1)]) continue;
                double rise = Math.Abs(hm.HeightAt(x + 8, z) - hm.HeightAt(x - 8, z)) + Math.Abs(hm.HeightAt(x, z + 8) - hm.HeightAt(x, z - 8));
                if (rise < 4) count++;
            }
        return count;
    }

    /// <summary>Driving distance (in 8-unit squares, 8-connected, a diagonal √2) from the nearest of the starts to every
    /// square; -1 where unreached. Diagonals count their true length, so a road running off the grid's axes (the pit
    /// is tilted) costs what it would in the game, not up to 41% more.</summary>
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
                    if (dx != 0 && dz != 0 && !(open(cx + dx, cz) && open(cx, cz + dz))) continue;   // no cutting corners
                    int ni = nz * w + nx;
                    double nd = dc + (dx != 0 && dz != 0 ? Math.Sqrt(2) : 1);
                    if (done[ni] || (d[ni] >= 0 && d[ni] <= nd)) continue;
                    d[ni] = nd;
                    queue.Enqueue(ni, nd);
                }
        }
        return d;
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
