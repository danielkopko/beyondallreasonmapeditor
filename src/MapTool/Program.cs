using MapCore;
using MapCore.Archives;
using MapCore.Imaging;
using MapCore.Lua;
using MapCore.Smf;
using MapCore.Textures;

// Temporary folders an earlier run left behind when it was stopped or crashed (FileSystemUtil.SweepTempDirs).
try
{
    FileSystemUtil.SweepTempDirs();
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
}

try
{
    return args.Length == 0 ? Usage() : args[0] switch
    {
        "env" => Env(),
        "list" when args.Length == 2 => List(args[1]),
        "info" when args.Length == 2 => Info(args[1]),
        "dds-info" when args.Length >= 2 => DdsInfo(args[1], args[2..]),
        "export-texture" when args.Length is 3 or 5 => ExportTexture(args[1], args[2], args[3..]),
        "export-minimap" when args.Length == 3 => ExportMinimap(args[1], args[2]),
        "set" when args.Length >= 4 => Set(args[1], args[2], args[3..]),
        "items" when args.Length is 2 or 3 && (args.Length == 2 || args[2] == "--all") => Items(args[1], args.Length == 3),
        "metal" when args.Length == 2 => Metal(args[1]),
        "edit" when args.Length >= 4 => Edit(args[1], args[2], args[3..]),
        "export" when args.Length == 4 => ExportLayer(args[1], args[2], args[3]),
        "render" when args.Length >= 3 => Render(args[1], args[2], args[3..]),
        "render3d" when args.Length >= 3 => Render3D(args[1], args[2], args[3..]),
        "png-diff" when args.Length == 3 => PngDiff(args[1], args[2]),
        "install" when args.Length >= 2 => Install(args[1], args[2..]),
        "assets" when args.Length >= 2 => Assets(args[1], args[2..]),
        "preview" when args.Length >= 4 => Preview(args[1], args[2], args[3], args[4..]),
        "extract" when args.Length == 3 => Extract(args[1], args[2]),
        "pack" when args.Length is 3 or 4 => Pack(args[1], args[2], Overwrite(args)),
        "repack" when args.Length is 3 or 4 => Repack(args[1], args[2], Overwrite(args)),
        "compare" when args.Length == 3 => Compare(args[1], args[2]),
        "worldmachine" when args.Length == 4 && args[1] == "send" => WorldMachineSend(args[2], args[3]),
        "worldmachine" when args.Length >= 5 && args[1] == "bring" => WorldMachineBring(args[2], args[3], args[4], args[5..]),
        "help" or "-h" or "--help" => Usage(),
        _ => BadArgs(args[0]),
    };
}
catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException
                               or NotSupportedException or UnauthorizedAccessException or FormatException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

/// <summary>The asset library folder: --library &lt;folder&gt; (taken out of the arguments), or the editor's own.</summary>
static string LibraryRoot(ref List<string> arguments)
{
    int i = arguments.IndexOf("--library");
    if (i < 0)
        return MapCore.Assets.AssetLibrary.DefaultRoot;
    if (i + 1 >= arguments.Count)
        throw new FormatException("--library needs a folder.");
    var folder = arguments[i + 1];
    arguments.RemoveRange(i, 2);
    return folder;
}

