// What MapTool adds: metal, the vents, and every feature. From the table: the beach's hedgehogs, the landing craft and
// tanks, the bunkers, the nests, the wire along both lines, the pines, the village and its church, the orchard. Here:
// stakes on the beach, driftwood at the wrack line; shattered stumps across the zone; the farm's walls and barn round
// its yard; the windmill and the lighthouse; the hedgerows' bushes and oaks along every hedge; marram on the dunes, the
// foredune and the sandy end; reeds on the inlet's north bank; hay ricks in the hay and stubble fields; the cove's jetty
// and boat sheds, channel markers down the inlet; telegraph poles along the hill's lanes; boulders on the headland.
// Nothing blocking on a start's ground, a spot, a vent, a road or a trench crossing; nothing on the water but markers.
using System.Globalization;
using System.Text;

static class Place
{
    public sealed record Item(string Name, P At, double Heading, string Kind, double Radius, bool Blocks = true);

    /// <summary>A heading (degrees, 0 facing south, 90 east) that points a model's +x along a direction.</summary>
    public static double Facing(P dir) => Math.Atan2(-dir.Z, dir.X) * 180 / Math.PI;

    static P Dir(double degrees) => new(Math.Cos(degrees * Math.PI / 180), Math.Sin(degrees * Math.PI / 180));

    static double H01(long k, long salt) => Layout.Hash01(k, salt);

