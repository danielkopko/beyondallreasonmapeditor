// Red Dust Open Cut's layout, in game units (x east, z south; the map 12288 square). Not symmetric: Team A holds the
// west (the plant side: the ROM pad and its stockpiles, the plant pad and site offices, the tailings dam), Team B the
// east (the dump side: two terraced waste dumps, the workshop and go-line). Between them the pit: an oval of benches
// tilted a little off north-south, a haul ramp down each side, a slide on the north-east wall that bots can climb,
// the bucket wheel excavator's terrace at the north end, and a green lake in the sump at the south end of the floor.
static class Layout
{
    public const int Squares = 1536;                 // 24 × 24
    public const double Size = Squares * 8;
    public const float MinH = -80, MaxH = 620;
    public const double Plain = 380;                 // the outback, well above the water (the pit lake is at 0)

    // The pit: centre, long axis (pointing south-south-east), half-lengths at the rim; eight benches about 46 high,
    // 96-wide berms, 22-wide faces (about 65°: nothing climbs them); the floor at 10.
    public static readonly P PitCentre = new(6100, 6200);
    public static readonly P Along = new P(0.25, 1).Unit, Across = new P(1, -0.25).Unit;
    public const double PitA = 2900, PitB = 1520;
    public const double Berm = 96, Face = 22, PitFloor = 10;
    public const int Benches = 8;
    public const double BenchHeight = (Plain - PitFloor) / Benches;
    public const double Wall = Benches * (Berm + Face);
    /// <summary>The pit's southern extension: a broad lobe from the old pit's south end to the map's south edge and past
    /// it, its walls wavering, cut back at its corners by the edge, joined to the old pit by a waist.</summary>
    public static readonly P SouthCentre = new(5950, 10600);
    public const double SouthHalfX = 1900, SouthHalfZ = 2600;
    public static readonly (P At, double A, double B)[] Cutbacks = [(new(7400, 12100), 600, 500), (new(4600, 12200), 550, 450)];
    /// <summary>The pool against the map's south edge, in the pit's lowest corner (the water at 0, too deep to wade).</summary>
    public static readonly P PoolCentre = new(5950, 12288);
    public const double PoolA = 750, PoolB = 650, PoolBed = -46;

    // Angles round the pit: 0 its south end (along), 90 its east side (across), 180 the north end, 270 the west.
    public const double RampWestFrom = 206, RampWestTo = 292, RampEastFrom = 36, RampEastTo = 128, RampHalf = 80;
    // The extension's two ramps, as angles round SouthCentre (0 south, 90 east, 180 north, 270 west): each from just
    // south of the waist down its wall toward the edge.
    public const double RampSouthWestFrom = 242, RampSouthWestTo = 300, RampSouthEastFrom = 105, RampSouthEastTo = 50;
    public const double SlideFrom = 142, SlideTo = 160, SlideDegrees = 40;
    public const double TerraceFrom = 166, TerraceTo = 200, TerraceDepth = 460, TerraceLevel = Plain - BenchHeight;

    /// <summary>The two starts of each team: A in the west, B in the east.</summary>
    public static readonly P[] StartsA = [new(2200, 2850), new(1800, 7100)];
    public static readonly P[] StartsB = [new(10500, 4600), new(10400, 8500)];
    public const double BaseRadius = 560;

    // The west: the ROM pad (with ore stockpiles), the plant pad, the tailings dam.
    public static readonly P RomPad = new(3650, 4300);
    public const double RomA = 620, RomB = 440;
    /// <summary>The ROM pad's ore stockpiles, in the gaps between the roads that meet at the pad's middle.</summary>
    public static readonly P[] Stockpiles = [new(3190, 4300), new(3760, 4690), new(4050, 4050)];
    public static readonly P PlantPad = new(2500, 5450);
    public const double PlantA = 520, PlantB = 380;
    public const double TsfX0 = 500, TsfX1 = 2800, TsfZ0 = 8500, TsfZ1 = 10900, TsfWall = 44, TsfCrest = 90;
    /// <summary>The dam's decant, dried out (the map has one water level, the pit's pool at 0, so the dam, high on the
    /// plain, holds none): a shallow dip of cracked mud in the beach's far corner, ringed with salt crust.</summary>
    public static readonly P Decant = new(TsfX1 - 520, TsfZ1 - 520);
    public const double DecantA = 420, DecantB = 320, DecantDip = 6;

    // The east: two waste dumps in two lifts of 60, each with a graded haul ramp up; the workshop's hardstand.
    public static readonly (P At, double A, double B, double Turn)[] Dumps = [(new(10050, 2300), 1250, 800, 12), (new(9800, 10350), 1300, 760, -8)];
    public const double Lift = 60, LiftInset = 300;
    public static readonly P[][] DumpRamps = [[new(8500, 2900), new(8950, 2560), new(9500, 2250)], [new(8700, 9350), new(8420, 9700), new(8520, 10050), new(8950, 10250)]];
    public static readonly P Workshop = new(10450, 6500);
    public const double WorkshopA = 560, WorkshopB = 420;

    // The railway: the main line due north-south at RailX, from the north edge to the loco shed (its end) ShedToRim
    // short of the pit's rim; a passing siding east of it, joined by turnouts at both ends; the loadout over the main
    // line. Rolling stock on a north-south line tiles into a solid wall (footprints are squared to quarter turns), so
    // the parked train walls the north lane but for three gaps: at the north edge (in front of the locos), where road 7
    // crosses the line, and between the shed and the rim.
    public const double RailX = 6700, SidingX = 6775, SidingFrom = 560, SidingTo = 2520, Turnout = 220;
    public const double LoadoutZ = 1900, LocoFront = 240, ShedLength = 528, ShedToRim = 270, CrossingClear = 160;
    public const double RailBedX = (RailX + SidingX) / 2, RailBedHalf = 120;
    /// <summary>The overland conveyor: from the crusher at the ROM pad north to the transfer tower, then due east to the
    /// foot of the loadout's incline.</summary>
    public static readonly P Crusher = new(3650, 3950), Transfer = new(4300, 1900), LoadoutFoot = new(RailX - 560, LoadoutZ);
    public static readonly P Magazine = new(1600, 11800);
    public static readonly P[] Windmills = [new(1300, 4600), new(11600, 10100), new(10500, 11950)];

