using System.Globalization;
using MapCore.Heights;
using MapCore.Imaging;
using MapCore.Smf;
using MapCore.Textures;

namespace MapCore.Editing;

/// <summary>Layer images (PNG export/import), texture painting, and the texture and resource stores.</summary>
public sealed partial class MapEditSession
{
    /// <summary>tEXt keyword carrying "min max" in exported height images, so a re-import uses the same scale.</summary>
    public const string HeightRangeKey = "BarMapEditor.HeightRange";

    /// <summary>Layer names for <see cref="ExportLayer"/> and <see cref="ImportLayer"/>.</summary>
    public static readonly IReadOnlyList<string> LayerNames = ["heights", "texture", "metal", "grass", "types", "resource:<mapinfo resources key>"];

    MapTexture? texture;
    RgbaImage? textureImage;
    bool textureChanged;
    readonly Dictionary<string, DdsFile> resources = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> changedResources = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The map texture (tiles), loaded on first use from the session's SMF.</summary>
    public MapTexture Texture => texture ??= Package.LoadTexture(Smf);

    /// <summary>The decoded map texture, kept in step with edits.</summary>
    public RgbaImage TextureImage => textureImage ??= Texture.Decode();

    /// <summary>
    /// Writes a layer as PNG: heights as 16-bit grey (with the height range in a text chunk), the texture as RGBA,
    /// metal, grass and the terrain types as 8-bit grey (raw values: the type map's are mapinfo's terrain type numbers), and a mapinfo resource (.dds, e.g. "resource:splatDistrTex")
    /// as RGBA in map orientation.
    /// </summary>
    public string ExportLayer(string layer, string pngPath)
    {
        switch (layer)
        {
            case "heights":
            {
                var h = Heights;
                var (values, min, max) = h.Quantize();
                var range = $"{min.ToString("R", CultureInfo.InvariantCulture)} {max.ToString("R", CultureInfo.InvariantCulture)}";
                PngWriter.WriteGray16(pngPath, h.Width, h.Height, values, new Dictionary<string, string> { [HeightRangeKey] = range });
                return $"heights: {h.Width}x{h.Height} 16-bit grey, black = {min}, white = {max} (one step below)" +
                       (min != h.MinHeight || max != h.MaxHeight ? "; range widened to fit unsaved edits" : "");
            }
            case "texture":
                PngWriter.WriteRgba(pngPath, TextureImage);
                return $"texture: {TextureImage.Width}x{TextureImage.Height} RGBA";
            case "metal":
                PngWriter.WriteGray8(pngPath, Smf.MetalMapWidth, Smf.MetalMapHeight, Smf.MetalMap);
                return $"metal: {Smf.MetalMapWidth}x{Smf.MetalMapHeight} grey, raw values (x maxMetal {Metal.MaxMetal} = metal per cell)";
            case "grass":
                PngWriter.WriteGray8(pngPath, Smf.GrassMapWidth, Smf.GrassMapHeight, Smf.GrassMap ?? new byte[Smf.GrassMapWidth * Smf.GrassMapHeight]);
                return $"grass: {Smf.GrassMapWidth}x{Smf.GrassMapHeight} grey (0 none, 1-254 blade size in BAR)";
            case "types":
                PngWriter.WriteGray8(pngPath, Smf.MetalMapWidth, Smf.MetalMapHeight, Smf.TypeMap);
                return $"terrain types: {Smf.MetalMapWidth}x{Smf.MetalMapHeight} grey, each value a terrain type number (mapinfo's terrainTypes)";
            default:
            {
                var dds = Resource(ResourceKey(layer), out var entry);
                var image = dds.Decode();
                image.FlipVertical();
                PngWriter.WriteRgba(pngPath, image);
                return $"{entry}: {image.Width}x{image.Height} RGBA in map orientation (north at the top)";
            }
        }
    }

