using System.Globalization;
using System.Numerics;
using System.Text;
using MapCore.Items;
using MapCore.Lua;
using MapCore.Smf;
using MapCore.Structures;

namespace MapCore.Editing;

/// <summary>What a door or tunnel kind is made of: its look (models the editor builds, or ones from assets), its size, and its gameplay numbers.</summary>
/// <param name="Wall">For <see cref="StructureLook.Assets"/>: the feature whose model the walls use (from the map or the asset library); likewise the others.</param>
/// <param name="Gap">Footprint squares (16 game units) across the opening; <paramref name="WallWidth"/> each wall; <paramref name="Depth"/> along the passage.</param>
/// <param name="Health">The centre's hit points (door, tunnel top).</param>
/// <param name="Work">A full repair's work: one builder takes Work ÷ its build power seconds; several share it.</param>
/// <param name="Metal">What a full repair costs, paid as the work goes.</param>
/// <param name="OpenWhenBroken">Whether the gap can be crossed while the structure is broken (a tunnel's ground goes back to its own type).</param>
/// <param name="Height">How tall built models are (game units); a tunnel's flat roof is that high.</param>
/// <param name="Roof">A tunnel's roof: flat (built on), or a jagged rock mass.</param>
/// <param name="MetalIncome">An oil pump's: metal a second it gives the team that repaired it; likewise <paramref name="EnergyIncome"/>.</param>
/// <param name="KillSeconds">A guardian tower's: how long a unit lasts once it has come into the zone (wherever it goes then), whatever the unit.</param>
/// <param name="RocketSeconds">A guardian tower's: how often it fires a rocket (for show) while anything it destroys is in its zone.</param>
/// <param name="ShieldPercent">A shield platform's: a shield's strength, as a percentage of the unit's maximum health.</param>
/// <param name="EnergyPerMetal">A shield platform's: a shield's price, energy for each metal the unit costs.</param>
/// <param name="BubbleScale">A shield platform's: a bubble's size, times the size it takes from its unit.</param>
public sealed record StructureKindSettings(string? Wall, string? Centre, string? Repair, int Gap = 4, int WallWidth = 2, int Depth = 2,
    double Health = 20_000, double Work = 36_000, double Metal = 1200, double Energy = 3000, bool OpenWhenBroken = true,
    StructureLook Look = StructureLook.Assets, double Height = 0, StructureRoof Roof = StructureRoof.Flat, double MetalIncome = 0, double EnergyIncome = 0,
    double KillSeconds = 0, double RocketSeconds = 0, double ShieldPercent = 0, double EnergyPerMetal = 0, double BubbleScale = 0)
{
    /// <summary>
    /// A shield platform's starting settings: a 6 by 6 square pad (a titan's footprint fits on it) with 6,000 health;
    /// repaired with 12,000 work (40 s for a commander alone) for 300 metal and 1,500 energy. A shield is half the
    /// unit's health and costs 10 energy for each metal the unit costs (a unit costing 100 metal pays 1,000 energy):
    /// a starting point, to be tuned in play.
    /// </summary>
    public static StructureKindSettings ShieldPad() =>
        new(null, null, null, Gap: 6, WallWidth: 1, Depth: 6, Health: 6000, Work: 12_000, Metal: 300, Energy: 1500, Look: StructureLook.Metal,
            Height: StructureModels.ShieldPadHeight, ShieldPercent: 50, EnergyPerMetal: 10, BubbleScale: 1);

    /// <summary>
    /// A guardian tower's starting settings: ancient, 3 by 3 squares (a commander's footprint), indestructible; a unit
    /// that comes into its zone lasts a second, and it fires a rocket every 2 seconds while anything is in it.
    /// </summary>
    public static StructureKindSettings Guardian() =>
        new(null, null, null, Gap: 3, WallWidth: 1, Depth: 3, Health: 1e9, Work: 1, Metal: 0, Energy: 0, Look: StructureLook.Ancient,
            Height: StructureModels.GuardianHeight, KillSeconds: 1, RocketSeconds: 2);

    /// <summary>
    /// An oil pump's starting settings: 4,000 health; repaired with 9,000 work (30 s for a commander alone) for 150
    /// metal and 600 energy; then 2 metal and 25 energy a second to whoever repaired it (about a T1 metal extractor's
    /// and a solar collector's worth). Its footprint is its model's, 4 squares by 6.
    /// </summary>
    public static StructureKindSettings Pump() =>
        new(null, null, null, Gap: 4, WallWidth: 1, Depth: 6, Health: 4000, Work: 9000, Metal: 150, Energy: 600, Look: StructureLook.Metal, Height: 56,
            MetalIncome: 2, EnergyIncome: 25);

    /// <summary>
    /// Built models' starting settings: taller and thicker than a commander. BAR's commanders (armcom, corcom,
    /// 2026-10-15) are a cylinder 28 across and 52 tall on a 3-square footprint (48 game units); walls and the door
    /// are 3 squares thick and 80 tall, and the gap is 4 squares (64) so a commander walks through it. An open tunnel
    /// starts with a rock roof, a tunnel with a flat one.
    /// </summary>
    public static StructureKindSettings Built(StructureLook look, StructureType type) =>
        new(null, null, null, Gap: 4, WallWidth: 3, Depth: 3, Look: look, Height: 80, Roof: type == StructureType.OpenTunnel ? StructureRoof.Cliff : StructureRoof.Flat);

    public string Encode()
    {
        static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        var parts = new List<string>();
        if (Look == StructureLook.Assets)
        {
            parts.AddRange(["wall=" + Uri.EscapeDataString(Wall ?? ""), "centre=" + Uri.EscapeDataString(Centre ?? "")]);
            if (Repair != null)
                parts.Add("repair=" + Uri.EscapeDataString(Repair));
        }
        parts.AddRange(["gap=" + Gap, "wallwidth=" + WallWidth, "depth=" + Depth, "health=" + N(Health), "work=" + N(Work),
            "metal=" + N(Metal), "energy=" + N(Energy), "open=" + (OpenWhenBroken ? "1" : "0"), "look=" + StructureKind.LookName(Look)]);
        if (Look != StructureLook.Assets)
            parts.AddRange(["height=" + N(Height), "roof=" + (Roof == StructureRoof.Cliff ? "cliff" : "flat")]);
        if (MetalIncome != 0 || EnergyIncome != 0)
            parts.AddRange(["mps=" + N(MetalIncome), "eps=" + N(EnergyIncome)]);
        if (KillSeconds != 0 || RocketSeconds != 0)
            parts.AddRange(["kill=" + N(KillSeconds), "rocket=" + N(RocketSeconds)]);
        if (ShieldPercent != 0 || EnergyPerMetal != 0 || BubbleScale != 0)
            parts.AddRange(["shield=" + N(ShieldPercent), "epm=" + N(EnergyPerMetal), "bubble=" + N(BubbleScale)]);
        return string.Join(";", parts);
    }

    /// <summary>
    /// Settings from their encoding. Without look=, models named with wall= are assets (as every kind was before built
    /// models), and none named means concrete. What isn't given starts from that look's defaults.
    /// </summary>
    public static StructureKindSettings Decode(string text, StructureType type = StructureType.Door)
    {
        var v = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
                throw new InvalidOperationException($"'{pair}' is not key=value.");
            v[pair[..eq]] = Uri.UnescapeDataString(pair[(eq + 1)..]);
        }
        var unknown = v.Keys.Except(["wall", "centre", "repair", "gap", "wallwidth", "depth", "health", "work", "metal", "energy", "open", "look", "height", "roof", "mps", "eps",
            "kill", "rocket", "shield", "epm", "bubble"]).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException($"unknown structure setting(s) {string.Join(", ", unknown)}.");
        var look = v.TryGetValue("look", out var l) ? StructureKind.ParseLook(l)
            : type == StructureType.Guardian ? StructureLook.Ancient
            : type == StructureType.ShieldPad ? StructureLook.Metal
            : v.ContainsKey("wall") ? StructureLook.Assets : StructureLook.Concrete;
        double Num(string key, double fallback) => v.TryGetValue(key, out var t)
            ? double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : throw new InvalidOperationException($"{key}: '{t}' is not a number.")
            : fallback;
        var d = type == StructureType.Pump ? Pump() with { Look = look }
            : type == StructureType.Guardian ? Guardian() with { Look = look }
            : type == StructureType.ShieldPad ? ShieldPad() with { Look = look }
            : look == StructureLook.Assets ? new StructureKindSettings(null, null, null) : Built(look, type);
        return new StructureKindSettings(
            look == StructureLook.Assets ? v.GetValueOrDefault("wall") ?? throw new InvalidOperationException("a wall model is needed (wall=<feature>).") : null,
            look == StructureLook.Assets ? v.GetValueOrDefault("centre") ?? throw new InvalidOperationException("a centre model is needed (centre=<feature>).") : null,
            look == StructureLook.Assets ? v.GetValueOrDefault("repair") : null,
            (int)Num("gap", d.Gap), (int)Num("wallwidth", d.WallWidth), (int)Num("depth", d.Depth), Num("health", d.Health), Num("work", d.Work),
            Num("metal", d.Metal), Num("energy", d.Energy), v.GetValueOrDefault("open") != "0",
            look, look == StructureLook.Assets ? 0 : Num("height", d.Height), v.TryGetValue("roof", out var roof) ? StructureKind.ParseRoof(roof) : d.Roof,
            Num("mps", d.MetalIncome), Num("eps", d.EnergyIncome), Num("kill", d.KillSeconds), Num("rocket", d.RocketSeconds),
            Num("shield", d.ShieldPercent), Num("epm", d.EnergyPerMetal), Num("bubble", d.BubbleScale));
    }

    public string? Problem(StructureType type)
    {
        if (type == StructureType.Pump && Look == StructureLook.Assets)
            return "an oil pump is built by the editor: its look can't be models from assets";
        if (type == StructureType.Guardian && Look == StructureLook.Assets)
            return "a guardian tower is built by the editor: it is ancient, concrete or metal";
        if (type == StructureType.ShieldPad && Look == StructureLook.Assets)
            return "a shield platform is built by the editor: its look can't be models from assets";
        if (type == StructureType.ShieldPad && (double.IsNaN(ShieldPercent) || ShieldPercent is < 1 or > 500))
            return "a shield is 1% to 500% of its unit's health";
        if (type == StructureType.ShieldPad && (double.IsNaN(EnergyPerMetal) || EnergyPerMetal is < 0 or > 1000))
            return "a shield costs 0 to 1,000 energy for each metal its unit costs";
        if (type == StructureType.ShieldPad && (double.IsNaN(BubbleScale) || BubbleScale is < 0.25 or > 4))
            return "a bubble is 0.25 to 4 times the size it takes from its unit";
        if (type == StructureType.ShieldPad && (Gap is < 4 or > 32 || Depth is < 4 or > 32))
            return "a shield platform is 4 to 32 squares across and along";
        if (type != StructureType.Guardian && Look == StructureLook.Ancient)
            return "only a guardian tower can look ancient";
        if (type == StructureType.Guardian && (double.IsNaN(KillSeconds) || KillSeconds is < 0.2 or > 60))
            return "a guardian destroys a unit in 0.2 to 60 seconds";
        if (type == StructureType.Guardian && (double.IsNaN(RocketSeconds) || RocketSeconds is < 0.5 or > 60))
            return "a guardian fires a rocket every 0.5 to 60 seconds";
        if (double.IsNaN(MetalIncome) || double.IsNaN(EnergyIncome) || MetalIncome is < 0 or > 1000 || EnergyIncome is < 0 or > 10_000)
            return "an oil pump gives from 0 to 1,000 metal and 0 to 10,000 energy a second";
        if (Gap is < 1 or > 32 || WallWidth is < 1 or > 32 || Depth is < 1 or > 32)
            return "gap, wall width and depth must be from 1 to 32 squares";
        if (Look == StructureLook.Assets && (string.IsNullOrEmpty(Wall) || string.IsNullOrEmpty(Centre)))
            return "models from assets need a wall and a centre model";
        if (Look == StructureLook.Assets && type != StructureType.OpenTunnel && string.IsNullOrEmpty(Repair))
            return "a door or tunnel needs a repair marker model";
        if (Look != StructureLook.Assets && (double.IsNaN(Height) || Height < 16 || Height > 512))
            return "the height must be from 16 to 512 game units";
        if (double.IsNaN(Health) || Health < 1 || Health > 1e9)
            return "the centre's health must be from 1 to 1,000,000,000";
        if (double.IsNaN(Work) || Work < 1 || Work > 1e9)
            return "the repair work must be from 1 to 1,000,000,000";
        if (double.IsNaN(Metal) || double.IsNaN(Energy) || Metal is < 0 or > 1e7 || Energy is < 0 or > 1e7)
            return "the repair's metal and energy must be from 0 to 10,000,000";
        return null;
    }
}