static int Assets(string command, string[] rest)
{
    var arguments = rest.ToList();
    var library = new MapCore.Assets.AssetLibrary(LibraryRoot(ref arguments));
    if (library.LoadProblem != null)
        Console.WriteLine("note: " + library.LoadProblem);
    switch (command)
    {
        case "collect" when arguments.Count >= 1:
            foreach (var archive in arguments)
                WithPackage(archive, package =>
                {
                    var report = library.Collect(package.Root, package.LoadSmf(), package.LoadMapInfo(), MapCore.Items.FeaturePlacerConfig.Load(package.Root));
                    Console.WriteLine($"{Path.GetFileName(archive)}: {report}");
                    foreach (var note in report.Notes)
                        Console.WriteLine($"  note: {note}");
                });
            return 0;
        case "list" when arguments.Count == 0:
            foreach (var group in library.Entries.GroupBy(e => e.Category).OrderBy(g => MapCore.Assets.AssetCategories.All.ToList().IndexOf(g.Key)))
            {
                Console.WriteLine($"{group.Key} ({group.Count()})");
                foreach (var a in group.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
                    Console.WriteLine($"  {a.Id,-34} {a.Kind,-13} {(a.HasFiles ? $"{a.Files.Count} files" : "by name"),-9} " +
                                      $"seen in {string.Join(", ", a.SeenIn.Select(s => $"{s.Map} {s.Version}".Trim()))}{(a.Variant ? "  (variant)" : "")}");
            }
            Console.WriteLine($"{library.Entries.Count} assets in {library.Root}");
            return 0;
        case "preview" when arguments.Count is 2 or 3:
        {
            var asset = library.Find(arguments[0]) ?? throw new FormatException($"No asset '{arguments[0]}' in the library.");
            var feature = arguments.Count == 3 ? arguments[1] : asset.Features.FirstOrDefault()?.Name ?? asset.Name;
            var folder = Path.Combine(library.Root, "files", asset.Id);
            var model = asset.Features.FirstOrDefault(f => f.Name.Equals(feature, StringComparison.OrdinalIgnoreCase))?.Model
                        ?? throw new FormatException($"{asset.Name} has no model for '{feature}' in the library.");
            WritePreview(MapCore.Assets.ModelPreview.Load(folder, model), arguments[^1], 320, 280, 35, 20);
            return 0;
        }
        default:
            throw new FormatException("assets takes collect <archive>..., list, or preview <id> [<feature>] <out.png>, with --library <folder> to use another library.");
    }
}

/// <summary>Draws a feature's model as the editor's preview does.</summary>
static int Preview(string source, string feature, string png, string[] options)
{
    int width = 320, height = 280;
    double yaw = 35, pitch = 20;
    for (int i = 0; i < options.Length; i++)
    {
        string Value() => i + 1 < options.Length ? options[++i] : throw new FormatException($"{options[i]} needs a value.");
        switch (options[i])
        {
            case "--size":
                var size = Value().Split('x');
                (width, height) = (int.Parse(size[0]), int.Parse(size[1]));
                break;
            case "--yaw":
                yaw = double.Parse(Value(), System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "--pitch":
                pitch = double.Parse(Value(), System.Globalization.CultureInfo.InvariantCulture);
                break;
            default:
                throw new FormatException($"Unknown option '{options[i]}'.");
        }
    }
    void Draw(string root) => WritePreview(MapCore.Assets.ModelPreview.ForFeature(root, feature)
        ?? throw new FormatException($"{feature} isn't a feature with a model in this map."), png, width, height, yaw, pitch);
    if (source.Equals("bar", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(source) && !File.Exists(source))
    {
        // BAR's own feature, from the models shipped beside the tool (BarFeatures).
        var bar = MapCore.Assets.BarFeatureCatalog.Shipped(AppContext.BaseDirectory)
                  ?? throw new FormatException("BAR's features (BarFeatures) aren't beside MapTool.");
        if (bar.FindUnit(feature) is MapCore.Assets.BarUnit unit)
        {
            Console.WriteLine($"{unit.Title}: a BAR {(unit.Building ? "building" : "unit")} ({unit.Faction}){(unit.Author is string by ? ", model by " + by : "")}");
            WritePreview(bar.LoadUnitModel(feature) ?? throw new FormatException($"{feature}'s model ({unit.Object ?? "none"}) isn't shipped here."),
                png, width, height, yaw, pitch);
            return 0;
        }
        var known = bar.Find(feature) ?? throw new FormatException($"BAR defines no feature or unit called {feature} (in {bar.Origin}).");
        WritePreview(bar.LoadModel(feature) ?? throw new FormatException(known.Note ?? $"{feature}'s model ({known.Object ?? "none"}) isn't shipped here."),
            png, width, height, yaw, pitch);
        return 0;
    }
    if (Directory.Exists(source))
        Draw(source);
    else
        WithPackage(source, package => Draw(package.Root));
    return 0;
}

static void WritePreview(MapCore.Assets.ModelPreview model, string png, int width, int height, double yaw, double pitch)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var render = MapCore.Rendering.ModelRenderer.Render(model, width, height, yaw, pitch);
    PngWriter.WriteRgba(png, render.Image);
    static string Describe(RgbaImage? image)
    {
        if (image == null)
            return "none";
        long clear = 0;
        for (int i = 3; i < image.Pixels.Length; i += 4)
            clear += image.Pixels[i] < 128 ? 1 : 0;
        return $"{image.Width}x{image.Height}, {100.0 * clear / (image.Width * image.Height):0.#}% clear alpha";
    }
    Console.WriteLine($"{model.ModelPath}: {model.Mesh.Pieces} pieces, {model.Mesh.TriangleCount:N0} triangles");
    Console.WriteLine($"  texture 1 {model.Mesh.Texture1}: {Describe(model.Colour)}");
    Console.WriteLine($"  texture 2 {model.Mesh.Texture2}: {Describe(model.Extra)}{(model.CutOut ? "; cuts out" : "")}");
    foreach (var note in model.Notes)
        Console.WriteLine("  note: " + note);
    Console.WriteLine($"  covers {render.Coverage:P0} of the picture; {sw.ElapsedMilliseconds} ms");
    Console.WriteLine($"Wrote {Path.GetFullPath(png)}");
}

static int Usage()
{
    Console.WriteLine("""
        MapTool - command-line editor for Spring/Recoil (.sd7/.sdz) maps

        Commands:
          env                                 Show the tools and runtime the editor will use
          list    <archive>                   List the files in a map archive
          info    <archive | file.smf>        Describe the map file (size, heights, metal, features, layout)
          dds-info <file.dds> [--png <out.png>] [--mip N] [--face N] [--no-alpha]
                                              Describe a texture; optionally export one level as PNG
          export-texture <archive> <out.png> [--width N]
                                              Export the map texture (tiles) as PNG, optionally scaled down
          export-minimap <archive> <out.png>  Export the map's built-in 1024x1024 minimap as PNG
          set <archive> <new-archive> <path>=<value> [...] [--overwrite]
                                              Change mapinfo.lua values and save as a new archive.
                                              Paths use dots: name, version, smf.minheight,
                                              atmosphere.fogColor, teams.0.startPos.x, ...
                                              Values: a number, text, or numbers separated by commas
          items <archive> [--all]             List placed objects (SMF features, featureplacer lists),
                                              the placeable feature names, and grass; --all lists
                                              every item with its ref for the items-* operations
          metal <archive>                     List metal spots (as BAR finds them) with T1 income
          edit <archive> <new-archive> <operation> [...] [--overwrite] [--mirror <lr|tb|diag|anti|turn>]
                                              Apply edits and save as a new archive. --mirror makes each
                                              edit at a place again at its mirror image: across the
                                              middle left-right or top-bottom, a diagonal (square maps),
                                              or turn: mirrored and flipped, a half turn. @<file> reads
                                              operations from a file, one a line (# starts a comment line).
                                              Operations:
                                                set:<path>=<value>
                                                unset:<path>   (removes the value, e.g. teams.15)
                                                start-move:<team>:<x>,<z>
                                                metal-move:<x>,<z>:<new x>,<new z>[:look]
                                                    (look: the spot's look moves too - texture, splat, normals)
                                                metal-add:<x>,<z>[:<metal/s>]
                                                metal-remove:<x>,<z>
                                                metal-income:<x>,<z>:<metal/s>
                                                metal-resize:<x>,<z>:<cells across>
                                                item-move:<x>,<z>:<new x>,<new z>
                                                item-rotate:<x>,<z>:<heading>
                                                item-add:<smf|placer>:<name>:<x>,<z>[:<heading>]
                                                item-remove:<x>,<z>
                                                items-move:<dx>,<dz>:<refs>   items-rotate:<degrees>:<refs>
                                                items-heading:<degrees>:<refs>   items-remove:<refs>
                                                    (refs: s3;f12;b0 - SMF feature / featureplacer object,
                                                     unit, building; 0-based, as "MapTool items" lists them)
                                                grass-paint:<x>,<z>:<radius>:<value 0-254>
                                                height-raise:<x>,<z>:<radius>:<amount>[:<hardness 0-1>]
                                                height-flatten:<x>,<z>:<radius>:<height>[:<hardness 0-1>[:<strength 0-1>]]
                                                height-smooth:<x>,<z>:<radius>[:<strength 0-1>[:<hardness 0-1>]]
                                                beautify-heights:[from=<deg>;amount=<units>;strata=<0-1>;gullies=<0-1>;ground=<units>]
                                                    (cliffs roughened, keeping where units and buildings go)
                                                beautify-texture:[colours=map;strength=<0-1>;rock=<r,g,b>;rockamount=<0-1>;...]
                                                    (the texture painted by the terrain; colours=map: the map's own)
                                                height-noise:<x>,<z>:<radius>:<amplitude>[:<hardness>[:<bump size>[:<mirror>]]]
                                                    (mirror: the noise read mirrored, as --mirror writes it)
                                                height-offset:<amount>      height-scale:<factor>
                                                ramp:<x1>,<z1>[;<bend x>,<bend z>...]:<x2>,<z2>:<width>:<h1|auto>,<h2|auto>
                                                    [:<edge|auto>[:<look 0|1|2>[:<roughness 0-1>[:<rock x>,<rock z>|auto
                                                    [:<dirt x>,<dirt z>|auto]]]]]
                                                    (a slope from end 1 to end 2, straight or through bends, each bend
                                                     rounded as much as the runs beside it allow; level across, blended
                                                     into the ground over the edge; auto heights: the ground at that end;
                                                     look: 0 heights only, 1 texture and splat copied from beyond the
                                                     ends, 2 rock to dirt; quote it: PowerShell reads ; as a new command)
                                                texture-paint:<x>,<z>:<radius>:<r>,<g>,<b>[:<hardness>[:<strength>]]
                                                import:<layer>=<file.png>
                                                asset-add:<id>   (copy a library asset's files in; --library <folder>)
                                                feature-custom:<name>:<base feature>:<settings>
                                                    (a new feature using another's model, from the map or the
                                                     library; settings: description=...;damage=N;blocking=1|0;
                                                     reclaimable=1|0;reclaimtime=N;autoreclaimable=1|0;metal=N;
                                                     energy=N;flammable=1|0;indestructible=1|0;footprintx=N;
                                                     footprintz=N;buildon=0|1|2 (1 build on it, 2 build on it
                                                     and units can't cross; adds the structures gadget),
                                                     values percent-encoded)
                                                structure-kind:<name>:<door|tunnel|open_tunnel>:<settings>
                                                    (a door or tunnel kind; settings: look=concrete|metal|assets;
                                                     height=N (game units; default 80); roof=flat|cliff (a tunnel's:
                                                     flat is built on; cliff, an open tunnel's default, is rock);
                                                     gap=N;wallwidth=N;depth=N (squares of 16; default 4, 3, 3);
                                                     health=N;work=N;metal=N;energy=N;open=1|0; with look=assets,
                                                     wall=<feature>;centre=<feature>;repair=<feature>, models from
                                                     the map or --library. Writes the parts (concrete and metal
                                                     models built), the config, the gadget)
                                                structure-kind:<name>:pump:mps=N;eps=N;health=N;work=N;metal=N;energy=N
                                                    (an oil pump: derelict at the start; whoever repairs it gets
                                                     mps metal and eps energy a second while it stands)
                                                structure-kind:<name>:shield_pad:gap=6;depth=6;shield=50;epm=10;bubble=1;health=N;work=N;metal=N;energy=N
                                                    (a shield platform: a low pad units drive over, derelict at the
                                                     start; whoever repairs it owns it, and their units and their
                                                     allies' get a bubble shield as they drive off it: shield% of
                                                     the unit's health, for epm energy a metal it costs (topped up,
                                                     for the part missing, when it drives over again). The bubble
                                                     surrounds the unit by its size (×bubble) and takes hits for it
                                                     and for allies within it until nothing is left)
                                                structure-change:<name>:<settings>   (new settings for a kind:
                                                     every one placed changes with it)
                                                structure-add:<kind>:<x>,<z>:<facing 0-3>[:broken]   (broken: it
                                                     starts the game destroyed, repair markers out)
                                                structure-move:<x>,<z>:<new x>,<new z>   structure-turn:<x>,<z>:<facing 0-3>
                                                     (quarter turns: the engine turns blocked footprints only so)
                                                structure-broken:<x>,<z>:<1|0>   structure-remove:<x>,<z>
                                                structure-marker-add:<x>,<z>:<marker x>,<marker z>   (a repair marker
                                                     for the structure at x,z: built kinds; green while it stands,
                                                     red to reclaim while it is destroyed; without any, what is left
                                                     of it in its gap is reclaimed)
                                                structure-marker-move:<x>,<z>:<new x>,<new z>   structure-marker-remove:<x>,<z>
                                                structure-kind:<name>:guardian:look=ancient|concrete|metal;kill=S;rocket=S:<targets>
                                                    (a guardian tower: indestructible, 3 by 3 squares, rocket pods
                                                     pointing up. A unit it dooms is destroyed kill seconds later
                                                     (default 1) wherever it goes, healed or not, however many come;
                                                     a rocket for show every rocket seconds (default 2) while it is
                                                     wearing anything down. Targets: unit names, comma-separated
                                                     (each one's Scavengers variant, name_scav, too), "default" for
                                                     the aircraft that attack the ground, "aircraft" for every unit
                                                     that flies (BarUnits/catalog.json beside MapTool), or "none":
                                                     what the first tower of the kind destroys crossing its path)
                                                structure-targets:<kind>:<targets>   (the first tower's, from then on;
                                                     towers placed keep their own rules, and a new one copies the
                                                     kind's last placed tower)
                                                structure-zone:<x>,<z>:<distance>:<0|1>:<x,z;x,z;...>   (the path
                                                     the guardian at x,z guards, anywhere on the map: 2 to 256
                                                     points, distance 16-2048 game units either side, 1 to close
                                                     it back to the first point; structure-zone:<x>,<z>:none clears)
                                                structure-rule:<x>,<z>:<band|inside|outside>:<delay>:<teams>:<targets>
                                                    (what the guardian at x,z does in one region: band, within the
                                                     distance of the path; inside a closed path beyond it; outside,
                                                     the rest of the map. Units of the targets, of the teams (all,
                                                     none, or BAR's lobby teams 1-8 such as 2,3,4; all takes in
                                                     Scavengers and Raptors too), are doomed once they have been in
                                                     the region delay seconds (0: at once) without leaving it)
                                              Positions pick the nearest spot or item within 64 units.
                                              Brushes are full strength inside radius x hardness (default 0.5).
          worldmachine send <archive> <folder>
                                              Write what World Machine works from: heights.png (16-bit) and
                                              .r16, texture.png, masks (water, slope, cliffs, keep), bar_map.json
                                              and README.txt (how to set World Machine up)
          worldmachine bring <archive> <new-archive> <folder> [--as-is] [--stretch | --range <low>,<high>]
                             [--flip | --no-flip] [--overwrite]
                                              Bring World Machine's outputs back from the folder (wm_heights.png
                                              or .r16/.r32, wm_texture.png, wm_splat.png, wm_grass.png), resampled,
                                              each as one step. Heights by bar_map.json's range; upside down found
                                              by comparing with the map. Unless --as-is, where units and buildings
                                              go is kept, and metal spots, structures and starts.
          export <archive> <layer> <out.png>  Export a layer for editing elsewhere. Layers: heights (16-bit),
                                              texture, metal, grass, types (terrain type numbers),
                                              resource:<mapinfo resources key>
                                              (e.g. resource:splatDistrTex). Re-import with edit import:...
          render <archive> <out.png> [options]
                                              Draw the map as the editor shows it (labels not drawn).
                                              --layer texture|heights|metal|grass|splat|normals|minimap
                                              --overlays all|none|water,metal,items,start,grid
                                              --size WxH  --center x,z  --scale <game units per pixel>
          render3d <archive> <out.png> [options]
                                              Draw the map in 3D as the editor's 3D view does.
                                              --camera x,z,yaw,pitch,distance (degrees; yaw 0 looks north)
                                              --size WxH  --exaggeration N  --markers all|none|metal,items,start
                                              --unlit  --no-shadows  --no-water  --mesh-step N
                                      --models (draw the placed features' models: trees, rocks, buildings;
                                               BAR's own with BAR's models, --no-bar-models to leave them out)
                                              --passability hovercraft|vehicles|bots  (tint where they can go:
                                               green open, red too steep, purple too deep, cyan a hover on
                                               water; --hover-paths is short for hovercraft)
          assets collect <archive>... [--library <folder>]
                                              Add maps' assets (features with their models and textures, and the
                                              BAR and engine names they use) to the asset library; a map collected
                                              again adds only what is new
          assets list [--library <folder>]    List the library by category, with ids for edit's asset-add:<id>
          assets preview <id> [<feature>] <out.png> [--library <folder>]
                                              Draw a library asset's model as the editor's preview does
          preview <archive | folder | bar> <feature> <out.png> [--size WxH] [--yaw N] [--pitch N]
                                              Draw the model of a feature the map defines (S3O models);
                                              "bar" draws one of BAR's own (treetype0, rocks30_def_01, armpw, armllt, ...)
          install <archive> [<maps folder>] [--overwrite] [--allow-same-name]
                                              Copy a map into BAR's maps folder (found automatically when
                                              not given). Refuses if another archive there has the same
                                              name and version, or a file of that name exists. Also puts
                                              the map's picture where BAR's lobby looks for it (its minimap,
                                              in LuaMenu/Images/Minimaps beside the maps folder): the lobby
                                              shows pictures only of maps it knows otherwise.
          png-diff <a.png> <b.png>            Count pixels whose colour differs (exit code 1 if any do)
          extract <archive> <folder>          Extract a map into a new or empty folder
          pack    <folder> <archive> [--overwrite]
                                              Pack a map folder into .sd7 or .sdz (by extension)
          repack  <archive> <new-archive> [--overwrite]
                                              Extract and pack again without changes
          compare <archive-a> <archive-b>     Compare contents (exit code 1 if they differ)
          help                                Show this help
        """);
    return 0;
}

static int Env()
{
    Console.WriteLine($".NET runtime : {Environment.Version}");
    Console.WriteLine($"64-bit       : {Environment.Is64BitProcess}");
    var sevenZip = Tools.FindSevenZip();
    Console.WriteLine($"7-Zip        : {sevenZip ?? "not found (.sd7 unavailable, .sdz still works)"}");
    return 0;
}

static int List(string archive)
{
    var listing = ArchiveIO.Read(archive);
    foreach (var key in new[] { "Type", "Solid", "Blocks", "Method" })
        if (listing.Properties.TryGetValue(key, out var value))
            Console.WriteLine($"{key,-7}: {value}");
    Console.WriteLine();
    foreach (var e in listing.Entries)
    {
        var crc = e.Crc is uint c ? c.ToString("X8") : "";
        Console.WriteLine($"{(e.IsDirectory ? "D" : "-")} {e.Size,10} {crc,8} {AttributeLetters(e.Attributes),-4} {e.Path}");
    }
    Console.WriteLine($"\n{listing.Entries.Count(e => !e.IsDirectory)} files, {listing.Entries.Count(e => e.IsDirectory)} folders");
    return 0;
}

static int Info(string path)
{
    if (Path.GetExtension(path).Equals(".smf", StringComparison.OrdinalIgnoreCase))
    {
        PrintSmf(SmfFile.Load(path), Path.GetFileName(path));
        return 0;
    }

    WithPackage(path, package =>
    {
        var smf = package.LoadSmf();
        PrintMapInfo(package.LoadMapInfo(), smf, package.Root);
        PrintSmf(smf, package.SmfEntry);
        var texture = package.LoadTexture(smf);
        var image = texture.Decode();
        Console.WriteLine($"Texture      : {texture.Width}x{texture.Height} from {string.Join(", ", texture.Entries!)}, {TransparentPixels(image):N0} transparent pixels");
        foreach (var (key, file) in package.TextureOverrides())
            Console.WriteLine($"Note         : mapinfo.lua sets smf.{key} = \"{file}\"; the game shows that image, not the SMF's own copy");
        foreach (var warning in texture.Warnings)
            Console.WriteLine($"warning: {warning}");
    });
    return 0;
}

static int ExportTexture(string archive, string png, string[] options)
{
    int? width = null;
    if (options.Length == 2)
        width = options[0] == "--width" ? ParseCount(options[1], "--width") : throw new InvalidOperationException($"Unknown option '{options[0]}'.");

    WithPackage(archive, package =>
    {
        var image = package.LoadTexture(package.LoadSmf()).Decode();
        if (width is int w && w > 0 && w < image.Width)
            image = image.Resample(w, (int)Math.Round((double)image.Height * w / image.Width));
        PngWriter.WriteRgba(png, image);
        Console.WriteLine($"Wrote {image.Width}x{image.Height} texture to {Path.GetFullPath(png)}");
    });
    return 0;
}

static int ExportMinimap(string archive, string png)
{
    WithPackage(archive, package =>
    {
        PngWriter.WriteRgba(png, Minimap.Decode(package.LoadSmf()));
        Console.WriteLine($"Wrote 1024x1024 minimap to {Path.GetFullPath(png)}");
    });
    return 0;
}

/// <summary>Extracts an archive into a temporary workspace, runs <paramref name="action"/>, and cleans up.</summary>
static void WithPackage(string archive, Action<MapPackage> action)
{
    var workspace = FileSystemUtil.NewTempDir("maptool");
    try
    {
        action(MapPackage.Open(archive, workspace));
    }
    finally
    {
        FileSystemUtil.DeleteTree(workspace);
    }
}

static int Items(string archive, bool all)
{
    WithPackage(archive, package =>
    {
        var smf = package.LoadSmf();
        var items = package.LoadItems(smf);
        Console.WriteLine($"SMF features : {smf.Features.Count}");
        Console.WriteLine(items.Placer == null
            ? "Featureplacer: none (no mapconfig/featureplacer/config.lua)"
            : $"Featureplacer: {items.Placer.Entry} (placed by the map's LuaGaia gadget at game start)");
        foreach (var group in items.All.GroupBy(i => (i.Source, i.Kind, i.Name)).OrderBy(g => g.Key.Source).ThenBy(g => g.Key.Name))
        {
            var first = group.First();
            Console.WriteLine($"  {group.Key.Source,-13} {group.Key.Kind,-8} {group.Key.Name,-20} x{group.Count(),-4} e.g. at ({first.X:0}, {first.Z:0}) rot {first.Rotation:0}");
        }
        if (all)
        {
            Console.WriteLine();
            Console.WriteLine("Every item (ref for the items-* operations, name, position, heading in degrees with 0 facing south):");
            foreach (var item in items.All)
                Console.WriteLine($"  {MapCore.Editing.MapEditSession.ItemRef(item),-6} {item.Name,-20} ({item.X,6:0}, {item.Z,6:0})  {MapCore.Editing.MapEditSession.HeadingDegrees(item),5:0}°");
        }

        var (definitions, warnings) = MapCore.Items.FeatureCatalog.Load(package.Root, smf);
        Console.WriteLine($"\nPlaceable features found in the package ({definitions.Count}; the game defines more):");
        foreach (var d in definitions)
            Console.WriteLine($"  {d.Name,-20} {d.Source}{(d.Model == null ? "" : $", model {d.Model}{(d.ModelFound == true ? "" : " (MISSING)")}")}");
        foreach (var warning in warnings)
            Console.WriteLine($"warning: {warning}");

        Console.WriteLine($"\nGrass        : {(smf.GrassMap == null ? "no grass map" : $"{GrassLayer.CellsWithGrass(smf)} of {smf.GrassMap.Length} cells")} (32x32 game units each)");
    });
    return 0;
}

static void PrintMapInfo(MapInfo info, SmfFile smf, string root)
{
    Console.WriteLine($"Name         : {info.Name} {info.Version}");
    Console.WriteLine($"Author       : {info.Author}");
    bool overridden = info.MinHeight != null || info.MaxHeight != null;
    Console.WriteLine($"Heights      : {info.MinHeight ?? smf.MinHeight} to {info.MaxHeight ?? smf.MaxHeight} " +
                      (overridden ? "(mapinfo.lua smf.minheight/maxheight, replacing the SMF header's)" : "(SMF header)"));
    Console.WriteLine($"Metal        : maxMetal {info.MaxMetal?.ToString() ?? "(engine default 0.02)"}, extractorRadius {info.ExtractorRadius?.ToString() ?? "(engine default 500)"}");
    Console.WriteLine($"Start points : {string.Join("  ", info.StartPositions.Select(s => $"team {s.Team} ({s.X:0}, {s.Z:0})"))}");
    var overrides = info.FindOverrides(root);
    foreach (var file in overrides.GroupBy(o => o.File))
        Console.WriteLine($"May override : {file.Key} ({file.First().Kind}): {string.Join(", ", file.Select(o => o.Path).Distinct(StringComparer.OrdinalIgnoreCase))}");
}

/// <summary>`set` is `edit` with only mapinfo assignments.</summary>
static int Set(string archive, string target, string[] arguments) =>
    Edit(archive, target, arguments.Select(a => a == "--overwrite" ? a : "set:" + a).ToArray());

/// <summary>
/// A guardian's targets given by a word, from BAR's unit catalog beside MapTool: "default" for the aircraft that attack
/// the ground, "aircraft" for every unit that flies (structure-kind:...:guardian:...:<targets>,
/// structure-targets:...:<targets>, structure-rule:...:<targets>).
/// </summary>
/// <summary>An operation as given, or, for @file, the file's operations: one a line, blank lines and lines starting # skipped.</summary>
static IEnumerable<string> OperationsFrom(string argument)
{
    if (!argument.StartsWith('@'))
        return [argument];
    var path = argument[1..];
    if (!File.Exists(path))
        throw new InvalidOperationException($"There is no operations file {Path.GetFullPath(path)}.");
    return File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
}

static string DefaultTargets(string operation)
{
    var parts = operation.Split(':');
    bool takesTargets = parts[0] switch
    {
        "structure-kind" => parts.Length == 5 && parts[2] == "guardian",
        "structure-targets" => parts.Length == 3,
        "structure-rule" => parts.Length == 6,
        _ => false,
    };
    if (!takesTargets || parts[^1] is not ("default" or "aircraft"))
        return operation;
    var catalog = MapCore.Structures.UnitCatalog.Shipped(AppContext.BaseDirectory)
                  ?? throw new InvalidOperationException($"\"{parts[^1]}\" targets come from BarUnits/catalog.json beside MapTool, which isn't there: list the unit names instead.");
    var names = parts[^1] == "default" ? catalog.DefaultTargets : catalog.Aircraft;
    return string.Join(':', parts[..^1]) + ":" + string.Join(",", names);
}

/// <summary>Writes what World Machine works from into a folder: heights, texture, masks, bar_map.json and README.txt.</summary>
static int WorldMachineSend(string archive, string folder)
{
    WithPackage(archive, package =>
    {
        var session = MapCore.Editing.MapEditSession.Open(package);
        Console.WriteLine(session.SendToWorldMachine(Path.GetFullPath(folder)));
    });
    return 0;
}

/// <summary>
/// Brings World Machine's outputs back from a folder (wm_heights, wm_texture, wm_splat, wm_grass: whichever are there),
/// each as one step, and writes a new archive. Heights are mapped by bar_map.json's range unless --stretch or --range.
/// </summary>
static int WorldMachineBring(string archive, string target, string folder, string[] arguments)
{
    folder = Path.GetFullPath(folder);
    bool overwrite = arguments.Contains("--overwrite"), asIs = arguments.Contains("--as-is"), stretch = arguments.Contains("--stretch");
    int rangeAt = Array.IndexOf(arguments, "--range");
    (double, double)? range = null;
    if (rangeAt >= 0)
    {
        var parts = rangeAt + 1 < arguments.Length ? arguments[rangeAt + 1].Split(',') : [];
        if (parts.Length != 2 || !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double lo) ||
            !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double hi))
            throw new FormatException("--range needs <low>,<high>: the heights black and white stand for.");
        range = (lo, hi);
    }
    var info = MapCore.WorldMachine.WorldMachineInfo.Load(folder);
    if (range == null && !stretch)
        range = info is { } sent ? (sent.MinHeight, sent.MaxHeight)
            : throw new InvalidOperationException($"{folder} has no {MapCore.WorldMachine.WorldMachineFiles.InfoFile} (worldmachine send writes it): give --range or --stretch.");
    bool? flip = arguments.Contains("--flip") ? true : arguments.Contains("--no-flip") ? false : null;
    var options = new MapCore.Editing.WorldMachineImport { Keep = !asIs, Range = range, Stretch = stretch, Flip = flip };
    WithPackage(archive, package =>
    {
        var session = MapCore.Editing.MapEditSession.Open(package);
        int brought = 0;
        foreach (var (layer, _) in MapCore.WorldMachine.WorldMachineFiles.Outputs)
            if (MapCore.WorldMachine.WorldMachineFiles.Output(folder, layer) is string path)
            {
                Console.WriteLine(session.BringBackFromWorldMachine(layer, path, options));
                brought++;
            }
        if (brought == 0)
            throw new InvalidOperationException($"{folder} has none of World Machine's outputs (wm_heights.png, wm_texture.png, wm_splat.png, wm_grass.png).");
        var checks = session.CheckBeforeSave(requireNewName: false);
        foreach (var check in checks)
            Console.WriteLine($"{check.Level.ToString().ToLowerInvariant()}: {check.Message}");
        if (checks.Any(c => c.Level == MapCore.Editing.CheckLevel.Error))
            throw new InvalidOperationException("Not saved: fix the errors above first.");
        foreach (var note in session.Save())
            Console.WriteLine(note);
        package.SaveAs(target, overwrite);
        Console.WriteLine($"Wrote {Path.GetFullPath(target)}");
    });
    return 0;
}

