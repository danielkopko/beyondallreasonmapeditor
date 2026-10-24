// The ship's features. Each faces +x (a craft's nose, a console's screens), y up from the deck, z across, and is built
// symmetric about its length (z → −z) so its mirror image on the starboard side is the same model turned (180° − h);
// a craft's navigation lights are red to its port and green to its starboard, which a turn keeps right. Landmarks
// (the reactor, shield, drive, engines, towers, turrets) are indestructible; the craft, machines and props reclaimable.
using System.Numerics;

static class Models
{
    static Vector3 V(double x, double y, double z) => new((float)x, (float)y, (float)z);
    static readonly int[] Sides = [1, -1];

    /// <summary>A ring of points (z, y) for a loft: an octagon-ish section, half-width w, from y0 up to y1, its top narrower.</summary>
    static Vector2[] Section(float w, float y0, float y1, float top = 0.6f, float bottom = 0.7f)
    {
        float h = y1 - y0;
        return
        [
            new(-w * bottom, y0), new(w * bottom, y0), new(w, y0 + h * 0.35f), new(w * 0.95f, y0 + h * 0.7f),
            new(w * top, y1), new(-w * top, y1), new(-w * 0.95f, y0 + h * 0.7f), new(-w, y0 + h * 0.35f),
        ];
    }

    static Region HullEdge(int k) => k is 0 ? Region.Graphite : k is 4 or 3 or 5 ? Region.PlateLight : Region.Plate;

    static void Wheel(Mesh m, Vector3 c, float r, float width)
    {
        m.Tube(c - V(0, 0, width / 2), c + V(0, 0, width / 2), r, r, Region.Rubber, 12, cap0: Region.Dark, cap1: Region.Dark);
    }

    static void Leg(Mesh m, Vector3 top, float drop)
    {
        m.Beam(top, top - V(0, drop - 3, 0), 2.6f, 2.6f, Region.Chrome);
        m.Box(top - V(7, drop, 7), top - V(-7, drop - 3, -7), Region.Dark, Region.Dark);
    }

    // ---- The hangars.

    public static FeatureDef Dropship()
    {
        var m = new Mesh();
        var stations = new List<(float, Vector2[])>
        {
            (-160, Section(34, 46, 80, 0.5f)), (-110, Section(52, 36, 92)), (0, Section(58, 32, 96)), (80, Section(54, 34, 92)),
            (130, Section(40, 40, 80, 0.5f)), (162, Section(14, 54, 64, 0.5f)),
        };
        m.Loft(stations, HullEdge, Region.Graphite, Region.Graphite);
        // The cockpit's windows round the nose, a lit cabin strip down each side.
        foreach (int s in Sides)
        {
            m.Quad(V(132, 78, s * 22), V(150, 70, s * 10), V(150, 62, s * 18), V(132, 66, s * 34), V(0.4, 0.5, s), Region.Window, stretch: true);
            m.Seam(V(-80, 70, s * 57.5), V(70, 70, s * 55), V(0, 0, s), 4, Region.Glow);
        }
        m.Quad(V(130, 80.5f, -22), V(130, 80.5f, 22), V(152, 70, 10), V(152, 70, -10), Vector3.UnitY, Region.Window, stretch: true);
        // Stub wings, swept, with engine pods under their roots; navigation lights at the tips.
        foreach (int s in Sides)
        {
            m.Slab(V(0, 56, 0), Vector3.UnitX, Vector3.UnitZ, [new(40, s * 56), new(-20, s * 140), new(-70, s * 140), new(-60, s * 56)], 6, Region.PlateLight, Region.Graphite);
            m.Tube(V(-150, 52, s * 76), V(-40, 52, s * 76), 20, 22, Region.Plate, 12, cap0: Region.EngineBell, cap1: Region.Graphite, stretch: false);
            m.Box(V(-70, 59, s * 136 - 4), V(-60, 64, s * 136 + 4), Region.Dark, s > 0 ? Region.NavGreen : Region.NavRed);
            // The tail fins, canted out.
            m.Slab(V(-150, 80, s * 20), Vector3.UnitX, Vector3.Normalize(V(0, 1, s * 0.5)), [new(0, 0), new(50, 0), new(10, 46), new(-10, 46)], 4, Region.PlateLight, Region.Graphite);
        }
        // The landing gear, and the rear ramp down to the deck.
        Leg(m, V(110, 36, 0), 36);
        foreach (int s in Sides) Leg(m, V(-50, 34, s * 46), 34);
        m.Quad(V(-160, 46, -26), V(-160, 46, 26), V(-210, 0, 26), V(-210, 0, -26), V(-0.6, 1, 0), Region.Grate);
        m.Quad(V(-160, 45, 26), V(-160, 45, -26), V(-210, -1, -26), V(-210, -1, 26), V(0.6, -1, 0), Region.Graphite);
        return new FeatureDef("sm_dropship", "Starship Meridian: a dropship", Features.Centred(m), false, 600, Base: (18, 8));
    }

