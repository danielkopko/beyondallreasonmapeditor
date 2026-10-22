// Where units can go on the finished heights, by the engine's rules (MapCore.Movement: per 8-unit square, the slope
// of its 16-unit cell, and the water depth): flood fills from the starts, with ramps, switchbacks, bridges or fords
// shut off to show what each one is the only way through.
using MapCore.Heights;
using MapCore.Imaging;
using MapCore.Movement;

static class Access
{
    sealed class Squares(int w, int h)
    {
        public readonly int W = w, H = h;
        public bool[] Mask(Func<P, bool> test)
        {
            var m = new bool[W * H];
            Parallel.For(0, H, z => { for (int x = 0; x < W; x++) m[z * W + x] = test(new P(x * 8 + 4, z * 8 + 4)); });
            return m;
        }
    }

    public static int Report(HeightMap heights, Design d, Action<string> log)
    {
        int failures = 0;
        void Expect(bool ok, string what)
        {
            log($"  {(ok ? "ok  " : "FAIL")} {what}");
            if (!ok) failures++;
        }
        var vehicle = new PassabilityMap(heights, BarMoveClasses.Vehicle);
        var bot = new PassabilityMap(heights, BarMoveClasses.Bot);
        var hover = new PassabilityMap(heights, BarMoveClasses.Hover);
        var sq = new Squares(vehicle.Width, vehicle.Height);

        bool[] Ramp(int team) => sq.Mask(p => Terrain.Ramps.Where(r => r.Team == team).Any(r =>
            Terrain.InRect(p, r.Foot, r.Top, r.HalfWidth + 24, 0, out _)));
        bool[] Switch(int team, int skip = -1) => sq.Mask(p => Terrain.Switchbacks.Where(s => s.Team == team).Where((_, i) => i != skip)
            .Any(s => Design.ToPolyline(p, s.Line, out _) <= Terrain.PathHalf + 16));
        var bridgesOverRivers = d.Bridges.Where(b => (b.A - Design.Centre).Length > d.LakeRadius + 300).ToList();
        var bridgesToIsle = d.Bridges.Except(bridgesOverRivers).ToList();
        bool[] Shut(IEnumerable<Segment> segs, double grow) => sq.Mask(p => segs.Any(b => Terrain.InRect(p, b.A, b.B, b.HalfWidth + grow, grow, out _)));
        bool[] Or(params bool[][] masks) { var m = new bool[sq.W * sq.H]; foreach (var k in masks) for (int i = 0; i < m.Length; i++) m[i] |= k[i]; return m; }

        var king = d.Starts.Where(s => s.N == 0).OrderBy(s => s.Team).Select(s => s.At).ToList();
        var peasant = d.Starts.Where(s => s.N == 1).OrderBy(s => s.Team).Select(s => s.At).ToList();
        var isle = Design.Centre + new P(0, 300);

        log("Vehicles (TANK3: 27 degrees, 20 deep):");
        var all = Fill(vehicle, peasant[0], null);
        Expect(d.Starts.All(s => Has(all, sq, s.At)), $"from A's first peasant every start is reached ({Pct(all)} of the map)");
        Expect(Has(all, sq, isle), "the isle is reached");
        for (int t = 0; t < Design.Teams; t++)
        {
            var noRamp = Fill(vehicle, peasant[t], Ramp(t));
            Expect(!Has(noRamp, sq, king[t]), $"team {"ABCDE"[t]}: with its ramp shut, its hall can't be reached");
            var noCrossing = Fill(vehicle, peasant[t], Or(Shut(d.Bridges, 30), Shut(d.Fords, 30)));
            Expect(!Has(noCrossing, sq, peasant[(t + 1) % 5]) && !Has(noCrossing, sq, peasant[(t + 4) % 5]) && !Has(noCrossing, sq, isle),
                $"team {"ABCDE"[t]}: with the bridges and fords shut, no neighbour and not the isle");
            var fordsOnly = Fill(vehicle, peasant[t], Shut(d.Bridges, 30));
            Expect(Has(fordsOnly, sq, peasant[(t + 1) % 5]), $"team {"ABCDE"[t]}: by the fords alone, the next team's land");
            var bridgesOnly = Fill(vehicle, peasant[t], Or(Shut(d.Fords, 30), Shut(bridgesToIsle, 30)));
            Expect(Has(bridgesOnly, sq, peasant[(t + 1) % 5]), $"team {"ABCDE"[t]}: by the river bridges alone, the next team's land");
            var swb = Terrain.Switchbacks.Where(s => s.Team == t).ToList();
            var up = swb.Select(s => Has(Fill(vehicle, Foot(s), Ramp(t)), sq, king[t])).ToList();
            Expect(up.All(x => !x), $"team {"ABCDE"[t]}: no switchback takes vehicles up");
        }

        log("Hovercraft (HOVER3: 33 degrees, over water):");
        for (int t = 0; t < Design.Teams; t++)
        {
            var swb = Terrain.Switchbacks.Where(s => s.Team == t).ToList();
            Expect(swb.All(s => !Has(Fill(hover, Foot(s), Ramp(t)), sq, king[t])), $"team {"ABCDE"[t]}: no switchback takes hovercraft up");
            Expect(Has(Fill(hover, peasant[t], null), sq, king[t]), $"team {"ABCDE"[t]}: the ramp takes them up");
        }

        log("Bots (BOT3: 54 degrees, 20 deep):");
        for (int t = 0; t < Design.Teams; t++)
        {
            var swb = Terrain.Switchbacks.Where(s => s.Team == t).ToList();
            for (int i = 0; i < swb.Count; i++)
                Expect(Has(Fill(bot, Foot(swb[i]), Or(Ramp(t), Switch(t, skip: i))), sq, king[t]),
                    $"team {"ABCDE"[t]}: switchback {i + 1} alone takes bots up");
            Expect(!Has(Fill(bot, peasant[t], Or(Ramp(t), Switch(t))), sq, king[t]), $"team {"ABCDE"[t]}: with the ramp and switchbacks shut, bots can't climb the cliffs");
            var noCrossing = Fill(bot, peasant[t], Or(Shut(d.Bridges, 30), Shut(d.Fords, 30)));
            Expect(!Has(noCrossing, sq, peasant[(t + 1) % 5]), $"team {"ABCDE"[t]}: with the bridges and fords shut, the rivers stop bots");
        }
        return failures;
    }

