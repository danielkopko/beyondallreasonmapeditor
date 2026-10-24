// The mine's texture atlas, 2048 × 4096 (4 × 8 cells of 512), painted: texture 1 is colour (alpha 0 everywhere: BAR
// mixes a feature's team colour, black, in by alpha), texture 2 is glow, metalness and roughness (red, green, blue)
// with alpha 255, as BAR's model shader reads them for features marked cuspbr. Tiled cells repeat exactly once across
// their used part (periodic noise), so tiles meet without a seam; stretched cells (wheel faces, lamps, windows,
// lattice bays, plants) are pictures. The look: an outback mine left days ago — mining yellow and orange paint worn
// at the edges, grey steel on the bucket wheel excavator, everything filmed in red dust.
using System.Numerics;
using MapCore.Imaging;

static class Atlas
{
    public const string Texture1 = "opencut_1.tga", Texture2 = "opencut_2.tga";

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
    static readonly Vector3 Paint_ = V(0, 50, 140), Metal = V(0, 200, 110), Rough = V(0, 0, 235), Glass = V(0, 30, 28), Rubber_ = V(0, 0, 245);
    static readonly Vector3 Dust = V(176, 92, 52), DustLight = V(196, 120, 72);

    static (Vector3, Vector3) Cell(Region r, double s, double t) => r switch
    {
        Region.Yellow => Painted(s, t, V(222, 168, 28), false, 10),
        Region.YellowDust => Painted(s, t, V(222, 168, 28), true, 10),
        Region.Orange => Painted(s, t, V(218, 104, 26), false, 20),
        Region.OrangeDust => Painted(s, t, V(218, 104, 26), true, 20),
        Region.Steel => Painted(s, t, V(168, 168, 162), false, 30),
        Region.SteelDust => Painted(s, t, V(168, 168, 162), true, 30),
        Region.Lattice => Lattice(s, t),
        Region.Rust => Rusty(s, t),
        Region.Tyre => Tread(s, t),
        Region.Rubber => WheelFace(s, t),
        Region.Window => Window(s, t),
        Region.Grille => Grille(s, t),
        Region.Chrome => Chrome(s, t),
        Region.Hazard => Hazard(s, t),
        Region.Lamp => Lamp(s, t),
        Region.White => Painted(s, t, V(232, 230, 222), false, 40),
        Region.Track => TrackPlates(s, t),
        Region.Bucket => Worn(s, t),
        Region.Ore => Ore(s, t),
        Region.Flag => (V(240, 96, 20) * (float)(0.9 + 0.1 * Math.Sin(s * 18)), V(0, 0, 220)),
        Region.Bark => Bark(s, t),
        Region.Leaves => Leaves(s, t, V(112, 124, 84), V(160, 168, 120)),
        Region.Spinifex => Spinifex(s, t),
        Region.Saltbush => Leaves(s, t, V(120, 132, 112), V(170, 178, 158)),
        Region.RedRock => RedRock(s, t),
        Region.Mound => MoundEarth(s, t),
        Region.Corrugated => Corrugated(s, t),
        Region.Concrete => (V(170, 164, 154) * (float)(0.85 + 0.25 * Fractal(s, t, 8, 4, 501)), V(0, 0, 225)),
        Region.Blue => Painted(s, t, V(40, 90, 160), false, 50),
        Region.Earth => Earth(s, t),
        Region.Dark => (V(46, 44, 42) * (float)(0.8 + 0.3 * Fractal(s, t, 8, 3, 511)), V(0, 120, 180)),
        _ => Timber(s, t),
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

    static double Fractal(double s, double t, int ps, int pt, int octaves, int seed)
    {
        double sum = 0, amp = 1, norm = 0;
        for (int o = 0; o < octaves; o++) { sum += amp * Lattice(s, t, ps << o, pt << o, seed + o * 31); norm += amp; amp *= 0.5; }
        return sum / norm;
    }

    static double Fractal(double s, double t, int period, int octaves, int seed) => Fractal(s, t, period, period, octaves, seed);

    // ---- Paint: big panels with fine seams and bolts, worn to primer and steel at edges and scuffs, grime streaks
    // down the face, mud splash low; dusty cells filmed in red dust.

    static (Vector3, Vector3) Painted(double s, double t, Vector3 paint, bool dusty, int seed)
    {
        double ps = s * 2, pt = t * 2;
        double fs = Frac(ps), ft = Frac(pt);
        double edge = Math.Min(Math.Min(fs, 1 - fs), Math.Min(ft, 1 - ft));
        double seam = 1 - S((edge - 0.006) / 0.006);
        double tone = Hash((int)Math.Floor(ps), (int)Math.Floor(pt), seed) - 0.5;
        var c = paint * (float)(1 + 0.06 * tone);
        // Bolts along the seams.
        if (edge < 0.04 && edge > 0.018 && Math.Abs(Frac((fs < 0.04 || fs > 0.96 ? ft : fs) * 12) - 0.5) < 0.12) c *= 0.8f;
        c = Mix(c, paint * 0.45f, seam * 0.8);
        // Wear: scuffs to dark primer, and bright steel where it's worn through.
        double wear = Fractal(s, t, 16, 3, seed + 1);
        if (wear > 0.7) c = Mix(c, V(70, 66, 60), S((wear - 0.7) / 0.05) * 0.8);
        if (wear > 0.8) c = Mix(c, V(150, 150, 146), 0.6);
        // Grime streaks running down, and the general film.
        double streak = Fractal(s, Frac(t + Fractal(s, t, 4, 2, seed + 2) * 0.05), 48, 2, 2, seed + 3);
        c = Mix(c, V(90, 70, 52), S((streak - 0.55) / 0.2) * 0.35);
        double film = Fractal(s, t, 4, 4, seed + 4);
        c = Mix(c, Dust, 0.12 + 0.12 * film);
        var e = Mix(Paint_, V(0, 160, 100), wear > 0.8 ? 0.8 : 0);
        if (dusty)
        {
            double cover = S((Fractal(s, t, 4, 5, seed + 5) - 0.3) / 0.25);
            var dust = Mix(Dust, DustLight, Fractal(s, t, 64, 2, seed + 6)) * (float)(0.9 + 0.15 * Fractal(s, t, 128, 1, seed + 7));
            c = Mix(c, dust, 0.45 + 0.45 * cover);
            e = Mix(e, Rough, 0.6 + 0.4 * cover);
        }
        return (c, e);
    }

    /// <summary>A bay of truss seen side-on: chords top and bottom, posts at the ends, a diagonal; shadow behind.</summary>
    static (Vector3, Vector3) Lattice(double s, double t)
    {
        var back = V(52, 46, 42) * (float)(0.8 + 0.3 * Fractal(s, t, 4, 2, 521));
        double bar = 0.06;
        bool chord = t < bar || t > 1 - bar, post = s < bar * 0.8 || s > 1 - bar * 0.8;
        bool diag = Math.Abs(t - s) < bar * 0.75 || Math.Abs(t - (1 - s)) < bar * 0.5;
        if (!(chord || post || diag)) return (back, V(0, 40, 230));
        var steel = V(176, 174, 166) * (float)(0.85 + 0.2 * Fractal(s, t, 16, 3, 522));
        steel = Mix(steel, Dust, 0.2 + 0.2 * Fractal(s, t, 8, 2, 523));
        if (Fractal(s, t, 16, 3, 524) > 0.72) steel = Mix(steel, V(126, 72, 40), 0.6);
        return (steel, V(0, 150, 130));
    }

    static (Vector3, Vector3) Rusty(double s, double t)
    {
        double n = Fractal(s, t, 8, 5, 531);
        var c = Mix(V(110, 58, 30), V(160, 92, 50), n) * (float)(0.85 + 0.25 * Fractal(s, t, 64, 2, 532));
        return (c, V(0, 40, 225));
    }

    static (Vector3, Vector3) Tread(double s, double t)
    {
        // Chevron lugs across the tread (s round the tyre, t across it).
        double v = Math.Abs(t - 0.5);
        double lug = Frac(s * 2 + v * 0.8);
        bool groove = lug < 0.32 && v > 0.08;
        var c = groove ? V(14, 13, 13) : V(38, 36, 35) * (float)(0.9 + 0.15 * Fractal(s, t, 32, 2, 541));
        c = Mix(c, Dust, (groove ? 0.35 : 0.18) * Fractal(s, t, 8, 3, 542) + 0.06);
        return (c, Rubber_);
    }

    /// <summary>A wheel's face: black sidewall, the yellow rim, the dark hub and its lug nuts.</summary>
    static (Vector3, Vector3) WheelFace(double s, double t)
    {
        double x = s - 0.5, y = t - 0.5, r = Math.Sqrt(x * x + y * y) * 2;
        Vector3 c;
        Vector3 e = Rubber_;
        if (r > 0.6) c = V(36, 34, 33) * (float)(0.9 + 0.1 * Math.Sin(r * 60));
        else if (r > 0.56) c = V(70, 66, 60);
        else if (r > 0.3) { c = V(214, 160, 30) * (float)(0.9 + 0.12 * Fractal(s, t, 16, 2, 551)); e = Paint_; }
        else
        {
            c = V(62, 60, 58);
            double a = Math.Atan2(y, x);
            if (r > 0.18 && r < 0.26 && Math.Abs(Frac(a / (2 * Math.PI) * 10) - 0.5) < 0.18) c = V(150, 146, 136);
            e = Metal;
        }
        c = Mix(c, Dust, 0.25 * Fractal(s, t, 8, 3, 552) + 0.1);
        return (c, e);
    }

    static (Vector3, Vector3) Window(double s, double t)
    {
        bool frame = s < 0.05 || s > 0.95 || t < 0.06 || t > 0.94;
        if (frame) return (V(40, 40, 42), Paint_);
        var c = Mix(V(16, 24, 32), V(80, 104, 120), (1 - t) * 0.55 + 0.2 * Fractal(s, t, 4, 2, 561));
        c = Mix(c, Dust, S((t - 0.7) / 0.3) * 0.5);            // dust settled at the bottom of the pane
        return (c, Glass);
    }

    static (Vector3, Vector3) Grille(double s, double t)
    {
        double f = Frac(t * 14);
        var c = f < 0.5 ? V(24, 24, 24) : V(70, 68, 64) * (float)(1.1 - f * 0.4);
        c = Mix(c, Dust, 0.2 * Fractal(s, t, 8, 2, 571));
        return (c, f < 0.5 ? Rough : Metal);
    }

    static (Vector3, Vector3) Chrome(double s, double t)
    {
        var c = V(196, 198, 202) * (float)(0.75 + 0.35 * Math.Abs(Math.Sin(s * Math.PI * 2)));
        c = Mix(c, Dust, 0.15 * Fractal(s, t, 16, 2, 581));
        return (c, V(0, 255, 40));
    }

    static (Vector3, Vector3) Hazard(double s, double t)
    {
        bool yellow = Frac((s + t) * 3) < 0.5;
        var c = yellow ? V(226, 178, 30) : V(26, 26, 26);
        c = Mix(c, Dust, 0.15 + 0.15 * Fractal(s, t, 8, 3, 591));
        return (c, Paint_);
    }

    /// <summary>A lamp: a bright warm face in a dark housing; still alight, left on when the crews went.</summary>
    static (Vector3, Vector3) Lamp(double s, double t)
    {
        double x = s - 0.5, y = t - 0.5, r = Math.Sqrt(x * x + y * y) * 2;
        if (r > 0.86) return (V(50, 50, 50), Paint_);
        double hot = 1 - S((r - 0.2) / 0.6);
        return (Mix(V(250, 226, 170), V(255, 252, 236), hot), V(255 * (0.6 + 0.4 * hot), 20, 40));
    }

    static (Vector3, Vector3) TrackPlates(double s, double t)
    {
        double f = Frac(s * 6);
        var c = f < 0.18 ? V(58, 54, 50) : f < 0.3 ? V(96, 92, 86) : V(72, 68, 62);
        c *= (float)(0.9 + 0.15 * Fractal(s, t, 16, 2, 601));
        c = Mix(c, Dust, 0.3 + 0.35 * Fractal(s, t, 8, 3, 602));
        return (c, V(0, 170, 170));
    }

    static (Vector3, Vector3) Worn(double s, double t)
    {
        var c = V(84, 82, 78) * (float)(0.8 + 0.3 * Fractal(s, t, 8, 4, 611));
        double scratch = Fractal(s, t, 4, 64, 1, 612);
        if (scratch > 0.7) c = Mix(c, V(150, 148, 142), 0.5);
        c = Mix(c, Dust, 0.25 * Fractal(s, t, 4, 3, 613));
        return (c, Metal);
    }

    static (Vector3, Vector3) Ore(double s, double t)
    {
        // Broken rock: cells of lit and shadowed chunks.
        double best = 9, second = 9; int id = 0;
        for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int cx = (int)Math.Floor(s * 8) + i, cy = (int)Math.Floor(t * 8) + j;
                double px = (cx + Hash(((cx % 8) + 8) % 8, ((cy % 8) + 8) % 8, 621)) / 8, py = (cy + Hash(((cx % 8) + 8) % 8, ((cy % 8) + 8) % 8, 622)) / 8;
                double d = (s - px) * (s - px) + (t - py) * (t - py);
                if (d < best) { second = best; best = d; id = cx * 31 + cy; }
                else if (d < second) second = d;
            }
        double edge = Math.Sqrt(second) - Math.Sqrt(best);
        var rock = Mix(V(150, 84, 50), V(110, 104, 98), Hash(id, 7, 623)) * (float)(0.75 + 0.4 * Hash(id, 3, 624));
        if (edge < 0.01) rock *= 0.5f;
        return (rock, Rough);
    }

