// The detail maps for the new ground, as Simmering Ravine's were made for All That Simmers' detail textures (rock,
// gravel, ground, grass: splatDetailNormalTex1-4) and with its values: the splat distribution, the specular map and
// the normal map, written as DDS; the grass; and a night sky for the Bloom planet.
using System.Buffers.Binary;
using MapCore.Imaging;
using MapCore.Textures;

static class Derive
{
    static readonly Noise N1 = new(91), N2 = new(92), Clump = new(93);
    static byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    static (double, double, double, double) Mix((double R, double G, double B, double A) a, (double R, double G, double B, double A) b, double t) =>
        (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);

    public static void Splat(Surface s, Masks m, double mapSize, int size, string path)
    {
        var img = new RgbaImage(size, size);
        double unit = mapSize / size;
        Parallel.For(0, size, py =>
        {
            for (int px = 0; px < size; px++)
            {
                double x = (px + 0.5) * unit, z = (py + 0.5) * unit;
                double y = s.At(s.H, x, z), sl = s.At(s.Slope, x, z);
                double n1 = N1.Fbm(x / 300, z / 300, 3), n2 = N2.Fbm(x / 90 + 40, z / 90, 3);
                double rock = Design.S((sl - 30 + 8 * n2) / 20), dirt = Design.S((sl - 12 + 6 * n2) / 14) * (1 - rock);
                double wet = 1 - Design.S((y - 6) / 14);
                double grass = Math.Max(0, 1 - rock - dirt) * (1 - wet);
                rock *= 1 - wet; dirt *= 1 - wet;
                double r = rock * (110 + 70 * n1) + dirt * 72 + wet * (40 + 20 * n2);
                double g = rock * 69 + dirt * 111 + wet * (160 + 60 * n1);
                double b = rock * 22 + dirt * 16 + wet * 12;
                double a = grass * (118 + 86 * n1 + 30 * n2) + (rock + dirt) * 33;
                (r, g, b, a) = Mix((r, g, b, a), (20, 150, 70, 0), m.At(m.Road, x, z));
                (r, g, b, a) = Mix((r, g, b, a), (140, 40, 20, 0), m.At(m.Stair, x, z));
                (r, g, b, a) = Mix((r, g, b, a), (210, 30, 20, 0), y >= Terrain.Deck - 1 ? m.At(m.Deck, x, z) : 0);
                (r, g, b, a) = Mix((r, g, b, a), (40, 200, 50, 0), m.At(m.Ford, x, z));
                (r, g, b, a) = Mix((r, g, b, a), (225, 40, 10, 0), 1 - Design.S((m.At(m.SpotDist, x, z) - 40) / 20));
                img.SetPixel(px, py, new Rgba(B(r), B(g), B(b), B(a)));
            }
        });
        img.FlipVertical(); // map .dds files are stored upside down (PLAN.md rule 7), as MapCore's import writes them
        DdsFile.Create(DdsFormat.Dxt5, img, true).Save(path);
    }

    public static void Specular(Surface s, Masks m, double mapSize, int size, string path)
    {
        var img = new RgbaImage(size, size);
        double unit = mapSize / size;
        Parallel.For(0, size, py =>
        {
            for (int px = 0; px < size; px++)
            {
                double x = (px + 0.5) * unit, z = (py + 0.5) * unit;
                double y = s.At(s.H, x, z), sl = s.At(s.Slope, x, z);
                double rock = Design.S((sl - 30) / 20), dirt = Design.S((sl - 12) / 14) * (1 - rock);
                double shore = y >= 0 ? 1 - Design.S(y / 16) : 0, under = y < 0 ? 1 : 0;
                double land = 1 - shore - under;
                double a = land * (64 * (1 - rock - dirt) + 80 * dirt + 106 * rock) + shore * 136 + under * 109;
                // The glowing water's edge and the roads catch a little cyan sheen.
                double r = shore * 12 + under * 6, g = shore * 40 + under * 14, b = shore * 44 + under * 22;
                double made = Math.Max(m.At(m.Road, x, z), Math.Max(m.At(m.Stair, x, z), y >= Terrain.Deck - 1 ? m.At(m.Deck, x, z) : 0));
                a = a * (1 - made) + 92 * made;
                g += 20 * made; b += 24 * made;
                img.SetPixel(px, py, new Rgba(B(r), B(g), B(b), B(a)));
            }
        });
        img.FlipVertical(); // map .dds files are stored upside down (PLAN.md rule 7), as MapCore's import writes them
        DdsFile.Create(DdsFormat.Dxt5, img, true).Save(path);
    }

