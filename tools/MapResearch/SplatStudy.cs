using MapCore.Imaging;
using MapCore.Smf;
using MapCore.Textures;

namespace MapResearch;

/// <summary>
/// How are metal spots drawn? Profiles the splat distribution (resources.splatDistrTex) and the map
/// texture in rings around every metal spot found in the SMF metal map.
/// </summary>
static class SplatStudy
{
    static readonly int[] RingEdges = [0, 8, 16, 24, 32, 48, 64, 96, 128, 192];

    public static void Run(MapData map, string outDir)
    {
        var splatDds = DdsFile.Load(map.ResourcePath("splatDistrTex"));
        var splat = splatDds.Decode();
        splat.FlipVertical(); // the engine loads map .dds files upside down (Bitmap.cpp, flipDDS)
        var texture = map.Package.LoadTexture(map.Smf).Decode();
        var spots = FindSpots(map.Smf);
        double mapUnits = map.Smf.MapX * 8.0;
        double splatPerUnit = splat.Width / mapUnits, texturePerUnit = texture.Width / mapUnits;
        Console.WriteLine($"splatDistrTex: {splatDds.FormatName} {splat.Width}x{splat.Height} ({1 / splatPerUnit:F1} game units per pixel); texture {texture.Width}x{texture.Height}; {spots.Count} metal spots");

        // Whole-map use of each channel.
        Console.WriteLine("\nWhole splat map, per channel: min / mean / max / share of pixels above 0:");
        for (int c = 0; c < 4; c++)
        {
            long sum = 0, nonZero = 0;
            int min = 255, max = 0;
            for (int i = c; i < splat.Pixels.Length; i += 4)
            {
                int v = splat.Pixels[i];
                sum += v;
                min = Math.Min(min, v);
                max = Math.Max(max, v);
                if (v > 0)
                    nonZero++;
            }
            long count = splat.Pixels.Length / 4;
            Console.WriteLine($"  {"RGBA"[c]}: {min,3} / {(double)sum / count,5:F1} / {max,3} / {100.0 * nonZero / count,5:F1}%");
        }

        int rings = RingEdges.Length - 1;
        var splatSum = new double[rings, 4];
        var splatCount = new long[rings];
        var textureSum = new double[rings, 3];
        var textureCount = new long[rings];
        foreach (var spot in spots)
        {
            Accumulate(splat, splatPerUnit, spot, (ring, p) =>
            {
                splatSum[ring, 0] += p.R; splatSum[ring, 1] += p.G; splatSum[ring, 2] += p.B; splatSum[ring, 3] += p.A;
                splatCount[ring]++;
            });
            Accumulate(texture, texturePerUnit, spot, (ring, p) =>
            {
                textureSum[ring, 0] += p.R; textureSum[ring, 1] += p.G; textureSum[ring, 2] += p.B;
                textureCount[ring]++;
            });
        }

        Console.WriteLine("\nAverage over all spots, by distance from the spot centre (game units):");
        Console.WriteLine("  ring        splat R   G     B     A   |  texture R   G     B    luma");
        for (int r = 0; r < rings; r++)
        {
            double S(int c) => splatSum[r, c] / Math.Max(1, splatCount[r]);
            double T(int c) => textureSum[r, c] / Math.Max(1, textureCount[r]);
            double luma = 0.299 * T(0) + 0.587 * T(1) + 0.114 * T(2);
            Console.WriteLine($"  {RingEdges[r],3}-{RingEdges[r + 1],-3}     {S(0),5:F0} {S(1),5:F0} {S(2),5:F0} {S(3),5:F0}   |  {T(0),7:F0} {T(1),5:F0} {T(2),5:F0} {luma,6:F0}");
        }

        // Radius of the strongest splat channel's bump around each spot (where it falls below half its peak above background).
        Console.WriteLine("\nPer spot: centre, metal cells, splat channel means at centre (0-16) vs background (128-192):");
        foreach (var spot in spots)
        {
            var near = new double[4];
            var far = new double[4];
            long nearCount = 0, farCount = 0;
            Accumulate(splat, splatPerUnit, spot, (ring, p) =>
            {
                double[] target = ring <= 1 ? near : ring >= 8 ? far : null!;
                if (target == null)
                    return;
                target[0] += p.R; target[1] += p.G; target[2] += p.B; target[3] += p.A;
                if (ring <= 1) nearCount++; else farCount++;
            });
            string Means(double[] s, long n) => string.Join(" ", s.Select(v => $"{v / Math.Max(1, n),4:F0}"));
            Console.WriteLine($"  ({spot.X,5:F0}, {spot.Z,5:F0}) h={map.HeightAt(spot.X, spot.Z),4:F0} cells={spot.Cells,2}   centre RGBA {Means(near, nearCount)}   background {Means(far, farCount)}");
        }

        foreach (var (spot, name) in new[] { (spots[0], "spot_a"), (spots[spots.Count / 2], "spot_b") })
        {
            PngWriter.WriteRgba(Path.Combine(outDir, $"{name}_texture.png"), CropAround(texture, texturePerUnit, spot, 160));
            var channels = new RgbaImage(4 * 320, 320);
            var crop = CropAround(splat, splatPerUnit, spot, 160);
            for (int c = 0; c < 4; c++)
                for (int y = 0; y < 320; y++)
                    for (int x = 0; x < 320; x++)
                    {
                        var p = crop.GetPixel(Math.Min(crop.Width - 1, x * crop.Width / 320), Math.Min(crop.Height - 1, y * crop.Height / 320));
                        byte v = c switch { 0 => p.R, 1 => p.G, 2 => p.B, _ => p.A };
                        channels.SetPixel(c * 320 + x, y, new Rgba(v, v, v, 255));
                    }
            PngWriter.WriteRgba(Path.Combine(outDir, $"{name}_splat_RGBA.png"), channels);
        }
        Console.WriteLine($"\nWrote spot_a/spot_b texture crops and splat channel strips (R|G|B|A, 320x320 game units each) to {Path.GetFullPath(outDir)}");
    }

