using MapCore.Imaging;
using MapCore.Textures;

namespace MapCore.Heights;

/// <summary>
/// Keeps mapinfo's resources.detailNormalTex in step with height edits (PLAN.md, Step 6 findings). The engine
/// applies that texture on top of a normal it computes from the heightmap, with R → +x, G → +z, B → along the
/// normal. When a map's texture is a world-space bake of the slopes (its R and G follow the heightmap normal's x
/// and z), each pixel in an edited area is rotated by the rotation that takes the old heightmap normal to the new
/// one: fine detail is kept, the slope follows the terrain, and unchanged terrain is untouched.
/// </summary>
public static class DetailNormalMap
{
    /// <summary>Correlation above which a normal map counts as a world-space bake of the slopes.</summary>
    public const double WorldBakedThreshold = 0.2;

    /// <summary>
    /// How strongly the texture's R and G follow the heightmap normal's x and z (the smaller of the two Pearson
    /// correlations), sampled every <paramref name="sampleStep"/> pixels. Image in map orientation.
    /// </summary>
    public static double WorldBakedScore(RgbaImage mapOriented, HeightMap heights, int sampleStep = 8)
    {
        double unitsPerPixel = heights.Width > 1 ? (heights.Width - 1) * HeightMap.Spacing / mapOriented.Width : 1;
        double n = 0, sr = 0, sg = 0, sx = 0, sz = 0, srr = 0, sgg = 0, sxx = 0, szz = 0, srx = 0, sgz = 0;
        for (int py = sampleStep / 2; py < mapOriented.Height; py += sampleStep)
            for (int px = sampleStep / 2; px < mapOriented.Width; px += sampleStep)
            {
                var p = mapOriented.GetPixel(px, py);
                double r = p.R / 127.5 - 1, g = p.G / 127.5 - 1;
                var normal = heights.NormalAt((px + 0.5) * unitsPerPixel, (py + 0.5) * unitsPerPixel, original: true);
                n++;
                sr += r; sg += g; sx += normal.X; sz += normal.Z;
                srr += r * r; sgg += g * g; sxx += normal.X * normal.X; szz += normal.Z * normal.Z;
                srx += r * normal.X; sgz += g * normal.Z;
            }
        static double Pearson(double n, double sa, double sb, double saa, double sbb, double sab)
        {
            double cov = sab / n - sa / n * (sb / n), va = saa / n - sa * sa / (n * n), vb = sbb / n - sb * sb / (n * n);
            return va <= 0 || vb <= 0 ? 0 : cov / Math.Sqrt(va * vb);
        }
        return Math.Min(Pearson(n, sr, sx, srr, sxx, srx), Pearson(n, sg, sz, sgg, szz, sgz));
    }

    /// <summary>
    /// Rotates the pixels of <paramref name="mapOriented"/> inside <paramref name="region"/> (map-orientation pixels)
    /// by the change in the heightmap normal there. Returns the pixels actually changed.
    /// </summary>
    public static PixelRect FollowHeightChange(RgbaImage mapOriented, HeightMap heights, PixelRect region)
    {
        double unitsPerPixel = (heights.Width - 1) * HeightMap.Spacing / mapOriented.Width;
        region = region.Intersect(mapOriented.Bounds);
        int changedLeft = int.MaxValue, changedTop = int.MaxValue, changedRight = int.MinValue, changedBottom = int.MinValue;
        object gate = new();

        Parallel.For(region.Y, region.Bottom, py =>
        {
            int left = int.MaxValue, right = int.MinValue;
            for (int px = region.X; px < region.Right; px++)
            {
                double wx = (px + 0.5) * unitsPerPixel, wz = (py + 0.5) * unitsPerPixel;
                var before = heights.NormalAt(wx, wz, original: true);
                var after = heights.NormalAt(wx, wz);
                if (Math.Abs(before.X - after.X) + Math.Abs(before.Y - after.Y) + Math.Abs(before.Z - after.Z) < 1e-6)
                    continue;

                var p = mapOriented.GetPixel(px, py);
                // Texture (R, G, B) = world (x, z, y).
                var v = (X: p.R / 127.5 - 1, Y: p.B / 127.5 - 1, Z: p.G / 127.5 - 1);
                var rotated = Rotate(v, before, after);
                var updated = new Rgba(Encode(rotated.X), Encode(rotated.Z), Encode(rotated.Y), p.A);
                if (updated == p)
                    continue;
                mapOriented.SetPixel(px, py, updated);
                left = Math.Min(left, px);
                right = Math.Max(right, px);
            }
            if (left <= right)
                lock (gate)
                {
                    changedLeft = Math.Min(changedLeft, left);
                    changedRight = Math.Max(changedRight, right + 1);
                    changedTop = Math.Min(changedTop, py);
                    changedBottom = Math.Max(changedBottom, py + 1);
                }
        });
        return changedLeft == int.MaxValue ? new PixelRect(0, 0, 0, 0) : PixelRect.FromEdges(changedLeft, changedTop, changedRight, changedBottom);
    }

