using System.Globalization;
using System.Text;
using MapCore.Lua;

namespace MapCore.Structures;

/// <summary>What a structure kind is.</summary>
public enum StructureType
{
    /// <summary>Two indestructible walls and a door between them that blocks and can't be built on; repaired when broken.</summary>
    Door,

    /// <summary>Two indestructible walls and a top that can be built on but not crossed; it collapses, killing what is on it, and is repaired.</summary>
    Tunnel,

    /// <summary>Two indestructible walls and a middle units pass through; it can't collapse, so there is nothing to repair.</summary>
    OpenTunnel,

    /// <summary>
    /// An oil pump: derelict at the start; the team that repairs it owns it and is given metal and energy a second
    /// while it stands, its beam nodding; destroyed, it is derelict again for whoever repairs it next.
    /// </summary>
    Pump,

    /// <summary>
    /// A guardian tower: indestructible, it guards a path anywhere on the map (its instance's <see cref="GuardianZone"/>).
    /// A unit of a type its kind lists (<see cref="StructureKind.Targets"/>) that comes within the zone's distance of the
    /// path is doomed: worn down from then on, wherever it goes, healed or not, and destroyed
    /// <see cref="StructureKind.KillSeconds"/> later, however many there are. It fires a rocket now and then, for show.
    /// </summary>
    Guardian,

    /// <summary>
    /// A shield platform: a low pad units drive over. It starts derelict; the team that repairs it owns it, and a unit of
    /// that team's or an ally's that drives off it gets a bubble shield (<see cref="StructureKind.ShieldPercent"/> of its
    /// health, for <see cref="StructureKind.EnergyPerMetal"/> energy a metal it costs), round it by its size, taking
    /// hits for it and for allies within it until nothing is left.
    /// </summary>
    ShieldPad,
}

/// <summary>
/// A kind of door or tunnel: its parts' feature names (<c>&lt;name&gt;_wall</c>, <c>_centre</c>, <c>_repair</c>), its
/// size in footprint squares (16 game units: <see cref="Gap"/> across the opening, <see cref="WallWidth"/> each wall,
/// <see cref="Depth"/> along the passage), and what a full repair costs (the work is the repair markers' reclaimtime).
/// <see cref="Height"/> is how tall built models are (game units; 0 for models from assets): a tunnel's flat roof is
/// that high, and the gadget lifts buildings onto it. <see cref="Settings"/> is everything the editor was given, to
/// show it again (null for kinds made before it was kept).
/// </summary>
/// <remarks>An oil pump uses <see cref="Gap"/> by <see cref="Depth"/> as its footprint, and <see cref="MetalIncome"/> and
/// <see cref="EnergyIncome"/> as what it gives its owner a second. A guardian tower uses <see cref="Gap"/> by
/// <see cref="Depth"/> as its footprint, and <see cref="Targets"/>, <see cref="KillSeconds"/> and
/// <see cref="RocketSeconds"/>. A shield platform uses <see cref="Gap"/> by <see cref="Depth"/> as its pad, and
/// <see cref="ShieldPercent"/> (a shield's strength, as a share of the unit's maximum health), <see cref="EnergyPerMetal"/>
/// (its price: energy for each metal the unit costs) and <see cref="BubbleScale"/> (its size, times the unit's).</remarks>
public sealed record StructureKind(string Name, StructureType Type, int Gap, int WallWidth, int Depth, double Metal, double Energy, bool OpenWhenBroken = true,
    double Height = 0, StructureRoof Roof = StructureRoof.Flat, StructureLook Look = StructureLook.Assets, string? Settings = null,
    double MetalIncome = 0, double EnergyIncome = 0, IReadOnlyList<string>? Targets = null, double KillSeconds = 0, double RocketSeconds = 0,
    double ShieldPercent = 0, double EnergyPerMetal = 0, double BubbleScale = 0)
{
    /// <summary>A guardian's targets: the BAR unit types (by name) it destroys in its zone; every other unit passes.</summary>
    public IReadOnlyList<string> TargetList => Targets ?? [];

    /// <summary>The harmless rocket every guardian fires, for show: a weapon of the map's, by this name, with this model.</summary>
    public const string GuardianRocket = "barmapeditor_guardian_rocket";

    public string Wall => Name + "_wall";
    public string Centre => Name + "_centre";
    /// <summary>
    /// What is reclaimed to repair it: for a built kind, its placed markers while red (the structure destroyed); for a
    /// kind made from assets, the two markers it puts beside its walls when destroyed. None for an open tunnel.
    /// </summary>
    public string? Repair => Type is StructureType.OpenTunnel or StructureType.Pump or StructureType.Guardian or StructureType.ShieldPad ? null : Name + "_repair";

    /// <summary>Whether it starts derelict and belongs to whoever repairs it: an oil pump or a shield platform.</summary>
    public bool Owned => Type is StructureType.Pump or StructureType.ShieldPad;

    bool Built => Look is StructureLook.Concrete or StructureLook.Metal;

    /// <summary>A built kind's markers while the structure stands (green, not reclaimed): <c>&lt;name&gt;_marker</c>.</summary>
    public string? Marker => Built && Type is StructureType.Door or StructureType.Tunnel ? Name + "_marker" : null;

    /// <summary>
    /// What a built door or tunnel leaves when destroyed, until repaired (reclaimed to repair it too), or an oil pump
    /// derelict: <c>&lt;name&gt;_broken</c>.
    /// </summary>
    public string? Debris => Built && Type is not (StructureType.OpenTunnel or StructureType.Guardian) ? Name + "_broken" : null;

    /// <summary>An oil pump's walking beam, its own feature so the gadget can make it nod: <c>&lt;name&gt;_beam</c>.</summary>
    public string? Beam => Type == StructureType.Pump ? Name + "_beam" : null;

    /// <summary>Whether buildings go on its centre: a tunnel with a flat roof.</summary>
    public bool BuildOnTop => Type == StructureType.Tunnel && Roof == StructureRoof.Flat;

    public static string LookName(StructureLook look) => look switch
    {
        StructureLook.Concrete => "concrete",
        StructureLook.Metal => "metal",
        StructureLook.Ancient => "ancient",
        _ => "assets",
    };

    public static StructureLook ParseLook(string text) => text switch
    {
        "concrete" => StructureLook.Concrete,
        "metal" => StructureLook.Metal,
        "ancient" => StructureLook.Ancient,
        "assets" => StructureLook.Assets,
        _ => throw new InvalidOperationException($"'{text}' is not concrete, metal, ancient or assets."),
    };

    public static StructureRoof ParseRoof(string text) => text switch
    {
        "flat" => StructureRoof.Flat,
        "cliff" => StructureRoof.Cliff,
        _ => throw new InvalidOperationException($"'{text}' is not flat or cliff."),
    };

    public static string TypeName(StructureType type) => type switch
    {
        StructureType.Door => "door",
        StructureType.Tunnel => "tunnel",
        StructureType.Pump => "pump",
        StructureType.Guardian => "guardian",
        StructureType.ShieldPad => "shield_pad",
        _ => "open_tunnel",
    };

    public static StructureType ParseType(string text) => text switch
    {
        "door" => StructureType.Door,
        "tunnel" => StructureType.Tunnel,
        "open_tunnel" => StructureType.OpenTunnel,
        "pump" => StructureType.Pump,
        "guardian" => StructureType.Guardian,
        "shield_pad" => StructureType.ShieldPad,
        _ => throw new InvalidOperationException($"'{text}' is not door, tunnel, open_tunnel, pump, guardian or shield_pad."),
    };
}

/// <summary>
/// One door or tunnel on the map: its kind, centre (game units, on a 16-unit grid) and facing (0 south, 1 east,
/// 2 north, 3 west: the way the passage runs). <see cref="Ground"/> is the terrain type under its centre in the
/// map's own type map, for the gadget to put back if it is reloaded while a tunnel top stands. <see cref="Broken"/>:
/// it starts the game destroyed (the gap open, the repair markers out), for a door or tunnel. <see cref="Markers"/>:
/// where its repair markers stand (a built kind's: always on the map, green, red while it is destroyed).
/// <see cref="Zone"/>: the path a guardian tower guards (none until one is set), and <see cref="Rules"/> what it does
/// in each of its three regions (null for a tower made before towers had their own: its kind's targets in the band).
/// </summary>
public sealed record StructureInstance(string Kind, double X, double Z, int Facing, int Ground = 0, bool Broken = false,
    IReadOnlyList<(double X, double Z)>? Markers = null, GuardianZone? Zone = null, GuardianRules? Rules = null)
{
    public IReadOnlyList<(double X, double Z)> MarkerList => Markers ?? [];
}

/// <summary>
/// The three regions of a guardian tower's zone: the band (within its distance of the path), inside a closed path
/// (beyond the band), and outside (everywhere else on the map, beyond the band: other towers' zones too). The band
/// wins where they meet; an open path has no inside.
/// </summary>
public enum GuardianRegion { Band, Inside, Outside }

/// <summary>
/// What a guardian tower does in one region: the unit types it destroys there (<see cref="Targets"/>, by name, each
/// with its Scavengers variant), of which teams (<see cref="Teams"/>: BAR's lobby teams 1 to 8, that is ally teams 0
/// to 7; null for every team, Scavengers and Raptors too), once one has been in the region <see cref="DelaySeconds"/>
/// without leaving it (0: as soon as it comes in; leaving starts the count again). A unit it dooms is destroyed the
/// kind's kill time later, wherever it goes.
/// </summary>
public sealed record GuardianRule(IReadOnlyList<string> Targets, IReadOnlyList<int>? Teams = null, double DelaySeconds = 0)
{
    public const double MaxDelay = 3600;
    public const int MaxTeam = 8;

    public static GuardianRule None { get; } = new([], null, 0);

    /// <summary>Whether it can destroy anything: some targets, of some team.</summary>
    public bool Acts => Targets.Count > 0 && Teams is not { Count: 0 };

    public bool AllTeams => Teams == null;

    /// <summary>The teams as the operation writes them: all, none, or the lobby numbers (2,3,4).</summary>
    public string TeamsText => Teams == null ? "all" : Teams.Count == 0 ? "none" : string.Join(",", Teams.Order());

    public bool Same(GuardianRule other) => Targets.Order(StringComparer.Ordinal).SequenceEqual(other.Targets.Order(StringComparer.Ordinal)) &&
                                            TeamsText == other.TeamsText && DelaySeconds == other.DelaySeconds;
}

/// <summary>A guardian tower's rules for its band, the inside of a closed path, and outside.</summary>
public sealed record GuardianRules(GuardianRule Band, GuardianRule Inside, GuardianRule Outside)
{
    /// <summary>A new tower's rules when there's nothing to copy: its kind's targets in the band, of every team, at once; nothing elsewhere.</summary>
    public static GuardianRules Default(IReadOnlyList<string> bandTargets) => new(new GuardianRule(bandTargets), GuardianRule.None, GuardianRule.None);

    public GuardianRule this[GuardianRegion region] => region switch
    {
        GuardianRegion.Inside => Inside,
        GuardianRegion.Outside => Outside,
        _ => Band,
    };

    public GuardianRules With(GuardianRegion region, GuardianRule rule) => region switch
    {
        GuardianRegion.Inside => this with { Inside = rule },
        GuardianRegion.Outside => this with { Outside = rule },
        _ => this with { Band = rule },
    };

    public static string RegionName(GuardianRegion region) => region switch
    {
        GuardianRegion.Inside => "inside",
        GuardianRegion.Outside => "outside",
        _ => "band",
    };

    public static GuardianRegion ParseRegion(string text) => text switch
    {
        "band" => GuardianRegion.Band,
        "inside" => GuardianRegion.Inside,
        "outside" => GuardianRegion.Outside,
        _ => throw new InvalidOperationException($"'{text}' is not band, inside or outside."),
    };
}

/// <summary>
/// The area a guardian tower guards: everything within <see cref="Distance"/> (game units, measured flat) of a path
/// of straight runs through <see cref="Points"/> (game units), back to the first if <see cref="Closed"/>. It needn't
/// reach the tower: a tower can guard a path anywhere on the map.
/// </summary>
public sealed record GuardianZone(IReadOnlyList<(double X, double Z)> Points, double Distance, bool Closed)
{
    public const double MinDistance = 16, MaxDistance = 2048;

    /// <summary>The runs of the path, first point to last (and back to the first if closed).</summary>
    public IEnumerable<((double X, double Z) A, (double X, double Z) B)> Segments()
    {
        for (int i = 0; i + 1 < Points.Count; i++)
            yield return (Points[i], Points[i + 1]);
        if (Closed && Points.Count > 2)
            yield return (Points[^1], Points[0]);
    }

    /// <summary>How far a point is from the path, measured flat (as the gadget measures it).</summary>
    public double DistanceTo(double x, double z)
    {
        double best = double.MaxValue;
        foreach (var (a, b) in Segments())
        {
            double dx = b.X - a.X, dz = b.Z - a.Z, length2 = dx * dx + dz * dz;
            double t = length2 > 0 ? Math.Clamp(((x - a.X) * dx + (z - a.Z) * dz) / length2, 0, 1) : 0;
            double qx = x - a.X - t * dx, qz = z - a.Z - t * dz;
            best = Math.Min(best, qx * qx + qz * qz);
        }
        return Math.Sqrt(best);
    }