    public static FeatureDef Fighter()
    {
        var m = new Mesh();
        var stations = new List<(float, Vector2[])> { (-56, Section(14, 14, 30, 0.6f)), (-20, Section(18, 12, 34)), (30, Section(14, 14, 32)), (64, Section(5, 20, 26, 0.5f)) };
        m.Loft(stations, k => k is 4 or 3 or 5 ? Region.Graphite : Region.Plate, Region.Graphite, Region.Graphite);
        m.Blob(V(14, 34, 0), V(22, 7, 9), Region.Window, 31, 0f, 1);
        foreach (int s in Sides)
        {
            m.Slab(V(0, 20, 0), Vector3.UnitX, Vector3.UnitZ, [new(16, s * 16), new(-30, s * 50), new(-44, s * 50), new(-36, s * 16)], 3, Region.Plate, Region.Graphite);
            m.Tube(V(-62, 22, s * 11), V(-30, 22, s * 11), 7, 8, Region.Graphite, 10, cap0: Region.EngineBell);
            m.Slab(V(-58, 30, s * 9), Vector3.UnitX, Vector3.Normalize(V(0, 1, s * 0.35)), [new(0, 0), new(26, 0), new(6, 22), new(-4, 22)], 2.5f, Region.Plate, Region.Graphite);
            m.Box(V(-44, 21, s * 50 - 2), V(-38, 24, s * 50 + 2), Region.Dark, s > 0 ? Region.NavGreen : Region.NavRed);
            m.Seam(V(-30, 23.5f, s * 32), V(4, 23.5f, s * 17), Vector3.UnitY, 2, Region.Glow);
        }
        Leg(m, V(40, 14, 0), 14);
        foreach (int s in Sides) Leg(m, V(-24, 12, s * 14), 12);
        return new FeatureDef("sm_fighter", "Starship Meridian: a fighter", Features.Centred(m), false, 200);
    }

    public static FeatureDef Shuttle()
    {
        var m = new Mesh();
        var stations = new List<(float, Vector2[])> { (-96, Section(48, 18, 70, 0.8f, 0.9f)), (60, Section(52, 16, 74, 0.8f, 0.9f)), (96, Section(40, 24, 60, 0.6f, 0.8f)) };
        m.Loft(stations, k => k is 4 ? Region.Orange : k == 0 ? Region.Graphite : Region.PlateLight, Region.Graphite, Region.Graphite);
        m.Quad(V(97, 58, -22), V(97, 58, 22), V(97, 44, 28), V(97, 44, -28), Vector3.UnitX, Region.Window, stretch: true);
        foreach (int s in Sides)
        {
            m.Quad(V(-10, 60, s * 51.5f), V(20, 60, s * 51.5f), V(20, 24, s * 51.5f), V(-10, 24, s * 51.5f), V(0, 0, s), Region.Hazard, stretch: true);   // the side door
            for (float x = -70; x < 50; x += 30) m.Quad(V(x, 62, s * 51), V(x + 16, 62, s * 51), V(x + 16, 54, s * 51), V(x, 54, s * 51), V(0, 0, s), Region.WindowLit, stretch: true);
            m.Tube(V(-118, 44, s * 24), V(-96, 44, s * 24), 16, 13, Region.Graphite, 12, cap0: Region.EngineBell);
            foreach (float x in new[] { -60f, 60f }) Leg(m, V(x, 18, s * 36), 18);
        }
        return new FeatureDef("sm_shuttle", "Starship Meridian: a cargo shuttle", Features.Centred(m), false, 400);
    }

    public static FeatureDef Tug()
    {
        var m = new Mesh();
        foreach (int s in Sides) foreach (float x in new[] { -20f, 22f }) Wheel(m, V(x, 8, s * 18), 8, 6);
        m.Box(V(-34, 8, -15), V(34, 20, 15), Region.Orange, Region.Orange);
        m.Box(V(-30, 20, -13), V(-4, 40, 13), Region.Orange, Region.Graphite);
        m.Quad(V(-3.5, 38, -11), V(-3.5, 38, 11), V(-3.5, 26, 11), V(-3.5, 26, -11), Vector3.UnitX, Region.Window, stretch: true);
        m.Box(V(34, 10, -6), V(52, 14, 6), Region.Hazard, Region.Hazard);                          // the tow bar
        m.Box(V(-10, 40, -10), V(-4, 43, 10), Region.Dark, Region.GlowWarm);                         // the beacon bar
        return new FeatureDef("sm_tug", "Starship Meridian: a tow tug", Features.Centred(m), false, 80);
    }

    public static FeatureDef FuelCart()
    {
        var m = new Mesh();
        foreach (int s in Sides) foreach (float x in new[] { -26f, 26f }) Wheel(m, V(x, 7, s * 17), 7, 5);
        m.Box(V(-38, 7, -14), V(38, 12, 14), Region.Graphite, Region.Graphite);
        m.Tube(V(-34, 28, 0), V(28, 28, 0), 15, 15, Region.Yellow, 14, cap0: Region.Yellow, cap1: Region.Yellow);
        m.Tube(V(34, 18, -9), V(34, 18, 9), 7, 7, Region.Rubber, 10, cap0: Region.Yellow, cap1: Region.Yellow);    // the hose reel
        m.Box(V(-40, 12, -10), V(-34, 30, 10), Region.Yellow, Region.Hazard);
        return new FeatureDef("sm_fuelcart", "Starship Meridian: a fuel cart", Features.Centred(m), false, 60, 300);
    }

