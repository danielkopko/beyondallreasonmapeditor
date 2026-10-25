// The ground's texture, one texel a game unit, and the detail maps and sky. West to east: a sandy seabed darkening
// with depth, weed on it; wet sand at the water's edge with the surf's lace of foam, dry pale sand above with wind
// ripples and the wrack line's weed; grey shingle and granite under the headland, heather, bracken and grass on its
// heath; the foredune's bare seaward face, marram over its crest; the dunes bare on their crests and slip faces, marram
// elsewhere, damp green slacks between with rushes; the trenches' spoil, sandbagged parapets, timber revetments and
// duckboards; the dead man's zone churned to mud round its craters, grass in tufts, chalky ejecta; the hill's fields
// each its own crop (pasture, hay, plough, stubble) inside dark hedgerows, sunken lanes, the village's rubble; the sandy
// end's flats; mud and reeds on the inlet's banks, clean sand round the cove. Tracks, lanes and old cratered roads.
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
        // The sun from the south-west, fairly low: dunes' slip faces and hedgerows cast shade.
        double lx = -0.62, ly = 0.6, lz = 0.5, ll = Math.Sqrt(lx * lx + ly * ly + lz * lz);
        lx /= ll; ly /= ll; lz /= ll;
        Parallel.For(0, w, z =>
        {
            for (int x = 0; x < w; x++)
            {
                double dx = (V(x + 1, z) - V(x - 1, z)) / 16.0, dz = (V(x, z + 1) - V(x, z - 1)) / 16.0;
                double len = Math.Sqrt(dx * dx + 1 + dz * dz);
                Slope[z * w + x] = (float)(Math.Atan(Math.Sqrt(dx * dx + dz * dz)) * 180 / Math.PI);
                Shade[z * w + x] = (float)(0.8 + 0.32 * Math.Clamp((-dx * lx + ly - dz * lz) / len, 0, 1));
            }
        });
    }

    float V(int x, int z) => H[Math.Clamp(z, 0, W - 1) * W + Math.Clamp(x, 0, W - 1)];
    public double At(float[] f, double x, double z) => Terrain.Sample(f, W, new P(x, z));
}

readonly record struct C(double R, double G, double B)
{
    public static C operator *(C a, double k) => new(a.R * k, a.G * k, a.B * k);
    public C Mix(C o, double t) { t = Math.Clamp(t, 0, 1); return new(R + (o.R - R) * t, G + (o.G - G) * t, B + (o.B - B) * t); }
}

sealed class Painter
{
    static readonly Noise N1 = new(1101), N2 = new(1102), N3 = new(1103), Fine = new(1104), Lace = new(1105), Weed = new(1106), Tuft = new(1107);

    static readonly C SeaSand = new(176, 162, 124), SeaDeep = new(118, 116, 98), SeaWeed = new(84, 92, 62), Mud = new(96, 86, 70), MudDark = new(68, 60, 48);
    static readonly C WetSand = new(156, 142, 108), DrySand = new(222, 206, 166), PaleSand = new(232, 220, 186), Foam = new(238, 238, 230), Wrack = new(66, 62, 42);
    static readonly C Shingle = new(144, 138, 128), Granite = new(128, 124, 116), Lichen = new(168, 170, 112);
    static readonly C Heather = new(108, 90, 88), HeathGreen = new(98, 106, 68), Bracken = new(146, 112, 68), GreyDune = new(150, 150, 114);
    static readonly C Marram = new(170, 172, 112), MarramDark = new(120, 130, 78), Slack = new(118, 132, 82), Rush = new(92, 104, 66), PineLitter = new(122, 98, 70);
    static readonly C Earth = new(108, 90, 68), EarthDark = new(78, 64, 50), Chalk = new(176, 168, 146), Turf = new(112, 126, 70), TuftC = new(140, 146, 82);
    static readonly C Burlap = new(176, 156, 112), Plank = new(124, 104, 78), Revet = new(100, 84, 64);
    static readonly C Pasture = new(96, 130, 64), Hay = new(166, 156, 88), Plough = new(118, 94, 68), Stubble = new(190, 170, 112), Rough = new(108, 116, 70), Gorse = new(150, 140, 50);
    static readonly C HedgeShade = new(52, 66, 38), LaneEarth = new(132, 112, 84), Cobble = new(132, 126, 116), Rubble = new(150, 140, 124);
    static readonly C TrackSand = new(204, 188, 148), Reeds = new(126, 128, 76), Saltmarsh = new(118, 120, 84);