    public bool Contains(double x, double z) => DistanceTo(x, z) <= Distance;

    /// <summary>
    /// Whether a point is inside a closed path of three or more points, by the even-odd rule (as the gadget decides it):
    /// a path that crosses itself (a figure 8) has each loop inside, and where loops overlap, outside.
    /// </summary>
    public bool Encloses(double x, double z)
    {
        if (!Closed || Points.Count < 3)
            return false;
        bool inside = false;
        for (int i = 0, j = Points.Count - 1; i < Points.Count; j = i++)
        {
            var (xi, zi) = Points[i];
            var (xj, zj) = Points[j];
            if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi) + xi)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>Which of the zone's regions a point is in: the band wins, then inside a closed path, else outside.</summary>
    public GuardianRegion RegionOf(double x, double z) =>
        Contains(x, z) ? GuardianRegion.Band : Encloses(x, z) ? GuardianRegion.Inside : GuardianRegion.Outside;

    /// <summary>The path's length (with its closing run).</summary>
    public double Length => Segments().Sum(s => Math.Sqrt((s.B.X - s.A.X) * (s.B.X - s.A.X) + (s.B.Z - s.A.Z) * (s.B.Z - s.A.Z)));

    /// <summary>The world box the zone lies in.</summary>
    public (double MinX, double MinZ, double MaxX, double MaxZ) Bounds =>
        (Points.Min(p => p.X) - Distance, Points.Min(p => p.Z) - Distance, Points.Max(p => p.X) + Distance, Points.Max(p => p.Z) + Distance);

    /// <summary>The zone's edge, traced every <paramref name="step"/> units, for drawing: closed lines round it (and round any hole a closed path leaves).</summary>
    public List<(double X, double Z)[]> Outline(double step)
    {
        var zone = this;
        return Contour.Lines((x, z) => zone.DistanceTo(x, z) - zone.Distance, Bounds, step);
    }

    /// <summary>The run nearest a point: its index (from Points[index] to the next point, or back to the first) and how far it is.</summary>
    public (int Index, double Distance) NearestRun(double x, double z)
    {
        int best = -1, i = 0;
        double bestDistance = double.MaxValue;
        foreach (var (a, b) in Segments())
        {
            double distance = new GuardianZone([a, b], Distance, false).DistanceTo(x, z);
            if (distance < bestDistance)
                (best, bestDistance) = (i, distance);
            i++;
        }
        return (best, bestDistance);
    }

    public GuardianZone WithPoint(int index, (double X, double Z) at) => this with { Points = [.. Points.Take(index), at, .. Points.Skip(index + 1)] };
    public GuardianZone Inserted(int index, (double X, double Z) at) => this with { Points = [.. Points.Take(index), at, .. Points.Skip(index)] };

    /// <summary>The zone without a point; closed only while it keeps more than two.</summary>
    public GuardianZone Removed(int index) => this with { Points = [.. Points.Take(index), .. Points.Skip(index + 1)], Closed = Closed && Points.Count > 3 };
}

/// <summary>A part of a structure as it stands on the map: which feature, where, and the rectangle its footprint blocks.</summary>
public sealed record StructurePart(string Role, string Feature, double X, double Z, int Heading, double X1, double Z1, double X2, double Z2);

/// <summary>
/// Where a structure's parts go. The same arithmetic is in the gadget (Parts, CentreRect). In the structure's own frame
/// the gap runs across x and the passage along z; the walls sit either side of the centre, touching it, and the
/// repair markers one square outside the walls. Facing turns the frame in quarter turns, as the engine turns
/// footprints (only by 90°), so blocked squares always tile with no gap between parts.
/// </summary>
public static class StructureLayout
{
    /// <summary>The engine's heading for each facing (CreateFeature takes a 16-bit heading; 180° is -32768).</summary>
    public static readonly int[] Headings = [0, 16384, -32768, -16384];

    public static (double X, double Z) ToMap(StructureInstance s, double across, double along) => s.Facing switch
    {
        1 => (s.X + along, s.Z - across),
        2 => (s.X - across, s.Z - along),
        3 => (s.X - along, s.Z + across),
        _ => (s.X + across, s.Z + along),
    };

    public static IReadOnlyList<StructurePart> Parts(StructureKind k, StructureInstance s)
    {
        var parts = new List<StructurePart>();
        void Add(string role, string feature, double across, int fx, int fz)
        {
            var (x, z) = ToMap(s, across, 0);
            if (s.Facing is 1 or 3)
                (fx, fz) = (fz, fx);
            parts.Add(new StructurePart(role, feature, x, z, Headings[s.Facing & 3], x - fx * 8, z - fz * 8, x + fx * 8, z + fz * 8));
        }
        if (k.Type == StructureType.Guardian) // a guardian tower: just itself
        {
            Add("centre", k.Centre, 0, k.Gap, k.Depth);
            return parts;
        }
        if (k.Owned) // an oil pump or a shield platform: itself, or derelict
        {
            Add("centre", k.Centre, 0, k.Gap, k.Depth);
            if (k.Debris is string derelict)
                Add("debris", derelict, 0, k.Gap, k.Depth);
            return parts;
        }
        Add("wall", k.Wall, -(k.Gap + k.WallWidth) * 8, k.WallWidth, k.Depth); // (the gadget's Parts: the same order)
        Add("wall", k.Wall, (k.Gap + k.WallWidth) * 8, k.WallWidth, k.Depth);
        Add("centre", k.Centre, 0, k.Gap, k.Depth);
        if (k.Repair is string repair && k.Marker == null) // a kind made from assets: two markers beside its walls
        {
            double out_ = (k.Gap + 2 * k.WallWidth) * 8 + 16;
            Add("repair", repair, -out_, 1, 1);
            Add("repair", repair, out_, 1, 1);
        }
        if (k.Debris is string debris)
            Add("debris", debris, 0, k.Gap, k.Depth); // where the centre stood, once it is destroyed
        if (k.Marker is string marker) // placed markers, where they were put (green; red while it is destroyed)
            foreach (var (mx, mz) in s.MarkerList)
                parts.Add(new StructurePart("marker", marker, mx, mz, Headings[s.Facing & 3], mx - 8, mz - 8, mx + 8, mz + 8));
        return parts;
    }

    /// <summary>
    /// Whether it starts the game destroyed: a door or tunnel can (an open tunnel can't be destroyed); an oil pump or a
    /// shield platform always starts derelict, for whoever repairs it first.
    /// </summary>
    public static bool StartsBroken(StructureKind k, StructureInstance s) => k.Owned || s.Broken && k.Repair != null;

    /// <summary>
    /// The parts there when the game starts: the walls and placed markers (green, or red if it starts destroyed), and
    /// the centre, or, if it starts destroyed, what is left of it (and a kind made from assets' markers).
    /// </summary>
    public static IEnumerable<StructurePart> AtStart(StructureKind k, StructureInstance s)
    {
        bool broken = StartsBroken(k, s);
        return Parts(k, s).Where(p => p.Role is "wall" or "marker" || (p.Role == "centre" ? !broken : broken))
            .Select(p => p.Role == "marker" && broken ? p with { Feature = k.Repair! } : p);
    }
}

/// <summary>
/// mapconfig/map_structures.lua: the kinds, the structures placed, the features units can build on, and the terrain
/// type with no speed the gadget gives the ground under a tunnel top (chosen unused by the map). The gadget reads it
/// at game start; the editor reads and writes it.
/// </summary>
public sealed class StructureConfig
{
    public const string Entry = "mapconfig/map_structures.lua";

    public int? TerrainType { get; set; }

    /// <summary>
    /// For a map whose type map used every type: the type <see cref="TerrainType"/>'s squares (type-map squares, 16
    /// game units) take at game start, one that behaves the same, so the type is free for tunnel tops. Null otherwise.
    /// </summary>
    public int? RemapInto { get; set; }
    public List<(int X, int Z)> RemapSquares { get; } = [];

    public SortedDictionary<string, StructureKind> Kinds { get; } = new(StringComparer.Ordinal);
    public List<StructureInstance> Instances { get; } = [];

    /// <summary>Features (by name) that can be built on: taken off the blocking map; impassable keeps units from crossing them.</summary>
    public SortedDictionary<string, bool> BuildOn { get; } = new(StringComparer.Ordinal);

    public bool IsEmpty => Kinds.Count == 0 && Instances.Count == 0 && BuildOn.Count == 0;

