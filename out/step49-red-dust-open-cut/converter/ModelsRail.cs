// The railway and the ore's way to it: the diesel-electric locomotives, the ore wagons (empty and loaded), a length of
// track, a buffer stop, the loco shed at the line's end, the train loadout over the main line, the primary crusher at
// the ROM pad, the overland conveyor's sections on their trestles and the transfer tower where it turns. Rolling stock
// faces +x along the track, its wheels on rails at z ±8.5 whose heads are 4.5 up (the track model). The locos are
// blue with a white stripe and hazard-striped ends, the wagons rusted red by the ore; no railway's or maker's marks.
using System.Numerics;

static partial class Models
{
    /// <summary>The height of the rails' heads above the ground, and the centres along the track of two wagons and of two locos coupled.</summary>
    public const float RailTop = 4.5f, WagonPitch = 120, LocoPitch = 262;
    /// <summary>The overland conveyor's deck, above the ground.</summary>
    public const float GalleryHeight = 62;

    /// <summary>A bogie at x: its axles' wheels on the rails, side frames outside them, a bolster across.</summary>
    static void Bogie(Mesh m, float x, int axles, float spacing, float r)
    {
        float half = (axles - 1) * spacing / 2, y = RailTop + r;
        for (int i = 0; i < axles; i++)
        {
            float ax = x - half + i * spacing;
            foreach (float z in new[] { -8.5f, 8.5f })
                m.Tube(V(ax, y, z - 1.8f), V(ax, y, z + 1.8f), r, r, Region.Steel, 10, cap0: Region.Dark, cap1: Region.Dark);
        }
        foreach (float z in new[] { -12.5f, 12.5f }) m.Box(V(x - half - r - 2, y - 3, z - 2), V(x + half + r + 2, y + 4, z + 2), Region.Dark, Region.Dark);
        m.Box(V(x - half, y - 1, -10.5f), V(x + half, y + 6, 10.5f), Region.Dark, Region.Dark);
    }

    /// <summary>A conveyor gallery from a to b (its deck's centre line, in the x-y plane): side frames braced in bays,
    /// the belt on its deck, a hood over the belt, a walkway with a handrail along the +z side.</summary>
    static void Gallery(Mesh m, Vector3 a, Vector3 b)
    {
        var along = b - a;
        var up = Vector3.UnitY;
        var z = Vector3.UnitZ;
        int bays = Math.Max(1, (int)MathF.Round(along.Length() / 32));
        foreach (int s in new[] { -1, 1 })
        {
            var o = z * (s * 12);
            m.Beam(a + o, b + o, 1.6f, 1.6f, Region.Steel);
            m.Beam(a + o + up * 16, b + o + up * 16, 1.4f, 1.4f, Region.Steel);
            for (int i = 0; i <= bays; i++)
            {
                var p = a + along * (i / (float)bays);
                m.Beam(p + o, p + o + up * 16, 1.1f, 1.1f, Region.Steel);
                if (i < bays) m.Beam(p + o, a + along * ((i + 1) / (float)bays) + o + up * 16, 0.9f, 0.9f, Region.Steel);
            }
        }
        var mid = (a + b) / 2;
        var half = along / 2;
        m.Block(mid + up * 0.5f, half, up, z * 12, Region.Steel, Region.SteelDust);                       // the deck
        m.Block(mid + up * 3.4f, half, up * 1.2f, z * 7, Region.Dark, Region.Dark);                       // the belt
        Vector3 h0 = up * 11 - z * 10, h1 = up * 15.5f, h2 = up * 11 + z * 10;
        m.Quad(a + h0, b + h0, b + h1, a + h1, up - z, Region.Corrugated);                                 // the hood
        m.Quad(a + h1, b + h1, b + h2, a + h2, up + z, Region.Corrugated);
        m.Block(mid + z * 17 + up * 0.5f, half, up * 0.8f, z * 5, Region.Steel, Region.SteelDust);       // the walkway
        Rail(m, a + z * 21.5f + up * 1.3f, b + z * 21.5f + up * 1.3f, 9, Region.Yellow);
    }

