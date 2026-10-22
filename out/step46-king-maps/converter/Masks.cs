// Where the made things are, at 4 game units a cell: 0-1 masks (roads and their edges, stairs, bridge decks, fords,
// bases' trodden ground, ramparts, plateau tops, cliffs), and distances (to spots, vents, starts, a plateau's edge).
sealed class Masks
{
    public const int Cell = 4;
    public readonly int N;
    public readonly float[] Road, Glow, Deck, Ford, Stair, Plaza, Wall, Top, Cliff, SpotDist, VentDist, StartDist, EdgeDist;

    public Masks(Design d)
    {
        N = (int)(d.MapSize / Cell);
        float[] New() => new float[N * N];
        float[] Filled(float v) { var a = New(); Array.Fill(a, v); return a; }
        Road = New(); Glow = New(); Deck = New(); Ford = New(); Stair = New(); Plaza = New(); Wall = New(); Top = New(); Cliff = New();
        SpotDist = Filled(999); VentDist = Filled(999); StartDist = Filled(9999); EdgeDist = Filled(999);
        foreach (var path in d.Paths)
            foreach (var p in Geo.Resample(path, 10))
            {
                Disc(Road, p, 40, 18);
                Disc(Glow, p, 86, 40);
            }
        foreach (var b in d.Bridges) Rect(Deck, b.A, b.B, b.HalfWidth, 0, 6);
        foreach (var f in d.Fords) Capsule(Ford, f.A, f.B, f.HalfWidth - 10, 30);
        // The design's ramps and tracks, until the planned ones replace them (Stairs).
        foreach (var r in d.Ramps) Rect(Stair, r.A, r.B, r.HalfWidth, 60, 8);
        foreach (var t in d.Tracks) Capsule(Stair, t.A, t.B, 60, 8);
        foreach (var s in d.Starts) Disc(Plaza, s.At, s.N == 0 ? 420 : 280, 140);
        foreach (var w in d.Walls) Capsule(Wall, w.A, w.B, w.HalfWidth + Terrain.RampartEdge, 4);
        foreach (var t in d.Towers) Disc(Wall, t.C, t.R + Terrain.RampartEdge, 4);
        foreach (var s in d.Spots) Distance(SpotDist, s.At, 260);
        foreach (var v in d.Vents) Distance(VentDist, v.At, 260);
        foreach (var s in d.Starts) Distance(StartDist, s.At, 900);
        foreach (var pl in d.Plateaus) Fill(Top, EdgeDist, pl);
        foreach (var c in d.Cliffs) Fill(Cliff, null, c);
    }

    /// <summary>The planned ramps and switchbacks, in place of the design's lines.</summary>
    public void Stairs(Terrain.Result t)
    {
        Array.Clear(Stair);
        foreach (var r in t.Ramps) Rect(Stair, r.Foot, r.Top, r.HalfWidth, 60, 8);
        foreach (var p in t.Paths)
            foreach (var q in Geo.Resample(p.Line, 6)) Disc(Stair, q, Terrain.PathHalf - 2, 6);
    }

    /// <summary>How much the planet's relief is held flat here (1 on roads, bases, spots, stairs, ramparts, plateaus).</summary>
    public double Flat(double x, double z) => Math.Max(Math.Max(At(Glow, x, z), At(Plaza, x, z)), Math.Max(Math.Max(At(Stair, x, z), At(Deck, x, z)),
        Math.Max(Math.Max(At(Wall, x, z), At(Top, x, z)), Math.Max(1 - Geo.S((At(SpotDist, x, z) - 70) / 120), 1 - Geo.S((At(VentDist, x, z) - 90) / 120)))));

    void Each(P lo, P hi, Action<int, P> f)
    {
        int x0 = Math.Max(0, (int)(lo.X / Cell)), x1 = Math.Min(N - 1, (int)(hi.X / Cell));
        int z0 = Math.Max(0, (int)(lo.Z / Cell)), z1 = Math.Min(N - 1, (int)(hi.Z / Cell));
        if (x1 < x0 || z1 < z0) return;
        Parallel.For(z0, z1 + 1, z =>
        {
            for (int x = x0; x <= x1; x++)
                f(z * N + x, new P(x * Cell + Cell / 2.0, z * Cell + Cell / 2.0));
        });
    }

    public void Disc(float[] m, P c, double radius, double soft)
    {
        var e = new P(radius + soft, radius + soft);
        Each(c - e, c + e, (i, p) =>
        {
            float v = (float)(1 - Geo.S(((p - c).Length - radius) / Math.Max(1, soft)));
            if (v > m[i]) m[i] = v;
        });
    }

    void Capsule(float[] m, P a, P b, double radius, double soft)
    {
        var e = new P(radius + soft, radius + soft);
        Each(new P(Math.Min(a.X, b.X), Math.Min(a.Z, b.Z)) - e, new P(Math.Max(a.X, b.X), Math.Max(a.Z, b.Z)) + e, (i, p) =>
        {
            float v = (float)(1 - Geo.S((Geo.ToSegment(p, a, b, out _) - radius) / Math.Max(1, soft)));
            if (v > m[i]) m[i] = v;
        });
    }

    void Rect(float[] m, P a, P b, double halfWidth, double extend, double soft)
    {
        double reach = (b - a).Length + halfWidth + extend + soft;
        var e = new P(reach, reach);
        var u = (b - a).Unit;
        double len = (b - a).Length;
        Each(a - e, a + e, (i, p) =>
        {
            var rel = p - a;
            double along = rel.Dot(u), across = Math.Abs(rel.Cross(u));
            double outside = Math.Max(Math.Max(-along - extend, along - len - extend), across - halfWidth);
            float v = (float)(1 - Geo.S(outside / soft));
            if (v > m[i]) m[i] = v;
        });
    }

    void Distance(float[] m, P c, double reach)
    {
        var e = new P(reach, reach);
        Each(c - e, c + e, (i, p) =>
        {
            float v = (float)(p - c).Length;
            if (v < m[i]) m[i] = v;
        });
    }

    void Fill(float[] m, float[]? edge, Shape s)
    {
        Each(new P(s.X0, s.Z0), new P(s.X1, s.Z1), (i, p) =>
        {
            if (!s.Contains(p)) return;
            m[i] = 1;
            if (edge != null) edge[i] = Math.Min(edge[i], (float)Geo.ToPolygonEdge(p, s.Pts));
        });
    }

    public float At(float[] m, double x, double z)
    {
        double fx = x / Cell - 0.5, fz = z / Cell - 0.5;
        int ix = Math.Clamp((int)Math.Floor(fx), 0, N - 2), iz = Math.Clamp((int)Math.Floor(fz), 0, N - 2);
        double tx = Math.Clamp(fx - ix, 0, 1), tz = Math.Clamp(fz - iz, 0, 1);
        double a = m[iz * N + ix] + (m[iz * N + ix + 1] - m[iz * N + ix]) * tx;
        double b = m[(iz + 1) * N + ix] + (m[(iz + 1) * N + ix + 1] - m[(iz + 1) * N + ix]) * tx;
        return (float)(a + (b - a) * tz);
    }
}
