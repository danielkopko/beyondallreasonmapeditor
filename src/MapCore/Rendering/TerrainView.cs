using System.Globalization;
using System.Numerics;

using MapCore.Assets;
using MapCore.Editing;
using MapCore.Heights;
using MapCore.Imaging;
using MapCore.Items;
using MapCore.Movement;

namespace MapCore.Rendering;

/// <summary>How the 3D view draws the terrain.</summary>
public sealed record TerrainOptions
{
    /// <summary>Vertical scale; 1 is true to the game.</summary>
    public double Exaggeration { get; init; } = 1;

    /// <summary>Sun lighting from mapinfo; off shows the bare texture (for comparing with the 2D view).</summary>
    public bool Lighting { get; init; } = true;

    /// <summary>Terrain shadows cast by the sun.</summary>
    public bool Shadows { get; init; } = true;

    /// <summary>Water tint below sea level, as the engine shades the sea bed.</summary>
    public bool Water { get; init; } = true;

    /// <summary>Markers drawn on the terrain (metal spots, items, start positions).</summary>
    public MapOverlays Markers { get; init; } = MapOverlays.None;

    /// <summary>Mesh step in heightmap squares: 1 uses every vertex; 2 or 4 for quick frames while the camera moves.</summary>
    public int MeshStep { get; init; } = 1;

    /// <summary>
    /// Tints the ground by where this movement class can go (see <see cref="PassabilityMap"/>): green open, cyan a
    /// hover on water, red too steep, purple too deep. Null for none.
    /// </summary>
    public MoveClass? Passability { get; init; }

    /// <summary>
    /// Draws the placed features' models (trees, rocks, buildings) where they stand, turned to their headings: those
    /// whose models the map has, and the engine's trees as simple stand-ins. BAR's own features have no model here.
    /// </summary>
    public bool Models { get; init; }
}

/// <summary>
/// Draws the map in 3D on the CPU: the heightmap as a triangle mesh with the map texture draped over it, lit as the
/// engine lights the ground (mapinfo sun, ambient and diffuse colours; shadows; the sea bed tinted by the water
/// colours). Heights and the texture are read live, so unsaved edits show at once. Rasterising stores, for each
/// pixel, which point of the ground it shows; shading then samples the texture pyramid at that point at the
/// level matching the pixel's footprint, so distant ground doesn't shimmer.
/// Not thread-safe: use it from the thread that edits the session, like <see cref="MapRenderer"/>.
/// </summary>
public sealed class TerrainView : IDisposable
{
    const double Near = 1;
    const int BandHeight = 16;
    const double ShadowUnits = 4;
    const double IntensityMult = 210.0 / 255.0; // SMFFragProg.glsl SMF_INTENSITY_MULT
    const double ShallowWater = 10;            // SMF_SHALLOW_WATER_DEPTH

    readonly MapEditSession session;
    readonly MapRenderer layers;
    float[] depth = [], groundX = [], groundZ = [];
    int width, height;
    Frame? last;
    float[]? shadow, surfaceShadow;
    double shadowExaggeration = double.NaN, shadowWaterLevel;
    (double X, double Y, double Z) shadowSun;
    bool shadowStale = true;
    WorldRect? staleArea; // where heights changed since the shadows were made; null with shadowStale: everywhere
    SceneLighting? lighting;
    PassabilityMap? passability;
    SceneModels? models;
    bool passabilityStale;
    WorldRect? passabilityArea; // where heights changed since the passability was worked out; null with passabilityStale: everywhere

    /// <summary>The tint for each kind of square, and how strongly it is blended over the shaded ground.</summary>
    internal static readonly Rgba OpenTint = new(60, 215, 80, 255), OnWaterTint = new(30, 200, 240, 255),
        TooSteepTint = new(235, 45, 35, 255), TooDeepTint = new(160, 70, 220, 255);
    const double TintStrength = 0.45;

    /// <summary>BAR's own features and their models, to draw the ones the map names but doesn't define (null: none).</summary>
    public BarFeatureCatalog? BarFeatures
    {
        get => barFeatures;
        set
        {
            barFeatures = value;
            models = null; // read again with them
        }
    }

    BarFeatureCatalog? barFeatures;

    public TerrainView(MapRenderer layers)
    {
        this.layers = layers;
        session = layers.Session;
        session.Changed += OnChanged;
    }

    public void Dispose() => session.Changed -= OnChanged;

    void OnChanged(object? sender, MapChange change)
    {
        if (change.Layers.HasFlag(MapLayers.Heights))
        {
            // Keep a local area only while every change since the last shadows was local.
            staleArea = change.Area is WorldRect a && (!shadowStale || staleArea != null) ? staleArea?.Union(a) ?? a : null;
            shadowStale = true;
        }
        if (change.Layers.HasFlag(MapLayers.MapInfo))
            lighting = null; // colours or the sun may have changed; shadows are redone only if the sun moved (see CheckShadows)
        if (change.Layers.HasFlag(MapLayers.Items))
            models?.FilesMayHaveChanged(); // an asset added or undone brings or takes models
        if (change.Layers.HasFlag(MapLayers.Heights) && passability != null)
        {
            passabilityArea = change.Area is WorldRect p && (!passabilityStale || passabilityArea != null) ? passabilityArea?.Union(p) ?? p : null;
            passabilityStale = true;
        }
    }

