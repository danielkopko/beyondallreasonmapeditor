namespace MapCore.Imaging;

public readonly record struct Rgba(byte R, byte G, byte B, byte A);

/// <summary>A rectangle of pixels; <see cref="Right"/> and <see cref="Bottom"/> are exclusive.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static PixelRect FromEdges(int left, int top, int right, int bottom) => new(left, top, right - left, bottom - top);

    public PixelRect Intersect(PixelRect other) => FromEdges(
        Math.Max(X, other.X), Math.Max(Y, other.Y), Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));

    public PixelRect Union(PixelRect other) =>
        IsEmpty ? other : other.IsEmpty ? this : FromEdges(
            Math.Min(X, other.X), Math.Min(Y, other.Y), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));

    /// <summary>
    /// The same area in an image of the given height turned upside down. Converts map-orientation regions to
    /// file coordinates for map .dds files, which the engine loads flipped (see <see cref="RgbaImage.FlipVertical"/>).
    /// </summary>
    public PixelRect FlipVertical(int imageHeight) => this with { Y = imageHeight - Y - Height };

    /// <summary>The pixels of the next (half-size) mip level whose box filter reads from this region.</summary>
    public PixelRect ToNextMip(int nextWidth, int nextHeight) =>
        FromEdges(X / 2, Y / 2, (Right + 1) / 2, (Bottom + 1) / 2).Intersect(new(0, 0, nextWidth, nextHeight));

    /// <summary>This region grown outwards to whole 4x4 compression blocks, clipped to the image.</summary>
    public PixelRect ToBlockBounds(int imageWidth, int imageHeight) =>
        FromEdges(X / 4 * 4, Y / 4 * 4, (Right + 3) / 4 * 4, (Bottom + 3) / 4 * 4).Intersect(new(0, 0, imageWidth, imageHeight));
}

