// The wreck's texture atlas, painted: texture 1 is colour (alpha 0 everywhere: BAR mixes a feature's team colour, black,
// in by alpha), texture 2 is glow, metalness and roughness (red, green, blue) with alpha 255, as BAR's model shader reads
// them for features marked cuspbr. Tiled cells repeat exactly once across their used part, so tiles meet without a seam;
// stretched cells (glow seams, windows, engine bells, the crest, vines, reeds) are pictures.
// The look: a giant war-transport of angular, layered armour, gunmetal with faded crimson and brass, cyan power
// seams still faintly alight, and centuries of jungle on it.
using System.Numerics;
using MapCore.Imaging;

static class Atlas
{
    public const string Texture1 = "starfall_wreck_1.tga", Texture2 = "starfall_wreck_2.tga";

    public static (RgbaImage Colour, RgbaImage Extra) Paint()
    {
        int size = Mesh.AtlasSize, cell = Mesh.Cell;
        var colour = new RgbaImage(size, size);
        var extra = new RgbaImage(size, size);
        double span = cell - 2 * Mesh.Inset;
        Parallel.For(0, size, y =>
        {
            for (int x = 0; x < size; x++)
            {
                var region = (Region)(y / cell * 4 + x / cell);
                double s = (x % cell - Mesh.Inset + 0.5) / span, t = (y % cell - Mesh.Inset + 0.5) / span;
                var (c, e) = Cell(region, Frac(s), Frac(t));
                colour.SetPixel(x, y, new Rgba(B(c.X), B(c.Y), B(c.Z), 0));
                extra.SetPixel(x, y, new Rgba(B(e.X), B(e.Y), B(e.Z), 255));
            }
        });
        return (colour, extra);
    }

    static byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
    static double Frac(double v) => v - Math.Floor(v);
    static double S(double t) => Geo.S(t);
    static Vector3 V(double r, double g, double b) => new((float)r, (float)g, (float)b);
    static Vector3 Mix(Vector3 a, Vector3 b, double t) => a + (b - a) * (float)Math.Clamp(t, 0, 1);

    // Extra: (glow, metalness, roughness).
    static readonly Vector3 Metal = V(0, 190, 120), Paint_ = V(0, 60, 150), Rough = V(0, 0, 235), Glass = V(0, 30, 26);

    static (Vector3, Vector3) Cell(Region r, double s, double t) => r switch
    {
        Region.Hull => Plating(s, t, moss: false, accent: false),
        Region.HullMoss => Plating(s, t, moss: true, accent: false),
        Region.Accent => Plating(s, t, moss: false, accent: true),
        Region.AccentMoss => Plating(s, t, moss: true, accent: true),
        Region.Trim => Trim(s, t),
        Region.Burnt => Burnt(s, t),
        Region.Vent => Vent(s, t),
        Region.Glow => Glow(s, t),
        Region.Window => Window(s, t),
        Region.Nozzle => Nozzle(s, t),
        Region.Girder => Girder(s, t),
        Region.Crest => Crest(s, t),
        Region.Vine => Vine(s, t),
        Region.Reed => Reed(s, t),
        Region.Earth => Earth(s, t),
        _ => Test(s, t),
    };

    // ---- Periodic noise: value noise on a lattice that wraps every `period` cells, so a tiled cell has no seam.