    public static FeatureDef ServiceTower()
    {
        var m = new Mesh();
        float h = 170;
        foreach (int a in Sides) foreach (int b in Sides) m.Beam(V(a * 22, 0, b * 22), V(a * 22, h, b * 22), 3, 3, Region.Yellow);
        for (float y = 30; y <= h; y += 35)
            foreach (int a in Sides) { m.Beam(V(a * 22, y, -22), V(a * 22, y, 22), 1.8f, 1.8f, Region.Yellow); m.Beam(V(-22, y, a * 22), V(22, y, a * 22), 1.8f, 1.8f, Region.Yellow); }
        m.Box(V(-30, h, -30), V(30, h + 6, 30), Region.Grate, Region.Grate);
        // The service arm out over a craft, its lights.
        m.Beam(V(22, h - 20, 0), V(110, h - 30, 0), 5, 4, Region.Yellow);
        m.Box(V(100, h - 46, -8), V(116, h - 30, 8), Region.Graphite, Region.GlowWarm);
        m.Box(V(-28, h + 6, -4), V(-22, h + 30, 4), Region.Dark, Region.NavRed);
        return new FeatureDef("sm_tower", "Starship Meridian: a hangar service tower", m, true, 0, Base: (4, 4));
    }

    // ---- The bridge and the CIC.

    public static FeatureDef Console()
    {
        var m = new Mesh();
        foreach (float z in new[] { -30f, 0f, 30f })
        {
            m.Box(V(-14, 0, z - 13), V(10, 18, z + 13), Region.Graphite, Region.Graphite);
            m.Quad(V(10, 18, z - 12), V(10, 18, z + 12), V(2, 32, z + 12), V(2, 32, z - 12), V(1, 0.6, 0), Region.Screen, stretch: true);
            m.Quad(V(-14, 18, z - 13), V(10, 18, z - 13), V(2, 32, z - 13), V(-14, 32, z - 13), V(0, 0, -1), Region.Graphite);
            m.Quad(V(-14, 18, z + 13), V(10, 18, z + 13), V(2, 32, z + 13), V(-14, 32, z + 13), V(0, 0, 1), Region.Graphite);
            m.Quad(V(-14, 32, z - 13), V(2, 32, z - 13), V(2, 32, z + 13), V(-14, 32, z + 13), Vector3.UnitY, Region.Graphite);
            m.Quad(V(-14, 32, z + 13), V(-14, 32, z - 13), V(-14, 18, z - 13), V(-14, 18, z + 13), -Vector3.UnitX, Region.Vent, stretch: true);
            m.Seam(V(10.5f, 4, z - 11), V(10.5f, 4, z + 11), Vector3.UnitX, 2, Region.Glow);
        }
        return new FeatureDef("sm_console", "Starship Meridian: a console bank", Features.Centred(m), false, 40);
    }

    public static FeatureDef Chair()
    {
        var m = new Mesh();
        m.Tube(V(0, 0, 0), V(0, 14, 0), 9, 6, Region.Chrome, 10, cap1: Region.Dark);
        m.Box(V(-12, 14, -12), V(12, 20, 12), Region.Graphite, Region.Fabric);
        m.Box(V(-14, 20, -12), V(-9, 44, 12), Region.Graphite, Region.Graphite);
        foreach (int s in Sides)
        {
            m.Box(V(-10, 20, s * 13 - 3), V(14, 27, s * 13 + 3), Region.Graphite, Region.Screen);
            m.Quad(V(6, 27.5f, s * 11), V(14, 27.5f, s * 11), V(14, 27.5f, s * 15), V(6, 27.5f, s * 15), Vector3.UnitY, Region.Screen, stretch: true);
        }
        return new FeatureDef("sm_chair", "Starship Meridian: the command chair", Features.Centred(m), false, 30);
    }

    public static FeatureDef HoloTable()
    {
        var m = new Mesh();
        m.Tube(V(0, 0, 0), V(0, 26, 0), 66, 60, Region.Graphite, 20, cap1: Region.Dark);
        m.Tube(V(0, 26, 0), V(0, 29, 0), 64, 64, Region.Glow, 20, cap1: Region.Holo);
        // The projection: a glowing globe of the ship's system over the table, a ring round it.
        m.Plant++;
        m.Blob(V(0, 66, 0), V(34, 30, 34), Region.Holo, 41, 0f, 2);
        m.Tube(V(0, 64, 0), V(0, 66, 0), 50, 50, Region.Holo, 20);
        m.Plant--;
        return new FeatureDef("sm_holotable", "Starship Meridian: the holo-table", m, true, 0, Base: (8, 8));
    }

