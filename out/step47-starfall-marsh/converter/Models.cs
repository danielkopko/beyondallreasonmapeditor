// The ship and its wreckage. A giant war-transport in angular, layered armour (gunmetal, faded crimson, brass trim,
// cyan seams), broken in two: the aft section (engines, hull with its bridge tower, torn front) lies in the furrow,
// the fore section (torn rear, hull) dives nose-down into the mound, and only the tip of its prow blade shows on the
// mound's crest. Wreckage lies about: armour plates and slabs, hull ribs, girders, engine bells and pods, fins,
// hatches with the ship's crest, a reactor core, spires and small chunks. Centuries of jungle: moss on everything
// that faces up, earth caked on the lower hull, vines down the sides, ferns and saplings growing out of the decks.
// Ship sections are built in the ship's frame (x as on the map, z from the middle line, y from the furrow floor) and
// placed where they were built; wreckage is built round its own origin, with a mirror-image twin (name_m) for the
// south half.
using System.Numerics;

/// <summary>A feature placed: where (game units) and its heading (degrees, 0 facing south, 90 east).</summary>
sealed record Placed(FeatureDef Def, P At, double Heading);

static class Models
{
    static Vector3 V(double x, double y, double z) => new((float)x, (float)y, (float)z);

    // ---- The hull.

    /// <summary>The hull's cross-section (z across, y up from the furrow floor), round from the keel.</summary>
    static readonly Vector2[] Section =
    [
        new(-200, -34), new(200, -34), new(300, 20), new(330, 120), new(318, 190), new(250, 262),
        new(120, 296), new(-120, 296), new(-250, 262), new(-318, 190), new(-330, 120), new(-300, 20),
    ];

    static Vector2[] Ring(float sz = 1, float sy = 1, float dy = 0) => Section.Select(q => new Vector2(q.X * sz, q.Y * sy + dy)).ToArray();

    /// <summary>The region of each edge of the section: the belly burnt, the upper side crimson, the rest plating.</summary>
    static Region EdgeRegion(int k) => k switch { 0 => Region.Burnt, 3 or 9 => Region.Accent, _ => Region.Hull };

    static Vector3 At(float x, Vector2 q) => new(x, q.Y, q.X);
    static Vector2 Edge(int k, float t) => Vector2.Lerp(Section[k], Section[(k + 1) % Section.Length], t);
    /// <summary>An edge's outward normal in the section (z, y).</summary>
    static Vector2 Normal(int k)
    {
        var d = Section[(k + 1) % Section.Length] - Section[k];
        var n = Vector2.Normalize(new Vector2(d.Y, -d.X));
        var mid = Edge(k, 0.5f) - new Vector2(0, 130);
        return Vector2.Dot(n, mid) < 0 ? -n : n;
    }
    static Vector3 N3(Vector2 n) => new(0, n.Y, n.X);

    /// <summary>
    /// Armour, seams, spine and life on a stretch of hull from x0 to x1: layered plates down both sides and over the
    /// shoulders with glowing seams between, a brass belt, a vented spine with chevron spine-fins, vines, ferns.
    /// </summary>
    static void Dress(Mesh m, float x0, float x1, int seed, bool spine = true, bool life = true)
    {
        var rnd = new Random(seed);
        float step = 150;
        for (float x = x0 + 10; x + step <= x1 + 1; x += step)
        {
            float xa = x + 6, xb = x + step - 6, xm = (xa + xb) / 2;
            foreach (int k in new[] { 2, 10, 4, 8 })
            {
                var n = Normal(k);
                var mid = Edge(k, 0.5f) + n * 9;
                var dir = Vector2.Normalize(Section[(k + 1) % Section.Length] - Section[k]);
                float len = Vector2.Distance(Section[k], Section[(k + 1) % Section.Length]);
                bool crimson = k is 2 or 10 ? rnd.NextDouble() < 0.35 : false;
                m.Plate(At(xm, mid), new Vector3((xb - xa) / 2, 0, 0), N3(dir) * (len * 0.44f), N3(n) * 6, crimson ? Region.Accent : Region.Hull, Region.Burnt);
                // A chevron ridge on some side plates.
                if (k is 2 or 10 && rnd.NextDouble() < 0.5)
                {
                    var c = At(xm, mid + n * 8);
                    m.Beam(c + V(-50, -18, 0) * 1, c + V(0, 18, 0), 4, 4, Region.Trim);
                    m.Beam(c + V(0, 18, 0), c + V(50, -18, 0), 4, 4, Region.Trim);
                }
            }
            // Seams between plates (and some glowing ones along the shoulder).
            foreach (int k in new[] { 2, 10 })
            {
                var n = Normal(k);
                var a = Edge(k, 0.08f) + n * 10; var b = Edge(k, 0.92f) + n * 10;
                m.Seam(At(x + 3, a), At(x + 3, b), N3(n), 5);
            }
            foreach (int k in new[] { 3, 9 })
            {
                var n = Normal(k);
                var a = Edge(k, 0.15f) + n * 2; var b = Edge(k, 0.85f) + n * 2;
                if (rnd.NextDouble() < 0.4) m.Seam(At(xa + 10, Vector2.Lerp(a, b, 0.5f)), At(xb - 10, Vector2.Lerp(a, b, 0.5f)), N3(n), 6);
            }
        }
        // The brass belt along the top of the crimson band.
        foreach (int k in new[] { 3, 9 })
        {
            var n = Normal(k);
            var top = Edge(k, 0.92f) + n * 4;
            var dir = Vector2.Normalize(Section[(k + 1) % Section.Length] - Section[k]);
            m.Plate(At((x0 + x1) / 2, top), new Vector3((x1 - x0) / 2, 0, 0), N3(dir) * 5, N3(n) * 4, Region.Trim, Region.Trim);
        }
        if (spine)
        {
            m.Box(V(x0, 290, -60), V(x1, 330, 60), Region.Hull, Region.Vent);
            for (float x = x0 + 60; x < x1 - 80; x += 220)
                m.Slab(V(0, 0, -10), Vector3.UnitX, Vector3.UnitY,
                    [new(x, 326), new(x + 30, 380), new(x + 70, 392), new(x + 140, 326)], 20, Region.Accent, Region.Trim);
        }
        if (life) Life(m, x0, x1, seed + 1);
    }

