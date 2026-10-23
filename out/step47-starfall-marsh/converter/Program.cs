// Starfall Marsh: a 2v2 map of a giant ship crashed into a jungle marsh, built from scratch with MapCore on All That
// Simmers' detail textures, plants and gadgets, with the ship and its wreckage modelled and painted here.
//   slice <template folder> <work folder>   a flat test map with a test model and the atlas on a board
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using MapCore.Imaging;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var clock = Stopwatch.StartNew();
switch (args[0])
{
    case "slice":
    {
        string work = Path.GetFullPath(args[2]), root = Path.Combine(work, "slice");
        Directory.CreateDirectory(work);
        Create.Map(args[1], root, 512, 512, 0, 400, (x, z) => 100, (y, row) => { for (int x = 0; x < row.Length; x++) row[x] = new Rgba(96, 112, 64, 255); },
            "Slice", null, 20261030);
        Features.Write(root, [Slice.TestModel(), Slice.Board()]);
        var info = MapCore.MapInfo.Load(Path.Combine(root, "mapinfo.lua"));
        info.SetFromText("name", "Starfall Slice");
        info.SetFromText("version", "0.1");
        for (int t = 0; t < 16; t++) info.RemoveStartPosition(t);
        info.SetFromText("teams.0.startPos.x", "500"); info.SetFromText("teams.0.startPos.z", "500");
        info.SetFromText("teams.1.startPos.x", "3500"); info.SetFromText("teams.1.startPos.z", "3500");
        info.Save(Path.Combine(root, "mapinfo.lua"));
        Create.Pack(root, Path.Combine(work, "slice_stage.sd7"));
        File.WriteAllText(Path.Combine(work, "run.txt"),
            "item-add:placer:sf_test:2048,2048:0\nitem-add:placer:sf_test:2600,2048:90\nitem-add:placer:sf_board:2048,1500:0\n");
        Console.WriteLine($"slice in {clock.Elapsed.TotalSeconds:0}s");
        break;
    }
    case "build":
    {
        string work = Path.GetFullPath(args[2]), root = Path.Combine(work, Build.File);
        Directory.CreateDirectory(work);
        var mir = new Mirror(Layout.Size);
        var t = Terrain.Build(mir);
        Console.WriteLine($"heights in {clock.Elapsed.TotalSeconds:0}s, {t.H.Min():0} to {t.H.Max():0}");
        var ship = Place.Ship(out var shipDefs);
        var wreckDefs = Models.Wreckage();
        var wreck = Place.Wreckage(t, ship, wreckDefs);
        var jungle = Place.Jungle(t, ship, wreck, mir);
        var reeds = new[] { Models.Reeds(0), Models.Reeds(1) };
        var allDefs = shipDefs.Concat(wreckDefs.Values).Concat(wreckDefs.Values.Select(Models.MirrorOf)).Concat(reeds).ToList();
        var ops = Place.Ops(mir, ship, wreck.Concat(jungle).ToList());
        File.WriteAllText(Path.Combine(work, "run.txt"), ops);
        Console.WriteLine($"placed in {clock.Elapsed.TotalSeconds:0}s: the ship ({ship.Count}), " +
                          string.Join(", ", wreck.Concat(jungle).GroupBy(i => i.Kind).Select(g => $"{g.Key} {g.Count() * 2}")));
        int failures = Check.Report(Build.HeightMapOf(t.H), ops.Split('\n'), Check.Footprints(allDefs), Console.WriteLine);
        if (failures > 0) Console.WriteLine($"{failures} check(s) failed (building anyway, to look)");
        var surface = new Surface(t.H, t.N);
        var fields = new Fields(ship, wreck, mir);
        var painter = new Painter(surface, t, fields, mir);
        byte[] grass = Derive.Grass(surface, fields, mir, out double share);
        Create.Map(args[1], root, Layout.Squares, Layout.Squares, Layout.MinH, Layout.MaxH, (x, z) => t.H[z * t.N + x], painter.Row, Build.Smf,
            smf => smf.GrassMap = grass, 20261101);
        Console.WriteLine($"map and texture in {clock.Elapsed.TotalSeconds:0}s, grass on {share:0}%");
        var maps = Path.Combine(root, "maps");
        Derive.Splat(surface, fields, 4096, Path.Combine(maps, $"{Build.Smf}_splat.dds"));
        Derive.Specular(surface, fields, 4096, Path.Combine(maps, $"{Build.Smf}_specular.dds"));
        Derive.Normals(surface, 4096, Path.Combine(maps, $"{Build.Smf}_normals.dds"));
        Derive.Sky(Path.Combine(maps, $"{Build.Smf}Sky.dds"), (0.36, 0.52, 0.64), (0.74, 0.8, 0.7));
        Features.Write(root, allDefs);
        JungleDefs.Write(root);
        Build.WriteMapInfo(Path.Combine(root, "mapinfo.lua"));
        Create.Pack(root, Path.Combine(work, $"{Build.File}_stage.sd7"));
        Console.WriteLine($"built in {clock.Elapsed.TotalSeconds:0}s");
        break;
    }
    case "check":
    {
        // check <map folder, extracted> <run.txt>: the checks on the built map's own heights.
        var session = MapCore.Editing.MapEditSession.Open(MapCore.MapPackage.OpenFolder(args[1]));
        var ship = Place.Ship(out var shipDefs);
        var wreckDefs = Models.Wreckage();
        var defs = shipDefs.Concat(wreckDefs.Values).Concat(wreckDefs.Values.Select(Models.MirrorOf)).Concat([Models.Reeds(0), Models.Reeds(1)]);
        int failures = Check.Report(session.Heights, File.ReadAllLines(args[2]), Check.Footprints(defs), Console.WriteLine);
        Console.WriteLine($"{failures} check(s) failed");
        return failures == 0 ? 0 : 1;
    }
    case "minimap":
    {
        // minimap <render3d top-down png> <out png> [size]: the map cut out of a straight-down 3D render (its border
        // is the renderer's background) and scaled to the minimap's size, for MapTool's import:minimap and the lobby.
        var img = MapCore.Imaging.PngReader.Read(args[1]).ToRgba();
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
        var cut = img.Crop(MapCore.Imaging.PixelRect.FromEdges(left, top, right + 1, bottom + 1)).Resample(size, size);
        MapCore.Imaging.PngWriter.WriteRgba(args[2], cut);
        Console.WriteLine($"map at ({left}, {top}) to ({right}, {bottom}) of {img.Width}x{img.Height}, written {size}x{size}");
        break;
    }
    case "setminimap":
    {
        // setminimap <extracted map folder> <png>: the map's SMF minimap made from the picture (the lobby's picture
        // follows it: MapTool install draws it from the minimap).
        var smfPath = Directory.GetFiles(Path.Combine(args[1], "maps"), "*.smf").Single();
        var smf = MapCore.Smf.SmfFile.Load(File.ReadAllBytes(smfPath));
        MapCore.Smf.Minimap.Rebuild(smf, MapCore.Imaging.PngReader.Read(args[2]).ToRgba());
        smf.Save(smfPath);
        Console.WriteLine($"minimap of {Path.GetFileName(smfPath)} set from {Path.GetFileName(args[2])}");
        break;
    }
    case "gallery":
    {
        // Every model on flat ground: the ship where it lies, the wreckage in a row south of it.
        string work = Path.GetFullPath(args[2]), root = Path.Combine(work, "gallery");
        Directory.CreateDirectory(work);
        Create.Map(args[1], root, Layout.Squares, Layout.Squares, -100, 620, (x, z) => 0, (y, row) => { for (int x = 0; x < row.Length; x++) row[x] = new Rgba(70, 92, 48, 255); },
            "Gallery", null, 20261031);
        var ship = new[] { Models.Engines(), Models.AftHull(), Models.AftBreak(), Models.ForeBreak(), Models.ForeHull() };
        var prow = Models.Prow();
        var wreck = Models.Wreckage().Values.ToList();
        var reeds = new[] { Models.Reeds(0), Models.Reeds(1) };
        Features.Write(root, ship.Select(s => s.Def).Append(prow).Concat(wreck).Concat(reeds));
        var info = MapCore.MapInfo.Load(Path.Combine(root, "mapinfo.lua"));
        info.SetFromText("name", "Starfall Gallery");
        for (int t = 0; t < 16; t++) info.RemoveStartPosition(t);
        info.SetFromText("teams.0.startPos.x", "500"); info.SetFromText("teams.0.startPos.z", "500");
        info.SetFromText("teams.1.startPos.x", "7500"); info.SetFromText("teams.1.startPos.z", "7500");
        info.Save(Path.Combine(root, "mapinfo.lua"));
        Create.Pack(root, Path.Combine(work, "gallery_stage.sd7"));
        var ops = new System.Text.StringBuilder();
        foreach (var s in ship) ops.AppendLine($"item-add:placer:{s.Def.Name}:{s.At.X:0},{s.At.Z:0}:0");
        ops.AppendLine($"item-add:placer:{prow.Name}:5600,{Layout.Axis:0}:0");
        for (int i = 0; i < wreck.Count; i++) ops.AppendLine($"item-add:placer:{wreck[i].Name}:{700 + i * 420},5600:0");
        for (int i = 0; i < reeds.Length; i++) ops.AppendLine($"item-add:placer:{reeds[i].Name}:{700 + i * 200},6200:0");
        File.WriteAllText(Path.Combine(work, "run.txt"), ops.ToString());
        foreach (var d in ship.Select(s => s.Def).Append(prow).Concat(wreck).Concat(reeds))
            Console.WriteLine($"{d.Name,-24} {d.Mesh.T.Count / 3,6} triangles  footprint {d.Footprint.X}x{d.Footprint.Z}");
        Console.WriteLine($"gallery in {clock.Elapsed.TotalSeconds:0}s");
        break;
    }
    default:
        throw new ArgumentException("unknown command " + args[0]);
}
return 0;

