// What MapTool adds: the metal spots, the geothermal vents, and the Bloom planet's plants, crystals and rocks
// (planets.py), placed in Team A's fifth and turned into the others, clear of roads, stairs, bridges, fords, bases,
// thrones, spots and vents. Plants are All That Simmers' (in the template); crystals and mossy rocks are BAR's own.
using System.Globalization;
using System.Text;

static class Place
{
    public sealed record Item(string Name, P At, double Heading, string Kind);

    static string F(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);

    public static List<Item> Vegetation(Design d, Surface s, Masks m)
    {
        var rnd = new Random(2610);
        var grove = new Noise(404);
        var items = new List<Item>();
        var hallA = d.Halls[0];

        double WaterDistance(P p)
        {
            double r = (p - Design.Centre).Length;
            return Math.Min(Math.Abs(r - d.LakeRadius), Math.Max(0, Terrain.RiverDistance(p, out _) - d.RiverHalf));
        }
        double MadeDistance(P p) =>
            Math.Min(Terrain.Ramps.Where(r => r.Team == 0).Min(r => Design.ToSegment(p, r.Foot, r.Top, out _) - r.HalfWidth),
                Terrain.Switchbacks.Where(sw => sw.Team == 0).Min(sw => Design.ToPolyline(p, sw.Line, out _) - Terrain.PathHalf));
        bool Clear(P p, double roads = 0.12) =>
            m.At(m.Glow, p.X, p.Z) < roads && m.At(m.Stair, p.X, p.Z) == 0 && m.At(m.Deck, p.X, p.Z) < 0.01 && m.At(m.Ford, p.X, p.Z) < 0.01
            && m.At(m.Plaza, p.X, p.Z) < 0.05 && m.At(m.SpotDist, p.X, p.Z) > 120 && m.At(m.VentDist, p.X, p.Z) > 140
            && m.At(m.StartDist, p.X, p.Z) > 380 && MadeDistance(p) > 110
            && (p - Design.Centre).Length < d.PlayRadius - 90;
        bool InA(P p) => Design.FifthOf(p) == 0;
        IEnumerable<P> Grid(double step)
        {
            for (double z = 0; z < Design.MapSize; z += step)
                for (double x = 0; x < Design.MapSize; x += step)
                {
                    var p = new P(x + rnd.NextDouble() * step, z + rnd.NextDouble() * step);
                    if (InA(p)) yield return p;
                }
        }
        double H(P p) => s.At(s.H, p.X, p.Z);
        double Sl(P p) => s.At(s.Slope, p.X, p.Z);
        bool OnHall(P p) => Terrain.HallDistance(hallA, p) < 40;

        // Giant trees in groves: kapok mostly, some oaks.
        foreach (var p in Grid(95))
        {
            var f = Design.Fold(p);
            double g = grove.Fbm(f.X / 1200, f.Z / 1200, 3);
            if (g < 0.16 || rnd.NextDouble() > Design.S((g - 0.16) / 0.2)) continue;
            double h = H(p);
            if (h < 30 || h > 250 || Sl(p) > 16 || OnHall(p) || WaterDistance(p) < 350 || !Clear(p)) continue;
            string name = rnd.NextDouble() < 0.7 ? $"kapok{rnd.Next(1, 5)}" : $"oak_holly_b{rnd.Next(1, 4)}";
            items.Add(new Item(name, p, rnd.Next(360), "trees"));
        }
        // Ferns along the banks.
        foreach (var p in Grid(70))
        {
            double w = WaterDistance(p), h = H(p);
            if (h < 8 || h > 130 || Sl(p) > 22 || w > 520 || OnHall(p)) continue;
            if (rnd.NextDouble() > 0.55 * (1 - Design.S((w - 60) / 460)) || !Clear(p, 0.3)) continue;
            items.Add(new Item($"cycas{rnd.Next(1, 9)}", p, rnd.Next(360), "ferns"));
        }
        // Glowing mushroom rings in the fields.
        foreach (var c in Grid(900))
        {
            if (rnd.NextDouble() > 0.4 || H(c) < 40 || H(c) > 240 || OnHall(c)) continue;
            for (int k = 0; k < 7; k++)
            {
                double a = k * Math.PI * 2 / 7 + rnd.NextDouble() * 0.3;
                var p = c + new P(Math.Cos(a), Math.Sin(a)) * (70 + rnd.NextDouble() * 12);
                if (InA(p) && Sl(p) < 14 && Clear(p)) items.Add(new Item($"mushroom0{rnd.Next(1, 10)}", p, rnd.Next(360), "mushrooms"));
            }
        }
        // Crystals at the water's edge, and round the isle.
        string Crystal() => $"pilha_crystal_{new[] { "teal", "teal", "lime", "blue" }[rnd.Next(4)]}{(rnd.NextDouble() < 0.6 ? 1 : rnd.NextDouble() < 0.7 ? 2 : 3)}";
        foreach (var p in Grid(160))
        {
            double w = WaterDistance(p), h = H(p);
            if (h < 6 || h > 40 || w > 170 || Sl(p) > 24 || rnd.NextDouble() > 0.35 || !Clear(p)) continue;
            items.Add(new Item(Crystal(), p, rnd.Next(360), "crystals"));
        }
        foreach (var p in Grid(120))
        {
            double r = (p - Design.Centre).Length;
            if (r < 330 || r > d.IsleRadius - 150 || rnd.NextDouble() > 0.4 || !Clear(p)) continue;
            items.Add(new Item(Crystal(), p, rnd.Next(360), "isle crystals"));
        }
        // Mossy rocks along the halls' rims (on top) and their feet (below), off the ramp, switchbacks and throne.
        foreach (var p in Grid(110))
        {
            double e = Terrain.HallDistance(hallA, p);
            bool rim = e > -220 && e < -70, foot = e > 40 && e < 150;
            if (!(rim || foot) || rnd.NextDouble() > (rim ? 0.35 : 0.25)) continue;
            if (m.At(m.Glow, p.X, p.Z) > 0.12 || MadeDistance(p) < 120 || m.At(m.SpotDist, p.X, p.Z) < 120 || m.At(m.VentDist, p.X, p.Z) < 140
                || m.At(m.StartDist, p.X, p.Z) < (rim ? 780 : 380)) continue;
            items.Add(new Item($"rocks30_moss_{rnd.Next(1, 31):00}", p, rnd.Next(360), "rocks"));
        }
        // Thin out anything closer than 30 units to another (trees on top of each other).
        var kept = new List<Item>();
        foreach (var it in items)
            if (!kept.Any(k => (k.At - it.At).Length < 30)) kept.Add(it);
        return kept;
    }

    /// <summary>The MapTool operations: metal, vents and every item, turned into each fifth.</summary>
    public static string Ops(Design d, List<Item> items)
    {
        var o = new StringBuilder();
        o.AppendLine("# Five Rivers: metal, geothermal vents, plants, crystals and rocks (all five fifths; no mirror).");
        foreach (var s in d.Spots)
            o.AppendLine($"metal-add:{F(s.At.X)},{F(s.At.Z)}:{s.Income.ToString(CultureInfo.InvariantCulture)}");
        foreach (var v in d.Vents)
            o.AppendLine($"item-add:smf:geovent:{F(v.At.X)},{F(v.At.Z)}");
        for (int k = 0; k < Design.Teams; k++)
            foreach (var it in items)
            {
                var p = Design.Turn(it.At, 72 * k);
                o.AppendLine($"item-add:placer:{it.Name}:{F(p.X)},{F(p.Z)}:{F((it.Heading + 72 * k) % 360)}");
            }
        return o.ToString();
    }
}
