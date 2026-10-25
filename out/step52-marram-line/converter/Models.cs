// Marram Line's features, all modelled here. Model space: x forward (a wreck's bow, a bunker's gun slit), y up, z
// across; each is built round the middle of its base. The old war's leavings: Czech hedgehogs and stakes on the beach,
// landing craft wrecked in the surf, burnt-out tanks in the zone, concrete bunkers at the draws and crossings, the
// coastal gun's casemate, sandbagged machine-gun nests, barbed wire on its pickets, shattered stumps; the coast's
// lighthouse; the farm and the village in ruins, the church with its tower broken, the windmill on the knoll; the
// country's hedgerows (bushes and oaks), maritime pines, apple trees, marram and reeds, hay ricks; the cove's jetty, boat
// shed and channel markers; telegraph poles, granite boulders, driftwood. No national markings anywhere.
using System.Numerics;

static partial class Models
{
    static Vector3 V(double x, double y, double z) => new((float)x, (float)y, (float)z);

    // Crush resistance against BAR's movement classes' crush strengths (movedefs.lua): light bots 15, scouts 18, bots
    // and hovers 25, T1 tanks 30, commanders 50, medium tanks 250, heavy tanks and heavy bots 252, the heaviest 1400+.
    const int ByTanks = 28, ByMedium = 100, ByHeavy = 251, ByNone = 1300;

    // ---- The beach.

    /// <summary>A Czech hedgehog: three steel angles crossed at their middles, rusted, sand drifted round its feet.</summary>
    public static FeatureDef Hedgehog()
    {
        var m = new Mesh { DirtLine = 3, DirtRegion = Region.Sand };
        var c = V(0, 13, 0);
        Vector3[] arms = [Vector3.Normalize(V(1, 1, 0.3)), Vector3.Normalize(V(-0.3, 1, 1)), Vector3.Normalize(V(-0.7, 1, -1))];
        foreach (var a in arms)
        {
            var e = a * 22;
            m.Beam(c - e, c + e, 2.2f, 2.2f, Region.Rust);
            m.Beam(c - e + V(1.5, 0, 0), c + e + V(1.5, 0, 0), 0.6f, 2.2f, Region.Rust);   // the angle's other flange
        }
        return new FeatureDef("ml_hedgehog", "A Czech hedgehog, rusted steel", Features.Centred(m), false, 2, Base: (2, 2), Crush: ByNone);
    }

    /// <summary>A wooden stake driven in at a slant toward the sea, its top capped with a rusted plate.</summary>
    public static FeatureDef Stake()
    {
        var m = new Mesh { DirtLine = 2, DirtRegion = Region.Sand };
        var top = V(-14, 30, 2);
        m.Tube(V(2, -4, 0), top, 3.4f, 2.8f, Region.TimberDark, 6);
        m.Tube(top, top + V(-2, 3, 0), 4.5f, 4.5f, Region.Rust, 6, cap1: Region.Rust);
        return new FeatureDef("ml_stake", "A beach stake", Features.Centred(m), false, 2, Blocking: false);
    }

    /// <summary>A landing craft: a flat-bottomed steel box with its bow ramp down, a gunwale and the coxswain's well at the
    /// stern; the first one aground upright, the second rolled on its side and burnt.</summary>
    public static FeatureDef LandingCraft(int k)
    {
        var hull = new Mesh();
        float L = 150, B = 40, D = 22;
        var paint = k == 0 ? Region.Grey : Region.Burnt;
        hull.Box(V(-L / 2, 0, -B / 2), V(L / 2 - 26, D, B / 2), paint, k == 0 ? Region.Dark : Region.Burnt);   // the hull, its open well
        hull.Box(V(-L / 2, D, -B / 2), V(L / 2 - 26, D + 2.5, -B / 2 + 3), paint, paint);            // the gunwales
        hull.Box(V(-L / 2, D, B / 2 - 3), V(L / 2 - 26, D + 2.5, B / 2), paint, paint);
        hull.Box(V(-L / 2, 0, -B / 2), V(-L / 2 + 26, D + 12, B / 2), paint, Region.Grey);          // the stern's raised deck
        hull.Box(V(-L / 2 + 6, D + 12, -10), V(-L / 2 + 20, D + 20, 10), Region.Grey, Region.Dark);  // the coxswain's well
        // The ramp, hinged at the bow and lowered onto the sand (k 0), or hanging half shut (k 1).
        float drop = k == 0 ? 0.25f : 0.9f;
        var hinge = V(L / 2 - 26, 2, 0);
        var tip = hinge + V(30 * MathF.Cos(drop), 30 * MathF.Sin(drop) * (k == 0 ? -0.1f : 1), 0);
        hull.Block((hinge + tip) / 2, (tip - hinge) / 2, V(0, 1.4, 0), V(0, 0, B / 2 - 1), Region.Rust, paint);
        var m = new Mesh { DirtLine = 5, DirtRegion = Region.Sand };
        var turn = k == 0 ? Matrix4x4.CreateRotationZ(0.04f) : Matrix4x4.CreateRotationX(1.1f) * Matrix4x4.CreateTranslation(0, 8, 0);
        m.Add(hull, turn * Matrix4x4.CreateTranslation(0, -4, 0));
        return new FeatureDef($"ml_craft{k}", k == 0 ? "A wrecked landing craft, its ramp down" : "A burnt-out landing craft on its side", Features.Centred(m), false, 100);
    }

