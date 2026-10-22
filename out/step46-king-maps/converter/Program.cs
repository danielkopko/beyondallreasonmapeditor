// KingMaps: builds a king map (out/koth-maps, K3 to K6) as a BAR map from its drawing's geometry and planet, on All
// That Simmers' detail textures, plants and gadgets. Every height and colour is made here, the same in every team's
// part; MapTool then adds the metal, vents and features. (Five Rivers, K7, has its own converter in step 44.)
//   build <geometry.json> <template folder> <work folder>   the map folder, five.._stage.sd7 and run.txt in work
//   check <geometry.json> <map folder>                      the access checks on a built map's own heights
using System.Diagnostics;
using System.Globalization;
using MapCore.Heights;
using MapCore.Smf;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var clock = Stopwatch.StartNew();
switch (args[0])
{
    case "build":
    {
        var d = new Design(args[1]);
        var cfg = Maps.For(d.Key);
        string work = Path.GetFullPath(args[3]), root = Path.Combine(work, cfg.File);
        Directory.CreateDirectory(work);
        var masks = new Masks(d);
        var t = Terrain.Build(d, cfg.Look, masks);
        masks.Stairs(t);
        Console.WriteLine($"{d.Name}: heights in {clock.Elapsed.TotalSeconds:0}s, {t.H.Min():0} to {t.H.Max():0}");
        int failures = Access.Report(Maps.HeightMapOf(d, t.H), d, t, Console.WriteLine);
        Access.Relief(t.H, t.N, Path.Combine(work, "relief.png"));
        if (failures > 0) { Console.WriteLine($"{failures} access check(s) failed: not built"); return 1; }
        var surface = new Surface(t.H, t.N);
        var groves = cfg.Look.Groves(d, t);
        var painter = new Painter(d, cfg.Look, surface, masks, groves);
        byte[]? grass = cfg.Look.Grass ? Derive.Grass(d, surface, masks, out double share) : null;
        Create.Map(args[2], root, d.Squares, d.Squares, Maps.MinHeight, Maps.MaxHeight, (x, z) => t.H[z * t.N + x], painter.Row, cfg.Smf,
            smf => smf.GrassMap = grass, cfg.MapId);
        Console.WriteLine($"map and texture in {clock.Elapsed.TotalSeconds:0}s, peak {Process.GetCurrentProcess().PeakWorkingSet64 / 1e9:0.0} GB");
        var maps = Path.Combine(root, "maps");
        Derive.Splat(d, cfg.Look, surface, masks, 4096, Path.Combine(maps, $"{cfg.Smf}_splat.dds"));
        Derive.Specular(d, surface, masks, 4096, Path.Combine(maps, $"{cfg.Smf}_specular.dds"));
        Derive.Normals(d, surface, 4096, Path.Combine(maps, $"{cfg.Smf}_normals.dds"));
        Derive.Sky(Path.Combine(maps, $"{cfg.Smf}Sky.dds"), cfg.Look.SkyTop, cfg.Look.SkyHorizon);
        Maps.WriteMapInfo(Path.Combine(root, "mapinfo.lua"), d, cfg, grass != null);
        var items = Place.Features(d, cfg.Look, surface, masks, t, groves);
        File.WriteAllText(Path.Combine(work, "run.txt"), Place.Ops(d, cfg.Assets, items));
        Console.WriteLine($"run.txt: {d.Spots.Count} spots, {d.Vents.Count} vents, {items.Count * d.Fold} features " +
                          $"({string.Join(", ", items.GroupBy(i => i.Kind).Select(g => $"{g.Key} {g.Count() * d.Fold}"))})");
        Create.Pack(root, Path.Combine(work, $"{cfg.File}_stage.sd7"));
        Console.WriteLine($"built in {clock.Elapsed.TotalSeconds:0}s");
        break;
    }
    case "check":
    {
        var d = new Design(args[1]);
        var cfg = Maps.For(d.Key);
        var t = Terrain.Build(d, cfg.Look, new Masks(d));
        var session = MapCore.Editing.MapEditSession.Open(MapCore.MapPackage.OpenFolder(args[2]));
        var h = session.Heights;
        int failures = Access.Report(h, d, t, Console.WriteLine);
        var rnd = new Random(1);
        var diffs = new List<double>();
        while (diffs.Count < 200000)
        {
            var p = new P(rnd.NextDouble() * d.MapSize, rnd.NextDouble() * d.MapSize);
            var q = d.Turn(p, d.Step);
            if (!d.InPlay(p, 60) || !d.InPlay(q, 60)) continue;
            diffs.Add(Math.Abs(h.HeightAt(p.X, p.Z) - h.HeightAt(q.X, q.Z)));
        }
        diffs.Sort();
        Console.WriteLine($"Parts alike (200000 points, each against the same point a part's turn round): median {diffs[diffs.Count / 2]:0.00}, " +
                          $"95% within {diffs[(int)(diffs.Count * 0.95)]:0.00}, 99% within {diffs[(int)(diffs.Count * 0.99)]:0.0}");
        Console.WriteLine($"{failures} access check(s) failed");
        return failures == 0 ? 0 : 1;
    }
    default:
        throw new ArgumentException("unknown command " + args[0]);
}
return 0;

