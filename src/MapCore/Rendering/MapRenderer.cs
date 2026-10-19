using System.Globalization;
using System.Runtime.InteropServices;
using MapCore.Editing;
using MapCore.Imaging;
using MapCore.Items;
using MapCore.Metal;
using MapCore.Smf;

namespace MapCore.Rendering;

/// <summary>What the map view shows.</summary>
public enum MapViewLayer
{
    /// <summary>The map texture (the .smt tiles).</summary>
    Texture,
    /// <summary>Hillshaded heights, coloured by height, water in blue.</summary>
    Heights,
    /// <summary>The metal map over dimmed hillshade.</summary>
    Metal,
    /// <summary>The grass map over dimmed hillshade.</summary>
    Grass,
    /// <summary>mapinfo resources.splatDistrTex (which detail texture shows where), RGB.</summary>
    Splat,
    /// <summary>mapinfo resources.detailNormalTex.</summary>
    Normals,
    /// <summary>The SMF's built-in minimap.</summary>
    Minimap,
}

/// <summary>Markers drawn over the layer.</summary>
[Flags]
public enum MapOverlays
{
    None = 0,
    WaterLine = 1,
    MetalSpots = 2,
    Items = 4,
    StartPositions = 8,
    Grid = 16,
}

/// <summary>Text for the UI to draw over a rendered image: centred on <see cref="X"/>, top at <see cref="Y"/> (image pixels).</summary>
public readonly record struct MapLabel(double X, double Y, string Text, Rgba Colour);

/// <summary>What one render produced besides pixels: labels to draw, and a note about the layer (or null).</summary>
public sealed record RenderResult(IReadOnlyList<MapLabel> Labels, string? Note);

/// <summary>
/// Draws the part of a map a view looks at into an <see cref="RgbaImage"/>, in map orientation (north up).
/// Each layer is kept as an <see cref="ImagePyramid"/>, built when first shown and updated over just the changed area
/// when the session reports an edit, so panning and zooming never touch a full-size image.
/// Not thread-safe: render, and apply edits, from one thread at a time.
/// </summary>
public sealed class MapRenderer : IDisposable
{
    public static readonly Rgba Background = new(38, 38, 42, 255);

    /// <summary>Hillshade resolution: game units per pixel (half a heightmap square).</summary>
    const double ShadeUnits = 4;

    /// <summary>Vertical exaggeration of the hillshade, so gentle slopes still read.</summary>
    const double Relief = 2;

    /// <summary>Grid spacing: one map unit (64 squares).</summary>
    public const double GridSpacing = 512;

    static readonly Rgba[] TeamColours =
    [
        new(40, 100, 255, 255), new(255, 50, 40, 255), new(40, 200, 60, 255), new(255, 220, 0, 255),
        new(255, 140, 0, 255), new(180, 70, 255, 255), new(0, 220, 220, 255), new(255, 110, 190, 255),
    ];

    enum Source { Texture, Shade, Metal, Grass, Splat, Normals, Minimap }

    readonly MapEditSession session;
    readonly Dictionary<Source, ImagePyramid> built = [];
    readonly Dictionary<Source, WorldRect?> stale = [];
    readonly Dictionary<Source, string> problems = [];
    readonly float shadeLow, shadeHigh;
    List<(MetalSpot Spot, double Income)>? spots;
    double waterLevel;

    public MapRenderer(MapEditSession session)
    {
        this.session = session;
        // The colour scale is fixed when the map opens, so colours don't shift when a save widens the height range.
        shadeLow = session.Heights.MinHeight;
        shadeHigh = session.Heights.MaxHeight;
        session.Changed += OnChanged;
    }

    public void Dispose() => session.Changed -= OnChanged;

    /// <summary>
    /// The sea level the heights view and the water line show, in game units. The game's is always 0; another value
    /// previews what a whole-map height shift of −level would do. It changes nothing in the map.
    /// </summary>
    public double WaterLevel
    {
        get => waterLevel;
        set
        {
            if (value == waterLevel)
                return;
            waterLevel = value;
            MarkStale(Source.Shade, null);
        }
    }