    /// <summary>The map's config (empty when it has none).</summary>
    public static StructureConfig Load(string packageRoot)
    {
        var path = Path.Combine(packageRoot, Entry.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : new StructureConfig();
    }

    public static StructureConfig Parse(string source)
    {
        var config = new StructureConfig();
        if (new LuaInterpreter().Run(source, Entry) is not [LuaMap root, ..])
            throw new InvalidDataException($"{Entry} returns no table.");
        static double Number(LuaMap? t, string key, double fallback = 0) => LuaValuesRuntime.ToNumber(t?.Get(key)) ?? fallback;
        if (LuaValuesRuntime.ToNumber(root.Get("terrainType")) is double type)
            config.TerrainType = (int)type;
        if (root.Get("remap") is LuaMap remap && LuaValuesRuntime.ToNumber(remap.Get("into")) is double into)
        {
            config.RemapInto = (int)into;
            if (remap.Get("squares") is LuaMap squares)
                for (int i = 1; LuaValuesRuntime.ToNumber(squares.Get((double)i)) is double x && LuaValuesRuntime.ToNumber(squares.Get((double)(i + 1))) is double z; i += 2)
                    config.RemapSquares.Add(((int)x, (int)z));
        }
        if (root.Get("kinds") is LuaMap kinds)
            foreach (var (key, value) in kinds.Entries)
                if (key is string name && value is LuaMap k)
                    config.Kinds[name] = new StructureKind(name, StructureKind.ParseType(k.Get("type") as string ?? ""),
                        (int)Number(k, "gap", 4), (int)Number(k, "wallWidth", 2), (int)Number(k, "depth", 2),
                        Number(k, "metal"), Number(k, "energy"), k.Get("openWhenBroken") is not false, Number(k, "height"),
                        k.Get("roof") is "cliff" ? StructureRoof.Cliff : StructureRoof.Flat,
                        k.Get("look") is string look ? StructureKind.ParseLook(look) : StructureLook.Assets, k.Get("settings") as string,
                        Number(k, "metalIncome"), Number(k, "energyIncome"), Strings(k.Get("targets")), Number(k, "killSeconds"), Number(k, "rocketSeconds"),
                        Number(k, "shieldPercent"), Number(k, "energyPerMetal"), Number(k, "bubbleScale"));
        if (root.Get("instances") is LuaMap instances)
            for (int i = 1; instances.Get((double)i) is LuaMap s; i++)
            {
                var markers = new List<(double X, double Z)>();
                if (s.Get("markers") is LuaMap list)
                    for (int m = 1; LuaValuesRuntime.ToNumber(list.Get((double)m)) is double mx && LuaValuesRuntime.ToNumber(list.Get((double)(m + 1))) is double mz; m += 2)
                        markers.Add((mx, mz));
                GuardianZone? zone = null;
                if (s.Get("zone") is LuaMap z)
                {
                    var points = new List<(double X, double Z)>();
                    if (z.Get("points") is LuaMap list2)
                        for (int m = 1; LuaValuesRuntime.ToNumber(list2.Get((double)m)) is double px && LuaValuesRuntime.ToNumber(list2.Get((double)(m + 1))) is double pz; m += 2)
                            points.Add((px, pz));
                    zone = new GuardianZone(points, Number(z, "distance", 160), z.Get("closed") is true);
                }
                GuardianRules? rules = null;
                if (s.Get("rules") is LuaMap r)
                {
                    GuardianRule Rule(string region)
                    {
                        if (r.Get(region) is not LuaMap rule)
                            return GuardianRule.None;
                        IReadOnlyList<int>? teams = null;
                        if (rule.Get("teams") is LuaMap list)
                        {
                            var numbers = new List<int>();
                            for (int t = 1; LuaValuesRuntime.ToNumber(list.Get((double)t)) is double team; t++)
                                numbers.Add((int)team);
                            teams = numbers;
                        }
                        return new GuardianRule(Strings(rule.Get("targets")) ?? [], teams, Number(rule, "delay"));
                    }
                    rules = new GuardianRules(Rule("band"), Rule("inside"), Rule("outside"));
                }
                config.Instances.Add(new StructureInstance(s.Get("kind") as string ?? "", Number(s, "x"), Number(s, "z"),
                    (int)Number(s, "facing") & 3, (int)Number(s, "ground"), s.Get("broken") is true, markers.Count > 0 ? markers : null, zone, rules));
            }
        if (root.Get("buildOn") is LuaMap buildOn)
            foreach (var (key, value) in buildOn.Entries)
                if (key is string name && value is LuaMap rule)
                    config.BuildOn[name] = rule.Get("impassable") is not false;
        return config;
    }

    /// <summary>A Lua list of strings, in order (null when it isn't one).</summary>
    static IReadOnlyList<string>? Strings(object? value)
    {
        if (value is not LuaMap list)
            return null;
        var items = new List<string>();
        for (int i = 1; list.Get((double)i) is string s; i++)
            items.Add(s);
        return items;
    }

    public string ToLua()
    {
        static string N(double v) => LuaValuesRuntime.NumberToString(v);
        var text = new StringBuilder();
        text.Append("-- Doors and tunnels, read by LuaGaia/Gadgets/map_structures.lua. Written by BAR Map Editor.\n");
        text.Append("-- Sizes are footprint squares (16 game units); height is game units; metal and energy are what a full repair costs.\n");
        text.Append("return {\n");
        if (TerrainType is int type)
            text.Append("\tterrainType = ").Append(type.ToString(CultureInfo.InvariantCulture)).Append(", -- a terrain type with no speed, under tunnel tops\n");
        if (TerrainType is int freed && RemapInto is int into)
        {
            text.Append("\t-- The map's type map uses every type: type ").Append(freed).Append("'s squares take type ").Append(into)
                .Append(" (which behaves the same) at game start,\n\t-- freeing it. Squares of 16 game units, x then z.\n");
            text.Append("\tremap = { into = ").Append(into).Append(", squares = {");
            for (int i = 0; i < RemapSquares.Count; i++)
                text.Append(i % 16 == 0 ? "\n\t\t" : " ").Append(RemapSquares[i].X).Append(", ").Append(RemapSquares[i].Z).Append(',');
            text.Append("\n\t} },\n");
        }
        text.Append("\tkinds = {\n");
        foreach (var k in Kinds.Values)
        {
            text.Append("\t\t").Append(k.Name).Append(" = { type = \"").Append(StructureKind.TypeName(k.Type)).Append('"')
                .Append(", wall = \"").Append(k.Wall).Append("\", centre = \"").Append(k.Centre).Append('"');
            if (k.Repair != null)
                text.Append(", repair = \"").Append(k.Repair).Append('"');
            if (k.Debris != null)
                text.Append(", broken = \"").Append(k.Debris).Append('"');
            if (k.Marker != null)
                text.Append(", marker = \"").Append(k.Marker).Append('"');
            if (k.Beam != null)
                text.Append(", beam = \"").Append(k.Beam).Append("\", metalIncome = ").Append(N(k.MetalIncome)).Append(", energyIncome = ").Append(N(k.EnergyIncome));
            if (k.Type == StructureType.Guardian)
            {
                text.Append(", killSeconds = ").Append(N(k.KillSeconds)).Append(", rocketSeconds = ").Append(N(k.RocketSeconds))
                    .Append(", rocket = \"").Append(StructureKind.GuardianRocket).Append("\", targets = {");
                for (int i = 0; i < k.TargetList.Count; i++)
                    text.Append(i % 8 == 0 ? "\n\t\t\t" : " ").Append('"').Append(k.TargetList[i]).Append("\",");
                text.Append(k.TargetList.Count > 0 ? "\n\t\t}" : "}");
            }
            if (k.Type == StructureType.ShieldPad)
                text.Append(", shieldPercent = ").Append(N(k.ShieldPercent)).Append(", energyPerMetal = ").Append(N(k.EnergyPerMetal))
                    .Append(", bubbleScale = ").Append(N(k.BubbleScale));
            text.Append(", gap = ").Append(k.Gap).Append(", wallWidth = ").Append(k.WallWidth).Append(", depth = ").Append(k.Depth)
                .Append(", metal = ").Append(N(k.Metal)).Append(", energy = ").Append(N(k.Energy))
                .Append(", openWhenBroken = ").Append(k.OpenWhenBroken ? "true" : "false")
                .Append(", height = ").Append(N(k.Height)).Append(", roof = \"").Append(k.Roof == StructureRoof.Cliff ? "cliff" : "flat")
                .Append("\", look = \"").Append(StructureKind.LookName(k.Look)).Append('"');
            if (k.Settings != null) // percent-encoded: no quotes or backslashes to escape
                text.Append(", settings = \"").Append(k.Settings).Append('"');
            text.Append(" },\n");
        }
        text.Append("\t},\n");
        text.Append("\tinstances = {\n");
        foreach (var s in Instances)
        {
            text.Append("\t\t{ kind = \"").Append(s.Kind).Append("\", x = ").Append(N(s.X)).Append(", z = ").Append(N(s.Z))
                .Append(", facing = ").Append(s.Facing).Append(", ground = ").Append(s.Ground).Append(s.Broken ? ", broken = true" : "");
            if (s.MarkerList.Count > 0)
                text.Append(", markers = { ").Append(string.Join(", ", s.MarkerList.Select(m => N(m.X) + ", " + N(m.Z)))).Append(" }");
            if (s.Zone is GuardianZone zone)
                text.Append(", zone = { distance = ").Append(N(zone.Distance)).Append(", closed = ").Append(zone.Closed ? "true" : "false")
                    .Append(", points = { ").Append(string.Join(", ", zone.Points.Select(p => N(p.X) + ", " + N(p.Z)))).Append(" } }");
            if (s.Rules is GuardianRules rules)
            {
                // What it does in each region: teams are BAR's lobby teams (ally team + 1), all of them when absent.
                text.Append(",\n\t\t\trules = {");
                foreach (var region in new[] { GuardianRegion.Band, GuardianRegion.Inside, GuardianRegion.Outside })
                {
                    var rule = rules[region];
                    text.Append("\n\t\t\t\t").Append(GuardianRules.RegionName(region)).Append(" = { delay = ").Append(N(rule.DelaySeconds));
                    if (rule.Teams != null)
                        text.Append(", teams = { ").Append(string.Join(", ", rule.Teams.Order())).Append(rule.Teams.Count > 0 ? " }" : "}");
                    text.Append(", targets = {");
                    for (int i = 0; i < rule.Targets.Count; i++)
                        text.Append(i % 8 == 0 ? "\n\t\t\t\t\t" : " ").Append('"').Append(rule.Targets[i]).Append("\",");
                    text.Append(rule.Targets.Count > 0 ? "\n\t\t\t\t} }," : "} },");
                }
                text.Append("\n\t\t\t}\n\t\t");
            }
            text.Append(" },\n");
        }
        text.Append("\t},\n");
        text.Append("\tbuildOn = {\n");
        foreach (var (name, impassable) in BuildOn)
            text.Append("\t\t").Append(name).Append(" = { impassable = ").Append(impassable ? "true" : "false").Append(" },\n");
        text.Append("\t},\n");
        text.Append("}\n");
        return text.ToString();
    }
}

/// <summary>The files that make structures work in game: the gadget, and LuaGaia's loader for maps without one.</summary>
public static class StructureFiles
{
    public const string GadgetEntry = "LuaGaia/Gadgets/map_structures.lua";
    public const string LuaGaiaMain = "LuaGaia/main.lua", LuaGaiaDraw = "LuaGaia/draw.lua";

    /// <summary>The guardian towers' rocket (a weapon of the map's, harmless), its model, and their effects.</summary>
    public const string RocketWeaponEntry = "weapons/barmapeditor_guardian_rocket.lua";
    public const string RocketModelEntry = "objects3d/barmapeditor_guardian_rocket.s3o";
    public const string GuardianEffectsEntry = "effects/barmapeditor_guardian.lua";

    /// <summary>The shield platforms' effects: a bubble going up, taking a hit, and going down.</summary>
    public const string ShieldEffectsEntry = "effects/barmapeditor_shield.lua";

    /// <summary>
    /// The shields' effects, from BAR's own projectile textures (flare, explo and circularthingy in its
    /// gamedata/resources.lua, checked 2026-10-16): a ring of light as a bubble goes up, a flash where one takes a hit,
    /// and a burst of sparks as one goes down.
    /// </summary>
    public const string ShieldEffectsText = """
-- The shield platforms' effects (LuaGaia/Gadgets/map_structures.lua). Written by BAR Map Editor.
return {
	barmapeditor_shield_up = {
		ring = {
			class = "CSimpleParticleSystem",
			count = 1,
			ground = true,
			air = true,
			water = true,
			properties = {
				airdrag = 1,
				colormap = [[0.3 0.8 1 0.6   0.2 0.6 1 0.3   0 0 0 0]],
				directional = false,
				emitrot = 0,
				emitrotspread = 0,
				emitvector = [[0, 1, 0]],
				gravity = [[0, 0, 0]],
				numparticles = 1,
				particlelife = 18,
				particlelifespread = 0,
				particlesize = 8,
				particlesizespread = 0,
				particlespeed = 0,
				particlespeedspread = 0,
				pos = [[0, 0, 0]],
				sizegrowth = 3,
				sizemod = 1,
				texture = [[circularthingy]],
			},
		},
	},
	barmapeditor_shield_hit = {
		flash = {
			class = "CSimpleParticleSystem",
			count = 1,
			ground = true,
			air = true,
			water = true,
			properties = {
				airdrag = 1,
				colormap = [[0.6 0.95 1 0.8   0.2 0.6 1 0.4   0 0 0 0]],
				directional = false,
				emitrot = 0,
				emitrotspread = 0,
				emitvector = [[0, 1, 0]],
				gravity = [[0, 0, 0]],
				numparticles = 1,
				particlelife = 8,
				particlelifespread = 2,
				particlesize = 10,
				particlesizespread = 4,
				particlespeed = 0,
				particlespeedspread = 0,
				pos = [[0, 0, 0]],
				sizegrowth = 1.5,
				sizemod = 1,
				texture = [[flare]],
			},
		},
	},
	barmapeditor_shield_down = {
		sparks = {
			class = "CSimpleParticleSystem",
			count = 1,
			ground = true,
			air = true,
			water = true,
			properties = {
				airdrag = 0.9,
				colormap = [[0.5 0.9 1 0.9   0.2 0.5 1 0.5   0 0 0 0]],
				directional = true,
				emitrot = 0,
				emitrotspread = 180,
				emitvector = [[0, 1, 0]],
				gravity = [[0, -0.05, 0]],
				numparticles = 16,
				particlelife = 16,
				particlelifespread = 6,
				particlesize = 3,
				particlesizespread = 2,
				particlespeed = 3,
				particlespeedspread = 2,
				pos = [[0, 0, 0]],
				sizegrowth = -0.1,
				sizemod = 1,
				texture = [[explo]],
			},
		},
	},
}
""";

    /// <summary>
    /// The rocket guardian towers fire, for show: a missile of the map's that does no harm at all (no damage, area,
    /// craters or push). The gadget fires it straight up and bursts it high over the tower. Names start
    /// barmapeditor_ so nothing of BAR's is replaced (BAR loads a map's files ahead of its own).
    /// </summary>
    public const string RocketWeaponText = """
-- The guardian towers' rocket, for show: it does no harm at all. The towers fire it straight up and burst it high
-- above themselves (LuaGaia/Gadgets/map_structures.lua). Written by BAR Map Editor.
return {
	barmapeditor_guardian_rocket = {
		name = "Guardian rocket",
		weapontype = "MissileLauncher",
		model = "barmapeditor_guardian_rocket.s3o",
		range = 2000,
		reloadtime = 1,
		startvelocity = 6,
		weaponvelocity = 14,
		weaponacceleration = 0.5,
		flighttime = 3,
		tracks = false,
		turnrate = 0,
		smoketrail = true,
		smokeperiod = 3,
		smoketime = 24,
		smokesize = 5,
		smokecolor = 0.8,
		areaofeffect = 0,
		edgeeffectiveness = 0,
		craterareaofeffect = 0,
		craterboost = 0,
		cratermult = 0,
		impulseboost = 0,
		impulsefactor = 0,
		collidefriendly = false,
		collidefeature = false,
		avoidfeature = false,
		avoidfriendly = false,
		explosiongenerator = "custom:barmapeditor_guardian_burst",
		customparams = { made_by = "BAR Map Editor" },
		damage = { default = 0 },
	},
}
""";

    /// <summary>The guardian towers' effects: a rocket's burst high over a tower, and the barrage's hits on what it wears down.</summary>
    public const string GuardianEffectsText = """
-- The guardian towers' effects (LuaGaia/Gadgets/map_structures.lua). Written by BAR Map Editor.
--   barmapeditor_guardian_burst: a rocket bursting high over its tower.
--   barmapeditor_guardian_hit: the barrage hitting something in the tower's zone.
return {
	barmapeditor_guardian_burst = {
		usedefaultexplosions = false,
		flash = {
			air = true, ground = true, water = true, count = 1,
			class = "CHeatCloudProjectile",
			properties = { heat = 10, maxheat = 10, heatfalloff = 1.1, size = 26, sizegrowth = 6, speed = [[0, 0, 0]], texture = [[flare]] },
		},
		sparks = {
			air = true, ground = true, water = true, count = 1,
			class = "CSimpleParticleSystem",
			properties = {
				airdrag = 0.9, colormap = [[1 0.9 0.6 0.04   1 0.5 0.15 0.02   0.2 0.1 0.05 0.01   0 0 0 0]],
				directional = true, emitrot = 0, emitrotspread = 180, emitvector = [[0, 1, 0]], gravity = [[0, -0.15, 0]],
				numparticles = 40, particlelife = 16, particlelifespread = 10, particlesize = 3, particlesizespread = 2,
				particlespeed = 7, particlespeedspread = 5, pos = [[0, 0, 0]], sizegrowth = -0.05, sizemod = 1, texture = [[flare]],
			},
		},
	},
	barmapeditor_guardian_hit = {
		usedefaultexplosions = false,
		flash = {
			air = true, ground = true, water = true, count = 1,
			class = "CHeatCloudProjectile",
			properties = { heat = 8, maxheat = 8, heatfalloff = 1.3, size = 9, sizegrowth = 3, speed = [[0, 0, 0]], texture = [[explo]] },
		},
		sparks = {
			air = true, ground = true, water = true, count = 1,
			class = "CSimpleParticleSystem",
			properties = {
				airdrag = 0.88, colormap = [[1 0.8 0.5 0.03   0.8 0.3 0.1 0.02   0 0 0 0]],
				directional = true, emitrot = 0, emitrotspread = 180, emitvector = [[0, 1, 0]], gravity = [[0, -0.2, 0]],
				numparticles = 10, particlelife = 10, particlelifespread = 6, particlesize = 2, particlesizespread = 1.5,
				particlespeed = 4, particlespeedspread = 3, pos = [[0, 0, 0]], sizegrowth = 0, sizemod = 1, texture = [[flare]],
			},
		},
	},
}
""";