    static (Vector3, Vector3) Bark(double s, double t)
    {
        // A ghost gum's pale bark, with orange-brown patches where it has shed.
        var c = V(206, 200, 186) * (float)(0.85 + 0.2 * Fractal(s, t, 4, 16, 3, 631));
        double patch = Fractal(s, t, 4, 8, 3, 632);
        if (patch > 0.58) c = Mix(c, V(170, 110, 64), S((patch - 0.58) / 0.06));
        if (Fractal(s, t, 16, 2, 2, 633) > 0.75) c *= 0.75f;
        return (c, Rough);
    }

    static (Vector3, Vector3) Leaves(double s, double t, Vector3 dark, Vector3 light)
    {
        var c = dark * 0.78f;
        for (int k = 0; k < 2; k++)
        {
            double ls = s * 7 + k * 0.5, lt = t * 7 + k * 0.37;
            double ds = Frac(ls) - 0.5, dt = Frac(lt) - 0.5;
            double rot = Hash((int)Math.Floor(ls), (int)Math.Floor(lt), 641 + k) * Math.PI;
            double u = ds * Math.Cos(rot) + dt * Math.Sin(rot), v = -ds * Math.Sin(rot) + dt * Math.Cos(rot);
            if (u * u / 0.16 + v * v / 0.02 < 1) c = Mix(dark, light, Hash((int)Math.Floor(ls), (int)Math.Floor(lt), 642 + k));
        }
        return (c, Rough);
    }

