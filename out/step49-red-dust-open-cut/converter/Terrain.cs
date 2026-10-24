// Red Dust Open Cut's heights, per heightmap vertex (8 units apart):
//   1. the outback plain at 380, gently rolling, level in the bases and on the pads, eased along the haul roads;
//   2. dry creeks; breakaways (low mesas with red scarps); the tailings dam (an embankment round a flat beach);
//      the waste dumps (two lifts with scalloped tip-head edges, each with a graded haul ramp, half the top lift
//      paddock-dumped in rows of truck loads, half dozed flat); the ROM pad's stockpiles;
//   3. the pit: benches from an exact distance-to-the-rim field (so every bench runs parallel to the rim and every
//      face stays steep), the floor at 10, a sump with the lake at the south end; the bucket wheel excavator's
//      terrace at the north end; the slide on the north-east wall (one slope, bots only);
//   4. the two haul ramps, each at one grade along its line from the rim to the floor, cut and filled;
//   5. level pads for the metal spots and vents.
// Also, on the plain: the rail bed, dead level from the north edge to the loco shed's apron; hardstands for the
// tanks, towers and sheds; the earth bund round the explosives magazine. And on the natural ground away from all of
// that (Natural.cs): gentle swells, small mounds and the main washes' shallow beds; the roads flat across.
static class Terrain
{
    public sealed class Result
    {
        public int N;
        public float[] H = [];
        /// <summary>Depth into the pit from its rim (units; 0 outside).</summary>
        public float[] Depth = [];
        public List<P>[] Ramps = [];
        public double[][] RampHeights = [];
        public List<(P At, double Income, string Side)> PitSpots = [];
        public P Vent;
        /// <summary>The railway along the main line (z): the pit's rim, the shed's two ends, the track's end in the shed, road 7's crossing.</summary>
        public double RimZ, ShedNorth, ShedSouth, TrackEnd, CrossingZ;
        /// <summary>The tracks: the main line, and the siding with its turnouts.</summary>
        public List<P>[] Rails = [];
        /// <summary>The bucket wheel excavators on the lobe's floor: where each stands and the way it faces (into its wall).</summary>
        public List<(P At, P Dir)> PitWheels = [];
        /// <summary>Per vertex: the distance outside the pit's rim (0 inside); the natural ground's mounds and swells (the
        /// height they add, and the mounds' alone); the main washes' cut; the washes as the painter shows them (0..1, wild ground only).</summary>
        public float[] FromRim = [], Rise = [], Mound = [], Cut = [], Wash = [];
    }

    /// <summary>The overland conveyor's line: the crusher, the transfer tower, the loadout's incline foot.</summary>
    public static readonly List<P> ConveyorLine = [Layout.Crusher, Layout.Transfer, Layout.LoadoutFoot];

    static readonly Noise R1 = new(901), R2 = new(902), Wob = new(903), Bench = new(904), Floor = new(905), Cap = new(906), Edge = new(907);

    public static readonly List<P>[] CreekLines = Layout.Creeks.Select(c => Geo.Smooth(c, 40)).ToArray();
    public static readonly List<P>[] RoadLines = Layout.Roads.Select(r => Geo.Smooth(r, 40)).ToArray();
    public static readonly List<P>[] DumpRampLines = Layout.DumpRamps.Select(r => Geo.Smooth(r, 30)).ToArray();

    // ---- The pit's frame.

    public static (double U, double V) Frame(P p)
    {
        var q = p - Layout.PitCentre;
        return (q.Dot(Layout.Along), q.Dot(Layout.Across));
    }

    public static P FromFrame(double u, double v) => Layout.PitCentre + Layout.Along * u + Layout.Across * v;

    /// <summary>The angle round the pit (degrees: 0 the south end, 90 the east side, 180 the north end, 270 the west).</summary>
    public static double Angle(P p)
    {
        var (u, v) = Frame(p);
        double a = Math.Atan2(v / Layout.PitB, u / Layout.PitA) * 180 / Math.PI;
        return (a + 360) % 360;
    }