    /// <summary>Hardstands, levelled and gravelled (rounded rectangles: centre, half-sizes): the transfer tower; the
    /// plant's fuel tanks, water tank, water tower, comms tower and power station; the workshop's fuel farm and water
    /// tank; the windmills and their troughs; the explosives magazine inside its bund.</summary>
    public static readonly (P At, double A, double B)[] Hardstands =
    [
        (Transfer, 70, 70), (new(3310, 5200), 170, 140), (new(1820, 5250), 95, 95), (new(2050, 5900), 55, 55), (new(1730, 5600), 90, 55), (new(2300, 4985), 190, 50),
        (new(11315, 6415), 185, 235), (new(11260, 6820), 95, 95),
        (Windmills[0] + new P(30, 0), 80, 45), (Windmills[1] + new P(30, 0), 80, 45), (Windmills[2] + new P(30, 0), 80, 45),
        (Magazine, 90, 90),
    ];

    // Breakaways: low mesas with red scarps (bots climb them, vehicles go round).
    public static readonly (P At, double A, double B, double Height)[] Breakaways = [(new(2950, 11500), 600, 340, 70), (new(11300, 1100), 700, 380, 70), (new(4400, 900), 520, 300, 60)];

    // Dry creeks with sandy beds and gums along them: one down each side of the map, the west's and the east's.
    public static readonly P[][] Creeks =
    [
        [new(400, -40), new(900, 2500), new(500, 5200), new(300, 7600), new(-40, 8200)],
        [new(12330, 3300), new(11950, 4400), new(12020, 5800), new(11820, 7300), new(11950, 8800), new(11650, 10200), new(11450, 11400), new(11350, 12330)],
    ];

    /// <summary>Haul roads (kept clear, drawn on the ground), from each ramp's top to each side's pads, starts and dumps, round the
    /// pit. A road meets a pit ramp's top in line with the ramp (road 7's start is the west ramp's top, road 8's end the
    /// south-west's, road 11 passes the south-east's, road 12 is the east's spur), and a road joining another ends inside it.</summary>
    public static readonly P[][] Roads =
    [
        [new(3650, 4300), new(4300, 4150), new(4600, 3560), new(4697, 3507)],
        [new(1800, 7100), new(2500, 5450), new(3650, 4300)],
        [new(10500, 4600), new(9300, 4800), new(8000, 5000)],
        [new(10400, 8500), new(10450, 6500), new(10500, 4600)],
        [new(9300, 4800), new(8700, 3400), new(8500, 2900)],
        [new(10400, 8500), new(9300, 9000), new(8700, 9350)],
        [new(4770, 3755), new(4700, 2900), new(6000, 2700), new(7400, 3100), new(8000, 5000)],
        [new(4300, 7600), new(4250, 8500), new(4200, 9320)],
        [new(2500, 5450), new(3600, 6400), new(4300, 7600)],
        [new(1800, 7100), new(1680, 8290)],
        [new(8000, 5000), new(7960, 7700), new(7930, 8350), new(7720, 8520), new(7860, 8950), new(8060, 9400), new(7930, 9870)],
        [new(7720, 8520), new(7615, 8375)],
    ];

    /// <summary>Metal: four at each start, two on each side's stockpile pad, one halfway down each ramp, four on the pit floor.</summary>
    public static readonly (P At, double Income, string Side)[] Spots =
    [
        (new(1900, 2600), 2.0, "A"), (new(2500, 2550), 2.0, "A"), (new(1900, 3150), 2.0, "A"), (new(2550, 3150), 2.0, "A"),
        (new(1500, 6850), 2.0, "A"), (new(2100, 6850), 2.0, "A"), (new(1500, 7400), 2.0, "A"), (new(2100, 7400), 2.0, "A"),
        (new(10200, 4350), 2.0, "B"), (new(10800, 4350), 2.0, "B"), (new(10200, 4900), 2.0, "B"), (new(10800, 4900), 2.0, "B"),
        (new(10100, 8250), 2.0, "B"), (new(10700, 8250), 2.0, "B"), (new(10100, 8800), 2.0, "B"), (new(10700, 8800), 2.0, "B"),
        (new(3500, 4500), 2.6, "A"), (new(3900, 4250), 2.6, "A"),
        (new(9100, 6000), 2.6, "B"), (new(9450, 6350), 2.6, "B"),
    ];

    /// <summary>Spots placed relative to the pit once it is laid out (halfway down each ramp, and on the floor).</summary>
    public const double RampSpotIncome = 2.8, FloorSpotIncome = 3.4;
    /// <summary>The two spots on the extension's floor (shared).</summary>
    public static readonly P[] SouthFloorSpots = [new(5550, 10700), new(6350, 10700)];
    /// <summary>The two bucket wheel excavators on the lobe's floor: where each works its wall (z, the west one then the
    /// east one), how far over the rim their mast tops stand, how far into its wall each stands from the floor's edge,
    /// and the half-angle of the arc each has cut, slewing its wheel along the face.</summary>
    public static readonly double[] PitWheelZ = [8750, 9450];
    public const double PitWheelAboveRim = 30, PitWheelInto = 60, PitWheelSlew = 38;
}
