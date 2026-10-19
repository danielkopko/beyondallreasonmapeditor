using System.Globalization;
using MapCore.Heights;
using MapCore.Imaging;
using MapCore.Smf;
using MapCore.Textures;

namespace MapCore.Editing;

/// <summary>How a ramp is textured.</summary>
public enum RampLook
{
    /// <summary>The texture is left as it was.</summary>
    None = 0,

    /// <summary>The ground beyond each end is copied along the ramp, blended from one end to the other.</summary>
    FromEnds = 1,

    /// <summary>
    /// As ramps are usually painted: rock on the upper ramp, turning to sand or dirt lower down and blending into the
    /// base ground beyond its foot, with rock on its steep sides. Rock and dirt are copied from places on the map.
    /// </summary>
    RockToDirt = 2,
}

/// <summary>
/// Ramps: a slope between two heights, straight or bending, blended into the ground over its edge, and textured.
/// <code>
///   ramp:&lt;x1&gt;,&lt;z1&gt;[;&lt;bend x&gt;,&lt;bend z&gt;...]:&lt;x2&gt;,&lt;z2&gt;:&lt;width&gt;:&lt;h1&gt;,&lt;h2&gt;[:&lt;edge&gt;[:&lt;look&gt;[:&lt;roughness&gt;[:&lt;rock x&gt;,&lt;rock z&gt;[:&lt;dirt x&gt;,&lt;dirt z&gt;]]]]]
/// </code>
/// From the first end to the second, <c>width</c> across. Bends, after the first end and separated by semicolons, make
/// it run straight from point to point, each bend rounded as much as the runs beside it allow (<see cref="RampPath"/>),
/// rising evenly along its length and level across. The line never leaves the shape its points enclose, so a ramp whose
/// points are all on the map is on the map. Heights may be <c>auto</c>: the ground where that end meets
/// it. The edge (the blend into the ground) may be <c>auto</c> (the default): as wide as the largest height step beside
/// the ramp, so the blend averages about 45°. Look: 0 leaves the texture, 1 (the default) fills it from the ends, 2 is
/// rock to dirt (<see cref="RampLook"/>). Roughness (0-1, default 0) breaks up the sides (<see cref="Ramp.Shaped"/>).
/// Rock and dirt, for look 2, are where on the map to copy them from, or <c>auto</c> (the default:
/// <see cref="RampMaterialSources"/>). A tenth argument (lr, tb, diag, anti or turn: <see cref="MapMirror"/>), written
/// for a ramp made as another's mirror image, reads its rocky noise mirrored, so the two match.
/// </summary>
public sealed partial class MapEditSession
{
    public const double MinRampSize = 16, MaxRampEdge = 512, MinAutoEdge = 32;

    /// <summary>The most points (ends and bends) a ramp may run through, and how far apart each must be from the one before.</summary>
    public const int MaxRampPoints = 64;
    public const double MinRampPointGap = 8;

    /// <summary>
    /// A bending ramp's centre line must turn on at least half its width and this (or the inside of the ramp would fold
    /// over), and two parts of it, apart along it, must be at least a width and twice this apart (so the ground between
    /// them can step down from one to the other).
    /// </summary>
    public const double RampBendRoom = 8;

    /// <summary>
    /// How far beyond the ramp's sides the from-the-ends fill fades out (at most its edge). The edge's slopes keep their
    /// own texture: copying ground from beyond the ends onto them smeared it over cliffs.
    /// </summary>
    public const double RampFillFeather = 24;

    /// <summary>A ramp rising less than this has no up or down, so rock to dirt falls back to filling from the ends.</summary>
    public const double LevelRampRise = 8;

    /// <summary>Rock is taken from ground at least this steep, measured over 64 units; dirt from ground between these.</summary>
    public const double RockDegrees = 35, DirtMinDegrees = 6, DirtMaxDegrees = 22;

    /// <summary>The size of the patch around each rock or dirt source whose average colour is used, in game units.</summary>
    public const double MaterialPatch = 64;

    /// <summary>Where the texture and splat map are written: the fill's feather beyond the sides, or the whole edge for rock to dirt.</summary>
    static Ramp FillShape(Ramp ramp, RampLook look) =>
        look == RampLook.RockToDirt ? ramp : ramp with { Edge = Math.Min(ramp.Edge, RampFillFeather) };

    /// <summary>How much wider the auto edge is than the largest height step beside the ramp (1: about 45° on average).</summary>
    const double AutoEdgePerHeight = 1;

    /// <summary>Builds a ramp (one undo step). Numbers are written with invariant formatting; arguments left at their defaults are left out.</summary>
    public string BuildRamp(Ramp ramp, double h1, double h2, RampLook look = RampLook.FromEnds, double roughness = 0,
        (double X, double Z)? rock = null, (double X, double Z)? dirt = null)
    {
        string bends = string.Concat(ramp.Bends.Select(b => Op($";{b.X},{b.Z}")));
        string op = Op($"ramp:{ramp.X1},{ramp.Z1}") + bends + Op($":{ramp.X2},{ramp.Z2}:{ramp.Width}:{h1},{h2}:{ramp.Edge}:{(int)look}");
        if (roughness > 0 || look == RampLook.RockToDirt)
            op += Op($":{roughness}");
        if (look == RampLook.RockToDirt)
            op += ":" + PointText(rock) + ":" + PointText(dirt);
        return Apply(op);
    }

