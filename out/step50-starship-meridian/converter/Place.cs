// What MapTool adds: the vents and the ship's features (the metal is the SMF's own: a metal map, see Metal.cs). Features are given for the port half and the
// centreline; every port feature is placed again at its mirror image (x → W − x, heading 180° − h), as its mirror-image
// twin (name_m) if the model isn't symmetric about its own length. Headings are in the engine's units (65536 a turn).
using System.Globalization;
using System.Text;

static class Place
{
    /// <summary>A feature: its model, where it stands, its heading (degrees: 0 facing east (+x), 90 north), kind and room it keeps.</summary>
    public sealed record Item(string Name, P At, double Heading, string Kind, double Radius);

    /// <summary>
    /// BAR's own energy crystals (1.1): their names, footprints in 16-unit squares from their models' radii (small 8,
    /// medium 15, large 25), blocking, for the checks; their energy; and the clusters they're laid in. BAR's maps lay them
    /// in clusters of one colour, never singly (measured on Crystallized Plains: 22 clusters, nearly all of 8, half small
    /// and half medium, 1,200 energy, about 110 across the middle, the crystals about 50 apart; Theta Crystals: about 19,
    /// 3,100 energy).
    /// </summary>
    public static class Crystals
    {
        public const string Small = "pilha_crystal_quartz1", Medium = "pilha_crystal_quartz2",
            SharedSmall = "pilha_crystal_teal1", SharedMedium = "pilha_crystal_teal2", SharedLarge = "pilha_crystal_teal3";
        public static readonly Dictionary<string, (int X, int Z, bool Blocks)> Footprints = new()
        {
            [Small] = (1, 1, true), [Medium] = (2, 2, true), [SharedSmall] = (1, 1, true), [SharedMedium] = (2, 2, true), [SharedLarge] = (3, 3, true),
        };
        public static double Energy(string name) => name.EndsWith('1') ? 100 : name.EndsWith('2') ? 200 : 500;
        public static bool Large(string name) => name.EndsWith('3');
        /// <summary>The least distance between two crystals' centres (a large one keeps more).</summary>
        public const double Spacing = 40, LargeSpacing = 52;
        /// <summary>How near each crystal stands to one already in its cluster, so a cluster grows as one clump from its middle.</summary>
        public const double Link = 90;

        /// <summary>A cluster's crystals, largest first, mediums and smalls alternating.</summary>
        static string[] Mix(int large, int medium, int small, bool shared)
        {
            var mix = Enumerable.Repeat(shared ? SharedLarge : "", large).ToList();
            for (int i = 0; i < Math.Max(medium, small); i++)
            {
                if (i < medium) mix.Add(shared ? SharedMedium : Medium);
                if (i < small) mix.Add(shared ? SharedSmall : Small);
            }
            return [.. mix];
        }
        /// <summary>A start's cluster, as Crystallized Plains': 4 medium, 4 small, 1,200. Each start's circle is edged by two.</summary>
        public static readonly string[] AtStart = Mix(0, 4, 4, false);
        /// <summary>The tech start's: 1 medium, 4 small, 600; two, half a start's (its geothermal vent makes up the rest).</summary>
        public static readonly string[] AtTech = Mix(0, 1, 4, false);
        /// <summary>An inboard room's: 6 medium, 6 small, 1,800; two, half again a start's.</summary>
        public static readonly string[] Inboard = Mix(0, 6, 6, false);
        /// <summary>A shared room's, teal: 1 large, 6 medium, 7 small, 2,400; two (one each side), double a start's.</summary>
        public static readonly string[] Middle = Mix(1, 6, 7, true);

        /// <summary>How far crystals keep from a cell's edge (its wall is 50 and its foot's slope), a door, a vent, a start's
        /// circle's edge and the drive core.</summary>
        public const double WallClear = 80, DoorClear = 150, VentClear = 120, CircleClear = 40, CoreClear = 300;
        /// <summary>A start's clusters lie in a band round its circle: from CircleClear outside it, this deep, this many
        /// degrees each way of their bearing (the tech start's narrower).</summary>
        public const double Band = 120, Spread = 25, TechSpread = 18;
    }

    /// <summary>A cluster of crystals: its room (port's, or a shared room's port half), its crystals and where candidate
    /// (u, v) in 0..1 puts one.</summary>
    sealed record Cluster(string Room, string[] Crystals, Func<double, double, P> Spot);

