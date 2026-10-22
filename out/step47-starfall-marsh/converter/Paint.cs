// The ground's texture, one texel a game unit, and the detail maps, grass and sky. The jungle floor is dark greens
// and leaf litter in patches; the marsh is mud, algae at the water's edge and a dark peaty bed; the mound is raw red
// earth where it is steep, in slumped layers on its west face, with rills and roots, the jungle taking back the rest;
// trails are trodden mud; the bases cleared. Under the ship and round the wreckage the ground is rust-stained, and
// the ship's silhouette is painted in its plating colour, so the crash reads on the minimap and from afar.
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
    public double At(float[] f, double x, double z) => Sample(f, W, 8, x, z);

    public static double Sample(float[] f, int n, double spacing, double x, double z)
    {
        double fx = Math.Clamp(x / spacing, 0, n - 1.001), fz = Math.Clamp(z / spacing, 0, n - 1.001);
        int ix = (int)fx, iz = (int)fz;
        double tx = fx - ix, tz = fz - iz;
        return (f[iz * n + ix] * (1 - tx) + f[iz * n + ix + 1] * tx) * (1 - tz) + (f[(iz + 1) * n + ix] * (1 - tx) + f[(iz + 1) * n + ix + 1] * tx) * tz;
    }
}

/// <summary>Fields on a 16-unit grid the painter reads: trails, the ship's silhouette, rust round the wreckage.</summary>
sealed class Fields
{
    public const int Cell = 16;
    public readonly int N;
    public readonly float[] Trail, Ship, Rust, Stream;

    public Fields(List<Place.Item> ship, List<Place.Item> wreck, Mirror mir)
    {
        N = (int)(Layout.Size / Cell) + 1;
        Trail = new float[N * N]; Ship = new float[N * N]; Rust = new float[N * N]; Stream = new float[N * N];
        // The ship's silhouette: its models' triangles seen from above.
        var shipCells = new bool[N * N];
        foreach (var s in ship) Rasterise(s.Def!.Mesh, s.At, s.Heading, shipCells, 4);
        var wreckCells = new bool[N * N];
        foreach (var w in wreck)
        {
            Rasterise(w.Def!.Mesh, w.At, w.Heading, wreckCells, 2);
            var q = mir.Image(w.At);
            Rasterise(w.Def!.Mesh.Mirrored(), q, -w.Heading, wreckCells, 2);
        }
        var toShip = Distance(shipCells);
        var toWreck = Distance(wreckCells);
        Parallel.For(0, N, z =>
        {
            for (int x = 0; x < N; x++)
            {
                var p = new P(x * Cell, z * Cell);
                var q = mir.North(p);
                int i = z * N + x;
                Trail[i] = (float)Place.TrailDistance(q);
                Stream[i] = (float)Terrain.StreamDistance(q);
                Ship[i] = shipCells[i] ? 1 : 0;
                Rust[i] = (float)Math.Max(1 - Geo.S(toShip[i] * Cell / 260), 0.8 * (1 - Geo.S(toWreck[i] * Cell / 110)));
            }
        });
    }

    public double At(float[] f, double x, double z) => Surface.Sample(f, N, Cell, x, z);

