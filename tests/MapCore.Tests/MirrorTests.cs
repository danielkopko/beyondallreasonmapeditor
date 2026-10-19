using MapCore.Editing;
using MapCore.Items;
using MapCore.Structures;

namespace MapCore.Tests;

/// <summary>
/// Symmetric editing (MapEditSession.Mirror): an edit at a place is made at its mirror image too, as one undo step,
/// turned to match; things already there are changed with their twins; settings that aren't at a place are made once;
/// and a journal replays it exactly, without mirroring again.
/// </summary>
static class MirrorTests
{
    static MapEditSession Open(string name) =>
        MapEditSession.Open(MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir(name)));

    [Test]
    public static void MirrorsMapPointsDirectionsAndHeadings()
    {
        var lr = new MapMirror(MirrorKind.LeftRight, 6144, 4096);
        var tb = new MapMirror(MirrorKind.TopBottom, 6144, 4096);
        var turn = new MapMirror(MirrorKind.HalfTurn, 6144, 4096);
        var diag = new MapMirror(MirrorKind.Diagonal, 4096, 4096);
        var anti = new MapMirror(MirrorKind.AntiDiagonal, 4096, 4096);
        Assert.True(lr.Map(1000, 500) == (5144, 500) && tb.Map(1000, 500) == (1000, 3596) && turn.Map(1000, 500) == (5144, 3596) &&
                    diag.Map(1000, 500) == (500, 1000) && anti.Map(1000, 500) == (3596, 3096), "points go to their mirror images");
        foreach (var m in new[] { lr, tb, turn, diag, anti })
            Assert.True(m.Map(m.Map(123, 456).X, m.Map(123, 456).Z) == (123, 456), $"{m.Kind} is its own inverse");
        // 0 faces south (+z), 90 east (+x).
        Assert.True(lr.Heading(0) == 0 && lr.Heading(90) == 270 && tb.Heading(0) == 180 && tb.Heading(90) == 90 && turn.Heading(0) == 180 &&
                    turn.Heading(90) == 270 && diag.Heading(0) == 90 && anti.Heading(0) == 270, "headings: south stays south left-right, turns north top-bottom");
        Assert.True(lr.Facing(1) == 3 && lr.Facing(0) == 0 && tb.Facing(0) == 2 && turn.Facing(1) == 3 && diag.Facing(0) == 1, "quarter facings likewise");
        Assert.True(lr.MapVector(10, 5) == (-10, 5) && turn.MapVector(10, 5) == (-10, -5) && lr.Reflects && !turn.Reflects, "offsets, and which ones reflect");
        Assert.Throws<InvalidOperationException>(() => new MapMirror(MirrorKind.Diagonal, 6144, 4096), "a diagonal needs a square map");
        Assert.True(MapMirror.ParseKind(turn.Code) == MirrorKind.HalfTurn, "codes read back");
    }

    [Test]
    public static void HeightBrushesAreMirroredExactlyAsOneUndoStep()
    {
        var s = Open("mirror-heights");
        s.Mirror = MirrorKind.LeftRight;
        var before = (float[])s.Heights.Heights.Clone();
        Report.Info(s.Apply("height-raise:1500,2000:300:40:0.4"));
        Report.Info(s.Apply("height-noise:1700,2500:250:30:0.5:48"));
        int w = s.Heights.Width, h = s.Heights.Height;
        double worst = 0;
        int changed = 0;
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x, j = z * w + (w - 1 - x);
                double a = s.Heights.Heights[i] - before[i], b = s.Heights.Heights[j] - before[j];
                worst = Math.Max(worst, Math.Abs(a - b));
                if (a != 0)
                    changed++;
            }
        Report.Info($"{changed:N0} vertices changed; the worst mismatch with the mirror image {worst:0.######}");
        Assert.True(changed > 2000 && worst < 1e-3, "the raise and the noise (read mirrored) change both halves alike");
        Assert.True(s.UndoDescription?.StartsWith("height-noise:1700,2500:", StringComparison.Ordinal) == true, $"named for the edit made: {s.UndoDescription}");
        s.Undo();
        s.Undo();
        Assert.True(s.Heights.Heights.AsSpan().SequenceEqual(before), "two undos take both edits and both mirror images away");

        // A dab on the line is its own mirror image: made once.
        s.Apply("height-raise:3072,1000:200:20");
        double middle = s.Heights.HeightAt(3072, 1000) - before[(1000 / 8) * w + 3072 / 8];
        Assert.True(Math.Abs(middle - 20) < 0.01, $"on the line, raised once, not twice ({middle:0.##})");
    }

    [Test]
    public static void TextureBrushesAreMirroredAcrossAHalfTurn()
    {
        var s = Open("mirror-texture");
        s.Mirror = MirrorKind.HalfTurn;
        s.BeginGroup("a stroke");
        s.Apply("texture-paint:1200,1500:120:200,40,40:1:1");
        s.Apply("texture-paint:1215,1490:70:40,40,220:0.3:0.6");
        s.Apply("texture-clone:1190,1510:30:20,-15:0.5:0.8");
        s.EndGroup();
        var image = s.TextureImage;
        double perUnit = image.Width / s.MapWidth;
        // The texture is kept as DXT1 stores it: each 4x4 block is compressed on its own, and a block and its mirror
        // image (the same pixels in the other order) can round a few levels apart where colours blend.
        int cx = (int)(1200 * perUnit), cy = (int)(1500 * perUnit), r = (int)(115 * perUnit), differing = 0, worst = 0;
        for (int y = cy - r; y <= cy + r; y++)
            for (int x = cx - r; x <= cx + r; x++)
            {
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) > r * r)
                    continue; // outside the first dab: the map around it isn't symmetric
                var (a, b) = (image.GetPixel(x, y), image.GetPixel(image.Width - 1 - x, image.Height - 1 - y));
                int d = Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
                worst = Math.Max(worst, d);
                if (d > 0)
                    differing++;
            }
        Report.Info($"{differing} of {Math.PI * r * r:N0} pixels differ from their mirror image, by at most {worst} levels");
        Assert.True(worst <= 12 && differing < 200 && image.GetPixel(cx - (int)(90 * perUnit), cy) == new MapCore.Imaging.Rgba(200, 40, 40, 255),
            "painted, blended and cloned alike at the opposite corner, but for the compression's rounding");
        Assert.True(s.UndoDescription == "a stroke", "a stroke's mirror images go in the stroke's own undo step");
    }

    [Test]
    public static void ItemsAreMirroredWithTheirTwins()
    {
        var s = Open("mirror-items");
        int count = s.Items.All.Count;
        s.Mirror = MirrorKind.LeftRight;
        Report.Info(s.Apply("item-add:smf:agorm_rock1:1000,2000:8192")); // 45°: south-east
        var twin = s.Items.All.Single(i => i.Name == "agorm_rock1" && Math.Abs(i.X - 5144) < 0.01 && Math.Abs(i.Z - 2000) < 0.01);
        Assert.True(s.Items.All.Count == count + 2 && Math.Abs(MapEditSession.HeadingDegrees(twin) - 315) < 0.01,
            $"added at its mirror image too, facing south-west ({MapEditSession.HeadingDegrees(twin):0.#}°)");
        Report.Info(s.Apply("item-move:1000,2000:1100,2100"));
        Assert.True(s.Items.All.Any(i => i.Name == "agorm_rock1" && Math.Abs(i.X - 5044) < 0.01 && Math.Abs(i.Z - 2100) < 0.01), "moved with it, mirrored");
        var original = s.Items.All.Single(i => i.Name == "agorm_rock1" && Math.Abs(i.X - 1100) < 0.01 && Math.Abs(i.Z - 2100) < 0.01);
        Report.Info(s.SetItemHeading([original], 90));
        twin = s.Items.All.Single(i => i.Name == "agorm_rock1" && Math.Abs(i.X - 5044) < 0.01);
        Assert.True(Math.Abs(MapEditSession.HeadingDegrees(twin) - 270) < 0.01, "turned east, its twin faces west");
        Report.Info(s.RemoveItems([s.Items.All.Single(i => i.Name == "agorm_rock1" && Math.Abs(i.X - 1100) < 0.01)]));
        Assert.True(s.Items.All.Count == count, "removed with it");

        // Both halves chosen: each changed once.
        s.Apply("item-add:smf:agorm_rock2:800,800:0");
        var pair = s.Items.All.Where(i => i.Name == "agorm_rock2" && Math.Abs(i.Z - 800) < 0.01).ToList();
        s.MoveItems(pair, 0, 64);
        Assert.True(s.Items.All.Count(i => i.Name == "agorm_rock2" && Math.Abs(i.Z - 864) < 0.01) == 2, "both chosen, both moved once");
        // Something without a twin: only it changes, and the result says so.
        var lone = s.Items.All.First(i => i.Name != "agorm_rock2" && i.Name != "agorm_rock1" && Math.Abs(i.X - s.MapWidth / 2) > 100);
        string result = s.Apply(FormattableString.Invariant($"item-move:{lone.X},{lone.Z}:{lone.X + 30},{lone.Z}"));
        Report.Info(result);
        Assert.True(result.Contains("not mirrored", StringComparison.Ordinal), "a lone item moves alone, said so");
    }

    [Test]
    public static void MetalSpotsStructuresAndStartsAreMirrored()
    {
        var s = Open("mirror-things");
        s.Mirror = MirrorKind.LeftRight;
        int spots = s.Metal.FindSpots().Count;
        Report.Info(s.AddSpot(1000, 3000));
        var a = s.Metal.SpotNear(1000, 3000, 32)!;
        var b = s.Metal.SpotNear(5144, 3000, 32);
        Assert.True(s.Metal.FindSpots().Count == spots + 2 && b != null && Math.Abs(s.Metal.Income(a) - s.Metal.Income(b)) < 1e-9,
            $"a metal spot and its twin, giving the same ({b?.X}, {b?.Z})");
        s.RemoveSpot(a.X, a.Z);
        Assert.True(s.Metal.FindSpots().Count == spots, "removed together");

        // A structure kind is made once; a door placed facing east has a twin facing west, its parts mirrored.
        s.AddStructureKind("gate", StructureType.Door, StructureKindSettings.Built(StructureLook.Concrete, StructureType.Door) with { Gap = 5 });
        Assert.True(s.Structures.Kinds.Count == 1, "the kind once");
        Report.Info(s.PlaceStructure("gate", 1008, 2000, 1));
        var placed = s.Structures.Instances;
        var first = placed.Single(i => i.X == 1008);
        var second = placed.SingleOrDefault(i => i.X == s.MapWidth - 1008);
        Assert.True(placed.Count == 2 && second is { Facing: 3, Z: 2000 }, $"its twin at the mirror image, facing west ({second?.X}, {second?.Facing})");
        var kind = s.Structures.Kinds["gate"];
        var parts = StructureLayout.Parts(kind, first).Select(p => (p.Role, X1: s.MapWidth - p.X2, p.Z1, X2: s.MapWidth - p.X1, p.Z2)).OrderBy(p => (p.Role, p.X1, p.Z1));
        var twinParts = StructureLayout.Parts(kind, second!).Select(p => (p.Role, p.X1, p.Z1, p.X2, p.Z2)).OrderBy(p => (p.Role, p.X1, p.Z1));
        Report.Info(string.Join("; ", twinParts));
        Assert.True(parts.SequenceEqual(twinParts), "with a gap of 5 its walls and gate are the mirror image of the first's");
        s.RemoveStructure(1008, 2000);
        Assert.True(s.Structures.Instances.Count == 0, "removed together");

        // Starts: Acidic Quarry has four; team 0's mirror image left | right is about 216 from team 2's start, its twin.
        var starts = s.Info.StartPositions;
        Report.Info(string.Join("; ", starts));
        var team2 = starts.Single(p => p.Team == 2);
        Report.Info(s.MoveStartPosition(0, 900, 4800));
        var moved = s.Info.StartPositions.Single(p => p.Team == 2);
        Assert.True(moved.X == s.MapWidth - 900 && moved.Z == 4800 && s.Info.StartPositions.Single(p => p.Team == 1) == starts.Single(p => p.Team == 1),
            $"team 0's twin, team 2, moves to the mirror image ({moved.X}, {moved.Z}); the others stay");
        s.Undo();
        Assert.True(s.Info.StartPositions.Single(p => p.Team == 2) == team2, "one undo puts both back");
        s.Mirror = null;
        s.MoveStartPosition(2, 3000, 3000); // nowhere near team 0's mirror image now
        s.Mirror = MirrorKind.LeftRight;
        string lone = s.MoveStartPosition(0, 950, 4850);
        Assert.True(lone.Contains("not mirrored", StringComparison.Ordinal) && s.Info.StartPositions.Single(p => p.Team == 2) is { X: 3000, Z: 3000 },
            $"with no start near its mirror image, it moves alone, said so: {lone}");

        // A map with two starts: the other one is always the twin.
        var two = MapEditSession.Open(MapPackage.Open(SyntheticMap.Build(TestData.NewWorkDir("mirror-two-starts-map")), TestData.NewWorkDir("mirror-two-starts")));
        two.Mirror = MirrorKind.LeftRight;
        Report.Info(two.MoveStartPosition(0, 300, 600));
        Assert.True(two.Info.StartPositions.Single(p => p.Team == 1) is var other && other.X == two.MapWidth - 300 && other.Z == 600,
            "on a map with two starts, moving one moves the other to its mirror image");
    }

    /// <summary>Changing what is already there changes its twin: structures moved and turned, repair markers, a guardian's path, a spot moved with its look, items turned, grass.</summary>
    [Test]
    public static void TwinsFollowMovesTurnsMarkersAndPaths()
    {
        var s = Open("mirror-twins");
        s.Mirror = MirrorKind.LeftRight;
        double w = s.MapWidth;
        StructureInstance At(double x, double z) => s.Structures.Instances.Single(i => i.X == x && i.Z == z);

        s.AddStructureKind("gate", StructureType.Door, StructureKindSettings.Built(StructureLook.Concrete, StructureType.Door));
        s.PlaceStructure("gate", 1008, 2000, 0);
        Report.Info(s.Apply("structure-move:1008,2000:1104,2096"));
        Assert.True(s.Structures.Instances.Count == 2 && At(w - 1104, 2096).Facing == 0, "a structure moved: its twin moves to the mirror image");
        Report.Info(s.Apply("structure-turn:1104,2096:1"));
        Assert.True(At(1104, 2096).Facing == 1 && At(w - 1104, 2096).Facing == 3, "turned east: its twin turns west");
        s.Undo();
        Assert.True(At(1104, 2096).Facing == 0 && At(w - 1104, 2096).Facing == 0, "one undo turns both back");
        Report.Info(s.Apply("structure-marker-add:1104,2096:1200,2200"));
        Assert.True(At(w - 1104, 2096).MarkerList.SequenceEqual([(w - 1200, 2200.0)]), "a repair marker: its twin's at the mirror image");
        Report.Info(s.Apply("structure-marker-move:1200,2200:1240,2240"));
        Assert.True(At(w - 1104, 2096).MarkerList.SequenceEqual([(w - 1240, 2240.0)]), "moved, and its twin");
        Report.Info(s.Apply("structure-marker-remove:1240,2240"));
        Assert.True(s.Structures.Instances.All(i => i.MarkerList.Count == 0), "removed, and its twin");

        s.AddGuardianKind("watch", StructureKindSettings.Guardian(), ["armthund"]);
        s.PlaceStructure("watch", 1504, 3008, 0);
        Report.Info(s.Apply("structure-zone:1504,3008:250:1:1300,2800;1700,2800;1700,3200"));
        Assert.True(At(w - 1504, 3008).Zone is { Closed: true } zone && zone.Points.SequenceEqual([(w - 1300, 2800.0), (w - 1700, 2800.0), (w - 1700, 3200.0)]),
            "a guardian's path: its twin guards the mirror image of it");
        s.Apply("structure-zone:1504,3008:none");
        Assert.True(At(w - 1504, 3008).Zone == null && At(1504, 3008).Zone == null, "cleared, and its twin's");

        s.AddSpot(1000, 3600);
        var spotsBefore = s.Metal.FindSpots().Select(p => (p.X, p.Z)).Order().ToList();
        Report.Info(s.Apply("metal-move:1000,3600:1100,3700:look"));
        Assert.True(s.Metal.SpotNear(w - 1100, 3700, 24) != null && s.Metal.SpotNear(w - 1000, 3600, 24) == null, "a spot moved with its look: its twin moves too");
        s.Undo();
        Assert.True(s.Metal.FindSpots().Select(p => (p.X, p.Z)).Order().SequenceEqual(spotsBefore), "one undo puts both back");

        s.Apply("item-add:smf:agorm_rock4:700,700:0");
        MapItem Rock(double x) => s.Items.All.Single(i => i.Name == "agorm_rock4" && Math.Abs(i.X - x) < 0.01 && Math.Abs(i.Z - 700) < 0.01);
        s.RotateItems([Rock(700)], 30);
        Assert.True(Math.Abs(MapEditSession.HeadingDegrees(Rock(700)) - 30) < 0.01 && Math.Abs(MapEditSession.HeadingDegrees(Rock(w - 700)) - 330) < 0.01,
            "turned 30° one way: its mirror image turns 30° the other");
        s.Apply(FormattableString.Invariant($"item-rotate:700,700:{16384}"));
        Assert.True(Math.Abs(MapEditSession.HeadingDegrees(Rock(w - 700)) - 270) < 0.01, "set to face east: its twin faces west");
        s.Mirror = MirrorKind.HalfTurn;
        s.Apply("item-add:smf:agorm_rock5:900,900:0");
        MapItem Rock5(double x, double z) => s.Items.All.Single(i => i.Name == "agorm_rock5" && Math.Abs(i.X - x) < 0.01 && Math.Abs(i.Z - z) < 0.01);
        s.RotateItems([Rock5(900, 900)], 30);
        Assert.True(Math.Abs(MapEditSession.HeadingDegrees(Rock5(w - 900, s.MapHeight - 900)) - 210) < 0.01, "with a half turn, the twin turns the same way");

        s.Mirror = MirrorKind.LeftRight;
        Report.Info(s.Apply("grass-paint:1500,2500:100:1"));
        Assert.True(MapCore.Smf.GrassLayer.ValueAt(s.Smf, w - 1500, 2500) == 1, "grass painted at the mirror image too");
    }

    [Test]
    public static void SettingsAreMadeOnceAndADiagonalNeedsASquareMap()
    {
        var s = Open("mirror-once");
        s.Mirror = MirrorKind.Diagonal; // Acidic Quarry is square
        var before = (float[])s.Heights.Heights.Clone();
        s.Apply("height-offset:10");
        Assert.True(s.Heights.Heights.Zip(before).All(p => Math.Abs(p.First - p.Second - 10) < 1e-3), "the whole map raised once, not twice");
        Assert.True(s.UndoDescription == "height-offset:10", "as one edit");
        var wide = MapEditSession.Open(MapPackage.Open(SyntheticMap.Build(TestData.NewWorkDir("mirror-wide-map")), TestData.NewWorkDir("mirror-wide")));
        Assert.Throws<InvalidOperationException>(() => wide.Mirror = MirrorKind.Diagonal, "a diagonal on a map twice as wide as it is tall (the synthetic map)");
        wide.Mirror = MirrorKind.HalfTurn;
        Assert.True(wide.Mirror == MirrorKind.HalfTurn, "a half turn fits any map");
    }

    /// <summary>A ramp's mirror image is the same ramp (its rough sides too), and a journal replays the lot without mirroring twice.</summary>
    [Test]
    public static void RampsAreMirroredAndAJournalReplaysWithoutMirroringAgain()
    {
        var live = Open("mirror-journal-live");
        var start = (float[])live.Heights.Heights.Clone();
        var dir = TestData.NewWorkDir("mirror-journal-record");
        float[] before;
        using (var journal = new SessionJournal(Path.Combine(dir, "j.journal"), Path.Combine(dir, "files")))
        {
            live.Journal = journal;
            live.Mirror = MirrorKind.TopBottom;
            // Level ground both sides first (the map isn't symmetric): a ramp blends into the ground that is there.
            live.Apply("height-flatten:1900,1000:700:100:1:1");
            before = (float[])live.Heights.Heights.Clone();
            Report.Info(live.Apply("ramp:1500,900;1800,1100:2200,1000:96:auto,180:auto:1:0.8"));
            live.Apply("item-add:smf:agorm_rock3:2500,700:4000");
            live.Apply("height-noise:2600,1300:200:25");
            live.Undo();
            live.Redo();
            live.Journal = null;
        }
        int w = live.Heights.Width, h = live.Heights.Height;
        double worst = 0;
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x, j = (h - 1 - z) * w + x;
                worst = Math.Max(worst, Math.Abs(live.Heights.Heights[i] - before[i] - (live.Heights.Heights[j] - before[j])));
            }
        Assert.True(worst < 0.05, $"the ramp, rough sides and all, and the noise match their mirror images (worst {worst:0.####})");
        var rebuilt = Open("mirror-journal-rebuilt");
        Assert.True(rebuilt.Mirror == null, "the rebuilt session has no mirror set");
        SessionJournal.Replay(rebuilt, SessionJournal.Read(Path.Combine(dir, "j.journal")));
        Assert.True(rebuilt.Heights.Heights.AsSpan().SequenceEqual(live.Heights.Heights), "the replay gives the same heights");
        Assert.True(rebuilt.Items.All.SequenceEqual(live.Items.All), "and the same items: nothing made twice");
        Assert.True(rebuilt.TextureImage.Pixels.AsSpan().SequenceEqual(live.TextureImage.Pixels), "and the same texture");
        Assert.True(rebuilt.UndoDescription == live.UndoDescription && rebuilt.UndoDescription!.StartsWith("height-noise:", StringComparison.Ordinal),
            "with the same undo steps");
        rebuilt.Undo();
        rebuilt.Undo();
        rebuilt.Undo();
        rebuilt.Undo();
        Assert.True(rebuilt.Heights.Heights.AsSpan().SequenceEqual(start) && rebuilt.Items.All.Count == Open("mirror-journal-fresh").Items.All.Count,
            "and four undos take the four edits, mirror images and all");
    }
}
