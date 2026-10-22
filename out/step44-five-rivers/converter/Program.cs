// FiveRivers: builds K7 Five Rivers (out/koth-maps/k7_five_rivers.svg) as a BAR map, 28 x 28, on All That Simmers'
// detail textures, sky, plants and gadgets. Every height and colour is made here, the same in all five fifths of the
// map; MapTool then adds the metal, vents, features and detail maps.
//   probe <template folder> <out folder>       a flat 28 x 28 map, to see MapCore and MapTool cope with the size
//   heights <geometry.json> <out folder>       the heights only: relief.png and the access checks
using System.Diagnostics;
using System.Globalization;
using MapCore.Heights;
using MapCore.Imaging;
using MapCore.Smf;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var clock = Stopwatch.StartNew();
switch (args[0])
{
    case "probe":
    {
        var root = Path.Combine(args[2], "probe_map");
        Create.Map(args[1], root, 1792, 1792, -100, 700, (x, z) => 220, (y, row) =>
        {
            for (int x = 0; x < row.Length; x++)
                row[x] = new Rgba((byte)(60 + (x >> 8) % 2 * 30), (byte)(140 + (y >> 8) % 2 * 30), 130, 255);
        }, "probe");
        Console.WriteLine($"created in {clock.Elapsed.TotalSeconds:0}s, peak {Process.GetCurrentProcess().PeakWorkingSet64 / 1e9:0.0} GB");
        Create.Pack(root, Path.Combine(args[2], "probe_1.sd7"));
        Console.WriteLine($"packed in {clock.Elapsed.TotalSeconds:0}s");
        break;
    }
    case "heights":
    {
        Directory.CreateDirectory(args[2]);
        var (h, _) = Build.Heights(args[1]);
        Console.WriteLine($"heights in {clock.Elapsed.TotalSeconds:0}s: {h.Heights.Min():0} to {h.Heights.Max():0}");
        Access.Relief(h, Path.Combine(args[2], "relief.png"));
        int failures = Access.Report(h, Build.Design!, Console.WriteLine);
        Console.WriteLine($"{failures} access check(s) failed; {clock.Elapsed.TotalSeconds:0}s");
        return failures == 0 ? 0 : 1;
    }
    case "build":
    {
        // build <geometry.json> <template folder> <work folder>: the map folder, its archive for MapTool, run.txt
        string work = Path.GetFullPath(args[3]), root = Path.Combine(work, "five_rivers");
        Directory.CreateDirectory(work);
        var (h, raw) = Build.Heights(args[1]);
        var d = Build.Design!;
        int failures = Access.Report(h, d, Console.WriteLine);
        if (failures > 0) { Console.WriteLine($"{failures} access check(s) failed: not built"); return 1; }
        Access.Relief(h, Path.Combine(work, "relief.png"));
        var surface = new Surface(raw, Build.Squares + 1);
        var masks = new Masks(d, Design.MapSize);
        Paint.Prepare(d, Design.MapSize);
        Console.WriteLine($"masks in {clock.Elapsed.TotalSeconds:0}s");
        var grass = Derive.Grass(surface, masks, Build.Squares / 4, Build.Squares / 4, out double grassShare);
        Create.Map(args[2], root, Build.Squares, Build.Squares, Build.MinHeight, Build.MaxHeight,
            (x, z) => raw[z * (Build.Squares + 1) + x], (y, row) => Paint.Row(d, surface, masks, y, row), "FiveRivers",
            smf => smf.GrassMap = grass);
        Console.WriteLine($"map and texture in {clock.Elapsed.TotalSeconds:0}s, peak {Process.GetCurrentProcess().PeakWorkingSet64 / 1e9:0.0} GB; grass on {grassShare:0}%");
        var maps = Path.Combine(root, "maps");
        Derive.Splat(surface, masks, Design.MapSize, 4096, Path.Combine(maps, "FiveRivers_splat.dds"));
        Derive.Specular(surface, masks, Design.MapSize, 4096, Path.Combine(maps, "FiveRivers_specular.dds"));
        Derive.Normals(surface, Design.MapSize, 4096, Path.Combine(maps, "FiveRivers_normals.dds"));
        Derive.NightSky(Path.Combine(maps, "BloomNightSky.dds"));
        Console.WriteLine($"detail maps and sky in {clock.Elapsed.TotalSeconds:0}s");
        Build.WriteMapInfo(Path.Combine(root, "mapinfo.lua"), d);
        var items = Place.Vegetation(d, surface, masks);
        File.WriteAllText(Path.Combine(work, "run.txt"), Place.Ops(d, items));
        Console.WriteLine($"run.txt: {d.Spots.Count} spots, {d.Vents.Count} vents, {items.Count * Design.Teams} items " +
                          $"({string.Join(", ", items.GroupBy(i => i.Kind).Select(g => $"{g.Key} {g.Count() * Design.Teams}"))})");
        Create.Pack(root, Path.Combine(work, "five_rivers_stage.sd7"));
        Console.WriteLine($"built in {clock.Elapsed.TotalSeconds:0}s");
        break;
    }
    case "sky":
        Derive.NightSky(args[1]);
        break;
    case "check":
    {
        // check <geometry.json> <map folder>: the access checks on the built map's own heights, and how alike the fifths are
        var d = Build.Design = new Design(args[1]);
        Terrain.Use(d);
        Terrain.Plan(d);
        var session = MapCore.Editing.MapEditSession.Open(MapCore.MapPackage.OpenFolder(args[2]));
        var h = session.Heights;
        int failures = Access.Report(h, d, Console.WriteLine);
        var rnd = new Random(1);
        var diffs = new List<double>();
        while (diffs.Count < 200000)
        {
            var p = new P(rnd.NextDouble() * Design.MapSize, rnd.NextDouble() * Design.MapSize);
            if ((p - Design.Centre).Length > d.PlayRadius - 60) continue;
            var q = Design.Turn(p, 72);
            diffs.Add(Math.Abs(h.HeightAt(p.X, p.Z) - h.HeightAt(q.X, q.Z)));
        }
        diffs.Sort();
        Console.WriteLine($"Fifths alike (200000 points, each against the same point a fifth of a turn round): median {diffs[diffs.Count / 2]:0.00}, " +
                          $"95% within {diffs[(int)(diffs.Count * 0.95)]:0.00}, 99% within {diffs[(int)(diffs.Count * 0.99)]:0.0} " +
                          $"(the rest on cliff edges, where a fraction of a square shifts the edge)");
        Console.WriteLine($"{failures} access check(s) failed");
        return failures == 0 ? 0 : 1;
    }
    default:
        throw new ArgumentException("unknown command " + args[0]);
}
return 0;