    readonly Surface s;
    readonly Terrain.Result t;
    readonly double[] coast, berm, fdw, fa, fb, hd;
    readonly (double Z0, double Full)?[] runA, runB;

    public Painter(Surface surface, Terrain.Result terrain)
    {
        s = surface;
        t = terrain;
        int rows = (int)Layout.Size;
        coast = new double[rows]; berm = new double[rows]; fdw = new double[rows]; fa = new double[rows]; fb = new double[rows]; hd = new double[rows];
        runA = new (double, double)?[rows]; runB = new (double, double)?[rows];
        Parallel.For(0, rows, z =>
        {
            double zz = z + 0.5;
            coast[z] = Layout.Coast(zz); berm[z] = Layout.BermX(zz); fdw[z] = Layout.ForeduneWidth(zz);
            fa[z] = Layout.FireA(zz); fb[z] = Layout.FireB(zz); hd[z] = Layout.Headland(zz);
            runA[z] = Terrain.Run("beach", zz); runB[z] = Terrain.Run("hill", zz);
        });
    }

    static byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    /// <summary>The colour at a point (for previews at a coarser scale).</summary>
    public Rgba At(int x, int z)
    {
        var c = Colour(x + 0.5, z + 0.5, x, z);
        return new Rgba(B(c.R), B(c.G), B(c.B), 255);
    }

    public void Row(int z, Span<Rgba> row)
    {
        for (int x = 0; x < row.Length; x++)
        {
            var c = Colour(x + 0.5, z + 0.5, x, z);
            row[x] = new Rgba(B(c.R), B(c.G), B(c.B), 255);
        }
    }

