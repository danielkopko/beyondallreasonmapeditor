// The checks, by the engine's rules (MapCore.Movement: per 8-unit square, the slope of its 16-unit cell, and the
// water depth) and round every blocking feature's footprint (16-unit footprint units, turned to its facing, and 8 more
// round it). A feature blocks a class only if the class can't crush it (its crush resistance over the class's crush
// strength: vehicles 30, bots 25, hovers 25), so tanks drive through the wire and the hedge bushes and bots don't.
// Ships aren't a MapCore class: a square is theirs when its every corner is deep enough (8 for the boats' movement
// classes, 15 for the battleship's file). The map isn't symmetric, so its fairness is measured: each start against its
// opposite number (0 against 4, 1 against 5 and so on).
using System.Globalization;
using MapCore.Heights;
using MapCore.Movement;

static class Check
{
    public static Dictionary<string, (int X, int Z, bool Blocks, int Crush, int Metal)> Footprints(IEnumerable<FeatureDef> defs) =>
        defs.ToDictionary(d => d.Name, d => (d.Footprint.X, d.Footprint.Z, d.Blocking, d.Landmark ? 1_000_000 : d.Crush ?? 1_000_000, d.Metal));

    public static int Report(HeightMap heights, Terrain.Result t, IEnumerable<string> ops, Dictionary<string, (int X, int Z, bool Blocks, int Crush, int Metal)> own, Action<string> log)
    {
        int failures = 0;
        void Expect(bool ok, string what)
        {
            log($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }
        var vehicle = new PassabilityMap(heights, BarMoveClasses.Vehicle);
        var bot = new PassabilityMap(heights, BarMoveClasses.Bot);
        var hover = new PassabilityMap(heights, BarMoveClasses.Hover);
        int w = vehicle.Width, h = vehicle.Height;
        var forVehicles = new bool[w * h];
        var forBots = new bool[w * h];
        var anyBlock = new bool[w * h];
        var placed = new List<(string Name, P At, int Facing)>();
        foreach (var line in ops.Where(l => l.StartsWith("item-add:placer:", StringComparison.Ordinal)))
        {
            var f = line.Split(':');
            var xz = f[3].Split(',');
            var at = new P(double.Parse(xz[0], CultureInfo.InvariantCulture), double.Parse(xz[1], CultureInfo.InvariantCulture));
            int facing = (int)Math.Round(double.Parse(f[4], CultureInfo.InvariantCulture) / 16384) % 4;
            placed.Add((f[2], at, facing));
            if (!own.TryGetValue(f[2], out var fp) || !fp.Blocks) continue;
            double hx = (facing % 2 == 0 ? fp.X : fp.Z) * 8, hz = (facing % 2 == 0 ? fp.Z : fp.X) * 8;
            Mark(anyBlock, w, h, at.X, at.Z, hx + 8, hz + 8);
            if (fp.Crush > 30) Mark(forVehicles, w, h, at.X, at.Z, hx + 8, hz + 8);
            if (fp.Crush > 25) Mark(forBots, w, h, at.X, at.Z, hx + 8, hz + 8);
        }
        log($"{placed.Count} features; blocking {anyBlock.Count(b => b) * 100.0 / anyBlock.Length:0.0}% of the map with the room round them " +
            $"({forVehicles.Count(b => b) * 100.0 / anyBlock.Length:0.0}% for vehicles, which crush the wire and hedge bushes)");

        var beach = Layout.StartsOf("beach").ToArray();
        var hill = Layout.StartsOf("hill").ToArray();
        bool Has(bool[] reach, P p, int r = 3)
        {
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = (int)(p.X / 8) + dx, z = (int)(p.Z / 8) + dz;
                    if (x >= 0 && z >= 0 && x < w && z < h && reach[z * w + x]) return true;
                }
            return false;
        }
        bool[] Shut(bool[] blocked, Func<P, bool> shut)
        {
            var m = (bool[])blocked.Clone();
            Parallel.For(0, h, z => { for (int x = 0; x < w; x++) if (shut(new P(x * 8 + 4, z * 8 + 4))) m[z * w + x] = true; });
            return m;
        }

        // ---- Getting about.
        log("Getting about (vehicles: TANK3, 27 degrees; bots: BOT3, 54 degrees; hovers: HOVER3, 33 on land, any slope on water), round the features:");
        var vAll = Access.Fill(vehicle, beach[1], forVehicles);
        var bAll = Access.Fill(bot, beach[1], forBots);
        Expect(beach.Concat(hill).All(s => Has(vAll, s)), $"vehicles reach all eight starts ({vAll.Count(b => b) * 100.0 / vAll.Length:0.0}% of the map)");
        var cutV = Layout.Spots.Where(s => !Has(vAll, s.At)).ToList();
        Expect(cutV.Count == 0, $"vehicles reach every metal spot{(cutV.Count > 0 ? $" (not {string.Join(", ", cutV.Select(c => $"({c.At.X:0}, {c.At.Z:0})"))})" : "")}");
        Expect(Layout.Vents.All(v => Has(vAll, v.At)), "vehicles reach every vent");
        Expect(Layout.Spots.All(s => Has(bAll, s.At)), "bots reach every metal spot");

        // ---- The trenches.
        log("The trenches (the fire, support and communication trenches' floors, sampled along them):");
        var floors = new List<(string What, P At)>();
        foreach (var (side, fn, sgn) in new (string, Func<double, double>, double)[] { ("beach", Layout.FireA, 1), ("hill", Layout.FireB, -1) })
            foreach (var (z0, z1) in Layout.FireRuns[side])
                for (double z = z0 + 60; z < z1 - 60; z += 150)
                {
                    floors.Add(($"{side} fire", new P(fn(z) + sgn * (Terrain.Crenel(z, z0) + 14), z)));
                    floors.Add(($"{side} support", new P(fn(z) - sgn * Layout.SupportBack + sgn * 14, z)));
                }
        foreach (var (side, line) in Layout.CommTrenches)
            floors.Add(($"{side} communication", line[line.Count / 2]));
        floors = floors.Where(f => Terrain.Sample(t.Trench, t.N, f.At) > 20).ToList();
        var hAll = Access.Fill(hover, beach[1], forBots);
        var outB = floors.Where(f => !Has(bAll, f.At, 1)).ToList();
        Expect(outB.Count == 0, $"bots get into every trench ({floors.Count} floors sampled){(outB.Count > 0 ? $"; not {string.Join(", ", outB.Take(5).Select(f => $"{f.What} ({f.At.X:0}, {f.At.Z:0})"))}" : "")}");
        var inV = floors.Where(f => Has(vAll, f.At, 0)).ToList();
        Expect(inV.Count == 0, $"vehicles get into none{(inV.Count > 0 ? $" (they do at {string.Join(", ", inV.Take(4).Select(f => $"{f.What} ({f.At.X:0}, {f.At.Z:0})"))})" : "")}");
        var inH = floors.Where(f => Has(hAll, f.At, 0)).ToList();
        Expect(inH.Count == 0, $"hovers get into none{(inH.Count > 0 ? $" (they do at {string.Join(", ", inH.Take(4).Select(f => $"{f.What} ({f.At.X:0}, {f.At.Z:0})"))})" : "")}");
        Expect(floors.All(f => heights.HeightAt(f.At.X, f.At.Z) > 2), $"no trench floor is under water (the lowest {floors.Min(f => heights.HeightAt(f.At.X, f.At.Z)):0.0})");

        bool InCrossing(P p, Func<double, double> fn, double g) => Math.Abs(p.Z - g) < Layout.CrossGap / 2 + 24 && Math.Abs(p.X - fn(g)) < 340;
        bool InletWater(P p) => Layout.InWater(p.X, p.Z, 10);
        var zoneMid = new P(Layout.ZoneMid(6000), 6000);
        foreach (var (side, fn, gaps, from) in new (string, Func<double, double>, double[], P)[] { ("beach team's", Layout.FireA, Layout.CrossA, beach[1]), ("hill team's", Layout.FireB, Layout.CrossB, hill[1]) })
        {
            var shut = Shut(forVehicles, p => gaps.Any(g => InCrossing(p, fn, g)));
            Expect(!Has(Access.Fill(vehicle, from, shut), zoneMid), $"with the {side} four crossings shut, no vehicle crosses its line");
            Expect(Has(Access.Fill(bot, from, Shut(forBots, p => gaps.Any(g => InCrossing(p, fn, g)))), zoneMid), $"with them shut, bots still cross it");
            var shutH = Shut(forBots, p => gaps.Any(g => InCrossing(p, fn, g)));
            Expect(!Has(Access.Fill(hover, from, Shut(shutH, p => heights.HeightAt(p.X, p.Z) < 0.5)), zoneMid),
                $"with them shut and every stretch of water shut, no hover crosses it over land (with the water open, hovers {(Has(Access.Fill(hover, from, shutH), zoneMid) ? "get round by the inlet or the sea" : "don't get round")})");
            foreach (var g in gaps)
            {
                var one = Shut(forVehicles, p => gaps.Any(o => o != g && InCrossing(p, fn, o)));
                Expect(Has(Access.Fill(vehicle, from, one), zoneMid), $"the {side} crossing at z {g:0} alone lets vehicles across its line");
            }
        }

        // ---- The beach and its draws.
        log("The beach (from the beach's middle, x a third of the way up it):");
        bool InDraw(P p, double dz) => Math.Abs(p.Z - dz) < Layout.DrawW / 2 + 60 && p.X > Layout.BermX(p.Z) - 40 && p.X < Layout.BermX(p.Z) + Layout.ForeduneWidth(p.Z) + 60;
        var hroad = Geo.Smooth(Layout.Roads[3].Line, 20);
        bool OnHeadlandRoad(P p) => Geo.ToPolyline(p, hroad, out double along) < 120 && along < 900;
        // The inlet's north bank by its mouth: the beach runs round into the inlet, and its bank is a fourth way up.
        bool MouthBank(P p) => p.X < Layout.BermX(p.Z) + Layout.ForeduneWidth(p.Z) + 500 && Layout.InletAt(p.X, p.Z) is { } a && a.Side < 0 && a.D < a.W + 280;
        bool OtherWays(P p) => OnHeadlandRoad(p) || MouthBank(p);
        foreach (var dz in Layout.Draws)
        {
            var onBeach = new P(Layout.Coast(dz) + 0.35 * Layout.BeachWidth(dz), dz);
            var others = Shut(forVehicles, p => OtherWays(p) || Layout.Draws.Any(o => o != dz && InDraw(p, o)));
            Expect(Has(Access.Fill(vehicle, onBeach, others), beach[1]), $"the draw at z {dz:0} alone takes vehicles from the beach to the dunes");
        }
        {
            var dz = Layout.Draws[1];
            var onBeach = new P(Layout.Coast(dz) + 0.35 * Layout.BeachWidth(dz), dz);
            Expect(Has(Access.Fill(vehicle, onBeach, Shut(forVehicles, p => MouthBank(p) || Layout.Draws.Any(o => InDraw(p, o)))), beach[0]), "so does the headland road alone (to the heath's start)");
            Expect(Has(Access.Fill(vehicle, onBeach, Shut(forVehicles, p => OnHeadlandRoad(p) || Layout.Draws.Any(o => InDraw(p, o)))), beach[3]), "and the inlet's north bank by its mouth alone");
            var none = Shut(forVehicles, p => OtherWays(p) || Layout.Draws.Any(o => InDraw(p, o)));
            Expect(!Has(Access.Fill(vehicle, onBeach, none), beach[1]), "with those five shut, vehicles stay on the beach");
            Expect(Has(Access.Fill(bot, onBeach, Shut(forBots, p => OtherWays(p) || Layout.Draws.Any(o => InDraw(p, o)))), beach[1]), "and bots still climb the foredune");
        }

        // ---- The craters and the dunes.
        log("The ground:");
        int bombs = 0, bombsOk = 0, holes = 0, holesOk = 0;
        var oddBombs = new List<string>();
        var oddHoles = new List<string>();
        foreach (var (at, r, depth, deep, _) in Layout.Craters)
        {
            if (Terrain.Sample(t.Crater, t.N, at) < 0.5) continue;
            bool v = Has(vAll, at, 1), b = Has(bAll, at, 1);
            if (deep) { bombs++; if (!v && b) bombsOk++; else oddBombs.Add($"({at.X:0}, {at.Z:0}) r {r:0}{(v ? " vehicles in" : "")}{(b ? "" : " bots not")}"); }
            else if (heights.HeightAt(at.X, at.Z) > -18) { holes++; if (v) holesOk++; else oddHoles.Add($"({at.X:0}, {at.Z:0}) r {r:0} floor {heights.HeightAt(at.X, at.Z):0}"); }
        }
        Expect(bombsOk == bombs, $"bomb craters are bots' only: bots get into all {bombs}, vehicles into {bombs - bombsOk} ({bombsOk} as planned){(oddBombs.Count > 0 ? "; not " + string.Join(", ", oddBombs.Take(5)) : "")}");
        Expect(holesOk >= holes * 0.95, $"shell holes let vehicles in: {holesOk} of {holes}{(oddHoles.Count > 0 ? "; not " + string.Join(", ", oddHoles.Take(6)) : "")}");
        int slip = 0, slipSteep = 0;
        for (int z = 0; z < h; z += 2)
            for (int x = 0; x < w; x += 2)
                if (t.Slip[Math.Min(z, t.N - 1) * t.N + Math.Min(x, t.N - 1)] > 0.5 && t.Dune[Math.Min(z, t.N - 1) * t.N + Math.Min(x, t.N - 1)] > 6)
                {
                    slip++;
                    if (vehicle[x, z] == Passage.TooSteep) slipSteep++;
                }
        log($"       the dunes' slip faces: {slipSteep * 100.0 / Math.Max(1, slip):0}% of their squares too steep for vehicles (the rest is their crests and feet)");
        int cliff = 0, cliffSteep = 0;
        for (int z = 0; z < h; z++)
        {
            double zz = z * 8 + 4;
            if (Layout.Headland(zz) < 0.7) continue;
            double b0 = Layout.BermX(zz);
            for (int x = (int)((b0 + 6) / 8); x < (b0 + Terrain.CliffRun - 6) / 8; x++)
            {
                cliff++;
                if (bot[x, z] == Passage.TooSteep) cliffSteep++;
            }
        }
        Expect(cliffSteep >= cliff * 0.85, $"the headland's cliffs stop bots: {cliffSteep * 100.0 / Math.Max(1, cliff):0}% of their face's squares too steep (spiders only)");

        // ---- The water.
        log("The water:");
        var offshore = new P(200, 6000);
        bool[] Ships(double depth)
        {
            var deep = new bool[w * h];
            Parallel.For(0, h, z =>
            {
                for (int x = 0; x < w; x++)
                {
                    double y = Math.Max(Math.Max(heights.HeightAt(x * 8, z * 8), heights.HeightAt(x * 8 + 8, z * 8)), Math.Max(heights.HeightAt(x * 8, z * 8 + 8), heights.HeightAt(x * 8 + 8, z * 8 + 8)));
                    deep[z * w + x] = y <= -depth;
                }
            });
            return Flood(deep, w, h, offshore);
        }
        var cove = Layout.InletPts[^1];
        var ships15 = Ships(15);
        Expect(Has(ships15, new P(cove.X, cove.Z), 2), "a battleship (15 deep) sails from the open sea up the inlet into the cove");
        var ships8 = Ships(8);
        Expect(Has(ships8, new P(cove.X, cove.Z), 2), "and so does every smaller ship (8)");
        // The trough between the beach and a bar, 280 out: about 9 deep, so only ships needing 8 fit, and only by a rip.
        int trough = 0, behind = 0;
        for (double z = 500; z < 10200; z += 200)
        {
            if (!Terrain.BarOn(z)) continue;
            var p = new P(Layout.Coast(z) - 280, z);
            if (heights.HeightAt(p.X, p.Z) > -8) continue;
            trough++;
            if (Has(ships8, p, 0)) behind++;
        }
        log($"       inside the bars (280 out, 8 deep or more at {trough} places sampled), ships needing 8 reach {behind} through the rips; the bars themselves are 5 deep");
        var sand = Layout.Spots.Where(s => Layout.SandyEnd(s.At.X, s.At.Z)).Select(s => s.At).ToList();
        var hillShut = Shut(forVehicles, p => Layout.CrossB.Any(g => InCrossing(p, Layout.FireB, g)));
        Expect(sand.Count > 0 && !sand.Any(p => Has(Access.Fill(vehicle, beach[3], hillShut), p)),
            "no vehicle crosses the inlet: with the hill team's crossings shut, the beach team's vehicles can't reach the sandy end");
        Expect(sand.All(p => Has(Access.Fill(vehicle, hill[3], forVehicles), p)), "the hill team's vehicles reach the sandy end by land");

        // ---- Fair shares.
        log("Fair shares (driving distance, vehicles, round the features; each start against its opposite number):");
        var distB = new double[4][];
        var distH = new double[4][];
        for (int i = 0; i < 4; i++) { distB[i] = Steps(vehicle, [beach[i]], forVehicles); distH[i] = Steps(vehicle, [hill[i]], forVehicles); }
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
        void Alike(string what, double a, double b, double within = 0.10)
        {
            double ratio = Math.Max(a, b) / Math.Min(a, b);
            Expect(!double.IsNaN(ratio) && ratio <= 1 + within, $"{what}: beach {a:0}, hill {b:0} ({(ratio - 1) * 100:0}% apart)");
        }
        double ToZone(double[] d) => Enumerable.Range(0, (int)(Layout.Size / 64)).Select(k => At(d, new P(Layout.ZoneMid(k * 64), k * 64), 2)).Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Min();
        double ToCrossing(double[] d, Func<double, double> fn, double[] gaps) => gaps.Select(g => At(d, new P(fn(g), g), 4)).Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Min();
        for (int i = 0; i < 4; i++)
        {
            Alike($"start {i} against {i + 4}, to the zone's middle", ToZone(distB[i]), ToZone(distH[i]));
            Alike($"start {i} against {i + 4}, to their own nearest crossing", ToCrossing(distB[i], Layout.FireA, Layout.CrossA), ToCrossing(distH[i], Layout.FireB, Layout.CrossB), 0.25);
        }
        double metalB = Layout.Spots.Where(s => s.Side == "beach").Sum(s => s.Income), metalH = Layout.Spots.Where(s => s.Side == "hill").Sum(s => s.Income);
        Expect(Math.Abs(metalB - metalH) < 0.01, $"their own metal: beach {metalB:0.0}, hill {metalH:0.0} metal/s, and {Layout.Spots.Where(s => s.Side == "zone").Sum(s => s.Income):0.0} in the zone");
        double reclaimB = 0, reclaimH = 0, reclaimZone = 0;
        var allB = Steps(vehicle, beach, forVehicles);
        var allH = Steps(vehicle, hill, forVehicles);
        foreach (var (name, at, _) in placed)
        {
            if (!own.TryGetValue(name, out var fp) || fp.Metal <= 0) continue;
            double a = At(allB, at, 12), b = At(allH, at, 12);
            if (name.StartsWith("ml_tank")) { reclaimZone += fp.Metal; continue; }
            if (double.IsNaN(b) || (!double.IsNaN(a) && a < b)) reclaimB += fp.Metal; else reclaimH += fp.Metal;
        }
        Alike("metal to reclaim on their own side (the zone's tanks apart)", reclaimB, reclaimH, 0.15);
        log($"       the zone's tanks: {reclaimZone:0} metal");
        double Room(P[] s) => s.Sum(st => Flat(heights, anyBlock, w, h, st, 900));
        Alike("level ground to build on within 900 of their starts (squares)", Room(beach), Room(hill), 0.15);

        // ---- Room to build.
        log("Room to build:");
        int crowded = Layout.Spots.Count(s => placed.Any(p => own.TryGetValue(p.Name, out var fp) && fp.Blocks && (p.At - s.At).Length < 40 + Math.Max(fp.X, fp.Z) * 8));
        Expect(crowded == 0, $"no blocking feature within 40 of any of the {Layout.Spots.Count} metal spots ({crowded} crowded)");
        var worstSpot = Layout.Spots.MaxBy(s => Rise(heights, s.At, 24));
        double worst = Rise(heights, worstSpot.At, 24);
        Expect(worst <= 6, $"every spot is level enough to build on (the most any rises over its footprint: {worst:0.0}, at ({worstSpot.At.X:0}, {worstSpot.At.Z:0}))");
        double worstStart = beach.Concat(hill).Max(s => Rise(heights, s, 48));
        Expect(worstStart <= 12 && beach.Concat(hill).All(s => heights.HeightAt(s.X, s.Z) > 4), $"every start is on dry, nearly level ground (the most any rises over 96: {worstStart:0.0})");
        var standing = placed.Where(p => own.TryGetValue(p.Name, out var fp) && fp.Blocks && fp.Crush >= 1_000_000 && !p.Name.StartsWith("ml_teeth") && !p.Name.StartsWith("ml_tank") && !p.Name.StartsWith("ml_craft") && !p.Name.StartsWith("ml_boulder")).ToList();
        var tilted = standing.Where(p => Rise(heights, p.At, Math.Max(own[p.Name].X, own[p.Name].Z) * 8) > 16).ToList();
        Expect(tilted.Count == 0, $"every building and bunker ({standing.Count}) stands near level: the ground rises no more than 16 under its footprint{(tilted.Count > 0 ? $" ({tilted.Count} tilted: {string.Join(", ", tilted.Take(5).Select(p => $"{p.Name} at ({p.At.X:0}, {p.At.Z:0}) {Rise(heights, p.At, Math.Max(own[p.Name].X, own[p.Name].Z) * 8):0}"))})" : "")}");
        return failures;
    }

    static double Rise(HeightMap hm, P c, double half)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        for (double z = c.Z - half; z <= c.Z + half; z += 8)
            for (double x = c.X - half; x <= c.X + half; x += 8)
            {
                double y = hm.HeightAt(Math.Clamp(x, 0, Layout.Size), Math.Clamp(z, 0, Layout.Size));
                lo = Math.Min(lo, y); hi = Math.Max(hi, y);
            }
        return hi - lo;
    }

