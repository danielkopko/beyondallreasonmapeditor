// The site's tanks, buildings, smaller machines and clutter: diesel tanks (large in a bund, and small), water tanks
// (large corrugated, and a rainwater tank on its stand), a water tower; the workshop shed; a water cart, a grader, a
// dozer, a wheel loader, a drill rig, a service truck and a crew bus; shipping containers; a windmill and its trough;
// a communications tower; power poles; generator sets; worn-out tyres; an explosives magazine; drums on a pallet; a
// stack of polyethylene pipe. Machines face +x as the others do.
using System.Numerics;

static partial class Models
{
    /// <summary>A ladder from a up to b, its stiles either side along <paramref name="across"/>, rungs every 6.</summary>
    static void Ladder(Mesh m, Vector3 a, Vector3 b, Vector3 across)
    {
        m.Plant++;
        var h = across * 3.5f;
        m.Beam(a - h, b - h, 0.8f, 0.8f, Region.Steel);
        m.Beam(a + h, b + h, 0.8f, 0.8f, Region.Steel);
        int n = Math.Max(2, (int)((b - a).Length() / 6));
        for (int i = 1; i < n; i++) { var p = Vector3.Lerp(a, b, i / (float)n); m.Beam(p - h, p + h, 0.6f, 0.6f, Region.Steel); }
        m.Plant--;
    }

    /// <summary>A handrail round a circle of radius r at height y.</summary>
    static void RingRail(Mesh m, float r, float y, int n)
    {
        for (int i = 0; i < n; i++)
        {
            double a0 = i * 2 * Math.PI / n, a1 = (i + 1) * 2 * Math.PI / n;
            Rail(m, V(Math.Cos(a0) * r, y, Math.Sin(a0) * r), V(Math.Cos(a1) * r, y, Math.Sin(a1) * r), 9, Region.Yellow);
        }
    }

    /// <summary>Windows round a cab between two heights (front +x, and both sides).</summary>
    static void CabWindows(Mesh m, float x0, float x1, float z, float y0, float y1, bool back = false)
    {
        m.Quad(V(x1 + 0.3f, y1, -z + 1), V(x1 + 0.3f, y1, z - 1), V(x1 + 0.3f, y0, z - 1), V(x1 + 0.3f, y0, -z + 1), Vector3.UnitX, Region.Window, stretch: true);
        if (back) m.Quad(V(x0 - 0.3f, y1, z - 1), V(x0 - 0.3f, y1, -z + 1), V(x0 - 0.3f, y0, -z + 1), V(x0 - 0.3f, y0, z - 1), -Vector3.UnitX, Region.Window, stretch: true);
        foreach (int s in new[] { 1, -1 })
            m.Quad(V(x0 + 2, y1, s * (z + 0.3f)), V(x1 - 2, y1, s * (z + 0.3f)), V(x1 - 2, y0, s * (z + 0.3f)), V(x0 + 2, y0, s * (z + 0.3f)), V(0, 0, s), Region.Window, stretch: true);
    }

    // ---- Tanks.

    /// <summary>A vertical diesel tank: large in a square concrete bund with a ladder and a handrail round its roof, or small on its own bund.</summary>
    public static FeatureDef FuelTankVertical(bool large)
    {
        var m = new Mesh { DirtLine = 3 };
        float r = large ? 52 : 22, h = large ? 92 : 50, bund = large ? 74 : 32;
        int sides = large ? 24 : 14;
        m.Box(V(-bund, 0, -bund), V(bund, 1.5f, bund), Region.Concrete, Region.Concrete);
        foreach (int s in new[] { -1, 1 })
        {
            m.Box(V(-bund, 0, s > 0 ? bund - 3 : -bund), V(bund, 9, s > 0 ? bund : -bund + 3), Region.Concrete, Region.Concrete);
            m.Box(V(s > 0 ? bund - 3 : -bund, 0, -bund + 3), V(s > 0 ? bund : -bund + 3, 9, bund - 3), Region.Concrete, Region.Concrete);
        }
        m.Tube(V(0, 1.5f, 0), V(0, h, 0), r, r, Region.White, sides);
        m.Tube(V(0, h, 0), V(0, h + r * 0.18f, 0), r + 1, r * 0.12f, Region.Steel, sides, cap1: Region.Steel);
        Ladder(m, V(-r - 1.5f, 2, 0), V(-r - 1.5f, h + 2, 0), Vector3.UnitZ);
        if (large) RingRail(m, r - 3, h + 2, 16);
        m.Tube(V(r - 4, 8, 0), V(bund - 1, 8, 0), 2.6f, 2.6f, Region.Steel, 8);                                 // the outlet
        m.Box(V(bund - 12, 3, -5), V(bund - 3, 15, 5), Region.Dark, Region.Hazard);                             // its valve
        return new FeatureDef(large ? "oc_tank_fuel" : "oc_tank_fuel_small", large ? "Red Dust Open Cut: a diesel tank in its bund" : "Red Dust Open Cut: a small diesel tank",
            Features.Centred(m), false, large ? 60 : 30, large ? 600 : 200);
    }

