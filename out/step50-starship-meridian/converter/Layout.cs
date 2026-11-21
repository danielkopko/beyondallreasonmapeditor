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
    /// <summary>The thruster platforms on the engine housings (1.1): 100 over the bulkheads, for long-range guns.</summary>
    public const double SternDeck = 440;
    /// <summary>
    /// The outer walk (1.1): a narrow path round the hull's outer edge, at the bridge's height (40 over the bulkheads, 60
    /// under the thruster platforms), and the bow's upper front ahead of the bridge. Units reach it from the step part way
    /// up each thruster ramp, by the narrow paths round the platform (<see cref="StepPath"/>), and from the bridge by its front opening; it looks down on the rooms but has no way down into
    /// them. It's every hull point not in a room within WalkBand of the outline (or ahead of BowFrontZ), clear of the
    /// rooms by 50 for their walls; the rim beside it, where the rooms' walls meet it, stands 40 under it (Rim).
    /// </summary>
    public const double WalkH = BridgeDeck, WalkBand = 96, BowFrontZ = 1450;
    /// <summary>How far a raised floor's walls stand over it (1.1): the least that keeps bots (54°) off them, low enough for
    /// long-range guns to fire over. The bridge's walls are its lip; the thruster platforms have none (<see cref="Open"/>).</summary>
    public const double Lip = 30;
    public const double Bulkhead = 340, Rim = WalkH - 40, BridgeWalls = BridgeDeck + Lip, Space = 20;
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
    /// <summary>The engine housings at the stern, their engine bells over space. 1.1: each carries a thruster platform.</summary>
    public static readonly (double X0, double Z0, double X1, double Z1)[] EngineHousings = [(1600, 13200, 3200, 14000), (5244, 13200, 7044, 14000), (Mirror(3200), 13200, Mirror(1600), 14000)];

    public enum Kind { Start, Team, Passage, Shared, Ramp, Landing, Platform, Step }

    /// <summary>A cell: its name, kind, the group whose cells don't wall each other off, its floor's height, its outline.</summary>
    public sealed record Cell(string Name, Kind Kind, string Group, double Floor, P[] Outline, string Side);

    static P[] Rect(double x0, double z0, double x1, double z1) => [new(x0, z0), new(x1, z0), new(x1, z1), new(x0, z1)];

    /// <summary>How wide the paths from a thruster step to the outer walk are (1.1): narrow, a way in, not a corridor.</summary>
    public const double StepPathWidth = 128;
    /// <summary>The thruster steps' landing (1.1), part way up their ramps; the rooms' aft edge (the platforms' fore edge);
    /// the hull's stern line, where the outer walk runs.</summary>
    public const double StepZ = 12700, AftZ = 13072, SternZ = 13200;

    /// <summary>
    /// A thruster step (1.1): the landing across its ramp at StepZ, and from its sides a path StepPathWidth wide that skirts
    /// the ramp's upper part back to the platform's fore edge, along it round the platform's corners, and aft beside it to the
    /// outer walk at the stern line. The ramp's upper part (listed before it) takes the middle. A path that would leave a slot
    /// of the room's floor beside it runs to the room's wall instead (rightTo).
    /// </summary>
    static P[] StepPath(double rampX0, double rampX1, double platX0, double platX1, double? rightTo = null)
    {
        double s = StepPathWidth, l = rampX0 - s, r = rightTo ?? rampX1 + s, band = AftZ - s;
        return [new(l, StepZ), new(r, StepZ), new(r, band), new(platX1 + s, band), new(platX1 + s, SternZ), new(platX1, SternZ), new(platX1, AftZ),
                new(platX0, AftZ), new(platX0, SternZ), new(platX0 - s, SternZ), new(platX0 - s, band), new(l, band)];
    }

    static readonly (string Name, Kind Kind, string Group, double Floor, P[] Outline)[] TeamCellsPort =
    [
        ("Weapons Bay", Kind.Team, "weapons", Deck, [new(2300, 2700), new(4400, 2700), new(4400, 3780), new(1080, 3780)]),
        ("Forward Hangar", Kind.Start, "fwdhangar", Deck, Rect(1028, 3900, 2700, 6600)),
        ("Crew Deck & Mess", Kind.Start, "crew", Deck, Rect(2800, 3900, 4000, 6600)),
        ("Power Room", Kind.Team, "power", Deck, Rect(1028, 6700, 2700, 8000)),
        ("Armory", Kind.Team, "armory", Deck, Rect(2800, 6700, 4000, 8000)),
        ("Main Hangar", Kind.Start, "mainhangar", Deck, Rect(1028, 8100, 2700, 10800)),
        ("Cargo Hold", Kind.Start, "cargo", Deck, Rect(2800, 8100, 4000, 10800)),
        // 1.1: the thruster platform on the outboard engine housing, 100 over the bulkheads and open (no wall over it, for
        // long-range guns), the outer walk round it. Its ramp rises out of the thruster bay's floor (no walls beside it) to
        // a step at the outer walk's height and on up to the platform. From the step's sides a narrow path (the walk's way
        // in) skirts the ramp's upper part and the platform's fore edge, round its corners to the outer walk; the right one
        // crosses the bulkhead into the machine shop's aft corner, so the shop is in the bay's group (no bulkhead between it
        // and the path: a sheer drop, as from the path to the bay).
        ("Thruster Ramp Top", Kind.Ramp, "thruster", WalkH, Rect(1900, 12800, 2500, 13072)),
        ("Thruster Step", Kind.Step, "thruster", WalkH, StepPath(1900, 2500, 1696, 3104, 2700)),
        ("Thruster Ramp", Kind.Ramp, "thruster", Deck, Rect(1900, 11700, 2500, 12700)),
        ("Thruster Platform", Kind.Platform, "thruster", SternDeck, Rect(1696, 13072, 3104, 13904)),
        ("Thruster Bay", Kind.Team, "thruster", Deck, Rect(1028, 10900, 2700, 13072)),
        ("Machine Shop", Kind.Team, "thruster", Deck, Rect(2800, 10900, 4000, 13072)),
        // The passage, its bridge ramp and the ramp's landing are one way with the bridge itself: no bulkheads between them.
        // 1.1: 1,000 wide below the weapons bay (it was 600), the inboard rooms 400 narrower.
        ("Passage", Kind.Passage, "bridgeway", Deck, [new(4400, 3280), new(5000, 3280), new(5000, 13072), new(4000, 13072), new(4000, 3900), new(4400, 3900)]),
        ("Bridge Ramp", Kind.Ramp, "bridgeway", Deck, Rect(4400, 2400, 5000, 3280)),
        ("Bridge Landing", Kind.Landing, "bridgeway", BridgeDeck, Rect(4400, 1900, 5000, 2400)),
    ];
    static readonly (string Name, Kind Kind, string Group, double Floor, P[] Outline)[] SharedCells =
    [
        ("Bridge", Kind.Shared, "bridgeway", BridgeDeck, [new(5200, 1500), new(7088, 1500), new(7288, 1800), new(7288, 3780), new(5000, 3780), new(5000, 1800)]),
        // 1.1: the bridge's front opening onto the bow's upper front and the outer walk, where the viewport was (level: the
        // walk is at the bridge's height).
        ("Front Opening", Kind.Landing, "bridgeway", BridgeDeck, Rect(5844, 1300, 6444, 1500)),
        ("CIC", Kind.Shared, "cic", Deck, Rect(5000, 3900, 7288, 6600)),
        ("Main Reactor", Kind.Shared, "reactor", Deck, Rect(5000, 6700, 7288, 9400)),
        ("Shield Generator", Kind.Shared, "shield", Deck, Rect(5000, 9500, 7288, 11200)),
        // 1.1: the centre engine housing's thruster platform, as the others: its ramp rises out of the engine room's floor to
        // a step at the outer walk's height and on up to the platform, the step's paths round the platform to the walk.
        ("Engine Ramp Top", Kind.Ramp, "engine", WalkH, Rect(5844, 12800, 6444, 13072)),
        ("Engine Step", Kind.Step, "engine", WalkH, StepPath(5844, 6444, 5340, 6948)),
        ("Engine Ramp", Kind.Ramp, "engine", Deck, Rect(5844, 12000, 6444, 12700)),
        ("Centre Thruster Platform", Kind.Platform, "engine", SternDeck, Rect(5340, 13072, 6948, 13904)),
        ("Engine Room", Kind.Shared, "engine", Deck, Rect(5000, 11300, 7288, 13072)),
    ];

    /// <summary>Every cell, port's, starboard's (mirrored, their groups suffixed) and the centreline's.</summary>
    public static readonly Cell[] Cells =
        TeamCellsPort.Select(c => new Cell(c.Name, c.Kind, c.Group == "bridgeway" ? c.Group : c.Group + ".port", c.Floor, c.Outline, "port"))
        .Concat(TeamCellsPort.Select(c => new Cell(c.Name, c.Kind, c.Group == "bridgeway" ? c.Group : c.Group + ".starboard", c.Floor, c.Outline.Select(Mirror).ToArray(), "starboard")))
        .Concat(SharedCells.Select(c => new Cell(c.Name, c.Kind, c.Group, c.Floor, c.Outline, "centre"))).ToArray();

    /// <summary>The bridge ramp (port; and its mirror): from the landing (z 2400, the bridge's deck) to the passage (z 3280, the deck).</summary>
    public const double RampTopZ = 2400, RampFootZ = 3280;

    /// <summary>Every ramp by its cell's name: where it is at its top's height and where at its foot's.</summary>
    public static readonly (string Name, double TopZ, double FootZ, double TopH, double FootH)[] Ramps =
        [("Bridge Ramp", RampTopZ, RampFootZ, BridgeDeck, Deck),
         ("Thruster Ramp", 12700, 11700, WalkH, Deck), ("Thruster Ramp Top", 13072, 12800, SternDeck, WalkH),
         ("Engine Ramp", 12700, 12000, WalkH, Deck), ("Engine Ramp Top", 13072, 12800, SternDeck, WalkH)];

    /// <summary>The thruster platforms, their ramps and steps (1.1) stand open: no wall over them, so long-range guns fire over
    /// their edges; their edges are sheer drops to the rooms below and the outer walk.</summary>
    public static bool Open(Cell c) => c.Kind is Kind.Platform or Kind.Step || c.Name is "Thruster Ramp" or "Thruster Ramp Top" or "Engine Ramp" or "Engine Ramp Top";

    /// <summary>Cells the outer walk runs right up to (the open ones, and the bridge's front opening); it keeps 50 from the rest.</summary>
    public static bool JoinsWalk(Cell c) => Open(c) || c.Name == "Front Opening";

    /// <summary>The reactor's sunken well and its two ramps facing the crossing doors (port's given: x0 z0 x1 z1, high at x0).</summary>
    public static readonly P WellAt = new(6144, 8050);
    public const double WellR = 640;
    public static readonly (double X0, double Z0, double X1, double Z1) WellRampPort = (5000, 7750, 5600, 8350);
    public const double CoreR = 180;

    public enum DoorKind { Room, Passage, Crossing, Spine, Stairs }

    /// <summary>Doors (gaps cut through the bulkheads, given as their opening; thickened across to cut the wall).</summary>
    static readonly (double X0, double Z0, double X1, double Z1, DoorKind Kind)[] DoorsPort =
    [
        (2700, 5000, 2800, 5500, DoorKind.Room), (2700, 7100, 2800, 7600, DoorKind.Room), (2700, 9200, 2800, 9700, DoorKind.Room), (2700, 11200, 2800, 11700, DoorKind.Room),
        (4000, 4800, 4000, 5400, DoorKind.Passage), (4000, 7100, 4000, 7600, DoorKind.Passage), (4000, 9100, 4000, 9700, DoorKind.Passage), (4000, 11700, 4000, 12200, DoorKind.Passage),
        (1500, 6600, 2100, 6700, DoorKind.Room), (1500, 8000, 2100, 8100, DoorKind.Room), (1500, 10800, 2100, 10900, DoorKind.Room),
        (3100, 6600, 3700, 6700, DoorKind.Room), (3100, 8000, 3700, 8100, DoorKind.Room), (3100, 10800, 3700, 10900, DoorKind.Room),
        (1500, 3780, 2100, 3900, DoorKind.Room), (3100, 3780, 3700, 3900, DoorKind.Room), (4400, 3280, 4400, 3780, DoorKind.Passage),
        // 1.1: the crossings wider, to open the middle up (the CIC's kept at 700, below the bridge ramps).
        (5000, 4900, 5000, 5600, DoorKind.Crossing), (5000, 7300, 5000, 8800, DoorKind.Crossing), (5000, 9650, 5000, 11050, DoorKind.Crossing), (5000, 11500, 5000, 12650, DoorKind.Crossing),
    ];
    static readonly (double X0, double Z0, double X1, double Z1, DoorKind Kind)[] DoorsCentre =
    [
        (5444, 6600, 6844, 6700, DoorKind.Spine), (5444, 9400, 6844, 9500, DoorKind.Spine), (5444, 11200, 6844, 11300, DoorKind.Spine),   // 1.1: 1,400 wide (were 688)
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

    /// <summary>
    /// The starts (1.1, at your call: 8v8). Eight a side, all in the outboard rooms along the hull's wall, the back line:
    /// the air start in the weapons bay; two in the forward hangar, on its landing circles; the tech start in the power room
    /// by its geothermal vent; two in the main hangar, on its landing circles; two in the thruster bay, the aft one
    /// outboard of the ramp (a zig-zag). None in the inboard rooms. Port teams 0-7 in this order, starboard 8-15 their
    /// mirror images. Each start's floor carries a circle (its centre, its radius): the hangars' landing circles, and the
    /// same etched for the others; the tech start's is centred on its vent, the start beside the vent inside it. Each
    /// start's role is stencilled on its circle (at your call: one air, one tech, one support, the rest front); the support
    /// start is the thruster bay's aft one, outboard of the ramp, the most sheltered seat.
    /// </summary>
    static readonly (string Room, string Role, P At, P Circle, double R)[] StartsPort =
    [
        ("Weapons Bay", "air", new(2400, 3240), new(2400, 3240), 300),
        ("Forward Hangar", "front", new(1864, 4575), new(1864, 4575), 330),
        ("Forward Hangar", "front", new(1864, 5925), new(1864, 5925), 330),
        ("Power Room", "tech", new(1864, 7580), new(1864, 7350), 330),
        ("Main Hangar", "front", new(1864, 8775), new(1864, 8775), 330),
        ("Main Hangar", "front", new(1864, 10125), new(1864, 10125), 330),
        ("Thruster Bay", "front", new(1864, 11280), new(1864, 11280), 300),
        ("Thruster Bay", "support", new(1430, 12420), new(1430, 12420), 300),
    ];
    public static readonly (string Room, P At)[] Starts =
        StartsPort.Select(s => (s.Room, s.At)).Concat(StartsPort.Select(s => (s.Room, Mirror(s.At)))).ToArray();
    /// <summary>Every start's floor circle, port's and starboard's: its role, centre and radius.</summary>
    public static readonly (string Role, P Circle, double R)[] StartCircles =
        StartsPort.Select(s => (s.Role, s.Circle, s.R)).Concat(StartsPort.Select(s => (s.Role, Mirror(s.Circle), s.R))).ToArray();

    /// <summary>
    /// Metal (1.1): a metal map. The whole ship reads as metal, so every metal cell (16 × 16) whose centre is deck floor
    /// holds raw metal; walls, hull tops and space hold none. The values are Cloud9_V2's (a certified BAR metal map, also
    /// structures in space): raw 79, maxMetal 7.5, and an extractor radius of 24, half an extractor's width, so
    /// extractors packed side by side don't share metal.
    /// </summary>
    public const byte MetalRaw = 79;
    public const double MaxMetal = 7.5, ExtractorRadius = 24;

    /// <summary>Geothermal vents: each power room's own, and two on the reactor well's floor.</summary>
    public static readonly (P At, string Side)[] Vents = [(new(1864, 7350), "port"), (new(Mirror(1864), 7350), "starboard"), (new(5844, 8470), "shared"), (new(Mirror(5844), 8470), "shared")];

    /// <summary>The five crossing lanes: the shared cells they run through.</summary>
    public static readonly string[] Lanes = ["Bridge", "CIC", "Main Reactor", "Shield Generator", "Engine Room"];
}
