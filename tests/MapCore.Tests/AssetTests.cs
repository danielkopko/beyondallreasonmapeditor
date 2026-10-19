using System.Text;
using MapCore.Assets;
using MapCore.Editing;
using MapCore.Items;

namespace MapCore.Tests;

static class AssetTests
{
    static byte[] Text(string s) => Encoding.UTF8.GetBytes(s);

    static string FeatureLua(string name, string obj, string extra = "") => $$"""
        local featureDef = {
            name = "{{name}}",
            object = "{{obj}}",
            footprintX = 2, footprintZ = 2,
            {{extra}}
        }
        return lowerkeys({[featureDef.name] = featureDef})
        """;

    /// <summary>
    /// A second map unlike Acidic Quarry in its assets: its own agorm_rock1 (different content: a variant), a pine (a
    /// tree by BAR's rule), a house, a wreck that leaves a heap (a chain of definitions), a file that isn't Lua and one
    /// that defines nothing; and a rocks1a.tga of its own, which clashes with Acidic Quarry's.
    /// </summary>
    static string MapB(string dir) => SyntheticMap.Build(dir, "synthetic_b.sdz", files:
    [
        ("features/agorm_rock1.lua", Text(FeatureLua("agorm_rock1", "rock1.s3o", "metal = 20, category = \"rocks\","))),
        ("objects3d/rock1.s3o", S3oModel.Make("rocks1a.tga", "rocks1b.tga")),
        ("unittextures/rocks1a.tga", [1, 2, 3, 4, 5]),
        ("unittextures/rocks1b.tga", [6, 7, 8]),
        ("features/pine_tall.lua", Text(FeatureLua("pine_tall", "pine_tall.s3o", "energy = 50,"))),
        ("objects3d/pine_tall.s3o", S3oModel.Make("pine_tall_color.tga", "bar_shared_other.dds")),
        ("unittextures/pine_tall_color.tga", [9, 9, 9]),
        ("features/house1.lua", Text(FeatureLua("house1", "house1.s3o", "description = \"small house\","))),
        ("objects3d/house1.s3o", S3oModel.Make("house1.tga", "")),
        ("unittextures/house1.tga", [4, 4]),
        ("features/tank_dead.lua", Text(FeatureLua("tank_dead", "tank_dead.s3o", "featuredead = \"tank_heap\", metal = 100,"))),
        ("features/tank_heap.lua", Text(FeatureLua("tank_heap", "tank_heap.s3o", "metal = 50,"))),
        ("objects3d/tank_dead.s3o", S3oModel.Make("tankwreck.tga", "")),
        ("objects3d/tank_heap.s3o", S3oModel.Make("tankwreck.tga", "")),
        ("unittextures/tankwreck.tga", [7, 7]),
        ("features/junk.lua", Text("<!DOCTYPE html><html>not lua")),
        ("features/settings.lua", Text("return { size = 3 }")),
    ]);

    static MapEditSession Open(string archive, string name, AssetLibrary? library = null)
    {
        var s = MapEditSession.Open(MapPackage.Open(archive, TestData.NewWorkDir(name)));
        s.Assets = library;
        return s;
    }

    static CollectReport Collect(AssetLibrary library, MapPackage package) =>
        library.Collect(package.Root, package.LoadSmf(), package.LoadMapInfo(), FeaturePlacerConfig.Load(package.Root));

    static string Listing(string root) => string.Join("\n", Directory.EnumerateFileSystemEntries(root, "*", FileSystemUtil.AllEntries)
        .Select(f => FileSystemUtil.EntryPath(root, f) + (File.Exists(f) ? ":" + new FileInfo(f).Length : "/")).Order(StringComparer.Ordinal));

    [Test]
    public static void AcidicQuarrysRockModelsNameTexturesThatAreThere()
    {
        var root = TestData.AcidicQuarry.Root;
        foreach (var model in Directory.EnumerateFiles(Path.Combine(root, "objects3d"), "*.s3o"))
        {
            var textures = S3oModel.ReadTextures(File.ReadAllBytes(model));
            Assert.True(textures is (string, string), $"{Path.GetFileName(model)} is an S3O naming two textures");
            var (t1, t2) = textures!.Value;
            Report.Info($"{Path.GetFileName(model)}: {t1}, {t2}");
            foreach (var t in new[] { t1!, t2! })
                Assert.True(Directory.EnumerateFiles(Path.Combine(root, "unittextures")).Any(f => Path.GetFileName(f).Equals(t, StringComparison.OrdinalIgnoreCase)),
                    $"{Path.GetFileName(model)}'s texture {t} is in unittextures/");
        }
    }

