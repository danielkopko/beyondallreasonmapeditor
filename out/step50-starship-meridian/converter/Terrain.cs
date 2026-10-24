// Starship Meridian's heights, per heightmap vertex (8 units apart), built for the port half and copied mirrored, so
// the two halves match exactly:
//   1. each vertex's cell (Layout.Cells) or door, or the solid hull, or space;
//   2. floors: a cell's vertices with no other group's cell within 50 (so a 100-wide bulkhead stands where two cells
//      of different groups touch), and every door's; each floor at its cell's height, the bridge ramps and the stair
//      graded, the reactor's sunken well and its two ramps cut into the reactor's floor;
//   3. the solid hull: rising from the nearest floor at 70° to its cap (bulkheads 340, the rim and the engine housings
//      380, round the bridge 440);
//   4. space: rough ground at about +20 (nothing builds or walks on it; the engine never draws it), and the hull's
//      sheer side down to it.
static class Terrain
{
    public sealed class Result
    {
        public int NX, NZ;
        public float[] H = [];
        /// <summary>Per vertex: the cell it is in (an index into Layout.Cells), -1 the solid hull, -2 space.</summary>
        public int[] Cell = [];
        /// <summary>Per vertex: a floor (a cell's floor or a door's).</summary>
        public bool[] Floor = [];
        /// <summary>Per vertex: a door's index (into Layout.Doors), or -1.</summary>
        public int[] Door = [];
        /// <summary>Per vertex: how far outside the hull's outline (0 inside), and how far inside it (0 outside).</summary>
        public float[] Outside = [], Inside = [];
    }

    public const int Space = -2, Solid = -1;
    static readonly Noise Rough = new(5001);

