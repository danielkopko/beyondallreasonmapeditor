// The checks, by the engine's rules (MapCore.Movement: per 8-unit square, the slope of its 16-unit cell) and round
// every blocking feature's footprint (16-unit footprint units, turned to its facing, and 8 more round it):
//   the halves match exactly, heights and features; every start and vent is reached; each of the five crossings
//   alone lets vehicles across, and with all five shut no ground unit crosses but spiders (their route is logged); no
//   ground class reaches space; every door is wide and clear of features; every start room keeps enough level ground
//   free; every machine and prop stands level. The metal map (1.1): metal on every deck floor cell and nowhere else,
//   mirrored, read by BAR as a metal map, Cloud9's yield, and all of it in reach. The fronts' driving distance is logged.
using System.Globalization;
using MapCore.Heights;
using MapCore.Movement;

static class Check
{
    /// <summary>Every feature's footprint: the map's own models', and BAR's crystals (1.1).</summary>
    public static Dictionary<string, (int X, int Z, bool Blocks)> Footprints(IEnumerable<FeatureDef> defs)
    {
        var all = defs.ToDictionary(d => d.Name, d => (d.Footprint.X, d.Footprint.Z, d.Blocking));
        foreach (var (name, fp) in Place.Crystals.Footprints) all[name] = fp;
        return all;
    }

    /// <param name="metal">The SMF metal map (16-unit cells), and the mapinfo values it's read with.</param>
    public static int Report(HeightMap heights, Terrain.Result t, IEnumerable<string> ops, Dictionary<string, (int X, int Z, bool Blocks)> own,
        byte[] metal, double maxMetal, double extractorRadius, Action<string> log)
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
        Expect(starts.All(s => Has(all, s)), $"vehicles reach all {starts.Length} starts ({all.Count(b => b) * 100.0 / all.Length:0.0}% of the map)");
        var cut = Layout.Vents.Where(v => !Has(all, v.At)).Select(v => $"vent ({v.At.X:0}, {v.At.Z:0})").ToList();
        Expect(cut.Count == 0, $"vehicles reach every vent (the reactor's by the well's ramps){List(cut)}");
        var bots = Access.Fill(bot, starts[0], items);
        Expect(starts.All(s => Has(bots, s)), "bots reach every start");
        // 1.1: the thruster platforms on the engine housings, for long-range guns at the stern as the bridge is at the bow.
        var platforms = Layout.Cells.Where(c => c.Kind == Layout.Kind.Platform).ToArray();
        var offPlatform = platforms.Select(c => new P((c.Outline.Min(q => q.X) + c.Outline.Max(q => q.X)) / 2, (c.Outline.Min(q => q.Z) + c.Outline.Max(q => q.Z)) / 2))
            .Where(p => !Has(all, p) || heights.HeightAt(p.X, p.Z) < Layout.Bulkhead + 100 - 1).Select(p => $"({p.X:0}, {p.Z:0})").ToList();
        Expect(offPlatform.Count == 0, $"vehicles reach the {platforms.Length} thruster platforms by their ramps, each at {Layout.SternDeck:0}, 100 over the bulkheads{List(offPlatform)}");