    /// <summary>How far out a point is relative to the pit's rim (1 on it): the old pit's tilted oval, its rim
    /// wavering, joined softly to the southern extension (a lobe with a wavering wall and two cutbacks by the edge).</summary>
    static double Rho(P p)
    {
        var (u, v) = Frame(p);
        double a = Math.Atan2(v / Layout.PitB, u / Layout.PitA);
        double wobble = 1 + 0.055 * Wob.Fbm(Math.Cos(a) * 1.4 + 3, Math.Sin(a) * 1.4, 3);
        double north = Math.Sqrt(u * u / (Layout.PitA * Layout.PitA) + v * v / (Layout.PitB * Layout.PitB)) / wobble;
        double dx = (p.X - Layout.SouthCentre.X) / Layout.SouthHalfX, dz = (p.Z - Layout.SouthCentre.Z) / Layout.SouthHalfZ;
        double south = Math.Sqrt(dx * dx + dz * dz) - 0.07 * Edge.Fbm(p.X / 900 + 21, p.Z / 900 + 5, 3);
        foreach (var (at, ca, cb) in Layout.Cutbacks) south = SoftMin(south, Ellipse(p, at, ca, cb, 0), 0.06);
        return SoftMin(north, south, 0.08);
    }

    /// <summary>A smooth minimum: the lesser of two, rounded where they are close (so joined shapes meet in a fillet).</summary>
    static double SoftMin(double a, double b, double k)
    {
        double m = Math.Min(a, b);
        return m - k * Math.Log(Math.Exp(-(a - m) / k) + Math.Exp(-(b - m) / k));
    }

    /// <summary>A ray out from the old pit's centre at an angle round it, and from the extension's centre round it.</summary>
    static P NorthRay(double deg)
    {
        double a = deg * Math.PI / 180;
        return (Layout.Along * (Math.Cos(a) * Layout.PitA) + Layout.Across * (Math.Sin(a) * Layout.PitB)).Unit;
    }

    static P SouthRay(double deg)
    {
        double a = deg * Math.PI / 180;
        return new P(Math.Sin(a) * Layout.SouthHalfX, Math.Cos(a) * Layout.SouthHalfZ).Unit;
    }

    /// <summary>The four haul ramps: their names, and the ray each follows round the wall (origin, ray, from and to angles).</summary>
    public static readonly string[] RampNames = ["west", "east", "south-west", "south-east"];
    static readonly (P Origin, Func<double, P> Ray, double From, double To)[] RampWays =
    [
        (Layout.PitCentre, NorthRay, Layout.RampWestFrom, Layout.RampWestTo),
        (Layout.PitCentre, NorthRay, Layout.RampEastFrom, Layout.RampEastTo),
        (Layout.SouthCentre, SouthRay, Layout.RampSouthWestFrom, Layout.RampSouthWestTo),
        (Layout.SouthCentre, SouthRay, Layout.RampSouthEastFrom, Layout.RampSouthEastTo),
    ];

    static double InSector(double angle, double from, double to, double soft)
    {
        // Weight of an angle inside [from, to] (either way round), softened over `soft` degrees.
        double lo = Math.Min(from, to), hi = Math.Max(from, to);
        double inside = Math.Min(angle - lo, hi - angle);
        return Geo.S(inside / soft + 0.5);
    }

    /// <summary>The pit's surface at a depth from the rim: a face dropping one bench, then a berm, six times, then the floor.</summary>
    static double Benched(double d, double rough)
    {
        double drop = (Layout.Plain - Layout.PitFloor) / Layout.Benches, pitch = Layout.Berm + Layout.Face;
        if (d <= 0) return Layout.Plain;
        int k = (int)Math.Floor(d / pitch);
        if (k >= Layout.Benches) return Layout.PitFloor;
        double x = d - k * pitch;
        double fall = Math.Min(1, x / Layout.Face);
        return Layout.Plain - drop * (k + fall) + (fall >= 1 ? rough : 0);
    }

