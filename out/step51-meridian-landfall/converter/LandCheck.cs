// Meridian Landfall's checks, by the engine's rules (MapCore.Movement) round every blocking feature's footprint (16-unit
// footprint units, turned to its facing, and 8 more round it):
//   the halves match, heights and features; every start, spot and vent is reached (the bowls' by bots); each of the
//   seven lanes alone (the ship's five crossings, round the bow, round the stern) lets vehicles across, and with all
//   seven shut no ground class but spiders crosses (over the ship's bulkheads); no ground class reaches the void,
//   spiders included; none but spiders gets onto the ship's solid tops from outside; vehicles and hovers never get into
//   a crater and bots get into every one; the bowls' walls stay in the bots' band; no crater's outline is a circle;
//   doors are wide and clear, the start rooms, mares and aprons level and free; every machine stands level.
using System.Globalization;
using MapCore.Heights;
using MapCore.Movement;

static class LandCheck
{
    public static int Report(HeightMap heights, Asteroid.Land land, IEnumerable<string> ops, Dictionary<string, (int X, int Z, bool Blocks)> own, Action<string> log)
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
        var ground = new[] { ("vehicles", vehicle), ("bots", bot), ("hovers", hover), ("amphibious units", amphibious), ("spiders", spider) };
        int w = vehicle.Width, h = vehicle.Height;

