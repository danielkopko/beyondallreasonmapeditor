// The design: k7_five_rivers.svg's geometry, as export_geometry.py writes it (the drawing's pixels, every team's part
// turned into place), in game units. 28 x 28: 640 drawing pixels are 14336 units, 22.4 a pixel.
using System.Text.Json;

readonly record struct P(double X, double Z)
{
    public static P operator +(P a, P b) => new(a.X + b.X, a.Z + b.Z);
    public static P operator -(P a, P b) => new(a.X - b.X, a.Z - b.Z);
    public static P operator *(P a, double k) => new(a.X * k, a.Z * k);
    public double Length => Math.Sqrt(X * X + Z * Z);
    public P Unit => this * (1 / Length);
    public P Perp => new(-Z, X);
    public double Dot(P o) => X * o.X + Z * o.Z;
    public double Cross(P o) => X * o.Z - Z * o.X;
}

sealed record Hall(int Team, P Centre, P Along, P Out, double A, double B);
sealed record Segment(int Team, P A, P B, double HalfWidth);
sealed record Spot(P At, double Income, string Kind, bool Wet, int Team);
sealed record Start(P At, int Team, int N); // N 0 for the king, 1-3 for the peasants

sealed class Design
{
    public const double MapSize = 14336;
    public const int Teams = 5;
    public static readonly P Centre = new(MapSize / 2, MapSize / 2);
    public readonly double Upp, PlayRadius, LakeRadius, IsleRadius, RiverHalf;
    public readonly List<P[]> Rivers = [];
    public readonly List<Hall> Halls = [];
    public readonly List<Segment> Ramps = [], Bridges = [], Fords = [], Tracks = [];
    public readonly List<P[]> Paths = [];
    public readonly List<Spot> Spots = [];
    public readonly List<(P At, int? Team)> Vents = [];
    public readonly List<Start> Starts = [];
    public readonly string Heights;