    /// <summary>Vines down both sides, ferns and the odd sapling on the deck.</summary>
    static void Life(Mesh m, float x0, float x1, int seed, float deckY = 296, float deckHalf = 110)
    {
        var rnd = new Random(seed);
        int vines = (int)((x1 - x0) / 34);
        for (int i = 0; i < vines; i++)
        {
            float x = x0 + (float)rnd.NextDouble() * (x1 - x0);
            int side = rnd.NextDouble() < 0.5 ? 1 : -1;
            // Down the outline from the deck edge, a little off the plates.
            int[] path = side > 0 ? [6, 5, 4, 3, 2] : [7, 8, 9, 10, 11];
            var pts = path.Select(k => Section[k] + Normal(side > 0 ? Math.Max(k - 1, 0) : Math.Min(k, 11)) * 0).ToList();
            float reach = 60 + (float)rnd.NextDouble() * 260, run = 0;
            var line = new List<Vector3>();
            float sway = (float)rnd.NextDouble() * 6;
            for (int k = 0; k + 1 < pts.Count && run < reach; k++)
            {
                var a = pts[k]; var b = pts[k + 1];
                var n = Normal(side > 0 ? path[k + 1] : path[k]) ;
                int steps = 4;
                for (int s = 0; s < steps && run < reach; s++)
                {
                    var q = Vector2.Lerp(a, b, s / (float)steps) + n * 16;
                    line.Add(new Vector3(x + MathF.Sin(run / 30 + sway) * 8, q.Y, q.X));
                    run += Vector2.Distance(a, b) / steps;
                }
            }
            if (line.Count > 1) m.Ribbon(line, Vector3.UnitX, 9 + (float)rnd.NextDouble() * 7, 0.4f, Region.Vine);
        }
        int ferns = (int)((x1 - x0) / 70);
        for (int i = 0; i < ferns; i++)
        {
            var at = V(x0 + rnd.NextDouble() * (x1 - x0), deckY, (rnd.NextDouble() * 2 - 1) * deckHalf);
            Fern(m, at, 40 + (float)rnd.NextDouble() * 40, rnd.Next());
        }
        int saplings = (int)((x1 - x0) / 400);
        for (int i = 0; i < saplings; i++)
        {
            var at = V(x0 + 40 + rnd.NextDouble() * (x1 - x0 - 80), deckY, (rnd.NextDouble() * 2 - 1) * deckHalf * 0.7);
            Sapling(m, at, 90 + (float)rnd.NextDouble() * 70, rnd.Next());
        }
    }

    /// <summary>A fern: fronds arching out and down from a point.</summary>
    public static void Fern(Mesh m, Vector3 at, float size, int seed)
    {
        m.Plant++;
        try { FernFronds(m, at, size, seed); }
        finally { m.Plant--; }
    }

    static void FernFronds(Mesh m, Vector3 at, float size, int seed)
    {
        var rnd = new Random(seed);
        int fronds = 7 + rnd.Next(5);
        for (int f = 0; f < fronds; f++)
        {
            double a = 2 * Math.PI * f / fronds + rnd.NextDouble() * 0.5;
            float len = size * (0.7f + 0.5f * (float)rnd.NextDouble()), rise = len * (0.5f + 0.3f * (float)rnd.NextDouble());
            var dir = V(Math.Cos(a), 0, Math.Sin(a));
            var line = new List<Vector3>();
            for (int k = 0; k <= 6; k++)
            {
                float t = k / 6f;
                line.Add(at + dir * (len * t) + Vector3.UnitY * (rise * t - rise * 1.3f * t * t));
            }
            m.Ribbon(line, V(-Math.Sin(a), 0, Math.Cos(a)), size * 0.28f, 0.15f, Region.Vine);
        }
    }

    /// <summary>A sapling: a leaning trunk and a crown of fronds.</summary>
    public static void Sapling(Mesh m, Vector3 at, float height, int seed)
    {
        m.Plant++;
        try { SaplingTree(m, at, height, seed); }
        finally { m.Plant--; }
    }

    static void SaplingTree(Mesh m, Vector3 at, float height, int seed)
    {
        var rnd = new Random(seed);
        var top = at + V((rnd.NextDouble() - 0.5) * height * 0.3, height, (rnd.NextDouble() - 0.5) * height * 0.3);
        m.Tube(at - V(0, 6, 0), top, height * 0.06f, height * 0.03f, Region.Earth, 6);
        Fern(m, top, height * 0.55f, rnd.Next());
        Fern(m, top - V(0, height * 0.12, 0), height * 0.45f, rnd.Next());
    }