    static string PointText((double X, double Z)? p) => p is (double x, double z) ? Op($"{x},{z}") : "auto";

    /// <summary>The ground height where each end of the ramp meets it: the average across that end.</summary>
    public (double First, double Second) RampEndHeights(Ramp ramp) => (EndHeight(ramp, 0), EndHeight(ramp, ramp.Length));

    double EndHeight(Ramp ramp, double u)
    {
        const int samples = 9;
        double sum = 0;
        for (int i = 0; i < samples; i++)
        {
            var (x, z) = ramp.World(u, (i / (samples - 1.0) - 0.5) * ramp.Width);
            sum += Heights.HeightAt(Math.Clamp(x, 0, MapWidth), Math.Clamp(z, 0, MapHeight));
        }
        return sum / samples;
    }

    /// <summary>
    /// An edge wide enough for the ramp to blend into the ground beside and beyond it without a wall: the largest
    /// difference between the ramp's surface and the ground along its sides and ends (at least 32 units).
    /// </summary>
    public double RampAutoEdge(Ramp ramp, double h1, double h2)
    {
        double worst = 0, length = ramp.Length;
        int along = Math.Max(2, (int)Math.Ceiling(length / 16)), across = Math.Max(2, (int)Math.Ceiling(ramp.Width / 16));
        void Check(double u, double v)
        {
            var (x, z) = ramp.World(u, v);
            if (x < 0 || z < 0 || x > MapWidth || z > MapHeight)
                return;
            // A bending ramp's surface there is known from u (no lookup needed, so the editor's preview stays quick).
            double surface = ramp.Path != null ? h1 + (h2 - h1) * Math.Clamp(u / length, 0, 1) : ramp.SurfaceAt(x, z, h1, h2);
            worst = Math.Max(worst, Math.Abs(surface - Heights.HeightAt(x, z)));
        }
        for (int i = 0; i <= along; i++)
        {
            Check(length * i / along, -ramp.Width / 2);
            Check(length * i / along, ramp.Width / 2);
        }
        for (int i = 0; i <= across; i++)
        {
            Check(0, ramp.Width * (i / (double)across - 0.5));
            Check(length, ramp.Width * (i / (double)across - 0.5));
        }
        return Math.Clamp(Math.Round(worst * AutoEdgePerHeight), MinAutoEdge, MaxRampEdge);
    }

    /// <summary>
    /// How steep it is (degrees) where the ramp would meet the ground at each end, from the heights it would leave
    /// (computed now, without building it), by the engine's slope measure. Across the ramp, every 8 units from the edge
    /// beyond an end to 16 units onto the ramp, the gentlest cell counts (a unit picks its way past single rough cells);
    /// the steepest of those crossings is the answer. Steeper than the ramp when an end isn't on ground at its height
    /// (a step across the whole width) or leads into a cliff.
    /// </summary>
    public (double Start, double End) RampLipDegrees(Ramp ramp, double h1, double h2, double roughness = 0)
    {
        var heights = Heights;
        return LipDegrees(ramp, (x, z) => ramp.Shaped(heights[x, z], x * HeightMap.Spacing, z * HeightMap.Spacing, h1, h2, roughness));
    }

    /// <summary>The same measure on the heights as they are (after the ramp is built).</summary>
    public (double Start, double End) RampLipDegreesAsBuilt(Ramp ramp)
    {
        var heights = Heights;
        return LipDegrees(ramp, (x, z) => heights[x, z]);
    }

    (double Start, double End) LipDegrees(Ramp ramp, Func<int, int, double> height)
    {
        int cellsX = (Heights.Width - 1) / 2, cellsZ = (Heights.Height - 1) / 2;
        double Crossing(double u)
        {
            double gentlest = double.MaxValue;
            for (double v = -ramp.Width / 2 + 8; v <= ramp.Width / 2 - 8 + 1e-9; v += 8)
            {
                var (x, z) = ramp.World(u, v);
                int cx = (int)Math.Floor(x / 16), cz = (int)Math.Floor(z / 16);
                if (cx >= 0 && cz >= 0 && cx < cellsX && cz < cellsZ)
                    gentlest = Math.Min(gentlest, Movement.TerrainSlope.Cell(height, cx, cz));
            }
            return gentlest == double.MaxValue ? 0 : gentlest;
        }
        double Steepest(double from, double to)
        {
            double worst = 0;
            for (double u = from; u <= to + 1e-9; u += 8)
                worst = Math.Max(worst, Crossing(u));
            return Movement.TerrainSlope.ToDegrees(worst);
        }
        return (Steepest(-ramp.Edge, 16), Steepest(ramp.Length - 16, ramp.Length + ramp.Edge));
    }

