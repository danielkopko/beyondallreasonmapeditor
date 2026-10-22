// The Bloom planet's texture (planets.py: a bioluminescent night world): teal moss fields glowing at their edges,
// blue-grey rock on the hall cliffs, cyan-lit riverbanks, pale glowing roads; stone stairs, causeways and gravel
// fords; dark metal spots and vents ringed with light. One texel a game unit, noise read in Team A's fifth.
using MapCore.Imaging;

/// <summary>Where the made things are, at 4 game units a cell: 0-1 masks, and distances to spots and vents.</summary>
sealed class Masks
{
    public const int Cell = 4;
    public readonly int N;
    public readonly float[] Road, Glow, Deck, Ford, Stair, Plaza, SpotDist, VentDist, StartDist;

    public Masks(Design d, double mapSize)
    {
        N = (int)(mapSize / Cell);
        Road = new float[N * N]; Glow = new float[N * N]; Deck = new float[N * N]; Ford = new float[N * N];
        Stair = new float[N * N]; Plaza = new float[N * N];
        SpotDist = Filled(999); VentDist = Filled(999); StartDist = Filled(9999);
        foreach (var path in d.Paths)
            foreach (var p in Resample(path, 10))
            {
                Disc(Road, p, 40, 18);
                Disc(Glow, p, 86, 40);
            }
        foreach (var b in d.Bridges) Rect(Deck, b.A, b.B, b.HalfWidth, 0, 6);
        foreach (var f in d.Fords) Capsule(Ford, f.A, f.B, f.HalfWidth - 10, 30);
        foreach (var r in Terrain.Ramps) Rect(Stair, r.Foot, r.Top, r.HalfWidth, 40, 8);
        foreach (var s in Terrain.Switchbacks)
            foreach (var p in Resample(s.Line, 6)) Disc(Stair, p, Terrain.PathHalf - 2, 6);
        foreach (var s in d.Starts) Disc(Plaza, s.At, s.N == 0 ? 420 : 280, 140);
        foreach (var s in d.Spots) Distance(SpotDist, s.At, 240);
        foreach (var v in d.Vents) Distance(VentDist, v.At, 240);
        foreach (var s in d.Starts) Distance(StartDist, s.At, 900);
    }

    float[] Filled(float v) { var a = new float[N * N]; Array.Fill(a, v); return a; }

    public static IEnumerable<P> Resample(IReadOnlyList<P> line, double step)
    {
        for (int i = 0; i + 1 < line.Count; i++)
        {
            var a = line[i]; var b = line[i + 1];
            int n = Math.Max(1, (int)Math.Ceiling((b - a).Length / step));
            for (int k = 0; k < n; k++) yield return a + (b - a) * (k / (double)n);
        }
        if (line.Count > 0) yield return line[^1];
    }

    void Each(P lo, P hi, Action<int, P> f)
    {
        int x0 = Math.Max(0, (int)(lo.X / Cell)), x1 = Math.Min(N - 1, (int)(hi.X / Cell));
        int z0 = Math.Max(0, (int)(lo.Z / Cell)), z1 = Math.Min(N - 1, (int)(hi.Z / Cell));
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
                f(z * N + x, new P(x * Cell + Cell / 2.0, z * Cell + Cell / 2.0));
    }

    public void Disc(float[] m, P c, double radius, double soft)
    {
        var e = new P(radius + soft, radius + soft);
        Each(c - e, c + e, (i, p) =>
        {
            float v = (float)(1 - Design.S(((p - c).Length - radius) / Math.Max(1, soft)));
            if (v > m[i]) m[i] = v;
        });
    }

