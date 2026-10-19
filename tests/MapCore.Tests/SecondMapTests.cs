using MapCore.Editing;
using MapCore.Imaging;
using MapCore.Items;
using MapCore.Rendering;

namespace MapCore.Tests;

/// <summary>The editor's work on a map unlike Acidic Quarry (see <see cref="SyntheticMap"/>).</summary>
static class SecondMapTests
{
    static readonly Lazy<string> archive = new(() => SyntheticMap.Build(TestData.NewWorkDir("synthetic")));

    /// <summary>Opened as the editor opens a map: primed, as for a journal (which needs no splat map to work).</summary>
    static MapEditSession Fresh(string name, string? from = null)
    {
        var s = MapEditSession.Open(MapPackage.Open(from ?? archive.Value, TestData.NewWorkDir(name)));
        s.Prime();
        return s;
    }

    static RgbaImage Intended()
    {
        var image = new RgbaImage(SyntheticMap.Width, SyntheticMap.Height);
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                image.SetPixel(x, y, SyntheticMap.Colour(x, y));
        return image;
    }

    [Test]
    public static void OpensAsBuilt()
    {
        var s = Fresh("synth-open");
        Assert.Equal((2048, 1024), (s.MapWidth, s.MapHeight), "size in game units (not square)");
        Assert.Equal(2, s.Texture.TileFiles.Count, "tile files");
        double psnr = ImageMetrics.Psnr(Intended(), s.TextureImage);
        Report.Info($"synthetic texture after DXT1: PSNR {psnr:0.0} dB");
        Assert.True(psnr > 30, $"the texture decodes to the intended colours across both tile files (PSNR {psnr:0.0} dB)");

        double worst = 0;
        for (int z = 0; z <= SyntheticMap.SquaresZ; z += 3)
            for (int x = 0; x <= SyntheticMap.SquaresX; x += 3)
                worst = Math.Max(worst, Math.Abs(s.Heights.HeightAt(x * 8, z * 8) - SyntheticMap.GroundAt(x * 8, z * 8)));
        Assert.True(worst < 0.01, $"heights as built (worst {worst:0.0000})");
        Assert.True(s.Heights.MinHeight < 0, "some ground is under water");

        var spots = s.Metal.FindSpots();
        Assert.Equal(SyntheticMap.Spots.Length, spots.Count, "metal spots");
        foreach (var (x, z) in SyntheticMap.Spots)
            Assert.True(spots.Any(p => Math.Abs(p.X - x) < 1 && Math.Abs(p.Z - z) < 1), $"a spot at ({x}, {z})");
        Assert.True(s.Metal.ReadOnlyReason == null, "metal is editable");
        Assert.Equal(MapEditSession.DefaultLookRadius, s.LookRadius.Radius, $"look radius without a splat map ({s.LookRadius.How})");

        Assert.True(s.Items.Placer == null, "no featureplacer");
        Assert.True(s.Items.All.Count > 20 && s.Items.All.All(i => i.Source == ItemSource.Smf), $"{s.Items.All.Count} SMF features, nothing else");
        Assert.Equal(2, s.Info.StartPositions.Count, "start positions");
        Assert.Equal(0, s.SplatChannelNames.Count(n => n != null), "no splat channels");

        var errors = s.CheckBeforeSave(requireNewName: false).Where(c => c.Level == CheckLevel.Error).Select(c => c.Message).ToList();
        Assert.Empty(errors, "save-check errors on the unedited map");
    }

    [Test]
    public static void EveryViewDrawsOrSaysWhyNot()
    {
        var s = Fresh("synth-views");
        using var renderer = new MapRenderer(s);
        var target = new RgbaImage(256, 128);
        var view = ViewTransform.Fit(s.MapWidth, s.MapHeight, target.Width, target.Height);
        var problems = new List<string>();
        foreach (var layer in Enum.GetValues<MapViewLayer>())
        {
            if (renderer.Unavailable(layer) is string why)
            {
                Report.Info($"{layer}: {why}");
                continue;
            }
            renderer.Render(target, view, layer, MapOverlays.MetalSpots | MapOverlays.Items | MapOverlays.StartPositions | MapOverlays.WaterLine);
            int distinct = Enumerable.Range(0, target.Width * target.Height).Select(i => target.GetPixel(i % target.Width, i / target.Width)).Distinct().Count();
            if (distinct < 8)
                problems.Add($"{layer} drew only {distinct} colours");
        }
        Assert.True(renderer.Unavailable(MapViewLayer.Splat) != null && renderer.Unavailable(MapViewLayer.Normals) != null,
            "splat and normals say the map has none");
        Assert.Empty(problems, "layers");

        using var terrain = new TerrainView(renderer);
        var picture = new RgbaImage(320, 200);
        terrain.Render(picture, OrbitCamera.Overview(s.MapWidth, s.MapHeight, 320 / 200.0), new TerrainOptions());
        int colours = Enumerable.Range(0, 320 * 200).Select(i => picture.GetPixel(i % 320, i / 320)).Distinct().Count();
        Assert.True(colours > 200, $"the 3D view draws the map ({colours} colours)");
    }

