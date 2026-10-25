// The ship's texture atlas, 2048 × 4096 (4 × 8 cells of 512), painted: texture 1 is colour (alpha 0 everywhere: BAR
// mixes a feature's team colour, black, in by alpha), texture 2 is glow, metalness and roughness (red, green, blue)
// with alpha 255, as BAR's model shader reads them for features marked cuspbr. Tiled cells repeat exactly once across
// their used part (periodic noise), so tiles meet without a seam; stretched cells (screens, lights, windows, the holo,
// engine bells) are pictures. The look: a working warship's interior, graphite and gunmetal plating, white craft hulls,
// orange and yellow ground gear, hazard stripes, cyan and amber light strips glowing, red and green navigation lights.
using System.Numerics;
using MapCore.Imaging;

static class Atlas
{
    public const string Texture1 = "meridian_1.tga", Texture2 = "meridian_2.tga";

    public static (RgbaImage Colour, RgbaImage Extra) Paint()
    {
        int w = Mesh.AtlasWidth, h = Mesh.AtlasHeight, cell = Mesh.Cell;
        var colour = new RgbaImage(w, h);
        var extra = new RgbaImage(w, h);
        double span = cell - 2 * Mesh.Inset;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
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

    public static (byte[] Colour, byte[] Extra) Files()
    {
        var (c, e) = Paint();
        return (TgaWriter.Write(c), TgaWriter.Write(e));
    }

    static byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
    static double Frac(double v) => v - Math.Floor(v);
    static double S(double t) => Geo.S(t);
    static Vector3 V(double r, double g, double b) => new((float)r, (float)g, (float)b);
    static Vector3 Mix(Vector3 a, Vector3 b, double t) => a + (b - a) * (float)Math.Clamp(t, 0, 1);

    // Extra: (glow, metalness, roughness).
    static readonly Vector3 Paint_ = V(0, 60, 130), Metal = V(0, 210, 100), Rough = V(0, 0, 230), Glass = V(0, 30, 24), Rubber_ = V(0, 0, 245);

    static (Vector3, Vector3) Cell(Region r, double s, double t) => r switch
    {
        Region.Plate => Panels(s, t, V(108, 112, 120), 1, 0.6),
        Region.PlateLight => Panels(s, t, V(204, 207, 210), 2, 0.25),
        Region.Graphite => Panels(s, t, V(54, 56, 62), 3, 0.4),
        Region.Orange => Panels(s, t, V(214, 104, 28), 4, 0.15),
        Region.Yellow => Panels(s, t, V(226, 178, 34), 5, 0.15),
        Region.Hazard => Hazard(s, t),
        Region.Window => Window(s, t, false),
        Region.WindowLit => Window(s, t, true),
        Region.Glow => Light(s, t, V(96, 236, 255)),
        Region.GlowWarm => Light(s, t, V(255, 186, 92)),
        Region.NavRed => Light(s, t, V(255, 60, 48)),
        Region.NavGreen => Light(s, t, V(60, 255, 110)),
        Region.Screen => Screen(s, t),
        Region.Holo => Holo(s, t),
        Region.Vent => Vent(s, t),
        Region.Grate => Grate(s, t),
        Region.Pipe => Pipe(s, t),
        Region.Copper => Copper(s, t),
        Region.Rubber => (V(30, 30, 32) * (float)(0.8 + 0.3 * Fractal(s, t, 16, 2, 81)), Rubber_),
        Region.Chrome => (Mix(V(150, 156, 164), V(232, 236, 240), 0.5 + 0.5 * Math.Sin(t * 9 + Fractal(s, t, 4, 2, 91) * 3)), V(0, 250, 40)),
        Region.Crate => Crate(s, t),
        Region.ContainerRed => Corrugated(s, t, V(150, 52, 40)),
        Region.ContainerBlue => Corrugated(s, t, V(40, 82, 140)),
        Region.Fabric => (V(78, 92, 112) * (float)(0.85 + 0.2 * Math.Sin(s * 40 + Fractal(s, t, 4, 2, 101) * 6)), V(0, 0, 250)),
        Region.Leaves => Leaves(s, t),
        Region.Soil => (Mix(V(54, 40, 30), V(84, 64, 46), Fractal(s, t, 16, 3, 111)), Rough),
        Region.EngineBell => EngineBell(s, t),
        Region.Burnt => Burnt(s, t),
        Region.Lattice => Lattice(s, t),
        Region.Energy => Energy(s, t),
        Region.Dark => (V(30, 31, 34) * (float)(0.85 + 0.25 * Fractal(s, t, 8, 3, 121)), V(0, 150, 150)),
        >= Region.Rock => (V(128, 128, 128), Rough),          // the moonrock's own atlas (Rocks.cs)
        _ => Marking(s, t),
    };

    // ---- Periodic noise: value noise on a lattice that wraps, so a tiled cell has no seam.

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

    public static double Fractal(double s, double t, int ps, int pt, int octaves, int seed)
    {
        double sum = 0, amp = 1, norm = 0;
        for (int o = 0; o < octaves; o++) { sum += amp * Lattice(s, t, ps << o, pt << o, seed + o * 31); norm += amp; amp *= 0.5; }
        return sum / norm;
    }

    public static double Fractal(double s, double t, int period, int octaves, int seed) => Fractal(s, t, period, period, octaves, seed);

    // ---- Plating: panels in a 2 × 2 grid with fine seams and rivets, scuffs worn to bare metal, grime streaks.

    static (Vector3, Vector3) Panels(double s, double t, Vector3 paint, int seed, double metal)
    {
        double ps = s * 2, pt = t * 2;
        double fs = Frac(ps), ft = Frac(pt);
        double edge = Math.Min(Math.Min(fs, 1 - fs), Math.Min(ft, 1 - ft));
        double seam = 1 - S((edge - 0.006) / 0.006);
        double tone = Hash((int)Math.Floor(ps), (int)Math.Floor(pt), seed) - 0.5;
        var c = paint * (float)(1 + 0.08 * tone);
        if (edge < 0.04 && edge > 0.018 && Math.Abs(Frac((fs < 0.04 || fs > 0.96 ? ft : fs) * 12) - 0.5) < 0.12) c *= 0.8f;   // rivets
        c = Mix(c, paint * 0.4f, seam * 0.85);
        double wear = Fractal(s, t, 16, 3, seed + 1);
        if (wear > 0.72) c = Mix(c, V(150, 154, 160), S((wear - 0.72) / 0.06) * 0.7);
        double streak = Fractal(s, Frac(t + Fractal(s, t, 4, 2, seed + 2) * 0.05), 48, 2, 2, seed + 3);
        c = Mix(c, V(40, 40, 44), S((streak - 0.6) / 0.2) * 0.3);
        var e = V(0, 255 * metal, 120 - 40 * metal);
        return (c, Mix(e, Metal, wear > 0.72 ? 0.7 : 0));
    }

    static (Vector3, Vector3) Hazard(double s, double t)
    {
        bool stripe = Frac((s + t) * 4) < 0.5;
        var c = stripe ? V(226, 176, 30) : V(28, 28, 30);
        c *= (float)(0.88 + 0.18 * Fractal(s, t, 16, 2, 131));
        return (c, Paint_);
    }

    static (Vector3, Vector3) Window(double s, double t, bool lit)
    {
        bool frame = s < 0.04 || s > 0.96 || t < 0.05 || t > 0.95 || Math.Abs(s - 0.5) < 0.012;
        if (frame) return (V(36, 38, 42), Paint_);
        if (lit) return (Mix(V(255, 214, 150), V(250, 236, 200), t), V(170, 30, 30));
        var c = Mix(V(18, 26, 38), V(84, 112, 140), (1 - t) * 0.6 + 0.2 * Fractal(s, t, 4, 2, 141));
        return (c, Glass);
    }

    static (Vector3, Vector3) Light(double s, double t, Vector3 colour)
    {
        double x = Math.Abs(s - 0.5) * 2, y = Math.Abs(t - 0.5) * 2;
        double core = 1 - S((Math.Max(x, y) - 0.55) / 0.4);
        return (Mix(colour * 0.45f, Mix(colour, V(255, 255, 255), 0.35), core), V(255 * (0.5 + 0.5 * core), 20, 40));
    }

    static (Vector3, Vector3) Screen(double s, double t)
    {
        bool frame = s < 0.05 || s > 0.95 || t < 0.06 || t > 0.94;
        if (frame) return (V(30, 32, 36), Paint_);
        var c = V(10, 24, 40);
        // Bars of a readout, a trace across the middle, a grid.
        int bar = (int)(s * 10);
        double barH = 0.2 + 0.6 * Hash(bar, 0, 151);
        if (t > 0.62 && t < 0.9 && (1 - (t - 0.62) / 0.28) < barH && Frac(s * 10) < 0.7) c = V(60, 220, 160);
        double trace = 0.35 + 0.12 * Math.Sin(s * 18) * Math.Sin(s * 5 + 1);
        if (Math.Abs(t - trace) < 0.015) c = V(120, 230, 255);
        if (Frac(s * 8) < 0.02 || Frac(t * 6) < 0.02) c = Mix(c, V(40, 90, 120), 0.6);
        return (c, V(200, 10, 30));
    }

    static (Vector3, Vector3) Holo(double s, double t)
    {
        double grid = (Frac(s * 12) < 0.06 || Frac(t * 12) < 0.06) ? 1 : 0;
        double scan = 0.5 + 0.5 * Math.Sin(t * 120);
        var c = Mix(V(30, 110, 140), V(150, 245, 255), 0.4 * grid + 0.3 * scan);
        return (c, V(255, 0, 20));
    }

    static (Vector3, Vector3) Vent(double s, double t)
    {
        bool slat = Frac(t * 10) < 0.55;
        var c = slat ? V(70, 74, 80) : V(14, 15, 17);
        if (s < 0.05 || s > 0.95) c = V(90, 94, 100);
        return (c, Metal);
    }

    static (Vector3, Vector3) Grate(double s, double t)
    {
        bool bar = Frac(s * 16) < 0.25 || Frac(t * 16) < 0.25;
        return (bar ? V(96, 100, 106) * (float)(0.9 + 0.2 * Fractal(s, t, 8, 2, 161)) : V(10, 10, 12), Metal);
    }

    static (Vector3, Vector3) Pipe(double s, double t)
    {
        var c = V(128, 132, 138) * (float)(0.85 + 0.2 * Fractal(s, t, 8, 2, 171));
        if (Frac(s * 4) < 0.05) c = V(200, 160, 40);          // banding
        return (c, V(0, 200, 90));
    }

    static (Vector3, Vector3) Copper(double s, double t)
    {
        double wind = Frac(t * 40);
        var c = Mix(V(150, 82, 40), V(222, 140, 80), 0.5 + 0.5 * Math.Sin(wind * Math.PI * 2));
        return (c * (float)(0.9 + 0.15 * Fractal(s, t, 8, 2, 181)), V(0, 240, 60));
    }

    static (Vector3, Vector3) Crate(double s, double t)
    {
        var c = V(96, 104, 70) * (float)(0.9 + 0.15 * Fractal(s, t, 8, 3, 191));
        bool frame = s < 0.07 || s > 0.93 || t < 0.07 || t > 0.93 || Math.Abs(s - t) < 0.04 || Math.Abs(s + t - 1) < 0.04;
        if (frame) c = V(68, 72, 52);
        if (s > 0.2 && s < 0.45 && t > 0.15 && t < 0.25) c = V(220, 220, 210);    // a stencil block
        return (c, Paint_);
    }

    static (Vector3, Vector3) Corrugated(double s, double t, Vector3 paint)
    {
        double ridge = 0.5 + 0.5 * Math.Sin(s * Math.PI * 20);
        var c = paint * (float)(0.72 + 0.35 * ridge);
        if (Fractal(s, t, 8, 3, 201) > 0.76) c = Mix(c, V(110, 64, 40), 0.5);
        return (c, V(0, 140, 140));
    }

    static (Vector3, Vector3) Leaves(double s, double t)
    {
        var c = V(36, 70, 34);
        for (int k = 0; k < 2; k++)
        {
            double ls = s * 8 + k * 0.5, lt = t * 8 + k * 0.37;
            double ds = Frac(ls) - 0.5, dt = Frac(lt) - 0.5;
            double rot = Hash((int)Math.Floor(ls), (int)Math.Floor(lt), 211 + k) * Math.PI;
            double u = ds * Math.Cos(rot) + dt * Math.Sin(rot), v = -ds * Math.Sin(rot) + dt * Math.Cos(rot);
            if (u * u / 0.16 + v * v / 0.03 < 1) c = Mix(V(60, 130, 50), V(120, 190, 80), Hash((int)Math.Floor(ls), (int)Math.Floor(lt), 213 + k));
        }
        return (c, Rough);
    }

    static (Vector3, Vector3) EngineBell(double s, double t)
    {
        // Along t: the throat (white-hot blue) to the lip (dull, heat-tinted); rings round it.
        var c = Mix(V(170, 220, 255), V(255, 140, 60), S(t / 0.4));
        c = Mix(c, V(60, 56, 60), S((t - 0.45) / 0.4));
        if (Frac(t * 10) < 0.05) c *= 0.7f;
        double glow = 1 - S(t / 0.55);
        return (c, V(255 * glow, 180, 90));
    }

    static (Vector3, Vector3) Burnt(double s, double t)
    {
        double n = Fractal(s, t, 8, 4, 221);
        var c = Mix(V(30, 28, 28), V(96, 84, 76), n);
        if (n > 0.62) c = Mix(c, V(110, 80, 120), 0.3);       // heat tint
        return (c, V(0, 120, 200));
    }

    static (Vector3, Vector3) Lattice(double s, double t)
    {
        bool chord = s < 0.1 || s > 0.9 || t < 0.1 || t > 0.9 || Math.Abs(s - t) < 0.06;
        return (chord ? V(118, 122, 128) : V(20, 21, 24), chord ? Metal : Rough);
    }

    static (Vector3, Vector3) Energy(double s, double t)
    {
        double swirl = Fractal(s, t, 4, 4, 231);
        var c = Mix(V(80, 200, 255), V(240, 252, 255), S((swirl - 0.4) / 0.3));
        return (c, V(255, 0, 10));
    }

    static (Vector3, Vector3) Marking(double s, double t)
    {
        var c = V(214, 216, 212) * (float)(0.9 + 0.12 * Fractal(s, t, 8, 2, 241));
        if (Frac((s - t) * 3) < 0.18) c = V(36, 38, 42);
        return (c, Paint_);
    }
}
