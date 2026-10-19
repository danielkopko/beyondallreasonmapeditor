using System.Numerics;
using MapCore.Imaging;

namespace MapCore.Structures;

/// <summary>
/// The guardian tower: a spire on a stepped plinth, braced by four struts, crowned by four rocket pods that point
/// up and out, with a sensor mast above them. Sleek and futuristic in shape; in the ancient look it is coloured as an
/// old weapon would be, bronze gone green with gold bands and glowing runes on weathered stone; it can also be
/// concrete or metal. Its own atlas (<see cref="GuardianTexture1"/>, <see cref="GuardianTexture2"/>). In its own frame x
/// and z run across it and y up; it fills <see cref="GuardianSize"/> square (3 by 3 footprint squares, a commander's).
/// The rocket it fires, for show, is <see cref="GuardianRocket"/>.
/// </summary>
public static partial class StructureModels
{
    public const string GuardianTexture1 = "map_guardian_1.tga", GuardianTexture2 = "map_guardian_2.tga";

    /// <summary>The tower's footprint, game units on each side (3 squares), and its height.</summary>
    public const float GuardianSize = 48, GuardianHeight = 146;

    /// <summary>Where its rockets leave the pods: above its ground, and out from its middle (as the gadget's POD_UP, POD_OUT).</summary>
    public const float GuardianPodUp = 124, GuardianPodOut = 17;

    /// <summary>The guardian atlas's cells, on the same 4 × 4 grid as the structures' atlas.</summary>
    internal static class GuardianCell
    {
        public const Region Stone = (Region)0, StoneDark = (Region)1, Bronze = (Region)2, Verdigris = (Region)3,
            Gold = (Region)4, DarkBronze = (Region)5, GlowTeal = (Region)6, Runes = (Region)7,
            Concrete = (Region)8, ConcreteDark = (Region)9, Steel = (Region)10, SteelDark = (Region)11,
            Hazard = (Region)12, GlowAmber = (Region)13, Bore = (Region)14, RunesAmber = (Region)15;
    }

    /// <summary>The cells a guardian's look uses for each kind of surface.</summary>
    readonly record struct GuardianPalette(Region Base, Region BaseTop, Region Body, Region Accent, Region Trim, Region Pod, Region Glow, Region Runes);

    static GuardianPalette GuardianPaletteOf(StructureLook look) => look switch
    {
        StructureLook.Concrete => new(GuardianCell.Concrete, GuardianCell.ConcreteDark, GuardianCell.Concrete, GuardianCell.Steel, GuardianCell.SteelDark,
            GuardianCell.Steel, GuardianCell.GlowAmber, GuardianCell.Hazard),
        StructureLook.Metal => new(GuardianCell.SteelDark, GuardianCell.Steel, GuardianCell.Steel, GuardianCell.SteelDark, GuardianCell.Hazard,
            GuardianCell.SteelDark, GuardianCell.GlowAmber, GuardianCell.RunesAmber),
        _ => new(GuardianCell.Stone, GuardianCell.StoneDark, GuardianCell.Verdigris, GuardianCell.Bronze, GuardianCell.Gold,
            GuardianCell.Bronze, GuardianCell.GlowTeal, GuardianCell.Runes),
    };

    /// <summary>The tower, in a look (ancient, concrete or metal).</summary>
    public static StructureMesh Guardian(StructureLook look)
    {
        var p = GuardianPaletteOf(look);
        var m = new MeshBuilder();
        float h = GuardianSize / 2;
        // The stepped plinth, reaching below ground for slopes.
        m.Box(new(-h, -Skirt, -h), new(h, 8, h), p.Base, p.BaseTop);
        m.Box(new(-h + 4, 8, -h + 4), new(h - 4, 15, h - 4), p.Base, p.BaseTop);
        // The spire: three tapering octagonal drums, banded where they meet.
        m.Cylinder(0, 0, 12.5f, 15, 42, p.Body, null);
        m.Cylinder(0, 0, 13.5f, 40, 44, p.Trim, p.Trim);
        m.Cylinder(0, 0, 11, 44, 78, p.Body, null);
        m.Cylinder(0, 0, 12, 76, 80, p.Trim, p.Trim);
        m.Cylinder(0, 0, 9.5f, 80, 100, p.Body, null);
        // Glowing runes running up the middle drum, one strip on each face.
        for (int i = 0; i < 4; i++)
        {
            var turn = Matrix4x4.CreateRotationY(i * MathF.PI / 2 + MathF.PI / 8);
            var strip = new MeshBuilder();
            strip.Box(new(-1.6f, 48, 9.9f), new(1.6f, 74, 11.2f), p.Runes, p.Runes, stretchSides: true);
            m.Append(strip.Build(Vector3.Zero, Vector3.Zero), turn);
        }
        // Four struts from the plinth's corners up to the spire, sleek and angled.
        foreach (var (sx, sz) in new[] { (1f, 1f), (-1f, 1f), (-1f, -1f), (1f, -1f) })
        {
            m.Beam(new(sx * (h - 6), 13, sz * (h - 6)), new(sx * 8, 66, sz * 8), 2.2f, 1.4f, p.Accent);
            m.Beam(new(sx * (h - 6), 13, sz * (h - 6)), new(sx * (h - 4.5f), 8, sz * (h - 4.5f)), 2.6f, 2.2f, p.Trim);
        }
        // The crown the pods stand on.
        m.Cylinder(0, 0, 17, 100, 105, p.Accent, p.Trim);
        // Four rocket pods, one to each side, pointing up and leaning out.
        for (int i = 0; i < 4; i++)
        {
            float a = i * MathF.PI / 2;
            // Leaning out by 15° (row vectors: x then y then z rotations apply in order), stood on the crown 12 out.
            var place = Matrix4x4.CreateRotationX(0.26f) * Matrix4x4.CreateTranslation(0, 105, 12) * Matrix4x4.CreateRotationY(a);
            m.Append(Pod(p), place);
        }
        // The sensor mast and its glowing eye.
        m.Tube(new(0, 105, 0), new(0, 136, 0), 2.4f, 1.2f, p.Trim, 8);
        m.Tube(new(0, 136, 0), new(0, 142, 0), 3.2f, 2.2f, p.Glow, 8);
        m.Tube(new(0, 142, 0), new(0, GuardianHeight, 0), 2.2f, 0.2f, p.Trim, 8);
        return m.Build(new(-h, 0, -h), new(h, GuardianHeight, h));
    }

