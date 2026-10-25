// Marram Line's layout: the design's table (../design/layout.py) in C#. Its lines (the coast, the beach, the foredune,
// the two fire trenches, the crest, the headland and its heath) and the inlet are ported exactly, so the map is built
// where the drawing shows it; everything the table generated (dunes, craters, hedgerows, wire, wrecks, roads, spots,
// the trenches' runs, bunkers, nests, hedgehogs, pines, the village) is read from layout.json, which the build copies
// beside the converter. Game units: x east, z south, 12288 square. Not symmetric: nothing is mirrored.
using System.Text.Json;

static class Layout
{
    public const int Squares = 1536;
    public const double Size = 12288;
    public const float MinH = -64, MaxH = 272;
    const long Seed = 52;

    // ---- Heights (as the table's).
    public const double Berm = 10, Slack = 22, HeadlandTop = 70, TrenchGround = 42, Meadow = 16, Crest = 215, EastEdge = 175, Knoll = 34, SandEnd = 10;
    public const double InletDepth = 26, InletMouthDepth = 32, CoveDepth = 24, CoveFrom = 8300, InletBankDeg = 12, InletShallows = 0.4;
    public const double SupportBack = 240, CrossW = 220, CrossGap = 420, DrawW = 260, PadR = 560;
    public static readonly (double D, double H)[] SeaFloor = [(0, 0), (240, 8), (640, 20), (1100, 40), (1500, 60)];
    public const double BarAt = 400, BarHalf = 90, BarDepth = 5;
    /// <summary>The fire trench's section, from its own rear to its front (toward the enemy): (offset, height over the ground).</summary>
    public static readonly (double D, double H)[] FireSection = [(-88, 0), (-64, 6), (-40, 6), (-6, -32), (34, -32), (52, -12), (72, -12), (90, 6), (114, 6), (178, 0)];
    /// <summary>The support trench: the same floor, no fire step, low spoil both sides.</summary>
    public static readonly (double D, double H)[] SupportSection = [(-84, 0), (-60, 4), (-40, 4), (-6, -32), (34, -32), (68, 4), (88, 4), (112, 0)];
    public static readonly double[] Wire = [150, 260];

    // ---- The table's hash and waves, exactly as layout.py has them.
    public static double Hash01(long k, long salt)
    {
        unchecked
        {
            uint h = (uint)((k * 374761393L + salt * 668265263L + Seed * 2246822519L) & 0xFFFFFFFFL);
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / (double)0x1000000;
        }
    }

    public static double Wave(double t, int seed, double amp, double wavelength, int octaves = 4)
    {
        double s = 0;
        for (int k = 0; k < octaves; k++)
            s += Math.Sin(2 * Math.PI * t / (wavelength / Math.Pow(2.1, k)) + 2 * Math.PI * Hash01(k, seed)) / Math.Pow(1.9, k);
        return amp * s / 1.95;
    }

    public static double Wave2(double x, double z, int seed, double amp, double wavelength) =>
        amp * 0.5 * (Math.Sin(2 * Math.PI * x / wavelength + 6.28 * Hash01(1, seed)) * Math.Sin(2 * Math.PI * z / (wavelength * 1.3) + 6.28 * Hash01(2, seed))
                     + Math.Sin(2 * Math.PI * (x + z) / (wavelength * 0.7) + 6.28 * Hash01(3, seed)));

    public static double Bump(double u) => Math.Abs(u) < 1 ? (1 - u * u) * (1 - u * u) : 0;
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public static double Wobble(double a, long k, double rough) =>
        1 + rough * (Math.Sin(3 * a + 6.28 * Hash01(k, 91)) * 0.55 + Math.Sin(5 * a + 6.28 * Hash01(k, 92)) * 0.3 + Math.Sin(9 * a + 6.28 * Hash01(k, 93)) * 0.15);

