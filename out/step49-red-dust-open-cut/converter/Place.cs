// What MapTool adds: metal, the vent, the mine's machines where the crews left them, and the outback's plants and
// rocks. The scenes: the bucket wheel excavator on its terrace at the north end, cutting the face below; on the pit
// floor an excavator at a face with a loaded truck beside it and an empty one waiting, a second excavator idle with
// its truck; a loaded truck at the foot of the west ramp, an empty one parked beside the top of each ramp (never on
// it); on the ROM pad a truck tipping at a stockpile and an excavator idle; on the north dump a truck tipping at the
// edge, another loaded at the ramp's foot; at the workshop two trucks on the go-line; site offices, fuel tanks, utes
// and light towers (still lit) at the plant and the workshop and in the pit. The reclaimable machines on each side
// are matched.
using System.Globalization;
using System.Text;

static class Place
{
    public sealed record Item(string Name, P At, double Heading, string Kind, double Radius);

    /// <summary>A heading (degrees, 0 facing south, 90 east) that points a model's +x along a direction.</summary>
    static double Facing(P dir) => Math.Atan2(-dir.Z, dir.X) * 180 / Math.PI;

    static P RampPoint(Terrain.Result t, int k, double fraction, out P dir)
    {
        var line = t.Ramps[k];
        int i = Math.Clamp((int)(fraction * (line.Count - 1)), 1, line.Count - 2);
        dir = (line[i + 1] - line[i - 1]).Unit;
        return line[i];
    }

