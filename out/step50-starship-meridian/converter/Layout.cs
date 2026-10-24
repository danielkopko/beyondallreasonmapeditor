// Starship Meridian's layout, in game units (x east, z south; the map 24 × 28, 12288 × 14336), the same numbers as
// the design's table (../design/layout.py). The port half (x < 6144) is given; the starboard half is its mirror image
// across the centreline x = 6144. A room is a cell; where two cells of different groups touch, a 100-wide bulkhead
// stands between them, and doors cut through the bulkheads. Everything in the hull that isn't a cell or a door is
// solid; outside the hull is space (ground the engine never draws: voidGround).
static class Layout
{
    public const int SquaresX = 1536, SquaresZ = 1792;                // 24 × 28
    public const double W = SquaresX * 8, H = SquaresZ * 8, CX = W / 2;
    public const float MinH = 0, MaxH = 520;

    public const double Deck = 240, BridgeDeck = 380, WellFloor = 120;
    public const double Bulkhead = 340, Rim = 380, BridgeWalls = 440, Space = 20;
    /// <summary>How steep a bulkhead's face is: rise per unit across (tan 70°).</summary>
    public const double WallRise = 2.75;
    /// <summary>The hull's rim: a band this wide inside the hull's outline stands at Rim.</summary>
    public const double RimBand = 160;

    public static double Mirror(double x) => W - x;
    public static P Mirror(P p) => new(W - p.X, p.Z);

    /// <summary>The hull's port outline, from the bow's tip round to the stern on the centreline.</summary>
    public static readonly P[] HullPort =
    [
        new(6144, 400), new(4300, 1100), new(2200, 2400), new(900, 3800), new(900, 13200), new(1600, 13200), new(1600, 14000),
        new(3200, 14000), new(3200, 13200), new(5244, 13200), new(5244, 14000), new(6144, 14000),
    ];
    public static readonly P[] Hull = HullPort.Concat(HullPort.Skip(1).Take(HullPort.Length - 2).Reverse().Select(Mirror)).ToArray();
    /// <summary>The engine housings at the stern: solid machinery at the rim's height, their engine bells over space.</summary>
    public static readonly (double X0, double Z0, double X1, double Z1)[] EngineHousings = [(1600, 13200, 3200, 14000), (5244, 13200, 7044, 14000), (Mirror(3200), 13200, Mirror(1600), 14000)];

    public enum Kind { Start, Team, Passage, Shared, Ramp, Landing }

    /// <summary>A cell: its name, kind, the group whose cells don't wall each other off, its floor's height, its outline.</summary>
    public sealed record Cell(string Name, Kind Kind, string Group, double Floor, P[] Outline, string Side);

    static P[] Rect(double x0, double z0, double x1, double z1) => [new(x0, z0), new(x1, z0), new(x1, z1), new(x0, z1)];

    static readonly (string Name, Kind Kind, string Group, double Floor, P[] Outline)[] TeamCellsPort =
    [
        ("Weapons Bay", Kind.Team, "weapons", Deck, [new(2300, 2700), new(4400, 2700), new(4400, 3780), new(1080, 3780)]),
        ("Forward Hangar", Kind.Start, "fwdhangar", Deck, Rect(1028, 3900, 2700, 6600)),
        ("Crew Deck & Mess", Kind.Start, "crew", Deck, Rect(2800, 3900, 4400, 6600)),
        ("Power Room", Kind.Team, "power", Deck, Rect(1028, 6700, 2700, 8000)),
        ("Armory", Kind.Team, "armory", Deck, Rect(2800, 6700, 4400, 8000)),
        ("Main Hangar", Kind.Start, "mainhangar", Deck, Rect(1028, 8100, 2700, 10800)),
        ("Cargo Hold", Kind.Start, "cargo", Deck, Rect(2800, 8100, 4400, 10800)),
        ("Thruster Bay", Kind.Team, "thruster", Deck, Rect(1028, 10900, 2700, 13072)),
        ("Machine Shop", Kind.Team, "shop", Deck, Rect(2800, 10900, 4400, 13072)),
        // The passage, its bridge ramp and the ramp's landing are one way with the bridge itself: no bulkheads between them.
        ("Passage", Kind.Passage, "bridgeway", Deck, Rect(4400, 3280, 5000, 13072)),
        ("Bridge Ramp", Kind.Ramp, "bridgeway", Deck, Rect(4400, 2400, 5000, 3280)),
        ("Bridge Landing", Kind.Landing, "bridgeway", BridgeDeck, Rect(4400, 1900, 5000, 2400)),
    ];
    static readonly (string Name, Kind Kind, string Group, double Floor, P[] Outline)[] SharedCells =
    [
        ("Bridge", Kind.Shared, "bridgeway", BridgeDeck, [new(5200, 1500), new(7088, 1500), new(7288, 1800), new(7288, 3780), new(5000, 3780), new(5000, 1800)]),
        ("CIC", Kind.Shared, "cic", Deck, Rect(5000, 3900, 7288, 6600)),
        ("Main Reactor", Kind.Shared, "reactor", Deck, Rect(5000, 6700, 7288, 9400)),
        ("Shield Generator", Kind.Shared, "shield", Deck, Rect(5000, 9500, 7288, 11200)),
        ("Engine Room", Kind.Shared, "engine", Deck, Rect(5000, 11300, 7288, 13072)),
    ];

