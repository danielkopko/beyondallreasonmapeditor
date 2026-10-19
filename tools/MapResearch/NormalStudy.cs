using MapCore.Imaging;
using MapCore.Textures;

namespace MapResearch;

/// <summary>
/// How does the detail normal map (mapinfo resources.detailNormalTex) relate to the heightmap?
/// The engine applies it in tangent space on top of a normal it computes from the heightmap
/// (SMFFragProg.glsl, SMF_BLEND_NORMALS: R -> +x, G -> +z, B -> along the base normal).
/// </summary>
static class NormalStudy
{
    public static void Run(MapData map, string outDir)
    {
        var dds = DdsFile.Load(map.ResourcePath("detailNormalTex"));
        var image = dds.Decode();
        image.FlipVertical(); // the engine loads map .dds files upside down (Bitmap.cpp, flipDDS)
        int squaresX = map.Smf.MapX, squaresZ = map.Smf.MapY;
        int perSquare = image.Width / squaresX;
        double unitsPerPixel = 8.0 / perSquare;
        Console.WriteLine($"detailNormalTex: {dds.FormatName} {image.Width}x{image.Height} ({perSquare}x{perSquare} pixels per heightmap square); heights {map.MinHeight} to {map.MaxHeight}");

        var normals = VertexNormals(map);

        // 1. Which decoded channel follows which component of the heightmap normal?
        var corr = new Correlations("R", "G", "B", "nx", "ny", "nz");
        Parallel.For(0, image.Height, () => new Correlations("R", "G", "B", "nx", "ny", "nz"), (py, _, local) =>
        {
            Span<double> v = stackalloc double[6];
            for (int px = 0; px < image.Width; px++)
            {
                var d = Decoded(image, px, py);
                var n = NormalAt(map, normals, (px + 0.5) * unitsPerPixel, (py + 0.5) * unitsPerPixel);
                (v[0], v[1], v[2], v[3], v[4], v[5]) = (d.X, d.Y, d.Z, n.X, n.Y, n.Z);
                local.Add(v);
            }
            return local;
        }, corr.Merge);

        Console.WriteLine("\nPearson correlation, decoded channel vs heightmap normal component (x east, y up, z south):");
        foreach (var channel in new[] { "R", "G", "B" })
            Console.WriteLine($"  {channel}:  nx {corr.Pearson(channel, "nx"),6:F3}   ny {corr.Pearson(channel, "ny"),6:F3}   nz {corr.Pearson(channel, "nz"),6:F3}   (mean {corr.Mean(channel),6:F3})");

        // Horizontal channel mapping and signs, as measured.
        bool rIsX = Math.Abs(corr.Pearson("R", "nx")) >= Math.Abs(corr.Pearson("R", "nz"));
        double signR = Math.Sign(corr.Pearson("R", rIsX ? "nx" : "nz")), signG = Math.Sign(corr.Pearson("G", rIsX ? "nz" : "nx"));
        Console.WriteLine($"  => R ~ {(signR < 0 ? "-" : "+")}{(rIsX ? "x" : "z")}, G ~ {(signG < 0 ? "-" : "+")}{(rIsX ? "z" : "x")}, B ~ up");
        (double X, double Y, double Z) Predict((double X, double Y, double Z) n) =>
            rIsX ? (signR * n.X, signG * n.Z, n.Y) : (signR * n.Z, signG * n.X, n.Y);

        // 2. Per-pixel agreement: world-space bake of the heightmap normal vs flat tangent-space detail.
        var world = new AngleHistogram();
        var flat = new AngleHistogram();
        var worldSteep = new AngleHistogram();
        var flatSteep = new AngleHistogram();
        double steepCos = Math.Cos(20 * Math.PI / 180);
        Parallel.For(0, image.Height, () => new AngleHistogram[4].Select(_ => new AngleHistogram()).ToArray(), (py, _, local) =>
        {
            for (int px = 0; px < image.Width; px++)
            {
                var d = Normalize(Decoded(image, px, py));
                var n = NormalAt(map, normals, (px + 0.5) * unitsPerPixel, (py + 0.5) * unitsPerPixel);
                double toWorld = Angle(d, Predict(n)), toFlat = Angle(d, (0, 0, 1));
                local[0].Add(toWorld);
                local[1].Add(toFlat);
                if (n.Y < steepCos)
                {
                    local[2].Add(toWorld);
                    local[3].Add(toFlat);
                }
            }
            return local;
        }, local => { world.Merge(local[0]); flat.Merge(local[1]); worldSteep.Merge(local[2]); flatSteep.Merge(local[3]); });

        Console.WriteLine("\nAngle between each decoded normal and ...");
        Console.WriteLine($"  the heightmap normal (world-space bake), all pixels : {world}");
        Console.WriteLine($"  straight up (tangent-space detail only), all pixels: {flat}");
        Console.WriteLine($"  world-space bake, slopes over 20°                   : {worldSteep}");
        Console.WriteLine($"  straight up, slopes over 20°                        : {flatSteep}");

        // 3. Per heightmap square: does the low-pass normal map match the heightmap, and how much finer detail is there?
        var lowPass = new AngleHistogram();
        var detailLand = new AngleHistogram();
        var detailSea = new AngleHistogram();
        var ratios = new List<double>();
        var heightmapImage = new RgbaImage(squaresX, squaresZ);
        var lowPassImage = new RgbaImage(squaresX, squaresZ);
        var detailImage = new RgbaImage(squaresX, squaresZ);
        Parallel.For(0, squaresZ, () => (Low: new AngleHistogram(), Land: new AngleHistogram(), Sea: new AngleHistogram(), Ratios: new List<double>()), (sz, _, local) =>
        {
            for (int sx = 0; sx < squaresX; sx++)
            {
                (double X, double Y, double Z) sum = (0, 0, 0);
                for (int y = 0; y < perSquare; y++)
                    for (int x = 0; x < perSquare; x++)
                    {
                        var d = Normalize(Decoded(image, sx * perSquare + x, sz * perSquare + y));
                        sum = (sum.X + d.X, sum.Y + d.Y, sum.Z + d.Z);
                    }
                var average = Normalize(sum);
                var centre = Normalize(Add(Add(Vertex(normals, map, sx, sz), Vertex(normals, map, sx + 1, sz)),
                                           Add(Vertex(normals, map, sx, sz + 1), Vertex(normals, map, sx + 1, sz + 1))));
                local.Low.Add(Angle(average, Predict(centre)));

                bool sea = (map.Height(sx, sz) + map.Height(sx + 1, sz + 1)) / 2 < 0;
                double residual = 0;
                for (int y = 0; y < perSquare; y++)
                    for (int x = 0; x < perSquare; x++)
                    {
                        double a = Angle(Normalize(Decoded(image, sx * perSquare + x, sz * perSquare + y)), average);
                        (sea ? local.Sea : local.Land).Add(a);
                        residual += a;
                    }

                // Tilt of the baked normal relative to the heightmap gradient (1.0 = baked with the same heights).
                double gx = (map.Height(sx + 1, sz) + map.Height(sx + 1, sz + 1) - map.Height(sx, sz) - map.Height(sx, sz + 1)) / 16;
                double gz = (map.Height(sx, sz + 1) + map.Height(sx + 1, sz + 1) - map.Height(sx, sz) - map.Height(sx + 1, sz)) / 16;
                double tanHeight = Math.Sqrt(gx * gx + gz * gz), tanBaked = Math.Sqrt(average.X * average.X + average.Y * average.Y) / average.Z;
                if (tanHeight > 0.2)
                    local.Ratios.Add(tanBaked / tanHeight);

                heightmapImage.SetPixel(sx, sz, Encode(Predict(centre)));
                lowPassImage.SetPixel(sx, sz, Encode(average));
                byte grey = (byte)Math.Min(255, residual / (perSquare * perSquare) * 8);
                detailImage.SetPixel(sx, sz, new Rgba(grey, grey, grey, 255));
            }
            return local;
        }, local =>
        {
            lowPass.Merge(local.Low);
            detailLand.Merge(local.Land);
            detailSea.Merge(local.Sea);
            lock (ratios)
                ratios.AddRange(local.Ratios);
        });

        ratios.Sort();
        Console.WriteLine("\nPer heightmap square (8x8 game units):");
        Console.WriteLine($"  averaged normal map vs heightmap normal (world bake) : {lowPass}");
        Console.WriteLine($"  detail finer than a square, land pixels              : {detailLand}");
        Console.WriteLine($"  detail finer than a square, sea-floor pixels         : {detailSea}");
        if (ratios.Count > 0)
            Console.WriteLine($"  baked tilt / heightmap tilt on slopes over ~11°: median {ratios[ratios.Count / 2]:F2} (p25 {ratios[ratios.Count / 4]:F2}, p75 {ratios[ratios.Count * 3 / 4]:F2}, n={ratios.Count:N0})");
        Console.WriteLine($"  (heights from mapinfo: {map.MinHeight}..{map.MaxHeight}; the SMF header's range would make the heightmap tilt {(map.Smf.MaxHeight - map.Smf.MinHeight) / (map.MaxHeight - map.MinHeight):F2}x as steep)");

        PngWriter.WriteRgba(Path.Combine(outDir, "normals_from_heightmap.png"), heightmapImage);
        PngWriter.WriteRgba(Path.Combine(outDir, "normals_lowpass.png"), lowPassImage);
        PngWriter.WriteRgba(Path.Combine(outDir, "normals_detail.png"), detailImage);
        Console.WriteLine($"\nWrote normals_from_heightmap.png, normals_lowpass.png and normals_detail.png ({squaresX}x{squaresZ}) to {Path.GetFullPath(outDir)}");
    }