    public static FeatureDef Viewport()
    {
        var m = new Mesh();
        // A long armoured window, sloped back, in its frame: set on the bridge's forward bulkhead.
        m.Box(V(-12, 0, -300), V(12, 10, 300), Region.Graphite, Region.Graphite);
        m.Quad(V(12, 10, -296), V(12, 10, 296), V(-4, 58, 296), V(-4, 58, -296), V(1, 0.4, 0), Region.Window);
        for (float z = -300; z <= 300; z += 75) m.Beam(V(12, 10, z), V(-4, 60, z), 3, 3, Region.Graphite);
        m.Box(V(-10, 58, -302), V(4, 66, 302), Region.Graphite, Region.Plate);
        return new FeatureDef("sm_viewport", "Starship Meridian: the bridge viewport", m, true, 0, Blocking: false);
    }

    public static FeatureDef SensorDome()
    {
        var m = new Mesh();
        m.Tube(V(0, 0, 0), V(0, 20, 0), 56, 52, Region.Plate, 18, cap1: Region.Graphite);
        m.Blob(V(0, 20, 0), V(46, 44, 46), Region.PlateLight, 51, 0.0f, 2, cutY: 18);
        m.Beam(V(0, 60, 0), V(0, 104, 0), 2, 2, Region.Chrome);
        m.Box(V(-3, 104, -3), V(3, 110, 3), Region.NavRed, Region.NavRed);
        return new FeatureDef("sm_sensordome", "Starship Meridian: a sensor dome", Features.Centred(m), true, 0);
    }

    // ---- Power, the reactor, the shield.

    public static FeatureDef ReactorCore()
    {
        var m = new Mesh();
        m.Tube(V(0, 0, 0), V(0, 18, 0), 178, 170, Region.Graphite, 28, cap1: Region.Plate);
        m.Tube(V(0, 18, 0), V(0, 34, 0), 140, 136, Region.Hazard, 28, cap1: Region.Graphite);
        // The core: a column of energy behind a cage of rings and ribs, capped.
        m.Tube(V(0, 34, 0), V(0, 250, 0), 70, 70, Region.Energy, 20);
        for (float y = 60; y <= 230; y += 42)
            m.Tube(V(0, y, 0), V(0, y + 10, 0), 92, 92, Region.Plate, 24, cap0: Region.Graphite, cap1: Region.Graphite);
        for (int k = 0; k < 8; k++)
        {
            double a = k * Math.PI / 4;
            var d = V(Math.Cos(a), 0, Math.Sin(a));
            m.Beam(d * 92 + V(0, 34, 0), d * 92 + V(0, 252, 0), 4, 4, Region.Graphite);
            m.Beam(d * 136 + V(0, 34, 0), d * 96 + V(0, 120, 0), 5, 5, Region.Plate);            // the buttresses
        }
        m.Tube(V(0, 250, 0), V(0, 278, 0), 100, 60, Region.Plate, 24, cap1: Region.Glow);
        return new FeatureDef("sm_reactor", "Starship Meridian: the main reactor's core", m, true, 0, Base: (22, 22));
    }

    public static FeatureDef Capacitor()
    {
        var m = new Mesh();
        m.Box(V(-36, 0, -22), V(36, 30, 22), Region.Graphite, Region.Plate);
        foreach (float x in new[] { -22f, 0f, 22f })
        {
            m.Tube(V(x, 30, 0), V(x, 58, 0), 9, 9, Region.Copper, 12, cap1: Region.Chrome);
            m.Tube(V(x, 58, 0), V(x, 66, 0), 4, 3, Region.Chrome, 8);
        }
        foreach (int s in Sides) m.Seam(V(-32, 20, s * 22.5f), V(32, 20, s * 22.5f), V(0, 0, s), 3, Region.Glow);
        return new FeatureDef("sm_capacitor", "Starship Meridian: a capacitor bank", Features.Centred(m), false, 60, 400);
    }

    public static FeatureDef Transformer()
    {
        var m = new Mesh();
        m.Box(V(-30, 0, -24), V(30, 44, 24), Region.Plate, Region.Graphite);
        foreach (int s in Sides) m.Quad(V(-26, 40, s * 24.3f), V(26, 40, s * 24.3f), V(26, 6, s * 24.3f), V(-26, 6, s * 24.3f), V(0, 0, s), Region.Vent, stretch: true);
        foreach (float x in new[] { -14f, 0f, 14f }) m.Tube(V(x, 44, 0), V(x, 64, 0), 4, 2.5f, Region.PlateLight, 8, cap1: Region.Chrome);
        m.Quad(V(30.3f, 40, -18), V(30.3f, 40, 18), V(30.3f, 30, 18), V(30.3f, 30, -18), Vector3.UnitX, Region.Hazard, stretch: true);
        return new FeatureDef("sm_transformer", "Starship Meridian: a transformer", Features.Centred(m), false, 50, 200);
    }

    public static FeatureDef Pipes()
    {
        var m = new Mesh();
        foreach (float x in new[] { -96f, 0f, 96f }) m.Box(V(x - 4, 0, -16), V(x + 4, 14, 16), Region.Graphite, Region.Graphite);
        foreach (var (z, y, r) in new[] { (-9f, 22f, 7f), (9f, 22f, 7f), (0f, 34f, 5f) })
            m.Tube(V(-128, y, z), V(128, y, z), r, r, Region.Pipe, 10);
        return new FeatureDef("sm_pipes", "Starship Meridian: a pipe run", m, true, 0, Blocking: false);
    }