static class Slice
{
    /// <summary>A box 200 long (x), 120 across (z), 120 high: the arrow on its +x end and top points along +s; a crimson fin on its +z side.</summary>
    public static FeatureDef TestModel()
    {
        var m = new Mesh { Overgrown = false };
        float x0 = -100, x1 = 100, z0 = -60, z1 = 60, y0 = -10, y1 = 120;
        m.Quad(new(x1, y1, z0), new(x1, y1, z1), new(x1, y0, z1), new(x1, y0, z0), Vector3.UnitX, Region.Test, stretch: true);
        m.Quad(new(x0, y1, z1), new(x0, y1, z0), new(x0, y0, z0), new(x0, y0, z1), -Vector3.UnitX, Region.Hull);
        m.Quad(new(x0, y1, z1), new(x1, y1, z1), new(x1, y0, z1), new(x0, y0, z1), Vector3.UnitZ, Region.Hull);
        m.Quad(new(x1, y1, z0), new(x0, y1, z0), new(x0, y0, z0), new(x1, y0, z0), -Vector3.UnitZ, Region.Hull);
        m.Quad(new(x0, y1, z0), new(x1, y1, z0), new(x1, y1, z1), new(x0, y1, z1), Vector3.UnitY, Region.Test, stretch: true);
        m.Box(new(-40, -10, 60), new(40, 90, 80), Region.Accent, Region.Trim);
        return new FeatureDef("sf_test", "Test block", Features.Centred(m), true, 0);
    }

