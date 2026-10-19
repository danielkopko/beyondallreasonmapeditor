namespace MapCore.Smf;

/// <summary>
/// The SMF grass map: one byte per 32x32 game units. The engine's own grass treats any non-zero value as grass.
/// BAR draws grass itself (luaui/Widgets/map_grass_gl4.lua): 0 = none, 1 = old-style on/off grass, and
/// 2-254 scale blade size from grassMinSize to grassMaxSize (tunable in mapinfo custom.grassConfig; a
/// grassDistTGA there replaces this map entirely).
/// </summary>
public static class GrassLayer
{
    public const int CellSize = 32;
    public const byte MaxValue = 254;

    public static byte ValueAt(SmfFile smf, double x, double z)
    {
        if (smf.GrassMap == null)
            return 0;
        int cx = (int)Math.Floor(x / CellSize), cz = (int)Math.Floor(z / CellSize);
        return cx < 0 || cz < 0 || cx >= smf.GrassMapWidth || cz >= smf.GrassMapHeight ? (byte)0 : smf.GrassMap[cz * smf.GrassMapWidth + cx];
    }

    /// <summary>
    /// Sets every cell whose centre lies within <paramref name="radius"/> of (x, z) to <paramref name="value"/>
    /// (0 removes grass; values above 254 are clamped). Creates the grass map when adding grass to a map without one.
    /// Returns the number of cells changed.
    /// </summary>
    public static int Paint(SmfFile smf, double x, double z, double radius, byte value)
    {
        value = Math.Min(value, MaxValue);
        if (smf.GrassMap == null)
        {
            if (value == 0)
                return 0;
            smf.GrassMap = new byte[smf.GrassMapWidth * smf.GrassMapHeight];
        }

        int changed = 0;
        int cz0 = Math.Max(0, (int)((z - radius) / CellSize)), cz1 = Math.Min(smf.GrassMapHeight - 1, (int)((z + radius) / CellSize));
        int cx0 = Math.Max(0, (int)((x - radius) / CellSize)), cx1 = Math.Min(smf.GrassMapWidth - 1, (int)((x + radius) / CellSize));
        for (int cz = cz0; cz <= cz1; cz++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                double dx = (cx + 0.5) * CellSize - x, dz = (cz + 0.5) * CellSize - z;
                int i = cz * smf.GrassMapWidth + cx;
                if (dx * dx + dz * dz <= radius * radius && smf.GrassMap[i] != value)
                {
                    smf.GrassMap[i] = value;
                    changed++;
                }
            }
        return changed;
    }

    public static int CellsWithGrass(SmfFile smf) => smf.GrassMap?.Count(v => v != 0) ?? 0;
}
