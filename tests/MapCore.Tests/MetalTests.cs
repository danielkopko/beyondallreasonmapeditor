using MapCore.Metal;
using MapCore.Smf;

namespace MapCore.Tests;

static class MetalTests
{
    static MetalLayer Load(out SmfFile smf)
    {
        smf = TestData.AcidicQuarry.LoadSmf();
        return new MetalLayer(smf, MapInfo.Load(TestData.AcidicQuarry.MapInfoPath));
    }

    static string Describe(IEnumerable<MetalSpot> spots) => string.Join("; ", spots.Select(s => $"({s.X},{s.Z}) {s.RawSum}"));

    [Test]
    public static void FindsTheSameSpotsAsBarsSpotFinder()
    {
        var metal = Load(out _);
        var spots = metal.FindSpots();
        Assert.Equal(24, spots.Count, "spots");
        Assert.True(spots.All(s => s.Cells.Count == 9 && s.Cells.All(c => c.Value == 246)), "every spot is 3x3 cells of 246");

        var bar = PythonReference.BarMetalSpots(TestData.AcidicQuarry.SmfPath);
        Assert.Equal(string.Join("; ", bar.Select(s => $"({s.X},{s.Z}) {s.Raw}")), Describe(spots), "C# spots vs Python port of BAR's finder");
        Assert.Equal(0, metal.EdgeCellsWithMetal(), "no metal in the outer ring");
    }

    [Test]
    public static void IncomeFollowsTheEngineFormula()
    {
        var metal = Load(out _);
        Assert.Equal((1.04, 90.0), (metal.MaxMetal, metal.ExtractorRadius), "mapinfo values used");
        var spot = metal.SpotNear(1000, 872)!;
        Assert.Equal((1000.0, 872.0), (spot.X, spot.Z), "spot centre");
        // extractsMetal x Σ(raw x maxMetal): 0.001 x 9 x 246 x 1.04
        Assert.True(Math.Abs(metal.Income(spot) - 2.30256) < 1e-9, $"T1 income {metal.Income(spot)}");
        Assert.True(Math.Abs(metal.Income(spot, 0.004) - 4 * 2.30256) < 1e-9, "moho-strength extractor");

        // 80 units east of the centre, the radius-90 circle reaches the middle and east columns (cell centres 80-82 and
        // 64-66 units away) but not the west column (96+ away): 6 of the 9 cells.
        double offCentre = metal.Income(spot.X + 80, spot.Z);
        Assert.True(Math.Abs(offCentre - 0.001 * 6 * 246 * 1.04) < 1e-9, $"off-centre extractor reaches 6 cells: {offCentre}");
        Assert.Equal(0.0, metal.Income(3072, 3072), "no metal in open sea");
    }

    [Test]
    public static void MovingASpotLeavesOthersAndTotalUnchanged()
    {
        var metal = Load(out var smf);
        var before = metal.FindSpots();
        long total = metal.TotalRaw;
        var spot = metal.SpotNear(1000, 872)!;

        var moved = metal.MoveSpot(spot, 1064, 900);
        Assert.Equal((1064.0, 904.0), (moved.X, moved.Z), "snapped to the 16-unit grid");
        var after = metal.FindSpots();
        Assert.Equal(24, after.Count, "still 24 spots");
        Assert.Equal(total, metal.TotalRaw, "total metal unchanged");
        Assert.Equal(Describe(before.Where(s => s != spot && !(s.X == spot.X && s.Z == spot.Z))),
                     Describe(after.Where(s => !(s.X == 1064 && s.Z == 904))), "the other 23 spots unchanged");
        Assert.True(Math.Abs(metal.Income(moved) - 2.30256) < 1e-9, "same income at the new place");
        Assert.Equal(string.Join(",", spot.Cells.Select(c => c.Value)), string.Join(",", moved.Cells.Select(c => c.Value)), "cell values carried over");
        Assert.True(smf.MetalMap[(int)(872 / 16) * smf.MetalMapWidth + (int)(1000 / 16)] == 0, "old centre cell cleared");
    }

