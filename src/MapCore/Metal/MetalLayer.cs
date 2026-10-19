using MapCore.Smf;

namespace MapCore.Metal;

/// <summary>A metal cell of a spot: metal-map column and row, and raw value (0-255).</summary>
public readonly record struct MetalCell(int X, int Z, byte Value);

/// <summary>
/// A metal spot as BAR finds it: 8-connected metal cells. <see cref="X"/>/<see cref="Z"/> is the middle of the
/// cells' bounding box in game units, which is where BAR puts the spot (api_resource_spot_finder.lua).
/// </summary>
public sealed record MetalSpot(double X, double Z, IReadOnlyList<MetalCell> Cells)
{
    public int RawSum => Cells.Sum(c => c.Value);
}

/// <summary>
/// The SMF metal map (one byte per 16x16 game units) as metal spots, with the engine's income formula.
/// Edits refuse layouts BAR would misread: cells in the outermost ring (BAR's spot finder skips it) and spots so
/// close their cells touch (BAR would merge them into one spot).
/// </summary>
/// <remarks>
/// Engine facts (RecoilEngine MetalMap.cpp, ReadMap.cpp, ExtractorBuilding.cpp, UnitDef.cpp, Unit.cpp):
/// a cell holds raw x maxMetal metal; an extractor takes extractsMetal x that amount from every cell whose centre
/// is within mapinfo's extractorRadius of the building, per second. BAR's T1 extractors use extractsMetal 0.001.
/// </remarks>
public sealed class MetalLayer
{
    public const int CellSize = 16;
    public const double T1ExtractsMetal = 0.001;
    public const double DefaultMaxMetal = 0.02;        // engine default when mapinfo has no maxMetal
    public const double DefaultExtractorRadius = 500;  // engine default when mapinfo has no extractorRadius

    readonly SmfFile smf;
    readonly MapInfo? info;

    /// <param name="info">Supplies maxMetal and extractorRadius (read live, so edits to mapinfo apply).</param>
    /// <param name="readOnlyReason">When set, every edit throws with this explanation.</param>
    public MetalLayer(SmfFile smf, MapInfo? info = null, IReadOnlyList<string>? warnings = null, string? readOnlyReason = null)
    {
        this.smf = smf;
        this.info = info;
        Warnings = warnings ?? [];
        ReadOnlyReason = readOnlyReason;
    }

    public double MaxMetal => info?.MaxMetal ?? DefaultMaxMetal;
    public double ExtractorRadius => info?.ExtractorRadius ?? DefaultExtractorRadius;
    public IReadOnlyList<string> Warnings { get; }
    public string? ReadOnlyReason { get; }

    int Width => smf.MetalMapWidth;
    int Height => smf.MetalMapHeight;
    byte this[int x, int z] => smf.MetalMap[z * Width + x];

    public long TotalRaw => smf.MetalMap.Sum(v => (long)v);

    /// <summary>Metal cells in the outermost ring, which BAR's spot finder never looks at.</summary>
    public int EdgeCellsWithMetal()
    {
        int count = 0;
        for (int z = 0; z < Height; z++)
            for (int x = 0; x < Width; x++)
                if (IsEdge(x, z) && this[x, z] != 0)
                    count++;
        return count;
    }

