// The mine's big machines and the outback's plants and rocks (the railway is in ModelsRail.cs, the site's tanks,
// buildings, smaller machines and clutter in ModelsSite.cs). Machines face +x (their front), y up, z across; each is
// built round the middle of its base, and the excavators keep their origin there (their booms and wheel overhang
// what's beneath, so their footprint is only their base). Paint is mining yellow and orange, the bucket wheel
// excavator grey steel; no makers' marks. Everything carries red dust.
using System.Numerics;

static partial class Models
{
    static Vector3 V(double x, double y, double z) => new((float)x, (float)y, (float)z);

    /// <summary>A wheel on an axle along z: tread round it, wheel faces both sides.</summary>
    static void Wheel(Mesh m, Vector3 c, float r, float width, int sides = 14)
    {
        m.Tube(c - V(0, 0, width / 2), c + V(0, 0, width / 2), r, r, Region.Tyre, sides, cap0: Region.Rubber, cap1: Region.Rubber, stretch: true);
    }

    /// <summary>A handrail along a line: posts every 10 or so and a top rail, 10 high.</summary>
    static void Rail(Mesh m, Vector3 a, Vector3 b, float height = 10, Region r = Region.Yellow)
    {
        m.Plant++;   // thin rails don't count in the solid box
        int n = Math.Max(1, (int)((b - a).Length() / 12));
        for (int i = 0; i <= n; i++) { var p = Vector3.Lerp(a, b, i / (float)n); m.Beam(p, p + V(0, height, 0), 0.9f, 0.9f, r); }
        m.Beam(a + V(0, height, 0), b + V(0, height, 0), 1f, 1f, r);
        m.Beam(a + V(0, height / 2, 0), b + V(0, height / 2, 0), 0.8f, 0.8f, r);
        m.Plant--;
    }

    /// <summary>Stairs from a to b (rails and treads), width across z.</summary>
    static void Stairs(Mesh m, Vector3 a, Vector3 b, float width)
    {
        var side = V(0, 0, width / 2);
        m.Beam(a - side, b - side, 1.2f, 1.6f, Region.Dark);
        m.Beam(a + side, b + side, 1.2f, 1.6f, Region.Dark);
        int n = Math.Max(2, (int)((b - a).Length() / 7));
        for (int i = 1; i < n; i++) { var p = Vector3.Lerp(a, b, i / (float)n); m.Beam(p - side, p + side, 1.2f, 0.6f, Region.Steel); }
        Rail(m, a - side, b - side, 9, Region.Yellow);
    }

    // ---- The haul truck: a dump body with its canopy over the cab, six big tyres, the deck and its stairs.

    public enum Load { Empty, Loaded, Tipping }

    /// <summary>A haul truck without its body: running gear, engine, deck, cab, tanks (the water cart's too).</summary>
    static Mesh HaulChassis()
    {
        var m = new Mesh { DirtLine = 10 };
        // Running gear.
        foreach (float z in new[] { -37f, 37f }) Wheel(m, V(44, 19, z), 19, 13);
        foreach (float z in new[] { -37f, -23f, 23f, 37f }) Wheel(m, V(-38, 19, z), 19, 13);
        m.Box(V(-64, 15, -18), V(64, 28, 18), Region.Dark, Region.Dark);
        m.Box(V(-48, 12, -29), V(-28, 26, 29), Region.Dark, Region.Dark);
        m.Box(V(34, 14, -31), V(54, 24, 31), Region.Dark, Region.Dark);
        // The engine and radiator up front, the bumper, the headlights.
        m.Box(V(48, 22, -24), V(74, 47, 24), Region.Yellow, Region.Yellow);
        m.Quad(V(74.2, 44, -18), V(74.2, 44, 18), V(74.2, 25, 18), V(74.2, 25, -18), Vector3.UnitX, Region.Grille, stretch: true);
        m.Box(V(70, 12, -32), V(79, 21, 32), Region.Dark, Region.Hazard);
        foreach (float z in new[] { -34f, 28f })
        {
            m.Box(V(70, 34, z), V(77, 42, z + 7), Region.Dark, Region.Dark);
            m.Quad(V(77.2, 41, z + 0.6f), V(77.2, 41, z + 6.4f), V(77.2, 35, z + 6.4f), V(77.2, 35, z + 0.6f), Vector3.UnitX, Region.Lamp, stretch: true);
        }
        // The deck across the front, its rails and the stairs up to it; the cab on the left.
        m.Box(V(38, 46, -45), V(77, 49, 45), Region.Yellow, Region.Steel);
        Rail(m, V(76, 49, -44), V(76, 49, 12));
        Rail(m, V(40, 49, -44), V(76, 49, -44));
        Stairs(m, V(82, 2, -36), V(64, 48, -36), 9);
        m.Box(V(44, 49, 14), V(67, 69, 42), Region.Yellow, Region.Yellow);
        m.Quad(V(67.2, 67, 15), V(67.2, 67, 41), V(67.2, 56, 41), V(67.2, 56, 15), Vector3.UnitX, Region.Window, stretch: true);
        m.Quad(V(46, 67, 42.2), V(65, 67, 42.2), V(65, 56, 42.2), V(46, 56, 42.2), Vector3.UnitZ, Region.Window, stretch: true);
        m.Quad(V(65, 67, 13.8), V(46, 67, 13.8), V(46, 56, 13.8), V(65, 56, 13.8), -Vector3.UnitZ, Region.Window, stretch: true);
        m.Box(V(20, 22, 36), V(40, 40, 46), Region.Yellow, Region.YellowDust);                      // fuel tank
        m.Box(V(20, 22, -46), V(40, 38, -36), Region.Dark, Region.Grille);                          // hydraulic tank
        return m;
    }

