// The ship's texture, one texel a game unit, and its detail maps and sky. Space is transparent (alpha 0: with
// voidGround the engine doesn't draw it and the starfield sky shows); the hull's own sheer side is opaque. Decks are
// plated in 64-unit panels with seams, rivets, wear, oil and scorch marks; the hangars have landing circles and guide
// lines in the side's colour (port red, starboard green, a ship's navigation colours), the passages edge stripes, the
// doors hazard chevrons, the bridge a dark blue deck, the CIC a lit grid, the reactor's well glowing rings round the
// core. Bulkhead tops and the rim are hull plating; their faces darker ribs. The hull's own outer sides (1.1) are a
// ship's side: plating, the livery, the ship's name and number, ports, airlocks, vents.
using System.Buffers.Binary;
using MapCore.Imaging;
using MapCore.Textures;

readonly record struct C(double R, double G, double B)
{
    public static C operator *(C a, double k) => new(a.R * k, a.G * k, a.B * k);
    public C Mix(C o, double t) { t = Math.Clamp(t, 0, 1); return new(R + (o.R - R) * t, G + (o.G - G) * t, B + (o.B - B) * t); }
    public static C operator +(C a, C b) => new(a.R + b.R, a.G + b.G, a.B + b.B);
}

/// <summary>Slope (degrees) and baked light per heightmap vertex.</summary>
sealed class Surface
{
    public readonly int NX, NZ;
    public readonly float[] H, Slope, Shade;

    public Surface(float[] heights, int nx, int nz)
    {
        NX = nx; NZ = nz; H = heights;
        Slope = new float[nx * nz];
        Shade = new float[nx * nz];
        double lx = -0.5, ly = 0.75, lz = -0.42, ll = Math.Sqrt(lx * lx + ly * ly + lz * lz);
        lx /= ll; ly /= ll; lz /= ll;
        Parallel.For(0, nz, z =>
        {
            for (int x = 0; x < nx; x++)
            {
                double dx = (V(x + 1, z) - V(x - 1, z)) / 16.0, dz = (V(x, z + 1) - V(x, z - 1)) / 16.0;
                double len = Math.Sqrt(dx * dx + 1 + dz * dz);
                Slope[z * nx + x] = (float)(Math.Atan(Math.Sqrt(dx * dx + dz * dz)) * 180 / Math.PI);
                Shade[z * nx + x] = (float)(0.82 + 0.3 * Math.Clamp((-dx * lx + ly - dz * lz) / len, 0, 1));
            }
        });
    }

    float V(int x, int z) => H[Math.Clamp(z, 0, NZ - 1) * NX + Math.Clamp(x, 0, NX - 1)];
    public double At(float[] f, double x, double z) => Terrain.Sample(f, NX, NZ, new P(x, z));
}

sealed class Painter(Surface s, Terrain.Result t)
{
    static readonly Noise N1 = new(2001), N2 = new(2002), N3 = new(2003), Stain = new(2004), Scorch = new(2005);

    static readonly C Deck = new(112, 116, 122), DeckDark = new(84, 88, 94), Seam = new(46, 48, 52), Rivet = new(150, 154, 160);
    static readonly C HangarDeck = new(128, 132, 136), BridgeDeck = new(58, 70, 92), CicDeck = new(70, 82, 96);
    static readonly C Hull = new(92, 96, 104), HullDark = new(60, 63, 70), HullTop = new(104, 108, 116), Rib = new(52, 54, 60);
    static readonly C Hazard = new(222, 168, 34), HazardDark = new(30, 30, 32), Crossing = new(232, 120, 36);
    static readonly C Port = new(196, 52, 44), Starboard = new(44, 170, 76), Cyan = new(80, 230, 245), Oil = new(34, 34, 38);
    static readonly C WellFloor = new(44, 52, 62);

    static byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    public void Row(int z, Span<Rgba> row)
    {
        for (int x = 0; x < row.Length; x++)
        {
            var (c, a) = Colour(x + 0.5, z + 0.5);
            row[x] = new Rgba(B(c.R), B(c.G), B(c.B), a);
        }
    }

