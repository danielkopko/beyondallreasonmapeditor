// What MapTool adds: metal, vents, and each planet's features, placed in Team A's part and turned into the others,
// clear of roads, stairs, bridges, ramparts, bases, thrones, spots and vents. The kings' level ground is kept for
// building, but for cairns along a keep's rim and crates stacked well back from a fortress's king (900 or more);
// `KingMaps check <geometry> <map> run.txt` tests the access round them all. Names are BAR's own (rocks, crystals,
// wrecks, tombstones), All That Simmers' (palms, ferns: in the template), or the editor's library's (firs and birches
// from Crimson Bay; crates and trucks from Industrial Revolution), copied in with asset-add.
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
            if (!kept.Any(k => (k.At - it.At).Length < Math.Max(Room(k.Kind), Room(it.Kind)))) kept.Add(it);
        return kept;
    }

    /// <summary>How far apart a kind of feature keeps from the others: wrecked buildings most, props next.</summary>
    static double Room(string kind) => kind switch { "ruins" => 110, "props" => 70, _ => 30 };

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
        string Birch() => $"euro_birch_tree_0{g.Rnd.Next(1, 9)}_{g.Rnd.Next(1, 5)}";
        double Rise(P p) => g.H(p) - d.GroundH;
        bool Open(P p, double starts = 380) => g.Clear(p, starts: starts) && !g.Top(p) && !g.CliffRock(p);
        // Pinewoods: old stands of pine on the gentler ground, thick in the middle and thinning out at the edges.
        foreach (var c in g.GridA(1300))
        {
            if (g.Rnd.NextDouble() > 0.55 || !Open(c, 600) || g.Slope(c) > 14) continue;
            double r = 260 + g.Rnd.NextDouble() * 220;
            for (int k = 0; k < 160; k++)
            {
                var p = g.Around(c, 0, r * 1.25);
                if (g.Rnd.NextDouble() > 1.1 - 0.8 * (p - c).Length / r) continue;
                if (Open(p, 420) && g.Slope(p) < 20) items.Add(new Item(Fir(), p, g.Heading(), "pines"));
            }
        }
        // Birch copses along the loch and in the wet hollows.
        foreach (var c in g.GridA(700))
        {
            bool shore = Wet(d, c, 600) && g.H(c) > 10;
            if (!(shore || Rise(c) < -18) || g.Rnd.NextDouble() > (shore ? 0.9 : 0.5) || !Open(c)) continue;
            int count = g.Rnd.Next(8, 18);
            for (int k = 0; k < count; k++)
            {
                var p = g.Around(c, 0, 200);
                if (Open(p) && g.H(p) > 10 && g.Slope(p) < 22) items.Add(new Item(Birch(), p, g.Heading(), "birches"));
            }
        }
        // Lone pines over the open moor, bracken on its slopes.
        foreach (var p in g.GridA(110))
        {
            if (!Open(p)) continue;
            if (g.Rnd.NextDouble() < 0.025 && g.Slope(p) < 18) items.Add(new Item(Fir(), p, g.Heading(), "pines"));
            else if (g.Slope(p) is > 5 and < 24 && g.Rnd.NextDouble() < 0.14) items.Add(new Item($"cycas{g.Rnd.Next(1, 9)}", p, g.Heading(), "bracken"));
        }
        // Granite tors on the crests: a stack of rocks with boulders strewn round it.
        foreach (var c in g.GridA(900))
        {
            if (Rise(c) < 12 || g.Rnd.NextDouble() > 0.7 || !Open(c)) continue;
            int count = g.Rnd.Next(6, 12);
            for (int k = 0; k < count; k++)
            {
                var p = g.Around(c, 0, k < 4 ? 50 : 170);
                if (Open(p)) items.Add(new Item(Rock(g, "def"), p, g.Heading(), "tors"));
            }
        }
        // Scree under the crags and down the loch's banks, and boulders the ice left across the moor.
        foreach (var p in g.GridA(70))
            if (Open(p) && g.Slope(p) is > 12 and < 30 && g.Rnd.NextDouble() < 0.25) items.Add(new Item(Rock(g, "def"), p, g.Heading(), "scree"));
        foreach (var p in g.GridA(600))
            if (Open(p) && g.Rnd.NextDouble() < 0.35) items.Add(new Item(Rock(g, "def"), p, g.Heading(), "boulders"));
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
        // Ruined works: a wrecked factory, its storage tanks and metal makers round it, rubble strewn about.
        foreach (var c in g.GridA(1100))
        {
            if (g.Rnd.NextDouble() > 0.6 || !g.Clear(c, roads: 0.05, starts: 700) || g.Top(c) || g.Slope(c) > 10) continue;
            items.Add(new Item(g.Pick("armvp_dead", "corvp_dead", "armlab_dead", "corlab_dead", "armmoho_dead", "cormoho_dead"), c, g.Rnd.Next(4) * 90, "ruins"));
            for (int k = 0; k < 4; k++)
            {
                var p = g.Around(c, 160, 290);
                if (g.Clear(p, roads: 0.05, starts: 600) && !g.Top(p) && g.Slope(p) < 12)
                    items.Add(new Item(g.Pick("armmstor_dead", "cormstor_dead", "armestor_dead", "corestor_dead", "armmmkr_dead", "cormmkr_dead"), p, g.Rnd.Next(4) * 90, "ruins"));
            }
            for (int k = 0; k < 10; k++)
            {
                var p = g.Around(c, 120, 400);
                if (g.Clear(p, starts: 500) && !g.Top(p)) items.Add(new Item(Rock(g, "map"), p, g.Heading(), "rubble"));
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
        // Smaller wrecks with rubble in the open ground.
        foreach (var c in g.GridA(900))
        {
            if (g.Rnd.NextDouble() > 0.25 || !g.Clear(c, starts: 600) || g.Top(c)) continue;
            for (int k = 0; k < 6; k++)
            {
                var p = g.Around(c, 0, 240);
                if (!g.Clear(p, starts: 500) || g.Top(p)) continue;
                items.Add(k < 3
                    ? new Item(g.Pick("armestor_dead", "corestor_dead", "armmex_dead", "cormex_dead", "armsolar_dead", "corsolar_dead"), p, g.Heading(), "wrecks")
                    : new Item(Rock(g, "map"), p, g.Heading(), "rubble"));
            }
        }
        // Crates stacked in the fortress yards, well back from the king; weeds in the waste ground.
        foreach (var c in g.GridA(420))
        {
            if (!g.Top(c) || g.Rnd.NextDouble() > 0.3 || m.At(m.EdgeDist, c.X, c.Z) < 160 || m.At(m.StartDist, c.X, c.Z) < 900
                || g.StairDist(c) < 200 || m.At(m.SpotDist, c.X, c.Z) < 160 || m.At(m.VentDist, c.X, c.Z) < 180) continue;
            for (int k = 0; k < 5; k++)
            {
                var p = c + new P(k % 3 * 80 - 80, k / 3 * 80);
                items.Add(new Item(g.Pick("crateblue", "crateorange", "cratered", "crateyellow"), p, 0, "props"));
            }
        }
        foreach (var p in g.GridA(140))
            if (g.Clear(p) && !g.Top(p) && g.Slope(p) < 14 && g.Rnd.NextDouble() < 0.06)
                items.Add(new Item($"cycas{g.Rnd.Next(1, 9)}", p, g.Heading(), "weeds"));
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