/// <summary>Applies operations through one edit session (one SMF load, one save) and writes a new archive.</summary>
static int Edit(string archive, string target, string[] arguments)
{
    bool overwrite = arguments.Contains("--overwrite");
    int mirrorAt = Array.IndexOf(arguments, "--mirror");
    if (mirrorAt >= 0 && mirrorAt + 1 >= arguments.Length)
        throw new InvalidOperationException("--mirror needs lr, tb, diag, anti or turn after it.");
    MapCore.MirrorKind? mirror = mirrorAt >= 0 ? MapCore.MapMirror.ParseKind(arguments[mirrorAt + 1]) : null;
    var operations = arguments.Where((a, i) => a != "--overwrite" && (mirrorAt < 0 || (i != mirrorAt && i != mirrorAt + 1)))
        .SelectMany(OperationsFrom).Select(DefaultTargets).ToList();
    WithPackage(archive, package =>
    {
        var session = MapCore.Editing.MapEditSession.Open(package);
        session.Mirror = mirror; // a diagonal on a map that isn't square refuses here
        if (operations.Any(o => o.StartsWith("asset-add:", StringComparison.Ordinal) || o.StartsWith("feature-custom:", StringComparison.Ordinal) ||
                                o.StartsWith("structure-kind:", StringComparison.Ordinal) || o.StartsWith("structure-change:", StringComparison.Ordinal)))
            session.Assets = new MapCore.Assets.AssetLibrary(LibraryRoot(ref operations));
        var overrides = session.Info.FindOverrides(package.Root);
        if (operations.Any(o => o.StartsWith("metal-")))
            foreach (var warning in session.Metal.Warnings)
                Console.WriteLine($"note: {warning}");
        foreach (var operation in operations)
        {
            Console.WriteLine(session.Apply(operation));
            if (operation.StartsWith("set:") && operation.IndexOf('=') is int eq and > 4)
                foreach (var o in MapInfo.Affecting(overrides, operation[4..eq]).Distinct())
                    Console.WriteLine($"  note: {o.File} may change {o.Path} in game ({o.Kind})");
        }
        // Checks before saving: errors stop the save (the original name only warns here, for test builds).
        var checks = session.CheckBeforeSave(requireNewName: false);
        foreach (var check in checks)
            Console.WriteLine($"{check.Level.ToString().ToLowerInvariant()}: {check.Message}");
        if (checks.Any(c => c.Level == MapCore.Editing.CheckLevel.Error))
            throw new InvalidOperationException("Not saved: fix the errors above first.");
        foreach (var note in session.Save())
            Console.WriteLine(note);
        package.SaveAs(target, overwrite);
        Console.WriteLine($"Wrote {Path.GetFullPath(target)}");
    });
    return 0;
}

