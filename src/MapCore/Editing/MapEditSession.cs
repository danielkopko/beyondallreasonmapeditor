using System.Globalization;
using MapCore.Heights;
using MapCore.Imaging;
using MapCore.Items;
using MapCore.Metal;
using MapCore.Smf;
using MapCore.Textures;

namespace MapCore.Editing;

/// <summary>
/// One editing session on an opened map package. The SMF is loaded once and shared by every layer, and written
/// once by <see cref="Save"/> (see PLAN.md: one SmfFile per session). Operations can be applied as text, which is
/// what <c>MapTool edit</c> uses:
/// <code>
///   set:&lt;path&gt;=&lt;value&gt;                       mapinfo.lua value (see MapInfo.SetFromText)
///   unset:&lt;path&gt;                              removes a mapinfo.lua value (a key and what it holds)
///   start-move:&lt;team&gt;:&lt;x&gt;,&lt;z&gt;              start position
///   metal-move:&lt;x&gt;,&lt;z&gt;:&lt;x2&gt;,&lt;z2&gt;[:look]     the spot nearest (x, z), within 64 units; look: its look moves too
///   metal-add:&lt;x&gt;,&lt;z&gt;[:&lt;income&gt;]          a spot shaped like the map's typical spot
///   metal-remove:&lt;x&gt;,&lt;z&gt;
///   metal-income:&lt;x&gt;,&lt;z&gt;:&lt;income&gt;         metal/s for a T1 extractor
///   metal-resize:&lt;x&gt;,&lt;z&gt;:&lt;cells across&gt;     n x n square, same income
///   item-move:&lt;x&gt;,&lt;z&gt;:&lt;x2&gt;,&lt;z2&gt;             the item nearest (x, z), within 64 units
///   item-rotate:&lt;x&gt;,&lt;z&gt;:&lt;rotation&gt;
///   item-add:&lt;smf|placer|unit|building&gt;:&lt;name&gt;:&lt;x&gt;,&lt;z&gt;[:&lt;rotation&gt;]   unit and building: the featureplacer's lists
///   item-remove:&lt;x&gt;,&lt;z&gt;
///   height-raise:&lt;x&gt;,&lt;z&gt;:&lt;radius&gt;:&lt;amount&gt;[:&lt;hardness&gt;]      negative amount lowers
///   height-flatten:&lt;x&gt;,&lt;z&gt;:&lt;radius&gt;:&lt;height&gt;[:&lt;hardness&gt;[:&lt;strength&gt;]]
///   height-smooth:&lt;x&gt;,&lt;z&gt;:&lt;radius&gt;[:&lt;strength&gt;[:&lt;hardness&gt;]]
///   height-noise:&lt;x&gt;,&lt;z&gt;:&lt;radius&gt;:&lt;amplitude&gt;[:&lt;hardness&gt;[:&lt;feature size&gt;[:&lt;mirror&gt;]]]   mirror: the pattern read mirrored (MapMirror)
///   height-offset:&lt;amount&gt;                                  whole map
///   height-scale:&lt;factor&gt;                                   whole map, sea level stays
///   asset-add:&lt;asset id&gt;                                  copy a library asset's files in (MapEditSession.Assets.cs)
///   ramp:&lt;x1&gt;,&lt;z1&gt;:&lt;x2&gt;,&lt;z2&gt;:&lt;width&gt;:&lt;h1&gt;,&lt;h2&gt;[:&lt;edge&gt;[:&lt;fill&gt;]]   see MapEditSession.Ramp.cs
///   texture-paint:&lt;x&gt;,&lt;z&gt;:&lt;radius&gt;:&lt;r&gt;,&lt;g&gt;,&lt;b&gt;[:&lt;hardness&gt;[:&lt;strength&gt;]]
///   texture-tint:&lt;x&gt;,&lt;z&gt;:&lt;radius&gt;:&lt;r&gt;,&lt;g&gt;,&lt;b&gt;[:&lt;hardness&gt;[:&lt;strength&gt;]]       keeps brightness
///   texture-adjust:&lt;x&gt;,&lt;z&gt;:&lt;radius&gt;:&lt;brightness&gt;,&lt;saturation&gt;,&lt;hue°&gt;[:&lt;hardness&gt;[:&lt;strength&gt;]]
///   texture-clone:&lt;x&gt;,&lt;z&gt;:&lt;radius&gt;:&lt;dx&gt;,&lt;dz&gt;[:&lt;hardness&gt;[:&lt;strength&gt;]]        copy from (x+dx, z+dz)
///   splat-paint:&lt;x&gt;,&lt;z&gt;:&lt;radius&gt;:&lt;r&gt;,&lt;g&gt;,&lt;b&gt;,&lt;a&gt;[:&lt;hardness&gt;[:&lt;strength&gt;]]  detail texture weights
///   items-move / items-rotate / items-heading / items-remove, grass-paint: see MapEditSession.Items.cs
///   wm-import:&lt;layer&gt;[;options]|&lt;file&gt;                 a World Machine output brought back (MapEditSession.WorldMachine.cs)
///   beautify-texture:&lt;settings&gt;                         the texture painted by its terrain (MapEditSession.Beautify.cs)
///   beautify-heights:&lt;settings&gt;                         cliffs roughened, keeping where units and buildings go (MapEditSession.CliffDetail.cs)
///   import:&lt;layer&gt;=&lt;file.png&gt;                               heights, texture, metal, grass, resource:&lt;key&gt;
/// </code>
/// </summary>
public sealed partial class MapEditSession
{
    const double PickDistance = 64;
    const double DefaultHardness = 0.5;

