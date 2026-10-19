using MapCore.Editing;
using MapCore.Heights;
using MapCore.Imaging;
using MapCore.Smf;
using MapCore.Textures;

namespace MapCore.Tests;

static class HeightTests
{
    static HeightMap Load(out SmfFile smf, out MapInfo info)
    {
        smf = TestData.AcidicQuarry.LoadSmf();
        info = MapInfo.Load(TestData.AcidicQuarry.MapInfoPath);
        return HeightMap.Load(smf, info);
    }

    [Test]
    public static void DecodesHeightsExactlyAsTheEngine()
    {
        var heights = Load(out var smf, out _);
        Assert.Equal((-97f, 353f), (heights.MinHeight, heights.MaxHeight), "mapinfo range, not the SMF header's -50..100");
        float mod = (353f - -97f) / 65536.0f; // SMFReadMap: (maxH - minH) / 65536, in float
        for (int i = 0; i < smf.Heightmap.Length; i += 9973)
            Assert.Equal(-97f + smf.Heightmap[i] * mod, heights.Heights[i], $"vertex {i}");
        Assert.Equal(-97f + 65535 * mod, heights.Heights.Max(), "the highest raw value is one step below maxheight");
    }

    [Test]
    public static void RaisingOneRegionLeavesEverythingElseExact()
    {
        var heights = Load(out var smf, out _);
        var before = (ushort[])smf.Heightmap.Clone();
        var touched = heights.Raise(new Brush(3072, 3072, 200, 0.5), 20);
        var result = heights.Write();
        Assert.True(!result.RangeChanged, "a 20-unit raise in the sea stays inside the range");

        int outsideChanged = 0, inside = 0;
        for (int z = 0; z < heights.Height; z++)
            for (int x = 0; x < heights.Width; x++)
            {
                int i = z * heights.Width + x;
                bool inRegion = x >= touched.X && x < touched.Right && z >= touched.Y && z < touched.Bottom;
                if (!inRegion && smf.Heightmap[i] != before[i])
                    outsideChanged++;
                if (inRegion)
                    inside++;
            }
        Assert.Equal(0, outsideChanged, "raw values outside the edited region");
        Assert.True(result.VerticesWritten <= inside, "only edited vertices re-quantised");

        var reloaded = HeightMap.Load(SmfFile.Load(smf.ToBytes()), MapInfo.Load(TestData.AcidicQuarry.MapInfoPath));
        Assert.True(Math.Abs(reloaded.HeightAt(3072, 3072) - heights.HeightAt(3072, 3072)) <= heights.Step, "edited height survives storage within one step");
        Assert.True(Math.Abs(reloaded.HeightAt(3072, 3072) - (heights.OriginalHeightAt(3072, 3072) + 20)) <= heights.Step, "centre raised by 20");
    }

    [Test]
    public static void WideningTheRangeKeepsUntouchedTerrainWithinOneStep()
    {
        var heights = Load(out var smf, out var info);
        var original = heights.Heights.ToArray();
        heights.Raise(new Brush(1100, 950, 300, 0.6), 80); // NW base plateau (~346) goes above 353
        var result = heights.Write();
        Assert.True(result.RangeChanged, "range widened");
        Assert.Equal((-97f, result.MaxHeight), (smf.MinHeight, smf.MaxHeight), "SMF header updated");
        Assert.Equal((double)result.MaxHeight, info.MaxHeight, "mapinfo smf.maxheight updated");
        Assert.True(result.MaxHeight >= 346 + 80, $"new max {result.MaxHeight} covers the raised plateau");

        var reloaded = HeightMap.Load(SmfFile.Load(smf.ToBytes()), info);
        double step = reloaded.Step, worst = 0;
        var brush = new Brush(1100, 950, 300, 0.6);
        for (int z = 0; z < reloaded.Height; z++)
            for (int x = 0; x < reloaded.Width; x++)
                if (brush.Weight(x * 8, z * 8) == 0)
                    worst = Math.Max(worst, Math.Abs(reloaded.Heights[z * reloaded.Width + x] - original[z * reloaded.Width + x]));
        Report.Info($"range now {result.MinHeight} .. {result.MaxHeight} (step {step:0.0000}); worst untouched-vertex change {worst:0.0000}");
        Assert.True(worst <= step, $"untouched terrain moved by {worst}, more than one step ({step})");
        Assert.True(Math.Abs(reloaded.HeightAt(1100, 950) - (heights.OriginalHeightAt(1100, 950) + 80)) <= step, "plateau centre raised by 80");
    }