    public static List<Item> Machines(Terrain.Result t)
    {
        var items = new List<Item>();
        void Add(string name, P at, double heading, string kind = "machine", double radius = 90) => items.Add(new Item(name, at, heading, kind, radius));
        P Toward(P from, P to) => (to - from).Unit;
        P InPit(double deg, double depth)
        {
            double a = deg * Math.PI / 180;
            var dir = (Layout.Along * (Math.Cos(a) * Layout.PitA) + Layout.Across * (Math.Sin(a) * Layout.PitB)).Unit;
            double r = 0;
            while (r < 6000 && Terrain.Sample(t.Depth, t.N, Layout.PitCentre + dir * r) > 0) r += 8;
            var rim = Layout.PitCentre + dir * r;
            for (double s = 0; s < 4000; s += 4)
                if (Terrain.Sample(t.Depth, t.N, rim - dir * s) >= depth) return rim - dir * s;
            return Layout.PitCentre;
        }

        // The bucket wheel excavator on its terrace, its wheel at the face below.
        var bwe = InPit(183, 210);
        Add("oc_bucketwheel", bwe, Facing(Toward(bwe, Layout.PitCentre)), "landmark", 200);
        // The pit floor: an excavator at the west face, its loaded truck and one waiting; another idle at the east.
        var face = InPit(258, Layout.Wall + 150);
        var outward = Toward(Layout.PitCentre, face);
        Add("oc_excavator", face, Facing(outward));
        var side = outward.Perp;
        Add("oc_truck_loaded", face + side * 200 - outward * 60, Facing(outward * -1));
        Add("oc_truck", face + side * 230 - outward * 300, Facing(side * -1));
        Add("oc_lighttower", face - side * 190 - outward * 120, Facing(outward), "gear", 30);
        var east = InPit(96, Layout.Wall + 170);
        var eastOut = Toward(Layout.PitCentre, east);
        Add("oc_excavator_idle", east, Facing(eastOut));
        Add("oc_truck", east - eastOut.Perp * 210 - eastOut * 80, Facing(eastOut.Perp));
        Add("oc_ute", InPit(300, Layout.Wall + 140), 30, "gear", 30);
        Add("oc_lighttower", InPit(60, Layout.Wall + 140), 200, "gear", 30);
        // The southern lobe's two bucket wheel excavators, one at each wall just south of the waist, each standing in the
        // round bite its wheel has cut into its wall (Terrain), facing the face, the floor between them open.
        foreach (var (wheelAt, wheelDir) in t.PitWheels) Add("oc_bucketwheel_pit", wheelAt, Facing(wheelDir), "landmark", 760);
        // The extension's floor: an excavator at its east face with a loaded truck beside it and an empty one waiting,
        // a lighting plant behind them.
        var from = new P(Layout.SouthCentre.X, 10250);
        var at = from;
        while (Terrain.Sample(t.Depth, t.N, at) > Layout.Wall + 150) at += new P(8, 0);
        var southOut = new P(1, 0);
        Add("oc_excavator", at, Facing(southOut));
        Add("oc_truck_loaded", at + southOut.Perp * 200 - southOut * 60, Facing(southOut * -1));
        Add("oc_truck", at - southOut.Perp * 230 - southOut * 320, Facing(southOut.Perp));
        Add("oc_lighttower", at - southOut * 380 + southOut.Perp * 120, 250, "gear", 30);
        // Beside the ramps (never on them: a truck's footprint, squared to the nearest quarter turn, would close a
        // diagonal road): one loaded at the foot of the west ramp, about to climb; one empty parked at the top of each
        // of the four.
        var w = RampPoint(t, 0, 0.97, out var wd);
        var wSide = (Layout.PitCentre - w).Dot(wd.Perp) > 0 ? wd.Perp : wd.Perp * -1;
        Add("oc_truck_loaded", w + wSide * 230, Facing(wd * -1));
        for (int k = 0; k < t.Ramps.Length; k++)
        {
            var top = RampPoint(t, k, 0.03, out var td);
            var away = (Layout.PitCentre - top).Dot(td.Perp) > 0 ? td.Perp * -1 : td.Perp;
            Add("oc_truck", top + away * 230, Facing(td));
        }
        // The west: the ROM pad (a truck tipping its tail onto a stockpile, an excavator idle at another), the plant
        // pad's offices and tanks.
        var sp = Layout.Stockpiles[1];
        var tip = sp + new P(-230, -40);
        Add("oc_truck_tipping", tip, Facing((tip - sp).Unit));
        var dig = new P(4020, 4600);
        Add("oc_excavator_idle", dig, Facing((sp - dig).Unit));
        Add("oc_lighttower", Layout.RomPad + new P(220, -330), 60, "gear", 30);
        Add("oc_truck", Layout.PlantPad + new P(-300, 230), 90);
        Add("oc_donga0", Layout.PlantPad + new P(80, -180), 0, "gear", 70);
        Add("oc_donga1", Layout.PlantPad + new P(110, -60), 0, "gear", 60);
        Add("oc_donga0", Layout.PlantPad + new P(80, 70), 0, "gear", 70);
        Add("oc_fueltank", Layout.PlantPad + new P(-260, -150), 90, "gear", 70);
        Add("oc_ute", Layout.PlantPad + new P(-40, 230), 0, "gear", 30);
        Add("oc_ute", Layout.PlantPad + new P(280, 160), 110, "gear", 30);
        // The east: the north dump (a truck tipping at its edge, one loaded at the ramp's foot), the workshop.
        var (dumpAt, dumpA, _, _) = Layout.Dumps[0];
        var edge = dumpAt + new P(-dumpA + 380, 260);
        Add("oc_truck_tipping", edge, Facing(new P(-1, 0.4).Unit * -1));
        Add("oc_truck_loaded", Layout.DumpRamps[0][0] + new P(-80, 140), Facing(new P(1, -0.6).Unit));
        Add("oc_lighttower", dumpAt + new P(420, -160), 250, "gear", 30);
        for (int i = 0; i < 2; i++) Add("oc_truck", Layout.Workshop + new P(-200 + i * 110, -60), 90);
        Add("oc_donga0", Layout.Workshop + new P(250, -350), 0, "gear", 70);
        Add("oc_donga1", Layout.Workshop + new P(400, 290), 0, "gear", 60);
        Add("oc_fueltank", Layout.Workshop + new P(510, 350), 0, "gear", 70);
        Add("oc_ute", Layout.Workshop + new P(60, 220), 30, "gear", 30);
        Add("oc_ute", Layout.Workshop + new P(130, 260), 50, "gear", 30);
        Add("oc_lighttower", Layout.Workshop + new P(-330, 200), 80, "gear", 30);
        return items;
    }

