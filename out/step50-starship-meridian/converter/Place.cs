// What MapTool adds: metal, the vents, and the ship's features. Features are given for the port half and the
// centreline; every port feature is placed again at its mirror image (x → W − x, heading 180° − h), as its mirror-image
// twin (name_m) if the model isn't symmetric about its own length. Headings are in the engine's units (65536 a turn).
using System.Globalization;
using System.Text;

static class Place
{
    /// <summary>A feature: its model, where it stands, its heading (degrees: 0 facing east (+x), 90 north), kind and room it keeps.</summary>
    public sealed record Item(string Name, P At, double Heading, string Kind, double Radius);

    /// <summary>A heading (degrees) that points a model's +x along a direction.</summary>
    public static double Facing(P dir) => Math.Atan2(-dir.Z, dir.X) * 180 / Math.PI;

    static string F(double v) => Math.Round(v).ToString(CultureInfo.InvariantCulture);

    /// <summary>A heading in degrees as the engine's (65536 to a turn), 0 to 65535.</summary>
    public static int Units(double degrees) => (int)(Math.Round(((degrees % 360 + 360) % 360) * 65536 / 360) % 65536);

    /// <summary>A feature's mirror image across the centreline: its place mirrored, its heading 180° − h.</summary>
    public static Item Mirrored(Item it, ISet<string> asymmetric) =>
        it with { Name = asymmetric.Contains(it.Name) ? it.Name + "_m" : it.Name, At = Layout.Mirror(it.At), Heading = 180 - it.Heading };

