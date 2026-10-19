using System.Numerics;
using MapCore.Imaging;

namespace MapCore.Structures;

/// <summary>
/// The shield platform: a low steel pad units drive over, its deck of plates, a ring of emitters round a glowing lens,
/// four short projector posts at its corners, and stripes in its owner's colour; and the derelict one it is until
/// repaired (rusted, its emitters dead, plates scorched, a post broken off). Its own atlas
/// (<see cref="ShieldPadTexture1"/>, <see cref="ShieldPadTexture2"/>). In its own frame x runs across, z along, y up;
/// it fills its footprint, the deck <see cref="ShieldPadDeck"/> high.
/// </summary>
public static partial class StructureModels
{
    public const string ShieldPadTexture1 = "map_shieldpad_1.tga", ShieldPadTexture2 = "map_shieldpad_2.tga";

    /// <summary>The deck's height (units drive over it: the pad doesn't block) and the corner posts' tops.</summary>
    public const float ShieldPadDeck = 4.5f, ShieldPadHeight = 16;

    /// <summary>The shield platform atlas's cells, on the same 4 × 4 grid as the structures' atlas.</summary>
    internal static class PadCell
    {
        public const Region Deck = (Region)0, Plates = (Region)1, Trim = (Region)2, Glow = (Region)3,
            Paint = (Region)4, Hazard = (Region)5, Lens = (Region)6, Copper = (Region)7,
            DeckWorn = (Region)8, DarkSteel = (Region)9, GlowDead = (Region)10, PaintFaded = (Region)11,
            Scorch = (Region)12, Concrete = (Region)13, Rust = (Region)14, Cable = (Region)15;
    }

    /// <summary>The working platform, <paramref name="width"/> across by <paramref name="depth"/> along (game units).</summary>
    public static StructureMesh ShieldPad(float width, float depth) => Pad(width, depth, derelict: false, seed: 0);

    /// <summary>The derelict platform: rusted and scorched, its emitters dead, some missing, a corner post broken off.</summary>
    public static StructureMesh DerelictShieldPad(float width, float depth, int seed) => Pad(width, depth, derelict: true, seed);

    static StructureMesh Pad(float width, float depth, bool derelict, int seed)
    {
        var m = new MeshBuilder();
        float hw = width / 2, hd = depth / 2, r = Math.Min(hw, hd);
        Region deck = derelict ? PadCell.DeckWorn : PadCell.Deck, plates = derelict ? PadCell.DeckWorn : PadCell.Plates,
            glow = derelict ? PadCell.GlowDead : PadCell.Glow, paint = derelict ? PadCell.PaintFaded : PadCell.Paint,
            trim = derelict ? PadCell.Rust : PadCell.Trim;
        // The slab, reaching below ground for slopes, and the raised deck of plates on it.
        m.Box(new(-hw, -Skirt, -hd), new(hw, 2.5f, hd), trim, deck);
        m.Box(new(-hw + 5, 2.5f, -hd + 5), new(hw - 5, ShieldPadDeck, hd - 5), PadCell.DarkSteel, plates);
        // Stripes in the owner's colour along both sides, and hazard edges at both ends, where units drive on and off.
        foreach (float sx in new[] { -1f, 1f })
            m.Box(new(sx * (hw - 2.5f) - 1.2f, 2.5f, -hd + 8), new(sx * (hw - 2.5f) + 1.2f, 3.2f, hd - 8), paint, paint);
        foreach (float sz in new[] { -1f, 1f })
            m.Box(new(-hw + 8, 2.5f, sz * (hd - 2.5f) - 1.2f), new(hw - 8, 3.0f, sz * (hd - 2.5f) + 1.2f), PadCell.Hazard, PadCell.Hazard);
        // A ring of emitters round the lens (a derelict one has lost some).
        const int emitters = 12;
        float ring = r * 0.55f;
        for (int i = 0; i < emitters; i++)
        {
            if (derelict && Texture.Jitter(i, 3, seed) < 0.3)
                continue;
            float a = i * 2 * MathF.PI / emitters;
            m.Cylinder(ring * MathF.Cos(a), ring * MathF.Sin(a), 2.4f, ShieldPadDeck, ShieldPadDeck + 1.3f, PadCell.DarkSteel, glow);
        }
        // The lens: a rim of trim round dark glass, its core glowing.
        m.Cylinder(0, 0, r * 0.26f, ShieldPadDeck, ShieldPadDeck + 0.9f, trim, derelict ? PadCell.GlowDead : PadCell.Lens, 16);
        m.Cylinder(0, 0, r * 0.12f, ShieldPadDeck + 0.9f, ShieldPadDeck + 1.3f, trim, glow, 16);
        // Four short projector posts at the corners: a steel post, a copper coil, a glowing collar and a spike.
        int broken = (int)(Texture.Jitter(5, 7, seed) * 4) & 3;
        int corner = 0;
        foreach (var (sx, sz) in new[] { (1f, 1f), (-1f, 1f), (-1f, -1f), (1f, -1f) })
        {
            float x = sx * (hw - 7), z = sz * (hd - 7);
            if (derelict && corner++ == broken)
            {
                // Snapped off: a stub, and the rest of it lying on the deck.
                m.Cylinder(x, z, 3.6f, 2.5f, 6.5f, PadCell.Rust, PadCell.Scorch);
                m.Beam(new(x - sx * 3, ShieldPadDeck + 1.5f, z - sz * 1), new(x - sx * 13, ShieldPadDeck + 1.2f, z - sz * 6), 2.4f, 1.6f, PadCell.Rust);
                continue;
            }
            m.Cylinder(x, z, 3.6f, 2.5f, 10, trim, null);
            m.Cylinder(x, z, 3.0f, 10, 13, derelict ? PadCell.Rust : PadCell.Copper, null);
            m.Tube(new(x, 13, z), new(x, 14.5f, z), 3.4f, 2.6f, glow);
            m.Tube(new(x, 14.5f, z), new(x, ShieldPadHeight, z), 2.0f, 0.4f, trim);
        }
        if (derelict) // scorched plates
            for (int i = 0; i < 3; i++)
            {
                float x = (float)(Texture.Jitter(i, 11, seed) - 0.5) * (width - 30), z = (float)(Texture.Jitter(i, 13, seed) - 0.5) * (depth - 30);
                m.Box(new(x - 7, ShieldPadDeck, z - 5), new(x + 7, ShieldPadDeck + 0.15f, z + 5), PadCell.Scorch, PadCell.Scorch);
            }
        return m.Build(new(-hw, 0, -hd), new(hw, ShieldPadHeight, hd));
    }