    void Rasterise(Mesh m, P at, double heading, bool[] cells, float minY)
    {
        double a = heading * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        (double X, double Z) W(System.Numerics.Vector3 v) => (at.X + v.X * c + v.Z * s, at.Z - v.X * s + v.Z * c);
        for (int t = 0; t < m.T.Count; t += 3)
        {
            var v0 = m.V[m.T[t]].Position; var v1 = m.V[m.T[t + 1]].Position; var v2 = m.V[m.T[t + 2]].Position;
            if (Math.Max(v0.Y, Math.Max(v1.Y, v2.Y)) < minY) continue;
            var (ax, az) = W(v0); var (bx, bz) = W(v1); var (cx, cz) = W(v2);
            int x0 = Math.Max(0, (int)(Math.Min(ax, Math.Min(bx, cx)) / Cell)), x1 = Math.Min(N - 1, (int)(Math.Max(ax, Math.Max(bx, cx)) / Cell) + 1);
            int z0 = Math.Max(0, (int)(Math.Min(az, Math.Min(bz, cz)) / Cell)), z1 = Math.Min(N - 1, (int)(Math.Max(az, Math.Max(bz, cz)) / Cell) + 1);
            double den = (bz - cz) * (ax - cx) + (cx - bx) * (az - cz);
            if (Math.Abs(den) < 1e-6) continue;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    double px = x * Cell, pz = z * Cell;
                    double l1 = ((bz - cz) * (px - cx) + (cx - bx) * (pz - cz)) / den, l2 = ((cz - az) * (px - cx) + (ax - cx) * (pz - cz)) / den;
                    if (l1 >= -0.02 && l2 >= -0.02 && 1 - l1 - l2 >= -0.02) cells[z * N + x] = true;
                }
        }
    }

    float[] Distance(bool[] marked)
    {
        // Two-pass chamfer distance in cells (close enough for stains).
        var d = new float[N * N];
        for (int i = 0; i < d.Length; i++) d[i] = marked[i] ? 0 : 1e6f;
        for (int z = 0; z < N; z++)
            for (int x = 0; x < N; x++)
            {
                int i = z * N + x;
                if (x > 0) d[i] = Math.Min(d[i], d[i - 1] + 1);
                if (z > 0) d[i] = Math.Min(d[i], d[i - N] + 1);
                if (x > 0 && z > 0) d[i] = Math.Min(d[i], d[i - N - 1] + 1.414f);
                if (x < N - 1 && z > 0) d[i] = Math.Min(d[i], d[i - N + 1] + 1.414f);
            }
        for (int z = N - 1; z >= 0; z--)
            for (int x = N - 1; x >= 0; x--)
            {
                int i = z * N + x;
                if (x < N - 1) d[i] = Math.Min(d[i], d[i + 1] + 1);
                if (z < N - 1) d[i] = Math.Min(d[i], d[i + N] + 1);
                if (x < N - 1 && z < N - 1) d[i] = Math.Min(d[i], d[i + N + 1] + 1.414f);
                if (x > 0 && z < N - 1) d[i] = Math.Min(d[i], d[i + N - 1] + 1.414f);
            }
        return d;
    }
}

readonly record struct C(double R, double G, double B)
{
    public static C operator *(C a, double k) => new(a.R * k, a.G * k, a.B * k);
    public C Mix(C o, double t) { t = Math.Clamp(t, 0, 1); return new(R + (o.R - R) * t, G + (o.G - G) * t, B + (o.B - B) * t); }
}

sealed class Painter(Surface s, Terrain.Result t, Fields f, Mirror mir)
{
    static readonly Noise N1 = new(401), N2 = new(402), N3 = new(403), Fine = new(404), Rill = new(405);

    static readonly C Dark = new(34, 52, 26), Mid = new(56, 78, 34), Moss = new(78, 102, 40), Litter = new(92, 72, 44), Loam = new(70, 54, 38);
    static readonly C Mud = new(66, 56, 42), WetMud = new(50, 46, 36), Algae = new(82, 98, 48), Bed = new(62, 64, 42), DeepBed = new(42, 44, 32);
    static readonly C Laterite = new(144, 80, 46), Ochre = new(164, 112, 60), Clay = new(112, 62, 40), Roots = new(64, 44, 30);
    static readonly C Trodden = new(98, 82, 58), Clearing = new(88, 104, 48), Rust = new(104, 62, 38), Plating = new(84, 88, 94);

    static byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    public void Row(int z, Span<Rgba> row)
    {
        double wz = z + 0.5;
        for (int x = 0; x < row.Length; x++)
        {
            double wx = x + 0.5;
            var c = Colour(wx, wz, x, z);
            row[x] = new Rgba(B(c.R), B(c.G), B(c.B), 255);
        }
    }