    static double Hash(int x, int y, int seed)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
        h = (h ^ (h >> 13)) * 1274126177;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (double)0xFFFFFF;
    }

    static double Lattice(double s, double t, int ps, int pt, int seed)
    {
        double x = s * ps, y = t * pt;
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        int Ws(int v) => ((v % ps) + ps) % ps;
        int Wt(int v) => ((v % pt) + pt) % pt;
        double a = Hash(Ws(x0), Wt(y0), seed), b = Hash(Ws(x0 + 1), Wt(y0), seed), c = Hash(Ws(x0), Wt(y0 + 1), seed), d = Hash(Ws(x0 + 1), Wt(y0 + 1), seed);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    /// <summary>Fractal noise in 0..1, wrapping every 1 in s and t, with ps and pt lattice cells across at its coarsest.</summary>
    static double Fractal(double s, double t, int ps, int pt, int octaves, int seed)
    {
        double sum = 0, amp = 1, norm = 0;
        for (int o = 0; o < octaves; o++) { sum += amp * Lattice(s, t, ps << o, pt << o, seed + o * 31); norm += amp; amp *= 0.5; }
        return sum / norm;
    }

    static double Fractal(double s, double t, int period, int octaves, int seed) => Fractal(s, t, period, period, octaves, seed);

    /// <summary>Noise drawn out along t (streaks running down a wall), wrapping.</summary>
    static double Streaks(double s, double t, int seed) => Fractal(s, Frac(t + Fractal(s, t, 4, 2, seed + 9) * 0.05), 64, 2, 2, seed);

    // ---- Plating: panels in a grid of 4 × 4 a tile (32 units), some joined, with grooves, bevels, rivets, a few
    // inset hatches; grime, streaks down the face, rust and chipped paint; moss in drifts over it on a mossy cell.

    static (Vector3, Vector3) Plating(double s, double t, bool moss, bool accent)
    {
        int seed = accent ? 70 : 10;
        int cols = accent ? 2 : 4, rows = accent ? 2 : 4;
        double pt = t * rows;
        int ri = (int)Math.Floor(pt);
        double ps = s * cols + (ri % 2) * 0.5;                    // panels staggered row by row
        int cf = (int)Math.Floor(ps), ci = cf % cols;
        double fs = ps - cf, ft = pt - ri;
        double edge = Math.Min(Math.Min(fs, 1 - fs), Math.Min(ft, 1 - ft));
        double groove = 1 - S((edge - 0.006 * cols) / (0.006 * cols));
        double bevelLight = S((0.05 - fs) / 0.03) + S((0.05 - ft) / 0.03), bevelDark = S((fs - 0.95) / 0.03) + S((ft - 0.95) / 0.03);
        double tone = Hash(ci, ri, seed + 1) - 0.5;

        var baseC = accent ? V(104, 36, 36) : V(96, 100, 106);
        var c = baseC * (float)(1 + 0.18 * tone);
        // A few panels carry an inset hatch with a darker frame; accent panels a chevron.
        if (!accent && Hash(ci, ri, seed + 2) < 0.18)
        {
            double hs = Math.Abs(fs - 0.5), ht = Math.Abs(ft - 0.5);
            double box = Math.Max(hs, ht);
            if (box < 0.32) c *= 0.9f;
            if (Math.Abs(box - 0.32) < 0.02) c *= 0.6f;
        }
        if (accent)
        {
            // A thin brass chevron inlay on some panels.
            double v = Math.Abs(fs - 0.5) * 2;
            double chev = Math.Abs(ft - 0.62 + v * 0.3);
            if (Hash(ci, ri, seed + 9) < 0.35 && chev < 0.025 && v < 0.85) c = Mix(c, V(160, 128, 66), 0.75);
        }
        // Rivets along panel edges.
        double rs = Frac(s * cols * 8), rt = Frac(t * rows * 8);
        bool rivetRow = edge < 0.06 && ((fs < 0.06 || fs > 0.94) ? Math.Abs(rt - 0.5) < 0.18 : Math.Abs(rs - 0.5) < 0.18);
        if (rivetRow && edge > 0.025) c *= 1.18f;
        c *= (float)(1 + 0.12 * bevelLight - 0.18 * bevelDark);
        c = Mix(c, V(46, 46, 50), groove * 0.7);

        // Weathering.
        double grime = Fractal(s, t, 4, 4, seed + 3);
        double streak = Streaks(s, t, seed + 4);
        c *= (float)(0.86 + 0.24 * grime);
        c = Mix(c, V(54, 50, 44), S((streak - 0.55) / 0.2) * 0.45);
        double rust = Fractal(s, t, 8, 4, seed + 5);
        double rusty = S((rust - 0.66) / 0.08);
        c = Mix(c, V(118, 70, 40) * (float)(0.8 + 0.4 * Fractal(s, t, 64, 2, seed + 6)), rusty * 0.85);
        double chip = Fractal(s, t, 32, 2, seed + 7);
        bool chipped = accent && chip > 0.72;
        if (chipped) c = Mix(c, V(150, 150, 150), 0.7);               // paint chipped to bare metal
        if (accent) c = Mix(c, V(104, 84, 80), 0.2 + Fractal(s, t, 2, 3, seed + 8) * 0.4); // sun-faded
        var e = Mix(accent && !chipped ? Paint_ : Metal, Rough, rusty * 0.8 + groove * 0.3);
        e.Z = (float)(e.Z * (0.85 + 0.3 * grime));

        if (moss)
        {
            // Moss drifts over the plates, thickest in grooves and hollows, with dirt and leaf litter under it.
            double m = Fractal(s, t, 4, 5, seed + 11) + 0.25 * groove;
            double cover = S((m - 0.42) / 0.12);
            double fuzz = Fractal(s, t, 128, 2, seed + 12);
            var mossC = Mix(V(46, 74, 30), V(92, 118, 44), Fractal(s, t, 16, 3, seed + 13)) * (float)(0.8 + 0.4 * fuzz);
            var dirt = V(70, 58, 42) * (float)(0.8 + 0.4 * fuzz);
            c = Mix(c, dirt, S((m - 0.32) / 0.1) * 0.6);
            c = Mix(c, mossC, cover);
            if (cover > 0.5 && Fractal(s, t, 64, 1, seed + 14) > 0.8) c = Mix(c, V(118, 96, 40), 0.6); // dead leaves
            e = Mix(e, Rough, Math.Max(cover, S((m - 0.32) / 0.1) * 0.6));
        }
        return (c, e);
    }

    static (Vector3, Vector3) Trim(double s, double t)
    {
        double ridge = Math.Abs(Frac(t * 8) - 0.5) * 2;
        var c = V(160, 128, 66) * (float)(0.75 + 0.35 * ridge);
        double patina = Fractal(s, t, 4, 4, 21);
        c = Mix(c, V(84, 128, 110), S((patina - 0.6) / 0.1) * 0.7);
        c *= (float)(0.85 + 0.25 * Fractal(s, t, 32, 2, 22));
        return (c, Mix(V(0, 220, 90), Rough, S((patina - 0.6) / 0.1) * 0.7));
    }

    static (Vector3, Vector3) Burnt(double s, double t)
    {
        double n = Fractal(s, t, 8, 4, 31);
        var c = V(34, 32, 36) * (float)(0.7 + 0.6 * n);
        double band = Math.Sin((Fractal(s, t, 4, 3, 32) * 6 + t * 3) * Math.PI);
        c = Mix(c, band > 0 ? V(70, 52, 90) : V(92, 72, 40), Math.Abs(band) * 0.35);  // heat tint
        double scratch = Fractal(s, t, 16, 64, 1, 33);
        if (scratch > 0.85) c = Mix(c, V(150, 146, 140), 0.5);
        return (c, V(0, 150, 160));
    }

    static (Vector3, Vector3) Vent(double s, double t)
    {
        double f = Frac(t * 10);
        var c = f < 0.12 ? V(150, 152, 156) : f < 0.6 ? V(70, 72, 78) * (float)(1 - (f - 0.12)) : V(16, 16, 18);
        c *= (float)(0.85 + 0.3 * Fractal(s, t, 8, 3, 41));
        return (c, f > 0.6 ? Rough : Metal);
    }

    static (Vector3, Vector3) Glow(double s, double t)
    {
        double d = Math.Abs(t - 0.5);
        double seg = Math.Abs(Frac(s * 6) - 0.5);
        double core = S((0.16 - d) / 0.08) * (seg < 0.46 ? 1 : 0.2);
        double halo = S((0.4 - d) / 0.3);
        var c = Mix(V(36, 40, 46), V(70, 140, 160), halo * 0.5);
        c = Mix(c, V(150, 240, 255), core);
        double flicker = 0.7 + 0.3 * Fractal(s, 0.5, 8, 2, 51);
        return (c, V(230 * core * flicker + 50 * halo, 120, 60));
    }

    static (Vector3, Vector3) Window(double s, double t)
    {
        bool mullion = Frac(s * 6) < 0.06 || t < 0.08 || t > 0.92;
        var c = mullion ? V(60, 62, 68) : Mix(V(8, 14, 22), V(46, 66, 84), (1 - t) * 0.6 + 0.15 * Fractal(s, t, 4, 2, 61));
        if (!mullion && Fractal(s, t, 8, 2, 62) > 0.78) c = Mix(c, V(120, 150, 170), 0.25); // a crazed pane
        return (c, mullion ? Metal : Glass);
    }

    static (Vector3, Vector3) Nozzle(double s, double t)
    {
        double r = Math.Sqrt((s - 0.5) * (s - 0.5) + (t - 0.5) * (t - 0.5)) * 2;
        double ring = Math.Abs(Frac(r * 7) - 0.5);
        var c = Mix(V(22, 22, 26), V(58, 54, 52), ring * 1.4);
        c = Mix(c, V(18, 16, 14), Fractal(s, t, 8, 3, 71) * 0.5);   // soot
        double ember = S((0.22 - r) / 0.18);
        c = Mix(c, V(90, 190, 220), ember * 0.6);
        return (c, V(150 * ember, 120, 140));
    }

    static (Vector3, Vector3) Girder(double s, double t)
    {
        double hs = Frac(s * 2) - 0.5, ht = Frac(t * 2) - 0.5;
        double hole = Math.Sqrt(hs * hs + ht * ht);
        var c = V(74, 76, 82);
        if (hole < 0.26) c = V(14, 14, 16);
        else if (hole < 0.31) c *= 1.3f;
        double edge = Math.Min(Frac(t * 2), 1 - Frac(t * 2));
        if (edge < 0.05) c *= 0.7f;
        double rust = Fractal(s, t, 8, 4, 81);
        c = Mix(c, V(116, 66, 38), S((rust - 0.6) / 0.1) * 0.8);
        c *= (float)(0.85 + 0.25 * Fractal(s, t, 32, 2, 82));
        return (c, hole < 0.26 ? Rough : Mix(Metal, Rough, S((rust - 0.6) / 0.1)));
    }

    /// <summary>The wreck's own crest (no real emblem): a gold blade through a downward chevron, swept wings, on crimson in a gold-rimmed hexagon.</summary>
    static (Vector3, Vector3) Crest(double s, double t)
    {
        double x = (s - 0.5) * 2, y = (t - 0.5) * 2;   // y down
        double hex = Math.Max(Math.Abs(x) * 0.866 + Math.Abs(y) * 0.5, Math.Abs(y));
        var field = V(110, 24, 28) * (float)(0.85 + 0.3 * Fractal(s, t, 8, 3, 91));
        var gold = V(196, 156, 64) * (float)(0.85 + 0.25 * Fractal(s, t, 16, 2, 92));
        var c = hex > 0.96 ? V(40, 40, 44) : hex > 0.86 ? gold : field;
        bool blade = Math.Abs(x) < 0.11 * (1 - (y + 0.75) / 1.6) && y > -0.75 && y < 0.85;
        double chevD = Math.Abs(y - (0.15 + 0.55 * (0.6 - Math.Abs(x))));
        bool chevron = Math.Abs(x) < 0.62 && Math.Abs(y - (0.55 - 0.9 * Math.Abs(x))) < 0.1 && y > -0.05;
        bool wing = Math.Abs(x) > 0.18 && Math.Abs(x) < 0.7 && Math.Abs(y - (-0.15 - 0.55 * (Math.Abs(x) - 0.18))) < 0.07;
        if (hex <= 0.86 && (blade || chevron || wing)) c = gold;
        _ = chevD;
        double glow = blade && Math.Abs(x) < 0.03 ? 1 : 0;
        if (glow > 0) c = V(160, 240, 255);
        double wear = Fractal(s, t, 16, 3, 93);
        c = Mix(c, V(70, 64, 60), S((wear - 0.7) / 0.1) * 0.6);
        return (c, glow > 0 ? V(200, 60, 60) : (blade || chevron || wing || (hex > 0.86 && hex <= 0.96)) ? V(0, 210, 90) : Paint_);
    }

    static (Vector3, Vector3) Vine(double s, double t)
    {
        var c = V(26, 54, 22) * (float)(0.8 + 0.3 * Fractal(s, t, 8, 2, 101));
        // Leaves: lit ellipses on a lattice, jittered.
        for (int k = 0; k < 2; k++)
        {
            double ls = s * 3 + k * 0.5, lt = t * 9 + k * 0.5 + Hash((int)Math.Floor(s * 3 + k * 0.5), 3, 102) * 0.5;
            double ds = Frac(ls) - 0.5, dt = Frac(lt) - 0.5;
            if (ds * ds / 0.16 + dt * dt / 0.09 < 1)
                c = Mix(V(56, 104, 36), V(96, 134, 50), Hash((int)Math.Floor(ls), (int)Math.Floor(lt), 103)) * (float)(1 - 0.5 * (ds * ds + dt * dt));
        }
        if (Math.Abs(s - 0.5) < 0.05) c = V(58, 46, 30);                  // the stem
        return (c, Rough);
    }

    static (Vector3, Vector3) Reed(double s, double t)
    {
        var c = Mix(V(70, 100, 40), V(150, 132, 76), S((t - 0.45) / 0.5));
        c *= (float)(0.8 + 0.35 * Math.Abs(Math.Sin(s * Math.PI * 3)));
        return (c, V(0, 0, 200));
    }

    static (Vector3, Vector3) Earth(double s, double t)
    {
        double n = Fractal(s, t, 4, 5, 111);
        var c = Mix(V(66, 50, 36), V(112, 86, 58), n);
        double clod = Fractal(s, t, 16, 2, 112);
        c *= (float)(0.8 + 0.35 * clod);
        double root = Math.Abs(Fractal(s, t, 4, 8, 3, 113) - 0.5);
        if (root < 0.02) c = V(58, 42, 30);
        if (Fractal(s, t, 8, 3, 114) > 0.68) c = Mix(c, V(60, 86, 34), 0.75);   // moss taking it
        return (c, Rough);
    }

    static (Vector3, Vector3) Test(double s, double t)
    {
        var c = V(230, 230, 230);
        bool shaft = Math.Abs(t - 0.5) < 0.08 && s > 0.15 && s < 0.65;
        bool head = s >= 0.65 && s < 0.9 && Math.Abs(t - 0.5) < (0.9 - s) * 1.2;
        if (shaft || head) c = V(30, 60, 200);
        if (s < 0.2 && t < 0.2) c = V(220, 30, 30);
        return (c, V(0, 0, 200));
    }

    /// <summary>The atlas's two textures as TGA files.</summary>
    public static (byte[] Colour, byte[] Extra) Files()
    {
        var (c, e) = Paint();
        return (TgaWriter.Write(c), TgaWriter.Write(e));
    }
}