    /// <summary>
    /// Updates a detail normal map file after height edits: decode, turn to map orientation (map .dds files are
    /// stored upside down), rotate the edited area, and re-encode only the touched blocks and their mips.
    /// Returns false when the texture is not a world-space bake (then the engine's own normal already follows the heights).
    /// </summary>
    public static bool Update(DdsFile dds, HeightMap heights, out double score)
    {
        var image = dds.Decode();
        image.FlipVertical();
        score = WorldBakedScore(image, heights);
        if (score < WorldBakedThreshold || !heights.HasChanges)
            return false;

        // Normals depend on neighbouring vertices, and pixels interpolate between vertices: grow by two squares.
        var vertices = heights.ChangedRegion;
        double pixelsPerVertex = image.Width / ((heights.Width - 1) * 1.0);
        var region = PixelRect.FromEdges(
            (int)Math.Floor((vertices.X - 2) * pixelsPerVertex), (int)Math.Floor((vertices.Y - 2) * pixelsPerVertex),
            (int)Math.Ceiling((vertices.Right + 2) * pixelsPerVertex), (int)Math.Ceiling((vertices.Bottom + 2) * pixelsPerVertex));
        var changed = FollowHeightChange(image, heights, region);
        if (changed.IsEmpty)
            return true;

        image.FlipVertical();
        var fileRegion = changed.FlipVertical(image.Height);
        dds.Encode(image, 0, 0, fileRegion);
        dds.RebuildMips(image, 0, fileRegion);
        return true;
    }

    /// <summary>Rotates v by the rotation taking unit vector a to unit vector b (Rodrigues).</summary>
    static (double X, double Y, double Z) Rotate((double X, double Y, double Z) v, (double X, double Y, double Z) a, (double X, double Y, double Z) b)
    {
        var k = (X: a.Y * b.Z - a.Z * b.Y, Y: a.Z * b.X - a.X * b.Z, Z: a.X * b.Y - a.Y * b.X);
        double sin = Math.Sqrt(k.X * k.X + k.Y * k.Y + k.Z * k.Z), cos = a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        if (sin < 1e-12)
            return v;
        k = (k.X / sin, k.Y / sin, k.Z / sin);
        double dot = k.X * v.X + k.Y * v.Y + k.Z * v.Z;
        var cross = (X: k.Y * v.Z - k.Z * v.Y, Y: k.Z * v.X - k.X * v.Z, Z: k.X * v.Y - k.Y * v.X);
        return (v.X * cos + cross.X * sin + k.X * dot * (1 - cos),
                v.Y * cos + cross.Y * sin + k.Y * dot * (1 - cos),
                v.Z * cos + cross.Z * sin + k.Z * dot * (1 - cos));
    }

    static byte Encode(double component) => (byte)Math.Clamp(Math.Round((component + 1) * 127.5), 0, 255);
}