    static double Grain(int x, int z)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return (h & 0xFFFF) / 32767.5 - 1;
    }

    C Colour(double x, double z, int ix, int iz)
    {
        var p = new P(x, z);
        double h = s.At(s.H, x, z), sl = s.At(s.Slope, x, z), shade = s.At(s.Shade, x, z);
        double n1 = mir.Value(p, a => N1.Fbm(a.X / 600, a.Z / 600, 3)), n2 = mir.Value(p, a => N2.Fbm(a.X / 140, a.Z / 140, 3));
        double n3 = mir.Value(p, a => N3.Fbm(a.X / 45, a.Z / 45, 2)), fine = mir.Value(p, a => Fine.At(a.X / 9, a.Z / 9));
        double grain = Grain(ix, iz);
        double mound = Surface.Sample(t.Mound, t.N, 8, x, z);

        C c;
        if (h < 0)
        {
            // The marsh bed: algae in the shallows, dark peat deeper.
            c = Algae.Mix(Bed, Geo.S(-h / 8)).Mix(DeepBed, Geo.S((-h - 14) / 10));
            c *= 0.9 + 0.12 * n2 + 0.04 * fine;
        }
        else
        {
            // The jungle floor: dark greens, moss, leaf litter in drifts.
            c = Dark.Mix(Mid, 0.5 + 0.6 * n1).Mix(Moss, Geo.S((n2 - 0.1) / 0.25) * 0.6);
            c = c.Mix(Litter, Geo.S((n3 + 0.05) / 0.2) * 0.45 * (1 - Geo.S((sl - 25) / 10)));
            c *= 0.9 + 0.1 * fine;
            // Banks and slopes: loam, then raw earth.
            c = c.Mix(Loam, Geo.S((sl - 14) / 10) * 0.7);
            double raw = Geo.S((sl - 26 + 6 * n2) / 10);
            var earth = Laterite.Mix(Ochre, Geo.S(n3 + 0.2)) * (0.85 + 0.25 * n2);
            if (mound > 0.05)
            {
                // The mound: slumped layers on its face, rills down it, roots, the jungle on the gentler ground.
                // Layers of the pushed-up ground show on the west face only, wavering.
                double west = Geo.S((Layout.MoundTop.X - x) / 400);
                double strata = Math.Sin(h / 13 + n2 * 1.2);
                if (west > 0.3) earth = strata > 0.55 ? earth.Mix(Ochre * (0.9 + 0.1 * n3), west) : strata < -0.65 ? earth.Mix(Clay * (0.9 + 0.1 * n3), west) : earth;
                double rill = Math.Abs(mir.Value(p, a => Rill.Fbm(a.X / 30, a.Z / 160, 2)));
                if (rill < 0.03 && sl > 18) earth *= 0.7;
                if (Math.Abs(n3 - 0.1) < 0.02 && sl > 20) earth = earth.Mix(Roots, 0.8);
                raw = Math.Max(raw, Geo.S((sl - 29 + 10 * n2) / 8) * mound);
                c = c.Mix(Moss * (0.9 + 0.2 * n2), Geo.S((n2 + 0.3) / 0.3) * mound * (1 - raw) * 0.6);
                // Creepers hanging down the raw face in curtains.
                double curtain = Geo.S((mir.Value(p, a => Rill.Fbm(a.X / 90 + 7, a.Z / 300, 2)) - 0.15) / 0.12);
                raw *= 1 - 0.75 * curtain * mound;
            }
            c = c.Mix(earth, raw);
            // The marsh's edge: mud, wet and dark at the water.
            c = c.Mix(Mud, (1 - Geo.S((h - 4) / 10)) * 0.85);
            c = c.Mix(WetMud, (1 - Geo.S(h / 3)) * 0.8);
            // Bases cleared; trails trodden.
            double toStart = Layout.Starts.Min(st => (st - mir.North(p)).Length);
            c = c.Mix(Clearing * (0.9 + 0.15 * n3), (1 - Geo.S((toStart - 380) / 260)) * 0.7);
            double trail = f.At(f.Trail, x, z);
            c = c.Mix(Trodden * (0.85 + 0.25 * n3), (1 - Geo.S((trail - 22 - 10 * n2) / 26)) * 0.75);
        }
        // Rust and scrap round the wreck; the ship's own silhouette.
        double rust = f.At(f.Rust, x, z);
        if (rust > 0)
        {
            c = c.Mix(Rust * (0.8 + 0.4 * n3), rust * Geo.S((n2 + 0.3) / 0.4) * 0.5);
            if (fine > 0.86 && rust > 0.3) c = c.Mix(Plating * 1.2, 0.6);
        }
        // (Not where the ground has buried it: there the dirt shows.)
        double hull = f.At(f.Ship, x, z) * (1 - Geo.S((h - 60) / 40));
        c = c.Mix(Plating * (0.9 + 0.1 * n3), hull * 0.9);
        // Spots and vents.
        foreach (var (at, _, _) in Layout.Spots)
        {
            double d = (mir.North(p) - at).Length;
            if (d < 62)
            {
                c = c.Mix(new C(150, 96, 60), 0.75 * (1 - Geo.S((d - 30) / 30)) * Geo.S((d - 18) / 12));
                c = c.Mix(new C(52, 46, 40) * (1 + 0.12 * grain), 1 - Geo.S((d - 24) / 8));
            }
        }
        foreach (var v in Layout.Vents)
        {
            double d = (mir.North(p) - v).Length;
            if (d < 90) c = c.Mix(new C(60, 190, 210), 0.5 * (1 - Geo.S((d - 34) / 40)) * Geo.S((d - 20) / 14)).Mix(new C(30, 30, 32), 1 - Geo.S((d - 28) / 8));
        }
        return c * (shade * (1 + 0.04 * grain));
    }
}

