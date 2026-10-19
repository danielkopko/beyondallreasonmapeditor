using System.Numerics;
using MapCore.Imaging;

namespace MapCore.Structures;

/// <summary>
/// The oil pump: an old pumpjack ("nodding donkey") on a concrete pad, and the derelict one it is until repaired. Its
/// own atlas (<see cref="PumpTexture1"/>, <see cref="PumpTexture2"/>): rusty and painted steel, wood, brass, a wooden
/// tank's staves, oil. In its own frame x runs across, z along it (the horse head and well at +z), y up; it fills
/// <see cref="PumpWidth"/> by <see cref="PumpLength"/>.
/// </summary>
public static partial class StructureModels
{
    public const string PumpTexture1 = "map_pump_1.tga", PumpTexture2 = "map_pump_2.tga";

    /// <summary>The pump's footprint, game units across (x) and along (z): 4 squares by 6.</summary>
    public const float PumpWidth = 64, PumpLength = 96;

    /// <summary>Where the walking beam pivots on its post (game units, in the pump's frame).</summary>
    public static readonly Vector3 PumpPivot = new(0, 50, 4);

    /// <summary>The pump atlas's cells, on the same 4 × 4 grid as the structures' atlas.</summary>
    internal static class PumpCell
    {
        public const Region Rust = (Region)0, Paint = (Region)1, DarkSteel = (Region)2, Wood = (Region)3,
            Concrete = (Region)4, Brass = (Region)5, Staves = (Region)6, Oil = (Region)7,
            Hazard = (Region)8, RustHeavy = (Region)9, WoodGrey = (Region)10, PaintFaded = (Region)11;
    }

    /// <summary>
    /// The working pump. With <paramref name="withBeam"/>, its walking beam (horse head, bridle, pitman arms) is in
    /// place; without, it is left out, to be its own feature that the gadget makes nod about <see cref="PumpPivot"/>.
    /// </summary>
    public static StructureMesh Pump(bool withBeam)
    {
        var m = new MeshBuilder();
        Base(m, derelict: false);
        if (withBeam)
            m.Append(PumpBeam(), Matrix4x4.CreateTranslation(PumpPivot));
        return m.Build(new(-PumpWidth / 2, 0, -PumpLength / 2), new(PumpWidth / 2, 56, PumpLength / 2));
    }

    /// <summary>
    /// The walking beam with its horse head, the bridle and polished rod hanging from it, and the pitman arms down to
    /// the crank: in the beam's own frame, the pivot at its origin.
    /// </summary>
    public static StructureMesh PumpBeam()
    {
        var m = new MeshBuilder();
        BeamParts(m, PumpCell.Paint, PumpCell.DarkSteel);
        return m.Build(new(-9, -40, -44), new(9, 18, 42));
    }

    /// <summary>The derelict pump: rusted and grey, its beam fallen to the ground, its tank collapsed into staves, oil spilt.</summary>
    public static StructureMesh DerelictPump(int seed)
    {
        var m = new MeshBuilder();
        Base(m, derelict: true);
        // The beam, fallen: lying on the ground, turned off its line, its head down.
        var beam = new MeshBuilder();
        BeamParts(beam, PumpCell.PaintFaded, PumpCell.RustHeavy, withRods: false);
        m.Append(beam.Build(Vector3.Zero, Vector3.Zero),
            Matrix4x4.CreateRotationX(0.09f) * Matrix4x4.CreateRotationY(0.42f + 0.1f * (float)(Texture.Jitter(1, 2, seed) - 0.5)) *
            Matrix4x4.CreateTranslation(-6, 4, -2));
        // Staves of the tank, scattered.
        for (int i = 0; i < 5; i++)
        {
            float a = i * 1.1f + (float)Texture.Jitter(i, 9, seed);
            var at = new Vector3(22 + 9 * MathF.Cos(a), 0.8f, 30 + 9 * MathF.Sin(a));
            m.Beam(at, at + new Vector3(MathF.Cos(a + 1.3f) * 14, 0, MathF.Sin(a + 1.3f) * 14), 1.4f, 0.6f, PumpCell.WoodGrey);
        }
        return m.Build(new(-PumpWidth / 2, 0, -PumpLength / 2), new(PumpWidth / 2, 50, PumpLength / 2));
    }

