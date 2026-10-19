using MapCore.Smf;

namespace MapCore.Items;

/// <summary>Where an item is stored.</summary>
public enum ItemSource
{
    /// <summary>The SMF's own feature list (trees, vents, and any feature the game defines).</summary>
    Smf,
    /// <summary>The map's featureplacer config (mapconfig/featureplacer).</summary>
    FeaturePlacer,
}

/// <summary>
/// An object placed on the map. <see cref="Index"/> is its 0-based position within its own list.
/// <see cref="Rotation"/> is a heading in 65536ths of a turn, except for featureplacer buildings (facing 0-3).
/// </summary>
public sealed record MapItem(ItemSource Source, PlacedItemKind Kind, int Index, string Name, double X, double Z, double Rotation);

/// <summary>
/// All placed objects of a map in one list — the SMF's features and the featureplacer entries — with uniform
/// move, rotate, add and remove. After a removal, re-read <see cref="All"/>: later items in that list shift down.
/// </summary>
public sealed class MapItems(SmfFile smf, FeaturePlacerConfig? placer)
{
    public SmfFile Smf => smf;
    public FeaturePlacerConfig? Placer => placer;

    public IReadOnlyList<MapItem> All =>
        smf.Features.Select((f, i) => new MapItem(ItemSource.Smf, PlacedItemKind.Feature, i,
                smf.FeatureTypes.ElementAtOrDefault(f.TypeIndex) ?? $"<type {f.TypeIndex}>", f.X, f.Z, f.Rotation))
            .Concat((placer?.Items ?? []).Select(p => new MapItem(ItemSource.FeaturePlacer, p.Kind, p.Index - 1, p.Name, p.X, p.Z, p.Rotation)))
            .ToList();

    public void Move(MapItem item, double x, double z)
    {
        if (item.Source == ItemSource.Smf)
            smf.Features[item.Index] = smf.Features[item.Index] with { X = (float)x, Z = (float)z };
        else
            RequirePlacer().Move(ToPlaced(item), x, z);
    }

    /// <summary>Sets the rotation, brought into range by <see cref="Rotation.Normalize"/>.</summary>
    public void SetRotation(MapItem item, double rotation)
    {
        if (item.Source == ItemSource.Smf)
            smf.Features[item.Index] = smf.Features[item.Index] with { Rotation = (float)Rotation.Normalize(item.Kind, rotation) };
        else
            RequirePlacer().SetRotation(ToPlaced(item), rotation);
    }

    /// <summary>Adds an item. The SMF list holds features only; the featureplacer also takes units and buildings.</summary>
    public MapItem Add(ItemSource source, PlacedItemKind kind, string name, double x, double z, double rotation)
    {
        if (source == ItemSource.Smf)
        {
            if (kind != PlacedItemKind.Feature)
                throw new InvalidOperationException("The SMF feature list can only hold features.");
            smf.AddFeature(name, (float)x, (float)z, (float)Rotation.Normalize(kind, rotation));
            return All.First(i => i.Source == ItemSource.Smf && i.Index == smf.Features.Count - 1);
        }
        var placed = RequirePlacer().Add(kind, name, x, z, rotation);
        return new MapItem(source, kind, placed.Index - 1, name, x, z, rotation);
    }

    public void Remove(MapItem item)
    {
        if (item.Source == ItemSource.Smf)
            smf.Features.RemoveAt(item.Index);
        else
            RequirePlacer().Remove(ToPlaced(item));
    }

    FeaturePlacerConfig RequirePlacer() =>
        placer ?? throw new InvalidOperationException("This map has no featureplacer config (mapconfig/featureplacer/config.lua).");

    static PlacedItem ToPlaced(MapItem item) => new(item.Kind, item.Index + 1, item.Name, item.X, item.Z, item.Rotation);
}