    /// <summary>The 16 atlas cells on a wall facing south (+z), 4 by 4, each 64 square, row 0 at the top, column 0 at the west.</summary>
    public static FeatureDef Board()
    {
        var m = new Mesh { Overgrown = false };
        for (int k = 0; k < 16; k++)
        {
            int col = k % 4, row = k / 4;
            float xa = -128 + col * 64, ya = 280 - row * 64;
            m.Quad(new(xa, ya, 8), new(xa + 60, ya, 8), new(xa + 60, ya - 60, 8), new(xa, ya - 60, 8), Vector3.UnitZ, (Region)k, stretch: true);
        }
        m.Box(new(-132, -10, -8), new(128, 284, 7), Region.Burnt);
        return new FeatureDef("sf_board", "Atlas board", Features.Centred(m), true, 0);
    }
}

static class Build
{
    public const string File = "starfall_marsh_1.0", Smf = "StarfallMarsh";

    public static MapCore.Heights.HeightMap HeightMapOf(float[] raw)
    {
        var smf = new MapCore.Smf.SmfFile(Layout.Squares, Layout.Squares) { MinHeight = Layout.MinH, MaxHeight = Layout.MaxH };
        for (int i = 0; i < raw.Length; i++)
            smf.Heightmap[i] = (ushort)Math.Clamp(Math.Round((raw[i] - Layout.MinH) / (Layout.MaxH - Layout.MinH) * 65535), 0, 65535);
        return MapCore.Heights.HeightMap.Load(smf, null);
    }