    /// <summary>A corrugated water tank: large on its sand pad, or a small rainwater tank on a timber stand.</summary>
    public static FeatureDef WaterTank(bool large)
    {
        var m = new Mesh { DirtLine = 3 };
        float r = large ? 66 : 20, h = large ? 54 : 30, stand = large ? 0 : 22;
        int sides = large ? 26 : 14;
        if (large) m.Tube(V(0, 0, 0), V(0, 2, 0), r + 6, r + 6, Region.Concrete, sides, cap1: Region.Concrete);
        else
        {
            foreach (float x in new[] { -15f, 15f }) foreach (float z in new[] { -15f, 15f }) m.Box(V(x - 2.5f, 0, z - 2.5f), V(x + 2.5f, stand, z + 2.5f), Region.Timber, Region.Timber);
            m.Box(V(-23, stand - 3, -23), V(23, stand, 23), Region.Timber, Region.Timber);
        }
        m.Tube(V(0, stand, 0), V(0, stand + h, 0), r, r, Region.Steel, sides);
        for (float y = stand + 5; y < stand + h - 2; y += 7) m.Tube(V(0, y, 0), V(0, y + 1.8f, 0), r + 0.7f, r + 0.7f, Region.Steel, sides);   // the corrugations
        m.Tube(V(0, stand + h, 0), V(0, stand + h + r * 0.2f, 0), r + 1.5f, 3, Region.Corrugated, sides, cap1: Region.Steel);
        m.Tube(V(r * 0.5f, stand + h + r * 0.08f, 0), V(r * 0.5f, stand + h + r * 0.2f + 8, 0), 2.4f, 2.4f, Region.Dark, 8);   // the inlet
        m.Tube(V(r - 1, stand + 4, 0), V(r + 10, stand + 4, 0), 2, 2, Region.Dark, 8);                         // the outlet and tap
        if (large) Ladder(m, V(-r - 1.5f, 2, 0), V(-r - 1.5f, h + 4, 0), Vector3.UnitZ);
        return new FeatureDef(large ? "oc_tank_water" : "oc_tank_water_small", large ? "Red Dust Open Cut: a water tank" : "Red Dust Open Cut: a rainwater tank on its stand",
            Features.Centred(m), false, large ? 50 : 20);
    }

    public static FeatureDef WaterTower()
    {
        var m = new Mesh { DirtLine = 3 };
        foreach (float x in new[] { -24f, 24f }) foreach (float z in new[] { -24f, 24f }) m.Box(V(x - 5, -4, z - 5), V(x + 5, 3, z + 5), Region.Concrete, Region.Concrete);
        Mast(m, V(0, 0, 0), 24, 24, 104, 16, 16, 4);
        m.Tube(V(0, 114, 0), V(0, 150, 0), 26, 26, Region.White, 18);
        m.Tube(V(0, 150, 0), V(0, 158, 0), 27, 3, Region.Steel, 18, cap1: Region.Steel);
        m.Tube(V(0, 0, 0), V(0, 114, 0), 2.6f, 2.6f, Region.Dark, 8);                                          // the riser
        Ladder(m, V(-27, 3, 0), V(-23, 112, 0), Vector3.UnitZ);
        return new FeatureDef("oc_watertower", "Red Dust Open Cut: a water tower", Features.Centred(m), false, 70);
    }

    // ---- The workshop shed: steel and corrugated iron, three roller doors along +z (one up), windows high on the ends.

    public static FeatureDef WorkshopShed()
    {
        var m = new Mesh { DirtLine = 2 };
        const float L = 180, W = 110, wall = 96, ridge = 126, doorTop = 80, doorHalf = 32;
        m.Box(V(-L, 0, -W), V(L, 2, W), Region.Concrete, Region.Concrete);
        // The walls: the back (-z) and the ends whole, the front (+z) between its doors.
        m.Box(V(-L, 2, -W), V(L, wall, -W + 4), Region.Corrugated, Region.Steel);
        foreach (int e in new[] { 1, -1 }) m.Box(V(e > 0 ? L - 4 : -L, 2, -W + 4), V(e > 0 ? L : -L + 4, wall, W - 4), Region.Corrugated, Region.Steel);
        float[] doors = [-110, 0, 110];
        float x = -L;
        foreach (float d in doors.Append(L + doorHalf))
        {
            m.Box(V(x, 2, W - 4), V(d - doorHalf, wall, W), Region.Corrugated, Region.Steel);
            x = d + doorHalf;
        }
        foreach (float d in doors)
        {
            m.Box(V(d - doorHalf, doorTop, W - 4), V(d + doorHalf, wall, W), Region.Corrugated, Region.Steel);
            bool open = d == 0;
            m.Quad(V(d - doorHalf, open ? doorTop : doorTop, W - (open ? 30 : 2)), V(d + doorHalf, doorTop, W - (open ? 30 : 2)), V(d + doorHalf, 2, W - (open ? 30 : 2)), V(d - doorHalf, 2, W - (open ? 30 : 2)),
                Vector3.UnitZ, open ? Region.Dark : Region.Steel, stretch: true);
            if (!open) for (float y = 10; y < doorTop; y += 10) m.Box(V(d - doorHalf, y, W - 2.6f), V(d + doorHalf, y + 1, W - 1.6f), Region.Steel, Region.Steel);   // the door's slats
            m.Box(V(d - doorHalf - 6, 0, W), V(d - doorHalf - 1, 12, W + 5), Region.Hazard, Region.Hazard);                                              // the bollards
            m.Box(V(d + doorHalf + 1, 0, W), V(d + doorHalf + 6, 12, W + 5), Region.Hazard, Region.Hazard);
        }
        // The portal frames' columns outside, the roof and under it, the gables, windows, downpipes.
        foreach (int s in new[] { 1, -1 })
        {
            for (float cx = -L; cx <= L + 0.1f; cx += 60)
                if (s < 0 || !doors.Any(d => Math.Abs(cx - d) < doorHalf + 4))
                    m.Box(V(cx - 3, 0, s * (W + 1) - 3), V(cx + 3, wall + 1, s * (W + 1) + 3), Region.Steel, Region.SteelDust);
            m.Quad(V(-L - 4, wall, s * (W + 4)), V(L + 4, wall, s * (W + 4)), V(L + 4, ridge, 0), V(-L - 4, ridge, 0), V(0, 1, s * 0.8f), Region.Corrugated);
            m.Quad(V(-L, wall - 1, s * W), V(L, wall - 1, s * W), V(L, ridge - 1, 0), V(-L, ridge - 1, 0), V(0, -1, -s * 0.5f), Region.Dark);
            m.Tube(V(s * (L + 3), 0, W + 3), V(s * (L + 3), wall, W + 3), 1.6f, 1.6f, Region.Steel, 6);
            m.Tube(V(s * (L + 3), 0, -W - 3), V(s * (L + 3), wall, -W - 3), 1.6f, 1.6f, Region.Steel, 6);
        }
        foreach (int e in new[] { -1, 1 })
        {
            float xe = e * (L + 0.2f);
            m.Poly([V(xe, wall, -W - 4), V(xe, wall, W + 4), V(xe, ridge, 0)], V(e, 0, 0), Region.Corrugated);
            for (float z = -80; z < 80; z += 40)
                m.Quad(V(xe + e * 0.2f, wall - 10, z), V(xe + e * 0.2f, wall - 10, z + 28), V(xe + e * 0.2f, wall - 24, z + 28), V(xe + e * 0.2f, wall - 24, z), V(e, 0, 0), Region.Window, stretch: true);
        }
        return new FeatureDef("oc_workshop", "Red Dust Open Cut: the heavy-vehicle workshop", Features.Centred(m), true, 0);
    }

