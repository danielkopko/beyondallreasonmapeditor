// Meridian Landfall's map, in game units (x east, z south; 32 × 36, 16384 × 18432). The ship is Starship Meridian's
// Layout moved by (OX, OZ) and raised by HY; everything else comes from the design's table (../design/layout.py writes
// layout.json, copied beside the converter when it is built). The port half (x < CX) is given; the starboard half is its
// mirror image. `Hash01` and the outlines are layout.py's own (SplitMix64), so the build makes the craters drawn there.
using System.Text.Json;

static class World
{
    public const int SquaresX = 2048, SquaresZ = 2304, NX = SquaresX + 1, NZ = SquaresZ + 1;
    public const double W = SquaresX * 8, H = SquaresZ * 8, CX = W / 2;
    public const float MinH = 100, MaxH = 1000;
    /// <summary>The ship's offset (game units, and vertices) and how far it is raised: its deck at Plain.</summary>
    public const double OX = 2048, OZ = 2048, HY = 400, Plain = 640;
    public const int VX = (int)(OX / 8), VZ = (int)(OZ / 8);

    public static P Mirror(P p) => new(W - p.X, p.Z);
    public static double Mirror(double x) => W - x;
    /// <summary>A point of Meridian's ship (its own coordinates) in this map.</summary>
    public static P FromShip(P p) => new(p.X + OX, p.Z + OZ);
    public static P ToShip(P p) => new(p.X - OX, p.Z - OZ);

    public sealed record Crater(P At, double R, double Depth, double Rim, int Key, string What);
    public sealed record Box(double X0, double Z0, double X1, double Z1)
    {
        public bool Has(P p, double margin = 0) => p.X >= X0 - margin && p.X <= X1 + margin && p.Z >= Z0 - margin && p.Z <= Z1 + margin;
        public Box Mirrored => new(W - X1, Z0, W - X0, Z1);
        public P Centre => new((X0 + X1) / 2, (Z0 + Z1) / 2);
    }

    // ---- The table.
    public static readonly long Seed;
    public static readonly double[] EdgeMin, EdgeMax;
    public static readonly double MareR;
    public static readonly (string Name, P At)[] StartsOutsidePort;
    public static readonly (P At, double Income)[] SpotsOutsidePort, SpotsContestedPort;
    public static readonly P[] VentsOutside;
    public static readonly Box[] BayDoorsPort, ApronsPort;
    /// <summary>Port's craters (bowls keys 2000+, pits 1000+, small 3000+, as in layout.py); starboard's are their mirror images.</summary>
    public static readonly Crater[] Bowls, Pits, Small;

