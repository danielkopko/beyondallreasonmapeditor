using System.Diagnostics;
using MapCore.Assets;
using MapCore.Editing;
using MapCore.Imaging;
using MapCore.Rendering;
using MapCore.Structures;

namespace MapCore.Tests;

/// <summary>
/// BAR's own features, units and buildings the editor ships (src/MapEditor/BarFeatures, from tools/MapResearch
/// barfeatures): what BAR defines, with their models, so the editor offers and draws them instead of placeholders.
/// </summary>
static class BarFeatureCatalogTests
{
    static string Folder => Path.Combine(TestData.RepoRoot, "src", "MapEditor", "BarFeatures");

    static BarFeatureCatalog Load() => BarFeatureCatalog.Load(Path.Combine(Folder, BarFeatureCatalog.FileName));

    /// <summary>
    /// Every name BAR or the engine supplies that the seven maps tried so far place (collected 2026-10-16: Acidic
    /// Quarry, All That Glitters, All That Simmers, Ascendancy, Crimson Bay, FolsomDamR, Industrial Revolution),
    /// leaving out the engine's geovent (no model, in game either) and two wrecks BAR no longer defines.
    /// </summary>
    static readonly string[] UsedByMaps =
    [
        "armadvsol_dead", "armafus_dead", "armamd_dead", "armanni_dead", "armcarry_dead", "armckfus_dead", "armestor_dead", "armfus_dead",
        "armgate_dead", "armgmm_dead", "armpb_dead", "armsolar_dead", "armtarg_dead", "armuwadves_dead", "armwin_dead", "corbats_dead",
        "corpship_dead", "corpt_dead", "corroy_dead", "corssub_dead", "rocks30_def_01", "rocks30_def_02", "rocks30_def_03", "rocks30_def_04",
        "rocks30_def_06", "rocks30_def_07", "rocks30_def_08", "rocks30_def_09", "rocks30_def_10", "rocks30_def_11", "rocks30_def_12", "rocks30_def_13",
        "rocks30_def_14", "rocks30_def_15", "rocks30_def_16", "rocks30_def_17", "rocks30_def_19", "rocks30_def_20", "rocks30_map_01", "rocks30_map_02",
        "rocks30_map_03", "rocks30_map_04", "rocks30_map_06", "rocks30_map_07", "rocks30_map_08", "rocks30_map_09", "rocks30_map_10", "rocks30_map_11",
        "rocks30_map_12", "rocks30_map_13", "rocks30_map_14", "rocks30_map_15", "rocks30_map_16", "rocks30_map_17", "rocks30_map_18", "rocks30_map_19",
        "rocks30_snow_01", "rocks30_snow_02", "rocks30_snow_03", "rocks30_snow_04", "rocks30_snow_05", "rocks30_snow_06", "rocks30_snow_07", "rocks30_snow_08",
        "rocks30_snow_09", "rocks30_snow_14", "rocks30_snow_18", "rocks30_snow_19", "rocks30_snow_20", "rocks30_snow_21", "rocks30_snow_25", "rocks30_snow_26",
        "rocks30_snow_27", "rocks30_snow_28", "rocks30_snow_29", "treetype0", "treetype1", "treetype2", "treetype3", "treetype4",
        "treetype5", "treetype6", "treetype7", "treetype8", "treetype9", "treetype10", "treetype11", "treetype12",
        "treetype13", "treetype14", "treetype15",
    ];

    [Test]
    public static void TheCatalogComesFromTheUnitCatalogsCommit()
    {
        var catalog = Load();
        var units = UnitCatalog.Load(Path.Combine(TestData.RepoRoot, "src", "MapEditor", "BarUnits", UnitCatalog.FileName));
        Report.Info($"{catalog.Features.Count} features, {catalog.Features.Count(f => f.Model != null)} with models, {catalog.Origin}");
        Assert.True(catalog.Source is { Repository: "beyond-all-reason/Beyond-All-Reason" } && catalog.Source.Commit == units.Source?.Commit,
            $"BAR's repository at the unit catalog's commit ({catalog.Source?.Commit} / {units.Source?.Commit})");
        Assert.True(catalog.Features.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == catalog.Features.Count, "each name once");
        Assert.True(File.Exists(Path.Combine(Folder, "CREDITS.txt")) &&
                    File.ReadAllText(Path.Combine(Folder, "CREDITS.txt")).Contains("0 A.D.", StringComparison.Ordinal),
            "the models' authors are credited beside them");
        // Wrecks are named unit_key, as BAR's gamedata/featuredefs_post.lua names a unit's featuredefs.
        Assert.True(catalog.Find("armfus_dead") is { FromUnit: "armfus", File: var file } && file.StartsWith("units/", StringComparison.Ordinal) &&
                    catalog.Find("armfus_heap") is { FromUnit: "armfus" },
            "a unit's wreck and heap are its name with _dead and _heap");
    }