    /// <summary>
    /// A torn end of the hull at <paramref name="xEdge"/> (jagged by up to 60), from a sound ring at <paramref name="xBase"/>:
    /// the outer skin, the inner skin and the rim between them burnt, a bulkhead inside, decks and ribs sticking out,
    /// cables hanging, and the reactor's glow inside. <paramref name="dir"/> is +1 if the tear faces +x.
    /// </summary>
    static void Torn(Mesh m, float xBase, float xEdge, int dir, int seed)
    {
        var rnd = new Random(seed);
        // The section subdivided three to an edge, each point's tear jittered.
        var ring = new List<Vector2>();
        for (int k = 0; k < Section.Length; k++)
            for (int s = 0; s < 3; s++) ring.Add(Edge(k, s / 3f));
        int nr = ring.Count;
        var tear = Enumerable.Range(0, nr).Select(i => xEdge - dir * (float)(rnd.NextDouble() * 60 + (i % 2) * 18)).ToArray();
        Vector3 Out(int i, float x) => new(x, ring[i % nr].Y, ring[i % nr].X);
        Vector3 In(int i, float x) { var q = ring[i % nr] * 0.9f + new Vector2(0, 14); return new(x, q.Y, q.X); }
        var axis = new Vector2(0, 130);
        for (int i = 0; i < nr; i++)
        {
            int j = (i + 1) % nr;
            var mid = (ring[i] + ring[j]) / 2 - axis;
            var outward = new Vector3(0, mid.Y, mid.X);
            int k = i / 3;
            m.Quad(Out(i, xBase), Out(j, xBase), Out(j, tear[j]), Out(i, tear[i]), outward, EdgeRegion(k));
            m.Quad(In(i, xBase + dir * 30), In(j, xBase + dir * 30), In(j, tear[j]), In(i, tear[i]), -outward, Region.Burnt);
            m.Quad(Out(i, tear[i]), Out(j, tear[j]), In(j, tear[j]), In(i, tear[i]), new Vector3(dir, 0, 0), Region.Burnt);
        }
        // The bulkhead inside (so the hull isn't hollow to the eye), with a seam of light round its rim.
        float xb = xBase + dir * 30;
        m.Poly(Enumerable.Range(0, nr).Select(i => In(i, xb)).ToList(), new Vector3(dir, 0, 0), Region.Burnt);
        m.Seam(V(xb + dir, 60, -200), V(xb + dir, 60, 200), new Vector3(dir, 0, 0), 8);
        // Decks, torn off raggedly beyond the tear.
        foreach (float y in new[] { 104f, 206f })
            for (int s = -3; s < 3; s++)
            {
                float z0 = s * 90, z1 = z0 + 90;
                float reach = (float)(rnd.NextDouble() * 70 - 20);
                m.Box(V(Math.Min(xb, xEdge + dir * reach), y, z0 + 2), V(Math.Max(xb, xEdge + dir * reach), y + 10, z1 - 2), Region.Burnt, Region.Hull);
            }
        // Ribs (frames) bent out beyond the tear.
        for (int i = 0; i < 5; i++)
        {
            int k = rnd.Next(nr);
            var p0 = Out(k, tear[k] - dir * 10);
            var p1 = p0 + V(dir * (40 + rnd.NextDouble() * 70), (rnd.NextDouble() - 0.3) * 60, (rnd.NextDouble() - 0.5) * 60);
            m.Beam(p0, p1, 8, 10, Region.Girder);
        }
        // Cables hanging from the upper deck to the ground.
        for (int i = 0; i < 6; i++)
        {
            float z = (float)(rnd.NextDouble() * 2 - 1) * 220;
            var a = V(xEdge - dir * 10, 206, z);
            var line = new List<Vector3>();
            float drop = 160 + (float)rnd.NextDouble() * 60;
            for (int s = 0; s <= 6; s++) line.Add(a + V(dir * 30 * Math.Sin(s / 6.0 * Math.PI) , -drop * s / 6, 0));
            m.Ribbon(line, Vector3.UnitZ, 6, 1, Region.Burnt);
        }
        // The reactor core, cracked open, still faintly alight.
        m.Tube(V(xb, 150, 0), V(xEdge + dir * 20, 150, 0), 58, 58, Region.Glow, 12, stretch: true);
        m.Tube(V(xb + dir * 20, 150, 0), V(xb + dir * 30, 150, 0), 66, 66, Region.Trim, 12);
        m.Tube(V(xEdge, 150, 0), V(xEdge + dir * 10, 150, 0), 66, 66, Region.Trim, 12);
    }

    /// <summary>A feature from a mesh built in the ship's frame: centred, and placed back where it was built.</summary>
    static Placed AtBuilt(string name, string description, Mesh m)
    {
        var (min, max) = m.Solid(above: 2);
        // Centred along x only: a section stays on the middle line, so the map stays fair.
        var def = new FeatureDef(name, description, Features.Centred(m, alongOnly: true), true, 0);
        return new Placed(def, new P((min.X + max.X) / 2, Layout.Axis), 0);
    }

    // ---- The ship's sections.

