// The texture, one texel a game unit: each texel's height, slope, light and what is made there, handed to the
// planet's look; and the detail maps, grass and sky that go with it.
using System.Buffers.Binary;
using MapCore.Imaging;
using MapCore.Textures;

/// <summary>Height, slope and baked light per heightmap vertex, read between vertices.</summary>
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
        double lx = -0.64, ly = 0.66, lz = -0.57, ll = Math.Sqrt(lx * lx + ly * ly + lz * lz);
        lx /= ll; ly /= ll; lz /= ll;
        Parallel.For(0, w, z =>
        {
            for (int x = 0; x < w; x++)
            {
                double dx = (V(x + 1, z) - V(x - 1, z)) / 16.0, dz = (V(x, z + 1) - V(x, z - 1)) / 16.0;
                double len = Math.Sqrt(dx * dx + 1 + dz * dz);
                Slope[z * w + x] = (float)(Math.Atan(Math.Sqrt(dx * dx + dz * dz)) * 180 / Math.PI);
                Shade[z * w + x] = (float)(0.82 + 0.3 * Math.Clamp((-dx * lx + ly - dz * lz) / len, 0, 1));
            }
        });
    }

    float V(int x, int z) => H[Math.Clamp(z, 0, W - 1) * W + Math.Clamp(x, 0, W - 1)];
    public double At(float[] f, double x, double z) => Grid.Sample(f, W, 8, x, z);
}

sealed class Painter
{
    const int Coarse = 16;
    static readonly Noise F1 = new(101), F2 = new(102), F3 = new(103), Fine = new(104);
    readonly Design d; readonly Look look; readonly Sym sym; readonly Surface s; readonly Masks m;
    readonly int cn;
    readonly float[] n1, n2, n3, kingDist, grove;

    public Painter(Design d, Look look, Surface s, Masks m, List<P> groves)
    {
        this.d = d; this.look = look; this.s = s; this.m = m;
        sym = new Sym(d);
        cn = (int)(d.MapSize / Coarse) + 1;
        n1 = new float[cn * cn]; n2 = new float[cn * cn]; n3 = new float[cn * cn];
        kingDist = new float[cn * cn]; grove = new float[cn * cn];
        var kings = d.Starts.Where(x => x.N == 0).Select(x => x.At).ToList();
        Parallel.For(0, cn, z =>
        {
            for (int x = 0; x < cn; x++)
            {
                var p = new P(x * Coarse, z * Coarse);
                var fr = sym.At(p);
                n1[z * cn + x] = (float)Sym.Value(fr, q => F1.Fbm(q.X / 900, q.Z / 900, 4));
                n2[z * cn + x] = (float)Sym.Value(fr, q => F2.Fbm(q.X / 260, q.Z / 260, 3));
                n3[z * cn + x] = (float)Sym.Value(fr, q => F3.Fbm(q.X / 110, q.Z / 110, 3));
                kingDist[z * cn + x] = (float)kings.Min(k => (k - p).Length);
                grove[z * cn + x] = groves.Count == 0 ? 0 : (float)(1 - Geo.S((groves.Min(g => (g - p).Length) - 420) / 320));
            }
        });
    }

    double C16(float[] a, double x, double z) => Grid.Sample(a, cn, Coarse, x, z);

    static double Grain(int x, int z)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return (h & 0xFFFF) / 32767.5 - 1;
    }

    static byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    public void Row(int z, Span<Rgba> row)
    {
        double wz = z + 0.5;
        for (int x = 0; x < row.Length; x++)
        {
            double wx = x + 0.5;
            var p = new P(wx, wz);
            var fr = sym.At(p);
            var c = new Ctx
            {
                X = wx, Z = wz, H = s.At(s.H, wx, wz), Slope = s.At(s.Slope, wx, wz), Shade = s.At(s.Shade, wx, wz), Radius = d.Radius(p),
                Road = m.At(m.Road, wx, wz), Glow = m.At(m.Glow, wx, wz), Stair = m.At(m.Stair, wx, wz), Deck = m.At(m.Deck, wx, wz),
                Ford = m.At(m.Ford, wx, wz), Plaza = m.At(m.Plaza, wx, wz), Wall = m.At(m.Wall, wx, wz), Top = m.At(m.Top, wx, wz),
                Cliff = m.At(m.Cliff, wx, wz), SpotD = m.At(m.SpotDist, wx, wz), VentD = m.At(m.VentDist, wx, wz), EdgeD = m.At(m.EdgeDist, wx, wz),
                KingPlaza = 1 - Geo.S((C16(kingDist, wx, wz) - 300) / 120), Oasis = C16(grove, wx, wz),
                N1 = C16(n1, wx, wz), N2 = C16(n2, wx, wz), N3 = C16(n3, wx, wz), Grain = Grain(x, z), F = fr,
            };
            c.Fine = Sym.Value(fr, q => Fine.At(q.X / 30, q.Z / 30));
            // Mountains beyond a round map's circle: the planet's rock.
            var col = look.Colour(d, ref c);
            if (!d.Square && c.Radius > d.PlayRadius - 12)
            {
                var rock = C.Of(d.Planet.Rock) * (1.15 + 0.2 * Geo.S((c.H - 400) / 200) + 0.08 * c.Fine) * c.Shade;
                col = col.Mix(rock, Geo.S((c.Radius - d.PlayRadius + 12) / 40));
            }
            row[x] = new Rgba(B(col.R), B(col.G), B(col.B), 255);
        }
    }
}

