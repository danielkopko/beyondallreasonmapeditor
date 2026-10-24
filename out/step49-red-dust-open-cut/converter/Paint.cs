// The ground's texture, one texel a game unit, and the detail maps and sky. The outback is red pindan with spinifex
// speckle, gravel lag and the odd calcrete patch; creeks are pale sand; breakaways a dark ferricrete cap over white
// kaolin scarps. The pit's walls are banded by bench as the ground changes with depth: red oxide at the top, white
// and pink kaolin, ochre and purple clays, grey-green fresh rock at the bottom; faces darker and streaked, berms
// lighter with rubble. Haul roads are compacted, rutted, edged with windrows; the pads concrete and oil-stained; the
// tailings dam a cracked pale beach round its dried-out decant (cracked mud ringed with salt: the map's one water level is
// the pit's pool, so the dam holds none); the dumps red oxide waste in tip lines, their tops half
// paddock-dumped (each load its own colour) and half dozed. Round the machines the ground is oily. The railway runs
// on grey ballast over a pale formation, concrete under the loadout and round the shed; a service track runs under
// the conveyor; the hardstands are gravel; the pit floor's north end is marked out in a fresh drill pattern. On the
// wild ground, the washes' pale gravel beds with darker banks, and the mounds' tops wind-sorted sand.
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
                Shade[z * w + x] = (float)(0.8 + 0.32 * Math.Clamp((-dx * lx + ly - dz * lz) / len, 0, 1));
            }
        });
    }

    float V(int x, int z) => H[Math.Clamp(z, 0, W - 1) * W + Math.Clamp(x, 0, W - 1)];
    public double At(float[] f, double x, double z) => Terrain.Sample(f, W, new P(x, z));
}

/// <summary>Fields on a 16-unit grid the painter reads: roads (and the pit's and dumps' ramps), creeks, oil round the
/// machines, the tracks, the conveyor's line.</summary>
sealed class Fields
{
    public const int Cell = 16;
    public readonly int N;
    public readonly float[] Road, Creek, Oil, Rail, Belt;

    public Fields(Terrain.Result t, IEnumerable<Place.Item> machines)
    {
        N = (int)(Layout.Size / Cell) + 1;
        Road = new float[N * N]; Creek = new float[N * N]; Oil = new float[N * N]; Rail = new float[N * N]; Belt = new float[N * N];
        var spots = machines.Select(m => m.At).ToList();
        var roads = Terrain.RoadLines.Concat(t.Ramps).Concat(Terrain.DumpRampLines).ToArray();
        Parallel.For(0, N, z =>
        {
            for (int x = 0; x < N; x++)
            {
                var p = new P(x * Cell, z * Cell);
                int i = z * N + x;
                Road[i] = (float)Terrain.ToLines(p, roads);
                Creek[i] = (float)Terrain.ToLines(p, Terrain.CreekLines);
                Rail[i] = (float)Terrain.ToLines(p, t.Rails);
                Belt[i] = (float)Geo.ToPolyline(p, Terrain.ConveyorLine, out _);
                double near = spots.Count == 0 ? 9999 : spots.Min(s => (s - p).Length);
                Oil[i] = (float)(1 - Geo.S((near - 40) / 160));
            }
        });
    }

    public double At(float[] f, double x, double z)
    {
        double fx = Math.Clamp(x / Cell, 0, N - 1.001), fz = Math.Clamp(z / Cell, 0, N - 1.001);
        int ix = (int)fx, iz = (int)fz;
        double tx = fx - ix, tz = fz - iz;
        return (f[iz * N + ix] * (1 - tx) + f[iz * N + ix + 1] * tx) * (1 - tz) + (f[(iz + 1) * N + ix] * (1 - tx) + f[(iz + 1) * N + ix + 1] * tx) * tz;
    }
}

readonly record struct C(double R, double G, double B)
{
    public static C operator *(C a, double k) => new(a.R * k, a.G * k, a.B * k);
    public C Mix(C o, double t) { t = Math.Clamp(t, 0, 1); return new(R + (o.R - R) * t, G + (o.G - G) * t, B + (o.B - B) * t); }
}

sealed class Painter(Surface s, Terrain.Result t, Fields f)
{
    static readonly Noise N1 = new(1001), N2 = new(1002), N3 = new(1003), Fine = new(1004), Crack = new(1005), Streak = new(1006);