    // ---- The smaller machines.

    public static FeatureDef WaterCart()
    {
        var m = HaulChassis();
        m.Box(V(-82, 28, -26), V(34, 34, 26), Region.Dark, Region.Dark);                                       // the tank's cradle
        m.Tube(V(-80, 58, 0), V(32, 58, 0), 26, 26, Region.Yellow, 16, cap0: Region.Yellow, cap1: Region.Yellow);
        foreach (float x in new[] { -58f, -20f, 18f }) m.Tube(V(x, 58, 0), V(x + 2.4f, 58, 0), 27, 27, Region.Steel, 16);   // its bands
        m.Box(V(-30, 83, -7), V(-10, 87, 7), Region.Steel, Region.Hazard);                                    // the hatch
        m.Box(V(-96, 18, -40), V(-88, 25, 40), Region.Steel, Region.Steel);                                   // the spray bar
        foreach (float z in new[] { -32f, 0f, 32f }) m.Box(V(-100, 15, z - 4), V(-96, 22, z + 4), Region.Dark, Region.Dark);
        m.Box(V(-88, 24, -20), V(-82, 42, 20), Region.Dark, Region.Grille);                                   // the pump
        return new FeatureDef("oc_watercart", "Red Dust Open Cut: a water cart", Features.Centred(m), false, 300);
    }

    public static FeatureDef Grader()
    {
        var m = new Mesh { DirtLine = 8 };
        foreach (float x in new[] { -46f, -20f }) foreach (float z in new[] { -24f, 24f }) Wheel(m, V(x, 11, z), 11, 8, 12);
        foreach (float z in new[] { -22f, 22f }) Wheel(m, V(58, 10, z), 10, 7, 12);
        foreach (float z in new[] { -17f, 17f }) m.Box(V(-48, 8, z - 3), V(-18, 18, z + 3), Region.Yellow, Region.Yellow);   // the tandems
        m.Box(V(-72, 14, -18), V(-26, 40, 18), Region.Yellow, Region.Grille);                                 // the engine
        m.Quad(V(-72.3f, 38, 16), V(-72.3f, 38, -16), V(-72.3f, 18, -16), V(-72.3f, 18, 16), -Vector3.UnitX, Region.Grille, stretch: true);
        m.Tube(V(-52, 40, 8), V(-52, 54, 8), 2.2f, 2.2f, Region.Dark, 6);
        m.Box(V(-26, 18, -17), V(2, 62, 17), Region.Yellow, Region.YellowDust);                               // the cab
        CabWindows(m, -26, 2, 17, 40, 59, back: true);
        // The frame up over the blade to the front axle; the circle, the drawbar and the blade, angled; its lift rams.
        m.Beam(V(0, 34, 0), V(40, 38, 0), 6, 5, Region.Yellow);
        m.Beam(V(40, 38, 0), V(60, 18, 0), 5, 4, Region.Yellow);
        m.Box(V(54, 12, -24), V(62, 18, 24), Region.Yellow, Region.Yellow);
        m.Tube(V(18, 22, 0), V(18, 25, 0), 16, 16, Region.Dark, 14, cap1: Region.Dark);
        m.Beam(V(36, 34, 0), V(20, 24, 0), 3, 3, Region.Yellow);
        var across = Vector3.Normalize(V(-0.42, 0, 0.9));
        m.Block(V(16, 10, 0), Vector3.Normalize(V(0.9, 0, 0.42)) * 2.5f, V(0, 8, 0), across * 36, Region.Steel, Region.Steel);
        foreach (int s in new[] { 1, -1 }) m.Tube(V(26, 40, s * 10), V(16 - s * 6, 16, s * 26), 2, 1.6f, Region.Chrome, 6);
        return new FeatureDef("oc_grader", "Red Dust Open Cut: a motor grader", Features.Centred(m), false, 200);
    }