    static double Hash(int x, int z, int seed)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(seed * 83492791);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return (h & 0xFFFF) / 65535.0;
    }

    /// <summary>The side a point is on, for its trim colour: port red, starboard green.</summary>
    static C SideColour(double x) => x < Layout.CX ? Port : Starboard;

    /// <summary>The mirror image's coordinates for the noise, so wear and stains are mirrored too.</summary>
    static double Mx(double x) => x <= Layout.CX ? x : Layout.W - x;

    (C, byte) Colour(double x, double z)
    {
        var p = new P(x, z);
        double mx = Mx(x);
        int cell = Terrain.Nearest(t.Cell, t.NX, t.NZ, p);
        double outside = s.At(t.Outside, x, z);
        double h = s.At(s.H, x, z), sl = s.At(s.Slope, x, z), shade = s.At(s.Shade, x, z);
        double n1 = N1.Fbm(mx / 600, z / 600, 3), n2 = N2.Fbm(mx / 120, z / 120, 2), n3 = N3.Fbm(mx / 30, z / 30, 2);

        // Space: transparent, but for the hull's own sheer side.
        if (cell == Terrain.Space && (outside > 64 || h < Layout.Space + 45))
            return (new C(0, 0, 0), 0);
        // The hull's own side (1.1): a ship's side, plated and in its livery, with its name, ports, airlocks and vents.
        if (cell == Terrain.Space)
        {
            // Four samples a texel, each at its own height: the side is steep (a texel spans about 6 of its height), and on
            // the bow's slanting faces the stripes and letters would otherwise step. The baked light is softened here, so
            // the sides facing away from it (starboard's, the stern's) read nearly as well as port's.
            var sum = new C(0, 0, 0);
            foreach (var (ox, oz) in SubSamples) sum = sum + HullSide(x + ox, z + oz, s.At(s.H, x + ox, z + oz));
            return (sum * (0.25 * (0.97 + 0.4 * (shade - 0.97))), 255);
        }

        // The 64-unit panel grid, its seams and rivets.
        double px = mx / 64, pz = z / 64;
        double fx = px - Math.Floor(px), fz = pz - Math.Floor(pz);
        double edge = Math.Min(Math.Min(fx, 1 - fx), Math.Min(fz, 1 - fz)) * 64;
        double tone = Hash((int)Math.Floor(px), (int)Math.Floor(pz), 3) - 0.5;
        bool rivet = edge > 3 && edge < 6 && (Math.Abs(fx * 64 - 4.5) < 1.5 || Math.Abs(fx * 64 - 59.5) < 1.5) && (Math.Abs(fz * 64 - 4.5) < 1.5 || Math.Abs(fz * 64 - 59.5) < 1.5);

        bool floor = Terrain.Nearest(t.Floor, t.NX, t.NZ, p);
        C c;
        if (!floor || sl > 40)
        {
            // Hull plating on the tops, darker ribs down the faces.
            var plate = HullPlate(h > Layout.Bulkhead + 20 ? HullTop : Hull, mx, z) * (0.96 + 0.05 * n2);
            double face = Geo.S((sl - 40) / 25);
            var faceC = Rib.Mix(Hull, 0.35 + 0.3 * Math.Abs(Math.Sin(mx / 9 + z / 9))) * (0.9 + 0.1 * n2);
            c = plate.Mix(faceC, face);
            // The rim's navigation lights, red to port and green to starboard, every 512 along the hull.
            if (cell != Terrain.Space && s.At(t.Inside, x, z) < 40 && Math.Abs(z % 512 - 256) < 10)
                c = c.Mix(SideColour(x) * 1.2, 0.85);
        }
        else
        {
            var info = cell >= 0 ? Layout.Cells[cell] : null;
            string name = info?.Name ?? "";
            var deck = name switch
            {
                "Forward Hangar" or "Main Hangar" => HangarDeck,
                "Bridge" or "Bridge Landing" => BridgeDeck,
                "CIC" => CicDeck,
                _ => Deck,
            };
            c = deck * (1 + 0.07 * tone) * (0.95 + 0.06 * n2);
            if (edge < 1.2) c = c.Mix(Seam, 0.75);
            if (rivet) c = c.Mix(Rivet, 0.45);
            // Wear down the middle of traffic, oil stains, the odd scorch.
            c = c.Mix(DeckDark, Geo.S((n1 + 0.1) / 0.3) * 0.25);
            c = c.Mix(Oil, Geo.S((Stain.Fbm(mx / 110, z / 110, 2) - 0.56) / 0.06) * 0.35);
            c = c.Mix(new C(24, 22, 22), Geo.S((Scorch.Fbm(mx / 160, z / 160, 3) - 0.58) / 0.06) * 0.45 * (0.6 + 0.4 * n3));
            if (info != null) c = Room(c, info, p, mx);
            int door = Terrain.Nearest(t.Door, t.NX, t.NZ, p);
            if (door >= 0) c = Door(c, door, p);
            // The outer walk (1.1): the navigation lights along its outer edge, red to port and green to starboard.
            if (cell < 0 && s.At(t.Inside, x, z) < 24 && Math.Abs(z % 512 - 256) < 10) c = c.Mix(SideColour(x) * 1.2, 0.85);
        }

        // Vents: a grated exhaust. (No metal spots to mark: the whole deck is metal, 1.1.)
        foreach (var (at, _) in Layout.Vents)
        {
            double d = (p - at).Length;
            if (d > 90) continue;
            c = c.Mix(new C(232, 170, 70), (1 - Geo.S(Math.Abs(d - 70) / 6)) * 0.8).Mix(new C(20, 20, 22), 1 - Geo.S((d - 56) / 6));
            if (d < 56 && (Math.Abs(Math.Sin((p.X - at.X) / 6)) < 0.3 || Math.Abs(Math.Sin((p.Z - at.Z) / 6)) < 0.3)) c = c.Mix(new C(80, 70, 60), 0.7);
        }
        return (c * shade, 255);
    }

    /// <summary>
    /// Hull plating: 256 × 128 plates in staggered courses, each a shade apart, seamed, with a lit lip and a row of
    /// rivets inside each seam; here and there a plate carries an access hatch or a vent grille.
    /// </summary>
    static C HullPlate(C baseC, double mx, double z)
    {
        double rz = z / 128, row = Math.Floor(rz);
        double rx = (mx + ((long)row % 2 == 0 ? 0 : 128)) / 256, col = Math.Floor(rx);
        double ex = (rx - col) * 256, ez = (rz - row) * 128;
        double toX = Math.Min(ex, 256 - ex), toZ = Math.Min(ez, 128 - ez), edge = Math.Min(toX, toZ);
        var c = baseC * (1 + 0.07 * (Hash((int)col, (int)row, 7) - 0.5));
        if (Math.Abs(ex % 64) < 0.8) c = c.Mix(Seam, 0.15);                              // the plate's own faint panel lines
        if (edge < 1.5) return c.Mix(Seam, 0.75);
        if (edge < 3) c *= 1.06;
        double along = toX < toZ ? ez : ex;
        if (Math.Abs(edge - 6) < 1.2 && Math.Abs(along % 16 - 8) < 1.2) c = c.Mix(Rivet, 0.5);
        double k = Hash((int)col, (int)row, 11);
        if (k < 0.07 && toX > 64 && toZ > 26)
        {
            // An access hatch: a dark inset with a light frame, a handle.
            c = Math.Min(toX - 64, toZ - 26) < 2.5 ? c.Mix(Rivet, 0.45) : c.Mix(HullDark, 0.55);
            if (Math.Abs(ez - 64) < 3 && Math.Abs(ex - 128) < 14) c = c.Mix(Hazard, 0.7);
        }
        else if (k < 0.12 && toX > 80 && toZ > 40)
            c = Math.Min(toX - 80, toZ - 40) < 2 ? c.Mix(Rivet, 0.35) : c.Mix(((int)Math.Floor(ex / 6) & 1) == 0 ? Seam : HullDark, 0.75);
        return c;
    }

    // ---- The hull's sides (1.1, at your call: "it looks like a cold wall instead of a ship exterior"). Painted on the
    // hull's own sheer side, which the top-down texture spreads over about 50 texels for its 320 of height (about 6 of
    // height a texel), so the markings are big and clean. Laid out along the hull (port's outline from the bow's tip round
    // to the stern's centre; starboard's points mirrored onto it) and up it by height; lettering reads left to right seen
    // from outside, on both sides.
    static readonly C SideUpper = new(126, 130, 136), SideLower = new(70, 74, 81), SideBand = new(44, 46, 51), SideTop = new(150, 154, 160);
    static readonly C Paintwork = new(236, 236, 228), Glass = new(26, 32, 42), Lamp = new(255, 224, 156), Soot = new(28, 26, 25);
    static readonly Noise Streak = new(2006), Blotch = new(2007);
    static readonly double[] HullS = HullLengths();
    static readonly (double X, double Z)[] SubSamples = [(-0.25, -0.25), (0.25, -0.25), (-0.25, 0.25), (0.25, 0.25)];

    static double[] HullLengths()
    {
        var hp = Layout.HullPort;
        var s = new double[hp.Length];
        for (int i = 1; i < hp.Length; i++) s[i] = s[i - 1] + (hp[i] - hp[i - 1]).Length;
        return s;
    }

    /// <summary>How far along the hull a point by its side is (from the bow's tip round port's side), and the outline's
    /// segment: 0-2 the bow, 3 the long side, 4 and 8 the stern between the housings, 5-7 and 9-10 the engine housings.</summary>
    static (double S, int Seg) AlongHull(P q)
    {
        var hp = Layout.HullPort;
        double best = double.MaxValue, s = 0; int seg = 0;
        for (int i = 0; i + 1 < hp.Length; i++)
        {
            double d = Geo.ToSegment(q, hp[i], hp[i + 1], out double t);
            if (d < best) { best = d; s = HullS[i] + t * (HullS[i + 1] - HullS[i]); seg = i; }
        }
        return (s, seg);
    }

    /// <summary>Stencil letters as strokes on a 4 × 6 grid (y up).</summary>
    static readonly Dictionary<char, (double X0, double Y0, double X1, double Y1)[]> Glyphs = new()
    {
        ['M'] = [(0, 0, 0, 6), (0, 6, 2, 2.5), (2, 2.5, 4, 6), (4, 6, 4, 0)],
        ['E'] = [(0, 0, 0, 6), (0, 6, 4, 6), (0, 3, 3.2, 3), (0, 0, 4, 0)],
        ['R'] = [(0, 0, 0, 6), (0, 6, 3, 6), (3, 6, 4, 5), (4, 5, 4, 4), (4, 4, 3, 3), (3, 3, 0, 3), (2.2, 3, 4, 0)],
        ['I'] = [(2, 0, 2, 6), (0.8, 6, 3.2, 6), (0.8, 0, 3.2, 0)],
        ['D'] = [(0, 0, 0, 6), (0, 6, 2.6, 6), (2.6, 6, 4, 4.6), (4, 4.6, 4, 1.4), (4, 1.4, 2.6, 0), (2.6, 0, 0, 0)],
        ['A'] = [(0, 0, 0, 4.6), (0, 4.6, 1.4, 6), (1.4, 6, 2.6, 6), (2.6, 6, 4, 4.6), (4, 4.6, 4, 0), (0, 2.6, 4, 2.6)],
        ['N'] = [(0, 0, 0, 6), (0, 6, 4, 0), (4, 0, 4, 6)],
        ['0'] = [(1.2, 0, 2.8, 0), (2.8, 0, 4, 1.2), (4, 1.2, 4, 4.8), (4, 4.8, 2.8, 6), (2.8, 6, 1.2, 6), (1.2, 6, 0, 4.8), (0, 4.8, 0, 1.2), (0, 1.2, 1.2, 0)],
        ['7'] = [(0, 6, 4, 6), (4, 6, 1.6, 0)],
        // 1.1: the starts' roles stencilled on their circles.
        ['O'] = [(1.2, 0, 2.8, 0), (2.8, 0, 4, 1.2), (4, 1.2, 4, 4.8), (4, 4.8, 2.8, 6), (2.8, 6, 1.2, 6), (1.2, 6, 0, 4.8), (0, 4.8, 0, 1.2), (0, 1.2, 1.2, 0)],
        ['T'] = [(0, 6, 4, 6), (2, 6, 2, 0)],
        ['C'] = [(4, 6, 1.2, 6), (1.2, 6, 0, 4.8), (0, 4.8, 0, 1.2), (0, 1.2, 1.2, 0), (1.2, 0, 4, 0)],
        ['H'] = [(0, 0, 0, 6), (4, 0, 4, 6), (0, 3, 4, 3)],
        ['F'] = [(0, 0, 0, 6), (0, 6, 4, 6), (0, 3, 3.2, 3)],
        ['S'] = [(4, 6, 1.2, 6), (1.2, 6, 0, 4.8), (0, 4.8, 0, 4.2), (0, 4.2, 1.2, 3), (1.2, 3, 2.8, 3), (2.8, 3, 4, 1.8), (4, 1.8, 4, 1.2), (4, 1.2, 2.8, 0), (2.8, 0, 0, 0)],
        ['U'] = [(0, 6, 0, 1.2), (0, 1.2, 1.2, 0), (1.2, 0, 2.8, 0), (2.8, 0, 4, 1.2), (4, 1.2, 4, 6)],
        ['P'] = [(0, 0, 0, 6), (0, 6, 3, 6), (3, 6, 4, 5), (4, 5, 4, 4), (4, 4, 3, 3), (3, 3, 0, 3)],
    };

    /// <summary>The starts' roles on their circles (1.1, at your call): the grid unit, the strokes' half-width (in units),
    /// and how far fore of the circle's middle the text's foot stands, in the band between the middle ring (120) and the
    /// inner ring (r - 30), clear of the diagonals: SUPPORT, the longest, fits the smaller circles (300).</summary>
    const double RoleUnit = 7, RoleHalf = 0.66, RoleFoot = 165;

    /// <summary>How much of a point a start's role covers: upright as BAR's default camera sees the map (from the south,
    /// the bow at the top), reading left to right on both sides.</summary>
    static double RoleCover(P p)
    {
        foreach (var (role, at, r) in Layout.StartCircles)
        {
            if ((p - at).Length > r) continue;
            string text = role.ToUpperInvariant();
            double width = ((text.Length - 1) * Advance + 4) * RoleUnit;
            return TextCover(text, p.X - (at.X - width / 2), at.Z - p.Z, RoleFoot, RoleUnit, RoleHalf);
        }
        return 0;
    }
    const double Advance = 5.6;

    /// <summary>The ship's name and hull number: the text, where along the hull its middle is, its grid unit, its strokes'
    /// half-width (in units), and its foot's height. The number at the bow, the name on each long side forward and across
    /// the stern between the engine housings (seen from the default camera, from the south).</summary>
    static readonly (string Text, double Mid, double Unit, double Half, double Foot)[] Lettering =
    [
        ("07", (HullS[1] + HullS[2]) / 2, 27, 0.75, 126),
        ("MERIDIAN", HullS[3] + 1150, 22, 0.55, 150),
        ("MERIDIAN", (HullS[8] + HullS[9]) / 2, 22, 0.55, 150),
    ];

    /// <summary>How much of a point text covers: gx along the text from its start (reading left to right), v its height.</summary>
    static double TextCover(string text, double gx, double v, double foot, double unit, double half)
    {
        double adv = Advance * unit;
        if (gx < -unit || gx > text.Length * adv || v < foot - unit || v > foot + 7 * unit) return 0;
        int i = (int)Math.Floor(gx / adv);
        double best = double.MaxValue, ly = (v - foot) / unit;
        for (int k = Math.Max(0, i - 1); k <= Math.Min(text.Length - 1, i + 1); k++)
        {
            if (!Glyphs.TryGetValue(text[k], out var strokes)) continue;
            var q = new P((gx - k * adv) / unit, ly);
            foreach (var (x0, y0, x1, y1) in strokes) best = Math.Min(best, Geo.ToSegment(q, new P(x0, y0), new P(x1, y1), out _));
        }
        return 1 - Geo.S(((best - half) * unit) / 4 + 0.5);
    }

    /// <summary>
    /// The ship's badge (1.1, at your call: "as the ship badge like they did on planes"): your map editor's robot-and-planet
    /// icon (badge.png, beside this file), as nose art, 190 square, its foot at 96: on each long side forward of the name,
    /// and on the stern outboard of it. The side gives it about 32 texel rows, so each texel is the icon's average over its
    /// footprint, cut to the icon's rounded square.
    /// </summary>
    static readonly Badge ShipBadge = Badge.Load(Path.Combine(AppContext.BaseDirectory, "badge.png"));
    const double BadgeSize = 190, BadgeFoot = 96;
    static readonly double[] Badges = [HullS[3] + 380, (HullS[8] + HullS[9]) / 2 - 675];

    /// <summary>How much of the badge's rounded square covers a point (from its top left).</summary>
    static double BadgeMask(double bx, double by)
    {
        double half = BadgeSize / 2 - BadgeSize * 0.004, r = BadgeSize * 0.175;
        double qx = Math.Abs(bx - BadgeSize / 2) - (half - r), qy = Math.Abs(by - BadgeSize / 2) - (half - r);
        double d = Math.Sqrt(Math.Max(qx, 0) * Math.Max(qx, 0) + Math.Max(qy, 0) * Math.Max(qy, 0)) + Math.Min(Math.Max(qx, qy), 0) - r;
        return 1 - Geo.S(d / 3 + 0.5);
    }

    /// <summary>The airlocks along each long side (how far along it), and the engines' mounts on the housings' aft faces.</summary>
    static readonly double[] Airlocks = [2800, 5200, 7600];
    static readonly double[] Mounts = [HullS[6] + 400, HullS[6] + 1200, HullS[10] + 500];

    C HullSide(double x, double z, double v)
    {
        bool port = x < Layout.CX;
        var (s, seg) = AlongHull(new P(Mx(x), z));
        double Band(double lo, double hi) => Geo.S((v - lo) / 3 + 0.5) * (1 - Geo.S((v - hi) / 3 + 0.5));
        double streak = Streak.Fbm(s / 5, v / 200, 2), blotch = Blotch.Fbm(s / 260, v / 120, 3);

        // Plating: the lower hull and two strakes above it, plates 448 long staggered along each, a shade apart; seams,
        // rivets, weathering streaks running down, darker towards the foot.
        int strake = v < 124 ? 0 : v < 210 ? 1 : 2;
        double run = s + strake * 224, col = Math.Floor(run / 448), inPlate = run - col * 448, butt = Math.Min(inPlate, 448 - inPlate);
        var c = (strake == 0 ? SideLower : SideUpper) * (1 + 0.07 * (Hash((int)col, strake, 21) - 0.5)) * (1 + 0.05 * blotch);
        c = c.Mix(Soot, Geo.S((streak - 0.1) / 0.35) * 0.2 * (1.2 - v / 380));
        if (butt < 1.4) c = c.Mix(Seam, 0.7);
        else if (butt < 3) c *= 1.05;
        if (Math.Abs(v - 210) < 3) c = c.Mix(Seam, 0.55);
        if ((Math.Abs(butt - 8) < 1.6 && v % 24 < 7) || (Math.Abs(v - 201) < 3 && s % 16 > 6 && s % 16 < 10)) c = c.Mix(Rivet, 0.35);

        // Small vents low on the hull, here and there.
        {
            double k = Math.Floor(s / 640), dv = s - (k * 640 + 320);
            if (Hash((int)k, 5, 41) < 0.5 && Math.Abs(dv) < 24 && v > 76 && v < 100)
                c = Math.Abs(dv) > 21 || v < 79 || v > 97 ? c.Mix(Rivet, 0.3) : c.Mix(((int)Math.Floor((dv + 24) / 6) & 1) == 0 ? Seam : SideBand, 0.85);
        }

        // The long side: rows of lit ports in groups of eight, a vent between the groups, three airlocks.
        if (seg == 3)
        {
            double rel = s - HullS[3];
            bool byLock = false;
            foreach (double a in Airlocks)
            {
                double da = rel - a;
                if (Math.Abs(da) < 160) byLock = true;
                if (Math.Abs(da) <= 62 && v >= 128 && v <= 274)
                {
                    bool frame = Math.Abs(da) > 50 || v < 138 || v > 264;
                    if (frame) c = c.Mix(((int)Math.Floor((rel + v * 0.6) / 16) & 1) == 0 ? Hazard : HazardDark, 0.85);
                    else
                    {
                        c = SideBand * 1.25;
                        if (Math.Abs(da) < 1.5) c = c.Mix(Seam, 0.8);
                        if (Math.Abs(da) < 14 && v > 224 && v < 250) c = Lamp * 0.8;
                    }
                }
                if (da > 72 && da < 84 && v > 236 && v < 252) c = SideColour(x) * 1.3;
            }
            if (!byLock && rel > 1900 && rel < HullS[4] - HullS[3] - 300)
            {
                double g = (rel - 1900) % 480;
                int slot = (int)Math.Floor(g / 40);
                if (slot < 8)
                {
                    double local = g - slot * 40 - 20;
                    if (Math.Abs(local) < 10 && v > 226 && v < 256)
                    {
                        bool rim = Math.Abs(local) > 6.5 || v < 231 || v > 251;
                        double lit = Hash((int)Math.Floor((rel - 1900) / 40), 3, 31);
                        c = rim ? SideBand : lit > 0.3 ? Lamp * (0.8 + 0.2 * lit) : Glass;
                    }
                }
                else
                {
                    double vg = g - 400;
                    if (Math.Abs(vg) < 26 && v > 192 && v < 216)
                        c = Math.Abs(vg) > 23 || v < 195 || v > 213 ? c.Mix(Rivet, 0.3) : c.Mix(((int)Math.Floor((vg + 26) / 6) & 1) == 0 ? Seam : SideBand, 0.85);
                    else if (Math.Abs(vg) < 22 && v > 130 && v <= 192)
                        c = c.Mix(Soot, 0.35 * (v - 130) / 62 * (0.6 + 0.4 * Math.Abs(streak)));
                }
            }
        }

        // The engines' mounts on the housings' aft faces: a dark ring round each throat, the plating scorched round it.
        if (seg is 6 or 10)
            foreach (double m in Mounts)
            {
                double d = Math.Sqrt((s - m) * (s - m) + (v - (Layout.WalkH + global::Models.BellAxis)) * (v - (Layout.WalkH + global::Models.BellAxis)));
                if (d > 280) continue;
                c = c.Mix(Soot, (1 - Geo.S((d - 110) / 160)) * 0.6 * (0.75 + 0.25 * streak));
                if (Math.Abs(d - (global::Models.BellThroat + 10)) < 7) c = c.Mix(SideBand, 0.9);
            }

        // The livery: a dark band over the lower hull, a white pinstripe, the side's colour (sweeping down at the bow), a
        // dark coaming at the top with a lit edge.
        double accentFoot = s < 1600 ? 304 - (1600 - s) * 0.09 : 304;
        c = c.Mix(SideBand, Band(118, 128) * 0.9);
        c = c.Mix(Paintwork * 0.95, Band(292, 300) * 0.9);
        c = c.Mix(SideColour(x) * 0.92, Band(accentFoot, 326) * 0.92);
        c = c.Mix(SideBand, Geo.S((v - 330) / 3 + 0.5) * 0.9);
        c = c.Mix(SideTop, Geo.S((v - 360) / 4) * 0.8);

        // The name and the number, a soft shadow under them, the paint worn here and there.
        foreach (var (text, mid, unit, half, foot) in Lettering)
        {
            double width = ((text.Length - 1) * Advance + 4) * unit, start = mid - width / 2;
            if (s < start - 60 || s > start + width + 60) continue;
            double gx = port ? s - start : start + width - s;
            double shadow = TextCover(text, gx - 6, v + 8, foot, unit, half), cover = TextCover(text, gx, v, foot, unit, half);
            double wear = 1 - 0.4 * Geo.S((Blotch.Fbm(s / 18, v / 18, 2) - 0.35) / 0.2);
            c = c.Mix(SideBand, shadow * 0.45).Mix(Paintwork, cover * wear);
        }

        // The ship's badge: nose art forward of the name on each long side, and outboard of it on the stern; upright and
        // unmirrored seen from outside, a soft shadow under it.
        foreach (double mid in Badges)
        {
            double start = mid - BadgeSize / 2;
            if (s < start - 20 || s > start + BadgeSize + 20 || v < BadgeFoot - 20 || v > BadgeFoot + BadgeSize + 20) continue;
            double bx = port ? s - start : start + BadgeSize - s, by = BadgeFoot + BadgeSize - v;    // from its top left
            c = c.Mix(SideBand, BadgeMask(bx - 5, by - 7) * 0.45);
            double m = BadgeMask(bx, by);
            if (m <= 0) continue;
            const double k = 1.0 / BadgeSize;
            var art = ShipBadge.Average((bx - 0.5) * k, (by - 3) * k, (bx + 0.5) * k, (by + 3) * k);
            double wear = 1 - 0.15 * Geo.S((Blotch.Fbm(s / 14, v / 14, 2) - 0.4) / 0.2);
            c = c.Mix(art, m * wear);
        }

        return c * (0.8 + 0.2 * Geo.S((v - 65) / 110));
    }

    /// <summary>A room's own markings.</summary>
    static C Room(C c, Layout.Cell info, P p, double mx)
    {
        var side = SideColour(p.X);
        var (min, max) = Bounds(info.Outline);
        double cx = (min.X + max.X) / 2, cz = (min.Z + max.Z) / 2;
        // The starts' circles (1.1, 8v8): the hangars' landing circles, and the same etched at every other start (the tech
        // start's round its vent): a ring in the side's colour, a ring inside it, a white ring at the middle, white
        // diagonals between.
        foreach (var (_, at, r) in Layout.StartCircles)
        {
            double d = (p - at).Length;
            if (d > r + 12) continue;
            if (Math.Abs(d - r) < 9 || Math.Abs(d - (r - 30)) < 3) c = c.Mix(side, 0.8);
            if (d < r - 30 && Math.Abs(Math.Abs(p.X - at.X) - Math.Abs(p.Z - at.Z)) < 6 && d > 120) c = c.Mix(new C(220, 220, 214), 0.6);
            if (Math.Abs(d - 120) < 4) c = c.Mix(new C(220, 220, 214), 0.7);
        }
        switch (info.Name)
        {
            case "Forward Hangar" or "Main Hangar":
            {
                // A guide line down the hangar between its two start circles, hazard edging round the deck.
                if (Math.Abs(p.X - cx) < 6) c = c.Mix(new C(230, 190, 40), 0.7);
                double inset = Math.Min(Math.Min(p.X - min.X, max.X - p.X), Math.Min(p.Z - min.Z, max.Z - p.Z));
                if (inset > 60 && inset < 84) c = c.Mix(((int)Math.Floor((p.X + p.Z) / 40) & 1) == 0 ? Hazard : HazardDark, 0.75);
                break;
            }
            case "Passage":
            {
                // Edge stripes in the side's colour, chevrons pointing fore and aft every 400.
                double a = Math.Min(p.X - min.X, max.X - p.X);
                if (a > 46 && a < 60) c = c.Mix(side, 0.75);
                double f = ((p.Z % 400) + 400) % 400;
                if (f < 70 && Math.Abs(Math.Abs(p.X - cx) - (f - 35) * 2.2) < 8 && Math.Abs(p.X - cx) < 140) c = c.Mix(new C(210, 214, 220), 0.6);
                break;
            }
            case "CIC":
            {
                // A lit grid, and the ring round the holo-table.
                if ((((p.X % 128) + 128) % 128 < 3 || ((p.Z % 128) + 128) % 128 < 3)) c = c.Mix(Cyan * 0.8, 0.35);
                double d = (p - new P(Layout.CX, cz)).Length;
                if (Math.Abs(d - 420) < 10) c = c.Mix(Cyan, 0.8);
                if (Math.Abs(d - 470) < 4) c = c.Mix(Cyan * 0.8, 0.6);
                break;
            }
            case "Bridge" or "Bridge Landing":
            {
                if ((((p.Z % 96) + 96) % 96) < 3) c = c.Mix(Cyan * 0.6, 0.3);
                break;
            }
            case "Main Reactor":
            {
                double r = (p - Layout.WellAt).Length;
                if (r < Layout.WellR - 40)
                {
                    c = WellFloor.Mix(c, 0.3);
                    foreach (double ring in new[] { 260.0, 380.0, 500.0 })
                        if (Math.Abs(r - ring) < 9) c = c.Mix(Cyan, 0.9);
                }
                else if (r < Layout.WellR + 70)
                    c = c.Mix(((int)Math.Floor(Math.Atan2(p.Z - Layout.WellAt.Z, p.X - Layout.WellAt.X) * 40 / Math.PI) & 1) == 0 ? Hazard : HazardDark, 0.8);
                break;
            }
            case "Shield Generator":
            {
                double d = (p - new P(Layout.CX, cz)).Length;
                if (Math.Abs(d - 520) < 8 || Math.Abs(d - 560) < 4) c = c.Mix(new C(120, 160, 255), 0.7);
                break;
            }
            case "Engine Room":
            {
                // Grating over the drive, framed (1.1: forward, the engine ramp and step aft of it).
                double gx = Math.Abs(p.X - Layout.CX);
                if (gx < 260 && p.Z > 11350 && p.Z < 11950)
                {
                    bool bar = (((p.Z % 24) + 24) % 24) < 4 || gx % 24 < 4;
                    c = gx > 250 || p.Z < 11360 || p.Z > 11940 ? c.Mix(Hazard, 0.7) : c.Mix(bar ? new C(80, 80, 78) : new C(30, 30, 32), 0.75);
                }
                break;
            }
        }
        // Each start's role stencilled on its circle (AIR, FRONT, TECH, SUPPORT), white like its markings, over the
        // hangars' guide line, the paint worn here and there.
        double role = RoleCover(p);
        if (role > 0) c = c.Mix(new C(226, 226, 220), role * 0.85 * (1 - 0.3 * Geo.S((Blotch.Fbm(p.X / 14, p.Z / 14, 2) - 0.4) / 0.2)));
        return c;
    }

    /// <summary>A door: hazard chevrons across its threshold (orange at the centreline crossings and the spine).</summary>
    static C Door(C c, int door, P p)
    {
        var d = Layout.Doors[door];
        if (d.Kind == Layout.DoorKind.Stairs)
            return c.Mix(new C(74, 78, 86), 0.6).Mix(new C(150, 156, 164), ((((p.Z - d.Z0) % 14) + 14) % 14 < 2) ? 0.7 : 0);
        bool across = d.X1 - d.X0 < d.Z1 - d.Z0;                    // a wall running north-south: the door opens east-west
        double along = across ? p.Z : p.X;
        var colour = d.Kind is Layout.DoorKind.Crossing or Layout.DoorKind.Spine ? Crossing : Hazard;
        return c.Mix((((int)Math.Floor((along + (across ? p.X : p.Z)) / 36)) & 1) == 0 ? colour : HazardDark, 0.8);
    }

    static (P Min, P Max) Bounds(P[] poly) => (new P(poly.Min(q => q.X), poly.Min(q => q.Z)), new P(poly.Max(q => q.X), poly.Max(q => q.Z)));
}

