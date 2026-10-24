// Red Dust Open Cut: a non-symmetric 2v2 map of an outback open-cut mine, left days before the war, built from
// scratch with MapCore on All That Simmers' detail textures and gadgets, with the mine's machines, site gear and
// the outback's plants modelled and painted here.
//   gallery <template> <work>                 every model on flat red ground, to look at
//   minimap <render.png> <out.png> [size]     the map cut out of a straight-down render3d, scaled for the minimap
//   setminimap <extracted map> <png>          the map's SMF minimap from that picture (the lobby picture follows)
using System.Diagnostics;
using System.Globalization;
using MapCore.Imaging;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var clock = Stopwatch.StartNew();
switch (args[0])
{
    case "gallery":
    {
        string work = Path.GetFullPath(args[2]), root = Path.Combine(work, "gallery");
        Directory.CreateDirectory(work);
        Create.Map(args[1], root, 768, 768, -100, 400, (x, z) => 0, (y, row) => { for (int x = 0; x < row.Length; x++) row[x] = new Rgba(178, 96, 56, 255); },
            "Gallery", null, 20261040);
        var defs = Models.All();
        Features.Write(root, defs);
        var info = MapCore.MapInfo.Load(Path.Combine(root, "mapinfo.lua"));
        info.SetFromText("name", "Open Cut Gallery");
        for (int t = 0; t < 16; t++) info.RemoveStartPosition(t);
        info.SetFromText("teams.0.startPos.x", "200"); info.SetFromText("teams.0.startPos.z", "200");
        info.SetFromText("teams.1.startPos.x", "5900"); info.SetFromText("teams.1.startPos.z", "5900");
        info.Save(Path.Combine(root, "mapinfo.lua"));
        Create.Pack(root, Path.Combine(work, "gallery_stage.sd7"));
        var ops = new System.Text.StringBuilder();
        var byName = defs.ToDictionary(d => d.Name);
        void At(string name, double x, double z, double heading = 0) => ops.AppendLine($"item-add:placer:{name}:{x:0},{z:0}:{Place.Heading(heading)}");
        void Row(double z, IEnumerable<string> names, double gap = 60)
        {
            double x = 300;
            foreach (var n in names)
            {
                var (min, max) = byName[n].Box;
                At(n, x - min.X, z);
                x += max.X - min.X + gap;
            }
        }
        // The bucket wheel excavator alone; the loadout, the crusher, the transfer tower and conveyor sections beside it.
        At("oc_bucketwheel", 1200, 900);
        At("oc_loadout", 2700, 1000);
        At("oc_crusher", 3300, 900);
        At("oc_transfer", 4300, 900);
        for (int i = 0; i < 4; i++) At("oc_conveyor", 4460 + i * 128, 900);
        // The train on its track, into the loco shed at the end of the line (its open end toward the train).
        const int sections = 34;
        for (int i = 0; i < sections; i++) At("oc_track", 300 + 64 + i * 128, 1900);
        double c = 330;
        foreach (var (n, len) in new[] { ("oc_loco", Models.LocoPitch), ("oc_loco", Models.LocoPitch) }.Concat(Enumerable.Repeat(("oc_wagon_loaded", Models.WagonPitch), 4)).Concat(Enumerable.Repeat(("oc_wagon", Models.WagonPitch), 4)))
        {
            At(n, c + len / 2, 1900, 180);
            c += len;
        }
        double end = 300 + sections * 128;
        At("oc_locoshed", end - 262, 1900, 180);
        At("oc_loco", end - 262 - 40, 1900, 180);
        At("oc_buffer", end - 20, 1900, 180);
        // The machines in a row; the tanks and bigger structures; the site gear and clutter; the outback.
        Row(2700, defs.Where(d => d.Name.StartsWith("oc_truck") || d.Name.StartsWith("oc_excavator")).Select(d => d.Name)
            .Concat(["oc_watercart", "oc_grader", "oc_dozer", "oc_loader", "oc_drill", "oc_servicetruck", "oc_bus"]));
        Row(3500, ["oc_workshop", "oc_tank_fuel", "oc_tank_fuel_small", "oc_tank_water", "oc_tank_water_small", "oc_watertower", "oc_windmill", "oc_commtower", "oc_genset", "oc_magazine"], 80);
        Row(4300, ["oc_ute", "oc_lighttower", "oc_donga0", "oc_donga1", "oc_fueltank", "oc_container0", "oc_container1", "oc_container2", "oc_tyres", "oc_drums", "oc_pipes"]);
        for (int i = 0; i < 3; i++) At("oc_powerpole", 3600 + i * 160, 4300);
        Row(5000, ["oc_spinifex0", "oc_spinifex1", "oc_gum0", "oc_gum1", "oc_deadtree", "oc_saltbush", "oc_termites0", "oc_termites1", "oc_boulder0", "oc_boulder1", "oc_boulder2"], 80);
        File.WriteAllText(Path.Combine(work, "run.txt"), ops.ToString());
        foreach (var d in defs)
            Console.WriteLine($"{d.Name,-22} {d.Mesh.T.Count / 3,6} triangles  footprint {d.Footprint.X}x{d.Footprint.Z}");
        Console.WriteLine($"gallery in {clock.Elapsed.TotalSeconds:0}s");
        break;
    }
    case "build":
    {
        // build <template> <work>: the map folder, <file>_stage.sd7 and run.txt (the checks run first; it builds anyway, to look).
        string work = Path.GetFullPath(args[2]), root = Path.Combine(work, Build.File);
        Directory.CreateDirectory(work);
        var t = Terrain.BuildWithPads();
        Console.WriteLine($"heights in {clock.Elapsed.TotalSeconds:0}s, {t.H.Min():0} to {t.H.Max():0}");
        Console.WriteLine($"the pit: {t.Depth.Count(d => d > 0) * 64 / 1e6:0.0} million square units inside its rim, {t.H.Count(y => y < 0) * 64 / 1e6:0.00} million under water");
        Console.WriteLine($"the railway: the rim at z {t.RimZ:0} on the line, the shed {t.ShedNorth:0} to {t.ShedSouth:0}, road 7's crossing at {t.CrossingZ:0}");
        for (int k = 0; k < t.Ramps.Length; k++)
        {
            // Each haul ramp's length from the rim to where it meets the floor, and its grade.
            var line = t.Ramps[k];
            double run = 0;
            for (int i = 1; i < line.Count && t.RampHeights[k][i - 1] > Layout.PitFloor + 2.001; i++) run += (line[i] - line[i - 1]).Length;
            Console.WriteLine($"the {Terrain.RampNames[k]} ramp: {run:0} long to the floor, at {Math.Atan((Layout.Plain - Layout.PitFloor - 2) / run) * 180 / Math.PI:0.0} degrees, from ({line[0].X:0}, {line[0].Z:0})");
        }
        var defs = Models.All();
        var machines = Place.Machines(t);
        var works = Place.Works(t);
        var outback = Place.Outback(t, machines.Concat(works).ToList());
        var items = machines.Concat(works).Concat(outback).ToList();
        var ops = Place.Ops(t, items);
        File.WriteAllText(Path.Combine(work, "run.txt"), ops);
        Console.WriteLine($"placed in {clock.Elapsed.TotalSeconds:0}s: " + string.Join(", ", items.GroupBy(i => i.Kind).Select(g => $"{g.Key} {g.Count()}")));
        int failures = Check.Report(Build.HeightMapOf(t.H), t, ops.Split('\n'), Check.Footprints(defs), defs.ToDictionary(d => d.Name, d => d.Metal), Console.WriteLine);
        if (failures > 0) Console.WriteLine($"{failures} check(s) failed (building anyway, to look)");
        var surface = new Surface(t.H, t.N);
        var fields = new Fields(t, machines.Concat(works.Where(w => w.Kind == "vehicle")));
        var painter = new Painter(surface, t, fields);
        Create.Map(args[1], root, Layout.Squares, Layout.Squares, Layout.MinH, Layout.MaxH, (x, z) => t.H[z * t.N + x], painter.Row, Build.Smf, null, 20261104);
        Console.WriteLine($"map and texture in {clock.Elapsed.TotalSeconds:0}s");
        var maps = Path.Combine(root, "maps");
        Derive.Splat(surface, fields, t, 4096, Path.Combine(maps, $"{Build.Smf}_splat.dds"));
        Derive.Specular(surface, 4096, Path.Combine(maps, $"{Build.Smf}_specular.dds"));
        Derive.Normals(surface, 4096, Path.Combine(maps, $"{Build.Smf}_normals.dds"));
        Derive.Sky(Path.Combine(maps, $"{Build.Smf}Sky.dds"), (0.3, 0.52, 0.86), (0.88, 0.8, 0.68));
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
        var t = Terrain.BuildWithPads();
        var defs = Models.All();
        int failures = Check.Report(session.Heights, t, File.ReadAllLines(args[2]), Check.Footprints(defs), defs.ToDictionary(d => d.Name, d => d.Metal), Console.WriteLine);
        Console.WriteLine($"{failures} check(s) failed");
        return failures == 0 ? 0 : 1;
    }
    case "roads":
    {
        // roads: the road survey (where each road's ends lead; how steep, how leaning, whether in the pit or a creek).
        var t = Terrain.BuildWithPads();
        foreach (var r in Roads.Survey(t, p => Terrain.Sample(t.H, t.N, p)))
            Console.WriteLine($"{r.Name,-12} {r.Length,6:0} long, from {r.StartsAt} to {r.EndsAt}; grade up to {r.MaxGrade:0.0} deg, leans up to {r.MaxLean:0.0} deg; {r.InPit:0} in the pit, {r.AlongCreek:0} in creek beds ({r.CreekCrossings} crossings); steepest at ({r.SteepestAt.X:0}, {r.SteepestAt.Z:0}), leaning most at ({r.LeaningAt.X:0}, {r.LeaningAt.Z:0})");
        if (args.Length > 2)
        {
            // roads <x> <z>: the heights round a point, 32 apart, and the depth into the pit.
            double cx = double.Parse(args[1], CultureInfo.InvariantCulture), cz = double.Parse(args[2], CultureInfo.InvariantCulture);
            for (int dz = -4; dz <= 4; dz++)
                Console.WriteLine(string.Join(" ", Enumerable.Range(-4, 9).Select(dx => $"{Terrain.Sample(t.H, t.N, new P(cx + dx * 32, cz + dz * 32)),5:0}")) + "   depth " +
                    string.Join(" ", Enumerable.Range(-4, 9).Select(dx => $"{Terrain.Sample(t.Depth, t.N, new P(cx + dx * 32, cz + dz * 32)),4:0}")));
        }
        for (int k = 0; k < t.Ramps.Length; k++)
        {
            var legs = Roads.All();
            string near = legs.Select(l => (l.Name, D: Geo.ToPolyline(t.Ramps[k][0], l.Line, out _))).MinBy(x => x.D) is var m ? $"{m.Name} {m.D:0} away" : "";
            Console.WriteLine($"the {Terrain.RampNames[k]} ramp's top ({t.Ramps[k][0].X:0}, {t.Ramps[k][0].Z:0}): the nearest road is {near}");
        }
        break;
    }
    case "relief":
    {
        // relief <out.png>: the heights alone, shaded and coloured by height, water blue.
        var t = Terrain.BuildWithPads();
        Console.WriteLine($"heights in {clock.Elapsed.TotalSeconds:0}s, {t.H.Min():0} to {t.H.Max():0}");
        int step = 2, n = t.N, w = (n - 1) / step;
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
                (double r, double g, double b) c = y < 0 ? (60, 140, 120) : slope > 54 ? (90, 60, 60) : slope > 27 ? (200, 150, 90) : y < 200 ? (150, 150, 140) : (190, 110, 70);
                double s = 0.35 + 0.75 * light;
                img.SetPixel(px, pz, new Rgba((byte)Math.Clamp(c.r * s, 0, 255), (byte)Math.Clamp(c.g * s, 0, 255), (byte)Math.Clamp(c.b * s, 0, 255), 255));
            }
        });
        PngWriter.WriteRgba(args[1], img);
        Console.WriteLine($"relief in {clock.Elapsed.TotalSeconds:0}s");
        break;
    }
    case "minimap":
    {
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
    public const string File = "red_dust_open_cut_1.0", Smf = "RedDustOpenCut";

    public static MapCore.Heights.HeightMap HeightMapOf(float[] raw)
    {
        var smf = new MapCore.Smf.SmfFile(Layout.Squares, Layout.Squares) { MinHeight = Layout.MinH, MaxHeight = Layout.MaxH };
        for (int i = 0; i < raw.Length; i++)
            smf.Heightmap[i] = (ushort)Math.Clamp(Math.Round((raw[i] - Layout.MinH) / (Layout.MaxH - Layout.MinH) * 65535), 0, 65535);
        return MapCore.Heights.HeightMap.Load(smf, null);
    }

    /// <summary>mapinfo.lua: name and credits, metal, a hot outback day's light, dusty haze, the pit lake's green, four starts.</summary>
    public static void WriteMapInfo(string path)
    {
        var info = MapCore.MapInfo.Load(path);
        void Set(string key, string value) => info.SetFromText(key, value);
        Set("name", "Red Dust Open Cut");
        Set("shortname", "RedDustOpenCut");
        Set("version", "1.0");
        Set("author", "Daniel Kopko");
        Set("description", "A 2v2 map of an outback open-cut mine, left days before the war: a benched pit with a green lake in its sump, haul ramps down each side, a bucket wheel excavator on its terrace, trucks and excavators where the crews left them. The west holds the ROM pad and plant, the east the waste dumps and workshop. Detail textures and gadgets from All That Simmers by Nikuksis.");
        Set("maxMetal", "4");
        Set("atmosphere.skyColor", "0.42,0.6,0.86");
        Set("atmosphere.fogColor", "0.86,0.76,0.62");
        Set("atmosphere.fogStart", "0.3");
        Set("atmosphere.sunColor", "1.06,0.98,0.86");
        Set("atmosphere.cloudColor", "0.95,0.92,0.88");
        Set("atmosphere.minWind", "6");
        Set("atmosphere.maxWind", "18");
        Set("atmosphere.skyBox", $"{Smf}Sky.dds");
        Set("tidalStrength", "0");
        Set("lighting.groundAmbientColor", "0.48,0.42,0.38");
        Set("lighting.groundDiffuseColor", "1.1,1.02,0.9");
        Set("lighting.groundSpecularColor", "0.32,0.28,0.24");
        Set("lighting.unitAmbientColor", "0.52,0.48,0.44");
        Set("lighting.unitDiffuseColor", "1.05,1,0.9");
        Set("custom.fog.color", "0.86,0.76,0.62");
        Set("water.baseColor", "0.2,0.5,0.42");
        Set("water.minColor", "0.04,0.14,0.12");
        Set("water.surfaceColor", "0.44,0.72,0.6");
        Set("water.planeColor", "0.2,0.5,0.42");
        Set("water.absorb", "0.008,0.004,0.006");
        Set("water.specularColor", "0.7,0.8,0.7");
        Set("resources.specularTex", $"{Smf}_specular.dds");
        Set("resources.splatDistrTex", $"{Smf}_splat.dds");
        Set("resources.detailNormalTex", $"{Smf}_normals.dds");
        info.Document.Remove("custom.grassConfig.grassDistTGA");
        for (int t = 0; t < 16; t++)
            info.RemoveStartPosition(t);
        // Teams 0 and 1 the west (north, south), 2 and 3 the east.
        var starts = Layout.StartsA.Concat(Layout.StartsB).ToArray();
        for (int n = 0; n < starts.Length; n++)
        {
            Set($"teams.{n}.startPos.x", Math.Round(starts[n].X).ToString(CultureInfo.InvariantCulture));
            Set($"teams.{n}.startPos.z", Math.Round(starts[n].Z).ToString(CultureInfo.InvariantCulture));
        }
        info.Save(path);
    }
}