static int ExportLayer(string archive, string layer, string png)
{
    WithPackage(archive, package =>
    {
        Console.WriteLine(MapCore.Editing.MapEditSession.Open(package).ExportLayer(layer, png));
        Console.WriteLine($"Wrote {Path.GetFullPath(png)}");
    });
    return 0;
}

static int Render(string archive, string png, string[] options)
{
    var request = MapCore.Rendering.RenderRequest.Parse(options);
    WithPackage(archive, package =>
    {
        var session = MapCore.Editing.MapEditSession.Open(package);
        using var renderer = new MapCore.Rendering.MapRenderer(session);
        var image = new RgbaImage(request.Width, request.Height);
        var view = request.View(session.MapWidth, session.MapHeight);
        var result = renderer.Render(image, view, request.Layer, request.Overlays);
        PngWriter.WriteRgba(png, image);
        Console.WriteLine($"{request.Layer} at {view.UnitsPerPixel:0.###} game units per pixel, {result.Labels.Count} labels (not drawn)");
        if (result.Note != null)
            Console.WriteLine($"note: {result.Note}");
        Console.WriteLine($"Wrote {Path.GetFullPath(png)}");
    });
    return 0;
}

static int Render3D(string archive, string png, string[] options)
{
    int width = 1280, height = 800;
    double[]? cameraValues = null;
    bool barModels = true;
    var settings = new MapCore.Rendering.TerrainOptions { Markers = MapCore.Rendering.MapOverlays.None };
    for (int i = 0; i < options.Length; i++)
    {
        string Value() => i + 1 < options.Length ? options[++i] : throw new FormatException($"{options[i]} needs a value.");
        switch (options[i])
        {
            case "--camera":
                cameraValues = Value().Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                if (cameraValues.Length != 5)
                    throw new FormatException("--camera needs x,z,yaw,pitch,distance.");
                break;
            case "--size":
                var size = Value().Split('x');
                (width, height) = (int.Parse(size[0]), int.Parse(size[1]));
                break;
            case "--exaggeration":
                settings = settings with { Exaggeration = double.Parse(Value(), System.Globalization.CultureInfo.InvariantCulture) };
                break;
            case "--markers":
                settings = settings with { Markers = MapCore.Rendering.RenderRequest.ParseOverlays(Value()) };
                break;
            case "--mesh-step":
                settings = settings with { MeshStep = int.Parse(Value()) };
                break;
            case "--unlit":
                settings = settings with { Lighting = false };
                break;
            case "--no-shadows":
                settings = settings with { Shadows = false };
                break;
            case "--no-water":
                settings = settings with { Water = false };
                break;
            case "--models":
                settings = settings with { Models = true };
                break;
            case "--no-bar-models":
                barModels = false;
                break;
            case "--hover-paths":
                settings = settings with { Passability = MapCore.Movement.BarMoveClasses.Hover };
                break;
            case "--passability" when i + 1 < options.Length:
            {
                string name = options[++i];
                var found = MapCore.Movement.BarMoveClasses.Overlays.FirstOrDefault(o => o.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase));
                settings = settings with
                {
                    Passability = found.Class ?? throw new FormatException($"--passability takes hovercraft, vehicles or bots, not '{name}'."),
                };
                break;
            }
            default:
                throw new FormatException($"Unknown option '{options[i]}'.");
        }
    }
    WithPackage(archive, package =>
    {
        var session = MapCore.Editing.MapEditSession.Open(package);
        using var layers = new MapCore.Rendering.MapRenderer(session);
        using var view = new MapCore.Rendering.TerrainView(layers)
        {
            BarFeatures = barModels ? MapCore.Assets.BarFeatureCatalog.Shipped(AppContext.BaseDirectory) : null,
        };
        var camera = cameraValues is { } c
            ? new MapCore.Rendering.OrbitCamera(c[0], c[1], c[2], c[3], c[4])
            : MapCore.Rendering.OrbitCamera.Overview(session.MapWidth, session.MapHeight, (double)width / height);
        var image = new RgbaImage(width, height);
        view.Render(image, camera, settings); // first frame builds the texture pyramid and shadows
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = view.Render(image, camera, settings);
        PngWriter.WriteRgba(png, image);
        Console.WriteLine($"camera {camera}; {sw.ElapsedMilliseconds} ms; {result.Labels.Count} labels (not drawn)");
        Console.WriteLine($"Wrote {Path.GetFullPath(png)}");
    });
    return 0;
}

