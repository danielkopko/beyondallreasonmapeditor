// What MapTool adds: the ship (once, on the middle line), the wreckage and the jungle, laid out in the north half and
// mirrored into the south (wreckage as its mirror-image twin, at the mirrored heading), metal and vents. Wreckage is
// heaviest at the breach and the mound and along the furrow, thinning out across the field to small scraps near the
// bases. The jungle is kapok and banyan giants, oaks, palms by the water, mangroves and reeds in the shallows, ferns
// everywhere beneath, thickest over the mound; it keeps off the trails, bases, spots, vents and the wreck.
using System.Globalization;
using System.Numerics;
using System.Text;

static class Place
{
    public sealed record Item(string Name, P At, double Heading, string Kind, double Radius, FeatureDef? Def = null);

    sealed class Ground(Terrain.Result t)
    {
        public double H(P p) => Sample(t.H, p);
        public double Slope(P p)
        {
            double dx = (H(p + new P(8, 0)) - H(p - new P(8, 0))) / 16, dz = (H(p + new P(0, 8)) - H(p - new P(0, 8))) / 16;
            return Math.Atan(Math.Sqrt(dx * dx + dz * dz)) * 180 / Math.PI;
        }
        public double MoundShare(P p) => Sample(t.Mound, p);
        double Sample(float[] f, P p)
        {
            int n = t.N;
            double fx = Math.Clamp(p.X / 8, 0, n - 1.001), fz = Math.Clamp(p.Z / 8, 0, n - 1.001);
            int x = (int)fx, z = (int)fz;
            double tx = fx - x, tz = fz - z;
            return (f[z * n + x] * (1 - tx) + f[z * n + x + 1] * tx) * (1 - tz) + (f[(z + 1) * n + x] * (1 - tx) + f[(z + 1) * n + x + 1] * tx) * tz;
        }
    }

    public static readonly List<P>[] TrailLines = Layout.Trails.Select(t => Geo.Smooth(t, 40)).ToArray();
    public static double TrailDistance(P q) => TrailLines.Min(l => Geo.ToPolyline(q, l, out _));

    /// <summary>The ship's sections and the prow, on the middle line.</summary>
    public static List<Item> Ship(out List<FeatureDef> defs)
    {
        var sections = new[] { Models.Engines(), Models.AftHull(), Models.AftBreak(), Models.ForeBreak(), Models.ForeHull() };
        var prow = Models.Prow();
        defs = sections.Select(s => s.Def).Append(prow).ToList();
        var items = sections.Select(s => new Item(s.Def.Name, s.At, 0, "ship", 0, s.Def)).ToList();
        items.Add(new Item(prow.Name, Layout.MoundTop, 0, "ship", 0, prow));
        return items;
    }

    /// <summary>Whether a point is under the ship (its sections' boxes, with a margin).</summary>
    public static bool UnderShip(List<Item> ship, P p, double margin)
    {
        foreach (var s in ship)
        {
            var (min, max) = s.Def!.Box;
            if (p.X > s.At.X + min.X - margin && p.X < s.At.X + max.X + margin && Math.Abs(p.Z - s.At.Z) < Math.Max(-min.Z, max.Z) + margin) return true;
        }
        return false;
    }

    /// <summary>How much room a piece takes: the mean of its footprint's sides (long thin pieces lie at any angle).</summary>
    static double RadiusOf(FeatureDef d) => (d.Footprint.X + d.Footprint.Z) / 2.0 * 8;

