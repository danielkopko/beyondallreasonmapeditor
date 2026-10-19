using System.Globalization;
using MapCore.Items;

namespace MapCore.Editing;

/// <summary>
/// Symmetric editing: with <see cref="Mirror"/> set, every edit made at a place is made again at its mirror image
/// (<see cref="MapMirror"/>), as the same undo step. The mirror image is an operation of its own, worked out from the
/// map as it is before the edit (which item, spot, structure or start is the edited one's twin) and applied after it;
/// the journal records both, so a replay applies them as they were. What isn't at a place (mapinfo settings, the whole
/// map's heights, imports, assets, new features and structure kinds) is made once.
/// <list type="bullet">
/// <item>Brushes (heights, texture, splat, grass), ramps and new items, metal spots and structures are made at the mirror
/// image of where they are made, turned to match (a mirror image faces the mirrored way; a half turn, the opposite way).
/// Noise, which is fixed to the map, is read mirrored, so the bumps match too.</item>
/// <item>Moving, turning, changing or removing something does the same to its twin: the same kind of thing at its
/// mirror image, within <see cref="MirrorTolerance"/>. Without one, only the original is changed, and the result says so.</item>
/// <item>A start position's twin is the other start, on a map with two; with more, the start nearest its mirror image
/// within <see cref="StartMirrorTolerance"/>.</item>
/// </list>
/// Something exactly on the mirror line is its own mirror image, so it is made once. A brush reaching across the line
/// works where it overlaps its mirror image twice, as two dabs there would.
/// </summary>
public sealed partial class MapEditSession
{
    /// <summary>How far from a thing's mirror image its twin may be (game units): what a mirrored edit makes lands exactly; a map made symmetric by hand may be a little off.</summary>
    public const double MirrorTolerance = 16;

    /// <summary>How far from a start's mirror image another team's start may be to be its twin, on a map with more than two.</summary>
    public const double StartMirrorTolerance = 256;

    MirrorKind? mirror;

    /// <summary>The symmetry edits are made with, or null for none (see the class summary).</summary>
    /// <exception cref="InvalidOperationException">A diagonal on a map that isn't square.</exception>
    public MirrorKind? Mirror
    {
        get => mirror;
        set
        {
            if (value is MirrorKind kind)
                _ = new MapMirror(kind, MapWidth, MapHeight); // refuses a diagonal on a map that isn't square
            mirror = value;
        }
    }

    /// <summary>
    /// Applies one text operation (see the class summary), records it for undo and describes what changed. A failed
    /// operation is rolled back, so it leaves nothing half-done. With <see cref="Mirror"/> set, an edit at a place is
    /// made at its mirror image too, in the same undo step; if the mirror image can't be made, the edit stands and the
    /// result says why.
    /// </summary>
    /// <exception cref="InvalidOperationException">The operation is malformed or cannot be applied.</exception>
    public string Apply(string operation)
    {
        MirrorPlan? plan = null;
        if (mirror is MirrorKind kind)
        {
            try
            {
                plan = PlanMirror(operation, new MapMirror(kind, MapWidth, MapHeight));
            }
            catch (InvalidOperationException)
            {
                plan = null; // the operation itself is wrong: applying it says how
            }
        }
        if (plan?.Build == null)
        {
            string done = ApplyOne(operation);
            return plan?.Note is string note ? $"{done}; {note}" : done;
        }
        bool own = group == null; // a brush stroke's group, say, takes both
        if (own)
            BeginGroup(operation);
        try
        {
            string result = ApplyOne(operation);
            try
            {
                return $"{result}; mirrored: {ApplyOne(plan.Build())}";
            }
            catch (InvalidOperationException ex)
            {
                return $"{result}; its mirror image wasn't made: {ex.Message}";
            }
        }
        finally
        {
            if (own)
                EndGroup();
        }
    }

    /// <summary>An operation's mirror image: <see cref="Build"/> makes its text once the original is applied; without one, <see cref="Note"/> may say why.</summary>
    sealed record MirrorPlan(Func<string>? Build, string? Note = null);

    static readonly MirrorPlan OnTheLine = new(null, "on the mirror line, so it is its own mirror image");

    static MirrorPlan Plan(string operation) => new(() => operation);

    static MirrorPlan NoTwin(string what, (double X, double Z) at) =>
        new(null, FormattableString.Invariant($"not mirrored: there is no {what} at its mirror image ({at.X:0}, {at.Z:0})"));