    /// <summary>A trestle under a gallery at x: two legs splayed out to concrete footings, a cross-head, an X of braces.</summary>
    static void Trestle(Mesh m, float x, float top)
    {
        float Z(int s, float y) => s * (22 - 9 * y / top);
        foreach (int s in new[] { -1, 1 })
        {
            m.Box(V(x - 5, -4, s * 22 - 5), V(x + 5, 3, s * 22 + 5), Region.Concrete, Region.Concrete);
            m.Beam(V(x, 3, Z(s, 3)), V(x, top, Z(s, top)), 2.2f, 2.2f, Region.Steel);
        }
        m.Beam(V(x, top - 1, -16), V(x, top - 1, 16), 2f, 2.4f, Region.Steel);
        float y0 = 8, y1 = top - 8;
        m.Beam(V(x, y0, Z(-1, y0)), V(x, y1, Z(1, y1)), 1.2f, 1.2f, Region.Steel);
        m.Beam(V(x, y0, Z(1, y0)), V(x, y1, Z(-1, y1)), 1.2f, 1.2f, Region.Steel);
    }

    // ---- The train.

    public static FeatureDef Loco()
    {
        var m = new Mesh { DirtLine = 13 };
        foreach (float x in new[] { -80f, 80f }) Bogie(m, x, 3, 16, 5.5f);
        const float deck = 22;
        m.Box(V(-124, deck - 4, -19), V(124, deck, 19), Region.Dark, Region.Steel);                         // the underframe and its walkways
        m.Box(V(-50, 9, -15), V(50, deck - 4, 15), Region.Dark, Region.Dark);                                // the fuel tank
        foreach (int e in new[] { 1, -1 })
        {
            m.Box(V(e > 0 ? 124 : -130, 10, -5), V(e > 0 ? 130 : -124, 15, 5), Region.Dark, Region.Dark);    // the couplers
            m.Box(V(e > 0 ? 116 : -126, 6, -18), V(e > 0 ? 126 : -116, deck - 4, 18), Region.Hazard, Region.Dark);   // the pilots
        }
        // The long hood: radiators, fans and the exhaust on top, a white stripe along it.
        m.Box(V(-118, deck, -14), V(66, deck + 32, 14), Region.Blue, Region.Blue);
        m.Box(V(-118.4f, deck + 13, -14.6f), V(66, deck + 16, 14.6f), Region.White, Region.White);
        foreach (int s in new[] { 1, -1 })
            m.Quad(V(-112, deck + 30, s * 14.3f), V(-62, deck + 30, s * 14.3f), V(-62, deck + 18, s * 14.3f), V(-112, deck + 18, s * 14.3f), V(0, 0, s), Region.Grille, stretch: true);
        foreach (float x in new[] { -100f, -78f }) m.Tube(V(x, deck + 32, 0), V(x, deck + 35, 0), 9, 9, Region.Dark, 12, cap1: Region.Grille);
        m.Tube(V(-30, deck + 32, 0), V(-30, deck + 39, 0), 3.5f, 3.5f, Region.Dark, 8);
        m.Quad(V(-118.6f, deck + 22, 14), V(-118.6f, deck + 22, -14), V(-118.6f, deck + 2, -14), V(-118.6f, deck + 2, 14), -Vector3.UnitX, Region.Hazard, stretch: true);
        // The cab and the short nose, its windows, the hazard-striped nose and headlights.
        m.Box(V(66, deck, -19), V(104, deck + 40, 19), Region.Blue, Region.White);
        m.Box(V(66, deck + 13, -19.6f), V(104.4f, deck + 16, 19.6f), Region.White, Region.White);
        m.Quad(V(104.6f, deck + 37, -17), V(104.6f, deck + 37, 17), V(104.6f, deck + 24, 17), V(104.6f, deck + 24, -17), Vector3.UnitX, Region.Window, stretch: true);
        foreach (int s in new[] { 1, -1 })
            m.Quad(V(72, deck + 37, s * 19.8f), V(100, deck + 37, s * 19.8f), V(100, deck + 24, s * 19.8f), V(72, deck + 24, s * 19.8f), V(0, 0, s), Region.Window, stretch: true);
        m.Box(V(104, deck, -16), V(118, deck + 22, 16), Region.Blue, Region.Blue);
        m.Quad(V(118.2f, deck + 21, -16), V(118.2f, deck + 21, 16), V(118.2f, deck + 1, 16), V(118.2f, deck + 1, -16), Vector3.UnitX, Region.Hazard, stretch: true);
        foreach (float z in new[] { -10f, 10f })
            m.Quad(V(118.5f, deck + 18, z - 3), V(118.5f, deck + 18, z + 3), V(118.5f, deck + 13, z + 3), V(118.5f, deck + 13, z - 3), Vector3.UnitX, Region.Lamp, stretch: true);
        // Handrails along the walkways, steps at the corners.
        foreach (int s in new[] { 1, -1 })
        {
            Rail(m, V(-122, deck, s * 18.4f), V(64, deck, s * 18.4f), 9, Region.Yellow);
            foreach (float x in new[] { -112f, 112f }) m.Box(V(x - 4, 7, s * 17 - 2), V(x + 4, deck - 4, s * 17 + 2), Region.Dark, Region.Yellow);
        }
        return new FeatureDef("oc_loco", "Red Dust Open Cut: a diesel-electric locomotive", Features.Centred(m), true, 0, Base: (16, 2));
    }