    static double Grain(int x, int z)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return (h & 0xFFFF) / 32767.5 - 1;
    }

    double F(float[] f, double x, double z) => Terrain.Sample(f, t.N, new P(x, z));

    T Nearest<T>(T[] f, double x, double z) => f[Math.Clamp((int)Math.Round(z / 8), 0, t.N - 1) * t.N + Math.Clamp((int)Math.Round(x / 8), 0, t.N - 1)];

    C Colour(double x, double z, int ix, int iz)
    {
        double h = s.At(s.H, x, z), sl = s.At(s.Slope, x, z), shade = s.At(s.Shade, x, z);
        double n1 = N1.Fbm(x / 600, z / 600, 3), n2 = N2.Fbm(x / 120, z / 120, 3), n3 = N3.Fbm(x / 30, z / 30, 2), fine = Fine.At(x / 6, z / 6);
        double grain = Grain(ix, iz);
        double c0 = coast[iz], b0 = berm[iz];
        var kind = Nearest(t.Kind, x, z);
        double inlet = F(t.Inlet, x, z);
        C c;

        // ---- Under water: the seabed and the inlet's bed.
        if (h < 0)
        {
            double deep = Geo.S(-h / 40);
            if (inlet < 0)
            {
                c = Mud.Mix(SeaSand * 0.9, Geo.S((-inlet - 40) / 80)).Mix(SeaDeep, deep * 0.6) * (0.92 + 0.08 * n3);
            }
            else if (x < c0 + 1)
            {
                c = SeaSand.Mix(SeaDeep, deep) * (0.94 + 0.06 * Math.Sin((c0 - x) / 6.5 + n2 * 4));
                c = c.Mix(SeaWeed, Geo.S((Weed.Fbm(x / 140, z / 140, 3) - 0.15) / 0.1) * Geo.S((-h - 5) / 6) * (1 - Geo.S((-h - 35) / 10)) * 0.7);
            }
            else c = MudDark.Mix(Mud, n3 * 0.5 + 0.3);                                   // a flooded crater's floor
            return c * (shade * (1 + 0.03 * grain));
        }

        // ---- The beach: wet sand at the water's edge with the surf's foam, dry sand above, the wrack line.
        if (x < b0 && kind is Terrain.Ground.Beach or Terrain.Ground.Sea)
        {
            double u = (x - c0) / Math.Max(1, b0 - c0);
            var dry = DrySand.Mix(PaleSand, Geo.S((n2 + 0.1) / 0.3) * 0.5) * (0.95 + 0.05 * Math.Sin((x * 0.8 + z * 0.3) / 3.2 + n3 * 5));
            var wet = WetSand * (0.95 + 0.05 * Math.Sin((x - z * 0.2) / 5 + n3 * 3));
            c = wet.Mix(dry, Geo.S((u - 0.4 + 0.06 * n2) / 0.08));
            double lace = Lace.Fbm(x / 9, z / 28, 3);
            c = c.Mix(Foam, (1 - Geo.S(u / 0.035)) * Geo.S((lace + 0.05) / 0.2) * 0.75);
            double wrackAt = 0.72 + 0.05 * Layout.Wave(z, 61, 1, 900);
            c = c.Mix(Wrack * (0.9 + 0.2 * fine), Layout.Bump((u - wrackAt) / 0.03) * Geo.S((Weed.At(x / 6, z / 6) + 0.1) / 0.3) * 0.8);
            c = c.Mix(Shingle * (0.85 + 0.25 * fine), hd[iz] * 0.9);
            return Finish(c, x, z, h, sl, shade, grain, n3);
        }

        switch (kind)
        {
            case Terrain.Ground.Cliff:
                c = Granite.Mix(Lichen, Geo.S((n3 - 0.35) / 0.1) * 0.4) * (0.85 + 0.25 * fine);
                c = c.Mix(Shingle, 1 - Geo.S((h - 8) / 10));
                break;
            case Terrain.Ground.Heath:
            {
                // Heather and grass in a fine mosaic, bracken in drifts, gorse in clumps, bare granite where it rises steeply.
                double mosaic = Tuft.Fbm(x / 26, z / 26, 3) + 0.3 * n2;
                c = Heather.Mix(HeathGreen, Geo.S((mosaic + 0.05) / 0.25)) * (0.9 + 0.15 * fine);
                c = c.Mix(Bracken * (0.9 + 0.15 * fine), Geo.S((n1 - 0.3) / 0.12) * 0.6);
                c = c.Mix(Gorse * (0.85 + 0.2 * fine), Geo.S((Weed.Fbm(x / 18, z / 18, 2) - 0.45) / 0.08) * 0.7);
                if (fine > 0.8) c = c.Mix(new C(138, 104, 124), 0.2);                                   // heather in flower
                c = c.Mix(Granite.Mix(Lichen, 0.3) * (0.9 + 0.15 * fine), Geo.S((sl - 10) / 8));
                break;
            }
            case Terrain.Ground.Foredune:
            {
                double face = 1 - Geo.S((x - b0 - 55) / 30);
                c = Sandy(x, z, n1, n2, n3, fine, 0.75 * (1 - face));
                break;
            }
            case Terrain.Ground.Dunes:
            {
                double dune = F(t.Dune, x, z), slip = F(t.Slip, x, z);
                double bare = Math.Max(slip, Geo.S((dune - 38) / 8));
                // Grey dune between the dunes: marram, moss and lichen over old sand; green slacks only in the wet
                // hollows, rushes in the wettest; the dunes themselves marram-grown but for their slip faces and crests.
                var grey = Sandy(x, z, n1, n2, n3, fine, 0.8).Mix(GreyDune * (0.9 + 0.15 * fine), 0.35 + 0.25 * Geo.S((n1 + 0.1) / 0.3));
                var wet = Slack.Mix(Rush, Geo.S((n2 - 0.25) / 0.12) * 0.8) * (0.9 + 0.14 * fine);
                c = grey.Mix(wet, Geo.S((n2 - 0.25) / 0.15) * (1 - Geo.S((h - Layout.Slack - 0.5) / 2)) * 0.45);
                if (dune > 2) c = c.Mix(Sandy(x, z, n1, n2, n3, fine, 0.75 * (1 - bare)), Geo.S((dune - 2) / 4));
                // Pine litter under the back-dune pines, along the rear ridge.
                double rear = x - fa[iz];
                c = c.Mix(PineLitter * (0.9 + 0.2 * fine), Layout.Bump((rear + 570) / 220) * Geo.S((n1 + 0.2) / 0.3) * 0.6);
                break;
            }
            case Terrain.Ground.SandyEnd:
                c = Sandy(x, z, n1, n2, n3, fine, 0.6 + 0.3 * Geo.S((n1 + 0.1) / 0.3)).Mix(GreyDune * (0.9 + 0.15 * fine), 0.25 * Geo.S((n2 + 0.1) / 0.3))
                     .Mix(Saltmarsh * (0.9 + 0.15 * fine), Geo.S((n1 - 0.3) / 0.15) * 0.5);
                c = c.Mix(Field(x, z, ix, iz, n2, n3, fine), Geo.S((x - fb[iz] + 100) / 400));
                break;
            case Terrain.Ground.Rise:
                c = Field(x, z, ix, iz, n2, n3, fine);
                break;
            default:
            {
                // The trench lines and the zone: churned earth and shelled grass, more mud the nearer the craters.
                double crater = F(t.Crater, x, z);
                double shelled = kind == Terrain.Ground.Meadow ? 0.55 : 0.35;
                var grass = Turf.Mix(TuftC, Geo.S((Tuft.At(x / 7, z / 7) - 0.1) / 0.3) * 0.6) * (0.9 + 0.15 * fine);
                var mud = Earth.Mix(EarthDark, Geo.S((n3 + 0.1) / 0.3) * 0.5) * (0.92 + 0.1 * fine);
                c = grass.Mix(mud, shelled + 0.3 * Geo.S((n2 + 0.05) / 0.25) + 0.4 * Geo.S((crater + 0.5) / 0.4));
                c = c.Mix(Chalk * (0.9 + 0.2 * fine), Layout.Bump((crater + 0.15) / 0.18) * Geo.S((fine - 0.2) / 0.3) * 0.5);   // thrown-out chalk on the rims
                if (crater > 0) c = c.Mix(MudDark, Geo.S(crater / 0.3) * 0.7);
                if (kind == Terrain.Ground.LineB && x > fb[iz] + 200) c = c.Mix(Field(x, z, ix, iz, n2, n3, fine), Geo.S((x - fb[iz] - 200) / 220));
                if (kind == Terrain.Ground.LineA && x < fa[iz] - 200) c = c.Mix(Sandy(x, z, n1, n2, n3, fine, 0.55), Geo.S((fa[iz] - 200 - x) / 220));
                break;
            }
        }

        // ---- The trenches: sandbagged parapets, spoil, revetted walls, duckboards on the floors.
        double trench = F(t.Trench, x, z);
        if (runA[iz] is { } ra) c = Parapets(c, x - (fa[iz] + Terrain.Crenel(z, ra.Z0)), z, fine, n3);
        if (runB[iz] is { } rb) c = Parapets(c, fb[iz] + Terrain.Crenel(z, rb.Z0) - x, z, fine, n3);
        if (trench > 2)
        {
            var wall = Revet.Mix(Plank, Math.Abs(Math.Sin((x + z) / 3.1)) * 0.5) * (0.8 + 0.2 * fine);
            var floor = (Math.Sin((x - z) * 0.9) > -0.2 ? Plank * (0.85 + 0.2 * fine) : MudDark);
            c = c.Mix(EarthDark, Geo.S(trench / 6)).Mix(wall, Geo.S((sl - 28) / 10) * 0.8).Mix(floor, Geo.S((trench - 24) / 4) * Geo.S((20 - sl) / 10));
        }

        // ---- The inlet's banks: wet mud at the water, reeds and salt marsh on the north bank in the dunes, clean sand round the cove.
        if (inlet < 140)
        {
            double cove = Geo.S((x - Layout.CoveFrom + 200) / 300);
            var bank = Mud.Mix(WetSand, 0.3 + 0.4 * cove).Mix(PaleSand, Geo.S((inlet - 20) / 40) * cove);
            c = c.Mix(bank * (0.92 + 0.1 * fine), 1 - Geo.S((inlet - 20 - 50 * cove) / 50));
            if (x < 6000 && x > 2900 && inlet > 10)
                c = c.Mix(Reeds.Mix(Saltmarsh, Geo.S((n3 + 0.1) / 0.3)) * (0.85 + 0.25 * fine), Geo.S((Weed.Fbm(x / 60, z / 60, 2) + 0.1) / 0.2) * (1 - Geo.S((inlet - 90) / 40)) * 0.8);
        }
        return Finish(c, x, z, h, sl, shade, grain, n3);
    }

    /// <summary>Sand with marram over it (0 bare to 1 thick cover), the grass in clumps with its pale, wind-sorted sand between.</summary>
    static C Sandy(double x, double z, double n1, double n2, double n3, double fine, double cover)
    {
        var sand = DrySand.Mix(PaleSand, Geo.S((n2 + 0.1) / 0.3) * 0.6) * (0.95 + 0.05 * Math.Sin((x * 0.7 + z * 0.5) / 3.6 + n3 * 4));
        double clump = Tuft.Fbm(x / 8, z / 8, 3) + 0.25 * n1 + 0.2 * n2;
        var grass = Marram.Mix(MarramDark, Geo.S((fine + 0.2) / 0.5) * 0.6) * (0.9 + 0.15 * fine);
        return sand.Mix(grass, Geo.S((clump - (0.35 - 0.7 * cover)) / 0.12) * 0.85);
    }

    /// <summary>A fire trench's parapet (sandbags) and parados (spoil), by the signed distance from its line.</summary>
    static C Parapets(C c, double d, double z, double fine, double n3)
    {
        if (d > 84 && d < 118)
        {
            double bag = Math.Abs(((z + (Math.Floor((d - 84) / 9) % 2) * 6) % 12 + 12) % 12 - 6) / 6;
            c = c.Mix(Burlap * (0.6 + 0.45 * Math.Sqrt(1 - bag * bag)) * (0.9 + 0.15 * fine), 0.9 * Layout.Bump((d - 101) / 20));
        }
        if (d > -72 && d < -30) c = c.Mix(EarthDark.Mix(Earth, 0.5 + 0.5 * n3), 0.6 * Layout.Bump((d + 52) / 24));
        if (d > 110 && d < 200) c = c.Mix(Earth * (0.9 + 0.15 * fine), 0.35 * (1 - Geo.S((d - 110) / 90)));                 // spoil thrown forward
        return c;
    }

    /// <summary>The hill's ground: each field its own crop, the hedgerows' dark banks, sunken lanes, the village's rubble.</summary>
    C Field(double x, double z, int ix, int iz, double n2, double n3, double fine)
    {
        int f = Nearest(t.Field, x, z);
        double k = f < 0 ? 0.3 : Layout.Hash01(f, 81);
        double angle = (f < 0 ? 0 : Layout.Hash01(f, 82)) * Math.PI;
        double along = x * Math.Cos(angle) + z * Math.Sin(angle);
        C c;
        if (k < 0.4) c = Pasture.Mix(Rough, Geo.S((n2 + 0.1) / 0.3) * 0.5) * (0.9 + 0.12 * fine);                        // pasture
        else if (k < 0.6) c = Hay * (0.92 + 0.08 * Math.Sin(along / 5)) * (0.94 + 0.1 * fine);                            // hay, mown in swaths
        else if (k < 0.78) c = Plough * (0.85 + 0.18 * (Math.Sin(along / 2.6) * 0.5 + 0.5)) * (0.94 + 0.1 * fine);         // ploughed in furrows
        else if (k < 0.9) c = Stubble.Mix(Hay, 0.3) * (0.9 + 0.1 * Math.Sin(along / 3.2)) * (0.95 + 0.08 * fine);          // stubble
        else c = Rough.Mix(Gorse, Geo.S((n3 - 0.2) / 0.1) * 0.6) * (0.9 + 0.15 * fine);                                     // rough grazing, gorse
        double hedge = F(t.Hedge, x, z);
        c = c.Mix(HedgeShade * (0.85 + 0.2 * fine), (1 - Geo.S((hedge - 10) / 10)) * 0.85);
        double lane = F(t.Lane, x, z);
        if (lane < 26)
            c = c.Mix(LaneEarth * (0.9 + 0.15 * fine), 1 - Geo.S((lane - 18) / 6)).Mix(Pasture * 0.9, (1 - Geo.S((lane - 2.5) / 2)) * 0.7)
                 .Mix(EarthDark, Layout.Bump((lane - 8) / 2.5) * 0.4);                                                      // a grass crown, ruts either side
        double v = (new P(x, z) - Layout.Village).Length;
        if (v < 460) c = c.Mix(Rubble.Mix(Cobble, Geo.S((n3 + 0.1) / 0.3)) * (0.85 + 0.2 * fine), (1 - Geo.S((v - 340) / 120)) * (0.4 + 0.4 * Geo.S((n2 + 0.1) / 0.2)));
        return c;
    }

    C Finish(C c, double x, double z, double h, double sl, double shade, double grain, double n3)
    {
        // Roads: sand tracks behind the beach, old cobbled roads across the zone, broken and cratered.
        double track = F(t.Track, x, z);
        if (track < 30)
            c = c.Mix(TrackSand * (0.92 + 0.08 * Math.Sin((x + z) / 2.2)), 1 - Geo.S((track - 22) / 8)).Mix(new C(170, 150, 112), Layout.Bump((track - 9) / 3) * 0.5);
        double old = F(t.OldRoad, x, z);
        if (old < 30)
        {
            bool sett = ((int)Math.Floor(x / 7) + (int)Math.Floor(z / 7)) % 2 == 0;
            var road = Cobble * (sett ? 0.95 : 0.82);
            c = c.Mix(road, (1 - Geo.S((old - 20) / 8)) * Geo.S((n3 + 0.25) / 0.2) * (1 - Geo.S((F(t.Crater, x, z) + 0.3) / 0.3)));
        }
        // Metal spots and the vents.
        foreach (var (at, _, _) in Layout.Spots)
        {
            double d = Math.Abs(at.X - x) + Math.Abs(at.Z - z);
            if (d > 90) continue;
            d = (new P(x, z) - at).Length;
            c = c.Mix(new C(96, 88, 76), 0.7 * (1 - Geo.S((d - 30) / 30)) * Geo.S((d - 18) / 12));
            c = c.Mix(new C(46, 42, 38) * (1 + 0.12 * grain), 1 - Geo.S((d - 24) / 8));
        }
        foreach (var (_, at) in Layout.Vents)
        {
            double d = (new P(x, z) - at).Length;
            if (d < 90) c = c.Mix(new C(220, 200, 140), 0.5 * (1 - Geo.S((d - 34) / 40)) * Geo.S((d - 20) / 14)).Mix(new C(30, 30, 32), 1 - Geo.S((d - 28) / 8));
        }
        // Scorched ground round the wrecks.
        foreach (var (_, at, _) in Layout.Wrecks)
        {
            double d = Math.Abs(at.X - x) + Math.Abs(at.Z - z);
            if (d < 220) c = c.Mix(new C(52, 46, 40), 0.5 * (1 - Geo.S(((new P(x, z) - at).Length - 50) / 90)) * Geo.S((n3 + 0.3) / 0.3));
        }
        return c * (shade * (1 + 0.04 * grain));
    }
}