    /// <summary>Every cell, port's, starboard's (mirrored, their groups suffixed) and the centreline's.</summary>
    public static readonly Cell[] Cells =
        TeamCellsPort.Select(c => new Cell(c.Name, c.Kind, c.Group == "bridgeway" ? c.Group : c.Group + ".port", c.Floor, c.Outline, "port"))
        .Concat(TeamCellsPort.Select(c => new Cell(c.Name, c.Kind, c.Group == "bridgeway" ? c.Group : c.Group + ".starboard", c.Floor, c.Outline.Select(Mirror).ToArray(), "starboard")))
        .Concat(SharedCells.Select(c => new Cell(c.Name, c.Kind, c.Group, c.Floor, c.Outline, "centre"))).ToArray();

    /// <summary>The bridge ramp (port; and its mirror): from the landing (z 2400, the bridge's deck) to the passage (z 3280, the deck).</summary>
    public const double RampTopZ = 2400, RampFootZ = 3280;

    /// <summary>The reactor's sunken well and its two ramps facing the crossing doors (port's given: x0 z0 x1 z1, high at x0).</summary>
    public static readonly P WellAt = new(6144, 8050);
    public const double WellR = 640;
    public static readonly (double X0, double Z0, double X1, double Z1) WellRampPort = (5000, 7750, 5600, 8350);
    public const double CoreR = 180;

    public enum DoorKind { Room, Passage, Crossing, Spine, Stairs }

    /// <summary>Doors (gaps cut through the bulkheads, given as their opening; thickened across to cut the wall).</summary>
    static readonly (double X0, double Z0, double X1, double Z1, DoorKind Kind)[] DoorsPort =
    [
        (2700, 5000, 2800, 5500, DoorKind.Room), (2700, 7100, 2800, 7600, DoorKind.Room), (2700, 9200, 2800, 9700, DoorKind.Room), (2700, 11700, 2800, 12200, DoorKind.Room),
        (4400, 4800, 4400, 5400, DoorKind.Passage), (4400, 7100, 4400, 7600, DoorKind.Passage), (4400, 9100, 4400, 9700, DoorKind.Passage), (4400, 11700, 4400, 12200, DoorKind.Passage),
        (1500, 6600, 2100, 6700, DoorKind.Room), (1500, 8000, 2100, 8100, DoorKind.Room), (1500, 10800, 2100, 10900, DoorKind.Room),
        (3300, 6600, 3900, 6700, DoorKind.Room), (3300, 8000, 3900, 8100, DoorKind.Room), (3300, 10800, 3900, 10900, DoorKind.Room),
        (1500, 3780, 2100, 3900, DoorKind.Room), (3100, 3780, 3700, 3900, DoorKind.Room), (4400, 3280, 4400, 3780, DoorKind.Passage),
        (5000, 4900, 5000, 5600, DoorKind.Crossing), (5000, 7700, 5000, 8400, DoorKind.Crossing), (5000, 10000, 5000, 10700, DoorKind.Crossing), (5000, 11800, 5000, 12500, DoorKind.Crossing),
    ];
    static readonly (double X0, double Z0, double X1, double Z1, DoorKind Kind)[] DoorsCentre =
    [
        (5800, 6600, 6488, 6700, DoorKind.Spine), (5800, 9400, 6488, 9500, DoorKind.Spine), (5800, 11200, 6488, 11300, DoorKind.Spine),
        (5794, 3770, 6494, 3910, DoorKind.Stairs),                    // the bridge's grand stair into the CIC: bots only
    ];