    /// <summary>The wreckage in the north half.</summary>
    public static List<Item> Wreckage(Terrain.Result t, List<Item> ship, Dictionary<string, FeatureDef> defs)
    {
        var g = new Ground(t);
        var rnd = new Random(4711);
        var items = new List<Item>();
        double laneMid = (Layout.AftEnd + Layout.ForeStart) / 2;
        bool Fits(P p, double r, double startRoom)
        {
            if (p.X < r + 120 || p.X > Layout.Size - r - 120 || p.Z < r + 120) return false;
            if (Layout.Axis - p.Z < r + 20) return false;                                  // clear of its own mirror image
            if (UnderShip(ship, p, r + 30)) return false;
            if (TrailDistance(p) < r + 50) return false;
            if (Math.Abs(p.X - laneMid) < r + 90 && Layout.Axis - p.Z < 1300) return false;   // the breach's way through
            if (Layout.Spots.Any(s => (s.At - p).Length < r + 140) || Layout.Vents.Any(v => (v - p).Length < r + 160)) return false;
            if (Layout.Starts.Any(s => (s - p).Length < r + startRoom)) return false;
            if (g.H(p) < -20 || g.Slope(p) > 26) return false;
            if (items.Any(i => (i.At - p).Length < i.Radius + r + 24)) return false;
            return true;
        }
        void Try(string name, P p, double startRoom = 420)
        {
            var d = defs[name];
            double r = RadiusOf(d);
            if (Fits(p, r, startRoom)) items.Add(new Item(name, p, rnd.NextDouble() * 360, "wreck", r, d));
        }
        string Pick(params string[] names) => names[rnd.Next(names.Length)];
        P Around(P c, double r0, double r1)
        {
            double a = rnd.NextDouble() * Math.PI * 2, r = r0 + (r1 - r0) * Math.Sqrt(rnd.NextDouble());
            return c + new P(Math.Cos(a), Math.Sin(a)) * r;
        }

        // Each of these until it finds room, near where it belongs.
        void Somewhere(string name, P near, double reach, int tries = 60)
        {
            int before = items.Count;
            for (int k = 0; k < tries && items.Count == before; k++) Try(name, k == 0 ? near : Around(near, 0, reach));
        }
        // The breach: the reactor spilled out of the break, ribs and girders, the biggest plates.
        Somewhere("sf_wreck_reactor", new P(2900, 3500), 400);
        Somewhere("sf_wreck_rib", new P(3480, 3480), 400);
        Somewhere("sf_wreck_girder", new P(2950, 3150), 400);
        Somewhere("sf_wreck_slab", new P(3450, 3150), 400);
        Somewhere("sf_wreck_pod", new P(2700, 3500), 400);
        for (int i = 0; i < 160; i++)
            Try(Pick("sf_wreck_plate0", "sf_wreck_plate1", "sf_wreck_shard", "sf_wreck_slab", "sf_wreck_chunk0", "sf_wreck_chunk2", "sf_wreck_girder", "sf_wreck_rib"), Around(new P(laneMid, 3500), 100, 900));
        // Along the hull's flanks, in the furrow's shallows and on its banks.
        for (int i = 0; i < 400; i++)
        {
            var p = new P(Layout.Tail - 200 + rnd.NextDouble() * (Layout.NoseEnd - Layout.Tail + 200), Layout.Axis - 380 - rnd.NextDouble() * 760);
            Try(Pick("sf_wreck_plate0", "sf_wreck_plate1", "sf_wreck_plate2", "sf_wreck_shard", "sf_wreck_pod", "sf_wreck_nozzle", "sf_wreck_girder", "sf_wreck_rib", "sf_wreck_chunk1", "sf_wreck_slab"), p);
        }
        // Round the mound's face, thrown up with the dirt.
        for (int i = 0; i < 200; i++)
            Try(Pick("sf_wreck_slab", "sf_wreck_fin", "sf_wreck_spire", "sf_wreck_plate2", "sf_wreck_shard", "sf_wreck_rib", "sf_wreck_spire"), Around(new P(5200, 3400), 100, 1100));
        // The trail it shed coming in, along the furrow's banks in the west.
        for (int i = 0; i < 40; i++)
        {
            var p = new P(120 + rnd.NextDouble() * (Layout.Tail - 200), Layout.Axis - 470 - rnd.NextDouble() * 650);
            Try(Pick("sf_wreck_fin", "sf_wreck_plate1", "sf_wreck_plate2", "sf_wreck_nozzle", "sf_wreck_chunk0", "sf_wreck_chunk1"), p);
        }
        // Thrown across the field: big near the crash, little bits out by the bases.
        for (int i = 0; i < 260; i++)
        {
            var p = new P(200 + rnd.NextDouble() * (Layout.Size - 400), 200 + rnd.NextDouble() * (Layout.Axis - 900));
            double fromCrash = Math.Min((p - new P(laneMid, Layout.Axis)).Length, (p - new P(5000, Layout.Axis)).Length);
            if (rnd.NextDouble() > 1.2 - fromCrash / 3200) continue;
            string name = fromCrash < 1700 ? Pick("sf_wreck_slab", "sf_wreck_plate0", "sf_wreck_fin", "sf_wreck_shard", "sf_wreck_spire", "sf_wreck_pod")
                : fromCrash < 2700 ? Pick("sf_wreck_plate1", "sf_wreck_plate2", "sf_wreck_fin", "sf_wreck_spire", "sf_wreck_chunk0", "sf_wreck_plate0")
                : Pick("sf_wreck_chunk0", "sf_wreck_chunk1", "sf_wreck_chunk2", "sf_wreck_chunk1", rnd.NextDouble() < 0.25 ? "sf_wreck_hatch" : "sf_wreck_chunk2", "sf_wreck_plate1");
            Try(name, p, name.Contains("chunk") ? 240 : 420);
        }
        return items;
    }

