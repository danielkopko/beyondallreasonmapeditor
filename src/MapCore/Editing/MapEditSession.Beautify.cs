using MapCore.Heights;
using MapCore.Imaging;
using MapCore.Smf;
using MapCore.Structures;

namespace MapCore.Editing;

/// <summary>
/// Beautify: the map texture painted by its terrain, as natural ground is (<see cref="BeautifySettings"/>):
/// <c>beautify-texture:&lt;settings&gt;</c>, one undo step. It reads the heights (slope, height above the water, hollows
/// and ridges, where cliffs are near) and lays each material where it belongs, mixed into the texture that is there
/// and keeping its grain. Heights, metal, water and everything placed are left as they are, and the texture is left
/// alone round metal spots (their look) and under structures. The same settings on the same map give the same bytes.
/// </summary>
public sealed partial class MapEditSession
{
    /// <summary>How far round a metal spot's centre the texture is left alone (its look), fading out over <see cref="ProtectFade"/> beyond.</summary>
    public const double SpotProtectRadius = 64, ProtectFade = 24;

    /// <summary>Paints the texture by its terrain (one undo step).</summary>
    public string BeautifyTexture(BeautifySettings settings) => Apply("beautify-texture:" + settings.Encode());

    /// <summary>
    /// The settings with the colours this map already has: its rock from its steepest ground (the greyest of the colours
    /// common there), its open ground from its level ground (the commonest colour there); dirt between the two, and sand,
    /// mud and seabed matched to its brightness. Materials the map has none of keep the colours given.
    /// </summary>
    public BeautifySettings SuggestBeautify(BeautifySettings settings)
    {
        var image = TextureImage;
        var h = Heights;
        var f = TerrainFields.From(h, settings);
        var local = LocalTexture.From(image); // the local average: not the grain's lights and shadows
        double perUnit = image.Width / MapWidth;
        Rgba At(int px, int py) => local.Mean(px, py) is var (r, g, b, _) ? new Rgba(Byte(r), Byte(g), Byte(b), 255) : default;
        var rock = new List<Rgba>();
        var ground = new List<Rgba>();
        for (int vz = 1; vz < h.Height - 1; vz += 2)
            for (int vx = 1; vx < h.Width - 1; vx += 2)
            {
                double slope = f.Slope[vz * h.Width + vx], above = h.Heights[vz * h.Width + vx];
                int px = Math.Min(image.Width - 1, (int)(vx * HeightMap.Spacing * perUnit)), py = Math.Min(image.Height - 1, (int)(vz * HeightMap.Spacing * perUnit));
                if (slope > Math.Max(settings.RockTo, 45))
                    rock.Add(At(px, py));
                else if (slope < 8 && above > settings.SandHeight * 1.5)
                    ground.Add(At(px, py));
            }
        var result = settings;
        // Rock: of the colours common on the steepest ground, the greyest (green hillsides are steep too).
        var rockColours = Clusters(rock);
        if (rock.Count >= 50 && rockColours.Where(c => c.Share >= 0.15).MinBy(c => Saturation(c.Colour)) is { } rockColour)
            result = result with { Rock = rockColour.Colour };
        if (ground.Count >= 50 && Clusters(ground).MaxBy(c => c.Share) is { } groundCluster)
        {
            var groundColour = groundCluster.Colour;
            double light = Luma(groundColour);
            Rgba Scaled(Rgba c, double luma) => Tinted(c, Math.Clamp(luma / Math.Max(1, Luma(c)), 0.55, 1.15));
            var dirt = Mixed(result.Rock, groundColour, 0.6);
            result = result with
            {
                Ground = groundColour,
                Dirt = Tinted(dirt, 1.1),
                Sand = Scaled(settings.Sand, light * 1.35),
                Mud = Tinted(dirt, 0.62),
                Seabed = new Rgba(Byte(groundColour.R * 0.55 + 14), Byte(groundColour.G * 0.55 + 26), Byte(groundColour.B * 0.55 + 26), 255),
            };
        }
        return result;

        static double Saturation(Rgba c) => Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
        static Rgba Tinted(Rgba c, double k) => new(Byte(c.R * k), Byte(c.G * k), Byte(c.B * k), 255);
        static Rgba Mixed(Rgba a, Rgba b, double t) => new(Byte(a.R + (b.R - a.R) * t), Byte(a.G + (b.G - a.G) * t), Byte(a.B + (b.B - a.B) * t), 255);
    }

