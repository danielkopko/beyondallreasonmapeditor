using MapCore.Smf;

namespace MapCore.Items;

/// <param name="Source">Where the definition comes from, e.g. "features/agorm_rock1.lua" or "engine default tree".</param>
/// <param name="Model">The model file named by the definition (under objects3d/), if any.</param>
/// <param name="ModelFound">Whether that model is in the package; null when there is no model to check.</param>
public sealed record FeatureDefinition(string Name, string Source, string? Model, bool? ModelFound);

/// <summary>
/// Feature names that can be placed from what the map package itself contains: the features its features/**/*.lua
/// files define (run as the game runs them, see <see cref="FeatureDefs"/>), plus the engine's built-in trees and
/// geothermal vent (engine FeatureDefHandler: SMF feature types containing "treetype" or "geovent" get default
/// definitions). The game (BAR) defines many more features, but those are not in the map archive, so they are not
/// listed here; any name can still be used.
/// </summary>
public static class FeatureCatalog
{
    public static (IReadOnlyList<FeatureDefinition> Definitions, IReadOnlyList<string> Warnings) Load(string packageRoot, SmfFile smf)
    {
        var definitions = new List<FeatureDefinition>();
        var warnings = new List<string>();
        var resolve = ModelPaths.Resolver(packageRoot);
        foreach (var file in FeatureDefs.Load(packageRoot))
        {
            if (file.Problem != null)
                warnings.Add($"{file.Entry} {file.Problem}");
            foreach (var d in file.Features)
                definitions.Add(new(d.Name, file.Entry, d.Object, d.Object == null ? null : ModelPaths.Find(d.Object, resolve) != null));
        }

        foreach (var type in smf.FeatureTypes)
        {
            if (definitions.Any(d => d.Name.Equals(type, StringComparison.OrdinalIgnoreCase)))
                continue;
            if (type.Contains("treetype", StringComparison.OrdinalIgnoreCase))
                definitions.Add(new(type, "engine default tree", null, null));
            else if (type.Contains("geovent", StringComparison.OrdinalIgnoreCase))
                definitions.Add(new(type, "engine default geothermal vent", null, null));
        }
        return (definitions, warnings);
    }
}