    static P Foot(Terrain.Switchback s) => s.Line[^1] + (s.Line[^1] - s.Line[^2]).Unit * 40;

    static string Pct(bool[] m) => $"{m.Count(x => x) * 100.0 / m.Length:0.0}%";

    static bool Has(bool[] reach, Squares sq, P p) =>
        Enumerable.Range(-2, 5).Any(dz => Enumerable.Range(-2, 5).Any(dx =>
        {
            int x = (int)(p.X / 8) + dx, z = (int)(p.Z / 8) + dz;
            return x >= 0 && z >= 0 && x < sq.W && z < sq.H && reach[z * sq.W + x];
        }));

    /// <summary>Squares reachable from a point (4-connected), not entering <paramref name="blocked"/>.</summary>
    public static bool[] Fill(PassabilityMap pass, P from, bool[]? blocked)
    {
        int w = pass.Width, h = pass.Height;
        var seen = new bool[w * h];
        var queue = new Queue<int>();
        int sx = (int)(from.X / 8), sz = (int)(from.Z / 8);
        // Start from the nearest open square (a start can sit on a small bump).
        for (int r = 0; r < 6 && queue.Count == 0; r++)
            for (int dz = -r; dz <= r && queue.Count == 0; dz++)
                for (int dx = -r; dx <= r && queue.Count == 0; dx++)
                {
                    int x = sx + dx, z = sz + dz;
                    if (x < 0 || z < 0 || x >= w || z >= h) continue;
                    if (pass[x, z] is Passage.TooSteep or Passage.TooDeep || (blocked?[z * w + x] ?? false)) continue;
                    seen[z * w + x] = true;
                    queue.Enqueue(z * w + x);
                }
        while (queue.Count > 0)
        {
            int c = queue.Dequeue(), cx = c % w, cz = c / w;
            foreach (var (nx, nz) in new[] { (cx + 1, cz), (cx - 1, cz), (cx, cz + 1), (cx, cz - 1) })
            {
                if (nx < 0 || nz < 0 || nx >= w || nz >= h) continue;
                int ni = nz * w + nx;
                if (seen[ni] || (blocked?[ni] ?? false)) continue;
                if (pass[nx, nz] is Passage.TooSteep or Passage.TooDeep) continue;
                seen[ni] = true;
                queue.Enqueue(ni);
            }
        }
        return seen;
    }

    /// <summary>A shaded relief, coloured by height, water blue, to look at the ground.</summary>
    public static void Relief(HeightMap h, string path, int step = 2)
    {
        int w = (h.Width - 1) / step, hh = (h.Height - 1) / step;
        var img = new RgbaImage(w, hh);
        Parallel.For(0, hh, pz =>
        {
            for (int px = 0; px < w; px++)
            {
                int x = px * step, z = pz * step;
                double dx = (h[x + 1, z] - h[x - 1, z]) / 16.0, dz = (h[x, z + 1] - h[x, z - 1]) / 16.0;
                double nx = -dx, ny = 1, nz = -dz, len = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                double light = Math.Clamp((nx * -0.6 + ny * 0.7 + nz * -0.4) / len, 0, 1);
                double y = h[x, z];
                (double r, double g, double b) c = y < -12 ? (50, 90, 170) : y < 0 ? (110, 160, 210) : y < 20 ? (200, 190, 140) : y < 150 ? (120, 170, 110)
                    : y < 230 ? (150, 190, 120) : y < 400 ? (220, 220, 200) : (130, 120, 115);
                double s = 0.35 + 0.75 * light;
                img.SetPixel(px, pz, new Rgba((byte)Math.Clamp(c.r * s, 0, 255), (byte)Math.Clamp(c.g * s, 0, 255), (byte)Math.Clamp(c.b * s, 0, 255), 255));
            }
        });
        PngWriter.WriteRgba(path, img);
    }
}