    public static FeatureDef Wagon(bool loaded)
    {
        var m = new Mesh { DirtLine = 10 };
        foreach (float x in new[] { -38f, 38f }) Bogie(m, x, 2, 15, 5f);
        const float floor = 17, top = 45;
        m.Box(V(-54, floor - 4, -14), V(54, floor, 14), Region.Dark, Region.Dark);                           // the underframe
        foreach (int e in new[] { 1, -1 })
            m.Box(V(e > 0 ? 54 : -60, floor - 7, -4), V(e > 0 ? 60 : -54, floor - 2, 4), Region.Dark, Region.Dark);   // the couplers
        // The body: an open box of plate, ribbed outside, worn bright inside.
        foreach (int s in new[] { 1, -1 })
        {
            m.Box(V(-56, floor, s > 0 ? 16 : -18), V(56, top, s > 0 ? 18 : -16), Region.Rust, Region.Steel);
            m.Quad(V(-54, top - 1, s * 15.8f), V(54, top - 1, s * 15.8f), V(54, floor + 2, s * 15.8f), V(-54, floor + 2, s * 15.8f), V(0, 0, -s), Region.Bucket);
            for (float x = -48; x <= 48; x += 16) m.Beam(V(x, floor, s * 18.6f), V(x, top, s * 18.6f), 1.6f, 0.8f, Region.Rust);
        }
        foreach (int e in new[] { 1, -1 })
        {
            m.Box(V(e > 0 ? 54 : -56, floor, -16), V(e > 0 ? 56 : -54, top, 16), Region.Rust, Region.Steel);
            m.Quad(V(e * 53.8f, top - 1, -16), V(e * 53.8f, top - 1, 16), V(e * 53.8f, floor + 2, 16), V(e * 53.8f, floor + 2, -16), V(-e, 0, 0), Region.Bucket);
        }
        m.Quad(V(-54, floor + 2, -16), V(54, floor + 2, -16), V(54, floor + 2, 16), V(-54, floor + 2, 16), Vector3.UnitY, Region.Bucket);
        if (loaded) m.Blob(V(0, top - 6, 0), V(52, 14, 15), Region.Ore, 51, 0.16f, 2, cutY: top - 8);
        // Its footprint is its body (112 long), so wagons coupled at 120 leave no gap a unit could squeeze through.
        return new FeatureDef(loaded ? "oc_wagon_loaded" : "oc_wagon", "Red Dust Open Cut: an ore wagon" + (loaded ? ", loaded" : ""), Features.Centred(m), true, 0, Base: (7, 2));
    }