/// <summary>
/// Doors and tunnels (see <see cref="StructureFiles.GadgetText"/>):
/// <c>structure-kind:&lt;name&gt;:&lt;door|tunnel|open_tunnel&gt;:&lt;settings&gt;</c> makes a kind (its parts' features
/// and, for built looks, their models and the shared textures, or models from the map or the library; the config
/// entry, the gadget, and LuaGaia's loader if the map has none); <c>structure-change:&lt;name&gt;:&lt;settings&gt;</c>
/// gives a kind new settings (every one placed changes with it); <c>structure-add:&lt;kind&gt;:&lt;x&gt;,&lt;z&gt;:&lt;facing 0-3&gt;</c>
/// places one (<c>:broken</c> after the facing: it starts the game destroyed), <c>structure-move:&lt;x&gt;,&lt;z&gt;:&lt;new x&gt;,&lt;new z&gt;</c>
/// and <c>structure-turn:&lt;x&gt;,&lt;z&gt;:&lt;facing 0-3&gt;</c> move and turn one; <c>structure-marker-add:&lt;x&gt;,&lt;z&gt;:&lt;marker x&gt;,&lt;marker z&gt;</c>
/// places a repair marker for it (a built kind's; always on the map, green, red while it is destroyed), and
/// <c>structure-marker-move</c> / <c>structure-marker-remove</c> move and remove the marker nearest a point (within 32);
/// <c>structure-broken:&lt;x&gt;,&lt;z&gt;:&lt;1|0&gt;</c>
/// sets whether the one nearest starts destroyed, and <c>structure-remove:&lt;x&gt;,&lt;z&gt;</c> takes it away (both within 64). Each is one undo step.
/// </summary>
public sealed partial class MapEditSession
{
    StructureConfig? structures;
    string? structuresStamp;

    /// <summary>
    /// The map's doors and tunnels, read from its config (none when it has no config), for views: read again only
    /// when the file changes. Don't change what it returns; operations read their own copy.
    /// </summary>
    public StructureConfig Structures
    {
        get
        {
            var file = new FileInfo(Path.Combine(Package.Root, StructureConfig.Entry.Replace('/', Path.DirectorySeparatorChar)));
            var stamp = file.Exists ? string.Create(CultureInfo.InvariantCulture, $"{file.Length}:{file.LastWriteTimeUtc.Ticks}") : "";
            if (structures == null || stamp != structuresStamp)
            {
                structures = file.Exists ? StructureConfig.Parse(File.ReadAllText(file.FullName)) : new StructureConfig();
                structuresStamp = stamp;
            }
            return structures;
        }
    }

    /// <summary>The structure whose walls or centre cover a point, if any.</summary>
    public StructureInstance? StructureAt(double x, double z)
    {
        var config = Structures;
        return config.Instances.FirstOrDefault(s => config.Kinds.TryGetValue(s.Kind, out var k) &&
            StructureLayout.Parts(k, s).Any(p => p.Role is "wall" or "centre" && x >= p.X1 && x < p.X2 && z >= p.Z1 && z < p.Z2));
    }

    /// <summary>
    /// A kind's settings as they were given. Kinds made before the editor kept them get theirs back from what they
    /// wrote: the models their parts were made from, the centre's health and the repair's work.
    /// </summary>
    public StructureKindSettings KindSettings(StructureKind kind)
    {
        if (kind.Settings != null)
        {
            try
            {
                return StructureKindSettings.Decode(kind.Settings, kind.Type);
            }
            catch (InvalidOperationException)
            {
                // rebuilt below
            }
        }
        var defs = FeatureDefs.Load(Package.Root).SelectMany(f => f.Features).ToList();
        LuaMap? Table(string? part) => part == null ? null : defs.FirstOrDefault(d => d.Name == part)?.Table;
        string? From(string? part) => (Table(part)?.GetIgnoreCase("customparams") as LuaMap)?.GetIgnoreCase("made_from") as string;
        double Number(string? part, string key, double fallback) => LuaValuesRuntime.ToNumber(Table(part)?.GetIgnoreCase(key)) ?? fallback;
        return new StructureKindSettings(From(kind.Wall), From(kind.Centre), From(kind.Repair), kind.Gap, kind.WallWidth, kind.Depth,
            Number(kind.Centre, "damage", 20_000), Number(kind.Repair, "reclaimtime", 36_000), kind.Metal, kind.Energy, kind.OpenWhenBroken);
    }