    public static FeatureDef Dozer()
    {
        var m = new Mesh { DirtLine = 6 };
        foreach (int s in new[] { 1, -1 })
        {
            float zc = s * 27;
            m.Box(V(-44, 2, zc - 9), V(40, 24, zc + 9), Region.Track, Region.Track);
            foreach (float x in new[] { -44f, 40f }) m.Tube(V(x, 13, zc - 9), V(x, 13, zc + 9), 11, 11, Region.Track, 10, cap0: Region.Dark, cap1: Region.Dark);
        }
        m.Box(V(-40, 22, -18), V(40, 32, 18), Region.Dark, Region.Dark);                                      // the frame
        m.Box(V(-6, 32, -15), V(42, 56, 15), Region.Yellow, Region.YellowDust);                               // the engine hood
        m.Quad(V(42.3f, 54, -13), V(42.3f, 54, 13), V(42.3f, 34, 13), V(42.3f, 34, -13), Vector3.UnitX, Region.Grille, stretch: true);
        m.Tube(V(20, 56, 6), V(20, 70, 6), 2.6f, 2.6f, Region.Dark, 6);
        m.Box(V(-44, 32, -19), V(-6, 50, 19), Region.Yellow, Region.YellowDust);                              // the cab's base and tank
        m.Box(V(-40, 50, -16), V(-8, 82, 16), Region.Yellow, Region.Yellow);                                  // the cab
        CabWindows(m, -40, -8, 16, 60, 79, back: true);
        m.Box(V(-42, 82, -18), V(-6, 86, 18), Region.Yellow, Region.YellowDust);
        // The blade on its push arms, its lift rams; the ripper behind.
        m.Block(V(64, 13, 0), V(3, 0, 0), V(0, 11, 0), V(0, 0, 42), Region.Yellow, Region.YellowDust);
        m.Block(V(61, 33, 0), Vector3.Normalize(V(1, 0.35, 0)) * 3, Vector3.Normalize(V(-0.35, 1, 0)) * 10, V(0, 0, 42), Region.Yellow, Region.YellowDust);
        m.Box(V(64, 0, -42), V(69, 3, 42), Region.Steel, Region.Steel);
        foreach (int s in new[] { 1, -1 })
        {
            m.Beam(V(-20, 14, s * 33), V(62, 12, s * 33), 3, 4, Region.Yellow);
            m.Tube(V(34, 52, s * 16), V(58, 30, s * 20), 3, 2.4f, Region.Chrome, 8);
        }
        m.Box(V(-66, 22, -16), V(-44, 30, 16), Region.Yellow, Region.Yellow);
        m.Beam(V(-62, 26, 0), V(-70, -2, 0), 3, 5, Region.Steel);
        return new FeatureDef("oc_dozer", "Red Dust Open Cut: a track dozer", Features.Centred(m), false, 400);
    }

    public static FeatureDef Loader()
    {
        var m = new Mesh { DirtLine = 10 };
        foreach (float x in new[] { -46f, 34f }) foreach (float z in new[] { -30f, 30f }) Wheel(m, V(x, 20, z), 20, 15, 14);
        m.Box(V(-78, 16, -20), V(-14, 54, 20), Region.Yellow, Region.YellowDust);                             // the engine
        foreach (int s in new[] { 1, -1 })
            m.Quad(V(-74, 50, s * 20.3f), V(-40, 50, s * 20.3f), V(-40, 30, s * 20.3f), V(-74, 30, s * 20.3f), V(0, 0, s), Region.Grille, stretch: true);
        m.Box(V(-90, 12, -24), V(-78, 44, 24), Region.Yellow, Region.Yellow);                                 // the counterweight
        m.Quad(V(-90.3f, 42, -22), V(-90.3f, 42, 22), V(-90.3f, 36, 22), V(-90.3f, 36, -22), -Vector3.UnitX, Region.Hazard, stretch: true);
        m.Tube(V(-56, 54, 10), V(-56, 68, 10), 2.6f, 2.6f, Region.Dark, 6);
        m.Box(V(-14, 18, -16), V(46, 36, 16), Region.Yellow, Region.Yellow);                                  // the front frame
        m.Box(V(-34, 54, -18), V(-2, 90, 18), Region.Yellow, Region.Yellow);                                  // the cab
        CabWindows(m, -34, -2, 18, 64, 87, back: true);
        m.Box(V(-36, 90, -20), V(0, 93, 20), Region.Yellow, Region.YellowDust);
        // The lift arms and their rams; the bucket, toothed.
        foreach (int s in new[] { 1, -1 })
        {
            m.Beam(V(10, 44, s * 13), V(80, 28, s * 13), 4, 6, Region.Yellow);
            m.Tube(V(14, 30, s * 10), V(58, 46, s * 10), 3.4f, 2.6f, Region.Chrome, 8);
            m.Slab(V(0, 0, s > 0 ? 36 : -40), Vector3.UnitX, Vector3.UnitY, [new(74, 50), new(102, 46), new(112, 6), new(98, 0), new(74, 4)], 4, Region.Yellow, Region.Yellow);
        }
        m.Quad(V(74, 50, -36), V(74, 50, 36), V(74, 4, 36), V(74, 4, -36), -Vector3.UnitX, Region.Yellow);
        m.Quad(V(74.5f, 48, -36), V(74.5f, 48, 36), V(74.5f, 6, 36), V(74.5f, 6, -36), Vector3.UnitX, Region.Bucket);
        m.Quad(V(74, 4, -36), V(74, 4, 36), V(98, 0, 36), V(98, 0, -36), -Vector3.UnitY, Region.Yellow);
        m.Quad(V(74.5f, 5, -36), V(74.5f, 5, 36), V(98, 1, 36), V(98, 1, -36), Vector3.UnitY, Region.Bucket);
        for (float z = -30; z <= 30; z += 10) m.Block(V(104, 1.5f, z), V(6, 0, 0), V(0, 1.5f, 0), V(0, 0, 2.5f), Region.Steel);
        return new FeatureDef("oc_loader", "Red Dust Open Cut: a wheel loader", Features.Centred(m), false, 400);
    }