    /// <summary>
    /// The main colours among some (k-means, four groups started from the darkest to the lightest), each with its share;
    /// the same colours always give the same answer.
    /// </summary>
    static List<(Rgba Colour, double Share)> Clusters(List<Rgba> colours, int k = 4)
    {
        if (colours.Count == 0)
            return [];
        var sorted = colours.OrderBy(Luma).ToList();
        var centres = Enumerable.Range(0, k).Select(i => sorted[(int)((i + 0.5) / k * sorted.Count)]).Select(c => (R: (double)c.R, G: (double)c.G, B: (double)c.B)).ToArray();
        var group = new int[colours.Count];
        for (int round = 0; round < 12; round++)
        {
            for (int i = 0; i < colours.Count; i++)
            {
                var c = colours[i];
                group[i] = Enumerable.Range(0, k).MinBy(j => (c.R - centres[j].R) * (c.R - centres[j].R) + (c.G - centres[j].G) * (c.G - centres[j].G) + (c.B - centres[j].B) * (c.B - centres[j].B));
            }
            for (int j = 0; j < k; j++)
            {
                var members = Enumerable.Range(0, colours.Count).Where(i => group[i] == j).Select(i => colours[i]).ToList();
                if (members.Count > 0)
                    centres[j] = (members.Average(c => c.R), members.Average(c => c.G), members.Average(c => c.B));
            }
        }
        return Enumerable.Range(0, k).Select(j => (new Rgba(Byte(centres[j].R), Byte(centres[j].G), Byte(centres[j].B), 255), group.Count(g => g == j) / (double)colours.Count))
            .Where(c => c.Item2 > 0).ToList();
    }