static class Derive
{
    static byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    /// <summary>No detail textures on a ship (the template's are a jungle's): the splat distribution all zero.</summary>
    public static void Splat(int size, string path)
    {
        var img = new RgbaImage(size, size);
        DdsFile.Create(DdsFormat.Dxt5, img, true).Save(path);
    }

    /// <summary>Decks and plating catch the light (metal); faces less; space none.</summary>
    public static void Specular(Surface s, Terrain.Result t, int sizeX, int sizeZ, string path)
    {
        var img = new RgbaImage(sizeX, sizeZ);
        double ux = Layout.W / sizeX, uz = Layout.H / sizeZ;
        Parallel.For(0, sizeZ, py =>
        {
            for (int px = 0; px < sizeX; px++)
            {
                double x = (px + 0.5) * ux, z = (py + 0.5) * uz;
                bool space = Terrain.Nearest(t.Cell, t.NX, t.NZ, new P(x, z)) == Terrain.Space;
                double sl = s.At(s.Slope, x, z);
                double a = space ? 0 : sl > 40 ? 70 : 150;
                img.SetPixel(px, py, new Rgba(B(a * 0.35), B(a * 0.36), B(a * 0.4), B(a)));
            }
        });
        img.FlipVertical(); // map .dds files are stored upside down (PLAN.md rule 7)
        DdsFile.Create(DdsFormat.Dxt5, img, true).Save(path);
    }

