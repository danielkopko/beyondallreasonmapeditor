// A king map's design: its drawing's geometry and planet, as export_geometry.py writes them (the drawing's pixels,
// every team's part turned into place), in game units. 640 drawing pixels are the whole map.
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

/// <summary>A segment part: a ramp (A low, B high), bridge, ford, rampart or track (A on top, B below).</summary>
sealed record Seg(int Team, P A, P B, double HalfWidth);

/// <summary>An outline part (band, cliff, plateau, water, land), with its box.</summary>
sealed class Shape(int team, P[] pts, double h, string layer)
{
    public readonly int Team = team;
    public readonly P[] Pts = pts;
    public readonly double H = h;
    public readonly string Layer = layer;
    public readonly double X0 = pts.Min(p => p.X), Z0 = pts.Min(p => p.Z), X1 = pts.Max(p => p.X), Z1 = pts.Max(p => p.Z);
    public bool Contains(P p) => p.X >= X0 && p.X <= X1 && p.Z >= Z0 && p.Z <= Z1 && Geo.Inside(p, Pts);
    /// <summary>Distance to the outline, negative inside.</summary>
    public double Signed(P p)
    {
        double d = Geo.ToPolygonEdge(p, Pts);
        return Contains(p) ? -d : d;
    }
}

sealed record Ring(int Team, P C, double R);
sealed record Spot(P At, double Income, string Kind, bool Wet, int Team);
sealed record Start(P At, int Team, int N); // N 0 for the king, 1-3 for the peasants

/// <summary>The planet (planets.py): colours as mapinfo takes them, and the weather.</summary>
sealed class Planet
{
    public string Name = "", Kind = "", Liquid = "";
    public (int R, int G, int B) Rock, Dirt, Sand, Mud, Seabed, Ground;
    public (double R, double G, double B) Sky, Fog, Sun, LiquidColour;
    public Dictionary<string, string> MapInfo = [];
}

sealed class Design
{
    public readonly string Key, Name;
    public readonly int Size, Fold, Squares;
    public readonly double Axis, MapSize, Upp, PlayRadius, GroundH;
    public readonly bool Square;
    public readonly P Centre;
    public readonly List<Shape> Bands = [], Cliffs = [], Plateaus = [], Water = [], Land = [];
    public readonly List<Seg> Ramps = [], Bridges = [], Fords = [], Walls = [], Tracks = [];
    public readonly List<Ring> Towers = [];
    public readonly List<P[]> Paths = [];
    public readonly List<Spot> Spots = [];
    public readonly List<(P At, bool Whole)> Vents = [];
    public readonly List<Start> Starts = [];
    public readonly Planet Planet = new();
    public readonly string Heights;