    /// <summary>Driftwood: a bleached log and a branch, half in the sand.</summary>
    public static FeatureDef Driftwood()
    {
        var m = new Mesh();
        m.Tube(V(-30, 2, -3), V(28, 3, 4), 4, 3, Region.Timber, 7);
        m.Tube(V(10, 3, 2), V(26, 5, 16), 1.8f, 1, Region.Timber, 5);
        return new FeatureDef("ml_driftwood", "Driftwood", Features.Centred(m), false, 0, 15, Blocking: false);
    }

    // ---- The defences.

    /// <summary>A concrete bunker: a low casemate with chamfered shoulders, its gun slit to the front (+x), earth banked up
    /// its sides and over its back, its entrance at the rear.</summary>
    public static FeatureDef Bunker()
    {
        var m = new Mesh { DirtLine = 3 };
        float L = 76, B = 70, Hh = 30;
        m.Box(V(-L / 2, 0, -B / 2), V(L / 2, Hh, B / 2), Region.Concrete, Region.Concrete);
        m.Plate(V(L / 2 + 0.5, Hh * 0.55, 0), V(0, Hh * 0.45, 0), V(0, 0, B / 2 - 6), V(0.5, 0, 0), Region.GunSlit, Region.Concrete, stretchFace: true);
        // A thick roof slab with an overhang over the slit.
        m.Box(V(-L / 2 - 2, Hh, -B / 2 - 2), V(L / 2 + 8, Hh + 6, B / 2 + 2), Region.ConcreteDark, Region.Concrete);
        // Earth banked against its sides and back, grassed over (the earth region), and the rear entrance's dark door.
        m.Blob(V(-L / 2 + 4, 10, 0), V(22, 18, B / 2 - 2), Region.Earth, 901, 0.25f, 2, cutY: -2);
        m.Box(V(-L / 2 - 12, 0, 8), V(-L / 2, 18, 22), Region.Concrete, Region.Concrete);
        m.Plate(V(-L / 2 - 12.5f, 9, 15), V(0, 9, 0), V(0, 0, 5), V(-0.5, 0, 0), Region.Dark, Region.Concrete);
        return new FeatureDef("ml_bunker", "A concrete bunker", Features.Centred(m), true, 0);
    }

    /// <summary>The coastal gun's casemate: a big concrete block with a wide embrasure and the gun's barrel through it,
    /// a sloped roof, ammunition niches at the back; scorched round the slit.</summary>
    public static FeatureDef Casemate()
    {
        var m = new Mesh { DirtLine = 3 };
        float L = 120, B = 100, Hh = 38;
        m.Box(V(-L / 2, 0, -B / 2), V(L / 2, Hh, B / 2), Region.Concrete, Region.ConcreteDark);
        m.Plate(V(L / 2 + 0.5, Hh * 0.5, 0), V(0, Hh * 0.42, 0), V(0, 0, B / 2 - 10), V(0.5, 0, 0), Region.GunSlit, Region.Concrete, stretchFace: true);
        m.Block(V(L / 2 + 6, Hh + 4, 0), V(10, 0.5, 0), V(0, 4, 0), V(0, 0, B / 2 + 4), Region.ConcreteDark, Region.Concrete);   // the visor
        m.Tube(V(L / 2 - 20, Hh * 0.48, 0), V(L / 2 + 70, Hh * 0.5, 0), 4.4f, 3.6f, Region.Rust, 10, cap1: Region.Dark);         // the barrel
        m.Tube(V(L / 2 - 4, Hh * 0.48, 0), V(L / 2 + 6, Hh * 0.48, 0), 9, 9, Region.Rust, 10);                                  // its mantlet
        for (int i = -1; i <= 1; i += 2) m.Box(V(-L / 2 - 8, 0, i * 30 - 10), V(-L / 2, 16, i * 30 + 10), Region.Concrete, Region.ConcreteDark);
        m.Blob(V(-10, 12, B / 2 + 6), V(L / 2 + 10, 20, 18), Region.Earth, 911, 0.25f, 2, cutY: -2);
        m.Blob(V(-10, 12, -B / 2 - 6), V(L / 2 + 10, 20, 18), Region.Earth, 912, 0.25f, 2, cutY: -2);
        return new FeatureDef("ml_casemate", "The coastal gun's casemate", Features.Centred(m), true, 0);
    }