    /// <summary>Edits across both tile files, heights, metal (with its look), SMF items and start positions survive a save to .sdz.</summary>
    [Test]
    public static void EditsSurviveSavingToSdz()
    {
        var s = Fresh("synth-edit");
        var spot = s.Metal.FindSpots().Single(p => Math.Abs(p.X - 1024) < 1);
        var item = s.Items.All[0];
        int itemCount = s.Items.All.Count;

        s.Apply("texture-paint:1024,200:60:255,0,0:1:1"); // across the seam between the two tile files
        s.Apply("height-raise:1500,300:150:50");
        s.Apply("metal-move:1024,512:1120,560:look");
        s.Apply("items-move:30,20:s0");
        s.Apply("item-add:smf:treetype2:900,900:4000");
        s.Apply("start-move:1:1700,520");
        Assert.Throws<InvalidOperationException>(() => s.Apply("item-add:placer:treetype1:500,500"), "a featureplacer item on a map without one");
        Assert.Throws<InvalidOperationException>(() => s.Apply("splat-paint:500,500:50:0,255,0,0"), "splat painting on a map without a splat map");
        Assert.True(Math.Abs(s.Heights.HeightAt(1500, 300) - SyntheticMap.GroundAt(1500, 300) - 50) < 0.01, "raised by 50 at the centre");

        var errors = s.CheckBeforeSave(requireNewName: false).Where(c => c.Level == CheckLevel.Error).Select(c => c.Message).ToList();
        Assert.Empty(errors, "save-check errors after the edits");
        s.Save();
        var saved = Path.Combine(TestData.NewWorkDir("synth-saved"), "synthetic_2.sdz");
        s.Package.SaveAs(saved);

        var r = Fresh("synth-reopened", saved);
        Assert.Equal(2, r.Texture.TileFiles.Count, "still two tile files");
        Assert.True(r.TextureImage.Pixels.AsSpan().SequenceEqual(s.TextureImage.Pixels), "the texture reads back exactly as edited");
        var painted = r.TextureImage.GetPixel(1024, 200);
        Assert.True(painted.R > 230 && painted.G < 30, $"the paint on the seam: {painted}");
        foreach (int column in new[] { 31, 32 })
        {
            int row = 200 / 32;
            var mine = r.Texture.DecodeTile(column, row).Pixels;
            Assert.True(PythonReference.DecodeTile(r.Package.SmfPath, column, row).AsSpan().SequenceEqual(mine),
                $"python decodes tile ({column}, {row}) the same way ({(column < 32 ? "west" : "east")} file)");
        }
        double worst = 0;
        for (int z = 0; z <= SyntheticMap.SquaresZ; z += 2)
            for (int x = 0; x <= SyntheticMap.SquaresX; x += 2)
                worst = Math.Max(worst, Math.Abs(r.Heights.HeightAt(x * 8, z * 8) - s.Heights.HeightAt(x * 8, z * 8)));
        Assert.True(worst < 0.01, $"heights read back as edited (worst {worst:0.0000})");
        var spots = r.Metal.FindSpots();
        Assert.True(spots.Any(p => Math.Abs(p.X - 1120) < 1 && Math.Abs(p.Z - 560) < 1) && !spots.Any(p => Math.Abs(p.X - 1024) < 1),
            "the spot moved");
        var dark = r.TextureImage.GetPixel(1120, 560);
        var old = r.TextureImage.GetPixel(1024, 512);
        Assert.True(dark.R < 80 && old.R > 100, $"its look moved with it (new place {dark}, old place {old})");
        Assert.Equal(itemCount + 1, r.Items.All.Count, "items");
        Assert.True(r.Items.All.Any(i => i.Name == item.Name && Math.Abs(i.X - item.X - 30) < 0.01 && Math.Abs(i.Z - item.Z - 20) < 0.01), "the moved feature");
        Assert.True(r.Items.All.Any(i => i.Name == "treetype2" && Math.Abs(i.X - 900) < 0.01), "the added feature");
        Assert.True(r.Info.StartPositions.Any(p => p.Team == 1 && p.X == 1700 && p.Z == 520), "the moved start position");
    }

    [Test]
    public static void MetalFromAnImageIsNotEdited()
    {
        var dir = TestData.NewWorkDir("synth-metalimage");
        var png = Path.Combine(dir, "metal.png");
        PngWriter.WriteGray8(png, SyntheticMap.SquaresX / 2, SyntheticMap.SquaresZ / 2, new byte[SyntheticMap.SquaresX / 2 * (SyntheticMap.SquaresZ / 2)]);
        var path = SyntheticMap.Build(dir, "synthetic_metalimage.sdz", "\n\t\tmetalmapTex = \"maps/metal.png\",",
            [("maps/metal.png", File.ReadAllBytes(png))]);
        var s = Fresh("synth-metalimage-open", path);
        Assert.True(s.Metal.ReadOnlyReason?.Contains("metalmapTex", StringComparison.Ordinal) == true, $"metal is read-only: {s.Metal.ReadOnlyReason}");
        Assert.Throws<InvalidOperationException>(() => s.Apply("metal-move:416,304:480,304"), "moving metal the game won't read");
        Assert.True(!s.CanUndo, "nothing was recorded");
    }
}