    public static FeatureDef ShieldEmitter()
    {
        var m = new Mesh();
        m.Tube(V(0, 0, 0), V(0, 20, 0), 150, 140, Region.Graphite, 24, cap1: Region.Plate);
        m.Tube(V(0, 20, 0), V(0, 26, 0), 120, 120, Region.Glow, 24, cap1: Region.Grate);
        for (int k = 0; k < 3; k++)
        {
            double a = k * 2 * Math.PI / 3;
            var d = V(Math.Cos(a), 0, Math.Sin(a));
            m.Beam(d * 120 + V(0, 20, 0), d * 70 + V(0, 150, 0), 9, 7, Region.Plate);
            m.Beam(d * 70 + V(0, 150, 0), d * 40 + V(0, 196, 0), 6, 5, Region.Graphite);
            m.Tube(d * 40 + V(0, 196, 0), d * 30 + V(0, 196, 0), 10, 6, Region.Copper, 8);
        }
        m.Blob(V(0, 170, 0), V(56, 56, 56), Region.Energy, 61, 0f, 2);
        m.Tube(V(0, 166, 0), V(0, 174, 0), 72, 72, Region.Plate, 24, cap0: Region.Graphite, cap1: Region.Graphite);
        return new FeatureDef("sm_shield", "Starship Meridian: the shield emitter", m, true, 0, Base: (18, 18));
    }

    // ---- The engines.

    public static FeatureDef DriveCore()
    {
        var m = new Mesh();
        foreach (float x in new[] { -150f, 0f, 150f }) foreach (int s in Sides) m.Box(V(x - 14, 0, s * 70 - 10), V(x + 14, 60, s * 70 + 10), Region.Graphite, Region.Graphite);
        m.Tube(V(-200, 110, 0), V(200, 110, 0), 86, 86, Region.Plate, 20, cap0: Region.Graphite, cap1: Region.Graphite);
        for (float x = -170; x <= 170; x += 68)
            m.Tube(V(x, 110, 0), V(x + 16, 110, 0), 96, 96, Region.Graphite, 20, cap0: Region.Dark, cap1: Region.Dark);
        foreach (float x in new[] { -120f, -52f, 16f, 84f })
            m.Tube(V(x + 14, 110, 0), V(x + 40, 110, 0), 87, 87, Region.Energy, 20);
        m.Tube(V(200, 110, 0), V(230, 110, 0), 60, 40, Region.Copper, 16, cap1: Region.Glow);
        return new FeatureDef("sm_drivecore", "Starship Meridian: the drive core", m, true, 0, Base: (27, 12));
    }

    public static FeatureDef CoolantTower()
    {
        var m = new Mesh();
        m.Tube(V(0, 0, 0), V(0, 210, 0), 50, 44, Region.Plate, 16, cap1: Region.Vent);
        for (float y = 30; y < 210; y += 45) m.Tube(V(0, y, 0), V(0, y + 6, 0), 54, 54, Region.Hazard, 16, cap0: Region.Graphite, cap1: Region.Graphite);
        foreach (int s in Sides) m.Tube(V(0, 180, s * 46), V(0, 20, s * 74), 7, 7, Region.Pipe, 8);
        m.Box(V(-4, 210, -4), V(4, 222, 4), Region.GlowWarm, Region.GlowWarm);
        return new FeatureDef("sm_coolant", "Starship Meridian: a coolant tower", Features.Centred(m), true, 0);
    }

    public static FeatureDef EngineBell()
    {
        var m = new Mesh();
        // Mounted on its housing's aft end, the bell flaring out aft (+x) over space.
        m.Box(V(-120, 0, -150), V(0, 150, 150), Region.Plate, Region.Graphite);
        m.Tube(V(0, 140, 0), V(60, 140, 0), 110, 110, Region.Graphite, 24, cap0: Region.Dark);
        m.Tube(V(60, 140, 0), V(330, 140, 0), 110, 210, Region.Plate, 24);
        m.Tube(V(60, 140, 0), V(328, 140, 0), 100, 200, Region.EngineBell, 24, inside: true);
        m.Tube(V(330, 140, 0), V(340, 140, 0), 214, 214, Region.Graphite, 24);
        return new FeatureDef("sm_enginebell", "Starship Meridian: a main engine", m, true, 0, Blocking: false);
    }

    public static FeatureDef FuelTank()
    {
        var m = new Mesh();
        foreach (float x in new[] { -40f, 40f }) m.Box(V(x - 8, 0, -22), V(x + 8, 16, 22), Region.Graphite, Region.Graphite);
        m.Tube(V(-64, 38, 0), V(64, 38, 0), 26, 26, Region.PlateLight, 16, cap0: Region.Plate, cap1: Region.Plate);
        foreach (float x in new[] { -30f, 30f }) m.Tube(V(x, 38, 0), V(x + 4, 38, 0), 27, 27, Region.Hazard, 16);
        m.Box(V(64, 30, -6), V(76, 46, 6), Region.Graphite, Region.Pipe);
        return new FeatureDef("sm_fueltank", "Starship Meridian: a fuel tank", Features.Centred(m), false, 80, 600);
    }