    public static List<Item> All(Terrain.Result t)
    {
        var items = new List<Item>();
        var roads = Layout.Roads.Select(r => r.Line).ToList();
        double ToRoads(P p) => roads.Min(l => Geo.ToPolyline(p, l, out _));
        bool Kept(P p, double r, bool blocks)
        {
            // Off the water, the spots and vents, a start's ground (blocking things), the roads and the crossings.
            if (Layout.InWater(p.X, p.Z, r + 10) || Terrain.Sample(t.H, t.N, p) < 1) return false;
            if (Layout.Spots.Any(s => (s.At - p).Length < r + 48) || Layout.Vents.Any(v => (v.At - p).Length < r + 70)) return false;
            if (!blocks) return true;
            if (Layout.Starts.Any(s => (s.At - p).Length < r + 420)) return false;
            if (ToRoads(p) < r + 40) return false;
            foreach (var (fn, gaps) in new (Func<double, double>, double[])[] { (Layout.FireA, Layout.CrossA), (Layout.FireB, Layout.CrossB) })
                foreach (var g in gaps)
                    if (Math.Abs(p.Z - g) < Layout.CrossW / 2 + r + 30 && Math.Abs(p.X - fn(g)) < 420) return false;
            return true;
        }
        void Add(string name, P at, double heading, string kind, double r, bool blocks = true, bool force = false)
        {
            if (at.X < r + 16 || at.Z < r + 16 || at.X > Layout.Size - r - 16 || at.Z > Layout.Size - r - 16) return;   // on the map
            if (force || Kept(at, r, blocks)) items.Add(new Item(name, at, heading, kind, r, blocks));
        }

        // ---- The beach: hedgehogs in their broken rows, stakes among them, landing craft, driftwood at the wrack line.
        int k = 0;
        foreach (var at in Layout.Hedgehogs) Add("ml_hedgehog", at, 360 * H01(k++, 201), "obstacle", 24);
        for (int i = 0; i < 160; i++)
        {
            double z = 200 + H01(i, 202) * (Layout.Size - 400);
            if (Layout.Headland(z) > 0.2 || Layout.Draws.Any(d => Math.Abs(z - d) < 160) || H01(i, 203) < 0.35) continue;
            double x = Layout.Coast(z) + (0.24 + 0.05 * H01(i, 204)) * Layout.BeachWidth(z);
            Add("ml_stake", new P(x, z), Facing(new P(1, 0)) + 30 * (H01(i, 205) - 0.5), "obstacle", 8, blocks: false);
        }
        foreach (var (kind, at, heading) in Layout.Wrecks)
        {
            if (kind == "craft") Add($"ml_craft{(int)(H01((long)at.Z, 206) * 2)}", at, Facing(Dir(heading - 90)), "wreck", 80, force: true);
            else Add($"ml_tank{(int)(H01((long)at.Z, 207) * 2)}", at, Facing(Dir(heading)), "wreck", 50, force: true);
        }
        for (int i = 0; i < 60; i++)
        {
            double z = 300 + H01(i, 208) * (Layout.Size - 600);
            if (Layout.Headland(z) > 0.3) continue;
            double u = 0.72 + 0.05 * Layout.Wave(z, 61, 1, 900) + 0.04 * (H01(i, 209) - 0.5);
            Add("ml_driftwood", new P(Layout.Coast(z) + u * Layout.BeachWidth(z), z), 360 * H01(i, 210), "beach", 30, blocks: false);
        }

        // ---- The defences: bunkers, the casemate, the lighthouse, the nests, the wire.
        foreach (var (kind, at, face) in Layout.Bunkers)
            Add(kind == "casemate" ? "ml_casemate" : "ml_bunker", at, Facing(new P(Math.Sin(face * Math.PI / 180), Math.Cos(face * Math.PI / 180))), "bunker", 60, force: true);
        Add("ml_lighthouse", Layout.Lighthouse, Facing(new P(1, 0)), "landmark", 40, force: true);
        // Dragon's teeth from each line's end across the inlet's bank and out into the water, until it's too deep to wade.
        foreach (var (fn, end) in new (Func<double, double>, double)[] { (Layout.FireA, Layout.EndA), (Layout.FireB, Layout.EndB) })
        {
            int deep = 0;
            for (double z = end + 16; z < end + 900 && deep < 2; z += 40)
            {
                var at = new P(fn(z), z);
                if (Terrain.Sample(t.H, t.N, at) < -22) deep++;
                Add("ml_teeth", at, Facing(new P(1, 0)), "teeth", 24, force: true);
            }
        }
        // Field guns left behind on the hill, below the crest, still trained on the zone.
        foreach (var z in new[] { 2300.0, 5150, 7700, 9950 })
        {
            var at = new P(Layout.CrestX(z) - 420, z);
            for (int tries = 0; tries < 8 && !Kept(at, 40, true); tries++) at += new P(0, 90);
            Add("ml_gun", at, Facing(new P(-1, 0)), "gun", 40);
        }
        // Rubble heaps round the village.
        for (int i = 0; i < 40 && items.Count(it => it.Name == "ml_rubble") < 12; i++)
        {
            double a = H01(i, 250) * Math.PI * 2, r = 150 + 280 * H01(i, 251);
            var at = Layout.Village + new P(Math.Cos(a) * r, Math.Sin(a) * r);
            if (Layout.Houses.Any(hs => (hs.At - at).Length < 80) || (at - Layout.Village).Length < 110) continue;
            Add("ml_rubble", at, 360 * H01(i, 252), "village", 30);
        }
        foreach (var (side, at) in Layout.Nests) Add("ml_nest", at, 360 * H01((long)at.Z, 211), "nest", 20);
        foreach (var (side, z0, z1, off) in Layout.WireRuns)
        {
            Func<double, double> fn = side == "beach" ? Layout.FireA : Layout.FireB;
            double sgn = side == "beach" ? 1 : -1;
            for (double z = z0 + 32; z <= z1 - 32; z += 64)
            {
                var at = new P(fn(z) + sgn * off, z);
                var dir = new P(fn(z + 16) - fn(z - 16), 32).Unit;
                Add("ml_wire", at, Facing(dir), "wire", 32, force: !Layout.Spots.Any(s => (s.At - at).Length < 90));
            }
        }

        // ---- The zone: shattered stumps; the farm's walls and barn round its yard.
        for (int i = 0, n = 0; i < 600 && n < 90; i++)
        {
            double z = 100 + H01(i, 212) * (Layout.Size - 200);
            double fa = Layout.FireA(z), fb = Layout.FireB(z);
            var at = new P(fa + 320 + H01(i, 213) * (fb - fa - 640), z);
            if (Layout.SouthOfInlet(at.X, at.Z) || Terrain.Sample(t.Crater, t.N, at) > -0.3 || (at - Layout.Farm).Length < 320) continue;
            int before = items.Count;
            Add($"ml_stump{(H01(i, 214) < 0.6 ? 0 : 1)}", at, 360 * H01(i, 215), "stump", 14);
            if (items.Count > before) n++;
        }
        Add("ml_barn", Layout.Farm + new P(-40, -96), Facing(new P(1, 0)), "farm", 60, force: true);
        for (int i = 0; i < 9; i++)
        {
            if (i == 2 || i == 6) continue;                                                    // the yard's two gateways
            double a = i * 2 * Math.PI / 9 + 0.2;
            var at = Layout.Farm + new P(Math.Cos(a) * 230, Math.Sin(a) * 170);
            Add("ml_farmwall", at, Facing(new P(-Math.Sin(a) * 230, Math.Cos(a) * 170).Unit), "farm", 50, force: true);
        }

        // ---- The village, the church, the orchard, the windmill.
        Add("ml_church", Layout.Village, Facing(new P(1, 0)), "village", 90, force: true);
        k = 0;
        foreach (var (at, turn) in Layout.Houses)
        {
            k++;
            if ((at - Layout.Village).Length < 140 || items.Any(it => it.Kind == "village" && (it.At - at).Length < 110)) continue;
            Add($"ml_house{k % 2}", at, Facing(Dir(turn)), "village", 50, force: ToRoads(at) > 70);
        }
        foreach (var at in Layout.Orchard) Add("ml_apple", at, 360 * H01((long)at.X, 216), "orchard", 18);
        Add("ml_windmill", Layout.Windmill, Facing(new P(-1, 0)), "landmark", 30, force: true);

        // ---- The hedgerows: a bush every 46 along each hedge, an oak every seventh or so.
        k = 0;
        foreach (var hedge in Layout.Hedges)
        {
            var line = Geo.Smooth(hedge, 23);
            double run = 0;
            for (int i = 1; i < line.Count; i++)
            {
                run += (line[i] - line[i - 1]).Length;
                if (run < 46) continue;
                run = 0;
                k++;
                string name = H01(k, 217) < 0.14 ? $"ml_oak{(int)(H01(k, 218) * 2)}" : $"ml_hedge{(int)(H01(k, 219) * 2)}";
                // Not across a trench: a hedge there would seal it off from the bots that use it.
                if (Terrain.Sample(t.Trench, t.N, line[i]) > 1 || Layout.CommTrenches.Any(c => Geo.ToPolyline(line[i], c.Line, out _) < 70)) continue;
                Add(name, line[i], 360 * H01(k, 220), "hedge", 22);
            }
        }
        // Hay ricks: one or two in each hay or stubble field, near its middle.
        for (int f = 0; f < Layout.Fields.Count; f++)
        {
            double crop = Layout.Hash01(f, 81);
            if (!(crop >= 0.4 && crop < 0.6) && !(crop >= 0.78 && crop < 0.9)) continue;
            var poly = Layout.Fields[f];
            var mid = new P(poly.Average(p => p.X), poly.Average(p => p.Z));
            if (mid.X < Layout.FireB(mid.Z) + 500) continue;
            for (int r = 0; r < 1 + (int)(H01(f, 221) * 2); r++)
                Add("ml_hayrick", mid + new P((H01(f * 3 + r, 222) - 0.5) * 160, (H01(f * 3 + r, 223) - 0.5) * 160), 360 * H01(f, 224), "rick", 20);
        }

        // ---- The coast's plants: the pines on the back dunes, marram on the dunes, the foredune and the sandy end, reeds
        // on the inlet's north bank.
        foreach (var (at, size) in Layout.Pines) Add($"ml_pine{(size > 0.5 ? 1 : 0)}", at, 360 * H01((long)at.Z, 225), "pine", 20);
        for (int iz = 0; iz < Layout.Size / 96; iz++)
            for (int ix = 0; ix < Layout.Size / 96; ix++)
            {
                long id = iz * 200 + ix;
                if (H01(id, 226) < 0.45) continue;
                var at = new P((ix + H01(id, 227)) * 96, (iz + H01(id, 228)) * 96);
                var kind = t.Kind[Math.Clamp((int)(at.Z / 8), 0, t.N - 1) * t.N + Math.Clamp((int)(at.X / 8), 0, t.N - 1)];
                if (kind is not (Terrain.Ground.Foredune or Terrain.Ground.Dunes or Terrain.Ground.SandyEnd)) continue;
                if (Terrain.Sample(t.Slip, t.N, at) > 0.3 || Terrain.Sample(t.Track, t.N, at) < 30 || Terrain.Sample(t.Trench, t.N, at) > 1) continue;
                Add($"ml_marram{(int)(H01(id, 229) * 2)}", at, 360 * H01(id, 230), "marram", 10, blocks: false);
            }
        for (int i = 0; i < 900; i++)
        {
            var at = new P(2900 + H01(i, 231) * 3300, 10050 + H01(i, 232) * 900);
            double bank = Terrain.Sample(t.Inlet, t.N, at);
            if (bank < 8 || bank > 90 || Layout.SouthOfInlet(at.X, at.Z) || H01(i, 233) < 0.6) continue;
            Add("ml_reeds", at, 360 * H01(i, 234), "reeds", 12, blocks: false);
        }

        // ---- The cove and the inlet: the jetty and the boat sheds on the cove's north shore, channel markers.
        var shore = Layout.CoveShore;
        Add("ml_jetty", shore + new P(0, 30), Facing(new P(0, 1)), "cove", 20, blocks: false, force: true);
        Add("ml_jetty", shore + new P(0, 150), Facing(new P(0, 1)), "cove", 20, blocks: false, force: true);
        foreach (var shed in Layout.BoatSheds) Add("ml_boatshed", shed, Facing(new P(0, 1)), "cove", 40, force: true);
        for (int i = 0; i * 760 + 900 < Layout.InletS[^1] - 1400; i++)
        {
            double s = i * 760 + 900;
            int j = Array.FindIndex(Layout.InletS, v => v >= s);
            var (x, z, w) = Layout.InletPts[j];
            var (x2, z2, _) = Layout.InletPts[Math.Min(j + 1, Layout.InletPts.Length - 1)];
            var side = new P(-(z2 - z), x2 - x).Unit * ((i % 2 == 0 ? 1 : -1) * w * 0.82);
            items.Add(new Item("ml_marker", new P(x, z) + side, 0, "marker", 4, false));
        }

        // ---- Telegraph poles along the hill's lanes; boulders on the headland's heath and by its cliffs.
        foreach (var (kind, line) in Layout.Roads.Where(r => r.Kind == "lane"))
        {
            var smooth = Geo.Smooth(line, 30);
            double run = 0;
            for (int i = 1; i < smooth.Count; i++)
            {
                run += (smooth[i] - smooth[i - 1]).Length;
                if (run < 190) continue;
                run = 0;
                var dir = (smooth[i] - smooth[i - 1]).Unit;
                Add("ml_pole", smooth[i] + dir.Perp * 28, 360 * H01(i, 235), "pole", 6, blocks: false);
            }
        }
        for (int i = 0; i < 260; i++)
        {
            var at = new P(500 + H01(i, 236) * 4300, H01(i, 237) * 3400);
            if (Layout.HeathIn(at.X, at.Z) < -0.05 || at.X < Layout.BermX(at.Z) + 60 || H01(i, 238) < 0.8 - 0.5 * Geo.S((Layout.BermX(at.Z) + 600 - at.X) / 400)) continue;
            Add($"ml_boulder{(H01(i, 239) < 0.6 ? 0 : 1)}", at, 360 * H01(i, 240), "boulder", 24);
        }
        return items;
    }

    static string F(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);

    public static string Heading(double degrees) => F(((degrees % 360 + 360) % 360) * 65536 / 360 % 65536);

    public static string Ops(List<Item> items)
    {
        var o = new StringBuilder();
        o.AppendLine("# Marram Line: metal, the vents, and every feature.");
        foreach (var (at, income, _) in Layout.Spots)
            o.AppendLine($"metal-add:{F(at.X)},{F(at.Z)}:{income.ToString(CultureInfo.InvariantCulture)}");
        foreach (var (_, at) in Layout.Vents)
            o.AppendLine($"item-add:smf:geovent:{F(at.X)},{F(at.Z)}");
        foreach (var it in items)
            o.AppendLine($"item-add:placer:{it.Name}:{F(it.At.X)},{F(it.At.Z)}:{Heading(it.Heading)}");
        return o.ToString();
    }
}