    public static FeatureDef DrillRig()
    {
        var m = new Mesh { DirtLine = 6 };
        foreach (int s in new[] { 1, -1 })
        {
            float zc = s * 28;
            m.Box(V(-54, 2, zc - 9), V(46, 20, zc + 9), Region.Track, Region.Track);
            foreach (float x in new[] { -54f, 46f }) m.Tube(V(x, 11, zc - 9), V(x, 11, zc + 9), 9, 9, Region.Track, 10, cap0: Region.Dark, cap1: Region.Dark);
        }
        m.Box(V(-60, 20, -38), V(56, 28, 38), Region.Yellow, Region.YellowDust);                              // the deck
        m.Box(V(-58, 28, -30), V(-4, 66, 30), Region.Yellow, Region.YellowDust);                              // the power pack
        foreach (int s in new[] { 1, -1 })
            m.Quad(V(-54, 62, s * 30.3f), V(-12, 62, s * 30.3f), V(-12, 36, s * 30.3f), V(-54, 36, s * 30.3f), V(0, 0, s), Region.Grille, stretch: true);
        m.Box(V(4, 28, 10), V(36, 66, 38), Region.Yellow, Region.Yellow);                                     // the cab
        m.Quad(V(36.3f, 63, 12), V(36.3f, 63, 36), V(36.3f, 44, 36), V(36.3f, 44, 12), Vector3.UnitX, Region.Window, stretch: true);
        m.Quad(V(6, 63, 38.3f), V(34, 63, 38.3f), V(34, 44, 38.3f), V(6, 44, 38.3f), Vector3.UnitZ, Region.Window, stretch: true);
        // The mast, upright at the front: the drill string in it, the crown, the table; rods in their rack; the jacks.
        Truss(m, V(52, 28, -12), V(52, 196, -12), 7, 24, 2.2f, 1.4f);
        m.Tube(V(52, 24, -12), V(52, 190, -12), 2.4f, 2.4f, Region.Dark, 6);
        m.Box(V(44, 190, -20), V(60, 202, -4), Region.Steel, Region.Steel);
        m.Box(V(44, 28, -22), V(62, 40, -2), Region.Dark, Region.Yellow);
        foreach (float z in new[] { -30f, -24f }) m.Tube(V(36, 30, z), V(36, 150, z), 2, 2, Region.Steel, 6);
        foreach (float x in new[] { -56f, 52f }) foreach (float z in new[] { -40f, 40f }) m.Box(V(x - 3, 0, z - 3), V(x + 3, 22, z + 3), Region.Dark, Region.Dark);
        return new FeatureDef("oc_drill", "Red Dust Open Cut: a blast-hole drill rig", Features.Centred(m), false, 300);
    }

    public static FeatureDef ServiceTruck()
    {
        var m = new Mesh { DirtLine = 6 };
        foreach (float x in new[] { 34f, -22f, -38f }) foreach (float z in new[] { -15f, 15f }) Wheel(m, V(x, 9, z), 9, 6, 12);
        m.Box(V(-52, 12, -12), V(46, 18, 12), Region.Dark, Region.Dark);                                      // the chassis
        m.Box(V(22, 18, -17), V(48, 46, 17), Region.White, Region.White);                                     // the cab
        CabWindows(m, 22, 48, 17, 30, 44);
        m.Box(V(48, 10, -18), V(52, 22, 18), Region.Dark, Region.Dark);                                       // the bull bar
        m.Box(V(30, 46, -10), V(40, 49, 10), Region.Dark, Region.Lamp);                                       // the beacons
        m.Box(V(-54, 18, -18), V(20, 40, 18), Region.Steel, Region.SteelDust);                               // the service body
        foreach (int s in new[] { 1, -1 })
            for (float x = -50; x < 16; x += 22)
                m.Quad(V(x, 37, s * 18.3f), V(x + 18, 37, s * 18.3f), V(x + 18, 21, s * 18.3f), V(x, 21, s * 18.3f), V(0, 0, s), Region.Grille, stretch: true);
        m.Tube(V(-57, 30, -10), V(-57, 30, 10), 7, 7, Region.Dark, 10, cap0: Region.Yellow, cap1: Region.Yellow);   // the hose reel
        m.Tube(V(-42, 40, 10), V(-42, 48, 10), 4, 4, Region.Yellow, 8);                                      // the crane
        m.Beam(V(-42, 48, 10), V(-10, 56, 10), 2.4f, 2.4f, Region.Yellow);
        return new FeatureDef("oc_servicetruck", "Red Dust Open Cut: a service truck", Features.Centred(m), false, 150);
    }