    /// <summary>
    /// Where the rock-to-dirt look copies rock and dirt from, found near the ramp in the map's own texture (null where
    /// nothing suits). Steepness is measured over 64 units (the engine's 16-unit measure calls rough flat ground steep),
    /// on ground above water and clear of what the ramp changes.
    /// <list type="bullet">
    /// <item>Rock: ground at least 35° steep. Of those, the one whose colour is nearest the typical cliff colour there
    /// (so not a sunlit or shadowed extreme), favouring ones near the ramp's top.</item>
    /// <item>Dirt: ground between 6° and 22° (cliff feet, banks), nearest 14°, favouring ones near the ramp's foot.</item>
    /// </list>
    /// </summary>
    public ((double X, double Z)? Rock, (double X, double Z)? Dirt) RampMaterialSources(Ramp ramp, double h1, double h2)
    {
        const double span = MaterialPatch / 2, step = 32;
        double radius = Math.Max(384, ramp.Length);
        var (cx, cz) = ramp.World(ramp.Length / 2, 0);
        var (lowX, lowZ) = h1 <= h2 ? (ramp.X1, ramp.Z1) : (ramp.X2, ramp.Z2);
        var (highX, highZ) = h1 <= h2 ? (ramp.X2, ramp.Z2) : (ramp.X1, ramp.Z1);
        var heights = Heights;
        var image = TextureImage;
        double perUnit = image.Width / MapWidth;
        var steep = new List<(double X, double Z, double Luma, double Near)>();
        (double X, double Z)? dirt = null;
        double bestDirt = double.MaxValue;
        for (double z = Math.Round(cz - radius); z <= cz + radius; z += step)
            for (double x = Math.Round(cx - radius); x <= cx + radius; x += step)
            {
                if (x < span || z < span || x > MapWidth - span || z > MapHeight - span || ramp.DistanceOutside(x, z) < ramp.Edge + span)
                    continue;
                double h = heights.HeightAt(x, z), west = heights.HeightAt(x - span, z), east = heights.HeightAt(x + span, z);
                double north = heights.HeightAt(x, z - span), south = heights.HeightAt(x, z + span);
                if (Math.Min(h, Math.Min(Math.Min(west, east), Math.Min(north, south))) <= 0)
                    continue; // under water, or reaching into it
                double gx = (east - west) / (2 * span), gz = (south - north) / (2 * span);
                double degrees = Math.Atan(Math.Sqrt(gx * gx + gz * gz)) * 180 / Math.PI;
                if (degrees >= RockDegrees)
                {
                    double luma = 0;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                            luma += Luma(image.GetPixel(Math.Clamp((int)((x + dx * 8) * perUnit), 0, image.Width - 1),
                                Math.Clamp((int)((z + dz * 8) * perUnit), 0, image.Height - 1)));
                    steep.Add((x, z, luma / 9, Math.Sqrt((x - highX) * (x - highX) + (z - highZ) * (z - highZ)) / radius));
                }
                else if (degrees is >= DirtMinDegrees and <= DirtMaxDegrees)
                {
                    double score = Math.Abs(degrees - 14) + 8 * Math.Sqrt((x - lowX) * (x - lowX) + (z - lowZ) * (z - lowZ)) / radius;
                    if (score < bestDirt)
                        (bestDirt, dirt) = (score, (x, z));
                }
            }
        (double X, double Z)? rock = null;
        if (steep.Count > 0)
        {
            var lumas = steep.Select(s => s.Luma).Order().ToList();
            double typical = lumas[lumas.Count / 2];
            var best = steep.MinBy(s => Math.Abs(s.Luma - typical) / Math.Max(1, typical) * 4 + s.Near);
            rock = (best.X, best.Z);
        }
        return (rock, dirt);
    }

    sealed record RampRequest(Ramp Ramp, double H1, double H2, RampLook Look, double Roughness,
        (double X, double Z)? Rock, (double X, double Z)? Dirt, string? LookNote);