    static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    string BeautifyTextureCore(string[] args)
    {
        Expect(args, 1, "beautify-texture");
        var settings = BeautifySettings.Decode(args[0]);
        if (settings.Problem is string problem)
            throw new InvalidOperationException($"Beautify can't run: {problem}.");
        if (settings.FromMap)
            settings = SuggestBeautify(settings); // the map's own colours, read from it as it is now
        var mirror = settings.Mirror is MirrorKind kind ? new MapMirror(kind, MapWidth, MapHeight) : (MapMirror?)null;
        var image = TextureImage;
        var fields = TerrainFields.From(Heights, settings);
        var local = LocalTexture.From(image);
        var protect = ProtectionMask(image);
        bool water = Heights.Heights.Any(v => v < 0);
        double perUnit = image.Width / MapWidth;
        int blocksX = (image.Width + 3) / 4;
        var touched = new bool[blocksX * ((image.Height + 3) / 4)];
        var s = settings;

        Parallel.For(0, image.Height, y =>
        {
            for (int x = 0; x < image.Width; x++)
            {
                double keep = protect.At(x, y);
                if (keep >= 1)
                    continue;
                double wx = (x + 0.5) / perUnit, wz = (y + 0.5) / perUnit;
                var (nx, nz) = Canonical(mirror, wx, wz);
                double n1 = Fbm(nx / 70, nz / 70, 3), n2 = Fbm(nx / 23 + 5.2, nz / 23 + 1.3, 3), n3 = TerrainNoise.Sample(nx / 6 + 11.1, nz / 6 + 7.7);
                double ragged = TerrainNoise.Sample(nx / 4.5 + 2.2, nz / 4.5 + 8.4); // fine: material edges ragged, not smooth
                double above = fields.Sample(fields.Above, wx, wz), slope = fields.Sample(fields.Slope, wx, wz);
                double hollow = fields.Sample(fields.Hollow, wx, wz), basin = fields.Sample(fields.Basin, wx, wz), nearRock = fields.Sample(fields.NearRock, wx, wz);

                var c0 = image.GetPixel(x, y);
                var (mr, mg, mb, ml) = local.Mean(x, y);
                double detail = Luma(c0) - ml;
                double r = c0.R, g = c0.G, b = c0.B;
                // Each material a little lighter or darker in broad patches, as ground is.
                double patch = 1 + 0.1 * s.Breakup * Fbm(nx / 260 + 7.7, nz / 260 + 2.2, 2);
                void Lay(Rgba colour, double detailScale, double w, double brightness = 1)
                {
                    w *= s.Strength;
                    if (w <= 0)
                        return;
                    w = Math.Min(1, w);
                    brightness *= patch;
                    r += (colour.R * brightness + detail * detailScale - r) * w;
                    g += (colour.G * brightness + detail * detailScale - g) * w;
                    b += (colour.B * brightness + detail * detailScale - b) * w;
                }

                // Hard edges already in the texture: a dirty blend across them.
                if (s.SoftenEdges > 0 && local.Edge(x, y) is double edge and > 0)
                {
                    var blend = new Rgba(Byte(mr + (s.Dirt.R - mr) * 0.35), Byte(mg + (s.Dirt.G - mg) * 0.35), Byte(mb + (s.Dirt.B - mb) * 0.35), 255);
                    Lay(blend, 1, s.SoftenEdges * edge * Smooth(-0.3, 0.5, n2));
                }
                double rocky = Smooth(s.RockFrom, s.RockTo, slope);
                // Open ground, then dirt on middle slopes and heaped at cliff feet, then rock with its strata.
                Lay(s.Ground, 1, s.GroundAmount * (1 - Smooth(s.DirtFrom - 6, s.DirtFrom, slope)) * (water ? Smooth(s.SandHeight, s.SandHeight * 1.5, above) : 1));
                double middle = Smooth(s.DirtFrom, s.DirtTo, slope + s.Breakup * (6 * n2 + 4 * ragged)) * (1 - rocky);
                double foot = Smooth(0.15, 0.55, nearRock * (1 - rocky) * Math.Clamp(0.6 + basin / 10, 0, 1) + s.Breakup * (0.25 * n2 + 0.12 * ragged));
                double dirty = s.DirtAmount * Math.Max(middle * 0.8, foot);
                Lay(s.Dirt, 1, dirty);
                // Strata: layers that follow the height, wandering and fading in and out, as bedding does in a cliff.
                double layers = above + 60 * Fbm(nx / 180 + 3.3, nz / 180 + 9.1, 2);
                double strata = (0.55 + 0.45 * Fbm(nx / 90 + 1.9, nz / 90 + 4.4, 2)) * Math.Sin(2 * Math.PI * layers / 42) +
                                0.3 * Math.Sin(2 * Math.PI * layers / 17 + 3 * Fbm(nx / 40 + 8.8, nz / 40 + 0.6, 2));
                double rockWeight = s.RockAmount * Smooth(s.RockFrom, s.RockTo, slope + s.Breakup * (8 * n1 + 5 * ragged));
                Lay(s.Rock, 1.2, rockWeight, 1 + s.Strata * 0.12 * strata + 0.06 * n3);
                if (water)
                {
                    // Under the water a darker bed, then sand along the shore and a wet edge at the waterline.
                    Lay(s.Seabed, 0.6, s.SeabedAmount * Smooth(0, 50, -above));
                    double flat = 1 - Smooth(28, 42, slope);
                    Lay(s.Sand, 0.8, s.SandAmount * flat * Smooth(-8, -1, above) *
                                     (1 - Smooth(s.SandHeight * 0.45, s.SandHeight, above + s.Breakup * s.SandHeight * (0.35 * n1 + 0.12 * ragged))));
                    Lay(s.Mud, 0.7, s.MudAmount * flat * (1 - Smooth(2, 7, Math.Abs(above) + 3 * s.Breakup * n2)));
                }
                // Made-up grain and tone, where the texture has little grain of its own: cracks on rock, speckle on dirt,
                // soft clumps elsewhere, and broad patches a little lighter or darker, warmer or cooler.
                if (s.Detail > 0)
                {
                    double plain = s.Detail * s.Strength * Math.Clamp(1 - local.Roughness(x, y) / 10, 0, 1);
                    if (plain > 0)
                    {
                        double fine = 0.6 * TerrainNoise.Sample(nx / 2.3 + 3.1, nz / 2.3 + 5.9) + 0.4 * TerrainNoise.Sample(nx / 1.1 + 9.4, nz / 1.1 + 2.6);
                        double cracks = 1 - 2 * Math.Abs(TerrainNoise.Sample(nx / 11 + 4.2, nz / 11 + 6.3));
                        double clumps = Fbm(nx / 7 + 2.7, nz / 7 + 7.1, 2);
                        double rockShare = Math.Min(1, rockWeight * s.Strength), dirtShare = Math.Min(1, dirty * s.Strength);
                        double grain = 14 * fine - rockShare * 26 * Math.Max(0, cracks - 0.55) + (1 - rockShare) * 10 * clumps + dirtShare * 6 * n3;
                        double tone = 0.13 * Fbm(nx / 420 + 6.6, nz / 420 + 3.8, 3) + 0.05 * Fbm(nx / 60 + 4.4, nz / 60 + 1.7, 2), warm = 0.05 * Fbm(nx / 600 + 1.4, nz / 600 + 8.2, 2);
                        r = (r * (1 + tone + warm) + grain) * plain + r * (1 - plain);
                        g = (g * (1 + tone) + grain) * plain + g * (1 - plain);
                        b = (b * (1 + tone - warm) + grain) * plain + b * (1 - plain);
                    }
                }
                // Shading: hollows and cliff feet darker, ridges a little lighter.
                double shade = 1 - s.Strength * s.Crevices * (0.45 * Math.Clamp(hollow / 5, 0, 1) + 0.2 * Math.Clamp(basin / 14, 0, 1)) +
                               s.Strength * s.Crevices * 0.1 * Math.Clamp(-hollow / 5, 0, 1);
                r *= shade;
                g *= shade;
                b *= shade;

                var done = new Rgba(Byte(c0.R + (r - c0.R) * (1 - keep)), Byte(c0.G + (g - c0.G) * (1 - keep)), Byte(c0.B + (b - c0.B) * (1 - keep)), 255);
                if (done == c0)
                    continue;
                image.SetPixel(x, y, done);
                touched[(y / 4) * blocksX + x / 4] = true;
            }
        });

        var blocks = new List<PixelRect>();
        for (int i = 0; i < touched.Length; i++)
            if (touched[i])
                blocks.Add(new PixelRect(i % blocksX * 4, i / blocksX * 4, 4, 4));
        if (blocks.Count == 0)
            return "beautify: nothing to change";
        var update = Texture.UpdateBlocks(image, blocks);
        var area = PixelRect.FromEdges(blocks.Min(b => b.X), blocks.Min(b => b.Y), blocks.Max(b => b.Right), blocks.Max(b => b.Bottom));
        RebuildMinimap(area);
        RefreshTexture(area);
        textureChanged = true;
        return $"beautify: the texture painted by its terrain ({blocks.Count * 16.0 / (image.Width * (double)image.Height):P0} of it changed, " +
               $"{update.TilesRewritten:N0} tiles re-encoded); metal spots and structures left as they were" +
               (water ? "" : "; no water, so no shore") + (mirror is MapMirror m ? $"; noise mirrored {m.Code}" : "");
    }