/// <summary>An 8-bit RGBA image in memory, stored row by row from the top-left corner.</summary>
public sealed class RgbaImage
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>Width x Height x 4 bytes: R, G, B, A for each pixel.</summary>
    public byte[] Pixels { get; }

    public PixelRect Bounds => new(0, 0, Width, Height);

    public RgbaImage(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), $"Image size {width}x{height} must be positive.");
        Width = width;
        Height = height;
        Pixels = new byte[checked(width * height * 4)];
    }

    public Rgba GetPixel(int x, int y)
    {
        int i = (y * Width + x) * 4;
        return new(Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
    }

    public void SetPixel(int x, int y, Rgba c)
    {
        int i = (y * Width + x) * 4;
        (Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]) = (c.R, c.G, c.B, c.A);
    }

    public void Fill(PixelRect rect, Rgba color)
    {
        rect = rect.Intersect(Bounds);
        for (int y = rect.Y; y < rect.Bottom; y++)
            for (int x = rect.X; x < rect.Right; x++)
                SetPixel(x, y, color);
    }

    /// <summary>A copy of <paramref name="rect"/>, which must lie inside the image.</summary>
    public RgbaImage Crop(PixelRect rect)
    {
        if (rect.Intersect(Bounds) != rect || rect.IsEmpty)
            throw new ArgumentOutOfRangeException(nameof(rect), $"{rect} is not inside the {Width}x{Height} image.");
        var crop = new RgbaImage(rect.Width, rect.Height);
        for (int y = 0; y < rect.Height; y++)
            Array.Copy(Pixels, ((rect.Y + y) * Width + rect.X) * 4, crop.Pixels, y * rect.Width * 4, rect.Width * 4);
        return crop;
    }

    /// <summary>Copies <paramref name="source"/> into this image with its top-left corner at (x, y), clipped to the image.</summary>
    public void Paste(RgbaImage source, int x, int y)
    {
        var area = new PixelRect(x, y, source.Width, source.Height).Intersect(Bounds);
        for (int row = area.Y; row < area.Bottom; row++)
            Array.Copy(source.Pixels, ((row - y) * source.Width + (area.X - x)) * 4, Pixels, (row * Width + area.X) * 4, area.Width * 4);
    }

    /// <summary>Turns the image upside down, in place.</summary>
    /// <remarks>
    /// The engine flips every map .dds vertically when loading it (Bitmap.cpp, flipDDS), so those files store
    /// the map's south edge in their first row. Flip after decoding to get map orientation, and before encoding.
    /// </remarks>
    public void FlipVertical()
    {
        int stride = Width * 4;
        var row = new byte[stride];
        for (int top = 0, bottom = Height - 1; top < bottom; top++, bottom--)
        {
            Array.Copy(Pixels, top * stride, row, 0, stride);
            Array.Copy(Pixels, bottom * stride, Pixels, top * stride, stride);
            Array.Copy(row, 0, Pixels, bottom * stride, stride);
        }
    }

    /// <summary>Sets alpha to 255 inside <paramref name="rect"/>.</summary>
    public void MakeOpaque(PixelRect rect)
    {
        rect = rect.Intersect(Bounds);
        for (int y = rect.Y; y < rect.Bottom; y++)
            for (int x = rect.X; x < rect.Right; x++)
                Pixels[(y * Width + x) * 4 + 3] = 255;
    }

    /// <summary>A copy at a different size, each target pixel averaging the source area it covers.</summary>
    public RgbaImage Resample(int width, int height)
    {
        var target = new RgbaImage(width, height);
        ResampleInto(target, target.Bounds);
        return target;
    }

    /// <summary>
    /// Area-average resampling of this image into <paramref name="target"/> (any size), computing only
    /// <paramref name="region"/> of the target. Each target pixel averages the source area it covers.
    /// </summary>
    public void ResampleInto(RgbaImage target, PixelRect region)
    {
        region = region.Intersect(target.Bounds);
        if (region.IsEmpty)
            return;
        double scaleX = (double)Width / target.Width, scaleY = (double)Height / target.Height;

        Parallel.For(region.Y, region.Bottom, ty =>
        {
            double y0 = ty * scaleY, y1 = (ty + 1) * scaleY;
            int sy0 = (int)y0, sy1 = Math.Min(Height, (int)Math.Ceiling(y1));
            for (int tx = region.X; tx < region.Right; tx++)
            {
                double x0 = tx * scaleX, x1 = (tx + 1) * scaleX;
                int sx0 = (int)x0, sx1 = Math.Min(Width, (int)Math.Ceiling(x1));
                double r = 0, g = 0, b = 0, a = 0, total = 0;
                for (int sy = sy0; sy < sy1; sy++)
                {
                    double wy = Math.Min(y1, sy + 1) - Math.Max(y0, sy);
                    for (int sx = sx0; sx < sx1; sx++)
                    {
                        double w = wy * (Math.Min(x1, sx + 1) - Math.Max(x0, sx));
                        int s = (sy * Width + sx) * 4;
                        r += w * Pixels[s];
                        g += w * Pixels[s + 1];
                        b += w * Pixels[s + 2];
                        a += w * Pixels[s + 3];
                        total += w;
                    }
                }
                int o = (ty * target.Width + tx) * 4;
                target.Pixels[o] = (byte)Math.Round(r / total);
                target.Pixels[o + 1] = (byte)Math.Round(g / total);
                target.Pixels[o + 2] = (byte)Math.Round(b / total);
                target.Pixels[o + 3] = (byte)Math.Round(a / total);
            }
        });
    }

    /// <summary>A half-size copy made with a 2x2 box filter (sizes never drop below 1).</summary>
    public RgbaImage Downsample()
    {
        var half = new RgbaImage(Math.Max(1, Width / 2), Math.Max(1, Height / 2));
        DownsampleInto(half, half.Bounds);
        return half;
    }

    /// <summary>Box-filters this image into <paramref name="half"/>, but only inside <paramref name="region"/> (half-size coordinates).</summary>
    public void DownsampleInto(RgbaImage half, PixelRect region)
    {
        if (half.Width != Math.Max(1, Width / 2) || half.Height != Math.Max(1, Height / 2))
            throw new ArgumentException($"Target is {half.Width}x{half.Height}, expected half of {Width}x{Height}.");
        region = region.Intersect(half.Bounds);
        if (region.IsEmpty)
            return;

        Parallel.For(region.Y, region.Bottom, y =>
        {
            int row0 = Math.Min(2 * y, Height - 1) * Width, row1 = Math.Min(2 * y + 1, Height - 1) * Width;
            for (int x = region.X; x < region.Right; x++)
            {
                int c0 = Math.Min(2 * x, Width - 1), c1 = Math.Min(2 * x + 1, Width - 1);
                int a = (row0 + c0) * 4, b = (row0 + c1) * 4, c = (row1 + c0) * 4, d = (row1 + c1) * 4;
                int o = (y * half.Width + x) * 4;
                for (int ch = 0; ch < 4; ch++)
                    half.Pixels[o + ch] = (byte)((Pixels[a + ch] + Pixels[b + ch] + Pixels[c + ch] + Pixels[d + ch] + 2) >> 2);
            }
        });
    }
}