    public Design(string json)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(json));
        var root = doc.RootElement;
        Key = root.GetProperty("key").GetString()!;
        Name = root.GetProperty("name").GetString()!;
        Size = root.GetProperty("size").GetInt32();
        Fold = root.GetProperty("fold").GetInt32();
        Axis = root.GetProperty("axis").GetDouble();
        Square = root.GetProperty("square").GetBoolean();
        GroundH = root.GetProperty("ground").GetDouble();
        Heights = root.GetProperty("heights").GetString()!;
        Squares = Size * 64;
        MapSize = Size * 512;
        Upp = root.GetProperty("units_per_pixel").GetDouble();
        double origin = root.GetProperty("origin").GetDouble();
        Centre = new P(MapSize / 2, MapSize / 2);
        PlayRadius = root.GetProperty("play_radius").GetDouble() * Upp;
        P Pt(JsonElement e) => new((e[0].GetDouble() - origin) * Upp, (e[1].GetDouble() - origin) * Upp);
        P[] Pts(JsonElement e) => [.. e.EnumerateArray().Select(Pt)];
        int Team(JsonElement e) => e.GetProperty("team").ValueKind == JsonValueKind.Null ? -1 : e.GetProperty("team").GetInt32();
        double Num(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
        foreach (var part in root.GetProperty("parts").EnumerateArray())
        {
            int team = Team(part);
            switch (part.GetProperty("kind").GetString())
            {
                case "band": Bands.Add(new Shape(team, Pts(part.GetProperty("pts")), Num(part, "h"), part.GetProperty("layer").GetString()!)); break;
                case "cliff": Cliffs.Add(new Shape(team, Pts(part.GetProperty("pts")), Num(part, "h"), "cliff")); break;
                case "clifftop": Plateaus.Add(new Shape(team, Pts(part.GetProperty("pts")), Num(part, "h"), "plateau")); break;
                case "water": Water.Add(new Shape(team, Pts(part.GetProperty("pts")), -60, "water")); break;
                case "land": Land.Add(new Shape(team, Pts(part.GetProperty("pts")), Num(part, "h"), "land")); break;
                case "ramp": Ramps.Add(new Seg(team, Pt(part.GetProperty("lo")), Pt(part.GetProperty("hi")), Num(part, "hw") * Upp)); break;
                case "bridge": Bridges.Add(new Seg(team, Pt(part.GetProperty("a")), Pt(part.GetProperty("b")), Num(part, "hw") * Upp)); break;
                case "ford": Fords.Add(new Seg(team, Pt(part.GetProperty("a")), Pt(part.GetProperty("b")), Num(part, "hw") * Upp)); break;
                case "wall": Walls.Add(new Seg(team, Pt(part.GetProperty("a")), Pt(part.GetProperty("b")), Num(part, "hw") * Upp)); break;
                case "track": Tracks.Add(new Seg(team, Pt(part.GetProperty("a")), Pt(part.GetProperty("b")), 0)); break;
                case "tower": Towers.Add(new Ring(team, Pt(part.GetProperty("c")), Num(part, "r") * Upp)); break;
                case "path": Paths.Add(Pts(part.GetProperty("pts"))); break;
            }
        }
        foreach (var s in root.GetProperty("spots").EnumerateArray())
            Spots.Add(new Spot(new P((s.GetProperty("x").GetDouble() - origin) * Upp, (s.GetProperty("y").GetDouble() - origin) * Upp),
                s.GetProperty("income").GetDouble(), s.GetProperty("kind").GetString()!, s.GetProperty("wet").GetBoolean(), s.GetProperty("team").GetInt32()));
        foreach (var v in root.GetProperty("vents").EnumerateArray())
            Vents.Add((new P((v.GetProperty("x").GetDouble() - origin) * Upp, (v.GetProperty("y").GetDouble() - origin) * Upp), Team(v) < 0));
        foreach (var k in root.GetProperty("kings").EnumerateArray())
            Starts.Add(new Start(new P((k.GetProperty("x").GetDouble() - origin) * Upp, (k.GetProperty("y").GetDouble() - origin) * Upp), k.GetProperty("team").GetInt32(), 0));
        foreach (var k in root.GetProperty("peasants").EnumerateArray())
            Starts.Add(new Start(new P((k.GetProperty("x").GetDouble() - origin) * Upp, (k.GetProperty("y").GetDouble() - origin) * Upp),
                k.GetProperty("team").GetInt32(), k.GetProperty("n").GetInt32()));
        // Vents drawn once at the centre come through once per team: keep one.
        Vents = [.. Vents.GroupBy(v => (Math.Round(v.At.X), Math.Round(v.At.Z))).Select(g => g.First())];

        var th = root.GetProperty("theme");
        (int, int, int) C8(string k) { var a = th.GetProperty("beautify").GetProperty(k); return (a[0].GetInt32(), a[1].GetInt32(), a[2].GetInt32()); }
        (double, double, double) C1(JsonElement a) => (a[0].GetDouble(), a[1].GetDouble(), a[2].GetDouble());
        Planet.Name = th.GetProperty("planet").GetString()!;
        Planet.Kind = th.GetProperty("kind").GetString()!;
        Planet.Liquid = th.GetProperty("liquid").GetString()!;
        Planet.Rock = C8("rock"); Planet.Dirt = C8("dirt"); Planet.Sand = C8("sand"); Planet.Mud = C8("mud"); Planet.Seabed = C8("seabed"); Planet.Ground = C8("ground");
        Planet.Sky = C1(th.GetProperty("sky")); Planet.Fog = C1(th.GetProperty("fog")); Planet.Sun = C1(th.GetProperty("sun"));
        Planet.LiquidColour = C1(th.GetProperty("liquid_colour"));
        foreach (var kv in th.GetProperty("mapinfo").EnumerateObject())
            Planet.MapInfo[kv.Name] = kv.Value.ValueKind == JsonValueKind.Array
                ? string.Join(",", kv.Value.EnumerateArray().Select(x => x.GetDouble().ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)))
                : kv.Value.GetDouble().ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    public double Radius(P p) => (p - Centre).Length;

    /// <summary>Degrees clockwise from north, as the drawing measures them.</summary>
    public double Bearing(P p) => Math.Atan2(p.X - Centre.X, -(p.Z - Centre.Z)) * 180 / Math.PI;

    public P Turn(P p, double degrees)
    {
        double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
        var q = p - Centre;
        return Centre + new P(q.X * c - q.Z * s, q.X * s + q.Z * c);
    }

    public double Step => 360.0 / Fold;

    /// <summary>The team whose part of the map a point is in (Team A's part is centred on the axis).</summary>
    public int PartOf(P p)
    {
        int k = (int)Math.Round((Bearing(p) - Axis) / Step);
        return (k % Fold + Fold) % Fold;
    }

    public bool InPlay(P p, double margin) => Square
        ? p.X >= margin && p.Z >= margin && p.X <= MapSize - margin && p.Z <= MapSize - margin
        : Radius(p) <= PlayRadius - margin;
}