    public static FeatureDef Truck(Load load)
    {
        var m = HaulChassis();
        // The dump body: walls, a floor rising to the tail, the canopy over the cab, ribs down its sides.
        var body = new Mesh { DirtLine = -1e9f };
        foreach (int side in new[] { 1, -1 })
        {
            body.Slab(V(0, 0, side > 0 ? 40 : -44), Vector3.UnitX, Vector3.UnitY, [new(-80, 40), new(-76, 70), new(36, 70), new(36, 31), new(-56, 31)], 4, Region.Yellow, Region.Yellow);
            body.Quad(V(-76, 69, side * 39.8f), V(35, 69, side * 39.8f), V(35, 33, side * 39.8f), V(-58, 33, side * 39.8f), V(0, 0, -side), Region.Bucket);
            foreach (float x in new[] { -60f, -36f, -12f, 12f })
                body.Beam(V(x, 32, side * 45), V(x - 2, 70, side * 45), 2.2f, 1.6f, Region.Yellow);
            body.Beam(V(-78, 70, side * 45), V(36, 70, side * 45), 1.8f, 2.2f, Region.Yellow);
        }
        body.Quad(V(-78, 42, -40), V(-78, 42, 40), V(34, 33, 40), V(34, 33, -40), Vector3.UnitY, Region.Bucket);
        body.Quad(V(-78, 39, 40), V(-78, 39, -40), V(34, 30, -40), V(34, 30, 40), -Vector3.UnitY, Region.Yellow);
        body.Box(V(-84, 36, -44), V(-76, 44, 44), Region.Yellow, Region.Yellow);
        body.Box(V(34, 30, -45), V(38, 73, 45), Region.Yellow, Region.Yellow);
        body.Quad(V(33.8, 72, -40), V(33.8, 72, 40), V(33.8, 34, 40), V(33.8, 34, -40), -Vector3.UnitX, Region.Bucket);
        body.Box(V(36, 71, -47), V(78, 75, 47), Region.Yellow, Region.Yellow);
        for (float z = -40; z <= 40; z += 20) body.Beam(V(38, 75, z), V(77, 75, z), 1.4f, 1.6f, Region.Yellow);
        if (load == Load.Loaded)
            body.Blob(V(-18, 50, 0), V(52, 22, 34), Region.Ore, 41, 0.18f, 2, cutY: 44);
        if (load == Load.Tipping)
        {
            // Raised about its hinge at the tail, the hoist rams out, the load spilled behind.
            var pivot = V(-58, 31, 0);
            var turn = Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateRotationZ(0.95f) * Matrix4x4.CreateTranslation(pivot);
            m.Add(body, turn);
            var under = Vector3.Transform(V(8, 30, 0), turn);
            foreach (float z in new[] { -22f, 22f })
                m.Tube(V(16, 26, z), under with { Z = z }, 4.2f, 3.2f, Region.Chrome, 8);
            m.Blob(V(-112, 0, 0), V(42, 20, 40), Region.Ore, 43, 0.25f, 2, cutY: 0);
        }
        else
        {
            m.Add(body, Matrix4x4.Identity);
            foreach (float z in new[] { -22f, 22f }) m.Tube(V(16, 26, z), V(8, 31, z), 4.2f, 4.2f, Region.Chrome, 8);
        }
        string name = load switch { Load.Loaded => "oc_truck_loaded", Load.Tipping => "oc_truck_tipping", _ => "oc_truck" };
        string what = load switch { Load.Loaded => "a haul truck, loaded", Load.Tipping => "a haul truck, tipping", _ => "a haul truck" };
        return new FeatureDef(name, "Red Dust Open Cut: " + what, Features.Centred(m), false, 300);
    }

