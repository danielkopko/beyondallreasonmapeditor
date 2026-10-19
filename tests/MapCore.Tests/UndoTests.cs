using MapCore.Editing;
using MapCore.Imaging;
using MapCore.Smf;
using MapCore.Textures;

namespace MapCore.Tests;

static class UndoTests
{
    static MapEditSession Fresh(string name) =>
        MapEditSession.Open(MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir(name)));

    /// <summary>Everything a session can change, as comparable values.</summary>
    static Dictionary<string, string> State(MapEditSession s, string pngDir)
    {
        var splat = Path.Combine(pngDir, $"splat{Guid.NewGuid():N}.png");
        s.ExportLayer("resource:splatDistrTex", splat);
        string Hash(byte[] b) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b));
        return new()
        {
            ["smf"] = Hash(s.Smf.ToBytes()),
            ["smt"] = Hash(s.Texture.TileFiles[0].ToBytes()),
            ["mapinfo"] = s.Info.Text,
            ["placer"] = s.Items.Placer!.Document.Text,
            ["heights"] = Hash(s.Heights.Heights.SelectMany(BitConverter.GetBytes).ToArray()),
            ["texture"] = Hash(s.TextureImage.Pixels),
            ["splat"] = Hash(PngReader.Read(splat).ToRgba().Pixels),
        };
    }

    static void AssertSame(Dictionary<string, string> expected, Dictionary<string, string> actual, string when)
    {
        var different = expected.Keys.Where(k => expected[k] != actual[k]).ToList();
        Assert.Empty(different, $"stores that differ {when}");
    }

    [Test]
    public static void UndoAndRedoRestoreEveryStoreExactly()
    {
        var s = Fresh("undo-all");
        var dir = TestData.NewWorkDir("undo-png");
        var splatPng = Path.Combine(dir, "edited_splat.png");
        s.ExportLayer("resource:splatDistrTex", splatPng);
        var splat = PngReader.Read(splatPng).ToRgba();
        splat.Fill(new PixelRect(300, 300, 40, 40), new Rgba(0, 0, 200, 255));
        PngWriter.WriteRgba(splatPng, splat);
        var grassPng = Path.Combine(dir, "grass.png");
        var grass = new byte[192 * 192];
        grass.AsSpan(1000, 50).Fill(100);
        PngWriter.WriteGray8(grassPng, 192, 192, grass);

        var before = State(s, dir);
        string[] ops =
        [
            "set:atmosphere.fogColor=0.1,0.2,0.3",
            "start-move:2:5000,4800",
            "metal-move:1000,872:1064,904",
            "item-add:smf:agorm_rock1:3000,3000:100",
            "item-add:placer:agorm_rock2:3100,3100",
            "item-move:5809,3562:5700,3500",
            "height-raise:3072,3072:200:30",
            "height-smooth:3500,3500:150",
            "texture-paint:1150,800:60:20,200,255:0.7",
            $"import:resource:splatDistrTex={splatPng}",
            $"import:grass={grassPng}",
        ];
        foreach (var op in ops)
            s.Apply(op);
        var after = State(s, dir);
        Assert.True(before.Keys.All(k => before[k] != after[k]), "every store was changed by the operations");

        for (int i = 0; i < ops.Length; i++)
            s.Undo();
        Assert.True(!s.CanUndo, "nothing left to undo");
        AssertSame(before, State(s, dir), "after undoing everything");
        Assert.True(!s.Heights.HasChanges, "heights back at the baseline: nothing to store, no normal-map rotation");
        Assert.Equal(0, GrassLayer.CellsWithGrass(s.Smf), "grass map empty again (Acidic Quarry has an empty one)");

        for (int i = 0; i < ops.Length; i++)
            s.Redo();
        AssertSame(after, State(s, dir), "after redoing everything");
    }

    [Test]
    public static void BrushStrokeUndoesAsOneStep()
    {
        var s = Fresh("undo-group");
        var smt = s.Texture.TileFiles[0].ToBytes();
        var minimap = (byte[])s.Smf.Minimap.Clone();
        s.BeginGroup("paint stroke");
        for (int i = 0; i < 5; i++)
            s.Apply($"texture-paint:{1000 + i * 30},900:40:255,0,0");
        s.EndGroup();
        Assert.Equal("paint stroke", s.UndoDescription, "one undo step for the stroke");
        s.Undo();
        Assert.True(!s.CanUndo, "the stroke was a single step");
        Assert.True(s.Texture.TileFiles[0].ToBytes().AsSpan().SequenceEqual(smt), "tiles restored");
        Assert.True(s.Smf.Minimap.AsSpan().SequenceEqual(minimap), "minimap restored");
    }

    /// <summary>A long paint stroke is hundreds of dabs: each must keep only what it touched, not whole stores.</summary>
    [Test]
    public static void LongStrokeUndoStaysSmallAndExact()
    {
        var s = Fresh("undo-memory");
        var smt = s.Texture.TileFiles[0].ToBytes();
        var minimap = (byte[])s.Smf.Minimap.Clone();
        var index = (int[])s.Smf.TileIndex.Clone();
        s.BeginGroup("long stroke");
        for (int i = 0; i < 300; i++)
            s.PaintTexture(new MapCore.Heights.Brush(500 + i * 10, 2000 + 40 * Math.Sin(i * 0.1), 40, 0.5), new Rgba(200, 30, 30, 255), 0.5);
        s.EndGroup();
        long bytes = s.UndoBytes;
        Report.Info($"a 300-dab paint stroke keeps {bytes / 1048576.0:0.0} MB for undo");
        Assert.True(bytes < 8 << 20, "a long stroke's undo stays small");
        s.Undo();
        Assert.True(s.Texture.TileFiles[0].ToBytes().AsSpan().SequenceEqual(smt) && s.Smf.Minimap.AsSpan().SequenceEqual(minimap) &&
                    s.Smf.TileIndex.AsSpan().SequenceEqual(index), "and undoes exactly: tiles, tile index and minimap");
    }

    [Test]
    public static void FailedOperationLeavesNothingBehind()
    {
        var s = Fresh("undo-fail");
        var metal = (byte[])s.Smf.MetalMap.Clone();
        // The spot is added, then setting 3 metal/s fails (a 3x3 spot tops out at 2.39): the add must not remain.
        Assert.Throws<InvalidOperationException>(() => s.Apply("metal-add:3072,3072:3"), "income too high");
        Assert.True(s.Smf.MetalMap.AsSpan().SequenceEqual(metal), "metal map untouched");
        Assert.True(!s.CanUndo, "nothing recorded");
    }

    [Test]
    public static void UndoWorksAcrossASave()
    {
        var s = Fresh("undo-save");
        var raw = (ushort[])s.Smf.Heightmap.Clone();
        s.Apply("height-raise:3072,3072:200:30");
        s.Save();
        Assert.True(!s.Smf.Heightmap.AsSpan().SequenceEqual(raw), "raise saved");
        s.Undo();
        Assert.True(s.Heights.HasChanges, "undoing a saved edit is a change against the saved state");
        s.Save();
        Assert.True(s.Smf.Heightmap.AsSpan().SequenceEqual(raw), "saving after undo restores the original heights exactly");
    }

    [Test]
    public static void CopiedSharedTilesAreUndone()
    {
        var smf = new SmfFile(128, 128);
        smf.TileFiles.Add(new("shared.smt", 1));
        var smt = new SmtFile();
        smt.AddTile(new byte[SmtFile.TileBytes]);
        var texture = new MapTexture(smf, [smt]);
        var state = texture.Capture(new PixelRect(100, 100, 10, 10));
        var image = texture.Decode();
        image.Fill(new PixelRect(100, 100, 10, 10), new Rgba(255, 255, 255, 255));
        texture.Update(image, new PixelRect(100, 100, 10, 10));
        Assert.Equal(2, smt.TileCount, "the shared tile was copied");

        var redoState = texture.Capture(new PixelRect(100, 100, 10, 10));
        texture.Restore(state);
        Assert.Equal((1, 1), (smt.TileCount, smf.TileFiles[0].TileCount), "copy removed on undo");
        Assert.True(smf.TileIndex.All(t => t == 0), "tile index restored");
        texture.Restore(redoState);
        Assert.Equal(2, smt.TileCount, "copy back on redo");
        Assert.Equal(new Rgba(255, 255, 255, 255), texture.DecodeTile(3, 3).GetPixel(8, 8), "painted pixels back on redo");
    }
}