    /// <summary>The ground's slopes baked into a normal map (x in red, z in green, up in blue), as the template's is.</summary>
    public static void Normals(Surface s, double mapSize, int size, string path)
    {
        var img = new RgbaImage(size, size);
        double unit = mapSize / size;
        Parallel.For(0, size, py =>
        {
            for (int px = 0; px < size; px++)
            {
                double x = (px + 0.5) * unit, z = (py + 0.5) * unit;
                double dx = (s.At(s.H, x + 8, z) - s.At(s.H, x - 8, z)) / 16, dz = (s.At(s.H, x, z + 8) - s.At(s.H, x, z - 8)) / 16;
                double len = Math.Sqrt(dx * dx + 1 + dz * dz);
                double nx = -dx / len, ny = 1 / len, nz = -dz / len;
                img.SetPixel(px, py, new Rgba(B(127.5 * (1 + nx)), B(127.5 * (1 + nz)), B(127.5 * (1 + ny)), 255));
            }
        });
        img.FlipVertical();
        DdsFile.Create(DdsFormat.Dxt1, img, true).Save(path);
    }

    /// <summary>Grass (one value a 32-unit cell): clumps on open level ground, kept off what is made and the bases.</summary>
    public static byte[] Grass(Surface s, Masks m, int w, int h, out double share)
    {
        var grass = new byte[w * h];
        int count = 0;
        for (int gz = 0; gz < h; gz++)
            for (int gx = 0; gx < w; gx++)
            {
                double x = gx * 32 + 16, z = gz * 32 + 16;
                double y = s.At(s.H, x, z), sl = s.At(s.Slope, x, z);
                var f = Design.Fold(new P(x, z));
                double clump = Clump.Fbm(f.X / 260 + 70, f.Z / 260, 3);
                bool clear = m.At(m.Glow, x, z) > 0.2 || m.At(m.Stair, x, z) > 0.05 || m.At(m.Deck, x, z) > 0.05 || m.At(m.Plaza, x, z) > 0.3
                             || m.At(m.SpotDist, x, z) < 90 || m.At(m.VentDist, x, z) < 110;
                if (y < 20 || y > 300 || sl > 12 || clear || clump < 0.1) continue;
                grass[gz * w + gx] = (byte)Math.Clamp(50 + 260 * (clump - 0.1), 40, 170);
                count++;
            }
        share = count * 100.0 / (w * h);
        return grass;
    }

    /// <summary>
    /// A night sky cube (A8R8G8B8, six faces, no mips): deep violet overhead and below, a teal glow along the horizon,
    /// stars. It depends only on height above the horizon, so it looks the same whichever way the engine turns faces.
    /// </summary>
    public static void NightSky(string path, int size = 512)
    {
        var face = new RgbaImage(size, size);
        var probe = DdsFile.Create(DdsFormat.Uncompressed, face, false).ToBytes();
        int headerBytes = probe.Length - size * size * 4;
        var bytes = new byte[headerBytes + 6 * size * size * 4];
        probe.AsSpan(0, headerBytes).CopyTo(bytes);
        // dwCaps: complex; dwCaps2: a cube map with all six faces.
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
                    double x = dir.X / len, y = dir.Y / len, z = dir.Z / len;
                    double up = Math.Abs(y);
                    double horizon = Math.Exp(-up * 7);
                    double r = 0.07 + 0.05 * (1 - up) + 0.02 * horizon, g = 0.05 + 0.18 * horizon, b = 0.18 + 0.12 * (1 - up) + 0.15 * horizon;
                    // Stars: about one texel in 1500, brighter ones rarer, fading into the horizon's glow.
                    uint hsh = (uint)(f * 83492791) ^ (uint)(tx * 73856093) ^ (uint)(ty * 19349663);
                    hsh ^= hsh >> 13; hsh *= 0x5bd1e995; hsh ^= hsh >> 15;
                    double roll = (hsh & 0xFFFFF) / (double)0xFFFFF;
                    if (roll > 0.99933)
                    {
                        double k = Math.Pow((roll - 0.99933) / 0.00067, 2) * (1 - horizon) * 1.2;
                        r += 0.9 * k; g += 0.95 * k; b += k;
                    }
                    int o = headerBytes + (f * size * size + ty * size + tx) * 4;
                    bytes[o] = B(b * 255); bytes[o + 1] = B(g * 255); bytes[o + 2] = B(r * 255); bytes[o + 3] = 255;
                }
            });
        File.WriteAllBytes(path, bytes);
    }
}
