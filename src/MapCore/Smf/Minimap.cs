using MapCore.Imaging;
using MapCore.Textures;

namespace MapCore.Smf;

/// <summary>The SMF's built-in minimap: DXT1, 1024x1024, with 9 mip levels down to 4x4.</summary>
public static class Minimap
{
    public const int Size = 1024;
    public const int LevelCount = 9;

    static Memory<byte> Level(SmfFile smf, int mip)
    {
        int offset = 0;
        for (int m = 0; m < mip; m++)
            offset += BlockCompression.LevelBytes(BlockFormat.Dxt1, Size >> m, Size >> m);
        return smf.Minimap.AsMemory(offset, BlockCompression.LevelBytes(BlockFormat.Dxt1, Size >> mip, Size >> mip));
    }

    public static RgbaImage Decode(SmfFile smf, int mip = 0) =>
        BlockCompression.Decode(BlockFormat.Dxt1, Level(smf, mip), Size >> mip, Size >> mip);

    /// <summary>
    /// Redraws the minimap from the map texture by area averaging. With <paramref name="textureRegion"/>
    /// (texture pixels), only the matching part of the minimap is redrawn; the rest keeps its original look,
    /// which may include effects the texture lacks (some tools bake water or lighting into the minimap).
    /// <paramref name="style"/>(x, y, colour) adjusts each redrawn minimap pixel (see <see cref="MinimapStyle"/>).
    /// </summary>
    public static void Rebuild(SmfFile smf, RgbaImage texture, PixelRect? textureRegion = null, Func<int, int, Rgba, Rgba>? style = null)
    {
        PixelRect? region = null;
        RgbaImage level0;
        if (textureRegion is PixelRect r)
        {
            var area = ToMinimapRegion(r, texture.Width, texture.Height);
            if (area.IsEmpty)
                return;
            region = area;
            level0 = Decode(smf);
            texture.ResampleInto(level0, area);
        }
        else
        {
            level0 = texture.Resample(Size, Size);
        }

        var written = region ?? level0.Bounds;
        if (style != null)
            for (int y = written.Y; y < written.Bottom; y++)
                for (int x = written.X; x < written.Right; x++)
                    level0.SetPixel(x, y, style(x, y, level0.GetPixel(x, y)));
        level0.MakeOpaque(written);
        BlockCompression.Encode(BlockFormat.Dxt1, level0, Level(smf, 0), written);
        MipChain.Rebuild(level0, LevelCount, region, m => Decode(smf, m),
            (m, image, dirty) => BlockCompression.Encode(BlockFormat.Dxt1, image, Level(smf, m), dirty));
    }

    /// <summary>The stored bytes a redraw of some minimap regions can change, for undo.</summary>
    public sealed class RegionState
    {
        internal List<(int Offset, byte[] Bytes)> Rows { get; } = [];
        public long Bytes => Rows.Sum(r => (long)r.Bytes.Length);
    }

    /// <summary>
    /// Copies the blocks <see cref="Rebuild"/> rewrites for the given level-0 regions, at every level (the mip chain
    /// over each region): a few hundred bytes for a brush dab, instead of the whole 699 KB minimap.
    /// </summary>
    public static RegionState CaptureRegions(SmfFile smf, IEnumerable<PixelRect> regions)
    {
        var state = new RegionState();
        foreach (var region in regions)
            foreach (var (offset, length) in Rows(region))
                state.Rows.Add((offset, smf.Minimap.AsSpan(offset, length).ToArray()));
        return state;
    }

    /// <summary>Puts captured blocks back (regions captured together are restored in reverse, so overlaps end as captured first).</summary>
    public static void RestoreRegions(SmfFile smf, RegionState state)
    {
        for (int i = state.Rows.Count - 1; i >= 0; i--)
            state.Rows[i].Bytes.CopyTo(smf.Minimap, state.Rows[i].Offset);
    }

    /// <summary>Byte runs (one per block row) of a level-0 region and its mip chain.</summary>
    static IEnumerable<(int Offset, int Length)> Rows(PixelRect region)
    {
        int offset = 0;
        var area = region.Intersect(new PixelRect(0, 0, Size, Size));
        for (int m = 0; m < LevelCount; m++)
        {
            int size = Size >> m, across = BlockCompression.BlocksAcross(size);
            if (m > 0)
                area = area.ToNextMip(size, size);
            if (area.IsEmpty)
                yield break;
            var blocks = area.ToBlockBounds(size, size);
            for (int by = blocks.Y / 4; by < (blocks.Bottom + 3) / 4; by++)
                yield return (offset + (by * across + blocks.X / 4) * 8, ((blocks.Right + 3) / 4 - blocks.X / 4) * 8);
            offset += BlockCompression.LevelBytes(BlockFormat.Dxt1, size, size);
        }
    }

    /// <summary>Minimap pixels whose source area overlaps <paramref name="textureRegion"/>.</summary>
    public static PixelRect ToMinimapRegion(PixelRect textureRegion, int textureWidth, int textureHeight) =>
        PixelRect.FromEdges(
            (int)Math.Floor((double)textureRegion.X * Size / textureWidth),
            (int)Math.Floor((double)textureRegion.Y * Size / textureHeight),
            (int)Math.Ceiling((double)textureRegion.Right * Size / textureWidth),
            (int)Math.Ceiling((double)textureRegion.Bottom * Size / textureHeight))
        .Intersect(new PixelRect(0, 0, Size, Size));
}
