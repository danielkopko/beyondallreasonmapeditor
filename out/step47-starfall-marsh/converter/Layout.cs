// Starfall Marsh's layout, in game units (x east, z south; the map 8192 square). Team A holds the north, Team B the
// south; the ship lies along the middle line, which the map mirrors across. It came in from the west, ploughing a
// furrow (now a reed-choked marsh), broke its back (the breach), and buried its nose in the dirt it pushed up (the
// mound). Each half is laid out here for the north; Mirror gives the south.
static class Layout
{
    public const int Squares = 1024;                 // 16 × 16
    public const double Size = Squares * 8, Axis = Size / 2;
    public const float MinH = -100, MaxH = 620;
    public const double Ground = 40;                 // the jungle floor
    public const double Water = 0;

    // The furrow: along the axis from beyond the west edge to the mound, its floor under shallow marsh water.
    public const double FurrowFloor = -14, FurrowHalf = 430, FurrowBank = 380, BermCrest = 760, BermHeight = 70, BermHalf = 300;
    public const double FurrowEast = 5200;

    // The ship: model x runs east. The aft section from the tail to its torn front, the breach, then the fore section
    // pitched nose-down into the mound.
    public const double Tail = 1500, AftEngines = 1860, AftHull = 2560, AftEnd = 2960, ForeStart = 3440, ForeBreak = 3840, NoseEnd = 5060;
    public const double Pitch = 4.0;                 // degrees, the fore section, nose down

    // The mound: pushed-up dirt, steep on its west face where the nose went in, long and gentle to the east.
    public static readonly P MoundTop = new(5700, Axis);
    public const double MoundWest = 820, MoundEast = 1750, MoundAcross = 1700, MoundPeak = 520;
    /// <summary>A slip of slumped earth up the mound's north-east flank, graded for vehicles, to the spot on its top.</summary>
    public static readonly P[] MoundTrack = [new(7150, 2850), new(6760, 3260), new(6320, 3540), new(5900, 3690)];
    public const double TrackHalf = 70;

    // The lanes north to south, for the checks and to keep clear: west of the tail across the marsh, the breach, the
    // mound and round it.
    public static readonly (string Name, double X0, double X1)[] Lanes =
        [("west, across the marsh", 0, Tail - 60), ("the breach", AftEnd + 20, ForeStart - 20), ("east, over and round the mound", NoseEnd + 40, Size)];

    /// <summary>The two starts of the north team: by the engines (west) and by the mound (east).</summary>
    public static readonly P[] Starts = [new(1250, 1150), new(6700, 1150)];

    /// <summary>Metal spots in the north half: four round each start, and the wreck's (engines, breach, mound).</summary>
    public static readonly (P At, double Income, string Kind)[] Spots =
    [
        (new(950, 950), 2.0, "base"), (new(1560, 920), 2.0, "base"), (new(940, 1430), 2.0, "base"), (new(1580, 1420), 2.0, "base"),
        (new(6400, 930), 2.0, "base"), (new(7010, 940), 2.0, "base"), (new(6390, 1420), 2.0, "base"), (new(7000, 1440), 2.0, "base"),
        (new(1120, 3330), 2.8, "wreck"), (new(3540, 3320), 2.8, "wreck"), (new(5820, 3720), 2.8, "wreck"),
    ];

    /// <summary>Geothermal vents in the north half: where the reactor spilled at the break.</summary>
    public static readonly P[] Vents = [new(2760, 3330)];

    /// <summary>Marsh streams draining into the furrow from the north (their beds shallow enough to wade), and pools.</summary>
    public static readonly P[][] Streams =
    [
        [new(420, -40), new(560, 700), new(380, 1500), new(640, 2400), new(820, 3300), new(900, 3700)],
        [new(3900, -40), new(3700, 800), new(3420, 1600), new(3600, 2400), new(3250, 3100), new(3200, 3700)],
    ];
    public static readonly (P At, double Rx, double Rz, double Depth)[] Pools =
    [
        (new(2300, 2100), 420, 280, 12), (new(4700, 2500), 360, 250, 10), (new(2400, 600), 260, 200, 9), (new(5200, 1600), 240, 180, 26),
        (new(7650, 2900), 300, 420, 12), (new(1900, 2900), 220, 160, 26),
    ];

    /// <summary>Trails the jungle is kept off: from each start to each lane.</summary>
    public static readonly P[][] Trails =
    [
        [new(1250, 1150), new(1000, 2000), new(800, 3000), new(700, 3700), new(700, 4096)],
        [new(1250, 1150), new(2100, 1700), new(2800, 2600), new(3200, 3500), new(3200, 4096)],
        [new(6700, 1150), new(5300, 1900), new(4200, 2700), new(3300, 3500)],
        [new(6700, 1150), new(7200, 2200), new(7500, 3300), new(7550, 4096)],
        [new(6700, 1150), new(6950, 2200), new(7150, 2850), new(6760, 3260), new(6320, 3540), new(5900, 3690)],
        [new(1250, 1150), new(4000, 1100), new(6700, 1150)],
    ];

    public const double BaseRadius = 520;
}