    [Test]
    public static void BarsOwnRulesSortTheCategories()
    {
        (string Name, AssetKind Kind, string? Category, string? Description, double Metal, double Energy, string Expected)[] cases =
        [
            ("pine_tall", AssetKind.MapFeature, null, null, 0, 0, AssetCategories.Trees),
            ("ad0_bush_1", AssetKind.MapFeature, null, null, 0, 0, AssetCategories.Trees),
            ("bigtree3", AssetKind.MapFeature, null, null, 0, 0, AssetCategories.Trees),
            ("treetype7", AssetKind.EngineFeature, null, null, 0, 0, AssetCategories.Trees),
            ("street_lamp", AssetKind.MapFeature, null, null, 0, 0, AssetCategories.Buildings),
            ("armcom_dead", AssetKind.MapFeature, null, null, 500, 0, AssetCategories.Wrecks),
            ("geovent", AssetKind.EngineFeature, null, null, 0, 0, AssetCategories.Vents),
            ("agorm_rock1", AssetKind.MapFeature, "rocks", null, 10, 0, AssetCategories.Rocks),
            ("thing", AssetKind.MapFeature, null, "a blue crystal", 0, 0, AssetCategories.Rocks),
            ("mystery", AssetKind.MapFeature, null, null, 0, 40, AssetCategories.Trees),
            ("lump", AssetKind.MapFeature, null, null, 15, 0, AssetCategories.Rocks),
            ("armwin", AssetKind.Building, null, null, 0, 0, AssetCategories.Buildings),
            ("armpw", AssetKind.Unit, null, null, 0, 0, AssetCategories.Units),
            ("blob", AssetKind.MapFeature, null, null, 0, 0, AssetCategories.Other),
            // Industrial Revolution's crates and trucks say "arm_corpses": copied from a wreck, not wrecks themselves.
            ("crateblue", AssetKind.MapFeature, "arm_corpses", "a blue crate", 0, 500, AssetCategories.Props),
            ("cranetruck1", AssetKind.MapFeature, "arm_corpses", "a crane truck", 0, 500, AssetCategories.Props),
            ("crateyellow", AssetKind.MapFeature, "arm_corpses", "a strange yellow box", 0, 500, AssetCategories.Props),
            ("pipeslong1", AssetKind.MapFeature, "arm_corpses", "a long pipe", 0, 500, AssetCategories.Buildings),
            ("smothdist", AssetKind.MapFeature, "Industrial", "Powerhouse", 150, 500, AssetCategories.Buildings),
            ("cycas1", AssetKind.MapFeature, "bush Lathan's Ferns", "Ferns and Stones", 0, 100, AssetCategories.Trees),
            ("kapok1", AssetKind.MapFeature, "Vegetation Vegetation Trees", "Kapok Tree", 0, 300, AssetCategories.Trees),
            ("forest_floor", AssetKind.MapFeature, null, null, 0, 0, AssetCategories.Other), // "ore" isn't in "forest"
        ];
        var wrong = cases.Where(c => AssetCategories.Classify(c.Name, c.Kind, c.Category, c.Description, c.Metal, c.Energy) != c.Expected)
            .Select(c => $"{c.Name}: {AssetCategories.Classify(c.Name, c.Kind, c.Category, c.Description, c.Metal, c.Energy)}, not {c.Expected}").ToList();
        Assert.Empty(wrong, "categories");
    }