    /// <summary>How far into an irregular blob a point is: 1 at its middle, 0 on its outline, negative outside.</summary>
    public static double InsideBlob(double x, double z, double cx, double cz, double rx, double rz, long k, double rough)
    {
        double a = Math.Atan2((z - cz) / rz, (x - cx) / rx);
        return 1 - Math.Sqrt((x - cx) * (x - cx) / (rx * rx) + (z - cz) * (z - cz) / (rz * rz)) / Wobble(a, k, rough);
    }

    // ---- The lines.
    public static double Headland(double z) => Math.Min(1, 1.6 * Bump((z - 1450) / 1750));

    public static double Coast(double z) =>
        1850 + Wave(z, 11, 150, 3400) - (1180 * Bump((z - 1450) / 1750) + Wave(z, 12, 80, 520) * Headland(z)) + 380 * Geo.S((z - 9000) / 1600);

    public static double BeachWidth(double z) => Lerp(520 + Wave(z, 13, 90, 2200), 110, Headland(z));
    public static double BermX(double z) => Coast(z) + BeachWidth(z);
    public static double ForeduneWidth(double z) => 380 + Wave(z, 14, 70, 1700);
    public static double ForeduneCrest(double z) => 52 + Wave(z, 15, 16, 1500);
    public static double FireA(double z) => 5800 + Wave(z, 21, 170, 2600) + 520 * Bump((z - 8300) / 1000);
    public static double FireB(double z) => 7850 + Wave(z, 22, 170, 2600) - 560 * Bump((z - 3600) / 1000);
    public static double CrestX(double z) => 10900 + Wave(z, 23, 300, 3000);
    public static double ZoneMid(double z) => (FireA(z) + FireB(z)) / 2;
    public static readonly (double X, double Z, double Rx, double Rz, long K, double Rough) Heath = (2650, 1500, 2150, 1650, 17, 0.13);
    public static double HeathIn(double x, double z) => InsideBlob(x, z, Heath.X, Heath.Z, Heath.Rx, Heath.Rz, Heath.K, Heath.Rough);

    public static double Rise(double x, double z)
    {
        double fb = FireB(z), cr = CrestX(z), top = Crest + Wave(z, 24, 20, 2500), h;
        // The swell runs on over the crest and down the back slope (stopping at the crest left a step along it).
        double swell = 10 * Wave2(x, z, 25, 1, 1900) * Geo.S((x - fb) / 900);
        if (x <= cr)
            h = TrenchGround + (top - TrenchGround) * Geo.S((x - fb) / (cr - fb));
        else
            h = Lerp(top, EastEdge, (x - cr) / (Size - cr));
        var w = Windmill;
        return h + swell + Knoll * Bump(Math.Sqrt((x - w.X) * (x - w.X) + (z - w.Z) * (z - w.Z)) / 420);
    }

    // ---- The inlet: its smoothed course from the table (x, z, half width) and the distance along it.
    public static (double X, double Z, double W)[] InletPts = [];
    public static double[] InletS = [];

    /// <summary>The inlet near a point: distance to its middle line, how far along it, its half width there (each bank
    /// wavering its own way) and the side (+1 south, -1 north); null when far from it.</summary>
    public static (double D, double S, double W, int Side)? InletAt(double x, double z)
    {
        if (z < 9700) return null;
        double best = double.MaxValue, bs = 0, bw = 0, across = 0;
        int side = 1;
        for (int i = 0; i + 1 < InletPts.Length; i++)
        {
            var (x0, z0, w0) = InletPts[i];
            var (x1, z1, w1) = InletPts[i + 1];
            double dx = x1 - x0, dz = z1 - z0, l2 = dx * dx + dz * dz;
            double t = l2 < 1e-9 ? 0 : Math.Clamp(((x - x0) * dx + (z - z0) * dz) / l2, 0, 1);
            double ex = x - x0 - t * dx, ez = z - z0 - t * dz, d = Math.Sqrt(ex * ex + ez * ez);
            if (d < best)
            {
                best = d;
                bs = InletS[i] + t * Math.Sqrt(l2);
                bw = Lerp(w0, w1, t);
                across = (dx * (z - z0) - dz * (x - x0)) / Math.Sqrt(l2);
                side = across > 0 ? 1 : -1;
            }
        }
        // Each bank wavers its own way; past the cove's end (where neither bank is nearer) the two are blended, so the
        // bank's slope runs on smoothly round the end.
        double south = Geo.S((across / Math.Max(best, 1e-6) + 1) / 2);
        double wobble = Lerp(Wave(bs, 34, 1, 900), Wave(bs, 33, 1, 900), south);
        return (best, bs, bw * (1 + 0.16 * wobble), side);
    }

