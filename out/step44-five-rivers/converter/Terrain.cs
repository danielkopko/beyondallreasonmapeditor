// The ground of Five Rivers, from the design, the same in every fifth (noise is read in Team A's fifth; the fifths
// meet under the rivers, and the isle where they all meet is kept smooth):
//   - the realms: rim 220 beyond 4256 units from the centre, falling to 140 at 2800 and level inside that;
//   - water: the lake (1792 across from the centre) and the rivers 60 deep under a water level of 0, in valleys that
//     fall gently to the water (vehicles reach the shore everywhere);
//   - the isle: 8 at its shore, 60 on top; fords 10 deep across each river; bridges, causeways with decks at 16;
//   - the halls: ovals at 340 on cliffs 120 high and 40 across (71 degrees: nothing but spiders climbs them), a graded
//     ramp at the front (12 degrees) and three bots-only switchbacks (two flights of 38 degrees with a landing);
//   - beyond the play disc: mountains on a cliff, impassable.
static class Terrain
{
    public const double Water = 0, RiverBed = -60, FordBed = -10, Deck = 16, Shore = 8;
    public const double Rim = 220, Inner = 140, HallTop = 340, IsleTop = 60;
    public const double CliffHalf = 20; // half the cliff's width across (40 units for 120 of height)
    public const double RampDegrees = 12, FlightDegrees = 38, FlightDrop = 60, Landing = 48, PathHalf = 32;
    public const double MountainTop = 640;

    static readonly Noise Lo = new(41), Hi = new(77), Edge = new(13), Peaks = new(5);

    public sealed record Ramp(int Team, P Foot, P Top, P Dir, double HalfWidth, double FootHeight, double Length);
    public sealed record Switchback(int Team, P[] Line, double[] H);

    public static List<Ramp> Ramps = [];
    public static List<Switchback> Switchbacks = [];

    /// <summary>Works out the ramps' and switchbacks' lines from the design (needs the bare ground first).</summary>
    public static void Plan(Design d)
    {
        Ramps = [];
        Switchbacks = [];
        foreach (var r in d.Ramps)
        {
            var hall = d.Halls.Single(h => h.Team == r.Team);
            var dir = (r.B - r.A).Unit;
            var top = Crossing(hall, r.A, r.B);
            double foot = Ground(r.A), len = 0;
            for (int i = 0; i < 4; i++)
            {
                len = (HallTop - foot) / Math.Tan(RampDegrees * Math.PI / 180);
                foot = Ground(top - dir * len);
            }
            Ramps.Add(new Ramp(r.Team, top - dir * len, top, dir, r.HalfWidth, foot, len));
        }
        foreach (var t in d.Tracks)
        {
            var hall = d.Halls.Single(h => h.Team == t.Team);
            var u = (t.B - t.A).Unit;
            var n = u.Perp;
            var edge = Crossing(hall, t.A, t.B);
            double run = FlightDrop / Math.Tan(FlightDegrees * Math.PI / 180);
            var s = edge - u * 40;
            var p1 = s + (u * 0.7071 + n * 0.7071) * run;
            var p1b = p1 + u * Landing;
            var p2 = p1b + (u * 0.7071 - n * 0.7071) * run;
            double foot = Ground(p2 + u * 24);
            Switchbacks.Add(new Switchback(t.Team, [s, p1, p1b, p2], [HallTop, HallTop - FlightDrop, HallTop - FlightDrop, HallTop - 2 * FlightDrop]));
            if (Math.Abs(foot - (HallTop - 2 * FlightDrop)) > 12)
                Console.WriteLine($"note: switchback of team {t.Team} ends at {HallTop - 2 * FlightDrop}, ground there {foot:0}");
        }
    }

    /// <summary>Where the line from a (inside the hall) to b (outside) crosses the hall's edge.</summary>
    static P Crossing(Hall hall, P a, P b)
    {
        double lo = 0, hi = 1;
        bool aInside = HallDistance(hall, a) < 0;
        for (int i = 0; i < 40; i++)
        {
            double mid = (lo + hi) / 2;
            bool inside = HallDistance(hall, a + (b - a) * mid) < 0;
            if (inside == aInside) lo = mid; else hi = mid;
        }
        return a + (b - a) * ((lo + hi) / 2);
    }

    /// <summary>About how far a point is outside the hall's oval (negative inside).</summary>
    public static double HallDistance(Hall hall, P p)
    {
        var d = p - hall.Centre;
        double u = d.Dot(hall.Along) / hall.A, v = d.Dot(hall.Out) / hall.B;
        double e = Math.Sqrt(u * u + v * v);
        if (e < 1e-9) return -Math.Min(hall.A, hall.B);
        double grad = Math.Sqrt(Math.Pow(u / hall.A, 2) + Math.Pow(v / hall.B, 2)) / e;
        return (e - 1) / grad;
    }

    static Design design = null!;
    public static void Use(Design d) => design = d;

    /// <summary>The realms' level ground by distance from the centre.</summary>
    static double Base(double r) => r >= 4256 ? Rim : r >= 2800 ? Inner + (Rim - Inner) * Design.S((r - 2800) / 1456) : Inner;