    public static FeatureDef Track()
    {
        var m = new Mesh { Dusty = false };
        for (float x = -56; x <= 56; x += 16) m.Box(V(x - 3.5f, 0, -15), V(x + 3.5f, 2.2f, 15), Region.Concrete, Region.Concrete);   // the sleepers
        foreach (float z in new[] { -8.5f, 8.5f })
        {
            m.Box(V(-64, 2.2f, z - 1.6f), V(64, 3.2f, z + 1.6f), Region.Steel, Region.Steel);                // the rail's foot
            m.Box(V(-64, 3.2f, z - 0.8f), V(64, RailTop, z + 0.8f), Region.Steel, Region.Chrome);            // its web and head
        }
        return new FeatureDef("oc_track", "Red Dust Open Cut: track on concrete sleepers", m, true, 0, Blocking: false);
    }

    public static FeatureDef BufferStop()
    {
        var m = new Mesh { DirtLine = 3 };
        m.Box(V(-30, 0, -20), V(14, 2.5f, 20), Region.Concrete, Region.Concrete);
        foreach (float z in new[] { -8.5f, 8.5f })
        {
            m.Beam(V(-26, RailTop, z), V(2, 26, z), 2.2f, 2.2f, Region.Steel);                                // the struts back along the rails
            m.Box(V(0, RailTop, z - 3), V(6, 30, z + 3), Region.Steel, Region.Steel);
        }
        m.Box(V(6, 15, -20), V(12, 27, 20), Region.Hazard, Region.Dark);                                    // the buffer beam
        foreach (float z in new[] { -13f, 13f }) m.Tube(V(12, 21, z), V(20, 21, z), 3.2f, 3.2f, Region.Dark, 8, cap1: Region.Steel);
        return new FeatureDef("oc_buffer", "Red Dust Open Cut: a buffer stop", Features.Centred(m), true, 0);
    }

    /// <summary>The loco shed: a long steel shed, open at its +x end where the track comes in, the track ending inside.</summary>
    public static FeatureDef LocoShed()
    {
        var m = new Mesh { DirtLine = 2 };
        const float L = 260, W = 70, wall = 90, ridge = 122, door = 74, doorHalf = 30;
        m.Box(V(-L, 0, -W), V(L, 2, W), Region.Concrete, Region.Concrete);                                  // the floor
        m.Quad(V(-L + 12, 2.3f, -5), V(L - 4, 2.3f, -5), V(L - 4, 2.3f, 5), V(-L + 12, 2.3f, 5), Vector3.UnitY, Region.Dark, stretch: true);   // the pit between the rails
        float Roof(float z) => ridge - (ridge - wall) * Math.Abs(z) / (W + 4);
        foreach (int s in new[] { 1, -1 })
        {
            m.Box(V(-L, 2, s > 0 ? W - 4 : -W), V(L, wall, s > 0 ? W : -W + 4), Region.Corrugated, Region.Steel);   // the side walls
            for (float x = -L; x <= L + 0.1f; x += 65)
                m.Box(V(x - 3, 0, s * (W + 1) - 3), V(x + 3, wall + 1, s * (W + 1) + 3), Region.Steel, Region.SteelDust);   // the portal frames' columns
            for (float x = -L + 30; x < L - 30; x += 65)
                m.Quad(V(x, wall - 8, s * (W + 0.3f)), V(x + 30, wall - 8, s * (W + 0.3f)), V(x + 30, wall - 20, s * (W + 0.3f)), V(x, wall - 20, s * (W + 0.3f)), V(0, 0, s), Region.Window, stretch: true);
            m.Quad(V(-L - 4, wall, s * (W + 4)), V(L + 4, wall, s * (W + 4)), V(L + 4, ridge, 0), V(-L - 4, ridge, 0), V(0, 1, s * 0.8f), Region.Corrugated);   // the roof
            m.Quad(V(-L, wall - 1, s * W), V(L, wall - 1, s * W), V(L, ridge - 1, 0), V(-L, ridge - 1, 0), V(0, -1, -s * 0.5f), Region.Dark);                    // and under it
            for (float x = -L + 40; x < L - 60; x += 130)
                m.Quad(V(x, Roof(40) + 0.6f, s * 40), V(x + 50, Roof(40) + 0.6f, s * 40), V(x + 50, Roof(20) + 0.6f, s * 20), V(x, Roof(20) + 0.6f, s * 20), V(0, 1, s * 0.5f), Region.Window, stretch: true);   // skylights
        }
        m.Box(V(-L, 2, -W + 4), V(-L + 4, wall, W - 4), Region.Corrugated, Region.Steel);                    // the back wall
        foreach (int e in new[] { -1, 1 })
        {
            float x = e * (L + 0.2f);
            m.Poly([V(x, wall, -W - 4), V(x, wall, W + 4), V(x, ridge, 0)], V(e, 0, 0), Region.Corrugated);     // the gables
            float xi = x - e * 1.5f;
            m.Poly([V(xi, wall, -W), V(xi, wall, W), V(xi, ridge - 1, 0)], V(-e, 0, 0), Region.Dark);
        }
        m.Box(V(L - 4, door, -W + 4), V(L, wall, W - 4), Region.Corrugated, Region.Steel);                   // over the doorway
        foreach (int s in new[] { 1, -1 }) m.Box(V(L - 4, 2, s > 0 ? doorHalf : -W + 4), V(L, door, s > 0 ? W - 4 : -doorHalf), Region.Corrugated, Region.Steel);
        m.Quad(V(-200, 40, W + 0.4f), V(-188, 40, W + 0.4f), V(-188, 2, W + 0.4f), V(-200, 2, W + 0.4f), Vector3.UnitZ, Region.Blue, stretch: true);   // a door
        return new FeatureDef("oc_locoshed", "Red Dust Open Cut: the loco shed", Features.Centred(m), true, 0);
    }