    /// <summary>
    /// The railway, the ore's way to it, and the site's tanks, buildings, smaller machines and clutter. The train stands
    /// on the main line from the north edge: three locos heading north, the wagons loaded as far as the loadout (one
    /// under it), empty behind it to just short of road 7's crossing; an empty rake on the siding; a spare loco in the
    /// shed at the line's end, against the buffer stop. The overland conveyor runs from the crusher to the transfer tower
    /// and due east to the loadout's incline, a section every 128. Each side gets the same reclaimable machines (a water
    /// cart, a grader, a dozer, a loader, a service truck, a bus); the pit gets the drill rigs.
    /// </summary>
    public static List<Item> Works(Terrain.Result t)
    {
        var items = new List<Item>();
        void Add(string name, P at, double heading, string kind, double radius) => items.Add(new Item(name, at, heading, kind, radius));
        double north = Facing(new P(0, -1)), south = Facing(new P(0, 1)), east = 0, west = 180;

        // The track: sections laid back from the track's end in the shed to the north edge; the siding and its turnouts.
        for (double c = t.TrackEnd - 64; c + 64 > 0; c -= 128) Add("oc_track", new P(Layout.RailX, c), north, "track", 64);
        foreach (var (a, b) in new[] { (t.Rails[1][0], t.Rails[1][1]), (t.Rails[1][1], t.Rails[1][2]), (t.Rails[1][2], t.Rails[1][3]) })
        {
            var d = b - a;
            int n = Math.Max(1, (int)Math.Ceiling(d.Length / 128));
            for (int i = 0; i < n; i++) Add("oc_track", a + d * ((i + 0.5) / n), Facing(d.Unit), "track", 64);
        }
        // The train on the main line.
        double z = Layout.LocoFront;
        for (int i = 0; i < 3; i++) { Add("oc_loco", new P(Layout.RailX, z + Models.LocoPitch / 2), north, "train", 140); z += Models.LocoPitch; }
        while (z + Models.WagonPitch / 2 <= Layout.LoadoutZ + Models.WagonPitch / 2) { Add("oc_wagon_loaded", new P(Layout.RailX, z + Models.WagonPitch / 2), north, "train", 64); z += Models.WagonPitch; }
        while (z + Models.WagonPitch <= t.CrossingZ - Layout.CrossingClear) { Add("oc_wagon", new P(Layout.RailX, z + Models.WagonPitch / 2), north, "train", 64); z += Models.WagonPitch; }
        // The empty rake on the siding.
        for (double c = Layout.SidingFrom + 70 + Models.WagonPitch / 2; c + Models.WagonPitch / 2 < Layout.SidingTo - 70; c += Models.WagonPitch)
            Add("oc_wagon", new P(Layout.SidingX, c), north, "train", 64);
        // The loadout over the main line; the shed at its end, its spare loco and the buffer stop.
        Add("oc_loadout", new P(Layout.RailX, Layout.LoadoutZ), north, "structure", 90);
        Add("oc_locoshed", new P(Layout.RailX, (t.ShedNorth + t.ShedSouth) / 2), north, "structure", 280);
        Add("oc_loco", new P(Layout.RailX, t.TrackEnd - 20 - Models.LocoPitch / 2), north, "train", 140);
        Add("oc_buffer", new P(Layout.RailX, t.TrackEnd + 6), north, "structure", 30);

        // The crusher at the ROM pad, the overland conveyor north to the transfer tower, then east to the loadout.
        var up = (Layout.Transfer - Layout.Crusher).Unit;
        Add("oc_crusher", Layout.Crusher, Facing(up), "structure", 120);
        double len1 = (Layout.Transfer - Layout.Crusher).Length;
        for (double c = 480 + 64; c - 64 < len1 - 40; c += 128) Add("oc_conveyor", Layout.Crusher + up * c, Facing(up), "conveyor", 40);
        Add("oc_transfer", Layout.Transfer, east, "structure", 50);
        for (double x = Layout.Transfer.X + 40 + 64; x - 64 < Layout.LoadoutFoot.X; x += 128) Add("oc_conveyor", new P(x, Layout.LoadoutZ), east, "conveyor", 40);
        // Power from the plant's generators to the crusher, a pole every 160.
        P p0 = new(2440, 4930), p1 = new(3560, 4060);
        int poles = (int)Math.Round((p1 - p0).Length / 160);
        for (int i = 0; i <= poles; i++) Add("oc_powerpole", p0 + (p1 - p0) * (i / (double)poles), Facing((p1 - p0).Unit), "pole", 10);

        // The west: the plant's fuel, water, power and comms; its vehicles; the ROM pad's dozer and loader; pipe by the dam.
        Add("oc_tank_fuel", new P(3250, 5150), east, "tank", 80);
        Add("oc_tank_fuel_small", new P(3410, 5260), east, "tank", 34);
        Add("oc_drums", new P(3400, 5120), 20, "clutter", 20);
        Add("oc_tank_water", new P(1820, 5250), east, "tank", 76);
        Add("oc_watertower", new P(2050, 5900), east, "tank", 34);
        Add("oc_tank_water_small", new P(2700, 5270), east, "tank", 24);
        Add("oc_tank_water_small", new P(2700, 5520), east, "tank", 24);
        Add("oc_commtower", new P(1700, 5600), east, "structure", 80);
        for (int i = 0; i < 3; i++) Add("oc_genset", new P(2190 + i * 110, 4985), east, "structure", 56);
        Add("oc_bus", new P(2650, 5770), east, "vehicle", 50);
        Add("oc_servicetruck", new P(2330, 5560), east, "vehicle", 56);
        for (int i = 0; i < 3; i++) Add($"oc_container{i}", new P(2120, 5130 + i * 35), east, "clutter", 40);
        Add("oc_tyres", new P(2050, 5420), 30, "clutter", 40);
        Add("oc_loader", new P(3800, 4110), east, "vehicle", 96);
        Add("oc_dozer", Layout.Stockpiles[0] + new P(-300, 0), east, "vehicle", 70);
        Add("oc_drums", new P(3950, 3800), 60, "clutter", 20);
        Add("oc_watercart", new P(4520, 3420), Facing(new P(0.06, -1).Unit), "vehicle", 90);
        Add("oc_grader", new P(3150, 6230), Facing(new P(1100, 950).Unit), "vehicle", 64);
        Add("oc_pipes", new P(2400, 8300), east, "clutter", 92);
        Add("oc_pipes", new P(2650, 8250), 10, "clutter", 92);
        // The east: the workshop and its fuel farm, water, tyres, containers and vehicles; a dozer on the north dump's dozed half.
        Add("oc_workshop", Layout.Workshop + new P(330, -80), south, "structure", 220);
        foreach (double fz in new[] { 6300.0, 6520.0 }) Add("oc_tank_fuel", new P(11250, fz), east, "tank", 80);
        foreach (double fz in new[] { 6300.0, 6420.0 }) Add("oc_tank_fuel_small", new P(11420, fz), east, "tank", 34);
        Add("oc_drums", new P(11420, 6560), 0, "clutter", 20);
        Add("oc_tank_water", new P(11260, 6820), east, "tank", 76);
        Add("oc_tank_water_small", new P(10580, 6200), east, "tank", 24);
        Add("oc_tyres", new P(10000, 6850), 0, "clutter", 40);
        Add("oc_tyres", new P(10160, 6870), 70, "clutter", 40);
        for (int i = 0; i < 3; i++) Add($"oc_container{(i + 1) % 3}", new P(9990, 6140 + i * 35), east, "clutter", 40);
        Add("oc_servicetruck", new P(10600, 6650), west, "vehicle", 56);
        Add("oc_bus", new P(10300, 6150), east, "vehicle", 50);
        Add("oc_loader", new P(10120, 6300), east, "vehicle", 96);
        var (dumpAt, _, _, turn) = Layout.Dumps[0];
        var u = new P(Math.Cos(turn * Math.PI / 180), Math.Sin(turn * Math.PI / 180));
        Add("oc_dozer", dumpAt + u * 500, Facing(u), "vehicle", 70);
        Add("oc_watercart", new P(9000, 4990), Facing(new P(-1300, 200).Unit), "vehicle", 90);
        Add("oc_grader", new P(9300, 9070), Facing(new P(-1300, 500).Unit), "vehicle", 64);
        // Out on the plain: windmills with a rainwater tank beside each; the explosives magazine in its bund.
        foreach (var wm in Layout.Windmills)
        {
            Add("oc_windmill", wm, east, "structure", 30);
            Add("oc_tank_water_small", wm + new P(-45, 35), east, "tank", 24);
        }
        Add("oc_magazine", Layout.Magazine, Facing(new P(1, -1).Unit), "structure", 44);
        // The pit floor's north end: two drill rigs on a fresh pattern, drums beside them.
        foreach (double v in new[] { -170.0, 170.0 }) Add("oc_drill", Terrain.FromFrame(-1450, v), Facing(Layout.Along * -1), "vehicle", 70);
        Add("oc_drums", Terrain.FromFrame(-1320, 0), 0, "clutter", 20);
        return items;
    }

