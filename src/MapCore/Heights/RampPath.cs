namespace MapCore.Heights;

/// <summary>
/// The centre line of a bending ramp: straight runs between its points (its first end, its bends, its last end), each
/// bend rounded by a circular arc as large as the runs beside it allow. A run between two bends is shared so that both
/// get the same radius (a bend that turns more takes more of it); a run to an end is the bend's alone; arcs never
/// overlap. A point the line runs straight on through takes nothing, so adding one on the line changes nothing. The
/// line is smooth (no corners) and never leaves the shape its points enclose; closely spaced points make it hug the
/// line they're drawn along. Along it, u is the distance from the first end; across it, v is the
/// distance from the line, positive to the left as the straight ramp has it. Beyond each end it runs on straight.
/// <para>
/// It is sampled every <see cref="SampleSpacing"/> units or so, each sample with the line's normal there; between
/// samples the normal turns evenly, so (u, v) and world points map to each other exactly and smoothly (within the
/// tightest bend's radius of the line). Finding where a point is, is the costly part, and the fills ask it for every
/// pixel, so it is looked up in a grid of 16-unit cells, each holding the samples that can matter in it
/// (<see cref="Grid"/>), one grid for each reach asked for.
/// </para>
/// </summary>
public sealed class RampPath : IEquatable<RampPath>
{
    /// <summary>About how far apart the line's samples are, in game units.</summary>
    public const double SampleSpacing = 4;

    /// <summary>
    /// How much further than a ramp's edge its grid answers exactly (see <see cref="Ramp.DistanceOutside"/>): enough for
    /// everything that looks just past the edge, such as where rock and dirt are taken from.
    /// </summary>
    public const double Margin = 64;

    /// <summary>
    /// The widest window over which u is smoothed beyond the ramp's sides (<see cref="SurfaceLocal"/>): where two parts of
    /// the line are about equally near, as between the legs of a switchback, the blend follows both rather than stepping
    /// between them.
    /// </summary>
    public const double SmoothWindow = 32;

    /// <summary>Below this window, the exact u counts for something in the smoothed one (so the window can close smoothly).</summary>
    const double ExactFade = 8;

    readonly (double X, double Z)[] points;
    readonly double[] xs, zs, us, nxs, nzs; // samples: position, how far along, normal (to the left)
    readonly (double X, double Z) startDirection, endDirection;
    readonly (double Radius, double X, double Z) tightest;
    readonly double[] tangents;
    readonly (double MinX, double MinZ, double MaxX, double MaxZ) sampleBounds;
    /// <summary>How many lookups (one for each reach and width) a path keeps: a fill uses two at once, and the editor changes the width as you drag.</summary>
    const int KeptGrids = 3;

    volatile Grid[] grids = []; // the most recently made first; replaced whole, so reading needs no lock
    readonly object making = new();

    /// <summary>The line through <paramref name="points"/>: at least two, each apart from the one before.</summary>
    public RampPath(IReadOnlyList<(double X, double Z)> points)
    {
        if (points.Count < 2)
            throw new ArgumentException("A ramp's path needs at least two points.", nameof(points));
        for (int i = 1; i < points.Count; i++)
            if (Distance(points[i - 1], points[i]) < 1e-6)
                throw new ArgumentException($"A ramp's path has two points at the same place ({points[i].X}, {points[i].Z}).", nameof(points));
        this.points = [.. points];
        var samples = new List<(double X, double Z, double Nx, double Nz)>();
        tangents = new double[this.points.Length];
        tightest = Build(this.points, samples, tangents);
        xs = samples.Select(s => s.X).ToArray();
        zs = samples.Select(s => s.Z).ToArray();
        nxs = samples.Select(s => s.Nx).ToArray();
        nzs = samples.Select(s => s.Nz).ToArray();
        us = new double[xs.Length];
        for (int i = 1; i < xs.Length; i++)
            us[i] = us[i - 1] + Math.Sqrt((xs[i] - xs[i - 1]) * (xs[i] - xs[i - 1]) + (zs[i] - zs[i - 1]) * (zs[i] - zs[i - 1]));
        Length = us[^1];
        startDirection = Unit(this.points[1].X - this.points[0].X, this.points[1].Z - this.points[0].Z);
        endDirection = Unit(this.points[^1].X - this.points[^2].X, this.points[^1].Z - this.points[^2].Z);
        sampleBounds = (xs.Min(), zs.Min(), xs.Max(), zs.Max());
    }