    /// <summary>The train loadout: a bin on four legs straddling the main line (+x along it), its chute over the wagon
    /// beneath, and the incline conveyor up to its head house from the overland conveyor's end, 560 off along -z.</summary>
    public static FeatureDef Loadout()
    {
        var m = new Mesh { DirtLine = 3 };
        const float leg = 46, binBottom = 72, binTop = 150, lo = 22, hi = 44;
        foreach (float x in new[] { -leg, leg })
            foreach (float z in new[] { -leg, leg })
            {
                m.Box(V(x - 6, -4, z - 6), V(x + 6, 4, z + 6), Region.Concrete, Region.Concrete);
                m.Beam(V(x, 4, z), V(x, binTop, z), 3.4f, 3.4f, Region.Steel);
            }
        // Braced along the sides (clear of the train) and across only above it.
        foreach (float z in new[] { -leg, leg })
        {
            m.Beam(V(-leg, 8, z), V(leg, 64, z), 1.6f, 1.6f, Region.Steel);
            m.Beam(V(leg, 8, z), V(-leg, 64, z), 1.6f, 1.6f, Region.Steel);
            m.Beam(V(-leg, 66, z), V(leg, 66, z), 2.4f, 2.4f, Region.Steel);
        }
        foreach (float x in new[] { -leg, leg }) m.Beam(V(x, 66, -leg), V(x, 66, leg), 2.4f, 2.4f, Region.Steel);
        // The hopper narrowing to its gate and chute, the bin above, the head house on top.
        float yh = binBottom + 26;
        m.Quad(V(-hi, yh, -hi), V(hi, yh, -hi), V(lo, binBottom, -lo), V(-lo, binBottom, -lo), V(0, -0.5f, -1), Region.Steel);
        m.Quad(V(-hi, yh, hi), V(hi, yh, hi), V(lo, binBottom, lo), V(-lo, binBottom, lo), V(0, -0.5f, 1), Region.Steel);
        m.Quad(V(-hi, yh, -hi), V(-hi, yh, hi), V(-lo, binBottom, lo), V(-lo, binBottom, -lo), V(-1, -0.5f, 0), Region.Steel);
        m.Quad(V(hi, yh, -hi), V(hi, yh, hi), V(lo, binBottom, lo), V(lo, binBottom, -lo), V(1, -0.5f, 0), Region.Steel);
        m.Box(V(-hi, yh, -hi), V(hi, binTop, hi), Region.Steel, Region.SteelDust);
        m.Box(V(-lo, binBottom - 14, -lo), V(lo, binBottom, lo), Region.Dark, Region.Dark);
        m.Tube(V(0, binBottom - 14, 0), V(0, 54, 0), 10, 8, Region.Dark, 10);
        m.Box(V(-30, binTop, -30), V(30, binTop + 24, 30), Region.Corrugated, Region.Steel);
        m.Quad(V(30.3f, binTop + 18, -18), V(30.3f, binTop + 18, 18), V(30.3f, binTop + 10, 18), V(30.3f, binTop + 10, -18), Vector3.UnitX, Region.Window, stretch: true);
        // A walkway round the bin with its handrails, a ladder up a leg.
        m.Box(V(-hi - 8, yh, -hi - 8), V(hi + 8, yh + 2, hi + 8), Region.Steel, Region.SteelDust);
        foreach (var (a, b) in new[] { (V(-hi - 8, yh + 2, -hi - 8), V(hi + 8, yh + 2, -hi - 8)), (V(hi + 8, yh + 2, -hi - 8), V(hi + 8, yh + 2, hi + 8)), (V(hi + 8, yh + 2, hi + 8), V(-hi - 8, yh + 2, hi + 8)), (V(-hi - 8, yh + 2, hi + 8), V(-hi - 8, yh + 2, -hi - 8)) })
            Rail(m, a, b, 9, Region.Yellow);
        Ladder(m, V(-leg - 5, 4, leg), V(-leg - 5, yh, leg), Vector3.UnitZ);
        // The incline conveyor from the overland conveyor's end (560 out along -z) up to the head house, on trestles.
        var incline = new Mesh { DirtLine = 3 };
        const float run = 530;
        Gallery(incline, V(0, GalleryHeight, 0), V(run, binTop + 4, 0));
        foreach (float x in new[] { 130f, 260f, 390f }) Trestle(incline, x, GalleryHeight + (binTop + 4 - GalleryHeight) * x / run - 1);
        m.Add(incline, Matrix4x4.CreateRotationY(-MathF.PI / 2) * Matrix4x4.CreateTranslation(0, 0, -30 - run));
        return new FeatureDef("oc_loadout", "Red Dust Open Cut: the train loadout", m, true, 0, Base: (7, 7));
    }

