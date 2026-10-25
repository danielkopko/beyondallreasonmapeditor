// Marram Line's heights, per heightmap vertex (8 units apart), west to east:
//   the sea (60 deep offshore, shelving to the beach, broken sandbars 400 out, sand ripples); the beach (0 to 10, a
//   runnel and ridge low down, shingle under the headland); the headland's cliffs (66 high, over 60 degrees) and its
//   heath at 70; the foredune (its seaward face about 40 degrees, cut by three draws to the slacks); the dune belt (the
//   table's 86 parabolic dunes, each a gentle windward back and a slip face at 40 degrees on its landward side, damp flat
//   slacks between); the beach team's trenches dug into the dunes' last ridge at 42 (a crenellated fire trench, the
//   support trench, the communication trenches back); the glacis down to the dead man's zone (a meadow at 16, churned,
//   its 190 shell holes and bomb craters, flooded where their floors are below the water); the glacis up to the hill
//   team's trenches, the same; the rise to the crest at 215 (hedgerows on low banks, the knoll in the salient), the back
//   slope. In the south the inlet is cut through everything to the cove, and the sandy end lies south of it.
// Level pads at the starts, spots, vents, the village and the farm's yard; the headland road ramped up from the beach.
static class Terrain
{
    public const int Cut = 0; // (no meaning; kept so the class has a constant section)

    public enum Ground : byte { Sea, Beach, Cliff, Heath, Foredune, Dunes, LineA, Meadow, LineB, Rise, SandyEnd }

    public sealed class Result
    {
        public int N;
        public float[] H = [];
        /// <summary>Signed distance to the inlet's water edge (negative in the water), capped at ±600.</summary>
        public float[] Inlet = [];
        /// <summary>How far a trench (fire, support or communication) is cut below the ground here.</summary>
        public float[] Trench = [];
        /// <summary>A dune's height over the slacks, and 1 on its slip face.</summary>
        public float[] Dune = [], Slip = [];
        /// <summary>A crater: how far into it (1 at its middle, 0 at its rim, negative out on its ejecta).</summary>
        public float[] Crater = [];
        /// <summary>Distances: to the nearest hedgerow, track (beach side), lane (hill side) and old road (the zone).</summary>
        public float[] Hedge = [], Track = [], Lane = [], OldRoad = [];
        /// <summary>The field each vertex is in on the hill (-1 none), for its crop.</summary>
        public short[] Field = [];
        public Ground[] Kind = [];
    }

    static readonly Noise Ripple = new(5201), Hum = new(5202), Rough = new(5203), Churn = new(5204), Heather = new(5205), Rock = new(5206);

    public static readonly double SlipTan = Math.Tan(40 * Math.PI / 180), FaceTan = Math.Tan(40 * Math.PI / 180), CliffRun = 24;

    /// <summary>The seaward depth at a distance out from the waterline.</summary>
    static double SeaDepth(double s)
    {
        var f = Layout.SeaFloor;
        for (int i = 0; i + 1 < f.Length; i++)
            if (s <= f[i + 1].D)
                return Layout.Lerp(f[i].H, f[i + 1].H, (s - f[i].D) / (f[i + 1].D - f[i].D));
        return f[^1].H;
    }

    public static bool BarOn(double z) => Layout.Wave(z, 31, 1, 1400) > -0.35 && Layout.Headland(z) < 0.5 && Math.Abs(z - 10950) > 650;

    static double Section((double D, double H)[] sec, double d)
    {
        if (d < sec[0].D || d > sec[^1].D) return 0;
        for (int i = 0; i + 1 < sec.Length; i++)
            if (d <= sec[i + 1].D)
                return Layout.Lerp(sec[i].H, sec[i + 1].H, (d - sec[i].D) / (sec[i + 1].D - sec[i].D));
        return 0;
    }

    /// <summary>A fire trench's crenellation: its bays alternately 14 forward and back of the line, 88 long.</summary>
    public static double Crenel(double z, double z0) => 14 * Math.Clamp(Math.Sin(Math.PI * (z - z0) / 88) * 5, -1, 1);

