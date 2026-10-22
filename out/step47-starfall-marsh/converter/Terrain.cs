// Starfall Marsh's heights, per heightmap vertex (8 units apart), the north half made and the south its mirror image:
//   1. the jungle floor at 40, in hummocks, nearly level in the bases;
//   2. the furrow the ship ploughed, along the middle: a floor 14 under water, banks, and berms of the dirt it threw up;
//   3. marsh streams draining into it, wadeable (10 deep), in shallow valleys, and pools (a few too deep to wade);
//   4. the mound: dirt pushed up over the buried nose, steep where the nose went in, lumpy, its west face terraced in
//      slumped layers;
//   5. level pads for the metal spots, vents and the prow on the mound.
static class Terrain
{
    public sealed class Result
    {
        public int N;
        public float[] H = [];
        /// <summary>How much of the mound each vertex is (0 off it, 1 at its heart).</summary>
        public float[] Mound = [];
    }

    static readonly Noise R1 = new(301), R2 = new(302), R3 = new(303), Lumps = new(304), Berm = new(305), Floor = new(306);

    public static readonly List<P>[] StreamLines = Layout.Streams.Select(s => Geo.Smooth(s, 40)).ToArray();
    static readonly List<P> TrackLine = Geo.Smooth(Layout.MoundTrack, 30);
    static readonly double TrackLength = Enumerable.Range(0, TrackLine.Count - 1).Sum(i => (TrackLine[i + 1] - TrackLine[i]).Length);
    // The track's ends: the ground at its foot, and the mound's top where it arrives (before the track itself).
    static double TrackFoot, TrackTop;
    static bool trackKnown;

    public static void Prepare(Mirror mir)
    {
        trackKnown = false;
        padsOff = true;
        TrackFoot = Height(mir, Layout.MoundTrack[0], out _);
        TrackTop = Height(mir, Layout.MoundTrack[^1], out _);
        padsOff = false;
        trackKnown = true;
    }

    public static Result Build(Mirror mir)
    {
        Prepare(mir);
        int n = Layout.Squares + 1;
        var res = new Result { N = n, H = new float[n * n], Mound = new float[n * n] };
        Parallel.For(0, n, z =>
        {
            for (int x = 0; x < n; x++)
            {
                var p = new P(x * 8.0, z * 8.0);
                double h = Height(mir, p, out double mound);
                res.H[z * n + x] = (float)h;
                res.Mound[z * n + x] = (float)mound;
            }
        });
        return res;
    }

    /// <summary>The mound's share at a point: 1 at its heart, falling to 0 at its edge; steeper on the west.</summary>
    public static double MoundBell(P p, out double r)
    {
        double dx = p.X - Layout.MoundTop.X, dz = p.Z - Layout.Axis;
        double rx = dx < 0 ? Layout.MoundWest : Layout.MoundEast;
        r = Math.Sqrt(dx * dx / (rx * rx) + dz * dz / (Layout.MoundAcross * Layout.MoundAcross));
        return Geo.S((1 - r) / 0.8);
    }

    public static double StreamDistance(P q) => StreamLines.Min(l => Geo.ToPolyline(q, l, out _));

    public static double PoolDepth(P q, out double inside)
    {
        double best = 0;
        inside = 99;
        foreach (var (at, rx, rz, depth) in Layout.Pools)
        {
            double r = Math.Sqrt((q.X - at.X) * (q.X - at.X) / (rx * rx) + (q.Z - at.Z) * (q.Z - at.Z) / (rz * rz));
            inside = Math.Min(inside, r);
            if (r < 1) best = Math.Max(best, depth);
        }
        return best;
    }

