using MapCore.Heights;
using MapCore.Imaging;
using MapCore.Smf;
using MapCore.Textures;

namespace MapCore.Editing;

/// <summary>
/// Brushes on the map texture (paint, tint, adjust, clone) and on the splat distribution map (which detail texture
/// shows where).
/// </summary>
public sealed partial class MapEditSession
{
    /// <summary>The texture pixels a brush can touch.</summary>
    PixelRect TextureRegion(Brush brush)
    {
        double perUnit = Texture.Width / MapWidth;
        return PixelRect.FromEdges(
            (int)Math.Floor((brush.X - brush.Radius) * perUnit), (int)Math.Floor((brush.Z - brush.Radius) * perUnit),
            (int)Math.Ceiling((brush.X + brush.Radius) * perUnit) + 1, (int)Math.Ceiling((brush.Z + brush.Radius) * perUnit) + 1)
            .Intersect(Texture.Bounds);
    }

    /// <summary>
    /// Blends each texture pixel under the brush towards <paramref name="target"/>(x, y, current), by brush weight ×
    /// strength. Only touched blocks are re-encoded, and the matching part of the minimap is redrawn.
    /// </summary>
    string EditTextureCore(Brush brush, double strength, Func<int, int, Rgba, Rgba?> target, string what)
    {
        var update = EditTextureRegion(TextureRegion(brush), brush.Weight, strength, target);
        return $"{what} at ({brush.X:0}, {brush.Z:0}) radius {brush.Radius}: {update.TilesRewritten} tiles re-encoded, minimap redrawn there";
    }

    /// <summary>
    /// Blends each texture pixel in <paramref name="region"/> towards <paramref name="target"/>(x, y, current), by
    /// <paramref name="weight"/> at the pixel's centre (game units) × strength; then re-encodes the 4x4 blocks where a
    /// pixel changed (no others: re-encoding isn't lossless), redraws the minimap there and refreshes the views.
    /// </summary>
    TextureUpdate EditTextureRegion(PixelRect region, Func<double, double, double> weight, double strength, Func<int, int, Rgba, Rgba?> target)
    {
        var image = TextureImage;
        double perUnit = image.Width / MapWidth;
        strength = Math.Clamp(strength, 0, 1);
        var touched = new HashSet<(int X, int Y)>();
        for (int y = region.Y; y < region.Bottom; y++)
            for (int x = region.X; x < region.Right; x++)
            {
                double w = weight((x + 0.5) / perUnit, (y + 0.5) / perUnit) * strength;
                if (w <= 0)
                    continue;
                var p = image.GetPixel(x, y);
                if (target(x, y, p) is not Rgba t)
                    continue;
                var updated = new Rgba(Blend(p.R, t.R, w), Blend(p.G, t.G, w), Blend(p.B, t.B, w), 255);
                if (updated == p)
                    continue;
                image.SetPixel(x, y, updated);
                touched.Add((x / 4, y / 4));
            }
        var update = Texture.UpdateBlocks(image, touched.Select(b => new PixelRect(b.X * 4, b.Y * 4, 4, 4)));
        RebuildMinimap(region);
        RefreshTexture(region);
        textureChanged = true;
        return update;
    }

    string PaintTextureCore(Brush brush, Rgba colour, double strength) =>
        EditTextureCore(brush, strength, (_, _, _) => colour, "texture painted");

    MinimapStyle? minimapStyle;

    /// <summary>
    /// How this map's minimap relates to its texture, fitted from the minimap as it was before this session's first
    /// redraw of it (land and ground under water apart; see <see cref="MinimapStyle"/>).
    /// </summary>
    MinimapStyle Style => minimapStyle ??= MinimapStyle.Fit(Minimap.Decode(Smf), TextureImage.Resample(Minimap.Size, Minimap.Size),
        (x, y) => Heights.HeightAt((x + 0.5) * MapWidth / Minimap.Size, (y + 0.5) * MapHeight / Minimap.Size) < 0);