    // ---- The ore's way to the train: the crusher, the overland conveyor, the transfer tower.

    /// <summary>The primary crusher: a concrete ROM bin trucks tip into from -x, its steel hopper and rock breaker,
    /// the crusher house, and the conveyor out at a climb along +x to the overland conveyor's height, 480 out.</summary>
    public static FeatureDef Crusher()
    {
        var m = new Mesh { DirtLine = 3 };
        m.Box(V(-70, 0, -60), V(30, 40, 60), Region.Concrete, Region.Concrete);
        m.Box(V(-82, 0, -54), V(-70, 9, 54), Region.Hazard, Region.Concrete);                                // the bump block
        const float y0 = 40, y1 = 62;
        Vector3 b0 = V(-52, y0, -44), b1 = V(16, y0, -44), b2 = V(16, y0, 44), b3 = V(-52, y0, 44);
        Vector3 t0 = V(-68, y1, -58), t1 = V(30, y1, -58), t2 = V(30, y1, 58), t3 = V(-68, y1, 58);
        m.Quad(t0, t1, b1, b0, V(0, 0.5f, 1), Region.Steel);
        m.Quad(t1, t2, b2, b1, V(-1, 0.5f, 0), Region.Steel);
        m.Quad(t2, t3, b3, b2, V(0, 0.5f, -1), Region.Steel);
        m.Quad(t3, t0, b0, b3, V(1, 0.5f, 0), Region.Steel);
        m.Quad(t0, t1, b1, b0, V(0, -0.5f, -1), Region.Steel);
        m.Quad(t1, t2, b2, b1, V(1, -0.5f, 0), Region.Steel);
        m.Quad(t2, t3, b3, b2, V(0, -0.5f, 1), Region.Steel);
        m.Quad(t3, t0, b0, b3, V(-1, -0.5f, 0), Region.Steel);
        m.Blob(V(-18, 40, 0), V(32, 10, 40), Region.Ore, 61, 0.2f, 2, cutY: 38);                             // ore left in it
        // The rock breaker on its pedestal.
        m.Box(V(10, y1, 40), V(24, y1 + 12, 54), Region.Yellow, Region.Yellow);
        m.Beam(V(17, y1 + 12, 47), V(-14, 104, 30), 4, 4, Region.Yellow);
        m.Beam(V(-14, 104, 30), V(-36, 70, 10), 3.4f, 3.4f, Region.Yellow);
        m.Tube(V(-36, 70, 10), V(-40, 54, 6), 2.6f, 2, Region.Dark, 8);
        // The crusher house, a window and a door, a vent.
        m.Box(V(30, 0, -38), V(96, 80, 38), Region.Corrugated, Region.Steel);
        m.Quad(V(60, 70, 38.3f), V(86, 70, 38.3f), V(86, 58, 38.3f), V(60, 70 - 12, 38.3f), Vector3.UnitZ, Region.Window, stretch: true);
        m.Quad(V(40, 40, 38.3f), V(52, 40, 38.3f), V(52, 0, 38.3f), V(40, 0, 38.3f), Vector3.UnitZ, Region.Blue, stretch: true);
        m.Tube(V(60, 80, -10), V(60, 94, -10), 3, 3, Region.Dark, 8);
        // The conveyor out, climbing to the overland conveyor's height.
        Gallery(m, V(96, 24, 0), V(480, GalleryHeight, 0));
        foreach (float x in new[] { 200f, 320f, 440f }) Trestle(m, x, 24 + (GalleryHeight - 24) * (x - 96) / 384 - 1);
        return new FeatureDef("oc_crusher", "Red Dust Open Cut: the primary crusher and its ROM bin", m, true, 0, Base: (11, 8));
    }

