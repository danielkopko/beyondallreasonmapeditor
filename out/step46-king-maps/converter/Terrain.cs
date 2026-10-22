// A king map's ground, from its drawing's parts, the same in every team's part (noise and patterns read in Team
// A's part through Sym), in this order:
//   1. bands: the drawing's level ground, blurred into gentle slopes;
//   2. water (lakes, rivers, moats, acid pools) 60 deep under a water level of 0, in valleys falling to the shore,
//      and land standing out of it (isles: 8 at the shore, a low dome);
//   3. the planet's relief (dunes, hummocks, a worked-flat yard), kept off roads, bases, spots and stairs;
//   4. fords 10 deep; bridges, causeways with decks at 16;
//   5. cliffs: impassable rock, rough on top, its faces 40 across (steeper than 54 degrees);
//   6. plateaus, the kings' raised areas: level tops on the same cliffs;
//   7. ramparts (the drawing's walls) and towers: 90 above the ground beneath, flat-topped to build on, sheer;
//   8. ramps (12 degrees, through a rampart's gap), switchbacks (flights of 38 degrees with landings: bots climb
//      54, vehicles 27, hovercraft 33), and passes over cliffs (the same flights, up and down);
//   9. beyond a round map's play circle, mountains on a cliff.
static class Terrain
{
    public const double RiverBed = -60, FordBed = -10, Deck = 16, Shore = 8, IsleCap = 60;
    public const double CliffHalf = 20, RampartRise = 90, TowerRise = 110, RampartEdge = 20;
    public const double RampDegrees = 12, FlightDegrees = 38, MaxFlightDrop = 65, Landing = 48, PathHalf = 32;

    public sealed record Ramp(int Team, P Foot, P Top, P Dir, double HalfWidth, double FootHeight, double TopHeight, double Length);
    public sealed record Path(int Team, P[] Line, double[] H, bool Pass);

    public sealed class Result
    {
        public int N;
        public float[] H = [], Ground = [];
        public List<Ramp> Ramps = [];
        public List<Path> Paths = [];
    }

    static readonly Noise Edge = new(13), Rough = new(17), Peaks = new(5);