static class Derive
{
    static readonly Noise N1 = new(91), N2 = new(92), Clump = new(93);
    static byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    static (double, double, double, double) Mix((double R, double G, double B, double A) a, (double R, double G, double B, double A) b, double t) =>
        (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);

    /// <summary>
    /// The splat distribution for All That Simmers' detail textures (rock, gravel, ground, grass): rock on the mound's
    /// scarps and the steep banks, gravel and ground on slopes and mud, grass over the jungle floor.
    /// </summary>
    public static void Splat(Surface s, Fields f, int size, string path)
    {
        var img = new RgbaImage(size, size);
        double unit = Layout.Size / size;
        Parallel.For(0, size, py =>
        {
            for (int px = 0; px < size; px++)
            {
                double x = (px + 0.5) * unit, z = (py + 0.5) * unit;
                double y = s.At(s.H, x, z), sl = s.At(s.Slope, x, z);
                double n1 = N1.Fbm(x / 300, z / 300, 3), n2 = N2.Fbm(x / 90 + 40, z / 90, 3);
                double rock = Geo.S((sl - 28 + 8 * n2) / 18), dirt = Geo.S((sl - 12 + 6 * n2) / 14) * (1 - rock);
                double wet = 1 - Geo.S((y - 6) / 14);
                double rest = Math.Max(0, 1 - rock - dirt) * (1 - wet);
                rock *= 1 - wet; dirt *= 1 - wet;
                double r = rock * (110 + 70 * n1) + dirt * 72 + wet * (40 + 20 * n2);
                double g = rock * 69 + dirt * 111 + wet * (160 + 60 * n1);
                double b = rock * 22 + dirt * 16 + wet * 12;
                double a = (rock + dirt) * 33 + rest * (118 + 86 * n1 + 30 * n2);
                (r, g, b, a) = Mix((r, g, b, a), (20, 150, 70, 0), 1 - Geo.S((f.At(f.Trail, x, z) - 20) / 30));
                (r, g, b, a) = Mix((r, g, b, a), (120, 60, 20, 0), f.At(f.Rust, x, z) * 0.6);
                img.SetPixel(px, py, new Rgba(B(r), B(g), B(b), B(a)));
            }
        });
        img.FlipVertical(); // map .dds files are stored upside down (PLAN.md rule 7)
        DdsFile.Create(DdsFormat.Dxt5, img, true).Save(path);
    }

