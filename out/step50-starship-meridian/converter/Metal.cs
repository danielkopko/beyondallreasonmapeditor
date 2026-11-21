// The metal map (1.1): the whole ship reads as metal, so Starship Meridian is a metal map. Every metal cell (16 × 16
// game units) whose centre is a deck floor vertex holds Layout.MetalRaw; walls, hull tops and space hold none. A cell's
// centre (16i + 8, 16j + 8) is a heightmap vertex, so cell i's mirror image is cell (width − 1 − i), as the floors are.
static class Metal
{
    public const int CellSize = 16;

    public static byte[] Map(Terrain.Result t)
    {
        int w = Layout.SquaresX / 2, h = Layout.SquaresZ / 2;
        var m = new byte[w * h];
        for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
                if (t.Floor[(2 * z + 1) * t.NX + 2 * x + 1])
                    m[z * w + x] = Layout.MetalRaw;
        return m;
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