    public sealed record Spot(double X, double Z, int Cells, int Sum);

    /// <summary>8-connected groups of non-zero metal cells; centre is the metal-weighted centroid in game units.</summary>
    public static List<Spot> FindSpots(SmfFile smf)
    {
        int w = smf.MetalMapWidth, h = smf.MetalMapHeight;
        var seen = new bool[w * h];
        var spots = new List<Spot>();
        for (int start = 0; start < w * h; start++)
        {
            if (smf.MetalMap[start] == 0 || seen[start])
                continue;
            var queue = new Queue<int>([start]);
            seen[start] = true;
            double sx = 0, sz = 0;
            int sum = 0, cells = 0;
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % w, z = i / w, m = smf.MetalMap[i];
                sx += (x * 16 + 8) * m;
                sz += (z * 16 + 8) * m;
                sum += m;
                cells++;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, nz = z + dz, j = nz * w + nx;
                        if (nx >= 0 && nx < w && nz >= 0 && nz < h && smf.MetalMap[j] != 0 && !seen[j])
                        {
                            seen[j] = true;
                            queue.Enqueue(j);
                        }
                    }
            }
            spots.Add(new(sx / sum, sz / sum, cells, sum));
        }
        return spots;
    }

    static void Accumulate(RgbaImage image, double pixelsPerUnit, Spot spot, Action<int, Rgba> add)
    {
        double maxR = RingEdges[^1];
        int x0 = (int)((spot.X - maxR) * pixelsPerUnit), x1 = (int)((spot.X + maxR) * pixelsPerUnit) + 1;
        int y0 = (int)((spot.Z - maxR) * pixelsPerUnit), y1 = (int)((spot.Z + maxR) * pixelsPerUnit) + 1;
        for (int y = Math.Max(0, y0); y < Math.Min(image.Height, y1); y++)
            for (int x = Math.Max(0, x0); x < Math.Min(image.Width, x1); x++)
            {
                double d = Math.Sqrt(Math.Pow((x + 0.5) / pixelsPerUnit - spot.X, 2) + Math.Pow((y + 0.5) / pixelsPerUnit - spot.Z, 2));
                int ring = Array.FindIndex(RingEdges, 1, e => d < e) - 1;
                if (ring >= 0)
                    add(ring, image.GetPixel(x, y));
            }
    }

    static RgbaImage CropAround(RgbaImage image, double pixelsPerUnit, Spot spot, double halfUnits)
    {
        int half = (int)(halfUnits * pixelsPerUnit);
        int cx = (int)(spot.X * pixelsPerUnit), cy = (int)(spot.Z * pixelsPerUnit);
        var rect = new PixelRect(cx - half, cy - half, 2 * half, 2 * half).Intersect(image.Bounds);
        return image.Crop(rect);
    }
}
