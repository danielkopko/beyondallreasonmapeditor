using MapCore.Editing;
using MapCore.Heights;
using MapCore.Imaging;
using MapCore.Movement;
using MapCore.Rendering;

namespace MapCore.Tests;

static class PassabilityTests
{
    static MapEditSession Fresh(string name) =>
        MapEditSession.Open(MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir(name)));

    static readonly Lazy<MapEditSession> shared = new(() => Fresh("pass-shared"));

    /// <summary>The engine's rule for one square, worked out independently of PassabilityMap.</summary>
    static Passage Rule(HeightMap h, MoveClass c, int x, int z, double level)
    {
        double highest = new[] { h[x, z], h[x + 1, z], h[x, z + 1], h[x + 1, z + 1] }.Max() - level;
        if (c.Kind == MoverKind.Hover && highest < 0)
            return Passage.OnWater; // HoverSpeedMod: no slope check on water
        if (TerrainSlope.Cell(h, x / 2, z / 2) > c.MaxSlope)
            return Passage.TooSteep;
        if (c.Kind != MoverKind.Hover && -highest > c.MaxWaterDepth)
            return Passage.TooDeep;
        return Passage.Open;
    }

    static int Mismatches(PassabilityMap map, HeightMap h, MoveClass c, double level, int step = 1)
    {
        int wrong = 0;
        for (int z = 0; z < map.Height; z += step)
            for (int x = 0; x < map.Width; x += step)
                if (map[x, z] != Rule(h, c, x, z, level))
                    wrong++;
        return wrong;
    }

    /// <summary>
    /// Hovers: every square whose highest corner is under water is open to them whatever its slope; on land the
    /// 33° limit applies. Every square agrees with the rule worked out independently.
    /// </summary>
    [Test]
    public static void HoversGoAnywhereOnWater()
    {
        var s = shared.Value;
        var map = new PassabilityMap(s.Heights, BarMoveClasses.Hover);
        var counts = map.Counts();
        Report.Info("hovers on Acidic Quarry: " + string.Join(", ", counts.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value:N0}")));
        Assert.True(counts.GetValueOrDefault(Passage.OnWater) > 100_000 && counts.GetValueOrDefault(Passage.TooSteep) > 1000 &&
                    counts.GetValueOrDefault(Passage.Open) > 1000 && !counts.ContainsKey(Passage.TooDeep), "water, steep land and open land, never too deep");
        Assert.Equal(Passage.Open, map.At(1000, 900), "a plateau top (0.2°)");
        Assert.Equal(0, Mismatches(map, s.Heights, BarMoveClasses.Hover, 0), "squares that differ from the engine's rule");

        // Under water, even the steepest squares are open to hovers.
        int steepUnderWater = 0;
        for (int z = 0; z < map.Height; z += 3)
            for (int x = 0; x < map.Width; x += 3)
                if (map[x, z] == Passage.OnWater && TerrainSlope.Cell(s.Heights, x / 2, z / 2) > BarMoveClasses.Hover.MaxSlope)
                    steepUnderWater++;
        Assert.True(steepUnderWater > 1000, $"{steepUnderWater} sampled squares under water are steeper than 33° and still open to hovers");
    }

    [Test]
    public static void TheSeaLevelPreviewMovesTheWater()
    {
        var s = shared.Value;
        var at0 = new PassabilityMap(s.Heights, BarMoveClasses.Hover, 0);
        var at60 = new PassabilityMap(s.Heights, BarMoveClasses.Hover, 60);
        Assert.True(at60.Counts()[Passage.OnWater] > at0.Counts()[Passage.OnWater], "a higher sea covers more ground");
        Assert.Equal(0, Mismatches(at60, s.Heights, BarMoveClasses.Hover, 60, step: 3), "squares that differ from the rule at sea level 60");
    }

    [Test]
    public static void LandUnitsStopInDeepWater()
    {
        var s = shared.Value;
        var bot = BarMoveClasses.All.Single(c => c.Name == "BOT3");
        var map = new PassabilityMap(s.Heights, bot);
        Assert.True(map.Counts().GetValueOrDefault(Passage.TooDeep) > 1000 && !map.Counts().ContainsKey(Passage.OnWater), "bots meet water too deep, and never ride on it");
        Assert.Equal(0, Mismatches(map, s.Heights, bot, 0, step: 3), "squares that differ from the rule for bots");
    }

    /// <summary>
    /// Vehicles (27°) and land bots (54°) each follow their own limits; both stop in water deeper than 20 and never ride
    /// on it; and since bots climb more and wade as deep, every square open to vehicles is open to bots.
    /// </summary>
    [Test]
    public static void VehiclesAndBotsFollowTheirOwnLimits()
    {
        var s = shared.Value;
        var vehicles = new PassabilityMap(s.Heights, BarMoveClasses.Vehicle);
        var bots = new PassabilityMap(s.Heights, BarMoveClasses.Bot);
        Report.Info("vehicles: " + string.Join(", ", vehicles.Counts().OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value:N0}")) +
                    "; bots: " + string.Join(", ", bots.Counts().OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value:N0}")));
        Assert.Equal(0, Mismatches(vehicles, s.Heights, BarMoveClasses.Vehicle, 0, step: 3), "squares that differ from the rule for vehicles");
        Assert.True(!vehicles.Counts().ContainsKey(Passage.OnWater), "vehicles never ride on water");
        Assert.Equal(Passage.Open, vehicles.At(1000, 900), "a plateau top is open to vehicles");
        int openToVehiclesOnly = 0;
        for (int z = 0; z < vehicles.Height; z++)
            for (int x = 0; x < vehicles.Width; x++)
                if (vehicles[x, z] == Passage.Open && bots[x, z] != Passage.Open)
                    openToVehiclesOnly++;
        Assert.Equal(0, openToVehiclesOnly, "squares open to vehicles but not to bots");
        Assert.True(bots.Counts()[Passage.Open] > vehicles.Counts()[Passage.Open], "bots reach more ground");
    }

    /// <summary>After an edit only its area is worked out again, and the result equals a fresh map.</summary>
    [Test]
    public static void UpdatesFollowEdits()
    {
        var s = Fresh("pass-update");
        var map = new PassabilityMap(s.Heights, BarMoveClasses.Hover);
        WorldRect? area = null;
        s.Changed += (_, change) => area = change.Area;
        // An island in the acid: water that hovers crossed becomes a 40° mound they can't climb.
        Assert.Equal(Passage.OnWater, map.At(3072, 3072), "the middle of the acid is water");
        s.Apply("ramp:3000,3072:3144,3072:120:-20,100:40:0");
        Assert.True(area != null, "the edit reports where it changed");
        map.Update(area);
        Assert.True(map.At(3100, 3072) != Passage.OnWater, "the ramp stands out of the water");
        var fresh = new PassabilityMap(s.Heights, BarMoveClasses.Hover);
        int different = 0;
        for (int z = 0; z < map.Height; z++)
            for (int x = 0; x < map.Width; x++)
                different += map[x, z] != fresh[x, z] ? 1 : 0;
        Assert.Equal(0, different, "squares where the updated map differs from a fresh one");
    }

    /// <summary>
    /// The 3D view tints each ground pixel by its square: exactly the shaded colour blended 45% towards the square's
    /// colour; and after an edit the tint follows the new ground.
    /// </summary>
    [Test]
    public static void TheThreeDViewTintsWhereHoversGo()
    {
        var s = Fresh("pass-3d");
        using var layers = new MapRenderer(s);
        using var view = new TerrainView(layers);
        var camera = new OrbitCamera(3072, 3072, 0, 60, 2500);
        var plain = new RgbaImage(400, 300);
        var tinted = new RgbaImage(400, 300);
        view.Render(plain, camera, new TerrainOptions { Shadows = false });
        foreach (var (moveClass, expected) in new[] { (BarMoveClasses.Hover, Passage.OnWater), (BarMoveClasses.Vehicle, Passage.TooSteep), (BarMoveClasses.Bot, Passage.TooDeep) })
        {
            view.Render(tinted, camera, new TerrainOptions { Shadows = false, Passability = moveClass });
            var classMap = new PassabilityMap(s.Heights, moveClass);
            int checkedPixels = 0, wrong = 0;
            var seen = new HashSet<Passage>();
            for (int y = 0; y < 300; y += 3)
                for (int x = 0; x < 400; x += 3)
                {
                    if (view.GroundAt(x, y) is not (double gx, double gz))
                        continue;
                    var kind = classMap.At(gx, gz);
                    seen.Add(kind);
                    var tint = kind switch
                    {
                        Passage.OnWater => TerrainView.OnWaterTint,
                        Passage.TooSteep => TerrainView.TooSteepTint,
                        Passage.TooDeep => TerrainView.TooDeepTint,
                        _ => TerrainView.OpenTint,
                    };
                    var p = plain.GetPixel(x, y);
                    var t = tinted.GetPixel(x, y);
                    static bool Near(byte got, byte from, byte to) => Math.Abs(got - (from + (to - from) * 0.45)) <= 1;
                    checkedPixels++;
                    if (!Near(t.R, p.R, tint.R) || !Near(t.G, p.G, tint.G) || !Near(t.B, p.B, tint.B))
                        wrong++;
                }
            Assert.True(checkedPixels > 5000 && seen.Contains(expected) && seen.Contains(Passage.TooSteep),
                $"{moveClass.Name}: {checkedPixels} ground pixels checked, showing {expected} and steep ground ({string.Join(", ", seen)})");
            Assert.Equal(0, wrong, $"{moveClass.Name}: pixels not tinted towards their square's colour");
        }
        var map = new PassabilityMap(s.Heights, BarMoveClasses.Hover);

        // Raise a block of the acid well above water: the view's map follows the edit.
        var (ix, iy) = (200, 150);
        var (bx, bz) = view.GroundAt(ix, iy)!.Value;
        Assert.Equal(Passage.OnWater, map.At(bx, bz), "the middle of the view is acid (water)");
        s.Apply(FormattableString.Invariant($"height-flatten:{bx},{bz}:150:150:1:1"));
        view.Render(tinted, camera, new TerrainOptions { Shadows = false, Passability = BarMoveClasses.Hover });
        Assert.True(view.Passability(BarMoveClasses.Hover).At(bx, bz) == Passage.Open, "after flattening it to 150 above the sea it is open land");
    }
}
