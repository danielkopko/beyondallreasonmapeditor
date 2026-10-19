using MapCore.Editing;
using MapCore.Heights;

namespace MapCore.Movement;

/// <summary>What a square of ground is for one movement class.</summary>
public enum Passage : byte
{
    /// <summary>It can go there (on land, or in water shallow enough).</summary>
    Open,

    /// <summary>A hover on water: any slope beneath is ignored.</summary>
    OnWater,

    /// <summary>The slope is over the class's limit.</summary>
    TooSteep,

    /// <summary>Deeper under water than the class goes.</summary>
    TooDeep,
}

/// <summary>
/// Where one movement class can go, per 8-unit square, by the engine's rule (RecoilEngine MoveMath.cpp GetPosSpeedMod,
/// with GroundMoveMath.cpp and HoverMoveMath.cpp): a square's height is its highest corner (ReadMap.cpp maxHeightMap)
/// and its slope is the 16-unit slope-map cell it lies in (<see cref="TerrainSlope"/>). Hovers go anywhere on water
/// (highest corner below sea level). Otherwise a square is blocked when its slope is over the class's limit, and, for
/// land units, when it is deeper than they go. Heights are taken relative to <see cref="WaterLevel"/>, so a previewed
/// sea level shows what the game would make of the shifted map.
/// </summary>
public sealed class PassabilityMap
{
    readonly HeightMap heights;
    readonly double[] cellSlope;
    readonly Passage[] squares;
    readonly int cellsX, cellsZ;

    public PassabilityMap(HeightMap heights, MoveClass moveClass, double waterLevel = 0)
    {
        this.heights = heights;
        Class = moveClass;
        WaterLevel = waterLevel;
        Width = heights.Width - 1;
        Height = heights.Height - 1;
        (cellsX, cellsZ) = (Width / 2, Height / 2);
        cellSlope = new double[cellsX * cellsZ];
        squares = new Passage[Width * Height];
        Update(null);
    }

    public MoveClass Class { get; }
    public double WaterLevel { get; }

    /// <summary>Squares across and down (8 game units each).</summary>
    public int Width { get; }
    public int Height { get; }

    public Passage this[int x, int z] => squares[Math.Clamp(z, 0, Height - 1) * Width + Math.Clamp(x, 0, Width - 1)];

    /// <summary>The square under a world point.</summary>
    public Passage At(double x, double z) => this[(int)Math.Floor(x / HeightMap.Spacing), (int)Math.Floor(z / HeightMap.Spacing)];

    /// <summary>Works the squares out again where heights changed (game units), or everywhere for null.</summary>
    public void Update(WorldRect? area)
    {
        const double cell = 2 * HeightMap.Spacing;
        int cx0 = 0, cz0 = 0, cx1 = cellsX, cz1 = cellsZ;
        if (area is WorldRect a)
        {
            // A cell reads the vertices around it, so one cell of margin covers every square the edit can reach.
            cx0 = Math.Max(0, (int)Math.Floor(a.X / cell) - 1);
            cz0 = Math.Max(0, (int)Math.Floor(a.Z / cell) - 1);
            cx1 = Math.Min(cellsX, (int)Math.Ceiling(a.Right / cell) + 1);
            cz1 = Math.Min(cellsZ, (int)Math.Ceiling(a.Bottom / cell) + 1);
        }
        if (cx0 >= cx1 || cz0 >= cz1)
            return;
        Parallel.For(cz0, cz1, cz =>
        {
            for (int cx = cx0; cx < cx1; cx++)
                cellSlope[cz * cellsX + cx] = TerrainSlope.Cell(heights, cx, cz);
        });
        Parallel.For(cz0 * 2, Math.Min(Height, cz1 * 2), z =>
        {
            for (int x = cx0 * 2; x < Math.Min(Width, cx1 * 2); x++)
                squares[z * Width + x] = Classify(x, z);
        });
    }

    Passage Classify(int x, int z)
    {
        double highest = Math.Max(Math.Max(heights[x, z], heights[x + 1, z]), Math.Max(heights[x, z + 1], heights[x + 1, z + 1])) - WaterLevel;
        if (Class.Kind == MoverKind.Hover && highest < 0)
            return Passage.OnWater;
        double slope = cellSlope[Math.Min(z / 2, cellsZ - 1) * cellsX + Math.Min(x / 2, cellsX - 1)];
        if (slope > Class.MaxSlope)
            return Passage.TooSteep;
        if (Class.Kind != MoverKind.Hover && -highest > Class.MaxWaterDepth)
            return Passage.TooDeep;
        return Passage.Open;
    }

    /// <summary>How many squares are of each kind (for summaries and tests).</summary>
    public IReadOnlyDictionary<Passage, int> Counts() =>
        squares.GroupBy(p => p).ToDictionary(g => g.Key, g => g.Count());
}