static class Derive
{
    static readonly Noise N1 = new(91), N2 = new(92);
    static byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    /// <summary>The splat distribution for All That Simmers' detail textures (red rock, green gravel, blue ground, alpha
    /// grass): rock on the cliffs and steep faces; gravel on the shingle, the tracks and the old roads; ground on sand,
    /// mud and the trenches; grass on the fields, the slacks, the heath and the marram.</summary>
    public static void Splat(Surface s, Terrain.Result t, int size, string path)
    {
        var img = new RgbaImage(size, size);
        double unit = Layout.Size / size;
        Parallel.For(0, size, py =>
        {
            for (int px = 0; px < size; px++)
            {
                double x = (px + 0.5) * unit, z = (py + 0.5) * unit;
                double y = s.At(s.H, x, z), sl = s.At(s.Slope, x, z);
                var kind = t.Kind[Math.Clamp((int)Math.Round(z / 8), 0, t.N - 1) * t.N + Math.Clamp((int)Math.Round(x / 8), 0, t.N - 1)];
                double n1 = N1.Fbm(x / 300, z / 300, 3), n2 = N2.Fbm(x / 90 + 40, z / 90, 3);
                double rock = Geo.S((sl - 34 + 8 * n2) / 16) + (kind == Terrain.Ground.Cliff ? 0.8 : 0);
                double gravel = kind == Terrain.Ground.Beach && Layout.Headland(z) > 0.3 ? 0.8 : 0;
                double road = Math.Min(Terrain.Sample(t.Track, t.N, new P(x, z)), Terrain.Sample(t.OldRoad, t.N, new P(x, z)));
                gravel += 0.8 * (1 - Geo.S((road - 16) / 12));
                double grass = kind switch
                {
                    Terrain.Ground.Rise or Terrain.Ground.Heath => 0.9,
                    Terrain.Ground.Dunes => Terrain.Sample(t.Dune, t.N, new P(x, z)) > 2 ? 0.25 : 0.8,
                    Terrain.Ground.Foredune or Terrain.Ground.SandyEnd => 0.25,
                    Terrain.Ground.LineA or Terrain.Ground.LineB => 0.4,
                    Terrain.Ground.Meadow => 0.3 + 0.2 * n1,
                    _ => 0,
                };
                double ground = Math.Max(0, 1 - rock - grass - gravel) * (y < 0 ? 0.5 : 1);
                double sum = Math.Max(1, rock + gravel + ground + grass);
                img.SetPixel(px, py, new Rgba(B(255 * rock / sum), B(255 * gravel / sum), B(255 * ground / sum), B(255 * grass / sum)));
            }
        });
        img.FlipVertical(); // map .dds files are stored upside down (PLAN.md rule 7)
        DdsFile.Create(DdsFormat.Dxt5, img, true).Save(path);
    }