    /// <summary>Every door as a rectangle spanning its bulkhead (a door given as a line is cut 60 to each side of it).</summary>
    public static readonly (double X0, double Z0, double X1, double Z1, DoorKind Kind)[] Doors = Expand(
        DoorsPort.Concat(DoorsPort.Select(d => (Mirror(d.X1), d.Z0, Mirror(d.X0), d.Z1, d.Kind))).Concat(DoorsCentre)).ToArray();

    static IEnumerable<(double, double, double, double, DoorKind)> Expand(IEnumerable<(double X0, double Z0, double X1, double Z1, DoorKind Kind)> doors)
    {
        foreach (var d in doors)
        {
            if (d.Kind == DoorKind.Stairs) { yield return (d.X0, d.Z0, d.X1, d.Z1, d.Kind); continue; }
            double x0 = d.X0, x1 = d.X1, z0 = d.Z0, z1 = d.Z1;
            if (x1 - x0 < z1 - z0) { double mid = (x0 + x1) / 2; x0 = Math.Min(x0, mid - 60); x1 = Math.Max(x1, mid + 60); }
            else { double mid = (z0 + z1) / 2; z0 = Math.Min(z0, mid - 60); z1 = Math.Max(z1, mid + 60); }
            yield return (x0, z0, x1, z1, d.Kind);
        }
    }

    /// <summary>The bridge's grand stair: from the bridge's deck at its north edge down to the CIC's at its south.</summary>
    public static readonly (double X0, double Z0, double X1, double Z1) Stairs = (5794, 3770, 6494, 3910);

    /// <summary>The starts: port teams 0-3 (the forward hangar and main hangar are back seats, the crew deck and cargo hold front); starboard 4-7 their mirror images.</summary>
    static readonly (string Room, P At)[] StartsPort = [("Forward Hangar", new(1864, 5250)), ("Crew Deck & Mess", new(3600, 5250)), ("Main Hangar", new(1864, 9450)), ("Cargo Hold", new(3600, 9450))];
    public static readonly (string Room, P At)[] Starts = StartsPort.Concat(StartsPort.Select(s => (s.Room, Mirror(s.At)))).ToArray();

    /// <summary>Metal (where, metal/s, whose: "port", "starboard" or "shared").</summary>
    public static readonly (P At, double Income, string Side)[] Spots = SpotsList().ToArray();

    static IEnumerable<(P, double, string)> SpotsList()
    {
        var port = new List<(P, double)>();
        foreach (var (_, s) in StartsPort)
            port.AddRange([(s + new P(-380, -300), 2.0), (s + new P(380, -300), 2.0), (s + new P(0, 420), 2.0)]);
        port.AddRange([(new P(2900, 3350), 2.4), (new P(1500, 7350), 2.4), (new P(2300, 7350), 2.4), (new P(3600, 7350), 2.4), (new P(1864, 12000), 2.4), (new P(3600, 12000), 2.4)]);
        foreach (var (at, m) in port) { yield return (at, m, "port"); yield return (Mirror(at), m, "starboard"); }
        foreach (var (at, m) in new[] { (new P(5600, 2500), 3.0), (new P(5600, 5250), 3.0), (new P(5650, 7500), 3.4), (new P(5600, 10350), 3.0), (new P(5600, 12200), 3.0) })
        { yield return (at, m, "shared"); yield return (Mirror(at), m, "shared"); }
    }

    /// <summary>Geothermal vents: each power room's own, and two on the reactor well's floor.</summary>
    public static readonly (P At, string Side)[] Vents = [(new(1864, 7350), "port"), (new(Mirror(1864), 7350), "starboard"), (new(5844, 8470), "shared"), (new(Mirror(5844), 8470), "shared")];

    /// <summary>The five crossing lanes: the shared cells they run through.</summary>
    public static readonly string[] Lanes = ["Bridge", "CIC", "Main Reactor", "Shield Generator", "Engine Room"];
}