    /// <summary>Per-vertex normals (x east, y up, z south) from central differences of the heights.</summary>
    static float[] VertexNormals(MapData map)
    {
        var normals = new float[map.VerticesX * map.VerticesZ * 3];
        Parallel.For(0, map.VerticesZ, z =>
        {
            for (int x = 0; x < map.VerticesX; x++)
            {
                int x0 = Math.Max(x - 1, 0), x1 = Math.Min(x + 1, map.VerticesX - 1);
                int z0 = Math.Max(z - 1, 0), z1 = Math.Min(z + 1, map.VerticesZ - 1);
                float dx = (map.Height(x1, z) - map.Height(x0, z)) / ((x1 - x0) * 8f);
                float dz = (map.Height(x, z1) - map.Height(x, z0)) / ((z1 - z0) * 8f);
                float length = MathF.Sqrt(dx * dx + 1 + dz * dz);
                int i = (z * map.VerticesX + x) * 3;
                (normals[i], normals[i + 1], normals[i + 2]) = (-dx / length, 1 / length, -dz / length);
            }
        });
        return normals;
    }

    static (double X, double Y, double Z) Vertex(float[] normals, MapData map, int x, int z)
    {
        int i = (Math.Clamp(z, 0, map.VerticesZ - 1) * map.VerticesX + Math.Clamp(x, 0, map.VerticesX - 1)) * 3;
        return (normals[i], normals[i + 1], normals[i + 2]);
    }