    // ---- The crew deck.

    public static FeatureDef Bunks()
    {
        var m = new Mesh();
        foreach (float x in new[] { -30f, 0f, 30f })
        {
            foreach (int a in Sides) foreach (int b in Sides) m.Beam(V(x + a * 12, 0, b * 22), V(x + a * 12, 38, b * 22), 1.2f, 1.2f, Region.Chrome);
            foreach (float y in new[] { 10f, 28f }) m.Box(V(x - 12, y, -22), V(x + 12, y + 5, 22), Region.Graphite, Region.Fabric);
        }
        return new FeatureDef("sm_bunks", "Starship Meridian: crew bunks", Features.Centred(m), false, 30);
    }

    public static FeatureDef MessTable()
    {
        var m = new Mesh();
        m.Box(V(-48, 14, -12), V(48, 17, 12), Region.Chrome, Region.PlateLight);
        foreach (float x in new[] { -40f, 40f }) m.Box(V(x - 2, 0, -2), V(x + 2, 14, 2), Region.Chrome, Region.Chrome);
        foreach (int s in Sides) m.Box(V(-46, 8, s * 22 - 5), V(46, 11, s * 22 + 5), Region.Graphite, Region.Orange);
        return new FeatureDef("sm_messtable", "Starship Meridian: a mess table", Features.Centred(m), false, 20);
    }

    public static FeatureDef Lockers()
    {
        var m = new Mesh();
        m.Box(V(-10, 0, -40), V(10, 44, 40), Region.Plate, Region.Graphite);
        for (float z = -30; z <= 30; z += 20)
        {
            m.Quad(V(10.3f, 40, z - 9), V(10.3f, 40, z + 9), V(10.3f, 4, z + 9), V(10.3f, 4, z - 9), Vector3.UnitX, Region.PlateLight, stretch: true);
            m.Quad(V(10.6f, 36, z - 6), V(10.6f, 36, z + 6), V(10.6f, 32, z + 6), V(10.6f, 32, z - 6), Vector3.UnitX, Region.Vent, stretch: true);
        }
        return new FeatureDef("sm_lockers", "Starship Meridian: lockers", Features.Centred(m), false, 20);
    }

    public static FeatureDef MedPod()
    {
        var m = new Mesh();
        m.Box(V(-34, 0, -16), V(34, 14, 16), Region.PlateLight, Region.Plate);
        m.Tube(V(-30, 18, 0), V(30, 18, 0), 14, 14, Region.Window, 14, cap0: Region.PlateLight, cap1: Region.PlateLight);
        m.Seam(V(-30, 14.5f, 16.5f), V(30, 14.5f, 16.5f), Vector3.UnitZ, 3, Region.Glow);
        m.Seam(V(-30, 14.5f, -16.5f), V(30, 14.5f, -16.5f), -Vector3.UnitZ, 3, Region.Glow);
        m.Box(V(34, 0, -8), V(42, 30, 8), Region.Graphite, Region.Screen);
        return new FeatureDef("sm_medpod", "Starship Meridian: a medical pod", Features.Centred(m), false, 40);
    }

    public static FeatureDef Planter()
    {
        var m = new Mesh();
        m.Box(V(-50, 0, -18), V(50, 16, 18), Region.PlateLight, Region.Soil);
        m.Plant++;
        for (int i = 0; i < 6; i++) m.Blob(V(-42 + i * 17, 22, 0), V(10, 10, 14), Region.Leaves, 70 + i, 0.3f, 1, cutY: 14);
        m.Plant--;
        foreach (float x in new[] { -46f, 46f }) m.Beam(V(x, 16, 0), V(x, 54, 0), 1.6f, 1.6f, Region.Chrome);
        m.Box(V(-48, 54, -6), V(48, 58, 6), Region.Graphite, Region.Graphite);
        m.Quad(V(-46, 53.7f, -5), V(46, 53.7f, -5), V(46, 53.7f, 5), V(-46, 53.7f, 5), -Vector3.UnitY, Region.GlowWarm, stretch: true);
        return new FeatureDef("sm_planter", "Starship Meridian: a hydroponics planter", Features.Centred(m), false, 20, 100);
    }

    // ---- The armory, the cargo hold, the machine shop.

    public static FeatureDef WeaponRack()
    {
        var m = new Mesh();
        m.Box(V(-8, 0, -40), V(8, 8, 40), Region.Graphite, Region.Graphite);
        m.Box(V(-10, 8, -40), V(-4, 46, 40), Region.Graphite, Region.Plate);
        for (int i = 0; i < 8; i++)
        {
            float z = -34 + i * 68f / 7;
            m.Beam(V(-2, 10, z), V(-2, 42, z), 1.6f, 1.2f, Region.Dark);
            m.Box(V(-4, 24, z - 2), V(2, 30, z + 2), Region.Dark, Region.Dark);
        }
        return new FeatureDef("sm_weaponrack", "Starship Meridian: a weapon rack", Features.Centred(m), false, 40);
    }