    /// <summary>The points it runs through and round: the first end, the bends, the last end.</summary>
    public IReadOnlyList<(double X, double Z)> Points => points;

    /// <summary>
    /// How far each point's arc reaches along the runs either side of it (0 at the ends, and at points straight on): the
    /// line runs straight from one point to the next except within these of them.
    /// </summary>
    public IReadOnlyList<double> Tangents => tangents;

    /// <summary>The length of the line, end to end.</summary>
    public double Length { get; }

    /// <summary>The line's samples, first end to last, with how far along it each is.</summary>
    public IEnumerable<(double X, double Z, double U)> Samples => xs.Select((x, i) => (x, zs[i], us[i]));

    /// <summary>The box the line's samples lie in.</summary>
    public (double MinX, double MinZ, double MaxX, double MaxZ) SampleBounds => sampleBounds;

    /// <summary>
    /// The tightest bend: the radius of its arc (exactly) and the middle of that arc; infinity (and the first end) for a
    /// path with no bend. A bend turning straight back on itself has no room for an arc: radius 0, at its point.
    /// </summary>
    public (double Radius, double X, double Z) Tightest() => tightest;

    // ---------------------------------------------------------------- the line

    /// <summary>Samples the runs and arcs into <paramref name="samples"/>; returns the tightest arc.</summary>
    static (double Radius, double X, double Z) Build((double X, double Z)[] p, List<(double X, double Z, double Nx, double Nz)> samples, double[] tangents)
    {
        int n = p.Length;
        var directions = new (double X, double Z)[n - 1];
        var lengths = new double[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            lengths[i] = Distance(p[i], p[i + 1]);
            directions[i] = Unit(p[i + 1].X - p[i].X, p[i + 1].Z - p[i].Z);
        }
        // How far each point turns the line (+ to the left), and tan(|turn| / 2): how far along the runs beside it an arc of
        // radius 1 reaches. The ends and points straight on turn nothing.
        var turns = new double[n];
        var tans = new double[n];
        for (int i = 1; i < n - 1; i++)
        {
            var (din, dout) = (directions[i - 1], directions[i]);
            turns[i] = Math.Atan2(din.X * dout.Z - din.Z * dout.X, din.X * dout.X + din.Z * dout.Z);
            tans[i] = Math.Tan(Math.Abs(turns[i]) / 2);
        }
        var tightest = (double.PositiveInfinity, p[0].X, p[0].Z);
        samples.Add((p[0].X, p[0].Z, -directions[0].Z, directions[0].X));
        var at = p[0];
        for (int i = 1; i < n - 1; i++)
        {
            double turn = turns[i];
            if (tans[i] < 1e-12)
                continue; // straight on: no arc
            // A run between two bends is shared so both get the same radius; a run to an end (or a point straight on) is
            // the bend's alone. The largest radius both sides allow.
            double radius = Math.Min(lengths[i - 1] / (tans[i] + tans[i - 1]), lengths[i] / (tans[i] + tans[i + 1]));
            double reach = radius * tans[i];
            tangents[i] = reach;
            var din = directions[i - 1];
            var start = (X: p[i].X - reach * din.X, Z: p[i].Z - reach * din.Z);
            Run(samples, at, start);
            double side = Math.Sign(turn);
            var centre = (X: start.X - side * radius * din.Z, Z: start.Z + side * radius * din.X); // to the inside of the turn
            double from = Math.Atan2(start.Z - centre.Z, start.X - centre.X);
            int steps = Math.Max(1, (int)Math.Round(radius * Math.Abs(turn) / SampleSpacing));
            for (int k = 1; k <= steps; k++)
            {
                double angle = from + turn * k / steps;
                double tx = -side * Math.Sin(angle), tz = side * Math.Cos(angle); // along the arc
                samples.Add((centre.X + radius * Math.Cos(angle), centre.Z + radius * Math.Sin(angle), -tz, tx));
            }
            at = (samples[^1].X, samples[^1].Z);
            if (radius < tightest.Item1)
            {
                double middle = from + turn / 2;
                tightest = radius < 1e-6 ? (radius, p[i].X, p[i].Z) : (radius, centre.X + radius * Math.Cos(middle), centre.Z + radius * Math.Sin(middle));
            }
        }
        Run(samples, at, p[^1]);
        return tightest;
    }