    /// <summary>Where a line's trenches are dug at this z: the run's start and how full the cut is (tapered at its ends), or null.</summary>
    public static (double Z0, double Full)? Run(string side, double z)
    {
        // Tapered where it stops for a crossing; at the line's south end it stops sheer (a taper there would be a ramp).
        double end = side == "beach" ? Layout.EndA : Layout.EndB;
        foreach (var (z0, z1) in Layout.FireRuns[side])
            if (z >= z0 && z <= z1)
                return (z0, Geo.S((z - z0) / 24) * (Math.Abs(z1 - end) < 1 ? 1 : Geo.S((z1 - z) / 24)));
        return null;
    }

    /// <summary>The trench cuts at a point on line A or B: (the change in height, the trench's depth below ground for the
    /// painter). The sections are measured square to the line, so where a line runs at a slant its walls keep their angle
    /// and its floor its width.</summary>
    static (double Dh, double Depth) Trenches(double x, double z, double fa, double fb)
    {
        double best = 0, top = 0;
        void Take(double v) { if (v < best) best = v; else if (best >= 0 && v > top) top = v; }
        if (Run("beach", z) is { } ra)
        {
            double k = Square(Layout.FireA, z);
            Take(Section(Layout.FireSection, (x - (fa + Crenel(z, ra.Z0))) * k) * ra.Full);
            Take(Section(Layout.SupportSection, (x - (Layout.FireA(z) - Layout.SupportBack)) * k) * ra.Full);
        }
        if (Run("hill", z) is { } rb)
        {
            double k = Square(Layout.FireB, z);
            Take(Section(Layout.FireSection, (fb + Crenel(z, rb.Z0) - x) * k) * rb.Full);
            Take(Section(Layout.SupportSection, (fb + Layout.SupportBack - x) * k) * rb.Full);
        }
        return best < 0 ? (best, -best) : (top, 0);
    }

    /// <summary>How much less a distance across x is than square to a line x = fn(z): the cosine of its slant.</summary>
    static double Square(Func<double, double> fn, double z)
    {
        double s = (fn(z + 6) - fn(z - 6)) / 12;
        return 1 / Math.Sqrt(1 + s * s);
    }

    /// <summary>A parabolic dune's height over the ground round it, and whether this is its slip face: a rounded dome
    /// whose crest sits east of its middle, a long gentle back to windward (west, under 25 degrees), and its lee cut off
    /// at 40 degrees, the slip face.</summary>
    public static double DuneHeight(double x, double z, int k, out bool slip)
    {
        slip = false;
        var (c, r, h) = Layout.Dunes[k];
        double rx = 1.6 * r, u = (x - c.X) / rx, v = (z - c.Z) / r;
        // The crest bows downwind: furthest east in the middle, its horns trailing back to windward, wavering.
        double crestU = 0.32 - 0.42 * v * v + 0.05 * Hum.At(z / 70 + k, 7);
        double ua = u < crestU ? (u - crestU) / (1 + crestU) : (u - crestU) / Math.Max(0.2, 1 - crestU);
        double rho = Math.Sqrt(ua * ua + v * v) / Layout.Wobble(Math.Atan2(v, u), 300 + k, 0.18);
        if (rho >= 1) return 0;
        double dome = h * Math.Pow(1 - rho * rho, 0.8) * (1 + 0.06 * Hum.At(x / 40, z / 40));
        double lee = h * (1 - 0.35 * v * v) - Math.Max(0, u - crestU) * rx * SlipTan;
        double y = Math.Max(0, Math.Min(dome, lee));
        slip = u > crestU && lee < dome && y > 2;
        return y;
    }

    /// <summary>A crater's change to the ground: its bowl (deep ones steeper) and a low rim of thrown-out earth.</summary>
    public static double CraterDh(double x, double z, int k, out double into)
    {
        var (c, r, depth, _, _) = Layout.Craters[k];
        double dx = x - c.X, dz = z - c.Z, d = Math.Sqrt(dx * dx + dz * dz);
        double rho = d / (r * Layout.Wobble(Math.Atan2(dz, dx), 600 + k, 0.2));
        into = 1 - rho;
        if (rho > 1.5) return 0;
        // A shell hole is a shallow bowl; a bomb crater has walls about 42 degrees all round its flat floor, however wide.
        double bowl = !Layout.Craters[k].Deep ? (rho < 1 ? -depth * (1 - rho * rho) : 0)
            : -depth * Geo.S((1 - rho) / Math.Min(0.9, 1.67 * depth / r));
        return bowl + 0.12 * depth * Layout.Bump((rho - 1.08) / 0.35);
    }