    // ---- The hydraulic excavator: crawlers, the house with its cab and counterweight, the boom, stick and bucket.

    public static FeatureDef Excavator(bool raised)
    {
        var m = new Mesh { DirtLine = 6 };
        foreach (int side in new[] { 1, -1 })
        {
            float zc = side * 52;
            m.Box(V(-62, 4, zc - 14), V(62, 30, zc + 14), Region.Track, Region.Track);
            foreach (float x in new[] { -62f, 62f })
                m.Tube(V(x, 17, zc - 14), V(x, 17, zc + 14), 13, 13, Region.Track, 10, cap0: Region.Dark, cap1: Region.Dark);
            for (float x = -50; x <= 50; x += 25) m.Tube(V(x, 4, zc - 16), V(x, 4, zc + 16), 4, 4, Region.Dark, 6);
        }
        m.Box(V(-40, 16, -38), V(40, 36, 38), Region.Dark, Region.Dark);
        m.Tube(V(0, 36, 0), V(0, 44, 0), 40, 40, Region.Dark, 16, cap1: Region.Dark);
        // The house.
        m.Box(V(-72, 44, -48), V(42, 92, 48), Region.Orange, Region.Orange);
        m.Plate(V(-82, 64, 0), V(10, 0, 0), V(0, 22, 0), V(0, 0, 47), Region.Orange, Region.Orange);
        m.Quad(V(-92.2, 84, -46), V(-92.2, 84, 46), V(-92.2, 70, 46), V(-92.2, 70, -46), -Vector3.UnitX, Region.Hazard, stretch: true);
        foreach (int side in new[] { 1, -1 })
            m.Quad(V(-60, 86, side * 48.2f), V(-10, 86, side * 48.2f), V(-10, 58, side * 48.2f), V(-60, 58, side * 48.2f), V(0, 0, side), Region.Grille, stretch: true);
        m.Box(V(-50, 92, -30), V(-14, 104, 10), Region.Orange, Region.Grille);                        // engine hood
        m.Tube(V(-30, 104, -20), V(-30, 120, -20), 3, 3, Region.Dark, 6);                              // exhaust
        m.Box(V(14, 92, 20), V(46, 126, 47), Region.Orange, Region.Orange);                          // cab
        m.Quad(V(46.2, 123, 21), V(46.2, 123, 46), V(46.2, 102, 46), V(46.2, 102, 21), Vector3.UnitX, Region.Window, stretch: true);
        m.Quad(V(16, 123, 47.2), V(44, 123, 47.2), V(44, 102, 47.2), V(16, 102, 47.2), Vector3.UnitZ, Region.Window, stretch: true);
        Rail(m, V(-70, 92, -47), V(10, 92, -47), 9, Region.Yellow);
        Rail(m, V(-70, 92, 47), V(12, 92, 47), 9, Region.Yellow);
        Stairs(m, V(52, 30, -42), V(36, 92, -42), 8);
        // The boom, stick and bucket: digging at a face, or held up.
        Vector3 pivot = V(38, 78, 0), elbow = V(150, 192, 0), tip = raised ? V(218, 170, 0) : V(232, 66, 0);
        m.Beam(pivot, elbow - V(12, 6, 0), 15, 16, Region.Orange);
        m.Beam(elbow - V(14, 8, 0), elbow + V(26, 0, 0), 14, 15, Region.Orange);
        var stickTop = elbow + V(20, 0, 0);
        m.Beam(stickTop, tip, 11, 12, Region.Orange);
        foreach (float z in new[] { -17f, 17f })
            m.Tube(V(44, 52, z), V(104, 146, z), 6, 4.5f, Region.Chrome, 8);
        m.Tube(elbow - V(30, -22, 0), stickTop + V(4, 18, 0), 5.5f, 4, Region.Chrome, 8);
        // The bucket: side plates, a curved back, teeth along the lip.
        var dir = Vector3.Normalize(tip - stickTop);
        var bk = new Mesh { DirtLine = -1e9f };
        foreach (int side in new[] { 1, -1 })
            bk.Slab(V(0, 0, side > 0 ? 24 : -28), Vector3.UnitX, Vector3.UnitY, [new(-8, 8), new(34, 14), new(46, -26), new(26, -44), new(-6, -26)], 4, Region.Orange, Region.Orange);
        bk.Quad(V(-8, 8, -24), V(-8, 8, 24), V(-6, -26, 24), V(-6, -26, -24), -Vector3.UnitX, Region.Orange);
        bk.Quad(V(-6, -26, -24), V(-6, -26, 24), V(26, -44, 24), V(26, -44, -24), V(-1, -1, 0), Region.Orange);
        bk.Quad(V(-5.5f, 7, -24), V(-5.5f, 7, 24), V(-4, -24, 24), V(-4, -24, -24), Vector3.UnitX, Region.Bucket);
        for (float z = -20; z <= 20; z += 8) bk.Block(V(30, -44, z), V(5, -2, 0), V(1.5f, 3, 0), V(0, 0, 2.5f), Region.Steel);
        float ang = MathF.Atan2(dir.Y, dir.X) + MathF.PI / 2.4f;
        m.Add(bk, Matrix4x4.CreateRotationZ(raised ? ang - 1.2f : ang) * Matrix4x4.CreateTranslation(tip));
        var def = new FeatureDef(raised ? "oc_excavator_idle" : "oc_excavator", "Red Dust Open Cut: a hydraulic excavator", m, false, 600, Base: (9, 8));
        return def;
    }