    [Test]
    public static void TheEnginesTreesAreBarsFirs()
    {
        // BAR's features/enginetrees_override.lua: treetype i takes the (i % 4 + 1)th of its four fir models.
        var catalog = Load();
        string[] firs = ["fir_tree_smallest", "fir_tree_small", "fir_tree_medium", "fir_tree_large"];
        for (int i = 0; i < 16; i++)
        {
            var tree = catalog.Find($"treetype{i}");
            Assert.True(tree?.Model == $"objects3d/{firs[i % 4]}.s3o" && tree.Author?.Contains("0 A.D.", StringComparison.Ordinal) == true &&
                        tree.File == "features/enginetrees_override.lua",
                $"treetype{i} is BAR's {firs[i % 4]} ({tree?.Model}, by {tree?.Author})");
        }
        var model = catalog.LoadModel("treetype0")!;
        Assert.True(model.Colour != null && model.Extra != null && model.CutOut && model.Notes.Count == 0,
            $"its textures are there, the branches cut out of texture 2 ({model.Mesh.Texture1}, {model.Mesh.Texture2})");
    }

    [Test]
    public static void EveryBarFeatureTheMapsUseHasItsModel()
    {
        var catalog = Load();
        var textures = new Dictionary<string, MapCore.Imaging.RgbaImage>();
        var problems = new List<string>();
        foreach (var name in UsedByMaps)
        {
            try
            {
                var model = catalog.LoadModel(name, textures);
                if (model == null)
                    problems.Add($"{name}: no model ({catalog.Find(name)?.Note ?? "not in the catalog"})");
                else if (model.Colour == null || model.Extra == null || model.Notes.Count > 0 || model.Mesh.TriangleCount == 0)
                    problems.Add($"{name}: {string.Join("; ", model.Notes)} ({model.Mesh.TriangleCount} triangles)");
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException)
            {
                problems.Add($"{name}: {ex.Message}");
            }
        }
        Assert.True(problems.Count == 0, $"all {UsedByMaps.Length} load with both textures ({string.Join(" | ", problems.Take(5))})");

        // rocks30_map_* use the map's own rock textures in BAR when the map ships them; BAR has none, so it draws
        // them as the default rocks (features/rocks30.lua), and so does the editor.
        Assert.True(catalog.Find("rocks30_map_07")?.Model == "objects3d/rocks30/rocks30_def_07.s3o", $"rocks30_map_07 is drawn as rocks30_def_07 ({catalog.Find("rocks30_map_07")?.Model})");
        Assert.True(catalog.Find("rocks30_snow_07")?.Model == "objects3d/rocks30/rocks30_snow_07.s3o", "a snowy rock has its own model");
    }

    [Test]
    public static void EveryUnitAndBuildingHasItsModel()
    {
        var catalog = Load();
        var listed = UnitCatalog.Load(Path.Combine(TestData.RepoRoot, "src", "MapEditor", "BarUnits", UnitCatalog.FileName));
        Assert.True(catalog.Units.Count == 681 && catalog.Units.Count(u => u.Building) == 302 && listed.Units.All(u => catalog.FindUnit(u.Name) != null),
            $"the unit catalog's 681 units, 302 of them buildings ({catalog.Units.Count}, {catalog.Units.Count(u => u.Building)})");
        // A building doesn't move or fly (its unit file has no speed): nano turrets are buildings, though BAR gives them a
        // movement class; commanders, bots, aircraft and ships aren't.
        foreach (var name in new[] { "armmex", "armsolar", "armllt", "armnanotc", "legnanotc", "legmex", "armvp" })
            Assert.True(catalog.FindUnit(name) is { Building: true }, $"{name} ({catalog.FindUnit(name)?.DisplayName}) is a building");
        foreach (var name in new[] { "armpw", "armcom", "armthund", "legcom", "armsub", "corak" })
            Assert.True(catalog.FindUnit(name) is { Building: false }, $"{name} ({catalog.FindUnit(name)?.DisplayName}) is a unit");
        Assert.True(catalog.FindUnit("armpw") is { Title: "armpw (Pawn)", Faction: "Armada", Author: "Kaiser" }, "a unit's name, title, faction and author");

        var clock = Stopwatch.StartNew();
        var textures = new Dictionary<string, RgbaImage>();
        var problems = new List<string>();
        int loaded = 0;
        foreach (var unit in catalog.Units)
        {
            try
            {
                var model = catalog.LoadUnitModel(unit.Name, textures);
                if (model == null)
                {
                    if (unit.Name != "armcomnew") // (its unit file names no model)
                        problems.Add($"{unit.Name}: no model");
                    continue;
                }
                loaded++;
                // (legeheatraymech_old names no texture 2 in BAR itself)
                if (model.Colour == null || (model.Extra == null && !string.IsNullOrEmpty(model.Mesh.Texture2)) || model.Notes.Count > 0 || model.Mesh.TriangleCount == 0)
                    problems.Add($"{unit.Name}: {string.Join("; ", model.Notes)} ({model.Mesh.TriangleCount} triangles)");
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException)
            {
                problems.Add($"{unit.Name}: {ex.Message}");
            }
        }
        Report.Info($"{loaded} unit models read in {clock.ElapsedMilliseconds} ms");
        Assert.True(loaded == 680 && problems.Count == 0, $"680 models load with both textures ({loaded}; {string.Join(" | ", problems.Take(5))})");
    }

