// Where units can go on a built map's heights, by the engine's rules (MapCore.Movement: per 8-unit square, the
// slope of its 16-unit cell, and the water depth): flood fills from the starts, with ramps or switchbacks shut off
// to show what each one is the only way through.
using MapCore.Heights;
using MapCore.Imaging;
using MapCore.Movement;

static class Access
{
    public static int Report(HeightMap heights, Design d, Terrain.Result t, Action<string> log)
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
        int w = vehicle.Width, h = vehicle.Height;
        bool[] Mask(Func<P, bool> test)
        {
            var m = new bool[w * h];
            Parallel.For(0, h, z => { for (int x = 0; x < w; x++) m[z * w + x] = test(new P(x * 8 + 4, z * 8 + 4)); });
            return m;
        }
        bool[] Or(params bool[][] masks) { var m = new bool[w * h]; foreach (var k in masks) for (int i = 0; i < m.Length; i++) m[i] |= k[i]; return m; }
        bool[] Ramp(int team) => Mask(p => t.Ramps.Where(r => r.Team == team).Any(r => Terrain.InRect(p, r.Foot, r.Top + r.Dir * 140, r.HalfWidth + 24, 0, out _)));
        var climbs = t.Paths.Where(p => !p.Pass).ToList();
        bool[] Switch(int team, Terrain.Path? except = null) =>
            Mask(p => climbs.Where(s => s.Team == team && s != except).Any(s => Geo.ToPolyline(p, s.Line, out _) <= Terrain.PathHalf + 16));
        string T(int team) => $"team {"ABCDE"[team]}";

        var king = d.Starts.Where(s => s.N == 0).OrderBy(s => s.Team).Select(s => s.At).ToList();
        var peasant = d.Starts.Where(s => s.N == 1).OrderBy(s => s.Team).Select(s => s.At).ToList();
        bool Has(bool[] reach, P p) => Enumerable.Range(-2, 5).Any(dz => Enumerable.Range(-2, 5).Any(dx =>
        {
            int x = (int)(p.X / 8) + dx, z = (int)(p.Z / 8) + dz;
            return x >= 0 && z >= 0 && x < w && z < h && reach[z * w + x];
        }));

        log("Vehicles (TANK3: 27 degrees, 20 deep):");
        var all = Fill(vehicle, peasant[0], null);
        Expect(d.Starts.All(s => Has(all, s.At)), $"from A's first peasant every start is reached ({all.Count(x => x) * 100.0 / all.Length:0.0}% of the map)");
        if (d.Land.Count > 0) Expect(Has(all, d.Centre + new P(0, 200)), "the isle is reached");
        for (int k = 0; k < d.Fold; k++)
        {
            Expect(!Has(Fill(vehicle, peasant[k], Ramp(k)), king[k]), $"{T(k)}: with its ramp shut, its king's ground can't be reached");
            Expect(climbs.Where(s => s.Team == k).All(s => !Has(Fill(vehicle, s.Line[^1], Ramp(k)), king[k])), $"{T(k)}: no zigzag takes vehicles up");
        }
        log("Hovercraft (HOVER3: 33 degrees, over water):");
        for (int k = 0; k < d.Fold; k++)
        {
            Expect(climbs.Where(s => s.Team == k).All(s => !Has(Fill(hover, s.Line[^1], Ramp(k)), king[k])), $"{T(k)}: no zigzag takes hovercraft up");
            Expect(Has(Fill(hover, peasant[k], null), king[k]), $"{T(k)}: the ramp takes them up");
        }
        log("Bots (BOT3: 54 degrees, 20 deep):");
        for (int k = 0; k < d.Fold; k++)
        {
            var mine = climbs.Where(s => s.Team == k).ToList();
            for (int i = 0; i < mine.Count; i++)
                Expect(Has(Fill(bot, mine[i].Line[^1], Or(Ramp(k), Switch(k, mine[i]))), king[k]), $"{T(k)}: zigzag {i + 1} alone takes bots up");
            Expect(!Has(Fill(bot, peasant[k], Or(Ramp(k), Switch(k))), king[k]), $"{T(k)}: with the ramp and zigzags shut, bots can't climb the cliffs");
        }
        var passes = t.Paths.Where(p => p.Pass).ToList();
        if (passes.Count > 0)
        {
            log("Passes over the cliffs (within 200 units of each):");
            foreach (var p in passes)
            {
                var near = Mask(q => Geo.ToPolyline(q, p.Line, out _) > 200);
                bool bots = Has(Fill(bot, p.Line[0], near), p.Line[^1]);
                bool tanks = Has(Fill(vehicle, p.Line[0], near), p.Line[^1]);
                Expect(bots && !tanks, $"{T(p.Team)}'s pass: bots cross it ({(bots ? "yes" : "no")}), vehicles don't ({(tanks ? "they do" : "right")})");
            }
        }
        return failures;
    }

    /// <summary>Squares reachable from a point (4-connected), not entering <paramref name="blocked"/>.</summary>
    public static bool[] Fill(PassabilityMap pass, P from, bool[]? blocked)
    {
        int w = pass.Width, h = pass.Height;
        var seen = new bool[w * h];
        var queue = new Queue<int>();
        int sx = (int)(from.X / 8), sz = (int)(from.Z / 8);
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

    /// <summary>A shaded relief, coloured by height, water blue.</summary>
    public static void Relief(float[] h, int n, string path, int step = 2)
    {
        int w = (n - 1) / step;
        var img = new RgbaImage(w, w);
        float V(int x, int z) => h[Math.Clamp(z, 0, n - 1) * n + Math.Clamp(x, 0, n - 1)];
        Parallel.For(0, w, pz =>
        {
            for (int px = 0; px < w; px++)
            {
                int x = px * step, z = pz * step;
                double dx = (V(x + 1, z) - V(x - 1, z)) / 16.0, dz = (V(x, z + 1) - V(x, z - 1)) / 16.0;
                double len = Math.Sqrt(dx * dx + 1 + dz * dz);
                double light = Math.Clamp((dx * 0.6 + 0.7 + dz * 0.4) / len, 0, 1);
                double y = V(x, z);
                (double r, double g, double b) c = y < -12 ? (50, 90, 170) : y < 0 ? (110, 160, 210) : y < 20 ? (200, 190, 140) : y < 200 ? (120, 170, 110)
                    : y < 260 ? (150, 190, 120) : y < 400 ? (220, 220, 200) : (130, 120, 115);
                double s = 0.35 + 0.75 * light;
                img.SetPixel(px, pz, new Rgba((byte)Math.Clamp(c.r * s, 0, 255), (byte)Math.Clamp(c.g * s, 0, 255), (byte)Math.Clamp(c.b * s, 0, 255), 255));
            }
        });
        PngWriter.WriteRgba(path, img);
    }
}