    /// <summary>Why a layer cannot be shown on this map (null if it can), without loading anything.</summary>
    public string? Unavailable(MapViewLayer layer) => layer switch
    {
        MapViewLayer.Splat when session.Info.Resource("splatDistrTex") == null => "This map has no splat distribution texture (resources.splatDistrTex).",
        MapViewLayer.Normals when session.Info.Resource("detailNormalTex") == null => "This map has no normal map (resources.detailNormalTex).",
        _ => null,
    };

    /// <summary>Builds what a layer needs now (e.g. while the map is still opening in the background).</summary>
    public void Prepare(MapViewLayer layer)
    {
        foreach (var source in SourcesOf(layer))
            Get(source);
    }

    public RenderResult Render(RgbaImage target, ViewTransform view, MapViewLayer layer, MapOverlays overlays)
    {
        Clear(target, Background);
        ReleaseUnused(layer);
        string? note = null;
        switch (layer)
        {
            case MapViewLayer.Texture:
                Get(Source.Texture)?.Draw(target, view);
                break;
            case MapViewLayer.Heights:
                Get(Source.Shade)?.Draw(target, view);
                if (waterLevel != 0)
                    note = string.Create(CultureInfo.InvariantCulture,
                        $"Previewing sea level at {waterLevel:0.#} (the game's is 0). Applying it shifts every height by {-waterLevel:+0.#;-0.#}.");
                break;
            case MapViewLayer.Metal:
                Get(Source.Shade)?.Draw(target, view, brightness: 0.5);
                Get(Source.Metal)?.Draw(target, view, blend: true);
                note = session.Metal.ReadOnlyReason ?? session.Metal.Warnings.FirstOrDefault();
                break;
            case MapViewLayer.Grass:
                Get(Source.Shade)?.Draw(target, view, brightness: 0.5);
                Get(Source.Grass)?.Draw(target, view, blend: true);
                if (session.Info.TextureOverrides.TryGetValue("grassmapTex", out var grassImage))
                    note = $"mapinfo.lua sets smf.grassmapTex = \"{grassImage}\"; the game takes grass from that image instead.";
                break;
            case MapViewLayer.Splat:
                Get(Source.Splat)?.Draw(target, view);
                break;
            case MapViewLayer.Normals:
                Get(Source.Normals)?.Draw(target, view);
                if (session.Heights.HasChanges)
                    note = "The normal map follows height edits when the map is saved.";
                break;
            case MapViewLayer.Minimap:
                Get(Source.Minimap)?.Draw(target, view);
                if (session.Info.TextureOverrides.TryGetValue("minimapTex", out var minimapImage))
                    note = $"mapinfo.lua sets smf.minimapTex = \"{minimapImage}\"; the game shows that image instead.";
                break;
        }
        foreach (var source in SourcesOf(layer))
            if (problems.TryGetValue(source, out var problem))
                note = problem;

        var labels = new List<MapLabel>();
        if (overlays.HasFlag(MapOverlays.Grid))
            DrawGrid(target, view);
        if (overlays.HasFlag(MapOverlays.WaterLine))
            DrawWaterLine(target, view);
        if (overlays.HasFlag(MapOverlays.Items))
            DrawItems(target, view, labels);
        if (overlays.HasFlag(MapOverlays.MetalSpots))
            DrawSpots(target, view, labels);
        if (overlays.HasFlag(MapOverlays.StartPositions))
            DrawStartPositions(target, view, labels);
        return new RenderResult(labels, note);
    }

    // ---------------------------------------------------------------- layer images

    static Source[] SourcesOf(MapViewLayer layer) => layer switch
    {
        MapViewLayer.Texture => [Source.Texture],
        MapViewLayer.Heights => [Source.Shade],
        MapViewLayer.Metal => [Source.Shade, Source.Metal],
        MapViewLayer.Grass => [Source.Shade, Source.Grass],
        MapViewLayer.Splat => [Source.Splat],
        MapViewLayer.Normals => [Source.Normals],
        _ => [Source.Minimap],
    };