    public static Result Build()
    {
        int n = Layout.Squares + 1;
        var res = new Result { N = n, H = new float[n * n], Depth = new float[n * n] };
        // The pit's mask and each vertex's distance to its rim.
        var inside = new bool[n * n];
        Parallel.For(0, n, z => { for (int x = 0; x < n; x++) inside[z * n + x] = Rho(new P(x * 8.0, z * 8.0)) < 1; });
        var toRim = Distance(inside.Select(b => !b).ToArray(), n);
        for (int i = 0; i < toRim.Length; i++) res.Depth[i] = inside[i] ? toRim[i] * 8 : 0;
        var outside = Distance(inside, n);
        res.FromRim = outside.Select(d => d * 8).ToArray();
        // The ramps follow depth contours: from just outside the rim to the floor, at one grade.
        res.Ramps = new List<P>[RampWays.Length];
        res.RampHeights = new double[RampWays.Length][];
        for (int k = 0; k < RampWays.Length; k++)
        {
            var (origin, ray, from, to) = RampWays[k];
            var line = new List<P>();
            int steps = 120;
            for (int s = 0; s <= steps; s++)
            {
                double t = s / (double)steps, ang = from + (to - from) * t;
                double want = -120 + t * (Layout.Wall + 200);
                line.Add(AtDepth(res, n, origin, ray(ang), want));
            }
            line = Geo.Smooth(Thin(line, 60), 24);
            double total = 0;
            var heights = new double[line.Count];
            var run = new double[line.Count];
            for (int i = 1; i < line.Count; i++) run[i] = total += (line[i] - line[i - 1]).Length;
            // One grade from the rim to where the line meets the floor, then level onto it: no raised tongue at the foot.
            int toe = line.FindIndex(q => Sample(res.Depth, n, q) >= Layout.Wall);
            double down = toe > 0 ? run[toe] : total;
            for (int i = 0; i < line.Count; i++) heights[i] = Layout.Plain + (Layout.PitFloor + 2 - Layout.Plain) * Math.Clamp(run[i] / down, 0, 1);
            res.Ramps[k] = line;
            res.RampHeights[k] = heights;
        }
        // The pit's spots: halfway down each ramp (beside it, on its deeper side, the berm below), four on the old pit's
        // floor and two on the extension's; the vent on the floor. The west ramps' spots are the west's, the east's the east's.
        for (int k = 0; k < res.Ramps.Length; k++)
        {
            var line = res.Ramps[k];
            var mid = line[line.Count / 2];
            var side = (line[line.Count / 2 + 1] - line[line.Count / 2 - 1]).Unit.Perp;
            double off = Layout.RampHalf + 70;
            var deeper = Sample(res.Depth, n, mid + side * off) > Sample(res.Depth, n, mid - side * off) ? side : side * -1;
            res.PitSpots.Add((mid + deeper * off, Layout.RampSpotIncome, k % 2 == 0 ? "A" : "B"));
        }
        foreach (var (u, v) in new[] { (-900.0, -330.0), (-900.0, 330.0), (300.0, -360.0), (300.0, 360.0) })
            res.PitSpots.Add((FromFrame(u, v), Layout.FloorSpotIncome, "pit"));
        foreach (var at in Layout.SouthFloorSpots) res.PitSpots.Add((at, Layout.FloorSpotIncome, "pit"));
        res.Vent = FromFrame(-300, 0);
        // The lobe's two bucket wheel excavators: each at its wall's foot (found along the line at its z), a little into
        // the wall, facing up the wall (against the depth's gradient).
        for (int k = 0; k < Layout.PitWheelZ.Length; k++)
        {
            int side = k == 0 ? -1 : 1;
            var p = new P(Layout.SouthCentre.X, Layout.PitWheelZ[k]);
            while (Sample(res.Depth, n, p) < Layout.Wall && p.X > Layout.SouthCentre.X - 1500) p -= new P(8, 0);
            while (Sample(res.Depth, n, p) < Layout.Wall && p.X < Layout.SouthCentre.X + 1500) p += new P(16, 0);
            while (Sample(res.Depth, n, p + new P(8 * side, 0)) >= Layout.Wall - 1) p += new P(8 * side, 0);
            var g = new P(Sample(res.Depth, n, p + new P(48, 0)) - Sample(res.Depth, n, p - new P(48, 0)), Sample(res.Depth, n, p + new P(0, 48)) - Sample(res.Depth, n, p - new P(0, 48)));
            var dir = g.Length > 1e-6 ? (g * -1).Unit : new P(side, 0);
            res.PitWheels.Add((p + dir * Layout.PitWheelInto, dir));
        }
        // The railway's south end: the shed stops ShedToRim short of the rim on the main line, the track 30 inside it.
        double rim = 0;
        while (rim < Layout.Size && Sample(res.Depth, n, new P(Layout.RailX, rim)) <= 0) rim += 4;
        res.RimZ = rim;
        res.ShedSouth = rim - Layout.ShedToRim;
        res.ShedNorth = res.ShedSouth - Layout.ShedLength;
        res.TrackEnd = res.ShedSouth - 30;
        var road7 = RoadLines[6];
        for (int i = 0; i + 1 < road7.Count; i++)
            if ((road7[i].X - Layout.RailX) * (road7[i + 1].X - Layout.RailX) <= 0)
                res.CrossingZ = road7[i].Z + (road7[i + 1].Z - road7[i].Z) * (Layout.RailX - road7[i].X) / (road7[i + 1].X - road7[i].X);
        res.Rails =
        [
            [new P(Layout.RailX, -40), new P(Layout.RailX, res.TrackEnd)],
            [new P(Layout.RailX, Layout.SidingFrom - Layout.Turnout), new P(Layout.SidingX, Layout.SidingFrom), new P(Layout.SidingX, Layout.SidingTo), new P(Layout.RailX, Layout.SidingTo + Layout.Turnout)],
        ];

        // The natural ground's relief and washes, where the ground is wild.
        res.Rise = new float[n * n]; res.Mound = new float[n * n]; res.Cut = new float[n * n]; res.Wash = new float[n * n];
        Parallel.For(0, n, z =>
        {
            for (int x = 0; x < n; x++)
            {
                int i = z * n + x;
                var p = new P(x * 8.0, z * 8.0);
                double wild = Natural.Wild(res, p, res.FromRim[i], false), wildPaint = Natural.Wild(res, p, res.FromRim[i], true);
                if (wild <= 0 && wildPaint <= 0) continue;
                var (main, any) = Natural.Wash(p);
                double creek = ToLines(p, CreekLines);
                double mound = Natural.Mound(p) * Geo.S((creek - 220) / 120) * (1 - main);          // no mounds in a creek or a wash
                res.Rise[i] = (float)(wild * (Natural.Swells(p) + mound));
                res.Mound[i] = (float)(wild * mound);
                res.Cut[i] = (float)(wild * main * Natural.WashDepth);
                res.Wash[i] = (float)(wildPaint * any);
            }
        });
        Parallel.For(0, n, z =>
        {
            for (int x = 0; x < n; x++)
                res.H[z * n + x] = (float)Height(res, new P(x * 8.0, z * 8.0), res.Depth[z * n + x]);
        });
        return res;
    }

