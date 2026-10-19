using MapCore.Imaging;

namespace MapCore.Textures;

public enum BlockFormat { Dxt1, Dxt5 }

/// <summary>Compresses and decompresses whole images, or just the blocks a region touches, in parallel.</summary>
public static class BlockCompression
{
    public static int BlockBytes(BlockFormat format) => format == BlockFormat.Dxt1 ? Dxt.Dxt1BlockBytes : Dxt.Dxt5BlockBytes;

    public static int BlocksAcross(int pixels) => Math.Max(1, (pixels + 3) / 4);

    /// <summary>Bytes needed for one compressed image of the given size.</summary>
    public static int LevelBytes(BlockFormat format, int width, int height) =>
        checked(BlocksAcross(width) * BlocksAcross(height) * BlockBytes(format));

    public static RgbaImage Decode(BlockFormat format, ReadOnlyMemory<byte> data, int width, int height)
    {
        var image = new RgbaImage(width, height);
        Parallel.For(0, BlocksAcross(height), by => DecodeBlockRow(format, data.Span, width, height, by, image, 0, 0));
        return image;
    }

    /// <summary>Decodes a compressed image into <paramref name="target"/> at (x, y), without parallelism (for small tiles).</summary>
    public static void DecodeInto(BlockFormat format, ReadOnlySpan<byte> data, int width, int height, RgbaImage target, int x, int y)
    {
        for (int by = 0; by < BlocksAcross(height); by++)
            DecodeBlockRow(format, data, width, height, by, target, x, y);
    }

    static void DecodeBlockRow(BlockFormat format, ReadOnlySpan<byte> data, int width, int height, int by,
        RgbaImage target, int offsetX, int offsetY)
    {
        int across = BlocksAcross(width), size = BlockBytes(format);
        if (data.Length < LevelBytes(format, width, height))
            throw new ArgumentException($"{data.Length} bytes is too little for a {width}x{height} {format} image.");

        Span<byte> px = stackalloc byte[64];
        for (int bx = 0; bx < across; bx++)
        {
            var block = data.Slice((by * across + bx) * size, size);
            if (format == BlockFormat.Dxt1)
                Dxt.DecodeDxt1Block(block, px);
            else
                Dxt.DecodeDxt5Block(block, px);

            int columns = Math.Min(4, width - bx * 4);
            for (int y = 0; y < 4 && by * 4 + y < height; y++)
                px.Slice(y * 16, columns * 4).CopyTo(
                    target.Pixels.AsSpan(((offsetY + by * 4 + y) * target.Width + offsetX + bx * 4) * 4));
        }
    }

    /// <summary>
    /// Compresses <paramref name="image"/> into <paramref name="destination"/>. With a region, only the blocks
    /// that region touches are rewritten; every other block keeps its existing bytes.
    /// </summary>
    /// <param name="parallel">False for small images whose callers already run in parallel.</param>
    public static void Encode(BlockFormat format, RgbaImage image, Memory<byte> destination, PixelRect? region = null, bool parallel = true)
    {
        int across = BlocksAcross(image.Width), size = BlockBytes(format);
        if (destination.Length < LevelBytes(format, image.Width, image.Height))
            throw new ArgumentException($"{destination.Length} bytes is too little for a {image.Width}x{image.Height} {format} image.");

        var area = (region ?? image.Bounds).Intersect(image.Bounds);
        if (area.IsEmpty)
            return;
        int bx0 = area.X / 4, by0 = area.Y / 4, bx1 = (area.Right + 3) / 4, by1 = (area.Bottom + 3) / 4;

        void EncodeRow(int by)
        {
            Span<byte> px = stackalloc byte[64];
            var target = destination.Span;
            for (int bx = bx0; bx < bx1; bx++)
            {
                GatherBlock(image, bx, by, px);
                var block = target.Slice((by * across + bx) * size, size);
                if (format == BlockFormat.Dxt1)
                    Dxt.EncodeDxt1Block(px, block);
                else
                    Dxt.EncodeDxt5Block(px, block);
            }
        }

        if (parallel)
            Parallel.For(by0, by1, EncodeRow);
        else
            for (int by = by0; by < by1; by++)
                EncodeRow(by);
    }

    /// <summary>Copies a 4x4 block; blocks hanging over the image edge repeat the last row/column.</summary>
    static void GatherBlock(RgbaImage image, int bx, int by, Span<byte> px)
    {
        var pixels = image.Pixels.AsSpan();
        for (int y = 0; y < 4; y++)
        {
            int sy = Math.Min(by * 4 + y, image.Height - 1);
            for (int x = 0; x < 4; x++)
            {
                int sx = Math.Min(bx * 4 + x, image.Width - 1);
                pixels.Slice((sy * image.Width + sx) * 4, 4).CopyTo(px.Slice((y * 4 + x) * 4, 4));
            }
        }
    }
}