    /// <summary>A section of the overland conveyor, 128 long along x, on its trestle; units pass under it.</summary>
    public static FeatureDef ConveyorSection()
    {
        var m = new Mesh { DirtLine = 4 };
        Gallery(m, V(-64, GalleryHeight, 0), V(64, GalleryHeight, 0));
        Trestle(m, 0, GalleryHeight);
        return new FeatureDef("oc_conveyor", "Red Dust Open Cut: the overland conveyor on its trestles", m, true, 0, Base: (2, 3));
    }

    public static FeatureDef TransferTower()
    {
        var m = new Mesh { DirtLine = 3 };
        foreach (float x in new[] { -30f, 30f }) foreach (float z in new[] { -30f, 30f }) m.Box(V(x - 6, -4, z - 6), V(x + 6, 4, z + 6), Region.Concrete, Region.Concrete);
        Mast(m, V(0, 0, 0), 30, 30, 70, 26, 26, 3);
        m.Box(V(-34, 80, -34), V(34, 108, 34), Region.Corrugated, Region.Steel);
        foreach (int s in new[] { 1, -1 })
            m.Quad(V(-24, 102, s * 34.3f), V(24, 102, s * 34.3f), V(24, 94, s * 34.3f), V(-24, 94, s * 34.3f), V(0, 0, s), Region.Window, stretch: true);
        m.Box(V(-10, 40, -10), V(10, 80, 10), Region.Dark, Region.Dark);                                    // the chute
        Ladder(m, V(-36, 4, 30), V(-36, 80, 30), Vector3.UnitZ);
        return new FeatureDef("oc_transfer", "Red Dust Open Cut: a conveyor transfer tower", m, true, 0, Base: (5, 5));
    }
}