    /// <summary>The point at a given depth along a ray out from inside the pit (outside the rim if negative).</summary>
    static P AtDepth(Result res, int n, P origin, P dir, double depth)
    {
        // Find the rim along the ray, then step in (or out) until the distance field reads the depth wanted.
        double r = 0;
        while (r < 6000 && Rho(origin + dir * r) < 1) r += 8;
        var rim = origin + dir * r;
        if (depth <= 0) return rim - dir * depth;
        for (double s = 0; s < 4000; s += 4)
        {
            var p = rim - dir * s;
            if (Sample(res.Depth, n, p) >= depth) return p;
        }
        return origin;
    }

    static List<P> Thin(List<P> line, double step)
    {
        var o = new List<P> { line[0] };
        foreach (var p in line) if ((p - o[^1]).Length >= step) o.Add(p);
        if (o[^1] != line[^1]) o.Add(line[^1]);
        return o;
    }

    public static double Sample(float[] f, int n, P p)
    {
        double fx = Math.Clamp(p.X / 8, 0, n - 1.001), fz = Math.Clamp(p.Z / 8, 0, n - 1.001);
        int x = (int)fx, z = (int)fz;
        double tx = fx - x, tz = fz - z;
        return (f[z * n + x] * (1 - tx) + f[z * n + x + 1] * tx) * (1 - tz) + (f[(z + 1) * n + x] * (1 - tx) + f[(z + 1) * n + x + 1] * tx) * tz;
    }

    public static double ToLines(P p, List<P>[] lines) => lines.Min(l => Geo.ToPolyline(p, l, out _));

    static double Ellipse(P p, P c, double a, double b, double turnDegrees)
    {
        double t = turnDegrees * Math.PI / 180, ct = Math.Cos(t), st = Math.Sin(t);
        var q = p - c;
        double u = q.X * ct + q.Z * st, v = -q.X * st + q.Z * ct;
        return Math.Sqrt(u * u / (a * a) + v * v / (b * b));
    }