    public static bool InPolygon(P p, IReadOnlyList<P> poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].Z > p.Z) != (poly[j].Z > p.Z) && p.X < (poly[j].X - poly[i].X) * (p.Z - poly[i].Z) / (poly[j].Z - poly[i].Z) + poly[i].X)
                inside = !inside;
        return inside;
    }

    public static double ToOutline(P p, IReadOnlyList<P> poly)
    {
        double best = double.MaxValue;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            best = Math.Min(best, Geo.ToSegment(p, poly[j], poly[i], out _));
        return best;
    }

    static bool InRect((double X0, double Z0, double X1, double Z1) r, P p) => p.X >= r.X0 && p.X <= r.X1 && p.Z >= r.Z0 && p.Z <= r.Z1;

    public static Result Build()
    {
        int nx = Layout.SquaresX + 1, nz = Layout.SquaresZ + 1, half = nx / 2;          // x index half (768) is the centreline
        var res = new Result { NX = nx, NZ = nz, H = new float[nx * nz], Cell = new int[nx * nz], Floor = new bool[nx * nz], Door = new int[nx * nz], Outside = new float[nx * nz], Inside = new float[nx * nz] };
        var cells = Layout.Cells;
        var doors = Layout.Doors;

        // 1. Cells, doors, the hull.
        Parallel.For(0, nz, z =>
        {
            for (int x = 0; x <= half; x++)
            {
                int i = z * nx + x;
                var p = new P(x * 8.0, z * 8.0);
                bool hull = InPolygon(p, Layout.Hull);
                double edge = ToOutline(p, Layout.Hull);
                res.Outside[i] = hull ? 0 : (float)edge;
                res.Inside[i] = hull ? (float)edge : 0;
                res.Cell[i] = hull ? Solid : Space;
                if (hull)
                    for (int c = 0; c < cells.Length; c++)
                        if (InPolygon(p, cells[c].Outline) || OnOutline(p, cells[c].Outline)) { res.Cell[i] = c; break; }
                res.Door[i] = -1;
                for (int d = 0; d < doors.Length; d++)
                    if (InRect((doors[d].X0, doors[d].Z0, doors[d].X1, doors[d].Z1), p)) { res.Door[i] = d; break; }
            }
        });
        MirrorInto(res.Cell, nx, nz, MirrorCell);
        MirrorInto(res.Door, nx, nz, MirrorDoor);
        MirrorInto(res.Outside, nx, nz, v => v);
        MirrorInto(res.Inside, nx, nz, v => v);

        // 2. Floors: a cell's vertex is floor unless another group's cell is within 50; a door's always is.
        Parallel.For(0, nz, z =>
        {
            for (int x = 0; x <= half; x++)
            {
                int i = z * nx + x;
                if (res.Door[i] >= 0) { res.Floor[i] = true; continue; }
                int c = res.Cell[i];
                if (c < 0) continue;
                bool walled = false;
                for (int dz = -6; dz <= 6 && !walled; dz++)
                    for (int dx = -6; dx <= 6 && !walled; dx++)
                    {
                        if (dx * dx + dz * dz > 6.25 * 6.25) continue;
                        int ox = x + dx, oz = z + dz;
                        if (ox < 0 || oz < 0 || ox >= nx || oz >= nz) continue;
                        int o = res.Cell[oz * nx + ox];
                        if (o >= 0 && o != c && cells[o].Group != cells[c].Group) walled = true;
                    }
                res.Floor[i] = !walled;
            }
        });
        MirrorInto(res.Floor, nx, nz, v => v);

        // The floors' heights.
        var floorH = new float[nx * nz];
        Parallel.For(0, nz, z =>
        {
            for (int x = 0; x <= half; x++)
            {
                int i = z * nx + x;
                if (res.Floor[i]) floorH[i] = (float)FloorHeight(res, new P(x * 8.0, z * 8.0), i);
            }
        });
        MirrorInto(floorH, nx, nz, v => v);                 // the walls by the centreline rise from the starboard floors too

        // 3 and 4. The solid hull and space.
        Parallel.For(0, nz, z =>
        {
            for (int x = 0; x <= half; x++)
            {
                int i = z * nx + x;
                var p = new P(x * 8.0, z * 8.0);
                if (res.Floor[i]) { res.H[i] = floorH[i]; continue; }
                if (res.Cell[i] == Space)
                {
                    // Rough (too steep to build on or walk, but above the water level, so there is no water), the hull's side down to it.
                    double rough = Layout.Space + 40 * Math.Abs(Rough.Fbm(Math.Abs(p.X - Layout.CX) / 26, p.Z / 26, 2));
                    res.H[i] = (float)Math.Max(rough, Layout.Rim - res.Outside[i] * 6);
                    continue;
                }
                double top = Top(p, res.Inside[i]);
                double best = top;
                for (int dz = -10; dz <= 10; dz++)
                    for (int dx = -10; dx <= 10; dx++)
                    {
                        int ox = x + dx, oz = z + dz;
                        if (ox < 0 || oz < 0 || ox >= nx || oz >= nz) continue;
                        int o = oz * nx + ox;
                        if (!res.Floor[o]) continue;
                        double d = Math.Sqrt(dx * dx + dz * dz) * 8 - 4;
                        best = Math.Min(best, floorH[o] + Math.Max(0, d) * Layout.WallRise);
                    }
                res.H[i] = (float)best;
            }
        });
        MirrorInto(res.H, nx, nz, v => v);
        return res;
    }

    static bool OnOutline(P p, P[] poly) => ToOutline(p, poly) < 0.5;

    /// <summary>A floor's height: its cell's, or graded along the bridge ramps and the stair, or the reactor's well and its ramps.</summary>
    static double FloorHeight(Result res, P p, int i)
    {
        int d = res.Door[i];
        if (d >= 0 && Layout.Doors[d].Kind == Layout.DoorKind.Stairs)
        {
            var (_, z0, _, z1) = Layout.Stairs;
            return Layout.BridgeDeck + (Layout.Deck - Layout.BridgeDeck) * Math.Clamp((p.Z - z0) / (z1 - z0), 0, 1);
        }
        // A door is at the deck's height, but where it opens onto the reactor's well ramp it follows the ramp.
        if (d >= 0 && (res.Cell[i] < 0 || Layout.Cells[res.Cell[i]].Name != "Main Reactor")) return Layout.Deck;
        var cell = Layout.Cells[res.Cell[i]];
        if (cell.Kind == Layout.Kind.Ramp)
            return Layout.BridgeDeck + (Layout.Deck - Layout.BridgeDeck) * Math.Clamp((p.Z - Layout.RampTopZ) / (Layout.RampFootZ - Layout.RampTopZ), 0, 1);
        if (cell.Name == "Main Reactor")
        {
            // The well: its wall at 70° inside its rim, its two ramps (from each crossing door down to the floor).
            double r = (p - Layout.WellAt).Length;
            double h = r < Layout.WellR ? Math.Max(Layout.WellFloor, Layout.Deck - (Layout.WellR - r) * Layout.WallRise) : Layout.Deck;
            var (x0, z0, x1, z1) = Layout.WellRampPort;
            double ax = p.X <= Layout.CX ? p.X : Layout.Mirror(p.X);
            if (ax >= x0 && ax <= x1 && p.Z >= z0 && p.Z <= z1)
                h = Layout.Deck + (Layout.WellFloor - Layout.Deck) * Math.Clamp((ax - x0) / (x1 - x0), 0, 1);
            return h;
        }
        return cell.Floor;
    }

    /// <summary>The solid hull's height where no floor is near: the rim, the engine housings, round the bridge, or a bulkhead.</summary>
    static double Top(P p, double inside)
    {
        if (inside < Layout.RimBand) return Layout.Rim;
        foreach (var e in Layout.EngineHousings) if (InRect(e, p)) return Layout.Rim;
        if (p.Z < 4000 && Math.Abs(p.X - Layout.CX) < 1700) return Layout.BridgeWalls;
        return Layout.Bulkhead;
    }

    /// <summary>The cell (by index) that is a cell's mirror image.</summary>
    static readonly Dictionary<int, int> mirrorCells = BuildMirrorCells();

    static Dictionary<int, int> BuildMirrorCells()
    {
        var map = new Dictionary<int, int>();
        var cells = Layout.Cells;
        for (int a = 0; a < cells.Length; a++)
        {
            var c = cells[a];
            var mirrored = c.Outline.Select(Layout.Mirror).ToArray();
            for (int b = 0; b < cells.Length; b++)
                if (cells[b].Name == c.Name && cells[b].Outline.All(q => mirrored.Any(m => (m - q).Length < 0.5)))
                { map[a] = b; break; }
        }
        return map;
    }

    static int MirrorCell(int c) => c < 0 ? c : mirrorCells[c];

    static readonly Dictionary<int, int> mirrorDoors = BuildMirrorDoors();

    static Dictionary<int, int> BuildMirrorDoors()
    {
        var map = new Dictionary<int, int>();
        var d = Layout.Doors;
        for (int a = 0; a < d.Length; a++)
            for (int b = 0; b < d.Length; b++)
                if (Math.Abs(Layout.Mirror(d[a].X1) - d[b].X0) < 0.5 && Math.Abs(Layout.Mirror(d[a].X0) - d[b].X1) < 0.5 && Math.Abs(d[a].Z0 - d[b].Z0) < 0.5 && Math.Abs(d[a].Z1 - d[b].Z1) < 0.5)
                { map[a] = b; break; }
        return map;
    }

    static int MirrorDoor(int d) => d < 0 ? d : mirrorDoors[d];

    /// <summary>Copies the port half (x up to the centreline) onto the starboard half, mapping each value.</summary>
    static void MirrorInto<T>(T[] a, int nx, int nz, Func<T, T> map)
    {
        Parallel.For(0, nz, z =>
        {
            for (int x = nx / 2 + 1; x < nx; x++)
                a[z * nx + x] = map(a[z * nx + (nx - 1 - x)]);
        });
    }

    public static double Sample(float[] f, int nx, int nz, P p)
    {
        double fx = Math.Clamp(p.X / 8, 0, nx - 1.001), fz = Math.Clamp(p.Z / 8, 0, nz - 1.001);
        int x = (int)fx, z = (int)fz;
        double tx = fx - x, tz = fz - z;
        return (f[z * nx + x] * (1 - tx) + f[z * nx + x + 1] * tx) * (1 - tz) + (f[(z + 1) * nx + x] * (1 - tx) + f[(z + 1) * nx + x + 1] * tx) * tz;
    }

    /// <summary>The value at the vertex nearest a point.</summary>
    public static T Nearest<T>(T[] f, int nx, int nz, P p) =>
        f[Math.Clamp((int)Math.Round(p.Z / 8), 0, nz - 1) * nx + Math.Clamp((int)Math.Round(p.X / 8), 0, nx - 1)];
}