    /// <summary>The lighthouse: a white tower tapering up, a red band, its gallery and railing, the lantern still lit, a
    /// domed roof, and the keeper's cottage at its foot.</summary>
    public static FeatureDef Lighthouse()
    {
        var m = new Mesh();
        m.Tube(V(0, -4, 0), V(0, 60, 0), 15, 12.5f, Region.White, 16);
        m.Tube(V(0, 60, 0), V(0, 74, 0), 12.5f, 12, Region.Red, 16);
        m.Tube(V(0, 74, 0), V(0, 104, 0), 12, 10.5f, Region.White, 16);
        m.Tube(V(0, 104, 0), V(0, 106, 0), 15, 15, Region.Dark, 16, cap1: Region.Dark);                     // the gallery
        m.Plant++;
        for (int i = 0; i < 16; i++) { double a = i * Math.PI / 8; m.Beam(V(Math.Cos(a) * 14.5, 106, Math.Sin(a) * 14.5), V(Math.Cos(a) * 14.5, 112, Math.Sin(a) * 14.5), 0.5f, 0.5f, Region.Dark); }
        m.Plant--;
        m.Tube(V(0, 106, 0), V(0, 120, 0), 8.5f, 8.5f, Region.Lamp, 12, stretch: true);                      // the lantern
        m.Tube(V(0, 120, 0), V(0, 128, 0), 10, 2, Region.Red, 12);                                             // the dome
        m.Tube(V(0, 128, 0), V(0, 133, 0), 1, 0.4f, Region.Dark, 6);
        // The keeper's cottage, on the landward side.
        m.Box(V(16, 0, -16), V(52, 22, 16), Region.Plaster, Region.Plaster);
        RoofGable(m, V(16, 22, -18), V(52, 22, 18), 12, Region.RoofTile);
        WindowOn(m, V(52.4f, 12, -6), V(1, 0, 0), 6, 7);
        WindowOn(m, V(52.4f, 12, 6), V(1, 0, 0), 6, 7);
        return new FeatureDef("ml_lighthouse", "The lighthouse", Features.Centred(m), true, 0);
    }

    /// <summary>A machine-gun nest: a ring of sandbags round a hollow, a gap at the back.</summary>
    public static FeatureDef Nest()
    {
        var m = new Mesh();
        int n = 10;
        for (int i = 0; i < n; i++)
        {
            double a0 = (i + 0.08) * 2 * Math.PI / (n + 1.6), a1 = (i + 0.92) * 2 * Math.PI / (n + 1.6);
            double am = (a0 + a1) / 2, r = 16;
            var c = V(Math.Cos(am) * r, 5, Math.Sin(am) * r);
            var along = V(-Math.Sin(am), 0, Math.Cos(am)) * (float)(r * (a1 - a0) / 2);
            var outw = V(Math.Cos(am), 0, Math.Sin(am)) * 5;
            m.Block(c, along, V(0, 5, 0), outw, Region.Sandbag, Region.Sandbag);
            m.Block(c + V(0, 8, 0), along * 0.9f, V(0, 3.2, 0), outw * 0.85f, Region.Sandbag, Region.Sandbag);
        }
        return new FeatureDef("ml_nest", "A sandbagged machine-gun nest", Features.Centred(m), false, 0, Base: (2, 2), Crush: ByMedium);
    }

    /// <summary>A length of barbed wire, 64 long along x: five pickets and coils of concertina wire between them.</summary>
    public static FeatureDef Wire()
    {
        var m = new Mesh();
        for (int i = 0; i <= 4; i++)
        {
            float x = -32 + i * 16;
            m.Beam(V(x, -3, -5 + (i % 2) * 10), V(x + 1, 22, -5 + (i % 2) * 10), 1.1f, 1.1f, Region.TimberDark);
            m.Beam(V(x, 20, -6), V(x, 20, 6), 0.4f, 0.4f, Region.Wire);
        }
        m.Plant++;
        // Coils: loops every 7, each a ring of six short strands, leaning a little.
        for (float x = -28; x <= 28; x += 7)
        {
            float r = 9 + (x % 14 == 0 ? 1 : 0), lean = 2.2f * MathF.Sin(x * 0.7f);
            var c = V(x + lean, r + 1, 0);
            for (int k = 0; k < 6; k++)
            {
                double a0 = k * Math.PI / 3, a1 = (k + 1) * Math.PI / 3;
                m.Beam(c + V(0, Math.Sin(a0) * r, Math.Cos(a0) * r), c + V(1.5, Math.Sin(a1) * r, Math.Cos(a1) * r), 0.35f, 0.35f, Region.Wire);
            }
        }
        for (int s = -1; s <= 1; s++) m.Beam(V(-32, 6 + 7 * (s + 1), s * 7), V(32, 6 + 7 * (s + 1), s * 7), 0.3f, 0.3f, Region.Wire);
        m.Plant--;
        return new FeatureDef("ml_wire", "Barbed wire", Features.Centred(m), false, 3, Base: (4, 1), Crush: ByTanks);
    }

    // ---- The zone.

