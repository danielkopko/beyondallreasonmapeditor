using MapCore.Lua;

namespace MapCore.Items;

/// <summary>Which list of the featureplacer config an entry belongs to.</summary>
public enum PlacedItemKind
{
    /// <summary><c>objectlist</c>: features; <c>rot</c> is a heading (65536 per full turn).</summary>
    Feature,
    /// <summary><c>unitlist</c>: neutral Gaia units; <c>rot</c> is a heading like features.</summary>
    Unit,
    /// <summary><c>buildinglist</c>: neutral Gaia buildings; <c>rot</c> is a facing, 0-3 (south, east, north, west).</summary>
    Building,
}

/// <summary>One entry of a featureplacer list. <see cref="Index"/> is its position in that list, counting from 1 as Lua does.</summary>
public sealed record PlacedItem(PlacedItemKind Kind, int Index, string Name, double X, double Z, double Rotation);

public static class Rotation
{
    /// <summary>
    /// Brings a rotation into the range the engine handles: headings (features, units) wrap into
    /// -32768..32767 — the engine casts them to a 16-bit value, and out-of-range casts are undefined —
    /// and building facings wrap into 0..3.
    /// </summary>
    public static double Normalize(PlacedItemKind kind, double rotation)
    {
        if (kind == PlacedItemKind.Building)
            return (((long)Math.Round(rotation) % 4) + 4) % 4;
        double wrapped = (Math.Round(rotation) + 32768) % 65536;
        return (wrapped < 0 ? wrapped + 65536 : wrapped) - 32768;
    }
}

/// <summary>
/// mapconfig/featureplacer: the features, units and buildings that the map's own LuaGaia gadget
/// (LuaGaia/Gadgets/FP_featureplacer.lua) creates when a game starts — features at ground height + 5.
/// config.lua either returns the lists itself or, as Acidic Quarry's does, <c>return VFS.Include("…/set.lua")</c>;
/// the included file is the one read and edited. Edits change only the touched text.
/// </summary>
public sealed class FeaturePlacerConfig
{
    public const string ConfigEntry = "mapconfig/featureplacer/config.lua";

    static readonly Dictionary<PlacedItemKind, string> ListNames = new()
    {
        [PlacedItemKind.Feature] = "objectlist",
        [PlacedItemKind.Unit] = "unitlist",
        [PlacedItemKind.Building] = "buildinglist",
    };

    FeaturePlacerConfig(string entry, LuaDocument document)
    {
        Entry = entry;
        Document = document;
    }

    /// <summary>Package path of the file holding the lists.</summary>
    public string Entry { get; }

    public LuaDocument Document { get; }

    /// <summary>The config, or null when the map has no mapconfig/featureplacer/config.lua.</summary>
    public static FeaturePlacerConfig? Load(string packageRoot)
    {
        var configPath = Path.Combine(packageRoot, ConfigEntry);
        if (!File.Exists(configPath))
            return null;
        var entry = IncludedFile(File.ReadAllText(configPath)) ?? ConfigEntry;
        var path = Path.Combine(packageRoot, entry);
        if (!File.Exists(path))
            throw new InvalidDataException($"{ConfigEntry} includes '{entry}', which is not in the package.");
        return new FeaturePlacerConfig(entry, LuaDocument.Load(path));
    }

    public void Save(string packageRoot) => Document.Save(Path.Combine(packageRoot, Entry));

    public IReadOnlyList<PlacedItem> Items =>
        ListNames.SelectMany(list => (Document.Get(list.Value) as LuaTable)?.Fields
                .Where(f => f.Position != null && f.Value is LuaTable)
                .Select(f => ToItem(list.Key, f.Position!.Value, (LuaTable)f.Value))
            ?? [])
        .ToList();

    public void Move(PlacedItem item, double x, double z)
    {
        Document.SetNumber(PathOf(item, "x"), x);
        Document.SetNumber(PathOf(item, "z"), z);
    }

    /// <summary>Sets the rotation, brought into range by <see cref="Rotation.Normalize"/>.</summary>
    public void SetRotation(PlacedItem item, double rotation) =>
        Document.SetNumber(PathOf(item, "rot"), Rotation.Normalize(item.Kind, rotation));

    /// <summary>Appends an entry in the list's own style, e.g. <c>{ name = "rock", x = 100, z = 200, rot = 0 },</c>.</summary>
    public PlacedItem Add(PlacedItemKind kind, string name, double x, double z, double rotation)
    {
        rotation = Rotation.Normalize(kind, rotation);
        var list = ListNames[kind];
        if (Document.Get(list) is not LuaTable)
            Document.SetLiteral(list, "{}");
        string entry = $"{{ name = {LuaDocument.QuoteString(name)}, x = {LuaDocument.FormatNumber(x)}, " +
                       $"z = {LuaDocument.FormatNumber(z)}, rot = {LuaDocument.FormatNumber(rotation)} }}";
        int index = Document.Append(list, entry);
        return new PlacedItem(kind, index, name, x, z, rotation);
    }

    /// <summary>Removes an entry; later entries in the same list move up by one.</summary>
    public void Remove(PlacedItem item) => Document.Remove($"{ListNames[item.Kind]}.{item.Index}");

    static string PathOf(PlacedItem item, string key) => $"{ListNames[item.Kind]}.{item.Index}.{key}";

    static PlacedItem ToItem(PlacedItemKind kind, int index, LuaTable entry) => new(
        kind, index,
        (entry["name"] as LuaString)?.Value ?? "",
        (entry["x"] as LuaNumber)?.Value ?? 0,
        (entry["z"] as LuaNumber)?.Value ?? 0,
        (entry["rot"] as LuaNumber)?.Value ?? 0);

    /// <summary>The path in <c>return VFS.Include("path")</c>, if that is what the file returns.</summary>
    static string? IncludedFile(string source)
    {
        var tokens = LuaLexer.Tokenize(source);
        int r = tokens.FindLastIndex(t => t.Kind == LuaTokenKind.Name && t.Text == "return");
        if (r < 0 || r + 5 >= tokens.Count)
            return null;
        bool include = tokens[r + 1].Is("VFS") && tokens[r + 2].Is(".") && tokens[r + 3].Is("Include") &&
                       tokens[r + 4].Is("(") && tokens[r + 5].Kind == LuaTokenKind.String;
        return include ? ((string)tokens[r + 5].Value!).Replace('\\', '/') : null;
    }
}
