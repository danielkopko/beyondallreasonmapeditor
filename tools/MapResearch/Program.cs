using MapCore;
using MapResearch;

// Measurements behind Step 6 of PLAN.md. Re-runnable on any map.
if (args is ["models", var modelsOut])
{
    ModelStudy.Run(modelsOut);
    return 0;
}
if (args is ["barunits", var unitsOut])
    return BarUnits.Run(unitsOut);
if (args is ["barfeatures", var featuresOut, var commit, var wrecks])
    return BarFeatures.Run(featuresOut, commit, wrecks);
if (args is ["barfeatures", var featuresOut2, var commit2, var wrecks2, var unitCatalog])
    return BarFeatures.Run(featuresOut2, commit2, wrecks2, unitCatalog);
if (args.Length != 3 || args[0] is not ("normals" or "splat"))
{
    Console.Error.WriteLine("""
        Usage: MapResearch <study> <archive> <output-folder>
          normals  Compare the detail normal map with normals computed from the heightmap
          splat    Profile the splat distribution and texture around metal spots
        MapResearch models <output-folder>
          Write the oil pump's models into a folder laid out like a map, for MapTool preview
        MapResearch barunits <output-folder>
          Build the catalog of BAR's standard units (names, kinds, weapons' targets, 64-pixel pictures) from
          BAR's repository on GitHub, for the guardian tower's target list
        MapResearch barfeatures <output-folder> <commit> none|all|<names-file> [<unit catalog.json>]
          Build the catalog of the features BAR defines, with the models and textures of BAR's own features and
          of the wrecks named (none, all, or those in a file), and of the units the unit catalog lists, from BAR's
          repository at a commit
        """);
    return 2;
}

Directory.CreateDirectory(args[2]);
var workspace = FileSystemUtil.NewTempDir("research");
try
{
    var map = MapData.Load(MapPackage.Open(args[1], workspace));
    if (args[0] == "normals")
        NormalStudy.Run(map, args[2]);
    else
        SplatStudy.Run(map, args[2]);
}
finally
{
    FileSystemUtil.DeleteTree(workspace);
}
return 0;