    public static double InletDepthAt(double x, double s)
    {
        double d = Lerp(InletMouthDepth, InletDepth, Geo.S((s - 1000) / 1000));
        return Lerp(d, CoveDepth, Geo.S((x - CoveFrom) / 300));
    }

    public static bool SouthOfInlet(double x, double z) => InletAt(x, z) is { } a && a.Side > 0 && a.D >= a.W;
    public static bool SandyEnd(double x, double z) => x < FireB(z) + 500 && SouthOfInlet(x, z);
    public static bool InWater(double x, double z, double margin = 0) => InletAt(x, z) is { } a && a.D < a.W + margin;

    // ---- What the table generated.
    public static List<(int N, string Team, P At)> Starts = [];
    public static List<(P At, double Income, string Side)> Spots = [];
    public static List<(string Kind, P At)> Vents = [];
    public static List<(string Kind, List<P> Line)> Roads = [];
    public static List<(P At, double R, double H)> Dunes = [];
    public static List<(P At, double R, double Depth, bool Deep, bool Wet)> Craters = [];
    public static List<(string Side, double Z0, double Z1, double Off)> WireRuns = [];
    public static List<List<P>> Hedges = [], Fields = [];
    public static List<(string Kind, P At, double Heading)> Wrecks = [];
    public static List<P> BrookDry = [];
    public static double[] CrossA = [], CrossB = [], Draws = [];
    public static double EndA, EndB, HeadlandRoadZ;
    public static Dictionary<string, List<(double Z0, double Z1)>> FireRuns = [];
    public static List<(string Side, List<P> Line)> CommTrenches = [];
    public static List<(string Side, P At)> Nests = [];
    public static List<(string Kind, P At, double Face)> Bunkers = [];
    public static List<P> Hedgehogs = [], Orchard = [];
    public static List<(P At, double Size)> Pines = [];
    public static List<(P At, double Turn)> Houses = [];
    public static P Village, Farm, Lighthouse, Battery, Windmill;

    static Layout() => Load(Path.Combine(AppContext.BaseDirectory, "layout.json"));

    static P Pt(JsonElement e) => new(e[0].GetDouble(), e[1].GetDouble());
    static List<P> Pts(JsonElement e) => e.EnumerateArray().Select(Pt).ToList();