    /// <summary>The large .dds layers are dropped when another layer is shown; they decode again when needed.</summary>
    void ReleaseUnused(MapViewLayer layer)
    {
        foreach (var source in new[] { Source.Splat, Source.Normals })
            if (!SourcesOf(layer).Contains(source))
            {
                built.Remove(source);
                stale.Remove(source);
            }
    }

    void OnChanged(object? sender, MapChange change)
    {
        if (change.Layers.HasFlag(MapLayers.Texture))
            MarkStale(Source.Texture, change.Area);
        if (change.Layers.HasFlag(MapLayers.Heights))
            // A hillshade pixel reads heights up to one vertex plus one pixel away.
            MarkStale(Source.Shade, change.Area is WorldRect a ? Grow(a, HeightMapMargin) : null);
        if (change.Layers.HasFlag(MapLayers.Metal))
        {
            MarkStale(Source.Metal, null);
            spots = null;
        }
        if (change.Layers.HasFlag(MapLayers.Grass))
            MarkStale(Source.Grass, null);
        if (change.Layers.HasFlag(MapLayers.Splat))
            MarkStale(Source.Splat, change.Layers.HasFlag(MapLayers.Resources) ? null : change.Area);
        if (change.Layers.HasFlag(MapLayers.Resources))
        {
            MarkStale(Source.Splat, null);
            MarkStale(Source.Normals, null);
        }
        if (change.Layers.HasFlag(MapLayers.Minimap))
            MarkStale(Source.Minimap, null);
        if (change.Layers.HasFlag(MapLayers.MapInfo))
            spots = null; // maxMetal and extractorRadius live in mapinfo
    }

    const double HeightMapMargin = 2 * 8 + ShadeUnits;

    static WorldRect Grow(WorldRect a, double by) => new(a.X - by, a.Z - by, a.Width + 2 * by, a.Height + 2 * by);

    void MarkStale(Source source, WorldRect? area)
    {
        if (!built.ContainsKey(source) && !problems.ContainsKey(source))
            return; // built from scratch when first needed
        stale[source] = stale.TryGetValue(source, out var pending)
            ? pending is WorldRect p && area is WorldRect a ? p.Union(a) : null
            : area;
    }

    /// <summary>The layer's pyramid, brought up to date; null if the layer cannot be loaded (see <see cref="problems"/>).</summary>
    ImagePyramid? Get(Source source)
    {
        if (stale.Remove(source, out var area))
        {
            problems.Remove(source);
            if (built.TryGetValue(source, out var existing) && area is WorldRect a && Refresh(source, existing, a))
                return existing;
            built.Remove(source);
        }
        if (built.TryGetValue(source, out var pyramid))
            return pyramid;
        if (problems.ContainsKey(source))
            return null;
        try
        {
            return built[source] = Build(source);
        }
        catch (InvalidOperationException ex)
        {
            problems[source] = ex.Message;
            return null;
        }
    }

    /// <summary>Updates part of a pyramid in place; false if it must be rebuilt instead.</summary>
    bool Refresh(Source source, ImagePyramid pyramid, WorldRect area)
    {
        var region = pyramid.ToPixels(area);
        switch (source)
        {
            case Source.Texture when ReferenceEquals(pyramid.Level0, session.TextureImage):
                break; // the session keeps the decoded texture up to date; only the smaller levels follow
            case Source.Shade:
                Shade(pyramid.Level0, region);
                break;
            case Source.Splat:
                session.ReadSplat(pyramid.Level0, region); // the session keeps the painted splat map decoded
                pyramid.Level0.MakeOpaque(region);
                break;
            default:
                return false;
        }
        pyramid.Update(region);
        return true;
    }