    /// <summary>A pod of four launch tubes, in its own frame: upright, its foot at the origin.</summary>
    static StructureMesh Pod(GuardianPalette p)
    {
        var m = new MeshBuilder();
        m.Box(new(-6, 0, -5), new(6, 15, 5), p.Pod, p.Accent);
        m.Box(new(-6.6f, 3, -5.6f), new(6.6f, 5, 5.6f), p.Trim, null);
        foreach (var (x, z) in new[] { (-3f, -2.4f), (3f, -2.4f), (-3f, 2.4f), (3f, 2.4f) })
        {
            m.Tube(new(x, 12, z), new(x, 20, z), 2.3f, 2.3f, p.Accent, 8);
            m.Tube(new(x, 20, z), new(x, 20.4f, z), 1.6f, 1.6f, GuardianCell.Bore, 8); // the open bore
            m.Tube(new(x, 18.6f, z), new(x, 20, z), 2.6f, 2.6f, p.Glow, 8);         // a glowing ring at its mouth
        }
        // A fin down its back.
        m.Box(new(-0.8f, 2, 5), new(0.8f, 13, 8), p.Accent, p.Accent);
        return m.Build(Vector3.Zero, Vector3.Zero);
    }

    /// <summary>
    /// The rocket a guardian fires, for show: a slim body, a gold nose and four fins, pointing along +z (as the engine
    /// draws a missile's model along its flight).
    /// </summary>
    public static StructureMesh GuardianRocket()
    {
        var m = new MeshBuilder();
        m.Tube(new(0, 0, -9), new(0, 0, 7), 1.6f, 1.6f, GuardianCell.Bronze, 8);
        m.Tube(new(0, 0, 7), new(0, 0, 12), 1.6f, 0.1f, GuardianCell.Gold, 8);
        m.Tube(new(0, 0, -10), new(0, 0, -9), 1.2f, 1.2f, GuardianCell.GlowTeal, 8);
        for (int i = 0; i < 4; i++)
        {
            float a = i * MathF.PI / 2;
            var out_ = new Vector3(MathF.Cos(a), MathF.Sin(a), 0);
            m.Beam(out_ * 1.4f + new Vector3(0, 0, -8), out_ * 4.2f + new Vector3(0, 0, -9.5f), 0.25f, 1.4f, GuardianCell.Gold);
        }
        return m.Build(new(-4.5f, -4.5f, -10), new(4.5f, 4.5f, 12));
    }

    /// <summary>The guardian atlas's two textures as TGA files (built once).</summary>
    public static (byte[] Colour, byte[] Extra) GuardianTextureFiles() => guardianFiles.Value;

    static readonly Lazy<(byte[], byte[])> guardianFiles = new(() =>
    {
        var (colour, extra) = Texture.GuardianAtlas();
        return (TgaWriter.Write(colour), TgaWriter.Write(extra));
    });

    internal static partial class Texture
    {
        /// <summary>The guardian's atlas: its glow cells and the runes' strokes glow (texture 2's red), everything else is lit as it is.</summary>
        public static (RgbaImage Colour, RgbaImage Extra) GuardianAtlas() => Paint(GuardianPaint);

        static readonly Vector3 Glowing = new(255, 0, 120);