    static readonly C Pindan = new(178, 86, 46), PindanDark = new(146, 66, 36), PindanLight = new(198, 112, 64), Gravel = new(118, 70, 48);
    static readonly C Spinifex = new(164, 146, 80), Calcrete = new(214, 198, 168), Sand = new(222, 196, 150);
    static readonly C Ferricrete = new(104, 50, 32), Kaolin = new(226, 214, 198), KaolinPink = new(212, 168, 158);
    static readonly C Oxide = new(170, 72, 40), Ochre = new(198, 156, 88), Purple = new(140, 112, 122), Fresh = new(112, 116, 106), FreshDark = new(86, 92, 88);
    static readonly C PitFloor = new(124, 120, 110), Wet = new(80, 84, 78), LakeBed = new(70, 106, 94);
    static readonly C RoadC = new(198, 152, 100), Rut = new(160, 112, 72), Windrow = new(140, 76, 48);
    static readonly C Concrete = new(170, 164, 152), OilC = new(56, 52, 48), Line = new(222, 182, 46);
    static readonly C Tailings = new(214, 208, 196), TailingsDark = new(170, 166, 158), DecantMud = new(150, 132, 112), Salt = new(236, 234, 226);
    static readonly C Ballast = new(124, 118, 110), Formation = new(184, 150, 116), Gravel2 = new(168, 140, 112);
    static readonly C RubbleGrey = new(120, 116, 110), RomC = new(156, 106, 78), Ore = new(122, 62, 46), OreLight = new(168, 98, 66);
    static readonly C DumpRed = new(170, 82, 48), DumpOchre = new(192, 132, 80), Buff = new(206, 178, 140), FreshGrey = new(142, 138, 130);

    static byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

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

    static double Ellipse(P p, P c, double a, double b) => Math.Sqrt((p.X - c.X) * (p.X - c.X) / (a * a) + (p.Z - c.Z) * (p.Z - c.Z) / (b * b));