    /// <summary>The jungle in the north half.</summary>
    public static List<Item> Jungle(Terrain.Result t, List<Item> ship, List<Item> wreck, Mirror mir)
    {
        var g = new Ground(t);
        var rnd = new Random(2027);
        var forest = new Noise(611);
        var items = new List<Item>();
        var occupied = new HashSet<(int, int)>();
        bool Free(P p, double room)
        {
            if (p.X < 60 || p.X > Layout.Size - 60 || p.Z < 60 || Layout.Axis - p.Z < 12) return false;
            if (UnderShip(ship, p, 24)) return false;
            if (wreck.Any(w => (w.At - p).Length < w.Radius + 30)) return false;
            if (Layout.Spots.Any(s => (s.At - p).Length < 110) || Layout.Vents.Any(v => (v - p).Length < 130)) return false;
            if (Layout.Starts.Any(s => (s - p).Length < 430)) return false;
            if ((p - Layout.MoundTop).Length < 260) return false;                     // the prow
            var cell = ((int)(p.X / room), (int)(p.Z / room));
            return occupied.Add(cell);
        }
        void Add(string name, P p, string kind) => items.Add(new Item(name, p, rnd.NextDouble() * 360, kind, 8));
        double Dense(P p) => Geo.S((mir.Value(p, a => forest.Fbm(a.X / 900, a.Z / 900, 3)) + 0.25) / 0.5);

        for (double z = 20; z < Layout.Axis; z += 34)
            for (double x = 20; x < Layout.Size; x += 34)
            {
                var p = new P(x + rnd.NextDouble() * 30, z + rnd.NextDouble() * 30);
                double h = g.H(p), sl = g.Slope(p), mound = g.MoundShare(p), trail = TrailDistance(p);
                double dense = Math.Max(Dense(p), mound * 0.9);
                double nearStart = Layout.Starts.Min(s => (s - p).Length);
                double clearing = Geo.S((nearStart - 430) / 350);
                double r = rnd.NextDouble();
                if (h < 0)
                {
                    // In the water: reeds and mangrove roots in the shallows.
                    if (h < -16) continue;
                    if (r < 0.04 && Free(p, 70)) Add(rnd.NextDouble() < 0.5 ? "mangrove_1_1" : "mangrove_2_1", p, "mangroves");
                    else if (r < 0.1 && Free(p, 48)) Add(rnd.NextDouble() < 0.6 ? "sf_reeds0" : "sf_reeds1", p, "reeds");
                    continue;
                }
                if (trail < 70 || sl > 40) continue;
                if (h < 6)
                {
                    if (r < 0.12 && Free(p, 40)) Add(rnd.NextDouble() < 0.5 ? "sf_reeds0" : "sf_reeds1", p, "reeds");
                    else if (r < 0.16 && Free(p, 50)) Add(rnd.NextDouble() < 0.5 ? "mangrove_1_1" : "mangrove_2_1", p, "mangroves");
                    continue;
                }
                bool wet = h < 24;
                double ferns = (0.03 + 0.1 * dense + 0.12 * mound) * clearing, trees = 0.15 * dense * clearing * (trail < 140 ? 0.3 : 1);
                if (sl > 30) trees *= 0.2;
                if (r < trees * 0.05 && sl < 14 && Free(p, 220)) Add($"banyan_{1 + rnd.Next(2)}", p, "banyans");
                else if (r < trees * 0.18 && sl < 20 && Free(p, 120)) Add($"kapok{1 + rnd.Next(4)}", p, "kapok");
                else if (r < trees * (wet ? 0.7 : 0.3) && Free(p, 60)) Add($"ad0_senegal_{1 + rnd.Next(7)}{(rnd.NextDouble() < 0.5 ? "_large" : "")}", p, "palms");
                else if (r < trees && Free(p, 56)) Add(PickOak(rnd), p, "oaks");
                else if (r < trees + ferns && Free(p, 30)) Add($"cycas{1 + rnd.Next(8)}", p, "ferns");
                else if (mound > 0.2 && r < trees + ferns + 0.02 && Free(p, 30)) Add($"mushroom0{1 + rnd.Next(9)}", p, "mushrooms");
            }
        return items;
    }