    // ---- The bucket wheel excavator: crawlers, a turntable, the house, two lattice masts with stays, the lattice boom
    // and its wheel of buckets, the counterweight boom, and the discharge conveyor under it.

    /// <summary>A lattice girder from a to b, square in section (half-size h): four chords, posts and diagonals every bay.</summary>
    static void Truss(Mesh m, Vector3 a, Vector3 b, float h, float bay, float chord = 4f, float brace = 2.6f, Region r = Region.Steel)
    {
        var along = b - a;
        float len = along.Length();
        var x = along / len;
        var z = Vector3.UnitZ;
        var y = Vector3.Normalize(Vector3.Cross(z, x));
        Vector3[] corner = [y * h + z * h, y * h - z * h, -y * h - z * h, -y * h + z * h];
        foreach (var c in corner) m.Beam(a + c, b + c, chord, chord, r);
        int n = Math.Max(1, (int)MathF.Round(len / bay));
        for (int i = 0; i <= n; i++)
        {
            var p = a + along * (i / (float)n);
            for (int k = 0; k < 4; k++) m.Beam(p + corner[k], p + corner[(k + 1) % 4], brace, brace, r);
            if (i == n) break;
            var q = a + along * ((i + 1) / (float)n);
            for (int k = 0; k < 4; k++) m.Beam(p + corner[k], q + corner[(k + 1) % 4], brace * 0.9f, brace * 0.9f, r);
        }
    }

    /// <summary>A lattice mast: four legs tapering from a base to a top, braced in bays.</summary>
    static void Mast(Mesh m, Vector3 baseC, float baseHalfX, float baseHalfZ, float topY, float topHalfX, float topHalfZ, int levels)
    {
        Vector3 C(int k, float t)
        {
            float hx = baseHalfX + (topHalfX - baseHalfX) * t, hz = baseHalfZ + (topHalfZ - baseHalfZ) * t;
            return baseC + V((k == 0 || k == 3 ? hx : -hx), (topY - baseC.Y) * t, (k < 2 ? hz : -hz));
        }
        for (int k = 0; k < 4; k++) m.Beam(C(k, 0), C(k, 1), 5f, 5f, Region.Steel);
        for (int l = 0; l <= levels; l++)
        {
            float t = l / (float)levels;
            for (int k = 0; k < 4; k++) m.Beam(C(k, t), C((k + 1) % 4, t), 3f, 3f, Region.Steel);
            if (l == levels) break;
            float t2 = (l + 1) / (float)levels;
            for (int k = 0; k < 4; k++) m.Beam(C(k, t), C((k + 1) % 4, t2), 2.4f, 2.4f, Region.Steel);
        }
        m.Box(baseC + V(-topHalfX - 6, topY - baseC.Y, -topHalfZ - 6), baseC + V(topHalfX + 6, topY - baseC.Y + 10, topHalfZ + 6), Region.Steel, Region.SteelDust);
    }