    public static FeatureDef Munitions()
    {
        var m = new Mesh();
        m.Box(V(-30, 0, -30), V(30, 4, 30), Region.Graphite, Region.Graphite);
        foreach (int a in Sides) foreach (int b in Sides) m.Box(V(a > 0 ? 2 : -28, 4, b > 0 ? 2 : -28), V(a > 0 ? 28 : -2, 22, b > 0 ? 28 : -2), Region.Crate, Region.Crate);
        m.Box(V(-14, 22, -14), V(14, 38, 14), Region.Crate, Region.Hazard);
        return new FeatureDef("sm_munitions", "Starship Meridian: munitions crates", Features.Centred(m), false, 80);
    }

    public static FeatureDef MissileCart()
    {
        var m = new Mesh();
        foreach (int s in Sides) foreach (float x in new[] { -30f, 30f }) Wheel(m, V(x, 6, s * 18), 6, 5);
        m.Box(V(-42, 6, -15), V(42, 12, 15), Region.Yellow, Region.Graphite);
        foreach (var (z, y) in new[] { (-9f, 20f), (9f, 20f), (0f, 34f) })
        {
            m.Tube(V(-40, y, z), V(30, y, z), 7, 7, Region.PlateLight, 10, cap0: Region.Graphite);
            m.Tube(V(30, y, z), V(46, y, z), 7, 1, Region.Orange, 10);
        }
        return new FeatureDef("sm_missilecart", "Starship Meridian: a missile cart", Features.Centred(m), false, 100);
    }

    public static FeatureDef Container(int k)
    {
        var m = new Mesh();
        var paint = k == 0 ? Region.ContainerRed : Region.ContainerBlue;
        m.Box(V(-70, 0, -28), V(70, 56, 28), paint, paint);
        foreach (int s in Sides) foreach (int e in Sides) m.Box(V(e * 70 - 3, 0, s * 28 - 3), V(e * 70 + 3, 56, s * 28 + 3), Region.Dark, Region.Dark);
        m.Quad(V(70.4f, 52, -24), V(70.4f, 52, 24), V(70.4f, 4, 24), V(70.4f, 4, -24), Vector3.UnitX, Region.Marking, stretch: true);
        return new FeatureDef($"sm_container{k}", "Starship Meridian: a cargo container", Features.Centred(m), false, 60);
    }

    public static FeatureDef Loader()
    {
        var m = new Mesh();
        foreach (int s in Sides) foreach (float x in new[] { -18f, 20f }) Wheel(m, V(x, 9, s * 18), 9, 7);
        m.Box(V(-30, 9, -14), V(26, 30, 14), Region.Yellow, Region.Yellow);
        m.Box(V(-26, 30, -14), V(0, 62, 14), Region.Graphite, Region.Yellow);
        m.Quad(V(0.3, 60, -12), V(0.3, 60, 12), V(0.3, 36, 12), V(0.3, 36, -12), Vector3.UnitX, Region.Window, stretch: true);
        // The mast and its forks.
        foreach (int s in Sides) m.Beam(V(30, 2, s * 10), V(30, 80, s * 10), 2.5f, 2.5f, Region.Graphite);
        m.Box(V(28, 20, -14), V(34, 28, 14), Region.Graphite, Region.Graphite);
        foreach (int s in Sides) m.Box(V(34, 2, s * 9 - 2), V(62, 6, s * 9 + 2), Region.Chrome, Region.Chrome);
        m.Box(V(-26, 62, -10), V(-18, 65, 10), Region.Dark, Region.GlowWarm);
        return new FeatureDef("sm_loader", "Starship Meridian: a cargo loader", Features.Centred(m), false, 120);
    }

    public static FeatureDef Pallet()
    {
        var m = new Mesh();
        m.Box(V(-26, 0, -26), V(26, 5, 26), Region.Graphite, Region.Grate);
        m.Box(V(-22, 5, -22), V(0, 26, 0), Region.Crate, Region.Crate);
        m.Box(V(2, 5, -22), V(22, 20, 0), Region.Crate, Region.Crate);
        m.Box(V(-22, 5, 2), V(22, 18, 22), Region.Crate, Region.Marking);
        return new FeatureDef("sm_pallet", "Starship Meridian: a loaded pallet", Features.Centred(m), false, 30);
    }

    public static FeatureDef RepairGantry()
    {
        var m = new Mesh();
        foreach (int s in Sides)
        {
            m.Box(V(-50, 0, s * 110 - 12), V(50, 10, s * 110 + 12), Region.Graphite, Region.Hazard);
            m.Beam(V(-40, 10, s * 110), V(-40, 170, s * 110), 7, 7, Region.Yellow);
            m.Beam(V(40, 10, s * 110), V(40, 170, s * 110), 7, 7, Region.Yellow);
        }
        foreach (float x in new[] { -40f, 40f }) m.Beam(V(x, 170, -116), V(x, 170, 116), 8, 8, Region.Yellow);
        m.Box(V(-12, 140, -40), V(12, 162, 40), Region.Graphite, Region.Graphite);                    // the trolley
        foreach (int s in Sides)
        {
            m.Beam(V(0, 140, s * 30), V(30, 90, s * 46), 4, 4, Region.Orange);
            m.Beam(V(30, 90, s * 46), V(50, 60, s * 30), 3, 3, Region.Orange);
            m.Box(V(46, 52, s * 30 - 5), V(56, 62, s * 30 + 5), Region.Dark, Region.GlowWarm);
        }
        return new FeatureDef("sm_repairgantry", "Starship Meridian: a repair gantry", Features.Centred(m), true, 0);
    }

