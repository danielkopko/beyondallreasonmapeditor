// The asteroid's own features, and their atlas. Moonrock: lumpy boulders in four sizes (dust settled on their tops),
// broken slabs tipped up out of the ground, and shards standing where rock blew out of the void pits; all landmarks
// (indestructible, nothing to reclaim), the smallest not blocking. The ship's landing struts (on Meridian's atlas). And
// the void's rubble: an invisible blocker with a 128 × 128 footprint, which no ground unit passes and nothing builds on.
// The moonrock atlas is 1024 × 1024, 2 × 2 cells of 512: regolith-coated rock, dark basalt, fresh-broken faces, dust.
using System.Numerics;
using MapCore.Imaging;

static class Rocks
{
    public const string Texture1 = "landfall_rock_1.tga", Texture2 = "landfall_rock_2.tga";
    static Vector3 V(double x, double y, double z) => new((float)x, (float)y, (float)z);

    /// <summary>A model whose faces turned up are dust, the rest the given rock.</summary>
    static Mesh Dusty() => new() { Rule = (centre, n, r) => n.Y > 0.72f && r == Region.Rock ? Region.RockDust : r };

    static FeatureDef Boulder(string name, string what, Vector3 radii, int seed, bool blocking)
    {
        var m = Dusty();
        m.Blob(V(0, radii.Y * 0.42, 0), radii, Region.Rock, seed, 0.16f, 2, cutY: -2);
        return new FeatureDef(name, $"Meridian Landfall: {what}", Features.Centred(m), true, 0, Blocking: blocking, RockAtlas: true);
    }

    /// <summary>
    /// Faceted rock (a coarse, lumpy polyhedron): its faces turned up dusty, the rest dark basalt or freshly broken,
    /// face by face. No straight edges longer than a facet, no boxes.
    /// </summary>
    static Mesh Faceted(Vector3 radii, int seed, float lumps)
    {
        var m = new Mesh
        {
            Rule = (centre, n, r) => n.Y > 0.82f ? Region.RockDust
                : (Math.Abs(centre.X * 12.9898f + centre.Y * 78.233f + centre.Z * 37.719f + seed) * 43.7585f % 1) < 0.45f ? Region.RockFresh : Region.RockDark,
        };
        m.Blob(V(0, 0, 0), radii, Region.RockDark, seed, lumps, 1);
        return m;
    }

    /// <summary>A slab of the surface broken off and tipped up on its edge, partly buried, with a fragment at its foot.</summary>
    static FeatureDef Slab(string name, double length, double width, double tilt, int seed)
    {
        var m = new Mesh();
        double a = tilt * Math.PI / 180;
        m.Add(Faceted(V(length / 2, 15, width / 2), seed, 0.28f), Matrix4x4.CreateRotationZ((float)a) * Matrix4x4.CreateTranslation(0, (float)(length / 2 * Math.Sin(a) * 0.55), 0));
        m.Add(Faceted(V(24, 14, 20), seed + 1, 0.3f), Matrix4x4.CreateRotationY(0.7f) * Matrix4x4.CreateTranslation((float)(-length * 0.42), 6, (float)(width * 0.25)));
        return new FeatureDef(name, "Meridian Landfall: a broken slab of moonrock", Features.Centred(m), true, 0, RockAtlas: true);
    }

    /// <summary>A shard of rock standing where the blast threw it: a tall, faceted spire, leaning, its foot in rubble.</summary>
    static FeatureDef Shard(string name, double height, double lean, int seed)
    {
        var m = new Mesh();
        m.Add(Faceted(V(17, height / 2, 13), seed, 0.32f), Matrix4x4.CreateRotationZ((float)(-lean * Math.PI / 180)) * Matrix4x4.CreateTranslation(0, (float)(height * 0.38), 0));
        m.Add(Faceted(V(26, 12, 22), seed + 1, 0.35f), Matrix4x4.CreateTranslation(-4, 2, 3));
        return new FeatureDef(name, "Meridian Landfall: a shard of moonrock", Features.Centred(m), true, 0, RockAtlas: true);
    }