    public string AddStructureKind(string name, StructureType type, StructureKindSettings settings) =>
        Apply($"structure-kind:{name}:{StructureKind.TypeName(type)}:{settings.Encode()}");

    /// <summary>Makes a guardian tower kind that destroys units of these types (BAR unit names) in its zones.</summary>
    public string AddGuardianKind(string name, StructureKindSettings settings, IEnumerable<string> targets) =>
        Apply($"structure-kind:{name}:guardian:{settings.Encode()}:{TargetsText(targets)}");

    /// <summary>
    /// A guardian kind's targets: what a new tower of it destroys in its band when there's no tower of the kind to copy
    /// rules from. Towers already placed keep their own rules.
    /// </summary>
    public string SetGuardianTargets(string kind, IEnumerable<string> targets) => Apply($"structure-targets:{kind}:{TargetsText(targets)}");

    /// <summary>What the guardian tower at (x, z) does in one region of its zone: targets, teams and delay.</summary>
    public string SetGuardianRule(double x, double z, GuardianRegion region, GuardianRule rule) =>
        Apply(string.Create(CultureInfo.InvariantCulture, $"structure-rule:{x},{z}:{GuardianRules.RegionName(region)}:{rule.DelaySeconds}:{rule.TeamsText}:") +
              TargetsText(rule.Targets));

    /// <summary>A guardian tower's rules as they act: its own, or for one placed before towers had their own, its kind's targets in the band.</summary>
    public static GuardianRules RulesOf(StructureInstance tower, StructureKind kind) => tower.Rules ?? GuardianRules.Default(kind.TargetList);