    /// <summary>LuaGaia's loader, byte for byte as every map looked at ships it (it runs the engine's gadget handler).</summary>
    public const string MainText = "if AllowUnsafeChanges then AllowUnsafeChanges(\"USE AT YOUR OWN PERIL\") end\r\nVFS.Include(\"LuaGadgets/gadgets.lua\",nil, VFS.BASE)\r\n";
    public const string DrawText = "VFS.Include(\"LuaGadgets/gadgets.lua\",nil, VFS.BASE)\r\n";

    /// <summary>
    /// The gadget (LuaGaia, synced). Every engine call it makes was checked in Recoil's source (2026-10-15):
    /// CreateFeature, DestroyFeature, ValidFeatureID, GetAllFeatures, GetFeatureDefID, GetFeaturePosition,
    /// GetFeatureHeading, GetFeatureHealth, SetFeatureHealth, SetFeatureBlocking, SetMapSquareTerrainType,
    /// SetTerrainTypeData, UseTeamResource, GetUnitsInRectangle, GetUnitDefID, GetUnitPosition, DestroyUnit,
    /// GetGroundHeight, GetGameFrame, CreateFeature's team (a feature made for a team wears its colour; TransferFeature
    /// doesn't recolour, so a pump is made again for its owner), SetFeatureMoveCtrl (held where it is, in the air too),
    /// SetFeatureDirection (a front only: right and up from the world's up), AddTeamResource ("metal" / "energy";
    /// counted as income, as BAR's top bar shows it), SendMessageToTeam, GetFeatureTeam, FeatureDefs (to leave out
    /// parts a map doesn't define), MoveCtrl.Enable (any unit, buildings included; it then holds its place: no
    /// gravity, no ground tracking), MoveCtrl.SetPosition, GetFeaturesInRectangle and SetFeaturePosition (BAR's footprint
    /// clearance: features step aside while a building is created over them); callins Initialize, FeatureCreated, FeatureDestroyed,
    /// AllowFeatureBuildStep (negative part on reclaim), AllowUnitCreation, UnitCreated (on the nanoframe),
    /// UnitDestroyed, GameFrame. For guardian towers (checked 2026-10-16): UnitDefNames (speed in elmos a second: the
    /// move types divide it by GAME_SPEED), WeaponDefNames, GetUnitHealth,
    /// SetUnitHealth, GetUnitIsBeingBuilt, GetUnitTransporter (nothing when not carried), SpawnCEG, SpawnProjectile
    /// (pos, end, speed, ttl, team; Gaia's by default), GetProjectilePosition, DeleteProjectile (no explosion),
    /// GetGaiaTeamID, GetUnitAllyTeam, GetAllUnits (every unit, to LuaGaia's full read). For shield platforms (checked
    /// 2026-10-16): the UnitDamaged callin (after ApplyDamage takes the health, before DoDamage's death check),
    /// GetUnitPosition's middle, GetUnitTeam, AreTeamsAllied, GetUnitsInSphere, UnitDefs' radius, height, metalCost,
    /// SendToUnsynced; on the drawing side RecvFromSynced, DrawWorld, GetSpectatingState, GetMyAllyTeamID, IsUnitInLos
    /// (with an ally team: LuaGaia reads everything, so IsUnitVisible wouldn't hide what this player can't see),
    /// IsUnitInView, GetUnitViewPosition, and gl's CreateList, BeginEnd, Vertex, CallList (still in Recoil's
    /// LuaOpenGL.cpp). BAR loads a map's weapons/*.lua (gamedata/weapondefs.lua) and effects/*.lua (the engine's
    /// explosions.lua, the map included). A building lifted higher than its radius stops blocking its footprint
    /// (SolidObject.cpp FootPrintOnGround), so the gadget keeps buildings on a roof apart itself; builders reach it
    /// still, since build range is measured flat (BuilderCAI.h, unless a unit sets buildRange3D).
    /// </summary>
    public const string GadgetText = """
--------------------------------------------------------------------------------
-- Map structures: doors and tunnels made of several features. Written by BAR Map Editor; its data is
-- mapconfig/map_structures.lua.
--
-- A door or a tunnel is two indestructible side walls with a centre between them.
--   Door: the centre blocks and can't be built on.
--   Tunnel: the centre is a top that can be built on (it is taken off the blocking map) but not crossed (the
--     ground under it gets a terrain type with no speed). Buildings go wholly on it, and are lifted onto its roof
--     (the kind's height). When it is destroyed, everything on it is destroyed too, except aircraft flying over.
--     A tunnel whose roof is rock (roof = "cliff") is built on by nobody: it blocks as a door does.
--   Open tunnel: units pass through the middle; it can't collapse.
-- A door or tunnel can start the game destroyed (broken = true): the gap open, ready to be repaired.
-- When a door or tunnel centre is destroyed the gap opens. A built one leaves what is left of it in the gap
-- (broken = "...": it doesn't block, and its middle is clear), and the repair markers placed for it (markers in its
-- instance, always on the map) turn from green (marker = "...") to red (repair = "..."); a kind made from assets
-- puts two markers beside its walls instead. Builders repair it by reclaiming what is left of it or any red marker
-- (the engine lets builders work on plain features only by reclaiming); all feed one shared pool, paid for in metal
-- and energy as the work goes on, and their health shows how far it has got. When the pool is full and nothing
-- stands in the gap, the centre is put back and the markers turn green.
-- An oil pump (type = "pump") starts derelict; the team that repairs it owns it and is given metal and energy a
-- second while it stands, its beam nodding; destroyed, it is derelict again for whoever repairs it next.
-- A guardian tower (type = "guardian") destroys units of the types its kind lists while they are near the path it
-- guards (see below).
-- A shield platform (type = "shield_pad") starts derelict like a pump; its owner's and allies' units get a bubble
-- shield as they drive off it (see below).
--------------------------------------------------------------------------------

function gadget:GetInfo()
	return {
		name    = "Map structures (doors, tunnels)",
		desc    = "Doors and tunnels made of several features; repaired by reclaiming their markers",
		author  = "BAR Map Editor",
		date    = "2026",
		license = "GNU GPL, v2 or later",
		layer   = 0,
		enabled = true,
	}
end

if not gadgetHandler:IsSyncedCode() then
	-- The drawing side: the shield platforms' bubbles, round the units that have them, as the game side tells of them
	-- (SendToUnsynced: unit, radius, how much is left; 0 when it is gone). Only units this player can see: LuaGaia
	-- reads everything, so its line of sight is asked for (a spectator with full view sees them all).
	local bubbles = {} -- by unit: { radius, left }
	local sphere, lines

	function gadget:RecvFromSynced(name, unitID, radius, left)
		if name ~= "barmapeditor_shield" then
			return false
		end
		if left and left > 0 then
			bubbles[unitID] = { radius = radius, left = left }
		else
			bubbles[unitID] = nil
		end
		return true
	end

	-- A sphere of radius 1: its surface (triangles), and lines of latitude and longitude over it.
	local function Point(lat, lon)
		local a, b = math.pi * lat / 12 - math.pi / 2, 2 * math.pi * lon / 24
		return math.cos(a) * math.cos(b), math.sin(a), math.cos(a) * math.sin(b)
	end
	local function MakeLists()
		sphere = gl.CreateList(function()
			gl.BeginEnd(GL.TRIANGLES, function()
				for lat = 0, 11 do
					for lon = 0, 23 do
						local corners = { { lat, lon }, { lat + 1, lon }, { lat + 1, lon + 1 }, { lat, lon }, { lat + 1, lon + 1 }, { lat, lon + 1 } }
						for _, c in ipairs(corners) do
							gl.Vertex(Point(c[1], c[2]))
						end
					end
				end
			end)
		end)
		lines = gl.CreateList(function()
			gl.BeginEnd(GL.LINES, function()
				for lat = 1, 11 do
					for lon = 0, 23 do
						gl.Vertex(Point(lat, lon))
						gl.Vertex(Point(lat, lon + 1))
					end
				end
				for lon = 0, 23, 2 do
					for lat = 0, 11 do
						gl.Vertex(Point(lat, lon))
						gl.Vertex(Point(lat + 1, lon))
					end
				end
			end)
		end)
	end

	function gadget:DrawWorld()
		if next(bubbles) == nil then
			return
		end
		if not sphere then
			MakeLists()
		end
		local _, fullView = Spring.GetSpectatingState()
		local ally = Spring.GetMyAllyTeamID()
		gl.DepthTest(true)
		gl.DepthMask(false)
		gl.Blending(GL.SRC_ALPHA, GL.ONE)
		gl.Culling(GL.BACK)
		for unitID, b in pairs(bubbles) do
			if (fullView or Spring.IsUnitInLos(unitID, ally)) and Spring.IsUnitInView(unitID) then
				local x, y, z = Spring.GetUnitViewPosition(unitID, true)
				if x then
					gl.PushMatrix()
					gl.Translate(x, y, z)
					gl.Scale(b.radius, b.radius, b.radius)
					gl.Color(0.3, 0.75, 1.0, 0.04 + 0.08 * b.left)
					gl.CallList(sphere)
					gl.Color(0.5, 0.9, 1.0, 0.1 + 0.25 * b.left)
					gl.CallList(lines)
					gl.PopMatrix()
				end
			end
		end
		gl.Culling(false)
		gl.Blending(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA)
		gl.DepthMask(true)
		gl.Color(1, 1, 1, 1)
	end

	function gadget:Shutdown()
		if sphere then
			gl.DeleteList(sphere)
			gl.DeleteList(lines)
		end
	end
	return
end

local CONFIG = "mapconfig/map_structures.lua"
if not VFS.FileExists(CONFIG) then
	return false
end

local config = VFS.Include(CONFIG) or {}
local kinds = config.kinds or {}
local placed = config.instances or {}
local buildOn = config.buildOn or {}
local blockedType = config.terrainType

-- A kind made before placed markers and wrecks has no definitions for them: it keeps its two markers beside its walls.
local defined = {}
for _, def in pairs(FeatureDefs) do
	defined[def.name] = true
end
for _, k in pairs(kinds) do
	if k.marker and not defined[k.marker] then
		k.marker = nil
	end
	if k.broken and not defined[k.broken] then
		k.broken = nil
	end
end

local HEADINGS = { [0] = 0, [1] = 16384, [2] = -32768, [3] = -16384 }
local CHECK_FRAMES = 15

local instances = {} -- every structure, with its parts' feature IDs
local partOf = {}    -- featureID -> { s = structure, role = "wall" | "centre" | "repair" | "marker" | "debris" }
local builtOn = {}   -- featureID of a feature that can be built on -> what it changed
local raisedOn = {}  -- unitID of a building lifted onto a tunnel's roof -> that structure
local pumps = {}     -- the oil pumps among the structures
local pads = {}      -- the shield platforms among the structures

-- Whether buildings go on a kind's centre: a tunnel with a flat roof.
local function Buildable(k)
	return k.type == "tunnel" and k.roof ~= "cliff"
end

-- A point in a structure's own frame (across the gap, along the passage), on the map.
local function ToMap(s, across, along)
	local f = s.facing
	if f == 1 then return s.x + along, s.z - across end
	if f == 2 then return s.x - across, s.z - along end
	if f == 3 then return s.x - along, s.z + across end
	return s.x + across, s.z + along
end

-- The rectangle (game units) a footprint of fx by fz squares centred at x, z covers, turned to the facing.
local function Rect(facing, x, z, fx, fz)
	if facing == 1 or facing == 3 then fx, fz = fz, fx end
	return x - fx * 8, z - fz * 8, x + fx * 8, z + fz * 8
end

local function Parts(s)
	local k = s.kind
	local g, w, d = k.gap, k.wallWidth, k.depth
	local parts = {
		{ role = "wall", name = k.wall, across = -(g + w) * 8 },
		{ role = "wall", name = k.wall, across = (g + w) * 8 },
		{ role = "centre", name = k.centre, across = 0 },
	}
	if k.repair and not k.marker then -- a kind made from assets: its markers beside its walls
		local out = (g + 2 * w) * 8 + 16
		parts[4] = { role = "repair", name = k.repair, across = -out }
		parts[5] = { role = "repair", name = k.repair, across = out }
	end
	for _, p in ipairs(parts) do
		p.x, p.z = ToMap(s, p.across, 0)
	end
	return parts
end

local function CentreRect(s)
	return Rect(s.facing, s.x, s.z, s.kind.gap, s.kind.depth)
end

local function Create(s, part)
	local id = Spring.CreateFeature(part.name, part.x, Spring.GetGroundHeight(part.x, part.z), part.z, HEADINGS[s.facing])
	if id then
		partOf[id] = { s = s, role = part.role }
	end
	return id
end

local function Remove(id)
	partOf[id] = nil -- first, so FeatureDestroyed doesn't take its going for damage
	if Spring.ValidFeatureID(id) then
		Spring.DestroyFeature(id)
	end
end

-- Gives the type-map squares (16 game units) under a rectangle the type with no speed, keeping what was there.
local function Impassable(x1, z1, x2, z2, saved, ground)
	if not blockedType then
		return
	end
	for z = math.floor(z1 / 16) * 16, z2 - 1, 16 do
		for x = math.floor(x1 / 16) * 16, x2 - 1, 16 do
			local old = Spring.SetMapSquareTerrainType(x, z, blockedType)
			if old == blockedType then
				old = ground -- already blocked (a reload): the map's own type, from the config
			end
			saved[#saved + 1] = { x = x, z = z, type = old or 0 }
		end
	end
end

local function Restore(saved)
	for i = #saved, 1, -1 do
		Spring.SetMapSquareTerrainType(saved[i].x, saved[i].z, saved[i].type)
		saved[i] = nil
	end
end

-- Off the blocking map so it can be built on, still hit by shots; units are kept off it by its squares' type.
local function MakeBuildable(id)
	Spring.SetFeatureBlocking(id, false, false, true, true)
end

-- A standing tunnel top: off the blocking map, its ground impassable, and its roof's height known.
local function TopStands(s, id)
	MakeBuildable(id)
	local x1, z1, x2, z2 = CentreRect(s)
	Impassable(x1, z1, x2, z2, s.saved, s.ground)
	local _, y = Spring.GetFeaturePosition(id)
	s.roofY = y and (s.kind.height or 0) > 0 and y + s.kind.height or nil
end

local function PutCentreBack(s)
	s.centreID = Create(s, Parts(s)[3])
	if s.centreID and Buildable(s.kind) then
		TopStands(s, s.centreID)
	end
end

local function IsFlying(unitID)
	local def = UnitDefs[Spring.GetUnitDefID(unitID)]
	if not (def and def.canFly) then
		return false
	end
	local x, y, z = Spring.GetUnitPosition(unitID)
	return y > Spring.GetGroundHeight(x, z) + 20
end

-- Units in a rectangle, leaving out aircraft in the air.
local function UnitsOn(x1, z1, x2, z2)
	local found = {}
	for _, unitID in ipairs(Spring.GetUnitsInRectangle(x1, z1, x2, z2) or {}) do
		if not IsFlying(unitID) then
			found[#found + 1] = unitID
		end
	end
	return found
end

-- Everything a destroyed structure is repaired through: its red markers (or its markers beside the walls) and what
-- is left of it in the gap.
local function Targets(s)
	local list = {}
	for _, id in ipairs(s.repairs) do
		list[#list + 1] = id
	end
	if s.debrisID then
		list[#list + 1] = s.debrisID
	end
	return list
end

local function ShowProgress(s)
	for _, id in ipairs(Targets(s)) do
		if Spring.ValidFeatureID(id) then
			local _, maxHealth = Spring.GetFeatureHealth(id)
			Spring.SetFeatureHealth(id, math.max(maxHealth * 0.01, maxHealth * s.progress))
		end
	end
end

-- What a built door or tunnel leaves when destroyed, in the gap until it is repaired.
local function AddDebris(s)
	if s.kind.broken and not s.debrisID then
		s.debrisID = Create(s, { role = "debris", name = s.kind.broken, x = s.x, z = s.z })
	end
end

-- The markers placed for a structure: green while it stands; red, to be reclaimed, while it is destroyed.
local function PlaceMarkers(s, broken)
	for _, id in ipairs(s.greens) do
		Remove(id)
	end
	s.greens = {}
	for _, at in ipairs(s.markerAt) do
		local id = Create(s, { role = broken and "repair" or "marker", name = broken and s.kind.repair or s.kind.marker, x = at.x, z = at.z })
		if id and broken then
			s.repairs[#s.repairs + 1] = id
		elseif id then
			s.greens[#s.greens + 1] = id
		end
	end
end

-- The markers that repair it while destroyed: its placed markers turned red, or a kind made from assets' two beside
-- its walls.
local function AddRepairMarkers(s)
	for _, id in ipairs(s.repairs) do
		Remove(id)
	end
	s.repairs = {}
	if s.kind.marker then
		PlaceMarkers(s, true)
	elseif s.kind.repair then
		local parts = Parts(s)
		for i = 4, #parts do
			local id = Create(s, parts[i])
			if id then
				s.repairs[#s.repairs + 1] = id
			end
		end
	end
	ShowProgress(s)
end

-- Oil pumps --------------------------------------------------------------------
-- A pump starts derelict; builders repair it by reclaiming the derelict (as a door's wreck), and the team that
-- finishes the repair owns it: the pump is made again as that team's, so it wears its colour, and it gives it metal
-- and energy every second while it stands. Its beam is its own feature, held in the air by MoveCtrl at the post's
-- bearing and pointed each frame along the pump, tipped up and down: SetFeatureDirection with only a front works out
-- right and up from the world's up, so the beam pitches about the axis across the pump whichever way it faces.
local NOD_FRAMES = 120              -- frames for one nod, down and up
local NOD = 0.22                    -- radians each way
local PIVOT_UP, PIVOT_ALONG = 50, 4 -- the bearing: above the pump's ground, and along it from its centre

-- The way a pump faces (its +z), on the map.
local function FacingVector(s)
	local f = s.facing
	if f == 1 then return 1, 0 end
	if f == 2 then return 0, -1 end
	if f == 3 then return -1, 0 end
	return 0, 1
end

local function PlaceBeam(s, team)
	local bx, bz = ToMap(s, 0, PIVOT_ALONG)
	local id = Spring.CreateFeature(s.kind.beam, bx, Spring.GetGroundHeight(s.x, s.z) + PIVOT_UP, bz, HEADINGS[s.facing], team)
	if id then
		partOf[id] = { s = s, role = "beam" }
		Spring.SetFeatureMoveCtrl(id, true, 0, 0, 0, 0, 0, 0)
	end
	s.beamID = id
end

-- Whether a kind starts derelict and belongs to whoever repairs it: an oil pump or a shield platform.
local function Owned(k)
	return k.type == "pump" or k.type == "shield_pad"
end

-- A shield platform as it stands: off the blocking map so units drive over it, still hit by shots.
local function PadStands(id)
	Spring.SetFeatureBlocking(id, false, false, true, true)
end

-- Repaired: the pump and its beam (or the platform) made again as the repairing team's, working for it.
local function PumpRunning(s, team)
	s.owner = team
	s.centreID = Spring.CreateFeature(s.kind.centre, s.x, Spring.GetGroundHeight(s.x, s.z), s.z, HEADINGS[s.facing], team)
	if s.centreID then
		partOf[s.centreID] = { s = s, role = "centre" }
	end
	if s.kind.type == "shield_pad" then
		if s.centreID then
			PadStands(s.centreID)
		end
		Spring.SendMessageToTeam(team, "Shield platform repaired: your units and your allies' get a bubble shield as they drive off it, " ..
			"paying " .. (s.kind.energyPerMetal or 0) .. " energy for each metal they cost.")
		return
	end
	PlaceBeam(s, team)
	Spring.SendMessageToTeam(team, "Oil pump repaired: it gives you " .. (s.kind.metalIncome or 0) .. " metal and " ..
		(s.kind.energyIncome or 0) .. " energy a second while it stands.")
end

-- Destroyed: a pump's beam goes with it, and its owner is told.
local function PumpStopped(s)
	if s.beamID then
		Remove(s.beamID)
		s.beamID = nil
	end
	if s.owner then
		Spring.SendMessageToTeam(s.owner, s.kind.type == "shield_pad" and "Your shield platform was destroyed: repair it to use it again."
			or "Your oil pump was destroyed: repair it to have it pump again.")
	end
	s.owner = nil
end

-- Every frame owned pumps nod; every second (as the engine counts income) they pay their owners.
local function PumpFrame(n)
	for _, s in ipairs(pumps) do
		if s.owner then
			if s.beamID then
				local a = NOD * math.sin(2 * math.pi * n / NOD_FRAMES + s.phase)
				local fx, fz = FacingVector(s)
				Spring.SetFeatureDirection(s.beamID, fx * math.cos(a), math.sin(a), fz * math.cos(a))
			end
			if n % 30 == 0 then
				Spring.AddTeamResource(s.owner, "metal", s.kind.metalIncome or 0)
				Spring.AddTeamResource(s.owner, "energy", s.kind.energyIncome or 0)
			end
		end
	end
end

-- The centre was destroyed: the gap opens (a tunnel top takes what stands on it with it), what is left of it lies in
-- the gap, and the markers turn red.
local function Broken(s)
	s.centreID = nil
	s.roofY = nil
	if Owned(s.kind) then
		PumpStopped(s)
	end
	if Buildable(s.kind) then
		local x1, z1, x2, z2 = CentreRect(s)
		for _, unitID in ipairs(UnitsOn(x1 - 8, z1 - 8, x2 + 8, z2 + 8)) do
			Spring.DestroyUnit(unitID, false, false)
		end
		for unitID in pairs(s.raised) do
			raisedOn[unitID] = nil
		end
		s.raised = {}
		if s.kind.openWhenBroken ~= false then
			Restore(s.saved)
		end
	end
	s.progress = 0
	AddDebris(s)
	AddRepairMarkers(s)
end

-- A structure the map starts destroyed (broken = true in the config): no centre, what is left of it in the gap, its
-- markers red; a tunnel that stays blocked while broken (openWhenBroken = false) keeps its ground impassable.
local function StartBroken(s)
	s.centreID = nil
	if Buildable(s.kind) and s.kind.openWhenBroken == false then
		local x1, z1, x2, z2 = CentreRect(s)
		Impassable(x1, z1, x2, z2, s.saved, s.ground)
	end
	s.progress = 0
	AddDebris(s)
	AddRepairMarkers(s)
end

local function TryRebuild(s)
	if s.progress < 1 or s.centreID then
		return
	end
	local x1, z1, x2, z2 = CentreRect(s)
	if s.kind.type ~= "shield_pad" and #UnitsOn(x1, z1, x2, z2) > 0 then
		s.held = true -- repaired, but something stands in the gap: put back once it is clear (units drive over a platform)
		return
	end
	s.held = false
	for _, id in ipairs(s.repairs) do
		Remove(id)
	end
	s.repairs = {}
	s.progress = 0
	if s.debrisID then
		Remove(s.debrisID)
		s.debrisID = nil
	end
	Restore(s.saved) -- a tunnel that stayed blocked when broken: blocked again, once, below
	if Owned(s.kind) then
		PumpRunning(s, s.lastTeam)
		return
	end
	PutCentreBack(s)
	if s.kind.marker then
		PlaceMarkers(s, false)
	end
end

local function Near(id, x, z)
	local fx, _, fz = Spring.GetFeaturePosition(id)
	return fx ~= nil and math.abs(fx - x) < 4 and math.abs(fz - z) < 4
end

-- A feature of these already on the map near (x, z) that no structure has taken: taken for s, in a role.
local function TakeNear(ids, x, z, s, role)
	for _, id in ipairs(ids or {}) do
		if not partOf[id] and Near(id, x, z) then
			partOf[id] = { s = s, role = role }
			return id
		end
	end
end

-- After a reload (game already running): take back the parts that are there instead of making new ones.
local function Rediscover(s, byName)
	local k = s.kind
	local parts = Parts(s)
	local found = {}
	for i, part in ipairs(parts) do
		found[i] = TakeNear(byName[part.name], part.x, part.z, s, part.role)
	end
	for i = 1, 2 do
		if not found[i] then
			Create(s, parts[i])
		end
	end
	if found[3] then
		s.centreID = found[3]
		if Buildable(k) then
			TopStands(s, found[3])
			if s.roofY then -- the buildings already lifted onto it
				local x1, z1, x2, z2 = CentreRect(s)
				for _, unitID in ipairs(Spring.GetUnitsInRectangle(x1, z1, x2, z2) or {}) do
					local def = UnitDefs[Spring.GetUnitDefID(unitID)]
					local x, y, z = Spring.GetUnitPosition(unitID)
					if def and def.isImmobile and y and y > Spring.GetGroundHeight(x, z) + 1 then
						local h = math.max(def.xsize or 2, def.zsize or 2) * 4
						s.raised[unitID] = { x - h, z - h, x + h, z + h }
						raisedOn[unitID] = s
					end
				end
			end
		end
		for _, at in ipairs(s.markerAt) do
			local id = TakeNear(byName[k.marker or ""], at.x, at.z, s, "marker") or Create(s, { role = "marker", name = k.marker, x = at.x, z = at.z })
			s.greens[#s.greens + 1] = id
		end
		return
	end
	s.debrisID = TakeNear(byName[k.broken or ""], s.x, s.z, s, "debris")
	AddDebris(s)
	s.repairs = {}
	if k.marker then
		for _, at in ipairs(s.markerAt) do
			local id = TakeNear(byName[k.repair or ""], at.x, at.z, s, "repair") or Create(s, { role = "repair", name = k.repair, x = at.x, z = at.z })
			s.repairs[#s.repairs + 1] = id
		end
	else
		for i = 4, #parts do
			s.repairs[#s.repairs + 1] = found[i]
		end
	end
	local progress = 0
	for _, id in ipairs(Targets(s)) do
		local health, maxHealth = Spring.GetFeatureHealth(id)
		if health then
			progress = math.max(progress, health / maxHealth)
		end
	end
	s.progress = progress > 0.011 and progress or 0
	if #s.repairs == 0 and k.repair and not k.marker then
		AddRepairMarkers(s)
	end
	ShowProgress(s)
end

-- After a reload: a pump (or a shield platform) standing, its owner the team its feature was made for, or derelict.
local function PumpRediscover(s, byName)
	local k = s.kind
	local id = TakeNear(byName[k.centre], s.x, s.z, s, "centre")
	if id then
		s.centreID = id
		s.owner = Spring.GetFeatureTeam(id)
		if k.type == "shield_pad" then
			PadStands(id)
			return
		end
		local bx, bz = ToMap(s, 0, PIVOT_ALONG)
		s.beamID = TakeNear(byName[k.beam], bx, bz, s, "beam")
		if s.beamID then
			Spring.SetFeatureMoveCtrl(s.beamID, true, 0, 0, 0, 0, 0, 0)
		else
			PlaceBeam(s, s.owner)
		end
		return
	end
	s.debrisID = TakeNear(byName[k.broken or ""], s.x, s.z, s, "debris")
	AddDebris(s)
	s.progress = 0
	if s.debrisID then
		local health, maxHealth = Spring.GetFeatureHealth(s.debrisID)
		if health and health / maxHealth > 0.011 then
			s.progress = health / maxHealth
		end
	end
end

-- Guardian towers ----------------------------------------------------------------
-- A guardian (type = "guardian") is an indestructible tower guarding a path anywhere on the map: its instance's
-- zone gives the path's points (x, z, ...), how far from it the tower reaches (distance, measured flat) and whether
-- it closes back on itself. The zone has three regions: the band (within the distance of the path), inside a closed
-- path (beyond the band), and outside (the rest of the map, beyond the band: other towers' zones too). The band wins
-- where they meet; an open path has no inside. The instance's rules say, for each region, which unit types it
-- destroys (targets: unit names, and each name's Scavengers variant, name_scav), of which teams (teams: BAR's lobby
-- teams, ally team + 1; every team when absent) and after how long there (delay, seconds; leaving starts the count
-- again). A tower placed before towers had rules destroys its kind's targets crossing its path.
-- A unit doomed by a rule is worn down from then on, wherever it goes: it loses its share of its maximum health
-- every GUARD_FRAMES frames and is destroyed (DestroyUnit) when none is left, so every unit takes killSeconds,
-- whatever it is and however many there are. Its health is set directly, so no armour, shield or damage modifier
-- changes it, and healing doesn't help: each tick goes on from what the last one left. With no delay, the band is
-- checked along the way each unit came since the last check, so a fast one can't slip across a narrow zone between
-- checks. (Recoil runs every simulation frame, catching up rather than skipping when a game lags, so no check is
-- missed.) Units still being built are left alone, and a unit carried by a transport is judged by the transport.
-- While a tower is wearing anything down, it fires a rocket every rocketSeconds from the pod facing it, straight up:
-- a harmless weapon of the map's that bursts high above the tower (the projectile is deleted where it is after
-- ROCKET_FLIGHT frames and a burst shown there).
local GUARD_FRAMES = 6
local ROCKET_FLIGHT = 45              -- frames from launch to burst
local POD_UP, POD_OUT = 124, 17       -- where a rocket leaves: above the tower's ground, out from its middle
local BURST_UP = 560                  -- how high above the tower's ground its rockets burst
local BURST_EFFECT = "barmapeditor_guardian_burst"
local HIT_EFFECT = "barmapeditor_guardian_hit"
local MAX_HITS = 16                   -- hit flashes a tick, at most, however many units are struck
local hitsShown = 0
local guardians = {}  -- every guardian tower
local rockets = {}    -- rockets in flight: { id, frame (to burst), x, y, z (where, if it is gone) }
local doomed = {}     -- by unit: { fraction (its share a tick), left (its health after the last tick), tower (that doomed it) }
local seen = {}       -- by unit: where each unit near a band was at the last check, { x, z }
local unknownNames = {} -- unit names a rule lists that this game doesn't have, reported once

-- A kind's share of health a tick and its rockets' interval in frames.
local function GuardianSetup(k)
	k.tickFraction = GUARD_FRAMES / (30 * math.max(0.1, k.killSeconds or 1))
	k.rocketFrames = math.max(15, math.floor(30 * (k.rocketSeconds or 2) + 0.5))
	k.ready = true
end

-- A region's rule as the tower uses it: its targets as unit def IDs, the ally teams it acts on (nil: every one), its
-- delay in frames, and the fastest target's speed (elmos a second, as UnitDefs has it).
local function RuleSetup(rule, where)
	local r = { defs = {}, delayFrames = math.floor(30 * ((rule and rule.delay) or 0) + 0.5), fastest = 0 }
	for _, name in ipairs((rule and rule.targets) or {}) do
		local found = false
		for _, n in ipairs({ name, name .. "_scav" }) do
			local def = UnitDefNames[n]
			if def then
				r.defs[def.id] = true
				r.fastest = math.max(r.fastest, def.speed or 0)
				found = true
			end
		end
		if not found and not unknownNames[name] then
			unknownNames[name] = true
			Spring.Echo("map_structures: the guardian " .. where .. " lists " .. name .. ", which this game doesn't have")
		end
	end
	if rule and type(rule.teams) == "table" then
		r.allies = {}
		for _, team in ipairs(rule.teams) do
			r.allies[team - 1] = true
		end
	end
	r.acts = next(r.defs) ~= nil and (r.allies == nil or next(r.allies) ~= nil)
	return r
end

-- The runs of a guardian's path, { x1, z1, x2, z2 } each.
local function ZoneSegments(zone)
	local p, segments = zone and zone.points or {}, {}
	local n = math.floor(#p / 2)
	for i = 1, n - 1 do
		segments[#segments + 1] = { p[2 * i - 1], p[2 * i], p[2 * i + 1], p[2 * i + 2] }
	end
	if zone and zone.closed and n > 2 then
		segments[#segments + 1] = { p[2 * n - 1], p[2 * n], p[1], p[2] }
	end
	return segments
end

-- The distance from (x, z) to the run from (x1, z1) to (x2, z2), squared.
local function PointRunSq(x, z, x1, z1, x2, z2)
	local dx, dz = x2 - x1, z2 - z1
	local length2 = dx * dx + dz * dz
	local t = 0
	if length2 > 0 then
		t = math.max(0, math.min(1, ((x - x1) * dx + (z - z1) * dz) / length2))
	end
	local qx, qz = x - x1 - t * dx, z - z1 - t * dz
	return qx * qx + qz * qz
end

local function Side(ax, az, bx, bz, cx, cz)
	return (bx - ax) * (cz - az) - (bz - az) * (cx - ax)
end

-- The closest a unit came to a run of the path (s), squared, going straight from (px, pz) to (x, z).
local function PassDistanceSq(px, pz, x, z, s)
	if Side(px, pz, x, z, s[1], s[2]) * Side(px, pz, x, z, s[3], s[4]) < 0
		and Side(s[1], s[2], s[3], s[4], px, pz) * Side(s[1], s[2], s[3], s[4], x, z) < 0 then
		return 0 -- it crossed the path
	end
	return math.min(PointRunSq(x, z, s[1], s[2], s[3], s[4]), PointRunSq(px, pz, s[1], s[2], s[3], s[4]),
		PointRunSq(s[1], s[2], px, pz, x, z), PointRunSq(s[3], s[4], px, pz, x, z))
end

-- Whether (x, z) is within a tower's distance of its path.
local function InBand(g, x, z)
	local d2 = g.distance * g.distance
	for _, seg in ipairs(g.segments) do
		if PointRunSq(x, z, seg[1], seg[2], seg[3], seg[4]) <= d2 then
			return true
		end
	end
	return false
end

-- Whether (x, z) is inside a tower's closed path, by the even-odd rule (as the editor decides it).
local function Encloses(g, x, z)
	if not g.closed then
		return false
	end
	local p, n = g.points, math.floor(#g.points / 2)
	local inside, j = false, n
	for i = 1, n do
		local xi, zi, xj, zj = p[2 * i - 1], p[2 * i], p[2 * j - 1], p[2 * j]
		if (zi > z) ~= (zj > z) and x < (xj - xi) * (z - zi) / (zj - zi) + xi then
			inside = not inside
		end
		j = i
	end
	return inside
end

-- Whether a unit is one a rule acts on: a listed type, of a team it covers, finished and not carried.
local function Eligible(unitID, r)
	if not r.defs[Spring.GetUnitDefID(unitID)] then
		return false
	end
	if r.allies and not r.allies[Spring.GetUnitAllyTeam(unitID)] then
		return false
	end
	return not Spring.GetUnitIsBeingBuilt(unitID) and not Spring.GetUnitTransporter(unitID)
end

-- A unit doomed by a tower (the quicker kill time where two towers doom it).
local function Doom(unitID, g)
	local doom = doomed[unitID]
	local fraction = g.kind.tickFraction
	if not doom then
		doomed[unitID] = { fraction = fraction, tower = g }
	elseif doom.fraction < fraction then
		doom.fraction = fraction
	end
end

-- A unit found in a region this tick: how long it has been there without leaving (from the last tick's count), doomed
-- once that reaches the rule's delay. Counts are kept by unit, in the tower, for this tick and the last.
local function Present(g, region, r, unitID, now)
	if now[unitID] then
		return
	end
	local before = g.present[region][unitID]
	local frames = before and before + GUARD_FRAMES or 0
	now[unitID] = frames
	if frames >= r.delayFrames then
		Doom(unitID, g)
	end
end

-- Wears a doomed unit down by its share; destroyed when less than a quarter of a share is left (so rounding, and the
-- engine keeping health as a float, can't add a tick). Healing doesn't count: each tick goes on from what the last
-- one left, whatever it has healed since.
local function Strike(unitID, d)
	local health, maxHealth = Spring.GetUnitHealth(unitID)
	if not health then
		doomed[unitID] = nil
		return
	end
	if hitsShown < MAX_HITS then
		local x, y, z = Spring.GetUnitPosition(unitID)
		if x then
			Spring.SpawnCEG(HIT_EFFECT, x, y, z)
			hitsShown = hitsShown + 1
		end
	end
	local share = maxHealth * d.fraction
	local left = math.min(health, d.left or health) - share
	if left <= share * 0.25 then
		doomed[unitID] = nil
		Spring.DestroyUnit(unitID, false, false)
	else
		Spring.SetUnitHealth(unitID, left)
		d.left = left
	end
end

-- A rocket from the pod facing (fx, fz), straight up, to burst high above the tower.
local function FireRocket(g, n, fx, fz)
	local def = WeaponDefNames[g.kind.rocket or ""]
	if not def then
		return
	end
	local dx, dz = fx - g.x, fz - g.z
	local ox, oz = 0, 0
	if math.abs(dx) > math.abs(dz) then
		ox = dx > 0 and POD_OUT or -POD_OUT
	else
		oz = dz > 0 and POD_OUT or -POD_OUT
	end
	local gy = Spring.GetGroundHeight(g.x, g.z)
	local sx, sy, sz = g.x + ox, gy + POD_UP, g.z + oz
	local bx, by, bz = g.x + ox * 3, gy + BURST_UP, g.z + oz * 3
	local id = Spring.SpawnProjectile(def.id, {
		pos = { sx, sy, sz },
		["end"] = { bx, by, bz },
		speed = { (bx - sx) / ROCKET_FLIGHT, (by - sy) / ROCKET_FLIGHT, (bz - sz) / ROCKET_FLIGHT },
		ttl = ROCKET_FLIGHT * 2,
		team = Spring.GetGaiaTeamID(),
	})
	if id then
		rockets[#rockets + 1] = { id = id, frame = n + ROCKET_FLIGHT, x = bx, y = by, z = bz }
	end
end

-- Rockets whose time is up burst where they are (or where they were going, if something stopped them).
local function RocketsFrame(n)
	for i = #rockets, 1, -1 do
		local r = rockets[i]
		if n >= r.frame then
			local x, y, z = Spring.GetProjectilePosition(r.id)
			if x then
				Spring.DeleteProjectile(r.id)
			end
			Spring.SpawnCEG(BURST_EFFECT, x or r.x, y or r.y, z or r.z)
			table.remove(rockets, i)
		end
	end
end

-- One tower's check: who is in each of its regions, and who that dooms.
local function GuardTower(g, seenNow, AllUnits)
	local band, inside, outside = g.rules.band, g.rules.inside, g.rules.outside
	local now = { band = {}, inside = {}, outside = {} }
	-- The band: units near each run. With no delay, checked along the way they came since the last check (from where
	-- they were seen then; one not seen then was too far away to have reached it).
	if band.acts then
		local d2 = g.distance * g.distance
		local reach = g.distance + g.margin
		for _, seg in ipairs(g.segments) do
			local x1, x2 = math.min(seg[1], seg[3]) - reach, math.max(seg[1], seg[3]) + reach
			local z1, z2 = math.min(seg[2], seg[4]) - reach, math.max(seg[2], seg[4]) + reach
			for _, unitID in ipairs(Spring.GetUnitsInRectangle(x1, z1, x2, z2) or {}) do
				if not doomed[unitID] and Eligible(unitID, band) then
					local x, _, z = Spring.GetUnitPosition(unitID)
					if x then
						seenNow[unitID] = seenNow[unitID] or { x, z }
						if band.delayFrames == 0 then
							local was = seen[unitID] or seenNow[unitID]
							if PassDistanceSq(was[1], was[2], x, z, seg) <= d2 then
								Doom(unitID, g)
							end
						elseif PointRunSq(x, z, seg[1], seg[2], seg[3], seg[4]) <= d2 then
							Present(g, "band", band, unitID, now.band)
						end
					end
				end
			end
		end
	end
	-- Inside a closed path, beyond the band.
	if inside.acts and g.closed then
		for _, unitID in ipairs(Spring.GetUnitsInRectangle(g.box[1], g.box[2], g.box[3], g.box[4]) or {}) do
			if not doomed[unitID] and Eligible(unitID, inside) then
				local x, _, z = Spring.GetUnitPosition(unitID)
				if x and Encloses(g, x, z) and not InBand(g, x, z) then
					Present(g, "inside", inside, unitID, now.inside)
				end
			end
		end
	end
	-- Outside: the rest of the map, beyond the band (anything off the zone's box is outside at once).
	if outside.acts then
		local box = g.box
		for _, unitID in ipairs(AllUnits()) do
			if not doomed[unitID] and Eligible(unitID, outside) then
				local x, _, z = Spring.GetUnitPosition(unitID)
				if x and (x < box[1] or z < box[2] or x > box[3] or z > box[4] or not InBand(g, x, z) and not Encloses(g, x, z)) then
					Present(g, "outside", outside, unitID, now.outside)
				end
			end
		end
	end
	g.present = now
end

local function GuardFrame(n)
	RocketsFrame(n)
	if n % GUARD_FRAMES ~= 0 then
		return
	end
	local seenNow = {}
	local allUnits
	local function AllUnits()
		allUnits = allUnits or Spring.GetAllUnits() or {}
		return allUnits
	end
	for _, g in ipairs(guardians) do
		if g.centreID and #g.segments > 0 then
			GuardTower(g, seenNow, AllUnits)
		end
	end
	seen = seenNow
	-- Every doomed unit, wherever it is now, is worn down (once a tick, however many regions doomed it); each tower
	-- wearing anything down fires at one of them.
	hitsShown = 0
	local aim = {}
	for unitID, d in pairs(doomed) do
		if d.tower and not aim[d.tower] then
			aim[d.tower] = unitID
		end
		Strike(unitID, d)
	end
	for _, g in ipairs(guardians) do
		local unitID = aim[g]
		if unitID and n >= (g.nextRocket or 0) then
			local x, _, z = Spring.GetUnitPosition(unitID)
			FireRocket(g, n, x or g.x, z or g.z + 1)
			g.nextRocket = n + g.kind.rocketFrames
		end
	end
end

-- A guardian tower: placed, or taken back after a reload. A tower placed before towers had rules destroys its kind's
-- targets crossing its path.
local function GuardianPlaced(k, p, reload, byName)
	if not k.ready then
		GuardianSetup(k)
	end
	local zone = p.zone or {}
	local g = {
		kind = k, x = p.x, z = p.z, facing = (p.facing or 0) % 4, distance = zone.distance or 160, segments = ZoneSegments(zone),
		points = zone.points or {}, present = { band = {}, inside = {}, outside = {} },
	}
	g.closed = zone.closed and #g.points >= 6 or false
	local minX, minZ, maxX, maxZ = math.huge, math.huge, -math.huge, -math.huge
	for i = 1, #g.points - 1, 2 do
		minX, maxX = math.min(minX, g.points[i]), math.max(maxX, g.points[i])
		minZ, maxZ = math.min(minZ, g.points[i + 1]), math.max(maxZ, g.points[i + 1])
	end
	g.box = { minX - g.distance, minZ - g.distance, maxX + g.distance, maxZ + g.distance }
	local rules = p.rules or { band = { targets = k.targets } }
	local where = k.centre .. " at (" .. g.x .. ", " .. g.z .. ")"
	g.rules = { band = RuleSetup(rules.band, where), inside = RuleSetup(rules.inside, where), outside = RuleSetup(rules.outside, where) }
	-- How far beyond the band to look for units that might cross it before the next check: twice the fastest target's
	-- run between checks (UnitDefs' speed is elmos a second), at least 256.
	g.margin = math.max(256, 2 * GUARD_FRAMES * g.rules.band.fastest / 30)
	guardians[#guardians + 1] = g
	local id = reload and TakeNear(byName[k.centre], g.x, g.z, g, "guardian") or nil
	if not id then
		id = Spring.CreateFeature(k.centre, g.x, Spring.GetGroundHeight(g.x, g.z), g.z, HEADINGS[g.facing])
		if id then
			partOf[id] = { s = g, role = "guardian" }
		end
	end
	g.centreID = id
end

-- Shield platforms ----------------------------------------------------------------
-- A shield platform (type = "shield_pad") is a low pad units drive over (it doesn't block). Like an oil pump it starts
-- derelict, and the team that repairs it owns it (it wears that team's colour); destroyed, it is derelict again. A
-- unit of its owner's or an ally's that drives off a standing platform is given a bubble shield: shieldPercent of its
-- maximum health, for which its team pays energyPerMetal energy for each metal the unit costs. A unit with part of a
-- shield left is topped up, paying only for what it is missing; a team that can't pay gets nothing and is charged
-- nothing (and is told, at most every 30 s). Aircraft, buildings, nanoframes and carried units get none.
-- The bubble surrounds its unit, centred on its middle: its radius the larger of the unit's radius and 0.6 of its
-- height, times SHIELD_MARGIN and the kind's bubbleScale, so a tall unit's reaches the units round its feet. It takes
-- the damage of weapons and debris for its unit and for allies whose middles are within it (the fullest bubble a unit
-- is in, its own first), but not from a shot fired from inside it. The hit lands and the gadget puts back what the
-- bubble takes: UnitDamaged comes after the damage and before the engine checks whether the unit died (Unit.cpp
-- DoDamage), so a hit the bubble takes whole kills nothing. Fire, acid and lava, collisions, crushing and paralysis
-- aren't taken. When nothing is left the bubble is gone, until its unit drives off a platform again. The drawing side
-- below draws the bubbles (told of them by SendToUnsynced).
local PAD_FRAMES = 6
local SHIELD_MARGIN = 1.15         -- a bubble's room round its unit
local SHIELD_UP, SHIELD_HIT, SHIELD_DOWN = "barmapeditor_shield_up", "barmapeditor_shield_hit", "barmapeditor_shield_down"
local MAX_FLASHES = 16             -- shield hit flashes a frame, at most
local shields = {}                 -- by unit: { power, max, radius, sent (the frame the drawing side was last told) }
local onPad = {}                   -- by unit: the platform it stood on at the last check
local largest = 0                  -- the largest bubble's radius, how far to look for bubbles round a hit
local told = {}                    -- by team: when it was last told it couldn't pay
local held = {}                    -- by unit: a bubble's strength changed but not yet told to the drawing side
local flashes, flashFrame = 0, -1

-- A unit's middle (where its bubble is centred, and what a bubble must hold to cover it).
local function Mid(unitID)
	local _, _, _, x, y, z = Spring.GetUnitPosition(unitID, true)
	return x, y, z
end

-- Whether a unit can be given a shield: a finished unit that moves on the ground or water, not carried.
local function Shieldable(unitID)
	local def = UnitDefs[Spring.GetUnitDefID(unitID) or -1]
	return def ~= nil and not def.canFly and not def.isImmobile and not Spring.GetUnitIsBeingBuilt(unitID) and not Spring.GetUnitTransporter(unitID)
end

local function ShieldRadius(def, scale)
	return math.max(def.radius or 16, 0.6 * (def.height or 24)) * SHIELD_MARGIN * (scale or 1)
end

local function Tell(unitID, sh, n)
	sh.sent = n
	held[unitID] = nil
	SendToUnsynced("barmapeditor_shield", unitID, sh.radius, sh.power / sh.max)
end

-- Driven off a platform: a shield, or the rest of one, paid for by the unit's team.
local function GiveShield(unitID, s, n)
	local def = UnitDefs[Spring.GetUnitDefID(unitID) or -1]
	local _, maxHealth = Spring.GetUnitHealth(unitID)
	if not (def and maxHealth and maxHealth > 0) then
		return
	end
	local k = s.kind
	local full = maxHealth * (k.shieldPercent or 50) / 100
	local sh = shields[unitID]
	local missing = sh and math.max(0, 1 - sh.power / full) or 1
	if missing < 0.01 then
		return
	end
	local team = Spring.GetUnitTeam(unitID)
	local cost = (def.metalCost or 0) * (k.energyPerMetal or 0) * missing
	if cost > 0 and not Spring.UseTeamResource(team, { energy = cost }) then
		if not told[team] or n - told[team] >= 900 then
			told[team] = n
			Spring.SendMessageToTeam(team, "Shield platform: not enough energy for a shield (" .. math.ceil(cost) .. " needed).")
		end
		return
	end
	local radius = ShieldRadius(def, k.bubbleScale)
	shields[unitID] = { power = full, max = full, radius = radius }
	largest = math.max(largest, radius)
	Tell(unitID, shields[unitID], n)
	local x, y, z = Mid(unitID)
	if x then
		Spring.SpawnCEG(SHIELD_UP, x, y, z)
	end
end

-- A bubble gone: the drawing side told, a burst where it was, and how far to look for bubbles worked out again.
local function ShieldLost(unitID, burst)
	local sh = shields[unitID]
	if not sh then
		return
	end
	shields[unitID] = nil
	held[unitID] = nil
	SendToUnsynced("barmapeditor_shield", unitID, sh.radius, 0)
	if burst then
		local x, y, z = Mid(unitID)
		if x then
			Spring.SpawnCEG(SHIELD_DOWN, x, y, z)
		end
	end
	if sh.radius >= largest then
		largest = 0
		for _, other in pairs(shields) do
			largest = math.max(largest, other.radius)
		end
	end
end

-- Whether a bubble round a unit holds a point, squared distances against its radius.
local function Holds(ownerID, sh, x, y, z)
	local ox, oy, oz = Mid(ownerID)
	if not ox then
		return false
	end
	local dx, dy, dz = x - ox, y - oy, z - oz
	return dx * dx + dy * dy + dz * dz <= sh.radius * sh.radius
end

-- The bubble that takes a hit on a unit: its own, else the fullest of its allies' it is within; never one the shot
-- came from inside.
local function Bubble(unitID, attackerID)
	local x, y, z = Mid(unitID)
	if not x then
		return nil
	end
	local ax, ay, az
	if attackerID and attackerID >= 0 then
		ax, ay, az = Mid(attackerID)
	end
	local function Takes(ownerID, sh)
		return Holds(ownerID, sh, x, y, z) and not (ax and Holds(ownerID, sh, ax, ay, az))
	end
	local own = shields[unitID]
	if own and Takes(unitID, own) then
		return unitID
	end
	local team = Spring.GetUnitTeam(unitID)
	local best, bestPower
	-- Bubbles are centred on their units' middles; looking twice as far finds every one whose unit's base is further off.
	for _, ownerID in ipairs(Spring.GetUnitsInSphere(x, y, z, largest * 2) or {}) do
		local sh = shields[ownerID]
		if sh and ownerID ~= unitID and (not bestPower or sh.power > bestPower) and Spring.AreTeamsAllied(Spring.GetUnitTeam(ownerID), team)
			and Takes(ownerID, sh) then
			best, bestPower = ownerID, sh.power
		end
	end
	return best
end

-- A flash on the bubble where the hit came in (toward whoever fired, else toward the unit hit); a few a frame at most.
local function Flash(ownerID, sh, unitID, attackerID)
	local n = Spring.GetGameFrame()
	if n ~= flashFrame then
		flashFrame, flashes = n, 0
	end
	if flashes >= MAX_FLASHES then
		return
	end
	local ox, oy, oz = Mid(ownerID)
	local tx, ty, tz
	if attackerID and attackerID >= 0 then
		tx, ty, tz = Mid(attackerID)
	end
	if not tx then
		tx, ty, tz = Mid(unitID)
	end
	if not (ox and tx) then
		return
	end
	local dx, dy, dz = tx - ox, ty - oy, tz - oz
	local d = math.sqrt(dx * dx + dy * dy + dz * dz)
	if d > 0.001 then
		dx, dy, dz = dx / d, dy / d, dz / d
	else
		dx, dy, dz = 0, 1, 0
	end
	flashes = flashes + 1
	Spring.SpawnCEG(SHIELD_HIT, ox + dx * sh.radius, oy + dy * sh.radius, oz + dz * sh.radius)
end

-- Every few frames: units of an owner's or ally's on a standing platform are noted; those that were on one at the
-- last check and aren't now have driven off it, and are shielded.
local function PadFrame(n)
	if n % PAD_FRAMES ~= 0 or #pads == 0 then
		return
	end
	-- Strengths held back since the last check (the drawing side is told at most once a check), told now.
	for unitID in pairs(held) do
		if shields[unitID] then
			Tell(unitID, shields[unitID], n)
		end
	end
	held = {}
	local now = {}
	for _, s in ipairs(pads) do
		if s.owner and s.centreID then
			local x1, z1, x2, z2 = CentreRect(s)
			for _, unitID in ipairs(Spring.GetUnitsInRectangle(x1, z1, x2, z2) or {}) do
				if not now[unitID] and Shieldable(unitID) and Spring.AreTeamsAllied(Spring.GetUnitTeam(unitID), s.owner) then
					now[unitID] = s
				end
			end
		end
	end
	for unitID, s in pairs(onPad) do
		if not now[unitID] and s.owner and s.centreID and Spring.GetUnitDefID(unitID) and Shieldable(unitID) then
			GiveShield(unitID, s, n)
		end
	end
	onPad = now
end

-- The markers placed for a structure, from its instance's list of x, z.
local function MarkerPositions(list)
	local at = {}
	for i = 1, #(list or {}) - 1, 2 do
		at[#at + 1] = { x = list[i], z = list[i + 1] }
	end
	return at
end

function gadget:Initialize()
	local reload = Spring.GetGameFrame() > 0
	-- A map whose type map used every type: the one for tunnel tops is freed first, its squares given a type that
	-- behaves the same (once: after a reload they are already moved, and squares of it are tunnel tops).
	local remap = config.remap
	if remap and remap.into and remap.squares and not reload then
		local squares = remap.squares
		for i = 1, #squares - 1, 2 do
			Spring.SetMapSquareTerrainType(squares[i] * 16, squares[i + 1] * 16, remap.into)
		end
	end
	if blockedType then
		Spring.SetTerrainTypeData(blockedType, 0, 0, 0, 0)
	end
	local all = Spring.GetAllFeatures() or {}
	local byName = {}
	for _, id in ipairs(all) do
		local def = FeatureDefs[Spring.GetFeatureDefID(id)]
		if def then
			local list = byName[def.name] or {}
			list[#list + 1] = id
			byName[def.name] = list
		end
	end
	for _, p in ipairs(placed) do
		local k = kinds[p.kind]
		if k and k.type == "guardian" then
			GuardianPlaced(k, p, reload, byName)
		elseif k then
			local s = { kind = k, x = p.x, z = p.z, facing = (p.facing or 0) % 4, ground = p.ground or 0, repairs = {}, greens = {},
				markerAt = k.marker and MarkerPositions(p.markers) or {}, progress = 0, saved = {}, raised = {} }
			instances[#instances + 1] = s
			if Owned(k) then
				if k.type == "pump" then
					pumps[#pumps + 1] = s
					s.phase = (#pumps * 1.7) % (2 * math.pi) -- pumps nod out of step
				else
					pads[#pads + 1] = s
				end
				if reload then
					PumpRediscover(s, byName)
				else
					StartBroken(s)
				end
			elseif reload then
				Rediscover(s, byName)
			else
				local parts = Parts(s)
				Create(s, parts[1])
				Create(s, parts[2])
				if p.broken and (k.repair or k.broken) then
					StartBroken(s)
				else
					PutCentreBack(s)
					if k.marker then
						PlaceMarkers(s, false)
					end
				end
			end
		end
	end
	for _, id in ipairs(all) do
		self:FeatureCreated(id)
	end
end

-- Features that can be built on (buildOn in the config): off the blocking map; impassable ones keep units off.
function gadget:FeatureCreated(featureID, allyTeamID)
	if partOf[featureID] or builtOn[featureID] then
		return
	end
	local def = FeatureDefs[Spring.GetFeatureDefID(featureID)]
	local rule = def and buildOn[def.name]
	if not rule then
		return
	end
	MakeBuildable(featureID)
	local x, _, z = Spring.GetFeaturePosition(featureID)
	local facing = math.floor(((Spring.GetFeatureHeading(featureID) or 0) + 8192) / 16384) % 4
	local x1, z1, x2, z2 = Rect(facing, x, z, def.xsize / 2, def.zsize / 2)
	local b = { saved = {}, impassable = rule.impassable ~= false, x1 = x1, z1 = z1, x2 = x2, z2 = z2 }
	if b.impassable then
		Impassable(x1, z1, x2, z2, b.saved, 0)
	end
	builtOn[featureID] = b
end

function gadget:FeatureDestroyed(featureID, allyTeamID)
	local b = builtOn[featureID]
	if b then
		builtOn[featureID] = nil
		Restore(b.saved)
		return
	end
	local part = partOf[featureID]
	if not part then
		return
	end
	partOf[featureID] = nil
	local s = part.s
	if part.role == "guardian" then
		s.centreID = nil -- gone (a guardian can't be destroyed in play; removed some other way): it guards no more
	elseif part.role == "centre" and s.centreID == featureID then
		Broken(s)
	elseif part.role == "beam" then
		s.beamID = nil
	elseif part.role == "debris" then
		s.debrisID = nil
		s.lostRepairs = true -- put back on the next check
	elseif part.role == "marker" then
		for i = #s.greens, 1, -1 do
			if s.greens[i] == featureID then
				table.remove(s.greens, i)
			end
		end
		s.lostMarkers = true
	elseif part.role == "repair" then
		for i = #s.repairs, 1, -1 do
			if s.repairs[i] == featureID then
				table.remove(s.repairs, i)
			end
		end
		s.lostRepairs = true -- put back on the next check
	end
end

-- Reclaiming a red marker or what is left of a structure repairs instead: the work goes into the structure's pool,
-- paid for as it goes. A green marker isn't reclaimed at all.
function gadget:AllowFeatureBuildStep(builderID, builderTeam, featureID, featureDefID, part)
	local p = partOf[featureID]
	if not p or p.role == "wall" or p.role == "centre" or p.role == "guardian" then
		return true
	end
	if p.role == "marker" or p.role == "beam" then
		return false
	end
	local s = p.s
	if part >= 0 or s.centreID then
		return false
	end
	s.lastTeam = builderTeam -- whoever finishes repairing a pump owns it
	if s.progress >= 1 then
		TryRebuild(s)
		return false
	end
	local step = math.min(-part, 1 - s.progress) -- the markers' reclaimtime is the whole repair's work
	local k = s.kind
	if not Spring.UseTeamResource(builderTeam, { metal = (k.metal or 0) * step, energy = (k.energy or 0) * step }) then
		return false -- the team can't pay: the repair waits
	end
	s.progress = s.progress + step
	ShowProgress(s)
	if s.progress >= 1 then
		TryRebuild(s)
	end
	return false -- the marker or wreck itself is never reclaimed
end

local function Overlaps(x, z, hx, hz, x1, z1, x2, z2)
	return x + hx > x1 and x - hx < x2 and z + hz > z1 and z - hz < z2
end

-- BAR's "Footprint clearance" gadget (luarules/gadgets/unit_footprint_clearance.lua) destroys every feature whose
-- position lies within a new building's or factory's xsize * 5 by zsize * 5 (geothermal vents apart), as the unit is
-- created. A tunnel's top, a part of any structure or a feature made to be built on would go with it. LuaRules gadgets
-- see a new unit before LuaGaia's (orders 100 and 300, EventHandler.cpp keeps clients in that order), so this one's
-- features step aside in AllowUnitCreation, the last say before the unit is made, and come back in UnitCreated, after
-- the clearance has looked; the next frame brings back any that are still aside (a unit refused after all).
local aside = {} -- featureID -> where it stood
local CLEARANCE = 5 -- BAR's: xsize * 5 either side

local function StepAside(x, z, def, facing)
	local rx, rz = (def.xsize or 2) * CLEARANCE + 16, (def.zsize or 2) * CLEARANCE + 16
	if facing == 1 or facing == 3 then
		rx, rz = rz, rx
	end
	local features = Spring.GetFeaturesInRectangle(x - rx, z - rz, x + rx, z + rz)
	for i = 1, #features do
		local id = features[i]
		if (partOf[id] or builtOn[id]) and not aside[id] then
			local fx, fy, fz = Spring.GetFeaturePosition(id)
			if fx then
				aside[id] = { fx, fy, fz }
				-- Along x, towards the middle of the map, clear of the rectangle.
				local dx = 2 * rx + 64
				if fx > Game.mapSizeX / 2 then
					dx = -dx
				end
				Spring.SetFeaturePosition(id, fx + dx, fy, fz)
			end
		end
	end
end

local function StepBack()
	for id, p in pairs(aside) do
		if Spring.ValidFeatureID(id) then
			Spring.SetFeaturePosition(id, p[1], p[2], p[3])
		end
		aside[id] = nil
	end
end

-- Building is kept out of gaps and open middles; on a standing tunnel top (or a feature that can be built on) only
-- buildings that aren't factories may go, since nothing can move there. A top with a roof to lift buildings onto
-- takes only buildings wholly on it, clear of those already there (lifted, they no longer block the ground).
function gadget:AllowUnitCreation(unitDefID, builderID, builderTeam, x, y, z, facing)
	if not x then
		return true, true
	end
	local def = UnitDefs[unitDefID]
	if not def then
		return true, true
	end
	local hx, hz = (def.xsize or 2) * 4, (def.zsize or 2) * 4
	if facing == 1 or facing == 3 then
		hx, hz = hz, hx
	end
	local building = def.isImmobile and not def.isFactory
	for _, s in ipairs(instances) do
		local x1, z1, x2, z2 = CentreRect(s)
		if Overlaps(x, z, hx, hz, x1, z1, x2, z2) then
			if not (Buildable(s.kind) and s.centreID and building) then
				return false, true
			end
			if s.roofY then
				if x - hx < x1 or x + hx > x2 or z - hz < z1 or z + hz > z2 then
					return false, true
				end
				for _, r in pairs(s.raised) do
					if Overlaps(x, z, hx, hz, r[1], r[2], r[3], r[4]) then
						return false, true
					end
				end
			end
		end
	end
	for _, b in pairs(builtOn) do
		if b.impassable and not building and Overlaps(x, z, hx, hz, b.x1, b.z1, b.x2, b.z2) then
			return false, true
		end
	end
	if def.isImmobile then
		StepAside(x, z, def, facing)
	end
	return true, true
end

-- A building begun on a tunnel's roof is lifted onto it, nanoframe and all, and stays there (MoveCtrl holds it).
function gadget:UnitCreated(unitID, unitDefID, unitTeam, builderID)
	StepBack()
	local def = UnitDefs[unitDefID]
	if not (def and def.isImmobile and not def.isFactory) then
		return
	end
	local x, _, z = Spring.GetUnitPosition(unitID)
	if not x then
		return
	end
	for _, s in ipairs(instances) do
		if s.centreID and s.roofY then
			local x1, z1, x2, z2 = CentreRect(s)
			if x > x1 and x < x2 and z > z1 and z < z2 then
				Spring.MoveCtrl.Enable(unitID)
				Spring.MoveCtrl.SetPosition(unitID, x, s.roofY, z)
				local h = math.max(def.xsize or 2, def.zsize or 2) * 4
				s.raised[unitID] = { x - h, z - h, x + h, z + h }
				raisedOn[unitID] = s
				return
			end
		end
	end
end

-- A shield takes what it can of a hit (see Shield platforms): the hit has landed; what the bubble takes is put back.
function gadget:UnitDamaged(unitID, unitDefID, unitTeam, damage, paralyzer, weaponDefID, projectileID, attackerID)
	if paralyzer or not damage or damage <= 0 or next(shields) == nil then
		return
	end
	if weaponDefID and weaponDefID < 0 and weaponDefID ~= -1 then
		return -- only weapons (0 and up) and debris (-1); not fire, acid, collisions, crushing
	end
	local ownerID = Bubble(unitID, attackerID)
	if not ownerID then
		return
	end
	local health = Spring.GetUnitHealth(unitID)
	if not health then
		return
	end
	local sh = shields[ownerID]
	local taken = math.min(damage, sh.power)
	sh.power = sh.power - taken
	Spring.SetUnitHealth(unitID, health + taken)
	Flash(ownerID, sh, unitID, attackerID)
	local n = Spring.GetGameFrame()
	if sh.power <= sh.max * 0.001 then
		ShieldLost(ownerID, true)
	elseif not sh.sent or n - sh.sent >= PAD_FRAMES then
		Tell(ownerID, sh, n)
	else
		held[ownerID] = true
	end
end

function gadget:UnitDestroyed(unitID, unitDefID, unitTeam)
	-- Its ID may be given to a new unit: nothing of it is passed on.
	doomed[unitID] = nil
	seen[unitID] = nil
	onPad[unitID] = nil
	ShieldLost(unitID, false)
	for _, g in ipairs(guardians) do
		g.present.band[unitID], g.present.inside[unitID], g.present.outside[unitID] = nil, nil, nil
	end
	local s = raisedOn[unitID]
	if s then
		raisedOn[unitID] = nil
		s.raised[unitID] = nil
	end
end

function gadget:GameFrame(n)
	StepBack()
	PumpFrame(n)
	GuardFrame(n)
	PadFrame(n)
	if n % CHECK_FRAMES ~= 0 then
		return
	end
	for _, s in ipairs(instances) do
		if s.held then
			TryRebuild(s)
		end
		if s.lostRepairs and not s.centreID then
			s.lostRepairs = false
			AddDebris(s)
			AddRepairMarkers(s)
		end
		if s.lostMarkers and s.centreID then
			s.lostMarkers = false
			PlaceMarkers(s, false)
		end
	end
end
""";
}