    /// <summary>Ground beside water: under it 60 deep, a shelf to the shore, then a valley up to the ground beyond.</summary>
    static double Bank(double outside, double ground, double valley)
    {
        if (outside < -120) return RiverBed;
        if (outside < 60) return RiverBed + (Shore - RiverBed) * Design.S((outside + 120) / 180);
        return Shore + (ground - Shore) * Design.S((outside - 60) / valley);
    }

    public static double RiverDistance(P p, out int river)
    {
        double best = double.MaxValue;
        river = -1;
        for (int i = 0; i < design.Rivers.Count; i++)
        {
            double d = Design.ToPolyline(p, design.Rivers[i], out _);
            if (d < best) { best = d; river = i; }
        }
        return best;
    }

    /// <summary>The ground before halls, ramps, switchbacks, bridges and mountains: realms, water, isle, fords.</summary>
    public static double Ground(P p)
    {
        double r = (p - Design.Centre).Length;
        double g = Base(r);
        double h = Math.Min(Bank(r - design.LakeRadius, g, 600), Bank(RiverDistance(p, out _) - design.RiverHalf, g, 850));
        // The isle: up from the lake bed to its shore, then a low dome.
        double inIsle = design.IsleRadius - r;
        if (inIsle > -150)
            h = Math.Max(h, inIsle < 30 ? RiverBed + (Shore - RiverBed) * Design.S((inIsle + 150) / 180) : Shore + (IsleTop - Shore) * Design.S((inIsle - 30) / 400));
        // Gentle rolls on the land (not on the isle, where the fifths meet).
        if (h > Shore + 2 && r > design.IsleRadius + 200)
        {
            var f = Design.Fold(p);
            double n = 5 * Lo.Fbm(f.X / 700, f.Z / 700, 3) + 1.5 * Hi.Fbm(f.X / 140, f.Z / 140, 2);
            h += n * Design.S((h - Shore - 2) / 30);
        }
        foreach (var ford in design.Fords)
        {
            double d = Design.ToSegment(p, ford.A, ford.B, out _);
            if (d < ford.HalfWidth + 100)
                h = Math.Max(h, d < ford.HalfWidth ? FordBed : FordBed + (RiverBed - FordBed) * Design.S((d - ford.HalfWidth) / 100));
        }
        return h;
    }

    /// <summary>The finished height at a point.</summary>
    public static double At(P p)
    {
        double h = Ground(p);
        foreach (var b in design.Bridges)
            if (InRect(p, b.A, b.B, b.HalfWidth, 0, out _))
                h = Math.Max(h, Deck);
        var hall = design.Halls[Design.FifthOf(p)];
        var f = Design.Fold(p);
        double sd = HallDistance(hall, p) + 10 * Edge.Fbm(f.X / 180, f.Z / 180, 3);
        if (sd < CliffHalf)
            h = Math.Max(h, sd <= -CliffHalf ? HallTop : HallTop - (HallTop - Rim) * (sd + CliffHalf) / (2 * CliffHalf));
        foreach (var ramp in Ramps)
        {
            if (ramp.Team != hall.Team) continue;
            var rel = p - ramp.Foot;
            double along = rel.Dot(ramp.Dir), across = Math.Abs(rel.Cross(ramp.Dir));
            if (along >= 0 && along <= ramp.Length + 60 && across <= ramp.HalfWidth)
                h = along >= ramp.Length ? Math.Max(h, HallTop) : ramp.FootHeight + (HallTop - ramp.FootHeight) * along / ramp.Length;
        }
        foreach (var s in Switchbacks)
        {
            if (s.Team != hall.Team) continue;
            if (Design.ToPolyline(p, s.Line, out double run) <= PathHalf)
                h = PathHeight(s, run);
        }
        double rr = (p - Design.Centre).Length - design.PlayRadius;
        if (rr > -8)
        {
            double peak = MountainTop + 90 * Peaks.Fbm(p.X / 900, p.Z / 900, 4);
            h = Math.Max(h, Rim + (peak - Rim) * Design.S((rr + 8) / 56));
        }
        return h;
    }

    public static double PathHeight(Switchback s, double run)
    {
        double acc = 0;
        for (int i = 0; i + 1 < s.Line.Length; i++)
        {
            double len = (s.Line[i + 1] - s.Line[i]).Length;
            if (run <= acc + len || i + 2 == s.Line.Length)
                return s.H[i] + (s.H[i + 1] - s.H[i]) * Math.Clamp((run - acc) / len, 0, 1);
            acc += len;
        }
        return s.H[^1];
    }

    public static bool InRect(P p, P a, P b, double halfWidth, double extend, out double t)
    {
        var ab = b - a;
        double len = ab.Length;
        var u = ab * (1 / len);
        var rel = p - a;
        double along = rel.Dot(u);
        t = along / len;
        return along >= -extend && along <= len + extend && Math.Abs(rel.Cross(u)) <= halfWidth;
    }

    /// <summary>Every heightmap vertex (8 units apart), in parallel.</summary>
    public static float[] Build(int vertsX, int vertsZ)
    {
        var heights = new float[vertsX * vertsZ];
        Parallel.For(0, vertsZ, z =>
        {
            for (int x = 0; x < vertsX; x++)
                heights[z * vertsX + x] = (float)At(new P(x * 8.0, z * 8.0));
        });
        return heights;
    }
}