    /// <summary>Reads a ramp operation's arguments, resolving <c>auto</c> against the map as it is now.</summary>
    RampRequest ParseRamp(string[] args)
    {
        const string usage = "ramp needs <x1>,<z1>[;<bend x>,<bend z>...]:<x2>,<z2>:<width>:<h1>,<h2>[:<edge>[:<look 0|1|2>[:<roughness 0-1>[:<rock x>,<rock z>[:<dirt x>,<dirt z>]]]]] " +
                             "(heights, edge, rock and dirt may be 'auto').";
        if (args.Length is < 4 or > 10)
            throw new InvalidOperationException(usage);
        var firstAndBends = args[0].Split(';');
        if (firstAndBends.Length + 1 > MaxRampPoints)
            throw new InvalidOperationException($"A ramp runs through at most {MaxRampPoints} points, ends included (this one has {firstAndBends.Length + 1}).");
        var points = firstAndBends.Select(Point).Append(Point(args[1])).ToList();
        double width = Number(args[2]);
        for (int i = 1; i < points.Count && points.Count > 2; i++)
        {
            double gap = Math.Sqrt((points[i].X - points[i - 1].X) * (points[i].X - points[i - 1].X) + (points[i].Z - points[i - 1].Z) * (points[i].Z - points[i - 1].Z));
            if (gap < MinRampPointGap)
                throw new InvalidOperationException(FormattableString.Invariant(
                    $"A ramp's points must be at least {MinRampPointGap} units apart: ({points[i - 1].X:0}, {points[i - 1].Z:0}) and ({points[i].X:0}, {points[i].Z:0}) are {gap:0.#}."));
        }
        var shape = Ramp.Through(points, width, 0);
        // How it bends first: a bend turning straight back leaves it no length, which says less.
        if (width >= MinRampSize && RampShapeProblem(shape) is string problem)
            throw new InvalidOperationException(problem);
        if (shape.Length < MinRampSize || width < MinRampSize)
            throw new InvalidOperationException($"A ramp must be at least {MinRampSize} units long and wide (this one is {shape.Length:0.#} long, {width:0.#} wide).");
        for (int i = 0; i < points.Count; i++)
        {
            var (x, z) = points[i];
            if (x < 0 || z < 0 || x > MapWidth || z > MapHeight)
                throw new InvalidOperationException($"The ramp's {(i == 0 || i == points.Count - 1 ? "end" : "bend")} ({x:0}, {z:0}) is off the map.");
        }
        var heights = args[3].Split(',');
        if (heights.Length != 2)
            throw new InvalidOperationException(usage);
        var (ground1, ground2) = RampEndHeights(shape);
        double h1 = heights[0].Trim() == "auto" ? ground1 : Number(heights[0]);
        double h2 = heights[1].Trim() == "auto" ? ground2 : Number(heights[1]);
        double edge = args.Length < 5 || args[4].Trim() == "auto" ? RampAutoEdge(shape, h1, h2) : Number(args[4]);
        if (edge is < 0 or > MaxRampEdge)
            throw new InvalidOperationException($"A ramp's edge must be between 0 and {MaxRampEdge} units, not {edge}.");
        var look = args.Length < 6 ? RampLook.FromEnds : args[5].Trim() switch
        {
            "0" => RampLook.None,
            "1" => RampLook.FromEnds,
            "2" => RampLook.RockToDirt,
            var other => throw new InvalidOperationException($"A ramp's look is 0 (leave the texture), 1 (from the ends) or 2 (rock to dirt), not '{other}'."),
        };
        double roughness = args.Length < 7 ? 0 : Number(args[6]);
        if (roughness is < 0 or > 1)
            throw new InvalidOperationException($"A ramp's roughness is between 0 and 1, not {roughness}.");
        (double X, double Z)? Source(int i) => args.Length <= i || args[i].Trim() == "auto" ? null : Point(args[i]);
        // A tenth argument, written when a ramp is made as another's mirror image: its noise is the other's, mirrored.
        var noise = args.Length == 10 ? new MapMirror(MapMirror.ParseKind(args[9]), MapWidth, MapHeight) : (MapMirror?)null;
        var ramp = shape with { Edge = edge, NoiseMirror = noise };
        (double X, double Z)? rock = Source(7), dirt = Source(8);
        string? note = null;
        if (look == RampLook.RockToDirt)
        {
            if (Math.Abs(h2 - h1) < LevelRampRise)
                (look, note) = (RampLook.FromEnds, "level, so no rock to dirt: filled from the ends");
            else
            {
                if (rock == null || dirt == null)
                {
                    var found = RampMaterialSources(ramp, h1, h2);
                    rock ??= found.Rock;
                    dirt ??= found.Dirt;
                }
                if (rock == null)
                    (look, note) = (RampLook.FromEnds, "no cliff near it to take rock from: filled from the ends");
            }
        }
        return new RampRequest(ramp, h1, h2, look, roughness, rock, dirt, note);
    }

    /// <summary>
    /// Why a bending ramp can't be built as it is, or null if it can (always, for a straight one): a bend so tight that
    /// the inside of the ramp would fold over, or two parts of it, apart along it, so near each other that their surfaces
    /// would meet (see <see cref="RampBendRoom"/>).
    /// </summary>
    public static string? RampShapeProblem(Ramp ramp)
    {
        if (ramp.Path is not { } path)
            return null;
        double room = ramp.Width / 2 + RampBendRoom;
        var (radius, x, z) = path.Tightest();
        if (radius < 1e-6)
            return FormattableString.Invariant($"The ramp turns straight back on itself at ({x:0}, {z:0}): use two bends to take it round.");
        if (radius < room)
            return FormattableString.Invariant(
                $"The ramp bends too tightly at ({x:0}, {z:0}): its centre line turns on a radius of {radius:0}, and a ramp {ramp.Width:0} wide needs at least {room:0}. Give that bend longer runs either side (move the points beside it away), turn it less, or narrow the ramp.");
        if (path.ClosestApproach(Math.PI * room) is var (distance, x1, z1, x2, z2) && distance < 2 * room)
            return FormattableString.Invariant(
                $"Two parts of the ramp come within {distance:0} units of each other, at ({x1:0}, {z1:0}) and ({x2:0}, {z2:0}): a ramp {ramp.Width:0} wide needs {2 * room:0} between them. Move them apart, or narrow the ramp.");
        return null;
    }

    bool TryParseRamp(string[] args, out RampRequest request)
    {
        try
        {
            request = ParseRamp(args);
            return true;
        }
        catch (InvalidOperationException)
        {
            request = null!;
            return false;
        }
    }

