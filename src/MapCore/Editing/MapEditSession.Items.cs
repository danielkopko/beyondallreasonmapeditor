using System.Globalization;
using MapCore.Items;
using MapCore.Smf;

namespace MapCore.Editing;

/// <summary>
/// Placed items addressed exactly — by store, list and index rather than by position — so the editor's selections and
/// group edits can't pick the wrong item where items crowd together. Group edits are single operations: if one item
/// can't be changed, none is.
/// <code>
///   items-move:&lt;dx&gt;,&lt;dz&gt;:&lt;refs&gt;        refs: s3;f12;b0 … (s: SMF feature, f/u/b: featureplacer object/unit/building, 0-based)
///   items-rotate:&lt;degrees&gt;:&lt;refs&gt;      turn each in place (buildings by whole quarter turns, at least one)
///   items-heading:&lt;degrees&gt;:&lt;refs&gt;     set each heading (0 = facing south, 90 = east; buildings to the nearest quarter)
///   items-remove:&lt;refs&gt;
///   grass-paint:&lt;x&gt;,&lt;z&gt;:&lt;radius&gt;:&lt;value&gt;   0 removes grass; 1 grass; 2-254 blade size in BAR
/// </code>
/// </summary>
public sealed partial class MapEditSession
{
    /// <summary>The token that names an item in the items-* operations.</summary>
    public static string ItemRef(MapItem item) =>
        (item.Source == ItemSource.Smf ? "s" : item.Kind switch { PlacedItemKind.Unit => "u", PlacedItemKind.Building => "b", _ => "f" })
        + item.Index.ToString(CultureInfo.InvariantCulture);

    /// <summary>Tokens for several items, as the operations take them.</summary>
    public static string ItemRefs(IEnumerable<MapItem> items) => string.Join(";", items.Select(ItemRef));

    /// <summary>A heading in degrees (0 = facing south, 90 = east), for any item kind.</summary>
    public static double HeadingDegrees(MapItem item) =>
        item.Kind == PlacedItemKind.Building ? item.Rotation * 90 : (item.Rotation * 360.0 / 65536 % 360 + 360) % 360;