static class Geo
{
    public static double S(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    public static bool Inside(P p, P[] poly)
    {
        bool hit = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].Z > p.Z) != (poly[j].Z > p.Z) && p.X < (poly[j].X - poly[i].X) * (p.Z - poly[i].Z) / (poly[j].Z - poly[i].Z) + poly[i].X)
                hit = !hit;
        return hit;
    }

    public static double ToSegment(P p, P a, P b, out double t)
    {
        var ab = b - a;
        double l2 = ab.Dot(ab);
        t = l2 < 1e-9 ? 0 : Math.Clamp((p - a).Dot(ab) / l2, 0, 1);
        return (p - (a + ab * t)).Length;
    }

    public static double ToPolygonEdge(P p, P[] poly)
    {
        double best = double.MaxValue;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            best = Math.Min(best, ToSegment(p, poly[j], poly[i], out _));
        return best;
    }

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

    public static IEnumerable<P> Resample(IReadOnlyList<P> line, double step)
    {
        for (int i = 0; i + 1 < line.Count; i++)
        {
            var a = line[i]; var b = line[i + 1];
            int n = Math.Max(1, (int)Math.Ceiling((b - a).Length / step));
            for (int k = 0; k < n; k++) yield return a + (b - a) * (k / (double)n);
        }
        if (line.Count > 0) yield return line[^1];
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

/// <summary>
/// Anything read in Team A's part of the map, so every team's part is the same: a point is turned into Team A's
/// part, and near the line between two parts it is blended with the neighbouring part's reading, so there is no
/// seam (both sides of the line blend the same two readings).
/// </summary>
sealed class Sym(Design d)
{
    public readonly record struct Frame(P Own, P Next, double W);

    public Frame At(P p)
    {
        double t = (d.Bearing(p) - d.Axis) / d.Step;
        int k = (int)Math.Round(t);
        double off = t - k;
        int nb = off > 0 ? k + 1 : k - 1;
        double r = d.Radius(p);
        double half = 0.5 * d.Step * Math.PI / 180 * r;
        double toLine = (0.5 - Math.Abs(off)) * d.Step * Math.PI / 180 * r;
        double w = 0.5 + 0.5 * Geo.S(toLine / Math.Max(1, Math.Min(400, half)));
        return new Frame(d.Turn(p, -k * d.Step), d.Turn(p, -nb * d.Step), w);
    }

    public double Value(P p, Func<P, double> f)
    {
        var fr = At(p);
        return fr.W >= 0.999 ? f(fr.Own) : fr.W * f(fr.Own) + (1 - fr.W) * f(fr.Next);
    }

    public static double Value(Frame fr, Func<P, double> f) => fr.W >= 0.999 ? f(fr.Own) : fr.W * f(fr.Own) + (1 - fr.W) * f(fr.Next);
}