    public static FeatureDef Bench()
    {
        var m = new Mesh();
        m.Box(V(-14, 26, -40), V(14, 30, 40), Region.Graphite, Region.Plate);
        foreach (int a in Sides) foreach (int b in Sides) m.Box(V(a * 12 - 2, 0, b * 36 - 2), V(a * 12 + 2, 26, b * 36 + 2), Region.Graphite, Region.Graphite);
        m.Box(V(-14, 30, -40), V(-10, 56, 40), Region.Graphite, Region.Grate);                        // the tool board
        m.Box(V(-6, 30, -20), V(6, 38, -6), Region.Orange, Region.Orange);
        m.Box(V(-4, 30, 8), V(8, 36, 24), Region.Chrome, Region.Chrome);
        return new FeatureDef("sm_bench", "Starship Meridian: a tool bench", Features.Centred(m), false, 30);
    }

    public static FeatureDef Crate()
    {
        var m = new Mesh();
        m.Box(V(-16, 0, -16), V(16, 30, 16), Region.Crate, Region.Crate);
        return new FeatureDef("sm_crate", "Starship Meridian: a supply crate", Features.Centred(m), false, 20);
    }

    // ---- The hull.

    public static FeatureDef Turret()
    {
        var m = new Mesh();
        m.Tube(V(0, 0, 0), V(0, 24, 0), 42, 38, Region.Plate, 16, cap1: Region.Graphite);
        m.Box(V(-30, 24, -26), V(20, 52, 26), Region.Graphite, Region.Plate);
        m.Quad(V(20.3f, 48, -20), V(20.3f, 48, 20), V(20.3f, 30, 20), V(20.3f, 30, -20), Vector3.UnitX, Region.Hazard, stretch: true);
        foreach (int s in Sides)
        {
            m.Tube(V(20, 40, s * 11), V(110, 44, s * 11), 5, 4, Region.Graphite, 10, cap1: Region.Dark);
            m.Tube(V(20, 40, s * 11), V(44, 41, s * 11), 7, 7, Region.Plate, 10);
        }
        return new FeatureDef("sm_turret", "Starship Meridian: a point-defence turret", Features.Centred(m), true, 0, Base: (5, 5));
    }

    public static FeatureDef Antenna()
    {
        var m = new Mesh();
        m.Box(V(-14, 0, -14), V(14, 10, 14), Region.Plate, Region.Graphite);
        m.Tube(V(0, 10, 0), V(0, 220, 0), 4, 2, Region.Chrome, 8);
        foreach (float y in new[] { 120f, 160f, 196f }) m.Beam(V(0, y, -30 + y / 10), V(0, y, 30 - y / 10), 1.2f, 1.2f, Region.Chrome);
        m.Box(V(-3, 220, -3), V(3, 226, 3), Region.NavRed, Region.NavRed);
        return new FeatureDef("sm_antenna", "Starship Meridian: an antenna mast", Features.Centred(m), true, 0);
    }

    public static FeatureDef Dish()
    {
        var m = new Mesh();
        m.Tube(V(0, 0, 0), V(0, 50, 0), 14, 10, Region.Plate, 10, cap1: Region.Graphite);
        // The dish, tilted up toward +x.
        var dish = new Mesh();
        dish.Tube(V(0, 0, 0), V(26, 0, 0), 6, 64, Region.PlateLight, 20);
        dish.Tube(V(0.5, 0, 0), V(25, 0, 0), 5, 60, Region.Graphite, 20, inside: true);
        dish.Beam(V(0, 0, 0), V(56, 0, 0), 1.5f, 1.5f, Region.Chrome);
        m.Add(dish, Matrix4x4.CreateRotationZ(0.7f) * Matrix4x4.CreateTranslation(0, 56, 0));
        return new FeatureDef("sm_dish", "Starship Meridian: a comm dish", Features.Centred(m), true, 0);
    }

    public static List<FeatureDef> All() =>
    [
        Dropship(), Fighter(), Shuttle(), Tug(), FuelCart(), ServiceTower(),
        Console(), Chair(), HoloTable(), Viewport(), SensorDome(),
        ReactorCore(), Capacitor(), Transformer(), Pipes(), ShieldEmitter(),
        DriveCore(), CoolantTower(), EngineBell(), FuelTank(),
        Bunks(), MessTable(), Lockers(), MedPod(), Planter(),
        WeaponRack(), Munitions(), MissileCart(), Container(0), Container(1), Loader(), Pallet(), RepairGantry(), Bench(), Crate(),
        Turret(), Antenna(), Dish(),
    ];
}
