// Marram Line's texture atlas, 2048 × 4096 (4 × 8 cells of 512), painted: texture 1 is colour (alpha 0 everywhere: BAR
// mixes a feature's team colour, black, in by alpha), texture 2 is glow, metalness and roughness (red, green, blue)
// with alpha 255, as BAR's model shader reads them for features marked cuspbr. Tiled cells repeat exactly once across
// their used part (periodic noise), so tiles meet without a seam; stretched cells (the gun slit, lamps, windows) are
// pictures. The look: an old war's leavings on a windy coast, salt-weathered concrete with formwork lines and lichen,
// rusted steel, burnt-out olive drab, burlap sandbags, grey timber; the farm and village in cream stone under slate and
// tile; oak, pine, marram and reeds. No national markings anywhere. The noise and paint helpers are Red Dust Open Cut's.
using System.Numerics;
using MapCore.Imaging;

static class Atlas
{
    public const string Texture1 = "marram_1.tga", Texture2 = "marram_2.tga";

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
    static readonly Vector3 PaintE = V(0, 40, 160), Metal = V(0, 190, 120), Rough = V(0, 0, 235), Glass = V(0, 30, 28);
    static readonly Vector3 Salt = V(196, 192, 176), Grime = V(70, 64, 54), RustC = V(122, 64, 34), Lichen = V(170, 172, 112);