    public static Result Build()
    {
        int n = Layout.Squares + 1;
        var t = new Result
        {
            N = n, H = new float[n * n], Inlet = new float[n * n], Trench = new float[n * n], Dune = new float[n * n], Slip = new float[n * n],
            Crater = new float[n * n], Hedge = Fill(n * n, 9999), Track = Fill(n * n, 9999), Lane = Fill(n * n, 9999), OldRoad = Fill(n * n, 9999),
            Field = new short[n * n], Kind = new Ground[n * n],
        };
        Array.Fill(t.Crater, -9f);
        Array.Fill(t.Field, (short)-1);

        // Fields rasterised: the dunes (both the belt's and the sandy end's) and the craters, each over its own box.
        var duneH = new float[n * n];
        var duneSlip = new bool[n * n];
        for (int k = 0; k < Layout.Dunes.Count; k++)
        {
            var (c, r, _) = Layout.Dunes[k];
            int kk = k;
            Box(n, c.X - 1.9 * r, c.Z - 1.25 * r, c.X + 1.9 * r, c.Z + 1.25 * r, (i, x, z) =>
            {
                double y = DuneHeight(x, z, kk, out bool s);
                if (y > duneH[i]) { duneH[i] = (float)y; duneSlip[i] = s; }
            });
        }
        var craterDh = new float[n * n];
        for (int k = 0; k < Layout.Craters.Count; k++)
        {
            var (c, r, _, _, _) = Layout.Craters[k];
            int kk = k;
            Box(n, c.X - 1.8 * r, c.Z - 1.8 * r, c.X + 1.8 * r, c.Z + 1.8 * r, (i, x, z) =>
            {
                double dh = CraterDh(x, z, kk, out double into);
                if (dh < 0) craterDh[i] = (float)Math.Min(craterDh[i], dh);
                else if (craterDh[i] >= 0) craterDh[i] = (float)Math.Max(craterDh[i], dh);
                t.Crater[i] = (float)Math.Max(t.Crater[i], into);
            });
        }
        // Lines rasterised by distance: hedgerows, the three kinds of road, the communication trenches, the brook's bed.
        foreach (var hedge in Layout.Hedges) Distance(n, hedge, 80, t.Hedge);
        var tracks = Layout.Roads.Where(r => r.Kind == "track").Select(r => r.Line).ToList();
        foreach (var (kind, line) in Layout.Roads)
            Distance(n, line, 140, kind == "track" ? t.Track : kind == "lane" ? t.Lane : t.OldRoad);
        var comm = Fill(n * n, 9999);
        foreach (var (_, line) in Layout.CommTrenches) Distance(n, line, 80, comm);
        var brook = Fill(n * n, 9999);
        Distance(n, Geo.Smooth(Layout.BrookDry, 30), 60, brook);
        // The fields' crops: each field polygon filled with its index.
        for (int f = 0; f < Layout.Fields.Count; f++)
            FillPolygon(n, Layout.Fields[f], (short)f, t.Field);
        // The headland road: its line and the distance along it from the beach.
        var hroad = Geo.Smooth(Layout.Roads[3].Line, 20);

        // Pads: (centre, radius levelled, blend beyond it, how fully levelled). The starts' ground is eased, not cut
        // flat: the slacks are flat already and the rise's 3 degrees takes any building.
        var pads = new List<(P At, double R, double Blend, double Strength)>();
        pads.AddRange(Layout.Starts.Select(s => (s.At, 300.0, 500.0, 0.6)));
        pads.AddRange(Layout.Spots.Select(s => (s.At, 48.0, 40.0, 1.0)));
        pads.AddRange(Layout.Vents.Select(v => (v.At, 64.0, 48.0, 1.0)));
        pads.Add((Layout.Village, 380, 200, 0.85));
        pads.Add((Layout.Farm, 200, 120, 1.0));
        foreach (var (kind, at, _) in Layout.Bunkers) pads.Add((at, kind == "casemate" ? 130 : kind == "draw" ? 64 : 56, kind == "casemate" ? 60 : kind == "draw" ? 30 : 24, 1.0));
        pads.Add((Layout.Lighthouse, 60, 40, 1.0));
        foreach (var shed in Layout.BoatSheds) pads.Add((shed, 46, 40, 1.0));

        float Base(int ix, int iz, out Ground kind, out double trench)
        {
            double x = ix * 8.0, z = iz * 8.0;
            double h = BaseAt(x, z, duneH[iz * n + ix], craterDh[iz * n + ix], t.Hedge[iz * n + ix], comm[iz * n + ix], brook[iz * n + ix], out kind, out trench);
            // The headland road, ramped from the beach up onto the heath: 5 degrees, 120 wide, its cuttings steep.
            double dr = Geo.ToPolyline(new P(x, z), hroad, out double along);
            if (dr < 170 && along < 900)
            {
                double target = Layout.Berm + (Layout.HeadlandTop - Layout.Berm) * Math.Clamp(along / 760, 0, 1);
                h = Layout.Lerp(target, h, Geo.S((dr - 60) / 110));
            }
            return (float)h;
        }

        // The ground with the inlet cut through it, before the pads: the pads go on last, so a pad on the inlet's bank stays level.
        double Cut(double x, double z, double h, out double inletD)
        {
            inletD = 600;
            if (Layout.InletAt(x, z) is not { } a) return h;
            double cut = a.D < a.W
                ? -Layout.InletDepthAt(x, a.S) * Geo.S((a.W - a.D) / (Layout.InletShallows * a.W)) + 1.2 * Ripple.At(x / 37, z / 23) * Geo.S((a.W - a.D) / 60)
                : (a.D - a.W) * Math.Tan(Layout.InletBankDeg * Math.PI / 180);
            inletD = Math.Clamp(a.D - a.W, -600, 600);
            return Math.Min(h, cut);
        }
        var padH = pads.Select(pd =>
        {
            int px = Math.Clamp((int)Math.Round(pd.At.X / 8), 0, n - 1), pz = Math.Clamp((int)Math.Round(pd.At.Z / 8), 0, n - 1);
            return Cut(px * 8.0, pz * 8.0, Base(px, pz, out _, out _), out _);
        }).ToArray();
        Parallel.For(0, n, iz =>
        {
            for (int ix = 0; ix < n; ix++)
            {
                int i = iz * n + ix;
                double x = ix * 8.0, z = iz * 8.0;
                double h = Cut(x, z, Base(ix, iz, out var kind, out double trench), out double inletD);
                t.Inlet[i] = (float)inletD;
                // Level pads: the starts, spots, vents, the village, the farm's yard, the bunkers' and sheds' footings.
                for (int k = 0; k < pads.Count; k++)
                {
                    var (at, r, blend, strength) = pads[k];
                    double d = (new P(x, z) - at).Length;
                    if (d > r + blend) continue;
                    h = Layout.Lerp(h, padH[k], strength * (1 - Geo.S((d - r) / blend)));
                }
                t.H[i] = (float)Math.Clamp(h, Layout.MinH + 1, Layout.MaxH - 1);
                t.Kind[i] = kind;
                t.Trench[i] = (float)trench;
                t.Dune[i] = duneH[i];
                t.Slip[i] = duneSlip[i] ? 1 : 0;
            }
        });
        return t;
    }