    /// <summary>How far out from a road's centre line its paint reaches (the windrows' outer edge).</summary>
    public const double RoadEdge = 86;

    /// <summary>Where a road's paint shows (0 to 1): a road runs onto a pad or hardstand and ends at its edge, and
    /// it never runs over the ROM pad's stockpiles or the magazine's bund.</summary>
    public static double RoadShown(P p)
    {
        double m = 1;
        foreach (var (at, a, b) in Pads) m = Math.Min(m, Geo.S((PadRho(p, at, a, b) - 0.9) / 0.12));
        foreach (var (at, a, b) in Layout.Hardstands) m = Math.Min(m, Geo.S((PadRho(p, at, a + 20, b + 20) - 0.95) / 0.1));
        foreach (var s in Layout.Stockpiles) m = Math.Min(m, Geo.S(((s - p).Length - 210) / 40));
        return Math.Min(m, Geo.S(((p - Layout.Magazine).Length - 190) / 30));
    }

    /// <summary>The pads: the ROM pad, the plant pad, the workshop's hardstand.</summary>
    public static readonly (P At, double A, double B)[] Pads =
        [(Layout.RomPad, Layout.RomA, Layout.RomB), (Layout.PlantPad, Layout.PlantA, Layout.PlantB), (Layout.Workshop, Layout.WorkshopA, Layout.WorkshopB)];

    /// <summary>A pad's shape, a rounded rectangle (1 at its edge).</summary>
    public static double PadRho(P p, P c, double a, double b)
    {
        double u = Math.Abs(p.X - c.X) / a, v = Math.Abs(p.Z - c.Z) / b;
        return Math.Pow(u * u * u * u + v * v * v * v, 0.25);
    }

    /// <summary>A dump's outline (1 at the toe of its first lift; the second lift's with the inset), scalloped where
    /// trucks tipped over the edge.</summary>
    public static double DumpRho(P p, int k, double inset = 0)
    {
        var (at, a, b, turn) = Layout.Dumps[k];
        return Ellipse(p, at, a - inset, b - inset, turn) - 0.06 * Edge.Fbm(p.X / 420, p.Z / 420, 2) - 0.022 * Edge.At(p.X / 70 + 31, p.Z / 70 + 17);
    }

    /// <summary>A breakaway's outline, eaten into by gullies.</summary>
    public static double BreakawayRho(P p, int k)
    {
        var (at, a, b, _) = Layout.Breakaways[k];
        return Ellipse(p, at, a, b, 20) - 0.22 * Edge.Fbm(p.X / 330 + 50, p.Z / 330, 2) - 0.06 * Edge.At(p.X / 90 + 70, p.Z / 90 + 9);
    }

    /// <summary>Paddock dumping on half of each dump's top lift: rows of truck-load heaps (0..1 high), a few loads
    /// missing; the tone picks each heap's colour. The other half is dozed flat.</summary>
    public static double Heap(P p, out double tone)
    {
        tone = 0;
        for (int k = 0; k < Layout.Dumps.Length; k++)
        {
            var (at, a, _, turn) = Layout.Dumps[k];
            if (DumpRho(p, k, Layout.LiftInset) > 0.82) continue;
            if (Geo.ToPolyline(p, DumpRampLines[k], out _) < 190) continue;                 // the ramp's way onto the top, kept clear
            double tr = turn * Math.PI / 180, ct = Math.Cos(tr), st = Math.Sin(tr);
            var q = p - at;
            double u = q.X * ct + q.Z * st, v = -q.X * st + q.Z * ct;
            if ((k == 0 ? u : -u) > a * 0.08) continue;                            // the dozed half
            const double Du = 100, Dv = 112, R = 44;
            int iu = (int)Math.Floor(u / Du), iv = (int)Math.Floor(v / Dv);
            double cu = (iu + 0.5) * Du + 4 * Hash(iu, iv, 11), cv = (iv + 0.5) * Dv + 4 * Hash(iu, iv, 12);
            if (Hash(iu, iv, 13) < -0.7) continue;                                  // a load never tipped
            double d = Math.Sqrt((u - cu) * (u - cu) + (v - cv) * (v - cv));
            if (d >= R) continue;
            tone = Hash(iu, iv, 14) * 0.5 + 0.5;
            return 1 - Geo.S(d / R);
        }
        return 0;
    }

