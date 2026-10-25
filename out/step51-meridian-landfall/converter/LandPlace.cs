// What MapTool adds to Meridian Landfall: metal, the vents, and the features.
//   - the ship's own features (Meridian's Place.Ship), moved with the ship;
//   - the landing struts on the hull's long sides, between the bay doors, and cargo set down at the aprons' corners;
//   - moonrock: shards, slabs and boulders thrown round each void pit, boulders on the bowls' rims, a few on the small
//     craters', crags along the asteroid's edge, and boulders scattered over the plains; never on the aprons, the
//     mares, the spots or vents, the ship or the void;
//   - the void's rubble: an invisible blocker over every 128 × 128 cell with void in it.
// Everything but the rubble is placed for the port half and again at its mirror image (as its twin if lopsided); the
// rubble covers the void, which is its own mirror image. Headings in degrees here (0 east, 90 north), in the engine's
// units in run.txt.
using System.Globalization;
using System.Text;

static class LandPlace
{
    public static List<Place.Item> Items(Asteroid.Land land, ISet<string> asymmetric)
    {
        var port = new List<Place.Item>();
        void Add(string name, P at, double heading, string kind) => port.Add(new Place.Item(name, at, heading, kind, 0));

        // The ship's own, moved (Meridian's list already has both halves and its centreline).
        var ship = Place.Ship(asymmetric).Select(it => it with { At = World.FromShip(it.At), Kind = "ship" }).ToList();

        // Landing struts, against the hull's long side between the bay doors.
        double hullX = 900 + World.OX;
        foreach (double z in new[] { 6250.0, 9450, 13650 }) Add("lf_strut", new P(hullX - 115, z), 0, "strut");

        // Cargo at each apron's outer corners, clear of the way in.
        foreach (var a in World.ApronsPort)
        {
            double zc = (a.Z0 + a.Z1) / 2;
            Add("sm_container0", new P(a.X0 + 220, zc - 520), 90, "cargo");
            Add("sm_container1", new P(a.X0 + 380, zc - 520), 90, "cargo");
            Add("sm_crate", new P(a.X0 + 560, zc - 560), 0, "cargo");
            Add("sm_crate", new P(a.X0 + 600, zc - 515), 0, "cargo");
            Add("sm_pallet", new P(a.X0 + 260, zc + 545), 0, "cargo");
            Add("sm_tug", new P(a.X0 + 520, zc + 540), 90, "cargo");
        }

        // Moonrock.
        var taken = new List<(P At, double R)>();
        bool Free(P p, double r)
        {
            if (p.X > World.CX - r - 20 || land.VoidAt(p) < 40 + r || land.ShipCell(p) != Terrain.Space) return false;
            if (Terrain.ToOutline(World.ToShip(p), Layout.Hull) < 220 + r) return false;
            if (World.Aprons.Any(a => a.Has(p, r + 120)) || World.BayDoors.Any(d => d.Has(p, r + 200))) return false;
            if (World.Starts.Any(s => (s.At - p).Length < World.MareR + 150 + r)) return false;
            if (World.Spots.Any(s => (s.At - p).Length < r + 170) || World.Vents.Any(v => (v - p).Length < r + 190)) return false;
            if (taken.Any(t => (t.At - p).Length < t.R + r + 12)) return false;
            return true;
        }
        void Rock(string name, P p, double r, double heading)
        {
            if (!Free(p, r)) return;
            taken.Add((p, r));
            Add(name, p, heading, "rock");
        }
        double Out(P from, P to) => Place.Facing(to - from);

        // Round each pit: shards and slabs on the shattered rim, thrown boulders beyond, thinning out.
        foreach (var c in World.Pits)
            for (int n = 0; n < 46; n++)
            {
                double a = 6.283 * World.Hash01(c.Key, n, 101), rj = World.Jag(c.R, c.Key, a);
                double reach = Math.Pow(World.Hash01(c.Key, n, 102), 1.6);
                var p = c.At + new P(Math.Cos(a), Math.Sin(a)) * (rj + 30 + reach * c.R * 0.9);
                double pick = World.Hash01(c.Key, n, 103), heading = Out(c.At, p) + 30 * (World.Hash01(c.Key, n, 104) - 0.5);
                if (reach < 0.3)
                    Rock(pick < 0.5 ? "lf_shard1" : pick < 0.75 ? "lf_shard2" : pick < 0.9 ? "lf_slab1" : "lf_slab2", p, 50, heading);
                else
                    Rock(pick < 0.45 ? "lf_boulder_s1" : pick < 0.7 ? "lf_boulder_s2" : pick < 0.85 ? "lf_boulder_m1" : pick < 0.95 ? "lf_boulder_m2" : "lf_boulder_l", p, pick < 0.7 ? 18 : pick < 0.95 ? 40 : 70, heading);
            }

        // On the bowls' rims, and a few on the small craters'.
        foreach (var c in World.Bowls.Concat(World.Small))
        {
            int count = c.Key < 3000 ? 16 : (int)(3 * World.Hash01(c.Key, 110));
            for (int n = 0; n < count; n++)
            {
                double a = 6.283 * World.Hash01(c.Key, n, 111), rj = World.Waver(c.R, c.Key, a);
                var p = c.At + new P(Math.Cos(a), Math.Sin(a)) * (rj + 20 + World.Hash01(c.Key, n, 112) * Math.Max(40, c.R * 0.4));
                double pick = World.Hash01(c.Key, n, 113), heading = 360 * World.Hash01(c.Key, n, 114);
                if (c.Key >= 3000) Rock(pick < 0.5 ? "lf_boulder_s1" : "lf_boulder_s2", p, 18, heading);
                else Rock(pick < 0.3 ? "lf_boulder_s1" : pick < 0.55 ? "lf_boulder_m1" : pick < 0.8 ? "lf_boulder_m2" : pick < 0.93 ? "lf_boulder_l" : "lf_slab1", p, pick < 0.3 ? 18 : pick < 0.8 ? 40 : 70, heading);
            }
        }

        // On the asteroid's worn edge: boulders weathered out of the slope and come to rest on it, the odd slab or shard.
        for (double z = 120; z < World.H; z += 170)
            for (double x = 120; x < World.CX; x += 170)
            {
                int i = (int)(x / 170), j = (int)(z / 170);
                var p = new P(x + 120 * (World.Hash01(i, j, 121) - 0.5), z + 120 * (World.Hash01(i, j, 122) - 0.5));
                double down = Asteroid.EdgeAt(p).Down;
                if (down < 10 || down > 230 || World.Hash01(i, j, 123) > 0.42) continue;
                double pick = World.Hash01(i, j, 124), heading = 360 * World.Hash01(i, j, 125);
                Rock(pick < 0.3 ? "lf_boulder_s1" : pick < 0.5 ? "lf_boulder_s2" : pick < 0.72 ? "lf_boulder_m1" : pick < 0.85 ? "lf_boulder_m2" : pick < 0.93 ? "lf_boulder_l" : "lf_slab2",
                    p, pick < 0.5 ? 18 : pick < 0.85 ? 40 : 70, heading);
            }

        // Scattered over the plains: mostly small, a few big, and here and there a huge one.
        for (double z = 130; z < World.H; z += 260)
            for (double x = 130; x < World.CX; x += 260)
            {
                int i = (int)(x / 260), j = (int)(z / 260);
                double roll = World.Hash01(i, j, 131);
                if (roll > 0.16) continue;
                var p = new P(x + 200 * (World.Hash01(i, j, 132) - 0.5), z + 200 * (World.Hash01(i, j, 133) - 0.5));
                double heading = 360 * World.Hash01(i, j, 134);
                if (roll < 0.006) Rock("lf_boulder_xl", p, 120, heading);
                else if (roll < 0.02) Rock("lf_boulder_l", p, 70, heading);
                else if (roll < 0.05) Rock(roll < 0.035 ? "lf_boulder_m1" : "lf_boulder_m2", p, 40, heading);
                else Rock(roll < 0.1 ? "lf_boulder_s1" : "lf_boulder_s2", p, 18, heading);
            }

        // The void's rubble, over every 128 × 128 cell with void in it (heading 90°: its own mirror image).
        var rubble = new List<Place.Item>();
        int cells = (int)(World.W / 128), rows = (int)(World.H / 128);
        for (int j = 0; j < rows; j++)
            for (int i = 0; i < cells; i++)
            {
                bool any = false;
                for (int z = j * 16; z <= j * 16 + 16 && !any; z++)
                    for (int x = i * 16; x <= i * 16 + 16 && !any; x++)
                        if (land.VoidV[Math.Min(z, World.NZ - 1) * World.NX + Math.Min(x, World.NX - 1)] < 0) any = true;
                if (any) rubble.Add(new Place.Item("lf_void", new P((i + 0.5) * 128, (j + 0.5) * 128), 90, "void", 0));
            }

        return [.. ship, .. port, .. port.Select(it => Place.Mirrored(it, asymmetric) with { At = World.Mirror(it.At) }), .. rubble];
    }

    static string F(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);

    public static string Ops(IEnumerable<Place.Item> items)
    {
        var o = new StringBuilder();
        o.AppendLine("# Meridian Landfall: metal, the vents, the ship's and the asteroid's features, the void's rubble.");
        foreach (var (at, income, _) in World.Spots)
            o.AppendLine($"metal-add:{F(at.X)},{F(at.Z)}:{income.ToString(CultureInfo.InvariantCulture)}");
        foreach (var at in World.Vents)
            o.AppendLine($"item-add:smf:geovent:{F(at.X)},{F(at.Z)}");
        foreach (var it in items)
            o.AppendLine($"item-add:placer:{it.Name}:{F(it.At.X)},{F(it.At.Z)}:{Place.Units(it.Heading)}");
        return o.ToString();
    }
}