    public static Placed Engines()
    {
        var m = new Mesh { DirtLine = 14 };
        float x0 = (float)Layout.Tail, x1 = (float)Layout.AftEngines;
        m.Loft([(x0, Ring(0.92f, 0.9f, 12)), (x0 + 70, Ring()), (x1, Ring())], EdgeRegion, Region.Hull, null);
        // Six bells in the stern, in two rows of three.
        foreach (float y in new[] { 78f, 196f })
            foreach (float z in new[] { -190f, 0f, 190f })
            {
                var c = V(x0, y, z);
                m.Tube(c, c - V(80, 0, 0), 70, 90, Region.Hull, 16);
                m.Tube(c - V(2, 0, 0), c - V(78, 0, 0), 66, 86, Region.Burnt, 16, inside: true);
                m.Poly(Enumerable.Range(0, 16).Select(k => c - V(8, 0, 0) + V(0, Math.Sin(k * Math.PI / 8) * 66, Math.Cos(k * Math.PI / 8) * 66)).ToList(), -Vector3.UnitX, Region.Nozzle, stretch: true);
                m.Tube(c - V(76, 0, 0), c - V(84, 0, 0), 93, 93, Region.Trim, 16);
            }
        // The pods either side, on pylons, each with its own bell and a fin canted out.
        foreach (int side in new[] { 1, -1 })
        {
            float pz = side * 455;
            var pod = new List<(float, Vector2[])>();
            foreach (var (x, r) in new[] { (x0 - 30, 92f), (x0 + 60, 104f), (x1 - 60, 104f), (x1 + 40, 70f) })
                pod.Add((x, Enumerable.Range(0, 8).Select(k => new Vector2(pz + MathF.Cos(k * MathF.PI / 4 + MathF.PI / 8) * r, 120 + MathF.Sin(k * MathF.PI / 4 + MathF.PI / 8) * r)).ToArray()));
            m.Loft(pod, k => k is 1 or 2 ? Region.Accent : Region.Hull, Region.Hull, Region.Burnt);
            var pc = V(x0 - 30, 120, pz);
            m.Tube(pc, pc - V(70, 0, 0), 76, 92, Region.Hull, 16);
            m.Tube(pc - V(2, 0, 0), pc - V(68, 0, 0), 72, 88, Region.Burnt, 16, inside: true);
            m.Poly(Enumerable.Range(0, 16).Select(k => pc - V(8, 0, 0) + V(0, Math.Sin(k * Math.PI / 8) * 72, Math.Cos(k * Math.PI / 8) * 72)).ToList(), -Vector3.UnitX, Region.Nozzle, stretch: true);
            m.Tube(pc - V(66, 0, 0), pc - V(74, 0, 0), 95, 95, Region.Trim, 16);
            m.Box(V(x0 + 80, 92, side > 0 ? 300 : -372), V(x1 - 40, 150, side > 0 ? 372 : -300), Region.Girder, Region.Hull);
            var up = Vector3.Normalize(V(0, 1, side * 0.45));
            m.Slab(V(0, 200, pz) , Vector3.UnitX, up, [new(x0 + 20, 0), new(x0 - 30, 190), new(x0 + 60, 205), new(x0 + 230, 0)], 18, Region.Accent, Region.Trim);
            m.Seam(V(x0 + 40, 120, pz + side * 105), V(x1 - 80, 120, pz + side * 105), V(0, 0, side), 7);
        }
        // The great dorsal fin.
        m.Slab(V(0, 0, -16), Vector3.UnitX, Vector3.UnitY, [new(x0 + 30, 290), new(x0 - 40, 600), new(x0 + 70, 620), new(x0 + 300, 292)], 32, Region.Accent, Region.Trim);
        m.Seam(V(x0 + 34, 300, 17), V(x0 - 32, 590, 17), Vector3.UnitZ, 6);
        m.Seam(V(x0 + 34, 300, -17), V(x0 - 32, 590, -17), -Vector3.UnitZ, 6);
        // Vent banks on the engine block's flanks.
        foreach (int side in new[] { 1, -1 })
            m.Plate(V(x0 + 190, 70, side * 334), V(110, 0, 0), V(0, 34, 0), V(0, 0, side * 6), Region.Vent, Region.Trim, stretchFace: true);
        Dress(m, x0 + 70, x1, 11, spine: false);
        return AtBuilt("sf_ship_engines", "Starfall wreck: the engines", m);
    }

    public static Placed AftHull()
    {
        var m = new Mesh { DirtLine = 14 };
        float x0 = (float)Layout.AftEngines, x1 = (float)Layout.AftHull;
        m.Loft([(x0, Ring()), (x1, Ring())], EdgeRegion, null, null);
        Dress(m, x0, x1, 21, spine: false);
        // The bridge tower, stepped back in layers, its windows dark.
        m.Box(V(1960, 290, -205), V(2420, 372, 205), Region.Hull, Region.Hull);
        m.Box(V(2010, 372, -152), V(2350, 452, 152), Region.Accent, Region.Hull);
        m.Box(V(2070, 452, -116), V(2310, 516, 116), Region.Hull, Region.Vent);
        foreach (int side in new[] { 1, -1 })
        {
            m.Quad(V(2080, 494, side * 118), V(2300, 494, side * 118), V(2300, 470, side * 118), V(2080, 470, side * 118), V(0, 0, side), Region.Window, stretch: true);
            m.Quad(V(2060, 430, side * 154), V(2180, 430, side * 154), V(2180, 384, side * 154), V(2060, 384, side * 154), V(0, 0, side), Region.Crest, stretch: true);
            m.Quad(V(2210, 440, side * 154), V(2330, 440, side * 154), V(2330, 424, side * 154), V(2210, 424, side * 154), V(0, 0, side), Region.Window, stretch: true);
            m.Seam(V(1975, 340, side * 207), V(2405, 340, side * 207), V(0, 0, side), 6);
            // The ship's crest on the hull's crimson band.
            var n = Normal(side > 0 ? 3 : 9);
            var mid = Edge(side > 0 ? 3 : 9, 0.5f) + n * 6;
            m.Plate(At(2200, mid), V(80, 0, 0), N3(Vector2.Normalize(Section[side > 0 ? 4 : 10] - Section[side > 0 ? 3 : 9])) * 34, N3(n) * 3, Region.Crest, Region.Trim, stretchFace: true);
        }
        m.Quad(V(2311, 494, -110), V(2311, 494, 110), V(2311, 470, 110), V(2311, 470, -110), Vector3.UnitX, Region.Window, stretch: true);
        m.Quad(V(2421, 360, -190), V(2421, 360, 190), V(2421, 300, 190), V(2421, 300, -190), Vector3.UnitX, Region.Vent, stretch: true);
        m.Tube(V(2140, 516, 60), V(2148, 660, 64), 7, 3, Region.Trim, 6);
        m.Tube(V(2250, 516, -70), V(2244, 610, -66), 6, 3, Region.Trim, 6);
        m.Tube(V(2200, 516, 0), V(2200, 548, 0), 46, 14, Region.Hull, 10, cap1: Region.Trim);
        m.Box(V(x0, 290, -60), V(1960, 330, 60), Region.Hull, Region.Vent);
        m.Box(V(2420, 290, -60), V(x1, 330, 60), Region.Hull, Region.Vent);
        Life(m, 1960, 2420, 22, 372, 190);
        Life(m, 2010, 2350, 23, 452, 140);
        return AtBuilt("sf_ship_aft_hull", "Starfall wreck: the aft hull and bridge", m);
    }

