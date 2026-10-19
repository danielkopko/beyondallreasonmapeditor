using System.Globalization;
using MapCore.Editing;
using MapCore.Heights;
using MapCore.Movement;

namespace MapCore.Tests;

static class MovementTests
{
    static MapEditSession Fresh(string name) =>
        MapEditSession.Open(MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir(name)));

    static GroupVerdict Verdict(IReadOnlyList<GroupVerdict> verdicts, string group) => verdicts.Single(v => v.Group == group);

    /// <summary>A rise over a run that gives <paramref name="degrees"/>.</summary>
    static (double Length, double Rise) Slope(double degrees, double length = 400) => (length, length * Math.Tan(degrees * Math.PI / 180));

    /// <summary>
    /// Each class's limit is what the engine makes of BAR's numbers: movedefs.lua stores degrees ÷ 1.5, and
    /// DegreesToMaxSlope takes 1 − cos(clamp(stored, 0, 60) × 1.5).
    /// </summary>
    [Test]
    public static void LimitsFollowTheEngineConversion()
    {
        static double Engine(double written)
        {
            double stored = written / 1.5;
            return 1 - Math.Cos(Math.Clamp(stored, 0, 60) * 1.5 * Math.PI / 180);
        }
        foreach (var c in BarMoveClasses.All)
            Assert.True(Math.Abs(c.MaxSlope - Engine(c.MaxSlopeDegrees)) < 1e-12, $"{c.Name}: {c.MaxSlope} vs {Engine(c.MaxSlopeDegrees)}");
        Assert.True(Math.Abs(BarMoveClasses.All.Single(c => c.Name == "TANK3").MaxSlope - 0.1090) < 1e-4, "a vehicle's limit is 1 - cos 27°");
        Assert.True(Math.Abs(BarMoveClasses.All.Single(c => c.Name == "BOT3").MaxSlope - 0.4122) < 1e-4, "a bot's limit is 1 - cos 54°");
        Assert.True(Math.Abs(BarMoveClasses.All.Single(c => c.Name == "TBOT3").MaxSlope - 1) < 1e-12, "spiders climb anything (1 - cos 90°)");
        Assert.True(BarMoveClasses.Groups.All(g => BarMoveClasses.All.Count(c => c.Group == g) > 0 &&
                                                   BarMoveClasses.All.Where(c => c.Group == g).Select(c => c.MaxSlopeDegrees).Distinct().Count() == 1),
            "every group has classes, all with the same limit");
    }

    /// <summary>On a built ramp the engine's slope measure, computed as ReadMap.cpp does, is 1 − cos(angle) everywhere on it.</summary>
    [Test]
    public static void ARampsSurfaceHasTheSlopeOfItsAngle()
    {
        var s = Fresh("move-plane");
        foreach (double turn in new[] { 0.0, 30.0 })
        {
            double a = turn * Math.PI / 180;
            var (length, rise) = Slope(20, 480);
            var ramp = new Ramp(3000, 2900, 3000 + length * Math.Cos(a), 2900 + length * Math.Sin(a), 160, 40);
            s.BuildRamp(ramp, 0, rise);
            double expected = TerrainSlope.OfAngle(20), worst = 0;
            int cells = 0;
            for (int cz = 0; cz < s.Heights.Height / 2; cz++)
                for (int cx = 0; cx < s.Heights.Width / 2; cx++)
                {
                    // Cells whose every corner is well on the ramp.
                    bool inside = true;
                    for (int dz = 0; dz <= 2 && inside; dz += 2)
                        for (int dx = 0; dx <= 2 && inside; dx += 2)
                        {
                            var (u, v) = ramp.Local((2 * cx + dx) * 8.0, (2 * cz + dz) * 8.0);
                            inside = u > 8 && u < ramp.Length - 8 && Math.Abs(v) < ramp.Width / 2 - 8;
                        }
                    if (!inside)
                        continue;
                    cells++;
                    worst = Math.Max(worst, Math.Abs(TerrainSlope.Cell(s.Heights, cx, cz) - expected));
                }
            Assert.True(cells > 100 && worst < 1e-4, $"turned {turn}°: {cells} cells on the ramp all measure 1 - cos 20° (worst off by {worst:0.000000})");
            s.Undo();
        }
        var flat = new Ramp(1000, 3000, 1300, 3000, 100, 40);
        s.BuildRamp(flat, 50, 50);
        Assert.True(TerrainSlope.Cell(s.Heights, 1150 / 16, 3000 / 16) < 1e-6, "a level ramp measures 0");
    }

    [Test]
    public static void VerdictsFollowEachGroupsLimit()
    {
        var (l20, r20) = Slope(20);
        var v20 = RampPassability.Check(l20, 0, r20);
        Assert.True(v20.All(v => v.Climbs), "at 20° everyone climbs it");
        double botSpeed = 1 / (1 + (1 - Math.Cos(20 * Math.PI / 180)) * 4);
        Assert.True(Math.Abs(Verdict(v20, BarMoveClasses.Bots).SlowestUphill - botSpeed) < 1e-9, $"bots go up at {botSpeed:P0}");
        var vehicles20 = Verdict(v20, BarMoveClasses.Vehicles);
        Assert.True(vehicles20.SlowestUphill < vehicles20.FastestUphill, "heavier vehicles slow more (slopeMod 18 to 36)");

        var (l30, r30) = Slope(30);
        var v30 = RampPassability.Check(l30, r30, 0); // downhill from the first end: the same ramp
        var vehicles30 = Verdict(v30, BarMoveClasses.Vehicles);
        Assert.True(!vehicles30.Climbs && Math.Abs(vehicles30.NeededLength!.Value - r30 / Math.Tan(27 * Math.PI / 180)) < 1e-6,
            $"at 30° vehicles can't, and need {vehicles30.NeededLength:0} instead of {l30}");
        Assert.True(Verdict(v30, BarMoveClasses.HeavyTank).Climbs && Verdict(v30, BarMoveClasses.Hovers).Climbs && Verdict(v30, BarMoveClasses.Bots).Climbs,
            "at 30° armthor (33°), hovers (33°) and bots (54°) can");

        var (l40, r40) = Slope(40);
        var v40 = RampPassability.Check(l40, 0, r40);
        Assert.True(!Verdict(v40, BarMoveClasses.HeavyTank).Climbs && !Verdict(v40, BarMoveClasses.Hovers).Climbs &&
                    Verdict(v40, BarMoveClasses.Bots).Climbs && Verdict(v40, BarMoveClasses.Amphibious).Climbs, "at 40° only the 54° groups and spiders");

        var (l60, r60) = Slope(60);
        var v60 = RampPassability.Check(l60, 0, r60);
        Assert.True(v60.Where(v => v.Climbs).Select(v => v.Group).SequenceEqual([BarMoveClasses.AllTerrain]), "at 60° only spiders and all-terrain bots");

        var (l26, r26) = Slope(26.5);
        string close = RampPassability.Describe(l26, 200, 0, r26);
        Assert.True(close.Contains("✓ Vehicles (27°)", StringComparison.Ordinal) && close.Contains("only just", StringComparison.Ordinal),
            $"26.5° is flagged as only just within the vehicles' 27°: {close}");
    }

    [Test]
    public static void HoversRideOverWaterWhateverItsSlope()
    {
        var v = RampPassability.Check(100, -80, -10); // 35°, all under water
        var hovers = Verdict(v, BarMoveClasses.Hovers);
        Assert.True(hovers.Climbs && hovers.SlowestUphill == 1, "hovers cross it at full speed");
        Assert.True(!Verdict(v, BarMoveClasses.Vehicles).Climbs, "vehicles still can't");
        var land = Verdict(RampPassability.Check(100, -30, 40), BarMoveClasses.Hovers); // 35°, partly on land
        Assert.True(!land.Climbs, "partly on land, the slope stops hovers");
    }

    [Test]
    public static void NotesOnDepthAndWidth()
    {
        string deep = RampPassability.Describe(500, 200, 200, -44);
        Assert.True(deep.Contains("44 under water", StringComparison.Ordinal) && deep.Contains("stop at 20 deep", StringComparison.Ordinal), deep);
        string narrow = RampPassability.Describe(500, 40, 0, 50);
        Assert.True(narrow.Contains("corjugg", StringComparison.Ordinal) && narrow.Contains("56 units", StringComparison.Ordinal), narrow);
        string plain = RampPassability.Describe(500, 200, 0, 50);
        Assert.True(!plain.Contains("under water", StringComparison.Ordinal) && !plain.Contains("Narrower", StringComparison.Ordinal), plain);
    }

    /// <summary>Two level areas on Acidic Quarry: height 0 up to x = 3000 and height 150 from x = 3200 (around z = 3072).</summary>
    static MapEditSession TwoLevels(string name)
    {
        var s = Fresh(name);
        s.Apply("height-flatten:2600,3072:400:0:1:1");
        s.Apply("height-flatten:3500,3072:300:150:1:1");
        return s;
    }

    /// <summary>The ramp operation reports who can climb it, so MapTool shows it too.</summary>
    [Test]
    public static void TheRampResultSaysWhoCanClimbIt()
    {
        var s = TwoLevels("move-result");
        double length = 150 / Math.Tan(30 * Math.PI / 180);
        string result = s.Apply(string.Create(CultureInfo.InvariantCulture, $"ramp:2950,3072:{2950 + length},3072:160:0,150:40:0"));
        Report.Info(result);
        Assert.True(result.Contains("too steep for Vehicles", StringComparison.Ordinal) && result.Contains("climbable by Bots and commanders · ", StringComparison.Ordinal) &&
                    !result.Contains("meets the ground", StringComparison.Ordinal), result);
        // A level ramp on the level ground: nothing is too steep, and the result doesn't say "too steep for none".
        string gentle = s.Apply("ramp:2400,3072:2700,3072:160:auto,auto:40:0");
        Report.Info(gentle);
        Assert.True(!gentle.Contains("too steep", StringComparison.Ordinal), $"nothing too steep, so no 'too steep for none': {gentle}");
    }

    /// <summary>
    /// Where a ramp meets the ground is measured as the engine would see it: a ramp with each end on level ground at its
    /// height gets no warning, one whose end is set well below the ground beyond it is flagged there, and what the
    /// preview predicts is what the built heights measure.
    /// </summary>
    [Test]
    public static void WhereARampMeetsTheGroundIsMeasuredToo()
    {
        var s = TwoLevels("move-lips");
        var good = new Ramp(2600, 3072, 3500, 3072, 160, 40);
        var (goodStart, goodEnd) = s.RampLipDegrees(good, 0, 150);
        double plane = good.SlopeDegrees(0, 150);
        Assert.True(goodStart <= plane + 0.5 && goodEnd <= plane + 0.5, $"ends on level ground at their heights: no steeper than the ramp ({goodStart:0.#}°, {goodEnd:0.#}° vs {plane:0.#}°)");
        string goodText = RampPassability.Describe(good.Length, good.Width, 0, 150, goodStart, goodEnd);
        Assert.True(goodText.Contains("✓ Vehicles", StringComparison.Ordinal) && !goodText.Contains("meets the ground", StringComparison.Ordinal), goodText);

        var bad = new Ramp(2600, 3072, 3350, 3072, 160, 40); // its end 130 below the level ground beyond it
        var (badStart, badEnd) = s.RampLipDegrees(bad, 0, 20);
        Assert.True(badStart < 5 && badEnd > 27, $"the start meets level ground ({badStart:0.#}°), the end a step ({badEnd:0.#}°)");
        string badText = RampPassability.Describe(bad.Length, bad.Width, 0, 20, badStart, badEnd);
        Report.Info(badText);
        Assert.True(badText.Contains("✗ Vehicles (27°): where it meets the ground at the end", StringComparison.Ordinal) &&
                    badText.Contains("put that end on ground at its height", StringComparison.Ordinal), badText);

        string result = s.BuildRamp(bad, 0, 20);
        Assert.True(result.Contains("where it meets the ground at the end", StringComparison.Ordinal) && result.Contains("Vehicles", StringComparison.Ordinal), result);
        var (builtStart, builtEnd) = s.RampLipDegreesAsBuilt(bad);
        Assert.True(Math.Abs(builtStart - badStart) < 1e-6 && Math.Abs(builtEnd - badEnd) < 1e-6,
            $"the built ramp measures what was predicted ({builtStart:0.###}°, {builtEnd:0.###}° vs {badStart:0.###}°, {badEnd:0.###}°)");

        // With rough sides the prediction still matches what gets built.
        s.Undo();
        var (roughStart, roughEnd) = s.RampLipDegrees(bad, 0, 20, roughness: 0.8);
        s.BuildRamp(bad, 0, 20, MapCore.Editing.RampLook.None, roughness: 0.8);
        var (builtRoughStart, builtRoughEnd) = s.RampLipDegreesAsBuilt(bad);
        Assert.True(Math.Abs(builtRoughStart - roughStart) < 1e-6 && Math.Abs(builtRoughEnd - roughEnd) < 1e-6,
            "with rough sides too, the built ramp measures what was predicted");
    }
}