    /// <summary>
    /// Acidic Quarry gives five rocks (each its definition, model and the textures the model names), skips the web page
    /// saved as features/agorm_rocks.lua, and lists the engine's trees and vent by name; collecting it again adds
    /// nothing, and the library reads back from disk the same.
    /// </summary>
    [Test]
    public static void CollectingTheSameMapAgainAddsNothing()
    {
        var library = new AssetLibrary(TestData.NewWorkDir("assets-aq"));
        var first = Collect(library, TestData.AcidicQuarry);
        Report.Info(first.ToString());
        foreach (var note in first.Notes)
            Report.Info("note: " + note);
        var rocks = library.Entries.Where(e => e.Kind == AssetKind.MapFeature).OrderBy(e => e.Name).ToList();
        Assert.Equal("agorm_rock1,agorm_rock2,agorm_rock3,agorm_rock4,agorm_rock5", string.Join(",", rocks.Select(r => r.Name)), "the map's own features");
        Assert.True(rocks.All(r => r.Category == AssetCategories.Rocks), "all of them rocks");
        var rock1 = rocks[0];
        var textures = S3oModel.ReadTextures(File.ReadAllBytes(Path.Combine(TestData.AcidicQuarry.Root, "objects3d", "rock1.s3o")))!.Value;
        Assert.True(rock1.Files.Select(f => f.Path).Contains("features/agorm_rock1.lua") && rock1.Files.Select(f => f.Path).Contains("objects3d/rock1.s3o") &&
                    rock1.Files.Any(f => f.Path.Equals("unittextures/" + textures.Texture1, StringComparison.OrdinalIgnoreCase)),
            $"agorm_rock1 brings its definition, model and texture: {string.Join(", ", rock1.Files.Select(f => f.Path))}");
        Assert.True(!rock1.Files.Any(f => f.Path.EndsWith("rocks1n.tga", StringComparison.OrdinalIgnoreCase)), "no file the definition and model don't name (BAR derives no normal map)");
        Assert.True(first.Notes.Any(n => n.Contains("agorm_rocks.lua isn't Lua", StringComparison.Ordinal)), "the saved web page is skipped with a note");
        Assert.True(library.Entries.Where(e => e.Kind == AssetKind.EngineFeature && e.Name.StartsWith("TreeType", StringComparison.OrdinalIgnoreCase)).All(e => e.Category == AssetCategories.Trees) &&
                    library.Entries.Any(e => e.Kind == AssetKind.EngineFeature && e.Category == AssetCategories.Vents), "engine trees and the vent, by name");
        Assert.True(File.Exists(library.FilePath(rock1, rock1.Files[0])), "the files are kept in the library");

        var again = Collect(library, TestData.AcidicQuarry);
        Assert.True(again.Added == 0 && again.Variants == 0 && again.NewNames == 0 && again.AlreadyThere == 5, $"again: {again}");
        var reread = new AssetLibrary(library.Root);
        Assert.Equal(string.Join(",", library.Entries.Select(e => e.Id)), string.Join(",", reread.Entries.Select(e => e.Id)), "the library reads back from disk");
        Assert.True(reread.Entries.All(e => e.SeenIn.Count == 1 && e.SeenIn[0].Map == "AcidicQuarry"), "each seen once, in Acidic Quarry");
    }

    [Test]
    public static void ASecondMapBringsNewAssetsAndVariants()
    {
        var library = new AssetLibrary(TestData.NewWorkDir("assets-two"));
        Collect(library, TestData.AcidicQuarry);
        var b = MapPackage.Open(MapB(TestData.NewWorkDir("assets-b")), TestData.NewWorkDir("assets-b-open"));
        var report = Collect(library, b);
        Report.Info(report.ToString());
        Assert.True(report.Added == 4 && report.Variants == 1, $"pine, house, the wreck and its heap new; agorm_rock1 a variant: {report}");
        var rock1s = library.Entries.Where(e => e.Name == "agorm_rock1").ToList();
        Assert.True(rock1s.Count == 2 && rock1s.All(e => e.Variant), "both agorm_rock1s kept, each marked a variant");
        AssetEntry Named(string n) => library.Entries.Single(e => e.Name == n);
        Assert.Equal(AssetCategories.Trees, Named("pine_tall").Category, "pine_tall (BAR's tree rule)");
        Assert.Equal(AssetCategories.Buildings, Named("house1").Category, "house1 (its description)");
        Assert.Equal(AssetCategories.Wrecks, Named("tank_dead").Category, "tank_dead (BAR's wreck rule)");
        Assert.True(Named("tank_dead").Files.Any(f => f.Path == "features/tank_heap.lua") && Named("tank_dead").Files.Any(f => f.Path == "objects3d/tank_heap.s3o"),
            "the wreck brings the heap it leaves");
        Assert.True(Named("pine_tall").Notes.Any(n => n.Contains("bar_shared_other.dds", StringComparison.Ordinal)), "a texture the map lacks is noted (BAR may have it)");
        Assert.True(report.Notes.Any(n => n.Contains("junk.lua isn't Lua", StringComparison.Ordinal)) && report.Notes.Any(n => n.Contains("settings.lua defines no feature", StringComparison.Ordinal)),
            "files that aren't definitions are skipped with notes");
        Assert.True(Named("pine_tall").SeenIn.Single().Map == "Synthetic Test", "where it came from");
    }