    public static FeatureDef Bus()
    {
        var m = new Mesh { DirtLine = 6 };
        foreach (float x in new[] { -28f, 30f }) foreach (float z in new[] { -12f, 12f }) Wheel(m, V(x, 7, z), 7, 5, 10);
        m.Box(V(-44, 7, -14), V(44, 38, 14), Region.White, Region.White);
        m.Box(V(-44.4f, 15, -14.4f), V(44.4f, 19, 14.4f), Region.Blue, Region.Blue);                          // the stripe
        foreach (int s in new[] { 1, -1 })
            m.Quad(V(-40, 35, s * 14.3f), V(36, 35, s * 14.3f), V(36, 25, s * 14.3f), V(-40, 25, s * 14.3f), V(0, 0, s), Region.Window, stretch: true);
        m.Quad(V(44.3f, 36, -13), V(44.3f, 36, 13), V(44.3f, 22, 13), V(44.3f, 22, -13), Vector3.UnitX, Region.Window, stretch: true);
        m.Quad(V(-44.3f, 35, 11), V(-44.3f, 35, -11), V(-44.3f, 25, -11), V(-44.3f, 25, 11), -Vector3.UnitX, Region.Window, stretch: true);
        m.Quad(V(34, 34, 14.5f), V(42, 34, 14.5f), V(42, 8, 14.5f), V(34, 8, 14.5f), Vector3.UnitZ, Region.Dark, stretch: true);   // the door
        m.Box(V(-20, 38, -8), V(0, 42, 8), Region.White, Region.Grille);                                       // the air conditioner
        m.Box(V(44, 6, -14), V(48, 16, 14), Region.Dark, Region.Dark);
        foreach (float z in new[] { -10f, 10f }) m.Quad(V(48.3f, 15, z - 3), V(48.3f, 15, z + 3), V(48.3f, 11, z + 3), V(48.3f, 11, z - 3), Vector3.UnitX, Region.Lamp, stretch: true);
        return new FeatureDef("oc_bus", "Red Dust Open Cut: a crew bus", Features.Centred(m), false, 120);
    }

    // ---- Clutter and smaller structures.

    public static FeatureDef ShippingContainer(int k)
    {
        var m = new Mesh { DirtLine = 2 };
        var paint = k switch { 0 => Region.Rust, 1 => Region.Blue, _ => Region.White };
        m.Box(V(-35, 0, -14), V(35, 30, 14), paint, paint);
        foreach (int s in new[] { 1, -1 })
            for (float x = -31; x <= 31.1f; x += 6.2f)
                m.Box(V(x - 1, 2, s > 0 ? 14 : -14.8f), V(x + 1, 28, s > 0 ? 14.8f : -14), paint, paint);   // the corrugations
        foreach (float z in new[] { -10f, -4f, 4f, 10f }) m.Tube(V(35.6f, 2, z), V(35.6f, 28, z), 0.8f, 0.8f, Region.Steel, 5);   // the door bars
        foreach (float x in new[] { -35f, 33f }) foreach (float z in new[] { -14f, 12f }) foreach (float y in new[] { 0f, 28f })
                    m.Box(V(x, y, z), V(x + 2, y + 2, z + 2), Region.Dark, Region.Dark);                      // the corner castings
        return new FeatureDef($"oc_container{k}", "Red Dust Open Cut: a shipping container", Features.Centred(m), false, 40);
    }

    /// <summary>A windmill: a slim lattice tower, the wheel of sails facing +x, the tail and its vane, the pump rod; its trough beside it.</summary>
    public static FeatureDef Windmill()
    {
        var m = new Mesh { DirtLine = 2 };
        foreach (float x in new[] { -20f, 20f }) foreach (float z in new[] { -20f, 20f }) m.Box(V(x - 4, -3, z - 4), V(x + 4, 3, z + 4), Region.Concrete, Region.Concrete);
        Vector3 Leg(int k, float t) => V((k == 0 || k == 3 ? 1 : -1) * (20 - 17 * t), 140 * t, (k < 2 ? 1 : -1) * (20 - 17 * t));
        for (int k = 0; k < 4; k++) m.Beam(Leg(k, 0), Leg(k, 1), 1.6f, 1.6f, Region.Steel);
        for (int l = 0; l < 6; l++)
        {
            float t0 = l / 6f, t1 = (l + 1) / 6f;
            for (int k = 0; k < 4; k++) { m.Beam(Leg(k, t1), Leg((k + 1) % 4, t1), 1f, 1f, Region.Steel); m.Beam(Leg(k, t0), Leg((k + 1) % 4, t1), 0.8f, 0.8f, Region.Steel); }
        }
        m.Box(V(-6, 140, -5), V(10, 152, 5), Region.Steel, Region.SteelDust);
        var hub = V(14, 150, 0);
        m.Tube(hub - V(4, 0, 0), hub + V(2, 0, 0), 4, 4, Region.Dark, 8);
        m.Plant++;
        Vector3 R(double a) => V(0, Math.Cos(a), Math.Sin(a));
        for (int i = 0; i < 18; i++)
        {
            double a = i * 2 * Math.PI / 18;
            var t = V(0, -Math.Sin(a), Math.Cos(a));
            Vector3 p0 = hub + R(a) * 10, p1 = hub + R(a) * 38, w0 = t * 3.5f, w1 = t * 6f + V(-1.5, 0, 0);
            m.Quad(p0 - w0, p1 - w1, p1 + w1, p0 + w0, Vector3.UnitX, Region.Steel);
            m.Quad(p0 - w0, p1 - w1, p1 + w1, p0 + w0, -Vector3.UnitX, Region.Steel);
            double b = (i + 1) * 2 * Math.PI / 18;
            m.Beam(hub + R(a) * 24, hub + R(b) * 24, 0.6f, 0.6f, Region.Steel);
            m.Beam(hub + R(a) * 38, hub + R(b) * 38, 0.6f, 0.6f, Region.Steel);
        }
        m.Plant--;
        m.Beam(V(0, 148, 0), V(-58, 150, 0), 1.2f, 1.2f, Region.Steel);                                     // the tail
        m.Plant++;
        foreach (float s in new[] { 0.3f, -0.3f })
            m.Quad(V(-36, 162, s), V(-64, 162, s), V(-64, 140, s), V(-44, 142, s), V(0, 0, Math.Sign(s)), Region.White, stretch: true);
        m.Plant--;
        m.Beam(V(0, 140, 0), V(0, 4, 0), 0.6f, 0.6f, Region.Steel);                                          // the pump rod
        m.Box(V(26, 0, -10), V(86, 10, 10), Region.Concrete, Region.Concrete);                                // the trough
        m.Quad(V(27, 9.2f, -8), V(85, 9.2f, -8), V(85, 9.2f, 8), V(27, 9.2f, 8), Vector3.UnitY, Region.Window, stretch: true);
        m.Tube(V(0, 4, 4), V(30, 8, 4), 1, 1, Region.Dark, 6);
        return new FeatureDef("oc_windmill", "Red Dust Open Cut: a windmill and its trough", m, false, 60, Base: (3, 3));
    }