    C Colour(double x, double z, int ix, int iz)
    {
        var p = new P(x, z);
        double h = s.At(s.H, x, z), sl = s.At(s.Slope, x, z), shade = s.At(s.Shade, x, z);
        double depth = Terrain.Sample(t.Depth, t.N, p);
        double n1 = N1.Fbm(x / 700, z / 700, 3), n2 = N2.Fbm(x / 150, z / 150, 3), n3 = N3.Fbm(x / 40, z / 40, 2), fine = Fine.At(x / 7, z / 7);
        double grain = Grain(ix, iz);

        // The outback.
        var c = Pindan.Mix(PindanDark, Geo.S((-n1 + 0.1) / 0.3) * 0.6).Mix(PindanLight, Geo.S((n2 - 0.15) / 0.2) * 0.5);
        c = c.Mix(Gravel, Geo.S((n3 - 0.25) / 0.1) * 0.5);
        if (fine > 0.55) c = c.Mix(Spinifex, 0.55 * Geo.S((n1 + 0.3) / 0.4));                      // spinifex speckle
        c = c.Mix(Calcrete, Geo.S((n2 - 0.42) / 0.06) * 0.7);
        c *= 0.94 + 0.08 * fine;

        // The washes: a pale bed of sand and pebbles, ripples along it, a darker edge where the flow last ran; the
        // mounds' tops paler, wind-sorted sand, their flanks gravelly.
        double wash = Terrain.Sample(t.Wash, t.N, p);
        if (wash > 0.01)
        {
            var bed = Sand.Mix(Gravel, Geo.S((n3 + 0.1) / 0.3) * 0.35) * (0.9 + 0.1 * Math.Sin((x - z) / 7 + n2 * 6)) * (0.94 + 0.1 * fine);
            if (grain > 0.86) bed *= 0.7;                                                          // pebbles
            c = c.Mix(PindanDark * 0.92, Geo.S(wash / 0.25) * (1 - Geo.S((wash - 0.55) / 0.2)) * 0.5);
            c = c.Mix(bed, Geo.S((wash - 0.45) / 0.3));
        }
        double rise = Terrain.Sample(t.Mound, t.N, p);
        if (rise > 3 && sl < 20) c = c.Mix(PindanLight * (0.97 + 0.06 * fine), Geo.S((rise - 4) / 10) * 0.35).Mix(Gravel, Geo.S((sl - 6) / 6) * 0.25);

        // Creeks: pale sand beds, ripples.
        double creek = f.At(f.Creek, x, z);
        c = c.Mix(Sand * (0.92 + 0.08 * Math.Sin((x + z) / 9 + n3 * 4)), (1 - Geo.S((creek - 50) / 50)) * Geo.S((Layout.Plain + 30 - h) / 15));

        // Steep natural ground (breakaway scarps, embankments): white kaolin under a dark cap; a pit ramp's cutting red. Not
        // on the dumps or their ramps' cut sides (waste rock, painted below), nor the magazine's earth bund.
        if (depth <= 0 && sl > 24 && (p - Layout.Magazine).Length > 220
            && Enumerable.Range(0, Layout.Dumps.Length).All(k => Terrain.DumpRho(p, k) > 1.15) && Terrain.ToLines(p, Terrain.DumpRampLines) > 200)
        {
            double scarp = Geo.S((sl - 24) / 12);
            var face = Kaolin.Mix(KaolinPink, Geo.S((n2 + 0.1) / 0.2) * 0.6).Mix(Ferricrete, Geo.S((h - Layout.Plain - 40) / 20));
            // A pit ramp's cutting, through the same red oxide as the pit's top bench.
            if (Terrain.ToLines(p, t.Ramps) < Layout.RampHalf + 60) face = Oxide.Mix(PindanDark, Geo.S((n2 + 0.2) / 0.3) * 0.4);
            c = c.Mix(face * (0.9 + 0.12 * Streak.At(x / 6, z / 60)), scarp * 0.85);
        }

        // Breakaway tops: the dark ferricrete cap, ironstone gravel on it.
        for (int k = 0; k < Layout.Breakaways.Length; k++)
        {
            double height = Layout.Breakaways[k].Height;
            if (Terrain.BreakawayRho(p, k) > 1.1) continue;
            double top = Geo.S((h - Layout.Plain - height * 0.55) / (height * 0.25));
            var cap = Ferricrete.Mix(Gravel, Geo.S((n3 + 0.1) / 0.3) * 0.5).Mix(PindanDark, Geo.S((n2 + 0.2) / 0.4) * 0.4) * (0.9 + 0.15 * fine);
            c = c.Mix(cap, top * Geo.S((24 - sl) / 8) * 0.9);
        }

        // The tailings dam: a cracked pale beach; its decant dried out: rings of white salt crust where the water stood,
        // and in the middle dark mud cracked into plates.
        if (x > Layout.TsfX0 + 60 && x < Layout.TsfX1 - 60 && z > Layout.TsfZ0 + 60 && z < Layout.TsfZ1 - 60 && h > Layout.Plain + 20 && sl < 10)
        {
            double crack = Math.Abs(Crack.Fbm(x / 26, z / 26, 2));
            var beach = Tailings.Mix(TailingsDark, Geo.S((n2 + 0.2) / 0.4) * 0.5);
            if (crack < 0.035) beach *= 0.72;
            double rho = Ellipse(p, Layout.Decant, Layout.DecantA, Layout.DecantB) + 0.12 * N2.Fbm(x / 160, z / 160, 2);
            beach = beach.Mix(TailingsDark * 0.85, (1 - Geo.S((rho - 1) / 0.35)) * 0.5);
            double ring = 0.5 + 0.5 * Math.Sin(rho * 38 + n3 * 2);                                 // old shorelines
            var salt = Salt.Mix(TailingsDark, ring * 0.35) * (0.95 + 0.05 * fine);
            beach = beach.Mix(salt, (1 - Geo.S((rho - 1.05) / 0.08)) * Geo.S((rho - 0.45) / 0.15));
            double plates = Math.Abs(Crack.Fbm(x / 14, z / 14, 2));
            var mud = DecantMud * (0.9 + 0.12 * n3) * (plates < 0.06 ? 0.6 : 1);
            mud = mud.Mix(Salt, Geo.S((n3 - 0.45) / 0.1) * 0.5);                                  // salt blooms on the mud
            c = beach.Mix(mud, 1 - Geo.S((rho - 0.5) / 0.12));
        }

        // The dumps: red oxide waste, tip lines down the batters; on the top lift each paddock-dumped load its own colour
        // (oxide, ochre, buff, grey fresh rock), the dozed half in blade lines.
        for (int k = 0; k < Layout.Dumps.Length; k++)
        {
            double rho = Terrain.DumpRho(p, k);
            if (rho > 1.12) continue;
            var (at, _, _, turn) = Layout.Dumps[k];
            double tip = 0.5 + 0.5 * Math.Sin(Math.Atan2(z - at.Z, x - at.X) * 140 + n3 * 3);
            double batter = Geo.S((sl - 14) / 10);
            var waste = DumpRed.Mix(DumpOchre, Geo.S((n2 + 0.05) / 0.25) * 0.6).Mix(RubbleGrey, Geo.S((n1 - 0.2) / 0.2) * 0.4);
            waste *= 0.9 + 0.16 * tip * batter + 0.08 * fine;
            if (h > Layout.Plain + 1.5 * Layout.Lift && batter < 1)
            {
                double heap = Terrain.Heap(p, out double tone);
                var load = tone < 0.3 ? DumpRed : tone < 0.55 ? DumpOchre : tone < 0.78 ? Buff : FreshGrey;
                double tr = turn * Math.PI / 180, along = (x - at.X) * Math.Cos(tr) + (z - at.Z) * Math.Sin(tr);
                var top = waste.Mix(DumpOchre, 0.25) * (0.96 + 0.05 * Math.Sin(along / 6));
                top = top.Mix(load * (0.9 + 0.1 * fine), Geo.S(heap / 0.25));
                waste = waste.Mix(top, 1 - batter);
            }
            c = c.Mix(waste, Geo.S((1.06 - rho) / 0.08) * (h > Layout.Plain + 4 ? 1 : 0.4));
        }

        // The pads: the ROM pad and its stockpiles, the plant pad and the workshop's hardstand.
        double rom = Terrain.PadRho(p, Layout.RomPad, Layout.RomA, Layout.RomB);
        if (rom < 1.1)
        {
            var pad = RomC.Mix(Ore, Geo.S((n2 + 0.1) / 0.2) * 0.3) * (0.92 + 0.1 * fine);
            foreach (var sp in Layout.Stockpiles)
            {
                double streak = 0.5 + 0.5 * Math.Sin(Math.Atan2(z - sp.Z, x - sp.X) * 23 + n3 * 4);
                var pile = Ore.Mix(OreLight, streak * 0.7) * (0.9 + 0.12 * fine);
                pad = pad.Mix(pile, 1 - Geo.S(((sp - p).Length - 160) / 40));
            }
            c = c.Mix(pad, 1 - Geo.S((rom - 0.96) / 0.06));
        }
        foreach (var (at, a, b) in new[] { (Layout.PlantPad, Layout.PlantA, Layout.PlantB), (Layout.Workshop, Layout.WorkshopA, Layout.WorkshopB) })
        {
            double r = Terrain.PadRho(p, at, a, b);
            if (r > 1.1) continue;
            bool joint = (x % 64) < 2 || (z % 64) < 2;
            var slab = Concrete * (joint ? 0.82 : 0.95 + 0.08 * Grain((int)(x / 64), (int)(z / 64)));
            slab = slab.Mix(OilC, Geo.S((n3 - 0.35) / 0.1) * 0.6);
            if (at == Layout.Workshop && Math.Abs(x - (at.X - 200)) % 110 < 3 && Math.Abs(z - at.Z) < 160) slab = Line;   // the go-line's bays
            c = c.Mix(slab, 1 - Geo.S((r - 0.96) / 0.06));
        }

        // The hardstands: levelled gravel, oil-stained.
        foreach (var (at, a, b) in Layout.Hardstands)
        {
            double r = Terrain.PadRho(p, at, a, b);
            if (r > 1.15) continue;
            var hs = Gravel2.Mix(Concrete, Geo.S((n2 + 0.1) / 0.3) * 0.4).Mix(OilC, Geo.S((n3 - 0.4) / 0.1) * 0.5) * (0.93 + 0.08 * fine);
            c = c.Mix(hs, 1 - Geo.S((r - 0.98) / 0.12));
        }
        // The conveyor's service track beneath it.
        double belt = f.At(f.Belt, x, z);
        if (belt < 40) c = c.Mix(Formation * (0.94 + 0.06 * fine), (1 - Geo.S((belt - 18) / 14)) * 0.55);
        // The railway: the formation, grey ballast under each track, concrete under the loadout and the shed with its apron.
        if (Math.Abs(x - Layout.RailBedX) < Layout.RailBedHalf + 40 && z < t.ShedSouth + 100)
        {
            double edge = Math.Max(Math.Abs(x - Layout.RailBedX) - Layout.RailBedHalf, z - (t.ShedSouth + 60));
            c = c.Mix(Formation.Mix(PindanLight, Geo.S((n2 + 0.2) / 0.4) * 0.3) * (0.93 + 0.08 * fine), 1 - Geo.S(edge / 30));
            double rail = f.At(f.Rail, x, z);
            c = c.Mix(Ballast * (0.86 + 0.2 * (fine * 0.5 + 0.5)) * (0.95 + 0.05 * grain), 1 - Geo.S((rail - 22) / 8));
            bool slab = (Math.Abs(x - Layout.RailX) < 70 && Math.Abs(z - Layout.LoadoutZ) < 70) || (Math.Abs(x - Layout.RailX) < 110 && z > t.ShedNorth - 80 && z < t.ShedSouth + 40);
            if (slab) c = Concrete.Mix(OilC, Geo.S((n3 - 0.3) / 0.1) * 0.5) * ((x % 64) < 2 || (z % 64) < 2 ? 0.84 : 0.96);
        }

        // The pit.
        if (depth > 0)
        {
            double drop = (Layout.Plain - Layout.PitFloor) / Layout.Benches;
            double level = (Layout.Plain - h) / drop;                                   // benches down from the rim
            C band = level < 1.5 ? Oxide.Mix(PindanDark, Geo.S((n2 + 0.2) / 0.3) * 0.4)
                   : level < 3 ? Kaolin.Mix(KaolinPink, Geo.S((n2 + 0.2) / 0.5) * 0.7)
                   : level < 4.5 ? Ochre.Mix(Purple, Geo.S((n2 + 0.2) / 0.5))
                   : level < 6.5 ? Fresh.Mix(Purple, Geo.S((n2 - 0.25) / 0.15) * 0.5)
                   : FreshDark.Mix(Fresh, Geo.S((n2 + 0.1) / 0.3) * 0.5);
            double face = Geo.S((sl - 35) / 15);
            band *= 0.93 + 0.12 * Streak.At(x / 5, z / 5) * face + 0.06 * fine;
            var berm = band.Mix(RubbleGrey, 0.25) * (1.08 + 0.05 * n3);
            var wall = band * (0.86 + 0.1 * Streak.At(x / 4 + z / 9, z / 40));
            c = berm.Mix(wall, face);
            if (depth > Layout.Wall - 2 && h < Layout.PitFloor + 12)
            {
                c = PitFloor.Mix(Fresh, Geo.S((n2 + 0.1) / 0.3) * 0.5) * (0.92 + 0.1 * fine);
                c = c.Mix(Wet, 1 - Geo.S((h - 2) / 8));
                var (fu, fv) = Terrain.Frame(p);
                if (fu > -1660 && fu < -1240 && Math.Abs(fv) < 330)
                {
                    // Holes drilled on a 32 grid, each with its ring of pale cuttings, the rows not yet charged.
                    double gu = fu / 32 - Math.Round(fu / 32), gv = fv / 32 - Math.Round(fv / 32);
                    double hole = Math.Sqrt(gu * gu + gv * gv) * 32;
                    c = c.Mix(new C(170, 164, 150), 1 - Geo.S((hole - 4) / 4)).Mix(new C(40, 38, 36), 1 - Geo.S((hole - 2) / 1.2));
                }
            }
            if (h < 0) c = LakeBed.Mix(Wet, Geo.S((-h - 20) / 20) * 0.6);
        }

        // Roads and ramps: compacted, rutted, windrows at the edges; ending at the pads' and hardstands' edges, never over a stockpile.
        double road = f.At(f.Road, x, z);
        double shown = road < 100 ? Terrain.RoadShown(p) : 0;
        if (shown > 0)
        {
            double rut = Math.Abs(road - 22) < 5 || Math.Abs(road - 48) < 5 ? 1 : 0;
            var surface = RoadC.Mix(Rut, rut * 0.5) * (0.94 + 0.06 * fine);
            double worn = (depth > Layout.Wall - 60 ? Geo.S((h - Layout.PitFloor - 2) / 30) : 1) * shown;   // a ramp's foot fades into the floor
            c = c.Mix(surface, (1 - Geo.S((road - 64) / 10)) * worn);
            c = c.Mix(Windrow, (1 - Geo.S(Math.Abs(road - 76) / 10)) * 0.6 * worn);
        }

        // Oil and churned ground round the machines; the bases cleared.
        c = c.Mix(OilC * (0.9 + 0.2 * n3), f.At(f.Oil, x, z) * Geo.S((n3 + 0.1) / 0.3) * 0.35);
        foreach (var st in Layout.StartsA.Concat(Layout.StartsB))
            c = c.Mix(PindanLight * (0.95 + 0.05 * n3), (1 - Geo.S(((st - p).Length - 420) / 200)) * 0.35);

        // Spots and the vent.
        foreach (var at in Layout.Spots.Select(sp => sp.At).Concat(t.PitSpots.Select(sp => sp.At)))
        {
            double d = (p - at).Length;
            if (d < 62)
            {
                c = c.Mix(new C(150, 96, 60), 0.75 * (1 - Geo.S((d - 30) / 30)) * Geo.S((d - 18) / 12));
                c = c.Mix(new C(52, 46, 40) * (1 + 0.12 * grain), 1 - Geo.S((d - 24) / 8));
            }
        }
        double vd = (p - t.Vent).Length;
        if (vd < 90) c = c.Mix(new C(240, 200, 120), 0.5 * (1 - Geo.S((vd - 34) / 40)) * Geo.S((vd - 20) / 14)).Mix(new C(30, 30, 32), 1 - Geo.S((vd - 28) / 8));
        return c * (shade * (1 + 0.04 * grain));
    }
}

