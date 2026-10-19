using MapCore.Editing;
using MapCore.Imaging;

namespace MapCore.Metal;

/// <summary>
/// How a metal spot's look (the splat blob, the light texture patch, the normal-map outline) is carried from A to B
/// when the spot moves. The look is a disc of <see cref="Radius"/> game units, blended over <see cref="Feather"/>.
/// <list type="bullet">
/// <item>Far moves (the two discs don't touch) <b>carry</b> the disc: B gets A's look, and A is filled from
/// (<see cref="FillX"/>, <see cref="FillZ"/>) — nearby ground that matches A's surroundings, or B's ground — or, with
/// <see cref="FillFromRim"/>, rebuilt from the ground around its rim (a smooth pull-push fill).</item>
/// <item>Nudges <b>warp</b>: everything within the radius of the path from A to B shifts by B − A, and the shift fades
/// out over a feather at least twice the distance moved, so the warp never folds over itself.</item>
/// </list>
/// </summary>
public sealed record SpotLook(double AX, double AZ, double BX, double BZ, double Radius, double Feather, bool Swap)
{
    /// <summary>Where the ground that fills the old place comes from (far moves); B unless set.</summary>
    public double FillX { get; init; } = BX;
    public double FillZ { get; init; } = BZ;

    /// <summary>Rebuild the old place from the ground around its rim (when no nearby disc matches).</summary>
    public bool FillFromRim { get; init; }

    /// <summary>The narrowest blend allowed at the edge of a swapped disc.</summary>
    public const double MinFeather = 8;

    public static SpotLook Plan(double ax, double az, double bx, double bz, double radius, double feather)
    {
        double d = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
        bool swap = d >= 2 * (radius + MinFeather);
        return new SpotLook(ax, az, bx, bz, radius, swap ? Math.Max(MinFeather, Math.Min(feather, d / 2 - radius)) : Math.Max(feather, 2 * d), swap);
    }

    public double Distance => Math.Sqrt((BX - AX) * (BX - AX) + (BZ - AZ) * (BZ - AZ));

    /// <summary>The ground the move can change: two squares (swap) or one box around the path (warp).</summary>
    public IReadOnlyList<WorldRect> Areas
    {
        get
        {
            double reach = Radius + Feather + 1;
            return Swap
                ? [Square(AX, AZ, reach), Square(BX, BZ, reach)]
                : [new WorldRect(Math.Min(AX, BX) - reach, Math.Min(AZ, BZ) - reach, Math.Abs(BX - AX) + 2 * reach, Math.Abs(BZ - AZ) + 2 * reach)];
        }
    }

    static WorldRect Square(double x, double z, double half) => new(x - half, z - half, 2 * half, 2 * half);

    /// <summary>1 inside the radius, easing to 0 over the feather (cosine).</summary>
    double Falloff(double distance) =>
        distance <= Radius ? 1 : distance >= Radius + Feather ? 0 : 0.5 * (1 + Math.Cos(Math.PI * (distance - Radius) / Feather));

    /// <summary>For a point of the map: where its new content comes from, and how much of it to take (0: keep).</summary>
    public (double X, double Z, double Weight) Source(double x, double z)
    {
        double dx = BX - AX, dz = BZ - AZ;
        if (Swap)
        {
            double wb = Falloff(Math.Sqrt((x - BX) * (x - BX) + (z - BZ) * (z - BZ)));
            if (wb > 0)
                return (x - dx, z - dz, wb);
            double wa = Falloff(Math.Sqrt((x - AX) * (x - AX) + (z - AZ) * (z - AZ)));
            return (x + FillX - AX, z + FillZ - AZ, wa); // (with FillFromRim, ApplyTo fills the old place itself)
        }
        // Distance to the path from A to B.
        double length2 = dx * dx + dz * dz;
        double t = length2 == 0 ? 0 : Math.Clamp(((x - AX) * dx + (z - AZ) * dz) / length2, 0, 1);
        double px = AX + t * dx - x, pz = AZ + t * dz - z;
        double m = Falloff(Math.Sqrt(px * px + pz * pz));
        return m > 0 ? (x - dx * m, z - dz * m, 1) : (x, z, 0);
    }