    static double Flat(HeightMap hm, bool[] items, int w, int h, P c, double radius)
    {
        int count = 0;
        for (double z = c.Z - radius; z <= c.Z + radius; z += 16)
            for (double x = c.X - radius; x <= c.X + radius; x += 16)
            {
                if ((new P(x, z) - c).Length > radius || x < 8 || z < 8 || x > Layout.Size - 8 || z > Layout.Size - 8) continue;
                int sx = (int)(x / 8), sz = (int)(z / 8);
                if (items[Math.Clamp(sz, 0, h - 1) * w + Math.Clamp(sx, 0, w - 1)]) continue;
                double y = hm.HeightAt(x, z);
                double rise = Math.Abs(hm.HeightAt(x + 8, z) - hm.HeightAt(x - 8, z)) + Math.Abs(hm.HeightAt(x, z + 8) - hm.HeightAt(x, z - 8));
                if (rise < 6 && y > 2) count++;
            }
        return count;
    }

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

    /// <summary>The squares of a mask joined to a point (4-connected).</summary>
    static bool[] Flood(bool[] open, int w, int h, P from)
    {
        var seen = new bool[w * h];
        int s = (int)(from.Z / 8) * w + (int)(from.X / 8);
        if (!open[s]) return seen;
        var q = new Queue<int>();
        seen[s] = true; q.Enqueue(s);
        while (q.Count > 0)
        {
            int c = q.Dequeue(), cx = c % w, cz = c / w;
            foreach (var (nx, nz) in new[] { (cx + 1, cz), (cx - 1, cz), (cx, cz + 1), (cx, cz - 1) })
            {
                if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                int ni = nz * w + nx;
                if (seen[ni] || !open[ni]) continue;
                seen[ni] = true; q.Enqueue(ni);
            }
        }
        return seen;
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