static class Derive
{
    static readonly Noise N1 = new(91), N2 = new(92);
    static byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    static (double, double, double, double) Mix((double R, double G, double B, double A) a, (double R, double G, double B, double A) b, double t) =>
        (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);

    /// <summary>The splat distribution for All That Simmers' detail textures: rock on the pit's faces and the scarps,
    /// gravel and ground on roads, pads and berms, the outback's ground elsewhere, wet at the lake.</summary>
    public static void Splat(Surface s, Fields f, Terrain.Result t, int size, string path)
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
                double rock = Geo.S((sl - 28 + 8 * n2) / 18), dirt = Geo.S((sl - 10 + 6 * n2) / 14) * (1 - rock);
                double wet = 1 - Geo.S((y - 4) / 14);
                double rest = Math.Max(0, 1 - rock - dirt) * (1 - wet);
                rock *= 1 - wet; dirt *= 1 - wet;
                double r = rock * (110 + 70 * n1) + dirt * 72 + wet * (40 + 20 * n2);
                double g = rock * 69 + dirt * 111 + wet * (160 + 60 * n1) + rest * (120 + 40 * n1);
                double b = rock * 22 + dirt * 16 + wet * 12 + rest * (70 + 40 * n2);
                double a = (rock + dirt) * 33;
                double road = f.At(f.Road, x, z);
                if (road < 80) (r, g, b, a) = Mix((r, g, b, a), (20, 150, 70, 0), (1 - Geo.S((road - 50) / 30)) * Terrain.RoadShown(new P(x, z)));
                img.SetPixel(px, py, new Rgba(B(r), B(g), B(b), B(a)));
            }
        });
        img.FlipVertical(); // map .dds files are stored upside down (PLAN.md rule 7)
        DdsFile.Create(DdsFormat.Dxt5, img, true).Save(path);
    }

    public static void Specular(Surface s, int size, string path)
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
                double shore = y >= 0 ? 1 - Geo.S(y / 12) : 0, under = y < 0 ? 1 : 0;
                double land = 1 - shore - under;
                double a = land * (50 * (1 - rock - dirt) + 70 * dirt + 96 * rock) + shore * 140 + under * 110;
                img.SetPixel(px, py, new Rgba(B(shore * 30), B(shore * 28), B(shore * 22), B(a)));
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
