using System.Globalization;
using MapCore.Editing;
using MapCore.Heights;
using MapCore.Imaging;
using MapCore.Items;

namespace MapCore.Tests;

static class OperationTests
{
    static MapEditSession Fresh(string name) =>
        MapEditSession.Open(MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir(name)));

    static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

    /// <summary>Every store an operation can change (the decoded texture follows the tiles, so the tiles stand for it).</summary>
    static string State(MapEditSession s) => string.Join("|",
        Hash(s.Smf.ToBytes()), Hash(s.Texture.TileFiles[0].ToBytes()), s.Info.Text, s.Items.Placer!.Document.Text,
        Hash(s.Heights.Heights.SelectMany(BitConverter.GetBytes).ToArray()), Hash(s.ResourceBytes(MapEditSession.SplatKey)),
        string.Join(",", Directory.EnumerateFiles(s.Package.Root, "*", FileSystemUtil.AllEntries).Select(f => FileSystemUtil.EntryPath(s.Package.Root, f)).Order()));

    static string At(double x, double z) => string.Create(CultureInfo.InvariantCulture, $"{x},{z}");

    /// <summary>
    /// Each operation in MapEditSession's list, on its own: it is recorded for undo, it reports a change (so views
    /// redraw), it changes something, and undo restores every store exactly. New operations belong in this list.
    /// </summary>
    [Test]
    public static void EveryOperationIsUndoableAndReported()
    {
        var s = Fresh("ops-all");
        var dir = TestData.NewWorkDir("ops-png");
        var spot = s.Metal.FindSpots()[0];
        var item = s.Items.All.First(i => i.Source == ItemSource.FeaturePlacer);
        string spotAt = At(spot.X, spot.Z), itemAt = At(item.X, item.Z);

        var heightsPng = Path.Combine(dir, "heights.png");
        s.ExportLayer("heights", heightsPng);
        var heights = PngReader.Read(heightsPng);
        var raw = heights.ToGray16();
        for (int i = 300 * 769 + 300; i < 300 * 769 + 340; i++)
            raw[i] = (ushort)Math.Min(65535, raw[i] + 3000);
        PngWriter.WriteGray16(heightsPng, heights.Width, heights.Height, raw, heights.Text);

        var metalPng = Path.Combine(dir, "metal.png");
        s.ExportLayer("metal", metalPng);
        var metal = PngReader.Read(metalPng).ToGray8();
        for (int z = 190; z < 193; z++)
            metal.AsSpan(z * 384 + 190, 3).Fill(120);
        PngWriter.WriteGray8(metalPng, 384, 384, metal);

        var grassPng = Path.Combine(dir, "grass.png");
        var grass = new byte[192 * 192];
        grass.AsSpan(5000, 40).Fill(1);
        PngWriter.WriteGray8(grassPng, 192, 192, grass);

        string[] ops =
        [
            "set:atmosphere.fogColor=0.1,0.2,0.3",
            "start-move:1:5000,1200",
            $"metal-move:{spotAt}:{At(spot.X + 64, spot.Z + 32)}",
            $"metal-move:{spotAt}:{At(spot.X + 64, spot.Z + 32)}:look",
            "metal-add:3072,3072",
            $"metal-remove:{spotAt}",
            $"metal-income:{spotAt}:1.5",
            $"metal-resize:{spotAt}:4",
            $"item-move:{itemAt}:{At(item.X + 50, item.Z)}",
            $"item-rotate:{itemAt}:8000",
            "item-add:smf:agorm_rock1:3000,3000:100",
            "item-add:placer:agorm_rock2:3100,3100",
            $"item-remove:{itemAt}",
            "items-move:10,5:f0;f1",
            "items-rotate:45:f2",
            "items-heading:90:f3",
            "items-remove:f4;f6",
            "grass-paint:3000,3000:100:1",
            "height-raise:3072,3072:200:30",
            "height-flatten:3072,3072:200:50:0.5:0.5",
            "height-smooth:3500,3500:150:0.8:0.3",
            "height-noise:2000,2000:300:20:0.5:96",
            "height-offset:5",
            "height-scale:1.2",
            "ramp:2000,2600:2400,2600:120:-20,150:40:1",
            "ramp:3000,3000:3346.41,3200:100:auto,auto:auto:1",
            "ramp:4300,3000:4300,3500:120:auto,auto:32:0",
            "ramp:1380,900:2180,900:200:auto,auto:auto:2:0.6:auto:auto",
            "ramp:3000,3000:3346.41,3200:100:auto,auto:auto:1:0.8",
            "texture-paint:1150,800:60:20,200,255:0.7",
            "texture-tint:1150,800:60:220,40,40:0.7",
            "texture-adjust:2000,3000:80:1.3,0.5,40",
            "texture-clone:2600,3400:50:-1500,-2450:0.8",
            "splat-paint:1500,900:120:0,255,0,0:0.8:0.9",
            $"import:heights={heightsPng}",
            $"import:metal={metalPng}",
            $"import:grass={grassPng}",
            "feature-custom:ops_monolith:agorm_rock1:" + MapCore.Items.CustomFeatureSettings.FromDefinition(
                MapCore.Items.FeatureDefs.Load(s.Package.Root).SelectMany(f => f.Features).First(f => f.Name == "agorm_rock1")).WithAncientMonolith().Encode(),
        ];

        var problems = new List<string>();
        int reports = 0;
        s.Changed += (_, change) => reports += change.Layers != MapLayers.None ? 1 : 0;
        foreach (var op in ops)
        {
            var before = State(s);
            reports = 0;
            try
            {
                s.Apply(op);
            }
            catch (InvalidOperationException ex)
            {
                problems.Add($"{op}: failed: {ex.Message}");
                continue;
            }
            if (!s.CanUndo)
                problems.Add($"{op}: not recorded for undo");
            if (reports != 1)
                problems.Add($"{op}: reported {reports} changes, expected 1");
            if (State(s) == before)
                problems.Add($"{op}: changed nothing");
            s.Undo();
            if (State(s) != before)
                problems.Add($"{op}: undo did not restore every store");
            if (s.CanUndo)
                problems.Add($"{op}: something is left to undo");
        }
        Assert.Empty(problems, "operations");
    }

    [Test]
    public static void TypedHelpersWorkInACommaDecimalLocale()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1,5", 1.5.ToString(), "the test locale writes a decimal comma");
            var s = Fresh("ops-locale");
            float before = s.Heights.HeightAt(1000.5, 900.25);
            s.RaiseHeights(new Brush(1000.5, 900.25, 120.5, 0.5), 2.5);
            Assert.True(Math.Abs(s.Heights.HeightAt(1000.5, 900.25) - before - 2.5) < 1e-3, "raised by 2.5 at the brush centre");
            s.FlattenHeights(new Brush(2000.5, 2000.5, 100.5, 0.5), 12.25, 0.5);
            s.SmoothHeights(new Brush(2500.5, 2500.5, 90.5, 0.25), 0.5);
            s.AddHeightNoise(new Brush(3000.5, 3000.5, 150.5, 0.5), 7.5, 80.5);
            s.OffsetHeights(-0.5);
            s.ScaleHeights(1.5);
            s.PaintTexture(new Brush(1100.5, 800.5, 30.5, 0.5), new Rgba(1, 2, 3, 255), 0.75);
            s.BuildRamp(new Ramp(1000.5, 900.25, 1300.5, 1000.25, 120.5, 30.5), 10.5, 60.5);
            int steps = 0;
            while (s.CanUndo)
            {
                s.Undo();
                steps++;
            }
            Assert.Equal(8, steps, "every helper made one undo step");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Test]
    public static void NoiseIsRepeatableSmoothAndBounded()
    {
        double min = 1, max = -1, worstStep = 0;
        for (int i = 0; i < 200; i++)
            for (int j = 0; j < 200; j++)
            {
                double x = i * 0.037, z = j * 0.041;
                double v = TerrainNoise.Sample(x, z);
                Assert.True(v == TerrainNoise.Sample(x, z), "same position, same value");
                (min, max) = (Math.Min(min, v), Math.Max(max, v));
                worstStep = Math.Max(worstStep, Math.Abs(TerrainNoise.Sample(x + 0.01, z) - v));
            }
        Report.Info($"noise range {min:0.00} .. {max:0.00}, largest change over 1% of a bump {worstStep:0.000}");
        Assert.True(min >= -1 && max <= 1 && min < -0.5 && max > 0.5, "noise spans most of -1 .. 1 and never leaves it");
        Assert.True(worstStep < 0.1, "noise is smooth");

        var s = Fresh("ops-noise");
        var before = (float[])s.Heights.Heights.Clone();
        s.AddHeightNoise(new Brush(2000, 2000, 300, 0.5), 20, 96);
        double biggest = s.Heights.Heights.Zip(before).Max(p => Math.Abs(p.First - p.Second));
        Assert.True(biggest <= 20.001 && biggest > 6, $"noise changes heights by up to its amplitude (largest change {biggest:0.0})");
    }

    [Test]
    public static void FlattenStrengthScalesThePull()
    {
        var s = Fresh("ops-flatten");
        float start = s.Heights.HeightAt(3072, 3072);
        s.FlattenHeights(new Brush(3072, 3072, 200, 0.5), start + 40, 0.5);
        Assert.True(Math.Abs(s.Heights.HeightAt(3072, 3072) - (start + 20)) < 0.01, "half strength goes half way at the core");
        s.FlattenHeights(new Brush(3072, 3072, 200, 0.5), start + 40);
        Assert.True(Math.Abs(s.Heights.HeightAt(3072, 3072) - (start + 40)) < 0.01, "full strength reaches the target at the core");
    }
}
