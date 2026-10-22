// What MapTool adds: metal, vents, and each planet's features, placed in Team A's part and turned into the others,
// clear of roads, stairs, bridges, ramparts, bases, thrones, spots and vents (and off the kings' level ground,
// which is for building). Names are BAR's own (rocks, crystals, wrecks, tombstones), All That Simmers' (palms,
// ferns, mushrooms: in the template), or the editor's library's (firs and birches from Crimson Bay; pipes, crates
// and trucks from Industrial Revolution), copied in with asset-add.
using System.Globalization;
using System.Text;

static class Place
{
    public sealed record Item(string Name, P At, double Heading, string Kind);

    sealed class Ground(Design d, Surface s, Masks m, Terrain.Result t)
    {
        public readonly Random Rnd = new(2610);
        public double H(P p) => s.At(s.H, p.X, p.Z);
        public double Slope(P p) => s.At(s.Slope, p.X, p.Z);
        public bool InA(P p) => d.PartOf(p) == 0;
        public bool Top(P p) => m.At(m.Top, p.X, p.Z) > 0.1;
        public bool CliffRock(P p) => m.At(m.Cliff, p.X, p.Z) > 0.5;
        public double StairDist(P p) => Math.Min(
            t.Ramps.Count == 0 ? 999 : t.Ramps.Min(r => Geo.ToSegment(p, r.Foot, r.Top, out _) - r.HalfWidth),
            t.Paths.Count == 0 ? 999 : t.Paths.Min(q => Geo.ToPolyline(p, q.Line, out _) - Terrain.PathHalf));
        public bool Clear(P p, double roads = 0.12, double starts = 380) =>
            d.InPlay(p, 90) && m.At(m.Glow, p.X, p.Z) < roads && m.At(m.Stair, p.X, p.Z) == 0 && m.At(m.Deck, p.X, p.Z) < 0.01
            && m.At(m.Ford, p.X, p.Z) < 0.01 && m.At(m.Wall, p.X, p.Z) < 0.01 && m.At(m.Plaza, p.X, p.Z) < 0.05
            && m.At(m.SpotDist, p.X, p.Z) > 120 && m.At(m.VentDist, p.X, p.Z) > 140 && m.At(m.StartDist, p.X, p.Z) > starts
            && StairDist(p) > 110 && H(p) > 6;
        public IEnumerable<P> GridA(double step)
        {
            for (double z = 0; z < d.MapSize; z += step)
                for (double x = 0; x < d.MapSize; x += step)
                {
                    var p = new P(x + Rnd.NextDouble() * step, z + Rnd.NextDouble() * step);
                    if (InA(p)) yield return p;
                }
        }
        public P Around(P c, double r0, double r1)
        {
            double a = Rnd.NextDouble() * Math.PI * 2, r = r0 + (r1 - r0) * Math.Sqrt(Rnd.NextDouble());
            return c + new P(Math.Cos(a), Math.Sin(a)) * r;
        }
        public string Pick(params string[] names) => names[Rnd.Next(names.Length)];
        public double Heading() => Rnd.Next(360);
    }

    public static List<Item> Features(Design d, Look look, Surface s, Masks m, Terrain.Result t, List<P> groves)
    {
        var g = new Ground(d, s, m, t);
        var items = look switch
        {
            Dune => DuneFeatures(d, g, groves),
            Heath => HeathFeatures(d, g, m),
            Forge => ForgeFeatures(d, g, m),
            _ => [],
        };
        // Nothing on top of anything else.
        var kept = new List<Item>();
        foreach (var it in items.Where(i => g.InA(i.At)))
            if (!kept.Any(k => (k.At - it.At).Length < (k.Kind == "props" || it.Kind == "props" ? 70 : 30))) kept.Add(it);
        return kept;
    }

    static string Rock(Ground g, string set) => $"rocks30_{set}_{g.Rnd.Next(1, 31):00}";