    public static double Height(Mirror mir, P p, out double moundShare)
    {
        var q = mir.North(p);
        double d = mir.ToLine(p);
        // 1. The jungle floor.
        double relief = mir.Value(p, a => 16 * R1.Fbm(a.X / 650, a.Z / 650, 3) + 5 * R2.Fbm(a.X / 150, a.Z / 150, 2));
        double toStart = Layout.Starts.Min(s => (s - q).Length);
        relief *= 0.2 + 0.8 * Geo.S((toStart - Layout.BaseRadius) / 300);
        double h = Layout.Ground + relief;

        // 2. The furrow and its berms (fading out under the mound).
        double berm = Layout.BermHeight * (0.7 + 0.5 * mir.Value(p, a => Berm.Fbm(a.X / 500, 7.3, 2))) *
                      (1 - Geo.S(Math.Abs(d - Layout.BermCrest) / Layout.BermHalf)) * (1 - Geo.S((q.X - (Layout.FurrowEast - 300)) / 400));
        h += Math.Max(0, berm);
        double floor = Layout.FurrowFloor + 3 * mir.Value(p, a => Floor.Fbm(a.X / 120, a.Z / 120, 2));
        double across = Math.Max(Geo.S((d - Layout.FurrowHalf) / Layout.FurrowBank), Geo.S((q.X - Layout.FurrowEast) / 260));
        double furrow = floor + (h - floor) * across;
        h = Math.Min(h, furrow);

        // 3. Streams and pools: wadeable beds in shallow valleys.
        double sd = StreamDistance(q);
        if (sd < 420)
        {
            double bed = -10 + 2 * mir.Value(p, a => Floor.Fbm(a.X / 90 + 3, a.Z / 90, 2));
            h = Math.Min(h, bed + (h - bed) * Geo.S((sd - 60) / 340));
        }
        for (int i = 0; i < Layout.Pools.Length; i++)
        {
            var (at, rx, rz, depth) = Layout.Pools[i];
            double ex = (q.X - at.X) / rx, ez = (q.Z - at.Z) / rz;
            double r = Math.Sqrt(ex * ex + ez * ez);
            if (r > 2.4) continue;
            // A ragged shore: the radius wanders round the pool.
            double a = Math.Atan2(ez, ex);
            r /= 1 + 0.22 * Lumps.Fbm(Math.Cos(a) * 1.6 + i * 7.1, Math.Sin(a) * 1.6 + i * 3.3, 3);
            double rim = (r - 1) * Math.Min(rx, rz);
            h = Math.Min(h, -depth + (h + depth) * Geo.S((rim + 40) / 300));
        }
        // The marsh round the streams and the furrow: a mosaic of shallow pools and mud, wadeable.
        double toWater = Math.Min(sd, q.X < Layout.FurrowEast ? Math.Max(0, d - Layout.FurrowHalf - Layout.FurrowBank) : 9999);
        double marsh = (1 - Geo.S((toWater - 300) / 700)) * Geo.S((toStart - Layout.BaseRadius - 150) / 300);
        if (marsh > 0 && h > 0)
        {
            // The ground sinks to the water's level, and puddles stand in its hollows.
            double low = 7 + 5 * mir.Value(p, a => R2.Fbm(a.X / 300 + 5, a.Z / 300, 2));
            h += (Math.Min(h, low) - h) * marsh * 0.9;
            double puddle = mir.Value(p, a => Floor.Fbm(a.X / 240 + 11, a.Z / 240, 3));
            double wet = Geo.S((puddle - 0.1) / 0.16) * marsh;
            h += (Math.Min(h, -5) - h) * wet;
        }

        // 4. The mound, rising from whatever is beneath it.
        double bell = MoundBell(p, out double mr);
        moundShare = bell;
        if (bell > 0)
        {
            double lumps = mir.Value(p, a => 60 * Lumps.Fbm(a.X / 380, a.Z / 380, 4));
            // Gullies cut straight down its flanks by centuries of rain, running out from the crest.
            double gully = Math.Abs(mir.Value(p, a =>
            {
                double ang = Math.Atan2(Math.Abs(a.Z - Layout.Axis) / Layout.MoundAcross, (a.X - Layout.MoundTop.X) / (a.X < Layout.MoundTop.X ? Layout.MoundWest : Layout.MoundEast));
                return Lumps.Fbm(ang * 7 + 9, mr * 1.5, 2);
            }));
            lumps -= 26 * (1 - Geo.S(gully / 0.12)) * Geo.S((mr - 0.3) / 0.3) * Geo.S((0.95 - mr) / 0.25);
            // Slumped terraces on the west face: the dirt fell back in steps.
            double westFace = Geo.S((Layout.MoundTop.X - q.X) / 300) * Geo.S((1.05 - mr) / 0.3);
            double terrace = westFace * 6 * Math.Sin(mr * 22 + 3 * Lumps.At(q.X / 200, q.Z / 300));
            double top = Layout.Ground + Layout.MoundPeak + lumps + terrace;
            h += (top - h) * bell;
        }

        // 4a. Dirt spilled west over the fore hull's nose: it rises over the hull, so the hull dives into the ground.
        if (q.X > 4450 && q.X < 5400 && d < 700)
        {
            double spill = (240 + 40 * mir.Value(p, a => Lumps.Fbm(a.X / 160 + 4, a.Z / 160, 2))) * Geo.S((q.X - 4520) / 480) * (1 - Geo.S((d - 360) / 260));
            h = Math.Max(h, Layout.FurrowFloor + spill);
        }

        // 4b. The track up the mound: a steady grade from its foot to the top, cut and filled, its banks blended.
        double td = Geo.ToPolyline(q, TrackLine, out double along);
        if (trackKnown && td < Layout.TrackHalf + 170)
        {
            double grade = TrackFoot + (TrackTop - TrackFoot) * Math.Clamp(along / TrackLength, 0, 1);
            h = grade + (h - grade) * Geo.S((td - Layout.TrackHalf) / 170);
        }

        // 5. Level pads: metal spots and vents (extractors and geothermals need flat ground), and the prow.
        if (!padsOff)
        {
            foreach (var (at, _, _) in Layout.Spots) h = Pad(mir, h, q, at, 70);
            foreach (var v in Layout.Vents) h = Pad(mir, h, q, v, 90);
            h = Pad(mir, h, q, Layout.MoundTop, 170);                       // the prow
        }
        return h;
    }

    static double Pad(Mirror mir, double h, P q, P at, double radius)
    {
        double dist = (q - at).Length;
        if (dist > radius + 200) return h;
        double level = Height0(mir, at);
        return level + (h - level) * Geo.S((dist - radius) / 200);
    }

    /// <summary>The height at a pad's centre, before the pads (so every pad is level).</summary>
    static double Height0(Mirror mir, P at)
    {
        lock (pads)
        {
            if (pads.TryGetValue(at, out double v)) return v;
        }
        double h = HeightNoPads(mir, at);
        lock (pads) pads[at] = h;
        return h;
    }

    static readonly Dictionary<P, double> pads = [];

    static double HeightNoPads(Mirror mir, P at)
    {
        // The same as Height, without step 5.
        var saved = padsOff;
        padsOff = true;
        try { return Height(mir, at, out _); }
        finally { padsOff = saved; }
    }

    [ThreadStatic] static bool padsOff;
}