    /// <summary>Shine: the wet sand at the water's edge and the inlet's mud glisten; the land is dull.</summary>
    public static void Specular(Surface s, Terrain.Result t, int size, string path)
    {
        var img = new RgbaImage(size, size);
        double unit = Layout.Size / size;
        Parallel.For(0, size, py =>
        {
            for (int px = 0; px < size; px++)
            {
                double x = (px + 0.5) * unit, z = (py + 0.5) * unit;
                double y = s.At(s.H, x, z), sl = s.At(s.Slope, x, z);
                double wet = y >= 0 ? 1 - Geo.S(y / 5) : 0, under = y < 0 ? 1 : 0;
                double rock = Geo.S((sl - 30) / 20);
                double land = 1 - wet - under;
                double a = land * (40 + 50 * rock) + wet * 150 + under * 110;
                img.SetPixel(px, py, new Rgba(B(wet * 40), B(wet * 40), B(wet * 36), B(a)));
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

    /// <summary>A sky cube (A8R8G8B8, six faces): a clear coastal sky overhead, sea haze at the horizon, high cloud.</summary>
    public static void Sky(string path, (double R, double G, double B) top, (double R, double G, double B) horizon, int size = 512)
    {
        var face = new RgbaImage(size, size);
        var probe = DdsFile.Create(DdsFormat.Uncompressed, face, false).ToBytes();
        int headerBytes = probe.Length - size * size * 4;
        var bytes = new byte[headerBytes + 6 * size * size * 4];
        probe.AsSpan(0, headerBytes).CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(108), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(108)) | 0x8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(112), 0x200 | 0xFC00);
        var cloud = new Noise(77);
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
                    double up = dir.Y / len, tt = Math.Pow(Math.Abs(up), 0.55);
                    double r = horizon.R + (top.R - horizon.R) * tt, g = horizon.G + (top.G - horizon.G) * tt, b = horizon.B + (top.B - horizon.B) * tt;
                    if (up > 0.05)
                    {
                        // High thin cloud, streaked by the wind off the sea.
                        double px = dir.X / len / up, pz = dir.Z / len / up;
                        double k = Geo.S((cloud.Fbm(px * 1.2, pz * 3.5, 5) - 0.05) / 0.35) * Geo.S((up - 0.05) / 0.25) * 0.55;
                        r += (0.96 - r) * k; g += (0.96 - g) * k; b += (0.97 - b) * k;
                    }
                    int o = headerBytes + (fc * size * size + ty * size + tx) * 4;
                    bytes[o] = B(b * 255); bytes[o + 1] = B(g * 255); bytes[o + 2] = B(r * 255); bytes[o + 3] = 255;
                }
            });
        File.WriteAllBytes(path, bytes);
    }
}
