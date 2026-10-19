using System.Text.Json;
using System.Text.Json.Serialization;

namespace MapCore.Structures;

/// <summary>One of BAR's standard units, as tools/MapResearch's barunits read it from BAR's repository.</summary>
public sealed record CatalogUnit(
    string Name, string DisplayName, string Description, string Faction, string Group,
    bool CanFly, bool Builder, bool Transport, bool Commander, bool AttacksSurface, bool DefaultDestroy,
    IReadOnlyList<CatalogWeapon> Weapons, string? Icon)
{
    /// <summary>Its name and what BAR calls it, e.g. "armthund (Stormbringer)".</summary>
    public string Title => DisplayName.Length > 0 && DisplayName != Name ? $"{Name} ({DisplayName})" : Name;
}

/// <summary>A unit's weapon: its def name and what it may aim at (onlytargetcategory; empty when anything).</summary>
public sealed record CatalogWeapon(string Def, string Targets);

/// <summary>Where the catalog came from: BAR's repository at a commit.</summary>
public sealed record CatalogSource(string Repository, string Commit, string Date, string Built);

/// <summary>
/// BAR's standard units (Armada, Cortex and Legion; Scavengers' variants are name_scav and follow their unit), with
/// their pictures: the list a guardian tower's targets are chosen from. Units destroyed by default are aircraft that
/// can attack the ground (bombers, gunships, drones, torpedo planes and armed transports); fighters, scouts,
/// transports and constructors pass.
/// </summary>
public sealed class UnitCatalog
{
    /// <summary>The catalog's file in the folder the editor ships it in (BarUnits), its pictures beside it.</summary>
    public const string FileName = "catalog.json";

    public CatalogSource? Source { get; }
    public IReadOnlyList<CatalogUnit> Units { get; }

    /// <summary>The folder the catalog was read from (its pictures' paths are relative to it).</summary>
    public string Folder { get; }

    readonly Dictionary<string, CatalogUnit> byName;

    UnitCatalog(CatalogSource? source, IReadOnlyList<CatalogUnit> units, string folder)
    {
        Source = source;
        Units = units;
        Folder = folder;
        byName = units.GroupBy(u => u.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    static readonly JsonSerializerOptions options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    sealed record FileShape(CatalogSource? Source, List<CatalogUnit>? Units);

    /// <summary>Reads a catalog file; its units in the file's order (by name).</summary>
    public static UnitCatalog Load(string path)
    {
        var file = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(path), options)
                   ?? throw new InvalidDataException($"{path} isn't a unit catalog");
        var units = (file.Units ?? []).Select(u => u with
        {
            DisplayName = u.DisplayName ?? "", Description = u.Description ?? "", Faction = u.Faction ?? "", Group = u.Group ?? "",
            Weapons = u.Weapons ?? [],
        }).ToList();
        return new UnitCatalog(file.Source, units, Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
    }

    /// <summary>The catalog shipped beside a program (BarUnits/catalog.json under its folder), or null when it isn't there.</summary>
    public static UnitCatalog? Shipped(string programFolder)
    {
        string path = Path.Combine(programFolder, "BarUnits", FileName);
        return File.Exists(path) ? Load(path) : null;
    }

    public CatalogUnit? Find(string name) => byName.TryGetValue(name, out var unit) ? unit : null;

    /// <summary>A unit's picture file, or null when it has none on disk.</summary>
    public string? IconPath(CatalogUnit unit)
    {
        if (string.IsNullOrEmpty(unit.Icon))
            return null;
        string path = Path.GetFullPath(Path.Combine(Folder, unit.Icon.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(Folder, StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
    }

    /// <summary>The names a new guardian destroys: aircraft that can attack the ground.</summary>
    public IReadOnlyList<string> DefaultTargets => Units.Where(u => u.DefaultDestroy).Select(u => u.Name).ToList();

    /// <summary>Every flying unit's name.</summary>
    public IReadOnlyList<string> Aircraft => Units.Where(u => u.CanFly).Select(u => u.Name).ToList();

    /// <summary>The factions in the order they first appear, then each one's groups.</summary>
    public IEnumerable<(string Faction, string Group, IReadOnlyList<CatalogUnit> Units)> Grouped() =>
        Units.GroupBy(u => (u.Faction, u.Group)).Select(g => (g.Key.Faction, g.Key.Group, (IReadOnlyList<CatalogUnit>)g.ToList()));
}