    /// <summary>Everything but the beam: the pad and skid, the post, the crank and its weights, gearbox, engine, well and tank.</summary>
    static void Base(MeshBuilder m, bool derelict)
    {
        Region steel = derelict ? PumpCell.RustHeavy : PumpCell.DarkSteel, paint = derelict ? PumpCell.PaintFaded : PumpCell.Paint,
            rust = derelict ? PumpCell.RustHeavy : PumpCell.Rust, wood = derelict ? PumpCell.WoodGrey : PumpCell.Wood;
        // The pad, and the skid on it.
        m.Box(new(-30, -6, -46), new(30, 1, 46), PumpCell.Concrete, PumpCell.Concrete);
        foreach (float x in new[] { -12f, 12f })
            m.Box(new(x - 2, 1, -44), new(x + 2, 5, 40), steel, steel);
        m.Box(new(-14, 1, -30), new(14, 4, -26), steel, steel);
        m.Box(new(-14, 1, 8), new(14, 4, 12), steel, steel);
        // The post: an A-frame each side up to the bearing (in the derelict one, a leg is gone).
        var top = PumpPivot - new Vector3(0, 3, 0);
        foreach (float side in new[] { -1f, 1f })
        {
            m.Beam(new(side * 12, 4, -12), top + new Vector3(side * 4, 0, -1), 1.6f, 1.6f, paint);
            if (!(derelict && side > 0))
                m.Beam(new(side * 12, 4, 18), top + new Vector3(side * 4, 0, 1), 1.6f, 1.6f, paint);
        }
        m.Beam(top + new Vector3(-6, 0, 0), top + new Vector3(6, 0, 0), 1.5f, 1.5f, paint);
        m.Box(top + new Vector3(-5, 0, -3), top + new Vector3(5, 4, 3), PumpCell.Brass, PumpCell.Brass);
        // The crank on its shaft through the gearbox, its weights hanging down.
        var shaft = new Vector3(0, 18, -34);
        m.Box(new(-8, 4, -42), new(8, 22, -26), steel, steel);
        m.Tube(shaft + new Vector3(-13, 0, 0), shaft + new Vector3(13, 0, 0), 2.2f, 2.2f, steel, 10);
        foreach (float x in new[] { -11f, 11f })
            m.ArcSlab(x - 1.2f, x + 1.2f, shaft.Y, shaft.Z, 12, derelict && x > 0 ? 0.6f : MathF.PI, derelict && x > 0 ? MathF.PI + 0.6f : 2 * MathF.PI, paint, steel);
        // The engine beside it, with its flywheel and exhaust.
        m.Box(new(16, 1, -44), new(28, 15, -28), steel, steel);
        m.ArcSlab(13, 15, 12, -36, 10, 0, 2 * MathF.PI, steel, steel);
        m.Tube(new(24, 15, -40), new(24, derelict ? 22 : 34, -40), 1.3f, 1.3f, steel, 8);
        // The well: casing, flange and stuffing box, piped to the tank.
        m.Tube(new(0, 1, 43), new(0, 8, 43), 3.5f, 3.5f, rust, 10);
        m.Tube(new(0, 8, 43), new(0, 9.5f, 43), 4.5f, 4.5f, steel, 10);
        m.Tube(new(0, 9.5f, 43), new(0, 13, 43), 2.4f, 2.4f, PumpCell.Brass, 8);
        m.Beam(new(3, 5, 42), new(15, 5, 34), 0.9f, 0.9f, steel);
        // The wooden tank, hooped with iron, under a cone of a roof; the derelict one's is down to its foot.
        var tank = new Vector3(22, 1, 30);
        float height = derelict ? 5 : 18;
        m.Tube(tank, tank + new Vector3(0, height, 0), 8, 8, PumpCell.Staves, 14);
        foreach (float y in derelict ? new[] { 2f } : new[] { 3f, 9f, 15f })
            m.Tube(tank + new Vector3(0, y, 0), tank + new Vector3(0, y + 1, 0), 8.3f, 8.3f, rust, 14);
        if (!derelict)
            m.Tube(tank + new Vector3(0, height, 0), tank + new Vector3(0, height + 6, 0), 8.6f, 0.8f, wood, 14);
        // Oil on the ground by the well (more of it, spilt, round the derelict one).
        m.Plate(new(0, 1.2f, 38), derelict ? new(16, 0.2f, 9) : new(9, 0.2f, 5), derelict ? 0.3f : 0, PumpCell.Oil);
    }

    /// <summary>The beam and what hangs from it, in its own frame with the pivot at the origin.</summary>
    static void BeamParts(MeshBuilder m, Region paint, Region steel, bool withRods = true)
    {
        m.Box(new(-3, 1, -42), new(3, 7, 26), paint, paint); // the beam
        m.ArcSlab(-4, 4, 4, 26, 13, -1.3f, 1.3f, paint, steel); // the horse head, curved to the well
        m.Box(new(-9, 0, -44), new(9, 4, -40), paint, paint); // the equalizer at its tail
        if (!withRods)
            return;
        m.Tube(new(0, -8, 39), new(0, -37, 39), 0.9f, 0.9f, steel, 6); // bridle and polished rod, down to the well
        foreach (float x in new[] { -8f, 8f })
            m.Beam(new(x, 1, -42), new(x + Math.Sign(x) * 1, -32, -38), 1, 1, steel); // pitman arms down to the crank
    }