    /// <summary>The ground at a point before the pads, the road and the inlet.</summary>
    static double BaseAt(double x, double z, double dune, double craterDh, double hedge, double comm, double brook, out Ground kind, out double trench)
    {
        trench = 0;
        double c = Layout.Coast(z);
        if (x < c)
        {
            kind = Ground.Sea;
            double s = c - x, d = SeaDepth(s);
            if (Math.Abs(s - Layout.BarAt) < Layout.BarHalf && BarOn(z))
                d = Math.Min(d, Layout.Lerp(d, Layout.BarDepth, Layout.Bump((s - Layout.BarAt) / Layout.BarHalf)));
            double ripple = 0.7 * Math.Sin((s + 6 * Ripple.At(x / 200, z / 200)) / 6.5) * Geo.S((s - 30) / 80) * (1 - Geo.S((s - 900) / 300));
            return -d + ripple + 1.5 * Hum.Fbm(x / 400, z / 400, 3);
        }
        double hd = Layout.Headland(z), b = Layout.BermX(z);
        if (x < b)
        {
            kind = Ground.Beach;
            double u = (x - c) / (b - c);
            double beach = Layout.Berm * u;
            beach += -1.2 * Layout.Bump((u - 0.22) / 0.08) + 0.8 * Layout.Bump((u - 0.32) / 0.07);   // a runnel and a ridge low on the beach
            return Layout.Lerp(beach, 4 + 1.5 * Rough.At(x / 9, z / 9), hd);
        }
        double fa = Layout.FireA(z), fb = Layout.FireB(z);
        if (Layout.SandyEnd(x, z))
        {
            kind = Ground.SandyEnd;
            double flat = Layout.SandEnd + dune + 2.5 * Math.Max(0, Hum.Fbm(x / 160, z / 160, 3));
            return Layout.Lerp(flat, Layout.Rise(x, z), Geo.S((x - (fb - 300)) / 800));
        }
        double ground = Land(x, z, b, dune, out kind);
        if (hd > 0.05 && x < b + CliffRun * 1.3)
        {
            // The headland's cliff: shingle at its foot, rock rising at over 60 degrees to the ground behind it.
            double run = CliffRun * (1 + 0.25 * Rock.At(z / 40, 3));
            if (x < b + run)
            {
                kind = Ground.Cliff;
                double top = Land(b + run, z, b, dune, out _);
                return Layout.Lerp(4, top, Geo.S((x - b) / run)) + 3 * Rock.Fbm(x / 14, z / 14, 2) * Geo.S((x - b) / 8) * Geo.S((b + run - x) / 6);
            }
        }
        if (kind == Ground.Foredune)
            return ground;
        ground = Layout.Lerp(ground, Layout.TrenchGround, Geo.S((x - (fa - 500)) / 500));
        double g;
        if (x < fa + 360)
        {
            g = x < fa ? ground : Layout.Lerp(Layout.TrenchGround, Layout.Meadow, Geo.S((x - fa) / 360));
            kind = x > fa - 420 ? Ground.LineA : kind;
        }
        else if (x < fb - 360)
        {
            kind = Ground.Meadow;
            g = Layout.Meadow + 5 * Layout.Wave2(x, z, 27, 1, 900) + 1.2 * Churn.Fbm(x / 50, z / 50, 3);
        }
        else if (x < fb)
        {
            kind = Ground.LineB;
            g = Layout.Lerp(Layout.Meadow, Layout.TrenchGround, Geo.S((x - (fb - 360)) / 360));
        }
        else
        {
            kind = x < fb + 420 ? Ground.LineB : Ground.Rise;
            g = Layout.Rise(x, z);
            // Hedgerows on low banks every class crosses (6 high, under 17 degrees); the brook's dry bed down the rise.
            g += 6 * Geo.S((30 - hedge) / 30);
            g -= 3 * Geo.S((22 - brook) / 14);
        }
        // Craters stop short of each line's parapet: a shell hole biting into a trench's front wall would be a way in.
        g += craterDh * Geo.S((x - (fa + 190)) / 60) * Geo.S(((fb - 190) - x) / 60);
        var (dh, depth) = Trenches(x, z, fa, fb);
        // A communication trench: floor 24 wide at 28 down, walls at 45 degrees.
        double cdh = comm < 40 ? -28 + Math.Max(0, comm - 12) : 0;
        if (cdh < dh) { dh = cdh; depth = -cdh; }
        // No trench floor below 6: a trench dug where the ground is low is shallower, never flooded (a road for hovers).
        if (dh < 0 && g + dh < 6) { dh = Math.Min(0, 6 - g); depth = -dh; }
        trench = depth;
        return g + dh;
    }