    public static void Specular(Surface s, Fields f, int size, string path)
    {
        var img = new RgbaImage(size, size);
        double unit = Layout.Size / size;
        Parallel.For(0, size, py =>
        {
            for (int px = 0; px < size; px++)
            {
                double x = (px + 0.5) * unit, z = (py + 0.5) * unit;
                double y = s.At(s.H, x, z), sl = s.At(s.Slope, x, z);
                double rock = Geo.S((sl - 30) / 20), dirt = Geo.S((sl - 12) / 14) * (1 - rock);
                double shore = y >= 0 ? 1 - Geo.S(y / 16) : 0, under = y < 0 ? 1 : 0;
                double land = 1 - shore - under;
                double a = land * (60 * (1 - rock - dirt) + 80 * dirt + 100 * rock) + shore * 150 + under * 110;
                img.SetPixel(px, py, new Rgba(B(shore * 34), B(shore * 30), B(shore * 22), B(a)));
            }
        });
        img.FlipVertical();
        DdsFile.Create(DdsFormat.Dxt5, img, true).Save(path);
    }

    /// <summary>The ground's slopes baked into a normal map (x red, z green, up blue).</summary>
    public static void Normals(Surface s, int size, string path)
    {
        var img = new RgbaImage(size, size);
        double unit = Layout.Size / size;
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

    /// <summary>Grass (32-unit cells): jungle grass in the clearings and along the marsh, off trails, spots and the wreck.</summary>
    public static byte[] Grass(Surface s, Fields f, Mirror mir, out double share)
    {
        int w = Layout.Squares / 4;
        var grass = new byte[w * w];
        int count = 0;
        for (int gz = 0; gz < w; gz++)
            for (int gx = 0; gx < w; gx++)
            {
                double x = gx * 32 + 16, z = gz * 32 + 16;
                var p = new P(x, z);
                double y = s.At(s.H, x, z), sl = s.At(s.Slope, x, z);
                double clump = mir.Value(p, q => Clump.Fbm(q.X / 260 + 70, q.Z / 260, 3));
                bool clear = f.At(f.Trail, x, z) < 40 || f.At(f.Ship, x, z) > 0.1 || f.At(f.Rust, x, z) > 0.5
                             || Layout.Spots.Any(sp => (sp.At - mir.North(p)).Length < 90) || Layout.Vents.Any(v => (v - mir.North(p)).Length < 110);
                if (y < 2 || sl > 14 || clear || clump < 0.0) continue;
                grass[gz * w + gx] = (byte)Math.Clamp(60 + 260 * clump, 50, 190);
                count++;
            }
        share = count * 100.0 / (w * w);
        return grass;
    }

    /// <summary>A sky cube (A8R8G8B8, six faces): the sky overhead, the haze at the horizon.</summary>
    public static void Sky(string path, (double R, double G, double B) top, (double R, double G, double B) horizon, int size = 512)
    {
        var face = new RgbaImage(size, size);
        var probe = DdsFile.Create(DdsFormat.Uncompressed, face, false).ToBytes();
        int headerBytes = probe.Length - size * size * 4;
        var bytes = new byte[headerBytes + 6 * size * size * 4];
        probe.AsSpan(0, headerBytes).CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(108), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(108)) | 0x8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(112), 0x200 | 0xFC00);
        for (int fc = 0; fc < 6; fc++)
            Parallel.For(0, size, ty =>
            {
                for (int tx = 0; tx < size; tx++)
                {
                    double sc = (tx + 0.5) / size * 2 - 1, tc = (ty + 0.5) / size * 2 - 1;
                    (double X, double Y, double Z) dir = fc switch
                    {
                        0 => (1, -tc, -sc), 1 => (-1, -tc, sc), 2 => (sc, 1, tc), 3 => (sc, -1, -tc), 4 => (sc, -tc, 1), _ => (-sc, -tc, -1),
                    };
                    double len = Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y + dir.Z * dir.Z);
                    double up = Math.Abs(dir.Y / len);
                    double tt = Math.Pow(up, 0.55);
                    double r = horizon.R + (top.R - horizon.R) * tt, g = horizon.G + (top.G - horizon.G) * tt, b = horizon.B + (top.B - horizon.B) * tt;
                    int o = headerBytes + (fc * size * size + ty * size + tx) * 4;
                    bytes[o] = B(b * 255); bytes[o + 1] = B(g * 255); bytes[o + 2] = B(r * 255); bytes[o + 3] = 255;
                }
            });
        File.WriteAllBytes(path, bytes);
    }
}