    /// <summary>
    /// Reads a layer from PNG. Only what differs is changed, so importing an unedited export is a no-op.
    /// The image must be the layer's exact size.
    /// </summary>
    string ImportLayerCore(string layer, string pngPath)
    {
        var png = PngReader.Read(pngPath);
        switch (layer)
        {
            case "heights":
            {
                var h = Heights;
                CheckSize(png, h.Width, h.Height, layer);
                float min = h.MinHeight, max = h.MaxHeight;
                if (png.Text.TryGetValue(HeightRangeKey, out var range))
                {
                    var parts = range.Split(' ');
                    min = float.Parse(parts[0], CultureInfo.InvariantCulture);
                    max = float.Parse(parts[1], CultureInfo.InvariantCulture);
                }
                float mod = (max - min) / 65536.0f;
                // A value that rounds to the same 16-bit step as the current height is that height: re-importing an
                // export, even one taken with unsaved edits, changes nothing.
                // (Compared in double: float rounding near 400 is larger than the margin at exactly half a step.)
                var current = h.Heights;
                double step = (max - (double)min) / 65536.0;
                var values = png.ToGray16().Select((v, i) =>
                    Math.Abs(min + v * step - current[i]) <= step * 0.5 * (1 + 1e-9) ? current[i] : min + v * mod).ToArray();
                int count = h.SetAll(values);
                return $"heights: {count:N0} vertices changed (scale {min} .. {max}{(png.Text.ContainsKey(HeightRangeKey) ? ", from the image" : ", the map's current range")})" +
                       (png.BitDepth == 8 ? "; note: an 8-bit image gives only 256 height levels" : "");
            }
            case "texture":
            {
                var image = png.ToRgba();
                var (update, changed) = Texture.UpdateFrom(image);
                if (!changed.IsEmpty)
                {
                    RebuildMinimap(changed);
                    RefreshTexture(changed);
                    textureChanged = true;
                }
                return $"texture: {update.TilesRewritten:N0} tiles re-encoded ({update.TilesAdded} new copies of shared tiles)" +
                       (changed.IsEmpty ? "; nothing differed" : $"; changed area {changed.Width}x{changed.Height} at ({changed.X}, {changed.Y})");
            }
            case "metal":
            {
                CheckSize(png, Smf.MetalMapWidth, Smf.MetalMapHeight, layer);
                if (Metal.ReadOnlyReason != null)
                    throw new InvalidOperationException(Metal.ReadOnlyReason);
                int count = CopyInto(png.ToGray8(), Smf.MetalMap);
                int edge = Metal.EdgeCellsWithMetal();
                return $"metal: {count:N0} cells changed; {Metal.FindSpots().Count} spots" +
                       (edge > 0 ? $"; {edge} cells are in the outer ring, which BAR's spot finder ignores" : "");
            }
            case "grass":
            {
                CheckSize(png, Smf.GrassMapWidth, Smf.GrassMapHeight, layer);
                var values = png.ToGray8().Select(v => Math.Min(v, GrassLayer.MaxValue)).ToArray();
                if (Smf.GrassMap == null && values.All(v => v == 0))
                    return "grass: no grass in the image and none on the map; nothing to do";
                Smf.GrassMap ??= new byte[values.Length];
                int count = CopyInto(values, Smf.GrassMap);
                return $"grass: {count:N0} cells changed; {GrassLayer.CellsWithGrass(Smf):N0} cells have grass";
            }
            case "types":
            {
                CheckSize(png, Smf.MetalMapWidth, Smf.MetalMapHeight, layer);
                int count = CopyInto(png.ToGray8(), Smf.TypeMap);
                return $"terrain types: {count:N0} squares changed";
            }
            default:
            {
                var dds = Resource(ResourceKey(layer), out var entry);
                var image = png.ToRgba();
                CheckSize(png, dds.Width, dds.Height, layer);
                image.FlipVertical();
                var changed = dds.EncodeChanged(image);
                if (!changed.IsEmpty)
                {
                    changedResources.Add(entry);
                    ForgetResourceEdit(entry);
                }
                return $"{entry}: " + (changed.IsEmpty ? "nothing differed" : $"blocks re-encoded over {changed.Width}x{changed.Height} pixels, mips rebuilt");
            }
        }
    }

    /// <summary>Re-decodes the stored tiles under <paramref name="region"/> so the cached image matches what is saved.</summary>
    void RefreshTexture(PixelRect region)
    {
        if (textureImage == null)
            return;
        const int size = SmtFile.TileSize;
        for (int ty = region.Y / size; ty < (region.Bottom + size - 1) / size; ty++)
            for (int tx = region.X / size; tx < (region.Right + size - 1) / size; tx++)
                textureImage.Paste(Texture.DecodeTile(tx, ty), tx * size, ty * size);
    }

    /// <summary>A mapinfo resource texture (.dds in the package), loaded once per session.</summary>
    DdsFile Resource(string key, out string entry)
    {
        var name = Info.Resource(key) ?? throw new InvalidOperationException($"mapinfo.lua sets no resources.{key}.");
        entry = Package.ResolveResource(name) ?? throw new InvalidOperationException($"resources.{key} = \"{name}\" is not in the package.");
        return ResourceByEntry(entry);
    }

    DdsFile ResourceByEntry(string entry)
    {
        if (!entry.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{entry} is not a .dds texture; only .dds resources can be edited.");
        if (!resources.TryGetValue(entry, out var dds))
        {
            dds = DdsFile.Load(Path.Combine(Package.Root, entry));
            if (!dds.IsEditable || dds.FaceCount != 1)
                throw new InvalidOperationException($"{entry} ({dds.FormatName}{(dds.IsCubemap ? ", cube map" : "")}) cannot be edited.");
            resources[entry] = dds;
        }
        return dds;
    }

    void SaveTextureAndResources()
    {
        if (textureChanged)
        {
            Texture.SaveTileFiles(Package.Root);
            textureChanged = false;
        }
        foreach (var entry in changedResources)
            resources[entry].Save(Path.Combine(Package.Root, entry));
        changedResources.Clear();
    }

    static string ResourceKey(string layer) =>
        layer.StartsWith("resource:", StringComparison.Ordinal) && layer.Length > "resource:".Length
            ? layer["resource:".Length..]
            : throw new InvalidOperationException($"Unknown layer '{layer}'. Layers: {string.Join(", ", LayerNames)}.");

    static void CheckSize(PngImage png, int width, int height, string layer)
    {
        if (png.Width != width || png.Height != height)
            throw new InvalidOperationException($"The {layer} image must be {width}x{height}; this one is {png.Width}x{png.Height}.");
    }

    static int CopyInto(byte[] source, byte[] target)
    {
        int count = 0;
        for (int i = 0; i < source.Length; i++)
            if (target[i] != source[i])
            {
                target[i] = source[i];
                count++;
            }
        return count;
    }
}
