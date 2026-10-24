// Starship Meridian: a 4v4 map that is one whole starship, port against starboard, built from scratch with MapCore on
// All That Simmers' gadgets (its detail textures unused), with every feature modelled and painted here.
//   build <template> <work>                   the map folder, <file>_stage.sd7 and run.txt (the checks run first; it
//                                             builds anyway, to look)
//   check <extracted map> <run.txt>           the checks on the built map's own heights
//   minimap <render.png> <out.png> <x0> <z0> <x1> <z1> [size]
//                                             the map cut out of a straight-down render3d at the given pixels, scaled
//                                             (unevenly: the map is 24 × 28) to the minimap's square, space starred
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
        string work = Path.GetFullPath(args[2]), root = Path.Combine(work, Build.File);
        Directory.CreateDirectory(work);
        var t = Terrain.Build();
        Console.WriteLine($"heights in {clock.Elapsed.TotalSeconds:0}s, {t.H.Min():0} to {t.H.Max():0}; " +
            $"{t.Floor.Count(f => f) * 64 / 1e6:0.0} M square units of floor, {t.Cell.Count(c => c == Terrain.Space) * 64 / 1e6:0.0} M of space");
        var defs = Build.Models();
        var items = Build.Items(t, defs);
        var ops = Place.Ops(items);
        File.WriteAllText(Path.Combine(work, "run.txt"), ops);
        Console.WriteLine($"placed in {clock.Elapsed.TotalSeconds:0}s: " + string.Join(", ", items.GroupBy(i => i.Kind).Select(g => $"{g.Key} {g.Count()}")));
        int failures = Check.Report(Build.HeightMapOf(t.H), t, ops.Split('\n'), Check.Footprints(defs), Console.WriteLine);
        if (failures > 0) Console.WriteLine($"{failures} check(s) failed (building anyway, to look)");
        var surface = new Surface(t.H, t.NX, t.NZ);
        var painter = new Painter(surface, t);
        Create.Map(args[1], root, Layout.SquaresX, Layout.SquaresZ, Layout.MinH, Layout.MaxH, (x, z) => t.H[z * t.NX + x], painter.Row, Build.Smf, null, 20261124);
        Console.WriteLine($"map and texture in {clock.Elapsed.TotalSeconds:0}s");
        var maps = Path.Combine(root, "maps");
        Derive.Splat(1024, Path.Combine(maps, $"{Build.Smf}_splat.dds"));
        Derive.Specular(surface, t, 4096, 4096, Path.Combine(maps, $"{Build.Smf}_specular.dds"));
        Derive.Normals(surface, 4096, 4096, Path.Combine(maps, $"{Build.Smf}_normals.dds"));
        Derive.Sky(Path.Combine(maps, $"{Build.Smf}Sky.dds"));
        if (defs.Count > 0) Features.Write(root, defs);
        Build.WriteMapInfo(Path.Combine(root, "mapinfo.lua"));
        Create.Pack(root, Path.Combine(work, $"{Build.File}_stage.sd7"));
        Console.WriteLine($"built in {clock.Elapsed.TotalSeconds:0}s");
        break;
    }
    case "gallery":
    {
        // gallery <template> <work>: every model on flat deck, in rows by kind, to look at; each model's triangles,
        // footprint and whether it's symmetric about its length (else it gets a mirror-image twin).
        string work = Path.GetFullPath(args[2]), root = Path.Combine(work, "gallery");
        Directory.CreateDirectory(work);
        Create.Map(args[1], root, 512, 512, 0, 400, (x, z) => 100, (y, row) => { for (int x = 0; x < row.Length; x++) row[x] = new Rgba((byte)(((x / 64 + y / 64) & 1) == 0 ? 100 : 112), 106, 114, 255); },
            "Gallery", null, 20261140);
        var defs = Build.Models();
        Features.Write(root, defs);
        var info = MapCore.MapInfo.Load(Path.Combine(root, "mapinfo.lua"));
        info.SetFromText("name", "Meridian Gallery");
        for (int n = 0; n < 16; n++) info.RemoveStartPosition(n);
        info.SetFromText("teams.0.startPos.x", "100"); info.SetFromText("teams.0.startPos.z", "100");
        info.SetFromText("teams.1.startPos.x", "4000"); info.SetFromText("teams.1.startPos.z", "4000");
        info.Save(Path.Combine(root, "mapinfo.lua"));
        Create.Pack(root, Path.Combine(work, "gallery_stage.sd7"));
        var byName = defs.ToDictionary(d => d.Name);
        var ops = new System.Text.StringBuilder();
        void Row(double z, IEnumerable<string> names, double gap = 50)
        {
            double x = 200;
            foreach (var n in names)
            {
                var (min, max) = byName[n].Box;
                ops.AppendLine($"item-add:placer:{n}:{x - min.X:0},{z:0}:16384");
                x += max.X - min.X + gap;
            }
        }
        Row(500, ["sm_dropship", "sm_fighter", "sm_shuttle", "sm_tug", "sm_fuelcart", "sm_tower"], 80);
        Row(1300, ["sm_reactor", "sm_shield", "sm_drivecore", "sm_coolant"], 100);
        Row(2100, ["sm_console", "sm_chair", "sm_holotable", "sm_sensordome", "sm_capacitor", "sm_transformer", "sm_pipes", "sm_fueltank"]);
        Row(2700, ["sm_bunks", "sm_messtable", "sm_lockers", "sm_medpod", "sm_planter", "sm_weaponrack", "sm_munitions", "sm_missilecart", "sm_crate"]);
        Row(3200, ["sm_container0", "sm_container1", "sm_loader", "sm_pallet", "sm_repairgantry", "sm_bench", "sm_turret", "sm_antenna", "sm_dish"]);
        ops.AppendLine("item-add:placer:sm_viewport:2000,3700:16384");
        ops.AppendLine("item-add:placer:sm_enginebell:3200,1300:0");
        File.WriteAllText(Path.Combine(work, "run.txt"), ops.ToString());
        foreach (var d in defs)
            Console.WriteLine($"{d.Name,-22} {d.Mesh.T.Count / 3,6} triangles  footprint {d.Footprint.X}x{d.Footprint.Z}  {(Build.Symmetric(d.Mesh) ? "symmetric" : "NOT symmetric")}");
        break;
    }
    case "check":
    {
        var session = MapCore.Editing.MapEditSession.Open(MapCore.MapPackage.OpenFolder(args[1]));
        var t = Terrain.Build();
        int failures = Check.Report(session.Heights, t, File.ReadAllLines(args[2]), Check.Footprints(Build.Models()), Console.WriteLine);
        Console.WriteLine($"{failures} check(s) failed");
        return failures == 0 ? 0 : 1;
    }
    case "minimap":
    {
        var img = PngReader.Read(args[1]).ToRgba();
        int x0 = int.Parse(args[3]), z0 = int.Parse(args[4]), x1 = int.Parse(args[5]), z1 = int.Parse(args[6]);
        int size = args.Length > 7 ? int.Parse(args[7], CultureInfo.InvariantCulture) : 1024;
        var cut = img.Crop(PixelRect.FromEdges(x0, z0, x1, z1));
        // Space: the renderer's backdrop (voidGround isn't drawn) becomes a starfield.
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

static class Build
{
    public const string File = "starship_meridian_1.0", Smf = "StarshipMeridian";

    /// <summary>The features: every model, and a mirror-image twin (name_m) of any that isn't symmetric about its length.</summary>
    public static List<FeatureDef> Models()
    {
        var defs = global::Models.All();
        var twins = defs.Where(d => !Symmetric(d.Mesh)).Select(d => d with { Name = d.Name + "_m", Mesh = d.Mesh.Mirrored() }).ToList();
        return [.. defs, .. twins];
    }

    /// <summary>
    /// Whether a mesh's surface is its own mirror image across z = 0: its triangles sampled about every 2 units into
    /// 4-unit cells, and nearly every cell (99%) with a filled cell at or next to its mirrored place. (Comparing the
    /// vertices would fail a symmetric model whose mirrored faces are split into triangles the other way.)
    /// </summary>
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

    /// <summary>The features placed: the port half's and the centreline's, then every port feature's mirror image (its twin where the model isn't symmetric).</summary>
    public static List<Place.Item> Items(Terrain.Result t, List<FeatureDef> defs)
    {
        var names = defs.Select(d => d.Name).ToHashSet();
        return Place.Ship(names.Where(n => names.Contains(n + "_m")).ToHashSet());
    }

    public static MapCore.Heights.HeightMap HeightMapOf(float[] raw)
    {
        var smf = new MapCore.Smf.SmfFile(Layout.SquaresX, Layout.SquaresZ) { MinHeight = Layout.MinH, MaxHeight = Layout.MaxH };
        for (int i = 0; i < raw.Length; i++)
            smf.Heightmap[i] = (ushort)Math.Clamp(Math.Round((raw[i] - Layout.MinH) / (Layout.MaxH - Layout.MinH) * 65535), 0, 65535);
        return MapCore.Heights.HeightMap.Load(smf, null);
    }

    /// <summary>mapinfo.lua: name and credits, metal, space (voidGround, a starfield sky, no fog, no wind or tide), a near
    /// star's hard light, eight starts (teams 0-3 port, 4-7 starboard).</summary>
    public static void WriteMapInfo(string path)
    {
        var info = MapCore.MapInfo.Load(path);
        void Set(string key, string value) => info.SetFromText(key, value);
        Set("name", "Starship Meridian");
        Set("shortname", "StarshipMeridian");
        Set("version", "1.0");
        Set("author", "Daniel Kopko");
        Set("description", "A 4v4 map that is one whole starship adrift in space, port against starboard: hangars, a crew deck, a cargo hold, power rooms on each side, and on the centreline the bridge, command room, reactor, shield generator and engine room to fight over. No wind in space. Gadgets from All That Simmers by Nikuksis.");
        Set("maxMetal", "4");
        // Space: the ground with a transparent texture isn't drawn (the hull's surroundings), and the sky is a starfield.
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
        for (int n = 0; n < Layout.Starts.Length; n++)
        {
            Set($"teams.{n}.startPos.x", Math.Round(Layout.Starts[n].At.X).ToString(CultureInfo.InvariantCulture));
            Set($"teams.{n}.startPos.z", Math.Round(Layout.Starts[n].At.Z).ToString(CultureInfo.InvariantCulture));
        }
        info.Save(path);
    }
}
