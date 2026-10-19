using MapCore.Editing;
using MapCore.Imaging;

namespace MapCore.Rendering;

/// <summary>
/// An image covering the whole map plus half-size copies of it, so any zoom level samples about one source pixel
/// per output pixel. Level-0 pixel i covers world [i·s, (i+1)·s), where s = map width / image width; every view
/// follows this rule, whatever its resolution (texture 1 unit, metal 16, grass 32, .dds by fraction of the map).
/// </summary>
sealed class ImagePyramid
{
    const int SmallestLevel = 32;

    readonly List<RgbaImage> levels;

    /// <param name="level0">The full-resolution image, in map orientation. It is kept, not copied.</param>
    /// <param name="smooth">Bilinear sampling (pictures); otherwise nearest (cell layers such as metal).</param>
    public ImagePyramid(RgbaImage level0, double mapWidth, double mapHeight, bool smooth)
    {
        MapWidth = mapWidth;
        MapHeight = mapHeight;
        Smooth = smooth;
        levels = [level0];
        while (Math.Max(levels[^1].Width, levels[^1].Height) >= 2 * SmallestLevel)
            levels.Add(levels[^1].Downsample());
    }

    public double MapWidth { get; }
    public double MapHeight { get; }
    public bool Smooth { get; }
    public IReadOnlyList<RgbaImage> Levels => levels;
    public RgbaImage Level0 => levels[0];

    /// <summary>Level-0 pixels touched by a world area, rounded outwards.</summary>
    public PixelRect ToPixels(WorldRect area)
    {
        double sx = Level0.Width / MapWidth, sz = Level0.Height / MapHeight;
        return PixelRect.FromEdges(
            (int)Math.Floor(area.X * sx), (int)Math.Floor(area.Z * sz),
            (int)Math.Ceiling(area.Right * sx), (int)Math.Ceiling(area.Bottom * sz)).Intersect(Level0.Bounds);
    }

    /// <summary>Re-derives the smaller levels over a changed region of level 0.</summary>
    public void Update(PixelRect region)
    {
        region = region.Intersect(Level0.Bounds);
        for (int k = 1; k < levels.Count && !region.IsEmpty; k++)
        {
            region = region.ToNextMip(levels[k].Width, levels[k].Height);
            levels[k - 1].DownsampleInto(levels[k], region);
        }
    }

    /// <summary>The finest level whose pixels are no smaller than the output's (level 0 when zoomed in further).</summary>
    public int LevelFor(double unitsPerPixel)
    {
        int k = 0;
        while (k + 1 < levels.Count && MapWidth / levels[k + 1].Width <= unitsPerPixel * 1.0001)
            k++;
        return k;
    }

