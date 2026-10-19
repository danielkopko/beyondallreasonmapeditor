using System.Text;
using MapCore.Assets;
using MapCore.Editing;
using MapCore.Items;
using MapCore.Lua;

namespace MapCore.Tests;

/// <summary>
/// New features made from another's model (Experimental in the Asset tab): the definition written holds only keys the
/// engine reads, every one of them, reads back the same whether run or read as text, and is one undo step; a model
/// from the library brings only what it needs.
/// </summary>
static class CustomFeatureTests
{
    static MapEditSession Open(string archive, string name, AssetLibrary? library = null)
    {
        var s = MapEditSession.Open(MapPackage.Open(archive, TestData.NewWorkDir(name)));
        s.Assets = library;
        return s;
    }

    static string Listing(string root) => string.Join("\n", Directory.EnumerateFileSystemEntries(root, "*", FileSystemUtil.AllEntries)
        .Select(f => FileSystemUtil.EntryPath(root, f) + (File.Exists(f) ? ":" + new FileInfo(f).Length : "/")).Order(StringComparer.Ordinal));

    static FeatureDef Base(MapEditSession s, string name) =>
        FeatureDefs.Load(s.Package.Root).SelectMany(f => f.Features).Single(f => f.Name == name);

    [Test]
    public static void ANewFeatureFromAMapsOwnModel()
    {
        var s = Open(TestData.PathOf(TestData.AcidicQuarryArchive), "custom-aq");
        string before = Listing(s.Package.Root);
        var rock = Base(s, "agorm_rock1");
        var settings = CustomFeatureSettings.FromDefinition(rock).WithAncientMonolith() with { Description = "Say \"hi\" \\ : ; = % & tab\there\nnew line" };
        string result = s.AddCustomFeature("ancient_monolith_choke", "agorm_rock1", settings);
        Report.Info(result);
        var path = Path.Combine(s.Package.Root, "features", "ancient_monolith_choke.lua");
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 3 && !(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF), "written without a byte-order mark (Lua 5.1 won't read one)");
        var text = Encoding.UTF8.GetString(bytes);
        Report.Info(text);

        var ran = FeatureDefs.Read("features/ancient_monolith_choke.lua", text);
        var def = ran.Features.Single();
        var t = def.Table!;
        Assert.True(ran.Ran && def.Name == "ancient_monolith_choke" && def.Object == rock.Object && def.Description == settings.Description,
            $"it runs, named, with the rock's model and the description exactly ({def.Object})");
        Assert.True(t.Get("damage") is 900000.0 && t.Get("reclaimtime") is 36000.0 && t.Get("autoreclaimable") is false && t.Get("reclaimable") is true &&
                    t.Get("indestructible") is false && t.Get("metal") is 5000.0 && t.Get("energy") is 0.0 && t.Get("blocking") is true && t.Get("flammable") is false,
            "every gameplay key written out, defaults included");
        Assert.True(t.Get("nodgun") == null && t.Get("sightdistance") == null && t.Get("featuredead") == null &&
                    ((LuaMap)t.Get("customparams")!).Get("made_from") is "agorm_rock1",
            "nothing the engine doesn't read, nothing naming the original but where it came from");
        Assert.Equal(CustomFeatureSettings.FromDefinition(rock) with { }, CustomFeatureSettings.FromDefinition(rock), "settings compare by value");
        Assert.Equal(settings, CustomFeatureSettings.FromDefinition(def), "read back, the settings are the ones written");

        var asText = FeatureDefs.Read("features/ancient_monolith_choke.lua", text, new LuaLimits(MaxSteps: 1));
        Assert.True(!asText.Ran && asText.Features.Single() is { Name: "ancient_monolith_choke" } read && read.Object == def.Object,
            "read as text (when a file can't run) it gives the same name and model");