    static string PickOak(Random rnd) => rnd.Next(4) switch
    {
        0 => $"oak_holly_a{1 + rnd.Next(3)}",
        1 => $"oak_holly_b{1 + rnd.Next(3)}",
        _ => $"oak_holly_c{1 + rnd.Next(6)}",
    };

    static string F(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);
    /// <summary>A heading in degrees as the engine's (65536 to a turn), 0 to 65535.</summary>
    static string Heading(double degrees) => F(((degrees % 360 + 360) % 360) * 65536 / 360 % 65536);

    /// <summary>
    /// The MapTool operations: metal, vents, the ship, and every north item with its mirror image in the south
    /// (wreckage as its twin model at heading −h: a mirror image of the original; plants at the mirrored heading).
    /// </summary>
    public static string Ops(Mirror mir, List<Item> ship, List<Item> north)
    {
        var o = new StringBuilder();
        o.AppendLine("# Starfall Marsh: metal, geothermal vents, the ship, the wreckage and the jungle (the south is the north mirrored).");
        foreach (var (at, income, _) in Layout.Spots)
            foreach (var p in new[] { at, mir.Image(at) })
                o.AppendLine($"metal-add:{F(p.X)},{F(p.Z)}:{income.ToString(CultureInfo.InvariantCulture)}");
        foreach (var v in Layout.Vents)
            foreach (var p in new[] { v, mir.Image(v) })
                o.AppendLine($"item-add:smf:geovent:{F(p.X)},{F(p.Z)}");
        foreach (var s in ship)
            o.AppendLine($"item-add:placer:{s.Name}:{F(s.At.X)},{F(s.At.Z)}:{Heading(s.Heading)}");
        foreach (var it in north)
        {
            o.AppendLine($"item-add:placer:{it.Name}:{F(it.At.X)},{F(it.At.Z)}:{Heading(it.Heading)}");
            var q = mir.Image(it.At);
            string twin = it.Kind == "wreck" ? it.Name + "_m" : it.Name;
            double h = it.Kind == "wreck" ? -it.Heading : 180 - it.Heading;
            o.AppendLine($"item-add:placer:{twin}:{F(q.X)},{F(q.Z)}:{Heading(h)}");
        }
        return o.ToString();
    }
}