    /// <summary>mapinfo.lua: name and credits, metal, a humid jungle's light, fog and murky water, the new maps, four starts.</summary>
    public static void WriteMapInfo(string path)
    {
        var info = MapCore.MapInfo.Load(path);
        void Set(string key, string value) => info.SetFromText(key, value);
        Set("name", "Starfall Marsh");
        Set("shortname", "StarfallMarsh");
        Set("version", "1.0");
        Set("author", "Daniel Kopko");
        Set("description", "A 2v2 map: a giant warship crashed into a jungle marsh centuries ago. Its engines and broken hull lie in the furrow it ploughed, its nose buried in an overgrown mound of the dirt it pushed up. Three ways across: the marsh past the engines, the breach where it broke, and over or round the mound. Plants, detail textures and gadgets from All That Simmers by Nikuksis.");
        Set("maxMetal", "4");
        Set("atmosphere.skyColor", "0.42,0.56,0.62");
        Set("atmosphere.fogColor", "0.6,0.66,0.55");
        Set("atmosphere.fogStart", "0.25");
        Set("atmosphere.sunColor", "1,0.94,0.8");
        Set("atmosphere.cloudColor", "0.86,0.9,0.84");
        Set("atmosphere.minWind", "4");
        Set("atmosphere.maxWind", "12");
        Set("atmosphere.skyBox", $"{Smf}Sky.dds");
        Set("tidalStrength", "6");
        Set("lighting.groundAmbientColor", "0.44,0.48,0.42");
        Set("lighting.groundDiffuseColor", "1.02,1,0.9");
        Set("lighting.groundSpecularColor", "0.3,0.3,0.26");
        Set("lighting.unitAmbientColor", "0.5,0.53,0.48");
        Set("lighting.unitDiffuseColor", "1,0.97,0.88");
        Set("custom.fog.color", "0.6,0.66,0.55");
        Set("water.baseColor", "0.16,0.22,0.12");
        Set("water.minColor", "0.03,0.05,0.02");
        Set("water.surfaceColor", "0.42,0.52,0.34");
        Set("water.planeColor", "0.16,0.22,0.12");
        Set("water.absorb", "0.03,0.025,0.04");
        Set("water.specularColor", "0.6,0.66,0.5");
        Set("resources.specularTex", $"{Smf}_specular.dds");
        Set("resources.splatDistrTex", $"{Smf}_splat.dds");
        Set("resources.detailNormalTex", $"{Smf}_normals.dds");
        info.Document.Remove("custom.grassConfig.grassDistTGA");
        for (int t = 0; t < 16; t++)
            info.RemoveStartPosition(t);
        // Teams 0 and 1 the north (west, east), 2 and 3 the south.
        var starts = Layout.Starts.Concat(Layout.Starts.Select(s => new P(s.X, Layout.Size - s.Z))).ToArray();
        for (int n = 0; n < starts.Length; n++)
        {
            Set($"teams.{n}.startPos.x", Math.Round(starts[n].X).ToString(CultureInfo.InvariantCulture));
            Set($"teams.{n}.startPos.z", Math.Round(starts[n].Z).ToString(CultureInfo.InvariantCulture));
        }
        info.Save(path);
    }
}