    public Design(string json)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(json));
        var root = doc.RootElement;
        Upp = root.GetProperty("units_per_pixel").GetDouble();
        double origin = root.GetProperty("origin").GetDouble();
        PlayRadius = root.GetProperty("play_radius").GetDouble() * Upp;
        Heights = root.GetProperty("heights").GetString()!;
        P Pt(JsonElement e) => new((e[0].GetDouble() - origin) * Upp, (e[1].GetDouble() - origin) * Upp);
        P[] Pts(JsonElement e) => [.. e.EnumerateArray().Select(Pt)];
        int Team(JsonElement e) => e.GetProperty("team").ValueKind == JsonValueKind.Null ? -1 : e.GetProperty("team").GetInt32();
        double riverHalf = 0, lake = 0, isle = 0;
        foreach (var part in root.GetProperty("parts").EnumerateArray())
        {
            int team = Team(part);
            switch (part.GetProperty("kind").GetString())
            {
                case "water" when team < 0:
                    lake = Pts(part.GetProperty("pts")).Average(p => (p - Centre).Length);
                    break;
                case "water":
                {
                    // A river: its strip is the left bank out, then the right bank back.
                    var pts = Pts(part.GetProperty("pts"));
                    int n = pts.Length / 2;
                    Rivers.Add([.. Enumerable.Range(0, n).Select(i => (pts[i] + pts[pts.Length - 1 - i]) * 0.5)]);
                    riverHalf = (pts[n / 2] - pts[pts.Length - 1 - n / 2]).Length / 2;
                    break;
                }
                case "land":
                    isle = Pts(part.GetProperty("pts")).Average(p => (p - Centre).Length);
                    break;
                case "clifftop":
                {
                    var pts = Pts(part.GetProperty("pts"));
                    var c = new P(pts.Average(p => p.X), pts.Average(p => p.Z));
                    var far = pts.MaxBy(p => (p - c).Length);
                    double a = (far - c).Length, b = pts.Min(p => (p - c).Length);
                    var along = (far - c).Unit;
                    var outward = (c - Centre).Unit;
                    Halls.Add(new Hall(team, c, along, outward, a, b));
                    break;
                }
                case "ramp":
                    Ramps.Add(new Segment(team, Pt(part.GetProperty("lo")), Pt(part.GetProperty("hi")), part.GetProperty("hw").GetDouble() * Upp));
                    break;
                case "bridge":
                    Bridges.Add(new Segment(team, Pt(part.GetProperty("a")), Pt(part.GetProperty("b")), part.GetProperty("hw").GetDouble() * Upp));
                    break;
                case "ford":
                    Fords.Add(new Segment(team, Pt(part.GetProperty("a")), Pt(part.GetProperty("b")), part.GetProperty("hw").GetDouble() * Upp));
                    break;
                case "track":
                    Tracks.Add(new Segment(team, Pt(part.GetProperty("a")), Pt(part.GetProperty("b")), 0));
                    break;
                case "path":
                    Paths.Add(Pts(part.GetProperty("pts")));
                    break;
            }
        }
        RiverHalf = riverHalf;
        LakeRadius = lake;
        IsleRadius = isle;
        PlanFifths();
        foreach (var s in root.GetProperty("spots").EnumerateArray())
            Spots.Add(new Spot(new P((s.GetProperty("x").GetDouble() - origin) * Upp, (s.GetProperty("y").GetDouble() - origin) * Upp),
                s.GetProperty("income").GetDouble(), s.GetProperty("kind").GetString()!, s.GetProperty("wet").GetBoolean(), s.GetProperty("team").GetInt32()));
        foreach (var v in root.GetProperty("vents").EnumerateArray())
            Vents.Add((new P((v.GetProperty("x").GetDouble() - origin) * Upp, (v.GetProperty("y").GetDouble() - origin) * Upp), Team(v) < 0 ? null : Team(v)));
        foreach (var k in root.GetProperty("kings").EnumerateArray())
            Starts.Add(new Start(new P((k.GetProperty("x").GetDouble() - origin) * Upp, (k.GetProperty("y").GetDouble() - origin) * Upp), k.GetProperty("team").GetInt32(), 0));
        foreach (var k in root.GetProperty("peasants").EnumerateArray())
            Starts.Add(new Start(new P((k.GetProperty("x").GetDouble() - origin) * Upp, (k.GetProperty("y").GetDouble() - origin) * Upp), k.GetProperty("team").GetInt32(),
                k.GetProperty("n").GetInt32()));
    }

    /// <summary>Degrees clockwise from north (up the map), as the drawing measures them; Team A's king is at 0.</summary>
    public static double Bearing(P p) => Math.Atan2(p.X - Centre.X, -(p.Z - Centre.Z)) * 180 / Math.PI;

    // How far the fifths' edges swing from 36 degrees (and its turns), by distance from the centre: they follow the
    // rivers' winding middles, so where noise read in one fifth meets the next's it is under water.
    static readonly double[] edgeSwing = new double[1024];

    void PlanFifths()
    {
        var river = Rivers.MinBy(r => Math.Abs(Bearing(r[r.Length / 2]) - 36))!;
        for (int i = 0; i < edgeSwing.Length; i++)
        {
            double r = i * 16.0;
            double swing = Bearing(river[0]) - 36;
            for (int k = 0; k + 1 < river.Length; k++)
            {
                double r0 = (river[k] - Centre).Length, r1 = (river[k + 1] - Centre).Length;
                if (r < r0) break;
                swing = Bearing(river[k + 1]) - 36;
                if (r <= r1)
                {
                    swing = Bearing(river[k] + (river[k + 1] - river[k]) * ((r - r0) / (r1 - r0))) - 36;
                    break;
                }
            }
            edgeSwing[i] = swing;
        }
    }

    /// <summary>The point turned by <paramref name="degrees"/> clockwise about the centre.</summary>
    public static P Turn(P p, double degrees)
    {
        double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        var d = p - Centre;
        return Centre + new P(d.X * c - d.Z * s, d.X * s + d.Z * c);
    }

    /// <summary>The same point in Team A's fifth, so noise read there is alike in every fifth.</summary>
    public static P Fold(P p) => Turn(p, -72 * FifthOf(p));

    /// <summary>The team whose fifth a point is in: between the rivers either side of its hall.</summary>
    public static int FifthOf(P p)
    {
        double swing = edgeSwing[Math.Clamp((int)((p - Centre).Length / 16), 0, edgeSwing.Length - 1)];
        int k = (int)Math.Floor((Bearing(p) - swing + 36) / 72);
        return (k % 5 + 5) % 5;
    }

    public static double ToSegment(P p, P a, P b, out double t)
    {
        var ab = b - a;
        t = Math.Clamp((p - a).Dot(ab) / ab.Dot(ab), 0, 1);
        return (p - (a + ab * t)).Length;
    }

    /// <summary>Distance to a polyline, with how far along it the nearest point is.</summary>
    public static double ToPolyline(P p, IReadOnlyList<P> line, out double along)
    {
        double best = double.MaxValue, run = 0;
        along = 0;
        for (int i = 0; i + 1 < line.Count; i++)
        {
            double d = ToSegment(p, line[i], line[i + 1], out double t);
            double len = (line[i + 1] - line[i]).Length;
            if (d < best) { best = d; along = run + t * len; }
            run += len;
        }
        return best;
    }

    public static double S(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }
}

sealed class Noise
{
    readonly int[] perm = new int[512];
    public Noise(int seed)
    {
        var p = Enumerable.Range(0, 256).ToArray();
        var rnd = new Random(seed);
        for (int i = 255; i > 0; i--) { int j = rnd.Next(i + 1); (p[i], p[j]) = (p[j], p[i]); }
        for (int i = 0; i < 512; i++) perm[i] = p[i & 255];
    }
    static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    static double Grad(int h, double x, double y) => (h & 3) switch { 0 => x + y, 1 => -x + y, 2 => x - y, _ => -x - y };
    public double At(double x, double y)
    {
        int xi = (int)Math.Floor(x) & 255, yi = (int)Math.Floor(y) & 255;
        double xf = x - Math.Floor(x), yf = y - Math.Floor(y), u = Fade(xf), v = Fade(yf);
        int aa = perm[perm[xi] + yi], ab = perm[perm[xi] + yi + 1], ba = perm[perm[xi + 1] + yi], bb = perm[perm[xi + 1] + yi + 1];
        double x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u), x2 = Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u);
        return Lerp(x1, x2, v); // about -1..1
    }
    public double Fbm(double x, double y, int octaves = 4)
    {
        double sum = 0, amp = 1, norm = 0;
        for (int o = 0; o < octaves; o++) { sum += amp * At(x, y); norm += amp; amp *= 0.5; x *= 2.03; y *= 2.03; }
        return sum / norm;
    }
    static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
