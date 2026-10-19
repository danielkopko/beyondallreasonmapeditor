using MapCore.Editing;
using MapCore.Heights;
using MapCore.Imaging;
using MapCore.Smf;

namespace MapCore.Tests;

static class SaveCheckTests
{
    static MapEditSession Fresh(string name) =>
        MapEditSession.Open(MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir(name)));

    static string Describe(IEnumerable<SaveCheck> checks) => string.Join(" | ", checks.Select(c => $"{c.Level}: {c.Message}"));

    [Test]
    public static void TheOriginalMapPassesExceptForItsName()
    {
        var s = Fresh("checks-clean");
        var checks = s.CheckBeforeSave(checkName: false);
        Report.Info("Acidic Quarry as it is: " + Describe(checks));
        Assert.True(checks.All(c => c.Level != CheckLevel.Error), "no errors");
        Assert.True(s.CheckBeforeSave().Any(c => c.Level == CheckLevel.Error && c.Message.Contains("still called", StringComparison.Ordinal)),
            "keeping the original name and version is an error for Save As");
        s.Apply("set:version=\"5.17-edit\"");
        Assert.True(!s.CheckBeforeSave().Any(c => c.Message.Contains("still called", StringComparison.Ordinal)), "a new version passes");
    }

    [Test]
    public static void BrokenThingsAreCaught()
    {
        var s = Fresh("checks-broken");
        s.Apply("item-add:placer:agorm_rock1:-40,100");
        s.Apply("item-add:placer:tree_that_does_not_exist:2000,2000");
        s.Apply("start-move:1:3072,3072"); // the middle of the acid sea
        s.RaiseHeights(new Brush(1100, 950, 60, 0.5), 300); // above the highest ground: the range must widen
        var checks = s.CheckBeforeSave(checkName: false);
        Report.Info(Describe(checks));
        Assert.True(checks.Any(c => c.Level == CheckLevel.Error && c.Message.Contains("off the map", StringComparison.Ordinal)), "an item off the map is an error");
        Assert.True(checks.Any(c => c.Level == CheckLevel.Warning && c.Message.Contains("tree_that_does_not_exist", StringComparison.Ordinal)), "an unknown name warns");
        Assert.True(checks.Any(c => c.Level == CheckLevel.Warning && c.Message.StartsWith("Start position 1 is under water", StringComparison.Ordinal)), "a start under water warns");
        Assert.True(checks.Any(c => c.Level == CheckLevel.Note && c.Message.Contains("height range widens", StringComparison.Ordinal)), "a wider height range is noted");
        s.Apply("start-move:1:7000,100");
        Assert.True(s.CheckBeforeSave(checkName: false).Any(c => c.Level == CheckLevel.Error && c.Message.StartsWith("Start position 1 is off the map", StringComparison.Ordinal)),
            "a start off the map is an error");
    }

    /// <summary>What the map already had when opened is a note; only what this session causes warns.</summary>
    [Test]
    public static void OnlyNewProblemsWarn()
    {
        var s = Fresh("checks-baseline");
        s.Apply("start-move:1:3072,3072"); // under water
        s.Save();
        var reopened = MapEditSession.Open(MapPackage.OpenFolder(s.Package.Root));
        var start1 = reopened.CheckBeforeSave(checkName: false).Single(c => c.Message.StartsWith("Start position 1 is under water", StringComparison.Ordinal));
        Assert.Equal(CheckLevel.Note, start1.Level, "a start that was already under water when opened");
        reopened.Apply("start-move:2:3150,3300");
        var checks = reopened.CheckBeforeSave(checkName: false);
        Assert.Equal(CheckLevel.Warning, checks.Single(c => c.Message.StartsWith("Start position 2 is under water", StringComparison.Ordinal)).Level, "a start moved under water now");
        Assert.Equal(CheckLevel.Note, checks.Single(c => c.Message.StartsWith("Start position 1 is under water", StringComparison.Ordinal)).Level, "the old one stays a note");
    }

    [Test]
    public static void WideningTheRangeWarnsAboutMapOptions()
    {
        var s = Fresh("checks-range");
        s.RaiseHeights(new Brush(1100, 950, 60, 0.5), 300);
        var warning = s.CheckBeforeSave(checkName: false).SingleOrDefault(c => c.Level == CheckLevel.Warning && c.Message.Contains("0_apply_options.lua", StringComparison.Ordinal));
        Assert.True(warning != null, "this map's water-level options set the height range in game: widening it warns, naming the file");
    }

    /// <summary>A redrawn bit of minimap is closer to the map's own minimap than the bare texture would be.</summary>
    [Test]
    public static void RedrawnMinimapFollowsTheMapsStyle()
    {
        var s = Fresh("minimap-style");
        var original = Minimap.Decode(s.Smf);
        var plain = s.TextureImage.Resample(Minimap.Size, Minimap.Size);
        // A paint stroke too faint to change the texture still redraws the minimap under it: water at the centre.
        s.Apply("texture-paint:3072,3072:400:0,0,0:1:0.0001");
        var redrawn = Minimap.Decode(s.Smf);
        double styled = 0, bare = 0;
        int n = 0;
        int lo = (int)((3072 - 300) / 6.0), hi = (int)((3072 + 300) / 6.0);
        for (int y = lo; y < hi; y++)
            for (int x = lo; x < hi; x++, n++)
                for (int c = 0; c < 3; c++)
                {
                    int i = (y * Minimap.Size + x) * 4 + c;
                    styled += Math.Pow(redrawn.Pixels[i] - original.Pixels[i], 2);
                    bare += Math.Pow(plain.Pixels[i] - original.Pixels[i], 2);
                }
        double Psnr(double se) => 10 * Math.Log10(255.0 * 255 / (se / (n * 3)));
        Report.Info($"redrawn under water vs the map's own minimap: {Psnr(styled):0.0} dB in its style, {Psnr(bare):0.0} dB from the bare texture");
        Assert.True(styled < bare * 0.8, "the redraw follows the map's minimap style");
    }

    [Test]
    public static void SavingRedrawsTheMinimapWhereTheGroundMoved()
    {
        var s = Fresh("minimap-heights");
        var before = (byte[])s.Smf.Minimap.Clone();
        s.RaiseHeights(new Brush(3072, 3072, 150, 0.7), 200); // a new island in the acid
        Assert.True(s.Smf.Minimap.AsSpan().SequenceEqual(before), "height edits alone leave the minimap until saving");
        var notes = s.Save();
        Assert.True(notes.Any(n => n.Contains("minimap redrawn", StringComparison.Ordinal)), "saving says it redrew the minimap");
        var after = Minimap.Decode(s.Smf);
        var old = new SmfFile(s.Smf.MapX, s.Smf.MapY);
        before.CopyTo(old.Minimap, 0);
        var was = Minimap.Decode(old);
        int centre = (int)(3072 / 6.0), corner = 50;
        Assert.True(after.GetPixel(centre, centre) != was.GetPixel(centre, centre), "the new island shows on the minimap");
        Assert.Equal(was.GetPixel(corner, corner), after.GetPixel(corner, corner), "far away is unchanged");
    }
}