        // 1.1: the outer walk (its floor off the rooms, and the thruster steps) is a sixth way, round the hull's edge.
        bool OnWalk(P p)
        {
            int c = CellAt(p);
            // (A doorway through a bulkhead is floor in the solid hull too: not the walk.)
            return c == Terrain.Solid ? Terrain.Nearest(t.Floor, t.NX, t.NZ, p) && Terrain.Nearest(t.Door, t.NX, t.NZ, p) < 0 : c >= 0 && Layout.Cells[c].Kind == Layout.Kind.Step;
        }
        var walkAt = new[] { new P(Layout.CX, 900), new P(943, 8000), new P(Layout.Mirror(943), 8000), new P(4200, 13160) };
        var offWalk = walkAt.Where(p => !OnWalk(p) || !Has(all, p)).Select(p => $"({p.X:0}, {p.Z:0})").ToList();
        Expect(offWalk.Count == 0, $"vehicles reach the outer walk all round: over the bow, along both sides, across the stern{List(offWalk)}");
        // Off the walk and the steps' paths (their long edges over the thruster bays, the machine shops and the engine room
        // included), with only the ramps and the bridge's front opening shut, nothing gets down into a room or onto a platform.
        var waysDown = Shut(p => { int c = CellAt(p); return c >= 0 && (Layout.Cells[c].Kind == Layout.Kind.Ramp || Layout.Cells[c].Name == "Front Opening"); });
        foreach (var (name, map) in new[] { ("vehicles", vehicle), ("bots", bot), ("hovers", hover), ("amphibious units", amphibious) })
        {
            var r = Access.Fill(map, walkAt[1], waysDown);
            int down = 0;
            for (int i = 0; i < r.Length; i++) { if (!r[i]) continue; int c = CellAt(SquareCentre(i)); if (c >= 0 && Layout.Cells[c].Kind != Layout.Kind.Step) down++; }
            Expect(down == 0, $"from the outer walk and the thruster steps' paths, the ramps and the bridge's front opening shut, {name} get down into no room ({down} squares)");
        }
        // Each thruster step's own path (round its platform's corners) takes vehicles from its landing to the walk on the stern
        // line beside it. (The other steps' paths cross the walk's band there, so they're shut too: no way round by them.)
        foreach (var step in Layout.Cells.Where(c => c.Kind == Layout.Kind.Step))
        {
            int me = Array.IndexOf(Layout.Cells, step);
            var landing = new P((step.Outline[0].X + step.Outline[1].X) / 2, Layout.StepZ + 50);
            var beside = new P(step.Outline.Min(q => q.X) - 60, Layout.SternZ - 40);
            var others = Shut(p => { int c = CellAt(p); return c >= 0 && c != me && Layout.JoinsWalk(Layout.Cells[c]) && Layout.Cells[c].Kind != Layout.Kind.Platform && !Layout.Cells[c].Name.Contains("Ramp"); });
            Expect(CellAt(landing) == me && OnWalk(beside) && Has(Access.Fill(vehicle, landing, others), beside),
                $"the {step.Side} {step.Name.ToLowerInvariant()}'s path round its platform takes vehicles from its landing to the outer walk, the other ways onto the walk shut");
        }

