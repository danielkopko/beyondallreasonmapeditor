// Marram Line: a non-symmetric 4v4 coast map, sea to beach to dunes to trenches, a dead man's zone, then a rise to a
// hill; a tidal inlet from the sea to a sandy cove behind the hill team's line. Built from scratch with MapCore on All
// That Simmers' detail textures and gadgets, every feature modelled and painted here. The layout is the design's table
// (../design/layout.py and its layout.json).
//   relief <out.png> [step]                   the heights alone, shaded and coloured by what climbs them, water blue
//   preview <out.png> [step] [x0 z0 x1 z1]    the painted texture, coarsely, or a window of it
using System.Diagnostics;
using System.Globalization;
using MapCore.Imaging;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var clock = Stopwatch.StartNew();
switch (args[0])
{
    case "relief":
    {
        var t = Terrain.Build();
        Console.WriteLine($"heights in {clock.Elapsed.TotalSeconds:0}s, {t.H.Min():0.0} to {t.H.Max():0.0}");
        int step = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 2;
        Relief.Write(t, args[1], step);
        Console.WriteLine($"relief in {clock.Elapsed.TotalSeconds:0}s");
        break;
    }
    case "preview":
    {
        // preview <out.png> [step] [x0 z0 x1 z1]: the painted texture at every step-th texel, or a window of it.
        var t = Terrain.Build();
        var painter = new Painter(new Surface(t.H, t.N), t);
        int step = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 8;
        int x0 = 0, z0 = 0, x1 = (int)Layout.Size, z1 = (int)Layout.Size;
        if (args.Length > 6) { x0 = int.Parse(args[3]); z0 = int.Parse(args[4]); x1 = int.Parse(args[5]); z1 = int.Parse(args[6]); }
        int w = (x1 - x0) / step, hgt = (z1 - z0) / step;
        var img = new RgbaImage(w, hgt);
        Parallel.For(0, hgt, py => { for (int px = 0; px < w; px++) img.SetPixel(px, py, painter.At(x0 + px * step, z0 + py * step)); });
        PngWriter.WriteRgba(args[1], img);
        Console.WriteLine($"preview {w}x{hgt} in {clock.Elapsed.TotalSeconds:0}s");
        break;
    }
    case "gallery":
    {
        // gallery <template> <work>: every model on flat sand, in rows, to look at.
        string work = Path.GetFullPath(args[2]), root = Path.Combine(work, "gallery");
        Directory.CreateDirectory(work);
        Create.Map(args[1], root, 512, 512, -100, 400, (x, z) => 0, (y, row) => { for (int x = 0; x < row.Length; x++) row[x] = new Rgba(206, 192, 150, 255); },
            "Gallery", null, 20261052);
        var defs = Models.All();
        Features.Write(root, defs);
        var info = MapCore.MapInfo.Load(Path.Combine(root, "mapinfo.lua"));
        info.SetFromText("name", "Marram Gallery");
        for (int n = 0; n < 16; n++) info.RemoveStartPosition(n);
        info.SetFromText("teams.0.startPos.x", "200"); info.SetFromText("teams.0.startPos.z", "200");
        info.SetFromText("teams.1.startPos.x", "3900"); info.SetFromText("teams.1.startPos.z", "3900");
        info.Save(Path.Combine(root, "mapinfo.lua"));
        Create.Pack(root, Path.Combine(work, "gallery_stage.sd7"));
        var ops = new System.Text.StringBuilder();
        double x0 = 200, z = 300, rowDepth = 0;
        foreach (var d in defs)
        {
            var (min, max) = d.Box;
            if (x0 + (max.X - min.X) > 3800) { x0 = 200; z += rowDepth + 70; rowDepth = 0; }
            ops.AppendLine($"item-add:placer:{d.Name}:{x0 - min.X:0},{z - min.Z:0}:{Place.Heading(Place.Facing(new P(1, 0)))}");
            x0 += max.X - min.X + 50;
            rowDepth = Math.Max(rowDepth, max.Z - min.Z);
        }
        File.WriteAllText(Path.Combine(work, "run.txt"), ops.ToString());
        foreach (var d in defs)
            Console.WriteLine($"{d.Name,-16} {d.Mesh.T.Count / 3,6} triangles  footprint {d.Footprint.X}x{d.Footprint.Z}  {(d.Landmark ? "landmark" : $"metal {d.Metal}, crush {d.Crush?.ToString() ?? "mass"}")}{(d.Blocking ? "" : ", doesn't block")}");
        Console.WriteLine($"gallery in {clock.Elapsed.TotalSeconds:0}s");
        break;
    }
    case "build":
    {
        // build <template> <work>: the map folder, <file>_stage.sd7 and run.txt (the checks run first; it builds anyway, to look).
        string work = Path.GetFullPath(args[2]), root = Path.Combine(work, Build.File);
        Directory.CreateDirectory(work);
        var t = Terrain.Build();
        Console.WriteLine($"heights in {clock.Elapsed.TotalSeconds:0}s, {t.H.Min():0.0} to {t.H.Max():0.0}");
        var defs = Models.All();
        var items = Place.All(t);
        var ops = Place.Ops(items);
        File.WriteAllText(Path.Combine(work, "run.txt"), ops);
        Console.WriteLine($"placed in {clock.Elapsed.TotalSeconds:0}s: {items.Count} features, " + string.Join(", ", items.GroupBy(i => i.Kind).Select(g => $"{g.Key} {g.Count()}")));
        int failures = Check.Report(Build.HeightMapOf(t.H), t, ops.Split('\n'), Check.Footprints(defs), Console.WriteLine);
        if (failures > 0) Console.WriteLine($"{failures} check(s) failed (building anyway, to look)");
        if (args.Length > 3 && args[3] == "--checks-only") break;
        var surface = new Surface(t.H, t.N);
        var painter = new Painter(surface, t);
        Create.Map(args[1], root, Layout.Squares, Layout.Squares, Layout.MinH, Layout.MaxH, (x, z) => t.H[z * t.N + x], painter.Row, Build.Smf, null, 20261052);
        Console.WriteLine($"map and texture in {clock.Elapsed.TotalSeconds:0}s");
        var maps = Path.Combine(root, "maps");
        Derive.Splat(surface, t, 4096, Path.Combine(maps, $"{Build.Smf}_splat.dds"));
        Derive.Specular(surface, t, 4096, Path.Combine(maps, $"{Build.Smf}_specular.dds"));
        Derive.Normals(surface, 4096, Path.Combine(maps, $"{Build.Smf}_normals.dds"));
        Derive.Sky(Path.Combine(maps, $"{Build.Smf}Sky.dds"), (0.34, 0.55, 0.84), (0.84, 0.88, 0.9));
        Features.Write(root, defs);
        Build.WriteMapInfo(Path.Combine(root, "mapinfo.lua"));
        Create.Pack(root, Path.Combine(work, $"{Build.File}_stage.sd7"));
        Console.WriteLine($"built in {clock.Elapsed.TotalSeconds:0}s");
        break;
    }
    case "check":
    {
        // check <extracted map> <run.txt>: the checks on the built map's own heights.
        var session = MapCore.Editing.MapEditSession.Open(MapCore.MapPackage.OpenFolder(args[1]));
        var t = Terrain.Build();
        int failures = Check.Report(session.Heights, t, File.ReadAllLines(args[2]), Check.Footprints(Models.All()), Console.WriteLine);
        Console.WriteLine($"{failures} check(s) failed");
        return failures == 0 ? 0 : 1;
    }
    case "probe":
    {
        // probe <x> <z> [half]: heights round a point, 8 apart, and who passes each square (V vehicles, B bots, H hovers;
        // . all three, - none, w water a hover's).
        var t = Terrain.Build();
        var hm = Build.HeightMapOf(t.H);
        var v = new MapCore.Movement.PassabilityMap(hm, MapCore.Movement.BarMoveClasses.Vehicle);
        var b = new MapCore.Movement.PassabilityMap(hm, MapCore.Movement.BarMoveClasses.Bot);
        var hv = new MapCore.Movement.PassabilityMap(hm, MapCore.Movement.BarMoveClasses.Hover);
        double cx = double.Parse(args[1], CultureInfo.InvariantCulture), cz = double.Parse(args[2], CultureInfo.InvariantCulture);
        int half = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 6;
        for (int dz = -half; dz <= half; dz++)
        {
            var heightsRow = new System.Text.StringBuilder();
            var passRow = new System.Text.StringBuilder();
            for (int dx = -half; dx <= half; dx++)
            {
                double x = cx + dx * 8, z = cz + dz * 8;
                heightsRow.Append($"{hm.HeightAt(x, z),6:0.0}");
                int sx = (int)(x / 8), sz = (int)(z / 8);
                bool pv = v[sx, sz] is not (MapCore.Movement.Passage.TooSteep or MapCore.Movement.Passage.TooDeep);
                bool pb = b[sx, sz] is not (MapCore.Movement.Passage.TooSteep or MapCore.Movement.Passage.TooDeep);
                bool ph = hv[sx, sz] is not (MapCore.Movement.Passage.TooSteep or MapCore.Movement.Passage.TooDeep);
                string code = pv && pb && ph ? "." : (pv ? "V" : "") + (pb ? "B" : "") + (ph ? (hv[sx, sz] == MapCore.Movement.Passage.OnWater ? "w" : "H") : "");
                passRow.Append($"{(code.Length == 0 ? "-" : code),4}");
            }
            Console.WriteLine(heightsRow + "   " + passRow);
        }
        break;
    }
    case "leaks":
    {
        // leaks <class> [x z]: where a class (vehicles, hovers) gets down into a trench: reachable trench-floor squares
        // (more than 20 down) beside reachable ground no more than 4 down, from the beach team's start 1 or a point.
        var t = Terrain.Build();
        var hm = Build.HeightMapOf(t.H);
        var cls = args[1] == "hovers" ? MapCore.Movement.BarMoveClasses.Hover : MapCore.Movement.BarMoveClasses.Vehicle;
        var pass = new MapCore.Movement.PassabilityMap(hm, cls);
        var from = args.Length > 3 ? new P(double.Parse(args[2], CultureInfo.InvariantCulture), double.Parse(args[3], CultureInfo.InvariantCulture)) : Layout.StartsOf("beach").ElementAt(1);
        var reach = Access.Fill(pass, from, null);
        int w = pass.Width;
        double Depth(int x, int z) => t.Trench[Math.Min(z, t.N - 1) * t.N + Math.Min(x, t.N - 1)];
        int shown = 0;
        var seen = new List<P>();
        for (int z = 1; z < pass.Height - 1 && shown < 40; z++)
            for (int x = 1; x < w - 1 && shown < 40; x++)
            {
                if (!reach[z * w + x] || Depth(x, z) < 20) continue;
                foreach (var (nx, nz) in new[] { (x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1) })
                    if (reach[nz * w + nx] && Depth(nx, nz) < 4)
                    {
                        var p = new P(x * 8 + 4, z * 8 + 4);
                        if (seen.Any(q => (q - p).Length < 200)) break;
                        seen.Add(p);
                        Console.WriteLine($"into a trench at ({p.X:0}, {p.Z:0}): {Depth(x, z):0} down, from ({nx * 8 + 4}, {nz * 8 + 4}) {Depth(nx, nz):0} down; heights {hm.HeightAt(p.X, p.Z):0.0} and {hm.HeightAt(nx * 8 + 4, nz * 8 + 4):0.0}");
                        shown++;
                        break;
                    }
            }
        Console.WriteLine($"{shown} way(s) in shown");
        break;
    }
    case "route":
    {
        // route <class> <fromX> <fromZ> <toX> <toZ> [shut-crossings beach|hill]: the way a class goes from one point to
        // another (round the features as the checks place them), its last 30 steps with heights and trench depths.
        var t = Terrain.Build();
        var hm = Build.HeightMapOf(t.H);
        var cls = args[1] == "hovers" ? MapCore.Movement.BarMoveClasses.Hover : args[1] == "bots" ? MapCore.Movement.BarMoveClasses.Bot : MapCore.Movement.BarMoveClasses.Vehicle;
        var pass = new MapCore.Movement.PassabilityMap(hm, cls);
        int w = pass.Width, hgt = pass.Height;
        var defs = Models.All().ToDictionary(d => d.Name);
        var blocked = new bool[w * hgt];
        int strength = args[1] == "vehicles" ? 30 : 25;
        foreach (var it in Place.All(t))
        {
            var d = defs[it.Name];
            if (!d.Blocking || (!d.Landmark && (d.Crush ?? 1_000_000) <= strength)) continue;
            int facing = (int)Math.Round(double.Parse(Place.Heading(it.Heading), CultureInfo.InvariantCulture) / 16384) % 4;
            double hx = (facing % 2 == 0 ? d.Footprint.X : d.Footprint.Z) * 8 + 8, hz = (facing % 2 == 0 ? d.Footprint.Z : d.Footprint.X) * 8 + 8;
            for (int sz = Math.Max(0, (int)((it.At.Z - hz) / 8)); sz <= Math.Min(hgt - 1, (int)((it.At.Z + hz) / 8)); sz++)
                for (int sx = Math.Max(0, (int)((it.At.X - hx) / 8)); sx <= Math.Min(w - 1, (int)((it.At.X + hx) / 8)); sx++)
                    if (Math.Abs(sx * 8 + 4 - it.At.X) <= hx && Math.Abs(sz * 8 + 4 - it.At.Z) <= hz) blocked[sz * w + sx] = true;
        }
        if (args.Length > 6)
        {
            Func<double, double> fn = args[6] == "beach" ? Layout.FireA : Layout.FireB;
            var gaps = args[6] == "beach" ? Layout.CrossA : Layout.CrossB;
            for (int sz = 0; sz < hgt; sz++)
                for (int sx = 0; sx < w; sx++)
                {
                    double x = sx * 8 + 4, z = sz * 8 + 4;
                    if (gaps.Any(g => Math.Abs(z - g) < Layout.CrossGap / 2 + 24 && Math.Abs(x - fn(g)) < 340)) blocked[sz * w + sx] = true;
                }
        }
        if (args.Length > 7 && args[7] == "inlet")
            for (int sz = 0; sz < hgt; sz++)
                for (int sx = 0; sx < w; sx++)
                    if (Layout.InWater(sx * 8 + 4, sz * 8 + 4, 10)) blocked[sz * w + sx] = true;
        double fx = double.Parse(args[2], CultureInfo.InvariantCulture), fz = double.Parse(args[3], CultureInfo.InvariantCulture);
        double tx = double.Parse(args[4], CultureInfo.InvariantCulture), tz = double.Parse(args[5], CultureInfo.InvariantCulture);
        var parent = new int[w * hgt];
        Array.Fill(parent, -2);
        var q = new Queue<int>();
        int s = (int)(fz / 8) * w + (int)(fx / 8), goal = (int)(tz / 8) * w + (int)(tx / 8);
        parent[s] = -1; q.Enqueue(s);
        bool Open(int i) => !blocked[i] && pass[i % w, i / w] is not (MapCore.Movement.Passage.TooSteep or MapCore.Movement.Passage.TooDeep);
        while (q.Count > 0 && parent[goal] == -2)
        {
            int c = q.Dequeue(), cx = c % w, cz = c / w;
            foreach (var (nx, nz) in new[] { (cx + 1, cz), (cx - 1, cz), (cx, cz + 1), (cx, cz - 1) })
            {
                if (nx < 0 || nz < 0 || nx >= w || nz >= hgt) continue;
                int ni = nz * w + nx;
                if (parent[ni] != -2 || !Open(ni)) continue;
                parent[ni] = c; q.Enqueue(ni);
            }
        }
        if (parent[goal] == -2) { Console.WriteLine("not reached"); break; }
        var path = new List<int>();
        for (int c = goal; c >= 0; c = parent[c]) path.Add(c);
        path.Reverse();
        Console.WriteLine($"{path.Count} squares");
        int every = Math.Max(1, path.Count / 40);
        for (int k = 0; k < path.Count; k += every)
        {
            int c = path[k];
            double x = c % w * 8 + 4, z = c / w * 8 + 4;
            Console.WriteLine($"  ({x:0}, {z:0}) height {hm.HeightAt(x, z):0.0}, trench {Terrain.Sample(t.Trench, t.N, new P(x, z)):0}, {t.Kind[Math.Min(c / w, t.N - 1) * t.N + Math.Min(c % w, t.N - 1)]}");
        }
        break;
    }
    case "minimap":
    {
        // minimap <render.png> <out.png> [size]: the map cut out of a straight-down render3d, scaled for the minimap.
        var img = PngReader.Read(args[1]).ToRgba();
        var bg = img.GetPixel(2, 2);
        bool Map(int x, int y)
        {
            var c = img.GetPixel(x, y);
            return Math.Abs(c.R - bg.R) + Math.Abs(c.G - bg.G) + Math.Abs(c.B - bg.B) > 18;
        }
        int left = 0, right = img.Width - 1, top = 0, bottom = img.Height - 1;
        while (left < right && !Enumerable.Range(img.Height / 4, img.Height / 2).Any(y => Map(left, y))) left++;
        while (right > left && !Enumerable.Range(img.Height / 4, img.Height / 2).Any(y => Map(right, y))) right--;
        while (top < bottom && !Enumerable.Range(img.Width / 4, img.Width / 2).Any(x => Map(x, top))) top++;
        while (bottom > top && !Enumerable.Range(img.Width / 4, img.Width / 2).Any(x => Map(x, bottom))) bottom--;
        int size = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 1024;
        var cut = img.Crop(PixelRect.FromEdges(left, top, right + 1, bottom + 1)).Resample(size, size);
        PngWriter.WriteRgba(args[2], cut);
        Console.WriteLine($"map at ({left}, {top}) to ({right}, {bottom}) of {img.Width}x{img.Height}, written {size}x{size}");
        break;
    }
    case "setminimap":
    {
        // setminimap <extracted map> <png>: the SMF's minimap from that picture (the lobby picture follows).
        var smfPath = Directory.GetFiles(Path.Combine(args[1], "maps"), "*.smf").Single();
        var smf = MapCore.Smf.SmfFile.Load(File.ReadAllBytes(smfPath));
        MapCore.Smf.Minimap.Rebuild(smf, PngReader.Read(args[2]).ToRgba());
        smf.Save(smfPath);
        Console.WriteLine($"minimap of {Path.GetFileName(smfPath)} set from {Path.GetFileName(args[2])}");
        break;
    }
    default:
        throw new ArgumentException("unknown command " + args[0]);
}
return 0;

static class Build
{
    public const string File = "marram_line_1.0", Smf = "MarramLine";

    public static MapCore.Heights.HeightMap HeightMapOf(float[] raw)
    {
        var smf = new MapCore.Smf.SmfFile(Layout.Squares, Layout.Squares) { MinHeight = Layout.MinH, MaxHeight = Layout.MaxH };
        for (int i = 0; i < raw.Length; i++)
            smf.Heightmap[i] = (ushort)Math.Clamp(Math.Round((raw[i] - Layout.MinH) / (Layout.MaxH - Layout.MinH) * 65535), 0, 65535);
        return MapCore.Heights.HeightMap.Load(smf, null);
    }

    /// <summary>mapinfo.lua: name and credits, metal, a bright windy coastal day, a clear green-blue sea, tidal, eight starts.</summary>
    public static void WriteMapInfo(string path)
    {
        var info = MapCore.MapInfo.Load(path);
        void Set(string key, string value) => info.SetFromText(key, value);
        Set("name", "Marram Line");
        Set("shortname", "MarramLine");
        Set("version", "1.0");
        Set("author", "Daniel Kopko");
        Set("description", "A 4v4 coast map where an old war dug in: the beach team's bases in the dunes with the sea behind, the hill team's on a slight rise among hedgerows, two trench lines facing across a dead man's zone. A tidal inlet carries ships from the sea through both lines to the hill team's sandy cove. Not symmetric. Detail textures and gadgets from All That Simmers by Nikuksis.");
        Set("maxMetal", "4");
        Set("atmosphere.skyColor", "0.5,0.66,0.88");
        Set("atmosphere.fogColor", "0.78,0.84,0.88");
        Set("atmosphere.fogStart", "0.35");
        Set("atmosphere.sunColor", "1.02,1,0.94");
        Set("atmosphere.cloudColor", "0.95,0.96,0.97");
        Set("atmosphere.minWind", "6");
        Set("atmosphere.maxWind", "18");
        Set("atmosphere.skyBox", $"{Smf}Sky.dds");
        Set("tidalStrength", "18");
        Set("lighting.groundAmbientColor", "0.46,0.48,0.5");
        Set("lighting.groundDiffuseColor", "1.02,1,0.94");
        Set("lighting.groundSpecularColor", "0.3,0.3,0.28");
        Set("lighting.unitAmbientColor", "0.5,0.52,0.54");
        Set("lighting.unitDiffuseColor", "1,0.98,0.92");
        Set("lighting.sunDir", "-0.62,0.6,0.5");
        Set("custom.fog.color", "0.78,0.84,0.88");
        // A clear temperate sea: turquoise over the sand in the shallows, deepening to blue-green.
        Set("water.baseColor", "0.56,0.8,0.78");
        Set("water.minColor", "0.03,0.12,0.16");
        Set("water.surfaceColor", "0.5,0.76,0.78");
        Set("water.planeColor", "0.06,0.24,0.3");
        Set("water.absorb", "0.012,0.0055,0.004");
        Set("water.specularColor", "0.85,0.9,0.92");
        Set("water.surfaceAlpha", "0.12");
        Set("resources.specularTex", $"{Smf}_specular.dds");
        Set("resources.splatDistrTex", $"{Smf}_splat.dds");
        Set("resources.detailNormalTex", $"{Smf}_normals.dds");
        info.Document.Remove("custom.grassConfig.grassDistTGA");
        for (int n = 0; n < 16; n++)
            info.RemoveStartPosition(n);
        // Teams 0 to 3 the beach team (north to south), 4 to 7 the hill team.
        foreach (var (n, _, at) in Layout.Starts)
        {
            Set($"teams.{n}.startPos.x", Math.Round(at.X).ToString(CultureInfo.InvariantCulture));
            Set($"teams.{n}.startPos.z", Math.Round(at.Z).ToString(CultureInfo.InvariantCulture));
        }
        info.Save(path);
    }
}

static class Relief
{
    /// <summary>The heights shaded, coloured by slope against BAR's movement classes: over 54 degrees (spiders only) dark
    /// red, over 33 (bots only) orange, over 27 (hovers but not vehicles) yellow, gentler by height; water blue by depth.</summary>
    public static void Write(Terrain.Result t, string path, int step)
    {
        int n = t.N, w = (n - 1) / step;
        var img = new RgbaImage(w, w);
        float Hv(int x, int z) => t.H[Math.Clamp(z, 0, n - 1) * n + Math.Clamp(x, 0, n - 1)];
        Parallel.For(0, w, pz =>
        {
            for (int px = 0; px < w; px++)
            {
                int x = px * step, z = pz * step;
                double dx = (Hv(x + 1, z) - Hv(x - 1, z)) / 16.0, dz = (Hv(x, z + 1) - Hv(x, z - 1)) / 16.0;
                double len = Math.Sqrt(dx * dx + 1 + dz * dz), light = Math.Clamp((dx * 0.6 + 0.7 + dz * 0.4) / len, 0, 1);
                double y = Hv(x, z), slope = Math.Atan(Math.Sqrt(dx * dx + dz * dz)) * 180 / Math.PI;
                (double r, double g, double b) c = y < 0 ? (40 + 60 * Math.Exp(y / 15), 110 + 90 * Math.Exp(y / 15), 160 + 60 * Math.Exp(y / 25))
                    : slope > 54 ? (120, 40, 40) : slope > 33 ? (210, 120, 60) : slope > 27 ? (225, 200, 80)
                    : y < 30 ? (214, 200, 160) : y < 80 ? (170, 180, 130) : (120, 150, 100);
                double s = y < 0 ? 1 : 0.35 + 0.75 * light;
                img.SetPixel(px, pz, new Rgba((byte)Math.Clamp(c.r * s, 0, 255), (byte)Math.Clamp(c.g * s, 0, 255), (byte)Math.Clamp(c.b * s, 0, 255), 255));
            }
        });
        PngWriter.WriteRgba(path, img);
    }
}