    /// <summary>The ground's slopes baked into a normal map (x red, z green, up blue).</summary>
    public static void Normals(Surface s, int sizeX, int sizeZ, string path)
    {
        var img = new RgbaImage(sizeX, sizeZ);
        double ux = Layout.W / sizeX, uz = Layout.H / sizeZ;
        Parallel.For(0, sizeZ, py =>
        {
            for (int px = 0; px < sizeX; px++)
            {
                double x = (px + 0.5) * ux, z = (py + 0.5) * uz;
                double dx = (s.At(s.H, x + 8, z) - s.At(s.H, x - 8, z)) / 16, dz = (s.At(s.H, x, z + 8) - s.At(s.H, x, z - 8)) / 16;
                double len = Math.Sqrt(dx * dx + 1 + dz * dz);
                img.SetPixel(px, py, new Rgba(B(127.5 * (1 - dx / len)), B(127.5 * (1 - dz / len)), B(127.5 * (1 + 1 / len)), 255));
            }
        });
        img.FlipVertical();
        DdsFile.Create(DdsFormat.Dxt1, img, true).Save(path);
    }

    /// <summary>A starfield sky cube (A8R8G8B8, six faces): black space, stars of a few brightnesses, a faint nebula.</summary>
    public static void Sky(string path, int size = 1024)
    {
        var nebula = new Noise(3001);
        var face = new RgbaImage(size, size);
        var probe = DdsFile.Create(DdsFormat.Uncompressed, face, false).ToBytes();
        int headerBytes = probe.Length - size * size * 4;
        var bytes = new byte[headerBytes + 6 * size * size * 4];
        probe.AsSpan(0, headerBytes).CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(108), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(108)) | 0x8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(112), 0x200 | 0xFC00);
        for (int fc = 0; fc < 6; fc++)
        {
            int f = fc;
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
                    double ux = dir.X / len, uy = dir.Y / len, uz = dir.Z / len;
                    // A faint violet-blue nebula band.
                    double band = Math.Exp(-Math.Pow((uy * 0.8 + ux * 0.6) / 0.35, 2));
                    double neb = Math.Max(0, nebula.Fbm(ux * 3 + uz * 2, uy * 3 - uz, 4) + 0.15) * band;
                    double r = 4 + 40 * neb, g = 5 + 22 * neb, b = 12 + 70 * neb;
                    // Stars: one in a few hundred texels, a few bright.
                    uint hsh = (uint)(f * 92837111) ^ (uint)(tx * 73856093) ^ (uint)(ty * 19349663);
                    hsh ^= hsh >> 13; hsh *= 0x5bd1e995; hsh ^= hsh >> 15;
                    double roll = (hsh & 0xFFFF) / 65535.0;
                    if (roll > 0.9965)
                    {
                        double bright = roll > 0.99965 ? 255 : roll > 0.999 ? 200 : 130;
                        double tint = ((hsh >> 16) & 0xFF) / 255.0;
                        r = Math.Max(r, bright * (0.85 + 0.15 * tint)); g = Math.Max(g, bright * 0.9); b = Math.Max(b, bright * (1 - 0.1 * tint));
                    }
                    int o = headerBytes + (f * size * size + ty * size + tx) * 4;
                    bytes[o] = B(b); bytes[o + 1] = B(g); bytes[o + 2] = B(r); bytes[o + 3] = 255;
                }
            });
        }
        File.WriteAllBytes(path, bytes);
    }
}

