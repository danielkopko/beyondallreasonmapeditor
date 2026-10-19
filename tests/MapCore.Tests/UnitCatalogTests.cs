using MapCore.Items;
using MapCore.Structures;

namespace MapCore.Tests;

/// <summary>The BAR unit catalog the editor ships (src/MapEditor/BarUnits), as a guardian's targets are chosen from it.</summary>
static class UnitCatalogTests
{
    static string CatalogPath => Path.Combine(TestData.RepoRoot, "src", "MapEditor", "BarUnits", UnitCatalog.FileName);

    [Test]
    public static void TheShippedCatalogHasEveryStandardUnitAndItsPicture()
    {
        var catalog = UnitCatalog.Load(CatalogPath);
        Report.Info($"{catalog.Units.Count} units from {catalog.Source?.Repository} at {catalog.Source?.Commit[..12]}");
        Assert.True(catalog.Units.Count == 681 && catalog.Source is { Repository: "beyond-all-reason/Beyond-All-Reason" },
            $"681 units from BAR's repository ({catalog.Units.Count})");
        var factions = catalog.Units.GroupBy(u => u.Faction).ToDictionary(g => g.Key, g => g.Count());
        Assert.True(factions.Count == 3 && factions["Armada"] == 223 && factions["Cortex"] == 224 && factions["Legion"] == 234,
            $"three factions ({string.Join(", ", factions.Select(f => $"{f.Key} {f.Value}"))})");
        Assert.True(catalog.Units.Select(u => u.Name).Distinct().Count() == catalog.Units.Count, "each name once");
        var badNames = catalog.Units.Where(u => !CustomFeature.NamePattern.IsMatch(u.Name)).Select(u => u.Name).ToList();
        Assert.True(badNames.Count == 0, $"every name can be a guardian's target ({string.Join(", ", badNames)})");
        var noPicture = catalog.Units.Where(u => catalog.IconPath(u) == null).Select(u => u.Name).ToList();
        Assert.True(noPicture.Count == 0, $"every unit's picture is there ({string.Join(", ", noPicture.Take(10))})");

        // Destroyed by default: aircraft that can attack the ground, armed transports among them; not fighters, scouts,
        // plain transports or constructors.
        var destroy = catalog.DefaultTargets;
        Assert.True(destroy.Count == 43 && destroy.All(n => catalog.Find(n) is { CanFly: true, AttacksSurface: true }),
            $"43 ground-attacking aircraft destroyed by default ({destroy.Count})");
        foreach (var name in new[] { "armthund", "corshad", "armkam", "armdfly", "legstronghold" })
            Assert.True(destroy.Contains(name), $"{name} ({catalog.Find(name)?.DisplayName}) is destroyed by default");
        foreach (var name in new[] { "armfig", "armpeep", "armatlas", "armca", "armcom", "corvamp" })
            Assert.True(catalog.Find(name) is { DefaultDestroy: false }, $"{name} ({catalog.Find(name)?.DisplayName}) passes by default");
        Assert.True(catalog.Find("armthund")?.Title == "armthund (Stormbringer)", "a unit's title is its name and what BAR calls it");
    }
}