    static List<Item> DuneFeatures(Design d, Ground g, List<P> groves)
    {
        var items = new List<Item>();
        // Oases: palms round each, ferns beneath.
        foreach (var c in groves.Where(g.InA))
        {
            for (int k = 0; k < 40 && items.Count(i => i.Kind == "palms" && (i.At - c).Length < 500) < 15; k++)
            {
                var p = g.Around(c, 60, 430);
                if (g.Clear(p) && g.Slope(p) < 16 && !g.Top(p))
                    items.Add(new Item($"ad0_senegal_{g.Rnd.Next(1, 8)}{(g.Rnd.NextDouble() < 0.5 ? "_large" : "")}", p, g.Heading(), "palms"));
            }
            for (int k = 0; k < 10; k++)
            {
                var p = g.Around(c, 40, 360);
                if (g.Clear(p) && g.Slope(p) < 18 && !g.Top(p)) items.Add(new Item($"cycas{g.Rnd.Next(1, 9)}", p, g.Heading(), "ferns"));
            }
        }
        // Rock outcrops in the dunes, and scree at the ridges' feet.
        foreach (var c in g.GridA(850))
        {
            if (g.Rnd.NextDouble() > 0.45 || !g.Clear(c) || g.Top(c)) continue;
            int count = g.Rnd.Next(3, 8);
            for (int k = 0; k < count; k++)
            {
                var p = g.Around(c, 0, 130);
                if (g.Clear(p) && g.Slope(p) < 22 && !g.Top(p)) items.Add(new Item(Rock(g, "desert"), p, g.Heading(), "rocks"));
            }
        }
        foreach (var p in g.GridA(90))
            if (!g.CliffRock(p) && g.Slope(p) is > 8 and < 26 && !g.Top(p) && g.Clear(p) && g.Rnd.NextDouble() < 0.18)
                items.Add(new Item(Rock(g, "desert"), p, g.Heading(), "scree"));
        // An old battle half-buried in the sand.
        foreach (var c in g.GridA(1500))
        {
            if (g.Rnd.NextDouble() > 0.3 || !g.Clear(c, starts: 700) || g.Top(c)) continue;
            for (int k = 0; k < 4; k++)
            {
                var p = g.Around(c, 0, 220);
                if (g.Clear(p, starts: 600) && g.Slope(p) < 14)
                    items.Add(new Item(g.Pick("armsolar_dead", "armadvsol_dead", "corsolar_dead", "armwin_dead", "corwin_dead"), p, g.Heading(), "wrecks"));
            }
        }
        return items;
    }