        static (Vector3 Colour, Vector3 Extra) GuardianPaint(Region region, double s, double t)
        {
            switch (region)
            {
                case GuardianCell.Stone: return (Masonry(s, t, new Vector3(158, 146, 122)), Stone);
                case GuardianCell.StoneDark: return (Masonry(s, t, new Vector3(108, 101, 90)), Stone);
                case GuardianCell.Bronze: return (Bronze(s, t, 0.25), new Vector3(0, 190, 95));
                case GuardianCell.Verdigris: return (Bronze(s, t, 0.85), new Vector3(0, 70, 185));
                case GuardianCell.Gold: return (new Vector3(222, 178, 78) * (float)(0.88 + 0.12 * Fractal(s, t, 8, 3, 141)), new Vector3(0, 235, 60));
                case GuardianCell.DarkBronze: return (new Vector3(74, 52, 34) * (float)(0.85 + 0.15 * Fractal(s, t, 8, 3, 142)), new Vector3(0, 170, 120));
                case GuardianCell.GlowTeal: return (new Vector3(110, 255, 225), Glowing);
                case GuardianCell.Runes: return Runes(s, t, Bronze(s, t, 0.85), new Vector3(120, 255, 228), new Vector3(0, 70, 185));
                case GuardianCell.Concrete: return (Concrete(s, t, 150, formwork: true), Rough);
                case GuardianCell.ConcreteDark: return (Concrete(s, t, 112, formwork: false), Rough);
                case GuardianCell.Steel: return (Steel(s, t, 1), Metal);
                case GuardianCell.SteelDark: return (Steel(s, t, 0.65), Metal);
                case GuardianCell.Hazard: return (Stripes(s, t, 4), Paintwork);
                case GuardianCell.GlowAmber: return (new Vector3(255, 176, 70), Glowing);
                case GuardianCell.Bore: return (Grey(20), new Vector3(0, 0, 255));
                case GuardianCell.RunesAmber: return Runes(s, t, Steel(s, t, 0.65), new Vector3(255, 176, 70), Metal);
                default: return (Grey(128), Rough);
            }
        }

        /// <summary>Old dressed stone: blocks in courses, their joints dark, weathered and stained.</summary>
        static Vector3 Masonry(double s, double t, Vector3 colour)
        {
            const double px = 1.0 / (Cell - 2 * Inset);
            double course = Frac(t * 4), block = Frac(s * 2 + ((int)Math.Floor(t * 4) % 2) * 0.5);
            double weather = Fractal(s, t, 4, 4, 143), grain = Fractal(s, t, 32, 2, 144);
            var v = colour * (float)(0.8 + 0.2 * weather) * (float)(0.94 + 0.12 * (grain - 0.5));
            if (course < 2.2 * px * 4 || block < 2.2 * px * 2)
                v *= 0.55f; // the joints
            return v;
        }

        /// <summary>Bronze with <paramref name="patina"/> (0 to 1) of it gone to verdigris, the green settling in hollows.</summary>
        static Vector3 Bronze(double s, double t, double patina)
        {
            double mottle = Fractal(s, t, 4, 4, 145), fine = Fractal(s, t, 16, 3, 146);
            var bronze = new Vector3(168, 114, 60) * (float)(0.85 + 0.2 * fine);
            var green = Vector3.Lerp(new Vector3(70, 138, 116), new Vector3(110, 176, 150), (float)fine);
            return Vector3.Lerp(bronze, green, (float)Math.Clamp((mottle - (1 - patina)) * 2.5 + patina * 0.45, 0, 1));
        }

        /// <summary>Rows of glyphs, their strokes glowing, on a ground; glyphs are a few strokes each, picked by position.</summary>
        static (Vector3, Vector3) Runes(double s, double t, Vector3 ground, Vector3 glow, Vector3 groundExtra)
        {
            const int columns = 2, rows = 6;
            double gs = s * columns, gt = t * rows;
            int cx = (int)Math.Floor(gs), cy = (int)Math.Floor(gt);
            double u = Frac(gs), v = Frac(gt);
            if (u < 0.15 || u > 0.85 || v < 0.12 || v > 0.88)
                return (ground, groundExtra);
            // Six possible strokes in a glyph: three upright, a bar, two slants.
            double best = double.MaxValue;
            for (int k = 0; k < 6; k++)
            {
                if (Jitter(cx * 7 + k, cy, 147) < 0.45)
                    continue;
                double d = k switch
                {
                    0 => Math.Abs(u - 0.3),
                    1 => Math.Abs(u - 0.7),
                    2 => Math.Abs(u - 0.5) + Math.Max(0, Math.Abs(v - 0.5) - 0.25),
                    3 => Math.Abs(v - 0.5) + Math.Max(0, Math.Abs(u - 0.5) - 0.2),
                    4 => Math.Abs((u - 0.3) - (v - 0.15) * 0.55),
                    _ => Math.Abs((0.7 - u) - (v - 0.15) * 0.55),
                };
                best = Math.Min(best, d);
            }
            return best < 0.06 ? (glow, Glowing) : (ground * 0.85f, groundExtra);
        }
    }
}