    ImagePyramid Build(Source source)
    {
        double w = session.MapWidth, h = session.MapHeight;
        switch (source)
        {
            case Source.Texture:
                return new ImagePyramid(session.TextureImage, w, h, smooth: true);
            case Source.Shade:
            {
                var image = new RgbaImage((int)Math.Round(w / ShadeUnits), (int)Math.Round(h / ShadeUnits));
                Shade(image, image.Bounds);
                return new ImagePyramid(image, w, h, smooth: true);
            }
            case Source.Metal:
            {
                var smf = session.Smf;
                var image = new RgbaImage(smf.MetalMapWidth, smf.MetalMapHeight);
                for (int i = 0; i < smf.MetalMap.Length; i++)
                    SetPixel(image, i, MetalColour(smf.MetalMap[i]));
                return new ImagePyramid(image, w, h, smooth: false);
            }
            case Source.Grass:
            {
                var smf = session.Smf;
                var image = new RgbaImage(smf.GrassMapWidth, smf.GrassMapHeight);
                if (smf.GrassMap != null)
                    for (int i = 0; i < smf.GrassMap.Length; i++)
                        SetPixel(image, i, GrassColour(smf.GrassMap[i]));
                return new ImagePyramid(image, w, h, smooth: false);
            }
            case Source.Splat:
            {
                var (sw, sh) = session.SplatSize;
                var image = new RgbaImage(sw, sh);
                session.ReadSplat(image, image.Bounds);
                image.MakeOpaque(image.Bounds); // show the colour channels; alpha is data here, not transparency
                return new ImagePyramid(image, w, h, smooth: true);
            }
            case Source.Normals:
            {
                var image = session.ResourceImage("detailNormalTex");
                image.MakeOpaque(image.Bounds);
                return new ImagePyramid(image, w, h, smooth: true);
            }
            default:
            {
                var image = Minimap.Decode(session.Smf);
                image.MakeOpaque(image.Bounds);
                return new ImagePyramid(image, w, h, smooth: true);
            }
        }
    }

    static void SetPixel(RgbaImage image, int index, Rgba c)
    {
        int o = index * 4;
        (image.Pixels[o], image.Pixels[o + 1], image.Pixels[o + 2], image.Pixels[o + 3]) = (c.R, c.G, c.B, c.A);
    }

    /// <summary>Hillshade, lit from the north-west, coloured by height; under water in blue.</summary>
    void Shade(RgbaImage image, PixelRect region)
    {
        var heights = session.Heights;
        double sx = session.MapWidth / image.Width, sz = session.MapHeight / image.Height;
        const double lx = -0.5, ly = 0.70710678, lz = -0.5; // 45° up, from the north-west
        region = region.Intersect(image.Bounds);
        Parallel.For(region.Y, region.Bottom, j =>
        {
            double z = (j + 0.5) * sz;
            for (int i = region.X; i < region.Right; i++)
            {
                double x = (i + 0.5) * sx;
                float h = heights.HeightAt(x, z);
                double dx = (heights.HeightAt(x + sx, z) - heights.HeightAt(x - sx, z)) / (2 * sx);
                double dz = (heights.HeightAt(x, z + sz) - heights.HeightAt(x, z - sz)) / (2 * sz);
                double nx = -Relief * dx, nz = -Relief * dz;
                double lambert = (nx * lx + ly + nz * lz) / Math.Sqrt(nx * nx + 1 + nz * nz);
                double light = Math.Clamp(0.3 + 0.7 * lambert / ly, 0, 1.3); // flat ground = 1
                var c = HeightColour(h);
                int o = (j * image.Width + i) * 4;
                image.Pixels[o] = (byte)Math.Min(255, c.R * light);
                image.Pixels[o + 1] = (byte)Math.Min(255, c.G * light);
                image.Pixels[o + 2] = (byte)Math.Min(255, c.B * light);
                image.Pixels[o + 3] = 255;
            }
        });
    }

    static readonly Rgba Shallow = new(70, 130, 170, 255), Deep = new(20, 42, 90, 255);
    static readonly Rgba Lowland = new(96, 112, 74, 255), Upland = new(170, 150, 112, 255), Peak = new(238, 234, 226, 255);

    Rgba HeightColour(float h)
    {
        if (h < waterLevel)
            return Lerp(Shallow, Deep, Math.Clamp((waterLevel - h) / Math.Max(1, waterLevel - shadeLow), 0, 1));
        double low = Math.Max(waterLevel, shadeLow);
        double t = Math.Clamp((h - low) / Math.Max(1, shadeHigh - low), 0, 1);
        return t < 0.5 ? Lerp(Lowland, Upland, t * 2) : Lerp(Upland, Peak, (t - 0.5) * 2);
    }