    [Test]
    public static void RefusesLayoutsBarWouldMisread()
    {
        var metal = Load(out var smf);
        var original = (byte[])smf.MetalMap.Clone();
        var spot = metal.SpotNear(1000, 872)!;
        var neighbour = metal.SpotNear(1288, 1000)!;

        var merge = Assert.Throws<InvalidOperationException>(() => metal.MoveSpot(spot, neighbour.X - 40, neighbour.Z), "moving next to another spot");
        Assert.True(merge.Message.Contains("merge"), merge.Message);
        var edge = Assert.Throws<InvalidOperationException>(() => metal.MoveSpot(spot, 16, 872), "moving to the edge ring");
        Assert.True(edge.Message.Contains("edge"), edge.Message);
        Assert.True(smf.MetalMap.AsSpan().SequenceEqual(original), "refused edits change nothing");
    }

    [Test]
    public static void AddRemoveAndSetIncome()
    {
        var metal = Load(out _);
        var pattern = metal.TypicalPattern();
        Assert.Equal(9, pattern.Count, "typical spot is the map's 3x3");

        var added = metal.AddSpot(3072, 3072, pattern);
        // A 3x3 spot's centre sits on cell centres (16n + 8), so 3072 is exactly between 3064 and 3080.
        Assert.True(Math.Abs(added.X - 3072) <= 8 && Math.Abs(added.Z - 3072) <= 8, $"centred within half a cell of (3072, 3072): ({added.X}, {added.Z})");
        Assert.Equal(25, metal.FindSpots().Count, "spot added");

        var richer = metal.SetIncome(added, 2.0);
        Assert.True(Math.Abs(metal.Income(richer) - 2.0) < 0.01, $"income set to about 2.0 (got {metal.Income(richer)})");
        Assert.Throws<InvalidOperationException>(() => metal.SetIncome(richer, 3.0), "3.0 metal/s needs values above 255");

        metal.RemoveSpot(richer);
        Assert.Equal(24, metal.FindSpots().Count, "spot removed");
    }

    [Test]
    public static void ResizingKeepsIncomeAndAllowsRicherSpots()
    {
        var metal = Load(out _);
        long total = metal.TotalRaw;
        var spot = metal.SpotNear(3976, 616)!;
        Assert.Throws<InvalidOperationException>(() => metal.SetIncome(spot, 3.0), "a 3x3 spot here tops out at 2.39 metal/s");

        var bigger = metal.ResizeSpot(spot, 4);
        Assert.Equal(16, bigger.Cells.Count, "4x4 cells");
        Assert.True(Math.Abs(metal.Income(bigger) - 2.30256) < 0.01, $"income kept: {metal.Income(bigger)}");
        Assert.True(Math.Abs(bigger.X - spot.X) <= 8 && Math.Abs(bigger.Z - spot.Z) <= 8, "same place, within half a cell");
        Assert.Equal(24, metal.FindSpots().Count, "still 24 spots");

        var richer = metal.SetIncome(bigger, 3.0);
        Assert.True(Math.Abs(metal.Income(richer) - 3.0) < 0.01, $"now 3 metal/s is possible: {metal.Income(richer)}");
        Assert.Throws<InvalidOperationException>(() => metal.ResizeSpot(richer, 13), "13x13 (208 units) is wider than the 90-unit extractor reach");
        Assert.True(metal.TotalRaw != total, "total changes only because income was raised");
    }

    [Test]
    public static void MetalmapTextureMakesTheLayerReadOnly()
    {
        var info = MapInfo.Parse("local m = { smf = { metalmapTex = \"metal.png\" } }\nreturn m");
        var smf = TestData.AcidicQuarry.LoadSmf();
        var root = TestData.NewWorkDir("metal-readonly");
        File.WriteAllText(Path.Combine(root, "mapinfo.lua"), info.Text);
        Directory.CreateDirectory(Path.Combine(root, "maps"));
        smf.Save(Path.Combine(root, "maps", "test.smf"));

        var session = Editing.MapEditSession.Open(MapPackage.OpenFolder(root));
        Assert.True(session.Metal.ReadOnlyReason?.Contains("metalmapTex") == true, "read-only reason given");
        var spot = session.Metal.FindSpots()[0];
        Assert.Throws<InvalidOperationException>(() => session.Metal.MoveSpot(spot, spot.X + 32, spot.Z), "move refused");
        Assert.Throws<InvalidOperationException>(() => session.Metal.RemoveSpot(spot), "remove refused");
        Assert.Equal(0.02, session.Metal.MaxMetal, "engine default maxMetal when mapinfo has none");
    }
}
