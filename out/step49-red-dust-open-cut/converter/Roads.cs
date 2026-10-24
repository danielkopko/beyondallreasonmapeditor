// A survey of the haul roads: where each road's ends lead, and along it how steep it climbs, how far it leans across,
// whether it runs into the pit or along a creek's bed. `OpenCut roads` prints it; the checks read it on the built
// map's own heights. A road meets a ramp's top in line with the ramp, not from the side: the ramp drops into a
// cutting the moment it leaves its top, and a road from the side runs over the cutting's wall.
static class Roads
{
    public sealed record Leg(string Name, List<P> Line);

    /// <summary>Every road: the haul roads (numbered as in Layout.Roads, from 1) and the dumps' ramps.</summary>
    public static List<Leg> All() =>
        Terrain.RoadLines.Select((l, i) => new Leg($"road {i + 1}", l))
            .Concat(Terrain.DumpRampLines.Select((l, i) => new Leg($"dump ramp {i + 1}", l))).ToList();

    public sealed record Report(string Name, double Length, string StartsAt, string EndsAt, double MaxGrade, double MaxLean,
        double InPit, double AlongCreek, int CreekCrossings, P SteepestAt, P LeaningAt);

    /// <summary>What a road's end meets: a start, a pad, a hardstand, a ramp's top, the dam, another road, or nothing.</summary>
    public static string EndAt(Terrain.Result t, P p, Leg self, List<Leg> legs)
    {
        const double reach = 130;
        if (Layout.StartsA.Concat(Layout.StartsB).Any(s => (s - p).Length < Layout.BaseRadius)) return "a start";
        if (Terrain.Pads.Any(pd => Terrain.PadRho(p, pd.At, pd.A + reach, pd.B + reach) < 1)) return "a pad";
        if (Layout.Hardstands.Any(h => Terrain.PadRho(p, h.At, h.A + reach, h.B + reach) < 1)) return "a hardstand";
        for (int k = 0; k < t.Ramps.Length; k++) if ((t.Ramps[k][0] - p).Length < reach + Layout.RampHalf) return $"the {Terrain.RampNames[k]} ramp's top";
        for (int k = 0; k < Layout.Dumps.Length; k++) if (Terrain.DumpRho(p, k, Layout.LiftInset) < 1) return "a dump's top";
        if (p.X > Layout.TsfX0 - 400 && p.X < Layout.TsfX1 + 400 && p.Z > Layout.TsfZ0 - 400 && p.Z < Layout.TsfZ1 + 400) return "the tailings dam";
        foreach (var o in legs.Where(o => o != self))
            if (Geo.ToPolyline(p, o.Line, out _) < reach) return o.Name;
        return "nothing";
    }

    public static List<Report> Survey(Terrain.Result t, Func<P, double> H)
    {
        var legs = All();
        var reports = new List<Report>();
        foreach (var leg in legs)
        {
            var line = leg.Line;
            double length = 0, maxGrade = 0, maxLean = 0, inPit = 0, alongCreek = 0;
            P steepest = line[0], leaning = line[0];
            int crossings = 0;
            bool wasInCreek = false;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                var a = line[i]; var b = line[i + 1];
                double seg = (b - a).Length;
                if (seg < 1e-6) continue;
                var dir = (b - a) * (1 / seg);
                for (double s = 0; s < seg; s += 16)
                {
                    var p = a + dir * s;
                    length += Math.Min(16, seg - s);
                    if (Terrain.RoadShown(p) < 0.5) continue;                       // on a pad: the pad, not the road
                    double grade = Math.Atan(Math.Abs(H(p + dir * 24) - H(p - dir * 24)) / 48) * 180 / Math.PI;
                    double lean = Math.Atan(Math.Abs(H(p + dir.Perp * 48) - H(p - dir.Perp * 48)) / 96) * 180 / Math.PI;
                    if (grade > maxGrade) { maxGrade = grade; steepest = p; }
                    if (lean > maxLean) { maxLean = lean; leaning = p; }
                    if (Terrain.Sample(t.Depth, t.N, p) > 0 || Terrain.Sample(t.Depth, t.N, p + dir.Perp * 64) > 0 || Terrain.Sample(t.Depth, t.N, p - dir.Perp * 64) > 0) inPit += 16;
                    bool inCreek = Terrain.ToLines(p, Terrain.CreekLines) < 150;
                    if (inCreek) alongCreek += 16;
                    if (inCreek && !wasInCreek) crossings++;
                    wasInCreek = inCreek;
                }
            }
            reports.Add(new Report(leg.Name, length, EndAt(t, line[0], leg, legs), EndAt(t, line[^1], leg, legs), maxGrade, maxLean, inPit, alongCreek, crossings, steepest, leaning));
        }
        return reports;
    }
}