    public static Result Build(Design d, Look look, Masks m)
    {
        var sym = new Sym(d);
        int n = d.Squares + 1;
        var res = new Result { N = n };
        P V(int i) => new((i % n) * 8.0, (i / n) * 8.0);

        // 1. Bands.
        var b = new float[n * n];
        Array.Fill(b, (float)d.GroundH);
        foreach (var band in d.Bands.Where(x => x.Layer != "islands"))
        {
            var mask = Grid.Mask(n, 8, [band]);
            for (int i = 0; i < b.Length; i++) if (mask[i]) b[i] = (float)band.H;
        }
        Grid.Blur(b, n, 20, 3);

        // 2. Water and the land in it.
        var g = new float[n * n];
        var water = Grid.Mask(n, 8, d.Water);
        var land = Grid.Mask(n, 8, d.Land);
        for (int i = 0; i < water.Length; i++) water[i] &= !land[i];
        bool wet = water.Any(x => x);
        float[] toWater = wet ? Grid.Distance(water, n) : [], toDry = wet ? Grid.Distance(water.Select(x => !x).ToArray(), n) : [];
        double landTop = d.Land.Count > 0 ? Math.Min(IsleCap, d.Land.Max(l => l.H)) : IsleCap;
        Parallel.For(0, n * n, i =>
        {
            double h = b[i];
            if (wet)
            {
                double sd = water[i] ? -toDry[i] * 8 : toWater[i] * 8;
                h = Bank(sd, h, 700);
                if (land[i]) h = Math.Max(h, Shore + (landTop - Shore) * Geo.S((toWater[i] * 8 - 30) / 400));
            }
            g[i] = (float)h;
        });

        // 3. The planet's relief, on dry land away from what is made.
        Parallel.For(0, n * n, i =>
        {
            var p = V(i);
            double h = g[i];
            if (h <= Shore + 2) return;
            double keep = m.Flat(p.X, p.Z);
            h += look.Relief(d, sym, p, h) * (1 - keep) * Geo.S((h - Shore - 2) / 30);
            g[i] = (float)h;
        });

        // 4. Fords and bridges.
        foreach (var f in d.Fords)
            ForBox(n, f.A, f.B, f.HalfWidth + 110, i =>
            {
                double dist = Geo.ToSegment(V(i), f.A, f.B, out _);
                if (dist < f.HalfWidth + 100)
                    g[i] = (float)Math.Max(g[i], dist < f.HalfWidth ? FordBed : FordBed + (RiverBed - FordBed) * Geo.S((dist - f.HalfWidth) / 100));
            });
        foreach (var br in d.Bridges)
            ForBox(n, br.A, br.B, br.HalfWidth + 8, i =>
            {
                if (InRect(V(i), br.A, br.B, br.HalfWidth, 0, out _)) g[i] = (float)Math.Max(g[i], Deck);
            });
        res.Ground = (float[])g.Clone();
        var h0 = (float[])g.Clone();

        // 5-6. Cliffs and plateaus.
        foreach (var c in d.Cliffs) Raise(d, sym, n, h0, c, rough: true);
        foreach (var p in d.Plateaus) Raise(d, sym, n, h0, p, rough: false);

        // 7. Ramparts and towers: 90 above the highest ground under them.
        foreach (var w in d.Walls)
        {
            double top = Geo.Resample([w.A, w.B], 16).Max(q => Grid.Sample(h0, n, 8, q.X, q.Z)) + RampartRise;
            ForBox(n, w.A, w.B, w.HalfWidth + RampartEdge + 8, i =>
            {
                double dist = Geo.ToSegment(V(i), w.A, w.B, out _);
                h0[i] = (float)Math.Max(h0[i], Profile(dist, w.HalfWidth, top, h0[i]));
            });
        }
        foreach (var t in d.Towers)
        {
            double top = Enumerable.Range(0, 16).Select(k => t.C + new P(Math.Cos(k * Math.PI / 8), Math.Sin(k * Math.PI / 8)) * t.R)
                .Append(t.C).Max(q => Grid.Sample(h0, n, 8, q.X, q.Z)) + TowerRise;
            ForBox(n, t.C, t.C, t.R + RampartEdge + 8, i =>
            {
                double dist = (V(i) - t.C).Length;
                h0[i] = (float)Math.Max(h0[i], Profile(dist, t.R, top, h0[i]));
            });
        }

        // 8. Ramps, switchbacks and passes.
        double GroundAt(P p) => Grid.Sample(res.Ground, n, 8, p.X, p.Z);
        foreach (var r in d.Ramps)
        {
            var plateau = d.Plateaus.First(pl => pl.Contains(r.B));
            var dir = (r.B - r.A).Unit;
            var top = Crossing(plateau, r.A, r.B);
            double foot = GroundAt(r.A), len = 0;
            for (int k = 0; k < 4; k++)
            {
                len = (plateau.H - foot) / Math.Tan(RampDegrees * Math.PI / 180);
                foot = GroundAt(top - dir * len);
            }
            res.Ramps.Add(new Ramp(r.Team, top - dir * len, top, dir, r.HalfWidth, foot, plateau.H, len));
        }
        foreach (var t in d.Tracks)
        {
            var plateau = d.Plateaus.FirstOrDefault(pl => pl.Contains(t.A));
            var u = (t.B - t.A).Unit;
            if (plateau != null)
            {
                var edge = Crossing(plateau, t.A, t.B);
                // Start inside the plateau, past any rampart, so the path comes out on the plateau's top.
                double inset = 40;
                for (double s = 0; s < 400; s += 8)
                {
                    var q = edge - u * s;
                    bool walled = d.Walls.Any(w => Geo.ToSegment(q, w.A, w.B, out _) < w.HalfWidth + RampartEdge + PathHalf)
                                  || d.Towers.Any(tw => (q - tw.C).Length < tw.R + RampartEdge + PathHalf);
                    if (!walled) { inset = Math.Max(40, s + 24); break; }
                }
                double footH = GroundAt(edge + u * 160);
                res.Paths.Add(Flights(t.Team, edge - u * inset, u, plateau.H, footH, false));
            }
            else
            {
                // A pass over a cliff: the highest way under the ground that is nowhere steeper than the flights
                // (each point no higher than any other plus 38 degrees' worth of the distance between them), and at
                // most 60 above the ground either side, so it climbs from the cliff's foot and cuts a notch over it.
                var a = t.A - u * 40; var bb = t.B + u * 40;
                double ga = GroundAt(a), gb = GroundAt(bb), saddle = Math.Max(ga, gb) + 60, len = (bb - a).Length;
                double k8 = Math.Tan(FlightDegrees * Math.PI / 180) * 8;
                var line = new List<P>();
                var hs = new List<double>();
                for (double s = 0; s <= len + 0.1; s += 8)
                {
                    var q = a + u * s;
                    line.Add(q);
                    hs.Add(Math.Min(saddle, Grid.Sample(h0, n, 8, q.X, q.Z)));
                }
                for (int i = 1; i < hs.Count; i++) hs[i] = Math.Min(hs[i], hs[i - 1] + k8);
                for (int i = hs.Count - 2; i >= 0; i--) hs[i] = Math.Min(hs[i], hs[i + 1] + k8);
                res.Paths.Add(new Path(t.Team, [.. line], [.. hs], true));
            }
        }
        foreach (var r in res.Ramps)
            ForBox(n, r.Foot, r.Top + r.Dir * 140, r.HalfWidth + 8, i =>
            {
                var rel = V(i) - r.Foot;
                double along = rel.Dot(r.Dir), across = Math.Abs(rel.Cross(r.Dir));
                if (along >= 0 && along <= r.Length + 140 && across <= r.HalfWidth)
                    h0[i] = (float)(along >= r.Length ? r.TopHeight : r.FootHeight + (r.TopHeight - r.FootHeight) * along / r.Length);
            });
        foreach (var path in res.Paths)
        {
            var lo = new P(path.Line.Min(q => q.X), path.Line.Min(q => q.Z));
            var hi = new P(path.Line.Max(q => q.X), path.Line.Max(q => q.Z));
            ForBox(n, lo, hi, PathHalf + 8, i =>
            {
                var p = V(i);
                if (Geo.ToPolyline(p, path.Line, out double run) > PathHalf) return;
                double ph = PathHeight(path, run);
                if (!path.Pass || h0[i] > ph) h0[i] = (float)ph;
            });
        }

        // 9. Beyond a round map's play circle, mountains.
        if (!d.Square)
            Parallel.For(0, n * n, i =>
            {
                var p = V(i);
                double over = d.Radius(p) - d.PlayRadius;
                if (over <= -8) return;
                double peak = look.MountainTop + 90 * Peaks.Fbm(p.X / 900, p.Z / 900, 4);
                h0[i] = (float)Math.Max(h0[i], d.GroundH + (peak - d.GroundH) * Geo.S((over + 8) / 56));
            });
        res.H = h0;
        return res;
    }