static class Build
{
    public const int Squares = 1792;
    public const float MinHeight = -100, MaxHeight = 760;
    public static Design? Design;

    /// <summary>The design read, the ramps and switchbacks planned, every vertex's height, as a height map.</summary>
    public static (HeightMap Heights, float[] Raw) Heights(string geometry)
    {
        Design = new Design(geometry);
        for (int i = 0; i < Design.Halls.Count; i++)
            if (Design.Halls[i].Team != i) throw new InvalidDataException("the halls aren't in team order");
        Terrain.Use(Design);
        Terrain.Plan(Design);
        var raw = Terrain.Build(Squares + 1, Squares + 1);
        var smf = new SmfFile(Squares, Squares) { MinHeight = MinHeight, MaxHeight = MaxHeight };
        for (int i = 0; i < raw.Length; i++)
            smf.Heightmap[i] = (ushort)Math.Clamp(Math.Round((raw[i] - MinHeight) / (MaxHeight - MinHeight) * 65535), 0, 65535);
        return (HeightMap.Load(smf, null), raw);
    }

    /// <summary>
    /// mapinfo.lua for Five Rivers on the Bloom planet: name and credits, metal, the night's colours and weather
    /// (planets.py), glowing water, the new detail maps and sky, and the twenty starts (team order: A's king, A's
    /// peasants 1-3, then B's, and so on).
    /// </summary>
    public static void WriteMapInfo(string path, Design d)
    {
        var info = MapCore.MapInfo.Load(path);
        void Set(string key, string value) => info.SetFromText(key, value);
        Set("name", "Five Rivers");
        Set("shortname", "FiveRivers");
        Set("version", "1.0");
        Set("author", "Daniel Kopko");
        Set("description", "Five kings and their peasants, twenty players: each king holds a cliff-top hall, the rivers divide the realms, the isle in the lake is everyone's. Plants, detail textures and gadgets from All That Simmers by Nikuksis.");
        Set("maxMetal", "4");
        Set("tidalStrength", "20");
        Set("atmosphere.minWind", "3");
        Set("atmosphere.maxWind", "12");
        Set("atmosphere.skyBox", "BloomNightSky.dds");
        Set("atmosphere.skyColor", "0.12,0.1,0.28");
        Set("atmosphere.fogColor", "0.2,0.3,0.4");
        Set("atmosphere.sunColor", "0.75,0.85,1");
        Set("atmosphere.cloudColor", "0.3,0.36,0.5");
        Set("custom.fog.color", "0.2,0.3,0.4");
        // Moonlit but bright enough to play: cool ambient, a blue-white sun.
        Set("lighting.groundAmbientColor", "0.42,0.5,0.6");
        Set("lighting.groundDiffuseColor", "0.95,1.02,1.12");
        Set("lighting.groundSpecularColor", "0.4,0.55,0.6");
        Set("lighting.unitAmbientColor", "0.5,0.56,0.64");
        Set("lighting.unitDiffuseColor", "0.9,0.96,1.05");
        Set("water.baseColor", "0.1,0.45,0.75");
        Set("water.minColor", "0.01,0.04,0.1");
        Set("water.surfaceColor", "0.58,0.72,0.88");
        Set("water.absorb", "0.012,0.004,0.002");
        Set("water.specularColor", "0.6,0.85,1");
        Set("resources.specularTex", "FiveRivers_specular.dds");
        Set("resources.splatDistrTex", "FiveRivers_splat.dds");
        Set("resources.detailNormalTex", "FiveRivers_normals.dds");
        info.Document.Remove("custom.grassConfig.grassDistTGA");
        for (int t = 0; t < 16; t++)
            info.RemoveStartPosition(t);
        int n = 0;
        foreach (var s in d.Starts.OrderBy(s => s.Team).ThenBy(s => s.N))
        {
            Set($"teams.{n}.startPos.x", Math.Round(s.At.X).ToString(CultureInfo.InvariantCulture));
            Set($"teams.{n}.startPos.z", Math.Round(s.At.Z).ToString(CultureInfo.InvariantCulture));
            n++;
        }
        info.Save(path);
    }
}
