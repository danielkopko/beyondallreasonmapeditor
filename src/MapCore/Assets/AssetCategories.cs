using System.Text.RegularExpressions;

namespace MapCore.Assets;

/// <summary>What an asset is, as far as copying it goes.</summary>
public enum AssetKind
{
    /// <summary>Defined by the map's own files (a features/*.lua with its model and textures): copied into a map to use it.</summary>
    MapFeature,

    /// <summary>A feature the game (BAR) defines; maps only name it, so it can be placed on any map by name.</summary>
    GameFeature,

    /// <summary>One of the engine's default features (treetypeN, geovent): placeable on any map by name.</summary>
    EngineFeature,

    /// <summary>A BAR unit placed by a map's featureplacer (unitlist).</summary>
    Unit,

    /// <summary>A BAR building placed by a map's featureplacer (buildinglist).</summary>
    Building,
}

/// <summary>The categories assets are sorted into, and how an asset's category is worked out.</summary>
public static class AssetCategories
{
    public const string Trees = "Trees & plants", Rocks = "Rocks", Buildings = "Buildings & structures", Props = "Props & vehicles",
        Units = "Units", Wrecks = "Wrecks", Vents = "Geothermal vents", Other = "Other";

    /// <summary>The asset browser's group for the editor's own experimental tools (structures, new features): never an asset's category.</summary>
    public const string Experimental = "Experimental";

    public static readonly IReadOnlyList<string> All = [Trees, Rocks, Buildings, Props, Wrecks, Vents, Units, Other, Experimental];

    /// <summary>
    /// BAR's own test for a tree (modelmaterials_gl4/128_features_special.lua, featureNameTrees): the name starts with
    /// one of these, or contains "tree" anywhere, except names containing "street".
    /// </summary>
    static readonly string[] TreePrefixes =
    [
        "ad0_", "art", "allpinesb", "bush", "vegetation", "vegitation", "baobab", "aleppo", "pine", "senegal", "palm", "shrub",
        "bloodthorn", "birch", "maple", "oak", "fern", "grass", "weed", "plant", "palmetto", "lowpoly_tree", "treetype", "btree",
    ];

    public static bool IsBarTree(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("street", StringComparison.Ordinal))
            return false;
        return TreePrefixes.Any(p => n.StartsWith(p, StringComparison.Ordinal)) || n.Contains("tree", StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the text has one of the words starting a word ("rock" in "agorm_rock1" and "rocks", not in "crock"),
    /// or one of the whole words (short ones that hide inside others: "ore" isn't in "forest").
    /// </summary>
    static bool Has(string text, string[] starts, params string[] words) =>
        starts.Any(w => Regex.IsMatch(text, $"(?<![a-z]){w}")) || words.Any(w => Regex.IsMatch(text, $"(?<![a-z]){w}(?![a-z])"));

    /// <summary>
    /// The category an asset is sorted into, from its kind, its name (BAR's own tree and wreck rules first), the
    /// definition's own category (a fern "Ferns and Stones" whose category is "bush" is a plant), then its name and
    /// description, then what it gives when reclaimed (energy only: a plant; metal: a rock). A definition's category
    /// isn't trusted for wrecks: maps copy "corpses" categories onto crates and the like. The user can move an asset
    /// to another category; that choice is kept in the library.
    /// </summary>
    public static string Classify(string name, AssetKind kind, string? category = null, string? description = null, double metal = 0, double energy = 0)
    {
        if (kind == AssetKind.Unit)
            return Units;
        if (kind == AssetKind.Building)
            return Buildings;
        var n = name.ToLowerInvariant();
        if (n.Contains("geovent", StringComparison.Ordinal) || n.Contains("geothermal", StringComparison.Ordinal))
            return Vents;
        if (IsBarTree(n))
            return Trees;
        if (n.Contains("_dead", StringComparison.Ordinal) || n.Contains("_heap", StringComparison.Ordinal))
            return Wrecks; // BAR's test for a unit's wreck or heap (same file)
        var own = $"{n} {description}".ToLowerInvariant();
        if ((category == null ? null : ByWords(category.ToLowerInvariant())) is string stated)
            return stated;
        if (ByWords(own) is string described)
            return described;
        if (Has(own, ["wreck", "debris", "scrap", "rubble", "heap"]))
            return Wrecks;
        if (energy > 0 && metal <= 0)
            return Trees;
        if (metal > 0)
            return Rocks;
        return Other;
    }

    /// <summary>The category words in a text point to, if any.</summary>
    static string? ByWords(string text)
    {
        if (Has(text, ["rock", "stone", "boulder", "crystal", "cliff", "pebble", "meteor"], "ore"))
            return Rocks;
        if (Has(text, ["plant", "flower", "cactus", "coral", "kelp", "mushroom", "vine", "bush", "fern", "vegetation", "palm", "grass",
                "shrub", "weed", "reed", "tree"], "log") && !text.Contains("street", StringComparison.Ordinal))
            return Trees;
        if (Has(text, ["crate", "barrel", "truck", "vehicle", "container", "cart", "wagon", "tire", "tyre", "jeep", "trailer", "candy"], "car", "cars", "box", "boxes", "sign"))
            return Props;
        if (Has(text, ["building", "house", "tower", "wall", "bridge", "fence", "pillar", "statue", "structure", "ruin", "lamp", "pipe",
                "hut", "temple", "monument", "industr", "powerhouse", "factory", "silo", "chimney", "bunker"]))
            return Buildings;
        return null;
    }
}