    public static Placed AftBreak()
    {
        var m = new Mesh { DirtLine = 14 };
        float x0 = (float)Layout.AftHull, x1 = (float)Layout.AftEnd;
        m.Loft([(x0, Ring()), (x1 - 120, Ring())], EdgeRegion, null, null);
        Dress(m, x0, x1 - 130, 31);
        Torn(m, x1 - 120, x1, 1, 32);
        return AtBuilt("sf_ship_aft_break", "Starfall wreck: the aft section's torn end", m);
    }

    static Matrix4x4 Pitched => Matrix4x4.CreateTranslation(-(float)Layout.ForeStart, 0, 0) * Matrix4x4.CreateRotationZ(-(float)(Layout.Pitch * Math.PI / 180)) *
                                Matrix4x4.CreateTranslation((float)Layout.ForeStart, 0, 0);

    public static Placed ForeBreak()
    {
        var s = new Mesh { DirtLine = 14 };
        float x0 = (float)Layout.ForeStart, x1 = (float)Layout.ForeBreak;
        s.Loft([(x0 + 120, Ring()), (x1, Ring())], EdgeRegion, null, null);
        Dress(s, x0 + 130, x1, 41);
        Torn(s, x0 + 120, x0, -1, 42);
        var m = new Mesh();
        m.Add(s, Pitched);
        return AtBuilt("sf_ship_fore_break", "Starfall wreck: the fore section's torn end", m);
    }

    public static Placed ForeHull()
    {
        var noise = new Noise(51);
        // Nearer the mound, the hull is caked in the dirt it ploughed.
        var s = new Mesh
        {
            DirtLine = 14,
            Rule = (c, n, r) => c.X > 4720 + 90 * noise.At(c.Y / 60, c.Z / 60) && r is Region.Hull or Region.Accent or Region.Trim ? Region.Earth : r,
        };
        float x0 = (float)Layout.ForeBreak, x1 = (float)Layout.NoseEnd;
        s.Loft([(x0, Ring()), (x1 - 300, Ring()), (x1, Ring(0.96f, 0.95f))], EdgeRegion, null, Region.Earth);
        Dress(s, x0, x1 - 260, 61);
        var m = new Mesh();
        m.Add(s, Pitched);
        return AtBuilt("sf_ship_fore_hull", "Starfall wreck: the fore hull, diving into the mound", m);
    }

    /// <summary>
    /// The prow blade's tip and broken spires out of the mound's crest: a layered blade of plating with a crimson band,
    /// brass leading edge and chevron ridges, hull plates heaved up beside it, all deep in moss, vines and saplings.
    /// Symmetric across the middle line but for the plants.
    /// </summary>
    public static FeatureDef Prow()
    {
        var m = new Mesh { DirtLine = 10, MossFrom = 0.3f };
        m.Slab(V(0, 0, -26), Vector3.UnitX, Vector3.UnitY, [new(-300, -160), new(-210, 190), new(-90, 270), new(230, 100), new(220, -160)], 52, Region.Hull, Region.Burnt);
        // Layered armour on both faces: a crimson band, plates stepped out, a brass leading edge.
        foreach (int side in new[] { 1, -1 })
        {
            float z = side * 27;
            m.Slab(V(0, 0, side > 0 ? 26 : -34), Vector3.UnitX, Vector3.UnitY, [new(-240, 60), new(-190, 175), new(-80, 240), new(140, 120), new(150, 60)], 8, Region.Accent, Region.Trim);
            m.Slab(V(0, 0, side > 0 ? 34 : -40), Vector3.UnitX, Vector3.UnitY, [new(-200, -40), new(-170, 50), new(100, 50), new(120, -40)], 6, Region.Hull, Region.Burnt);
            for (int k = 0; k < 3; k++)
            {
                var c = V(-120 + k * 90, 150 - k * 30, z + side * 12);
                m.Beam(c + V(-40, -26, 0), c + V(0, 14, 0), 5, 4, Region.Trim);
                m.Beam(c + V(0, 14, 0), c + V(40, -26, 0), 5, 4, Region.Trim);
            }
            m.Seam(V(-196, 186, z + side * 9), V(-86, 262, z + side * 9), V(0, 0, side), 7);
            m.Seam(V(-180, 52, z + side * 14), V(110, 52, z + side * 14), V(0, 0, side), 5);
            m.Tube(V(-60, -60, side * 140), V(-20, 230, side * 158), 18, 9, Region.Hull, 8);
            m.Tube(V(-36, 110, side * 150), V(-34, 124, side * 151), 22, 22, Region.Trim, 8);
            m.Tube(V(-20, 230, side * 158), V(-14, 262, side * 160), 9, 0.5f, Region.Burnt, 8);
            m.Plate(V(120, 20, side * 130), V(90, 34, 0), V(0, 22, side * 56), V(0, 7, 0), Region.Hull, Region.Burnt);
            m.Plate(V(-150, 10, side * 180), V(66, -24, 0), V(0, 44, side * 12), V(0, 0, side * 7), Region.Accent, Region.Burnt);
        }
        m.Beam(V(-300, -160, 0), V(-210, 190, 0), 30, 8, Region.Trim);
        m.Beam(V(-210, 190, 0), V(-90, 270, 0), 30, 8, Region.Trim);
        var rnd = new Random(77);
        foreach (int side in new[] { 1, -1 })
        {
            Sapling(m, V(60, 8, side * 210), 150 + 30 * side, 71 + side);
            Sapling(m, V(-230, 8, side * 120), 110, 79 + side);
            Fern(m, V(-180, 8, side * 90), 70, 73 + side);
            Fern(m, V(180, 8, side * 70), 60, 75 + side);
            Fern(m, V(0, 8, side * 260), 80, 85 + side);
        }
        // Vines down the blade's flanks, swaying.
        for (int i = 0; i < 30; i++)
        {
            float x = -190 + (float)rnd.NextDouble() * 380;
            int side = i % 2 == 0 ? 1 : -1;
            float y0 = x < -90 ? 190 + (x + 210) * 0.66f : 270 - (x + 90) * 0.53f;
            float reach = (y0 + 10) * (0.4f + 0.6f * (float)rnd.NextDouble());
            float ph = (float)rnd.NextDouble() * 6;
            var line = Enumerable.Range(0, 9).Select(k => V(x + Math.Sin(k * 0.9 + ph) * 10, y0 - k * reach / 8, side * (44 + 3 * Math.Sin(k + ph)))).ToList();
            m.Ribbon(line, Vector3.UnitX, 10 + (float)rnd.NextDouble() * 10, 0.3f, Region.Vine);
        }
        return new FeatureDef("sf_ship_prow", "Starfall wreck: the buried prow's blade", Features.Centred(m), true, 0);
    }