    static string P((double X, double Z) p) => Op($"{p.X},{p.Z}");

    static bool Same((double X, double Z) a, (double X, double Z) b) => Math.Abs(a.X - b.X) < 0.5 && Math.Abs(a.Z - b.Z) < 0.5;

    static double Distance(double x1, double z1, double x2, double z2) => Math.Sqrt((x1 - x2) * (x1 - x2) + (z1 - z2) * (z1 - z2));

    static double Snap(double v, double grid) => Math.Round(v / grid) * grid;

    /// <summary>
    /// What mirroring <paramref name="operation"/> takes, worked out from the map as it is now; null when it isn't made
    /// at a place (or is malformed: applying it says how), or a brush on the mirror line.
    /// </summary>
    MirrorPlan? PlanMirror(string operation, MapMirror m)
    {
        int colon = operation.IndexOf(':');
        if (colon <= 0)
            return null;
        string name = operation[..colon];
        var args = operation[(colon + 1)..].Split(':');
        (double X, double Z) Mapped(string point)
        {
            var (x, z) = Point(point);
            return m.Map(x, z);
        }
        string With(params (int Index, string Value)[] changes)
        {
            var copy = (string[])args.Clone();
            foreach (var (index, value) in changes)
                copy[index] = value;
            return name + ":" + string.Join(":", copy);
        }

        switch (name)
        {
            case "height-raise" or "height-flatten" or "height-smooth" or "texture-paint" or "texture-tint" or "texture-adjust" or "splat-paint" or "grass-paint":
            {
                var p = Point(args[0]);
                var image = m.Map(p.X, p.Z);
                return Same(p, image) ? null : Plan(With((0, P(image))));
            }
            case "texture-clone" when args.Length >= 3:
            {
                // Copied from the mirror image of where the original copies from.
                var p = Point(args[0]);
                var image = m.Map(p.X, p.Z);
                var (dx, dz) = Point(args[2]);
                return Same(p, image) ? null : Plan(With((0, P(image)), (2, P(m.MapVector(dx, dz)))));
            }
            case "height-noise" when args.Length is >= 3 and <= 5:
            {
                var p = Point(args[0]);
                var image = m.Map(p.X, p.Z);
                if (Same(p, image))
                    return null;
                string hardness = args.Length >= 4 ? args[3] : Op($"{DefaultHardness}"), size = args.Length >= 5 ? args[4] : "64";
                return Plan($"{name}:{P(image)}:{args[1]}:{args[2]}:{hardness}:{size}:{m.Code}");
            }
            case "height-noise" or "ramp" when args.Length >= (name == "ramp" ? 10 : 6):
                return new MirrorPlan(null, "not mirrored: it is itself a mirror image");
            case "ramp" when args.Length >= 4:
            {
                var points = args[0].Split(';').Select(Point).Append(Point(args[1])).ToList();
                var images = points.Select(p => m.Map(p.X, p.Z)).ToList();
                if (images.Zip(points).All(t => Same(t.First, t.Second)) || images.Zip(Enumerable.Reverse(points)).All(t => Same(t.First, t.Second)))
                    return null; // its own mirror image
                // The same ramp: what the original works out from the ground (auto heights, edge, rock and dirt) is taken
                // as it comes out there, not worked out again from the ground at the mirror image, which may differ.
                var request = ParseRamp(args);
                string Source((double X, double Z)? p) => p is var (x, z) ? P(m.Map(x, z)) : "auto";
                return Plan(string.Join(":", "ramp", string.Join(";", images.SkipLast(1).Select(P)), P(images[^1]), args[2],
                    Op($"{request.H1},{request.H2}"), Op($"{request.Ramp.Edge}"), ((int)request.Look).ToString(CultureInfo.InvariantCulture),
                    Op($"{request.Roughness}"), Source(request.Rock), Source(request.Dirt), m.Code));
            }

            case "item-add" when args.Length is 3 or 4:
            {
                var p = Point(args[2]);
                var image = m.Map(p.X, p.Z);
                if (Same(p, image))
                    return OnTheLine;
                double rotation = args.Length == 4 ? Number(args[3]) : 0;
                var kind = args[0] == "building" ? PlacedItemKind.Building : PlacedItemKind.Feature;
                return Plan($"item-add:{args[0]}:{args[1]}:{P(image)}:{StoredRotation(kind, rotation, m)}");
            }
            case "item-move" or "item-rotate" or "item-remove" when args.Length == (name == "item-remove" ? 1 : 2):
            {
                var item = PickItem(args[0]);
                var image = m.Map(item.X, item.Z);
                if (Twin(item, m) is not MapItem twin)
                    return NoTwin(item.Name, image);
                return Plan(name switch
                {
                    "item-move" => With((0, P((twin.X, twin.Z))), (1, P(Mapped(args[1])))),
                    "item-rotate" => With((0, P((twin.X, twin.Z))), (1, StoredRotation(item.Kind, Number(args[1]), m))),
                    _ => With((0, P((twin.X, twin.Z)))),
                });
            }
            case "items-move" or "items-rotate" or "items-heading" or "items-remove" when args.Length == (name == "items-remove" ? 1 : 2):
            {
                var items = ResolveRefs(args[^1]);
                // Each one's twin, unless it is itself among those edited (both halves chosen): it is changed once.
                var twins = items.Select(i => Twin(i, m)).OfType<MapItem>().Where(t => !items.Contains(t)).Distinct().ToList();
                if (twins.Count == 0)
                    return items.Count == 1 ? NoTwin(items[0].Name, m.Map(items[0].X, items[0].Z))
                        : new MirrorPlan(null, "not mirrored: none of them has a twin at its mirror image");
                switch (name)
                {
                    case "items-move":
                        var (dx, dz) = Point(args[0]);
                        return Plan($"items-move:{P(m.MapVector(dx, dz))}:{ItemRefs(twins)}");
                    case "items-rotate":
                        double turn = Number(args[0]);
                        return Plan(Op($"items-rotate:{(m.Reflects ? -turn : turn)}:") + ItemRefs(twins));
                    case "items-heading":
                        return Plan(Op($"items-heading:{m.Heading(Number(args[0]))}:") + ItemRefs(twins));
                    default:
                        // Removing the originals renumbers the lists: the twins are found again afterwards, by what they are and where.
                        return new MirrorPlan(() => "items-remove:" + ItemRefs(twins.Select(t => Items.All.FirstOrDefault(i =>
                            i.Source == t.Source && i.Kind == t.Kind && i.Name == t.Name && i.X == t.X && i.Z == t.Z)
                            ?? throw new InvalidOperationException($"{t.Name} at ({t.X:0}, {t.Z:0}) went with the others."))));
                }
            }

            case "metal-add" when args.Length is 1 or 2:
            {
                var p = Point(args[0]);
                var image = m.Map(p.X, p.Z);
                return Distance(p.X, p.Z, image.X, image.Z) < MetalLayerCell ? OnTheLine : Plan(With((0, P(image))));
            }
            case "metal-move" or "metal-remove" or "metal-income" or "metal-resize" when args.Length >= 1:
            {
                var spot = PickSpot(args[0]);
                var image = m.Map(spot.X, spot.Z);
                var twin = Metal.SpotNear(image.X, image.Z, 2 * MirrorTolerance);
                if (twin == null)
                    return NoTwin("metal spot", image);
                if (twin.X == spot.X && twin.Z == spot.Z)
                    return OnTheLine;
                return Plan(name == "metal-move" && args.Length >= 2 ? With((0, P((twin.X, twin.Z))), (1, P(Mapped(args[1])))) : With((0, P((twin.X, twin.Z)))));
            }

            case "start-move" when args.Length == 2:
            {
                int team = (int)Number(args[0]);
                var starts = Info.StartPositions;
                var own = starts.FirstOrDefault(s => s.Team == team);
                var others = starts.Where(s => s.Team != team).ToList();
                if (own == null || others.Count == 0)
                    return new MirrorPlan(null, "not mirrored: there is no other start position to be its twin");
                var image = m.Map(own.X, own.Z);
                var twin = others.Count == 1 ? others[0]
                    : others.Where(s => Distance(s.X, s.Z, image.X, image.Z) <= StartMirrorTolerance).MinBy(s => Distance(s.X, s.Z, image.X, image.Z));
                if (twin == null)
                    return NoTwin("other team's start", image);
                return Plan(Op($"start-move:{twin.Team}:") + P(Mapped(args[1])));
            }

            case "structure-add" when args.Length is 3 or 4:
            {
                // Where it is placed (on the 16-unit grid), mirrored.
                var (x, z) = Point(args[1]);
                var at = (Snap(x, 16), Snap(z, 16));
                var image = m.Map(at.Item1, at.Item2);
                if (Same(at, image))
                    return OnTheLine;
                return Plan(With((1, P(image)), (2, m.Facing((int)Number(args[2])).ToString(CultureInfo.InvariantCulture))));
            }
            case "structure-move" or "structure-turn" or "structure-broken" or "structure-remove" or "structure-rule" or "structure-zone" or "structure-marker-add"
                when args.Length >= (name == "structure-remove" ? 1 : 2):
            {
                var config = Structures;
                var (x, z) = Point(args[0]);
                var nearest = Nearest(config, x, z);
                var image = m.Map(nearest.X, nearest.Z);
                var twin = config.Instances.Where(i => i != nearest && i.Kind == nearest.Kind && Distance(i.X, i.Z, image.X, image.Z) <= MirrorTolerance)
                    .MinBy(i => Distance(i.X, i.Z, image.X, image.Z));
                if (twin == null)
                    return NoTwin(nearest.Kind, image);
                var at = (0, P((twin.X, twin.Z)));
                return Plan(name switch
                {
                    "structure-move" when Point(args[1]) is var (nx, nz) => With(at, (1, P(m.Map(Snap(nx, 16), Snap(nz, 16))))),
                    "structure-turn" => With(at, (1, m.Facing((int)Number(args[1])).ToString(CultureInfo.InvariantCulture))),
                    "structure-marker-add" when Point(args[1]) is var (mx, mz) => With(at, (1, P(m.Map(Snap(mx, 8), Snap(mz, 8))))),
                    "structure-zone" when args.Length == 4 => With(at, (3, string.Join(";", args[3].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(s => P(Mapped(s)))))),
                    _ => With(at), // broken, remove, rule (its teams as they are), a zone cleared
                });
            }
            case "structure-marker-move" or "structure-marker-remove" when args.Length >= 1:
            {
                var config = Structures;
                var (x, z) = Point(args[0]);
                var (instance, index) = NearestMarker(config, x, z);
                var marker = instance.MarkerList[index];
                var image = m.Map(marker.X, marker.Z);
                var twins = config.Instances.SelectMany(i => i.MarkerList).Where(k => k != marker && Distance(k.X, k.Z, image.X, image.Z) <= MirrorTolerance).ToList();
                if (twins.Count == 0)
                    return NoTwin("repair marker", image);
                var twin = twins.MinBy(k => Distance(k.X, k.Z, image.X, image.Z));
                return Plan(name == "structure-marker-move" && args.Length >= 2 && Point(args[1]) is var (nx, nz)
                    ? With((0, P(twin)), (1, P(m.Map(Snap(nx, 8), Snap(nz, 8)))))
                    : With((0, P(twin))));
            }
            default:
                return null; // not at a place: set, import, the whole map's heights, assets, new features, structure kinds and their targets
        }
    }

    /// <summary>The metal map's cell (game units): a new spot nearer its mirror image than this would overlap it.</summary>
    const double MetalLayerCell = 16;

    /// <summary>
    /// An item's twin: the same thing (name and kind) at its mirror image, within <see cref="MirrorTolerance"/>, other
    /// than itself; the nearest if several.
    /// </summary>
    MapItem? Twin(MapItem item, MapMirror m)
    {
        var (x, z) = m.Map(item.X, item.Z);
        return Items.All.Where(i => i != item && i.Kind == item.Kind && i.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase) &&
                                    Distance(i.X, i.Z, x, z) <= MirrorTolerance)
            .MinBy(i => Distance(i.X, i.Z, x, z));
    }

    /// <summary>A stored rotation turned as the mirror turns it: buildings in quarters (0-3), everything else in 65,536ths of a turn.</summary>
    static string StoredRotation(PlacedItemKind kind, double rotation, MapMirror m)
    {
        if (kind == PlacedItemKind.Building)
            return m.Facing((int)Math.Round(rotation)).ToString(CultureInfo.InvariantCulture);
        double units = Math.Round(m.Heading(rotation * 360 / 65536) * 65536 / 360) % 65536;
        return units.ToString(CultureInfo.InvariantCulture);
    }
}