    /// <summary>
    /// One of the ship's landing struts, from its footpad on the moonrock (the origin) up to the hull's side (+x): the leg,
    /// its hydraulic ram, a brace, and the mounting block against the hull.
    /// </summary>
    static FeatureDef Strut()
    {
        var m = new Mesh();
        m.Tube(V(0, 0, 0), V(0, 12, 0), 52, 48, Region.Graphite, 20, cap1: Region.Hazard);
        m.Tube(V(0, 12, 0), V(0, 22, 0), 26, 22, Region.Plate, 14, cap1: Region.Graphite);
        foreach (int s in new[] { 1, -1 })
            m.Beam(V(0, 20, s * 16), V(96, 138, s * 26), 7, 7, Region.Graphite);
        m.Tube(V(14, 30, 0), V(80, 122, 0), 11, 11, Region.Plate, 12);
        m.Tube(V(30, 52, 0), V(70, 108, 0), 7, 7, Region.Chrome, 10);
        m.Beam(V(40, 70, -26), V(40, 70, 26), 5, 5, Region.Graphite);
        m.Box(V(94, 118, -40), V(116, 150, 40), Region.Plate, Region.Graphite);
        m.Box(V(96, 126, -42), V(114, 130, 42), Region.Hazard, Region.Hazard);
        return new FeatureDef("lf_strut", "Meridian Landfall: one of Starship Meridian's landing struts", m, true, 0, Base: (8, 6));
    }

    /// <summary>The void's rubble: nothing to see (one tiny face well under the ground), a 128 × 128 footprint that blocks.</summary>
    static FeatureDef Void()
    {
        var m = new Mesh();
        m.Poly([V(-1, -60, -1), V(1, -60, -1), V(0, -60, 1)], V(0, 1, 0), Region.RockDark);
        return new FeatureDef("lf_void", "Meridian Landfall: the void (nothing crosses it but aircraft)", m, true, 0, Base: (8, 8), RockAtlas: true, Invisible: true);
    }

    public static List<FeatureDef> All() =>
    [
        Boulder("lf_boulder_s1", "a small moonrock boulder", V(16, 11, 13), 601, false),
        Boulder("lf_boulder_s2", "a small moonrock boulder", V(13, 9, 15), 602, false),
        Boulder("lf_boulder_m1", "a moonrock boulder", V(36, 24, 30), 611, true),
        Boulder("lf_boulder_m2", "a moonrock boulder", V(30, 28, 34), 612, true),
        Boulder("lf_boulder_l", "a big moonrock boulder", V(68, 46, 56), 621, true),
        Boulder("lf_boulder_xl", "a huge moonrock boulder", V(118, 74, 96), 631, true),
        Slab("lf_slab1", 150, 92, 26, 641),
        Slab("lf_slab2", 120, 70, 38, 642),
        Shard("lf_shard1", 110, 14, 651),
        Shard("lf_shard2", 78, 26, 652),
        Strut(),
        Void(),
    ];

    // ---- The moonrock atlas.

    public static (byte[] Colour, byte[] Extra) Files()
    {
        int size = Mesh.RockAtlasSize, cell = Mesh.Cell;
        var colour = new RgbaImage(size, size);
        var extra = new RgbaImage(size, size);
        double span = cell - 2 * Mesh.Inset;
        Parallel.For(0, size, y =>
        {
            for (int x = 0; x < size; x++)
            {
                int k = y / cell * 2 + x / cell;
                double s = (x % cell - Mesh.Inset + 0.5) / span, t = (y % cell - Mesh.Inset + 0.5) / span;
                s -= Math.Floor(s); t -= Math.Floor(t);
                var c = Cell(k, s, t);
                colour.SetPixel(x, y, new Rgba(B(c.X), B(c.Y), B(c.Z), 0));
                extra.SetPixel(x, y, new Rgba(0, 8, 236, 255));
            }
        });
        return (TgaWriter.Write(colour), TgaWriter.Write(extra));
    }

    static byte B(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

    static Vector3 Cell(int k, double s, double t)
    {
        double f1 = Atlas.Fractal(s, t, 6, 4, 701 + k), f2 = Atlas.Fractal(s, t, 24, 2, 711 + k), f3 = Atlas.Fractal(s, t, 64, 1, 721 + k);
        switch (k)
        {
            case 0:     // regolith-coated rock: grey, mottled, pitted
            {
                double g = 118 + 30 * (f1 - 0.5) + 16 * (f2 - 0.5) + 12 * (f3 - 0.5);
                if (f3 > 0.82) g -= 30;
                return V(g, g * 0.99, g * 0.97);
            }
            case 1:     // dark basalt, with vesicles
            {
                double g = 70 + 18 * (f1 - 0.5) + 10 * (f2 - 0.5);
                if (f3 > 0.78) g *= 0.6;
                return V(g, g, g * 1.03);
            }
            case 2:     // fresh-broken faces: pale, glittering, cracked
            {
                double g = 148 + 24 * (f1 - 0.5) + 16 * (f3 - 0.5);
                if (Math.Abs(Atlas.Fractal(s, t, 10, 3, 731) - 0.5) < 0.02) g -= 70;
                return V(g * 1.01, g, g * 0.98);
            }
            default:    // dust
            {
                double g = 136 + 12 * (f1 - 0.5) + 6 * (f3 - 0.5);
                return V(g, g * 0.985, g * 0.96);
            }
        }
    }
}