    /// <summary>Ground beside water: under it 60 deep, a shelf up to the shore, then a valley up to the ground beyond.</summary>
    static double Bank(double outside, double ground, double valley)
    {
        if (outside < -120) return RiverBed;
        if (outside < 60) return RiverBed + (Shore - RiverBed) * Geo.S((outside + 120) / 180);
        return Shore + (ground - Shore) * Geo.S((outside - 60) / valley);
    }

    /// <summary>A rampart or tower across: its flat top, then a sheer face down to the ground.</summary>
    static double Profile(double dist, double halfWidth, double top, double ground)
    {
        double inner = halfWidth - RampartEdge;
        if (dist <= inner) return top;
        if (dist >= halfWidth + RampartEdge) return ground;
        return top + (ground - top) * (dist - inner) / (2 * RampartEdge);
    }

    /// <summary>Cliff-edged ground: rock with a rough top, or a plateau's level top.</summary>
    static void Raise(Design d, Sym sym, int n, float[] h, Shape s, bool rough)
    {
        var lo = new P(s.X0, s.Z0); var hi = new P(s.X1, s.Z1);
        ForBox(n, lo, hi, CliffHalf + 30, i =>
        {
            var p = new P((i % n) * 8.0, (i / n) * 8.0);
            double sd = s.Signed(p) + 12 * sym.Value(p, q => Edge.Fbm(q.X / 180, q.Z / 180, 3));
            if (sd >= CliffHalf) return;
            double ground = h[i];
            double top = rough ? Math.Max(s.H, ground + 60) + 18 * sym.Value(p, q => Rough.Fbm(q.X / 220, q.Z / 220, 3)) : s.H;
            double v = sd <= -CliffHalf ? top : top + (ground - top) * (sd + CliffHalf) / (2 * CliffHalf);
            h[i] = (float)Math.Max(h[i], v);
        });
    }