    static List<Item> HeathFeatures(Design d, Ground g, Masks m)
    {
        var items = new List<Item>();
        string Fir() => g.Rnd.NextDouble() < 0.7
            ? $"fir_tree_tall_{g.Rnd.Next(1, 9)}__tree_fir_tall_{g.Rnd.Next(1, 6)}"
            : $"fir_tree_small_{g.Rnd.Next(1, 4)}__tree_fir_tall_{g.Rnd.Next(1, 6)}";
        // Pine shelterbelts: double rows along gentle curves across the fields.
        int belts = 0;
        foreach (var c in g.GridA(1100))
        {
            if (belts >= 3 || g.Rnd.NextDouble() > 0.5 || !g.Clear(c, starts: 500) || g.Top(c)) continue;
            belts++;
            double a = g.Rnd.NextDouble() * Math.PI, bend = (g.Rnd.NextDouble() - 0.5) * 0.0012, len = 900 + g.Rnd.NextDouble() * 600;
            for (double s = -len / 2; s <= len / 2; s += 52)
            {
                double ang = a + bend * s;
                var along = c + new P(Math.Cos(ang), Math.Sin(ang)) * s;
                var side = new P(-Math.Sin(ang), Math.Cos(ang));
                foreach (double row in new[] { -24.0, 24.0 })
                {
                    var p = along + side * (row + (g.Rnd.NextDouble() - 0.5) * 14);
                    if (g.Clear(p, starts: 420) && g.Slope(p) < 20 && !g.Top(p)) items.Add(new Item(Fir(), p, g.Heading(), "pines"));
                }
            }
        }
        // Birches by the loch, and scattered pines on the slopes.
        foreach (var p in g.GridA(85))
        {
            double h = g.H(p);
            if (!g.Clear(p) || g.Top(p)) continue;
            if (h is > 10 and < 150 && Wet(d, p, 700) && g.Rnd.NextDouble() < 0.4)
                items.Add(new Item($"euro_birch_tree_0{g.Rnd.Next(1, 9)}_{g.Rnd.Next(1, 5)}", p, g.Heading(), "birches"));
            else if (g.Slope(p) is > 5 and < 22 && g.Rnd.NextDouble() < 0.05)
                items.Add(new Item(Fir(), p, g.Heading(), "pines"));
            else if (g.Slope(p) is > 4 and < 24 && g.Rnd.NextDouble() < 0.16)
                items.Add(new Item($"cycas{g.Rnd.Next(1, 9)}", p, g.Heading(), "bracken"));
        }
        // Granite tors on the high ground, and boulders.
        foreach (var c in g.GridA(800))
        {
            if (g.Rnd.NextDouble() > 0.5 || !g.Clear(c) || g.Top(c)) continue;
            int count = g.Rnd.Next(5, 10);
            for (int k = 0; k < count; k++)
            {
                var p = g.Around(c, 0, 150);
                if (g.Clear(p) && !g.Top(p)) items.Add(new Item(Rock(g, "def"), p, g.Heading(), "tors"));
            }
        }
        // Cairns on the keeps' rims, mossy rocks round the isle.
        foreach (var p in g.GridA(140))
        {
            if (g.Top(p) && m.At(m.EdgeDist, p.X, p.Z) is > 60 and < 200 && g.StairDist(p) > 160 && m.At(m.SpotDist, p.X, p.Z) > 120
                && m.At(m.VentDist, p.X, p.Z) > 140 && m.At(m.StartDist, p.X, p.Z) > 700 && g.Rnd.NextDouble() < 0.25)
                items.Add(new Item("legstone", p, g.Heading(), "cairns"));
            if (d.Land.Count > 0 && d.Land.Any(l => l.Contains(p)) && g.H(p) > 10 && g.Clear(p) && g.Rnd.NextDouble() < 0.5)
                items.Add(new Item(Rock(g, "moss"), p, g.Heading(), "isle rocks"));
        }
        return items;
    }

    static bool Wet(Design d, P p, double within) => d.Water.Any(w => w.Signed(p) < within);