static int Install(string archive, string[] options)
{
    bool overwrite = options.Contains("--overwrite"), allowSame = options.Contains("--allow-same-name");
    var folder = options.FirstOrDefault(o => !o.StartsWith("--", StringComparison.Ordinal)) ?? MapInstaller.CandidateFolders().FirstOrDefault()
        ?? throw new InvalidOperationException("BAR's maps folder was not found; give it: MapTool install <archive> <maps folder>.");
    if (MapInstaller.ReadIdentity(archive) is not (var name, var version))
        throw new InvalidDataException($"{archive} is not a map archive this tool can read.");
    var same = MapInstaller.FindSameMap(folder, name, version, except: Path.Combine(folder, Path.GetFileName(archive)));
    foreach (var other in same)
        Console.WriteLine($"{(allowSame ? "warning" : "error")}: {Path.GetFileName(other)} is also \"{name} {version}\"; BAR would list two maps under that name.");
    if (same.Count > 0 && !allowSame)
        throw new InvalidOperationException("Not copied. Give the map a new name or version, remove the other archive, or add --allow-same-name.");
    Console.WriteLine($"Copied \"{name} {version}\" to {MapInstaller.Install(archive, folder, overwrite)}");
    try
    {
        Console.WriteLine(MapInstaller.InstallLobbyPicture(archive, folder) is string picture
            ? $"Lobby picture: {picture}"
            : "warning: no lobby picture written (the map's minimap couldn't be read); the lobby will show none.");
    }
    catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
    {
        Console.WriteLine($"warning: the map is installed, but its lobby picture wasn't written ({ex.Message}); the lobby will show none.");
    }
    return 0;
}