    /// <summary>
    /// Applies the move to an image covering the whole map. <paramref name="upsideDown"/>: the image is stored with the
    /// map's south edge in its first row (map .dds files, rule 7). Every new pixel is sampled from the image as it was
    /// before, so the move never reads its own output. Returns the image regions changed.
    /// </summary>
    public IReadOnlyList<PixelRect> ApplyTo(RgbaImage image, bool upsideDown, double mapWidth, double mapHeight)
    {
        double ux = mapWidth / image.Width, uz = mapHeight / image.Height;
        var regions = Areas.Select(a => ToPixels(a)).Where(r => !r.IsEmpty).ToList();
        var sources = regions.Select(r => (Rect: r, Pixels: image.Crop(r))).ToList();
        var rimFill = Swap && FillFromRim ? RimFill(image, upsideDown, ux, uz, mapHeight) : null;
        for (int k = 0; k < regions.Count; k++)
        {
            var region = regions[k];
            var earlier = regions.Take(k).ToList(); // where squares overlap, each pixel is written once
            Parallel.For(region.Y, region.Bottom, j =>
            {
                double z = upsideDown ? mapHeight - (j + 0.5) * uz : (j + 0.5) * uz;
                for (int i = region.X; i < region.Right; i++)
                {
                    if (earlier.Any(r => i >= r.X && j >= r.Y && i < r.Right && j < r.Bottom))
                        continue;
                    var (sx, sz, w) = Source((i + 0.5) * ux, z);
                    if (w <= 0)
                        continue;
                    if (rimFill is (PixelRect fillArea, _) && i >= fillArea.X && j >= fillArea.Y && i < fillArea.Right && j < fillArea.Bottom &&
                        (i + 0.5) * ux is var wx && (wx - AX) * (wx - AX) + (z - AZ) * (z - AZ) <= (Radius + Feather) * (Radius + Feather))
                    {
                        var (area, filled) = rimFill.Value;
                        var f = filled.GetPixel(i - area.X, j - area.Y);
                        var o = image.GetPixel(i, j);
                        image.SetPixel(i, j, new Rgba(Mix(o.R, f.R, w), Mix(o.G, f.G, w), Mix(o.B, f.B, w), Mix(o.A, f.A, w)));
                        continue;
                    }
                    // Source position in image pixels (continuous, pixel centres at +0.5).
                    double si = sx / ux - 0.5, sj = (upsideDown ? (mapHeight - sz) / uz : sz / uz) - 0.5;
                    var sample = Sample(sources, si, sj, image);
                    var p = image.GetPixel(i, j);
                    image.SetPixel(i, j, new Rgba(Mix(p.R, sample.R, w), Mix(p.G, sample.G, w), Mix(p.B, sample.B, w), Mix(p.A, sample.A, w)));
                }
            });
        }
        return regions;

        PixelRect ToPixels(WorldRect a)
        {
            var map = PixelRect.FromEdges((int)Math.Floor(a.X / ux), (int)Math.Floor(a.Z / uz), (int)Math.Ceiling(a.Right / ux), (int)Math.Ceiling(a.Bottom / uz))
                .Intersect(image.Bounds);
            return upsideDown ? map.FlipVertical(image.Height) : map;
        }
    }

