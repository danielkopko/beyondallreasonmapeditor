using MapCore.Editing;
using MapCore.Items;

namespace MapCore.Tests;

static class EditSessionTests
{
    [Test]
    public static void OperationsApplyAndSaveTogether()
    {
        var package = MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir("session"));
        var session = MapEditSession.Open(package);
        var results = new[]
        {
            "metal-move:1000,872:1064,904",
            "set:atmosphere.fogColor=0.2,0.3,0.4",
            "item-move:5809,3562:5700,3500",
            "item-add:smf:agorm_rock1:3000,3000:100",
            "item-add:placer:agorm_rock2:3100,3100",
            "start-move:1:5200,1100",
        }.Select(session.Apply).ToList();
        Assert.True(results.All(r => r.Length > 0), "each operation describes its change");
        session.Save();

        var reopened = MapEditSession.Open(MapPackage.OpenFolder(package.Root));
        Assert.True(reopened.Metal.SpotNear(1064, 904, 1) != null && reopened.Metal.SpotNear(1000, 872, 1) == null, "metal spot moved");
        Assert.Equal("0.2,0.3,0.4", string.Join(",", reopened.Info.GetNumbers("atmosphere.fogColor")!), "fog colour");
        Assert.Equal((5200.0, 1100.0), (reopened.Info.StartPositions[1].X, reopened.Info.StartPositions[1].Z), "start position");
        var all = reopened.Items.All;
        Assert.True(all.Any(i => i.Source == ItemSource.FeaturePlacer && i.X == 5700 && i.Z == 3500), "rock moved");
        Assert.True(all.Any(i => i.Source == ItemSource.Smf && i.Name == "agorm_rock1" && i.X == 3000 && i.Rotation == 100), "SMF rock added");
        Assert.True(all.Any(i => i.Source == ItemSource.FeaturePlacer && i.Name == "agorm_rock2" && i.X == 3100), "placer rock added");

        // The independent parser reads the saved SMF, sees the new feature and BAR's finder sees the moved spot.
        var py = PythonReference.DescribeSmf(reopened.Package.SmfPath);
        Assert.Equal(1, py.GetProperty("features").GetProperty("count").GetInt32(), "python: one SMF feature");
        Assert.True(PythonReference.BarMetalSpots(reopened.Package.SmfPath).Any(s => s.X == 1064 && s.Z == 904), "python BAR finder: moved spot");
    }

    [Test]
    public static void UnsetRemovesAValueAndUndoRestoresIt()
    {
        var session = MapEditSession.Open(MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir("unset")));
        var before = session.Info.Text;
        Assert.True(session.Info.StartPositions.Any(s => s.Team == 3), "Acidic Quarry has a team 3");
        var result = session.Apply("unset:teams.3");
        Assert.True(result.Contains("removed"), "it says what it removed");
        Assert.True(session.Info.Get("teams.3") == null && session.Info.StartPositions.All(s => s.Team != 3), "team 3's start is gone");
        Assert.True(session.Info.StartPositions.Any(s => s.Team == 2), "the others stay");
        session.Undo();
        Assert.Equal(before, session.Info.Text, "undo restores mapinfo.lua exactly");
        Assert.Throws<InvalidOperationException>(() => session.Apply("unset:teams.99"), "a value the map doesn't have");
        Assert.Throws<InvalidOperationException>(() => session.Apply("unset:name=x"), "unset takes a path, not a value");
        Assert.Equal(before, session.Info.Text, "refused operations change nothing");
    }

    [Test]
    public static void BadOperationsAreRejectedWithoutChanges()
    {
        var session = MapEditSession.Open(MapPackage.OpenFolder(TestData.AcidicQuarry.Root));
        var metal = (byte[])session.Smf.MetalMap.Clone();
        foreach (var op in new[] { "nonsense", "teleport:1,2", "metal-move:1000,872", "metal-move:3072,3072:100,100",
                                   "metal-add:abc,5", "item-add:attic:rock:1,2", "item-remove:3072,3072", "set:name" })
            Assert.Throws<InvalidOperationException>(() => session.Apply(op), $"'{op}'");
        Assert.True(session.Smf.MetalMap.AsSpan().SequenceEqual(metal), "metal map untouched");
    }
}