    /// <summary>
    /// A definition file that builds its features in a loop (as most of BAR's newer maps do) is one asset with all
    /// of them, each with its own model; the names it makes aren't taken for BAR's, and the Items list knows them.
    /// </summary>
    [Test]
    public static void GeneratedFeaturesBecomeOneAssetWithManyFeatures()
    {
        var archive = SyntheticMap.Build(TestData.NewWorkDir("assets-gen"), "synthetic_gen.sdz", files:
        [
            ("features/palms.lua", Text("""
                local base = { description = "Palm Tree", category = "Vegetation", energy = 250,
                  customparams = { normaltex = "unittextures/palm_normal.tga" } }
                local palms = {}
                for i = 1, 3 do
                  local def = {}
                  for k, v in pairs(base) do def[k] = v end
                  def.object = "palm_" .. i .. ".s3o"
                  palms["palm_" .. i] = def
                end
                return palms
                """)),
            ("objects3d/palm_1.s3o", S3oModel.Make("palm.tga", "palm_other.tga")),
            ("objects3d/palm_2.s3o", S3oModel.Make("palm.tga", "palm_other.tga")),
            ("objects3d/palm_3.s3o", S3oModel.Make("palm.tga", "palm_other.tga")),
            ("unittextures/palm.tga", [1, 1, 1]),
            ("unittextures/palm_other.tga", [2, 2, 2]),
            ("unittextures/palm_normal.tga", [3, 3, 3]),
        ]);
        var package = MapPackage.Open(archive, TestData.NewWorkDir("assets-gen-open"));
        var library = new AssetLibrary(TestData.NewWorkDir("assets-gen-library"));
        var report = Collect(library, package);
        var palms = library.Entries.Single(e => e.HasFiles);
        Assert.True(report.Added == 1 && palms.Name == "palms" && palms.Source == "features/palms.lua" && palms.Category == AssetCategories.Trees &&
                    palms.ParseVersion == AssetLibrary.CurrentParse, $"one asset, named after its file, a tree: {report}");
        Assert.Equal("palm_1:objects3d/palm_1.s3o,palm_2:objects3d/palm_2.s3o,palm_3:objects3d/palm_3.s3o",
            string.Join(",", palms.Features.Select(f => $"{f.Name}:{f.Model}")), "its three features, each with its model");
        Assert.True(palms.Files.Any(f => f.Path == "unittextures/palm_normal.tga") && palms.Files.Count == 7, "the models, their textures and the normal map the loop names");
        Assert.True(!library.Entries.Any(e => !e.HasFiles && e.Name.StartsWith("palm_", StringComparison.Ordinal)), "none of its names is taken for BAR's");
        var (definitions, _) = FeatureCatalog.Load(package.Root, package.LoadSmf());
        Assert.True(definitions.Count(d => d.Name.StartsWith("palm_", StringComparison.Ordinal) && d.ModelFound == true) == 3, "Items › Place knows all three");
    }

    /// <summary>
    /// An asset goes into another map whole: its files land at their paths, its feature can be placed, and it all
    /// survives saving and reopening; undo leaves the package exactly as it was, folders included.
    /// </summary>
    [Test]
    public static void AnAssetGoesIntoAnotherMap()
    {
        var library = new AssetLibrary(TestData.NewWorkDir("assets-add"));
        Collect(library, TestData.AcidicQuarry);
        var rock3 = library.Entries.Single(e => e.Name == "agorm_rock3");
        var c = Open(SyntheticMap.Build(TestData.NewWorkDir("assets-c")), "assets-c-open", library);
        string before = Listing(c.Package.Root);
        Assert.True(!c.HasAsset(rock3), "the map doesn't have it yet");
        string result = c.AddAsset(rock3);
        Report.Info(result);
        Assert.True(c.HasAsset(rock3) && FeatureCatalog.Load(c.Package.Root, c.Smf).Definitions.Any(d => d.Name == "agorm_rock3" && d.ModelFound == true),
            "its files are in, and its feature is defined with its model");
        c.Undo();
        Assert.Equal(before, Listing(c.Package.Root), "undo takes every file and folder away again");
        c.Redo();
        c.Apply("item-add:smf:agorm_rock3:500,500:0");
        c.Save();
        var saved = Path.Combine(TestData.NewWorkDir("assets-c-saved"), "synthetic_c2.sdz");
        c.Package.SaveAs(saved);
        var reopened = Open(saved, "assets-c-reopened");
        Assert.True(reopened.HasAsset(rock3) || rock3.Files.All(f => File.Exists(Path.Combine(reopened.Package.Root, f.Path))), "the files are in the saved map");
        Assert.True(reopened.Items.All.Any(i => i.Name == "agorm_rock3"), "and so is the placed rock");
    }