    HeightMap? heights;

    /// <summary>The terrain heights, loaded on first use from the session's SMF.</summary>
    public HeightMap Heights => heights ??= HeightMap.Load(Smf, Info);

    MapEditSession(MapPackage package, SmfFile smf, MapInfo info, MapItems items, MetalLayer metal)
    {
        Package = package;
        Smf = smf;
        Info = info;
        Items = items;
        Metal = metal;
    }

    public MapPackage Package { get; }

    /// <summary>The session's single SMF instance; every layer edits this one.</summary>
    public SmfFile Smf { get; }

    public MapInfo Info { get; }
    public MapItems Items { get; }
    public MetalLayer Metal { get; }

    public static MapEditSession Open(MapPackage package)
    {
        var smf = package.LoadSmf();
        var info = package.LoadMapInfo();
        var (warnings, readOnly) = MetalNotes(package, info, smf);
        return new MapEditSession(package, smf, info, package.LoadItems(smf), new MetalLayer(smf, info, warnings, readOnly))
        {
            LoadedName = info.Name,
            LoadedVersion = info.Version,
        };
    }

    /// <summary>
    /// Writes every edited store. Height edits first update the detail normal map (when it bakes in the slopes)
    /// and are quantised into the SMF; then texture tiles and edited .dds resources are written, then the SMF once,
    /// then mapinfo.lua and the featureplacer file. Returns notes on what was done beyond the plain edits.
    /// Raises <see cref="Changed"/> for what saving itself changed (the rotated normal map, a widened height range).
    /// </summary>
    public IReadOnlyList<string> Save()
    {
        var notes = new List<string>();
        bool heightsWritten = heights?.HasChanges == true;
        if (heights != null && heightsWritten)
        {
            if (UpdateDetailNormals(heights) is string note)
                notes.Add(note);
            // The minimap shows the ground's look, water included: redraw it where the ground moved.
            var changed = heights.ChangedRegion;
            double perSquare = Texture.Width / (double)Smf.MapX;
            RebuildMinimap(PixelRect.FromEdges((int)((changed.X - 1) * perSquare), (int)((changed.Y - 1) * perSquare),
                (int)((changed.Right + 1) * perSquare), (int)((changed.Bottom + 1) * perSquare)).Intersect(Texture.Bounds));
            notes.Add("minimap redrawn over the reshaped ground");
            var result = heights.Write();
            notes.Add($"heights: {result.VerticesWritten:N0} vertices stored" +
                      (result.RangeChanged ? $"; height range widened to {result.MinHeight} .. {result.MaxHeight}" : ""));
            heights.AcceptChanges();
        }
        SaveTextureAndResources();
        Smf.Save(Package.SmfPath);
        Package.SaveMapInfo(Info);
        Items.Placer?.Save(Package.Root);
        savedChangeCount = changeCount;
        Journal?.Write(new JournalEntry("save"));
        if (heightsWritten)
            Changed?.Invoke(this, new MapChange(MapLayers.Resources | MapLayers.MapInfo | MapLayers.Minimap, null));
        return notes;
    }