static int PngDiff(string a, string b)
{
    var first = PngReader.Read(a).ToRgba();
    var second = PngReader.Read(b).ToRgba();
    if (first.Width != second.Width || first.Height != second.Height)
    {
        Console.WriteLine($"different sizes: {first.Width}x{first.Height} vs {second.Width}x{second.Height}");
        return 1;
    }
    int differing = 0, swapped = 0, worst = 0;
    for (int i = 0; i < first.Pixels.Length; i += 4)
    {
        int d = 0;
        for (int c = 0; c < 3; c++)
            d = Math.Max(d, Math.Abs(first.Pixels[i + c] - second.Pixels[i + c]));
        if (d > 0)
            differing++;
        worst = Math.Max(worst, d);
        if (first.Pixels[i] != second.Pixels[i + 2] || first.Pixels[i + 2] != second.Pixels[i])
            swapped++;
    }
    int total = first.Width * first.Height;
    Console.WriteLine($"{differing:N0} of {total:N0} pixels differ (largest channel difference {worst}); " +
                      $"with red and blue swapped, {swapped:N0} would differ in those channels");
    return differing == 0 ? 0 : 1;
}

static int Metal(string archive)
{
    WithPackage(archive, package =>
    {
        var metal = MapCore.Editing.MapEditSession.Open(package).Metal;
        var spots = metal.FindSpots();
        Console.WriteLine($"maxMetal {metal.MaxMetal}, extractorRadius {metal.ExtractorRadius}: T1 income = 0.001 x sum(raw x maxMetal) within the radius");
        Console.WriteLine($"{spots.Count} spots, total raw metal {metal.TotalRaw}, {spots.Sum(s => metal.Income(s)):0.##} metal/s with a T1 extractor on every spot");
        foreach (var spot in spots)
            Console.WriteLine($"  ({spot.X,6}, {spot.Z,6})  {spot.Cells.Count,2} cells  raw {spot.RawSum,5}  {metal.Income(spot),6:0.###} metal/s");
        foreach (var warning in metal.Warnings)
            Console.WriteLine($"note: {warning}");
    });
    return 0;
}