    static List<Item> ForgeFeatures(Design d, Ground g, Masks m)
    {
        var items = new List<Item>();
        // Pipe runs outside the fortress ramparts, along them.
        foreach (var w in d.Walls.Where(w => g.InA((w.A + w.B) * 0.5)))
        {
            var u = (w.B - w.A).Unit;
            var n = u.Perp;
            var mid = (w.A + w.B) * 0.5;
            if (g.Top(mid + n * (w.HalfWidth + 60))) n = n * -1;
            double heading = Math.Atan2(u.X, -u.Z) * 180 / Math.PI;
            for (double s = 60; s < (w.B - w.A).Length - 60; s += 130)
            {
                var p = w.A + u * s + n * (w.HalfWidth + 90);
                if (g.Clear(p, roads: 0.3, starts: 300)) items.Add(new Item(g.Pick("pipeslong1", "pipeslong2", "pipeslong3", "pipesarray1", "pipesarray2"), p, heading, "props"));
            }
        }
        // Container yards: two a part, rows of crates; trucks parked by the roads; a crane by the market.
        int yards = 0;
        foreach (var c in g.GridA(700))
        {
            if (yards >= 2 || g.Rnd.NextDouble() > 0.5 || !g.Clear(c, starts: 500) || g.Top(c)) continue;
            yards++;
            double a = g.Rnd.Next(4) * 90;
            var u = new P(Math.Sin(a * Math.PI / 180), -Math.Cos(a * Math.PI / 180));
            var v = u.Perp;
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 3; j++)
                {
                    if (g.Rnd.NextDouble() < 0.25) continue;
                    var p = c + u * (i * 80 - 120) + v * (j * 80 - 80);
                    if (g.Clear(p, starts: 350)) items.Add(new Item(g.Pick("crateblue", "crateorange", "cratered", "crateyellow"), p, a, "props"));
                }
        }
        foreach (var path in d.Paths)
            for (int i = 3; i + 1 < path.Length; i += 9)
            {
                if (g.Rnd.NextDouble() > 0.35) continue;
                var u = (path[i + 1] - path[i]).Unit;
                var p = path[i] + u.Perp * (g.Rnd.NextDouble() < 0.5 ? 130 : -130);
                if (g.Clear(p, roads: 0.5, starts: 350)) items.Add(new Item(g.Pick("minitruck1", "minitruck2"), p, Math.Atan2(u.X, -u.Z) * 180 / Math.PI, "props"));
            }
        foreach (var p in g.GridA(60))
            if (d.Radius(p) is > 1300 and < 1700 && g.Clear(p, roads: 0.3) && g.Rnd.NextDouble() < 0.02)
                items.Add(new Item("cranetruck1", p, g.Heading(), "props"));
        // Wreck fields with rubble in the open ground.
        foreach (var c in g.GridA(900))
        {
            if (g.Rnd.NextDouble() > 0.35 || !g.Clear(c, starts: 600) || g.Top(c)) continue;
            for (int k = 0; k < 6; k++)
            {
                var p = g.Around(c, 0, 240);
                if (!g.Clear(p, starts: 500) || g.Top(p)) continue;
                items.Add(k < 3
                    ? new Item(g.Pick("armestor_dead", "corestor_dead", "armmex_dead", "cormex_dead", "armsolar_dead", "corsolar_dead"), p, g.Heading(), "wrecks")
                    : new Item(Rock(g, "map"), p, g.Heading(), "rubble"));
            }
        }
        // Mushrooms in the damp corners of the yards, against the ramparts.
        foreach (var p in g.GridA(45))
            if (g.Top(p) && m.At(m.EdgeDist, p.X, p.Z) is > 130 and < 260 && m.At(m.Wall, p.X, p.Z) < 0.01 && g.StairDist(p) > 120
                && m.At(m.SpotDist, p.X, p.Z) > 110 && m.At(m.VentDist, p.X, p.Z) > 130 && m.At(m.StartDist, p.X, p.Z) > 700 && g.Rnd.NextDouble() < 0.12)
                items.Add(new Item($"mushroom0{g.Rnd.Next(1, 10)}", p, g.Heading(), "mushrooms"));
        return items;
    }

    static string F(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);

    /// <summary>The MapTool operations: assets copied in, metal, vents and every item, turned into each part.</summary>
    public static string Ops(Design d, IEnumerable<string> assets, List<Item> items)
    {
        var o = new StringBuilder();
        o.AppendLine($"# {d.Name}: assets, metal, geothermal vents and features (every part; no mirror).");
        foreach (var a in assets) o.AppendLine($"asset-add:{a}");
        foreach (var s in d.Spots) o.AppendLine($"metal-add:{F(s.At.X)},{F(s.At.Z)}:{s.Income.ToString(CultureInfo.InvariantCulture)}");
        foreach (var v in d.Vents) o.AppendLine($"item-add:smf:geovent:{F(v.At.X)},{F(v.At.Z)}");
        for (int k = 0; k < d.Fold; k++)
            foreach (var it in items)
            {
                var p = d.Turn(it.At, d.Step * k);
                o.AppendLine($"item-add:placer:{it.Name}:{F(p.X)},{F(p.Z)}:{F(((it.Heading + d.Step * k) % 360 + 360) % 360)}");
            }
        return o.ToString();
    }
}