    static (Vector3, Vector3) Cell(Region r, double s, double t) => r switch
    {
        Region.Concrete => Concrete(s, t, V(170, 168, 158), 0.15, 11),
        Region.ConcreteDark => Concrete(s, t, V(128, 124, 116), 0.45, 12),
        Region.GunSlit => GunSlit(s, t),
        Region.Rust => Rusty(s, t),
        Region.Drab => Painted(s, t, V(84, 90, 60), 0.5, 21),
        Region.Grey => Painted(s, t, V(108, 118, 114), 0.3, 22),
        Region.Burnt => Burnt(s, t),
        Region.Track => TrackPlates(s, t),
        Region.Sandbag => Sandbags(s, t),
        Region.Timber => Timber(s, t, V(118, 110, 96), V(160, 152, 136), 31),
        Region.TimberDark => Timber(s, t, V(54, 44, 36), V(86, 72, 58), 32),
        Region.Wire => WireSteel(s, t),
        Region.Stone => Masonry(s, t, V(190, 178, 150), false),
        Region.StoneDark => Masonry(s, t, V(128, 118, 100), true),
        Region.RoofTile => Tiles(s, t),
        Region.Plaster => Plaster(s, t),
        Region.Bark => Bark(s, t),
        Region.Leaves => Leaves(s, t, V(52, 78, 36), V(96, 124, 58), 41),
        Region.Pine => Leaves(s, t, V(30, 52, 38), V(58, 84, 56), 42),
        Region.Marram => Grass(s, t, V(150, 156, 98), V(206, 196, 128), 43),
        Region.Reeds => Grass(s, t, V(110, 116, 66), V(170, 150, 92), 44),
        Region.Hay => Grass(s, t, V(176, 150, 76), V(222, 196, 112), 45),
        Region.Blossom => Orchard(s, t),
        Region.Earth => Earth(s, t),
        Region.White => Painted(s, t, V(226, 224, 216), 0.15, 51),
        Region.Red => Painted(s, t, V(170, 42, 36), 0.2, 52),
        Region.Lamp => Lamp(s, t),
        Region.Window => Window(s, t),
        Region.Granite => Granite(s, t),
        Region.Shingle => Shingle(s, t),
        Region.Dark => (V(30, 28, 26) * (float)(0.8 + 0.3 * Fractal(s, t, 8, 3, 511)), Rough),
        _ => SandCell(s, t),
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

    // ---- Concrete: cast in board formwork (horizontal lines, the boards' grain), salt-weathered, rust streaks from
    // the steel inside, lichen low down; the dark one scorched and stained.
    static (Vector3, Vector3) Concrete(double s, double t, Vector3 baseC, double soot, int seed)
    {
        double board = Frac(t * 6);
        var c = baseC * (float)(0.88 + 0.16 * Fractal(s, t, 8, 4, seed));
        c *= (float)(0.94 + 0.08 * Fractal(s * 0.2, t, 2, 48, 2, seed + 1));               // the boards' grain
        if (board < 0.03) c *= 0.82f;                                                      // the lines between boards
        double streak = Fractal(s, Frac(t + Fractal(s, t, 4, 2, seed + 2) * 0.04), 40, 2, 2, seed + 3);
        c = Mix(c, RustC, S((streak - 0.66) / 0.12) * 0.35);
        c = Mix(c, Salt, S((Fractal(s, t, 4, 3, seed + 4) - 0.55) / 0.2) * 0.3);
        c = Mix(c, Lichen, S((Fractal(s, t, 16, 3, seed + 5) - 0.68) / 0.06) * 0.5 * S((t - 0.5) / 0.4));
        c = Mix(c, V(40, 36, 32), soot * Fractal(s, t, 4, 4, seed + 6));
        if (Fractal(s, t, 32, 2, seed + 7) > 0.8) c *= 0.7f;                                // pits and spalls
        return (c, Rough);
    }

    /// <summary>A bunker's front: concrete with a stepped embrasure across its middle, dark inside.</summary>
    static (Vector3, Vector3) GunSlit(double s, double t)
    {
        var (c, e) = Concrete(s, t, V(160, 158, 148), 0.25, 13);
        double x = Math.Abs(s - 0.5), y = Math.Abs(t - 0.45);
        if (x < 0.36 && y < 0.11) { c = Mix(c * 0.6f, V(60, 58, 54), S((0.36 - x) / 0.04)); }    // the reveal
        if (x < 0.3 && y < 0.06) c = V(10, 10, 10);                                              // the slit
        if (t > 0.56 && t < 0.6 && x < 0.38) c *= 0.7f;                                          // the sill's shadow
        return (c, e);
    }

    static (Vector3, Vector3) Painted(double s, double t, Vector3 paint, double worn, int seed)
    {
        double ps = s * 2, pt = t * 2;
        double fs = Frac(ps), ft = Frac(pt);
        double edge = Math.Min(Math.Min(fs, 1 - fs), Math.Min(ft, 1 - ft));
        double seam = 1 - S((edge - 0.006) / 0.006);
        double tone = Hash((int)Math.Floor(ps), (int)Math.Floor(pt), seed) - 0.5;
        var c = paint * (float)(1 + 0.06 * tone);
        if (edge < 0.04 && edge > 0.018 && Math.Abs(Frac((fs < 0.04 || fs > 0.96 ? ft : fs) * 12) - 0.5) < 0.12) c *= 0.8f;   // rivets
        c = Mix(c, paint * 0.45f, seam * 0.8);
        // Salt air: rust blooming through, streaks running down, paint chipped to primer.
        double rust = Fractal(s, t, 16, 3, seed + 1);
        c = Mix(c, RustC * (float)(0.8 + 0.3 * Fractal(s, t, 64, 1, seed + 2)), S((rust - (0.75 - 0.25 * worn)) / 0.08) * 0.85);
        double streak = Fractal(s, Frac(t + Fractal(s, t, 4, 2, seed + 3) * 0.05), 48, 2, 2, seed + 4);
        c = Mix(c, RustC * 0.8f, S((streak - 0.6) / 0.15) * 0.4 * worn + 0.05);
        c = Mix(c, Grime, 0.08 + 0.12 * Fractal(s, t, 4, 4, seed + 5));
        c = Mix(c, Salt, S((Fractal(s, t, 8, 3, seed + 6) - 0.7) / 0.1) * 0.25);
        return (c, Mix(PaintE, V(0, 60, 220), rust > 0.6 ? 0.7 : 0.2));
    }

    static (Vector3, Vector3) Rusty(double s, double t)
    {
        double n = Fractal(s, t, 8, 5, 531);
        var c = Mix(V(96, 50, 28), V(150, 86, 48), n) * (float)(0.85 + 0.25 * Fractal(s, t, 64, 2, 532));
        c = Mix(c, V(60, 40, 30), S((Fractal(s, t, 16, 3, 533) - 0.6) / 0.1) * 0.5);
        return (c, V(0, 50, 225));
    }

    static (Vector3, Vector3) Burnt(double s, double t)
    {
        double n = Fractal(s, t, 8, 5, 541);
        var c = Mix(V(30, 28, 26), V(70, 56, 44), n);
        c = Mix(c, RustC, S((Fractal(s, t, 16, 3, 542) - 0.55) / 0.15) * 0.7);                 // rust where the paint burnt off
        c = Mix(c, V(150, 140, 120), S((Fractal(s, t, 32, 2, 543) - 0.8) / 0.05) * 0.4);       // ash
        return (c, V(0, 70, 230));
    }

    static (Vector3, Vector3) TrackPlates(double s, double t)
    {
        double f = Frac(s * 6);
        var c = f < 0.18 ? V(48, 40, 34) : f < 0.3 ? V(90, 70, 52) : V(66, 52, 40);
        c *= (float)(0.9 + 0.15 * Fractal(s, t, 16, 2, 601));
        c = Mix(c, RustC, 0.35 + 0.3 * Fractal(s, t, 8, 3, 602));
        return (c, V(0, 120, 200));
    }

    /// <summary>Burlap sandbags laid in stretcher courses, each bag rounded and shadowed, rotting and sun-bleached.</summary>
    static (Vector3, Vector3) Sandbags(double s, double t)
    {
        double rows = t * 5, row = Math.Floor(rows), ft = Frac(rows);
        double fs = Frac(s * 3 + (row % 2) * 0.5);
        double bx = Math.Min(fs, 1 - fs) / 0.5, by = Math.Min(ft, 1 - ft) / 0.5;            // 0 at the bag's edge, 1 in the middle
        double puff = Math.Sqrt(Math.Clamp(Math.Min(bx * 1.6, 1) * Math.Min(by * 2.2, 1), 0, 1));
        var cloth = Mix(V(150, 128, 88), V(196, 176, 130), Fractal(s, t, 16, 3, 611)) * (float)(0.55 + 0.5 * puff);
        cloth *= (float)(0.92 + 0.1 * Math.Sin(s * 380) * Math.Sin(t * 380));               // the weave
        double bag = Hash((int)Math.Floor(s * 3 + (row % 2) * 0.5) % 3, (int)row % 5, 612);
        cloth = Mix(cloth, V(110, 104, 84), bag * 0.3);                                       // older, greyer bags
        cloth = Mix(cloth, V(120, 100, 70), S((Fractal(s, t, 8, 3, 613) - 0.62) / 0.1) * 0.4); // sand spilling from a split
        return (cloth, Rough);
    }

    static (Vector3, Vector3) Timber(double s, double t, Vector3 dark, Vector3 light, int seed)
    {
        // Planks running down the cell, their grain along them, dark gaps between, nail heads rusting.
        double plank = Frac(s * 4);
        double grain = Fractal(s, t, 32, 2, 3, seed);
        var c = Mix(dark, light, grain) * (float)(0.88 + 0.18 * Fractal(s, t, 4, 4, seed + 1));
        c *= (float)(0.9 + 0.12 * (Hash((int)Math.Floor(s * 4), 0, seed + 2) - 0.5));
        if (plank < 0.03 || plank > 0.97) c *= 0.45f;
        if (Math.Abs(Frac(t * 4) - 0.5) < 0.01 && Math.Abs(plank - 0.5) < 0.04) c = Mix(c, RustC, 0.7);
        c = Mix(c, Lichen, S((Fractal(s, t, 16, 3, seed + 3) - 0.74) / 0.06) * 0.4);
        return (c, Rough);
    }

    static (Vector3, Vector3) WireSteel(double s, double t)
    {
        double twist = 0.5 + 0.5 * Math.Sin((s * 20 + t * 6) * Math.PI);
        var c = Mix(V(54, 40, 32), V(110, 76, 52), twist) * (float)(0.85 + 0.2 * Fractal(s, t, 16, 2, 621));
        return (c, V(0, 140, 190));
    }

    /// <summary>Coursed rubble masonry: cream stone in uneven courses, recessed mortar; the dark one soot-blackened.</summary>
    static (Vector3, Vector3) Masonry(double s, double t, Vector3 stone, bool sooty)
    {
        double rows = t * 6, row = Math.Floor(rows), ft = Frac(rows);
        double len = 2.2 + Hash((int)row % 6, 0, 631) * 1.2;
        double cs = s * len + Hash((int)row % 6, 1, 632), col = Math.Floor(cs), fs = Frac(cs);
        bool mortar = ft < 0.07 || fs < 0.04;
        double tone = Hash((int)col, (int)row, 633);
        var c = mortar ? V(150, 142, 126) * 0.8f : stone * (float)(0.82 + 0.22 * tone) * (float)(0.9 + 0.15 * Fractal(s, t, 32, 2, 634));
        c = Mix(c, Lichen, S((Fractal(s, t, 16, 3, 635) - 0.7) / 0.06) * 0.45);
        if (sooty) c = Mix(c, V(36, 32, 30), 0.25 + 0.5 * S((t - 0.2) / -0.2 + Fractal(s, t, 4, 3, 636)));
        return (c, Rough);
    }

    static (Vector3, Vector3) Tiles(double s, double t)
    {
        // Slate on a Norman roof: grey courses, staggered, a few slipped and gone (dark gaps).
        double rows = t * 8, row = Math.Floor(rows), ft = Frac(rows);
        double cs = s * 6 + (row % 2) * 0.5, col = Math.Floor(cs), fs = Frac(cs);
        var c = V(84, 88, 96) * (float)(0.8 + 0.3 * Hash((int)col % 6, (int)row % 8, 641)) * (float)(0.9 + 0.2 * ft);
        if (fs < 0.03) c *= 0.6f;
        if (Hash((int)col % 6, (int)row % 8, 642) > 0.92) c = V(26, 24, 22);
        c = Mix(c, Lichen * 0.9f, S((Fractal(s, t, 8, 3, 643) - 0.7) / 0.08) * 0.5);
        return (c, V(0, 20, 200));
    }

    static (Vector3, Vector3) Plaster(double s, double t)
    {
        var c = V(214, 206, 188) * (float)(0.88 + 0.14 * Fractal(s, t, 8, 4, 651));
        double peel = Fractal(s, t, 8, 4, 652);
        if (peel > 0.62) c = Masonry(s, t, V(186, 172, 146), false).Item1;                   // stone showing where it peeled
        c = Mix(c, Grime, S((t - 0.75) / 0.25) * 0.3 + 0.1 * Fractal(s, t, 16, 2, 653));
        return (c, Rough);
    }

    static (Vector3, Vector3) Bark(double s, double t)
    {
        // Oak bark: dark grey-brown in deep furrows running up the trunk, lichen on the ridges.
        double furrow = Math.Abs(Math.Sin(s * Math.PI * 7 + Fractal(s, t, 4, 16, 3, 661) * 4));
        var c = Mix(V(48, 40, 34), V(108, 98, 86), furrow) * (float)(0.85 + 0.2 * Fractal(s, t, 8, 32, 2, 662));
        c = Mix(c, Lichen, S((Fractal(s, t, 16, 3, 663) - 0.72) / 0.06) * 0.5 * furrow);
        return (c, Rough);
    }

    static (Vector3, Vector3) Leaves(double s, double t, Vector3 dark, Vector3 light, int seed)
    {
        var c = dark * 0.72f;
        for (int k = 0; k < 3; k++)
        {
            double ls = s * 8 + k * 0.5, lt = t * 8 + k * 0.37;
            double ds = Frac(ls) - 0.5, dt = Frac(lt) - 0.5;
            double rot = Hash((int)Math.Floor(ls) % 8, (int)Math.Floor(lt) % 8, seed + k) * Math.PI;
            double u = ds * Math.Cos(rot) + dt * Math.Sin(rot), v = -ds * Math.Sin(rot) + dt * Math.Cos(rot);
            if (u * u / 0.15 + v * v / 0.03 < 1) c = Mix(dark, light, Hash((int)Math.Floor(ls) % 8, (int)Math.Floor(lt) % 8, seed + 10 + k));
        }
        return (c, Rough);
    }

    static (Vector3, Vector3) Grass(double s, double t, Vector3 dark, Vector3 light, int seed)
    {
        double blade = Math.Abs(Math.Sin(s * Math.PI * 11 + Fractal(s, t, 4, 2, seed) * 3));
        var c = Mix(dark, light, blade) * (float)(0.8 + 0.25 * Fractal(s, t, 32, 4, 2, seed + 1));
        c = Mix(c, dark * 0.8f, S((0.35 - t) / 0.35) * 0.4);                                  // darker at the base
        return (c, Rough);
    }

    static (Vector3, Vector3) Orchard(double s, double t)
    {
        var (c, e) = Leaves(s, t, V(62, 92, 44), V(120, 150, 70), 71);
        // A few apples going unpicked.
        double fs = Frac(s * 5), ft = Frac(t * 5);
        if (Hash((int)(s * 5) % 5, (int)(t * 5) % 5, 72) > 0.7 && (fs - 0.5) * (fs - 0.5) + (ft - 0.5) * (ft - 0.5) < 0.012) c = V(180, 60, 40);
        return (c, e);
    }

    static (Vector3, Vector3) Earth(double s, double t)
    {
        double n = Fractal(s, t, 4, 5, 691);
        var c = Mix(V(84, 70, 52), V(128, 108, 80), n) * (float)(0.85 + 0.25 * Fractal(s, t, 32, 2, 692));
        return (c, Rough);
    }

    /// <summary>The lighthouse's lantern, still burning: a bright warm face behind its glazing bars.</summary>
    static (Vector3, Vector3) Lamp(double s, double t)
    {
        bool bar = Frac(s * 4) < 0.06 || t < 0.06 || t > 0.94;
        if (bar) return (V(40, 40, 40), Metal);
        double hot = 1 - S((Math.Abs(t - 0.5) - 0.1) / 0.35);
        return (Mix(V(250, 222, 160), V(255, 250, 230), hot), V(255 * (0.6 + 0.4 * hot), 20, 40));
    }

    static (Vector3, Vector3) Window(double s, double t)
    {
        bool frame = s < 0.06 || s > 0.94 || t < 0.06 || t > 0.94 || Math.Abs(s - 0.5) < 0.03 || Math.Abs(t - 0.5) < 0.03;
        if (frame) return (V(70, 62, 52), Rough);
        var c = V(14, 14, 16);
        if (Hash((int)(s * 2), (int)(t * 2), 711) > 0.6) c = Mix(V(40, 52, 60), V(110, 130, 140), 1 - t);   // a pane still in
        return (c, Glass);
    }

    static (Vector3, Vector3) Granite(double s, double t)
    {
        double n = Fractal(s, t, 4, 5, 721);
        var c = Mix(V(110, 108, 104), V(168, 164, 156), n);
        if (Hash((int)(s * 96) % 96, (int)(t * 96) % 96, 722) > 0.85) c *= 0.7f;          // the grain's dark crystals
        c = Mix(c, Lichen, S((Fractal(s, t, 16, 3, 723) - 0.66) / 0.08) * 0.6);
        c = Mix(c, V(220, 210, 160), S((Fractal(s, t, 32, 2, 724) - 0.82) / 0.04) * 0.5);   // yellow lichen spots
        return (c * (float)(0.9 + 0.15 * Fractal(s, t, 64, 1, 725)), Rough);
    }

    static (Vector3, Vector3) Shingle(double s, double t)
    {
        double best = 9, second = 9; int id = 0;
        for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int cx = (int)Math.Floor(s * 10) + i, cy = (int)Math.Floor(t * 10) + j;
                int wx = ((cx % 10) + 10) % 10, wy = ((cy % 10) + 10) % 10;
                double px = (cx + Hash(wx, wy, 731)) / 10, py = (cy + Hash(wx, wy, 732)) / 10;
                double d = (s - px) * (s - px) + (t - py) * (t - py);
                if (d < best) { second = best; best = d; id = wx * 31 + wy; }
                else if (d < second) second = d;
            }
        double edge = Math.Sqrt(second) - Math.Sqrt(best);
        var c = Mix(V(120, 116, 108), V(186, 180, 166), Hash(id, 1, 733)) * (float)(0.8 + 0.3 * (1 - Math.Sqrt(best) * 8));
        if (edge < 0.008) c *= 0.5f;
        return (c, V(0, 0, 180));
    }

    static (Vector3, Vector3) SandCell(double s, double t)
    {
        var c = Mix(V(196, 178, 136), V(222, 206, 166), Fractal(s, t, 8, 4, 741)) * (float)(0.9 + 0.12 * Fractal(s, t, 64, 1, 742));
        c *= (float)(0.96 + 0.05 * Math.Sin((s * 3 + Fractal(s, t, 4, 2, 743)) * Math.PI * 8));  // wind ripples
        return (c, Rough);
    }
}
