using MapCore.Imaging;
using MapCore.Textures;

namespace MapCore.Tests;

/// <summary>
/// The engine flips every map .dds vertically on load (RecoilEngine Bitmap.cpp, flipDDS), so those files store the
/// map's south edge first. These tests pin that convention to Acidic Quarry's data.
/// </summary>
static class OrientationTests
{
    [Test]
    public static void FlipVerticalSwapsRowsAndIsItsOwnInverse()
    {
        var image = new RgbaImage(3, 5);
        for (int y = 0; y < 5; y++)
            image.SetPixel(1, y, new Rgba((byte)y, 0, 0, 255));
        var original = (byte[])image.Pixels.Clone();
        image.FlipVertical();
        Assert.Equal((byte)4, image.GetPixel(1, 0).R, "top row after flip");
        Assert.Equal((byte)2, image.GetPixel(1, 2).R, "middle row stays");
        image.FlipVertical();
        Assert.True(image.Pixels.AsSpan().SequenceEqual(original), "flipping twice restores the image");
    }

    [Test]
    public static void RegionEditInMapOrientationLandsOnTheRightBlocks()
    {
        // Edit a map-orientation rectangle of an upside-down .dds: flip the image, paint, flip back, and encode
        // the region converted to file coordinates. Only the blocks under the mirrored rectangle may change.
        var path = TestData.MapTexture("splat_distr.dds");
        var original = DdsFile.Load(path);
        var dds = DdsFile.Load(path);
        var mapRegion = new PixelRect(400, 100, 40, 24);
        var fileRegion = mapRegion.FlipVertical(dds.Height);
        Assert.Equal(new PixelRect(400, 2048 - 100 - 24, 40, 24), fileRegion, "flipped region");

        var image = dds.Decode();
        image.FlipVertical();
        image.Fill(mapRegion, new Rgba(0, 0, 255, 255));
        image.FlipVertical();
        dds.Encode(image, region: fileRegion);

        var before = original.LevelData().Span;
        var after = dds.LevelData().Span;
        var blocks = fileRegion.ToBlockBounds(dds.Width, dds.Height);
        int across = dds.Width / 4, changedOutside = 0, changedInside = 0;
        for (int b = 0; b < before.Length / 16; b++)
        {
            if (before.Slice(b * 16, 16).SequenceEqual(after.Slice(b * 16, 16)))
                continue;
            int x = b % across * 4, y = b / across * 4;
            if (x >= blocks.X && x < blocks.Right && y >= blocks.Y && y < blocks.Bottom)
                changedInside++;
            else
                changedOutside++;
        }
        Assert.Equal(0, changedOutside, "blocks changed outside the mirrored region");
        Assert.True(changedInside > 0, "the mirrored region changed");

        var check = dds.Decode();
        check.FlipVertical();
        Assert.True(check.GetPixel(420, 110).B > 240, "painted pixel reads back at its map position");
    }

    [Test]
    public static void SplatMetalBlobsLineUpWithMetalMapOnlyWhenFlipped()
    {
        var smf = TestData.AcidicQuarry.LoadSmf();
        var splat = DdsFile.Load(TestData.MapTexture("splat_distr.dds")).Decode();
        double unitsPerPixel = smf.MapX * 8.0 / splat.Width;

        double MeanRedAtMetal(RgbaImage image)
        {
            double sum = 0;
            int count = 0;
            for (int z = 0; z < smf.MetalMapHeight; z++)
                for (int x = 0; x < smf.MetalMapWidth; x++)
                    if (smf.MetalMap[z * smf.MetalMapWidth + x] != 0)
                    {
                        sum += image.GetPixel((int)((x * 16 + 8) / unitsPerPixel), (int)((z * 16 + 8) / unitsPerPixel)).R;
                        count++;
                    }
            return sum / count;
        }

        double asStored = MeanRedAtMetal(splat);
        splat.FlipVertical();
        double flipped = MeanRedAtMetal(splat);
        Report.Info($"mean splat red at metal cells: as stored {asStored:F0}, flipped {flipped:F0}");
        // Measured 145 flipped vs 37 as stored. The 3x3-cell spots reach 23 units from their centre, where the
        // red blob is already fading (ring study: 181 at the centre, 135 at 16-24 units).
        Assert.True(flipped > 120, "flipped splat should show the metal blob (red channel) on the metal cells");
        Assert.True(flipped > asStored + 50, "the flipped orientation should match clearly better than the stored one");
    }
}