    /// <summary>A wrecked tank: hull, tracks on their wheels, a turret and its gun; the second's turret blown off and lying
    /// beside it, burnt out. Generic: no markings.</summary>
    public static FeatureDef Tank(int k)
    {
        var m = new Mesh { DirtLine = 6 };
        var paint = k == 0 ? Region.Drab : Region.Burnt;
        float L = 66, B = 34;
        m.Box(V(-L / 2, 8, -B / 2 + 7), V(L / 2, 22, B / 2 - 7), paint, paint);                                   // the hull
        m.Block(V(L / 2 + 3, 15, 0), V(4, 0, 0), V(0, 7, 0), V(0, 0, B / 2 - 7), paint, paint);                   // the glacis
        for (int side = -1; side <= 1; side += 2)
        {
            float z = side * (B / 2 - 4);
            m.Box(V(-L / 2 - 2, 2, z - 4), V(L / 2 + 2, 14, z + 4), Region.Track, Region.Track);                // the tracks
            for (int w = 0; w < 5; w++) m.Tube(V(-L / 2 + 8 + w * 12.5, 7, z - 4.5), V(-L / 2 + 8 + w * 12.5, 7, z + 4.5), 5, 5, Region.Rust, 8, cap0: Region.Rust, cap1: Region.Rust);
        }
        if (k == 0)
        {
            m.Tube(V(-4, 22, 0), V(-4, 32, 0), 12, 10, paint, 10, cap1: paint);                                     // the turret
            m.Tube(V(6, 27, 0), V(48, 28, 0), 1.8f, 1.6f, paint, 8, cap1: Region.Dark);                               // the gun
            m.Box(V(-14, 32, -4), V(-4, 35, 4), paint, Region.Dark);                                                  // the hatch, open
        }
        else
        {
            var turret = new Mesh();
            turret.Tube(V(0, 0, 0), V(0, 10, 0), 12, 10, paint, 10, cap1: paint);
            turret.Tube(V(10, 5, 0), V(46, 6, 0), 1.8f, 1.6f, paint, 8, cap1: Region.Dark);
            m.Add(turret, Matrix4x4.CreateRotationX(1.25f) * Matrix4x4.CreateRotationY(0.6f) * Matrix4x4.CreateTranslation(-12, 8, B / 2 + 18));
            m.Tube(V(-4, 21.5, 0), V(-4, 22.5, 0), 11, 11, Region.Dark, 10, cap1: Region.Dark);                      // the open ring
        }
        return new FeatureDef($"ml_tank{k}", k == 0 ? "A knocked-out tank" : "A burnt-out tank, its turret blown off", Features.Centred(m), false, 200, Base: (5, 3));
    }

    /// <summary>A shell-shattered tree: a stump splintered off, a few broken limbs.</summary>
    public static FeatureDef Stump(int k)
    {
        var m = new Mesh();
        var rnd = new Random(930 + k);
        float h = 22 + 16 * k;
        m.Tube(V(0, -4, 0), V(1, h, 0), 6 + k, 4, Region.Bark, 7);
        for (int i = 0; i < 4; i++)
        {
            double a = rnd.NextDouble() * Math.PI * 2;
            m.Tube(V(1, h - 2, 0), V(1 + Math.Cos(a) * 3, h + 6 + rnd.NextDouble() * 8, Math.Sin(a) * 3), 1.6f, 0.4f, Region.Timber, 4);   // splinters
        }
        if (k == 1) m.Tube(V(1, h * 0.6, 0), V(26, h * 0.8, 10), 2.4f, 1, Region.Bark, 5);
        return new FeatureDef($"ml_stump{k}", "A shattered tree", Features.Centred(m), false, 0, 40, Base: (1, 1), Crush: ByTanks);
    }

    // ---- Buildings.

    static void RoofGable(Mesh m, Vector3 min, Vector3 max, float rise, Region r, bool broken = false)
    {
        // A pitched roof along x from min to max (eaves at min.Y), ridge rise high; broken: its middle third gone.
        float mz = (min.Z + max.Z) / 2;
        float x0 = min.X, x1 = max.X;
        void Slope(float a, float b)
        {
            m.Quad(V(a, min.Y, min.Z), V(b, min.Y, min.Z), V(b, min.Y + rise, mz), V(a, min.Y + rise, mz), V(0, 1, -1), r);
            m.Quad(V(a, min.Y, max.Z), V(a, min.Y + rise, mz), V(b, min.Y + rise, mz), V(b, min.Y, max.Z), V(0, 1, 1), r);
        }
        if (!broken) Slope(x0, x1);
        else { Slope(x0, x0 + (x1 - x0) * 0.35f); Slope(x1 - (x1 - x0) * 0.25f, x1); }
        foreach (var x in new[] { x0, x1 })
            m.Poly([V(x, min.Y, min.Z), V(x, min.Y, max.Z), V(x, min.Y + rise, mz)], V(x == x0 ? -1 : 1, 0, 0), Region.Stone);
    }