    static Rgba MetalColour(byte v)
    {
        if (v == 0)
            return new Rgba(40, 170, 255, 0);
        double t = v / 255.0;
        var c = t < 0.5 ? Lerp(new(40, 170, 255, 255), new(255, 235, 40, 255), t * 2) : Lerp(new(255, 235, 40, 255), new(255, 90, 40, 255), (t - 0.5) * 2);
        return c with { A = 235 };
    }

    static Rgba GrassColour(byte v) => v switch
    {
        0 => new Rgba(90, 220, 90, 0),
        1 => new Rgba(90, 220, 90, 210),
        _ => Lerp(new(50, 140, 50, 210), new(170, 255, 120, 210), v / 254.0),
    };

    static Rgba Lerp(Rgba a, Rgba b, double t) => new(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t), (byte)Math.Round(a.A + (b.A - a.A) * t));

    static void Clear(RgbaImage image, Rgba c) =>
        MemoryMarshal.Cast<byte, uint>(image.Pixels.AsSpan()).Fill((uint)(c.R | c.G << 8 | c.B << 16 | c.A << 24));

    // ---------------------------------------------------------------- overlays

    void DrawGrid(RgbaImage target, ViewTransform view)
    {
        var colour = new Rgba(255, 255, 255, 70);
        var (left, top) = view.ToImage(0, 0);
        var (right, bottom) = view.ToImage(session.MapWidth, session.MapHeight);
        int y0 = (int)Math.Ceiling(top - 0.5), y1 = (int)Math.Floor(bottom - 0.5), x0 = (int)Math.Ceiling(left - 0.5), x1 = (int)Math.Floor(right - 0.5);
        for (double gx = GridSpacing; gx < session.MapWidth; gx += GridSpacing)
            Canvas.VerticalLine(target, (int)Math.Floor(view.ToImage(gx, 0).X), y0, y1, colour);
        for (double gz = GridSpacing; gz < session.MapHeight; gz += GridSpacing)
            Canvas.HorizontalLine(target, (int)Math.Floor(view.ToImage(0, gz).Y), x0, x1, colour);
    }

    /// <summary>Marks pixels where the ground crosses sea level (<see cref="WaterLevel"/>) between neighbouring pixels.</summary>
    void DrawWaterLine(RgbaImage target, ViewTransform view)
    {
        int w = target.Width, h = target.Height;
        var heights = new float[w * h];
        var map = session.Heights;
        Parallel.For(0, h, y =>
        {
            var (_, z) = view.ToWorld(0, y + 0.5);
            for (int x = 0; x < w; x++)
            {
                var (wx, _) = view.ToWorld(x + 0.5, 0);
                heights[y * w + x] = wx < 0 || z < 0 || wx >= session.MapWidth || z >= session.MapHeight ? float.NaN : (float)(map.HeightAt(wx, z) - waterLevel);
            }
        });
        var colour = new Rgba(90, 235, 255, 235);
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                float here = heights[y * w + x];
                if (float.IsNaN(here))
                    continue;
                bool below = here < 0;
                if ((x + 1 < w && Crosses(heights[y * w + x + 1])) || (y + 1 < h && Crosses(heights[(y + 1) * w + x])))
                    Canvas.Blend(target, x, y, colour, 1);

                bool Crosses(float other) => !float.IsNaN(other) && other < 0 != below;
            }
        });
    }

    /// <summary>
    /// Doors and tunnels: their walls (grey), centre (a door orange, a tunnel top teal, an open tunnel's middle light
    /// blue) and outlined, where a kind made from assets puts its repair markers when broken; placed repair markers
    /// filled green, or red. One that starts destroyed has its centre only outlined (and its markers red or filled).
    /// </summary>
    void DrawStructures(RgbaImage target, ViewTransform view, List<MapLabel> labels, bool names)
    {
        var config = session.Structures;
        foreach (var s in config.Instances)
        {
            if (!config.Kinds.TryGetValue(s.Kind, out var kind))
                continue;
            var centreColour = kind.Type switch
            {
                Structures.StructureType.Door => new Rgba(215, 130, 60, 255),
                Structures.StructureType.Tunnel => new Rgba(60, 185, 175, 255),
                Structures.StructureType.Pump => new Rgba(190, 70, 50, 255),
                Structures.StructureType.Guardian => new Rgba(225, 185, 70, 255),
                Structures.StructureType.ShieldPad => new Rgba(80, 200, 235, 255),
                _ => new Rgba(130, 190, 245, 255),
            };
            if (s.Zone is Structures.GuardianZone zone && kind.Type == Structures.StructureType.Guardian)
                DrawZone(target, view, zone);
            bool broken = Structures.StructureLayout.StartsBroken(kind, s);
            foreach (var part in Structures.StructureLayout.Parts(kind, s).Where(p => p.Role != "debris"))
            {
                var (ax, ay) = view.ToImage(part.X1, part.Z1);
                var (bx, by) = view.ToImage(part.X2, part.Z2);
                if (Math.Max(ax, bx) < -20 || Math.Max(ay, by) < -20 || Math.Min(ax, bx) > target.Width + 20 || Math.Min(ay, by) > target.Height + 20)
                    continue;
                var colour = part.Role switch
                {
                    "wall" => new Rgba(105, 105, 112, 255),
                    "centre" => centreColour,
                    "marker" => broken ? new Rgba(235, 70, 60, 255) : new Rgba(90, 225, 110, 255), // placed: lit green, or red while destroyed
                    _ => new Rgba(255, 220, 90, 255),
                };
                if (part.Role is "wall" or "marker" || part.Role == "centre" && !broken || part.Role == "repair" && broken)
                    Canvas.FillRect(target, ax, ay, bx, by, colour with { A = 150 });
                double width = part.Role == "repair" ? 1 : 1.5;
                Canvas.Line(target, ax, ay, bx, ay, width, colour);
                Canvas.Line(target, bx, ay, bx, by, width, colour);
                Canvas.Line(target, bx, by, ax, by, width, colour);
                Canvas.Line(target, ax, by, ax, ay, width, colour);
            }
            if (names)
            {
                var (sx, sy) = view.ToImage(s.X, s.Z);
                labels.Add(new MapLabel(sx, sy + 4, kind.Name, centreColour));
            }
        }
    }

    readonly Dictionary<Structures.GuardianZone, List<(double X, double Z)[]>> zoneEdges = [];

    /// <summary>A guardian tower's zone: its edge (red-orange) and the path it guards (gold).</summary>
    void DrawZone(RgbaImage target, ViewTransform view, Structures.GuardianZone zone)
    {
        List<(double X, double Z)[]>? edges;
        lock (zoneEdges)
            zoneEdges.TryGetValue(zone, out edges);
        if (edges == null)
        {
            var (minX, minZ, maxX, maxZ) = zone.Bounds;
            edges = zone.Outline(Math.Max(8, Math.Max(maxX - minX, maxZ - minZ) / 200));
            lock (zoneEdges)
            {
                if (zoneEdges.Count > 64)
                    zoneEdges.Clear();
                zoneEdges[zone] = edges;
            }
        }
        var edge = new Rgba(255, 110, 70, 210);
        foreach (var line in edges)
            for (int i = 0; i < line.Length; i++)
            {
                var (ax, ay) = view.ToImage(line[i].X, line[i].Z);
                var (bx, by) = view.ToImage(line[(i + 1) % line.Length].X, line[(i + 1) % line.Length].Z);
                Canvas.Line(target, ax, ay, bx, by, 1.5, edge);
            }
        var path = new Rgba(235, 195, 70, 190);
        foreach (var (a, b) in zone.Segments())
        {
            var (ax, ay) = view.ToImage(a.X, a.Z);
            var (bx, by) = view.ToImage(b.X, b.Z);
            Canvas.Line(target, ax, ay, bx, by, 1, path);
        }
    }

    void DrawItems(RgbaImage target, ViewTransform view, List<MapLabel> labels)
    {
        double r = Math.Clamp(12 / view.UnitsPerPixel, 2.5, 9);
        bool names = view.UnitsPerPixel <= 1.5;
        var shadow = new Rgba(0, 0, 0, 170);
        DrawStructures(target, view, labels, names);
        foreach (var item in session.Items.All)
        {
            var (sx, sy) = view.ToImage(item.X, item.Z);
            if (sx < -50 || sy < -50 || sx > target.Width + 50 || sy > target.Height + 50)
                continue;
            var colour = item.Source == ItemSource.Smf ? new Rgba(255, 150, 30, 255)
                : item.Kind == PlacedItemKind.Feature ? new Rgba(130, 255, 130, 255) : new Rgba(255, 90, 255, 255);
            double heading = item.Kind == PlacedItemKind.Building ? item.Rotation * 16384 : item.Rotation;
            double angle = heading * 2 * Math.PI / 65536; // heading 0 faces south (+z), 16384 east (+x)
            Canvas.FillCircle(target, sx, sy, r + 1.2, shadow);
            Canvas.Line(target, sx, sy, sx + Math.Sin(angle) * r * 2, sy + Math.Cos(angle) * r * 2, 2.5, shadow);
            Canvas.FillCircle(target, sx, sy, r, colour);
            Canvas.Line(target, sx, sy, sx + Math.Sin(angle) * r * 2, sy + Math.Cos(angle) * r * 2, 1.2, colour);
            if (names)
                labels.Add(new MapLabel(sx, sy + r + 3, item.Name, colour));
        }
    }

    IReadOnlyList<(MetalSpot Spot, double Income)> Spots =>
        spots ??= session.Metal.FindSpots().Select(s => (s, session.Metal.Income(s))).ToList();

    void DrawSpots(RgbaImage target, ViewTransform view, List<MapLabel> labels)
    {
        double radius = session.Metal.ExtractorRadius / view.UnitsPerPixel;
        var ring = new Rgba(255, 225, 40, 210);
        foreach (var (spot, income) in Spots)
        {
            var (sx, sy) = view.ToImage(spot.X, spot.Z);
            if (sx < -radius - 50 || sy < -radius - 50 || sx > target.Width + radius + 50 || sy > target.Height + radius + 50)
                continue;
            Canvas.StrokeCircle(target, sx, sy, Math.Max(radius, 5), 1.6, ring);
            Canvas.FillCircle(target, sx, sy, 3.5, new Rgba(0, 0, 0, 180));
            Canvas.FillCircle(target, sx, sy, 2.5, ring with { A = 255 });
            labels.Add(new MapLabel(sx, sy + 6, income.ToString("0.00", CultureInfo.InvariantCulture), ring with { A = 255 }));
        }
    }

    void DrawStartPositions(RgbaImage target, ViewTransform view, List<MapLabel> labels)
    {
        foreach (var start in session.Info.StartPositions)
        {
            var (sx, sy) = view.ToImage(start.X, start.Z);
            var colour = TeamColours[((start.Team % TeamColours.Length) + TeamColours.Length) % TeamColours.Length];
            Canvas.FillCircle(target, sx, sy, 11, new Rgba(255, 255, 255, 255));
            Canvas.FillCircle(target, sx, sy, 9, colour);
            labels.Add(new MapLabel(sx, sy + 13, $"Start {start.Team}", new Rgba(255, 255, 255, 255)));
        }
    }

    /// <summary>For tests: the pyramid currently held for a layer's main image (built if needed).</summary>
    internal IReadOnlyList<RgbaImage>? LevelsOf(MapViewLayer layer) => Pyramid(layer)?.Levels;

    /// <summary>A layer's main image pyramid, brought up to date (the 3D view drapes it over the terrain).</summary>
    internal ImagePyramid? Pyramid(MapViewLayer layer) => Get(SourcesOf(layer)[^1]);

    /// <summary>The session this renderer draws.</summary>
    internal MapEditSession Session => session;
}