        var (definitions, _) = FeatureCatalog.Load(s.Package.Root, s.Smf);
        Assert.True(definitions.Single(d => d.Name == "ancient_monolith_choke").ModelFound == true, "Items › Place lists it, with its model found");
        Assert.True((s.UndoDescription ?? "").StartsWith("feature-custom:ancient_monolith_choke:agorm_rock1:", StringComparison.Ordinal) && !(s.UndoDescription ?? "").Contains('&'),
            $"one undo step, with no '&' to become a menu mnemonic: {(s.UndoDescription ?? "")}");
        s.Undo();
        Assert.Equal(before, Listing(s.Package.Root), "undo takes the file away");
        s.Redo();
        Assert.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes), "the same op gives the same bytes");
    }

    /// <summary>
    /// The map's new features made with the editor are found by their files' first line (the browser lists them under
    /// Experimental): not the map's own features, nor a structure kind's parts, which are written the same way.
    /// </summary>
    [Test]
    public static void NewFeaturesMadeWithTheEditorAreFound()
    {
        var s = Open(TestData.PathOf(TestData.AcidicQuarryArchive), "custom-found");
        Assert.True(s.MadeFeatures().Count == 0, "none in the map as it came (its rocks are its own)");
        s.AddStructureKind("gate", MapCore.Structures.StructureType.Door, new StructureKindSettings("agorm_rock1", "agorm_rock2", "agorm_rock3"));
        s.AddStructureKind("crag", MapCore.Structures.StructureType.Tunnel,
            StructureKindSettings.Built(MapCore.Structures.StructureLook.Concrete, MapCore.Structures.StructureType.Tunnel));
        string FirstLine(string feature) => File.ReadLines(Path.Combine(s.Package.Root, "features", feature + ".lua")).First();
        Assert.True(CustomFeature.MadeBy(FirstLine("gate_wall")) is ("gate_wall", "agorm_rock1") && CustomFeature.MadeBy(FirstLine("crag_wall")) == null,
            $"a kind's parts made from models say the same as a new feature ({FirstLine("gate_wall")}); built ones don't");
        Assert.True(s.MadeFeatures().Count == 0, "a kind's parts aren't listed");

        var settings = CustomFeatureSettings.FromDefinition(Base(s, "agorm_rock4")).WithAncientMonolith() with { BuildOn = 1 };
        s.AddCustomFeature("monolith", "agorm_rock4", settings);
        var found = s.MadeFeatures();
        Report.Info(string.Join("; ", found));
        Assert.True(found.Count == 1 && found[0] is { Name: "monolith", MadeFrom: "agorm_rock4", Model: string model } &&
                    File.Exists(Path.Combine(s.Package.Root, model)),
            "the new feature is found, with what it was made from and its model in the map");
        Assert.Equal(settings, found[0].Settings, "with its settings as written, build-on from the structures config");
        s.Undo();
        Assert.True(s.MadeFeatures().Count == 0, "undone, it is gone");
    }

    [Test]
    public static void BadNewFeaturesAreRefused()
    {
        var s = Open(TestData.PathOf(TestData.AcidicQuarryArchive), "custom-refuse");
        string before = Listing(s.Package.Root);
        var ok = CustomFeatureSettings.FromDefinition(Base(s, "agorm_rock1"));
        foreach (var (op, why) in new[]
        {
            ($"feature-custom:Monolith!:agorm_rock1:{ok.Encode()}", "a name with capitals and punctuation"),
            ($"feature-custom:agorm_rock2:agorm_rock1:{ok.Encode()}", "a name the map already defines"),
            ($"feature-custom:my_treetype:agorm_rock1:{ok.Encode()}", "a name the engine would take for its own tree"),
            ($"feature-custom:fine_name:no_such_feature:{ok.Encode()}", "a base nobody defines"),
            ($"feature-custom:fine_name:agorm_rock1:{(ok with { Health = 0 }).Encode()}", "no health"),
            ($"feature-custom:fine_name:agorm_rock1:{ok.Encode()};nodgun=1", "a key the engine doesn't read"),
            ($"feature-custom:fine_name:agorm_rock1:{(ok with { FootprintX = 40 }).Encode()}", "a footprint over 32"),
        })
        {
            var ex = Assert.Throws<InvalidOperationException>(() => s.Apply(op), why);
            Report.Info($"{why}: {ex.Message}");
        }
        Assert.True(!s.CanUndo && Listing(s.Package.Root) == before, "nothing changed, nothing to undo");
    }

    /// <summary>
    /// A model from the library onto a map without it: its model and textures come in, its definition doesn't (so the
    /// map gains only the new feature), all as one step; a map whose own file of that path differs is refused.
    /// </summary>
    [Test]
    public static void ANewFeatureFromALibraryModel()
    {
        var library = new AssetLibrary(TestData.NewWorkDir("custom-library"));
        var aq = MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir("custom-library-aq"));
        library.Collect(aq.Root, aq.LoadSmf(), aq.LoadMapInfo(), FeaturePlacerConfig.Load(aq.Root));
        var rock3 = library.Entries.Single(e => e.Name == "agorm_rock3");
        var settings = CustomFeatureSettings.FromDefinition(MapEditSession.LibraryDefinition(library, rock3, "agorm_rock3")!) with { Health = 5000 };

        var c = Open(SyntheticMap.Build(TestData.NewWorkDir("custom-c")), "custom-c-open", library);
        string before = Listing(c.Package.Root);
        Report.Info(c.AddCustomFeature("big_rock", "agorm_rock3", settings));
        var added = Listing(c.Package.Root).Split('\n').Except(before.Split('\n')).Select(l => l.Split(':')[0]).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var textures = S3oModel.ReadTextures(File.ReadAllBytes(Path.Combine(aq.Root, "objects3d", "rock3.s3o")))!.Value;
        var expected = new[] { "features/", "features/big_rock.lua", "objects3d/", "objects3d/rock3.s3o", "unittextures/", $"unittextures/{textures.Texture1}", $"unittextures/{textures.Texture2}" }
            .Where(p => !before.Split('\n').Any(l => l.StartsWith(p + (p.EndsWith('/') ? "" : ":"), StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(string.Join(", ", expected), string.Join(", ", added), "the new definition, the model and its textures, nothing else");
        var (definitions, _) = FeatureCatalog.Load(c.Package.Root, c.Smf);
        Assert.True(definitions.Single(d => d.Name == "big_rock").ModelFound == true && definitions.All(d => d.Name != "agorm_rock3"),
            "big_rock is placeable with its model; agorm_rock3 itself isn't brought in");
        c.Undo();
        Assert.Equal(before, Listing(c.Package.Root), "one undo takes every file and folder away");

        // A map with its own, different file at the texture's path: copying would change that map's own features.
        var clash = Open(SyntheticMap.Build(TestData.NewWorkDir("custom-clash"), "synthetic_clash.sdz",
            files: [("unittextures/" + textures.Texture1, [9, 9, 9])]), "custom-clash-open", library);
        string clashBefore = Listing(clash.Package.Root);
        var ex = Assert.Throws<InvalidOperationException>(() => clash.AddCustomFeature("big_rock", "agorm_rock3", settings), "a texture of the same path that differs");
        Report.Info(ex.Message);
        Assert.True(Listing(clash.Package.Root) == clashBefore && !clash.CanUndo, "nothing copied, nothing to undo");
    }

    [Test]
    public static void ANewFeatureReplaysFromTheJournal()
    {
        var library = new AssetLibrary(TestData.NewWorkDir("custom-journal-library"));
        var aq = MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir("custom-journal-aq"));
        library.Collect(aq.Root, aq.LoadSmf(), aq.LoadMapInfo(), FeaturePlacerConfig.Load(aq.Root));
        var rock4 = library.Entries.Single(e => e.Name == "agorm_rock4");
        var settings = CustomFeatureSettings.FromDefinition(MapEditSession.LibraryDefinition(library, rock4, "agorm_rock4")!).WithAncientMonolith();
        var archive = SyntheticMap.Build(TestData.NewWorkDir("custom-journal-map"));
        var live = Open(archive, "custom-journal-live", library);
        var dir = TestData.NewWorkDir("custom-journal-record");
        using (var journal = new SessionJournal(Path.Combine(dir, "j.journal"), Path.Combine(dir, "files")))
        {
            live.Journal = journal;
            live.AddCustomFeature("monolith_a", "agorm_rock4", settings);
            live.AddCustomFeature("monolith_b", "agorm_rock4", settings with { Description = "second" });
            live.Undo();
            live.Apply("item-add:smf:monolith_a:300,300:0");
            live.Journal = null;
        }
        var rebuilt = Open(archive, "custom-journal-rebuilt", library);
        SessionJournal.Replay(rebuilt, SessionJournal.Read(Path.Combine(dir, "j.journal")));
        Assert.Equal(Listing(live.Package.Root), Listing(rebuilt.Package.Root), "the replayed package has the same files");
        Assert.True(File.ReadAllText(Path.Combine(live.Package.Root, "features", "monolith_a.lua")) ==
                    File.ReadAllText(Path.Combine(rebuilt.Package.Root, "features", "monolith_a.lua")), "and the same definition");
    }
}