static class Derive
{
    static readonly Noise N1 = new(91), N2 = new(92), Clump = new(93);
    static byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    static (double, double, double, double) Mix((double R, double G, double B, double A) a, (double R, double G, double B, double A) b, double t) =>
        (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);

    /// <summary>
    /// The splat distribution for All That Simmers' detail textures (rock, gravel, ground, grass), with its values:
    /// rock on cliffs and ramparts, gravel on middle slopes and what is made, the planet's own ground elsewhere
    /// (grass only on a green planet).
    /// </summary>
    public static void Splat(Design d, Look look, Surface s, Masks m, int size, string path)
    {
        var img = new RgbaImage(size, size);
        double unit = d.MapSize / size;
        Parallel.For(0, size, py =>
        {
            for (int px = 0; px < size; px++)
            {
                double x = (px + 0.5) * unit, z = (py + 0.5) * unit;
                double y = s.At(s.H, x, z), sl = s.At(s.Slope, x, z);
                double n1 = N1.Fbm(x / 300, z / 300, 3), n2 = N2.Fbm(x / 90 + 40, z / 90, 3);
                double rock = Math.Max(Geo.S((sl - 30 + 8 * n2) / 20), m.At(m.Wall, x, z)), dirt = Geo.S((sl - 12 + 6 * n2) / 14) * (1 - rock);
                double wet = 1 - Geo.S((y - 6) / 14);
                double rest = Math.Max(0, 1 - rock - dirt) * (1 - wet);
                rock *= 1 - wet; dirt *= 1 - wet;
                double r = rock * (110 + 70 * n1) + dirt * 72 + wet * (40 + 20 * n2);
                double g = rock * 69 + dirt * 111 + wet * (160 + 60 * n1);
                double b = rock * 22 + dirt * 16 + wet * 12;
                double a = (rock + dirt) * 33;
                if (look.Grass) a += rest * (118 + 86 * n1 + 30 * n2);
                else { g += rest * (120 + 40 * n1); b += rest * (70 + 40 * n2); }
                (r, g, b, a) = Mix((r, g, b, a), (20, 150, 70, 0), m.At(m.Road, x, z));
                (r, g, b, a) = Mix((r, g, b, a), (140, 40, 20, 0), m.At(m.Stair, x, z));
                (r, g, b, a) = Mix((r, g, b, a), (210, 30, 20, 0), y >= Terrain.Deck - 1 ? m.At(m.Deck, x, z) : 0);
                (r, g, b, a) = Mix((r, g, b, a), (40, 200, 50, 0), m.At(m.Ford, x, z));
                (r, g, b, a) = Mix((r, g, b, a), (225, 40, 10, 0), 1 - Geo.S((m.At(m.SpotDist, x, z) - 40) / 20));
                img.SetPixel(px, py, new Rgba(B(r), B(g), B(b), B(a)));
            }
        });
        img.FlipVertical(); // map .dds files are stored upside down (PLAN.md rule 7)
        DdsFile.Create(DdsFormat.Dxt5, img, true).Save(path);
    }

    public static void Specular(Design d, Surface s, Masks m, int size, string path)
    {
        var img = new RgbaImage(size, size);
        double unit = d.MapSize / size;
        Parallel.For(0, size, py =>
        {
            for (int px = 0; px < size; px++)
            {
                double x = (px + 0.5) * unit, z = (py + 0.5) * unit;
                double y = s.At(s.H, x, z), sl = s.At(s.Slope, x, z);
                double rock = Geo.S((sl - 30) / 20), dirt = Geo.S((sl - 12) / 14) * (1 - rock);
                double shore = y >= 0 ? 1 - Geo.S(y / 16) : 0, under = y < 0 ? 1 : 0;
                double land = 1 - shore - under;
                double a = land * (64 * (1 - rock - dirt) + 80 * dirt + 106 * rock) + shore * 136 + under * 109;
                double made = Math.Max(Math.Max(m.At(m.Road, x, z), m.At(m.Stair, x, z)), Math.Max(m.At(m.Top, x, z) * 0.6, y >= Terrain.Deck - 1 ? m.At(m.Deck, x, z) : 0));
                a = a * (1 - made) + 92 * made;
                img.SetPixel(px, py, new Rgba(B(shore * 30), B(shore * 26), B(shore * 20), B(a)));
            }
        });
        img.FlipVertical();
        DdsFile.Create(DdsFormat.Dxt5, img, true).Save(path);
    }