    void Capsule(float[] m, P a, P b, double radius, double soft)
    {
        var e = new P(radius + soft, radius + soft);
        Each(new P(Math.Min(a.X, b.X), Math.Min(a.Z, b.Z)) - e, new P(Math.Max(a.X, b.X), Math.Max(a.Z, b.Z)) + e, (i, p) =>
        {
            float v = (float)(1 - Design.S((Design.ToSegment(p, a, b, out _) - radius) / soft));
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
            float v = (float)(1 - Design.S(outside / soft));
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

/// <summary>The ground's height, slope and shading, per heightmap vertex, read between vertices.</summary>
sealed class Surface
{
    public readonly int W;
    public readonly float[] H, Slope, Shade;

    public Surface(float[] heights, int w)
    {
        W = w;
        H = heights;
        Slope = new float[w * w];
        Shade = new float[w * w];
        // The template's sun (mapinfo lighting.sunDir), normalised.
        double lx = -0.64, ly = 0.66, lz = -0.57, ll = Math.Sqrt(lx * lx + ly * ly + lz * lz);
        lx /= ll; ly /= ll; lz /= ll;
        Parallel.For(0, w, z =>
        {
            for (int x = 0; x < w; x++)
            {
                double dx = (V(x + 1, z) - V(x - 1, z)) / 16.0, dz = (V(x, z + 1) - V(x, z - 1)) / 16.0;
                double len = Math.Sqrt(dx * dx + 1 + dz * dz);
                Slope[z * w + x] = (float)(Math.Atan(Math.Sqrt(dx * dx + dz * dz)) * 180 / Math.PI);
                double lambert = (-dx * lx + ly - dz * lz) / len;
                Shade[z * w + x] = (float)(0.82 + 0.3 * Math.Clamp(lambert, 0, 1));
            }
        });
    }

    float V(int x, int z) => H[Math.Clamp(z, 0, W - 1) * W + Math.Clamp(x, 0, W - 1)];

    public double At(float[] f, double x, double z)
    {
        double fx = x / 8, fz = z / 8;
        int ix = Math.Clamp((int)fx, 0, W - 2), iz = Math.Clamp((int)fz, 0, W - 2);
        double tx = fx - ix, tz = fz - iz;
        double a = f[iz * W + ix] + (f[iz * W + ix + 1] - f[iz * W + ix]) * tx;
        double b = f[(iz + 1) * W + ix] + (f[(iz + 1) * W + ix + 1] - f[(iz + 1) * W + ix]) * tx;
        return a + (b - a) * tz;
    }
}

static class Paint
{
    static readonly Noise Patch = new(101), Moss = new(202), Fine = new(303);
    const int Coarse = 16;

    readonly record struct C(double R, double G, double B)
    {
        public static C operator +(C a, C b) => new(a.R + b.R, a.G + b.G, a.B + b.B);
        public static C operator *(C a, double k) => new(a.R * k, a.G * k, a.B * k);
        public C Mix(C o, double t) => new(R + (o.R - R) * t, G + (o.G - G) * t, B + (o.B - B) * t);
    }

    static readonly C MossDark = new(34, 90, 88), MossA = new(44, 116, 108), MossB = new(82, 162, 150), GlowEdge = new(90, 220, 205);
    static readonly C Dirt = new(62, 96, 106), Rock = new(64, 74, 96), Sand = new(150, 200, 190), BankGlow = new(120, 240, 225);
    static readonly C Mud = new(44, 66, 74), Seabed = new(20, 40, 70), Deep = new(14, 30, 60), HallStone = new(96, 112, 130);
    static readonly C MountainRock = new(48, 56, 76), Road = new(165, 222, 210), RoadGlow = new(100, 235, 220), Stair = new(140, 158, 170);
    static readonly C DeckStone = new(128, 140, 156), Gravel = new(108, 152, 150), SpotCore = new(26, 36, 46), SpotGlow = new(60, 230, 215);
    static readonly C VentCore = new(20, 26, 36), VentGlow = new(80, 190, 255), Plaza = new(72, 150, 140);

    /// <summary>Coarse noise fields (16 units a cell), read in Team A's fifth outside the lake.</summary>
    sealed class Fields
    {
        public readonly int N;
        public readonly float[] Patches, MossDark;
        public Fields(Design d, double mapSize)
        {
            N = (int)(mapSize / Coarse) + 1;
            Patches = new float[N * N];
            MossDark = new float[N * N];
            Parallel.For(0, N, z =>
            {
                for (int x = 0; x < N; x++)
                {
                    var p = new P(x * Coarse, z * Coarse);
                    var f = (p - Design.Centre).Length > d.LakeRadius ? Design.Fold(p) : p;
                    Patches[z * N + x] = (float)Patch.Fbm(f.X / 900, f.Z / 900, 4);
                    MossDark[z * N + x] = (float)Moss.Fbm(f.X / 260, f.Z / 260, 3);
                }
            });
        }
        public double At(float[] m, double x, double z)
        {
            double fx = x / Coarse, fz = z / Coarse;
            int ix = Math.Clamp((int)fx, 0, N - 2), iz = Math.Clamp((int)fz, 0, N - 2);
            double tx = fx - ix, tz = fz - iz;
            double a = m[iz * N + ix] + (m[iz * N + ix + 1] - m[iz * N + ix]) * tx;
            double b = m[(iz + 1) * N + ix] + (m[(iz + 1) * N + ix + 1] - m[(iz + 1) * N + ix]) * tx;
            return a + (b - a) * tz;
        }
    }

    static Fields? fields;

    public static void Prepare(Design d, double mapSize) => fields = new Fields(d, mapSize);

    static double Grain(int x, int z)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return (h & 0xFFFF) / 32767.5 - 1;
    }

    /// <summary>One row of texels (z), one per game unit.</summary>
    public static void Row(Design d, Surface s, Masks m, int z, Span<Rgba> row)
    {
        var f = fields!;
        double wz = z + 0.5;
        for (int x = 0; x < row.Length; x++)
        {
            double wx = x + 0.5;
            var c = Colour(d, s, m, f, wx, wz, x, z);
            row[x] = new Rgba(B(c.R), B(c.G), B(c.B), 255);
        }
    }

    static byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    static C Colour(Design d, Surface s, Masks m, Fields f, double x, double z, int ix, int iz)
    {
        double h = s.At(s.H, x, z), slope = s.At(s.Slope, x, z), shade = s.At(s.Shade, x, z);
        double n1 = f.At(f.Patches, x, z), n2 = f.At(f.MossDark, x, z), grain = Grain(ix, iz);
        var p = new P(x, z);
        double r = (p - Design.Centre).Length;
        C c;
        if (h < 0)
        {
            double depth = -h;
            c = Mud.Mix(Seabed, Design.S(depth / 30)).Mix(Deep, Design.S((depth - 30) / 30));
            c = c * (1 + 0.08 * n2);
        }
        else
        {
            var fp = r > d.LakeRadius ? Design.Fold(p) : p;
            double fine = Fine.At(fp.X / 30, fp.Z / 30);
            c = MossA.Mix(MossB, 0.5 + 0.5 * n1);
            c = c.Mix(MossDark, Design.S(n2 * 1.6 - 0.1) * 0.6);
            c = c + GlowEdge * (0.22 * (1 - Design.S(Math.Abs(n2) / 0.07)));
            double wr = Design.S((slope - 30) / 12), wd = Design.S((slope - 12) / 12) * (1 - wr);
            var rock = Rock * (1 + 0.12 * Math.Sin(h / 7 + n1 * 3) + 0.06 * fine);
            c = c.Mix(Dirt, wd).Mix(rock, wr);
            if (h < 18)
            {
                double ws = 1 - Design.S((h - 4) / 14);
                c = c.Mix(Sand * (1 + 0.05 * fine), ws * (1 - wr));
                double band = 1 - Design.S(Math.Abs(h - 5) / 3);
                c = c + BankGlow * (0.35 * band);
            }
            if (h < 3) c = c.Mix(Mud, 1 - Design.S((h + 2) / 5));
            double wh = Design.S((h - 300) / 30) * (1 - wr);
            c = c.Mix(HallStone * (1 + 0.08 * fine), wh * (0.55 + 0.45 * Design.S(n2 + 0.3)));
            double over = r - d.PlayRadius;
            if (over > -12)
                c = c.Mix(MountainRock * (1 + 0.25 * Design.S((h - 400) / 200) + 0.1 * fine), Design.S((over + 12) / 40));
            double plaza = m.At(m.Plaza, x, z);
            c = c.Mix(Plaza, plaza * 0.35 * (1 - wr));
            double road = m.At(m.Road, x, z), glow = m.At(m.Glow, x, z);
            c = c.Mix(RoadGlow, glow * (1 - road) * 0.5 * (1 - wr));
            c = c.Mix(Road * (0.93 + 0.05 * grain + 0.04 * fine), road * (1 - wr));
            double stair = m.At(m.Stair, x, z);
            if (stair > 0)
            {
                // Steps: a darker riser every 6 units of height on the slopes (the flights and the ramp).
                double riser = slope > 6 && h % 6 < 1.1 ? 0.78 : 1;
                c = c.Mix(Stair * riser * (0.95 + 0.05 * grain), stair);
            }
        }
        double deck = m.At(m.Deck, x, z);
        if (deck > 0 && h >= Terrain.Deck - 1)
        {
            double joint = (Math.Abs(Math.Sin(x / 40 * Math.PI)) < 0.06 || Math.Abs(Math.Sin(z / 40 * Math.PI)) < 0.06) ? 0.8 : 1;
            c = c.Mix(DeckStone * joint * (0.95 + 0.05 * grain), deck);
        }
        double ford = m.At(m.Ford, x, z);
        if (ford > 0)
        {
            double pebble = Fine.At(x / 9, z / 9) > 0.35 ? 0.75 : 1;
            c = c.Mix(Gravel * pebble, ford * (h < 2 ? 0.9 : 0.4));
        }
        double sd = m.At(m.SpotDist, x, z);
        if (sd < 60)
        {
            c = c.Mix(SpotGlow, 0.85 * (1 - Design.S((sd - 30) / 28)) * Design.S((sd - 18) / 12));
            c = c.Mix(SpotCore * (1 + 0.15 * grain), 1 - Design.S((sd - 24) / 8));
        }
        double vd = m.At(m.VentDist, x, z);
        if (vd < 76)
        {
            c = c.Mix(VentGlow, 0.8 * (1 - Design.S((vd - 34) / 38)) * Design.S((vd - 20) / 14));
            c = c.Mix(VentCore, 1 - Design.S((vd - 28) / 8));
        }
        c = c * (shade * (1 + 0.045 * grain));
        return c;
    }
}