    /// <summary>The texture pixels a ramp can touch.</summary>
    PixelRect RampTextureRegion(Ramp ramp, RampLook look)
    {
        double perUnit = Texture.Width / MapWidth;
        var (minX, minZ, maxX, maxZ) = FillShape(ramp, look).Bounds;
        return PixelRect.FromEdges((int)Math.Floor(minX * perUnit), (int)Math.Floor(minZ * perUnit),
            (int)Math.Ceiling(maxX * perUnit) + 1, (int)Math.Ceiling(maxZ * perUnit) + 1).Intersect(Texture.Bounds);
    }

    static WorldRect RampArea(Ramp ramp, RampLook look)
    {
        var (minX, minZ, maxX, maxZ) = FillShape(ramp, look).Bounds;
        return new WorldRect(minX, minZ, maxX - minX, maxZ - minZ);
    }

    /// <summary>What a ramp operation can change, captured before it runs (the same footprint <see cref="RampCore"/> writes).</summary>
    List<Snapshot> RampSnapshots(RampRequest request)
    {
        var ramp = request.Ramp;
        var snapshots = new List<Snapshot> { new HeightSnapshot(Heights, Heights.VertexBounds(ramp)) };
        if (request.Look != RampLook.None)
        {
            var region = RampTextureRegion(ramp, request.Look);
            snapshots.Add(new TextureSnapshot(this, region));
            snapshots.Add(new MinimapSnapshot(this, [region]));
            if (TrySplat(out var entry))
                snapshots.Add(new DdsRegionSnapshot(this, entry, [RampArea(ramp, request.Look)], MapLayers.Splat));
        }
        return snapshots;
    }

    string RampCore(string[] args)
    {
        var request = ParseRamp(args);
        var (ramp, h1, h2, look, roughness) = (request.Ramp, request.H1, request.H2, request.Look, request.Roughness);
        var (startLip, endLip) = RampLipDegrees(ramp, h1, h2, roughness); // from the ground as it is before the ramp
        // Rock and dirt are read before anything changes; the fill reads the texture as it was, too.
        var materials = look == RampLook.RockToDirt ? Materials(request) : null;
        var touched = Heights.ApplyRamp(ramp, h1, h2, roughness);
        int bends = ramp.Bends.Count;
        string through = ramp.Path != null ? $" through {bends} bend{(bends == 1 ? "" : "s")}" : "";
        var result = FormattableString.Invariant(
                         $"ramp from ({ramp.X1:0}, {ramp.Z1:0}) at {h1:0.#}{through} to ({ramp.X2:0}, {ramp.Z2:0}) at {h2:0.#}, {ramp.Width:0} wide, edge {ramp.Edge:0}") +
                     (roughness > 0 ? FormattableString.Invariant($", rough {roughness:0.##}") : "") +
                     FormattableString.Invariant($": {ramp.SlopeDegrees(h1, h2):0.#}° ({Math.Abs(h2 - h1):0} over {ramp.Length:0}), {touched.Width * touched.Height:N0} vertices");
        if (ramp.TightestBend(h1, h2) is var (radius, bendX, bendZ, inside))
            result += FormattableString.Invariant($"; its tightest bend, at ({bendX:0}, {bendZ:0}), turns on {radius:0} units, {inside:0.#}° on its inside");
        var verdicts = Movement.RampPassability.Check(ramp.Length, h1, h2, startLip, endLip);
        result += $"; climbable by {Climbers(verdicts.Where(v => v.Climbs))}";
        if (verdicts.Any(v => !v.Climbs))
            result += $"; too steep for {Climbers(verdicts.Where(v => !v.Climbs))}";
        foreach (var v in verdicts.Where(v => v.BlockedAt != null).GroupBy(v => v.BlockedAt))
            result += string.Create(CultureInfo.InvariantCulture,
                $" (where it meets the ground at the {v.Key}, {v.First().BlockedDegrees:0}°: {Climbers(v)})");
        if (look == RampLook.None)
            return result;
        var update = FillRampTexture(ramp, look, materials, TextureImage);
        result += look == RampLook.RockToDirt
            ? string.Create(CultureInfo.InvariantCulture, $"; textured rock to dirt ({update.TilesRewritten} tiles): rock from ({request.Rock!.Value.X:0}, {request.Rock.Value.Z:0}), ") +
              (request.Dirt is (double dx, double dz) ? string.Create(CultureInfo.InvariantCulture, $"dirt from ({dx:0}, {dz:0})") : "dirt mixed from the rock and the ground at its foot")
            : $"; texture filled from the ends ({update.TilesRewritten} tiles)";
        if (request.LookNote != null)
            result += $" ({request.LookNote})";
        if (TrySplat(out _))
            result += "; " + FillRampSplat(ramp, look, materials);
        return result;
    }

    static string Climbers(IEnumerable<Movement.GroupVerdict> verdicts)
    {
        var names = verdicts.Select(v => v.Group).ToList();
        return names.Count == 0 ? "none" : string.Join(" · ", names);
    }