    /// <summary>The land behind the beach before the trench lines: the headland's heath, the foredune, or the dune belt.</summary>
    static double Land(double x, double z, double b, double dune, out Ground kind)
    {
        double ht = Layout.HeathIn(x, z);
        if (ht > -0.15)
        {
            kind = Ground.Heath;
            // Rolling heath: long low swells, knolls of granite breaking through here and there.
            double roll = 9 * Heather.Fbm(x / 420, z / 420, 3) + 4 * Heather.Fbm(x / 120 + 50, z / 120, 2);
            double tor = 10 * Math.Pow(Math.Max(0, Rock.Fbm(x / 260 + 9, z / 260, 2) - 0.25) / 0.75, 2);
            return Layout.Lerp(Layout.Slack, Layout.HeadlandTop, Geo.S((ht + 0.15) / 0.35)) + (roll + tor) * Geo.S((ht + 0.05) / 0.3);
        }
        double fw = Layout.ForeduneWidth(z), into = x - b;
        if (into < fw)
        {
            // The foredune: a seaward face at about 40 degrees up to its crest, a gentle back down to the slacks;
            // at each draw a plain ramp from the beach's top to the slacks instead.
            kind = Ground.Foredune;
            double hc = Layout.ForeduneCrest(z) + 3 * Hum.Fbm(x / 60, z / 60, 2);
            double faceRun = Math.Max(16, (hc - Layout.Berm) / FaceTan);
            double y = into < faceRun ? Layout.Berm + (hc - Layout.Berm) * into / faceRun : Layout.Lerp(hc, Layout.Slack, Geo.S((into - faceRun) / (fw - faceRun)));
            double draw = Layout.Draws.Max(dz => Geo.S((Layout.DrawW / 2 + 40 - Math.Abs(z - dz)) / 40));
            return Layout.Lerp(y, Layout.Lerp(Layout.Berm, Layout.Slack, Geo.S(into / fw)), draw);
        }
        kind = Ground.Dunes;
        return Layout.Slack + dune + 3 * Math.Max(0, Hum.Fbm(x / 90, z / 90, 3)) * Geo.S((into - fw) / 200);
    }