    public static FeatureDef BucketWheel()
    {
        var m = new Mesh { DirtLine = 8 };
        // Crawlers and the chassis.
        foreach (float x in new[] { -88f, 88f })
            foreach (float z in new[] { -76f, 76f })
            {
                m.Box(V(x - 58, 2, z - 24), V(x + 58, 36, z + 24), Region.Track, Region.Track);
                foreach (float xe in new[] { x - 58, x + 58 })
                    m.Tube(V(xe, 19, z - 24), V(xe, 19, z + 24), 17, 17, Region.Track, 10, cap0: Region.Dark, cap1: Region.Dark);
            }
        m.Box(V(-120, 30, -96), V(120, 52, 96), Region.Steel, Region.SteelDust);
        // The turntable, stacked.
        m.Tube(V(0, 52, 0), V(0, 74, 0), 104, 104, Region.Steel, 24, cap1: Region.SteelDust);
        m.Tube(V(0, 74, 0), V(0, 112, 0), 82, 82, Region.Steel, 24);
        m.Tube(V(0, 112, 0), V(0, 126, 0), 94, 94, Region.Dark, 24, cap1: Region.Dark);
        // The house and its deck.
        m.Box(V(-150, 126, -72), V(150, 150, 72), Region.Steel, Region.SteelDust);
        m.Box(V(-130, 150, -62), V(30, 204, 62), Region.Corrugated, Region.Steel);
        foreach (int side in new[] { 1, -1 })
        {
            m.Quad(V(-120, 196, side * 62.2f), V(20, 196, side * 62.2f), V(20, 184, side * 62.2f), V(-120, 184, side * 62.2f), V(0, 0, side), Region.Window, stretch: true);
            Rail(m, V(-148, 150, side * 71), V(148, 150, side * 71), 10, Region.Yellow);
        }
        // The masts and their stays.
        Mast(m, V(-6, 150, 0), 34, 44, 420, 16, 22, 6);
        Mast(m, V(-104, 150, 0), 30, 40, 400, 14, 20, 6);
        // The main boom out to the wheel, a conveyor along it, and the operator's cab near the wheel.
        Vector3 bootA = V(110, 160, 0), hub = V(660, 112, 0);
        Truss(m, bootA, hub - V(70, -6, 0), 26, 52);
        m.Box(V(120, 180, -10), V(590, 184, 10), Region.Rubber, Region.Dark);
        m.Box(V(560, 150, 30), V(606, 184, 62), Region.Steel, Region.SteelDust);
        m.Quad(V(606.2, 182, 32), V(606.2, 182, 60), V(606.2, 164, 60), V(606.2, 164, 32), Vector3.UnitX, Region.Window, stretch: true);
        m.Box(V(590, 92, -34), V(640, 140, 34), Region.Steel, Region.SteelDust);                       // the wheel's head
        // The wheel: rim, spokes, and its buckets, each with teeth.
        m.Tube(hub - V(0, 0, 18), hub + V(0, 0, 18), 82, 82, Region.Steel, 28, cap0: Region.Dark, cap1: Region.Dark);
        m.Tube(hub - V(0, 0, 26), hub + V(0, 0, 26), 16, 16, Region.Dark, 10, cap0: Region.Steel, cap1: Region.Steel);
        for (int k = 0; k < 8; k++)
        {
            float a = k * MathF.PI / 4;
            var dirR = V(Math.Cos(a), Math.Sin(a), 0);
            foreach (float z in new[] { -19f, 19f }) m.Beam(hub + V(0, 0, z) + dirR * 14, hub + V(0, 0, z) + dirR * 80, 3.2f, 2.4f, Region.Steel);
        }
        for (int k = 0; k < 14; k++)
        {
            float a = k * 2 * MathF.PI / 14;
            var bucket = new Mesh { DirtLine = -1e9f };
            bucket.Box(V(-12, -2, -22), V(12, 20, 22), Region.Bucket, Region.Dark);
            for (float z = -16; z <= 16; z += 8) bucket.Block(V(13, 18, z), V(4, 0, 0), V(0, 3, 0), V(0, 0, 2), Region.Steel);
            m.Add(bucket, Matrix4x4.CreateTranslation(0, 82, 0) * Matrix4x4.CreateRotationZ(a) * Matrix4x4.CreateTranslation(hub));
        }
        // The counterweight boom and its weight, the discharge conveyor beneath.
        Truss(m, V(-130, 176, 0), V(-430, 212, 0), 22, 50);
        m.Box(V(-488, 150, -54), V(-420, 236, 54), Region.Concrete, Region.SteelDust);
        Truss(m, V(-120, 132, 0), V(-380, 84, 0), 16, 52, 3.4f, 2.2f);
        m.Box(V(-380, 78, -12), V(-120, 82, 12), Region.Rubber, Region.Dark);
        // Stays from the mast tops to the booms.
        foreach (var p in new[] { V(300, 186, 18), V(470, 168, 18), V(590, 150, 18), V(300, 186, -18), V(470, 168, -18), V(590, 150, -18) })
            m.Beam(V(-6, 425, Math.Sign(p.Z) * 18), p, 1.8f, 1.8f, Region.Dark);
        foreach (var p in new[] { V(-430, 236, 18), V(-270, 220, 18), V(-430, 236, -18), V(-270, 220, -18) })
            m.Beam(V(-104, 405, Math.Sign(p.Z) * 16), p, 1.8f, 1.8f, Region.Dark);
        m.Beam(V(-6, 430, 0), V(-104, 410, 0), 3f, 3f, Region.Steel);
        return new FeatureDef("oc_bucketwheel", "Red Dust Open Cut: a bucket wheel excavator", m, true, 0, Base: (18, 13));
    }

    /// <summary>The bucket wheel excavator's wheel: its hub's distance ahead of the turntable and height, and its radius.</summary>
    public const float WheelAhead = 660, WheelUp = 112, WheelRadius = 82;