    /// <summary>A communications tower: a lattice mast banded orange and white, dishes and antennas, its hut beside it.</summary>
    public static FeatureDef CommTower()
    {
        var m = new Mesh { DirtLine = 2 };
        const float height = 300;
        Vector3 Leg(int k, float t) => V((k == 0 || k == 3 ? 1 : -1) * (26 - 20 * t), height * t, (k < 2 ? 1 : -1) * (26 - 20 * t));
        const int levels = 10;
        for (int l = 0; l < levels; l++)
        {
            float t0 = l / (float)levels, t1 = (l + 1) / (float)levels;
            var paint = l % 2 == 0 ? Region.Orange : Region.White;
            for (int k = 0; k < 4; k++)
            {
                m.Beam(Leg(k, t0), Leg(k, t1), 2.2f, 2.2f, paint);
                m.Beam(Leg(k, t1), Leg((k + 1) % 4, t1), 1.2f, 1.2f, paint);
                m.Beam(Leg(k, t0), Leg((k + 1) % 4, t1), 1f, 1f, paint);
            }
        }
        foreach (float x in new[] { -26f, 26f }) foreach (float z in new[] { -26f, 26f }) m.Box(V(x - 5, -3, z - 5), V(x + 5, 3, z + 5), Region.Concrete, Region.Concrete);
        m.Tube(V(8, 230, 0), V(18, 230, 0), 2, 15, Region.White, 14, cap1: Region.White);                    // dishes
        m.Tube(V(0, 200, -8), V(0, 200, -17), 2, 12, Region.White, 14, cap1: Region.White);
        foreach (int s in new[] { 1, -1 }) m.Box(V(-4, 270, s * 7 - 2), V(4, 296, s * 7 + 2), Region.White, Region.White);   // panel antennas
        m.Tube(V(0, height, 0), V(0, height + 40, 0), 1.4f, 0.6f, Region.Steel, 6);                          // the whip
        m.Box(V(-2, height + 40, -2), V(2, height + 44, 2), Region.Lamp, Region.Lamp);                       // its beacon
        m.Box(V(36, 0, -16), V(76, 28, 16), Region.White, Region.Steel);                                      // the hut
        m.Quad(V(76.3f, 22, -5), V(76.3f, 22, 5), V(76.3f, 0, 5), V(76.3f, 0, -5), Vector3.UnitX, Region.Steel, stretch: true);
        m.Beam(V(36, 24, 0), V(20, 40, 0), 1.6f, 1.6f, Region.Steel);                                        // the cable bridge
        return new FeatureDef("oc_commtower", "Red Dust Open Cut: a communications tower", m, false, 80, Base: (4, 4));
    }

    /// <summary>A timber power pole with its crossarm and insulators, the wires out half a span (80) each way along x.</summary>
    public static FeatureDef PowerPole()
    {
        var m = new Mesh { Dusty = false };
        m.Tube(V(0, -4, 0), V(0, 96, 0), 3.2f, 2.4f, Region.Timber, 8, cap1: Region.Timber);
        m.Box(V(-2, 86, -24), V(2, 90, 24), Region.Timber, Region.Timber);
        foreach (int s in new[] { 1, -1 }) m.Beam(V(0, 72, 0), V(0, 87, s * 18), 0.7f, 0.7f, Region.Timber);
        m.Plant++;
        foreach (float z in new[] { -20f, 0f, 20f })
        {
            m.Tube(V(0, 90, z), V(0, 95, z), 1.4f, 1f, Region.White, 6, cap1: Region.White);
            foreach (int s in new[] { 1, -1 })
                m.Ribbon(Enumerable.Range(0, 6).Select(i => { float f = i / 5f; return V(s * 80 * f, 95 - 7 * f * f, z); }).ToList(), V(0, 1, 0), 0.7f, 1, Region.Dark);
        }
        m.Plant--;
        return new FeatureDef("oc_powerpole", "Red Dust Open Cut: a power pole", m, false, 0, 30, Blocking: false, Base: (1, 1));
    }