sealed record MapConfig(string Name, string Short, string File, string Smf, string Description, Look Look, string[] Assets, int MapId);

static class Maps
{
    public const float MinHeight = -100, MaxHeight = 760;

    static readonly string[] HeathAssets = ["ad0_fir-09afd514", "birchtree-967cee52"];
    static readonly string[] ForgeAssets =
    [
        "crateblue-f00d1b45", "crateorange-734ef546", "cratered-74d6eff6", "crateyellow-947e5b77", "cranetruck1-5987a6a4",
        "minitruck1-c2e2201a", "minitruck2-83876ebe",
    ];

    public static MapConfig For(string key) => key switch
    {
        "star_fort" => new("Star Fort", "StarFort", "star_fort_1.0", "StarFort",
            "Five kings on the raised bastions of one star fort in a sand sea, twenty players: ramps in at the bastions' points, bots-only zigzags up their flanks, peasants in the realms outside. Plants, detail textures and gadgets from All That Simmers by Nikuksis.",
            new Dune(), [], 20261026),
        "edge_keeps" => new("Edge Keeps", "EdgeKeeps", "edge_keeps_1.0", "EdgeKeeps",
            "Four kings on cliff keeps at the middle of each edge of a highland moor, sixteen players: a ramp to each keep, bots-only zigzags up its sides, peasants in the corners, a loch with an isle in the middle. Detail textures and gadgets from All That Simmers by Nikuksis; firs and birches from Crimson Bay.",
            new Heath(), HeathAssets, 20261027),
        "corner_fortresses" => new("Corner Fortresses", "CornerFortresses", "corner_fortresses_1.0", "CornerFortresses",
            "Four kings in raised corner fortresses of an industrial wasteland, sixteen players: ramps through the gates, bots-only zigzags up the ramparts, all twelve peasants round the market in the middle, acid lakes between. Detail textures and gadgets from All That Simmers by Nikuksis; props from Industrial Revolution.",
            new Forge(), ForgeAssets, 20261028),
        _ => throw new ArgumentException($"no settings for {key}"),
    };

    public static HeightMap HeightMapOf(Design d, float[] raw)
    {
        var smf = new SmfFile(d.Squares, d.Squares) { MinHeight = MinHeight, MaxHeight = MaxHeight };
        for (int i = 0; i < raw.Length; i++)
            smf.Heightmap[i] = (ushort)Math.Clamp(Math.Round((raw[i] - MinHeight) / (MaxHeight - MinHeight) * 65535), 0, 65535);
        return HeightMap.Load(smf, null);
    }

    /// <summary>mapinfo.lua: name and credits, metal, the planet's colours and weather, the look's light and water,
    /// the new detail maps and sky, the starts (each team's king, then its peasants 1-3; teams clockwise from A).</summary>
    public static void WriteMapInfo(string path, Design d, MapConfig cfg, bool grass)
    {
        var info = MapCore.MapInfo.Load(path);
        void Set(string key, string value) => info.SetFromText(key, value);
        Set("name", cfg.Name);
        Set("shortname", cfg.Short);
        Set("version", "1.0");
        Set("author", "Daniel Kopko");
        Set("description", cfg.Description);
        Set("maxMetal", "4");
        foreach (var (k, v) in d.Planet.MapInfo) Set(k, v);
        foreach (var (k, v) in cfg.Look.MapInfo) Set(k, v);
        Set("atmosphere.skyBox", $"{cfg.Smf}Sky.dds");
        Set("atmosphere.cloudColor", "0.9,0.88,0.84");
        Set("resources.specularTex", $"{cfg.Smf}_specular.dds");
        Set("resources.splatDistrTex", $"{cfg.Smf}_splat.dds");
        Set("resources.detailNormalTex", $"{cfg.Smf}_normals.dds");
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