    [Test]
    public static void ClashesCopyNothing()
    {
        var library = new AssetLibrary(TestData.NewWorkDir("assets-clash"));
        Collect(library, TestData.AcidicQuarry);
        var b = Open(MapB(TestData.NewWorkDir("assets-clash-b")), "assets-clash-b-open", library);
        string before = Listing(b.Package.Root);
        foreach (var name in new[] { "agorm_rock3", "agorm_rock1" })
        {
            var asset = library.Entries.Single(e => e.Name == name);
            var ex = Assert.Throws<InvalidOperationException>(() => b.AddAsset(asset), $"{name} into a map with a different rocks1a.tga / agorm_rock1");
            Report.Info(ex.Message);
        }
        Assert.Equal(before, Listing(b.Package.Root), "nothing copied");
        Assert.True(!b.CanUndo, "nothing recorded");
        var tree = library.Entries.First(e => e.Kind == AssetKind.EngineFeature);
        Assert.Throws<InvalidOperationException>(() => b.AddAsset(tree), "an engine tree has nothing to copy");
    }

    [Test]
    public static void ACategoryChoiceIsKept()
    {
        var library = new AssetLibrary(TestData.NewWorkDir("assets-choice"));
        Collect(library, TestData.AcidicQuarry);
        var rock2 = library.Entries.Single(e => e.Name == "agorm_rock2");
        library.SetCategory(rock2, AssetCategories.Buildings);
        Collect(library, TestData.AcidicQuarry);
        var reread = new AssetLibrary(library.Root);
        var again = reread.Entries.Single(e => e.Name == "agorm_rock2");
        Assert.True(again.Category == AssetCategories.Buildings && again.CategoryChosen, "moved to Buildings, and it stays there through collecting again and reloading");
        reread.Remove(again);
        Assert.True(new AssetLibrary(library.Root).Entries.All(e => e.Name != "agorm_rock2") && !Directory.Exists(Path.Combine(library.Root, "files", again.Id)),
            "removing takes it and its files out");
    }

    [Test]
    public static void AddingAnAssetReplaysFromTheJournal()
    {
        var library = new AssetLibrary(TestData.NewWorkDir("assets-journal"));
        Collect(library, TestData.AcidicQuarry);
        var archive = SyntheticMap.Build(TestData.NewWorkDir("assets-journal-map"));
        var live = Open(archive, "assets-journal-live", library);
        var dir = TestData.NewWorkDir("assets-journal-record");
        using (var journal = new SessionJournal(Path.Combine(dir, "j.journal"), Path.Combine(dir, "files")))
        {
            live.Journal = journal;
            live.AddAsset(library.Entries.Single(e => e.Name == "agorm_rock4"));
            live.AddAsset(library.Entries.Single(e => e.Name == "agorm_rock5"));
            live.Undo();
            live.Apply("item-add:smf:agorm_rock4:300,300:0");
            live.Journal = null;
        }
        var rebuilt = Open(archive, "assets-journal-rebuilt", library);
        SessionJournal.Replay(rebuilt, SessionJournal.Read(Path.Combine(dir, "j.journal")));
        Assert.Equal(Listing(live.Package.Root), Listing(rebuilt.Package.Root), "the replayed package has the same files");
        rebuilt.Redo();
        live.Redo();
        Assert.Equal(Listing(live.Package.Root), Listing(rebuilt.Package.Root), "and redo agrees");
    }
}