    /// <summary>Redraws the minimap over a texture region, in the style of the map's own minimap.</summary>
    void RebuildMinimap(PixelRect textureRegion)
    {
        var style = Style;
        double ux = MapWidth / Minimap.Size, uz = MapHeight / Minimap.Size;
        Minimap.Rebuild(Smf, TextureImage, textureRegion, (x, y, p) => style.Apply(p, Heights.HeightAt((x + 0.5) * ux, (y + 0.5) * uz) < 0));
    }

    /// <summary>Recolours towards <paramref name="tint"/> but keeps each pixel's brightness, so the texture's detail stays.</summary>
    string TintTextureCore(Brush brush, Rgba tint, double strength)
    {
        double tintLuma = Math.Max(1, Luma(tint));
        return EditTextureCore(brush, strength, (_, _, p) =>
        {
            double scale = Luma(p) / tintLuma;
            return new Rgba(Clamp(tint.R * scale), Clamp(tint.G * scale), Clamp(tint.B * scale), 255);
        }, "texture tinted");
    }

    /// <summary>
    /// Scales brightness, scales saturation (0 grey, 1 unchanged, 2 twice as vivid) and turns the hue by
    /// <paramref name="hueDegrees"/>.
    /// </summary>
    string AdjustTextureCore(Brush brush, double brightness, double saturation, double hueDegrees, double strength)
    {
        if (brightness < 0 || saturation < 0)
            throw new InvalidOperationException("Brightness and saturation factors cannot be negative.");
        // Rotation about the grey axis (the usual hue-rotate matrix), then saturation about the luma, then brightness.
        double a = hueDegrees * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        double[] m =
        [
            0.213 + cos * 0.787 - sin * 0.213, 0.715 - cos * 0.715 - sin * 0.715, 0.072 - cos * 0.072 + sin * 0.928,
            0.213 - cos * 0.213 + sin * 0.143, 0.715 + cos * 0.285 + sin * 0.140, 0.072 - cos * 0.072 - sin * 0.283,
            0.213 - cos * 0.213 - sin * 0.787, 0.715 - cos * 0.715 + sin * 0.715, 0.072 + cos * 0.928 + sin * 0.072,
        ];
        return EditTextureCore(brush, strength, (_, _, p) =>
        {
            double r = m[0] * p.R + m[1] * p.G + m[2] * p.B;
            double g = m[3] * p.R + m[4] * p.G + m[5] * p.B;
            double b = m[6] * p.R + m[7] * p.G + m[8] * p.B;
            double luma = 0.299 * r + 0.587 * g + 0.114 * b;
            return new Rgba(
                Clamp((luma + (r - luma) * saturation) * brightness),
                Clamp((luma + (g - luma) * saturation) * brightness),
                Clamp((luma + (b - luma) * saturation) * brightness), 255);
        }, "texture adjusted");
    }

    /// <summary>Copies the texture from (dx, dz) game units away into the brush.</summary>
    string CloneTextureCore(Brush brush, double dx, double dz, double strength)
    {
        var image = TextureImage;
        double perUnit = image.Width / MapWidth;
        int ox = (int)Math.Round(dx * perUnit), oy = (int)Math.Round(dz * perUnit);
        if (ox == 0 && oy == 0)
            throw new InvalidOperationException("The clone source is the brush itself; pick a source elsewhere.");
        // Read the whole source area first, so overlapping source and target don't feed into each other.
        var target = TextureRegion(brush);
        var source = new PixelRect(target.X + ox, target.Y + oy, target.Width, target.Height).Intersect(image.Bounds);
        if (source.IsEmpty)
            throw new InvalidOperationException("The clone source is off the map.");
        var copy = image.Crop(source);
        return EditTextureCore(brush, strength, (x, y, _) =>
        {
            int sx = x + ox - source.X, sy = y + oy - source.Y;
            return sx < 0 || sy < 0 || sx >= copy.Width || sy >= copy.Height ? null : copy.GetPixel(sx, sy);
        }, "texture cloned");
    }

