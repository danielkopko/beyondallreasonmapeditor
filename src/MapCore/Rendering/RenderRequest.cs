using System.Globalization;

namespace MapCore.Rendering;

/// <summary>
/// A view described on the command line (MapTool render, MapEditor --render):
/// <c>--layer &lt;name&gt; --overlays &lt;list|all|none&gt; --size &lt;W&gt;x&lt;H&gt; --center &lt;x&gt;,&lt;z&gt; --scale &lt;units per pixel&gt;</c>.
/// Without --center and --scale the whole map is shown.
/// </summary>
public sealed record RenderRequest(MapViewLayer Layer, MapOverlays Overlays, int Width, int Height, (double X, double Z)? Centre, double? UnitsPerPixel)
{
    public const string Help = "[--layer texture|heights|metal|grass|splat|normals|minimap] " +
                               "[--overlays all|none|water,metal,items,start,grid] [--size WxH] [--center x,z] [--scale units-per-pixel]";

    public static readonly IReadOnlyDictionary<string, MapOverlays> OverlayNames = new Dictionary<string, MapOverlays>(StringComparer.OrdinalIgnoreCase)
    {
        ["water"] = MapOverlays.WaterLine,
        ["metal"] = MapOverlays.MetalSpots,
        ["items"] = MapOverlays.Items,
        ["start"] = MapOverlays.StartPositions,
        ["grid"] = MapOverlays.Grid,
    };

    public const MapOverlays AllOverlays = MapOverlays.WaterLine | MapOverlays.MetalSpots | MapOverlays.Items | MapOverlays.StartPositions | MapOverlays.Grid;

    /// <exception cref="FormatException">An option is unknown or its value is malformed.</exception>
    public static RenderRequest Parse(IReadOnlyList<string> options)
    {
        var request = new RenderRequest(MapViewLayer.Texture, AllOverlays, 1024, 1024, null, null);
        for (int i = 0; i < options.Count; i++)
        {
            string Value() => i + 1 < options.Count ? options[++i] : throw new FormatException($"{options[i]} needs a value.");
            switch (options[i])
            {
                case "--layer":
                {
                    var name = Value();
                    request = request with
                    {
                        Layer = Enum.TryParse<MapViewLayer>(name, ignoreCase: true, out var layer) && Enum.IsDefined(layer)
                            ? layer
                            : throw new FormatException($"Unknown layer '{name}'. Layers: {string.Join(", ", Enum.GetNames<MapViewLayer>()).ToLowerInvariant()}."),
                    };
                    break;
                }
                case "--overlays":
                    request = request with { Overlays = ParseOverlays(Value()) };
                    break;
                case "--size":
                {
                    var parts = Value().Split('x', 'X');
                    if (parts.Length != 2 || !int.TryParse(parts[0], out int w) || !int.TryParse(parts[1], out int h) || w is < 16 or > 8192 || h is < 16 or > 8192)
                        throw new FormatException("--size needs WxH, each 16 to 8192 (e.g. 1200x800).");
                    request = request with { Width = w, Height = h };
                    break;
                }
                case "--center" or "--centre":
                {
                    var parts = Value().Split(',');
                    if (parts.Length != 2 || !TryNumber(parts[0], out double x) || !TryNumber(parts[1], out double z))
                        throw new FormatException("--center needs x,z in game units (e.g. 3072,3072).");
                    request = request with { Centre = (x, z) };
                    break;
                }
                case "--scale":
                    request = request with
                    {
                        UnitsPerPixel = TryNumber(Value(), out double u) && u > 0 ? u : throw new FormatException("--scale needs a positive number of game units per pixel."),
                    };
                    break;
                default:
                    throw new FormatException($"Unknown option '{options[i]}'. Options: {Help}");
            }
        }
        return request;
    }

    public static MapOverlays ParseOverlays(string text)
    {
        if (text.Equals("all", StringComparison.OrdinalIgnoreCase))
            return AllOverlays;
        if (text.Equals("none", StringComparison.OrdinalIgnoreCase))
            return MapOverlays.None;
        var overlays = MapOverlays.None;
        foreach (var name in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            overlays |= OverlayNames.TryGetValue(name, out var o) ? o : throw new FormatException(
                $"Unknown overlay '{name}'. Overlays: {string.Join(", ", OverlayNames.Keys)}, or all / none.");
        return overlays;
    }

    /// <summary>The requested view of a map of the given size (game units).</summary>
    public ViewTransform View(double mapWidth, double mapHeight)
    {
        var fit = ViewTransform.Fit(mapWidth, mapHeight, Width, Height);
        double units = UnitsPerPixel ?? fit.UnitsPerPixel;
        var (x, z) = Centre ?? (mapWidth / 2, mapHeight / 2);
        return new ViewTransform(x - Width * units / 2, z - Height * units / 2, units);
    }

    static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}