/// <summary>A picture painted onto the ground (1.1: the ship's badge), with a summed-area table so any rectangle of it
/// averages in constant time: the hull's side samples it far below its own resolution.</summary>
sealed class Badge
{
    readonly int w, h;
    readonly double[] sum;          // (w + 1) × (h + 1) × 3: R, G, B summed over every pixel above and left

    Badge(RgbaImage img)
    {
        w = img.Width; h = img.Height;
        sum = new double[(w + 1) * (h + 1) * 3];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = img.GetPixel(x, y);
                int o = ((y + 1) * (w + 1) + x + 1) * 3, up = (y * (w + 1) + x + 1) * 3, left = ((y + 1) * (w + 1) + x) * 3, diag = (y * (w + 1) + x) * 3;
                sum[o] = p.R + sum[up] - sum[diag] + sum[left];
                sum[o + 1] = p.G + sum[up + 1] - sum[diag + 1] + sum[left + 1];
                sum[o + 2] = p.B + sum[up + 2] - sum[diag + 2] + sum[left + 2];
            }
    }

    public static Badge Load(string path) => new(PngReader.Read(path).ToRgba());

    /// <summary>The picture's average colour over a rectangle given in its own units (0 to 1 across and down).</summary>
    public C Average(double u0, double v0, double u1, double v1)
    {
        int x0 = Math.Clamp((int)Math.Floor(u0 * w), 0, w - 1), x1 = Math.Clamp((int)Math.Ceiling(u1 * w), x0 + 1, w);
        int y0 = Math.Clamp((int)Math.Floor(v0 * h), 0, h - 1), y1 = Math.Clamp((int)Math.Ceiling(v1 * h), y0 + 1, h);
        double n = (x1 - x0) * (y1 - y0);
        double At(int x, int y, int k) => sum[(y * (w + 1) + x) * 3 + k];
        double Box(int k) => (At(x1, y1, k) - At(x0, y1, k) - At(x1, y0, k) + At(x0, y0, k)) / n;
        return new C(Box(0), Box(1), Box(2));
    }
}