    static (Vector3, Vector3) Spinifex(double s, double t)
    {
        double blade = Math.Abs(Math.Sin(s * Math.PI * 9 + Fractal(s, t, 4, 2, 651) * 3));
        var c = Mix(V(150, 140, 70), V(196, 176, 96), blade) * (float)(0.8 + 0.25 * Fractal(s, t, 32, 4, 2, 652));
        c = Mix(c, V(120, 120, 60), S((0.4 - t) / 0.4) * 0.4);     // greener at the base
        return (c, Rough);
    }

    static (Vector3, Vector3) RedRock(double s, double t)
    {
        double n = Fractal(s, t, 4, 5, 661);
        var c = Mix(V(120, 66, 46), V(158, 100, 72), n);                                      // ironstone, dull
        c = Mix(c, V(70, 46, 38), S((Fractal(s, t, 8, 3, 662) - 0.62) / 0.1) * 0.6);   // desert varnish
        if (Fractal(s, t, 16, 2, 663) > 0.8) c = Mix(c, V(190, 184, 120), 0.5);              // lichen
        return (c * (float)(0.9 + 0.15 * Fractal(s, t, 64, 1, 664)), Rough);
    }

    static (Vector3, Vector3) MoundEarth(double s, double t)
    {
        double knob = Fractal(s, t, 12, 3, 671);
        var c = Mix(V(150, 76, 40), V(196, 112, 62), knob) * (float)(0.85 + 0.25 * Fractal(s, t, 48, 2, 672));
        return (c, Rough);
    }

