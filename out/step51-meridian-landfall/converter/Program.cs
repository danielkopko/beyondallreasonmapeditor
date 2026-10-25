// Meridian Landfall: an 8v8 map, Starship Meridian (the same ship, its own files) landed on an asteroid. Built with
// MapCore on All That Simmers' gadgets (its detail textures unused); every feature modelled and painted here.
//   build <template> <work>                   the map folder, <file>_stage.sd7 and run.txt (the checks run first; it
//                                             builds anyway, to look)
//   gallery <template> <work>                 the asteroid's models on flat ground, with each model's footprint
//   check <extracted map> <run.txt>           the checks on the built map's own heights
//   minimap <render.png> <out.png> <x0> <z0> <x1> <z1> [size]
//                                             the map cut out of a straight-down render3d at the given pixels, scaled
//                                             (unevenly: the map is 32 × 36) to the minimap's square, the void starred
//   setminimap <extracted map> <png>          the map's SMF minimap from that picture (the lobby picture follows)
using System.Diagnostics;
using System.Globalization;
using MapCore.Imaging;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var clock = Stopwatch.StartNew();
switch (args[0])
{
    case "build":
    {
        string work = Path.GetFullPath(args[2]), root = Path.Combine(work, LandBuild.File);
        Directory.CreateDirectory(work);
        var land = Asteroid.Build();
        Console.WriteLine($"heights in {clock.Elapsed.TotalSeconds:0}s, {land.H.Min():0} to {land.H.Max():0}; {land.VoidV.Count(v => v < 0) * 64 / 1e6:0.0} M square units of void, " +
            $"{land.Kind.Count(k => k == Asteroid.InCrater) * 64 / 1e6:0.0} M in craters");
        var defs = LandBuild.Models();
        var items = LandPlace.Items(land, LandBuild.Asymmetric(defs));
        var ops = LandPlace.Ops(items);
        File.WriteAllText(Path.Combine(work, "run.txt"), ops);
        Console.WriteLine($"placed in {clock.Elapsed.TotalSeconds:0}s: " + string.Join(", ", items.GroupBy(i => i.Kind).Select(g => $"{g.Key} {g.Count()}")));
        int failures = LandCheck.Report(LandBuild.HeightMapOf(land.H), land, ops.Split('\n'), Check.Footprints(defs), Console.WriteLine);
        if (failures > 0) Console.WriteLine($"{failures} check(s) failed (building anyway, to look)");
        Console.WriteLine($"checked in {clock.Elapsed.TotalSeconds:0}s");
        var surface = new Surface(land.H, World.NX, World.NZ);
        var painter = new LandPainter(surface, land);
        Create.Map(args[1], root, World.SquaresX, World.SquaresZ, World.MinH, World.MaxH, (x, z) => land.H[z * World.NX + x], painter.Row, LandBuild.Smf, null, 20261125);
        Console.WriteLine($"map and texture in {clock.Elapsed.TotalSeconds:0}s");
        var maps = Path.Combine(root, "maps");
        Derive.Splat(1024, Path.Combine(maps, $"{LandBuild.Smf}_splat.dds"));
        LandDerive.Specular(surface, land, 4096, 4096, Path.Combine(maps, $"{LandBuild.Smf}_specular.dds"));
        LandDerive.Normals(surface, 4096, 4096, Path.Combine(maps, $"{LandBuild.Smf}_normals.dds"));
        Derive.Sky(Path.Combine(maps, $"{LandBuild.Smf}Sky.dds"));
        Features.Write(root, defs);
        LandBuild.WriteMapInfo(Path.Combine(root, "mapinfo.lua"));
        Create.Pack(root, Path.Combine(work, $"{LandBuild.File}_stage.sd7"));
        Console.WriteLine($"built in {clock.Elapsed.TotalSeconds:0}s");
        break;
    }
    case "gallery":
    {
        // gallery <template> <work>: the asteroid's models (and a few of the ship's for scale) on flat ground.
        string work = Path.GetFullPath(args[2]), root = Path.Combine(work, "gallery");
        Directory.CreateDirectory(work);
        Create.Map(args[1], root, 512, 512, 0, 400, (x, z) => 100, (y, row) => { for (int x = 0; x < row.Length; x++) row[x] = new Rgba((byte)(((x / 64 + y / 64) & 1) == 0 ? 120 : 132), 130, 134, 255); },
            "Gallery", null, 20261141);
        var defs = LandBuild.Models();
        Features.Write(root, defs);
        var info = MapCore.MapInfo.Load(Path.Combine(root, "mapinfo.lua"));
        info.SetFromText("name", "Landfall Gallery");
        for (int n = 0; n < 16; n++) info.RemoveStartPosition(n);
        info.SetFromText("teams.0.startPos.x", "100"); info.SetFromText("teams.0.startPos.z", "100");
        info.SetFromText("teams.1.startPos.x", "4000"); info.SetFromText("teams.1.startPos.z", "4000");
        info.Save(Path.Combine(root, "mapinfo.lua"));
        Create.Pack(root, Path.Combine(work, "gallery_stage.sd7"));
        var byName = defs.ToDictionary(d => d.Name);
        var ops = new System.Text.StringBuilder();
        void Row(double z, IEnumerable<string> names, double gap = 60)
        {
            double x = 300;
            foreach (var n in names)
            {
                var (min, max) = byName[n].Box;
                ops.AppendLine($"item-add:placer:{n}:{x - min.X:0},{z:0}:0");
                x += max.X - min.X + gap;
            }
        }
        Row(600, ["lf_boulder_s1", "lf_boulder_s2", "lf_boulder_m1", "lf_boulder_m2", "lf_boulder_l", "lf_boulder_xl"]);
        Row(1100, ["lf_slab1", "lf_slab2", "lf_shard1", "lf_shard2", "lf_strut", "sm_tug", "sm_container0"], 90);
        File.WriteAllText(Path.Combine(work, "run.txt"), ops.ToString());
        foreach (var d in defs.Where(d => d.Name.StartsWith("lf_", StringComparison.Ordinal)))
            Console.WriteLine($"{d.Name,-18} {d.Mesh.T.Count / 3,6} triangles  footprint {d.Footprint.X}x{d.Footprint.Z}  {(d.Invisible ? "invisible" : LandBuild.Symmetric(d.Mesh) ? "symmetric" : "NOT symmetric (twin)")}");
        break;
    }
    case "check":
    {
        var session = MapCore.Editing.MapEditSession.Open(MapCore.MapPackage.OpenFolder(args[1]));
        var land = Asteroid.Build();
        int failures = LandCheck.Report(session.Heights, land, File.ReadAllLines(args[2]), Check.Footprints(LandBuild.Models()), Console.WriteLine);
        Console.WriteLine($"{failures} check(s) failed");
        return failures == 0 ? 0 : 1;
    }
    case "minimap":
    {
        var img = PngReader.Read(args[1]).ToRgba();
        int x0 = int.Parse(args[3]), z0 = int.Parse(args[4]), x1 = int.Parse(args[5]), z1 = int.Parse(args[6]);
        int size = args.Length > 7 ? int.Parse(args[7], CultureInfo.InvariantCulture) : 1024;
        var cut = img.Crop(PixelRect.FromEdges(x0, z0, x1, z1));
        // The void: the renderer's backdrop (voidGround isn't drawn) becomes a starfield.
        var bg = MapCore.Rendering.MapRenderer.Background;
        for (int y = 0; y < cut.Height; y++)
            for (int x = 0; x < cut.Width; x++)
            {
                var c = cut.GetPixel(x, y);
                if (c.R != bg.R || c.G != bg.G || c.B != bg.B) continue;
                uint hsh = (uint)(x * 73856093) ^ (uint)(y * 19349663);
                hsh ^= hsh >> 13; hsh *= 0x5bd1e995; hsh ^= hsh >> 15;
                double roll = (hsh & 0xFFFF) / 65535.0;
                byte v = (byte)(roll > 0.9985 ? 230 : roll > 0.994 ? 120 : 0);
                cut.SetPixel(x, y, new Rgba((byte)(6 + v), (byte)(8 + v), (byte)(18 + v), 255));
            }
        PngWriter.WriteRgba(args[2], cut.Resample(size, size));
        Console.WriteLine($"map at ({x0}, {z0}) to ({x1}, {z1}) of {img.Width}x{img.Height}, written {size}x{size}");
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

static class LandBuild
{
    public const string File = "meridian_landfall_1.0", Smf = "MeridianLandfall";

    /// <summary>The features: Meridian's models and the asteroid's, and a mirror-image twin (name_m) of any that isn't symmetric about its length.</summary>
    public static List<FeatureDef> Models()
    {
        var defs = global::Models.All().Concat(Rocks.All()).ToList();
        var twins = defs.Where(d => !d.Invisible && !Symmetric(d.Mesh)).Select(d => d with { Name = d.Name + "_m", Mesh = d.Mesh.Mirrored() }).ToList();
        return [.. defs, .. twins];
    }

    public static HashSet<string> Asymmetric(List<FeatureDef> defs)
    {
        var names = defs.Select(d => d.Name).ToHashSet();
        return names.Where(n => names.Contains(n + "_m")).ToHashSet();
    }

    /// <summary>Whether a mesh's surface is its own mirror image across z = 0 (Meridian's voxel test: 4-unit cells, 99% matched).</summary>
    public static bool Symmetric(Mesh m)
    {
        var cells = new HashSet<(int, int, int)>();
        for (int i = 0; i < m.T.Count; i += 3)
        {
            var a = m.V[m.T[i]].Position; var b = m.V[m.T[i + 1]].Position; var c = m.V[m.T[i + 2]].Position;
            int n = Math.Clamp((int)(Math.Max((b - a).Length(), (c - a).Length()) / 2) + 1, 1, 200);
            for (int u = 0; u <= n; u++)
                for (int v = 0; u + v <= n; v++)
                {
                    var p = a + (b - a) * (u / (float)n) + (c - a) * (v / (float)n);
                    cells.Add(((int)Math.Floor(p.X / 4), (int)Math.Floor(p.Y / 4), (int)Math.Floor(p.Z / 4)));
                }
        }
        int missing = cells.Count(q =>
        {
            int mz = -q.Item3 - 1;
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                        if (cells.Contains((q.Item1 + dx, q.Item2 + dy, mz + dz))) return false;
            return true;
        });
        return missing <= cells.Count / 100;
    }

    public static MapCore.Heights.HeightMap HeightMapOf(float[] raw)
    {
        var smf = new MapCore.Smf.SmfFile(World.SquaresX, World.SquaresZ) { MinHeight = World.MinH, MaxHeight = World.MaxH };
        for (int i = 0; i < raw.Length; i++)
            smf.Heightmap[i] = (ushort)Math.Clamp(Math.Round((raw[i] - World.MinH) / (World.MaxH - World.MinH) * 65535), 0, 65535);
        return MapCore.Heights.HeightMap.Load(smf, null);
    }

    /// <summary>mapinfo.lua: name and credits, metal, the void (voidGround, a starfield sky, no fog, no wind or tide), a near
    /// star's hard light (Meridian's), sixteen starts (port 0-7, starboard 8-15).</summary>
    public static void WriteMapInfo(string path)
    {
        var info = MapCore.MapInfo.Load(path);
        void Set(string key, string value) => info.SetFromText(key, value);
        Set("name", "Meridian Landfall");
        Set("shortname", "MeridianLandfall");
        Set("version", "1.0");
        Set("author", "Daniel Kopko");
        Set("description", "An 8v8 map: Starship Meridian has come down on an asteroid. Port against starboard, inside the ship (its hangars, crew deck, cargo hold and power rooms, and on the centreline the bridge, command room, reactor, shield generator and engine room) and outside on the cratered moonrock round it, where void pits blasted through the rock let nothing but aircraft across. No wind. Gadgets from All That Simmers by Nikuksis.");
        Set("maxMetal", "4");
        // The void: the ground with a transparent texture isn't drawn (the pits and the asteroid's edge); the sky is a starfield.
        info.Document.SetLiteral("voidGround", "true");
        info.Document.SetLiteral("voidWater", "false");
        Set("atmosphere.skyColor", "0.02,0.025,0.05");
        Set("atmosphere.fogColor", "0.02,0.025,0.05");
        Set("atmosphere.fogStart", "0.99");
        Set("atmosphere.sunColor", "1.0,0.97,0.92");
        Set("atmosphere.cloudColor", "0.1,0.1,0.14");
        Set("atmosphere.minWind", "0");
        Set("atmosphere.maxWind", "0");
        Set("atmosphere.skyBox", $"{Smf}Sky.dds");
        Set("tidalStrength", "0");
        Set("lighting.sunDir", "-0.5,0.8,-0.35");
        Set("lighting.groundAmbientColor", "0.32,0.34,0.4");
        Set("lighting.groundDiffuseColor", "1.05,1.0,0.95");
        Set("lighting.groundSpecularColor", "0.45,0.45,0.5");
        Set("lighting.unitAmbientColor", "0.36,0.37,0.43");
        Set("lighting.unitDiffuseColor", "1.0,0.98,0.94");
        Set("custom.fog.color", "0.02,0.025,0.05");
        Set("resources.specularTex", $"{Smf}_specular.dds");
        Set("resources.splatDistrTex", $"{Smf}_splat.dds");
        Set("resources.detailNormalTex", $"{Smf}_normals.dds");
        info.Document.Remove("custom.grassConfig.grassDistTGA");
        for (int t = 0; t < 16; t++)
            info.RemoveStartPosition(t);
        for (int n = 0; n < World.Starts.Length; n++)
        {
            Set($"teams.{n}.startPos.x", Math.Round(World.Starts[n].At.X).ToString(CultureInfo.InvariantCulture));
            Set($"teams.{n}.startPos.z", Math.Round(World.Starts[n].At.Z).ToString(CultureInfo.InvariantCulture));
        }
        info.Save(path);
    }
}
