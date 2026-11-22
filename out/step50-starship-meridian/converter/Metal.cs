// The metal map (1.1): the whole ship reads as metal, so Starship Meridian is a metal map. Every metal cell (16 × 16
// game units) whose centre is a deck floor vertex holds Layout.MetalRaw; walls, hull tops and space hold none, but for
// BAR's two marker strips (1.2, below). A cell's centre (16i + 8, 16j + 8) is a heightmap vertex, so cell i's mirror image
// is cell (width − 1 − i), as the floors are.
static class Metal
{
    public const int CellSize = 16;

    /// <summary>
    /// The marker strips (1.2). BAR decides whether a map is a metal map in its spot finder
    /// (common/upgets/api_resource_spot_finder.lua, GetSpotsMetal): it scans the metal map row by row, joins each row's
    /// runs of metal into regions, and calls it a metal map when a region is more than 6 extractor radii across. If not,
    /// its mex denier (luarules/gadgets/cmd_mex_denier.lua) lets extractors go only on the spots it found. Its merging
    /// loses regions: when a run joins two regions, the runs already met on that row still point at the one merged away,
    /// and a later row can fold the live region into that dead one. On this ship's decks every region is lost so: it
    /// finds no spots and no metal map, and every extractor is refused (1.1's fault). Cloud9 never runs the test (it's on
    /// BAR's own list of metal maps). So a run of MarkerCells cells of trace metal (MarkerRaw, 1/79 of a deck cell's) on
    /// row MarkerRow in each top corner of space: the first metal the scan meets, alone on its row and touching nothing,
    /// so nothing merges it away, and it is wider than 6 extractor radii: BAR reads a metal map from it whatever becomes
    /// of the decks' regions. An extractor on it (only an air constructor could get one there) would take 0.02 metal/s.
    /// </summary>
    public const byte MarkerRaw = 1;
    public const int MarkerRow = 1, MarkerCells = 16;

    /// <summary>Whether metal cell (x, z) of a map w cells wide is in a marker strip (cells 1 to 16, and their mirror images;
    /// BAR's scan skips the outermost ring).</summary>
    public static bool Marker(int x, int z, int w) => z == MarkerRow && (x >= 1 && x <= MarkerCells || x >= w - 1 - MarkerCells && x <= w - 2);

    public static byte[] Map(Terrain.Result t)
    {
        int w = Layout.SquaresX / 2, h = Layout.SquaresZ / 2;
        var m = new byte[w * h];
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
                if (t.Floor[(2 * z + 1) * t.NX + 2 * x + 1])
                    m[z * w + x] = Layout.MetalRaw;
        for (int x = 0; x < w; x++)
        {
            if (!Marker(x, MarkerRow, w)) continue;
            int v = (2 * MarkerRow + 1) * t.NX + 2 * x + 1;
            if (t.Cell[v] != Terrain.Space) throw new InvalidOperationException($"metal cell ({x}, {MarkerRow}) of the marker strips isn't over space");
            m[MarkerRow * w + x] = MarkerRaw;
        }
        return m;
    }

    sealed class Region
    {
        public readonly Dictionary<double, double> Left = [], Right = [];
        public double MinZ, MaxZ;
    }

    /// <summary>
    /// BAR's metal-map test, GetSpotsMetal in common/upgets/api_resource_spot_finder.lua, line for line, its merging
    /// included: whether it reads a metal map, how many regions it keeps, and whether it would stop on a Lua error (a row
    /// missing from a region).
    /// </summary>
    public static (bool MetalMap, int Regions, bool Error) BarReads(byte[] metal, int w, int h, double extractorRadius)
    {
        const double M = CellSize;
        double sizeX = w * M - 1.5 * M, sizeZ = h * M - 1.5 * M, start = 1.5 * M, maxStrip = extractorRadius * 6;
        var regions = new List<Region>();
        var left = new List<double> { 0 };
        var right = new List<double> { 0 };
        var owner = new List<Region?> { null };    // the runs, from 1 as in Lua
        long above = 0, working = long.MaxValue;

        void Run(double x1, double x2, double z)
        {
            Region? to = null;
            for (long i = above; i <= working - 1; i++)    // Lua's for takes its bounds once; above moves on for later runs
            {
                if (left[(int)i] > x2 + M) break;
                if (right[(int)i] + M >= x1)
                {
                    var match = owner[(int)i]!;
                    if (to != null)
                    {
                        if (match != to)
                        {
                            for (double iz = match.MinZ; iz <= to.MinZ - M; iz += M)
                                if (match.Left.TryGetValue(iz, out double l)) to.Left[iz] = l; else to.Left.Remove(iz);
                            for (double iz = match.MinZ; iz <= match.MaxZ; iz += M)
                                if (match.Right.TryGetValue(iz, out double r)) to.Right[iz] = r; else to.Right.Remove(iz);
                            if (match.MinZ < to.MinZ) to.MinZ = match.MinZ;
                            to.MaxZ = z;
                            regions.Remove(match);    // table.removeFirst
                        }
                    }
                    else
                    {
                        to = match;
                        to.Left.TryAdd(z, x1);
                        to.Right[z] = x2;
                        to.MaxZ = z;
                    }
                }
                else above++;
            }
            left.Add(x1);
            right.Add(x2);
            if (to != null) owner.Add(to);
            else
            {
                var r = new Region { MinZ = z, MaxZ = z };
                r.Left[z] = x1;
                r.Right[z] = x2;
                owner.Add(r);
                regions.Add(r);
            }
        }

        for (double mz = start; mz <= sizeZ; mz += M)
        {
            above = working;
            working = left.Count;
            double? runStart = null;
            int sz = (int)Math.Floor(mz / M), sx = (int)Math.Floor(start / M);
            for (double mx = start; mx <= sizeX; mx += M)
            {
                bool any = metal[sz * w + sx] > 0;
                sx++;
                if (any) runStart ??= mx;
                else if (runStart is double s)
                {
                    Run(s, mx - M, mz);
                    runStart = null;
                }
            }
            if (runStart is double e) Run(e, sizeX, mz);
        }

        foreach (var g in regions)
        {
            double minX = double.MaxValue, maxX = -1;
            for (double iz = g.MinZ; iz <= g.MaxZ; iz += M)
            {
                if (!g.Left.TryGetValue(iz, out double l) || !g.Right.TryGetValue(iz, out double r)) return (false, regions.Count, true);
                minX = Math.Min(minX, l);
                maxX = Math.Max(maxX, r);
            }
            if (maxX - minX > maxStrip || g.MaxZ - g.MinZ > maxStrip) return (true, regions.Count, false);
        }
        return (false, regions.Count, false);
    }

    /// <summary>A T1 extractor's metal/s at a point by the engine's rule: extractsMetal 0.001 × raw × maxMetal from every
    /// cell whose centre is closer than the extractor radius.</summary>
    public static double T1Income(byte[] metal, int w, int h, P at, double maxMetal, double radius)
    {
        double sum = 0;
        int x0 = (int)((at.X - radius) / CellSize), x1 = (int)((at.X + radius) / CellSize);
        int z0 = (int)((at.Z - radius) / CellSize), z1 = (int)((at.Z + radius) / CellSize);
        for (int z = Math.Max(0, z0); z <= Math.Min(h - 1, z1); z++)
            for (int x = Math.Max(0, x0); x <= Math.Min(w - 1, x1); x++)
            {
                double dx = x * CellSize + 8 - at.X, dz = z * CellSize + 8 - at.Z;
                if (dx * dx + dz * dz < radius * radius) sum += metal[z * w + x];
            }
        return sum * maxMetal * 0.001;
    }
}