    [Test]
    public static void WrecksAndHeapsHaveTheirModels()
    {
        var catalog = Load();
        var wrecks = catalog.Features.Where(f => f.FromUnit != null).ToList();
        int shipped = wrecks.Count(f => catalog.ModelOf(f.Name) != null);
        Assert.True(wrecks.Count == 1189 && shipped == wrecks.Count(f => f.Note == null), $"every wreck and heap BAR keeps has its model ({shipped} of {wrecks.Count})");
        // Every 25th, and every heap model there is (heaps share a few).
        var textures = new Dictionary<string, RgbaImage>();
        var sample = wrecks.Where((_, i) => i % 25 == 0).Concat(wrecks.Where(f => f.Name.EndsWith("_heap", StringComparison.Ordinal)).DistinctBy(f => f.Model))
            .DistinctBy(f => f.Name).Where(f => f.Note == null).ToList();
        var problems = sample.Select(f => (f.Name, Model: catalog.LoadModel(f.Name, textures)))
            .Where(x => x.Model is not { Colour: not null, Mesh.TriangleCount: > 0 }).Select(x => x.Name).ToList();
        Assert.True(problems.Count == 0, $"a sample of {sample.Count} loads, coloured ({string.Join(", ", problems.Take(5))})");
    }

    [Test]
    public static void BarsEntriesForTheBrowser()
    {
        var catalog = Load();
        var entries = catalog.AssetEntries();
        Assert.True(entries.Count == catalog.Features.Count(f => f.Note == null) + 681 && entries.All(e => e.BarOnly && !e.HasFiles) &&
                    entries.Select(e => e.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == entries.Count,
            $"one entry for each feature BAR keeps and each unit, all BAR's own, ids unique ({entries.Count})");
        AssetEntry Of(string id) => entries.Single(e => e.Id == id);
        Assert.True(Of("unit:armpw") is { Kind: AssetKind.Unit, Category: AssetCategories.Units, Description: "Pawn: Fast Infantry Bot" } &&
                    Of("building:armllt") is { Kind: AssetKind.Building, Category: AssetCategories.Buildings } &&
                    Of("gamefeature:armfus_dead") is { Kind: AssetKind.GameFeature, Category: AssetCategories.Wrecks } &&
                    Of("gamefeature:rocks30_def_07") is { Category: AssetCategories.Rocks } &&
                    Of("enginefeature:treetype3") is { Kind: AssetKind.EngineFeature, Category: AssetCategories.Trees },
            "ids, kinds and categories as the library gives name-only entries, so a name a map used is the same entry");
    }

    [Test]
    public static void PlacedUnitsAndBuildingsAreDrawnWithBarsModels()
    {
        var s = MapEditSession.Open(MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir("bar-units-3d")));
        s.Apply("item-add:building:armllt:2200,2580:1");
        s.Apply("item-add:unit:armpw:2260,2600:16384");
        using var layers = new MapRenderer(s);
        using var view = new TerrainView(layers);
        var camera = new OrbitCamera(2230, 2590, 30, 35, 400);
        var image = new RgbaImage(640, 400);
        view.Render(image, camera, new TerrainOptions { Models = true });
        var (without, _) = view.ModelsDrawn;
        view.BarFeatures = Load();
        view.Render(image, camera, new TerrainOptions { Models = true });
        var (with, _) = view.ModelsDrawn;
        Assert.True(with == without + 2, $"the placed Sentry and Pawn are drawn with BAR's models ({without} models without the catalog, {with} with it)");
    }

    [Test]
    public static void NamesBarDoesntDefineArentThere()
    {
        var catalog = Load();
        // armasp (the air repair pad) is gone from BAR; armnanotc no longer leaves a wreck. A map placing either gets
        // nothing in game, which the editor says.
        foreach (var name in new[] { "armasp_dead", "armnanotc_dead", "geovent" })
            Assert.True(catalog.Find(name) == null, $"{name} isn't one of BAR's features");
        // A feature whose model BAR lacks is listed, with why it is dropped.
        var dropped = catalog.Features.Where(f => f.Note != null).ToList();
        Assert.True(dropped.All(f => f.Model == null && f.Note!.Contains("drops it", StringComparison.Ordinal)),
            $"features BAR drops for want of a model say so ({dropped.Count}: {string.Join(", ", dropped.Take(4).Select(f => f.Name))})");
    }
}