    /// <summary>Rotates the detail normal map to follow the height edits (when it bakes in the slopes); returns a note, if any.</summary>
    string? UpdateDetailNormals(HeightMap changedHeights)
    {
        if (Info.Resource("detailNormalTex") is not string name)
            return null;
        var entry = Package.ResolveResource(name);
        if (entry == null || !entry.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            return $"detail normal map '{name}' was not updated (only .dds files in the package are handled)";
        DdsFile dds;
        try
        {
            dds = ResourceByEntry(entry);
        }
        catch (InvalidOperationException ex)
        {
            return $"detail normal map was not updated: {ex.Message}";
        }
        if (!DetailNormalMap.Update(dds, changedHeights, out double score))
            return $"detail normal map {entry} left as is: it holds detail only (slope-bake score {score:0.00}), and the engine's own normals follow the heights";
        changedResources.Add(entry);
        ForgetResourceEdit(entry);
        return $"detail normal map {entry} rotated to follow the new slopes (slope-bake score {score:0.00})";
    }

    /// <summary>Applies one text operation (see the class summary) and describes what changed.</summary>
    /// <exception cref="InvalidOperationException">The operation is malformed or cannot be applied.</exception>
    string ApplyCore(string operation)
    {
        int colon = operation.IndexOf(':');
        if (colon <= 0)
            throw new InvalidOperationException($"'{operation}' is not an operation (expected name:arguments).");
        string name = operation[..colon], rest = operation[(colon + 1)..];
        var args = rest.Split(':');

        switch (name)
        {
            case "import":
            {
                // import:<layer>=<file.png>; the layer may itself contain ':' (resource:<key>), paths may contain ':' (C:\).
                int eq = rest.IndexOf('=');
                if (eq <= 0)
                    throw new InvalidOperationException("import needs <layer>=<file.png>.");
                return ImportLayerCore(rest[..eq], rest[(eq + 1)..]);
            }
            case "texture-paint" or "texture-tint" or "texture-adjust" or "texture-clone" or "splat-paint":
            {
                if (args.Length is < 3 or > 5)
                    throw new InvalidOperationException($"{name} needs <x>,<z>:<radius>:<values>[:<hardness>[:<strength>]].");
                var brush = MakeBrush(args[0], args[1], args.Length >= 4 ? args[3] : null);
                double strength = args.Length == 5 ? Number(args[4]) : 1;
                var values = args[2].Split(',');
                void Count(int expected, string what)
                {
                    if (values.Length != expected)
                        throw new InvalidOperationException($"'{args[2]}' is not {what}.");
                }
                switch (name)
                {
                    case "texture-paint" or "texture-tint":
                        Count(3, "a colour (expected r,g,b from 0 to 255)");
                        var colour = new Imaging.Rgba(Channel(values[0]), Channel(values[1]), Channel(values[2]), 255);
                        return name == "texture-paint" ? PaintTextureCore(brush, colour, strength) : TintTextureCore(brush, colour, strength);
                    case "texture-adjust":
                        Count(3, "brightness,saturation,hue (e.g. 1.2,1,0)");
                        return AdjustTextureCore(brush, Number(values[0]), Number(values[1]), Number(values[2]), strength);
                    case "texture-clone":
                        Count(2, "an offset (dx,dz in game units)");
                        return CloneTextureCore(brush, Number(values[0]), Number(values[1]), strength);
                    default:
                        Count(4, "splat weights (r,g,b,a from 0 to 255)");
                        return PaintSplatCore(brush, new Imaging.Rgba(Channel(values[0]), Channel(values[1]), Channel(values[2]), Channel(values[3])), strength);
                }
            }
            case "set":
            {
                int eq = rest.IndexOf('=');
                if (eq <= 0)
                    throw new InvalidOperationException($"set needs <path>=<value>, got '{rest}'.");
                string path = rest[..eq];
                var before = Lua.LuaDocument.DisplayText(Info.Get(path));
                Info.SetFromText(path, rest[(eq + 1)..]);
                return $"{path}: {before} -> {Lua.LuaDocument.DisplayText(Info.Get(path))}";
            }
            case "unset":
            {
                if (rest.Length == 0 || rest.Contains('='))
                    throw new InvalidOperationException($"unset needs a mapinfo.lua path, got '{rest}'.");
                var before = Info.Get(rest) ?? throw new InvalidOperationException($"mapinfo.lua has no {rest}.");
                Info.Document.Remove(rest);
                return $"{rest}: {Lua.LuaDocument.DisplayText(before)} removed";
            }
            case "start-move":
            {
                Expect(args, 2, name);
                int team = int.Parse(args[0], CultureInfo.InvariantCulture);
                var (x, z) = Point(args[1]);
                Info.SetStartPosition(team, x, z);
                return $"start position of team {team} -> ({x}, {z})";
            }
            case "metal-move":
            {
                if (args.Length is < 2 or > 3 || (args.Length == 3 && args[2] != "look"))
                    throw new InvalidOperationException("metal-move needs <x>,<z>:<new x>,<new z>[:look].");
                var spot = PickSpot(args[0]);
                var (x, z) = Point(args[1]);
                var plan = args.Length == 3 ? PlanLook(spot, x, z) : null; // from the map before the move, as the undo capture saw it
                var moved = Metal.MoveSpot(spot, x, z);
                string result = $"metal spot ({spot.X}, {spot.Z}) -> ({moved.X}, {moved.Z})";
                if (plan != null)
                    result += "; " + MoveLookCore(plan, LookRadius.How);
                return result;
            }
            case "metal-add":
            {
                if (args.Length is < 1 or > 2)
                    throw new InvalidOperationException("metal-add needs <x>,<z>[:<income>].");
                var (x, z) = Point(args[0]);
                var spot = Metal.AddSpot(x, z, Metal.TypicalPattern());
                if (args.Length == 2)
                    spot = Metal.SetIncome(spot, Number(args[1]));
                return $"metal spot added at ({spot.X}, {spot.Z}), {Metal.Income(spot):0.###} metal/s";
            }
            case "metal-remove":
            {
                Expect(args, 1, name);
                var spot = PickSpot(args[0]);
                Metal.RemoveSpot(spot);
                return $"metal spot ({spot.X}, {spot.Z}) removed";
            }
            case "metal-income":
            {
                Expect(args, 2, name);
                var spot = PickSpot(args[0]);
                double before = Metal.Income(spot);
                var updated = Metal.SetIncome(spot, Number(args[1]));
                return $"metal spot ({spot.X}, {spot.Z}): {before:0.###} -> {Metal.Income(updated):0.###} metal/s";
            }
            case "metal-resize":
            {
                Expect(args, 2, name);
                var spot = PickSpot(args[0]);
                int across = (int)Number(args[1]);
                var resized = Metal.ResizeSpot(spot, across);
                return $"metal spot ({spot.X}, {spot.Z}) -> {across}x{across} at ({resized.X}, {resized.Z}), {Metal.Income(resized):0.###} metal/s";
            }
            case "item-move":
            {
                Expect(args, 2, name);
                var item = PickItem(args[0]);
                var (x, z) = Point(args[1]);
                Items.Move(item, x, z);
                return $"{item.Name} ({item.X}, {item.Z}) -> ({x}, {z})";
            }
            case "item-rotate":
            {
                Expect(args, 2, name);
                var item = PickItem(args[0]);
                Items.SetRotation(item, Number(args[1]));
                return $"{item.Name} at ({item.X}, {item.Z}) rotation {item.Rotation} -> {Number(args[1])}";
            }
            case "item-add":
            {
                if (args.Length is < 3 or > 4)
                    throw new InvalidOperationException("item-add needs <smf|placer|unit|building>:<name>:<x>,<z>[:<rotation>].");
                var (source, kind) = args[0] switch
                {
                    "smf" => (ItemSource.Smf, PlacedItemKind.Feature),
                    "placer" => (ItemSource.FeaturePlacer, PlacedItemKind.Feature),
                    "unit" => (ItemSource.FeaturePlacer, PlacedItemKind.Unit),
                    "building" => (ItemSource.FeaturePlacer, PlacedItemKind.Building),
                    _ => throw new InvalidOperationException($"item-add: '{args[0]}' is not smf, placer, unit or building."),
                };
                var (x, z) = Point(args[2]);
                var added = Items.Add(source, kind, args[1], x, z, args.Length == 4 ? Number(args[3]) : 0);
                return $"{added.Name} added to {(source == ItemSource.Smf ? "the SMF feature list" : Items.Placer!.Entry + (kind == PlacedItemKind.Feature ? "" : $" ({kind.ToString().ToLowerInvariant()}list)"))} at ({x}, {z})";
            }
            case "item-remove":
            {
                Expect(args, 1, name);
                var item = PickItem(args[0]);
                Items.Remove(item);
                return $"{item.Name} at ({item.X}, {item.Z}) removed";
            }
            case "height-raise" or "height-flatten":
            {
                int most = name == "height-raise" ? 4 : 5;
                if (args.Length < 3 || args.Length > most)
                    throw new InvalidOperationException(name == "height-raise"
                        ? "height-raise needs <x>,<z>:<radius>:<amount>[:<hardness>]."
                        : "height-flatten needs <x>,<z>:<radius>:<height>[:<hardness>[:<strength>]].");
                var brush = MakeBrush(args[0], args[1], args.Length >= 4 ? args[3] : null);
                double value = Number(args[2]);
                var touched = name == "height-raise"
                    ? Heights.Raise(brush, value)
                    : Heights.Flatten(brush, value, args.Length == 5 ? Number(args[4]) : 1);
                return $"{name} at ({brush.X}, {brush.Z}) radius {brush.Radius}: {touched.Width * touched.Height:N0} vertices, " +
                       $"height there now {Heights.HeightAt(brush.X, brush.Z):0.#}";
            }
            case "height-smooth":
            {
                if (args.Length is < 2 or > 4)
                    throw new InvalidOperationException("height-smooth needs <x>,<z>:<radius>[:<strength>[:<hardness>]].");
                var brush = MakeBrush(args[0], args[1], args.Length == 4 ? args[3] : null);
                var touched = Heights.Smooth(brush, args.Length >= 3 ? Number(args[2]) : 1);
                return $"height-smooth at ({brush.X}, {brush.Z}) radius {brush.Radius}: {touched.Width * touched.Height:N0} vertices";
            }
            case "height-noise":
            {
                if (args.Length is < 3 or > 6)
                    throw new InvalidOperationException("height-noise needs <x>,<z>:<radius>:<amplitude>[:<hardness>[:<feature size>[:<mirror>]]].");
                var brush = MakeBrush(args[0], args[1], args.Length >= 4 ? args[3] : null);
                double size = args.Length >= 5 ? Number(args[4]) : 64;
                if (size <= 0)
                    throw new InvalidOperationException($"Noise feature size must be positive, not {size}.");
                var mirrored = args.Length == 6 ? new MapMirror(MapMirror.ParseKind(args[5]), MapWidth, MapHeight) : (MapMirror?)null;
                var touched = Heights.Noise(brush, Number(args[2]), size, mirrored);
                return $"height-noise at ({brush.X}, {brush.Z}) radius {brush.Radius}: {touched.Width * touched.Height:N0} vertices";
            }
            case "height-offset":
            {
                Expect(args, 1, name);
                Heights.Offset(Number(args[0]));
                return $"all heights {Number(args[0]):+0.##;-0.##}";
            }
            case "height-scale":
            {
                Expect(args, 1, name);
                Heights.Scale(Number(args[0]));
                return $"all heights x {Number(args[0])}";
            }
            case "items-move" or "items-rotate" or "items-heading" or "items-remove" or "grass-paint":
                return ItemsOperation(name, args);
            case "ramp":
                return RampCore(args);
            case "asset-add":
                return AssetAddCore(args);
            case "feature-custom":
                return CustomFeatureCore(args);
            case "beautify-texture":
                return BeautifyTextureCore(args);
            case "wm-import":
                return WorldMachineImportCore(rest);
            case "beautify-heights":
                return BeautifyHeightsCore(args);
            case "structure-kind" or "structure-change" or "structure-add" or "structure-move" or "structure-turn" or "structure-broken" or "structure-remove"
                or "structure-marker-add" or "structure-marker-move" or "structure-marker-remove"
                or "structure-zone" or "structure-targets" or "structure-rule":
                return StructureCore(name, args);
            default:
                throw new InvalidOperationException($"Unknown operation '{name}'.");
        }
    }

    static byte Channel(string text)
    {
        double v = Number(text);
        return v is >= 0 and <= 255 ? (byte)Math.Round(v) : throw new InvalidOperationException($"Colour value {v} is outside 0-255.");
    }

    static Brush MakeBrush(string point, string radius, string? hardness)
    {
        var (x, z) = Point(point);
        double r = Number(radius);
        if (r <= 0)
            throw new InvalidOperationException($"Brush radius must be positive, not {r}.");
        double h = hardness == null ? DefaultHardness : Number(hardness);
        if (h is < 0 or > 1)
            throw new InvalidOperationException($"Brush hardness must be between 0 and 1, not {h}.");
        return new Brush(x, z, r, h);
    }

    MetalSpot PickSpot(string point)
    {
        var (x, z) = Point(point);
        return Metal.SpotNear(x, z, PickDistance)
            ?? throw new InvalidOperationException($"No metal spot within {PickDistance} units of ({x}, {z}).");
    }

    MapItem PickItem(string point)
    {
        var (x, z) = Point(point);
        return Items.All.Where(i => Math.Sqrt((i.X - x) * (i.X - x) + (i.Z - z) * (i.Z - z)) <= PickDistance)
                   .MinBy(i => (i.X - x) * (i.X - x) + (i.Z - z) * (i.Z - z))
               ?? throw new InvalidOperationException($"No item within {PickDistance} units of ({x}, {z}).");
    }

    static (double X, double Z) Point(string text)
    {
        var parts = text.Split(',');
        if (parts.Length != 2)
            throw new InvalidOperationException($"'{text}' is not a position (expected x,z).");
        return (Number(parts[0]), Number(parts[1]));
    }

    static double Number(string text) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value)
            ? value
            : throw new InvalidOperationException($"'{text}' is not a number.");

