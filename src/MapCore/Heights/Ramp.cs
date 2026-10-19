namespace MapCore.Heights;

/// <summary>
/// A ramp in game units: a band along its centre line from its first end (X1, Z1) to its second end (X2, Z2),
/// <see cref="Width"/> across, with an <see cref="Edge"/> margin all round over which it blends into the ground. The
/// centre line is straight, or with a <see cref="Path"/> bends through points between the ends (<see cref="Through"/>).
/// Along the ramp, u runs from 0 at the first end to <see cref="Length"/> at the second; across it, v is 0 on the
/// centre line.
/// </summary>
public readonly record struct Ramp(double X1, double Z1, double X2, double Z2, double Width, double Edge)
{
    /// <summary>The curve the centre line follows when the ramp bends; null for a straight ramp.</summary>
    public RampPath? Path { get; init; }

    /// <summary>
    /// For the mirror image of another ramp: its rocky noise is the other's, mirrored (the noise is fixed to the map, so
    /// without this the two would be rough in different places). Null: the map's own.
    /// </summary>
    public MapMirror? NoiseMirror { get; init; }

    /// <summary>Where the noise at a point is read: the point itself, or its mirror image for a mirrored ramp.</summary>
    public (double X, double Z) NoisePoint(double x, double z) => NoiseMirror is MapMirror mirror ? mirror.Map(x, z) : (x, z);

    /// <summary>A ramp through <paramref name="points"/> (first end, bends, last end); straight with just its two ends.</summary>
    public static Ramp Through(IReadOnlyList<(double X, double Z)> points, double width, double edge)
    {
        if (points.Count < 2)
            throw new ArgumentException("A ramp needs at least its two ends.", nameof(points));
        var (first, last) = (points[0], points[^1]);
        var ramp = new Ramp(first.X, first.Z, last.X, last.Z, width, edge);
        return points.Count == 2 ? ramp : ramp with { Path = new RampPath(points) };
    }

    /// <summary>The points the centre line runs through: the first end, any bends, the second end.</summary>
    public IReadOnlyList<(double X, double Z)> Points => Path?.Points ?? new[] { (X1, Z1), (X2, Z2) };

    /// <summary>The points it bends through between its ends (none for a straight ramp).</summary>
    public IReadOnlyList<(double X, double Z)> Bends => Path is { } path ? [.. path.Points.Skip(1).Take(path.Points.Count - 2)] : [];

    /// <summary>Its length along the centre line.</summary>
    public double Length => Path?.Length ?? Math.Sqrt((X2 - X1) * (X2 - X1) + (Z2 - Z1) * (Z2 - Z1));

    /// <summary>How far from the centre line a bending ramp's lookups are quick and exact: past the edge by a margin.</summary>
    double Reach => Width / 2 + Edge + RampPath.Margin;

    (double Dx, double Dz) Direction
    {
        get
        {
            double length = Length;
            return ((X2 - X1) / length, (Z2 - Z1) / length);
        }
    }

    /// <summary>A world point as (u along the ramp from the first end, v across it): by the nearest point of a bending centre line.</summary>
    public (double U, double V) Local(double x, double z)
    {
        if (Path is { } path)
            return path.Local(x, z, Reach, Width / 2);
        var (dx, dz) = Direction;
        double rx = x - X1, rz = z - Z1;
        return (rx * dx + rz * dz, -rx * dz + rz * dx);
    }

    /// <summary>
    /// A world point as the ramp's surface and texture see it: <see cref="Local"/>, except that beyond the sides of a
    /// bending ramp u is smoothed, so the blend into the ground doesn't step where two parts of it are about equally
    /// near (<see cref="RampPath.SurfaceLocal"/>). The same as <see cref="Local"/> for a straight ramp.
    /// </summary>
    public (double U, double V) SurfaceLocal(double x, double z) => Path is { } path ? path.SurfaceLocal(x, z, Reach, Width / 2) : Local(x, z);

    /// <summary>The world point at (u, v); the inverse of <see cref="Local"/>.</summary>
    public (double X, double Z) World(double u, double v)
    {
        if (Path is { } path)
            return path.World(u, v);
        var (dx, dz) = Direction;
        return (X1 + u * dx - v * dz, Z1 + u * dz + v * dx);
    }

    /// <summary>
    /// How far a point lies outside the ramp's band (0 on it). For a bending ramp, a point well beyond the edge (further
    /// than <see cref="RampPath.Margin"/> past it) is not measured: the answer is infinity.
    /// </summary>
    public double DistanceOutside(double x, double z)
    {
        if (Path is { } path && path.IsFar(x, z, Reach, Width / 2))
            return double.PositiveInfinity;
        var (u, v) = Local(x, z);
        double du = Math.Max(0, Math.Max(-u, u - Length)), dv = Math.Max(0, Math.Abs(v) - Width / 2);
        return Math.Sqrt(du * du + dv * dv);
    }

    /// <summary>1 on the ramp, easing to 0 at <see cref="Edge"/> outside it along a cosine, as a brush does.</summary>
    public double Weight(double x, double z)
    {
        double d = DistanceOutside(x, z);
        if (d <= 0)
            return 1;
        if (d >= Edge)
            return 0;
        return 0.5 * (1 + Math.Cos(Math.PI * d / Edge));
    }

    /// <summary>
    /// The height the ramp leaves at a point where the ground was <paramref name="old"/>: its surface, blended into the
    /// ground by <see cref="Weight"/>. With <paramref name="roughness"/> (0-1) the blend down the sides is bent by rocky
    /// noise, most half-way down the side and none on the ramp itself or past its edge, so the sides look like broken
    /// rock rather than a smooth bank. The ends, where units get on and off, stay smooth (the noise fades in along the
    /// sides). The result always lies between the old ground and the ramp's surface.
    /// </summary>
    public double Shaped(double old, double x, double z, double h1, double h2, double roughness = 0)
    {
        double w = Weight(x, z);
        if (w <= 0)
            return old;
        if (w < 1 && roughness > 0)
        {
            double u = SurfaceLocal(x, z).U;
            double fade = Math.Clamp(Math.Min(u, Length - u) / Math.Max(1, Math.Min(Edge, Length / 4)), 0, 1);
            var (nx, nz) = NoisePoint(x, z);
            if (fade > 0)
                w = Math.Clamp(w + Math.Clamp(roughness, 0, 1) * fade * 1.5 * RockNoise(nx, nz) * 4 * w * (1 - w), 0, 1);
        }
        return old + (SurfaceAt(x, z, h1, h2) - old) * w;
    }

    /// <summary>Rocky noise in about [-1, 1], fixed to the map: lumps about 40 units across, broken up by finer ones.</summary>
    public static double RockNoise(double x, double z) =>
        0.65 * TerrainNoise.Sample(x / 40, z / 40) + 0.35 * TerrainNoise.Sample(x / 13 + 71.3, z / 13 + 19.7);

    /// <summary>
    /// The ramp's surface height at a point: a straight line from <paramref name="h1"/> to <paramref name="h2"/> along
    /// the centre line, level across it and beyond the ends.
    /// </summary>
    public double SurfaceAt(double x, double z, double h1, double h2) => h1 + (h2 - h1) * Math.Clamp(SurfaceLocal(x, z).U / Length, 0, 1);

    /// <summary>A straight ramp's corners, grown by <paramref name="grow"/> on every side, in order round it.</summary>
    public (double X, double Z)[] Corners(double grow = 0) =>
    [
        World(-grow, -Width / 2 - grow), World(Length + grow, -Width / 2 - grow),
        World(Length + grow, Width / 2 + grow), World(-grow, Width / 2 + grow),
    ];

    /// <summary>
    /// Its outline, grown by <paramref name="grow"/> on every side, in order round it: the corners of a straight ramp,
    /// or both sides of a bending one along its curve.
    /// </summary>
    public (double X, double Z)[] Outline(double grow = 0) => Path is { } path ? path.Outline(Width / 2 + grow, grow) : Corners(grow);

    /// <summary>
    /// Where the blend into the ground ends (the edge's outer line, where <see cref="Weight"/> reaches 0), traced on a grid
    /// every <paramref name="step"/> units: one or more closed lines, for drawing. Unlike <see cref="Outline"/> grown by
    /// the edge, it never loops, inside a bend tighter than the edge or where the edges of two parts of the ramp overlap.
    /// </summary>
    public List<(double X, double Z)[]> EdgeContour(double step)
    {
        var ramp = this;
        return Contour.Lines((x, z) => ramp.DistanceOutside(x, z) - ramp.Edge, Bounds, step);
    }

    /// <summary>The world box everything the ramp changes lies in (the band and its edge).</summary>
    public (double MinX, double MinZ, double MaxX, double MaxZ) Bounds
    {
        get
        {
            if (Path is { } path)
            {
                // Everything within the edge of the band is within half the width and the edge of the curve.
                var (minX, minZ, maxX, maxZ) = path.SampleBounds;
                double grow = Width / 2 + Edge;
                return (minX - grow, minZ - grow, maxX + grow, maxZ + grow);
            }
            var c = Corners(Edge);
            return (c.Min(p => p.X), c.Min(p => p.Z), c.Max(p => p.X), c.Max(p => p.Z));
        }
    }

    /// <summary>The steepness along the ramp, in degrees, for heights <paramref name="h1"/> to <paramref name="h2"/>.</summary>
    public double SlopeDegrees(double h1, double h2) => Math.Atan2(Math.Abs(h2 - h1), Length) * 180 / Math.PI;

    /// <summary>
    /// A bending ramp's tightest bend: the radius its centre line turns on there, where, and how steep (degrees) its
    /// surface is on the inside of that bend for heights <paramref name="h1"/> to <paramref name="h2"/> (level across, so
    /// the inside, being shorter, is steeper than the centre line). Null for a straight ramp, or one too tight to say.
    /// </summary>
    public (double Radius, double X, double Z, double InsideDegrees)? TightestBend(double h1, double h2)
    {
        if (Path is not { } path)
            return null;
        var (radius, x, z) = path.Tightest();
        if (double.IsInfinity(radius) || radius <= Width / 2)
            return double.IsInfinity(radius) ? null : (radius, x, z, 90);
        double grade = Math.Abs(h2 - h1) / Length * radius / (radius - Width / 2);
        return (radius, x, z, Math.Atan(grade) * 180 / Math.PI);
    }
}