    [Test]
    public static void FlattenSmoothOffsetAndScale()
    {
        var heights = Load(out _, out _);
        heights.Flatten(new Brush(3072, 3072, 160, 0.5), 10);
        Assert.True(Math.Abs(heights.HeightAt(3072, 3072) - 10) < 1e-4, "flat inside the hard core");
        Assert.True(Math.Abs(heights.HeightAt(3072 + 64, 3072) - 10) < 1e-4, "still flat at 64 units (core is 80)");

        var noisy = Load(out _, out _);
        double Roughness(HeightMap h) => Enumerable.Range(370, 20).Sum(x => Math.Abs(h[x, 380] - h[x + 1, 380]));
        double before = Roughness(noisy);
        noisy.Smooth(new Brush(3040, 3040, 200, 0.8));
        Assert.True(Roughness(noisy) < before, "smoothing reduces bumpiness of the sea floor");

        var whole = Load(out _, out _);
        float h0 = whole[100, 100];
        whole.Offset(5);
        Assert.Equal(h0 + 5, whole[100, 100], "offset");
        whole.Scale(2);
        Assert.Equal((h0 + 5) * 2, whole[100, 100], "scale around sea level");
        Assert.Equal(new PixelRect(0, 0, 769, 769), whole.ChangedRegion, "whole-map edits mark everything");
    }

    [Test]
    public static void RotationMakesAFlatBakeFollowANewSlope()
    {
        // 16x16-square map, flat at height 0, with a flat world-space bake (every pixel straight up).
        var smf = new SmfFile(16, 16) { MinHeight = -100, MaxHeight = 100 };
        Array.Fill(smf.Heightmap, (ushort)32768);
        var heights = HeightMap.Load(smf, null);
        var image = new RgbaImage(128, 128);
        image.Fill(image.Bounds, new Rgba(128, 128, 255, 255));

        heights.Raise(new Brush(64, 64, 48, 0.3), 30); // a mound in the middle
        var changed = DetailNormalMap.FollowHeightChange(image, heights, image.Bounds);
        Assert.True(!changed.IsEmpty, "pixels on the new slopes changed");

        // On the mound's east flank the baked normal should now point like the new terrain normal.
        int px = 64 + 32, py = 64;
        var p = image.GetPixel(px, py);
        var baked = Normalize((p.R / 127.5 - 1, p.B / 127.5 - 1, p.G / 127.5 - 1));
        var terrain = heights.NormalAt(px + 0.5, py + 0.5);
        double angle = Math.Acos(Math.Clamp(baked.X * terrain.X + baked.Y * terrain.Y + baked.Z * terrain.Z, -1, 1)) * 180 / Math.PI;
        Assert.True(angle < 2, $"baked normal {angle:0.00}° from the new terrain normal");
        Assert.True(terrain.X > 0.1, "the east flank leans east");
        Assert.Equal(new Rgba(128, 128, 255, 255), image.GetPixel(2, 2), "far corner untouched");
    }

    [Test]
    public static void SessionHeightEditUpdatesTheNormalMapOnlyWhereNeeded()
    {
        var package = MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir("heights"));
        var normalPath = Path.Combine(package.Root, "maps", "normalmap.dds");
        var before = DdsFile.Load(normalPath);

        var session = MapEditSession.Open(package);
        session.Apply("height-raise:1100,950:300:80:0.6");
        var notes = session.Save();
        Report.Info(string.Join(" | ", notes));
        Assert.True(notes.Any(n => n.Contains("rotated")), "normal map rotated (Acidic Quarry's is a slope bake)");
        Assert.True(notes.Any(n => n.Contains("widened")), "height range widened");

        var after = DdsFile.Load(normalPath);
        // Only blocks near the edit may change: the edit spans world x 800-1400, z 650-1250; in the upside-down file,
        // rows count from the south edge.
        var region = PixelRect.FromEdges(800 - 32, 6144 - 1250 - 32, 1400 + 32, 6144 - 650 + 32);
        var beforeLevel = before.LevelData().Span;
        var afterLevel = after.LevelData().Span;
        int outside = 0, inside = 0, across = 6144 / 4;
        for (int b = 0; b < beforeLevel.Length / 8; b++)
        {
            if (beforeLevel.Slice(b * 8, 8).SequenceEqual(afterLevel.Slice(b * 8, 8)))
                continue;
            int x = b % across * 4, y = b / across * 4;
            if (x >= region.X && x < region.Right && y >= region.Y && y < region.Bottom)
                inside++;
            else
                outside++;
        }
        Assert.Equal(0, outside, "normal-map blocks changed away from the edit");
        Assert.True(inside > 100, $"blocks around the edit changed ({inside})");

        var reopened = MapEditSession.Open(MapPackage.OpenFolder(package.Root));
        Assert.True(reopened.Heights.HeightAt(1100, 950) > 420, $"plateau raised: {reopened.Heights.HeightAt(1100, 950)}");
        Assert.True(reopened.Info.MaxHeight > 353, "mapinfo range saved");
    }

    static (double X, double Y, double Z) Normalize((double X, double Y, double Z) v)
    {
        double l = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return (v.X / l, v.Y / l, v.Z / l);
    }
}