    static void Expect(string[] args, int count, string name)
    {
        if (args.Length != count)
            throw new InvalidOperationException($"{name} takes {count} argument(s) separated by ':'.");
    }

    /// <summary>What may stop metal edits from showing in game, and whether edits must be refused.</summary>
    static (IReadOnlyList<string> Warnings, string? ReadOnly) MetalNotes(MapPackage package, MapInfo info, SmfFile smf)
    {
        var warnings = new List<string>();
        string? readOnly = null;
        if (info.TextureOverrides.TryGetValue("metalmapTex", out var image))
        {
            readOnly = $"mapinfo.lua sets smf.metalmapTex = \"{image}\"; the game takes metal from that image, so the SMF metal map is not used.";
            warnings.Add(readOnly);
        }
        bool hasMetal = smf.MetalMap.Any(v => v != 0);
        if (File.Exists(Path.Combine(package.Root, "mapconfig", "map_metal_layout.lua")))
            warnings.Add(hasMetal
                ? "mapconfig/map_metal_layout.lua is ignored by BAR, because the SMF metal map already has metal."
                : "BAR places this map's metal from mapconfig/map_metal_layout.lua because the SMF metal map is empty; adding metal here switches that off.");
        var layer = new MetalLayer(smf, info);
        int edge = layer.EdgeCellsWithMetal();
        if (edge > 0)
            warnings.Add($"{edge} metal cells lie within 16 units of the map edge; BAR's spot finder ignores them.");
        return (warnings, readOnly);
    }
}