    /// <summary>
    /// How far beyond each end the fill looks for ground to copy: half the ramp's length, between 16 and 128 units.
    /// The copy is mirrored back and forth over that strip, so it meets the ground seamlessly at the ends.
    /// </summary>
    static double FillStrip(Ramp ramp) => Math.Clamp(ramp.Length / 2, 16, 128);

    /// <summary>Folds a distance into [0, strip], back and forth (negative distances too).</summary>
    static double Fold(double d, double strip)
    {
        double m = (d % (2 * strip) + 2 * strip) % (2 * strip);
        return m <= strip ? m : 2 * strip - m;
    }

    /// <summary>
    /// The from-the-ends fill at a world point: the ground beyond the first end, mirrored back over the ramp, blended
    /// into the ground beyond the second end towards it. Beyond either end the point keeps its own ground.
    /// </summary>
    static Rgba FillAt(Ramp ramp, double x, double z, Func<double, double, Rgba> sample)
    {
        var (u, v) = ramp.SurfaceLocal(x, z);
        double length = ramp.Length;
        double t = Math.Clamp(u / length, 0, 1);
        t = t * t * (3 - 2 * t);
        return MixColour(BeyondEnd(ramp, u, v, first: true, sample), BeyondEnd(ramp, u, v, first: false, sample), t);
    }

    /// <summary>The ground beyond one end, mirrored back over the ramp (beyond that end, the point's own ground).</summary>
    static Rgba BeyondEnd(Ramp ramp, double u, double v, bool first, Func<double, double, Rgba> sample)
    {
        double length = ramp.Length, strip = FillStrip(ramp);
        double along = first ? (u >= 0 ? -Fold(u, strip) : u) : (u <= length ? length + Fold(length - u, strip) : u);
        var (x, z) = ramp.World(along, v);
        return sample(x, z);
    }

    static Rgba MixColour(Rgba a, Rgba b, double t) => new(Mix(a.R, b.R, t), Mix(a.G, b.G, t), Mix(a.B, b.B, t), Mix(a.A, b.A, t));

    static byte Mix(byte a, byte b, double t) => (byte)Math.Round(a + (b - a) * Math.Clamp(t, 0, 1));