        // The features: their footprints marked (and 8 round them).
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
            var (hx, hz) = Half(fp, heading);
            Mark(items, w, h, at.X, at.Z, hx + 8, hz + 8);
        }
        log($"{laid.Count} features ({laid.Count(l => l.Name == "lf_void")} of them the void's rubble), {items.Count(b => b) * 100.0 / items.Length:0.0}% of the map blocked with the room round them.");

        P Centre(int i) => new((i % w) * 8 + 4, (i / w) * 8 + 4);
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
            double most = 0;
            for (int z = 0; z < World.NZ; z++)
                for (int x = 0; x < World.NX / 2; x++)
                    most = Math.Max(most, Math.Abs(heights.HeightAt(x * 8, z * 8) - heights.HeightAt(World.W - x * 8, z * 8)));
            Expect(most < 0.01, $"the heights match their mirror image at every vertex (the most apart {most:0.000})");
            var index = laid.GroupBy(l => (Math.Round(l.At.X), Math.Round(l.At.Z))).ToDictionary(g => g.Key, g => g.ToList());
            var unmatched = new List<string>();
            foreach (var (name, at, heading) in laid)
            {
                if (Math.Abs(at.X - World.CX) < 1) continue;
                string twin = name.EndsWith("_m", StringComparison.Ordinal) ? name[..^2] : name + "_m";
                int want = ((32768 - heading) % 65536 + 65536) % 65536;
                bool found = index.TryGetValue((Math.Round(World.W - at.X), Math.Round(at.Z)), out var there) && there.Any(o =>
                    (o.Name == name || o.Name == twin) && Math.Min(Math.Abs(o.Heading - want), 65536 - Math.Abs(o.Heading - want)) <= 2);
                if (!found) unmatched.Add($"{name} at ({at.X:0}, {at.Z:0})");
            }
            Expect(unmatched.Count == 0, $"every feature has its mirror image (the same model, or its twin, at the mirrored place and heading){List(unmatched)}");
        }

        log("The ship:");
        {
            // Meridian's own heights, raised: they differ only in the bay doors and by the bridge-level floors' walls.
            var ship = land.Ship;
            int changed = 0, unexplained = 0;
            var odd = new List<string>();
            for (int z = 0; z < World.NZ; z++)
                for (int x = 0; x < World.NX; x++)
                {
                    int si = land.ShipIndex(x, z);
                    if (si < 0 || ship.Cell[si] == Terrain.Space) continue;
                    int i = z * World.NX + x;
                    if (Math.Abs(land.H[i] - (ship.H[si] + World.HY)) < 0.01) continue;
                    changed++;
                    if (land.Kind[i] == Asteroid.BayDoor || NearHighFloor(ship, si)) continue;
                    if (unexplained++ < 12) odd.Add($"({x * 8}, {z * 8})");
                }
            Expect(unexplained == 0, $"the ship is Meridian's, raised {World.HY:0}: {changed} of its vertices differ, all of them in the bay doors or the walls by the bridge-level floors{List(odd)}");
        }

        log("Getting about (vehicles TANK3 27°, bots BOT3 54°, hovers 33°, amphibious ATANK3 54°, spiders TBOT3 90°), round the features:");
        var starts = World.Starts.Select(s => s.At).ToArray();
        var cars = Access.Fill(vehicle, starts[0], items);
        var bots = Access.Fill(bot, starts[0], items);
        Expect(starts.All(s => Has(cars, s)), $"vehicles reach all 16 starts ({cars.Count(b => b) * 100.0 / cars.Length:0.0}% of the map)");
        var spotsCut = World.Spots.Where(s => s.Side != "bowl" && !Has(cars, s.At)).Select(s => $"({s.At.X:0}, {s.At.Z:0})")
            .Concat(World.Vents.Where(v => !InCrater(v) && !Has(cars, v)).Select(v => $"vent ({v.X:0}, {v.Z:0})")).ToList();
        Expect(spotsCut.Count == 0, $"vehicles reach every spot and vent but those on the bowls' floors{List(spotsCut)}");
        var botsCut = World.Spots.Where(s => !Has(bots, s.At)).Select(s => $"({s.At.X:0}, {s.At.Z:0})").Concat(World.Vents.Where(v => !Has(bots, v)).Select(v => $"vent ({v.X:0}, {v.Z:0})")).ToList();
        Expect(starts.All(s => Has(bots, s)) && botsCut.Count == 0, $"bots reach every start, spot and vent, the bowls' included{List(botsCut)}");
        var doorsCut = new List<string>();
        for (int k = 0; k < 4; k++)
        {
            var outside = starts[4 + k];
            var alone = Shut(p => land.ShipCell(p) != Terrain.Space && !World.BayDoors.Any(d => d.Has(p, 24)) || LaneOf(land, p) is "bow" or "stern");
            var reach = Access.Fill(vehicle, outside, alone);
            foreach (var d in World.BayDoorsPort)
                if (!Has(reach, d.Centre)) doorsCut.Add($"start {4 + k} to the bay door at z {d.Centre.Z:0}");
        }
        Expect(doorsCut.Count == 0, $"each port outside start reaches both its team's bay doors by vehicle, without going round the bow or the stern (starboard's are the mirror image){List(doorsCut)}");

        log("The lanes (the ship's five crossings, round the bow and round the stern):");
        string[] lanes = [.. Layout.Lanes, "bow", "stern"];
        P from = starts[5], to = starts[13];
        foreach (var lane in lanes)
        {
            var shut = Shut(p => LaneOf(land, p) is string l && l != lane);
            Expect(Has(Access.Fill(vehicle, from, shut), to), $"the {lane} alone lets vehicles across (outside start 5 to 13)");
        }
        var none = Shut(p => LaneOf(land, p) != null);
        foreach (var (name, map) in ground.Take(4))
            Expect(!Has(Access.Fill(map, from, none), to), $"with all seven shut, {name} can't cross");
        log($"       spiders, all seven shut: {(Has(Access.Fill(spider, from, none), to) ? "they cross, over the ship's bulkheads (all-terrain bots climb any wall; intended, as on Meridian)" : "they don't cross")}");

        log("The void (the pits and the asteroid's edge) and the ship's solid tops:");
        foreach (var (name, map) in ground)
        {
            var reach = Access.Fill(map, starts[0], items);
            int inVoid = 0;
            for (int i = 0; i < reach.Length; i++) if (reach[i] && land.VoidAt(Centre(i)) < 0) inVoid++;
            Expect(inVoid == 0, $"{name} don't reach the void ({inVoid} squares)");
        }
        foreach (var (name, map) in ground)
        {
            var reach = Access.Fill(map, starts[4], items);
            int onTop = 0;
            var where = new List<string>();
            for (int i = 0; i < reach.Length; i++)
                if (reach[i] && land.ShipCell(Centre(i)) == Terrain.Solid && !land.ShipFloor(Centre(i)) && heights.HeightAt(Centre(i).X, Centre(i).Z) > World.Plain + 90)
                {
                    if (onTop++ % 4000 == 0) where.Add($"({Centre(i).X:0}, {Centre(i).Z:0})");
                }
            if (name == "spiders") log($"       spiders reach {onTop} squares of the ship's solid tops (they climb its walls; intended)");
            else Expect(onTop == 0, $"{name} from outside don't get onto the ship's walls and solid tops ({onTop} squares){List(where)}");
        }

        log("Craters:");
        {
            var all = World.Bowls.Concat(World.Small).SelectMany(c => new[] { c, c with { At = World.Mirror(c.At) } }).ToList();
            var hovers = Access.Fill(hover, starts[0], items);
            var amph = Access.Fill(amphibious, starts[0], items);
            var carIn = all.Where(c => Has(cars, c.At)).Select(c => $"({c.At.X:0}, {c.At.Z:0})").ToList();
            var hoverIn = all.Where(c => Has(hovers, c.At)).Select(c => $"({c.At.X:0}, {c.At.Z:0})").ToList();
            var botOut = all.Where(c => !Has(bots, c.At)).Select(c => $"({c.At.X:0}, {c.At.Z:0}) r {c.R:0}").ToList();
            Expect(carIn.Count == 0, $"vehicles get into none of the {all.Count} craters (no ramps){List(carIn)}");
            Expect(hoverIn.Count == 0, $"hovers get into none of them{List(hoverIn)}");
            Expect(botOut.Count == 0, $"bots get into (and out of) every one{List(botOut)}");
            log($"       amphibious tanks (54°, as bots) get into {all.Count(c => Has(amph, c.At))} of them");
            double lo = 90, hi = 0;
            P? worstAt = null;
            foreach (var c in World.Bowls)
            {
                double wall = (c.Depth + c.Rim) / Asteroid.BowlSlope(c);
                for (int k = 0; k < 96; k++)
                {
                    double a = k * 2 * Math.PI / 96, s = World.Waver(c.R, c.Key, a) / c.R;
                    for (double r = (c.R - wall) * s + 28; r <= c.R * s - 28; r += 8)
                    {
                        var p = c.At + new P(Math.Cos(a), Math.Sin(a)) * r;
                        double gx = (heights.HeightAt(p.X + 8, p.Z) - heights.HeightAt(p.X - 8, p.Z)) / 16, gz = (heights.HeightAt(p.X, p.Z + 8) - heights.HeightAt(p.X, p.Z - 8)) / 16;
                        double deg = Math.Atan(Math.Sqrt(gx * gx + gz * gz)) * 180 / Math.PI;
                        if (deg < 36 || deg > 48) worstAt ??= p;
                        lo = Math.Min(lo, deg); hi = Math.Max(hi, deg);
                    }
                }
            }
            Expect(lo >= 36 && hi <= 48, $"the bowls' walls stay between 36° and 48° all round, on 16-unit cells ({lo:0.0}° to {hi:0.0}°{(worstAt is P q ? $", the worst by ({q.X:0}, {q.Z:0})" : "")})");
            var round = World.Bowls.Concat(World.Small).Select(c => (c, Spread(a => World.Waver(c.R, c.Key, a))))
                .Concat(World.Pits.Select(c => (c, Spread(a => World.Jag(c.R, c.Key, a))))).Where(x => x.Item2 < 0.06).Select(x => $"({x.c.At.X:0}, {x.c.At.Z:0}) {x.Item2:P0}").ToList();
            Expect(round.Count == 0, $"no crater's or pit's outline is a circle: each strays from its mean radius by 6% or more{List(round)}");
        }

        log("Doors, rooms, mares and aprons:");
        var shipDoors = Layout.Doors.Select(d => (World.FromShip(new P(d.X0, d.Z0)), World.FromShip(new P(d.X1, d.Z1)), d.Kind)).ToList();
        var narrow = Layout.Doors.Where(d => d.Kind != Layout.DoorKind.Stairs && Math.Max(d.X1 - d.X0, d.Z1 - d.Z0) < 500).Select(d => $"({d.X0:0}, {d.Z0:0})")
            .Concat(World.BayDoors.Where(b => b.Z1 - b.Z0 < 500).Select(b => $"bay door ({b.X0:0}, {b.Z0:0})")).ToList();
        Expect(narrow.Count == 0, $"every door is at least 500 wide, the bay doors {World.BayDoorsPort[0].Z1 - World.BayDoorsPort[0].Z0:0}{List(narrow)}");
        bool Blocked(P a, P b)
        {
            for (double z = a.Z; z <= b.Z; z += 8)
                for (double x = a.X; x <= b.X; x += 8)
                {
                    int sx = (int)(x / 8), sz = (int)(z / 8);
                    if (sx >= 0 && sz >= 0 && sx < w && sz < h && items[sz * w + sx]) return true;
                }
            return false;
        }
        var blockedDoors = shipDoors.Where(d => Blocked(d.Item1, d.Item2)).Select(d => $"({d.Item1.X:0}, {d.Item1.Z:0})")
            .Concat(World.BayDoors.Where(b => Blocked(new P(b.X0, b.Z0), new P(b.X1, b.Z1))).Select(b => $"bay door ({b.X0:0}, {b.Z0:0})"))
            .Concat(World.Aprons.Select(a => (a, zc: (a.Z0 + a.Z1) / 2)).Where(x => Blocked(new P(x.a.X0, x.zc - 350), new P(x.a.X1, x.zc + 350))).Select(x => $"the way across the apron at z {x.zc:0}")).ToList();
        Expect(blockedDoors.Count == 0, $"no feature stands in a doorway or in the way across an apron{List(blockedDoors)}");
        double Free(Func<P, bool> inside, double x0, double z0, double x1, double z1)
        {
            int level = 0, total = 0;
            for (double z = z0; z < z1; z += 16)
                for (double x = x0; x < x1; x += 16)
                {
                    var p = new P(x + 8, z + 8);
                    if (!inside(p)) continue;
                    total++;
                    int sx = (int)(p.X / 8), sz = (int)(p.Z / 8);
                    if (items[sz * w + sx]) continue;
                    double rise = Math.Abs(heights.HeightAt(p.X + 8, p.Z) - heights.HeightAt(p.X - 8, p.Z)) + Math.Abs(heights.HeightAt(p.X, p.Z + 8) - heights.HeightAt(p.X, p.Z - 8));
                    if (rise < 2) level++;
                }
            return level / (double)Math.Max(1, total);
        }
        foreach (var (room, at) in World.Starts.Take(4))
        {
            int c = land.ShipCell(at);
            double share = Free(p => land.ShipCell(p) == c, at.X - 2000, at.Z - 2000, at.X + 2000, at.Z + 2000);
            Expect(share >= 0.6, $"the {room} (start {Array.FindIndex(World.Starts, s => s.At == at)}) keeps {share:P0} of its floor level and free (at least 60%)");
        }
        foreach (var (name, at) in World.Starts.Skip(4).Take(4))
        {
            double share = Free(p => (p - at).Length < World.MareR, at.X - World.MareR, at.Z - World.MareR, at.X + World.MareR, at.Z + World.MareR);
            Expect(share >= 0.6, $"the {name} start's mare (start {Array.FindIndex(World.Starts, s => s.At == at)}) keeps {share:P0} of its ground level and free (at least 60%)");
        }
        double apronLevel = World.Aprons.Min(a => Free(p => a.Has(p), a.X0, a.Z0, a.X1, a.Z1));
        Expect(apronLevel >= 0.75, $"every apron is level and mostly free, its cargo at the corners (the least {apronLevel:P0})");

        log("Standing true:");
        {
            string[] skip = ["lf_", "sm_viewport", "sm_enginebell", "sm_pipes"];
            var tilted = new List<string>();
            double most = 0;
            foreach (var (name, at, heading) in laid)
            {
                if (skip.Any(n => name.StartsWith(n, StringComparison.Ordinal)) && name != "lf_strut" || !own.TryGetValue(name, out var fp)) continue;
                var (hx, hz) = Half(fp, heading);
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
            Expect(tilted.Count == 0, $"every machine, prop and strut stands level: the ground under its footprint rises no more than 4 (the most {most:0.0}; the moonrock lies as it fell){List(tilted)}");
        }

        log("Metal spots, vents and starts:");
        int crowded = World.Spots.Count(s => Enumerable.Range(-5, 11).Any(dz => Enumerable.Range(-5, 11).Any(dx =>
        {
            int x = (int)(s.At.X / 8) + dx, z = (int)(s.At.Z / 8) + dz;
            return x >= 0 && z >= 0 && x < w && z < h && items[z * w + x];
        })));
        Expect(crowded == 0, $"no blocking feature within 40 of any of the {World.Spots.Length} metal spots ({crowded} crowded)");
        var worstSpot = World.Spots.Select(s => s.At).Concat(World.Vents).MaxBy(p => Rise(heights, p, 48));
        double worstRise = Rise(heights, worstSpot, 48);
        Expect(worstRise < 1, $"every spot and vent is level (the most any rises over its footprint: {worstRise:0.0}, at ({worstSpot.X:0}, {worstSpot.Z:0}))");
        Expect(starts.All(s => Rise(heights, s, 200) < 1), "every start is on level ground");
        double port = World.Spots.Where(s => s.Side == "port").Sum(s => s.Income), starboard = World.Spots.Where(s => s.Side == "starboard").Sum(s => s.Income);
        Expect(Math.Abs(port - starboard) < 0.01, $"each team's own metal: {port:0.0} metal/s; {World.Spots.Where(s => s.Side == "shared").Sum(s => s.Income):0.0} contested in the ship and on the asteroid, " +
            $"{World.Spots.Where(s => s.Side == "bowl").Sum(s => s.Income):0.0} on the bowls' floors (bots only); {World.Spots.Length} spots, {World.Spots.Sum(s => s.Income):0.0} metal/s; {World.Vents.Length} vents");

        var d1 = Steps(vehicle, [starts[1]], items);
        var d5 = Steps(vehicle, [starts[5]], items);
        log($"       the fronts (driving): crew deck to crew deck {At(d1, w, h, starts[9]):0}; forward bay to forward bay, outside {At(d5, w, h, starts[13]):0}; fore flank to fore flank {At(Steps(vehicle, [starts[4]], items), w, h, starts[12]):0}");
        return failures;
    }

    /// <summary>The lane a point shuts, if it is in one: a shared room of the ship, or the strip across the centreline at the bow or stern.</summary>
    static string? LaneOf(Asteroid.Land land, P p)
    {
        int c = land.ShipCell(p);
        if (c >= 0 && Layout.Cells[c].Side == "centre" && Layout.Lanes.Contains(Layout.Cells[c].Name)) return Layout.Cells[c].Name;
        if (c == Terrain.Space && Math.Abs(p.X - World.CX) < 220)
        {
            if (p.Z < World.OZ + 1000) return "bow";
            if (p.Z > World.OZ + 13000) return "stern";
        }
        return null;
    }

    /// <summary>Whether a ship vertex is a wall within 48 of a floor above Asteroid.HighFloor (Asteroid.Build raises those walls).</summary>
    static bool NearHighFloor(Terrain.Result ship, int si)
    {
        if (ship.Floor[si]) return false;
        int sx = si % ship.NX, sz = si / ship.NX;
        for (int dz = -6; dz <= 6; dz++)
            for (int dx = -6; dx <= 6; dx++)
            {
                int ox = sx + dx, oz = sz + dz;
                if (ox < 0 || oz < 0 || ox >= ship.NX || oz >= ship.NZ || dx * dx + dz * dz > 36) continue;
                int o = oz * ship.NX + ox;
                if (ship.Floor[o] && ship.H[o] > Asteroid.HighFloor) return true;
            }
        return false;
    }

    static bool InCrater(P p) => World.Bowls.Concat(World.Small).Any(c => (c.At - p).Length < c.R || (World.Mirror(c.At) - p).Length < c.R);

    static double Spread(Func<double, double> radius)
    {
        var rs = Enumerable.Range(0, 360).Select(k => radius(k * Math.PI / 180)).ToArray();
        return (rs.Max() - rs.Min()) / rs.Average();
    }

    static (double X, double Z) Half((int X, int Z, bool Blocks) fp, int heading)
    {
        int facing = (int)Math.Round(heading / 16384.0) % 4;
        return ((facing % 2 == 0 ? fp.X : fp.Z) * 8, (facing % 2 == 0 ? fp.Z : fp.X) * 8);
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