    /// <summary>The scale of the two bucket wheel excavators on the lobe's floor: their mast tops stand PitWheelAboveRim
    /// over the rim, standing on the floor of a pit Plain - PitFloor deep.</summary>
    public static readonly float PitWheelScale = (float)((Layout.Plain - Layout.PitFloor + Layout.PitWheelAboveRim) / BucketWheel().Mesh.Bounds().Max.Y);

    /// <summary>A bucket wheel excavator for the lobe's floor: the same model scaled about its base so it stands only
    /// just higher than the pit is deep; its footprint is its crawlers.</summary>
    public static FeatureDef BucketWheelPit()
    {
        var m = new Mesh();
        m.Add(BucketWheel().Mesh, Matrix4x4.CreateScale(PitWheelScale));
        var foot = ((int)Math.Round(18 * PitWheelScale), (int)Math.Round(13 * PitWheelScale));
        return new FeatureDef("oc_bucketwheel_pit", "Red Dust Open Cut: a bucket wheel excavator at the face", m, true, 0, Base: foot);
    }

    // ---- Site gear.

    public static FeatureDef LightVehicle()
    {
        var m = new Mesh { DirtLine = 6 };
        foreach (float x in new[] { -15f, 16f }) foreach (float z in new[] { -11f, 11f }) Wheel(m, V(x, 7, z), 7, 5, 10);
        m.Box(V(-27, 7, -12), V(27, 18, 12), Region.White, Region.White);
        m.Box(V(-11, 18, -11), V(15, 30, 11), Region.White, Region.White);
        m.Quad(V(15.2, 29, -10), V(15.2, 29, 10), V(15.2, 20, 10), V(15.2, 20, -10), Vector3.UnitX, Region.Window, stretch: true);
        foreach (int side in new[] { 1, -1 })
            m.Quad(V(-9, 29, side * 11.2f), V(13, 29, side * 11.2f), V(13, 20, side * 11.2f), V(-9, 20, side * 11.2f), V(0, 0, side), Region.Window, stretch: true);
        m.Box(V(27, 6, -12), V(31, 18, 12), Region.Dark, Region.Dark);                                    // bull bar
        m.Box(V(-4, 30, -9), V(4, 32, 9), Region.Dark, Region.Lamp);                                      // light bar
        m.Box(V(-26, 18, -11), V(-12, 22, 11), Region.Bucket, Region.Bucket);                             // tray
        m.Plant++;
        m.Tube(V(-24, 18, 9), V(-24, 92, 9), 0.9f, 0.6f, Region.Steel, 5);
        m.Quad(V(-24, 92, 9.2f), V(-12, 90, 9.2f), V(-12, 82, 9.2f), V(-24, 84, 9.2f), Vector3.UnitZ, Region.Flag, stretch: true);
        m.Quad(V(-24, 92, 8.8f), V(-12, 90, 8.8f), V(-12, 82, 8.8f), V(-24, 84, 8.8f), -Vector3.UnitZ, Region.Flag, stretch: true);
        m.Plant--;
        return new FeatureDef("oc_ute", "Red Dust Open Cut: a mine ute", Features.Centred(m), false, 40);
    }

    public static FeatureDef LightTower()
    {
        var m = new Mesh { DirtLine = 6 };
        foreach (float z in new[] { -13f, 13f }) Wheel(m, V(-6, 7, z), 7, 4, 10);
        m.Box(V(-24, 8, -10), V(22, 30, 10), Region.Yellow, Region.Grille);
        m.Beam(V(22, 14, 0), V(40, 10, 0), 2, 2, Region.Dark);
        foreach (float z in new[] { -18f, 18f }) m.Beam(V(14, 12, Math.Sign(z) * 9), V(20, 1, z), 1.6f, 1.6f, Region.Dark);
        m.Tube(V(0, 30, 0), V(0, 150, 0), 3.4f, 2.6f, Region.Steel, 8);
        m.Box(V(-3, 146, -24), V(3, 152, 24), Region.Dark, Region.Dark);
        foreach (float z in new[] { -18f, -6f, 6f, 18f })
        {
            m.Box(V(2, 136, z - 5), V(10, 148, z + 5), Region.Dark, Region.Dark);
            m.Quad(V(10.2, 147, z - 4.2f), V(10.2, 147, z + 4.2f), V(10.2, 137, z + 4.2f), V(10.2, 137, z - 4.2f), Vector3.UnitX, Region.Lamp, stretch: true);
        }
        return new FeatureDef("oc_lighttower", "Red Dust Open Cut: a lighting plant, still lit", Features.Centred(m), false, 40);
    }