    /// <summary>Teams from their text: all, none, or lobby team numbers 1 to 8 separated by commas.</summary>
    static IReadOnlyList<int>? ParseTeams(string text)
    {
        text = text.Trim();
        if (text == "all")
            return null;
        if (text == "none")
            return [];
        var teams = new List<int>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int team) || team is < 1 or > GuardianRule.MaxTeam)
                throw new InvalidOperationException($"'{part}' isn't a team: teams are all, none, or BAR's lobby teams 1 to {GuardianRule.MaxTeam}.");
            if (!teams.Contains(team))
                teams.Add(team);
        }
        teams.Sort();
        return teams;
    }

    /// <summary>The path the guardian tower at (x, z) guards (null: none).</summary>
    public string SetGuardianZone(double x, double z, GuardianZone? zone) =>
        Apply(string.Create(CultureInfo.InvariantCulture, $"structure-zone:{x},{z}:") + (zone == null ? "none"
            : string.Create(CultureInfo.InvariantCulture, $"{zone.Distance}:{(zone.Closed ? 1 : 0)}:") +
              string.Join(";", zone.Points.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.X},{p.Z}")))));

    static string TargetsText(IEnumerable<string> targets)
    {
        var list = targets.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return list.Count == 0 ? "none" : string.Join(",", list);
    }

    /// <summary>A list of BAR unit names from its text ("none": empty).</summary>
    static List<string> ParseTargets(string text)
    {
        if (text.Trim() == "none")
            return [];
        var names = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        foreach (var n in names)
            if (!CustomFeature.NamePattern.IsMatch(n) || n.Length > 64)
                throw new InvalidOperationException($"'{n}' isn't a unit name (lower-case letters, digits and '_').");
        return names;
    }

    public string ChangeStructureKind(string name, StructureKindSettings settings) => Apply($"structure-change:{name}:{settings.Encode()}");

    public string PlaceStructure(string kind, double x, double z, int facing, bool broken = false) =>
        Apply(string.Create(CultureInfo.InvariantCulture, $"structure-add:{kind}:{x},{z}:{facing & 3}") + (broken ? ":broken" : ""));

    /// <summary>Moves the structure centred at (x, z) so its centre is at (newX, newZ), on the 16-unit grid.</summary>
    public string MoveStructure(double x, double z, double newX, double newZ) =>
        Apply(string.Create(CultureInfo.InvariantCulture, $"structure-move:{x},{z}:{newX},{newZ}"));

    /// <summary>
    /// Turns the structure centred at (x, z) to a facing: 0 south, 1 east, 2 north, 3 west, the way its passage runs.
    /// Quarter turns only, as the engine turns a feature's blocked footprint only by quarters.
    /// </summary>
    public string TurnStructure(double x, double z, int facing) =>
        Apply(string.Create(CultureInfo.InvariantCulture, $"structure-turn:{x},{z}:{facing & 3}"));

    /// <summary>Places a repair marker at (mx, mz) for the structure centred at (x, z).</summary>
    public string AddStructureMarker(double x, double z, double mx, double mz) =>
        Apply(string.Create(CultureInfo.InvariantCulture, $"structure-marker-add:{x},{z}:{mx},{mz}"));

    public string MoveStructureMarker(double x, double z, double newX, double newZ) =>
        Apply(string.Create(CultureInfo.InvariantCulture, $"structure-marker-move:{x},{z}:{newX},{newZ}"));

    public string RemoveStructureMarker(double x, double z) => Apply(string.Create(CultureInfo.InvariantCulture, $"structure-marker-remove:{x},{z}"));

    /// <summary>Whether the structure at a point starts the game destroyed.</summary>
    public string SetStructureBroken(double x, double z, bool broken) =>
        Apply(string.Create(CultureInfo.InvariantCulture, $"structure-broken:{x},{z}:{(broken ? 1 : 0)}"));

    public string RemoveStructure(double x, double z) => Apply(string.Create(CultureInfo.InvariantCulture, $"structure-remove:{x},{z}"));

    /// <summary>Files an operation writes (text and binary), copies in and deletes.</summary>
    sealed record PackagePlan(string Report, List<(string Entry, string Text)> Writes, List<(string From, string Target, bool Present)> Copies)
    {
        public List<(string Entry, byte[] Bytes)> Binaries { get; init; } = [];
        public List<string> Deletes { get; init; } = [];
    }

    PackagePlan PlanStructure(string name, string[] args)
    {
        var config = StructureConfig.Load(Package.Root);
        switch (name)
        {
            case "structure-kind":
                return PlanStructureKind(args, config, change: false);
            case "structure-change":
                return PlanStructureKind(args, config, change: true);
            case "structure-add":
            {
                if (args.Length == 4 && args[3] != "broken")
                    throw new InvalidOperationException($"'{args[3]}' isn't 'broken' (a door or tunnel that starts the game destroyed).");
                if (args.Length != 4)
                    Expect(args, 3, name);
                var kind = config.Kinds.GetValueOrDefault(args[0]) ?? throw new InvalidOperationException($"This map has no structure kind called {args[0]}.");
                var (x, z) = Point(args[1]);
                int facing = (int)Number(args[2]);
                if (facing is < 0 or > 3)
                    throw new InvalidOperationException("facing must be 0 (south), 1 (east), 2 (north) or 3 (west).");
                bool broken = args.Length == 4;
                if (broken && kind.Repair == null)
                    throw new InvalidOperationException($"{kind.Name} is {(kind.Type == StructureType.Guardian ? "a guardian tower" : "an open tunnel")}: it can't be destroyed, so it can't start destroyed.");
                // On a 16-unit grid: every part's blocked squares then tile exactly, and the type map lines up.
                double sx = Math.Round(x / 16) * 16, sz = Math.Round(z / 16) * 16;
                var instance = new StructureInstance(kind.Name, sx, sz, facing, GroundType(sx, sz, config), broken);
                // A new guardian tower takes the rules of its kind's last placed tower (the last ones used), or its kind's
                // targets in the band when it is the first.
                string rulesNote = "";
                if (kind.Type == StructureType.Guardian)
                {
                    var last = config.Instances.LastOrDefault(i => i.Kind == kind.Name);
                    instance = instance with { Rules = last != null ? RulesOf(last, kind) : GuardianRules.Default(kind.TargetList) };
                    rulesNote = last != null ? $", with the rules of the {kind.Name} at ({last.X}, {last.Z})" : "";
                }
                CheckFits(kind, instance);
                config.Instances.Add(instance);
                return new PackagePlan($"{kind.Name} ({StructureKind.TypeName(kind.Type)}) placed at ({sx}, {sz}) facing {"south east north west".Split(' ')[facing]}" +
                                       (broken ? ", destroyed at the start" : "") + rulesNote + GroundNote(kind, instance), [(StructureConfig.Entry, config.ToLua())], []);
            }
            case "structure-move":
            {
                Expect(args, 2, name);
                var (x, z) = Point(args[0]);
                var (nx, nz) = Point(args[1]);
                var nearest = Nearest(config, x, z);
                var kind = config.Kinds.GetValueOrDefault(nearest.Kind) ?? throw new InvalidOperationException($"This map has no structure kind called {nearest.Kind}.");
                double sx = Math.Round(nx / 16) * 16, sz = Math.Round(nz / 16) * 16;
                if (sx == nearest.X && sz == nearest.Z)
                    throw new InvalidOperationException($"The {nearest.Kind} is already at ({sx}, {sz}).");
                var moved = nearest with { X = sx, Z = sz, Ground = GroundType(sx, sz, config) };
                CheckFits(kind, moved);
                config.Instances[config.Instances.IndexOf(nearest)] = moved;
                return new PackagePlan($"{nearest.Kind} moved from ({nearest.X}, {nearest.Z}) to ({sx}, {sz})" + GroundNote(kind, moved),
                    [(StructureConfig.Entry, config.ToLua())], []);
            }
            case "structure-turn":
            {
                Expect(args, 2, name);
                var (x, z) = Point(args[0]);
                int facing = (int)Number(args[1]);
                if (facing is < 0 or > 3)
                    throw new InvalidOperationException("facing must be 0 (south), 1 (east), 2 (north) or 3 (west).");
                var nearest = Nearest(config, x, z);
                var kind = config.Kinds.GetValueOrDefault(nearest.Kind) ?? throw new InvalidOperationException($"This map has no structure kind called {nearest.Kind}.");
                if (nearest.Facing == facing)
                    throw new InvalidOperationException($"The {nearest.Kind} at ({nearest.X}, {nearest.Z}) already faces that way.");
                var turned = nearest with { Facing = facing };
                CheckFits(kind, turned);
                config.Instances[config.Instances.IndexOf(nearest)] = turned;
                return new PackagePlan($"{nearest.Kind} at ({nearest.X}, {nearest.Z}) turned: " +
                                       (kind.Type == StructureType.Guardian ? $"it faces {"south east north west".Split(' ')[facing]}" : $"its passage runs {(facing % 2 == 0 ? "north-south" : "east-west")}") +
                                       GroundNote(kind, turned), [(StructureConfig.Entry, config.ToLua())], []);
            }
            case "structure-marker-add":
            {
                Expect(args, 2, name);
                var (x, z) = Point(args[0]);
                var (mx, mz) = Point(args[1]);
                var nearest = Nearest(config, x, z);
                var kind = config.Kinds.GetValueOrDefault(nearest.Kind) ?? throw new InvalidOperationException($"This map has no structure kind called {nearest.Kind}.");
                if (kind.Marker == null)
                    throw new InvalidOperationException($"{kind.Name} has no repair markers to place: only built doors and tunnels do (an open tunnel isn't repaired; " +
                                                        "a kind made from assets puts its own beside its walls).");
                var (px, pz) = MarkerSpot(config, mx, mz);
                config.Instances[config.Instances.IndexOf(nearest)] = nearest with { Markers = [.. nearest.MarkerList, (px, pz)] };
                return new PackagePlan($"a repair marker for the {kind.Name} at ({nearest.X}, {nearest.Z}) placed at ({px}, {pz})", [(StructureConfig.Entry, config.ToLua())], []);
            }
            case "structure-marker-move":
            {
                Expect(args, 2, name);
                var (x, z) = Point(args[0]);
                var (nx, nz) = Point(args[1]);
                var (instance, index) = NearestMarker(config, x, z);
                var markers = instance.MarkerList.ToList();
                var from = markers[index];
                markers.RemoveAt(index);
                var (px, pz) = MarkerSpot(config, nx, nz, except: from);
                markers.Insert(index, (px, pz));
                config.Instances[config.Instances.IndexOf(instance)] = instance with { Markers = markers };
                return new PackagePlan($"the {instance.Kind}'s repair marker moved from ({from.X}, {from.Z}) to ({px}, {pz})", [(StructureConfig.Entry, config.ToLua())], []);
            }
            case "structure-marker-remove":
            {
                Expect(args, 1, name);
                var (x, z) = Point(args[0]);
                var (instance, index) = NearestMarker(config, x, z);
                var markers = instance.MarkerList.ToList();
                var gone = markers[index];
                markers.RemoveAt(index);
                config.Instances[config.Instances.IndexOf(instance)] = instance with { Markers = markers.Count > 0 ? markers : null };
                return new PackagePlan($"the {instance.Kind}'s repair marker at ({gone.X}, {gone.Z}) removed", [(StructureConfig.Entry, config.ToLua())], []);
            }
            case "structure-broken":
            {
                Expect(args, 2, name);
                var (x, z) = Point(args[0]);
                var nearest = Nearest(config, x, z);
                bool broken = args[1] switch { "1" => true, "0" => false, _ => throw new InvalidOperationException($"'{args[1]}' isn't 1 (starts destroyed) or 0 (starts standing).") };
                if (broken && config.Kinds.GetValueOrDefault(nearest.Kind)?.Repair == null)
                    throw new InvalidOperationException($"{nearest.Kind} is an open tunnel: it can't be destroyed, so it can't start destroyed.");
                if (nearest.Broken == broken)
                    throw new InvalidOperationException($"The {nearest.Kind} at ({nearest.X}, {nearest.Z}) already starts {(broken ? "destroyed" : "standing")}.");
                config.Instances[config.Instances.IndexOf(nearest)] = nearest with { Broken = broken };
                return new PackagePlan($"{nearest.Kind} at ({nearest.X}, {nearest.Z}) starts {(broken ? "destroyed" : "standing")}", [(StructureConfig.Entry, config.ToLua())], []);
            }
            case "structure-targets":
            {
                Expect(args, 2, name);
                var kind = config.Kinds.GetValueOrDefault(args[0]) ?? throw new InvalidOperationException($"This map has no structure kind called {args[0]}.");
                if (kind.Type != StructureType.Guardian)
                    throw new InvalidOperationException($"{kind.Name} isn't a guardian tower: only guardians have targets.");
                var targets = ParseTargets(args[1]);
                config.Kinds[kind.Name] = kind with { Targets = targets };
                return new PackagePlan($"the first {kind.Name} placed destroys {(targets.Count == 0 ? "nothing" : targets.Count == 1 ? "1 unit type" : $"{targets.Count} unit types")} " +
                                       "crossing its path (towers already placed keep their own rules; later ones copy the last placed)",
                    [(StructureConfig.Entry, config.ToLua())], []);
            }
            case "structure-rule":
            {
                if (args.Length != 5)
                    throw new InvalidOperationException("structure-rule needs <x>,<z>:<band|inside|outside>:<delay seconds>:<all|none|teams 1-8, e.g. 2,3>:<unit names|none>.");
                var (x, z) = Point(args[0]);
                var nearest = Nearest(config, x, z);
                var kind = config.Kinds.GetValueOrDefault(nearest.Kind) ?? throw new InvalidOperationException($"This map has no structure kind called {nearest.Kind}.");
                if (kind.Type != StructureType.Guardian)
                    throw new InvalidOperationException($"The {nearest.Kind} at ({nearest.X}, {nearest.Z}) isn't a guardian tower: only guardians have rules.");
                var region = GuardianRules.ParseRegion(args[1].Trim());
                double delay = Number(args[2]);
                if (double.IsNaN(delay) || delay is < 0 or > GuardianRule.MaxDelay)
                    throw new InvalidOperationException($"A delay is 0 to {GuardianRule.MaxDelay} seconds, not {args[2]}.");
                var rule = new GuardianRule(ParseTargets(args[4]), ParseTeams(args[3]), delay);
                var rules = RulesOf(nearest, kind);
                if (nearest.Rules != null && rules[region].Same(rule))
                    throw new InvalidOperationException($"The {nearest.Kind} at ({nearest.X}, {nearest.Z}) already does that in its {GuardianRules.RegionName(region)}.");
                config.Instances[config.Instances.IndexOf(nearest)] = nearest with { Rules = rules.With(region, rule) };
                string where = region switch
                {
                    GuardianRegion.Inside => "inside its path",
                    GuardianRegion.Outside => "outside its path",
                    _ => "crossing its path",
                };
                string what = rule.Acts
                    ? string.Create(CultureInfo.InvariantCulture,
                        $"destroys {(rule.Targets.Count == 1 ? "1 unit type" : $"{rule.Targets.Count} unit types")} of {(rule.AllTeams ? "every team" : $"team{(rule.Teams!.Count == 1 ? "" : "s")} {string.Join(", ", rule.Teams!)}")} {where}") +
                      (rule.DelaySeconds > 0 ? string.Create(CultureInfo.InvariantCulture, $" after {rule.DelaySeconds:0.#} s there") : "")
                    : $"destroys nothing {where}";
                return new PackagePlan($"the {nearest.Kind} at ({nearest.X}, {nearest.Z}) {what}" +
                                       (region == GuardianRegion.Inside && nearest.Zone is not { Closed: true } ? " (once its path is closed)" : ""),
                    [(StructureConfig.Entry, config.ToLua())], []);
            }
            case "structure-zone":
            {
                if (args.Length is not (2 or 4))
                    throw new InvalidOperationException("structure-zone needs <x>,<z>:<distance>:<closed 0|1>:<x1>,<z1>;<x2>,<z2>;... or <x>,<z>:none.");
                var (x, z) = Point(args[0]);
                var nearest = Nearest(config, x, z);
                var kind = config.Kinds.GetValueOrDefault(nearest.Kind) ?? throw new InvalidOperationException($"This map has no structure kind called {nearest.Kind}.");
                if (kind.Type != StructureType.Guardian)
                    throw new InvalidOperationException($"The {nearest.Kind} at ({nearest.X}, {nearest.Z}) isn't a guardian tower: only guardians guard a path.");
                GuardianZone? zone = null;
                if (args.Length == 2)
                {
                    if (args[1].Trim() != "none")
                        throw new InvalidOperationException($"'{args[1]}' isn't 'none' (no zone).");
                    if (nearest.Zone == null)
                        throw new InvalidOperationException($"The {nearest.Kind} at ({nearest.X}, {nearest.Z}) has no zone to clear.");
                }
                else
                {
                    double distance = Number(args[1]);
                    if (double.IsNaN(distance) || distance is < GuardianZone.MinDistance or > GuardianZone.MaxDistance)
                        throw new InvalidOperationException($"A guardian reaches {GuardianZone.MinDistance} to {GuardianZone.MaxDistance} game units from its path, not {distance}.");
                    bool closed = args[2].Trim() switch { "1" => true, "0" => false, var c => throw new InvalidOperationException($"'{c}' isn't 1 (the path closes) or 0.") };
                    var points = args[3].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(Point).ToList();
                    if (points.Count < 2)
                        throw new InvalidOperationException("A guardian's path needs at least two points.");
                    if (points.Count > 256)
                        throw new InvalidOperationException("A guardian's path has at most 256 points.");
                    foreach (var (px, pz) in points)
                        if (px < 0 || pz < 0 || px > MapWidth || pz > MapHeight)
                            throw new InvalidOperationException($"The path's point ({px}, {pz}) is off the map.");
                    for (int i = 1; i < points.Count; i++)
                        if (points[i] == points[i - 1])
                            throw new InvalidOperationException($"The path has the point ({points[i].X}, {points[i].Z}) twice in a row.");
                    zone = new GuardianZone(points, distance, closed && points.Count > 2);
                }
                config.Instances[config.Instances.IndexOf(nearest)] = nearest with { Zone = zone };
                double length = zone?.Segments().Sum(s => Math.Sqrt((s.B.X - s.A.X) * (s.B.X - s.A.X) + (s.B.Z - s.A.Z) * (s.B.Z - s.A.Z))) ?? 0;
                return new PackagePlan(zone == null
                        ? $"the {nearest.Kind} at ({nearest.X}, {nearest.Z}) guards no path"
                        : string.Create(CultureInfo.InvariantCulture,
                            $"the {nearest.Kind} at ({nearest.X}, {nearest.Z}) guards a {(zone.Closed ? "closed " : "")}path of {zone.Points.Count} points ({length:0} long), {zone.Distance:0} either side"),
                    [(StructureConfig.Entry, config.ToLua())], []);
            }
            case "structure-remove":
            {
                Expect(args, 1, name);
                var (x, z) = Point(args[0]);
                var nearest = Nearest(config, x, z);
                config.Instances.Remove(nearest);
                return new PackagePlan($"{nearest.Kind} at ({nearest.X}, {nearest.Z}) removed", [(StructureConfig.Entry, config.ToLua())], []);
            }
            default:
                throw new InvalidOperationException($"Unknown operation '{name}'.");
        }
    }

    /// <summary>Where a repair marker goes: on the 8-unit grid, on the map, and not on another marker (<paramref name="except"/>: the one being moved).</summary>
    (double X, double Z) MarkerSpot(StructureConfig config, double x, double z, (double X, double Z)? except = null)
    {
        double px = Math.Round(x / 8) * 8, pz = Math.Round(z / 8) * 8;
        if (px < 8 || pz < 8 || px > MapWidth - 8 || pz > MapHeight - 8)
            throw new InvalidOperationException($"A repair marker at ({px}, {pz}) would stick out of the map.");
        if (except is var (ex, ez) && ex == px && ez == pz)
            throw new InvalidOperationException($"The repair marker is already at ({px}, {pz}).");
        if (config.Instances.SelectMany(i => i.MarkerList).Any(m => Math.Abs(m.X - px) < 16 && Math.Abs(m.Z - pz) < 16 && m != except))
            throw new InvalidOperationException($"There is already a repair marker at ({px}, {pz}).");
        return (px, pz);
    }

    /// <summary>The repair marker nearest a point (within 32), and the structure it belongs to.</summary>
    static (StructureInstance Instance, int Index) NearestMarker(StructureConfig config, double x, double z)
    {
        var found = config.Instances.SelectMany(i => i.MarkerList.Select((m, index) => (Instance: i, Index: index, m.X, m.Z)))
            .OrderBy(m => (m.X - x) * (m.X - x) + (m.Z - z) * (m.Z - z)).FirstOrDefault();
        if (found.Instance == null || Math.Abs(found.X - x) > 32 || Math.Abs(found.Z - z) > 32)
            throw new InvalidOperationException($"There is no repair marker within 32 of ({x}, {z}).");
        return (found.Instance, found.Index);
    }

    /// <summary>The repair marker at a point (within <paramref name="reach"/>), if any: its structure and where it is.</summary>
    public (StructureInstance Instance, double X, double Z)? StructureMarkerAt(double x, double z, double reach)
    {
        var found = Structures.Instances.SelectMany(i => i.MarkerList.Select(m => (Instance: i, m.X, m.Z)))
            .OrderBy(m => (m.X - x) * (m.X - x) + (m.Z - z) * (m.Z - z)).FirstOrDefault();
        return found.Instance != null && Math.Abs(found.X - x) <= reach && Math.Abs(found.Z - z) <= reach ? found : null;
    }

    static StructureInstance Nearest(StructureConfig config, double x, double z)
    {
        var nearest = config.Instances.OrderBy(s => (s.X - x) * (s.X - x) + (s.Z - z) * (s.Z - z)).FirstOrDefault();
        if (nearest == null || Math.Abs(nearest.X - x) > 64 || Math.Abs(nearest.Z - z) > 64)
            throw new InvalidOperationException($"There is no door or tunnel within 64 of ({x}, {z}).");
        return nearest;
    }

    void CheckFits(StructureKind kind, StructureInstance instance)
    {
        foreach (var part in StructureLayout.Parts(kind, instance))
            if (part.X1 < 0 || part.Z1 < 0 || part.X2 > MapWidth || part.Z2 > MapHeight)
                throw new InvalidOperationException($"The {kind.Name} at ({instance.X}, {instance.Z}) would stick out of the map.");
    }

    /// <summary>
    /// A warning when the ground under a structure's walls and centre isn't level: each part stands at its own ground
    /// height in game, so on a slope they step and their tops don't line up.
    /// </summary>
    string GroundNote(StructureKind kind, StructureInstance instance)
    {
        double low = double.MaxValue, high = double.MinValue;
        var parts = StructureLayout.Parts(kind, instance).Where(p => p.Role is "wall" or "centre").ToList();
        foreach (var part in parts)
            for (double x = part.X1; x <= part.X2; x += 8)
                for (double z = part.Z1; z <= part.Z2; z += 8)
                {
                    double h = Heights.HeightAt(Math.Clamp(x, 0, MapWidth), Math.Clamp(z, 0, MapHeight));
                    low = Math.Min(low, h);
                    high = Math.Max(high, h);
                }
        if (high - low <= 8)
            return "";
        // One part (a guardian tower): it stands at the ground height at its middle, its base reaching a skirt below.
        return parts.Count == 1
            ? string.Create(CultureInfo.InvariantCulture, $". Note: the ground under it rises and falls by {high - low:0} game units; it stands at the height of the ground at its middle, so its base may float or sink at the edges: level the ground there (Heights › Flatten)")
            : string.Create(CultureInfo.InvariantCulture, $". Note: the ground under it rises and falls by {high - low:0} game units; each part stands at its own ground height, so they may not line up: level the ground there (Heights › Flatten)");
    }

    PackagePlan PlanStructureKind(string[] args, StructureConfig config, bool change)
    {
        string op = change ? "structure-change" : "structure-kind";
        // A guardian kind may be given its targets after its settings (structure-kind:<name>:guardian:<settings>:<targets>).
        bool withTargets = !change && args.Length == 4 && args[1] == "guardian";
        if (!withTargets)
            Expect(args, change ? 2 : 3, op);
        var name = args[0];
        StructureType type;
        StructureKind? old = null;
        if (change)
        {
            old = config.Kinds.GetValueOrDefault(name) ?? throw new InvalidOperationException($"This map has no structure kind called {name}.");
            type = old.Type;
        }
        else
            type = StructureKind.ParseType(args[1]);
        var settings = StructureKindSettings.Decode(args[change ? 1 : 2], type);
        if (!CustomFeature.NamePattern.IsMatch(name) || name.Length > 56)
            throw new InvalidOperationException($"'{name}' can't be a structure name: use lower-case letters, digits and '_', starting with a letter (56 at most).");
        if (settings.Problem(type) is string problem)
            throw new InvalidOperationException($"{name} can't be {(change ? "changed" : "made")}: {problem}.");
        bool pump = type == StructureType.Pump, guardian = type == StructureType.Guardian, pad = type == StructureType.ShieldPad;
        int guardianSquares = (int)(StructureModels.GuardianSize / 16);
        var kind = new StructureKind(name, type, pump ? (int)(StructureModels.PumpWidth / 16) : guardian ? guardianSquares : settings.Gap, settings.WallWidth,
            pump ? (int)(StructureModels.PumpLength / 16) : guardian ? guardianSquares : settings.Depth, settings.Metal, settings.Energy, settings.OpenWhenBroken,
            settings.Look == StructureLook.Assets ? 0 : guardian ? StructureModels.GuardianHeight : pad ? StructureModels.ShieldPadHeight : settings.Height,
            settings.Roof, settings.Look, settings.Encode(),
            pump ? settings.MetalIncome : 0, pump ? settings.EnergyIncome : 0,
            guardian ? (withTargets ? ParseTargets(args[3]) : old?.Targets ?? []) : null,
            guardian ? settings.KillSeconds : 0, guardian ? settings.RocketSeconds : 0,
            pad ? settings.ShieldPercent : 0, pad ? settings.EnergyPerMetal : 0, pad ? settings.BubbleScale : 0);
        var defined = FeatureDefs.Load(Package.Root).SelectMany(f => f.Features).ToList();
        var existing = PackageFiles();
        if (change)
        {
            foreach (var instance in config.Instances.Where(s => s.Kind == name))
                CheckFits(kind, instance);
        }
        else
        {
            if (config.Kinds.ContainsKey(name))
                throw new InvalidOperationException($"This map already has a structure kind called {name}.");
            foreach (var part in new[] { kind.Wall, kind.Centre, kind.Repair, kind.Name + "_broken", kind.Name + "_marker", kind.Name + "_beam" }.OfType<string>())
                if (defined.Any(d => d.Name == part) || existing.ContainsKey($"features/{part}.lua"))
                    throw new InvalidOperationException($"This map already has a feature called {part}.");
        }

        var copies = new List<(string From, string Target, bool Present)>();
        var writes = new List<(string Entry, string Text)>();
        var binaries = new List<(string Entry, byte[] Bytes)>();
        var deletes = new List<string>();
        var none = new CustomFeatureSettings { Metal = 0, Energy = 0, Flammable = false, Reclaimable = false, AutoReclaimable = false, ReclaimTime = 1 };
        var wall = none with
        {
            Description = $"{name}: wall", Health = 1e9, Indestructible = true, Blocking = true, FootprintX = kind.WallWidth, FootprintZ = kind.Depth,
        };
        var centre = none with
        {
            Description = type switch
            {
                StructureType.Door => $"{name}: door",
                StructureType.Tunnel when kind.BuildOnTop => $"{name}: tunnel top (build on it; it can collapse)",
                StructureType.Tunnel => $"{name}: tunnel (it can collapse)",
                _ => $"{name}: open tunnel",
            },
            Health = type == StructureType.OpenTunnel ? 1e9 : settings.Health,
            Indestructible = type == StructureType.OpenTunnel,
            Blocking = type != StructureType.OpenTunnel, // a tunnel top is taken off the blocking map by the gadget
            FootprintX = kind.Gap, FootprintZ = kind.Depth,
        };
        var repair = none with
        {
            Description = $"Reclaim to repair the {(type == StructureType.Door ? "door" : "tunnel")} ({name})",
            Health = 100, Indestructible = true, Blocking = false, Reclaimable = true, ReclaimTime = settings.Work,
            FootprintX = 1, FootprintZ = 1,
        };
        if (settings.Look == StructureLook.Assets)
        {
            void Part(string feature, string baseName, CustomFeatureSettings partSettings)
            {
                var baseDef = ModelSource(baseName.ToLowerInvariant(), name, defined, existing, copies);
                writes.Add(($"features/{feature}.lua", CustomFeature.Lua(feature, baseDef.Name, baseDef.Object!, baseDef.Table, null, partSettings)));
            }
            Part(kind.Wall, settings.Wall!, wall);
            Part(kind.Centre, settings.Centre!, centre);
            if (kind.Repair != null)
                Part(kind.Repair, settings.Repair!, repair);
            if (old?.Look is StructureLook.Concrete or StructureLook.Metal) // the models it built go with it, and its broken door
                foreach (var part in new[] { kind.Wall, kind.Centre, kind.Repair, old.Debris, old.Marker }.OfType<string>())
                    foreach (var entry in new[] { BuiltModel(part), $"features/{part}.lua" })
                        if (existing.ContainsKey(entry) && (entry.EndsWith(".s3o", StringComparison.Ordinal) || part == old.Debris || part == old.Marker))
                            deletes.Add(entry);
        }
        else if (guardian)
        {
            // The tower (indestructible), and what every guardian shares: its atlas, the rocket's model and weapon, and the effects.
            var tower = StructureModels.Guardian(settings.Look);
            binaries.Add((BuiltModel(kind.Centre), tower.ToS3o(kind.Centre, StructureModels.GuardianTexture1, StructureModels.GuardianTexture2)));
            writes.Add(($"features/{kind.Centre}.lua", BuiltPartLua(kind.Centre, name, settings.Look, tower, none with
            {
                Description = $"{name}: guardian tower (it destroys the units it is set to in the zone it guards)", Health = 1e9,
                Indestructible = true, Blocking = true, FootprintX = kind.Gap, FootprintZ = kind.Depth,
            }, what: "guardian tower")));
            var (colour, extra) = StructureModels.GuardianTextureFiles();
            binaries.Add(("unittextures/" + StructureModels.GuardianTexture1, colour));
            binaries.Add(("unittextures/" + StructureModels.GuardianTexture2, extra));
            binaries.Add((StructureFiles.RocketModelEntry, StructureModels.GuardianRocket().ToS3o("rocket", StructureModels.GuardianTexture1, StructureModels.GuardianTexture2)));
            writes.Add((StructureFiles.RocketWeaponEntry, StructureFiles.RocketWeaponText));
            writes.Add((StructureFiles.GuardianEffectsEntry, StructureFiles.GuardianEffectsText));
        }
        else if (pump)
        {
            // The pump, its beam (its own feature, made to nod by the gadget) and the derelict, from the pump's own atlas.
            void PumpPart(string feature, StructureMesh mesh, CustomFeatureSettings partSettings, bool noSelect = false)
            {
                binaries.Add((BuiltModel(feature), mesh.ToS3o(feature, StructureModels.PumpTexture1, StructureModels.PumpTexture2)));
                writes.Add(($"features/{feature}.lua", BuiltPartLua(feature, name, StructureLook.Metal, mesh, partSettings, noSelect, "oil pump")));
            }
            PumpPart(kind.Centre, StructureModels.Pump(withBeam: false), none with
            {
                Description = $"{name}: oil pump (it pumps metal and energy for the team that repaired it)", Health = settings.Health,
                Indestructible = false, Blocking = true, FootprintX = kind.Gap, FootprintZ = kind.Depth,
            });
            PumpPart(kind.Beam!, StructureModels.PumpBeam(), none with
            {
                Description = $"{name}: oil pump's beam", Health = 1e9, Indestructible = true, Blocking = false, FootprintX = 1, FootprintZ = 1,
            }, noSelect: true);
            PumpPart(kind.Debris!, StructureModels.DerelictPump(StructureModels.Seed(name)), none with
            {
                // Reclaimed to repair it (the gadget turns the reclaiming into repairing); whoever finishes owns the pump.
                Description = $"Derelict oil pump ({name}): reclaim it to repair it, and it pumps for you", Health = 100, Indestructible = true,
                Blocking = true, Reclaimable = true, ReclaimTime = settings.Work, FootprintX = kind.Gap, FootprintZ = kind.Depth,
            });
            var (colour, extra) = StructureModels.PumpTextureFiles();
            binaries.Add(("unittextures/" + StructureModels.PumpTexture1, colour));
            binaries.Add(("unittextures/" + StructureModels.PumpTexture2, extra));
        }
        else if (pad)
        {
            // The platform (units drive over it: it doesn't block) and the derelict, from their own atlas, and the shields' effects.
            float w = kind.Gap * 16, d = kind.Depth * 16;
            void PadPart(string feature, StructureMesh mesh, CustomFeatureSettings partSettings)
            {
                binaries.Add((BuiltModel(feature), mesh.ToS3o(feature, StructureModels.ShieldPadTexture1, StructureModels.ShieldPadTexture2)));
                writes.Add(($"features/{feature}.lua", BuiltPartLua(feature, name, settings.Look, mesh, partSettings, what: "shield platform")));
            }
            PadPart(kind.Centre, StructureModels.ShieldPad(w, d), none with
            {
                Description = $"{name}: shield platform (its owner's and allies' units get a bubble shield as they drive off it)", Health = settings.Health,
                Indestructible = false, Blocking = false, FootprintX = kind.Gap, FootprintZ = kind.Depth,
            });
            PadPart(kind.Debris!, StructureModels.DerelictShieldPad(w, d, StructureModels.Seed(name)), none with
            {
                // Reclaimed to repair it (the gadget turns the reclaiming into repairing); whoever finishes owns the platform.
                Description = $"Derelict shield platform ({name}): reclaim it to repair it, and it shields your units", Health = 100, Indestructible = true,
                Blocking = false, Reclaimable = true, ReclaimTime = settings.Work, FootprintX = kind.Gap, FootprintZ = kind.Depth,
            });
            var (colour, extra) = StructureModels.ShieldPadTextureFiles();
            binaries.Add(("unittextures/" + StructureModels.ShieldPadTexture1, colour));
            binaries.Add(("unittextures/" + StructureModels.ShieldPadTexture2, extra));
            writes.Add((StructureFiles.ShieldEffectsEntry, StructureFiles.ShieldEffectsText));
        }
        else
        {
            float g = kind.Gap * 16, w = kind.WallWidth * 16, d = kind.Depth * 16, h = (float)kind.Height;
            int seed = StructureModels.Seed(name);
            var look = settings.Look;
            void Built(string feature, StructureMesh mesh, CustomFeatureSettings partSettings)
            {
                binaries.Add((BuiltModel(feature), mesh.ToS3o(feature)));
                writes.Add(($"features/{feature}.lua", BuiltPartLua(feature, name, look, mesh, partSettings)));
            }
            Built(kind.Wall, StructureModels.Wall(look, w, d, h), wall);
            Built(kind.Centre, type switch
            {
                StructureType.Door => StructureModels.Door(look, g, d, h),
                StructureType.Tunnel => StructureModels.TunnelTop(look, settings.Roof, g, w, d, h, seed),
                _ => StructureModels.OpenRoof(look, settings.Roof, g, w, d, h, seed),
            }, centre);
            if (kind.Repair != null)
                Built(kind.Repair, StructureModels.Repair(look, red: true), repair with { Description = $"Reclaim to repair the {(type == StructureType.Door ? "door" : "tunnel")} ({name}): red, it is destroyed" });
            if (kind.Marker != null)
                Built(kind.Marker, StructureModels.Repair(look, red: false), repair with
                {
                    Description = $"{name}: repair marker (green: it stands)", Reclaimable = false,
                });
            if (kind.Debris != null)
                Built(kind.Debris, type == StructureType.Door ? StructureModels.BrokenDoor(look, g, d, h, seed) : StructureModels.BrokenTunnel(look, settings.Roof, g, d, h, seed), none with
                {
                    // Reclaimed to repair it, as a red marker is (the gadget turns the reclaiming into repairing).
                    Description = $"{name}: what is left of the {(type == StructureType.Door ? "door" : "tunnel")} (reclaim to repair it)", Health = 100,
                    Indestructible = true, Blocking = false, Reclaimable = true, ReclaimTime = settings.Work, FootprintX = kind.Gap, FootprintZ = kind.Depth,
                });
            var (colour, extra) = StructureModels.TextureFiles(CliffColour());
            binaries.Add(("unittextures/" + StructureModels.Texture1, colour));
            binaries.Add(("unittextures/" + StructureModels.Texture2, extra));
        }

        config.Kinds[name] = kind;
        AssignTerrainType(config);
        writes.Add((StructureConfig.Entry, config.ToLua()));
        writes.AddRange(GadgetFiles(existing));
        int placed = config.Instances.Count(s => s.Kind == name);
        var what = change
            ? $"{name}: changed ({(placed == 1 ? "1 placed" : $"{placed} placed")}; {writes.Count + binaries.Count} file(s) written"
            : $"{name}: a new {StructureKind.TypeName(type).Replace('_', ' ')} kind ({writes.Count + binaries.Count} file(s) written";
        return new PackagePlan(what + (copies.Count(c => !c.Present) > 0 ? $", {copies.Count(c => !c.Present)} model file(s) copied from the asset library" : "") + ")",
            writes, copies) { Binaries = binaries, Deletes = deletes };
    }

    static string BuiltModel(string feature) => $"objects3d/{feature}.s3o";

    /// <summary>
    /// The colour of the map's cliffs, for rock roofs: the minimap's average where the ground is steeper than about
    /// 55° (bare rock faces, not grassy slopes), kept rock-like: half its saturation and a middling brightness. Null
    /// when the map has too little steep ground to say. Read from the map as it is, so the same map always gives the
    /// same colour.
    /// </summary>
    internal Imaging.Rgba? CliffColour()
    {
        var minimap = Smf.Minimap.Length > 0 ? Minimap.Decode(Smf, 2) : null;
        if (minimap == null)
            return null;
        double r = 0, g = 0, b = 0;
        int n = 0;
        double stepX = MapWidth / minimap.Width, stepZ = MapHeight / minimap.Height;
        for (int y = 0; y < minimap.Height; y++)
            for (int x = 0; x < minimap.Width; x++)
            {
                double wx = (x + 0.5) * stepX, wz = (y + 0.5) * stepZ;
                double dx = Heights.HeightAt(Math.Min(wx + 8, MapWidth), wz) - Heights.HeightAt(Math.Max(wx - 8, 0), wz);
                double dz = Heights.HeightAt(wx, Math.Min(wz + 8, MapHeight)) - Heights.HeightAt(wx, Math.Max(wz - 8, 0));
                if (Math.Sqrt(dx * dx + dz * dz) / 16 < 1.4)
                    continue;
                var c = minimap.GetPixel(x, y);
                (r, g, b, n) = (r + c.R, g + c.G, b + c.B, n + 1);
            }
        if (n < 64)
            return null;
        (r, g, b) = (r / n, g / n, b / n);
        double luma = 0.299 * r + 0.587 * g + 0.114 * b, target = Math.Clamp(luma, 70, 140);
        byte C(double v) => (byte)Math.Clamp(Math.Round(target + (v - luma) * 0.5), 0, 255);
        return new Imaging.Rgba(C(r), C(g), C(b), 255);
    }

    /// <summary>
    /// A built part's definition: the gameplay settings, kept upright on slopes, hit over the box it fills (the
    /// engine centres a collision volume on the model's middle, which the model's header gives as its bounding box's
    /// middle), and drawn by BAR's PBR model shader so texture 2's metalness and roughness count (cuspbr).
    /// </summary>
    static string BuiltPartLua(string feature, string kindName, StructureLook look, StructureMesh mesh, CustomFeatureSettings settings,
        bool noSelect = false, string? what = null)
    {
        static string V(Vector3 v) => string.Create(CultureInfo.InvariantCulture, $"{v.X:0.##} {v.Y:0.##} {v.Z:0.##}");
        var table = new LuaMap();
        table.Set("upright", true);
        if (noSelect)
            table.Set("noselect", true);
        table.Set("collisionvolumetype", "box");
        table.Set("collisionvolumescales", V(mesh.Max - mesh.Min));
        table.Set("collisionvolumeoffsets", V((mesh.Min + mesh.Max) / 2 - (mesh.VertexMin + mesh.VertexMax) / 2));
        var custom = new LuaMap();
        custom.Set("cuspbr", true);
        custom.Set("model_author", "BAR Map Editor");
        table.Set("customparams", custom);
        return CustomFeature.Lua(feature, $"{what ?? StructureKind.LookName(look)} ({kindName})", Path.GetFileName(BuiltModel(feature)), table, null, settings,
            $"a part of {kindName}, its {what ?? StructureKind.LookName(look)} model built by BAR Map Editor");
    }

    /// <summary>
    /// The structures gadget (always its current text), and whichever of LuaGaia's loaders the map lacks: main.lua runs
    /// the gadget's game side, draw.lua its drawing side (the shield bubbles). A map's own are never replaced.
    /// </summary>
    static IEnumerable<(string Entry, string Text)> GadgetFiles(Dictionary<string, string> existing)
    {
        yield return (StructureFiles.GadgetEntry, StructureFiles.GadgetText);
        if (!existing.ContainsKey(StructureFiles.LuaGaiaMain))
            yield return (StructureFiles.LuaGaiaMain, StructureFiles.MainText);
        if (!existing.ContainsKey(StructureFiles.LuaGaiaDraw))
            yield return (StructureFiles.LuaGaiaDraw, StructureFiles.DrawText);
    }

    /// <summary>
    /// Gives the config a terrain type for tunnel tops, if it has none: the highest the map doesn't use (in its type
    /// map or mapinfo's terrainTypes). A map whose type map uses all 256 (one painted as a picture, say) gets one
    /// freed instead: the least used of the types that behave exactly as another does. Its squares are listed for the
    /// gadget to give that other type at game start, which changes nothing in play.
    /// </summary>
    void AssignTerrainType(StructureConfig config)
    {
        if (config.TerrainType != null)
            return;
        var counts = new int[256];
        foreach (byte b in Smf.TypeMap)
            counts[b]++;
        var defined = new Dictionary<int, LuaTable?>();
        if (Info.Get("terrainTypes") is LuaTable types)
            foreach (var field in types.Fields)
                if (int.TryParse(field.KeyText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && index is >= 0 and < 256)
                    defined[index] = field.Value as LuaTable;
        for (int t = 255; t > 0; t--)
            if (counts[t] == 0 && !defined.ContainsKey(t))
            {
                config.TerrainType = t;
                return;
            }
        // Every type is used. Those that behave as the engine's default does (no definition, or one with only its
        // default values: Recoil's MapInfo.cpp, hardness 1, tracks, every speed 1) are interchangeable.
        // Its squares go to another undefined type if there is one: map option scripts (All That Glitters' roads) may
        // rewrite every type mapinfo defines, but never one it doesn't.
        var plain = Enumerable.Range(0, 256).Where(t => counts[t] > 0 && (!defined.TryGetValue(t, out var def) || IsDefaultTerrain(def))).ToList();
        var freed = plain.Where(t => !defined.ContainsKey(t)).OrderBy(t => counts[t]).ThenByDescending(t => t).FirstOrDefault(-1);
        var into = plain.Where(t => t != freed).OrderBy(t => defined.ContainsKey(t)).ThenByDescending(t => counts[t]).ThenBy(t => t).FirstOrDefault(-1);
        if (freed < 0 || into < 0)
            throw new InvalidOperationException("This map uses every terrain type, and none behaves as another does: none can be freed for tunnel tops.");
        config.TerrainType = freed;
        config.RemapInto = into;
        int w = Smf.MapX / 2;
        for (int i = 0; i < Smf.TypeMap.Length; i++)
            if (Smf.TypeMap[i] == freed)
                config.RemapSquares.Add((i % w, i / w));
    }

    /// <summary>A mapinfo terrain type with nothing but the engine's defaults (a name aside); null (not a table) isn't.</summary>
    static bool IsDefaultTerrain(LuaTable? def)
    {
        if (def == null)
            return false;
        static bool Is(LuaValue? v, double value) => v == null || v is LuaNumber n && n.Value == value;
        if (!Is(def["hardness"], 1) || def["receiveTracks"] is not (null or LuaBoolean { Value: true }))
            return false;
        if (def["moveSpeeds"] is null)
            return true;
        return def["moveSpeeds"] is LuaTable speeds && new[] { "tank", "kbot", "hover", "ship" }.All(k => Is(speeds[k], 1)) &&
               speeds.Fields.All(f => f.KeyText.ToLowerInvariant() is "tank" or "kbot" or "hover" or "ship");
    }

    /// <summary>
    /// The map's own terrain type at a point (its SMF type map, one entry per 16 game units), as the game has it
    /// once the gadget has freed a type for tunnel tops.
    /// </summary>
    int GroundType(double x, double z, StructureConfig config)
    {
        int w = Smf.MapX / 2, h = Smf.MapY / 2;
        int tx = Math.Clamp((int)(x / 16), 0, w - 1), tz = Math.Clamp((int)(z / 16), 0, h - 1);
        int type = Smf.TypeMap.Length >= w * h ? Smf.TypeMap[tz * w + tx] : 0;
        return type == config.TerrainType && config.RemapInto is int into ? into : type;
    }

    string StructureCore(string name, string[] args)
    {
        var plan = PlanStructure(name, args);
        foreach (var (from, target, present) in plan.Copies)
        {
            if (present)
                continue;
            var path = Path.Combine(Package.Root, target.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(from, path, overwrite: false);
            File.SetAttributes(path, FileAttributes.Normal);
        }
        foreach (var (entry, text) in plan.Writes)
        {
            var path = Path.Combine(Package.Root, entry.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text, new UTF8Encoding(false)); // Lua 5.1 won't read a byte-order mark
        }
        foreach (var (entry, bytes) in plan.Binaries)
        {
            var path = Path.Combine(Package.Root, entry.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            FileSystemUtil.WriteAllBytesAtomic(path, bytes);
        }
        foreach (var entry in plan.Deletes)
        {
            var path = Path.Combine(Package.Root, entry.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
        structures = null; // read again, even within the file time's resolution
        return plan.Report;
    }

    List<Snapshot> StructureSnapshots(string name, string[] args)
    {
        try
        {
            var plan = PlanStructure(name, args);
            return [new PackageFilesSnapshot(this, [.. plan.Writes.Select(w => w.Entry), .. plan.Binaries.Select(b => b.Entry), .. plan.Deletes,
                .. plan.Copies.Where(c => !c.Present).Select(c => c.Target)])];
        }
        catch (InvalidOperationException)
        {
            return []; // the operation itself will refuse, with the reason
        }
    }
}
