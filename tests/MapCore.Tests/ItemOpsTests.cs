using MapCore.Editing;
using MapCore.Items;
using MapCore.Smf;

namespace MapCore.Tests;

static class ItemOpsTests
{
    static MapEditSession Fresh(string name) =>
        MapEditSession.Open(MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir(name)));

    static List<MapItem> Placer(MapEditSession s) => s.Items.All.Where(i => i.Source == ItemSource.FeaturePlacer).ToList();

    [Test]
    public static void GroupMoveIsOneOperationAndAllOrNothing()
    {
        var s = Fresh("items-move");
        var items = Placer(s).Take(3).ToList();
        var text = s.Items.Placer!.Document.Text;
        s.MoveItems(items, 50, -20);
        var moved = Placer(s).Take(3).ToList();
        Assert.True(items.Zip(moved).All(p => p.Second.X == p.First.X + 50 && p.Second.Z == p.First.Z - 20), "all three moved");
        s.Undo();
        Assert.Equal(text, s.Items.Placer!.Document.Text, "one undo restores set.lua exactly");

        // One of them would leave the map: none moves.
        var edge = items.Append(Placer(s).MinBy(i => i.X)!).ToList();
        double dx = -edge.Min(i => i.X) - 10;
        Assert.Throws<InvalidOperationException>(() => s.MoveItems(edge, dx, 0), "a move taking one item off the map is refused");
        Assert.Equal(text, s.Items.Placer!.Document.Text, "and changes nothing");
    }

    [Test]
    public static void RefsAddressExactlyAndRemovalKeepsTheRest()
    {
        var s = Fresh("items-remove");
        var all = Placer(s);
        var gone = new[] { all[3], all[5] };
        s.RemoveItems(gone);
        var left = Placer(s);
        Assert.Equal(all.Count - 2, left.Count, "two removed");
        var expected = all.Where((_, i) => i != 3 && i != 5).Select(i => (i.Name, i.X, i.Z)).ToList();
        Assert.True(left.Select(i => (i.Name, i.X, i.Z)).SequenceEqual(expected), "exactly those two went; the rest are unchanged and in order");
        Assert.Throws<InvalidOperationException>(() => s.Apply("items-remove:f999"), "an unknown ref is refused");
        Assert.Throws<InvalidOperationException>(() => s.Apply("items-remove:x1"), "a malformed ref is refused");
    }

    [Test]
    public static void RotationInDegreesAndBuildingsByQuarters()
    {
        var s = Fresh("items-rotate");
        var rock = Placer(s)[0];
        double before = MapEditSession.HeadingDegrees(rock);
        s.RotateItems([rock], 90);
        Assert.True(Math.Abs((MapEditSession.HeadingDegrees(Placer(s)[0]) - before + 360) % 360 - 90) < 0.01, "a feature turns 90°");
        s.SetItemHeading([Placer(s)[0]], 270);
        Assert.True(Math.Abs(MapEditSession.HeadingDegrees(Placer(s)[0]) - 270) < 0.01, "and can be set to face west (270°)");

        s.Items.Add(ItemSource.FeaturePlacer, PlacedItemKind.Building, "armwin", 3000, 3000, 0);
        var building = s.Items.All.Single(i => i.Kind == PlacedItemKind.Building);
        s.RotateItems([building], 15);
        Assert.Equal(1.0, s.Items.All.Single(i => i.Kind == PlacedItemKind.Building).Rotation, "a small turn turns a building one quarter (east)");
        s.SetItemHeading([s.Items.All.Single(i => i.Kind == PlacedItemKind.Building)], 175);
        Assert.Equal(2.0, s.Items.All.Single(i => i.Kind == PlacedItemKind.Building).Rotation, "a heading snaps to the nearest quarter (north)");
    }

    /// <summary>Scattering adds items one at a time, and every frame lists them all: both must stay quick with many items.</summary>
    [Test]
    public static void ManyItemsStayQuick()
    {
        var s = Fresh("items-many");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        s.BeginGroup("scatter");
        for (int i = 0; i < 1000; i++)
            s.AddItem(ItemSource.FeaturePlacer, "agorm_rock1", 200 + i % 40 * 140, 200 + i / 40 * 200, i * 7 % 360);
        s.EndGroup();
        double addMs = sw.Elapsed.TotalMilliseconds / 1000;
        using var renderer = new MapCore.Rendering.MapRenderer(s);
        var target = new MapCore.Imaging.RgbaImage(1600, 1000);
        var view = MapCore.Rendering.ViewTransform.Fit(s.MapWidth, s.MapHeight, 1600, 1000);
        renderer.Render(target, view, MapCore.Rendering.MapViewLayer.Texture, MapCore.Rendering.MapOverlays.Items);
        sw.Restart();
        for (int i = 0; i < 5; i++)
            renderer.Render(target, view, MapCore.Rendering.MapViewLayer.Texture, MapCore.Rendering.MapOverlays.Items);
        double frameMs = sw.Elapsed.TotalMilliseconds / 5;
        Report.Info($"1056 items: {addMs:0.0} ms per added item (with undo), {frameMs:0} ms per 1600x1000 frame drawing them all");
        Assert.True(addMs < 30 && frameMs < 200, "adding and drawing many items stays quick");
    }

    /// <summary>
    /// Acidic Quarry has a grass section; a map without one must gain the section and its extra header when grass is
    /// painted, and the independent parser must read the result.
    /// </summary>
    [Test]
    public static void GrassOnAMapWithoutAGrassMap()
    {
        var s = Fresh("grass-new");
        s.Smf.GrassMap = null;
        s.Save();
        var reopened = MapEditSession.Open(MapPackage.OpenFolder(s.Package.Root));
        Assert.True(reopened.Smf.GrassMap == null && !reopened.Smf.ExtraHeaders.Any(h => h.Type == SmfExtraHeader.GrassType), "saved without grass");

        reopened.PaintGrass(3000, 3000, 200, 1);
        Assert.True(GrassLayer.CellsWithGrass(reopened.Smf) > 100, "painting created the grass map");
        reopened.Save();
        var reference = PythonReference.DescribeSmf(reopened.Package.SmfPath);
        int cells = reference.GetProperty("grass").GetProperty("nonzero").GetInt32();
        Assert.Equal(GrassLayer.CellsWithGrass(reopened.Smf), cells, "grass cells the Python parser finds");
        Assert.Equal(0, reference.GetProperty("gaps").GetArrayLength() + reference.GetProperty("overlaps").GetArrayLength(), "gaps and overlaps in the file");
        Assert.True(reference.GetProperty("extraHeaders").EnumerateArray().Any(e => e.GetProperty("name").GetString() == "grass"), "the grass extra header is there");

        reopened.Undo();
        Assert.True(reopened.Smf.GrassMap == null, "undo removes the grass map again");
    }
}