    static World()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "layout.json")));
        var r = doc.RootElement;
        double D(JsonElement e) => e.GetDouble();
        double[] A(JsonElement e) => e.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN).ToArray();
        if (D(r.GetProperty("mapUnits")[0]) * 512 != W || D(r.GetProperty("mapUnits")[1]) * 512 != H || D(r.GetProperty("offset")[0]) != OX
            || D(r.GetProperty("offset")[1]) != OZ || D(r.GetProperty("raise")) != HY || D(r.GetProperty("plain")) != Plain)
            throw new InvalidDataException("layout.json's map, offset or heights differ from World.cs: run layout.py and rebuild");
        Seed = r.GetProperty("seed").GetInt64();
        EdgeMin = A(r.GetProperty("edge")[0]); EdgeMax = A(r.GetProperty("edge")[1]);
        MareR = D(r.GetProperty("mareRadius"));
        StartsOutsidePort = r.GetProperty("startsOutside").EnumerateArray().Select(e => (e[0].GetString()!, new P(D(e[1]), D(e[2])))).ToArray();
        SpotsOutsidePort = r.GetProperty("spotsOutside").EnumerateArray().Select(e => (new P(D(e[0]), D(e[1])), D(e[2]))).ToArray();
        SpotsContestedPort = r.GetProperty("spotsContested").EnumerateArray().Select(e => (new P(D(e[0]), D(e[1])), D(e[2]))).ToArray();
        VentsOutside = r.GetProperty("ventsOutside").EnumerateArray().Select(e => new P(D(e[0]), D(e[1]))).ToArray();
        BayDoorsPort = r.GetProperty("bayDoors").EnumerateArray().Select(e => { var a = A(e); return new Box(a[0], a[1], a[2], a[3]); }).ToArray();
        ApronsPort = r.GetProperty("aprons").EnumerateArray().Select(e => { var a = A(e); return new Box(a[0], a[1], a[2], a[3]); }).ToArray();
        Bowls = r.GetProperty("bowls").EnumerateArray().Select((e, k) => new Crater(new P(D(e[0]), D(e[1])), D(e[2]), D(e[3]), D(e[4]), 2000 + k, e[5].GetString()!)).ToArray();
        Pits = r.GetProperty("pits").EnumerateArray().Select((e, k) => new Crater(new P(D(e[0]), D(e[1])), D(e[2]), D(e[3]), 70, 1000 + k, "void")).ToArray();
        Small = r.GetProperty("small").EnumerateArray().Select((e, k) => new Crater(new P(D(e[0]), D(e[1])), D(e[2]), D(e[3]), D(e[4]), 3000 + k, "")).ToArray();
        Starts = BuildStarts();
        Spots = BuildSpots().ToArray();
        Vents = [.. Layout.Vents.Select(v => FromShip(v.At)), .. VentsOutside, .. Bowls.Where(b => b.What == "vent").SelectMany(b => new[] { b.At, Mirror(b.At) })];
        BayDoors = [.. BayDoorsPort, .. BayDoorsPort.Select(b => b.Mirrored)];
        Aprons = [.. ApronsPort, .. ApronsPort.Select(b => b.Mirrored)];
    }

    // ---- Starts, metal, vents, in this map's coordinates.

    /// <summary>The 16 starts: port's 0-7 (Meridian's four in the ship, then the four outside), starboard's 8-15 their mirror images.</summary>
    public static readonly (string Name, P At)[] Starts;

    static (string, P)[] BuildStarts()
    {
        var port = Layout.Starts.Take(4).Select(s => (s.Room, FromShip(s.At))).Concat(StartsOutsidePort).ToArray();
        return [.. port, .. port.Select(s => (s.Item1, Mirror(s.Item2)))];
    }

    /// <summary>Every metal spot: where, metal/s, and whose ("port", "starboard", "shared", or "bowl": in a crater, bots only).</summary>
    public static readonly (P At, double Income, string Side)[] Spots;

    static IEnumerable<(P, double, string)> BuildSpots()
    {
        foreach (var (at, m, side) in Layout.Spots) yield return (FromShip(at), m, side);
        foreach (var (at, m) in SpotsOutsidePort) { yield return (at, m, "port"); yield return (Mirror(at), m, "starboard"); }
        foreach (var (at, m) in SpotsContestedPort) { yield return (at, m, "shared"); yield return (Mirror(at), m, "shared"); }
        foreach (var b in Bowls.Where(b => b.What.StartsWith("metal", StringComparison.Ordinal)))
        {
            double m = double.Parse(b.What[6..], System.Globalization.CultureInfo.InvariantCulture);
            yield return (b.At, m, "bowl"); yield return (Mirror(b.At), m, "bowl");
        }
    }

    /// <summary>The geothermal vents: the ship's, the asteroid's on the centreline, and those on bowls' floors.</summary>
    public static readonly P[] Vents;

    public static readonly Box[] BayDoors, Aprons;

    // ---- Random numbers and outlines (layout.py's).

    static ulong SplitMix(ulong v)
    {
        v += 0x9E3779B97F4A7C15UL;
        v = (v ^ (v >> 30)) * 0xBF58476D1CE4E5B9UL;
        v = (v ^ (v >> 27)) * 0x94D049BB133111EBUL;
        return v ^ (v >> 31);
    }

    public static double Hash01(params long[] ints)
    {
        ulong h = (ulong)Seed;
        foreach (var i in ints) h = SplitMix(h ^ (ulong)i);
        return (h >> 11) / (double)(1UL << 53);
    }

    /// <summary>
    /// A pit's radius at angle a (layout.py's jag): a fractal outline (harmonics 2 to 40, each weaker by about 1/n, so
    /// the line is rough at every scale), and a few smooth notches where a chunk of rock blew out or a buttress stayed.
    /// </summary>
    public static double Jag(double r, int k, double a) => r * Lookup(jagTables.GetOrAdd(k, BuildJag), a / (2 * Math.PI));

    static double JagExact(int k, double a)
    {
        double w = 1;
        for (int n = 2; n <= 40; n++)
            w += 0.13 * Math.Pow(n, -0.9) * (0.4 + 0.9 * Hash01(k, 200 + n)) * Math.Sin(n * a + 6.283 * Hash01(k, 100 + n));
        for (int j = 0; j < 5; j++)
        {
            double c = 6.283 * Hash01(k, 300 + j), width = 0.04 + 0.08 * Hash01(k, 310 + j), size = (Hash01(k, 320 + j) - 0.5) * 0.22;
            double d = Math.Atan2(Math.Sin(a - c), Math.Cos(a - c)) / width;
            w += size * Math.Exp(-d * d);
        }
        return w;
    }

    const int JagSamples = 4096;
    static readonly System.Collections.Concurrent.ConcurrentDictionary<int, float[]> jagTables = new();
    static float[] BuildJag(int k) => Enumerable.Range(0, JagSamples).Select(i => (float)JagExact(k, 2 * Math.PI * i / JagSamples)).ToArray();

    /// <summary>A periodic table read at fraction f of its period, linearly between its samples.</summary>
    static double Lookup(float[] t, double f)
    {
        double x = (f - Math.Floor(f)) * t.Length;
        int i = (int)x;
        double u = x - i;
        return t[i % t.Length] * (1 - u) + t[(i + 1) % t.Length] * u;
    }

    /// <summary>A smooth wave round a crater, 0 to 1 (its first five harmonics): how wide its rim is, how steep its wall.</summary>
    public static double Around(int k, int salt, double a)
    {
        double s = 0;
        for (int n = 1; n <= 5; n++) s += Math.Sin(n * a + 6.283 * Hash01(k, salt, n)) / n;
        return Math.Clamp(0.5 + s / 3.2, 0, 1);
    }

    /// <summary>A bowl's or small crater's radius at angle a (layout.py's waver): no circle, and no spikes.</summary>
    public static double Waver(double r, int k, double a) =>
        r * (1 + 0.05 * Math.Sin(3 * a + 6.3 * Hash01(k, 1)) + 0.035 * Math.Sin(5 * a + 6.3 * Hash01(k, 2)) + 0.02 * Math.Sin(8 * a + 6.3 * Hash01(k, 3)));

    /// <summary>
    /// How far in from border b (0 north, 1 the sides, 2 south) the rock starts, at u along it (layout.py's edge_depth):
    /// bays, eight octaves of waves from 700 down to 6 long (each weaker), and here and there a smooth bite where a
    /// chunk broke away or a spur stayed.
    /// </summary>
    public static double EdgeDepth(int b, double u)
    {
        var t = edgeTables[b] ??= BuildEdge(b);
        double x = Math.Clamp(u, 0, EdgeLength) / EdgeStep;
        int i = Math.Min((int)x, t.Length - 2);
        return t[i] + (t[i + 1] - t[i]) * (x - i);
    }

    static double EdgeExact(int b, double u)
    {
        double bay = Math.Pow(Math.Max(0, Math.Sin(u / 1150 + 6.3 * Hash01(b, 1)) * Math.Sin(u / 470 + 6.3 * Hash01(b, 2))), 1.3);
        double d = EdgeMin[b] + (EdgeMax[b] - EdgeMin[b]) * bay;
        for (int j = 0; j < 8; j++)
        {
            double len = 700 / Math.Pow(2, j) * (0.8 + 0.4 * Hash01(b, 320 + j));
            d += 60 * Math.Pow(len / 700, 0.8) * Math.Sin(u / len + 6.283 * Hash01(b, 300 + j));
        }
        long k0 = (long)Math.Floor(u / 400);
        for (long k = k0 - 1; k <= k0 + 1; k++)
        {
            if (Hash01(b, k, 14) > 0.45) continue;
            double c = (k + Hash01(b, k, 11)) * 400, w = 30 + 90 * Hash01(b, k, 12), h = (Hash01(b, k, 13) - 0.35) * 200, q = (u - c) / w;
            d += h * Math.Exp(-q * q);
        }
        return Math.Max(60, d);
    }

    const double EdgeStep = 2, EdgeLength = 20000;
    static readonly float[]?[] edgeTables = new float[3][];
    static float[] BuildEdge(int b) => Enumerable.Range(0, (int)(EdgeLength / EdgeStep) + 2).Select(i => (float)EdgeExact(b, i * EdgeStep)).ToArray();

    /// <summary>Signed distance into the asteroid's rock from its edge (negative: the void toward the map's border).</summary>
    public static double RockEdge(P p)
    {
        double u = Math.Abs(p.X - CX);
        return Math.Min(Math.Min(p.Z - EdgeDepth(0, u), H - p.Z - EdgeDepth(2, u)), Math.Min(p.X - EdgeDepth(1, p.Z), W - p.X - EdgeDepth(1, p.Z)));
    }
}