    /// <summary>
    /// Draws the map area seen by <paramref name="view"/> into <paramref name="target"/>. Pixels outside the map
    /// are left alone. With <paramref name="blend"/>, source alpha mixes with what is there; otherwise pixels are
    /// replaced (alpha set to 255). <paramref name="brightness"/> scales the colour (1 = as is).
    /// </summary>
    public void Draw(RgbaImage target, ViewTransform view, bool blend = false, double brightness = 1)
    {
        var level = levels[LevelFor(view.UnitsPerPixel)];
        double sx = MapWidth / level.Width, sz = MapHeight / level.Height;
        var columns = Axis(target.Width, view.Left, view.UnitsPerPixel, sx, level.Width, MapWidth);
        var rows = Axis(target.Height, view.Top, view.UnitsPerPixel, sz, level.Height, MapHeight);
        int scale = (int)Math.Round(Math.Clamp(brightness, 0, 4) * 256);

        Parallel.For(0, target.Height, y =>
        {
            var row = rows[y];
            if (!row.Inside)
                return;
            int r0 = row.I0 * level.Width * 4, r1 = row.I1 * level.Width * 4;
            var src = level.Pixels;
            var dst = target.Pixels;
            for (int x = 0; x < target.Width; x++)
            {
                var col = columns[x];
                if (!col.Inside)
                    continue;
                int a = r0 + col.I0 * 4, b = r0 + col.I1 * 4, c = r1 + col.I0 * 4, d = r1 + col.I1 * 4;
                int o = (y * target.Width + x) * 4;
                int r, g, bl, al;
                if (Smooth)
                {
                    float fx = col.F, fy = row.F;
                    float w00 = (1 - fx) * (1 - fy), w01 = fx * (1 - fy), w10 = (1 - fx) * fy, w11 = fx * fy;
                    r = (int)(src[a] * w00 + src[b] * w01 + src[c] * w10 + src[d] * w11 + 0.5f);
                    g = (int)(src[a + 1] * w00 + src[b + 1] * w01 + src[c + 1] * w10 + src[d + 1] * w11 + 0.5f);
                    bl = (int)(src[a + 2] * w00 + src[b + 2] * w01 + src[c + 2] * w10 + src[d + 2] * w11 + 0.5f);
                    al = (int)(src[a + 3] * w00 + src[b + 3] * w01 + src[c + 3] * w10 + src[d + 3] * w11 + 0.5f);
                }
                else
                {
                    (r, g, bl, al) = (src[a], src[a + 1], src[a + 2], src[a + 3]);
                }
                if (scale != 256)
                {
                    r = Math.Min(255, r * scale >> 8);
                    g = Math.Min(255, g * scale >> 8);
                    bl = Math.Min(255, bl * scale >> 8);
                }
                if (blend)
                {
                    if (al == 0)
                        continue;
                    dst[o] = (byte)((dst[o] * (255 - al) + r * al + 127) / 255);
                    dst[o + 1] = (byte)((dst[o + 1] * (255 - al) + g * al + 127) / 255);
                    dst[o + 2] = (byte)((dst[o + 2] * (255 - al) + bl * al + 127) / 255);
                }
                else
                {
                    (dst[o], dst[o + 1], dst[o + 2]) = ((byte)r, (byte)g, (byte)bl);
                }
                dst[o + 3] = 255;
            }
        });
    }

    /// <summary>The colour at one world position, bilinear, from the level matching <paramref name="unitsPerPixel"/>.</summary>
    public (float R, float G, float B) Sample(double x, double z, double unitsPerPixel)
    {
        var level = levels[LevelFor(unitsPerPixel)];
        int w = level.Width, h = level.Height;
        double u = Math.Clamp(x * w / MapWidth - 0.5, 0, w - 1), v = Math.Clamp(z * h / MapHeight - 0.5, 0, h - 1);
        int x0 = (int)u, y0 = (int)v, x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
        float fx = (float)(u - x0), fy = (float)(v - y0);
        var p = level.Pixels;
        int a = (y0 * w + x0) * 4, b = (y0 * w + x1) * 4, c = (y1 * w + x0) * 4, d = (y1 * w + x1) * 4;
        float w00 = (1 - fx) * (1 - fy), w01 = fx * (1 - fy), w10 = (1 - fx) * fy, w11 = fx * fy;
        return (p[a] * w00 + p[b] * w01 + p[c] * w10 + p[d] * w11,
                p[a + 1] * w00 + p[b + 1] * w01 + p[c + 1] * w10 + p[d + 1] * w11,
                p[a + 2] * w00 + p[b + 2] * w01 + p[c + 2] * w10 + p[d + 2] * w11);
    }

    readonly record struct Tap(bool Inside, int I0, int I1, float F);

    /// <summary>
    /// For each output pixel along one axis: whether its centre is on the map, and the source pixels and weight to
    /// sample. Bilinear taps are clamped at the map edge; nearest uses I0 only.
    /// </summary>
    Tap[] Axis(int count, double start, double unitsPerPixel, double sourceUnits, int sourceCount, double mapSize)
    {
        var taps = new Tap[count];
        for (int i = 0; i < count; i++)
        {
            double world = start + (i + 0.5) * unitsPerPixel;
            if (world < 0 || world >= mapSize)
                continue;
            if (Smooth)
            {
                double u = Math.Clamp(world / sourceUnits - 0.5, 0, sourceCount - 1);
                int i0 = Math.Min((int)u, sourceCount - 1);
                taps[i] = new Tap(true, i0, Math.Min(i0 + 1, sourceCount - 1), (float)(u - i0));
            }
            else
            {
                int i0 = Math.Min((int)(world / sourceUnits), sourceCount - 1);
                taps[i] = new Tap(true, i0, i0, 0);
            }
        }
        return taps;
    }
}