    /// <summary>The passability map for a class at the current sea level, brought up to date (only where heights changed).</summary>
    internal PassabilityMap Passability(MoveClass moveClass)
    {
        if (passability == null || passability.Class != moveClass || passability.WaterLevel != WaterLevel)
            passability = new PassabilityMap(session.Heights, moveClass, WaterLevel);
        else if (passabilityStale)
            passability.Update(passabilityArea);
        passabilityStale = false;
        passabilityArea = null;
        return passability;
    }

    /// <summary>For tests: how many times shadows were (re)built.</summary>
    internal int ShadowBuilds { get; private set; }

    /// <summary>Shadows must be redone everywhere when the sun or the (previewed) sea level moved.</summary>
    void CheckShadows()
    {
        if (shadow != null && (Lighting.SunDirection != shadowSun || layers.WaterLevel != shadowWaterLevel))
        {
            shadowStale = true;
            staleArea = null;
        }
    }

    /// <summary>Sea level: the game's 0, or the level the 2D view is previewing.</summary>
    double WaterLevel => layers.WaterLevel;

    SceneLighting Lighting => lighting ??= SceneLighting.From(session.Info);

    /// <summary>The camera and image size of one render, for projecting points.</summary>
    sealed record Frame(double Ex, double Ey, double Ez, double Fx, double Fy, double Fz, double Rx, double Ry, double Rz,
        double Ux, double Uy, double Uz, double Focal, double Cx, double Cy, double Exaggeration)
    {
        public (double X, double Y, double Depth)? Project(double x, double y, double z)
        {
            double vx = x - Ex, vy = y - Ey, vz = z - Ez;
            double zc = vx * Fx + vy * Fy + vz * Fz;
            if (zc < Near)
                return null;
            return (Cx + (vx * Rx + vy * Ry + vz * Rz) / zc * Focal, Cy - (vx * Ux + vy * Uy + vz * Uz) / zc * Focal, zc);
        }

        /// <summary>The direction of the ray through an image position (not normalised).</summary>
        public (double X, double Y, double Z) Ray(double px, double py)
        {
            double a = (px - Cx) / Focal, b = (Cy - py) / Focal;
            return (Fx + Rx * a + Ux * b, Fy + Ry * a + Uy * b, Fz + Rz * a + Uz * b);
        }
    }

    /// <summary>The height the camera orbits: the ground at the target, or the sea surface over water.</summary>
    public double TargetHeight(OrbitCamera camera, double exaggeration) =>
        Math.Max(WaterLevel, session.Heights.HeightAt(camera.TargetX, camera.TargetZ)) * exaggeration;

    public RenderResult Render(RgbaImage target, OrbitCamera camera, TerrainOptions options)
    {
        var frame = MakeFrame(camera, options.Exaggeration, target.Width, target.Height);
        last = frame;
        Resize(target.Width, target.Height);
        Rasterise(frame, options);
        var texture = layers.Pyramid(MapViewLayer.Texture);
        CheckShadows();
        if (options.Lighting && options.Shadows && (shadowStale || shadowExaggeration != options.Exaggeration || shadow == null))
            if (options.MeshStep == 1 || shadow == null || shadowExaggeration != options.Exaggeration)
                BuildShadows(options.Exaggeration);
        Shade(target, frame, options, texture);
        if (options.Passability is MoveClass moveClass)
            Tint(target, Passability(moveClass));
        if (options.Models)
            DrawModels(target, frame, options);
        var labels = new List<MapLabel>();
        DrawMarkers(target, frame, options, labels);
        return new RenderResult(labels, null);
    }

    /// <summary>Where a world point appears in the last render: image position and camera distance; null if behind the camera.</summary>
    public (double X, double Y, double Depth)? Project(double x, double y, double z) => last?.Project(x, y, z);

    /// <summary>The ground position shown at an image pixel in the last render, or null where there is no ground.</summary>
    public (double X, double Z)? GroundAt(int px, int py)
    {
        if ((uint)px >= (uint)width || (uint)py >= (uint)height)
            return null;
        int i = py * width + px;
        return depth[i] > 0 ? (groundX[i], groundZ[i]) : null;
    }

    Frame MakeFrame(OrbitCamera camera, double exaggeration, int w, int h)
    {
        double ty = TargetHeight(camera, exaggeration);
        var (ex, ey, ez) = camera.Eye(ty);
        // Keep the eye above the ground (and the sea) under it: inside a hill, culling would make the hill vanish.
        ey = Math.Max(ey, Math.Max(session.Heights.HeightAt(ex, ez), WaterLevel) * exaggeration + 20);
        double fx = camera.TargetX - ex, fy = ty - ey, fz = camera.TargetZ - ez;
        double fl = Math.Sqrt(fx * fx + fy * fy + fz * fz);
        (fx, fy, fz) = (fx / fl, fy / fl, fz / fl);
        // right = forward × up(0, 1, 0); camera up = right × forward
        double rx = -fz, ry = 0, rz = fx, rl = Math.Sqrt(rx * rx + rz * rz);
        if (rl < 1e-9)
            (rx, rz, rl) = (1, 0, 1);
        (rx, rz) = (rx / rl, rz / rl);
        double ux = ry * fz - rz * fy, uy = rz * fx - rx * fz, uz = rx * fy - ry * fx;
        double focal = h / 2.0 / Math.Tan(camera.FieldOfView * Math.PI / 360);
        return new Frame(ex, ey, ez, fx, fy, fz, rx, ry, rz, ux, uy, uz, focal, w / 2.0, h / 2.0, exaggeration);
    }