    List<MapItem> ResolveRefs(string refs)
    {
        var all = Items.All;
        var found = new List<MapItem>();
        foreach (var token in refs.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Length < 2 || !int.TryParse(token[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                throw new InvalidOperationException($"'{token}' is not an item (expected s, f, u or b and an index, e.g. f12).");
            var (source, kind) = token[0] switch
            {
                's' => (ItemSource.Smf, PlacedItemKind.Feature),
                'f' => (ItemSource.FeaturePlacer, PlacedItemKind.Feature),
                'u' => (ItemSource.FeaturePlacer, PlacedItemKind.Unit),
                'b' => (ItemSource.FeaturePlacer, PlacedItemKind.Building),
                _ => throw new InvalidOperationException($"'{token}' is not an item (expected s, f, u or b and an index, e.g. f12)."),
            };
            var item = all.FirstOrDefault(i => i.Source == source && i.Kind == kind && i.Index == index)
                       ?? throw new InvalidOperationException($"There is no item {token}.");
            if (!found.Contains(item))
                found.Add(item);
        }
        if (found.Count == 0)
            throw new InvalidOperationException("No items given.");
        return found;
    }

    string ItemsOperation(string name, string[] args)
    {
        switch (name)
        {
            case "items-move":
            {
                Expect(args, 2, name);
                var (dx, dz) = Point(args[0]);
                var items = ResolveRefs(args[1]);
                foreach (var item in items)
                {
                    double x = item.X + dx, z = item.Z + dz;
                    if (x < 0 || z < 0 || x > MapWidth || z > MapHeight)
                        throw new InvalidOperationException($"{item.Name} would leave the map at ({x:0}, {z:0}).");
                }
                foreach (var item in items)
                    Items.Move(item, item.X + dx, item.Z + dz);
                return $"{Count(items)} moved by ({dx:0.#}, {dz:0.#})";
            }
            case "items-rotate" or "items-heading":
            {
                Expect(args, 2, name);
                double degrees = Number(args[0]);
                var items = ResolveRefs(args[1]);
                foreach (var item in items)
                    Items.SetRotation(item, NewRotation(item, degrees, relative: name == "items-rotate"));
                return name == "items-rotate" ? $"{Count(items)} turned {degrees:+0.#;-0.#}°" : $"{Count(items)} set to face {degrees:0.#}°";
            }
            case "items-remove":
            {
                Expect(args, 1, name);
                var items = ResolveRefs(args[0]);
                // Later items in a list shift down when one is removed: go from the end of each list.
                foreach (var item in items.OrderByDescending(i => i.Index))
                    Items.Remove(item);
                return $"{Count(items)} removed";
            }
            default: // grass-paint
            {
                Expect(args, 3, name);
                if (Info.TextureOverrides.TryGetValue("grassmapTex", out var image))
                    throw new InvalidOperationException($"mapinfo.lua sets smf.grassmapTex = \"{image}\"; the game takes grass from that image, so the SMF grass map is not used.");
                var (x, z) = Point(args[0]);
                double radius = Number(args[1]), value = Number(args[2]);
                if (radius <= 0 || value is < 0 or > 255)
                    throw new InvalidOperationException("grass-paint needs a positive radius and a value from 0 to 254.");
                int cells = GrassLayer.Paint(Smf, x, z, radius, (byte)Math.Round(value));
                return $"grass {(value == 0 ? "removed" : $"set to {value:0}")} in {cells} cells around ({x:0}, {z:0})";
            }
        }

        static string Count(List<MapItem> items) => items.Count == 1 ? items[0].Name : $"{items.Count} items";
    }

    /// <summary>
    /// The stored rotation after turning (or setting) by degrees. Headings: 65536 per turn, 0 facing south. Buildings
    /// face 0-3 (south, east, north, west) and turn by whole quarters — at least one when turning.
    /// </summary>
    static double NewRotation(MapItem item, double degrees, bool relative)
    {
        if (item.Kind == PlacedItemKind.Building)
        {
            int quarters = (int)Math.Round(degrees / 90);
            if (relative && quarters == 0 && degrees != 0)
                quarters = Math.Sign(degrees);
            return relative ? item.Rotation + quarters : quarters;
        }
        double units = degrees * 65536 / 360;
        return relative ? item.Rotation + units : units;
    }

    // ---------------------------------------------------------------- typed forms

    public string MoveItems(IEnumerable<MapItem> items, double dx, double dz) => Apply(Op($"items-move:{dx},{dz}:{ItemRefs(items)}"));

    public string RotateItems(IEnumerable<MapItem> items, double degrees) => Apply(Op($"items-rotate:{degrees}:{ItemRefs(items)}"));

    public string SetItemHeading(IEnumerable<MapItem> items, double degrees) => Apply(Op($"items-heading:{degrees}:{ItemRefs(items)}"));

    public string RemoveItems(IEnumerable<MapItem> items) => Apply(Op($"items-remove:{ItemRefs(items)}"));

    /// <summary>
    /// Adds a feature to the SMF list or to the featureplacer's objectlist, facing <paramref name="degrees"/>; or, with
    /// <paramref name="kind"/>, a unit or building to the featureplacer's unitlist or buildinglist (a building faces
    /// the nearest quarter).
    /// </summary>
    public string AddItem(ItemSource source, string name, double x, double z, double degrees, PlacedItemKind kind = PlacedItemKind.Feature) => kind switch
    {
        PlacedItemKind.Unit => Apply(Op($"item-add:unit:{name}:{x},{z}:{Math.Round(degrees * 65536 / 360)}")),
        PlacedItemKind.Building => Apply(Op($"item-add:building:{name}:{x},{z}:{((int)Math.Round(degrees / 90) % 4 + 4) % 4}")),
        _ => Apply(Op($"item-add:{(source == ItemSource.Smf ? "smf" : "placer")}:{name}:{x},{z}:{Math.Round(degrees * 65536 / 360)}")),
    };

    public string PaintGrass(double x, double z, double radius, int value) => Apply(Op($"grass-paint:{x},{z}:{radius}:{value}"));

    public string MoveStartPosition(int team, double x, double z) => Apply(Op($"start-move:{team}:{x},{z}"));
}