    static float[] Fill(int count, float v)
    {
        var a = new float[count];
        Array.Fill(a, v);
        return a;
    }

    /// <summary>Calls back for every vertex in a box (in parallel rows): its index and position.</summary>
    static void Box(int n, double x0, double z0, double x1, double z1, Action<int, double, double> each)
    {
        int ix0 = Math.Max(0, (int)Math.Floor(x0 / 8)), ix1 = Math.Min(n - 1, (int)Math.Ceiling(x1 / 8));
        int iz0 = Math.Max(0, (int)Math.Floor(z0 / 8)), iz1 = Math.Min(n - 1, (int)Math.Ceiling(z1 / 8));
        for (int iz = iz0; iz <= iz1; iz++)
            for (int ix = ix0; ix <= ix1; ix++)
                each(iz * n + ix, ix * 8.0, iz * 8.0);
    }

    /// <summary>The distance to a polyline, kept where it is the nearest so far, out to <paramref name="margin"/>.</summary>
    static void Distance(int n, IReadOnlyList<P> line, double margin, float[] into)
    {
        for (int s = 0; s + 1 < line.Count; s++)
        {
            P a = line[s], b = line[s + 1];
            Box(n, Math.Min(a.X, b.X) - margin, Math.Min(a.Z, b.Z) - margin, Math.Max(a.X, b.X) + margin, Math.Max(a.Z, b.Z) + margin, (i, x, z) =>
            {
                double d = Geo.ToSegment(new P(x, z), a, b, out _);
                if (d < into[i]) into[i] = (float)d;
            });
        }
    }

    static void FillPolygon(int n, List<P> poly, short value, short[] into)
    {
        double x0 = poly.Min(p => p.X), x1 = poly.Max(p => p.X), z0 = poly.Min(p => p.Z), z1 = poly.Max(p => p.Z);
        Box(n, x0, z0, x1, z1, (i, x, z) =>
        {
            bool inside = false;
            for (int a = 0, b = poly.Count - 1; a < poly.Count; b = a++)
                if ((poly[a].Z > z) != (poly[b].Z > z) && x < (poly[b].X - poly[a].X) * (z - poly[a].Z) / (poly[b].Z - poly[a].Z) + poly[a].X)
                    inside = !inside;
            if (inside) into[i] = value;
        });
    }

    /// <summary>A field read between vertices.</summary>
    public static double Sample(float[] f, int n, P p)
    {
        double fx = Math.Clamp(p.X / 8, 0, n - 1.001), fz = Math.Clamp(p.Z / 8, 0, n - 1.001);
        int ix = (int)fx, iz = (int)fz;
        double tx = fx - ix, tz = fz - iz;
        return (f[iz * n + ix] * (1 - tx) + f[iz * n + ix + 1] * tx) * (1 - tz) + (f[(iz + 1) * n + ix] * (1 - tx) + f[(iz + 1) * n + ix + 1] * tx) * tz;
    }
}
