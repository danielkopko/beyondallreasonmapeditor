using MapCore.Heights;

namespace MapCore.Movement;

/// <summary>
/// Where every BAR movement class can go, square by square (<see cref="PassabilityMap"/> for each distinct class in
/// <see cref="BarMoveClasses.All"/>: its slope limit, its kind and how deep it goes). Two of these taken before and after
/// an edit say whether the edit changed where any unit can go: steepness within each class's limit, the water's edge,
/// depth limits, ramps that stay climbable.
/// </summary>
public sealed class AccessMap
{
    readonly List<(MoveClass Class, Passage[] Squares)> classes = [];

    /// <summary>The classes that differ in where they go: one per slope limit, kind and depth.</summary>
    public static IReadOnlyList<MoveClass> DistinctClasses { get; } =
        [.. BarMoveClasses.All.GroupBy(c => (c.Kind, c.MaxSlope, c.MaxWaterDepth)).Select(g => g.First())];

    public AccessMap(HeightMap heights, double waterLevel = 0)
    {
        foreach (var c in DistinctClasses)
        {
            var map = new PassabilityMap(heights, c, waterLevel);
            var squares = new Passage[map.Width * map.Height];
            for (int z = 0; z < map.Height; z++)
                for (int x = 0; x < map.Width; x++)
                    squares[z * map.Width + x] = map[x, z];
            classes.Add((c, squares));
            (Width, Height) = (map.Width, map.Height);
        }
    }

    /// <summary>Squares across and down (8 game units each).</summary>
    public int Width { get; }
    public int Height { get; }

    /// <summary>The squares where some class goes differently in <paramref name="other"/> (none: the same access everywhere).</summary>
    public List<(int X, int Z, string Class, Passage Before, Passage After)> Differences(AccessMap other, int most = int.MaxValue)
    {
        var found = new List<(int, int, string, Passage, Passage)>();
        for (int i = 0; i < classes.Count && found.Count < most; i++)
        {
            var (c, a) = classes[i];
            var b = other.classes[i].Squares;
            for (int s = 0; s < a.Length && found.Count < most; s++)
                if (a[s] != b[s])
                    found.Add((s % Width, s / Width, c.Name, a[s], b[s]));
        }
        return found;
    }
}