    static (Vector3, Vector3) Corrugated(double s, double t)
    {
        double ridge = 0.5 + 0.5 * Math.Sin(s * Math.PI * 24);
        var c = V(156, 160, 158) * (float)(0.75 + 0.35 * ridge);
        c = Mix(c, Dust, 0.2 + 0.25 * Fractal(s, t, 4, 3, 681));
        if (Fractal(s, t, 8, 3, 682) > 0.74) c = Mix(c, V(126, 70, 40), 0.6);
        return (c, V(0, 180, 120));
    }

    static (Vector3, Vector3) Earth(double s, double t)
    {
        double n = Fractal(s, t, 4, 5, 691);
        var c = Mix(V(136, 64, 34), V(184, 100, 56), n) * (float)(0.85 + 0.25 * Fractal(s, t, 32, 2, 692));
        return (c, Rough);
    }

    static (Vector3, Vector3) Timber(double s, double t)
    {
        // Sun-greyed timber (power poles, pallets, tank stands): grain running down the cell, dark checks along it.
        double grain = Fractal(s, t, 32, 2, 3, 701);
        var c = Mix(V(112, 102, 88), V(158, 148, 130), grain) * (float)(0.88 + 0.18 * Fractal(s, t, 4, 4, 702));
        if (Math.Abs(Fractal(s, t, 24, 1, 2, 703) - 0.5) < 0.025) c *= 0.55f;
        c = Mix(c, Dust, 0.15 * Fractal(s, t, 4, 2, 704));
        return (c, Rough);
    }
}