    /// <summary>The shield platform atlas's two textures as TGA files (built once).</summary>
    public static (byte[] Colour, byte[] Extra) ShieldPadTextureFiles() => padFiles.Value;

    static readonly Lazy<(byte[], byte[])> padFiles = new(() =>
    {
        var (colour, extra) = Texture.ShieldPadAtlas();
        return (TgaWriter.Write(colour), TgaWriter.Write(extra));
    });

    internal static partial class Texture
    {
        /// <summary>
        /// The platform's atlas: its emitters, collars and lens core glow (texture 2's red), and its stripes are painted
        /// so a platform made for a team wears that team's colour there (texture 1's alpha 200). The derelict, made for
        /// no team, is painted from the faded cell, whose alpha is 0.
        /// </summary>
        public static (RgbaImage Colour, RgbaImage Extra) ShieldPadAtlas() =>
            Paint(PadPaint, region => region == PadCell.Paint ? (byte)200 : (byte)0);

        static (Vector3 Colour, Vector3 Extra) PadPaint(Region region, double s, double t)
        {
            switch (region)
            {
                case PadCell.Deck: return (Tread(s, t), Metal);
                case PadCell.Plates: return (Plates(s, t), Metal);
                case PadCell.Trim: return (Steel(s, t, 0.75), Metal);
                case PadCell.Glow: return (new Vector3(90, 215, 255) * (float)(0.9 + 0.1 * Fractal(s, t, 8, 2, 111)), Glowing);
                case PadCell.Paint: return (Painted(s, t, new Vector3(70, 110, 160), 0.05), Paintwork);
                case PadCell.Hazard: return (Stripes(s, t, 4), Paintwork);
                case PadCell.Lens: return (Lens(s, t), new Vector3(110, 220, 30));
                case PadCell.Copper: return (Coil(s, t), Metal);
                case PadCell.DeckWorn: return (Vector3.Lerp(Plates(s, t), Rusty(s, t, 0.7), 0.45f), new Vector3(0, 110, 200));
                case PadCell.DarkSteel: return (Grey(48 * (0.86 + 0.14 * Fractal(s, t, 8, 3, 112))), Metal);
                case PadCell.GlowDead: return (Grey(36 + 14 * Fractal(s, t, 6, 3, 113)) + new Vector3(0, 6, 10), new Vector3(0, 80, 60));
                case PadCell.PaintFaded: return (Painted(s, t, new Vector3(84, 94, 104), 0.6), new Vector3(0, 60, 200));
                case PadCell.Scorch: return (Grey(20 + 18 * Fractal(s, t, 4, 3, 114)), new Vector3(0, 0, 240));
                case PadCell.Concrete: return (Concrete(s, t, 140, formwork: false), Rough);
                case PadCell.Rust: return (Rusty(s, t, 0.85), new Vector3(0, 90, 220));
                default: return (Grey(26 + 8 * Fractal(s, t, 8, 2, 115)), Rough); // cable
            }
        }

        /// <summary>Steel plates laid in staggered rows (four a row across the cell), their seams dark, each a little different.</summary>
        static Vector3 Plates(double s, double t)
        {
            double y = t * 4;
            int row = (int)Math.Floor(y);
            double x = s * 4 + ((row & 1) == 1 ? 0.5 : 0);
            double u = Frac(x), v = Frac(y);
            var plate = Steel(s, t, 0.95) * (float)(0.9 + 0.12 * Jitter((int)Math.Floor(x), row, 116));
            return u < 0.04 || u > 0.96 || v < 0.05 || v > 0.95 ? plate * 0.4f : plate;
        }

        /// <summary>Dark glass, lit from within toward its middle.</summary>
        static Vector3 Lens(double s, double t)
        {
            double d = Math.Sqrt((s - 0.5) * (s - 0.5) + (t - 0.5) * (t - 0.5)) * 2;
            return Vector3.Lerp(new Vector3(60, 170, 210), new Vector3(10, 30, 44), (float)Math.Clamp(d, 0, 1));
        }

        /// <summary>Copper wire wound round and round: bright turns, dark between them.</summary>
        static Vector3 Coil(double s, double t)
        {
            double turn = Frac(t * 16);
            var copper = new Vector3(184, 110, 60) * (float)(0.85 + 0.15 * Fractal(s, t, 8, 2, 117));
            return turn < 0.2 ? copper * 0.5f : copper;
        }
    }
}