    /// <summary>The pump atlas's two textures as TGA files (built once).</summary>
    public static (byte[] Colour, byte[] Extra) PumpTextureFiles() => pumpFiles.Value;

    static readonly Lazy<(byte[], byte[])> pumpFiles = new(() =>
    {
        var (colour, extra) = Texture.PumpAtlas();
        return (TgaWriter.Write(colour), TgaWriter.Write(extra));
    });

    internal static partial class Texture
    {
        /// <summary>
        /// The pump's atlas, painted as the structures' is, but for its paint: texture 1's alpha there is high, so a
        /// working pump (made for the team that repaired it) wears that team's colour on its frame and beam. The
        /// derelict, made for no team, is painted from the faded cell, whose alpha is 0 (no team's colour is black).
        /// </summary>
        public static (RgbaImage Colour, RgbaImage Extra) PumpAtlas() =>
            Paint((region, s, t) => PumpPaint(region, s, t), region => region == PumpCell.Paint ? (byte)200 : (byte)0);

        static (Vector3 Colour, Vector3 Extra) PumpPaint(Region region, double s, double t)
        {
            switch (region)
            {
                case PumpCell.Rust: return (Rusty(s, t, 0.35), Metal);
                case PumpCell.Paint: return (Painted(s, t, new Vector3(150, 52, 38), 0.2), Paintwork);
                case PumpCell.DarkSteel: return (Steel(s, t, 0.7), Metal);
                case PumpCell.Wood: return (Planks(s, t, new Vector3(128, 92, 60)), Rough);
                case PumpCell.Concrete: return (Concrete(s, t, 140, formwork: false), Rough);
                case PumpCell.Brass: return (new Vector3(178, 140, 70) * (float)(0.85 + 0.15 * Fractal(s, t, 8, 3, 91)), Metal);
                case PumpCell.Staves: return Staves(s, t);
                case PumpCell.Oil: return (Grey(18 + 14 * Fractal(s, t, 4, 3, 92)) + new Vector3(4, 2, 0), new Vector3(0, 60, 40));
                case PumpCell.Hazard: return (Stripes(s, t, 4), Paintwork);
                case PumpCell.RustHeavy: return (Rusty(s, t, 0.85), new Vector3(0, 90, 220));
                case PumpCell.WoodGrey: return (Planks(s, t, new Vector3(110, 104, 96)), Rough);
                case PumpCell.PaintFaded: return (Painted(s, t, new Vector3(120, 70, 58), 0.65), new Vector3(0, 60, 200));
                default: return (Rusty(s, t, 0.5), Metal);
            }
        }

        /// <summary>Steel going to rust: <paramref name="amount"/> of it (0 to 1) taken over by orange-brown patches.</summary>
        static Vector3 Rusty(double s, double t, double amount)
        {
            var steel = Steel(s, t, 0.85);
            double patch = Fractal(s, t, 4, 4, 93), pits = Fractal(s, t, 32, 2, 94);
            var rust = Vector3.Lerp(new Vector3(98, 52, 30), new Vector3(150, 82, 40), (float)pits);
            return Vector3.Lerp(steel, rust, (float)Math.Clamp((patch - (1 - amount)) * 3 + amount * 0.3, 0, 1));
        }

        /// <summary>Paint over steel, worn through to rust in places (<paramref name="wear"/> 0 to 1), with streaks running down.</summary>
        static Vector3 Painted(double s, double t, Vector3 colour, double wear)
        {
            double blotch = Fractal(s, t, 4, 4, 95), streak = Noise(s, t, 32, 2, 96);
            var paint = colour * (float)(0.85 + 0.15 * blotch) * (float)(0.92 + 0.08 * streak);
            return Fractal(s, t, 6, 3, 97) < wear * 0.6 ? Rusty(s, t, 0.9) : paint;
        }

        /// <summary>Planks running up the cell, their joints dark, the grain in streaks.</summary>
        static Vector3 Planks(double s, double t, Vector3 colour)
        {
            double plank = Frac(s * 4), grain = Noise(s * 4, t, 4, 48, 98);
            var v = colour * (float)(0.8 + 0.25 * grain) * (float)(0.92 + 0.08 * Jitter((int)Math.Floor(s * 4), 3, 99));
            return plank < 0.04 || plank > 0.96 ? v * 0.45f : v;
        }

        /// <summary>A wooden tank's staves, hooped in iron.</summary>
        static (Vector3, Vector3) Staves(double s, double t)
        {
            double hoop = Frac(t * 3);
            if (hoop < 0.07)
                return (Rusty(s, t, 0.6), Metal);
            return (Planks(s * 2, t, new Vector3(120, 86, 56)), Rough);
        }
    }
}