    /// <summary>Bilinearly interpolated heightmap normal at a world position, as the engine's normal texture is sampled.</summary>
    static (double X, double Y, double Z) NormalAt(MapData map, float[] normals, double worldX, double worldZ)
    {
        double u = worldX / 8, v = worldZ / 8;
        int x = (int)u, z = (int)v;
        double fx = u - x, fz = v - z;
        var top = Lerp(Vertex(normals, map, x, z), Vertex(normals, map, x + 1, z), fx);
        var bottom = Lerp(Vertex(normals, map, x, z + 1), Vertex(normals, map, x + 1, z + 1), fx);
        return Normalize(Lerp(top, bottom, fz));
    }

    static (double X, double Y, double Z) Decoded(RgbaImage image, int x, int y)
    {
        var p = image.GetPixel(x, y);
        return (p.R / 127.5 - 1, p.G / 127.5 - 1, p.B / 127.5 - 1);
    }

    static Rgba Encode((double X, double Y, double Z) v) =>
        new((byte)Math.Clamp((v.X + 1) * 127.5, 0, 255), (byte)Math.Clamp((v.Y + 1) * 127.5, 0, 255), (byte)Math.Clamp((v.Z + 1) * 127.5, 0, 255), 255);

    static (double X, double Y, double Z) Lerp((double X, double Y, double Z) a, (double X, double Y, double Z) b, double t) =>
        (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    static (double X, double Y, double Z) Add((double X, double Y, double Z) a, (double X, double Y, double Z) b) => (a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    static (double X, double Y, double Z) Normalize((double X, double Y, double Z) v)
    {
        double length = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return length == 0 ? (0, 0, 1) : (v.X / length, v.Y / length, v.Z / length);
    }

    static double Angle((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        Math.Acos(Math.Clamp(a.X * b.X + a.Y * b.Y + a.Z * b.Z, -1, 1)) * 180 / Math.PI;
}