    /// <summary>
    /// The ship's features: the port half's (its rooms, the port half of the shared rooms, the hull's port side) and
    /// those on the centreline, then every port feature's mirror image. A room's props stand along its walls, clear of
    /// its doorways (and the way in front of them), its metal and its start; each shared room keeps its crossing lane
    /// open round its landmark. Footprints (16-unit units, length × width) are in the gallery's list.
    /// </summary>
    public static List<Item> Ship(ISet<string> asymmetric)
    {
        var port = new List<Item>();
        var centre = new List<Item>();
        void Add(string name, double x, double z, double heading, string kind = "prop") => port.Add(new Item(name, new P(x, z), heading, kind, 0));
        void Mid(string name, double z, double heading, string kind = "landmark") => centre.Add(new Item(name, new P(Layout.CX, z), heading, kind, 0));
        const double North = 90, South = -90, East = 0, West = 180;

        // The weapons bay: weapon racks along its forward bulkhead, munitions, a missile cart by the bridge ramp.
        foreach (double x in new[] { 2600.0, 2900, 3700, 4000 }) Add("sm_weaponrack", x, 2800, South);
        Add("sm_munitions", 3300, 2830, East);
        Add("sm_munitions", 4250, 2850, East);
        Add("sm_missilecart", 4150, 3050, North);
        // The forward hangar: fighters on its two landing circles, a service tower, a tug, a fuel cart, stores.
        Add("sm_fighter", 1864, 4575, North, "craft");
        Add("sm_fighter", 1864, 5925, North, "craft");
        Add("sm_tower", 1230, 4250, East, "landmark");
        Add("sm_tug", 2440, 4300, North, "vehicle");
        Add("sm_fuelcart", 2440, 6300, North, "vehicle");
        Add("sm_pallet", 1220, 6200, East);
        Add("sm_crate", 1200, 6400, East); Add("sm_crate", 1250, 6440, East);
        // The crew deck: bunks off its outboard bulkhead, mess tables and lockers, med pods and hydroponics aft.
        foreach (double z in new[] { 4120.0, 4220, 4320, 4420, 6080, 6180, 6280, 6380 }) Add("sm_bunks", 2950, z, East);
        foreach (double z in new[] { 4200.0, 4400 }) Add("sm_messtable", 3900, z, East);
        foreach (double z in new[] { 4150.0, 4300 }) Add("sm_lockers", 4300, z, West);
        foreach (double z in new[] { 5950.0, 6100 }) Add("sm_medpod", 4300, z, North);
        foreach (double z in new[] { 6390.0, 6480 }) Add("sm_planter", 4100, z, East);
        // The power room: capacitor banks forward, transformers aft, a pipe run along the hull.
        foreach (double x in new[] { 1180.0, 2560 }) foreach (double z in new[] { 6830.0, 6980 }) Add("sm_capacitor", x, z, East);
        foreach (double x in new[] { 1180.0, 2560 }) Add("sm_transformer", x, 7880, East);
        Add("sm_pipes", 1110, 7350, North, "pipe");
        // The armory: weapon racks along its forward bulkhead, munitions aft, a missile cart.
        foreach (double x in new[] { 2980.0, 3130, 4070, 4220 }) Add("sm_weaponrack", x, 6800, South);
        foreach (double x in new[] { 2980.0, 4220 }) Add("sm_munitions", x, 7880, East);
        Add("sm_missilecart", 3000, 7000, East);
        // The main hangar: a dropship and a cargo shuttle on its circles, a service tower, a tug, a fuel cart.
        Add("sm_dropship", 1864, 8775, North, "craft");
        Add("sm_shuttle", 1864, 10125, North, "craft");
        Add("sm_tower", 1230, 9450, East, "landmark");
        Add("sm_tug", 2440, 8400, North, "vehicle");
        Add("sm_fuelcart", 2440, 10500, North, "vehicle");
        // The cargo hold: containers along its outboard bulkhead, pallets and crates, a loader.
        foreach (double z in new[] { 8300.0, 8460 }) Add("sm_container0", 2950, z, North);
        foreach (double z in new[] { 10440.0, 10600 }) Add("sm_container1", 2950, z, North);
        Add("sm_pallet", 4200, 8300, East); Add("sm_pallet", 4250, 8380, East); Add("sm_crate", 4150, 8420, East);
        Add("sm_loader", 4150, 10450, North, "vehicle");
        Add("sm_pallet", 4250, 10620, East); Add("sm_crate", 4150, 10620, East);
        // The thruster bay: fuel tanks along the hull, transformers, a pipe run.
        foreach (double z in new[] { 11200.0, 11400, 12500, 12700 }) Add("sm_fueltank", 1180, z, North);
        foreach (double z in new[] { 11100.0, 12900 }) Add("sm_transformer", 2550, z, East);
        Add("sm_pipes", 1110, 11950, North, "pipe");
        // The machine shop: a repair gantry aft with a tug beside it, tool benches, crates.
        Add("sm_repairgantry", 3600, 12750, East, "landmark");
        Add("sm_tug", 3950, 12750, North, "vehicle");
        foreach (double z in new[] { 11150.0, 12500 }) Add("sm_bench", 2930, z, East);
        Add("sm_crate", 4250, 11100, East); Add("sm_crate", 4250, 11150, East);

        // The shared rooms' port halves. The bridge: two rows of consoles facing the viewport, south of the lane
        // across it between the ramps' landings.
        foreach (double x in new[] { 5450.0, 5700 }) foreach (double z in new[] { 2950.0, 3200 }) Add("sm_console", x, z, North);
        // The CIC: consoles either side of the crossing lane, facing the holo-table.
        foreach (double z in new[] { 4550.0, 5950 }) Add("sm_console", 5700, z, East);
        // The reactor: a capacitor bank forward, a transformer aft.
        Add("sm_capacitor", 5380, 6870, East);
        Add("sm_transformer", 5380, 9250, East);
        // The shield generator: transformers in its corners.
        foreach (double z in new[] { 9680.0, 11040 }) Add("sm_transformer", 5300, z, East);
        // The engine room: a coolant tower aft.
        Add("sm_coolant", 5380, 12850, East, "landmark");

        // The hull: point-defence turrets and a comm dish on the bow's flank; the main engines on the housings' aft
        // ends, flaring out over space, and a turret between them.
        Add("sm_turret", 3300, 2000, West, "hull");
        Add("sm_turret", 4200, 1500, West, "hull");
        Add("sm_dish", 4000, 2300, West, "hull");
        foreach (double x in new[] { 2000.0, 2800 }) Add("sm_enginebell", x, 13960, South, "engine");
        Add("sm_enginebell", 5744, 13960, South, "engine");
        Add("sm_turret", 2400, 13400, West, "hull");

        // On the centreline: the command chair and the viewport, the bow's antenna and sensor dome, the holo-table,
        // the reactor's core, the shield emitter, the drive core (its glowing end aft), a turret on the stern.
        Mid("sm_chair", 3050, North, "prop");
        Mid("sm_viewport", 1462, North, "hull");
        Mid("sm_antenna", 1250, North, "hull");
        Mid("sm_sensordome", 900, North, "hull");
        Mid("sm_holotable", 5250, North);
        Mid("sm_reactor", Layout.WellAt.Z, North);
        Mid("sm_shield", 10350, North);
        Mid("sm_drivecore", 12420, South);
        Mid("sm_turret", 13400, South, "hull");

        return [.. port, .. port.Select(it => Mirrored(it, asymmetric)), .. centre];
    }

    public static string Ops(IEnumerable<Item> items)
    {
        var o = new StringBuilder();
        o.AppendLine("# Starship Meridian: metal, the vents, the ship's features.");
        foreach (var (at, income, _) in Layout.Spots)
            o.AppendLine($"metal-add:{F(at.X)},{F(at.Z)}:{income.ToString(CultureInfo.InvariantCulture)}");
        foreach (var (at, _) in Layout.Vents)
            o.AppendLine($"item-add:smf:geovent:{F(at.X)},{F(at.Z)}");
        foreach (var it in items)
            o.AppendLine($"item-add:placer:{it.Name}:{F(it.At.X)},{F(it.At.Z)}:{Units(it.Heading)}");
        return o.ToString();
    }
}