    static double Luma(Rgba c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

    static byte Clamp(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    static byte Blend(byte from, byte to, double w) => (byte)Math.Round(from + (to - from) * w);

    // ---------------------------------------------------------------- splat distribution

    /// <summary>The mapinfo resource that says which detail texture shows where.</summary>
    public const string SplatKey = "splatDistrTex";

    /// <summary>
    /// A resource being painted: its .dds and every level decoded (file orientation, as stored), kept in step with
    /// each edit so a dab re-encodes only its blocks without decoding anything.
    /// </summary>
    sealed class ResourceEdit(DdsFile dds)
    {
        public DdsFile Dds { get; } = dds;
        public List<RgbaImage> Levels { get; } = Enumerable.Range(0, dds.MipCount).Select(m => dds.Decode(m)).ToList();
    }

    readonly Dictionary<string, ResourceEdit> resourceEdits = new(StringComparer.OrdinalIgnoreCase);

    ResourceEdit SplatEdit(out string entry)
    {
        var dds = Resource(SplatKey, out entry);
        if (!resourceEdits.TryGetValue(entry, out var edit))
            resourceEdits[entry] = edit = new ResourceEdit(dds);
        return edit;
    }

    /// <summary>A resource's stored bytes changed some other way (import, undo of an import, normal-map update).</summary>
    void ForgetResourceEdit(string entry) => resourceEdits.Remove(entry);

    /// <summary>The detail texture each splat channel (R, G, B, A) selects, from mapinfo; null where none is set.</summary>
    public IReadOnlyList<string?> SplatChannelNames =>
        Enumerable.Range(1, 4).Select(i => Info.Resource($"splatDetailNormalTex{i}")).ToList();

    /// <summary>Splat pixels (map orientation) a brush can touch.</summary>
    PixelRect SplatRegion(Brush brush, int width, int height) => PixelRect.FromEdges(
        (int)Math.Floor((brush.X - brush.Radius) * width / MapWidth), (int)Math.Floor((brush.Z - brush.Radius) * height / MapHeight),
        (int)Math.Ceiling((brush.X + brush.Radius) * width / MapWidth) + 1, (int)Math.Ceiling((brush.Z + brush.Radius) * height / MapHeight) + 1)
        .Intersect(new PixelRect(0, 0, width, height));

    /// <summary>
    /// Blends the splat weights under the brush towards <paramref name="weights"/> (R, G, B, A = detail textures 1-4),
    /// by brush weight × strength. Only touched blocks are re-encoded, with the mips over them.
    /// </summary>
    string PaintSplatCore(Brush brush, Rgba weights, double strength)
    {
        var edit = SplatEdit(out var entry);
        var dds = edit.Dds;
        var level0 = edit.Levels[0];
        int w = dds.Width, h = dds.Height;
        var region = SplatRegion(brush, w, h);
        strength = Math.Clamp(strength, 0, 1);
        for (int my = region.Y; my < region.Bottom; my++)
            for (int mx = region.X; mx < region.Right; mx++)
            {
                double wt = brush.Weight((mx + 0.5) * MapWidth / w, (my + 0.5) * MapHeight / h) * strength;
                if (wt <= 0)
                    continue;
                int fy = h - 1 - my; // the file is stored upside down (rule 7)
                var p = level0.GetPixel(mx, fy);
                level0.SetPixel(mx, fy, new Rgba(Blend(p.R, weights.R, wt), Blend(p.G, weights.G, wt), Blend(p.B, weights.B, wt), Blend(p.A, weights.A, wt)));
            }
        var file = region.FlipVertical(h);
        dds.Encode(level0, 0, 0, file);
        MipChain.Rebuild(level0, dds.MipCount, file, m => edit.Levels[m], (m, image, dirty) => dds.Encode(image, m, 0, dirty));
        changedResources.Add(entry);
        return $"{entry} painted at ({brush.X:0}, {brush.Z:0}) radius {brush.Radius} towards {weights.R},{weights.G},{weights.B},{weights.A}";
    }

    /// <summary>Copies splat weights for a map-orientation region into <paramref name="target"/> (map orientation, full size).</summary>
    public void ReadSplat(RgbaImage target, PixelRect region)
    {
        var level0 = SplatEdit(out _).Levels[0];
        if (target.Width != level0.Width || target.Height != level0.Height)
            throw new ArgumentException($"Target is {target.Width}x{target.Height}; the splat map is {level0.Width}x{level0.Height}.");
        region = region.Intersect(target.Bounds);
        for (int y = region.Y; y < region.Bottom; y++)
            Array.Copy(level0.Pixels, ((level0.Height - 1 - y) * level0.Width + region.X) * 4, target.Pixels, (y * target.Width + region.X) * 4, region.Width * 4);
    }

    /// <summary>The splat map's size in pixels.</summary>
    public (int Width, int Height) SplatSize
    {
        get
        {
            var dds = Resource(SplatKey, out _);
            return (dds.Width, dds.Height);
        }
    }

    /// <summary>The square a brush can touch, in game units.</summary>
    static WorldRect BrushArea(Brush brush) => new(brush.X - brush.Radius, brush.Z - brush.Radius, 2 * brush.Radius, 2 * brush.Radius);

    /// <summary>
    /// Undo for edits of a .dds resource over some areas (game units): its stored bytes there and over the mips that
    /// follow, plus the decoded levels if the session keeps them (the splat map while painting).
    /// </summary>
    sealed class DdsRegionSnapshot : Snapshot
    {
        readonly MapEditSession session;
        readonly string entry;
        readonly IReadOnlyList<WorldRect> areas;
        readonly MapLayers layers;
        readonly List<DdsFile.RegionState> bytes = [];
        readonly List<(int Mip, PixelRect At, RgbaImage Pixels)> pixels = [];
        readonly WorldRect area;

        public DdsRegionSnapshot(MapEditSession session, string entry, IReadOnlyList<WorldRect> areas, MapLayers layers)
        {
            this.session = session;
            this.entry = entry;
            this.areas = areas;
            this.layers = layers;
            var dds = session.ResourceByEntry(entry);
            session.resourceEdits.TryGetValue(entry, out var edit);
            double ux = session.MapWidth / dds.Width, uz = session.MapHeight / dds.Height;
            WorldRect? union = null;
            foreach (var a in areas)
            {
                // Map-orientation pixels, then file rows (the file is stored upside down, rule 7).
                var map = PixelRect.FromEdges((int)Math.Floor(a.X / ux) - 1, (int)Math.Floor(a.Z / uz) - 1,
                    (int)Math.Ceiling(a.Right / ux) + 1, (int)Math.Ceiling(a.Bottom / uz) + 1).Intersect(new PixelRect(0, 0, dds.Width, dds.Height));
                if (map.IsEmpty)
                    continue;
                var file = map.FlipVertical(dds.Height);
                bytes.Add(dds.CaptureRegion(file));
                if (edit != null)
                    pixels.AddRange(dds.RegionChain(file).Where(c => !c.Area.IsEmpty).Select(c => (c.Mip, c.Area, edit.Levels[c.Mip].Crop(c.Area))));
                var blocks = file.ToBlockBounds(dds.Width, dds.Height).FlipVertical(dds.Height);
                var rect = new WorldRect(blocks.X * ux, blocks.Y * uz, blocks.Width * ux, blocks.Height * uz);
                union = union?.Union(rect) ?? rect;
            }
            area = union ?? new WorldRect(0, 0, 0, 0);
        }

        public override MapLayers Layers => layers;
        public override WorldRect? Area => area;
        public override long Bytes => bytes.Sum(b => b.Bytes) + pixels.Sum(p => p.Pixels.Pixels.LongLength);
        public override Snapshot CaptureAgain() => new DdsRegionSnapshot(session, entry, areas, layers);

        public override void Restore()
        {
            var dds = session.ResourceByEntry(entry);
            for (int i = bytes.Count - 1; i >= 0; i--)
                dds.RestoreRegion(bytes[i]);
            if (session.resourceEdits.TryGetValue(entry, out var edit) && pixels.Count > 0)
                for (int i = pixels.Count - 1; i >= 0; i--)
                    edit.Levels[pixels[i].Mip].Paste(pixels[i].Pixels, pixels[i].At.X, pixels[i].At.Y);
            else if (edit != null)
                session.ForgetResourceEdit(entry); // decoded levels from before this snapshot existed: decode again when needed
            session.changedResources.Add(entry);
        }
    }

    /// <summary>A resource's file bytes as they would be saved now (for comparisons).</summary>
    public byte[] ResourceBytes(string key) => Resource(key, out _).ToBytes();
}