    // ---- Wreckage. Each is built round its origin; the south half uses its mirror image.

    public static FeatureDef Plate(int seed)
    {
        var m = new Mesh { DirtLine = -4 };
        var rnd = new Random(seed);
        float w = 150 + (float)rnd.NextDouble() * 60, d = 90 + (float)rnd.NextDouble() * 40;
        var tilt = Matrix4x4.CreateRotationZ((float)(0.35 + rnd.NextDouble() * 0.3));
        var a = new Mesh { DirtLine = -4 };
        a.Plate(V(0, 0, 0), V(w / 2, 0, 0), V(0, 0, d / 2), V(0, 7, 0), seed % 2 == 0 ? Region.Accent : Region.Hull, Region.Burnt);
        a.Plate(V(w / 2 + 34, -16, 0), V(36, -16, 0), V(0, 0, d / 2), V(4, 7, 0), Region.Hull, Region.Burnt);
        a.Seam(V(-w / 2 + 10, 8, d / 2 - 12), V(w / 2 - 10, 8, d / 2 - 12), Vector3.UnitY, 5);
        m.Add(a, tilt * Matrix4x4.CreateTranslation(0, w * 0.18f, 0));
        if (seed % 3 == 0) Fern(m, V(-w * 0.2, 0, -d * 0.7), 34, seed);
        return new FeatureDef($"sf_wreck_plate{seed % 3}", "Starfall wreckage: a bent armour plate", Features.Centred(m), false, 60);
    }

    public static FeatureDef Slab()
    {
        var m = new Mesh { DirtLine = 4 };
        var s = new Mesh { DirtLine = -1e9f };
        s.Slab(V(0, 0, -12), Vector3.UnitX, Vector3.UnitY, [new(-140, -40), new(-120, 70), new(0, 130), new(30, 10)], 24, Region.Accent, Region.Burnt);
        s.Slab(V(0, 0, -12), Vector3.UnitX, Vector3.UnitY, [new(30, 10), new(0, 130), new(120, 70), new(150, -40)], 24, Region.Hull, Region.Burnt);
        s.Seam(V(-118, 66, 13), V(-2, 124, 13), Vector3.UnitZ, 5);
        m.Add(s, Matrix4x4.CreateRotationX(-0.55f));
        Fern(m, V(80, 0, 60), 46, 5);
        return new FeatureDef("sf_wreck_slab", "Starfall wreckage: a chevron armour slab", Features.Centred(m), false, 110);
    }

    public static FeatureDef Rib()
    {
        var m = new Mesh { DirtLine = 6 };
        var pts = Enumerable.Range(0, 13).Select(k => { double a = Math.PI * k / 12; return V(Math.Cos(a) * 150, Math.Sin(a) * 190 - 20, 0); }).ToList();
        for (int k = 0; k + 1 < pts.Count; k++) m.Beam(pts[k], pts[k + 1], 14, 18, Region.Girder);
        for (int k = 2; k + 2 < pts.Count; k += 3) m.Beam(pts[k] + V(0, 0, -14), pts[k] + V(0, 0, -60), 6, 6, Region.Girder);
        var rnd = new Random(81);
        for (int i = 0; i < 9; i++)
        {
            var top = pts[2 + rnd.Next(9)];
            var line = Enumerable.Range(0, 6).Select(k => top + V(Math.Sin(k + i) * 4, -k * (top.Y + 15) / 5 * (0.4 + 0.5 * rnd.NextDouble()), 15)).ToList();
            m.Ribbon(line, Vector3.UnitX, 10, 0.4f, Region.Vine);
        }
        Fern(m, V(-150, 0, 20), 40, 82);
        return new FeatureDef("sf_wreck_rib", "Starfall wreckage: a hull rib", Features.Centred(m), false, 130);
    }