    /// <summary>Samples a straight run, from its start (already sampled) to its end.</summary>
    static void Run(List<(double X, double Z, double Nx, double Nz)> samples, (double X, double Z) from, (double X, double Z) to)
    {
        double length = Distance(from, to);
        if (length < 1e-9)
            return;
        var (dx, dz) = Unit(to.X - from.X, to.Z - from.Z);
        int steps = Math.Max(1, (int)Math.Round(length / SampleSpacing));
        for (int k = 1; k <= steps; k++)
            samples.Add(k == steps ? (to.X, to.Z, -dz, dx) : (from.X + (to.X - from.X) * k / steps, from.Z + (to.Z - from.Z) * k / steps, -dz, dx));
    }

    static double Distance((double X, double Z) a, (double X, double Z) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    static (double X, double Z) Unit(double x, double z)
    {
        double length = Math.Sqrt(x * x + z * z);
        return length < 1e-12 ? (1, 0) : (x / length, z / length);
    }

    /// <summary>The world point at (u, v): on the line at u, then v along its normal there (beyond the ends, straight on).</summary>
    public (double X, double Z) World(double u, double v)
    {
        if (u <= 0)
        {
            var (dx, dz) = startDirection;
            return (xs[0] + u * dx - v * dz, zs[0] + u * dz + v * dx);
        }
        if (u >= Length)
        {
            var (dx, dz) = endDirection;
            double t = u - Length;
            return (xs[^1] + t * dx - v * dz, zs[^1] + t * dz + v * dx);
        }
        int i = Array.BinarySearch(us, u);
        if (i < 0)
            i = ~i - 1;
        i = Math.Clamp(i, 0, us.Length - 2);
        double length = us[i + 1] - us[i], f = length > 0 ? (u - us[i]) / length : 0;
        var (nx, nz) = Unit(nxs[i] + (nxs[i + 1] - nxs[i]) * f, nzs[i] + (nzs[i + 1] - nzs[i]) * f);
        return (xs[i] + (xs[i + 1] - xs[i]) * f + v * nx, zs[i] + (zs[i + 1] - zs[i]) * f + v * nz);
    }

    /// <summary>
    /// The outline of a ramp <paramref name="halfWidth"/> either side of the line, run on <paramref name="grow"/> beyond
    /// each end, in order round it. Inside a bend tighter than the half width, a side would run backwards and loop over
    /// itself; those points are left out, so it cuts across instead.
    /// </summary>
    public (double X, double Z)[] Outline(double halfWidth, double grow)
    {
        var outline = new List<(double X, double Z)>(2 * xs.Length + 4);
        if (grow > 0)
            outline.Add(World(-grow, -halfWidth));
        outline.AddRange(Side(-halfWidth));
        if (grow > 0)
        {
            outline.Add(World(Length + grow, -halfWidth));
            outline.Add(World(Length + grow, halfWidth));
        }
        var left = Side(halfWidth);
        left.Reverse();
        outline.AddRange(left);
        if (grow > 0)
            outline.Add(World(-grow, halfWidth));
        return [.. outline];
    }

    /// <summary>
    /// One side of the outline, first end to last, <paramref name="v"/> from the line. Where the side crosses itself (inside
    /// a bend tighter than <paramref name="v"/>) it goes straight on from the crossing, leaving the loop out.
    /// </summary>
    List<(double X, double Z)> Side(double v)
    {
        int n = xs.Length;
        var points = new (double X, double Z)[n];
        for (int i = 0; i < n; i++)
            points[i] = (xs[i] + v * nxs[i], zs[i] + v * nzs[i]);
        var side = new List<(double X, double Z)>(n) { points[0] };
        var from = points[0];
        int at = 0;
        while (at < n - 1)
        {
            var to = points[at + 1];
            int hit = -1;
            (double X, double Z) crossing = default;
            for (int j = n - 2; j > at + 1 && hit < 0; j--)
                if (Crossing(from, to, points[j], points[j + 1]) is { } c)
                    (hit, crossing) = (j, c);
            if (hit >= 0)
                (from, at) = (crossing, hit); // on along the later segment, from where they cross
            else
                (from, at) = (to, at + 1);
            side.Add(from);
        }
        return side;
    }

    /// <summary>Where segments ab and cd cross (not counting touching at their ends), or null.</summary>
    static (double X, double Z)? Crossing((double X, double Z) a, (double X, double Z) b, (double X, double Z) c, (double X, double Z) d)
    {
        double rx = b.X - a.X, rz = b.Z - a.Z, sx = d.X - c.X, sz = d.Z - c.Z, denominator = rx * sz - rz * sx;
        if (Math.Abs(denominator) < 1e-12)
            return null;
        double t = ((c.X - a.X) * sz - (c.Z - a.Z) * sx) / denominator, u = ((c.X - a.X) * rz - (c.Z - a.Z) * rx) / denominator;
        return t > 1e-9 && t < 1 - 1e-9 && u > 1e-9 && u < 1 - 1e-9 ? (a.X + t * rx, a.Z + t * rz) : null;
    }

    /// <summary>
    /// Where two parts of the line more than <paramref name="apart"/> from each other along it come nearest each other
    /// (samples only, the ends not run on); null if no two parts are that far apart along it.
    /// </summary>
    public (double Distance, double X1, double Z1, double X2, double Z2)? ClosestApproach(double apart)
    {
        (double, double, double, double, double)? best = null;
        double bestSquared = double.MaxValue;
        for (int i = 0; i < xs.Length; i++)
            for (int j = i + 1; j < xs.Length; j++)
            {
                if (us[j] - us[i] <= apart)
                    continue;
                double d = (xs[i] - xs[j]) * (xs[i] - xs[j]) + (zs[i] - zs[j]) * (zs[i] - zs[j]);
                if (d < bestSquared)
                    (bestSquared, best) = (d, (Math.Sqrt(d), xs[i], zs[i], xs[j], zs[j]));
            }
        return best;
    }

    // ---------------------------------------------------------------- where a point is

    Grid GridFor(double reach, double halfWidth)
    {
        foreach (var kept in grids)
            if (kept.Reach == reach && kept.HalfWidth == halfWidth)
                return kept;
        lock (making)
        {
            var current = grids;
            foreach (var kept in current)
                if (kept.Reach == reach && kept.HalfWidth == halfWidth)
                    return kept;
            var grid = new Grid(this, reach, halfWidth);
            grids = [grid, .. current.Take(KeptGrids - 1)];
            return grid;
        }
    }

    /// <summary>
    /// A world point as (u along the line, v across it), for a ramp <paramref name="halfWidth"/> either side of it: of the
    /// places on the line (or its straight run on beyond either end) whose normal passes through the point, the one that
    /// puts it nearest the ramp itself, its band from end to end (so a run on beyond one end never takes a point from a
    /// nearer part of the ramp). Exact everywhere; quick within <paramref name="reach"/> of the line, by the grid.
    /// </summary>
    public (double U, double V) Local(double x, double z, double reach, double halfWidth)
    {
        var grid = GridFor(reach, halfWidth);
        if (grid.Locate(x, z, out var local))
            return local;
        return Locate(x, z, halfWidth);
    }

    /// <summary>
    /// As <see cref="Local"/>, but with u smoothed beyond the ramp's sides: where other places on the line put the point
    /// nearly as near the ramp (within a window that opens from nothing at the ramp's side to <see cref="SmoothWindow"/>
    /// units), u is their weighted average, so it moves smoothly from one part of the line to another (between the legs
    /// of a switchback, inside a tight bend, where a run on beyond one end meets another part) instead of jumping. Where
    /// one place is clearly nearest, as on the ramp itself, u is exact. Beyond <paramref name="reach"/>, exact.
    /// </summary>
    public (double U, double V) SurfaceLocal(double x, double z, double reach, double halfWidth)
    {
        var grid = GridFor(reach, halfWidth);
        return grid.Smoothed(x, z, out var local) ? local : Locate(x, z, halfWidth);
    }

    /// <summary>True when the point is further than <paramref name="reach"/> from the line (and its straight run on), worked out quickly.</summary>
    public bool IsFar(double x, double z, double reach, double halfWidth) => GridFor(reach, halfWidth).IsFar(x, z);

    /// <summary>How far (u, v) is from a ramp's band, <paramref name="halfWidth"/> either side of the line from end to end.</summary>
    static double OutsideBand(double u, double v, double length, double halfWidth)
    {
        double du = Math.Max(0, Math.Max(-u, u - length)), dv = Math.Max(0, Math.Abs(v) - halfWidth);
        return Math.Sqrt(du * du + dv * dv);
    }

    /// <summary>Whether (u, v) puts a point nearer the band than the best so far (ties: nearer the line).</summary>
    static bool Better(double outside, double v, double bestOutside, double bestV) =>
        outside < bestOutside - 1e-9 || (outside <= bestOutside + 1e-9 && Math.Abs(v) < Math.Abs(bestV));

    /// <summary>Where a point is (see <see cref="Local"/>), by every sample and the straight runs on beyond the ends.</summary>
    (double U, double V) Locate(double x, double z, double halfWidth)
    {
        double best = double.MaxValue, bestU = 0, bestV = double.MaxValue;
        for (int i = 0; i < xs.Length - 1; i++)
            if (AlongNormal(xs[i], zs[i], xs[i + 1], zs[i + 1], nxs[i], nzs[i], nxs[i + 1], nzs[i + 1], x, z, out double t, out double v))
            {
                double u = us[i] + t * (us[i + 1] - us[i]), outside = OutsideBand(u, v, Length, halfWidth);
                if (Better(outside, v, best, bestV))
                    (best, bestU, bestV) = (outside, u, v);
            }
        foreach (bool first in new[] { true, false })
        {
            var (dx, dz) = first ? startDirection : endDirection;
            double ox = first ? xs[0] : xs[^1], oz = first ? zs[0] : zs[^1];
            double along = (x - ox) * dx + (z - oz) * dz, v = -(x - ox) * dz + (z - oz) * dx;
            if (first ? along >= 0 : along <= 0)
                continue;
            double u = (first ? 0 : Length) + along, outside = OutsideBand(u, v, Length, halfWidth);
            if (Better(outside, v, best, bestV))
                (best, bestU, bestV) = (outside, u, v);
        }
        if (best < double.MaxValue)
            return (bestU, bestV);
        // No normal passes through it (past every bend's radius): the nearest point instead.
        for (int i = 0; i < xs.Length - 1; i++)
        {
            double sx = xs[i + 1] - xs[i], sz = zs[i + 1] - zs[i], length = us[i + 1] - us[i];
            if (length <= 0)
                continue;
            (sx, sz) = (sx / length, sz / length);
            double t = Math.Clamp((x - xs[i]) * sx + (z - zs[i]) * sz, 0, length);
            double qx = x - xs[i] - t * sx, qz = z - zs[i] - t * sz, d = Math.Sqrt(qx * qx + qz * qz);
            if (d < best)
                (best, bestU, bestV) = (d, us[i] + t, sx * qz - sz * qx < 0 ? -d : d);
        }
        return (bestU, bestV);
    }

    /// <summary>
    /// Where, on the segment from a to b (normals na and nb at its ends, turning evenly between), the normal passes
    /// through the point p: the fraction t along it, and v, how far along that normal p is. The nearer to p of up to two
    /// such places; false if there is none.
    /// </summary>
    static bool AlongNormal(double ax, double az, double bx, double bz, double nax, double naz, double nbx, double nbz,
        double px, double pz, out double t, out double v)
    {
        // The normal at t, N = na + t m, is parallel to p - (a + t d): cross(N, p - a - t d) = 0, a quadratic in t.
        double dx = bx - ax, dz = bz - az, mx = nbx - nax, mz = nbz - naz, wx = px - ax, wz = pz - az;
        double qa = -(mx * dz - mz * dx), qb = (mx * wz - mz * wx) - (nax * dz - naz * dx), qc = nax * wz - naz * wx;
        (t, v) = (0, 0);
        double r1, r2;
        if (Math.Abs(qa) < 1e-12)
        {
            if (Math.Abs(qb) < 1e-12)
                return false;
            r1 = r2 = -qc / qb;
        }
        else
        {
            double disc = qb * qb - 4 * qa * qc;
            if (disc < 0)
                return false;
            double q = -0.5 * (qb + (qb < 0 ? -1 : 1) * Math.Sqrt(disc));
            r1 = q / qa;
            r2 = q != 0 ? qc / q : r1;
        }
        bool found = false;
        for (int k = 0; k < 2; k++)
        {
            double root = k == 0 ? r1 : r2;
            if (root < -1e-9 || root > 1 + 1e-9)
                continue;
            double s = Math.Clamp(root, 0, 1);
            var (nx, nz) = Unit(nax + s * mx, naz + s * mz);
            double candidate = (px - ax - s * dx) * nx + (pz - az - s * dz) * nz;
            if (!found || Math.Abs(candidate) < Math.Abs(v))
                (t, v, found) = (s, candidate, true);
        }
        return found;
    }

    /// <summary>
    /// The lookup for one reach and half width. The line runs on straight beyond each end a little further than the
    /// reach, and is cut into segments between samples. The grid covers the line's box grown by the reach, in 16-unit
    /// cells; each cell keeps the segments that can matter to some point in it, or nothing when every point in it is
    /// further than the reach from the line. Cells are sorted in 8 by 8 blocks first, so a long line doesn't measure
    /// every segment from every cell.
    /// <para>
    /// Which segments can matter: for a point p, the place on the line nearest the core line (between the ends) is no
    /// further from the band than p is from that line, d(p); every place within the smoothing window of the best is then
    /// within d(p) + window + half width of p along its normal, so its segment is that near p. A cell keeps the segments
    /// within its core-nearest distance + its diameter + window + half width of its centre (and a little more for the
    /// normal leaning off the perpendicular).
    /// </para>
    /// </summary>
    sealed class Grid
    {
        const double Cell = 16;
        const int Block = 8;
        const double Slack = 4;

        public readonly double Reach, HalfWidth;
        readonly double length;
        readonly double x0, z0;
        readonly int columns, rows;
        readonly int coreFirst, coreLast;           // the segments between the ends
        readonly double[] vx, vz, vu, vnx, vnz;     // vertices: position, how far along, normal
        readonly double[] sx, sz, sl;               // segments (vertex i to i + 1): unit direction and length
        readonly int[]?[] cells;

        public Grid(RampPath path, double reach, double halfWidth)
        {
            (Reach, HalfWidth, length) = (reach, halfWidth, path.Length);
            int extra = (int)Math.Ceiling((reach + 2 * Cell) / SampleSpacing);
            int core = path.xs.Length, count = core + 2 * extra;
            (vx, vz, vu, vnx, vnz) = (new double[count], new double[count], new double[count], new double[count], new double[count]);
            var (sdx, sdz) = path.startDirection;
            var (edx, edz) = path.endDirection;
            for (int k = 0; k < extra; k++)
            {
                double t = (extra - k) * SampleSpacing; // furthest first
                (vx[k], vz[k], vu[k], vnx[k], vnz[k]) = (path.xs[0] - t * sdx, path.zs[0] - t * sdz, -t, -sdz, sdx);
                double e = (k + 1) * SampleSpacing;
                int j = extra + core + k;
                (vx[j], vz[j], vu[j], vnx[j], vnz[j]) = (path.xs[^1] + e * edx, path.zs[^1] + e * edz, path.Length + e, -edz, edx);
            }
            Array.Copy(path.xs, 0, vx, extra, core);
            Array.Copy(path.zs, 0, vz, extra, core);
            Array.Copy(path.us, 0, vu, extra, core);
            Array.Copy(path.nxs, 0, vnx, extra, core);
            Array.Copy(path.nzs, 0, vnz, extra, core);
            (coreFirst, coreLast) = (extra, extra + core - 2);
            int segments = count - 1;
            (sx, sz, sl) = (new double[segments], new double[segments], new double[segments]);
            for (int i = 0; i < segments; i++)
            {
                double dx = vx[i + 1] - vx[i], dz = vz[i + 1] - vz[i], l = Math.Sqrt(dx * dx + dz * dz);
                (sx[i], sz[i], sl[i]) = l > 0 ? (dx / l, dz / l, l) : (1, 0, 0);
            }

            var (minX, minZ, maxX, maxZ) = path.SampleBounds;
            (x0, z0) = (minX - reach, minZ - reach);
            columns = (int)Math.Ceiling((maxX - minX + 2 * reach) / Cell) + 1;
            rows = (int)Math.Ceiling((maxZ - minZ + 2 * reach) / Cell) + 1;
            cells = new int[]?[columns * rows];
            int blockColumns = (columns + Block - 1) / Block, blockRows = (rows + Block - 1) / Block;
            double cellHalf = Cell * Math.Sqrt(2) / 2, blockHalf = Block * Cell * Math.Sqrt(2) / 2;
            var all = Enumerable.Range(0, segments).ToArray();
            Parallel.For(0, blockColumns * blockRows, b =>
            {
                int bx = b % blockColumns, bz = b / blockColumns;
                double cx = x0 + (bx + 0.5) * Block * Cell, cz = z0 + (bz + 0.5) * Block * Cell;
                // A segment that matters to a cell in the block is this near the block's centre (the cell's bound, moved
                // out by how far the cell's centre can be from the block's).
                var candidates = Candidates(all, cx, cz, blockHalf);
                if (candidates == null)
                    return; // every cell in the block is far
                for (int iz = bz * Block; iz < Math.Min(rows, (bz + 1) * Block); iz++)
                    for (int ix = bx * Block; ix < Math.Min(columns, (bx + 1) * Block); ix++)
                        cells[iz * columns + ix] = Candidates(candidates, x0 + (ix + 0.5) * Cell, z0 + (iz + 0.5) * Cell, cellHalf);
            });
        }

        /// <summary>
        /// The segments that can matter anywhere within <paramref name="half"/> of (cx, cz) (see the class), or null if
        /// none is within reach of it.
        /// </summary>
        int[]? Candidates(int[] from, double cx, double cz, double half)
        {
            var distances = new double[from.Length];
            double nearest = double.MaxValue, nearestCore = double.MaxValue;
            for (int k = 0; k < from.Length; k++)
            {
                double d = distances[k] = SegmentDistance(from[k], cx, cz);
                nearest = Math.Min(nearest, d);
                if (from[k] >= coreFirst && from[k] <= coreLast)
                    nearestCore = Math.Min(nearestCore, d);
            }
            if (nearest - half > Reach)
                return null;
            double bound = nearestCore + 2 * half + SmoothWindow + HalfWidth + Slack;
            return from.Where((_, k) => distances[k] <= bound).ToArray();
        }

        double SegmentDistance(int i, double x, double z)
        {
            double t = Math.Clamp((x - vx[i]) * sx[i] + (z - vz[i]) * sz[i], 0, sl[i]);
            double qx = x - vx[i] - t * sx[i], qz = z - vz[i] - t * sz[i];
            return Math.Sqrt(qx * qx + qz * qz);
        }

        int[]? CellAt(double x, double z)
        {
            int ix = (int)Math.Floor((x - x0) / Cell), iz = (int)Math.Floor((z - z0) / Cell);
            return ix < 0 || iz < 0 || ix >= columns || iz >= rows ? null : cells[iz * columns + ix];
        }

        public bool IsFar(double x, double z) => CellAt(x, z) == null;

        const int MaxPlaces = 32;

        /// <summary>
        /// The places in a cell whose normal passes through the point (one per place, however many segments meet there),
        /// each with how far it puts the point from the band; the best of them first. None when no normal passes through it.
        /// </summary>
        int Places(int[] cell, double x, double z, Span<(double U, double V, double Outside)> places)
        {
            int count = 0, best = -1;
            foreach (int i in cell)
            {
                // The normal can only pass through p on this segment if p isn't on the same side of the normals at both its ends.
                double fa = vnx[i] * (z - vz[i]) - vnz[i] * (x - vx[i]), fb = vnx[i + 1] * (z - vz[i + 1]) - vnz[i + 1] * (x - vx[i + 1]);
                if (fa * fb > 0 && Math.Abs(fa) > 1e-6 && Math.Abs(fb) > 1e-6)
                    continue;
                if (!AlongNormal(vx[i], vz[i], vx[i + 1], vz[i + 1], vnx[i], vnz[i], vnx[i + 1], vnz[i + 1], x, z, out double t, out double v))
                    continue;
                double u = vu[i] + t * sl[i];
                bool seen = false;
                for (int k = 0; k < count && !seen; k++)
                    seen = Math.Abs(places[k].U - u) < 1e-6;
                if (seen || count == MaxPlaces)
                    continue;
                places[count] = (u, v, OutsideBand(u, v, length, HalfWidth));
                if (best < 0 || Better(places[count].Outside, v, places[best].Outside, places[best].V))
                    best = count;
                count++;
            }
            if (best > 0)
                (places[0], places[best]) = (places[best], places[0]);
            return count;
        }

        /// <summary>Where a point is (see <see cref="RampPath.Local"/>), if its cell keeps any segments (false: further than the reach, or off the grid).</summary>
        public bool Locate(double x, double z, out (double U, double V) local)
        {
            local = default;
            var cell = CellAt(x, z);
            if (cell == null)
                return false;
            Span<(double U, double V, double Outside)> places = stackalloc (double, double, double)[MaxPlaces];
            local = Places(cell, x, z, places) > 0 ? (places[0].U, places[0].V) : Nearest(cell, x, z);
            return true;
        }

        /// <summary>The smoothed (u, v) of <see cref="RampPath.SurfaceLocal"/>; false off the grid or further than the reach.</summary>
        public bool Smoothed(double x, double z, out (double U, double V) local)
        {
            local = default;
            var cell = CellAt(x, z);
            if (cell == null)
                return false;
            Span<(double U, double V, double Outside)> places = stackalloc (double, double, double)[MaxPlaces];
            int count = Places(cell, x, z, places);
            if (count == 0)
            {
                local = Nearest(cell, x, z);
                return true;
            }
            double best = places[0].Outside, window = Math.Min(SmoothWindow, best);
            double sum = 1, sumU = places[0].U;
            if (window > 0)
                for (int k = 1; k < count; k++)
                {
                    double past = places[k].Outside - best;
                    if (past >= window)
                        continue;
                    double weight = 0.5 * (1 + Math.Cos(Math.PI * Math.Max(0, past) / window));
                    sum += weight;
                    sumU += weight * places[k].U;
                }
            local = (sumU / sum, places[0].V);
            return true;
        }

        /// <summary>The nearest point of the cell's segments, where no normal passes through the point.</summary>
        (double U, double V) Nearest(int[] cell, double x, double z)
        {
            double best = double.MaxValue, u = 0, v = 0;
            foreach (int i in cell)
            {
                double t = Math.Clamp((x - vx[i]) * sx[i] + (z - vz[i]) * sz[i], 0, sl[i]);
                double qx = x - vx[i] - t * sx[i], qz = z - vz[i] - t * sz[i], d = Math.Sqrt(qx * qx + qz * qz);
                if (d < best)
                    (best, u, v) = (d, vu[i] + t, sx[i] * qz - sz[i] * qx < 0 ? -d : d);
            }
            return (u, v);
        }
    }

    // ---------------------------------------------------------------- equality

    public bool Equals(RampPath? other) => other != null && points.AsSpan().SequenceEqual(other.points);

    public override bool Equals(object? obj) => Equals(obj as RampPath);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var p in points)
            hash.Add(p);
        return hash.ToHashCode();
    }

    public override string ToString() => $"path through {points.Length} points, {Length:0.#} long";
}