    /// <summary>The outback's plants and rocks.</summary>
    public static List<Item> Outback(Terrain.Result t, List<Item> machines)
    {
        var rnd = new Random(48);
        var noise = new Noise(4801);
        var items = new List<Item>();
        var taken = new HashSet<(int, int)>();
        var starts = Layout.StartsA.Concat(Layout.StartsB).ToArray();
        var spots = Layout.Spots.Select(s => s.At).Concat(t.PitSpots.Select(s => s.At)).Append(t.Vent).ToArray();
        var roads = Terrain.RoadLines.Concat(t.Ramps).Concat(Layout.DumpRamps.Select(r => Geo.Smooth(r, 30))).ToArray();
        bool Free(P p, double room, bool blocking)
        {
            if (p.X < 60 || p.Z < 60 || p.X > Layout.Size - 60 || p.Z > Layout.Size - 60) return false;
            if (machines.Any(m => (m.At - p).Length < m.Radius + 40)) return false;
            if (spots.Any(s => (s - p).Length < 110)) return false;
            if (starts.Any(s => (s - p).Length < (blocking ? 520 : 380))) return false;
            if (Terrain.ToLines(p, roads) < (blocking ? 160 : 80)) return false;
            if (!taken.Add(((int)(p.X / room), (int)(p.Z / room)))) return false;
            return true;
        }
        double H(P p) => Terrain.Sample(t.H, t.N, p);
        double Slope(P p)
        {
            double dx = (H(p + new P(8, 0)) - H(p - new P(8, 0))) / 16, dz = (H(p + new P(0, 8)) - H(p - new P(0, 8))) / 16;
            return Math.Atan(Math.Sqrt(dx * dx + dz * dz)) * 180 / Math.PI;
        }
        bool OnWorks(P p) =>
            (Math.Abs(p.X - Layout.RailBedX) < Layout.RailBedHalf + 70 && p.Z < t.ShedSouth + 120)
            || Geo.ToPolyline(p, Terrain.ConveyorLine, out _) < 70
            || Layout.Hardstands.Any(hs => Terrain.PadRho(p, hs.At, hs.A + 40, hs.B + 40) < 1)
            || (p - Layout.Magazine).Length < 190;
        bool OnPad(P p) =>
            Terrain.PadRho(p, Layout.RomPad, Layout.RomA + 80, Layout.RomB + 80) < 1 || Terrain.PadRho(p, Layout.PlantPad, Layout.PlantA + 80, Layout.PlantB + 80) < 1
            || Terrain.PadRho(p, Layout.Workshop, Layout.WorkshopA + 80, Layout.WorkshopB + 80) < 1
            || (p.X > Layout.TsfX0 - 80 && p.X < Layout.TsfX1 + 80 && p.Z > Layout.TsfZ0 - 80 && p.Z < Layout.TsfZ1 + 80)
            || Enumerable.Range(0, Layout.Dumps.Length).Any(k => Terrain.DumpRho(p, k) < 1.06);
        void Add(string name, P p, string kind, double radius = 10) => items.Add(new Item(name, p, rnd.NextDouble() * 360, kind, radius));

        for (double z = 30; z < Layout.Size; z += 46)
            for (double x = 30; x < Layout.Size; x += 46)
            {
                var p = new P(x + rnd.NextDouble() * 40, z + rnd.NextDouble() * 40);
                double depth = Terrain.Sample(t.Depth, t.N, p), sl = Slope(p), r = rnd.NextDouble();
                double creek = Terrain.ToLines(p, Terrain.CreekLines);
                if (depth > 0)
                {
                    // Rockfall on the pit's berms (never on the ramps).
                    if (sl < 12 && r < 0.012 && Free(p, 120, true)) Add($"oc_boulder{rnd.Next(2)}", p, "rocks", 30);
                    continue;
                }
                if (OnWorks(p)) continue;
                if (OnPad(p))
                {
                    if (sl > 26 && r < 0.02 && Free(p, 120, true)) Add($"oc_boulder{rnd.Next(3)}", p, "rocks", 40);
                    continue;
                }
                double clump = noise.Fbm(x / 600, z / 600, 3);
                // Along the washes: saltbush and the odd ghost gum where water runs; spinifex hummocks on the mounds.
                double wash = Terrain.Sample(t.Wash, t.N, p), rise = Terrain.Sample(t.Mound, t.N, p);
                if (wash > 0.35)
                {
                    if (r < 0.025 && Free(p, 140, true)) Add($"oc_gum{rnd.Next(2)}", p, "gums", 16);
                    else if (r < 0.2 && Free(p, 50, false)) Add("oc_saltbush", p, "saltbush");
                    continue;
                }
                if (rise > 6 && r < 0.12 && Free(p, 40, false)) { Add($"oc_spinifex{rnd.Next(2)}", p, "spinifex"); continue; }
                if (creek < 230 && creek > 50)
                {
                    if (r < 0.16 && Free(p, 70, true)) Add($"oc_gum{rnd.Next(2)}", p, "gums", 16);
                    else if (r < 0.26 && Free(p, 50, false)) Add("oc_saltbush", p, "saltbush");
                    continue;
                }
                if (sl > 26)
                {
                    if (r < 0.05 && Free(p, 100, true)) Add($"oc_boulder{rnd.Next(3)}", p, "rocks", 40);
                    continue;
                }
                if (r < 0.004 && Free(p, 300, true)) Add($"oc_termites{rnd.Next(2)}", p, "termite mounds", 24);
                else if (r < 0.006 && Free(p, 200, true)) Add($"oc_gum{rnd.Next(2)}", p, "gums", 16);
                else if (r < 0.007 && Free(p, 200, true)) Add("oc_deadtree", p, "dead trees", 16);
                else if (r < 0.02 + 0.08 * Geo.S((clump + 0.1) / 0.3) && Free(p, 40, false)) Add($"oc_spinifex{rnd.Next(2)}", p, "spinifex");
                else if (r < 0.125 && r > 0.11 && Free(p, 50, false)) Add("oc_saltbush", p, "saltbush");
            }
        return items;
    }

    static string F(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);
    /// <summary>A heading in degrees as the engine's (65536 to a turn), 0 to 65535.</summary>
    public static string Heading(double degrees) => F(((degrees % 360 + 360) % 360) * 65536 / 360 % 65536);

    public static string Ops(Terrain.Result t, List<Item> items)
    {
        var o = new StringBuilder();
        o.AppendLine("# Red Dust Open Cut: metal, the vent, the mine's machines and gear, the outback's plants and rocks.");
        foreach (var (at, income, _) in Layout.Spots.Concat(t.PitSpots))
            o.AppendLine($"metal-add:{F(at.X)},{F(at.Z)}:{income.ToString(CultureInfo.InvariantCulture)}");
        o.AppendLine($"item-add:smf:geovent:{F(t.Vent.X)},{F(t.Vent.Z)}");
        foreach (var it in items)
            o.AppendLine($"item-add:placer:{it.Name}:{F(it.At.X)},{F(it.At.Z)}:{Heading(it.Heading)}");
        return o.ToString();
    }
}