    public static FeatureDef Girder()
    {
        var m = new Mesh { DirtLine = 4 };
        var s = new Mesh();
        for (int x = -160; x < 160; x += 40)
        {
            s.Beam(V(x, 0, -30), V(x + 40, 0, -30), 7, 7, Region.Girder);
            s.Beam(V(x, 0, 30), V(x + 40, 0, 30), 7, 7, Region.Girder);
            s.Beam(V(x, 0, -30), V(x + 40, 0, 30), 4, 4, Region.Girder);
            s.Beam(V(x, 40, -30), V(x + 40, 40, -30), 6, 6, Region.Girder);
            s.Beam(V(x, 0, -30), V(x, 40, -30), 4, 4, Region.Girder);
        }
        s.Beam(V(160, 0, -30), V(196, 30, -50), 7, 7, Region.Burnt);
        m.Add(s, Matrix4x4.CreateRotationZ(0.16f) * Matrix4x4.CreateTranslation(0, 26, 0));
        Fern(m, V(-170, 0, 0), 38, 91);
        return new FeatureDef("sf_wreck_girder", "Starfall wreckage: a broken girder", Features.Centred(m), false, 120);
    }

    public static FeatureDef Nozzle()
    {
        var m = new Mesh { DirtLine = 6 };
        var a = V(-90, 70, 0); var b = V(90, 86, 0);
        m.Tube(a, b, 60, 92, Region.Hull, 16);
        m.Tube(a + V(4, 0, 0), b - V(2, 0, 0), 56, 88, Region.Burnt, 16, inside: true);
        m.Poly(Enumerable.Range(0, 16).Select(k => a + V(10, Math.Sin(k * Math.PI / 8) * 56, Math.Cos(k * Math.PI / 8) * 56)).ToList(), Vector3.UnitX, Region.Nozzle, stretch: true);
        m.Tube(b - V(4, 0, 0), b + V(4, 0, 0), 96, 96, Region.Trim, 16);
        m.Tube(a - V(4, 0, 0), a + V(8, 0, 0), 64, 64, Region.Trim, 16, cap0: Region.Burnt);
        Fern(m, V(-30, 0, 90), 40, 101);
        return new FeatureDef("sf_wreck_nozzle", "Starfall wreckage: a fallen engine bell", Features.Centred(m), false, 190);
    }

    public static FeatureDef Pod()
    {
        var m = new Mesh { DirtLine = 6 };
        var s = new Mesh();
        var stations = new List<(float, Vector2[])>();
        foreach (var (x, r) in new[] { (-150f, 66f), (-110f, 80f), (110f, 80f), (150f, 56f) })
            stations.Add((x, Enumerable.Range(0, 8).Select(k => new Vector2(MathF.Cos(k * MathF.PI / 4 + MathF.PI / 8) * r, 70 + MathF.Sin(k * MathF.PI / 4 + MathF.PI / 8) * r)).ToArray()));
        s.Loft(stations, k => k is 1 or 2 ? Region.Accent : Region.Hull, Region.Burnt, Region.Hull);
        s.Slab(V(0, 140, -9), Vector3.UnitX, Vector3.UnitY, [new(-100, 0), new(-130, 70), new(-60, 76), new(40, 0)], 18, Region.Accent, Region.Trim);
        s.Seam(V(-100, 70, 81), V(100, 70, 81), Vector3.UnitZ, 6);
        s.Seam(V(-100, 70, -81), V(100, 70, -81), -Vector3.UnitZ, 6);
        m.Add(s, Matrix4x4.CreateRotationZ(-0.12f) * Matrix4x4.CreateRotationY(0.1f));
        Fern(m, V(120, 0, 70), 44, 111);
        return new FeatureDef("sf_wreck_pod", "Starfall wreckage: a broken engine pod", Features.Centred(m), false, 225);
    }

    public static FeatureDef Fin()
    {
        var m = new Mesh { DirtLine = 6 };
        var s = new Mesh();
        s.Slab(V(0, 0, -10), Vector3.UnitX, Vector3.UnitY, [new(-60, -40), new(-90, 160), new(-30, 240), new(90, -40)], 20, Region.Accent, Region.Trim);
        s.Seam(V(-88, 150, 11), V(-58, -30, 11), Vector3.UnitZ, 5);
        m.Add(s, Matrix4x4.CreateRotationX(0.22f));
        Fern(m, V(40, 0, -40), 36, 121);
        return new FeatureDef("sf_wreck_fin", "Starfall wreckage: a fin", Features.Centred(m), false, 90);
    }

    public static FeatureDef Hatch()
    {
        var m = new Mesh { DirtLine = 3 };
        var s = new Mesh();
        var hex = Enumerable.Range(0, 6).Select(k => new Vector2(MathF.Cos(k * MathF.PI / 3) * 80, MathF.Sin(k * MathF.PI / 3) * 80)).ToList();
        s.Slab(V(0, 0, 0), Vector3.UnitX, Vector3.UnitZ, hex, -14, Region.Hull, Region.Trim);
        s.Poly(hex.Select(q => V(q.X * 0.86, 15.5, q.Y * 0.86)).ToList(), Vector3.UnitY, Region.Crest, stretch: true);
        m.Add(s, Matrix4x4.CreateRotationX(0.25f) * Matrix4x4.CreateTranslation(0, 16, 0));
        return new FeatureDef("sf_wreck_hatch", "Starfall wreckage: a hatch with the ship's crest", Features.Centred(m), false, 75);
    }