    static double Smooth(double from, double to, double x)
    {
        double t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Bilinear sample at fractional pixel coordinates (pixel centres at whole numbers + 0.5), clamped to the image.</summary>
    static Rgba Bilinear(RgbaImage image, double px, double py)
    {
        double fx = Math.Clamp(px - 0.5, 0, image.Width - 1), fy = Math.Clamp(py - 0.5, 0, image.Height - 1);
        int x0 = (int)fx, y0 = (int)fy, x1 = Math.Min(x0 + 1, image.Width - 1), y1 = Math.Min(y0 + 1, image.Height - 1);
        double tx = fx - x0, ty = fy - y0;
        Rgba p00 = image.GetPixel(x0, y0), p10 = image.GetPixel(x1, y0), p01 = image.GetPixel(x0, y1), p11 = image.GetPixel(x1, y1);
        byte C(byte a, byte b, byte c, byte d) => (byte)Math.Round((a * (1 - tx) + b * tx) * (1 - ty) + (c * (1 - tx) + d * tx) * ty);
        return new Rgba(C(p00.R, p10.R, p01.R, p11.R), C(p00.G, p10.G, p01.G, p11.G), C(p00.B, p10.B, p01.B, p11.B), C(p00.A, p10.A, p01.A, p11.A));
    }

    // ---------------------------------------------------------------- rock to dirt

    /// <summary>What rock to dirt needs, worked out before the ramp changes anything.</summary>
    sealed record RampMaterials(Ramp Ramp, double H1, double H2, (double X, double Z) Rock, (double X, double Z)? Dirt);

    RampMaterials Materials(RampRequest r) => new(r.Ramp, r.H1, r.H2, r.Rock!.Value, r.Dirt);

    /// <summary>The mean of a 64-unit patch around a source (sampled every 4 units): the colour rock or dirt is given.</summary>
    static (double R, double G, double B, double A) PatchMean((double X, double Z) source, Func<double, double, Rgba> sample)
    {
        double r = 0, g = 0, b = 0, a = 0;
        int n = 0;
        for (double dz = 0; dz < MaterialPatch; dz += 4)
            for (double dx = 0; dx < MaterialPatch; dx += 4)
            {
                var p = sample(source.X - MaterialPatch / 2 + dx, source.Z - MaterialPatch / 2 + dz);
                (r, g, b, a, n) = (r + p.R, g + p.G, b + p.B, a + p.A, n + 1);
            }
        return (r / n, g / n, b / n, a / n);
    }

    /// <summary>
    /// The rock-to-dirt texture for every point of a ramp, from one map image (the texture, or the splat map) read as it
    /// was before the ramp. Rock and dirt give only their colour, the mean of a 64-unit patch at each source; the detail
    /// is the ground's own, each pixel's brightness against its local average, added to the material's colour. So
    /// nothing is tiled or repeated, and the ramp keeps the grain of the ground it was cut from. Along the ramp, from
    /// its foot: the ground beyond the foot, turning to dirt, then to rock over the upper half, and over its last tenth
    /// into the ground beyond the top; the boundaries wander with noise so they don't look painted. On its sides the
    /// ground turns to rock as it gets steeper (by the built heights). For the splat map there is no detail: the
    /// weights are the materials' own.
    /// </summary>
    sealed class RockToDirtFill
    {
        readonly RampMaterials m;
        readonly Func<double, double, Rgba> sample;
        readonly Func<double, double, double>? localLuma;
        readonly HeightMap heights;
        readonly bool footFirst;
        readonly (double R, double G, double B, double A) rock, dirt;

        /// <param name="localLuma">The local average brightness at a point, for the texture; null for the splat map.</param>
        public RockToDirtFill(RampMaterials materials, Func<double, double, Rgba> sample, Func<double, double, double>? localLuma, HeightMap heights)
        {
            m = materials;
            this.sample = sample;
            this.localLuma = localLuma;
            this.heights = heights;
            footFirst = materials.H1 <= materials.H2;
            rock = PatchMean(materials.Rock, sample);
            if (materials.Dirt is (double dx, double dz))
                dirt = PatchMean((dx, dz), sample);
            else
            {
                // Sand or dirt in the map's own colours: half way between its rock and the ground at the foot (lightened, for the texture).
                var ramp = materials.Ramp;
                var foot = footFirst ? ramp.World(-FillStrip(ramp) / 2, 0) : ramp.World(ramp.Length + FillStrip(ramp) / 2, 0);
                var ground = PatchMean(foot, sample);
                double lighten = localLuma != null ? 1.12 : 1;
                dirt = (Math.Min(255, (rock.R + ground.R) / 2 * lighten), Math.Min(255, (rock.G + ground.G) / 2 * lighten),
                    Math.Min(255, (rock.B + ground.B) / 2 * lighten), (rock.A + ground.A) / 2);
            }
        }

        /// <summary>A material's colour with the ground's own detail on it; the texture is always opaque.</summary>
        Rgba Material((double R, double G, double B, double A) colour, double detail) => new(
            Byte(colour.R + detail), Byte(colour.G + detail), Byte(colour.B + detail), localLuma != null ? (byte)255 : Byte(colour.A));

        static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

        /// <summary>The texture at a world point, or null where the ramp leaves it alone.</summary>
        public Rgba? At(double x, double z)
        {
            var ramp = m.Ramp;
            double outside = ramp.DistanceOutside(x, z);
            if (outside >= ramp.Edge)
                return null;
            var (u, v) = ramp.SurfaceLocal(x, z);
            var (nx, nz) = ramp.NoisePoint(x, z); // (mirrored, for a ramp made as another's mirror image)
            var own = sample(x, z);
            double detail = localLuma != null ? Luma(own) - localLuma(x, z) : 0;

            // The sides (and the ground round the ends): rock where the built ground is steep.
            Rgba side = own;
            if (outside > 0)
            {
                const double d = 4;
                double gx = (heights.HeightAt(x + d, z) - heights.HeightAt(x - d, z)) / (2 * d);
                double gz = (heights.HeightAt(x, z + d) - heights.HeightAt(x, z - d)) / (2 * d);
                double degrees = Math.Atan(Math.Sqrt(gx * gx + gz * gz)) * 180 / Math.PI;
                double rocky = Smooth(22, 40, degrees + 6 * Ramp.RockNoise(nx + 311, nz + 97)) * Math.Sqrt(ramp.Weight(x, z));
                side = MixColour(own, Material(rock, detail), rocky);
                if (outside >= RampFillFeather)
                    return side;
            }

            // Along the ramp, from its foot (0) to its top (1), with wandering boundaries that settle at the ends.
            double along = Math.Clamp(u / ramp.Length, 0, 1);
            double s = footFirst ? along : 1 - along;
            s += 0.08 * Math.Min(1, Math.Min(s, 1 - s) / 0.1) * Ramp.RockNoise(nx * 0.7 + 53, nz * 0.7 + 11);
            var foot = BeyondEnd(ramp, u, v, first: footFirst, sample);
            var top = BeyondEnd(ramp, u, v, first: !footFirst, sample);
            var colour = MixColour(foot, Material(dirt, detail * 0.8), Smooth(0, 0.22, s));
            colour = MixColour(colour, Material(rock, detail), Smooth(0.35, 0.7, s));
            colour = MixColour(colour, top, Smooth(0.9, 1, s));
            return outside > 0 ? MixColour(colour, side, Smooth(0, RampFillFeather, outside)) : colour;
        }
    }

    // ---------------------------------------------------------------- writing

    /// <summary>Textures the ramp (texture tiles): from the ends, feathered just past its sides, or rock to dirt over the whole edge.</summary>
    TextureUpdate FillRampTexture(Ramp ramp, RampLook look, RampMaterials? materials, RgbaImage image)
    {
        double perUnit = image.Width / MapWidth;
        var region = RampTextureRegion(ramp, look);
        var shape = FillShape(ramp, look);
        Rgba Sample(double sx, double sz) => Bilinear(image, sx * perUnit, sz * perUnit);
        var natural = materials != null ? new RockToDirtFill(materials, Sample, LocalLuma(image, region, perUnit), Heights) : null;
        // Every source pixel is read before any is written: the sources overlap what is being written.
        var fill = new Rgba?[region.Width * region.Height];
        Parallel.For(region.Y, region.Bottom, y =>
        {
            for (int x = region.X; x < region.Right; x++)
            {
                double wx = (x + 0.5) / perUnit, wz = (y + 0.5) / perUnit;
                if (shape.Weight(wx, wz) > 0)
                    fill[(y - region.Y) * region.Width + x - region.X] = natural != null ? natural.At(wx, wz) : FillAt(ramp, wx, wz, Sample);
            }
        });
        // Rock to dirt says exactly what each pixel becomes; from the ends is blended in by the feather's weight.
        Func<double, double, double> weight = natural != null ? (_, _) => 1 : shape.Weight;
        return EditTextureRegion(region, weight, 1, (x, y, _) => fill[(y - region.Y) * region.Width + x - region.X]);
    }

    /// <summary>
    /// The average brightness around a point (a box about 25 game units across), from a summed-area table of the image
    /// over a region and a margin: the "local average" against which rock to dirt keeps each pixel's own detail.
    /// </summary>
    static Func<double, double, double> LocalLuma(RgbaImage image, PixelRect region, double perUnit)
    {
        int r = Math.Max(2, (int)Math.Round(12 * perUnit));
        var area = PixelRect.FromEdges(region.X - r, region.Y - r, region.Right + r, region.Bottom + r).Intersect(image.Bounds);
        int w = area.Width + 1;
        var sum = new double[w * (area.Height + 1)];
        for (int y = 0; y < area.Height; y++)
        {
            double row = 0;
            for (int x = 0; x < area.Width; x++)
            {
                row += Luma(image.GetPixel(area.X + x, area.Y + y));
                sum[(y + 1) * w + x + 1] = sum[y * w + x + 1] + row;
            }
        }
        return (wx, wz) =>
        {
            int px = (int)Math.Floor(wx * perUnit) - area.X, py = (int)Math.Floor(wz * perUnit) - area.Y;
            int x0 = Math.Clamp(px - r, 0, area.Width), x1 = Math.Clamp(px + r + 1, 0, area.Width);
            int y0 = Math.Clamp(py - r, 0, area.Height), y1 = Math.Clamp(py + r + 1, 0, area.Height);
            if (x1 <= x0 || y1 <= y0)
                return 0;
            return (sum[y1 * w + x1] - sum[y0 * w + x1] - sum[y1 * w + x0] + sum[y0 * w + x0]) / ((x1 - x0) * (y1 - y0));
        };
    }

    /// <summary>Fills the splat map the same way (map orientation; the file is stored upside down, rule 7).</summary>
    string FillRampSplat(Ramp ramp, RampLook look, RampMaterials? materials)
    {
        var edit = SplatEdit(out var entry);
        var dds = edit.Dds;
        var level0 = edit.Levels[0];
        int w = dds.Width, h = dds.Height;
        double ux = MapWidth / w, uz = MapHeight / h;
        var shape = FillShape(ramp, look);
        var (minX, minZ, maxX, maxZ) = shape.Bounds;
        var region = PixelRect.FromEdges((int)Math.Floor(minX / ux), (int)Math.Floor(minZ / uz),
            (int)Math.Ceiling(maxX / ux) + 1, (int)Math.Ceiling(maxZ / uz) + 1).Intersect(new PixelRect(0, 0, w, h));
        Rgba Sample(double x, double z) => Bilinear(level0, x / ux, h - z / uz); // map row r is file row h-1-r
        var natural = materials != null ? new RockToDirtFill(materials, Sample, null, Heights) : null;
        var fill = new (Rgba Value, double Weight)[region.Width * region.Height];
        for (int my = region.Y; my < region.Bottom; my++)
            for (int mx = region.X; mx < region.Right; mx++)
            {
                double wx = (mx + 0.5) * ux, wz = (my + 0.5) * uz, weight = shape.Weight(wx, wz);
                if (weight <= 0)
                    continue;
                if (natural != null)
                    fill[(my - region.Y) * region.Width + mx - region.X] = natural.At(wx, wz) is Rgba value ? (value, 1) : default;
                else
                    fill[(my - region.Y) * region.Width + mx - region.X] = (FillAt(ramp, wx, wz, Sample), weight);
            }
        for (int my = region.Y; my < region.Bottom; my++)
            for (int mx = region.X; mx < region.Right; mx++)
            {
                var (value, weight) = fill[(my - region.Y) * region.Width + mx - region.X];
                if (weight <= 0)
                    continue;
                int fy = h - 1 - my;
                var p = level0.GetPixel(mx, fy);
                level0.SetPixel(mx, fy, new Rgba(Blend(p.R, value.R, weight), Blend(p.G, value.G, weight), Blend(p.B, value.B, weight), Blend(p.A, value.A, weight)));
            }
        var file = region.FlipVertical(h);
        dds.Encode(level0, 0, 0, file);
        MipChain.Rebuild(level0, dds.MipCount, file, m => edit.Levels[m], (m, image, dirty) => dds.Encode(image, m, 0, dirty));
        changedResources.Add(entry);
        return look == RampLook.RockToDirt ? $"{entry} rock to dirt" : $"{entry} filled from the ends";
    }
}