    static bool Inside(P p, P[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].Z > p.Z) != (poly[j].Z > p.Z) && p.X < (poly[j].X - poly[i].X) * (p.Z - poly[i].Z) / (poly[j].Z - poly[i].Z) + poly[i].X)
                inside = !inside;
        return inside;
    }

    static double ToOutline(P p, P[] poly)
    {
        double best = double.MaxValue;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++) best = Math.Min(best, Geo.ToSegment(p, poly[j], poly[i], out _));
        return best;
    }

    /// <summary>Whether a crystal may stand at p in the given room: in it (the first cell there, as the terrain reads them),
    /// clear of every cell's edge (walls, ramps, steps), the doors, the vents, the start circles and the drive core, and
    /// on port's side of the centreline (its mirror image is starboard's).</summary>
    static bool Free(P p, string room)
    {
        var cell = Layout.Cells.FirstOrDefault(c => Inside(p, c.Outline));
        if (cell == null || cell.Name != room || cell.Side == "starboard" || p.X > Layout.CX - Crystals.LargeSpacing) return false;
        if (Layout.Cells.Any(c => ToOutline(p, c.Outline) < Crystals.WallClear)) return false;
        if (Layout.Doors.Any(d => Math.Sqrt(Math.Pow(Math.Max(0, Math.Max(d.X0 - p.X, p.X - d.X1)), 2) + Math.Pow(Math.Max(0, Math.Max(d.Z0 - p.Z, p.Z - d.Z1)), 2)) < Crystals.DoorClear)) return false;
        if (Layout.Vents.Any(v => (p - v.At).Length < Crystals.VentClear)) return false;
        if (Layout.StartCircles.Any(s => (p - s.Circle).Length < s.R + Crystals.CircleClear)) return false;
        return (p - DriveCore).Length >= Crystals.CoreClear;
    }

    static readonly P DriveCore = new(Layout.CX, 11650);

    /// <summary>
    /// Every crystal (port's half; Ship mirrors them): each cluster's crystals at the first free candidates of a fixed
    /// low-discrepancy sequence (its own offset), at least Spacing from every other and within Link of one already in the
    /// cluster. The first candidate is the cluster's middle (a shared cluster's large crystal stands there).
    /// </summary>
    public static List<(string Name, P At)> CrystalsPort()
    {
        var clusters = new List<Cluster>();
        // Each start's circle, edged on its two wall sides: a band round it, outboard (180°) and inboard (0°). The thruster
        // bay's aft start stands hard by the hull with the ramp inboard of it, so its two swing to its fore- and
        // aft-outboard quarters.
        foreach (var ((room, _), (role, at, r)) in Layout.Starts.Zip(Layout.StartCircles).Take(Layout.Starts.Length / 2))
        {
            bool tech = role == "tech";
            double spread = tech ? Crystals.TechSpread : Crystals.Spread;
            foreach (double bearing in at.X - r < 1300 ? new[] { 125.0, 235 } : [0.0, 180])
                clusters.Add(new Cluster(room, tech ? Crystals.AtTech : Crystals.AtStart, (u, v) =>
                {
                    double a = (bearing + (2 * u - 1) * spread) * Math.PI / 180, rho = r + Crystals.CircleClear + v * Crystals.Band;
                    return new P(at.X + rho * Math.Cos(a), at.Z + rho * Math.Sin(a));
                }));
        }
        // An oval of them: its middle, its half-widths across (x) and along (z).
        Func<double, double, P> Oval(P c, double ax, double az) => (u, v) =>
            new P(c.X + ax * Math.Sqrt(v) * Math.Cos(2 * Math.PI * u), c.Z + az * Math.Sqrt(v) * Math.Sin(2 * Math.PI * u));
        // Each inboard room: two, one against each long wall, diagonally across the room, clear of its four doors (the
        // armory's, shorter, stand in from its corners).
        foreach (var (room, c, ax, az) in new (string, P, double, double)[]
        {
            ("Crew Deck & Mess", new(2990, 4400), 110, 170), ("Crew Deck & Mess", new(3810, 6050), 110, 170),
            ("Armory", new(3110, 7010), 130, 130), ("Armory", new(3690, 7690), 130, 130),
            ("Cargo Hold", new(2990, 8550), 110, 170), ("Cargo Hold", new(3810, 10250), 110, 170),
            ("Machine Shop", new(3810, 11250), 110, 170), ("Machine Shop", new(2990, 12550), 110, 170),
        })
            clusters.Add(new Cluster(room, Crystals.Inboard, Oval(c, ax, az)));
        // Each shared room but the reactor: one each side (port's given), off the crossing doors and the spine's lanes.
        foreach (var (room, c, ax, az) in new (string, P, double, double)[]
        {
            ("Bridge", new(5560, 2300), 150, 170), ("CIC", new(5450, 4330), 150, 150),
            ("Shield Generator", new(5750, 10350), 120, 210), ("Engine Room", new(5470, 12500), 150, 150),
        })
            clusters.Add(new Cluster(room, Crystals.Middle, Oval(c, ax, az)));

        const double G1 = 0.7548776662466927, G2 = 0.5698402909980532;   // the plastic number's R2 sequence
        var laid = new List<(string Name, P At)>();
        for (int k = 0; k < clusters.Count; k++)
        {
            var cl = clusters[k];
            double seed = k * 0.3819660112501051;
            int got = 0;
            for (int i = 0; got < cl.Crystals.Length; i++)
            {
                if (i > 6000) throw new InvalidOperationException($"no room for the {cl.Room}'s cluster {k}: {got} of {cl.Crystals.Length} crystals placed");
                double u = i == 0 ? 0.5 : (0.5 + i * G1 + seed) % 1, v = i == 0 ? 0 : (0.5 + i * G2 + seed) % 1;
                var p = cl.Spot(u, v);
                string name = cl.Crystals[got];
                if (!Free(p, cl.Room)) continue;
                if (laid.Any(q => (q.At - p).Length < (Crystals.Large(name) || Crystals.Large(q.Name) ? Crystals.LargeSpacing : Crystals.Spacing))) continue;
                if (got > 0 && !laid.Skip(laid.Count - got).Any(q => (q.At - p).Length <= Crystals.Link)) continue;
                laid.Add((name, new P(Math.Round(p.X), Math.Round(p.Z))));
                got++;
            }
        }
        return laid;
    }

    /// <summary>A heading (degrees) that points a model's +x along a direction.</summary>
    public static double Facing(P dir) => Math.Atan2(-dir.Z, dir.X) * 180 / Math.PI;

    static string F(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);

    /// <summary>A heading in degrees as the engine's (65536 to a turn), 0 to 65535.</summary>
    public static int Units(double degrees) => (int)(Math.Round(((degrees % 360 + 360) % 360) * 65536 / 360) % 65536);

    /// <summary>A feature's mirror image across the centreline: its place mirrored, its heading 180° − h.</summary>
    public static Item Mirrored(Item it, ISet<string> asymmetric) =>
        it with { Name = asymmetric.Contains(it.Name) ? it.Name + "_m" : it.Name, At = Layout.Mirror(it.At), Heading = 180 - it.Heading };

    /// <summary>
    /// The ship's features: the port half's and those on the centreline, then every port feature's mirror image.
    /// Footprints (16-unit units, length × width) are in the gallery's list.
    /// </summary>
    public static List<Item> Ship(ISet<string> asymmetric)
    {
        var port = new List<Item>();
        var centre = new List<Item>();
        void Add(string name, double x, double z, double heading, string kind = "prop") => port.Add(new Item(name, new P(x, z), heading, kind, 0));
        void Mid(string name, double z, double heading, string kind = "landmark") => centre.Add(new Item(name, new P(Layout.CX, z), heading, kind, 0));
        const double North = 90, South = -90;

        // 1.1, at your call: the ship is bare of props, craft and dressing. What stays: the main engines under the
        // housings' aft edges, flaring out over space below the outer walk; on the centreline the reactor's core in its
        // well and the drive core (forward in the engine room, clear of its ramp).
        foreach (double x in new[] { 2000.0, 2800 }) Add("sm_enginebell", x, 13976, South, "engine");
        Add("sm_enginebell", 5744, 13976, South, "engine");
        Mid("sm_reactor", Layout.WellAt.Z, North);
        Mid("sm_drivecore", 11650, South);

        // 1.1, at your call: energy crystals, BAR's own (pilha_crystal; the game defines them, so the map carries no
        // model): small 100 energy, medium 200, large 500 (a twentieth of that in metal), in clusters as BAR's maps lay
        // them (Crystals). Quartz in each team's half, teal in the shared rooms. Each start's circle is edged by two, one
        // on each wall side: 2,400 a start, the tech start 1,200 (its geothermal vent makes up the rest). Each inboard room
        // (no starts) 3,600, half again a start's; each shared room 4,800, double a start's; none in the reactor (its core
        // is 3,000 and its vents are contested). All clear of doors, ramps, steps and the vents (CrystalsPort, checked).
        int n = 0;
        foreach (var (name, at) in CrystalsPort())
            Add(name, at.X, at.Z, (n++ * 137.5) % 360, "crystal");    // a different facing for each

        return [.. port, .. port.Select(it => Mirrored(it, asymmetric)), .. centre];
    }

    public static string Ops(IEnumerable<Item> items)
    {
        var o = new StringBuilder();
        o.AppendLine("# Starship Meridian: the vents and the ship's features (the metal is in the SMF: a metal map).");
        foreach (var (at, _) in Layout.Vents)
            o.AppendLine($"item-add:smf:geovent:{F(at.X)},{F(at.Z)}");
        foreach (var it in items)
            o.AppendLine($"item-add:placer:{it.Name}:{F(it.At.X)},{F(it.At.Z)}:{Units(it.Heading)}");
        return o.ToString();
    }
}
