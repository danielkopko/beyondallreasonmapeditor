// Points, geometry, noise, and the map's mirror: the north half is made, and the south is its mirror image across
// the line along the middle (z = half the map), where the ship lies.
readonly record struct P(double X, double Z)
{
    public static P operator +(P a, P b) => new(a.X + b.X, a.Z + b.Z);
    public static P operator -(P a, P b) => new(a.X - b.X, a.Z - b.Z);
    public static P operator *(P a, double k) => new(a.X * k, a.Z * k);
    public double Length => Math.Sqrt(X * X + Z * Z);
    public P Unit => this * (1 / Length);
    public P Perp => new(-Z, X);
    public double Dot(P o) => X * o.X + Z * o.Z;
}

static class Geo
{
    public static double S(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    public static double ToSegment(P p, P a, P b, out double t)
    {
        var ab = b - a;
        double l2 = ab.Dot(ab);
        t = l2 < 1e-9 ? 0 : Math.Clamp((p - a).Dot(ab) / l2, 0, 1);
        return (p - (a + ab * t)).Length;
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

    /// <summary>A smooth line through the points (Catmull-Rom), every <paramref name="step"/> units or so.</summary>
    public static List<P> Smooth(IReadOnlyList<P> pts, double step)
    {
        var line = new List<P>();
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            P p0 = pts[Math.Max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Math.Min(pts.Count - 1, i + 2)];
            int n = Math.Max(1, (int)Math.Ceiling((p2 - p1).Length / step));
            for (int k = 0; k < n; k++)
            {
                double t = k / (double)n, t2 = t * t, t3 = t2 * t;
                line.Add((p1 * 2 + (p2 - p0) * t + (p0 * 2 - p1 * 5 + p2 * 4 - p3) * t2 + (p1 * 3 - p0 - p2 * 3 + p3) * t3) * 0.5);
            }
        }
        line.Add(pts[^1]);
        return line;
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
/// The mirror across the middle line. Anything read here is read in the north half; near the line it is blended
/// with the reading at its mirror image, so both halves are the same and meet smoothly, with no crease.
/// </summary>
sealed class Mirror(double size)
{
    public readonly double Size = size, Half = size / 2;

    public P North(P p) => p.Z <= Half ? p : new P(p.X, Size - p.Z);
    public P Image(P p) => new(p.X, Size - p.Z);
    public double ToLine(P p) => Math.Abs(p.Z - Half);

    public double Value(P p, Func<P, double> f)
    {
        var own = North(p);
        double w = 0.5 + 0.5 * Geo.S(ToLine(p) / 400);
        return w >= 0.999 ? f(own) : w * f(own) + (1 - w) * f(Image(own));
    }
}
