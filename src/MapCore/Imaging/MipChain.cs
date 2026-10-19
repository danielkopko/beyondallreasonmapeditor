namespace MapCore.Imaging;

/// <summary>Regenerates the mip levels below level 0 with a 2x2 box filter.</summary>
public static class MipChain
{
    /// <summary>
    /// Rebuilds levels 1 to <paramref name="levelCount"/>-1 from <paramref name="level0"/>. With a region
    /// (level-0 pixels), only the parts of each level that depend on it are recomputed and stored.
    /// </summary>
    /// <param name="decodeLevel">Current contents of a level; called only for partial rebuilds.</param>
    /// <param name="encodeLevel">Stores a level image, rewriting only the given region of it.</param>
    public static void Rebuild(RgbaImage level0, int levelCount, PixelRect? region,
        Func<int, RgbaImage> decodeLevel, Action<int, RgbaImage, PixelRect> encodeLevel)
    {
        var dirty = region ?? level0.Bounds;
        var previous = level0;
        for (int m = 1; m < levelCount; m++)
        {
            int width = Math.Max(1, previous.Width / 2), height = Math.Max(1, previous.Height / 2);
            dirty = dirty.ToNextMip(width, height);
            if (dirty.IsEmpty)
                return;

            RgbaImage level;
            if (region == null)
            {
                level = previous.Downsample();
            }
            else
            {
                level = decodeLevel(m);
                previous.DownsampleInto(level, dirty);
            }
            encodeLevel(m, level, dirty);
            previous = level;
        }
    }
}