    public static FeatureDef Genset()
    {
        var m = new Mesh { DirtLine = 2 };
        m.Box(V(-48, 0, -17), V(48, 4, 17), Region.Steel, Region.Steel);                                      // the skid
        m.Box(V(-44, 4, -15), V(44, 36, 15), Region.Yellow, Region.YellowDust);
        foreach (int s in new[] { 1, -1 })
            for (float x = -36; x < 34; x += 18)
                m.Quad(V(x, 32, s * 15.3f), V(x + 14, 32, s * 15.3f), V(x + 14, 10, s * 15.3f), V(x, 10, s * 15.3f), V(0, 0, s), Region.Grille, stretch: true);
        m.Quad(V(44.3f, 34, -13), V(44.3f, 34, 13), V(44.3f, 6, 13), V(44.3f, 6, -13), Vector3.UnitX, Region.Grille, stretch: true);   // the radiator
        foreach (float z in new[] { -6f, 6f }) m.Tube(V(-24, 36, z), V(-24, 56, z), 2.8f, 2.8f, Region.Dark, 8);
        m.Box(V(-56, 0, -10), V(-48, 16, 10), Region.Steel, Region.Hazard);                                    // the day tank
        return new FeatureDef("oc_genset", "Red Dust Open Cut: a diesel generator set", Features.Centred(m), false, 80);
    }

    public static FeatureDef TyreStack()
    {
        var m = new Mesh { Dusty = false };
        foreach (var (x, z, n) in new[] { (-26f, -10f, 3), (16f, -16f, 2) })
            for (int i = 0; i < n; i++)
                m.Tube(V(x, i * 13, z), V(x, i * 13 + 12.5f, z), 19, 19, Region.Tyre, 14, cap1: Region.Dark, stretch: true);
        foreach (var (x, z, a) in new[] { (-6f, 24f, 0.2f), (36f, 22f, -0.3f) })
        {
            var ax = V(Math.Sin(a), 0, Math.Cos(a)) * 6.5f;
            m.Tube(V(x, 19, z) - ax, V(x, 19, z) + ax, 19, 19, Region.Tyre, 14, cap0: Region.Dark, cap1: Region.Dark, stretch: true);
        }
        return new FeatureDef("oc_tyres", "Red Dust Open Cut: worn-out haul truck tyres", Features.Centred(m), false, 20);
    }

    public static FeatureDef Magazine()
    {
        var m = new Mesh { DirtLine = 2 };
        m.Box(V(-40, 0, -26), V(40, 3, 26), Region.Concrete, Region.Concrete);
        m.Box(V(-30, 3, -18), V(30, 32, 18), Region.White, Region.Steel);
        m.Box(V(-30.4f, 24, -18.4f), V(30.4f, 28, 18.4f), Region.Hazard, Region.Hazard);
        m.Quad(V(30.6f, 22, -8), V(30.6f, 22, 8), V(30.6f, 3, 8), V(30.6f, 3, -8), Vector3.UnitX, Region.Steel, stretch: true);   // the door
        m.Box(V(-32, 32, -20), V(32, 35, 20), Region.Steel, Region.SteelDust);
        m.Plant++;
        m.Tube(V(34, 0, 20), V(34, 70, 20), 0.9f, 0.7f, Region.Steel, 5);
        foreach (float s in new[] { 0.3f, -0.3f })
            m.Quad(V(34, 70, 20 + s), V(50, 67, 20 + s), V(50, 57, 20 + s), V(34, 60, 20 + s), V(0, 0, Math.Sign(s)), Region.Flag, stretch: true);
        m.Plant--;
        return new FeatureDef("oc_magazine", "Red Dust Open Cut: an explosives magazine", Features.Centred(m), false, 40);
    }

    public static FeatureDef Drums()
    {
        var m = new Mesh { DirtLine = -1e9f };
        m.Box(V(-17, 0, -17), V(17, 3, 17), Region.Timber, Region.Timber);
        Region[] paints = [Region.Blue, Region.Rust, Region.Yellow, Region.Blue];
        int i = 0;
        foreach (float x in new[] { -8.5f, 8.5f })
            foreach (float z in new[] { -8.5f, 8.5f })
            {
                m.Tube(V(x, 3, z), V(x, 25, z), 7.6f, 7.6f, paints[i++], 10, cap1: Region.Steel);
                foreach (float y in new[] { 10f, 17f }) m.Tube(V(x, y, z), V(x, y + 1.4f, z), 8, 8, Region.Dark, 10);
            }
        return new FeatureDef("oc_drums", "Red Dust Open Cut: a pallet of fuel and oil drums", Features.Centred(m), false, 20, 60);
    }

    public static FeatureDef PipeStack()
    {
        var m = new Mesh { Dusty = false };
        foreach (float x in new[] { -60f, 0f, 60f }) m.Box(V(x - 4, 0, -24), V(x + 4, 4, 24), Region.Timber, Region.Timber);
        foreach (var (y, z) in new[] { (11.4f, -15f), (11.4f, 0f), (11.4f, 15f), (24.2f, -7.5f), (24.2f, 7.5f), (37f, 0f) })
            m.Tube(V(-90, y, z), V(90, y, z), 7.4f, 7.4f, Region.Dark, 12, cap0: Region.Dark, cap1: Region.Dark);
        return new FeatureDef("oc_pipes", "Red Dust Open Cut: a stack of polyethylene pipe", Features.Centred(m), false, 30);
    }
}