        log("The crossings (each team's half joins the other only through the shared rooms on the centreline, and the outer walk):");
        int Shared(string name) => Array.FindIndex(Layout.Cells, c => c.Name == name && c.Side == "centre");
        var lanes = Layout.Lanes.Select(Shared).ToArray();
        int half = starts.Length / 2;
        P port = starts[1], starboard = starts[half + 1];          // each side's forward hangar, fore
        foreach (var k in lanes)
        {
            var others = lanes.Where(o => o != k).ToHashSet();
            var one = Shut(p => others.Contains(CellAt(p)) || OnWalk(p));
            Expect(Has(Access.Fill(vehicle, port, one), starboard), $"the {Layout.Cells[k].Name} alone lets vehicles across");
        }
        var none = Shut(p => lanes.Contains(CellAt(p)));
        Expect(Has(Access.Fill(vehicle, port, none), starboard), "with the five shut, the outer walk alone lets vehicles across (up a thruster ramp to its step, round the hull, down the enemy's)");
        var sealedAll = Shut(p => lanes.Contains(CellAt(p)) || OnWalk(p));
        foreach (var (name, map) in new[] { ("vehicles", vehicle), ("bots", bot), ("hovers", hover), ("amphibious units", amphibious) })
            Expect(!Has(Access.Fill(map, port, sealedAll), starboard), $"with all five and the outer walk shut, {name} can't cross");
        var spiders = Access.Fill(spider, port, sealedAll);
        log($"       spiders, all six shut: {(Has(spiders, starboard) ? "they cross, over the bulkheads (all-terrain bots climb any wall; intended)" : "they don't cross")}");

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

        log("Walls:");
        foreach (var (name, map) in new[] { ("vehicles", vehicle), ("bots", bot), ("hovers", hover), ("amphibious units", amphibious), ("spiders", spider) })
        {
            // No ground unit but spiders gets onto a wall or the hull's solid top: nothing it reaches is off the floors and
            // more than 30 over the highest floor within 24 (or the deck, with none near): a wall's foot is lower.
            var reach = Access.Fill(map, port, items);
            int onTop = 0;
            var where = new List<string>();
            for (int i = 0; i < reach.Length; i++)
            {
                var p = SquareCentre(i);
                if (!reach[i] || CellAt(p) == Terrain.Space || Terrain.Nearest(t.Floor, t.NX, t.NZ, p) || heights.HeightAt(p.X, p.Z) <= FloorNear(t, p) + 30) continue;
                if (onTop++ % 400 == 0) where.Add($"({p.X:0}, {p.Z:0})");
            }
            if (name == "spiders") log($"       spiders get onto {onTop} squares of the walls and the hull's tops (they climb any wall; intended)");
            else Expect(onTop == 0, $"{name} don't get onto the walls or the hull's solid tops ({onTop} squares){List(where)}");
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
        // 1.1: BAR's energy crystals: vehicles reach every one, none is in the reactor, and what each side holds.
        {
            var crystals = laid.Where(l => Place.Crystals.Footprints.ContainsKey(l.Name)).ToList();
            bool InReach(P p)
            {
                for (int dz = -6; dz <= 6; dz++)
                    for (int dx = -6; dx <= 6; dx++)
                    {
                        int x = (int)(p.X / 8) + dx, z = (int)(p.Z / 8) + dz;
                        if (x >= 0 && z >= 0 && x < w && z < h && all[z * w + x]) return true;
                    }
                return false;
            }
            static double Energy(string name) => Place.Crystals.Energy(name);
            double Held(string side) => crystals.Where(l => CellAt(l.At) >= 0 && Layout.Cells[CellAt(l.At)].Side == side).Sum(l => Energy(l.Name));
            var unreachable = crystals.Where(l => !InReach(l.At) || CellAt(l.At) < 0 || Layout.Cells[CellAt(l.At)].Name == "Main Reactor").Select(l => $"({l.At.X:0}, {l.At.Z:0})").ToList();
            Expect(crystals.Count > 0 && unreachable.Count == 0,
                $"vehicles reach all {crystals.Count} energy crystals, none in the reactor: {Held("port"):0} energy in port's rooms, {Held("starboard"):0} in starboard's, {Held("centre"):0} in the shared rooms{List(unreachable)}");

            // In clusters, as BAR's maps lay them: crystals within 100 of each other are one (as measured on theirs).
            var parent = Enumerable.Range(0, crystals.Count).ToArray();
            int Root(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            double closest = double.MaxValue;
            for (int i = 0; i < crystals.Count; i++)
                for (int j = i + 1; j < crystals.Count; j++)
                {
                    double d = (crystals[i].At - crystals[j].At).Length;
                    closest = Math.Min(closest, d);
                    if (d <= 100) parent[Root(i)] = Root(j);
                }
            var clusters = Enumerable.Range(0, crystals.Count).GroupBy(Root).Select(g => g.ToList()).ToList();
            double ClusterEnergy(List<int> g) => g.Sum(i => Energy(crystals[i].Name));
            Expect(clusters.All(g => g.Count >= 5) && closest >= Place.Crystals.Spacing - 1,
                $"the crystals lie in {clusters.Count} clusters of {clusters.Min(g => g.Count)} to {clusters.Max(g => g.Count)} ({clusters.Min(ClusterEnergy):0} to {clusters.Max(ClusterEnergy):0} energy), none alone; no two closer than {closest:0}");

            // Each start's circle is edged by two of them (one each wall side), none inside its ring: 1,200 each, the tech
            // start's 600. Each inboard room holds 1.5 times a start's, each shared room but the reactor 2 times.
            const double PerStart = 2400;
            var wrong = new List<string>();
            foreach (var (role, at, r) in Layout.StartCircles)
            {
                var near = clusters.Where(g => g.All(i => (crystals[i].At - at).Length < r + 220)).ToList();
                bool inside = crystals.Any(l => (l.At - at).Length < r + 24);
                double each = role == "tech" ? PerStart / 4 : PerStart / 2;
                if (near.Count != 2 || near.Any(g => ClusterEnergy(g) != each) || inside) wrong.Add($"{role} start at ({at.X:0}, {at.Z:0})");
            }
            var byRoom = crystals.Where(l => CellAt(l.At) >= 0).GroupBy(l => Layout.Cells[CellAt(l.At)]).ToDictionary(g => g.Key, g => g.Sum(l => Energy(l.Name)));
            foreach (var cell in Layout.Cells.Where(c => c.Kind is not (Layout.Kind.Ramp or Layout.Kind.Landing or Layout.Kind.Platform or Layout.Kind.Step or Layout.Kind.Passage)))
            {
                int seats = Layout.Starts.Count(s => Layout.Cells[CellAt(s.At)] == cell);
                bool tech = Layout.StartCircles.Any(s => s.Role == "tech" && CellAt(s.Circle) >= 0 && Layout.Cells[CellAt(s.Circle)] == cell);
                double want = cell.Name == "Main Reactor" ? 0 : cell.Side == "centre" ? 2 * PerStart : seats == 0 ? 1.5 * PerStart : tech ? PerStart / 2 : seats * PerStart;
                double held = byRoom.GetValueOrDefault(cell);
                if (held != want) wrong.Add($"the {cell.Side} {cell.Name}: {held:0}, not {want:0}");
            }
            Expect(wrong.Count == 0,
                $"each start's circle is edged by two clusters (on its wall sides) and none inside its ring: {PerStart / 2:0} energy each ({PerStart:0} a start; the tech start's {PerStart / 4:0}, half); each inboard room holds {1.5 * PerStart:0} (1.5 times a start's), each shared room but the reactor {2 * PerStart:0} (2 times), the reactor none{List(wrong)}");
        }
        foreach (var room in Layout.Starts.Take(Layout.Starts.Length / 2).Select(s => s.Room).Distinct())
        {
            var at = Layout.Starts.First(s => s.Room == room).At;
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

            // 1.1: the main engines hang wholly under the outer walk behind the platforms, so nothing of them shows on it.
            double bellTop = global::Models.EngineBell().Mesh.V.Max(v => v.Position.Y);
            var bells = laid.Where(l => l.Name.StartsWith("sm_enginebell", StringComparison.Ordinal)).ToList();
            var proud = bells.Where(l => Math.Abs(heights.HeightAt(l.At.X, l.At.Z) - Layout.WalkH) > 1 || bellTop >= 0).Select(l => $"({l.At.X:0}, {l.At.Z:0})").ToList();
            Expect(bells.Count == 6 && proud.Count == 0, $"the {bells.Count} main engines stand on the outer walk's level and wholly under it: their highest point {-bellTop:0} below its surface{List(proud)}");
            // ... and their cones stand clear of the rough ground of space under them (past the foot of the hull's aft face,
            // which the throat comes out of): nothing of the ground cuts through them.
            double least = double.MaxValue;
            var cutBy = new List<string>();
            foreach (var l in bells)
            {
                double origin = heights.HeightAt(l.At.X, l.At.Z);
                for (float x = 0; x <= global::Models.BellRimEnd; x += 4)
                {
                    float r = global::Models.BellRadius(x);
                    for (float side = -r; side <= r; side += 4)
                    {
                        var p = new P(l.At.X + side, l.At.Z + x);
                        if (Terrain.Sample(t.Outside, t.NX, t.NZ, p) <= 56) continue;
                        double bottom = origin + global::Models.BellAxis - Math.Sqrt(r * r - side * side);
                        double clear = bottom - heights.HeightAt(p.X, p.Z);
                        least = Math.Min(least, clear);
                        if (clear <= 0 && cutBy.Count < 5) cutBy.Add($"({p.X:0}, {p.Z:0})");
                    }
                }
            }
            Expect(cutBy.Count == 0, $"the main engines' cones clear the ground of space under them, by {least:0} at the least{List(cutBy)}");
        }

        log("Metal (a metal map: the whole deck), vents and starts:");
        int mw = Layout.SquaresX / 2, mh = Layout.SquaresZ / 2;
        P CellCentre(int x, int z) => new(x * Metal.CellSize + 8, z * Metal.CellSize + 8);
        bool FloorCell(int x, int z) => t.Floor[(2 * z + 1) * t.NX + 2 * x + 1];
        var misplaced = new List<string>();
        int cells = 0, notMirrored = 0;
        var bySide = new Dictionary<string, int>();
        for (int z = 0; z < mh; z++)
            for (int x = 0; x < mw; x++)
            {
                byte v = metal[z * mw + x];
                if (v != metal[z * mw + mw - 1 - x]) notMirrored++;
                if ((v != 0) != FloorCell(x, z) || (v != 0 && v != Layout.MetalRaw))
                    misplaced.Add($"({CellCentre(x, z).X:0}, {CellCentre(x, z).Z:0}) {v}");
                if (v == 0) continue;
                cells++;
                int c = CellAt(CellCentre(x, z));
                string side = c >= 0 ? Layout.Cells[c].Side : "door";      // doorways, and (1.1) the outer walk
                bySide[side] = bySide.GetValueOrDefault(side) + 1;
            }
        double Area(int n) => n * 256 / 1e6;
        Expect(misplaced.Count == 0, $"metal {Layout.MetalRaw} on every deck floor cell and nowhere else (not the walls, hull tops or space): {cells} cells, {Area(cells):0.0} M square units{List(misplaced)}");
        Expect(notMirrored == 0, $"the metal is its own mirror image ({notMirrored} cells differ)");
        Expect(bySide.GetValueOrDefault("port") == bySide.GetValueOrDefault("starboard"),
            $"each team's rooms hold the same metal: {Area(bySide.GetValueOrDefault("port")):0.0} M square units each, the shared rooms {Area(bySide.GetValueOrDefault("centre")):0.0} M, the doorways and the outer walk {Area(bySide.GetValueOrDefault("door")):0.0} M");
        var smf = new MapCore.Smf.SmfFile(Layout.SquaresX, Layout.SquaresZ);
        metal.CopyTo(smf.MetalMap, 0);
        var widest = new MapCore.Metal.MetalLayer(smf).FindSpots()
            .Select(s => (Math.Max(s.Cells.Max(q => q.X) - s.Cells.Min(q => q.X), s.Cells.Max(q => q.Z) - s.Cells.Min(q => q.Z)) + 1) * Metal.CellSize).Max();
        Expect(widest > 6 * extractorRadius, $"BAR reads it as a metal map, not spots: its widest stretch of metal is {widest} across, over 6 x the extractor radius ({6 * extractorRadius:0})");
        double centred = 9 * Layout.MetalRaw * Layout.MaxMetal * 0.001;
        var yields = starts.Select(s => Metal.T1Income(metal, mw, mh, new P(Math.Floor(s.X / 16) * 16 + 8, Math.Floor(s.Z / 16) * 16 + 8), maxMetal, extractorRadius)).ToArray();
        double corner = Metal.T1Income(metal, mw, mh, new P(Math.Floor(starts[0].X / 16) * 16, Math.Floor(starts[0].Z / 16) * 16), maxMetal, extractorRadius);
        Expect(maxMetal == Layout.MaxMetal && extractorRadius == Layout.ExtractorRadius && yields.All(y => Math.Abs(y - centred) < 0.001),
            $"Cloud9's yield (maxMetal {maxMetal:0.##}, extractor radius {extractorRadius:0}): a T1 extractor on open deck takes {yields.Min():0.00} metal/s centred on a metal cell (9 cells), {corner:0.00} on a cell corner (4)");
        int byVehicle = 0, byBot = 0, underFeatures = 0;
        var unreached = new List<string>();
        for (int z = 0; z < mh; z++)
            for (int x = 0; x < mw; x++)
            {
                if (metal[z * mw + x] == 0) continue;
                var p = CellCentre(x, z);
                if (Has(all, p)) byVehicle++;
                else if (Has(bots, p)) byBot++;
                else if (items[(int)(p.Z / 8) * w + (int)(p.X / 8)]) underFeatures++;
                else unreached.Add($"({p.X:0}, {p.Z:0})");
            }
        Expect(unreached.Count == 0, $"all the metal is in reach: vehicles reach {byVehicle * 100.0 / cells:0.0}% of its cells, bots another {byBot * 100.0 / cells:0.0}%, and {underFeatures * 100.0 / cells:0.0}% is under features{List(unreached)}");
        log("Gun sites (1.1: the bridge and the thruster platforms, high ground for long-range guns):");
        {
            // Open tops: no wall within 48 of a thruster platform or its ramp stands over it, and the bridge's walls are a lip.
            int over = 0, overLip = 0;
            for (int z = 0; z < t.NZ; z++)
                for (int x = 0; x < t.NX; x++)
                {
                    int i = z * t.NX + x, c = t.Cell[i];
                    if (!t.Floor[i] || c < 0) continue;
                    bool open = Layout.Open(Layout.Cells[c]), bridge = t.Door[i] < 0 && Layout.Cells[c].Name is "Bridge" or "Bridge Landing";
                    if (!open && !bridge) continue;
                    double here = heights.HeightAt(x * 8, z * 8), most = double.MinValue;
                    for (int dz = -6; dz <= 6; dz++)
                        for (int dx = -6; dx <= 6; dx++)
                        {
                            int ox = x + dx, oz = z + dz;
                            if (ox < 0 || oz < 0 || ox >= t.NX || oz >= t.NZ || dx * dx + dz * dz > 36) continue;
                            int o = oz * t.NX + ox;
                            if (!t.Floor[o] && t.Cell[o] != Terrain.Space) most = Math.Max(most, heights.HeightAt(ox * 8, oz * 8) - here);
                        }
                    if (open && most > 0.5) over++;
                    if (bridge && most > Layout.Lip + 0.5) overLip++;
                }
            Expect(over == 0, $"the thruster platforms and their ramps stand open: no wall within 48 stands over them ({over} floor points)");
            Expect(overLip == 0, $"the bridge's walls stand no more than its lip, {Layout.Lip:0}, over its deck ({overLip} floor points)");

            // What a long-range gun reaches from them (not a check): the deck within its range of anywhere on a site.
            const double Range = 6100;                  // Legion's Starfall (its unit page)
            var sites = Layout.Cells.Where(c => c.Kind == Layout.Kind.Platform || c.Name is "Bridge" or "Bridge Landing").ToArray();
            var ownSites = sites.Where(c => c.Side != "starboard").ToArray();
            double Off(Layout.Cell c, P p) => Terrain.InPolygon(p, c.Outline) ? 0 : Terrain.ToOutline(p, c.Outline);
            var ownPlatform = sites.Single(c => c.Kind == Layout.Kind.Platform && c.Side == "port");
            int deck = 0, any = 0, enemy = 0, fromOwn = 0, fromPlatform = 0;
            for (int z = 0; z < mh; z++)
                for (int x = 0; x < mw; x++)
                {
                    if (metal[z * mw + x] == 0) continue;
                    var p = CellCentre(x, z);
                    deck++;
                    if (sites.Any(c => Off(c, p) <= Range)) any++;
                    if (p.X <= Layout.CX) continue;
                    enemy++;
                    if (ownSites.Any(c => Off(c, p) <= Range)) fromOwn++;
                    if (Off(ownPlatform, p) <= Range) fromPlatform++;
                }
            log($"       a gun of range {Range:0} (Legion's Starfall) on the bridge or a thruster platform reaches {any * 100.0 / deck:0}% of the deck; " +
                $"from one side's own sites and the shared ones (its platform and bridge landing, the bridge, the centre platform), {fromOwn * 100.0 / enemy:0}% of the enemy's half, and from its own thruster platform alone {fromPlatform * 100.0 / enemy:0}%");
        }

        double worstRise = Layout.Vents.Max(v => Rise(heights, v.At, 48));
        Expect(worstRise < 1, $"every vent is level (the most any rises over its footprint: {worstRise:0.0}); {Layout.Vents.Length} vents");
        Expect(starts.All(s => Rise(heights, s, 200) < 1), "every start is on level deck");
        // 1.1 (8v8): each start's floor circle lies wholly on its own room's level deck, clear of ramps, doors and walls.
        {
            var off = new List<string>();
            foreach (var (_, at, r) in Layout.StartCircles)
            {
                int room = CellAt(at);
                bool clear = room >= 0;
                for (double dz = -r; dz <= r && clear; dz += 16)
                    for (double dx = -r; dx <= r && clear; dx += 16)
                    {
                        if (dx * dx + dz * dz > r * r) continue;
                        var q = new P(at.X + dx, at.Z + dz);
                        if (CellAt(q) != room || !Terrain.Nearest(t.Floor, t.NX, t.NZ, q) || Terrain.Nearest(t.Door, t.NX, t.NZ, q) >= 0
                            || Math.Abs(heights.HeightAt(q.X, q.Z) - heights.HeightAt(at.X, at.Z)) > 0.5) clear = false;
                    }
                if (!clear) off.Add($"({at.X:0}, {at.Z:0})");
            }
            Expect(off.Count == 0, $"every start's floor circle ({Layout.StartCircles.Length}) lies wholly on its room's level deck, clear of ramps, doors and walls{List(off)}");
        }

        // The fronts: how far the two teams' starts drive to each other (not a check): the air starts (in the weapons bays,
        // nearest the bridge), and the forward hangars' fore starts.
        int halfStarts = starts.Length / 2;
        var dAir = Steps(vehicle, [starts[0]], items);
        var dHangar = Steps(vehicle, [starts[1]], items);
        log($"       the fronts: air start to the enemy's air start {At(dAir, w, h, starts[halfStarts]):0}, forward hangar to the enemy's forward hangar {At(dHangar, w, h, starts[halfStarts + 1]):0} (driving)");
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

    /// <summary>The highest floor within 24 of a point (in the ship's own heights), or the deck if there is none.</summary>
    public static double FloorNear(Terrain.Result t, P p)
    {
        int cx = (int)Math.Round(p.X / 8), cz = (int)Math.Round(p.Z / 8);
        double best = double.MinValue;
        for (int dz = -3; dz <= 3; dz++)
            for (int dx = -3; dx <= 3; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || z < 0 || x >= t.NX || z >= t.NZ || !t.Floor[z * t.NX + x]) continue;
                best = Math.Max(best, t.H[z * t.NX + x]);
            }
        return best == double.MinValue ? Layout.Deck : best;
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