    static void WindowOn(Mesh m, Vector3 c, Vector3 outward, float w, float h)
    {
        var across = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, outward)) * (w / 2);
        m.Plate(c, across, V(0, h / 2, 0), outward * 0.4f, Region.Window, Region.StoneDark, stretchFace: true);
    }

    /// <summary>Four walls of stone rubble, gaps where they've fallen (the gaps along x, at fractions of the side).</summary>
    static void RuinWalls(Mesh m, float L, float B, float H, Region r, int seed, float thick = 4)
    {
        var rnd = new Random(seed);
        void Wall(Vector3 a, Vector3 b, Vector3 outward)
        {
            int n = 6;
            for (int i = 0; i < n; i++)
            {
                var p0 = Vector3.Lerp(a, b, i / (float)n);
                var p1 = Vector3.Lerp(a, b, (i + 1) / (float)n);
                double keep = rnd.NextDouble();
                if (keep < 0.12) continue;                                                      // fallen
                float top = H * (float)(keep < 0.4 ? 0.35 + rnd.NextDouble() * 0.3 : 0.75 + rnd.NextDouble() * 0.25);
                var c = (p0 + p1) / 2 + V(0, top / 2, 0);
                m.Block(c, (p1 - p0) / 2, V(0, top / 2, 0), outward * (thick / 2), r, Region.StoneDark);
            }
        }
        Wall(V(-L / 2, 0, -B / 2), V(L / 2, 0, -B / 2), V(0, 0, -1));
        Wall(V(-L / 2, 0, B / 2), V(L / 2, 0, B / 2), V(0, 0, 1));
        Wall(V(-L / 2, 0, -B / 2), V(-L / 2, 0, B / 2), V(-1, 0, 0));
        Wall(V(L / 2, 0, -B / 2), V(L / 2, 0, B / 2), V(1, 0, 0));
    }

    /// <summary>A village house, shelled: stone walls (one partly fallen), windows, a slate roof with a hole torn in it,
    /// rubble round it. Two sizes.</summary>
    public static FeatureDef House(int k)
    {
        var m = new Mesh { DirtLine = 2 };
        float L = 64 + 18 * k, B = 40, H = 26 + 6 * k;
        m.Box(V(-L / 2, 0, -B / 2), V(L / 2, H, B / 2), k == 0 ? Region.Plaster : Region.Stone, Region.Dark);
        for (int i = 0; i < 3; i++)
        {
            float x = -L / 2 + L * (i + 0.5f) / 3;
            WindowOn(m, V(x, H * 0.65f, B / 2 + 0.3f), V(0, 0, 1), 7, 8);
            WindowOn(m, V(x, H * 0.65f, -B / 2 - 0.3f), V(0, 0, -1), 7, 8);
        }
        m.Plate(V(L / 2 + 0.3f, 9, 0), V(0, 9, 0), V(0, 0, 6), V(0.4, 0, 0), Region.Dark, Region.TimberDark);    // the doorway
        RoofGable(m, V(-L / 2 - 2, H, -B / 2 - 3), V(L / 2 + 2, H, B / 2 + 3), 16, Region.RoofTile, broken: true);
        m.Box(V(-L / 2 + 8, H, -3), V(-L / 2 + 16, H + 20, 3), Region.Stone, Region.StoneDark);                  // the chimney
        m.Blob(V(L / 2 - 10, 3, B / 2 + 6), V(18, 6, 10), Region.StoneDark, 940 + k, 0.4f, 1, cutY: -2);         // rubble
        return new FeatureDef($"ml_house{k}", "A shelled village house", Features.Centred(m), true, 0);
    }

    /// <summary>The church: a nave with tall lancet windows, its roof half gone, and its tower broken off ragged.</summary>
    public static FeatureDef Church()
    {
        var m = new Mesh { DirtLine = 2 };
        float L = 130, B = 54, H = 40;
        m.Box(V(-L / 2, 0, -B / 2), V(L / 2, H, B / 2), Region.Stone, Region.Dark);
        for (int i = 0; i < 5; i++)
        {
            float x = -L / 2 + 14 + i * (L - 28) / 4;
            WindowOn(m, V(x, H * 0.55f, B / 2 + 0.3f), V(0, 0, 1), 7, 20);
            WindowOn(m, V(x, H * 0.55f, -B / 2 - 0.3f), V(0, 0, -1), 7, 20);
        }
        RoofGable(m, V(-L / 2 - 2, H, -B / 2 - 3), V(L / 2 + 2, H, B / 2 + 3), 26, Region.RoofTile, broken: true);
        // The tower at the west end, broken off at different heights round its top.
        float T = 34;
        m.Box(V(-L / 2 - T, 0, -T / 2), V(-L / 2, 70, T / 2), Region.Stone, Region.StoneDark);
        m.Box(V(-L / 2 - T, 70, -T / 2), V(-L / 2 - T / 2, 96, -T / 2 + 6), Region.Stone, Region.StoneDark);
        m.Box(V(-L / 2 - T, 70, -T / 2), V(-L / 2 - T + 6, 88, T / 2), Region.Stone, Region.StoneDark);
        m.Box(V(-L / 2 - 8, 70, -T / 2), V(-L / 2, 78, T / 2), Region.Stone, Region.StoneDark);
        WindowOn(m, V(-L / 2 - T - 0.3f, 56, 0), V(-1, 0, 0), 8, 16);
        m.Blob(V(-L / 2 - T / 2, 4, T / 2 + 10), V(20, 7, 10), Region.StoneDark, 951, 0.4f, 1, cutY: -2);
        return new FeatureDef("ml_church", "The village church, its tower broken", Features.Centred(m), true, 0);
    }

    /// <summary>The farm's barn: walls of stone with its roof gone but for bare rafters.</summary>
    public static FeatureDef Barn()
    {
        var m = new Mesh { DirtLine = 2 };
        float L = 110, B = 56, H = 30;
        RuinWalls(m, L, B, H, Region.Stone, 961, 5);
        for (int i = 0; i < 7; i++)
        {
            float x = -L / 2 + 10 + i * (L - 20) / 6;
            if (i % 3 == 1) continue;
            m.Beam(V(x, H, -B / 2), V(x, H + 18, 0), 1.6f, 1.6f, Region.TimberDark);
            m.Beam(V(x, H + 18, 0), V(x, H, B / 2), 1.6f, 1.6f, Region.TimberDark);
        }
        m.Beam(V(-L / 2 + 10, H + 18, 0), V(L / 2 - 30, H + 18, 0), 1.8f, 1.8f, Region.TimberDark);
        return new FeatureDef("ml_barn", "The farm's barn, roofless", Features.Centred(m), true, 0);
    }

    /// <summary>A length of the farmyard's broken wall.</summary>
    public static FeatureDef FarmWall()
    {
        var m = new Mesh { DirtLine = 1 };
        var rnd = new Random(971);
        for (int i = 0; i < 6; i++)
        {
            float top = 8 + (float)rnd.NextDouble() * 12;
            m.Box(V(-48 + i * 16, 0, -3.5), V(-32 + i * 16, top, 3.5), Region.Stone, Region.StoneDark);
        }
        return new FeatureDef("ml_farmwall", "A broken farmyard wall", Features.Centred(m), false, 0, Base: (6, 1), Crush: ByHeavy);
    }

    /// <summary>The windmill on the knoll: a tapering stone tower, its cap turned to the sea wind, four sails of timber
    /// frames, one broken.</summary>
    public static FeatureDef Windmill()
    {
        var m = new Mesh();
        m.Tube(V(0, -4, 0), V(0, 64, 0), 20, 14, Region.Plaster, 14);
        m.Tube(V(0, 64, 0), V(0, 76, 0), 16, 6, Region.RoofTile, 12, cap1: Region.RoofTile);
        WindowOn(m, V(20.2f, 30, 0), V(1, 0, 0), 6, 8);
        m.Plate(V(19.8f, 8, 0), V(0, 8, 0), V(0, 0, 5), V(0.4, 0, 0), Region.Dark, Region.TimberDark);
        var hub = V(-18, 66, 0);
        m.Tube(hub + V(4, 0, 0), hub, 3, 3, Region.TimberDark, 8, cap1: Region.TimberDark);
        for (int s = 0; s < 4; s++)
        {
            double a = s * Math.PI / 2 + 0.3;
            float len = s == 2 ? 26 : 58;
            var dir = V(0, Math.Sin(a), Math.Cos(a));
            var side = V(0, Math.Cos(a), -Math.Sin(a));
            m.Beam(hub, hub + dir * len, 1.4f, 1.4f, Region.TimberDark);
            m.Plant++;
            for (float r = 14; r < len; r += 6) m.Beam(hub + dir * r, hub + dir * r + side * 10, 0.6f, 0.6f, Region.Timber);
            m.Beam(hub + dir * 14 + side * 10, hub + dir * len + side * 10, 0.6f, 0.6f, Region.Timber);
            m.Plant--;
        }
        return new FeatureDef("ml_windmill", "The windmill on the knoll", Features.Centred(m), true, 0);
    }

    // ---- The country's plants.

    /// <summary>A hedgerow bush: hawthorn and bramble in a dense clump, a little taller than a man.</summary>
    public static FeatureDef HedgeBush(int k)
    {
        var m = new Mesh();
        var rnd = new Random(1000 + k);
        for (int i = 0; i < 3; i++)
            m.Blob(V(rnd.NextDouble() * 20 - 10, 10 + rnd.NextDouble() * 6, rnd.NextDouble() * 12 - 6), V(15 + rnd.NextDouble() * 5, 11 + rnd.NextDouble() * 5, 13), Region.Leaves, 1010 + k * 10 + i, 0.3f, 1, cutY: -2);
        return new FeatureDef($"ml_hedge{k}", "A hedgerow", Features.Centred(m), false, 0, 15, Base: (3, 3), Crush: ByTanks);
    }

    /// <summary>A hedgerow oak: a stout trunk, spreading limbs, a broad crown.</summary>
    public static FeatureDef Oak(int k)
    {
        var m = new Mesh();
        var rnd = new Random(1040 + k);
        float h = 58 + 14 * k;
        var fork = V(2, h * 0.4, -1);
        m.Tube(V(0, -6, 0), fork, 6.5f + k, 5, Region.Bark, 9);
        for (int i = 0; i < 4; i++)
        {
            double a = i * Math.PI / 2 + rnd.NextDouble();
            var end = fork + V(Math.Cos(a) * h * 0.3, h * (0.22 + 0.15 * rnd.NextDouble()), Math.Sin(a) * h * 0.3);
            m.Tube(fork, end, 3.6f, 1.6f, Region.Bark, 6);
            m.Plant++;
            m.Blob(end + V(0, 4, 0), V(h * 0.26, h * 0.17, h * 0.26), Region.Leaves, 1050 + 10 * k + i, 0.3f, 1);
            m.Plant--;
        }
        m.Plant++;
        m.Blob(fork + V(0, h * 0.42, 0), V(h * 0.3, h * 0.2, h * 0.3), Region.Leaves, 1090 + k, 0.3f, 1);
        m.Plant--;
        return new FeatureDef($"ml_oak{k}", "A hedgerow oak", Features.Centred(m), false, 0, 80, Base: (2, 2), Crush: ByHeavy);
    }

    /// <summary>A maritime pine: a tall bare trunk leaning from the sea wind, an umbrella crown.</summary>
    public static FeatureDef Pine(int k)
    {
        var m = new Mesh();
        float h = 70 + 18 * k;
        var top = V(10 + 4 * k, h * 0.8, 2);
        m.Tube(V(0, -6, 0), top, 4.5f, 2.6f, Region.Bark, 7);
        m.Plant++;
        m.Blob(top + V(0, 6, 0), V(26 + 4 * k, 9, 24), Region.Pine, 1110 + k, 0.35f, 1);
        m.Blob(top + V(-10, 2, 8), V(16, 7, 14), Region.Pine, 1120 + k, 0.35f, 1);
        m.Plant--;
        return new FeatureDef($"ml_pine{k}", "A maritime pine", Features.Centred(m), false, 0, 60, Base: (2, 2), Crush: ByHeavy);
    }

    /// <summary>An apple tree in the orchard: a short gnarled trunk and a round crown, apples unpicked.</summary>
    public static FeatureDef AppleTree()
    {
        var m = new Mesh();
        m.Tube(V(0, -4, 0), V(2, 16, 1), 3.6f, 2.6f, Region.Bark, 6);
        m.Plant++;
        m.Blob(V(2, 28, 1), V(20, 14, 19), Region.Blossom, 1130, 0.3f, 1);
        m.Plant--;
        return new FeatureDef("ml_apple", "An apple tree", Features.Centred(m), false, 0, 30, Base: (2, 2), Crush: ByMedium);
    }

    /// <summary>Marram grass: a tussock of stiff blades, blown landward. It doesn't block.</summary>
    public static FeatureDef Marram(int k)
    {
        var m = new Mesh();
        var rnd = new Random(1140 + k);
        m.Plant++;
        int blades = 14 + 6 * k;
        for (int i = 0; i < blades; i++)
        {
            double a = rnd.NextDouble() * Math.PI * 2, r = rnd.NextDouble() * 7;
            var at = V(Math.Cos(a) * r, 0, Math.Sin(a) * r);
            float up = 12 + (float)rnd.NextDouble() * 9, lean = 3 + (float)rnd.NextDouble() * 5;
            var line = Enumerable.Range(0, 4).Select(s => { float t = s / 3f; return at + V(lean * t * t + Math.Cos(a) * t * 2, up * t, Math.Sin(a) * t * 2); }).ToList();
            m.Ribbon(line, V(-Math.Sin(a), 0, Math.Cos(a)), 1.6f, 0.2f, Region.Marram);
        }
        m.Plant--;
        return new FeatureDef($"ml_marram{k}", "Marram grass", Features.Centred(m), false, 0, 2, Blocking: false);
    }

    /// <summary>Reeds: a clump of tall stems with their seed heads. They don't block.</summary>
    public static FeatureDef Reeds()
    {
        var m = new Mesh();
        var rnd = new Random(1150);
        m.Plant++;
        for (int i = 0; i < 22; i++)
        {
            double a = rnd.NextDouble() * Math.PI * 2, r = rnd.NextDouble() * 10;
            var at = V(Math.Cos(a) * r, -2, Math.Sin(a) * r);
            float up = 22 + (float)rnd.NextDouble() * 12;
            var line = Enumerable.Range(0, 4).Select(s => { float t = s / 3f; return at + V(2.5 * t * t, up * t, 0); }).ToList();
            m.Ribbon(line, V(0, 0, 1), 1.4f, 0.4f, Region.Reeds);
        }
        m.Plant--;
        return new FeatureDef("ml_reeds", "Reeds", Features.Centred(m), false, 0, 2, Blocking: false);
    }

    /// <summary>A hay rick: a rounded stack of hay, thatched, standing in a field.</summary>
    public static FeatureDef HayRick()
    {
        var m = new Mesh();
        m.Blob(V(0, 12, 0), V(20, 16, 16), Region.Hay, 1160, 0.12f, 2, cutY: -2);
        return new FeatureDef("ml_hayrick", "A hay rick", Features.Centred(m), false, 0, 30, Base: (2, 2), Crush: ByTanks);
    }

    // ---- The cove and the country round it.

    /// <summary>A length of timber jetty, 120 long along x, on its piles, a bollard at its end. It doesn't block.</summary>
    public static FeatureDef Jetty()
    {
        var m = new Mesh();
        m.Box(V(-60, 6, -10), V(60, 8, 10), Region.Timber, Region.Timber);
        for (int i = 0; i < 6; i++)
            for (int s = -1; s <= 1; s += 2)
                m.Tube(V(-56 + i * 22.4, -20, s * 9), V(-56 + i * 22.4, 10, s * 9), 1.8f, 1.8f, Region.TimberDark, 6, cap1: Region.TimberDark);
        m.Tube(V(54, 8, 0), V(54, 13, 0), 2.6f, 2.2f, Region.Rust, 8, cap1: Region.Rust);
        return new FeatureDef("ml_jetty", "A timber jetty", Features.Centred(m, alongOnly: true), false, 10, Blocking: false);
    }

    /// <summary>A boat shed: timber walls, a slate roof, its doors open to the water.</summary>
    public static FeatureDef BoatShed()
    {
        var m = new Mesh();
        m.Box(V(-34, 0, -20), V(34, 22, 20), Region.Timber, Region.Dark);
        RoofGable(m, V(-36, 22, -22), V(36, 22, 22), 14, Region.RoofTile);
        m.Plate(V(34.4f, 10, 0), V(0, 10, 0), V(0, 0, 14), V(0.4, 0, 0), Region.Dark, Region.TimberDark);
        return new FeatureDef("ml_boatshed", "A boat shed", Features.Centred(m), false, 30);
    }

    /// <summary>A channel marker: a post with a red top mark. It doesn't block.</summary>
    public static FeatureDef Marker()
    {
        var m = new Mesh();
        m.Tube(V(0, -30, 0), V(0, 24, 0), 2.4f, 2, Region.TimberDark, 6);
        m.Box(V(-4, 24, -4), V(4, 34, 4), Region.Red, Region.Red);
        return new FeatureDef("ml_marker", "A channel marker", Features.Centred(m), false, 0, Blocking: false);
    }

    /// <summary>A telegraph pole, leaning, its wires cut. It doesn't block.</summary>
    public static FeatureDef Pole()
    {
        var m = new Mesh();
        m.Tube(V(0, -4, 0), V(4, 56, 1), 2.6f, 2, Region.TimberDark, 6);
        m.Box(V(1, 48, -9), V(6, 50, 9), Region.TimberDark, Region.TimberDark);
        return new FeatureDef("ml_pole", "A telegraph pole", Features.Centred(m), false, 2, Blocking: false);
    }

    /// <summary>A granite boulder on the headland, lichened.</summary>
    public static FeatureDef Boulder(int k)
    {
        var m = new Mesh();
        float r = 16 + 10 * k;
        m.Blob(V(0, r * 0.3f, 0), V(r, r * 0.65f, r * 0.8f), Region.Granite, 1170 + k, 0.3f, 1, cutY: -6);
        if (k > 0) m.Blob(V(r * 0.8f, r * 0.15f, r * 0.5f), V(r * 0.5f, r * 0.4f, r * 0.4f), Region.Granite, 1180 + k, 0.3f, 1, cutY: -6);
        return new FeatureDef($"ml_boulder{k}", "A granite boulder", Features.Centred(m), false, 4 + 4 * k);
    }

    /// <summary>Dragon's teeth: three concrete pyramids on a footing, a row of them closing the ground between a line's
    /// end and the deep water.</summary>
    public static FeatureDef Teeth()
    {
        var m = new Mesh { DirtLine = 2, DirtRegion = Region.Sand };
        m.Box(V(-22, -6, -22), V(22, 3, 22), Region.ConcreteDark, Region.ConcreteDark);
        foreach (var (x, z, s) in new[] { (-10.0, -10.0, 1.0), (11.0, -6.0, 0.85), (-2.0, 12.0, 0.95) })
        {
            float b = (float)(10 * s), hgt = (float)(26 * s);
            var c = V(x, 3, z);
            var apex = c + V(0, hgt, 0);
            Vector3[] base4 = [c + V(-b, 0, -b), c + V(b, 0, -b), c + V(b, 0, b), c + V(-b, 0, b)];
            for (int i = 0; i < 4; i++)
            {
                var a = base4[i]; var bb = base4[(i + 1) % 4];
                m.Poly([a, bb, apex], (a + bb) / 2 - c, Region.Concrete);
            }
        }
        return new FeatureDef("ml_teeth", "Concrete dragon's teeth", Features.Centred(m), true, 0, Base: (3, 3));
    }

    /// <summary>An abandoned field gun: its split trail, two spoked wheels, the shield and barrel, rusting.</summary>
    public static FeatureDef FieldGun()
    {
        var m = new Mesh { DirtLine = 3 };
        m.Box(V(-6, 6, -9), V(6, 14, 9), Region.Drab, Region.Drab);                                                   // the cradle
        m.Tube(V(2, 11, 0), V(46, 13, 0), 2.2f, 1.8f, Region.Drab, 8, cap1: Region.Dark);                              // the barrel
        m.Plate(V(6, 16, 0), V(0, 10, 0), V(0, 0, 14), V(0.8, 0, 0), Region.Drab, Region.Rust);                        // the shield
        for (int s = -1; s <= 1; s += 2)
        {
            m.Tube(V(0, 9, s * 14), V(0, 9, s * 17), 9, 9, Region.Rust, 12, cap0: Region.TimberDark, cap1: Region.TimberDark);
            m.Beam(V(-4, 6, s * 4), V(-42, 1, s * 14), 1.6f, 2, Region.Drab);                                         // the split trail
        }
        return new FeatureDef("ml_gun", "An abandoned field gun", Features.Centred(m), false, 150, Base: (3, 2));
    }

    /// <summary>A heap of the village's rubble: broken stone, a charred beam, slates.</summary>
    public static FeatureDef Rubble()
    {
        var m = new Mesh();
        m.Blob(V(0, 4, 0), V(26, 9, 18), Region.StoneDark, 1190, 0.4f, 1, cutY: -3);
        m.Blob(V(14, 3, 8), V(12, 6, 10), Region.Stone, 1191, 0.4f, 1, cutY: -3);
        m.Beam(V(-22, 4, -6), V(18, 10, 4), 1.8f, 1.8f, Region.Burnt);
        return new FeatureDef("ml_rubble", "A heap of rubble", Features.Centred(m), false, 40, Base: (3, 2), Crush: ByHeavy);
    }

    public static List<FeatureDef> All() =>
    [
        Hedgehog(), Stake(), LandingCraft(0), LandingCraft(1), Driftwood(),
        Bunker(), Casemate(), Lighthouse(), Nest(), Wire(),
        Tank(0), Tank(1), Stump(0), Stump(1),
        House(0), House(1), Church(), Barn(), FarmWall(), Windmill(),
        HedgeBush(0), HedgeBush(1), Oak(0), Oak(1), Pine(0), Pine(1), AppleTree(), Marram(0), Marram(1), Reeds(), HayRick(),
        Jetty(), BoatShed(), Marker(), Pole(), Boulder(0), Boulder(1), Teeth(), FieldGun(), Rubble(),
    ];
}