    /// <summary>A noise point: the point itself, or for a symmetric map whichever of it and its mirror image comes first, so both read the same noise.</summary>
    static (double X, double Z) Canonical(MapMirror? mirror, double x, double z)
    {
        if (mirror is not MapMirror m)
            return (x, z);
        var (mx, mz) = m.Map(x, z);
        return x < mx || (x == mx && z <= mz) ? (x, z) : (mx, mz);
    }

    /// <summary>Smooth noise in about [-1, 1] with finer octaves added (each half the size and half as strong).</summary>
    static double Fbm(double x, double z, int octaves)
    {
        double sum = 0, amplitude = 1, total = 0;
        for (int i = 0; i < octaves; i++)
        {
            sum += amplitude * TerrainNoise.Sample(x, z);
            total += amplitude;
            (x, z, amplitude) = (x * 2 + 17.3, z * 2 + 31.7, amplitude * 0.5);
        }
        return sum / total;
    }

    /// <summary>
    /// How much of the texture to leave alone at each pixel (0 change it, 1 leave it), at a quarter of its size: round
    /// metal spots (their look) and under structures and their repair markers, fading out at the edges.
    /// </summary>
    QuarterMask ProtectionMask(RgbaImage image) => ProtectionMask(image.Width, image.Height);

    /// <summary>The same for an image of any size covering the map (the splat map's, say).</summary>
    QuarterMask ProtectionMask(int width, int height)
    {
        var mask = new QuarterMask(width, height, width / MapWidth);
        foreach (var spot in Metal.FindSpots())
            mask.Circle(spot.X, spot.Z, SpotProtectRadius, ProtectFade);
        var config = Structures;
        foreach (var instance in config.Instances)
        {
            if (config.Kinds.TryGetValue(instance.Kind, out var kind))
                foreach (var part in StructureLayout.Parts(kind, instance))
                    mask.Rectangle(part.X1, part.Z1, part.X2, part.Z2, 16, ProtectFade);
            foreach (var (mx, mz) in instance.MarkerList)
                mask.Circle(mx, mz, 24, ProtectFade);
        }
        return mask;
    }