    /// <summary>
    /// The old place rebuilt from the ground around it: every pixel within the look's reach is unknown, everything
    /// around it known, and a pull-push pass (masked averages down a pyramid, then filling back up) gives a smooth fill
    /// that meets the surroundings without a seam.
    /// </summary>
    (PixelRect Area, RgbaImage Filled)? RimFill(RgbaImage image, bool upsideDown, double ux, double uz, double mapHeight)
    {
        double reach = Radius + Feather, margin = reach * 0.6;
        double left = AX - reach - margin, right = AX + reach + margin, top = AZ - reach - margin, bottom = AZ + reach + margin;
        var map = PixelRect.FromEdges((int)Math.Floor(left / ux), (int)Math.Floor(top / uz), (int)Math.Ceiling(right / ux), (int)Math.Ceiling(bottom / uz))
            .Intersect(new PixelRect(0, 0, image.Width, image.Height));
        if (map.IsEmpty)
            return null;
        var area = upsideDown ? map.FlipVertical(image.Height) : map;
        int w = area.Width, h = area.Height;
        var values = new float[w * h * 4];
        var known = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double wx = (area.X + x + 0.5) * ux;
                double wz = upsideDown ? mapHeight - (area.Y + y + 0.5) * uz : (area.Y + y + 0.5) * uz;
                if ((wx - AX) * (wx - AX) + (wz - AZ) * (wz - AZ) <= reach * reach)
                    continue;
                var p = image.GetPixel(area.X + x, area.Y + y);
                int i = y * w + x;
                (values[i * 4], values[i * 4 + 1], values[i * 4 + 2], values[i * 4 + 3], known[i]) = (p.R, p.G, p.B, p.A, 1);
            }
        PullPush(values, known, w, h);
        var filled = new RgbaImage(w, h);
        for (int i = 0; i < w * h; i++)
            filled.Pixels.AsSpan(i * 4, 4).Fill(0);
        for (int i = 0; i < w * h; i++)
            for (int c = 0; c < 4; c++)
                filled.Pixels[i * 4 + c] = (byte)Math.Clamp(Math.Round(values[i * 4 + c]), 0, 255);
        return (area, filled);
    }

    /// <summary>Fills unknown pixels (known = 0) from the known ones: weighted 2x2 averages down, bilinear blends back up.</summary>
    static void PullPush(float[] values, float[] known, int w, int h)
    {
        if (w <= 1 && h <= 1)
            return;
        int cw = Math.Max(1, (w + 1) / 2), ch = Math.Max(1, (h + 1) / 2);
        var coarse = new float[cw * ch * 4];
        var weight = new float[cw * ch];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x, j = (y / 2) * cw + x / 2;
                if (known[i] <= 0)
                    continue;
                for (int c = 0; c < 4; c++)
                    coarse[j * 4 + c] += values[i * 4 + c] * known[i];
                weight[j] += known[i];
            }
        for (int j = 0; j < cw * ch; j++)
            if (weight[j] > 0)
            {
                for (int c = 0; c < 4; c++)
                    coarse[j * 4 + c] /= weight[j];
                weight[j] = Math.Min(1, weight[j]);
            }
        if (weight.Any(v => v < 1) && (cw > 1 || ch > 1))
            PullPush(coarse, weight, cw, ch);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (known[i] >= 1)
                    continue;
                // Bilinear from the coarser level (pixel centres at (2k + 0.5) in this level's units).
                double cx = Math.Clamp((x + 0.5) / 2 - 0.5, 0, cw - 1), cy = Math.Clamp((y + 0.5) / 2 - 0.5, 0, ch - 1);
                int x0 = (int)cx, y0 = (int)cy, x1 = Math.Min(x0 + 1, cw - 1), y1 = Math.Min(y0 + 1, ch - 1);
                double fx = cx - x0, fy = cy - y0;
                for (int c = 0; c < 4; c++)
                {
                    double up = (coarse[(y0 * cw + x0) * 4 + c] * (1 - fx) + coarse[(y0 * cw + x1) * 4 + c] * fx) * (1 - fy) +
                                (coarse[(y1 * cw + x0) * 4 + c] * (1 - fx) + coarse[(y1 * cw + x1) * 4 + c] * fx) * fy;
                    values[i * 4 + c] = (float)(values[i * 4 + c] * known[i] + up * (1 - known[i]));
                }
                known[i] = 1;
            }
    }

    /// <summary>Bilinear sample at a continuous pixel position from the saved copies (the original image).</summary>
    static (double R, double G, double B, double A) Sample(List<(PixelRect Rect, RgbaImage Pixels)> sources, double x, double y, RgbaImage image)
    {
        x = Math.Clamp(x, 0, image.Width - 1);
        y = Math.Clamp(y, 0, image.Height - 1);
        int x0 = (int)x, y0 = (int)y;
        double fx = x - x0, fy = y - y0;
        double r = 0, g = 0, b = 0, a = 0;
        foreach (var (dx, dy, w) in new[] { (0, 0, (1 - fx) * (1 - fy)), (1, 0, fx * (1 - fy)), (0, 1, (1 - fx) * fy), (1, 1, fx * fy) })
        {
            int px = Math.Min(x0 + dx, image.Width - 1), py = Math.Min(y0 + dy, image.Height - 1);
            var c = Original(sources, px, py, image);
            (r, g, b, a) = (r + c.R * w, g + c.G * w, b + c.B * w, a + c.A * w);
        }
        return (r, g, b, a);
    }

    static Rgba Original(List<(PixelRect Rect, RgbaImage Pixels)> sources, int x, int y, RgbaImage image)
    {
        foreach (var (rect, pixels) in sources)
            if (x >= rect.X && y >= rect.Y && x < rect.Right && y < rect.Bottom)
                return pixels.GetPixel(x - rect.X, y - rect.Y);
        return image.GetPixel(x, y); // outside every edited area: never written, so still the original
    }

    static byte Mix(byte from, double to, double w) => (byte)Math.Clamp(Math.Round(from + (to - from) * w), 0, 255);
}