static void PrintSmf(SmfFile smf, string name)
{
    Console.WriteLine($"Map file     : {name}");
    Console.WriteLine($"Size         : {smf.MapX}x{smf.MapY} squares = {smf.MapX * SmfFile.SquareSize}x{smf.MapY * SmfFile.SquareSize} game units ({smf.MapX / 64.0:0.##}x{smf.MapY / 64.0:0.##} map units)");
    Console.WriteLine($"Height range : {smf.MinHeight} to {smf.MaxHeight} (SMF header; mapinfo.lua may override)");
    Console.WriteLine($"Map id       : {smf.MapId}");
    Console.WriteLine($"Tile files   : {string.Join(", ", smf.TileFiles.Select(f => $"{f.FileName} ({f.TileCount} tiles)"))}");
    Console.WriteLine($"Tile index   : {smf.TileColumns}x{smf.TileRows}, {smf.TileIndex.Distinct().Count()} distinct tiles used");
    Console.WriteLine($"Metal map    : {smf.MetalMap.Count(b => b != 0)} non-zero cells, highest value {smf.MetalMap.Max()}");
    Console.WriteLine($"Grass map    : {(smf.GrassMap == null ? "none" : $"{smf.GrassMap.Count(b => b != 0)} non-zero cells")}");
    var minimap = BlockCompression.Decode(BlockFormat.Dxt1, smf.Minimap, 1024, 1024);
    Console.WriteLine($"Minimap      : 1024x1024 DXT1, {TransparentPixels(minimap):N0} transparent pixels");
    Console.WriteLine($"Features     : {smf.Features.Count} placed, {smf.FeatureTypes.Count} types defined");
    foreach (var group in smf.Features.GroupBy(f => f.TypeIndex).OrderBy(g => g.Key))
        Console.WriteLine($"    {smf.FeatureTypes.ElementAtOrDefault(group.Key) ?? $"<type {group.Key}>"}: {group.Count()}");
    Console.WriteLine("Layout       :");
    foreach (var s in smf.SourceLayout)
        Console.WriteLine($"    {s.Section,-10} offset {s.Offset,10:N0}   length {s.Length,10:N0}");
    foreach (var warning in smf.Warnings)
        Console.WriteLine($"warning: {warning}");
}