    static double Hash(int x, int z, int seed)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(seed * 83492791);
        h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
        return (h & 0xFFFF) / 32767.5 - 1;
    }

    static double Height(Result res, P p, double depth)
    {
        // 1. The plain, level in the bases and on the pads, flat across the roads; on wild ground its swells, mounds and washes.
        double relief = 12 * R1.Fbm(p.X / 800, p.Z / 800, 3) + 4 * R2.Fbm(p.X / 160, p.Z / 160, 2);
        double flat = 0;
        foreach (var s in Layout.StartsA.Concat(Layout.StartsB)) flat = Math.Max(flat, 1 - Geo.S(((s - p).Length - Layout.BaseRadius) / 300));
        flat = Math.Max(flat, 1 - Geo.S((PadRho(p, Layout.RomPad, Layout.RomA, Layout.RomB) - 1) * 4));
        flat = Math.Max(flat, 1 - Geo.S((PadRho(p, Layout.PlantPad, Layout.PlantA, Layout.PlantB) - 1) * 4));
        flat = Math.Max(flat, 1 - Geo.S((PadRho(p, Layout.Workshop, Layout.WorkshopA, Layout.WorkshopB) - 1) * 4));
        foreach (var (at, a, b) in Layout.Hardstands) flat = Math.Max(flat, 1 - Geo.S((PadRho(p, at, a, b) - 1) * 4));
        double road = ToLines(p, RoadLines);
        flat = Math.Max(flat, 1 - Geo.S((road - 70) / 170));
        int vi = (int)Math.Round(p.Z / 8) * res.N + (int)Math.Round(p.X / 8);
        double h = Layout.Plain + relief * (1 - flat) + res.Rise[vi] - res.Cut[vi];
        // The rail bed: dead level from the north edge to the shed's apron, so the train stands true.
        double bed = Math.Abs(p.X - Layout.RailBedX) - Layout.RailBedHalf, bedEnd = p.Z - (res.ShedSouth + 60);
        if (bed < 60 && bedEnd < 60) h += (Layout.Plain + 3 - h) * (1 - Geo.S(bed / 60)) * (1 - Geo.S(bedEnd / 60));
        // The magazine's bund: a ring of earth with a gap for the track in, on its north-east.
        double md = (p - Layout.Magazine).Length;
        if (md < 180)
        {
            double ma = Math.Atan2(p.Z - Layout.Magazine.Z, p.X - Layout.Magazine.X) * 180 / Math.PI;
            double gap = Geo.S((Math.Abs(ma + 45) - 14) / 10);
            h = Math.Max(h, Layout.Plain + 16 * (1 - Geo.S(Math.Abs(md - 130) / 34)) * gap);
        }

        // 2. Creeks, breakaways, the tailings dam, the dumps, the stockpiles.
        double creek = ToLines(p, CreekLines);
        if (creek < 300) h = Math.Min(h, Layout.Plain - 14 + (h - Layout.Plain + 14) * Geo.S((creek - 60) / 240));
        for (int k = 0; k < Layout.Breakaways.Length; k++)
        {
            var (_, a, b, height) = Layout.Breakaways[k];
            double rho = BreakawayRho(p, k);
            if (rho > 1.3) continue;
            double scarp = height / Math.Tan(42 * Math.PI / 180);
            double up = Geo.S((1 - rho) * Math.Min(a, b) / scarp + 0.15);
            h += up * (height + 8 * Cap.Fbm(p.X / 120, p.Z / 120, 2));
        }
        {
            double ex = Math.Max(Layout.TsfX0 - p.X, p.X - Layout.TsfX1), ez = Math.Max(Layout.TsfZ0 - p.Z, p.Z - Layout.TsfZ1);
            double e = Math.Max(ex, ez);
            if (e < 0) e = Math.Max(ex, ez);                                   // inside: distance to the nearest side, negative
            double outer = Layout.TsfWall / Math.Tan(22 * Math.PI / 180);
            double rise = e > 0 ? Layout.TsfWall * (1 - Geo.S(e / outer))
                        : e > -Layout.TsfCrest ? Layout.TsfWall
                        : Layout.TsfWall - 10 * Geo.S((-e - Layout.TsfCrest) / 60)
                          - Layout.DecantDip * (1 - Geo.S((Ellipse(p, Layout.Decant, Layout.DecantA, Layout.DecantB, 0) - 0.7) / 0.6));
            if (e < outer) h = Math.Max(h, Layout.Plain + rise);
        }
        for (int k = 0; k < Layout.Dumps.Length; k++)
        {
            var (_, a, b, _) = Layout.Dumps[k];
            double rho = DumpRho(p, k);
            if (rho > 1.3) continue;
            double repose = Layout.Lift / Math.Tan(37 * Math.PI / 180);
            double l1 = Geo.S((1 - rho) * Math.Min(a, b) / repose + 0.2);
            double rho2 = DumpRho(p, k, Layout.LiftInset);
            double l2 = Geo.S((1 - rho2) * Math.Min(a - Layout.LiftInset, b - Layout.LiftInset) / repose + 0.2);
            double heap = l2 > 0.99 ? 14 * Heap(p, out _) : 0;
            h = Math.Max(h, Layout.Plain + Layout.Lift * (l1 + l2) + 3 * R2.Fbm(p.X / 90, p.Z / 90, 2) * l1 + heap);
        }
        for (int i = 0; i < DumpRampLines.Length; i++)
        {
            var line = DumpRampLines[i];
            double dd = Geo.ToPolyline(p, line, out double along);
            if (dd > 140) continue;
            double len = Enumerable.Range(0, line.Count - 1).Sum(k => (line[k + 1] - line[k]).Length);
            double grade = Layout.Plain + 2 * Layout.Lift * Math.Clamp(along / len, 0, 1);
            h = grade + (h - grade) * Geo.S((dd - 70) / 70);
        }
        foreach (var s in Layout.Stockpiles)
        {
            double d = (s - p).Length;
            if (d < 190) h = Math.Max(h, Layout.Plain + 46 * (1 - Geo.S(d / 190)) + 4 * R2.At(p.X / 30, p.Z / 30));
        }

        // 3. The pit.
        if (depth > 0)
        {
            double ang = Angle(p);
            double rough = 1.5 * Bench.At(p.X / 60, p.Z / 60);
            double benched = Benched(depth, rough);
            // The terrace at the north end: one bench down, wide, for the bucket wheel excavator.
            double terrace = InSector(ang, Layout.TerraceFrom, Layout.TerraceTo, 5);
            if (terrace > 0)
            {
                double pitch = Layout.Berm + Layout.Face;
                double dEff = depth < Layout.Face ? depth : depth < Layout.TerraceDepth ? Layout.Face + 1 : depth - (Layout.TerraceDepth - pitch);
                benched = benched + (Benched(dEff, rough) - benched) * terrace;
            }
            // The slide: one slope of fallen ground, steeper than vehicles climb, gentler than bots' limit.
            double slide = InSector(ang, Layout.SlideFrom, Layout.SlideTo, 3);
            if (slide > 0)
            {
                double slope = Math.Max(Layout.PitFloor, Layout.Plain - depth * Math.Tan(Layout.SlideDegrees * Math.PI / 180));
                benched = benched + (slope - benched) * slide;
            }
            if (depth > Layout.Wall - 1) benched = Layout.PitFloor + 3 * Floor.Fbm(p.X / 200, p.Z / 200, 2);
            // The pool in the pit's lowest corner, against the map's south edge.
            double pool = Ellipse(p, Layout.PoolCentre, Layout.PoolA, Layout.PoolB, 0);
            if (pool < 1.4) benched = Math.Min(benched, Layout.PoolBed + (benched - Layout.PoolBed) * Geo.S((pool - 0.55) / 0.6));
            h = Math.Min(h, benched);
        }

        // 3b. The bucket wheel excavators' cuts: each wheel, slewing along its arc, has cut the wall back to the floor in
        // a round bite as far as it reaches (its buckets just into the face), with a steep curved face beyond; behind and
        // round its crawlers the floor too.
        foreach (var (c, d) in res.PitWheels)
        {
            var q = p - c;
            double r = q.Length;
            double reach = Models.PitWheelScale * (Models.WheelAhead + Models.WheelRadius) - 15;
            if (r > reach + 400) continue;
            double off = r < 1e-6 ? 0 : Math.Acos(Math.Clamp(q.Dot(d) / r, -1, 1)) * 180 / Math.PI;
            double bite = Math.Max(220, reach * Geo.S((Layout.PitWheelSlew + 17 - off) / 25));
            h = Math.Min(h, Layout.PitFloor + 2 + Math.Max(0, r - bite) * Math.Tan(70 * Math.PI / 180));
        }

        // 4. The haul ramps: one grade along each line, cut and filled.
        for (int k = 0; k < res.Ramps.Length; k++)
        {
            var line = res.Ramps[k];
            double dd = Geo.ToPolyline(p, line, out double along);
            if (dd > Layout.RampHalf + 20) continue;
            double grade = GradeAt(line, res.RampHeights[k], along);
            h = grade + (h - grade) * Geo.S((dd - Layout.RampHalf) / 20);
        }

        // 5. Level pads.
        foreach (var (at, _, _) in Layout.Spots) h = Pad(h, p, at, 60);
        foreach (var (at, _, _) in res.PitSpots) h = Pad(h, p, at, 60);
        h = Pad(h, p, res.Vent, 80);
        return h;
    }

    static double GradeAt(List<P> line, double[] heights, double along)
    {
        double run = 0;
        for (int i = 0; i + 1 < line.Count; i++)
        {
            double len = (line[i + 1] - line[i]).Length;
            if (along <= run + len || i == line.Count - 2) return heights[i] + (heights[i + 1] - heights[i]) * Math.Clamp((along - run) / Math.Max(1e-6, len), 0, 1);
            run += len;
        }
        return heights[^1];
    }

    /// <summary>A pad: level at the height its centre would have, blended out over 120.</summary>
    static double Pad(double h, P p, P at, double radius)
    {
        double d = (p - at).Length;
        if (d > radius + 120) return h;
        lock (pads) { if (!pads.TryGetValue(at, out _)) pads[at] = double.NaN; }
        double level = pads[at];
        if (double.IsNaN(level)) return h;                                   // the pad's own height isn't known yet
        return level + (h - level) * Geo.S((d - radius) / 120);
    }

    static readonly Dictionary<P, double> pads = [];

    /// <summary>Sets each pad's level from the heights built without pads, and builds again with them.</summary>
    public static Result BuildWithPads()
    {
        lock (pads) pads.Clear();
        var first = Build();
        var all = Layout.Spots.Select(s => s.At).Concat(first.PitSpots.Select(s => s.At)).Append(first.Vent);
        lock (pads) foreach (var at in all) pads[at] = Sample(first.H, first.N, at);
        return Build();
    }

    /// <summary>Distance in cells from each cell to the nearest marked one (Felzenszwalb and Huttenlocher's exact transform).</summary>
    public static float[] Distance(bool[] marked, int n)
    {
        const float Inf = 1e20f;
        var f = new float[n * n];
        for (int i = 0; i < f.Length; i++) f[i] = marked[i] ? 0 : Inf;
        Parallel.For(0, n, x =>
        {
            var col = new float[n]; var outp = new float[n];
            for (int z = 0; z < n; z++) col[z] = f[z * n + x];
            Edt1(col, outp, n);
            for (int z = 0; z < n; z++) f[z * n + x] = outp[z];
        });
        Parallel.For(0, n, z =>
        {
            var row = new float[n]; var outp = new float[n];
            for (int x = 0; x < n; x++) row[x] = f[z * n + x];
            Edt1(row, outp, n);
            for (int x = 0; x < n; x++) f[z * n + x] = MathF.Sqrt(outp[x]);
        });
        return f;
    }

    static void Edt1(float[] f, float[] d, int n)
    {
        var v = new int[n];
        var zz = new float[n + 1];
        int k = 0;
        v[0] = 0; zz[0] = float.NegativeInfinity; zz[1] = float.PositiveInfinity;
        for (int q = 1; q < n; q++)
        {
            float s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2 * q - 2 * v[k]);
            while (s <= zz[k])
            {
                k--;
                s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2 * q - 2 * v[k]);
            }
            k++;
            v[k] = q; zz[k] = s; zz[k + 1] = float.PositiveInfinity;
        }
        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (zz[k + 1] < q) k++;
            d[q] = (q - v[k]) * (q - v[k]) + f[v[k]];
        }
    }
}