    /// <summary>A mask at a quarter of the texture's size (4 x 4 pixels a cell).</summary>
    sealed class QuarterMask(int width, int height, double perUnit)
    {
        readonly int w = (width + 3) / 4, h = (height + 3) / 4;
        readonly float[] cells = new float[((width + 3) / 4) * ((height + 3) / 4)];

        public double At(int x, int y) => cells[(y / 4) * w + x / 4];

        void Stamp(double minX, double minZ, double maxX, double maxZ, Func<double, double, double> value)
        {
            double q = perUnit / 4;
            int x0 = Math.Max(0, (int)(minX * q)), x1 = Math.Min(w - 1, (int)(maxX * q) + 1);
            int z0 = Math.Max(0, (int)(minZ * q)), z1 = Math.Min(h - 1, (int)(maxZ * q) + 1);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    double v = value((x + 0.5) / q, (z + 0.5) / q);
                    if (v > cells[z * w + x])
                        cells[z * w + x] = (float)v;
                }
        }

        public void Circle(double cx, double cz, double radius, double fade) =>
            Stamp(cx - radius - fade, cz - radius - fade, cx + radius + fade, cz + radius + fade,
                (x, z) => 1 - Math.Clamp((Math.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)) - radius) / fade, 0, 1));

        public void Rectangle(double x1, double z1, double x2, double z2, double margin, double fade) =>
            Stamp(x1 - margin - fade, z1 - margin - fade, x2 + margin + fade, z2 + margin + fade, (x, z) =>
            {
                double dx = Math.Max(0, Math.Max(x1 - margin - x, x - x2 - margin)), dz = Math.Max(0, Math.Max(z1 - margin - z, z - z2 - margin));
                return 1 - Math.Clamp(Math.Sqrt(dx * dx + dz * dz) / fade, 0, 1);
            });
    }

    /// <summary>
    /// What the heights say about the ground, per vertex (8 game units apart): height above the water, slope (degrees,
    /// smoothed over a vertex), hollows (how far below the ground round it a point is, over 24 and 64 units; negative on
    /// ridges) and how near cliffs are (rock's share within about 50 units).
    /// </summary>
    sealed class TerrainFields
    {
        public required int Width { get; init; }
        public required int Height { get; init; }
        /// <summary>Height above the water (game units; negative under it).</summary>
        public required float[] Above { get; init; }
        public required float[] Slope { get; init; }
        public required float[] Hollow { get; init; }
        public required float[] Basin { get; init; }
        public required float[] NearRock { get; init; }

        public static TerrainFields From(HeightMap heights, BeautifySettings settings)
        {
            int w = heights.Width, h = heights.Height;
            var height = heights.Heights;
            var slope = new float[w * h];
            Parallel.For(0, h, z =>
            {
                for (int x = 0; x < w; x++)
                {
                    int x0 = Math.Max(0, x - 1), x1 = Math.Min(w - 1, x + 1), z0 = Math.Max(0, z - 1), z1 = Math.Min(h - 1, z + 1);
                    double gx = (height[z * w + x1] - height[z * w + x0]) / ((x1 - x0) * HeightMap.Spacing);
                    double gz = (height[z1 * w + x] - height[z0 * w + x]) / ((z1 - z0) * HeightMap.Spacing);
                    slope[z * w + x] = (float)(Math.Atan(Math.Sqrt(gx * gx + gz * gz)) * 180 / Math.PI);
                }
            });
            slope = BoxBlur(slope, w, h, 1);
            var near3 = BoxBlur(height, w, h, 3);
            var near8 = BoxBlur(height, w, h, 8);
            var hollow = new float[w * h];
            var basin = new float[w * h];
            var rock = new float[w * h];
            for (int i = 0; i < hollow.Length; i++)
            {
                hollow[i] = near3[i] - height[i];
                basin[i] = near8[i] - height[i];
                rock[i] = (float)Smooth(settings.RockFrom, settings.RockTo, slope[i]);
            }
            return new TerrainFields
            {
                Width = w, Height = h, Above = (float[])height.Clone(), Slope = slope, Hollow = hollow, Basin = basin, NearRock = BoxBlur(rock, w, h, 6),
            };
        }

        /// <summary>A field at a world point, bilinear between vertices.</summary>
        public double Sample(float[] field, double x, double z)
        {
            double gx = Math.Clamp(x / HeightMap.Spacing, 0, Width - 1), gz = Math.Clamp(z / HeightMap.Spacing, 0, Height - 1);
            int x0 = Math.Min((int)gx, Width - 2), z0 = Math.Min((int)gz, Height - 2);
            double fx = gx - x0, fz = gz - z0;
            int i = z0 * Width + x0;
            double top = field[i] + (field[i + 1] - field[i]) * fx, bottom = field[i + Width] + (field[i + Width + 1] - field[i + Width]) * fx;
            return top + (bottom - top) * fz;
        }
    }

    /// <summary>A box blur of a grid (radius in cells, edges clamped), done as two passes of running sums.</summary>
    static float[] BoxBlur(float[] source, int w, int h, int radius)
    {
        var across = new float[source.Length];
        var result = new float[source.Length];
        Parallel.For(0, h, z =>
        {
            double sum = 0;
            for (int i = -radius; i <= radius; i++)
                sum += source[z * w + Math.Clamp(i, 0, w - 1)];
            for (int x = 0; x < w; x++)
            {
                across[z * w + x] = (float)(sum / (2 * radius + 1));
                sum += source[z * w + Math.Min(w - 1, x + radius + 1)] - source[z * w + Math.Max(0, x - radius)];
            }
        });
        Parallel.For(0, w, x =>
        {
            double sum = 0;
            for (int i = -radius; i <= radius; i++)
                sum += across[Math.Clamp(i, 0, h - 1) * w + x];
            for (int z = 0; z < h; z++)
            {
                result[z * w + x] = (float)(sum / (2 * radius + 1));
                sum += across[Math.Min(h - 1, z + radius + 1) * w + x] - across[Math.Max(0, z - radius) * w + x];
            }
        });
        return result;
    }

    /// <summary>
    /// The texture's local average colour and brightness (over about 28 pixels) and how sharp an edge it has there, at a
    /// quarter of its size: the grain is a pixel's difference from the local average.
    /// </summary>
    sealed class LocalTexture
    {
        int w, h;
        float[] r = [], g = [], b = [], l = [], edge = [], rough = [];

        public static LocalTexture From(RgbaImage image)
        {
            int w = (image.Width + 3) / 4, h = (image.Height + 3) / 4;
            var r = new float[w * h];
            var g = new float[w * h];
            var b = new float[w * h];
            Parallel.For(0, h, qy =>
            {
                for (int qx = 0; qx < w; qx++)
                {
                    double sr = 0, sg = 0, sb = 0;
                    int n = 0;
                    for (int y = qy * 4; y < Math.Min(image.Height, qy * 4 + 4); y++)
                        for (int x = qx * 4; x < Math.Min(image.Width, qx * 4 + 4); x++)
                        {
                            var p = image.GetPixel(x, y);
                            (sr, sg, sb, n) = (sr + p.R, sg + p.G, sb + p.B, n + 1);
                        }
                    (r[qy * w + qx], g[qy * w + qx], b[qy * w + qx]) = ((float)(sr / n), (float)(sg / n), (float)(sb / n));
                }
            });
            var luma = new float[w * h];
            for (int i = 0; i < luma.Length; i++)
                luma[i] = (float)(0.299 * r[i] + 0.587 * g[i] + 0.114 * b[i]);
            // Edges between regions, not grain: the local average (over about 28 pixels) compared 24 pixels apart, so
            // the grain averages out and only a change of ground (a plate, a road, a crater's rim) stands out.
            var mean = BoxBlur(luma, w, h, 3);
            var edge = new float[w * h];
            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    float L(int ax, int ay) => mean[Math.Clamp(ay, 0, h - 1) * w + Math.Clamp(ax, 0, w - 1)];
                    double d = Math.Max(Math.Abs(L(x + 3, y) - L(x - 3, y)), Math.Abs(L(x, y + 3) - L(x, y - 3)));
                    edge[y * w + x] = (float)Smooth(12, 36, d);
                }
            });
            // Grain: how far pixels stray from their 4 x 4 cell's average, averaged round about.
            var grain = new float[w * h];
            Parallel.For(0, h, qy =>
            {
                for (int qx = 0; qx < w; qx++)
                {
                    double mean = luma[qy * w + qx], sum = 0;
                    int n = 0;
                    for (int y = qy * 4; y < Math.Min(image.Height, qy * 4 + 4); y++)
                        for (int x = qx * 4; x < Math.Min(image.Width, qx * 4 + 4); x++)
                        {
                            sum += Math.Abs(Luma(image.GetPixel(x, y)) - mean);
                            n++;
                        }
                    grain[qy * w + qx] = (float)(sum / n);
                }
            });
            return new LocalTexture
            {
                w = w, h = h, r = BoxBlur(r, w, h, 3), g = BoxBlur(g, w, h, 3), b = BoxBlur(b, w, h, 3), l = BoxBlur(luma, w, h, 3), edge = BoxBlur(edge, w, h, 2),
                rough = BoxBlur(grain, w, h, 3),
            };
        }

        /// <summary>The local average at a pixel (bilinear between quarter cells).</summary>
        public (double R, double G, double B, double L) Mean(int x, int y)
        {
            double qx = Math.Clamp((x + 0.5) / 4 - 0.5, 0, w - 1), qy = Math.Clamp((y + 0.5) / 4 - 0.5, 0, h - 1);
            int x0 = Math.Min((int)qx, w - 2), y0 = Math.Min((int)qy, h - 2);
            double fx = qx - x0, fy = qy - y0;
            double S(float[] f)
            {
                int i = y0 * w + x0;
                double top = f[i] + (f[i + 1] - f[i]) * fx, bottom = f[i + w] + (f[i + w + 1] - f[i + w]) * fx;
                return top + (bottom - top) * fy;
            }
            return (S(r), S(g), S(b), S(l));
        }

        public double Edge(int x, int y) => edge[Math.Min(h - 1, y / 4) * w + Math.Min(w - 1, x / 4)];

        /// <summary>How much grain the texture has round a pixel (average difference from the local brightness, 0-255).</summary>
        public double Roughness(int x, int y) => rough[Math.Min(h - 1, y / 4) * w + Math.Min(w - 1, x / 4)];
    }
}