static int DdsInfo(string path, string[] options)
{
    string? png = null;
    int mip = 0, face = 0;
    bool noAlpha = false;
    for (int i = 0; i < options.Length; i++)
    {
        switch (options[i])
        {
            case "--png" when i + 1 < options.Length:
                png = options[++i];
                break;
            case "--mip" when i + 1 < options.Length:
                mip = ParseCount(options[++i], "--mip");
                break;
            case "--face" when i + 1 < options.Length:
                face = ParseCount(options[++i], "--face");
                break;
            case "--no-alpha":
                noAlpha = true;
                break;
            default:
                throw new InvalidOperationException($"Unknown or incomplete option '{options[i]}'.");
        }
    }

    var dds = DdsFile.Load(path);
    Console.WriteLine($"Format  : {dds.FormatName}");
    Console.WriteLine($"Size    : {dds.Width}x{dds.Height}{(dds.IsCubemap ? ", cube map (6 faces)" : dds.FaceCount > 1 ? $", {dds.FaceCount} faces" : "")}");
    Console.WriteLine($"Mips    : {dds.MipCount} ({string.Join(", ", Enumerable.Range(0, dds.MipCount).Select(m => $"{dds.MipWidth(m)}x{dds.MipHeight(m)}"))})");
    Console.WriteLine($"Editable: {(dds.IsEditable ? "yes" : "no")}");
    if (dds.Format == DdsFormat.Dxt1)
    {
        // DXT1's 3-colour mode can mark pixels transparent; editing tools must know whether a texture uses it.
        long transparent = Enumerable.Range(0, dds.FaceCount).Sum(f => (long)TransparentPixels(dds.Decode(0, f)));
        Console.WriteLine($"Transparent pixels (mip 0, all faces): {transparent:N0}");
    }
    foreach (var warning in dds.Warnings)
        Console.WriteLine($"warning: {warning}");

    if (png != null)
    {
        var image = dds.Decode(mip, face);
        if (noAlpha)
            for (int i = 3; i < image.Pixels.Length; i += 4)
                image.Pixels[i] = 255;
        PngWriter.WriteRgba(png, image);
        Console.WriteLine($"Wrote mip {mip}, face {face} ({image.Width}x{image.Height}) to {Path.GetFullPath(png)}");
    }
    return 0;
}

static int TransparentPixels(RgbaImage image)
{
    int count = 0;
    for (int i = 3; i < image.Pixels.Length; i += 4)
        if (image.Pixels[i] < 128)
            count++;
    return count;
}

static int ParseCount(string text, string option) =>
    int.TryParse(text, out int value) && value >= 0
        ? value
        : throw new InvalidOperationException($"{option} needs a whole number, not '{text}'.");

static int Extract(string archive, string folder)
{
    var package = MapPackage.Open(archive, folder);
    Console.WriteLine($"Extracted to {package.Root}");
    Console.WriteLine($"Map file    : {package.SmfEntry}");
    return 0;
}

static int Pack(string folder, string archive, bool overwrite)
{
    MapPackage.OpenFolder(folder).SaveAs(archive, overwrite);
    Console.WriteLine($"Wrote {Path.GetFullPath(archive)}");
    return 0;
}

static int Repack(string source, string target, bool overwrite)
{
    var workspace = FileSystemUtil.NewTempDir("repack");
    try
    {
        MapPackage.Open(source, workspace).SaveAs(target, overwrite);
    }
    finally
    {
        FileSystemUtil.DeleteTree(workspace);
    }
    Console.WriteLine($"Wrote {Path.GetFullPath(target)}");
    return Compare(source, target);
}

static int Compare(string a, string b)
{
    // Attributes only survive between two 7z archives; zip does not keep hidden/system flags.
    bool attributes = ArchiveIO.FormatOf(a) == ArchiveFormat.SevenZip && ArchiveIO.FormatOf(b) == ArchiveFormat.SevenZip;
    var diffs = ArchiveComparer.Diff(ArchiveIO.List(a), ArchiveIO.List(b), attributes);
    foreach (var d in diffs)
        Console.WriteLine(d);
    Console.WriteLine(diffs.Count == 0
        ? $"Contents identical (paths, sizes, CRCs{(attributes ? ", attributes" : "")})."
        : $"{diffs.Count} difference(s).");
    return diffs.Count == 0 ? 0 : 1;
}

static bool Overwrite(string[] args)
{
    if (args.Length < 4)
        return false;
    if (args[3] == "--overwrite")
        return true;
    throw new InvalidOperationException($"Unknown option '{args[3]}'.");
}

static string AttributeLetters(FileAttributes a) =>
    (a.HasFlag(FileAttributes.ReadOnly) ? "R" : "") + (a.HasFlag(FileAttributes.Hidden) ? "H" : "") +
    (a.HasFlag(FileAttributes.System) ? "S" : "") + (a.HasFlag(FileAttributes.Archive) ? "A" : "");

static int BadArgs(string command)
{
    Console.Error.WriteLine($"Unknown command or wrong arguments for '{command}'. Run 'MapTool help'.");
    return 2;
}