    public static FeatureDef Donga(int k)
    {
        var m = new Mesh { DirtLine = 3 };
        float len = k == 0 ? 60 : 44;
        foreach (float z in new[] { -16f, 16f }) m.Beam(V(-len - 2, 3, z), V(len + 2, 3, z), 2.5f, 3, Region.Dark);
        m.Box(V(-len, 6, -20), V(len, 42, 20), Region.White, Region.White);
        m.Box(V(-len - 2, 42, -22), V(len + 2, 46, 22), Region.Corrugated, Region.Corrugated);
        foreach (int side in new[] { 1, -1 })
            for (float x = -len + 14; x < len - 10; x += 26)
                m.Quad(V(x, 34, side * 20.2f), V(x + 12, 34, side * 20.2f), V(x + 12, 24, side * 20.2f), V(x, 24, side * 20.2f), V(0, 0, side), Region.Window, stretch: true);
        m.Quad(V(len - 18, 36, 20.3f), V(len - 8, 36, 20.3f), V(len - 8, 8, 20.3f), V(len - 18, 8, 20.3f), Vector3.UnitZ, Region.Blue, stretch: true);
        m.Box(V(len - 20, 0, 20), V(len - 6, 6, 30), Region.Concrete, Region.Dark);
        m.Box(V(-len - 8, 22, -10), V(-len, 34, 10), Region.White, Region.Grille);
        return new FeatureDef($"oc_donga{k}", "Red Dust Open Cut: a site office", Features.Centred(m), false, 40);
    }

    public static FeatureDef FuelTank()
    {
        var m = new Mesh { DirtLine = -1e9f };
        m.Box(V(-62, 0, -34), V(62, 6, 34), Region.Concrete, Region.Concrete);
        foreach (float x in new[] { -32f, 32f }) m.Box(V(x - 6, 6, -18), V(x + 6, 22, 18), Region.Concrete, Region.Concrete);
        m.Tube(V(-52, 34, 0), V(52, 34, 0), 22, 22, Region.White, 16, cap0: Region.Steel, cap1: Region.Steel);
        Stairs(m, V(-30, 6, 26), V(-14, 56, 20), 6);
        m.Box(V(-10, 56, -6), V(10, 60, 6), Region.Steel, Region.Hazard);
        return new FeatureDef("oc_fueltank", "Red Dust Open Cut: a diesel tank in its bund", Features.Centred(m), false, 80);
    }

    // ---- The outback.

    public static FeatureDef Spinifex(int k)
    {
        var m = new Mesh { Dusty = false };
        var rnd = new Random(700 + k);
        int blades = 46 + 16 * k;
        float radius = 16 + 8 * k, height = 18 + 6 * k;
        m.Plant++;
        for (int i = 0; i < blades; i++)
        {
            double a = rnd.NextDouble() * Math.PI * 2, r0 = rnd.NextDouble() * radius * 0.5;
            var dir = V(Math.Cos(a), 0, Math.Sin(a));
            var at = dir * (float)r0 + V(0, -2, 0);
            float reach = radius * (0.5f + 0.6f * (float)rnd.NextDouble()), up = height * (0.6f + 0.5f * (float)rnd.NextDouble());
            var line = Enumerable.Range(0, 5).Select(s => { float t = s / 4f; return at + dir * (reach * t) + Vector3.UnitY * (up * (1 - (1 - t) * (1 - t))); }).ToList();
            m.Ribbon(line, V(-Math.Sin(a), 0, Math.Cos(a)), 3.2f, 0.15f, Region.Spinifex);
        }
        m.Plant--;
        return new FeatureDef($"oc_spinifex{k}", "Spinifex", Features.Centred(m), false, 0, 10, Blocking: false);
    }

    public static FeatureDef Gum(int k)
    {
        var m = new Mesh { Dusty = false, DirtLine = -1e9f };
        var rnd = new Random(720 + k);
        float h = 70 + 30 * k;
        var top = V(rnd.NextDouble() * 10 - 5, h * 0.55, rnd.NextDouble() * 10 - 5);
        m.Tube(V(0, -6, 0), top, 6 + 2 * k, 4, Region.Bark, 8);
        int branches = 3 + k;
        for (int i = 0; i < branches; i++)
        {
            double a = i * 2 * Math.PI / branches + rnd.NextDouble();
            var end = top + V(Math.Cos(a) * h * 0.32, h * (0.25 + 0.2 * rnd.NextDouble()), Math.Sin(a) * h * 0.32);
            m.Tube(top, end, 3.4f, 1.6f, Region.Bark, 6);
            m.Plant++;
            m.Blob(end + V(0, 6, 0), V(h * 0.24, h * 0.14, h * 0.24), Region.Leaves, 730 + 10 * k + i, 0.3f, 1);
            m.Plant--;
        }
        m.Plant++;
        m.Blob(top + V(0, h * 0.3, 0), V(h * 0.2, h * 0.13, h * 0.2), Region.Leaves, 790 + k, 0.3f, 1);
        m.Plant--;
        return new FeatureDef($"oc_gum{k}", "A ghost gum", Features.Centred(m), false, 0, 120, Base: (1, 1));
    }