    static void Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var r = doc.RootElement;
        InletPts = r.GetProperty("inletPts").EnumerateArray().Select(e => (e[0].GetDouble(), e[1].GetDouble(), e[2].GetDouble())).ToArray();
        InletS = r.GetProperty("inletS").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        Starts = r.GetProperty("starts").EnumerateArray().Select(e => (e[0].GetInt32(), e[1].GetString()!, new P(e[2].GetDouble(), e[3].GetDouble()))).ToList();
        Spots = r.GetProperty("spots").EnumerateArray().Select(e => (new P(e[0].GetDouble(), e[1].GetDouble()), e[2].GetDouble(), e[3].GetString()!)).ToList();
        Vents = r.GetProperty("vents").EnumerateArray().Select(e => (e[0].GetString()!, new P(e[1].GetDouble(), e[2].GetDouble()))).ToList();
        Roads = r.GetProperty("roads").EnumerateArray().Select(e => (e[0].GetString()!, Pts(e[1]))).ToList();
        Dunes = r.GetProperty("dunes").EnumerateArray().Select(e => (new P(e[0].GetDouble(), e[1].GetDouble()), e[2].GetDouble(), e[3].GetDouble())).ToList();
        Craters = r.GetProperty("craters").EnumerateArray().Select(e => (new P(e[0].GetDouble(), e[1].GetDouble()), e[2].GetDouble(), e[3].GetDouble(), e[4].GetBoolean(), e[5].GetBoolean())).ToList();
        WireRuns = r.GetProperty("wire").EnumerateArray().Select(e => (e[0].GetString()!, e[1].GetDouble(), e[2].GetDouble(), e[3].GetDouble())).ToList();
        Hedges = r.GetProperty("hedges").EnumerateArray().Select(Pts).ToList();
        Fields = r.GetProperty("fields").EnumerateArray().Select(Pts).ToList();
        Wrecks = r.GetProperty("wrecks").EnumerateArray().Select(e => (e[0].GetString()!, new P(e[1].GetDouble(), e[2].GetDouble()), e[3].GetDouble())).ToList();
        BrookDry = Pts(r.GetProperty("brookDry"));
        CrossA = r.GetProperty("crossA").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        CrossB = r.GetProperty("crossB").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        Draws = r.GetProperty("draws").EnumerateArray().Select(e => e.GetDouble()).ToArray();
        HeadlandRoadZ = r.GetProperty("headlandRoadZ").GetDouble();
        EndA = r.GetProperty("linesEnd").GetProperty("A").GetDouble();
        EndB = r.GetProperty("linesEnd").GetProperty("B").GetDouble();
        FireRuns = r.GetProperty("fireRuns").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(e => (e[0].GetDouble(), e[1].GetDouble())).ToList());
        CommTrenches = r.GetProperty("commTrenches").EnumerateArray().Select(e => (e[0].GetString()!, Pts(e[1]))).ToList();
        Nests = r.GetProperty("mgNests").EnumerateArray().Select(e => (e[0].GetString()!, new P(e[1].GetDouble(), e[2].GetDouble()))).ToList();
        Bunkers = r.GetProperty("bunkers").EnumerateArray().Select(e => (e[0].GetString()!, new P(e[1].GetDouble(), e[2].GetDouble()), e[3].GetDouble())).ToList();
        Hedgehogs = Pts(r.GetProperty("hedgehogs"));
        Orchard = Pts(r.GetProperty("orchard"));
        Pines = r.GetProperty("pines").EnumerateArray().Select(e => (new P(e[0].GetDouble(), e[1].GetDouble()), e[2].GetDouble())).ToList();
        Houses = r.GetProperty("villageHouses").EnumerateArray().Select(e => (new P(e[0].GetDouble(), e[1].GetDouble()), e[2].GetDouble())).ToList();
        Village = Pt(r.GetProperty("village"));
        Farm = Pt(r.GetProperty("farm"));
        Lighthouse = Pt(r.GetProperty("lighthouse"));
        Battery = Pt(r.GetProperty("battery"));
        Windmill = Pt(r.GetProperty("windmill"));
        // The table's own samples of its lines, to prove the port: every 64 along z, within a hundredth of a unit.
        foreach (var line in r.GetProperty("lines").EnumerateObject())
        {
            Func<double, double> f = line.Name switch
            {
                "coast" => Coast, "berm" => BermX, "foredune" => z => BermX(z) + ForeduneWidth(z), "fireA" => FireA, "fireB" => FireB, "crest" => CrestX,
                _ => throw new InvalidDataException(line.Name),
            };
            foreach (var e in line.Value.EnumerateArray())
            {
                double z = e[0].GetDouble(), want = e[1].GetDouble();
                if (Math.Abs(f(z) - want) > 0.06)
                    throw new InvalidDataException($"layout.json's {line.Name} at z {z} is {want}, the port gives {f(z):0.00}");
            }
        }
    }

    /// <summary>The cove's north shore by the hill team's lane down to it: the jetty runs out from here, the boat sheds stand back from it.</summary>
    public static P CoveShore => InletPts.Where(p => p.X > 9050 && p.X < 9300).Select(p => new P(p.X, p.Z - p.W)).First();
    public static P[] BoatSheds => [CoveShore + new P(-170, -330), CoveShore + new P(190, -320)];

    public static IEnumerable<P> StartsOf(string team) => Starts.Where(s => s.Team == team).Select(s => s.At);
}