    public static FeatureDef Reactor()
    {
        var m = new Mesh { DirtLine = 6 };
        m.Tube(V(-110, 64, 0), V(110, 64, 0), 64, 64, Region.Glow, 12, stretch: true);
        foreach (float x in new[] { -110f, -40f, 40f, 110f })
            m.Tube(V(x - 8, 64, 0), V(x + 8, 64, 0), 72, 72, Region.Trim, 12, cap0: x < 0 ? Region.Burnt : null, cap1: x > 0 ? Region.Burnt : null);
        m.Box(V(-70, 0, -80), V(70, 40, 80), Region.Girder, Region.Hull);
        m.Plate(V(0, 130, 0), V(100, 0, 0), V(0, 0, 50), V(0, 6, 0), Region.Vent, Region.Burnt, stretchFace: true);
        Fern(m, V(-120, 0, 70), 40, 131);
        return new FeatureDef("sf_wreck_reactor", "Starfall wreckage: a reactor core, still faintly alight", Features.Centred(m), false, 300);
    }

    public static FeatureDef Spire()
    {
        var m = new Mesh { DirtLine = 4 };
        m.Tube(V(0, -20, 0), V(30, 200, 10), 16, 9, Region.Hull, 8);
        foreach (float t in new[] { 0.3f, 0.6f })
        {
            var c = V(30 * t, -20 + 220 * t, 10 * t);
            m.Tube(c - V(0, 5, 0), c + V(0, 5, 0), 20 - 8 * t, 20 - 8 * t, Region.Trim, 8);
        }
        m.Tube(V(30, 200, 10), V(36, 236, 12), 9, 0.5f, Region.Burnt, 8);
        Fern(m, V(0, 0, 0), 40, 141);
        return new FeatureDef("sf_wreck_spire", "Starfall wreckage: a broken antenna spire", Features.Centred(m), false, 45);
    }

    public static FeatureDef Shard()
    {
        var m = new Mesh { DirtLine = 4 };
        m.Plate(V(-60, 60, 0), V(70, 0, 0), V(0, 60, -30), V(0, 4, 8), Region.Accent, Region.Burnt);
        m.Plate(V(60, 50, -14), V(56, -10, 30), V(0, 56, -20), V(-3, 3, 8), Region.Hull, Region.Burnt);
        m.Seam(V(-120, 100, -18), V(0, 100, -18), V(0, 0.4, 1), 5);
        return new FeatureDef("sf_wreck_shard", "Starfall wreckage: a shard of hull wall", Features.Centred(m), false, 150);
    }

    public static FeatureDef Chunk(int k)
    {
        var m = new Mesh { DirtLine = 3 };
        var rnd = new Random(150 + k);
        switch (k)
        {
            case 0:
                m.Slab(V(0, 0, -18), Vector3.UnitX, Vector3.UnitY, [new(-30, -6), new(-20, 28), new(26, 18), new(34, -6)], 36, Region.Hull, Region.Burnt);
                break;
            case 1:
                m.Box(V(-24, -6, -20), V(24, 30, 20), Region.Hull, Region.Vent);
                m.Box(V(-10, 30, -8), V(14, 40, 10), Region.Trim, Region.Burnt);
                break;
            default:
                m.Plate(V(0, 14, 0), V(32, 10, 0), V(0, 4, 26), V(-3, 3, 0), Region.Accent, Region.Burnt);
                m.Beam(V(-30, 0, 20), V(20, 30, -10), 4, 4, Region.Girder);
                break;
        }
        _ = rnd;
        return new FeatureDef($"sf_wreck_chunk{k}", "Starfall wreckage: a scrap of hull", Features.Centred(m), false, 20);
    }

    public static FeatureDef Reeds(int k)
    {
        var m = new Mesh { Overgrown = false };
        var rnd = new Random(170 + k);
        int blades = 26 + 10 * k;
        for (int i = 0; i < blades; i++)
        {
            double a = rnd.NextDouble() * Math.PI * 2, r = rnd.NextDouble() * (22 + 10 * k);
            var at = V(Math.Cos(a) * r, -4, Math.Sin(a) * r);
            float h = 40 + (float)rnd.NextDouble() * (30 + 15 * k);
            var lean = V(Math.Cos(a), 0, Math.Sin(a)) * (float)(rnd.NextDouble() * 0.35) ;
            var line = Enumerable.Range(0, 5).Select(s => at + (Vector3.UnitY + lean * (s / 4f)) * (h * s / 4f)).ToList();
            m.Ribbon(line, V(-Math.Sin(a + 1.2), 0, Math.Cos(a + 1.2)), 4, 0.2f, Region.Reed);
            if (rnd.NextDouble() < 0.18)
            {
                var top = line[^1] - Vector3.UnitY * 6;
                m.Tube(top - Vector3.UnitY * 14, top, 2.4f, 2.4f, Region.Burnt, 6);   // a bulrush head
            }
        }
        return new FeatureDef($"sf_reeds{k}", "Marsh reeds", Features.Centred(m), false, 0, 15, Blocking: false);
    }

    /// <summary>Every wreck piece by kind, and the reeds.</summary>
    public static Dictionary<string, FeatureDef> Wreckage()
    {
        var list = new List<FeatureDef> { Plate(0), Plate(1), Plate(2), Slab(), Rib(), Girder(), Nozzle(), Pod(), Fin(), Hatch(), Reactor(), Spire(), Shard(), Chunk(0), Chunk(1), Chunk(2) };
        return list.ToDictionary(d => d.Name);
    }

    public static FeatureDef MirrorOf(FeatureDef d) => d with { Name = d.Name + "_m", Mesh = d.Mesh.Mirrored() };
}
