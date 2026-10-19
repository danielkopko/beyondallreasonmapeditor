using System.Diagnostics;
using MapCore.Editing;
using MapCore.Imaging;
using MapCore.Movement;
using MapCore.WorldMachine;

namespace MapCore.Tests;

/// <summary>
/// World Machine (MapEditSession.WorldMachine.cs): the map sent to an exchange folder, and outputs as World Machine
/// would write them brought back: resampled, the right way up, at the scale sent (or the scale reported), keeping where
/// every unit and building goes even after a whole-map change, as undoable, journaled steps. World Machine itself isn't
/// run here: its outputs are made by the test.
/// </summary>
static class WorldMachineTests
{
    static MapEditSession Open(string name, string? archive = null) =>
        MapEditSession.Open(MapPackage.Open(archive ?? TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir(name)));

    /// <summary>Heights as World Machine would write them at another size (16-bit PNG, 0-65535 over the range sent).</summary>
    static void WriteHeights(string path, float[] heights, int w, int h, double low, double high, int size, bool flip = false, double scale = 1)
    {
        var resampled = WorldMachineFiles.ResampleCorners(heights, w, h, size, size);
        var grey = new ushort[resampled.Length];
        for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                double v = (resampled[z * size + x] - low) / (high - low) * scale;
                grey[(flip ? size - 1 - z : z) * size + x] = (ushort)Math.Clamp(Math.Round(v * 65535), 0, 65535);
            }
        PngWriter.WriteGray16(path, size, size, grey);
    }

    static WorldMachineImport AsSent(WorldMachineInfo info, bool keep = true) => new() { Keep = keep, Range = (info.MinHeight, info.MaxHeight) };

    [Test]
    public static void TheMapIsSentWithWhatItsFilesMean()
    {
        var s = Open("wm-send");
        var folder = TestData.NewWorkDir("wm-send-folder");
        Report.Info(s.SendToWorldMachine(folder));
        var info = WorldMachineInfo.Load(folder)!;
        Assert.True(info.HeightsWidth == s.Heights.Width && info.WorldWidth == s.MapWidth && info.MinHeight == s.Heights.Heights.Min() &&
                    info.MaxHeight == s.Heights.Heights.Max() && info.TextureWidth == s.TextureImage.Width, "bar_map.json records the sizes and the range sent");
        var heights = PngReader.Read(Path.Combine(folder, WorldMachineFiles.Heights));
        Assert.True(heights.BitDepth == 16 && heights.Width == s.Heights.Width && new FileInfo(Path.Combine(folder, WorldMachineFiles.HeightsRaw)).Length == heights.Width * heights.Height * 2L,
            "heights as a 16-bit PNG and RAW16, at the heightmap's size");
        foreach (var file in new[] { WorldMachineFiles.Texture, WorldMachineFiles.WaterMask, WorldMachineFiles.SlopeMask, WorldMachineFiles.CliffMask, WorldMachineFiles.KeepMask, WorldMachineFiles.ReadMe })
            Assert.True(File.Exists(Path.Combine(folder, file)), $"{file} written");
        var readMe = File.ReadAllText(Path.Combine(folder, WorldMachineFiles.ReadMe));
        Assert.True(readMe.Contains($"0 to {info.AltitudeRange:0.###} m", StringComparison.Ordinal) && readMe.Contains("wm_heights.png", StringComparison.Ordinal),
            "README.txt gives the altitude range and the names to write");
        Assert.True(!s.CanUndo, "sending isn't an edit");
    }

    [Test]
    public static void APassThroughComesBackAsItWent()
    {
        var s = Open("wm-pass");
        var folder = TestData.NewWorkDir("wm-pass-folder");
        s.SendToWorldMachine(folder);
        var info = WorldMachineInfo.Load(folder)!;
        var before = (float[])s.Heights.Heights.Clone();
        int w = s.Heights.Width, h = s.Heights.Height;
        // At the heightmap's own size (as README.txt asks): the same heights, to within a 16-bit step.
        var same = Path.Combine(folder, "wm_heights.png");
        WriteHeights(same, before, w, h, info.MinHeight, info.MaxHeight, w);
        string same769 = s.BringBackFromWorldMachine("heights", same, AsSent(info));
        Report.Info(same769);
        Assert.True(same769.Contains("came back as it went", StringComparison.Ordinal), "and says so: the set-up check passes");
        double step = info.AltitudeRange / 65535, worst = s.Heights.Heights.Zip(before).Max(p => Math.Abs(p.First - p.Second));
        Assert.True(worst <= step * 0.51, $"at {w}x{w}, back within half a step ({worst:0.#####})");
        // At 1025 x 1025, World Machine's own common size: resampled twice, near enough.
        s.Undo();
        var larger = Path.Combine(folder, "wm_heights_1025.png");
        WriteHeights(larger, before, w, h, info.MinHeight, info.MaxHeight, 1025);
        string result = s.BringBackFromWorldMachine("heights", larger, AsSent(info, keep: false));
        Report.Info(result);
        double mean = s.Heights.Heights.Zip(before).Average(p => Math.Abs(p.First - p.Second));
        Assert.True(result.Contains("1025x1025 resampled to 769x769", StringComparison.Ordinal) && mean < 0.6 && !result.Contains("upside down", StringComparison.Ordinal),
            $"at 1025x1025, resampled back with corners on corners (mean difference {mean:0.###})");
        // Edited since it was sent: said so.
        s.Undo();
        s.Apply("height-raise:3000,3000:400:30");
        result = s.BringBackFromWorldMachine("heights", same, AsSent(info, keep: false));
        Report.Info(result);
        Assert.True(result.Contains("the map has changed since it was sent", StringComparison.Ordinal), "edits made since sending are pointed out, as they are replaced");
    }

    [Test]
    public static void UpsideDownAndScaledOutputsAreFoundOut()
    {
        var s = Open("wm-flip");
        var folder = TestData.NewWorkDir("wm-flip-folder");
        s.SendToWorldMachine(folder);
        var info = WorldMachineInfo.Load(folder)!;
        var before = (float[])s.Heights.Heights.Clone();
        int w = s.Heights.Width, h = s.Heights.Height;
        var flipped = Path.Combine(folder, "wm_heights.png");
        WriteHeights(flipped, before, w, h, info.MinHeight, info.MaxHeight, w, flip: true);
        string result = s.BringBackFromWorldMachine("heights", flipped, AsSent(info));
        Report.Info(result);
        double worst = s.Heights.Heights.Zip(before).Max(p => Math.Abs(p.First - p.Second));
        Assert.True(result.Contains("upside down", StringComparison.Ordinal) && worst < 0.01, $"an upside-down output is turned the right way up ({worst:0.####})");
        s.Undo();
        var doubled = Path.Combine(folder, "wm_heights_scaled.png");
        WriteHeights(doubled, before, w, h, info.MinHeight, info.MaxHeight, w, scale: 0.5); // World Machine's scale twice the range sent
        result = s.BringBackFromWorldMachine("heights", doubled, AsSent(info, keep: false));
        Report.Info(result);
        Assert.True(result.Contains("look 0.5 times those sent", StringComparison.Ordinal), "a height scale that doesn't match is reported, with what to set");
        s.Undo();
        result = s.BringBackFromWorldMachine("heights", doubled, new WorldMachineImport { Keep = false, Stretch = true });
        worst = s.Heights.Heights.Zip(before).Max(p => Math.Abs(p.First - p.Second));
        Assert.True(worst < 0.05, $"brought back stretched over the map's range, it fits again ({worst:0.###})");
    }

    /// <summary>A whole-map change (World Machine's erosion, say): kept to where units and buildings go, it ends, and access is as it was after saving.</summary>
    [Test]
    public static void AWholeMapChangeKeepsWhereUnitsAndBuildingsGo()
    {
        var s = Open("wm-heavy");
        var folder = TestData.NewWorkDir("wm-heavy-folder");
        s.SendToWorldMachine(folder);
        var info = WorldMachineInfo.Load(folder)!;
        var before = (float[])s.Heights.Heights.Clone();
        int w = s.Heights.Width, h = s.Heights.Height;
        // "Eroded", as World Machine's erosion leaves a map: every height changed, smoothed a little, a gentle swell over
        // the plains (sediment), and gullies cut into the steep ground, up to about 25 deep.
        var eroded = new float[before.Length];
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                double sum = 0;
                int n = 0;
                for (int dz = -2; dz <= 2; dz++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int xx = Math.Clamp(x + dx, 0, w - 1), zz = Math.Clamp(z + dz, 0, h - 1);
                        sum += before[zz * w + xx];
                        n++;
                    }
                int x0 = Math.Max(0, x - 1), x1 = Math.Min(w - 1, x + 1), z0 = Math.Max(0, z - 1), z1 = Math.Min(h - 1, z + 1);
                double steep = Math.Clamp(Math.Sqrt(Math.Pow(before[z * w + x1] - before[z * w + x0], 2) + Math.Pow(before[z1 * w + x] - before[z0 * w + x], 2)) / 16, 0, 1.5);
                double gully = -25 * steep * Math.Pow(Math.Abs(Math.Sin(x * 0.21 + z * 0.08)), 6);
                eroded[z * w + x] = (float)(0.5 * before[z * w + x] + 0.5 * sum / n + 3 * Math.Sin(x * 0.02) * Math.Cos(z * 0.025) + gully);
            }
        var path = Path.Combine(folder, "wm_heights.png");
        WriteHeights(path, eroded, w, h, info.MinHeight, info.MaxHeight, w);
        var access = new AccessMap(s.Heights);
        var clock = Stopwatch.StartNew();
        string result = s.BringBackFromWorldMachine("heights", path, AsSent(info));
        Report.Info($"{result} ({clock.ElapsedMilliseconds} ms)");
        int changed = s.Heights.Heights.Zip(before).Count(p => p.First != p.Second);
        Assert.True(changed > before.Length / 4, $"much of the change is taken ({changed:N0} heights)");
        Assert.True(access.Differences(new AccessMap(s.Heights), 5).Count == 0, "every movement class goes where it went");
        var target = Path.Combine(TestData.NewWorkDir("wm-heavy-saved"), "heavy.sd7");
        s.Save();
        s.Package.SaveAs(target, overwrite: true);
        var reopened = Open("wm-heavy-reopened", target);
        var afterSave = access.Differences(new AccessMap(reopened.Heights), 5);
        Assert.True(afterSave.Count == 0, $"and so after saving and opening again: {string.Join("; ", afterSave)}");
        foreach (var spot in s.Metal.FindSpots())
        {
            int i = (int)Math.Round(spot.Z / 8) * w + (int)Math.Round(spot.X / 8);
            Assert.True(s.Heights.Heights[i] == before[i], $"the metal spot at ({spot.X}, {spot.Z}) stands on the ground it had");
        }
        s.Undo();
        Assert.True(s.Heights.Heights.AsSpan().SequenceEqual(before), "undo puts the heights back exactly");
    }

    [Test]
    public static void TextureSplatAndGrassComeBackAndAJournalReplaysThem()
    {
        var live = Open("wm-layers");
        var folder = TestData.NewWorkDir("wm-layers-folder");
        live.SendToWorldMachine(folder);
        var info = WorldMachineInfo.Load(folder)!;
        var textureBefore = (byte[])live.TextureImage.Pixels.Clone();
        // A 2048 x 2048 bitmap: a gradient.
        var bitmap = new RgbaImage(2048, 2048);
        for (int y = 0; y < 2048; y++)
            for (int x = 0; x < 2048; x++)
                bitmap.SetPixel(x, y, new Rgba((byte)(x / 8), (byte)(y / 8), 90, 255));
        var texturePath = Path.Combine(folder, "wm_texture.png");
        PngWriter.WriteRgba(texturePath, bitmap);
        // A splat map, red in the north-west quarter, green north-east, blue south-west, alpha south-east.
        var splat = new RgbaImage(256, 256);
        for (int y = 0; y < 256; y++)
            for (int x = 0; x < 256; x++)
                splat.SetPixel(x, y, (x < 128, y < 128) switch
                {
                    (true, true) => new Rgba(255, 0, 0, 0),
                    (false, true) => new Rgba(0, 255, 0, 0),
                    (true, false) => new Rgba(0, 0, 255, 0),
                    _ => new Rgba(0, 0, 0, 255),
                });
        var splatPath = Path.Combine(folder, "wm_splat.png");
        PngWriter.WriteRgba(splatPath, splat);
        // Grass in the west half.
        var grass = new byte[200 * 200];
        for (int i = 0; i < grass.Length; i++)
            grass[i] = i % 200 < 100 ? (byte)255 : (byte)0;
        var grassPath = Path.Combine(folder, "wm_grass.png");
        PngWriter.WriteGray8(grassPath, 200, 200, grass);
        var heightsPath = Path.Combine(folder, "wm_heights.png");
        WriteHeights(heightsPath, live.Heights.Heights.Select(v => v + 3f).ToArray(), live.Heights.Width, live.Heights.Height, info.MinHeight, info.MaxHeight, 1025);

        var dir = TestData.NewWorkDir("wm-layers-record");
        using (var journal = new SessionJournal(Path.Combine(dir, "j.journal"), Path.Combine(dir, "files")))
        {
            live.Journal = journal;
            Report.Info(live.BringBackFromWorldMachine("texture", texturePath, new WorldMachineImport()));
            Report.Info(live.BringBackFromWorldMachine("splat", splatPath, new WorldMachineImport { Keep = false }));
            Report.Info(live.BringBackFromWorldMachine("grass", grassPath, new WorldMachineImport()));
            Report.Info(live.BringBackFromWorldMachine("heights", heightsPath, AsSent(info)));
            live.Journal = null;
        }
        File.Delete(texturePath); // World Machine writes it again on its next build: the journal keeps what was read
        var image = live.TextureImage;
        var mid = image.GetPixel(image.Width / 2, image.Height / 4);
        Assert.True(Math.Abs(mid.R - 128) <= 3 && Math.Abs(mid.G - 64) <= 3, $"the 2048 texture is resampled to {image.Width}x{image.Height} ({mid})");
        int spotChanged = 0;
        foreach (var spot in live.Metal.FindSpots())
        {
            int i = ((int)spot.Z * image.Width + (int)spot.X) * 4;
            if (Math.Abs(image.Pixels[i] - textureBefore[i]) > 6 || Math.Abs(image.Pixels[i + 1] - textureBefore[i + 1]) > 6)
                spotChanged++;
        }
        Assert.True(spotChanged == 0, $"metal spots keep their look ({spotChanged} changed)");
        var splatPng = Path.Combine(dir, "splat_back.png");
        live.ExportLayer("resource:splatDistrTex", splatPng);
        var back = PngReader.Read(splatPng).ToRgba();
        Rgba Quarter(double fx, double fy) => back.GetPixel((int)(back.Width * fx), (int)(back.Height * fy));
        Assert.True(Quarter(0.25, 0.25).R > 200 && Quarter(0.75, 0.25).G > 200 && Quarter(0.25, 0.75).B > 200 && Quarter(0.75, 0.75).A > 200,
            "the splat map's red, green, blue and alpha land in the north-west, north-east, south-west and south-east, as drawn");
        var grassMap = live.Smf.GrassMap!;
        int gw = live.Smf.GrassMapWidth;
        Assert.True(grassMap[10 * gw + 5] == 1 && grassMap[10 * gw + gw - 5] == 0, "grass in the west half only");

        var rebuilt = Open("wm-layers-rebuilt");
        SessionJournal.Replay(rebuilt, SessionJournal.Read(Path.Combine(dir, "j.journal")));
        Assert.True(rebuilt.TextureImage.Pixels.AsSpan().SequenceEqual(live.TextureImage.Pixels) && rebuilt.Heights.Heights.AsSpan().SequenceEqual(live.Heights.Heights) &&
                    rebuilt.Smf.GrassMap!.AsSpan().SequenceEqual(live.Smf.GrassMap!), "a replay of the journal gives the same map, though World Machine's file is gone");
        for (int i = 0; i < 4; i++)
            live.Undo();
        Assert.True(live.TextureImage.Pixels.AsSpan().SequenceEqual(textureBefore), "four undos take the four back");
    }

    [Test]
    public static void TheAutomationScriptLoadsBuildsAndWrites()
    {
        string script = WorldMachineFiles.AutomationScript(@"C:\Maps\My Map & Co\project.tmd", 769);
        Report.Info(script);
        var xml = new System.Xml.XmlDocument();
        xml.LoadXml(script);
        var root = xml.DocumentElement!;
        Assert.True(root.Name == "automation" && root.GetAttribute("version") == "WMP2" && root["load"]!.GetAttribute("file") == @"C:\Maps\My Map & Co\project.tmd" &&
                    root["modify"]!["world"]!.GetAttribute("res") == "769" && root["build"]!.GetAttribute("mode") == "normal" && root["output"] != null,
            "load (the path escaped), the resolution, build and output, as World Machine Professional's scripts have them");
    }
}