    /// <summary>The ground's slopes baked into a normal map (x red, z green, up blue), as Five Rivers' is.</summary>
    public static void Normals(Design d, Surface s, int size, string path)
    {
        var img = new RgbaImage(size, size);
        double unit = d.MapSize / size;
        Parallel.For(0, size, py =>
        {
            for (int px = 0; px < size; px++)
            {
                double x = (px + 0.5) * unit, z = (py + 0.5) * unit;
                double dx = (s.At(s.H, x + 8, z) - s.At(s.H, x - 8, z)) / 16, dz = (s.At(s.H, x, z + 8) - s.At(s.H, x, z - 8)) / 16;
                double len = Math.Sqrt(dx * dx + 1 + dz * dz);
                img.SetPixel(px, py, new Rgba(B(127.5 * (1 - dx / len)), B(127.5 * (1 - dz / len)), B(127.5 * (1 + 1 / len)), 255));
            }
        });
        img.FlipVertical();
        DdsFile.Create(DdsFormat.Dxt1, img, true).Save(path);
    }

    /// <summary>Grass (32-unit cells) on open level ground, kept off what is made, plateau tops and the bases.</summary>
    public static byte[] Grass(Design d, Surface s, Masks m, out double share)
    {
        int w = d.Squares / 4;
        var grass = new byte[w * w];
        var sym = new Sym(d);
        int count = 0;
        for (int gz = 0; gz < w; gz++)
            for (int gx = 0; gx < w; gx++)
            {
                double x = gx * 32 + 16, z = gz * 32 + 16;
                double y = s.At(s.H, x, z), sl = s.At(s.Slope, x, z);
                double clump = sym.Value(new P(x, z), q => Clump.Fbm(q.X / 260 + 70, q.Z / 260, 3));
                bool clear = m.At(m.Glow, x, z) > 0.2 || m.At(m.Stair, x, z) > 0.05 || m.At(m.Deck, x, z) > 0.05 || m.At(m.Plaza, x, z) > 0.3
                             || m.At(m.Wall, x, z) > 0.05 || m.At(m.Top, x, z) > 0.5 || m.At(m.SpotDist, x, z) < 90 || m.At(m.VentDist, x, z) < 110;
                if (y < 20 || sl > 12 || clear || clump < 0.05 || !d.InPlay(new P(x, z), 40)) continue;
                grass[gz * w + gx] = (byte)Math.Clamp(50 + 260 * (clump - 0.05), 40, 170);
                count++;
            }
        share = count * 100.0 / (w * w);
        return grass;
    }

    /// <summary>A sky cube (A8R8G8B8, six faces): the planet's sky overhead, its haze at the horizon. It depends only
    /// on height above the horizon, so it looks the same whichever way the engine turns the faces.</summary>
    public static void Sky(string path, (double R, double G, double B) top, (double R, double G, double B) horizon, int size = 512)
    {
        var face = new RgbaImage(size, size);
        var probe = DdsFile.Create(DdsFormat.Uncompressed, face, false).ToBytes();
        int headerBytes = probe.Length - size * size * 4;
        var bytes = new byte[headerBytes + 6 * size * size * 4];
        probe.AsSpan(0, headerBytes).CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(108), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(108)) | 0x8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(112), 0x200 | 0xFC00);
        for (int f = 0; f < 6; f++)
            Parallel.For(0, size, ty =>
            {
                for (int tx = 0; tx < size; tx++)
                {
                    double sc = (tx + 0.5) / size * 2 - 1, tc = (ty + 0.5) / size * 2 - 1;
                    (double X, double Y, double Z) dir = f switch
                    {
                        0 => (1, -tc, -sc), 1 => (-1, -tc, sc), 2 => (sc, 1, tc), 3 => (sc, -1, -tc), 4 => (sc, -tc, 1), _ => (-sc, -tc, -1),
                    };
                    double len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y + dir.Z * dir.Z);
                    double up = Math.Abs(dir.Y / len);
                    double t = Math.Pow(up, 0.55);
                    double r = horizon.R + (top.R - horizon.R) * t, g = horizon.G + (top.G - horizon.G) * t, b = horizon.B + (top.B - horizon.B) * t;
                    int o = headerBytes + (f * size * size + ty * size + tx) * 4;
                    bytes[o] = B(b * 255); bytes[o + 1] = B(g * 255); bytes[o + 2] = B(r * 255); bytes[o + 3] = 255;
                }
            });
        File.WriteAllBytes(path, bytes);
    }
}