    void Resize(int w, int h)
    {
        if (w * h != depth.Length)
        {
            depth = new float[w * h];
            groundX = new float[w * h];
            groundZ = new float[w * h];
        }
        (width, height) = (w, h);
        Array.Clear(depth);
    }

    // ---------------------------------------------------------------- rasterising

    /// <summary>
    /// Projects the mesh vertices, then fills the depth buffer band by band (bands run in parallel and never
    /// overlap). Each pixel keeps the nearest ground point, interpolated perspective-correctly.
    /// </summary>
    void Rasterise(Frame frame, TerrainOptions options)
    {
        var heights = session.Heights;
        int step = Math.Clamp(options.MeshStep, 1, 16);
        var cols = Grid(heights.Width, step);
        var rows = Grid(heights.Height, step);
        int nc = cols.Length, nr = rows.Length;
        var sx = new float[nc * nr];
        var sy = new float[nc * nr];
        var iz = new float[nc * nr];
        var h = heights.Heights;
        double spacing = HeightMap.Spacing, e = options.Exaggeration;

        Parallel.For(0, nr, j =>
        {
            int z = rows[j];
            for (int i = 0; i < nc; i++)
            {
                int x = cols[i], k = j * nc + i;
                var p = frame.Project(x * spacing, h[z * heights.Width + x] * e, z * spacing);
                if (p is (double px, double py, double d))
                    (sx[k], sy[k], iz[k]) = ((float)px, (float)py, (float)(1 / d));
                else
                    iz[k] = 0;
            }
        });

        int w = width, hh = height;
        int bands = (hh + BandHeight - 1) / BandHeight;
        Parallel.For(0, bands, band =>
        {
            int y0 = band * BandHeight, y1 = Math.Min(hh, y0 + BandHeight);
            for (int j = 0; j + 1 < nr; j++)
            {
                double z0 = rows[j] * spacing, z1 = rows[j + 1] * spacing;
                for (int i = 0; i + 1 < nc; i++)
                {
                    int a = j * nc + i, b = a + 1, c = a + nc, d = c + 1;
                    if (iz[a] == 0 || iz[b] == 0 || iz[c] == 0 || iz[d] == 0)
                        continue; // a corner is behind the camera
                    float top = Math.Min(Math.Min(sy[a], sy[b]), Math.Min(sy[c], sy[d]));
                    float bottom = Math.Max(Math.Max(sy[a], sy[b]), Math.Max(sy[c], sy[d]));
                    if (bottom < y0 - 1 || top > y1 + 1)
                        continue;
                    double x0 = cols[i] * spacing, x1 = cols[i + 1] * spacing;
                    // Two triangles per square: (NW, NE, SW) and (NE, SE, SW).
                    Triangle(a, b, c, x0, z0, x1, z0, x0, z1);
                    Triangle(b, d, c, x1, z0, x1, z1, x0, z1);
                }
            }

            void Triangle(int v0, int v1, int v2, double wx0, double wz0, double wx1, double wz1, double wx2, double wz2)
            {
                float ax = sx[v0], ay = sy[v0], bx = sx[v1], by = sy[v1], cx = sx[v2], cy = sy[v2];
                float area = (bx - ax) * (cy - ay) - (cx - ax) * (by - ay);
                if (area <= 0)
                    return; // facing away (or edge-on)
                int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx))));
                int maxX = Math.Min(w - 1, (int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx))));
                int minY = Math.Max(y0, (int)MathF.Floor(MathF.Min(ay, MathF.Min(by, cy))));
                int maxY = Math.Min(y1 - 1, (int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, cy))));
                if (minX > maxX || minY > maxY)
                    return;
                float i0 = iz[v0], i1 = iz[v1], i2 = iz[v2];
                float inv = 1 / area;
                for (int py = minY; py <= maxY; py++)
                {
                    float fy = py + 0.5f;
                    for (int px = minX; px <= maxX; px++)
                    {
                        float fx = px + 0.5f;
                        float l0 = ((cx - bx) * (fy - by) - (cy - by) * (fx - bx)) * inv;
                        float l1 = ((ax - cx) * (fy - cy) - (ay - cy) * (fx - cx)) * inv;
                        float l2 = 1 - l0 - l1;
                        if (l0 < 0 || l1 < 0 || l2 < 0)
                            continue;
                        float invZ = l0 * i0 + l1 * i1 + l2 * i2;
                        int o = py * w + px;
                        if (invZ <= depth[o])
                            continue;
                        depth[o] = invZ;
                        groundX[o] = (float)((l0 * i0 * wx0 + l1 * i1 * wx1 + l2 * i2 * wx2) / invZ);
                        groundZ[o] = (float)((l0 * i0 * wz0 + l1 * i1 * wz1 + l2 * i2 * wz2) / invZ);
                    }
                }
            }
        });
    }

    /// <summary>Vertex indices 0, step, 2·step, … always ending on the last vertex, so the mesh reaches the map edge.</summary>
    static int[] Grid(int count, int step)
    {
        var list = new List<int>();
        for (int i = 0; i < count - 1; i += step)
            list.Add(i);
        list.Add(count - 1);
        return [.. list];
    }

    // ---------------------------------------------------------------- shading

    void Shade(RgbaImage target, Frame frame, TerrainOptions options, ImagePyramid? texture)
    {
        var heights = session.Heights;
        var light = Lighting;
        double e = options.Exaggeration, level = WaterLevel;
        var (lx, ly, lz) = light.SunDirection;
        var sky = ToBytes(light.Sky);
        var voidColour = MapRenderer.Background;
        double mapW = session.MapWidth, mapH = session.MapHeight;
        bool useShadows = options.Lighting && options.Shadows && shadow != null;
        int shadowW = (int)Math.Round(mapW / ShadowUnits), shadowH = (int)Math.Round(mapH / ShadowUnits);
        var pixels = target.Pixels;
        int w = width;

        Parallel.For(0, height, py =>
        {
            for (int px = 0; px < w; px++)
            {
                int i = py * w + px, o = i * 4;
                pixels[o + 3] = 255;
                if (depth[i] <= 0)
                {
                    // No ground: sky above the horizon, and the editor's backdrop below it.
                    var (_, ry, _) = frame.Ray(px + 0.5, py + 0.5);
                    var c = ry > 0 ? sky : voidColour;
                    (pixels[o], pixels[o + 1], pixels[o + 2]) = (c.R, c.G, c.B);
                    continue;
                }
                double x = groundX[i], z = groundZ[i];
                double distance = 1 / depth[i];
                double footprint = distance / frame.Focal;
                var (r, g, b) = texture?.Sample(x, z, footprint) ?? (128f, 128f, 128f);
                if (!options.Lighting)
                {
                    (pixels[o], pixels[o + 1], pixels[o + 2]) = (Byte(r), Byte(g), Byte(b));
                    continue;
                }

                // Normal of the (exaggerated) ground from neighbouring heights, as the hillshade does.
                const double d = HeightMap.Spacing / 2;
                double ground = heights.HeightAt(x, z);
                double dx = (heights.HeightAt(x + d, z) - heights.HeightAt(x - d, z)) / (2 * d) * e;
                double dz = (heights.HeightAt(x, z + d) - heights.HeightAt(x, z - d)) / (2 * d) * e;
                double nl = Math.Sqrt(dx * dx + 1 + dz * dz);
                double cos = Math.Clamp((-dx * lx + ly - dz * lz) / nl, 0, 1);
                double lit = useShadows ? ShadowAt(shadow!, x, z, shadowW, shadowH) : 1;
                double shadowCoeff = 1 + (lit - 1) * light.ShadowDensity;

                // SMFFragProg.glsl GetShadeInt: ambient + diffuse·cos·shadow, then the sea-bed tint below 0.
                double sr = (light.GroundAmbient.R + light.GroundDiffuse.R * cos * shadowCoeff) * IntensityMult;
                double sg = (light.GroundAmbient.G + light.GroundDiffuse.G * cos * shadowCoeff) * IntensityMult;
                double sb = (light.GroundAmbient.B + light.GroundDiffuse.B * cos * shadowCoeff) * IntensityMult;
                if (options.Water && ground < level && !light.VoidWater)
                {
                    double below = level - ground, alpha = Math.Min(1, below / ShallowWater + (below >= ShallowWater ? 1 : 0));
                    double decay = 0.2 + below / ShallowWater * 0.1, step = Math.Min(1023, below);
                    double waterLight = Math.Min(cos * 2 + 0.4, 1) * IntensityMult * (1 - decay * (1 - shadowCoeff));
                    sr += (Math.Max(light.WaterMin.R, light.WaterBase.R - light.WaterAbsorb.R * step) * waterLight - sr) * alpha;
                    sg += (Math.Max(light.WaterMin.G, light.WaterBase.G - light.WaterAbsorb.G * step) * waterLight - sg) * alpha;
                    sb += (Math.Max(light.WaterMin.B, light.WaterBase.B - light.WaterAbsorb.B * step) * waterLight - sb) * alpha;
                }
                double fr = r * sr, fg = g * sg, fb = b * sb;
                if (options.Water && ground < level && !light.VoidWater)
                {
                    // The water surface over it. The engine's water shader (reflections, refraction) is not
                    // reproduced: an approximation, a sunlit layer of the surface colour, thicker over deeper water.
                    double cover = 0.35 + 0.4 * (1 - Math.Exp(-(level - ground) / 60));
                    // The surface is lit where sunlight reaches sea level, whatever shades the sea bed below it.
                    double surfaceLit = useShadows ? ShadowAt(surfaceShadow!, x, z, shadowW, shadowH) : 1;
                    shadowCoeff = 1 + (surfaceLit - 1) * light.ShadowDensity;
                    fr += (light.WaterSurface.R * 255 * (light.GroundAmbient.R + light.GroundDiffuse.R * ly * shadowCoeff) * IntensityMult - fr) * cover;
                    fg += (light.WaterSurface.G * 255 * (light.GroundAmbient.G + light.GroundDiffuse.G * ly * shadowCoeff) * IntensityMult - fg) * cover;
                    fb += (light.WaterSurface.B * 255 * (light.GroundAmbient.B + light.GroundDiffuse.B * ly * shadowCoeff) * IntensityMult - fb) * cover;
                }
                (pixels[o], pixels[o + 1], pixels[o + 2]) = (Byte(fr), Byte(fg), Byte(fb));
            }
        });
    }

    /// <summary>Blends each ground pixel towards the colour of what its square is for the class (see <see cref="TerrainOptions.Passability"/>).</summary>
    void Tint(RgbaImage target, PassabilityMap map)
    {
        var pixels = target.Pixels;
        int w = width;
        Parallel.For(0, height, py =>
        {
            for (int px = 0; px < w; px++)
            {
                int i = py * w + px, o = i * 4;
                if (depth[i] <= 0)
                    continue;
                var tint = map.At(groundX[i], groundZ[i]) switch
                {
                    Passage.OnWater => OnWaterTint,
                    Passage.TooSteep => TooSteepTint,
                    Passage.TooDeep => TooDeepTint,
                    _ => OpenTint,
                };
                pixels[o] = Byte(pixels[o] + (tint.R - pixels[o]) * TintStrength);
                pixels[o + 1] = Byte(pixels[o + 1] + (tint.G - pixels[o + 1]) * TintStrength);
                pixels[o + 2] = Byte(pixels[o + 2] + (tint.B - pixels[o + 2]) * TintStrength);
            }
        });
    }

    /// <summary>For tests: how lit the ground is at a point (1 sunlit, 0 in shadow), building the shadows if needed.</summary>
    internal double LitAt(double x, double z, double exaggeration = 1)
    {
        CheckShadows();
        if (shadow == null || shadowStale || shadowExaggeration != exaggeration)
            BuildShadows(exaggeration);
        return ShadowAt(shadow!, x, z, (int)Math.Round(session.MapWidth / ShadowUnits), (int)Math.Round(session.MapHeight / ShadowUnits));
    }

    /// <summary>For tests: both shadow maps (ground, then water surface), brought up to date.</summary>
    internal float[] ShadowMaps(double exaggeration = 1)
    {
        LitAt(0, 0, exaggeration);
        return [.. shadow!, .. surfaceShadow!];
    }

    static double ShadowAt(float[] s, double x, double z, int w, int h)
    {
        double u = Math.Clamp(x / ShadowUnits - 0.5, 0, w - 1), v = Math.Clamp(z / ShadowUnits - 0.5, 0, h - 1);
        int x0 = (int)u, y0 = (int)v, x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
        double fx = u - x0, fy = v - y0;
        return (s[y0 * w + x0] * (1 - fx) + s[y0 * w + x1] * fx) * (1 - fy) + (s[y1 * w + x0] * (1 - fx) + s[y1 * w + x1] * fx) * fy;
    }

    /// <summary>
    /// Which ground the sun reaches (1) or not (0), every 4 game units: from each point, march towards the sun until
    /// the ray is above the highest ground or leaves the map. A second map does the same from the water surface
    /// (sea level) wherever the ground is below it. After local height edits only the points whose sun-ray can
    /// cross the edited area are redone: the area itself and the strip down-sun of it, as far as a shadow can reach.
    /// </summary>
    void BuildShadows(double exaggeration)
    {
        var heights = session.Heights;
        var (lx, ly, lz) = Lighting.SunDirection;
        double mapW = session.MapWidth, mapH = session.MapHeight, level = WaterLevel;
        int w = (int)Math.Round(mapW / ShadowUnits), h = (int)Math.Round(mapH / ShadowUnits);
        bool partial = staleArea != null && shadow?.Length == w * h && surfaceShadow?.Length == w * h && shadowExaggeration == exaggeration;
        var result = shadow?.Length == w * h ? shadow : new float[w * h];
        var surface = surfaceShadow?.Length == w * h ? surfaceShadow : new float[w * h];
        double horizontal = Math.Sqrt(lx * lx + lz * lz);
        if (horizontal < 1e-6)
        {
            Array.Fill(result, 1f); // sun straight overhead: no terrain shadows
            Array.Fill(surface, 1f);
        }
        else
        {
            double dirX = lx / horizontal, dirZ = lz / horizontal, rise = ly / horizontal;
            double highest = heights.Heights.Max() * exaggeration, lowest = Math.Min(level * exaggeration, heights.Heights.Min() * exaggeration);
            var region = new PixelRect(0, 0, w, h);
            if (partial && staleArea is WorldRect a)
            {
                double reach = (highest - lowest) / rise + 2 * ShadowUnits;
                double left = Math.Min(a.X, a.X - dirX * reach), right = Math.Max(a.Right, a.Right - dirX * reach);
                double top = Math.Min(a.Z, a.Z - dirZ * reach), bottom = Math.Max(a.Bottom, a.Bottom - dirZ * reach);
                region = PixelRect.FromEdges(
                    (int)Math.Floor(left / ShadowUnits) - 1, (int)Math.Floor(top / ShadowUnits) - 1,
                    (int)Math.Ceiling(right / ShadowUnits) + 1, (int)Math.Ceiling(bottom / ShadowUnits) + 1).Intersect(region);
            }
            Parallel.For(region.Y, region.Bottom, j =>
            {
                double z = (j + 0.5) * ShadowUnits;
                for (int i = region.X; i < region.Right; i++)
                {
                    double x = (i + 0.5) * ShadowUnits;
                    double ground = heights.HeightAt(x, z) * exaggeration;
                    result[j * w + i] = Lit(x, z, ground);
                    surface[j * w + i] = ground < level * exaggeration ? Lit(x, z, level * exaggeration) : result[j * w + i];
                }
            });

            float Lit(double x, double z, double from)
            {
                double start = from + 0.5;
                for (double t = ShadowUnits; ; t += ShadowUnits)
                {
                    double px = x + dirX * t, pz = z + dirZ * t, ray = start + rise * t;
                    if (ray > highest || px < 0 || pz < 0 || px >= mapW || pz >= mapH)
                        return 1;
                    if (heights.HeightAt(px, pz) * exaggeration > ray)
                        return 0;
                }
            }
        }
        shadow = result;
        surfaceShadow = surface;
        shadowExaggeration = exaggeration;
        shadowSun = Lighting.SunDirection;
        shadowWaterLevel = level;
        shadowStale = false;
        staleArea = null;
        ShadowBuilds++;
    }

    // ---------------------------------------------------------------- placed models

    /// <summary>For tests: the models read so far.</summary>
    internal int ModelReads => models?.Reads ?? 0;

    /// <summary>For tests: how many placed features the last render drew as models, and how many as far-off dots.</summary>
    internal (int Models, int Dots) ModelsDrawn { get; private set; }

    /// <summary>One placed feature, projected for this frame.</summary>
    sealed class PlacedModel
    {
        public required SceneModel Model;
        public required float[] Sx, Sy, Iz; // each vertex on screen, and 1/distance (0: behind the camera)
        public float Cos, Sin;              // its heading
        public float Light;                 // the shadow coefficient where it stands
        public int Top, Bottom;             // the rows it can cover
    }

    /// <summary>
    /// Draws the placed features' models into the frame, hidden by nearer ground through the depth buffer. Each is
    /// turned to its heading, stood on the ground, textured, cut out where texture 2's alpha is clear, and lit as the
    /// ground is (mapinfo sun, ambient and diffuse colours), shaded where the terrain shadows its foot. Far ones
    /// (under about two pixels across, more while the camera moves) are single dots of their mean colour. Projection
    /// runs in parallel per feature, drawing in parallel per band of rows.
    /// </summary>
    void DrawModels(RgbaImage target, Frame frame, TerrainOptions options)
    {
        models ??= new SceneModels(session, barFeatures);
        var heights = session.Heights;
        var light = Lighting;
        double e = options.Exaggeration;
        bool useShadows = options.Lighting && options.Shadows && shadow != null;
        int shadowW = (int)Math.Round(session.MapWidth / ShadowUnits), shadowH = (int)Math.Round(session.MapHeight / ShadowUnits);
        float dotSize = options.MeshStep > 1 ? 6 : 1.5f;

        var placed = new List<(double X, double Z, double Degrees, SceneModel Model)>();
        foreach (var item in session.Items.All)
        {
            var model = item.Kind == PlacedItemKind.Feature ? models.For(item.Name) : models.ForUnit(item.Name); // units and buildings: BAR's models
            if (model is { Triangles.Length: > 0 })
                placed.Add((item.X, item.Z, MapEditSession.HeadingDegrees(item), model));
        }
        // Doors and tunnels as they stand at the start: walls and centre, or walls and repair markers if they start destroyed.
        var structures = session.Structures;
        foreach (var s in structures.Instances)
            if (structures.Kinds.TryGetValue(s.Kind, out var kind))
                foreach (var part in Structures.StructureLayout.AtStart(kind, s))
                    if (models.For(part.Feature) is SceneModel model && model.Triangles.Length > 0)
                        placed.Add((part.X, part.Z, part.Heading * 360.0 / 65536, model));
        if (placed.Count == 0)
        {
            ModelsDrawn = (0, 0);
            return;
        }

        int w = width, h = height;
        var projected = new PlacedModel?[placed.Count];
        var dots = new (int X, int Y, float InvZ, Vector3 Colour)?[placed.Count];
        Parallel.For(0, placed.Count, k =>
        {
            var (px0, pz0, degrees, model) = placed[k];
            double heading = degrees * Math.PI / 180;
            float cos = (float)Math.Cos(heading), sin = (float)Math.Sin(heading);
            double gx = px0, gz = pz0, gy = heights.HeightAt(gx, gz) * e;
            var c = model.Centre;
            if (frame.Project(gx + c.X * cos + c.Z * sin, gy + c.Y, gz - c.X * sin + c.Z * cos) is not (double cx, double cy, double depthAt))
                return;
            double screenRadius = model.Radius * frame.Focal / depthAt;
            if (cx + screenRadius < 0 || cx - screenRadius > w || cy + screenRadius < 0 || cy - screenRadius > h)
                return;
            float lit = useShadows ? (float)(1 + (ShadowAt(shadow!, gx, gz, shadowW, shadowH) - 1) * light.ShadowDensity) : 1;
            if (screenRadius < dotSize)
            {
                var a = model.Average;
                float shade = options.Lighting ? (float)IntensityMult : 1;
                dots[k] = ((int)cx, (int)cy, (float)(1 / depthAt), options.Lighting
                    ? new Vector3(a.X * (float)(light.GroundAmbient.R + light.GroundDiffuse.R * light.SunDirection.Y * lit),
                                  a.Y * (float)(light.GroundAmbient.G + light.GroundDiffuse.G * light.SunDirection.Y * lit),
                                  a.Z * (float)(light.GroundAmbient.B + light.GroundDiffuse.B * light.SunDirection.Y * lit)) * shade
                    : a);
                return;
            }
            int n = model.Positions.Length;
            var sx = new float[n];
            var sy = new float[n];
            var iz = new float[n];
            float top = float.MaxValue, bottom = float.MinValue;
            for (int v = 0; v < n; v++)
            {
                var p = model.Positions[v];
                if (frame.Project(gx + p.X * cos + p.Z * sin, gy + p.Y, gz - p.X * sin + p.Z * cos) is (double px, double py, double d))
                {
                    (sx[v], sy[v], iz[v]) = ((float)px, (float)py, (float)(1 / d));
                    top = Math.Min(top, sy[v]);
                    bottom = Math.Max(bottom, sy[v]);
                }
            }
            if (top > bottom)
                return;
            projected[k] = new PlacedModel
            {
                Model = model, Sx = sx, Sy = sy, Iz = iz, Cos = cos, Sin = sin, Light = lit,
                Top = Math.Max(0, (int)Math.Floor(top)), Bottom = Math.Min(h - 1, (int)Math.Ceiling(bottom)),
            };
        });

        var drawn = projected.Where(p => p != null).Select(p => p!).ToList();
        ModelsDrawn = (drawn.Count, dots.Count(d => d != null));
        var pixels = target.Pixels;
        var forward = new Vector3((float)frame.Fx, (float)frame.Fy, (float)frame.Fz);
        var sun = new Vector3((float)light.SunDirection.X, (float)light.SunDirection.Y, (float)light.SunDirection.Z);
        var ambient = new Vector3((float)light.GroundAmbient.R, (float)light.GroundAmbient.G, (float)light.GroundAmbient.B);
        var diffuse = new Vector3((float)light.GroundDiffuse.R, (float)light.GroundDiffuse.G, (float)light.GroundDiffuse.B);
        const int Band = 32;
        int bands = (h + Band - 1) / Band;
        Parallel.For(0, bands, band =>
        {
            int y0 = band * Band, y1 = Math.Min(h, y0 + Band);
            foreach (var m in drawn)
            {
                if (m.Bottom < y0 || m.Top >= y1)
                    continue;
                var model = m.Model;
                var tris = model.Triangles;
                for (int t = 0; t + 2 < tris.Length; t += 3)
                    Triangle(m, tris[t], tris[t + 1], tris[t + 2], t / 3, y0, y1);
            }
        });

        foreach (var dot in dots)
            if (dot is var (x, y, invZ, colour) && (uint)x < (uint)w && (uint)y < (uint)h)
            {
                int o = y * w + x;
                if (invZ <= depth[o])
                    continue;
                depth[o] = invZ;
                (pixels[o * 4], pixels[o * 4 + 1], pixels[o * 4 + 2]) = (Byte(colour.X * 255), Byte(colour.Y * 255), Byte(colour.Z * 255));
            }

        void Triangle(PlacedModel m, int a, int b, int c, int face, int y0, int y1)
        {
            float ia = m.Iz[a], ib = m.Iz[b], ic = m.Iz[c];
            if (ia == 0 || ib == 0 || ic == 0)
                return; // a corner is behind the camera
            float ax = m.Sx[a], ay = m.Sy[a], bx = m.Sx[b], by = m.Sy[b], cx = m.Sx[c], cy = m.Sy[c];
            float area = (bx - ax) * (cy - ay) - (cx - ax) * (by - ay);
            if (MathF.Abs(area) < 1e-6f)
                return;
            int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx))));
            int maxX = Math.Min(w - 1, (int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx))));
            int minY = Math.Max(y0, (int)MathF.Floor(MathF.Min(ay, MathF.Min(by, cy))));
            int maxY = Math.Min(y1 - 1, (int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, cy))));
            if (minX > maxX || minY > maxY)
                return;
            var model = m.Model;
            float inv = 1 / area;
            for (int py = minY; py <= maxY; py++)
            {
                float fy = py + 0.5f;
                for (int px = minX; px <= maxX; px++)
                {
                    float fx = px + 0.5f;
                    float l0 = ((cx - bx) * (fy - by) - (cy - by) * (fx - bx)) * inv;
                    float l1 = ((ax - cx) * (fy - cy) - (ay - cy) * (fx - cx)) * inv;
                    float l2 = 1 - l0 - l1;
                    if (l0 < 0 || l1 < 0 || l2 < 0)
                        continue; // both windings are drawn: dividing by the signed area makes the weights positive inside
                    float invZ = l0 * ia + l1 * ib + l2 * ic;
                    int o = py * w + px;
                    if (invZ <= depth[o])
                        continue;
                    float p0 = l0 * ia / invZ, p1 = l1 * ib / invZ, p2 = 1 - p0 - p1;
                    Vector3 colour;
                    float glow = 0;
                    if (model.FaceColours is Vector3[] faces)
                        colour = faces[face];
                    else
                    {
                        var uv = model.Uvs[a] * p0 + model.Uvs[b] * p1 + model.Uvs[c] * p2;
                        if (model.CutOut && model.Extra != null && ModelRenderer.Sample(model.Extra, uv).W < 0.5f)
                            continue;
                        var texel = model.Colour == null ? new Vector4(0.6f) : ModelRenderer.Sample(model.Colour, uv);
                        colour = new Vector3(texel.X, texel.Y, texel.Z);
                        if (model.Extra != null)
                            glow = ModelRenderer.Sample(model.Extra, uv).X * 0.6f;
                    }
                    if (options.Lighting)
                    {
                        var local = model.Normals[a] * p0 + model.Normals[b] * p1 + model.Normals[c] * p2;
                        var normal = new Vector3(local.X * m.Cos + local.Z * m.Sin, local.Y, -local.X * m.Sin + local.Z * m.Cos);
                        normal = normal.LengthSquared() > 1e-8f ? Vector3.Normalize(normal) : Vector3.UnitY;
                        if (Vector3.Dot(normal, forward) > 0)
                            normal = -normal; // the side facing the viewer
                        float lambert = Math.Max(0, Vector3.Dot(normal, sun)) * m.Light;
                        colour *= (ambient + diffuse * lambert) * (float)IntensityMult + new Vector3(glow);
                    }
                    depth[o] = invZ;
                    int q = o * 4;
                    (pixels[q], pixels[q + 1], pixels[q + 2]) = (Byte(colour.X * 255), Byte(colour.Y * 255), Byte(colour.Z * 255));
                }
            }
        }
    }

    // ---------------------------------------------------------------- markers

    void DrawMarkers(RgbaImage target, Frame frame, TerrainOptions options, List<MapLabel> labels)
    {
        if (options.Markers == MapOverlays.None)
            return;
        double e = options.Exaggeration;
        var heights = session.Heights;

        (double X, double Y)? Visible(double x, double z)
        {
            double y = Math.Max(heights.HeightAt(x, z), options.Water ? WaterLevel : double.MinValue) * e;
            if (frame.Project(x, y + 2, z) is not (double px, double py, double d))
                return null;
            int ix = (int)px, iy = (int)py;
            if ((uint)ix >= (uint)width || (uint)iy >= (uint)height)
                return null;
            // Hidden if the ground drawn there is clearly nearer than the marker.
            float there = depth[iy * width + ix];
            return there > 0 && 1 / there < d * 0.97 ? null : (px, py);
        }

        if (options.Markers.HasFlag(MapOverlays.Items))
            foreach (var item in session.Items.All)
                if (Visible(item.X, item.Z) is (double px, double py))
                {
                    var colour = item.Source == ItemSource.Smf ? new Rgba(255, 150, 30, 255) : new Rgba(130, 255, 130, 255);
                    Canvas.FillCircle(target, px, py, 3.2, new Rgba(0, 0, 0, 170));
                    Canvas.FillCircle(target, px, py, 2.2, colour);
                }
        if (options.Markers.HasFlag(MapOverlays.MetalSpots))
            foreach (var spot in session.Metal.FindSpots())
                if (Visible(spot.X, spot.Z) is (double px, double py))
                {
                    var ring = new Rgba(255, 225, 40, 255);
                    Canvas.FillCircle(target, px, py, 4.5, new Rgba(0, 0, 0, 170));
                    Canvas.FillCircle(target, px, py, 3.2, ring);
                    labels.Add(new MapLabel(px, py + 6, session.Metal.Income(spot).ToString("0.00", CultureInfo.InvariantCulture), ring));
                }
        if (options.Markers.HasFlag(MapOverlays.StartPositions))
            foreach (var start in session.Info.StartPositions)
                if (Visible(start.X, start.Z) is (double px, double py))
                {
                    Canvas.FillCircle(target, px, py, 9, new Rgba(255, 255, 255, 255));
                    Canvas.FillCircle(target, px, py, 7, new Rgba(40, 100, 255, 255));
                    labels.Add(new MapLabel(px, py + 11, $"Start {start.Team}", new Rgba(255, 255, 255, 255)));
                }
    }

    static Rgba ToBytes((double R, double G, double B) c) => new(Byte(c.R * 255), Byte(c.G * 255), Byte(c.B * 255), 255);

    static byte Byte(double v) => (byte)Math.Clamp(v + 0.5, 0, 255);
}