    public static FeatureDef DeadTree()
    {
        var m = new Mesh { Dusty = false, DirtLine = -1e9f };
        var rnd = new Random(760);
        var top = V(4, 46, -3);
        m.Tube(V(0, -6, 0), top, 6, 3.6f, Region.Bark, 7);
        for (int i = 0; i < 4; i++)
        {
            double a = i * Math.PI / 2 + rnd.NextDouble();
            var end = top + V(Math.Cos(a) * 34, 18 + 16 * rnd.NextDouble(), Math.Sin(a) * 34);
            m.Tube(top, end, 2.6f, 0.8f, Region.Bark, 5);
            m.Tube(Vector3.Lerp(top, end, 0.6f), end + V(Math.Cos(a + 1) * 12, 14, Math.Sin(a + 1) * 12), 1.4f, 0.5f, Region.Bark, 4);
        }
        return new FeatureDef("oc_deadtree", "A dead tree", Features.Centred(m), false, 0, 60, Base: (1, 1));
    }

    public static FeatureDef Saltbush()
    {
        var m = new Mesh { Dusty = false };
        m.Plant++;
        m.Blob(V(0, 6, 0), V(14, 10, 14), Region.Saltbush, 801, 0.35f, 1, cutY: -2);
        m.Blob(V(9, 4, 6), V(9, 7, 9), Region.Saltbush, 802, 0.35f, 1, cutY: -2);
        m.Plant--;
        return new FeatureDef("oc_saltbush", "Saltbush", Features.Centred(m), false, 0, 8, Blocking: false);
    }

    public static FeatureDef TermiteMound(int k)
    {
        var m = new Mesh { Dusty = false };
        float h = 34 + 18 * k;
        m.Blob(V(0, h * 0.42f, 0), V(13 + 3 * k, h * 0.55f, 11 + 3 * k), Region.Mound, 810 + k, 0.28f, 2, cutY: -4);
        m.Blob(V(9, h * 0.26f, 4), V(7, h * 0.32f, 7), Region.Mound, 815 + k, 0.3f, 1, cutY: -4);
        m.Blob(V(-7, h * 0.2f, -6), V(6, h * 0.25f, 6), Region.Mound, 818 + k, 0.3f, 1, cutY: -4);
        return new FeatureDef($"oc_termites{k}", "A termite mound", Features.Centred(m), false, 0, 0, Base: (2, 2));
    }

    public static FeatureDef Boulder(int k)
    {
        var m = new Mesh { Dusty = false };
        float r = 18 + 12 * k;
        m.Blob(V(0, r * 0.35f, 0), V(r, r * 0.7f, r * 0.85f), Region.RedRock, 830 + k, 0.35f, 1, cutY: -6);
        if (k > 0) m.Blob(V(r * 0.9f, r * 0.2f, r * 0.4f), V(r * 0.5f, r * 0.4f, r * 0.45f), Region.RedRock, 840 + k, 0.35f, 1, cutY: -6);
        return new FeatureDef($"oc_boulder{k}", "A red boulder", Features.Centred(m), false, 15 + 15 * k);
    }

    public static List<FeatureDef> All() =>
    [
        BucketWheel(), BucketWheelPit(), Truck(Load.Empty), Truck(Load.Loaded), Truck(Load.Tipping), Excavator(false), Excavator(true),
        LightVehicle(), LightTower(), Donga(0), Donga(1), FuelTank(),
        Spinifex(0), Spinifex(1), Gum(0), Gum(1), DeadTree(), Saltbush(), TermiteMound(0), TermiteMound(1), Boulder(0), Boulder(1), Boulder(2),
        // The railway and the ore's way to it.
        Loco(), Wagon(false), Wagon(true), Track(), BufferStop(), LocoShed(), Loadout(), Crusher(), ConveyorSection(), TransferTower(),
        // Tanks.
        FuelTankVertical(true), FuelTankVertical(false), WaterTank(true), WaterTank(false), WaterTower(),
        // The site's buildings, smaller machines and clutter.
        WorkshopShed(), WaterCart(), Grader(), Dozer(), Loader(), DrillRig(), ServiceTruck(), Bus(),
        ShippingContainer(0), ShippingContainer(1), ShippingContainer(2), Windmill(), CommTower(), PowerPole(), Genset(), TyreStack(), Magazine(), Drums(), PipeStack(),
    ];
}
