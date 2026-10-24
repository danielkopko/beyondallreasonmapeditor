// The natural ground away from the mine's works: low swells, small mounds (real heights, so they slow what crosses
// them), and dry washes (pale gravel beds, the main ones a few units deep, the rills only paint). None of it within
// reach of man's works: the pit and its ramps, the roads (which stay flat across), the pads and hardstands, the dumps,
// the tailings dam, the railway and the conveyor, the bases, the spots, the magazine, the breakaways.
static class Natural
{
    static readonly Noise Swell = new(1101), Lump = new(1102), WarpX = new(1103), WarpZ = new(1104), Main = new(1105),
        MainOn = new(1106), Rill = new(1107), RillOn = new(1108);

    /// <summary>The main washes' depth (they're shallow: a step down a vehicle notices, not a ditch).</summary>
    public const double WashDepth = 5;

    /// <summary>
    /// How wild ground is (1 untouched, 0 man's), with the roads' margin wide (for the heights, so roads stay flat
    /// across and their verges even) or narrow (for the paint, so a wash runs right up to a road's edge and the road
    /// crosses it, as a floodway does).
    /// </summary>
    public static double Wild(Terrain.Result t, P p, double fromRim, bool forPaint)
    {
        double w = Geo.S((fromRim - 200) / 250);                                                  // the pit
        if (w <= 0) return 0;
        foreach (var s in Layout.StartsA.Concat(Layout.StartsB)) w = Math.Min(w, Geo.S(((s - p).Length - 1000) / 250));
        foreach (var s in Layout.Spots) w = Math.Min(w, Geo.S(((s.At - p).Length - 220) / 120));
        foreach (var (at, a, b) in Terrain.Pads) w = Math.Min(w, Geo.S((Terrain.PadRho(p, at, a + 200, b + 200) - 1) * 5));
        foreach (var (at, a, b) in Layout.Hardstands) w = Math.Min(w, Geo.S((Terrain.PadRho(p, at, a + 160, b + 160) - 1) * 5));
        if (w <= 0) return 0;
        double road = Terrain.ToLines(p, Terrain.RoadLines.Concat(Terrain.DumpRampLines).Concat(t.Ramps).ToArray());
        w = Math.Min(w, forPaint ? Geo.S((road - 70) / 40) : Geo.S((road - 170) / 180));
        double rail = Math.Max(Math.Abs(p.X - Layout.RailBedX) - Layout.RailBedHalf, p.Z - (t.ShedSouth + 120));
        w = Math.Min(w, Geo.S((rail - 120) / 150));
        w = Math.Min(w, Geo.S((Geo.ToPolyline(p, Terrain.ConveyorLine, out _) - 120) / 150));
        for (int k = 0; k < Layout.Dumps.Length; k++) w = Math.Min(w, Geo.S((Terrain.DumpRho(p, k) - 1.12) / 0.15));
        double dam = Math.Max(Math.Max(Layout.TsfX0 - p.X, p.X - Layout.TsfX1), Math.Max(Layout.TsfZ0 - p.Z, p.Z - Layout.TsfZ1));
        w = Math.Min(w, Geo.S((dam - 220) / 200));
        for (int k = 0; k < Layout.Breakaways.Length; k++) w = Math.Min(w, Geo.S((Terrain.BreakawayRho(p, k) - 1.2) / 0.2));
        w = Math.Min(w, Geo.S(((p - Layout.Magazine).Length - 280) / 150));
        return w;
    }

    static double Hash(int x, int z, int seed)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(seed * 83492791);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return (h & 0xFFFF) / 65535.0;
    }

    /// <summary>The small mounds: on a jittered grid of 380, about half the cells hold one, 90 to 230 across its long
    /// axis, 6 to 18 high, smooth-shouldered (the steepest about 17°) and a little lumpy.</summary>
    public static double Mound(P p)
    {
        const double Cell = 380;
        int cx = (int)Math.Floor(p.X / Cell), cz = (int)Math.Floor(p.Z / Cell);
        double best = 0;
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int ix = cx + dx, iz = cz + dz;
                if (Hash(ix, iz, 1) > 0.55) continue;
                var c = new P((ix + 0.2 + 0.6 * Hash(ix, iz, 2)) * Cell, (iz + 0.2 + 0.6 * Hash(ix, iz, 3)) * Cell);
                double r = 90 + 140 * Hash(ix, iz, 4), height = 6 + 12 * Hash(ix, iz, 5), turn = Math.PI * Hash(ix, iz, 6);
                double stretch = 1 + 0.6 * Hash(ix, iz, 7);
                var q = p - c;
                double u = (q.X * Math.Cos(turn) + q.Z * Math.Sin(turn)) / stretch, v = -q.X * Math.Sin(turn) + q.Z * Math.Cos(turn);
                double d = Math.Sqrt(u * u + v * v) / r;
                if (d >= 1) continue;
                double steep = Math.Min(height, r * 0.2);                                         // keeps the shoulders under about 17°
                best = Math.Max(best, steep * (1 - Geo.S(d)) * (1 + 0.15 * Lump.At(p.X / 50, p.Z / 50)));
            }
        return best;
    }

    /// <summary>The washes: meandering lines (the zero-crossings of a warped noise), broken into stretches (so they
    /// don't close into rings), the rills finer and fainter. Main is 0..1 across a main wash's bed; Any the strongest of either.</summary>
    public static (double Main, double Any) Wash(P p)
    {
        var q = p + new P(320 * WarpX.Fbm(p.X / 900, p.Z / 900, 2), 320 * WarpZ.Fbm(p.X / 900 + 40, p.Z / 900, 2));
        double main = (1 - Geo.S(Math.Abs(Main.Fbm(q.X / 1600, q.Z / 1600, 3)) / 0.032)) * Geo.S((MainOn.Fbm(p.X / 3000, p.Z / 3000, 2) + 0.15) / 0.3);
        double rill = (1 - Geo.S(Math.Abs(Rill.Fbm(q.X / 650, q.Z / 650, 2)) / 0.024)) * Geo.S((RillOn.Fbm(p.X / 1100, p.Z / 1100, 2) - 0.1) / 0.25) * 0.7;
        return (main, Math.Max(main, rill));
    }

    /// <summary>The gentle swells of the plain (±10 over a couple of thousand).</summary>
    public static double Swells(P p) => 10 * Swell.Fbm(p.X / 2200, p.Z / 2200, 2);
}