    /// <summary>Spots as BAR's resource spot finder sees them (outermost ring ignored), ordered by row then column.</summary>
    public IReadOnlyList<MetalSpot> FindSpots()
    {
        var seen = new bool[Width * Height];
        var spots = new List<MetalSpot>();
        for (int z = 1; z < Height - 1; z++)
            for (int x = 1; x < Width - 1; x++)
            {
                if (this[x, z] == 0 || seen[z * Width + x])
                    continue;
                var cells = new List<MetalCell>();
                var queue = new Queue<(int X, int Z)>([(x, z)]);
                seen[z * Width + x] = true;
                while (queue.Count > 0)
                {
                    var (cx, cz) = queue.Dequeue();
                    cells.Add(new MetalCell(cx, cz, this[cx, cz]));
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = cx + dx, nz = cz + dz;
                            if (nx >= 1 && nz >= 1 && nx < Width - 1 && nz < Height - 1 && this[nx, nz] != 0 && !seen[nz * Width + nx])
                            {
                                seen[nz * Width + nx] = true;
                                queue.Enqueue((nx, nz));
                            }
                        }
                }
                spots.Add(ToSpot(cells));
            }
        return spots.OrderBy(s => s.Z).ThenBy(s => s.X).ToList();
    }

    /// <summary>The spot whose centre is nearest to (x, z), within <paramref name="maxDistance"/> game units.</summary>
    public MetalSpot? SpotNear(double x, double z, double maxDistance = 64) =>
        FindSpots().Where(s => Distance(s.X, s.Z, x, z) <= maxDistance).MinBy(s => Distance(s.X, s.Z, x, z));

    /// <summary>Metal per second an extractor at (x, z) would make: extractsMetal x Σ(raw x maxMetal) within extractorRadius.</summary>
    public double Income(double x, double z, double extractsMetal = T1ExtractsMetal)
    {
        double radius = ExtractorRadius, sum = 0;
        int x0 = Math.Max(0, (int)((x - radius) / CellSize)), x1 = Math.Min(Width - 1, (int)((x + radius) / CellSize));
        int z0 = Math.Max(0, (int)((z - radius) / CellSize)), z1 = Math.Min(Height - 1, (int)((z + radius) / CellSize));
        for (int cz = z0; cz <= z1; cz++)
            for (int cx = x0; cx <= x1; cx++)
                if (Distance((cx + 0.5) * CellSize, (cz + 0.5) * CellSize, x, z) < radius)
                    sum += this[cx, cz] * MaxMetal;
        return sum * extractsMetal;
    }

    public double Income(MetalSpot spot, double extractsMetal = T1ExtractsMetal) => Income(spot.X, spot.Z, extractsMetal);

    /// <summary>
    /// Moves a spot so its centre lands as near (x, z) as the 16-unit metal grid allows. Its cells keep their values,
    /// so the map's total metal is unchanged.
    /// </summary>
    public MetalSpot MoveSpot(MetalSpot spot, double x, double z)
    {
        int dx = (int)Math.Round((x - spot.X) / CellSize), dz = (int)Math.Round((z - spot.Z) / CellSize);
        var moved = spot.Cells.Select(c => c with { X = c.X + dx, Z = c.Z + dz }).ToList();
        CheckPlacement(moved, spot.Cells);
        foreach (var c in spot.Cells)
            smf.MetalMap[c.Z * Width + c.X] = 0;
        Write(moved);
        return ToSpot(moved);
    }

    /// <summary>Where <see cref="MoveSpot"/> would put the spot's centre, and why it would refuse (null if it wouldn't). Changes nothing.</summary>
    public (double X, double Z, string? Problem) TryMove(MetalSpot spot, double x, double z)
    {
        int dx = (int)Math.Round((x - spot.X) / CellSize), dz = (int)Math.Round((z - spot.Z) / CellSize);
        var moved = spot.Cells.Select(c => c with { X = c.X + dx, Z = c.Z + dz }).ToList();
        var centre = ToSpot(moved);
        return (centre.X, centre.Z, Problem(() => CheckPlacement(moved, spot.Cells)));
    }

    /// <summary>Where <see cref="AddSpot"/> would put a new typical spot, and why it would refuse. Changes nothing.</summary>
    public (double X, double Z, string? Problem) TryAdd(double x, double z)
    {
        var pattern = TypicalPattern();
        int width = pattern.Max(c => c.X) + 1, height = pattern.Max(c => c.Z) + 1;
        int left = (int)Math.Round((x - CellSize / 2.0) / CellSize - (width - 1) / 2.0);
        int top = (int)Math.Round((z - CellSize / 2.0) / CellSize - (height - 1) / 2.0);
        var cells = pattern.Select(c => c with { X = left + c.X, Z = top + c.Z }).ToList();
        var centre = ToSpot(cells);
        return (centre.X, centre.Z, Problem(() => CheckPlacement(cells, [])));
    }

    static string? Problem(Action check)
    {
        try
        {
            check();
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Adds a spot with the given cell pattern (offsets from the pattern's top-left), centred as near (x, z) as the grid allows.
    /// </summary>
    public MetalSpot AddSpot(double x, double z, IReadOnlyList<MetalCell> pattern)
    {
        int width = pattern.Max(c => c.X) + 1, height = pattern.Max(c => c.Z) + 1;
        int left = (int)Math.Round((x - CellSize / 2.0) / CellSize - (width - 1) / 2.0);
        int top = (int)Math.Round((z - CellSize / 2.0) / CellSize - (height - 1) / 2.0);
        var cells = pattern.Select(c => c with { X = left + c.X, Z = top + c.Z }).ToList();
        CheckPlacement(cells, []);
        Write(cells);
        return ToSpot(cells);
    }

    public void RemoveSpot(MetalSpot spot)
    {
        RequireEditable();
        foreach (var c in spot.Cells)
            smf.MetalMap[c.Z * Width + c.X] = 0;
    }

    /// <summary>Scales a spot's cell values so it yields <paramref name="income"/> metal/s; returns the spot as written (rounded).</summary>
    public MetalSpot SetIncome(MetalSpot spot, double income, double extractsMetal = T1ExtractsMetal)
    {
        RequireEditable();
        double current = Income(spot, extractsMetal);
        if (current <= 0)
            throw new InvalidOperationException("The spot yields no metal now, so it cannot be scaled.");
        var scaled = spot.Cells.Select(c => (Cell: c, Value: Math.Round(c.Value * income / current))).ToList();
        if (scaled.Any(s => s.Value > 255))
            throw new InvalidOperationException(
                $"{income:0.##} metal/s needs cell values above 255 for this {spot.Cells.Count}-cell spot; " +
                "make the spot bigger first (ResizeSpot / metal-resize) or raise maxMetal.");
        var cells = scaled.Select(s => s.Cell with { Value = (byte)Math.Max(1, s.Value) }).ToList();
        Write(cells);
        return ToSpot(cells);
    }

    /// <summary>
    /// Rebuilds a spot as an n x n square of equal cells around the same centre, keeping its income (as far as
    /// whole cell values allow). Bigger spots can then hold more metal. Every cell must lie within extractor reach.
    /// </summary>
    public MetalSpot ResizeSpot(MetalSpot spot, int cellsAcross, double extractsMetal = T1ExtractsMetal)
    {
        RequireEditable();
        if (cellsAcross is < 1 or > 32)
            throw new InvalidOperationException($"A spot must be 1 to 32 cells across, not {cellsAcross}.");
        double income = Income(spot, extractsMetal);
        int left = (int)Math.Round((spot.X - CellSize / 2.0) / CellSize - (cellsAcross - 1) / 2.0);
        int top = (int)Math.Round((spot.Z - CellSize / 2.0) / CellSize - (cellsAcross - 1) / 2.0);
        var placed = (from z in Enumerable.Range(0, cellsAcross) from x in Enumerable.Range(0, cellsAcross)
                      select new MetalCell(left + x, top + z, 1)).ToList();
        var shape = ToSpot(placed);
        if (placed.Any(c => Distance((c.X + 0.5) * CellSize, (c.Z + 0.5) * CellSize, shape.X, shape.Z) >= ExtractorRadius))
            throw new InvalidOperationException(
                $"A {cellsAcross}x{cellsAcross} spot is wider than an extractor reaches ({ExtractorRadius} units); its outer cells would be wasted.");
        double value = income / (placed.Count * MaxMetal * extractsMetal);
        if (value > 255.5)
            throw new InvalidOperationException($"{income:0.##} metal/s needs cell values above 255 even as {cellsAcross}x{cellsAcross}.");
        if (value < 0.5)
            throw new InvalidOperationException($"{income:0.##} metal/s spread over {cellsAcross}x{cellsAcross} cells rounds to nothing.");

        CheckPlacement(placed, spot.Cells);
        foreach (var c in spot.Cells)
            smf.MetalMap[c.Z * Width + c.X] = 0;
        var cells = placed.Select(c => c with { Value = (byte)Math.Clamp(Math.Round(value), 1, 255) }).ToList();
        Write(cells);
        return ToSpot(cells);
    }

    /// <summary>
    /// The most common spot shape on the map (cell offsets from its top-left, with values) — a natural template for
    /// new spots. Falls back to a 3x3 square worth about 2 metal/s.
    /// </summary>
    public IReadOnlyList<MetalCell> TypicalPattern()
    {
        var patterns = FindSpots().Select(s =>
        {
            int left = s.Cells.Min(c => c.X), top = s.Cells.Min(c => c.Z);
            return s.Cells.Select(c => c with { X = c.X - left, Z = c.Z - top }).OrderBy(c => c.Z).ThenBy(c => c.X).ToList();
        }).ToList();
        var common = patterns.GroupBy(p => string.Join(";", p)).OrderByDescending(g => g.Count()).FirstOrDefault()?.First();
        if (common != null)
            return common;
        byte value = (byte)Math.Clamp(Math.Round(2.0 / (9 * MaxMetal * T1ExtractsMetal)), 1, 255);
        return [.. from z in Enumerable.Range(0, 3) from x in Enumerable.Range(0, 3) select new MetalCell(x, z, value)];
    }

    void Write(IEnumerable<MetalCell> cells)
    {
        RequireEditable();
        foreach (var c in cells)
            smf.MetalMap[c.Z * Width + c.X] = c.Value;
    }

    /// <summary>New cells must avoid the outer ring and must not touch metal other than <paramref name="replacing"/>.</summary>
    void CheckPlacement(IReadOnlyList<MetalCell> cells, IReadOnlyList<MetalCell> replacing)
    {
        RequireEditable();
        var own = replacing.Select(c => (c.X, c.Z)).ToHashSet();
        foreach (var c in cells)
        {
            if (c.X < 1 || c.Z < 1 || c.X >= Width - 1 || c.Z >= Height - 1)
                throw new InvalidOperationException(
                    $"The spot would reach within {CellSize} units of the map edge, where BAR's spot finder does not look.");
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = c.X + dx, nz = c.Z + dz;
                    if (this[nx, nz] != 0 && !own.Contains((nx, nz)))
                        throw new InvalidOperationException(
                            $"The spot would touch existing metal at ({nx * CellSize + 8}, {nz * CellSize + 8}); BAR would merge them into one spot.");
                }
        }
    }

    void RequireEditable()
    {
        if (ReadOnlyReason != null)
            throw new InvalidOperationException(ReadOnlyReason);
    }

    bool IsEdge(int x, int z) => x == 0 || z == 0 || x == Width - 1 || z == Height - 1;

    static MetalSpot ToSpot(IReadOnlyList<MetalCell> cells) => new(
        (cells.Min(c => c.X) + cells.Max(c => c.X)) / 2.0 * CellSize + CellSize / 2.0,
        (cells.Min(c => c.Z) + cells.Max(c => c.Z)) / 2.0 * CellSize + CellSize / 2.0,
        cells.OrderBy(c => c.Z).ThenBy(c => c.X).ToList());

    static double Distance(double ax, double az, double bx, double bz) => Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
}