    /// <summary>Flights from a plateau's top at <paramref name="start"/> down to the ground: zigzag legs at 38 degrees with landings.</summary>
    static Path Flights(int team, P start, P u, double top, double foot, bool pass)
    {
        double rise = Math.Max(30, top - foot);
        int flights = (int)Math.Ceiling(rise / MaxFlightDrop);
        double drop = rise / flights, run = drop / Math.Tan(FlightDegrees * Math.PI / 180);
        var n = u.Perp;
        var line = new List<P> { start };
        var hs = new List<double> { top };
        var at = start;
        double h = top;
        for (int k = 0; k < flights; k++)
        {
            double side = k % 2 == 0 ? 1 : -1;
            at += (u * 0.7071 + n * (0.7071 * side)) * run;
            h -= drop;
            line.Add(at); hs.Add(h);
            if (k + 1 < flights)
            {
                at += u * Landing;
                line.Add(at); hs.Add(h);
            }
        }
        // Run out onto the ground.
        line.Add(at + u * 40); hs.Add(h);
        return new Path(team, [.. line], [.. hs], pass);
    }

    public static double PathHeight(Path s, double run)
    {
        double acc = 0;
        for (int i = 0; i + 1 < s.Line.Length; i++)
        {
            double len = (s.Line[i + 1] - s.Line[i]).Length;
            if (run <= acc + len || i + 2 == s.Line.Length)
                return s.H[i] + (s.H[i + 1] - s.H[i]) * Math.Clamp((run - acc) / Math.Max(1e-6, len), 0, 1);
            acc += len;
        }
        return s.H[^1];
    }

    /// <summary>Where the line from a to b crosses the outline's edge.</summary>
    static P Crossing(Shape s, P a, P b)
    {
        double lo = 0, hi = 1;
        bool aInside = s.Contains(a);
        for (int i = 0; i < 40; i++)
        {
            double mid = (lo + hi) / 2;
            if (s.Contains(a + (b - a) * mid) == aInside) lo = mid; else hi = mid;
        }
        return a + (b - a) * ((lo + hi) / 2);
    }

    public static bool InRect(P p, P a, P b, double halfWidth, double extend, out double t)
    {
        var ab = b - a;
        double len = ab.Length;
        var u = ab * (1 / len);
        var rel = p - a;
        double along = rel.Dot(u);
        t = along / len;
        return along >= -extend && along <= len + extend && Math.Abs(rel.Cross(u)) <= halfWidth;
    }

    /// <summary>Every vertex index in the box round a to b grown by <paramref name="grow"/>, in parallel rows.</summary>
    static void ForBox(int n, P a, P b, double grow, Action<int> f)
    {
        int x0 = Math.Max(0, (int)Math.Floor((Math.Min(a.X, b.X) - grow) / 8)), x1 = Math.Min(n - 1, (int)Math.Ceiling((Math.Max(a.X, b.X) + grow) / 8));
        int z0 = Math.Max(0, (int)Math.Floor((Math.Min(a.Z, b.Z) - grow) / 8)), z1 = Math.Min(n - 1, (int)Math.Ceiling((Math.Max(a.Z, b.Z) + grow) / 8));
        if (x1 < x0 || z1 < z0) return;
        Parallel.For(z0, z1 + 1, z => { for (int x = x0; x <= x1; x++) f(z * n + x); });
    }
}
