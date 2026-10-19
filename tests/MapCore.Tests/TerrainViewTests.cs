using System.Diagnostics;
using MapCore.Editing;
using MapCore.Imaging;
using MapCore.Rendering;

namespace MapCore.Tests;

static class TerrainViewTests
{
    static MapEditSession Fresh(string name) =>
        MapEditSession.Open(MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir(name)));

    static readonly Lazy<MapEditSession> shared = new(() => Fresh("terrain-shared"));

    static (MapRenderer Layers, TerrainView View) Views(MapEditSession s)
    {
        var layers = new MapRenderer(s);
        return (layers, new TerrainView(layers));
    }

    /// <summary>
    /// The placed features' models in the 3D view: Acidic Quarry's rocks are drawn where they stand (their pixels
    /// change, and only over ground near them), each kind read once whatever the number placed; the engine's trees on
    /// the synthetic map get their stand-in, with nothing read. (A low view behind the plateau's rim is reported for
    /// interest: what the rim hides there depends on the depth test the ground already uses.)
    /// </summary>
    [Test]
    public static void PlacedFeaturesAreDrawnAsTheirModels()
    {
        var s = shared.Value;
        var (layers, view) = Views(s);
        using (layers)
        using (view)
        {
            var camera = new OrbitCamera(2200, 2580, 30, 35, 600);
            var bare = new RgbaImage(640, 400);
            var drawn = new RgbaImage(640, 400);
            view.Render(bare, camera, new TerrainOptions());
            view.Render(drawn, camera, new TerrainOptions { Models = true });
            var (models, dots) = view.ModelsDrawn;
            int changed = 0, nearRock = 0;
            var rocks = s.Items.All.Where(i => i.Name.StartsWith("agorm_rock", StringComparison.Ordinal)).ToList();
            for (int y = 0; y < 400; y++)
                for (int x = 0; x < 640; x++)
                    if (bare.GetPixel(x, y) != drawn.GetPixel(x, y))
                    {
                        changed++;
                        if (view.GroundAt(x, y) is (double gx, double gz) && rocks.Any(r => Math.Abs(r.X - gx) < 120 && Math.Abs(r.Z - gz) < 120))
                            nearRock++;
                    }
            Report.Info($"{models} models and {dots} dots; {changed} pixels changed, {nearRock} of them over ground near a rock; {view.ModelReads} models read");
            Assert.True(models > 0 && changed > 300, "the rocks are drawn");
            Assert.True(nearRock > changed * 0.9, "where the rocks stand, and nowhere else");
            Assert.True(view.ModelReads == rocks.Select(r => r.Name).Distinct().Count(), "each kind of rock read once");

            // From low down behind the plateau, its rim hides rocks beyond it: fewer pixels change than are drawn in the open.
            var low = new OrbitCamera(1300, 1300, 135, 6, 900);
            view.Render(bare, low, new TerrainOptions());
            view.Render(drawn, low, new TerrainOptions { Models = true });
            int hidden = view.ModelsDrawn.Models > 0 ? Enumerable.Range(0, 640 * 400).Count(i => bare.GetPixel(i % 640, i / 640) != drawn.GetPixel(i % 640, i / 640)) : -1;
            Report.Info($"low behind the rim: {view.ModelsDrawn.Models} models in view, {hidden} pixels changed");
        }

        var synthetic = MapEditSession.Open(MapPackage.Open(SyntheticMap.Build(TestData.NewWorkDir("terrain-trees")), TestData.NewWorkDir("terrain-trees-open")));
        var (treeLayers, treeView) = Views(synthetic);
        using (treeLayers)
        using (treeView)
        {
            var image = new RgbaImage(400, 300);
            treeView.Render(image, OrbitCamera.Overview(synthetic.MapWidth, synthetic.MapHeight, 4 / 3.0) with { Distance = 2500 }, new TerrainOptions { Models = true });
            var (models, dots) = treeView.ModelsDrawn;
            Assert.True(models + dots == synthetic.Items.All.Count(i => i.Name.StartsWith("treetype", StringComparison.Ordinal)) && treeView.ModelReads == 0,
                $"every engine tree drawn as the stand-in, nothing read ({models} models, {dots} dots)");
        }
    }

    [Test]
    public static void ProjectedPointsShowTheirOwnGround()
    {
        var s = shared.Value;
        var (layers, view) = Views(s);
        using (layers)
        using (view)
        {
            var target = new RgbaImage(800, 600);
            var camera = OrbitCamera.Overview(s.MapWidth, s.MapHeight, 800 / 600.0);
            view.Render(target, camera, new TerrainOptions { Shadows = false });
            var problems = new List<string>();
            // Plateau tops near the start positions: in the open, so nothing should hide them.
            foreach (var start in s.Info.StartPositions)
            {
                double y = Math.Max(0, s.Heights.HeightAt(start.X, start.Z));
                if (view.Project(start.X, y, start.Z) is not (double px, double py, _))
                {
                    problems.Add($"start {start.Team} is behind the camera");
                    continue;
                }
                var ground = view.GroundAt((int)px, (int)py);
                if (ground is not (double gx, double gz) || Math.Abs(gx - start.X) > 24 || Math.Abs(gz - start.Z) > 24)
                    problems.Add($"start {start.Team} ({start.X}, {start.Z}) projects to ({px:0}, {py:0}), which shows ground at {ground}");
            }
            Assert.Empty(problems, "projected points vs the ground drawn there");

            // From the south (yaw 0): north is up the screen and east is to the right.
            var north = view.Project(3072, 0, 1000)!.Value;
            var south = view.Project(3072, 0, 5000)!.Value;
            var east = view.Project(5000, 0, 3072)!.Value;
            var west = view.Project(1000, 0, 3072)!.Value;
            Assert.True(north.Y < south.Y && east.X > west.X, "north is up and east is right when looking north");
        }
    }

    /// <summary>
    /// Acidic Quarry is nearly symmetric, so a mirrored 3D view could look right. A patch painted at one corner must
    /// appear where that corner projects, and not where its mirror images do, from two directions.
    /// </summary>
    [Test]
    public static void PaintedPatchAppearsWhereItProjects()
    {
        var s = Fresh("terrain-patch");
        const double px0 = 1250, pz0 = 4950;
        s.Apply("texture-paint:1250,4950:150:255,0,0:0.8");
        var (layers, view) = Views(s);
        using (layers)
        using (view)
        {
            var problems = new List<string>();
            foreach (double yaw in new[] { 0.0, 90.0, 215.0 })
            {
                var camera = new OrbitCamera(2000, 4000, yaw, 50, 4500);
                var target = new RgbaImage(640, 480);
                view.Render(target, camera, new TerrainOptions());
                bool Red(double x, double z)
                {
                    double y = Math.Max(0, s.Heights.HeightAt(x, z));
                    if (view.Project(x, y, z) is not (double ix, double iy, _) || ix < 0 || iy < 0 || ix >= 640 || iy >= 480)
                        return false;
                    var p = target.GetPixel((int)ix, (int)iy);
                    return p.R > p.G + 50 && p.R > p.B + 50;
                }
                if (!Red(px0, pz0))
                    problems.Add($"yaw {yaw}: the patch is not red where it projects");
                if (Red(s.MapWidth - px0, pz0) || Red(px0, s.MapHeight - pz0))
                    problems.Add($"yaw {yaw}: a mirror image of the patch is red");
            }
            Assert.Empty(problems, "painted patch");
        }
    }

    [Test]
    public static void RaisedMoundHidesTheGroundBehindIt()
    {
        var s = Fresh("terrain-mound");
        var (layers, view) = Views(s);
        using (layers)
        using (view)
        {
            // A metal spot on a plateau top near the middle, seen from the south over open sea.
            var spot = s.Metal.FindSpots().MinBy(p => Math.Abs(p.X - 3072) + Math.Abs(p.Z - 2000))!;
            double bx = spot.X, bz = spot.Z;
            var camera = new OrbitCamera(bx, bz + 750, 0, 25, 1600);
            var target = new RgbaImage(640, 480);
            view.Render(target, camera, new TerrainOptions());
            var (ix, iy, _) = view.Project(bx, s.Heights.HeightAt(bx, bz), bz)!.Value;
            var before = view.GroundAt((int)ix, (int)iy);
            Assert.True(before is (double x0, double z0) && Math.Abs(x0 - bx) < 30 && Math.Abs(z0 - bz) < 40,
                $"before: the spot at ({bx}, {bz}) is visible (that pixel shows {before})");

            s.RaiseHeights(new MapCore.Heights.Brush(bx, bz + 400, 250, 0.6), 700);
            view.Render(target, camera, new TerrainOptions());
            var after = view.GroundAt((int)ix, (int)iy);
            Assert.True(after is (_, double z1) && z1 > bz + 150, $"after raising a mound in front, that pixel shows the mound instead: {after}");
        }
    }

    [Test]
    public static void LookingStraightDownMatchesThe2DView()
    {
        var s = shared.Value;
        var (layers, view) = Views(s);
        using (layers)
        using (view)
        {
            const int size = 300;
            const double fov = 2, cx = 1700, cz = 4600, units = 4;
            double distance = size * units / 2 / Math.Tan(fov * Math.PI / 360);
            var three = new RgbaImage(size, size);
            view.Render(three, new OrbitCamera(cx, cz, 0, 89.9, distance, fov), new TerrainOptions { Lighting = false });
            var two = new RgbaImage(size, size);
            layers.Render(two, new ViewTransform(cx - size * units / 2, cz - size * units / 2, units), MapViewLayer.Texture, MapOverlays.None);
            var mirrored = new RgbaImage(size, size);
            layers.Render(mirrored, new ViewTransform(cx - size * units / 2, cz - size * units / 2, units), MapViewLayer.Texture, MapOverlays.None);
            mirrored.FlipVertical();

            double Difference(RgbaImage a, RgbaImage b)
            {
                long total = 0;
                for (int i = 0; i < a.Pixels.Length; i += 4)
                    for (int c = 0; c < 3; c++)
                        total += Math.Abs(a.Pixels[i + c] - b.Pixels[i + c]);
                return total / (size * size * 3.0);
            }
            double same = Difference(three, two), flipped = Difference(three, mirrored);
            Report.Info($"straight down vs 2D: mean difference {same:0.0}; vs the 2D view upside down {flipped:0.0}");
            Assert.True(same < 8 && same < flipped / 2, "looking straight down shows what the 2D view shows, the right way up");
        }
    }

    [Test]
    public static void ShadowsFallAwayFromTheSun()
    {
        var s = Fresh("terrain-shadow");
        var (layers, view) = Views(s);
        using (layers)
        using (view)
        {
            var (lx, _, lz) = SceneLighting.From(s.Info).SunDirection;
            double h = Math.Sqrt(lx * lx + lz * lz), dx = lx / h, dz = lz / h;
            const double x = 3072, z = 3072;
            s.RaiseHeights(new MapCore.Heights.Brush(x, z, 60, 0.8), 700);
            double away = view.LitAt(x - dx * 160, z - dz * 160), towards = view.LitAt(x + dx * 160, z + dz * 160);
            Assert.True(away < 0.1 && towards > 0.9, $"a tall pillar shades the side away from the sun ({away:0.##}) and not the sunny side ({towards:0.##})");
        }
    }

    /// <summary>
    /// Colour edits (a stream of them while a slider drags) must not redo the shadows, but must show; moving the sun
    /// or previewing another sea level redoes them.
    /// </summary>
    [Test]
    public static void ShadowsAreRedoneOnlyWhenTheSunOrSeaLevelMoves()
    {
        var s = Fresh("terrain-mapinfo");
        var (layers, view) = Views(s);
        using (layers)
        using (view)
        {
            var target = new RgbaImage(320, 240);
            var camera = OrbitCamera.Overview(s.MapWidth, s.MapHeight, 320 / 240.0);
            view.Render(target, camera, new TerrainOptions());
            int builds = view.ShadowBuilds;
            var before = (byte[])target.Pixels.Clone();

            s.Apply("set:water.surfaceColor=1,0,0");
            s.Apply("start-move:0:1100,5000");
            view.Render(target, camera, new TerrainOptions());
            Assert.Equal(builds, view.ShadowBuilds, "shadow builds after a colour edit and a start-position move");
            Assert.True(!target.Pixels.AsSpan().SequenceEqual(before), "the new water colour shows (mapinfo was read again)");

            s.Apply("set:lighting.sunDir=0.6,0.5,0.3");
            view.Render(target, camera, new TerrainOptions());
            Assert.Equal(builds + 1, view.ShadowBuilds, "shadow builds after moving the sun");

            layers.WaterLevel = 60;
            view.Render(target, camera, new TerrainOptions());
            Assert.Equal(builds + 2, view.ShadowBuilds, "shadow builds after previewing another sea level");
        }
    }

    /// <summary>After brush edits only part of the shadows is redone; the result must equal a full rebuild.</summary>
    [Test]
    public static void LocalShadowUpdatesMatchAFullRebuild()
    {
        var s = Fresh("terrain-shadow-local");
        var (layers, view) = Views(s);
        using (layers)
        using (view)
        {
            view.ShadowMaps(); // full build
            s.RaiseHeights(new MapCore.Heights.Brush(3072, 3072, 150, 0.5), 600); // a new highest point: shadows reach further
            s.RaiseHeights(new MapCore.Heights.Brush(1500, 2000, 120, 0.5), -60);
            var sw = Stopwatch.StartNew();
            var local = view.ShadowMaps();
            long localMs = sw.ElapsedMilliseconds;
            using var other = new TerrainView(layers);
            var full = other.ShadowMaps();
            int differing = local.Zip(full).Count(p => p.First != p.Second);
            Report.Info($"local shadow update {localMs} ms; {differing} texels differ from a full rebuild");
            Assert.Equal(0, differing, "texels that differ from a full rebuild");
        }
    }

    [Test]
    public static void ThreeDFramesAreQuickEnough()
    {
        var s = shared.Value;
        var (layers, view) = Views(s);
        using (layers)
        using (view)
        {
            var camera = OrbitCamera.Overview(s.MapWidth, s.MapHeight, 1.6).Orbit(30, -10);
            var full = new RgbaImage(1600, 1000);
            var quick = new RgbaImage(800, 500);
            view.Render(full, camera, new TerrainOptions()); // builds the texture pyramid and shadows
            var sw = Stopwatch.StartNew();
            view.Render(full, camera.Orbit(5, 0), new TerrainOptions { Markers = RenderRequest.AllOverlays });
            double fullMs = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            const int frames = 5;
            for (int i = 0; i < frames; i++)
                view.Render(quick, camera.Orbit(i * 3, 0), new TerrainOptions { MeshStep = 4 });
            double quickMs = sw.Elapsed.TotalMilliseconds / frames;
            var fresh = Fresh("terrain-speed");
            var (freshLayers, freshView) = Views(fresh);
            using (freshLayers)
            using (freshView)
            {
                sw.Restart();
                freshView.LitAt(0, 0);
                Report.Info($"3D: shadows for the whole map in {sw.ElapsedMilliseconds} ms (first still frame only; brush edits redo just the strip they can shade)");
            }
            Report.Info($"3D: {fullMs:0} ms for a 1600x1000 still frame (every vertex, markers); {quickMs:0} ms per 800x500 moving frame (every 4th vertex)");
            Assert.True(quickMs < 100, "moving frames stay under 100 ms");
            Assert.True(fullMs < 1000, "still frames stay under a second");
        }
    }
}
