using System.Globalization;
using MapCore.Editing;
using MapCore.Heights;
using MapCore.Imaging;
using MapCore.Metal;

namespace MapCore.Tests;

static class MetalLookTests
{
    static MapEditSession Fresh(string name) =>
        MapEditSession.Open(MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir(name)));

    static string At(double x, double z) => string.Create(CultureInfo.InvariantCulture, $"{x},{z}");

    static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

    static string State(MapEditSession s) => string.Join("|", Hash(s.Smf.ToBytes()), Hash(s.Texture.TileFiles[0].ToBytes()),
        Hash(s.ResourceBytes(MapEditSession.SplatKey)), Hash(s.ResourceBytes("detailNormalTex")));

    static RgbaImage Splat(MapEditSession s)
    {
        var (w, h) = s.SplatSize;
        var splat = new RgbaImage(w, h);
        s.ReadSplat(splat, splat.Bounds);
        return splat;
    }

    /// <summary>The molten-metal (R) weight at a world point, bilinear.</summary>
    static double R(MapEditSession s, RgbaImage splat, double x, double z)
    {
        double u = Math.Clamp(x * splat.Width / s.MapWidth - 0.5, 0, splat.Width - 2), v = Math.Clamp(z * splat.Height / s.MapHeight - 0.5, 0, splat.Height - 2);
        int x0 = (int)u, y0 = (int)v;
        double fx = u - x0, fy = v - y0;
        return (splat.GetPixel(x0, y0).R * (1 - fx) + splat.GetPixel(x0 + 1, y0).R * fx) * (1 - fy) +
               (splat.GetPixel(x0, y0 + 1).R * (1 - fx) + splat.GetPixel(x0 + 1, y0 + 1).R * fx) * fy;
    }

    /// <summary>Mean difference between the R weights in two discs, offset for offset (every 4 units within the radius).</summary>
    static double Compare(MapEditSession s, RgbaImage a, double ax, double az, RgbaImage b, double bx, double bz, double radius)
    {
        double total = 0;
        int n = 0;
        for (double dz = -radius; dz <= radius; dz += 4)
            for (double dx = -radius; dx <= radius; dx += 4)
                if (dx * dx + dz * dz <= radius * radius)
                {
                    total += Math.Abs(R(s, a, ax + dx, az + dz) - R(s, b, bx + dx, bz + dz));
                    n++;
                }
        return total / n;
    }

    /// <summary>Centroid of the R weight above the ring just outside the blob.</summary>
    static (double X, double Z) Centroid(MapEditSession s, RgbaImage splat, double x, double z, double reach)
    {
        double ring = Enumerable.Range(0, 48).Average(k => R(s, splat, x + (reach + 12) * Math.Cos(k * Math.PI / 24), z + (reach + 12) * Math.Sin(k * Math.PI / 24)));
        double total = 0, sx = 0, sz = 0;
        for (double dz = -reach; dz <= reach; dz += 2)
            for (double dx = -reach; dx <= reach; dx += 2)
                if (dx * dx + dz * dz <= reach * reach)
                {
                    double v = Math.Max(0, R(s, splat, x + dx, z + dz) - ring);
                    (total, sx, sz) = (total + v, sx + v * (x + dx), sz + v * (z + dz));
                }
        return (sx / total, sz / total);
    }

    [Test]
    public static void LookRadiusIsMeasuredFromTheSplat()
    {
        var s = Fresh("look-radius");
        var (radius, how) = s.LookRadius;
        Report.Info($"look radius {radius} units, {how}");
        Assert.True(how.StartsWith("measured: splat channel R", StringComparison.Ordinal) && radius is >= 32 and <= 64,
            "Acidic Quarry's metal look is the molten-metal splat channel (R), about 48 units across the edge (the plan's estimate)");
    }

    /// <summary>A small nudge: the blob moves with the spot, by the same amount, unchanged.</summary>
    [Test]
    public static void NudgeMovesTheBlobWithTheSpot()
    {
        var s = Fresh("look-nudge");
        var spot = s.Metal.FindSpots()[0];
        var (radius, _) = s.LookRadius;
        var before = Splat(s);
        var c0 = Centroid(s, before, spot.X, spot.Z, radius * 1.5);
        s.Apply($"metal-move:{At(spot.X, spot.Z)}:{At(spot.X + 32, spot.Z)}:look");
        var after = Splat(s);
        var c1 = Centroid(s, after, spot.X + 32, spot.Z, radius * 1.5);
        double same = Compare(s, before, spot.X, spot.Z, after, spot.X + 32, spot.Z, radius);
        Report.Info($"nudge 32 east: blob centroid ({c0.X:0.0}, {c0.Z:0.0}) -> ({c1.X:0.0}, {c1.Z:0.0}); the blob's weights differ by {same:0.0} on average from before");
        Assert.True(Math.Abs(c1.X - c0.X - 32) <= 4 && Math.Abs(c1.Z - c0.Z) <= 4, "the blob's centre moved 32 units east, like the spot");
        Assert.True(same < 12, "the blob arrived unchanged");
    }

    /// <summary>A far move: the new place gets the look; the old place is filled from matching ground nearby.</summary>
    [Test]
    public static void FarMoveCarriesTheLookAndFillsTheOldPlace()
    {
        var s = Fresh("look-far");
        var spot = s.Metal.FindSpots()[0];
        var (radius, _) = s.LookRadius;
        var before = Splat(s);
        string result = s.Apply($"metal-move:{At(spot.X, spot.Z)}:{At(3072, 3072)}:look");
        var moved = s.Metal.SpotNear(3072, 3072, 32)!;
        var after = Splat(s);
        double arrived = Compare(s, before, spot.X, spot.Z, after, moved.X, moved.Z, radius);
        // How much the old place stands out from its own rim (just outside the look) in the molten-metal channel.
        double Contrast(RgbaImage splat, double x, double z)
        {
            double inside = Enumerable.Range(0, 48).Average(k => R(s, splat, x + (k % 3) * radius / 4 * Math.Cos(k * Math.PI / 24), z + (k % 3) * radius / 4 * Math.Sin(k * Math.PI / 24)));
            double rim = Enumerable.Range(0, 48).Average(k => R(s, splat, x + radius * 1.8 * Math.Cos(k * Math.PI / 24), z + radius * 1.8 * Math.Sin(k * Math.PI / 24)));
            return inside - rim;
        }
        double oldBefore = Contrast(before, spot.X, spot.Z), oldAfter = Contrast(after, spot.X, spot.Z);
        Report.Info($"far move: {result}");
        Report.Info($"new place vs the old look {arrived:0.0}; the old place stands out from its rim by {oldBefore:0} before, {oldAfter:0} after");
        Assert.True(arrived < 10, "the look arrived unchanged");
        Assert.True(Math.Abs(oldAfter) < oldBefore * 0.3, "the old place no longer stands out: the blob is gone");
        Assert.True(result.Contains("filled smoothly from the ground around it", StringComparison.Ordinal) && result.Contains("different ground", StringComparison.Ordinal),
            "on this small plateau the old place is filled from its own surroundings, and the move onto the sea is flagged");
    }

    [Test]
    public static void MoveWithLookUndoesAndRedoesExactly()
    {
        var s = Fresh("look-undo");
        var spot = s.Metal.FindSpots()[3];
        var before = State(s);
        s.Apply($"metal-move:{At(spot.X, spot.Z)}:{At(spot.X - 48, spot.Z + 16)}:look");
        var after = State(s);
        Assert.True(before.Split('|').Zip(after.Split('|')).All(p => p.First != p.Second), "the move changed the SMF, tiles, splat and normal map");
        s.Undo();
        Assert.Equal(before, State(s), "after undo: SMF, tiles, splat and normal map");
        s.Redo();
        Assert.Equal(after, State(s), "after redo");
    }

    /// <summary>
    /// Acidic Quarry has no shared tiles, so make some: the tiles under the destination point at the tiles under the
    /// spot. Editing them then copies tiles; undo and redo must still restore every byte.
    /// </summary>
    [Test]
    public static void MoveWithLookOnSharedTilesUndoesExactly()
    {
        var s = Fresh("look-shared");
        var spot = s.Metal.FindSpots()[0];
        double bx = spot.X + 640, bz = spot.Z + 640;
        int cols = s.Smf.TileColumns, shift = 640 / 32;
        for (int ty = (int)(bz - 160) / 32; ty <= (int)(bz + 160) / 32; ty++)
            for (int tx = (int)(bx - 160) / 32; tx <= (int)(bx + 160) / 32; tx++)
                s.Smf.TileIndex[ty * cols + tx] = s.Smf.TileIndex[(ty - shift) * cols + tx - shift];
        int tilesBefore = s.Texture.TileFiles[0].TileCount;
        var before = State(s);
        s.Apply($"metal-move:{At(spot.X, spot.Z)}:{At(bx, bz)}:look");
        Assert.True(s.Texture.TileFiles[0].TileCount > tilesBefore, "shared tiles were copied before editing");
        var after = State(s);
        s.Undo();
        Assert.Equal(before, State(s), "after undo");
        Assert.Equal(tilesBefore, s.Texture.TileFiles[0].TileCount, "tile copies removed");
        s.Redo();
        Assert.Equal(after, State(s), "after redo");
        s.Undo();
        Assert.Equal(before, State(s), "after undo again");
    }

    [Test]
    public static void SpotNotesFollowTheEnginesBuildCheck()
    {
        var s = Fresh("look-notes");
        var spots = s.Metal.FindSpots();
        Assert.True(spots.All(p => s.SpotNotes(p).Count == 0), "Acidic Quarry's spots are all buildable: " +
            string.Join("; ", spots.SelectMany(p => s.SpotNotes(p))));
        var spot = spots[0];
        s.RaiseHeights(new Brush(spot.X + 14, spot.Z, 10, 1), 60);
        Assert.True(s.SpotNotes(spot).Any(n => n.StartsWith("too steep", StringComparison.Ordinal)), "a 60-unit bump in the footprint is too steep");
        var (x, z, problem) = s.Metal.TryAdd(3072, 3072);
        Assert.True(problem == null, "a spot can go in the middle of the sea");
        s.Apply($"metal-add:{At(x, z)}");
        Assert.True(s.SpotNotes(s.Metal.SpotNear(x, z, 32)!).Any(n => n.StartsWith("under water", StringComparison.Ordinal)), "and is noted as under water");
    }
}
