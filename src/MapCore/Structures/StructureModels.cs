using System.Numerics;
using MapCore.Assets;
using MapCore.Imaging;

namespace MapCore.Structures;

/// <summary>
/// What a structure kind's parts look like: models the editor builds (concrete or metal), or models chosen from assets.
/// A guardian tower is concrete, metal, or ancient (bronze gone green, gold and old stone).
/// </summary>
public enum StructureLook
{
    Assets,
    Concrete,
    Metal,
    Ancient,
}

/// <summary>A tunnel's roof: flat (a tunnel top can be built on), or a jagged rock mass like a cliff (nothing is built on it).</summary>
public enum StructureRoof
{
    Flat,
    Cliff,
}

/// <summary>A part's model: its triangles, and the box it fills (for the feature's collision volume).</summary>
public sealed record StructureMesh(IReadOnlyList<ModelVertex> Vertices, IReadOnlyList<int> Triangles, Vector3 Min, Vector3 Max)
{
    public int TriangleCount => Triangles.Count / 3;

    /// <summary>The corners of the box round every vertex (the skirt below ground, rock spilling over, included).</summary>
    public Vector3 VertexMin => Vertices.Aggregate(new Vector3(float.MaxValue), (m, v) => Vector3.Min(m, v.Position));
    public Vector3 VertexMax => Vertices.Aggregate(new Vector3(float.MinValue), (m, v) => Vector3.Max(m, v.Position));

    public byte[] ToS3o(string pieceName) => S3oModel.Write(StructureModels.Texture1, StructureModels.Texture2, pieceName, Vertices, Triangles);

    /// <summary>As an S3O textured from another atlas (the pump's).</summary>
    public byte[] ToS3o(string pieceName, string texture1, string texture2) => S3oModel.Write(texture1, texture2, pieceName, Vertices, Triangles);
}

/// <summary>
/// Builds the models of doors and tunnels: walls, the centre (a door, a tunnel's block with a flat roof or a rock
/// roof, an open tunnel's roof) and repair markers, sized to their footprints, in concrete or metal, all textured from
/// one atlas (<see cref="Texture1"/>, <see cref="Texture2"/>). In a part's own frame x runs across the gap, z along the
/// passage and y up from the ground; every part reaches <see cref="Skirt"/> below ground so a slope shows no gap under
/// it. Triangles are wound as real S3O models are (measured on 259 of them in seven maps: (b−a)×(c−a) points out of
/// the model), with flat normals. The same settings always give the same bytes.
/// </summary>
public static partial class StructureModels
{
    public const string Texture1 = "map_structures_1.tga", Texture2 = "map_structures_2.tga";

    /// <summary>How far every part (but the little repair marker) reaches below the ground (game units).</summary>
    public const float Skirt = 16;

    /// <summary>The atlas: 512 pixels square, a 4 × 4 grid of 128-pixel cells; tiled cells repeat every <see cref="Tile"/> game units.</summary>
    internal const int AtlasSize = 512, Cell = 128, Inset = 4;
    internal const float Tile = 32;

    /// <summary>The atlas cells (row by row).</summary>
    internal enum Region
    {
        ConcreteWall, ConcreteTop, MetalWall, MetalTop,
        ConcreteDoor, MetalDoor, ConcreteEntrance, MetalEntrance,
        Hazard, DarkTrim, RockSide, RockTop,
        ConcretePad, MetalPad, RepairPanel, MetalTrim,
    }

    /// <summary>The cells a look uses for each kind of surface.</summary>
    readonly record struct Palette(Region Wall, Region Top, Region Door, Region Entrance, Region Pad, Region Trim);

    static Palette PaletteOf(StructureLook look) => look == StructureLook.Metal
        ? new(Region.MetalWall, Region.MetalTop, Region.MetalDoor, Region.MetalEntrance, Region.MetalPad, Region.MetalTrim)
        : new(Region.ConcreteWall, Region.ConcreteTop, Region.ConcreteDoor, Region.ConcreteEntrance, Region.ConcretePad, Region.MetalTrim);

    /// <summary>A wall: <paramref name="width"/> across the gap, <paramref name="depth"/> along the passage, <paramref name="height"/> tall.</summary>
    public static StructureMesh Wall(StructureLook look, float width, float depth, float height)
    {
        var p = PaletteOf(look);
        var m = new MeshBuilder();
        float hw = width / 2, hd = depth / 2;
        float plinth = Math.Min(8, height * 0.12f), cap = Math.Min(8, height * 0.1f), inset = Math.Min(3, hd * 0.15f);
        m.Box(new(-hw, -Skirt, -hd), new(hw, plinth, hd), p.Wall, p.Top);
        m.Box(new(-hw, plinth, -hd + inset), new(hw, height - cap, hd - inset), p.Wall, null);
        m.Box(new(-hw, height - cap, -hd), new(hw, height, hd), p.Trim, p.Top);
        // Ribs on the ends that face along the passage: one broad buttress in concrete, two narrower ones in metal.
        if (look == StructureLook.Metal)
        {
            float rib = Math.Min(width * 0.16f, 8);
            foreach (float x in new[] { -hw * 0.5f, hw * 0.5f })
                m.Box(new(x - rib / 2, plinth, -hd), new(x + rib / 2, height - cap, hd), p.Trim, null);
        }
        else
        {
            float rib = Math.Min(width * 0.34f, 16);
            m.Box(new(-rib / 2, plinth, -hd), new(rib / 2, height - cap, hd), p.Wall, null);
        }
        return m.Build(new(-hw, 0, -hd), new(hw, height, hd));
    }

    /// <summary>
    /// A door that lowers into the ground to open: a thin gate across the gap (<paramref name="gap"/> across), held
    /// between the pillars (the walls, which brace it) in a guide rail at each side, stiffened across its faces, with
    /// two hydraulic rams on one face. The gate, rails and rams are the part that is destroyed.
    /// </summary>
    public static StructureMesh Door(StructureLook look, float gap, float depth, float height)
    {
        var p = PaletteOf(look);
        var m = new MeshBuilder();
        float hg = gap / 2, hd = depth / 2;
        float half = Math.Min(4, hd * 0.25f), top = height - 6, rail = Math.Min(3, hg * 0.12f), railHalf = Math.Min(half + 3, hd);
        // The guide rails, against the pillars, down into the slot the gate drops into.
        m.Box(new(-hg, -Skirt, -railHalf), new(-hg + rail, height - 2, railHalf), p.Trim, p.Trim);
        m.Box(new(hg - rail, -Skirt, -railHalf), new(hg, height - 2, railHalf), p.Trim, p.Trim);
        // The gate: its face's picture starts at the ground, and it runs on into the rails.
        m.Box(new(-hg + 1, -Skirt, -half), new(hg - 1, 0, half), p.Trim, null);
        m.Box(new(-hg + 1, 0, -half), new(hg - 1, top - 4, half), p.Trim, null, ends: p.Door, stretchEnds: true);
        m.Box(new(-hg + rail, top - 4, -half - 1.5f), new(hg - rail, top, half + 1.5f), p.Trim, p.Trim); // its stiffened top edge
        foreach (float y in new[] { top * 0.3f, top * 0.62f })
            m.Box(new(-hg + rail + 2, y - 2, -half - 1.5f), new(hg - rail - 2, y + 2, half + 1.5f), p.Trim, p.Trim);
        // Two rams on the face towards -z: a barrel on a mount, a bright rod, and a bracket to the gate.
        float rz = -half - 5;
        foreach (float rx in new[] { -hg * 0.55f, hg * 0.55f })
        {
            m.Box(new(rx - 4, -4, rz - 4), new(rx + 4, 4, rz + 4), p.Trim, Region.Hazard);
            m.Cylinder(rx, rz, 3, 4, top * 0.5f, Region.DarkTrim, null);
            m.Cylinder(rx, rz, 3.7f, top * 0.5f - 3, top * 0.5f, Region.MetalTrim, Region.MetalTrim);
            m.Cylinder(rx, rz, 1.4f, top * 0.5f, top - 9, Region.MetalTrim, null);
            m.Box(new(rx - 3, top - 14, rz - 3), new(rx + 3, top - 8, -half), p.Trim, p.Trim);
        }
        return m.Build(new(-hg, 0, rz - 4), new(hg, top, half + 1.5f));
    }

    /// <summary>The share of a broken door's gap left clear in the middle, so units look as if they can walk or drive through.</summary>
    public const float BrokenDoorClear = 0.6f;

    /// <summary>
    /// What is left of a door once it is destroyed, all at the edges of its gap: the middle (<see cref="BrokenDoorClear"/>
    /// of it) is clear ground but for the dark slot the gate stood in, flush with the ground. A torn stub of the gate
    /// hangs in each rail, tapering towards the middle; the rails are snapped; plates of the gate lie fallen along the
    /// walls; the rams are knocked aside to the pillars, snapped short. None of it blocks.
    /// </summary>
    public static StructureMesh BrokenDoor(StructureLook look, float gap, float depth, float height, int seed)
    {
        var p = PaletteOf(look);
        var m = new MeshBuilder();
        float hg = gap / 2, hd = depth / 2;
        float half = Math.Min(4, hd * 0.25f), top = height - 6, rail = Math.Min(3, hg * 0.12f), railHalf = Math.Min(half + 3, hd);
        float clear = hg * BrokenDoorClear;
        // Snapped rails, against the pillars.
        m.Box(new(-hg, -Skirt, -railHalf), new(-hg + rail, height * 0.42f, railHalf), p.Trim, Region.DarkTrim);
        m.Box(new(hg - rail, -Skirt, -railHalf), new(hg, height * 0.3f, railHalf), p.Trim, Region.DarkTrim);
        // The slot the gate stood in, across the gap, flush with the ground.
        m.Box(new(-hg + 1, -Skirt, -half), new(hg - 1, 0.5f, half), p.Trim, Region.DarkTrim);
        // A torn stub of the gate in each rail, tall at the rail and down to almost nothing towards the middle; its
        // faces show the gate's picture where the whole gate's was (its hazard band at the foot still).
        m.TornSlab(-hg + 1, -clear, -half, half, height * 0.38f, 2, -hg + 1, hg - 1, top - 4, p.Door, seed);
        m.TornSlab(clear, hg - 1, -half, half, 2, height * 0.3f, -hg + 1, hg - 1, top - 4, p.Door, seed + 1);
        // Plates of it, fallen along the walls on the side away from the rams (where the gap leaves room beside the middle).
        if (hg - 12 >= clear)
        {
            m.Plate(new(-hg + 6.5f, 0.8f, half + 10), new(9, 0.8f, 4), 1.35f, p.Door);
            m.Plate(new(hg - 6.5f, 0.8f, half + 8), new(8, 0.8f, 3.5f), -1.8f, p.Door);
        }
        // The rams, knocked aside to the pillars: barrels snapped on their mounts, a stub of rod in each.
        float rz = -half - 5, ramX = Math.Max(hg * 0.55f, hg - rail - 5);
        foreach (float rx in new[] { -ramX, ramX })
        {
            m.Box(new(rx - 4, -4, rz - 4), new(rx + 4, 4, rz + 4), p.Trim, Region.Hazard);
            m.Cylinder(rx, rz, 3, 4, top * 0.3f, Region.DarkTrim, null);
            m.Cylinder(rx, rz, 3.7f, top * 0.3f - 3, top * 0.3f, Region.MetalTrim, Region.DarkTrim);
            m.Cylinder(rx, rz, 1.4f, top * 0.3f, top * 0.3f + 4, Region.MetalTrim, Region.DarkTrim);
        }
        return m.Build(new(-hg, 0, rz - 4), new(hg, height * 0.42f, hd));
    }

    /// <summary>
    /// What is left of a tunnel once it collapses, all at the edges of its gap like a broken door's: the middle
    /// (<see cref="BrokenDoorClear"/> of it) is clear ground. Along each wall lie broken slabs of its block and roof (or
    /// of its vault, with lumps of its rock, for a rock roof), heaped low. None of it blocks.
    /// </summary>
    public static StructureMesh BrokenTunnel(StructureLook look, StructureRoof roof, float gap, float depth, float height, int seed)
    {
        var p = PaletteOf(look);
        var m = new MeshBuilder();
        float hg = gap / 2, hd = depth / 2, clear = hg * BrokenDoorClear;
        float band = hg - clear; // the width along each wall the wreckage lies in
        foreach (int side in new[] { -1, 1 })
        {
            float cx = side * (clear + band / 2);
            // Slabs lying along the wall, one partly on another, turned only a little so they keep out of the middle:
            // the block's sides, and a piece of the roof (or of the vault).
            for (int i = 0; i < 3; i++)
            {
                float along = -hd * 0.6f + hd * 0.6f * i;
                float jitter = (float)Texture.Jitter(i, side + 5, seed) - 0.5f;
                var size = new Vector3(band * 0.35f, 1.2f, Math.Min(hd * 0.3f, 8));
                m.Plate(new(cx + jitter * band * 0.1f, 1.2f + (i & 1) * 2.4f, along + jitter * 4), size,
                    jitter * 0.3f, i == 1 ? (roof == StructureRoof.Flat ? p.Pad : Region.ConcreteWall) : p.Wall);
            }
            if (roof == StructureRoof.Cliff) // thick lumps of its rock, against the wall
                for (int i = 0; i < 2; i++)
                    m.Plate(new(side * (hg - band * 0.3f), 2.2f + i * 4.4f, (i - 0.5f) * hd * 0.7f), new(band * 0.28f, 2.2f, Math.Min(hd * 0.25f, 6)),
                        0.3f * side + 0.2f * i, Region.RockTop);
            else // a stub of the block, broken off low against the wall
                m.TornSlab(side < 0 ? -hg : hg - band * 0.35f, side < 0 ? -hg + band * 0.35f : hg, -hd * 0.7f, hd * 0.7f,
                    side < 0 ? height * 0.28f : 3, side < 0 ? 3 : height * 0.24f, -hg, hg, height, p.Entrance, seed + 3 + side);
        }
        return m.Build(new(-hg, 0, -hd), new(hg, height * 0.3f, hd));
    }

    /// <summary>
    /// A tunnel's centre, sealed: either a block whose ends look like a closed tunnel mouth under a flat roof level with
    /// the wall tops (buildings go on it), or, as the open tunnel's, jagged rock on a concrete vault, the vault's arch
    /// closed at each end by the mouth's shutter, set back so the vault's underside shows.
    /// </summary>
    public static StructureMesh TunnelTop(StructureLook look, StructureRoof roof, float gap, float wallWidth, float depth, float height, int seed)
    {
        var p = PaletteOf(look);
        var m = new MeshBuilder();
        float hg = gap / 2, hd = depth / 2, rim = Math.Min(6, height * 0.08f), inset = Math.Min(3, hd * 0.15f);
        m.Box(new(-hg, -Skirt, -hd + inset), new(hg, 0, hd - inset), p.Wall, null);
        if (roof == StructureRoof.Flat)
        {
            m.Box(new(-hg, 0, -hd + inset), new(hg, height - rim, hd - inset), p.Wall, null, ends: p.Entrance, stretchEnds: true);
            m.Box(new(-hg, height - rim, -hd), new(hg, height, hd), p.Trim, p.Pad, stretchTop: true);
            return m.Build(new(-hg, 0, -hd), new(hg, height, hd));
        }
        RockOnVault(m, hg, hg + wallWidth, hd, height, seed);
        m.ArchFace(-hg, hg, -hd + inset, height * VaultSpring, height - VaultCrownBelow, p.Entrance, -1);
        m.ArchFace(-hg, hg, hd - inset, height * VaultSpring, height - VaultCrownBelow, p.Entrance, 1);
        return m.Build(new(-hg, 0, -hd), new(hg, height, hd));
    }

    /// <summary>The vault under a rock roof springs at this share of the height; its crown is this far below the top.</summary>
    const float VaultSpring = 0.55f, VaultCrownBelow = 18;

    /// <summary>
    /// A rock roof on a concrete vault over a passage (<paramref name="hg"/> half its width, <paramref name="hd"/> half
    /// its depth): the rock spills over the walls (out to <paramref name="reach"/>) and rests on their tops; the vault's
    /// crown stays below the lowest the rock hangs (8 + 6 × 1.5 under its base), its top inside the rock.
    /// </summary>
    static void RockOnVault(MeshBuilder m, float hg, float reach, float hd, float height, int seed)
    {
        m.Vault(-hg, hg, -hd, hd, height * VaultSpring, height - VaultCrownBelow, height - 6);
        m.Rock(-reach - 4, reach + 4, -hd - 4, hd + 4, height - 8, height * 0.45f + 8, 6, seed);
    }

    /// <summary>
    /// An open tunnel's roof over the passage, resting on the walls: a jagged rock mass carried on a concrete vault
    /// (an arch springing from the walls' inner faces, so it looks engineered), or a flat bridge slab.
    /// </summary>
    public static StructureMesh OpenRoof(StructureLook look, StructureRoof roof, float gap, float wallWidth, float depth, float height, int seed)
    {
        var p = PaletteOf(look);
        var m = new MeshBuilder();
        float hg = gap / 2, hd = depth / 2, reach = hg + wallWidth;
        if (roof == StructureRoof.Flat)
        {
            m.Box(new(-reach, height, -hd), new(reach, height + 8, hd), p.Trim, p.Top, bottom: true);
            return m.Build(new(-reach, height, -hd), new(reach, height + 8, hd));
        }
        RockOnVault(m, hg, reach, hd, height, seed);
        return m.Build(new(-reach, height - 8, -hd), new(reach, height + height * 0.4f, hd)); // the roof, not the passage under it
    }

    /// <summary>A repair marker: a small post, striped, with a green light on top of its panel.</summary>
    public static StructureMesh Repair(StructureLook look, bool red = true)
    {
        var p = PaletteOf(look);
        var m = new MeshBuilder();
        m.Box(new(-7, -4, -7), new(7, 2, 7), p.Trim, Region.DarkTrim); // small: a short skirt does
        // The repair panel's cell holds both: the green-lit panel on its left half, the red-lit on its right.
        float s0 = red ? 0.5f : 0, s1 = red ? 1 : 0.5f;
        m.Box(new(-4.5f, 2, -4.5f), new(4.5f, 20, 4.5f), Region.RepairPanel, null, stretchSides: true, ends: Region.RepairPanel, stretchEnds: true, s0: s0, s1: s1);
        m.Box(new(-5.5f, 20, -5.5f), new(5.5f, 23, 5.5f), p.Trim, p.Top);
        return m.Build(new(-7, 0, -7), new(7, 23, 7));
    }

    /// <summary>A repeatable number from a name, to vary the rock between kinds.</summary>
    public static int Seed(string name)
    {
        uint h = 2166136261;
        foreach (char c in name)
            h = (h ^ c) * 16777619;
        return (int)(h & 0x7FFFFFFF);
    }

    /// <summary>The texture coordinate of a point in a cell (<paramref name="s"/>, <paramref name="t"/> from 0 to 1; t = 0 is the top row).</summary>
    internal static Vector2 Uv(Region region, float s, float t)
    {
        int col = (int)region % 4, row = (int)region / 4;
        const float span = Cell - 2 * Inset;
        return new((col * Cell + Inset + s * span) / AtlasSize, (row * Cell + Inset + t * span) / AtlasSize);
    }

    sealed class MeshBuilder
    {
        readonly List<ModelVertex> vertices = [];
        readonly List<int> triangles = [];

        public StructureMesh Build(Vector3 min, Vector3 max) => new(vertices, triangles, min, max);

        void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.LengthSquared() < 1e-10f)
                return;
            n = Vector3.Normalize(n);
            int i = vertices.Count;
            vertices.Add(new(a, n, ua));
            vertices.Add(new(b, n, ub));
            vertices.Add(new(c, n, uc));
            triangles.AddRange([i, i + 1, i + 2]);
        }

        /// <summary>A triangle turned to face <paramref name="outward"/>.</summary>
        void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0)
                Triangle(a, c, b, ua, uc, ub);
            else
                Triangle(a, b, c, ua, ub, uc);
        }

        /// <summary>
        /// A rectangle seen from outside: its top-left corner and the vectors to its right and bottom edges. Tiled
        /// cells repeat every <see cref="Tile"/> units from the top-left; a stretched cell covers the whole rectangle.
        /// </summary>
        public void Face(Vector3 topLeft, Vector3 right, Vector3 down, Region region, bool stretch = false, float s0 = 0, float s1 = 1)
        {
            float w = right.Length(), h = down.Length();
            if (w < 1e-3f || h < 1e-3f)
                return;
            Vector3 r = right / w, d = down / h;
            if (stretch)
            {
                Quad(topLeft, right, down, region, s0, s1, 0, 1);
                return;
            }
            for (float y = 0; y < h - 1e-3f; y += Tile)
                for (float x = 0; x < w - 1e-3f; x += Tile)
                {
                    float x2 = Math.Min(x + Tile, w), y2 = Math.Min(y + Tile, h);
                    Quad(topLeft + r * x + d * y, r * (x2 - x), d * (y2 - y), region, 0, (x2 - x) / Tile, 0, (y2 - y) / Tile);
                }
        }

        void Quad(Vector3 tl, Vector3 r, Vector3 d, Region region, float s0, float s1, float t0, float t1)
        {
            Vector3 bl = tl + d, br = tl + d + r, tr = tl + r;
            Vector2 uTl = Uv(region, s0, t0), uBl = Uv(region, s0, t1), uBr = Uv(region, s1, t1), uTr = Uv(region, s1, t0);
            Triangle(tl, bl, br, uTl, uBl, uBr);
            Triangle(tl, br, tr, uTl, uBr, uTr);
        }

        /// <summary>
        /// A box: its four sides in <paramref name="sides"/> (the two facing along z, the passage ends, in
        /// <paramref name="ends"/> if given), its top in <paramref name="top"/> (none when null: something covers it),
        /// and its bottom only when asked (nothing below ground is ever seen).
        /// </summary>
        public void Box(Vector3 min, Vector3 max, Region sides, Region? top, Region? ends = null, bool stretchEnds = false,
            bool stretchSides = false, bool stretchTop = false, bool bottom = false, float s0 = 0, float s1 = 1)
        {
            float x0 = min.X, y0 = min.Y, z0 = min.Z, x1 = max.X, y1 = max.Y, z1 = max.Z;
            var down = new Vector3(0, y0 - y1, 0);
            Face(new(x1, y1, z1), new(0, 0, z0 - z1), down, sides, stretchSides, s0, s1);
            Face(new(x0, y1, z0), new(0, 0, z1 - z0), down, sides, stretchSides, s0, s1);
            Face(new(x0, y1, z1), new(x1 - x0, 0, 0), down, ends ?? sides, ends != null ? stretchEnds : stretchSides, s0, s1);
            Face(new(x1, y1, z0), new(x0 - x1, 0, 0), down, ends ?? sides, ends != null ? stretchEnds : stretchSides, s0, s1);
            if (top is Region t)
                Face(new(x0, y1, z0), new(x1 - x0, 0, 0), new(0, 0, z1 - z0), t, stretchTop);
            if (bottom && top is Region b)
                Face(new(x0, y0, z1), new(x1 - x0, 0, 0), new(0, 0, z0 - z1), b);
        }

        /// <summary>
        /// A slab over x0..x1, z0..z1 from the ground up to a torn top edge: about <paramref name="atX0"/> high at x0 and
        /// <paramref name="atX1"/> at x1, zigzagging and roughened. Its faces show the cell stretched as over a whole face
        /// from faceX0 to faceX1 and up to <paramref name="faceTop"/> (so a piece of a gate shows that piece of its
        /// picture); its torn edge is dark metal.
        /// </summary>
        public void TornSlab(float x0, float x1, float z0, float z1, float atX0, float atX1, float faceX0, float faceX1, float faceTop, Region face, int seed)
        {
            int n = Math.Max(3, (int)((x1 - x0) / 4));
            var xs = new float[n + 1];
            var ys = new float[n + 1];
            for (int i = 0; i <= n; i++)
            {
                xs[i] = x0 + (x1 - x0) * i / n;
                float along = atX0 + (atX1 - atX0) * i / n;
                double tooth = (i & 1) == 0 ? 0.62 : 1.0; // a zigzag, roughened
                ys[i] = Math.Max(1.5f, along * (float)(tooth * (0.8 + 0.4 * Texture.Jitter(i, 7, seed))));
            }
            Vector2 FaceUv(float x, float y) => Uv(face, Math.Clamp((x - faceX0) / (faceX1 - faceX0), 0, 1), Math.Clamp((faceTop - y) / faceTop, 0, 1));
            float zc = (z0 + z1) / 2;
            for (int i = 0; i < n; i++)
            {
                foreach (float z in new[] { z0, z1 })
                {
                    var outward = new Vector3(0, 0, z < zc ? -1 : 1);
                    Vector3 a = new(xs[i], 0, z), b = new(xs[i + 1], 0, z), c = new(xs[i + 1], ys[i + 1], z), d = new(xs[i], ys[i], z);
                    Triangle(a, b, c, FaceUv(a.X, a.Y), FaceUv(b.X, b.Y), FaceUv(c.X, c.Y), outward);
                    Triangle(a, c, d, FaceUv(a.X, a.Y), FaceUv(c.X, c.Y), FaceUv(d.X, d.Y), outward);
                }
                // The torn edge between the faces.
                Vector3 e0 = new(xs[i], ys[i], z0), e1 = new(xs[i + 1], ys[i + 1], z0), e2 = new(xs[i + 1], ys[i + 1], z1), e3 = new(xs[i], ys[i], z1);
                var up = Vector3.Cross(e3 - e0, e1 - e0);
                if (up.Y < 0)
                    up = -up;
                Triangle(e0, e1, e2, Uv(Region.DarkTrim, 0, 0), Uv(Region.DarkTrim, 1, 0), Uv(Region.DarkTrim, 1, 1), up);
                Triangle(e0, e2, e3, Uv(Region.DarkTrim, 0, 0), Uv(Region.DarkTrim, 1, 1), Uv(Region.DarkTrim, 0, 1), up);
            }
            foreach (var (x, y, sign) in new[] { (x0, ys[0], -1f), (x1, ys[n], 1f) })
                Face(sign < 0 ? new(x, y, z0) : new(x, y, z1), new(0, 0, sign < 0 ? z1 - z0 : z0 - z1), new(0, -y, 0), Region.DarkTrim, stretch: true);
        }

        /// <summary>A flat plate lying on the ground, turned by <paramref name="yaw"/> (radians) about its centre: its top the cell stretched, its edges dark metal.</summary>
        public void Plate(Vector3 centre, Vector3 half, float yaw, Region top)
        {
            float c = (float)Math.Cos(yaw), s = (float)Math.Sin(yaw);
            Vector3 P(float x, float y, float z) => centre + new Vector3(x * c - z * s, y, x * s + z * c);
            var corner = new Vector3[8];
            for (int i = 0; i < 8; i++)
                corner[i] = P((i & 1) == 0 ? -half.X : half.X, (i & 2) == 0 ? -half.Y : half.Y, (i & 4) == 0 ? -half.Z : half.Z);
            // Top: corners 2, 3, 7, 6 (y up); the four edges.
            Triangle(corner[2], corner[6], corner[7], Uv(top, 0, 0), Uv(top, 0, 1), Uv(top, 1, 1), Vector3.UnitY);
            Triangle(corner[2], corner[7], corner[3], Uv(top, 0, 0), Uv(top, 1, 1), Uv(top, 1, 0), Vector3.UnitY);
            foreach (var (i0, i1, i2, i3) in new[] { (0, 1, 3, 2), (4, 6, 7, 5), (0, 2, 6, 4), (1, 5, 7, 3) })
            {
                var outward = (corner[i0] + corner[i1] + corner[i2] + corner[i3]) / 4 - centre;
                outward.Y = 0;
                Triangle(corner[i0], corner[i1], corner[i2], Uv(Region.DarkTrim, 0, 0), Uv(Region.DarkTrim, 1, 0), Uv(Region.DarkTrim, 1, 1), outward);
                Triangle(corner[i0], corner[i2], corner[i3], Uv(Region.DarkTrim, 0, 0), Uv(Region.DarkTrim, 1, 1), Uv(Region.DarkTrim, 0, 1), outward);
            }
        }

        /// <summary>Another mesh, moved and turned by <paramref name="transform"/>, added to this one.</summary>
        public void Append(StructureMesh mesh, Matrix4x4 transform)
        {
            int first = vertices.Count;
            foreach (var v in mesh.Vertices)
                vertices.Add(new(Vector3.Transform(v.Position, transform), Vector3.Normalize(Vector3.TransformNormal(v.Normal, transform)), v.Uv));
            triangles.AddRange(mesh.Triangles.Select(i => i + first));
        }

        /// <summary>A beam from p0 to p1, <paramref name="halfWide"/> to each side and <paramref name="halfHigh"/> up and down, its faces the cell stretched.</summary>
        public void Beam(Vector3 p0, Vector3 p1, float halfWide, float halfHigh, Region region)
        {
            var along = Vector3.Normalize(p1 - p0);
            var up = Math.Abs(along.Y) > 0.95f ? Vector3.UnitX : Vector3.UnitY;
            var side = Vector3.Normalize(Vector3.Cross(along, up)) * halfWide;
            var over = Vector3.Normalize(Vector3.Cross(side, along)) * halfHigh;
            Vector3[] a = [p0 - side - over, p0 + side - over, p0 + side + over, p0 - side + over];
            Vector3[] b = [p1 - side - over, p1 + side - over, p1 + side + over, p1 - side + over];
            var middle = (p0 + p1) / 2;
            for (int k = 0; k < 4; k++)
            {
                int n = (k + 1) % 4;
                var outward = (a[k] + a[n]) / 2 - p0;
                Triangle(a[k], a[n], b[n], Uv(region, 0, 0), Uv(region, 1, 0), Uv(region, 1, 1), outward);
                Triangle(a[k], b[n], b[k], Uv(region, 0, 0), Uv(region, 1, 1), Uv(region, 0, 1), outward);
            }
            Triangle(a[0], a[1], a[2], Uv(region, 0, 0), Uv(region, 1, 0), Uv(region, 1, 1), p0 - middle);
            Triangle(a[0], a[2], a[3], Uv(region, 0, 0), Uv(region, 1, 1), Uv(region, 0, 1), p0 - middle);
            Triangle(b[0], b[1], b[2], Uv(region, 0, 0), Uv(region, 1, 0), Uv(region, 1, 1), p1 - middle);
            Triangle(b[0], b[2], b[3], Uv(region, 0, 0), Uv(region, 1, 1), Uv(region, 0, 1), p1 - middle);
        }

        /// <summary>
        /// A tube (or cone) from p0 (radius r0) to p1 (radius r1), of <paramref name="sides"/> faces, capped at both
        /// ends: the cell wraps once round it and runs its length.
        /// </summary>
        public void Tube(Vector3 p0, Vector3 p1, float r0, float r1, Region region, int sides = 8)
        {
            var along = Vector3.Normalize(p1 - p0);
            var up = Math.Abs(along.Y) > 0.95f ? Vector3.UnitX : Vector3.UnitY;
            var u = Vector3.Normalize(Vector3.Cross(along, up));
            var w = Vector3.Cross(u, along);
            Vector3 Ring(Vector3 centre, float r, int k) => centre + (u * MathF.Cos(2 * MathF.PI * k / sides) + w * MathF.Sin(2 * MathF.PI * k / sides)) * r;
            for (int k = 0; k < sides; k++)
            {
                Vector3 a0 = Ring(p0, r0, k), a1 = Ring(p0, r0, k + 1), b0 = Ring(p1, r1, k), b1 = Ring(p1, r1, k + 1);
                var outward = (a0 + a1 + b0 + b1) / 4 - (p0 + p1) / 2;
                float s0 = k / (float)sides, s1 = (k + 1) / (float)sides;
                Triangle(a0, a1, b1, Uv(region, s0, 1), Uv(region, s1, 1), Uv(region, s1, 0), outward);
                Triangle(a0, b1, b0, Uv(region, s0, 1), Uv(region, s1, 0), Uv(region, s0, 0), outward);
                if (r0 > 0.01f)
                    Triangle(p0, a0, a1, Uv(region, 0.5f, 0.5f), Uv(region, 0.5f, 0), Uv(region, 0.6f, 0), -along);
                if (r1 > 0.01f)
                    Triangle(p1, b0, b1, Uv(region, 0.5f, 0.5f), Uv(region, 0.5f, 0), Uv(region, 0.6f, 0), along);
            }
        }

        /// <summary>
        /// A slab between x0 and x1 shaped as a slice of a disc in the y-z plane: centre (cy, cz), from angle a0 to a1
        /// (radians; 0 points along +z, π/2 up). Its flat faces show <paramref name="face"/> as a picture of the disc,
        /// its rim and cut edges <paramref name="edge"/>. A whole turn makes a wheel.
        /// </summary>
        public void ArcSlab(float x0, float x1, float cy, float cz, float radius, float a0, float a1, Region face, Region edge)
        {
            int n = Math.Max(3, (int)Math.Ceiling((a1 - a0) / (MathF.PI / 10)));
            bool whole = a1 - a0 >= 2 * MathF.PI - 1e-4f;
            var rim = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float a = a0 + (a1 - a0) * i / n;
                rim[i] = new(cz + radius * MathF.Cos(a), cy + radius * MathF.Sin(a)); // (z, y)
            }
            Vector2 FaceUv(Vector2 p) => Uv(face, (p.X - (cz - radius)) / (2 * radius), ((cy + radius) - p.Y) / (2 * radius));
            var centre = new Vector2(cz, cy);
            foreach (float x in new[] { x0, x1 })
            {
                var outward = new Vector3(x == x0 ? -1 : 1, 0, 0);
                for (int i = 0; i < n; i++)
                    Triangle(new(x, centre.Y, centre.X), new(x, rim[i].Y, rim[i].X), new(x, rim[i + 1].Y, rim[i + 1].X), FaceUv(centre), FaceUv(rim[i]), FaceUv(rim[i + 1]), outward);
            }
            for (int i = 0; i < n; i++)
            {
                var mid = (rim[i] + rim[i + 1]) / 2 - centre;
                var outward = new Vector3(0, mid.Y, mid.X);
                Vector3 p = new(x0, rim[i].Y, rim[i].X), q = new(x0, rim[i + 1].Y, rim[i + 1].X), r = new(x1, rim[i + 1].Y, rim[i + 1].X), t = new(x1, rim[i].Y, rim[i].X);
                Triangle(p, q, r, Uv(edge, 0, 0), Uv(edge, 1, 0), Uv(edge, 1, 1), outward);
                Triangle(p, r, t, Uv(edge, 0, 0), Uv(edge, 1, 1), Uv(edge, 0, 1), outward);
            }
            if (whole)
                return;
            foreach (var (end, next) in new[] { (rim[0], rim[1]), (rim[n], rim[n - 1]) }) // the cut faces of a slice, from its centre out
            {
                var inward = next - end; // along the rim, into the slice: the cut face looks the other way
                var away = new Vector3(0, -inward.Y, -inward.X);
                Vector3 p = new(x0, centre.Y, centre.X), q = new(x0, end.Y, end.X), r = new(x1, end.Y, end.X), t = new(x1, centre.Y, centre.X);
                Triangle(p, q, r, Uv(edge, 0, 0), Uv(edge, 1, 0), Uv(edge, 1, 1), away);
                Triangle(p, r, t, Uv(edge, 0, 0), Uv(edge, 1, 1), Uv(edge, 0, 1), away);
            }
        }

        /// <summary>An upright prism of <paramref name="sides"/> faces round (x, z), from y0 to y1, with a cap on top if given.</summary>
        public void Cylinder(float x, float z, float radius, float y0, float y1, Region side, Region? cap, int sides = 8)
        {
            var centre = new Vector3(x, y1, z);
            for (int k = 0; k < sides; k++)
            {
                double a0 = 2 * Math.PI * k / sides, a1 = 2 * Math.PI * (k + 1) / sides;
                var p0 = new Vector3(x + radius * (float)Math.Cos(a0), 0, z + radius * (float)Math.Sin(a0));
                var p1 = new Vector3(x + radius * (float)Math.Cos(a1), 0, z + radius * (float)Math.Sin(a1));
                var outward = (p0 + p1) / 2 - new Vector3(x, 0, z);
                Vector3 t0 = p0 with { Y = y1 }, t1 = p1 with { Y = y1 }, b0 = p0 with { Y = y0 }, b1 = p1 with { Y = y0 };
                float s0 = k / (float)sides, s1 = (k + 1) / (float)sides;
                Triangle(t0, b0, b1, Uv(side, s0, 0), Uv(side, s0, 1), Uv(side, s1, 1), outward);
                Triangle(t0, b1, t1, Uv(side, s0, 0), Uv(side, s1, 1), Uv(side, s1, 0), outward);
                if (cap is Region c)
                    Triangle(centre, t0, t1, Uv(c, 0.5f, 0.5f), Uv(c, 0.5f + 0.5f * (float)Math.Cos(a0), 0.5f + 0.5f * (float)Math.Sin(a0)),
                        Uv(c, 0.5f + 0.5f * (float)Math.Cos(a1), 0.5f + 0.5f * (float)Math.Sin(a1)), Vector3.UnitY);
            }
        }

        /// <summary>
        /// The arch-shaped face closing a vault's opening at <paramref name="z"/>, facing out along z (<paramref name="facing"/>
        /// ±1): a rectangle from the ground to the springing under a half ellipse up to the crown. It is mapped onto the
        /// tunnel mouth's picture so the shutter fills the arch and the striped ring runs round its edge (the picture's
        /// arch springs at 0.42 of its height, as the vault's does at 55% of 80 under a crown of 62).
        /// </summary>
        public void ArchFace(float x0, float x1, float z, float spring, float crown, Region region, int facing)
        {
            const int n = 14;
            float cx = (x0 + x1) / 2, half = (x1 - x0) / 2;
            var outline = new List<Vector2> { new(x0, 0) };
            for (int i = 0; i <= n; i++)
            {
                double a = Math.PI * (1 - i / (double)n);
                outline.Add(new(cx + half * (float)Math.Cos(a), spring + (crown - spring) * (float)Math.Sin(a)));
            }
            outline.Add(new(x1, 0));
            Vector2 UvOf(Vector2 p) => Uv(region, 0.165f + 0.67f * (p.X - x0) / (x1 - x0), 0.985f - 0.8f * p.Y / crown);
            var centre = new Vector2(cx, spring / 2);
            var outward = new Vector3(0, 0, facing);
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 a = outline[i], b = outline[(i + 1) % outline.Count];
                Triangle(new(centre.X, centre.Y, z), new(a.X, a.Y, z), new(b.X, b.Y, z), UvOf(centre), UvOf(a), UvOf(b), outward);
            }
        }

        /// <summary>
        /// A concrete barrel vault over x0..x1 (the passage), running z0..z1: its underside a half ellipse from
        /// <paramref name="spring"/> at the sides up to <paramref name="crown"/> in the middle, and at each end a ring of
        /// concrete from that curve up to <paramref name="top"/>. Its top and its ends against the walls are hidden.
        /// </summary>
        public void Vault(float x0, float x1, float z0, float z1, float spring, float crown, float top)
        {
            const int n = 14;
            float cx = (x0 + x1) / 2, half = (x1 - x0) / 2, rise = crown - spring;
            var curve = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                double a = Math.PI * (1 - i / (double)n);
                curve[i] = new(cx + half * (float)Math.Cos(a), spring + rise * (float)Math.Sin(a));
            }
            for (int i = 0; i < n; i++)
            {
                Vector2 a = curve[i], b = curve[i + 1];
                var inward = new Vector3(cx - (a.X + b.X) / 2, spring - (a.Y + b.Y) / 2, 0); // towards the passage
                Vector3 a0 = new(a.X, a.Y, z0), a1 = new(a.X, a.Y, z1), b0 = new(b.X, b.Y, z0), b1 = new(b.X, b.Y, z1);
                float t0 = i / (float)n, t1 = (i + 1) / (float)n;
                Vector2 ua0 = Uv(Region.ConcreteTop, 0, t0), ua1 = Uv(Region.ConcreteTop, 1, t0), ub0 = Uv(Region.ConcreteTop, 0, t1), ub1 = Uv(Region.ConcreteTop, 1, t1);
                Triangle(a0, b0, b1, ua0, ub0, ub1, inward);
                Triangle(a0, b1, a1, ua0, ub1, ua1, inward);
                foreach (float z in new[] { z0, z1 })
                {
                    var outward = new Vector3(0, 0, z == z0 ? -1 : 1);
                    float sa = (a.X - x0) / (x1 - x0), sb = (b.X - x0) / (x1 - x0), ta = (top - a.Y) / (top - spring), tb = (top - b.Y) / (top - spring);
                    Vector3 pa = new(a.X, a.Y, z), pb = new(b.X, b.Y, z), qa = new(a.X, top, z), qb = new(b.X, top, z);
                    Vector2 upa = Uv(Region.ConcreteWall, sa, ta), upb = Uv(Region.ConcreteWall, sb, tb), uqa = Uv(Region.ConcreteWall, sa, 0), uqb = Uv(Region.ConcreteWall, sb, 0);
                    Triangle(qa, pa, pb, uqa, upa, upb, outward);
                    Triangle(qa, pb, qb, uqa, upb, uqb, outward);
                }
            }
        }

        /// <summary>
        /// A jagged rock mass over x0..x1, z0..z1, like a piece of cliff: its underside about <paramref name="baseY"/>
        /// (hanging down by up to <paramref name="hang"/>), its top ridged and peaked up to about
        /// <paramref name="thickness"/> above it, its outline pushed in and out, and its sides bulging and faceted. The
        /// grid's cells divide a texture tile exactly, so no cell's texture wraps.
        /// </summary>
        public void Rock(float x0, float x1, float z0, float z1, float baseY, float thickness, float hang, int seed)
        {
            const float cell = Tile / 5;
            int nx = Math.Max(3, (int)Math.Ceiling((x1 - x0) / cell - 1e-3f)), nz = Math.Max(3, (int)Math.Ceiling((z1 - z0) / cell - 1e-3f));
            float X(int i) => Math.Min(x0 + i * cell, x1);
            float Z(int j) => Math.Min(z0 + j * cell, z1);
            // Noise with bumps of a given size in game units (it repeats only every 64 bumps).
            static double Bumps(double x, double z, double size, int octaves, int seed) => Texture.Fractal(x / (size * 64), z / (size * 64), 64, octaves, seed);
            var top = new float[nx + 1, nz + 1];
            var under = new float[nx + 1, nz + 1];
            var pos = new Vector2[nx + 1, nz + 1];
            var outwardOf = new Vector2[nx + 1, nz + 1];
            float cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= nz; j++)
                {
                    float x = X(i), z = Z(j);
                    int edge = Math.Min(Math.Min(i, nx - i), Math.Min(j, nz - j));
                    double n = Bumps(x, z, 40, 3, seed), ridge = 1 - Math.Abs(2 * Bumps(x, z, 20, 3, seed + 7) - 1);
                    double jitter = Texture.Jitter(i, j, seed + 3) - 0.5; // facets
                    double lift = edge == 0 ? 0.18 + 0.3 * n + 0.16 * jitter
                        : (0.32 + 0.4 * n + 0.4 * ridge * ridge + 0.11 * jitter) * (edge == 1 ? 0.8 : 1);
                    top[i, j] = baseY + thickness * (float)Math.Clamp(lift, 0.08, 1.2);
                    under[i, j] = baseY - hang * (float)(Bumps(x, z, 12, 2, seed + 13) + 0.4 * jitter) * (edge == 0 ? 1.5f : 1);
                    var p = new Vector2(x, z);
                    if (edge == 0) // the outline: pushed in or out, so the rock's sides are ragged
                    {
                        var out_ = Vector2.Normalize(new Vector2(i == 0 ? -1 : i == nx ? 1 : 0, j == 0 ? -1 : j == nz ? 1 : 0));
                        p += out_ * (float)(Bumps(x, z, 10, 2, seed + 29) * 14 - 5);
                        outwardOf[i, j] = out_;
                    }
                    pos[i, j] = p;
                }
            Vector3 T(int i, int j) => new(pos[i, j].X, top[i, j], pos[i, j].Y);
            Vector3 B(int i, int j) => new(pos[i, j].X, under[i, j], pos[i, j].Y);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    // Texture coordinates come from the grid before the outline is pushed about, a tile at a time.
                    int ti = (int)((X(i) - x0) / Tile + 1e-3f), tj = (int)((Z(j) - z0) / Tile + 1e-3f);
                    float s0 = (X(i) - x0) / Tile - ti, s1 = (X(i + 1) - x0) / Tile - ti, t0 = (Z(j) - z0) / Tile - tj, t1 = (Z(j + 1) - z0) / Tile - tj;
                    Vector2 u00 = Uv(Region.RockTop, s0, t0), u10 = Uv(Region.RockTop, s1, t0), u01 = Uv(Region.RockTop, s0, t1), u11 = Uv(Region.RockTop, s1, t1);
                    if (((i + j) & 1) == 0)
                    {
                        Triangle(T(i, j), T(i, j + 1), T(i + 1, j + 1), u00, u01, u11, Vector3.UnitY);
                        Triangle(T(i, j), T(i + 1, j + 1), T(i + 1, j), u00, u11, u10, Vector3.UnitY);
                    }
                    else
                    {
                        Triangle(T(i, j), T(i, j + 1), T(i + 1, j), u00, u01, u10, Vector3.UnitY);
                        Triangle(T(i + 1, j), T(i, j + 1), T(i + 1, j + 1), u10, u01, u11, Vector3.UnitY);
                    }
                    Vector2 d00 = Uv(Region.RockSide, s0, t0), d10 = Uv(Region.RockSide, s1, t0), d01 = Uv(Region.RockSide, s0, t1), d11 = Uv(Region.RockSide, s1, t1);
                    Triangle(B(i, j), B(i + 1, j + 1), B(i, j + 1), d00, d11, d01, -Vector3.UnitY);
                    Triangle(B(i, j), B(i + 1, j), B(i + 1, j + 1), d00, d10, d11, -Vector3.UnitY);
                }
            // The sides, round the outline: the side cell is stretched from the top (t = 0) to the lowest underside.
            float high = float.MinValue, low = float.MaxValue;
            foreach (float y in top)
                high = Math.Max(high, y);
            foreach (float y in under)
                low = Math.Min(low, y);
            var ring = new List<(int I, int J)>();
            for (int i = 0; i < nx; i++)
                ring.Add((i, 0));
            for (int j = 0; j < nz; j++)
                ring.Add((nx, j));
            for (int i = nx; i > 0; i--)
                ring.Add((i, nz));
            for (int j = nz; j > 0; j--)
                ring.Add((0, j));
            float V(float y) => Math.Clamp((high - y) / (high - low), 0, 1);
            // Down each side, a middle ring bulges out (or in) from the outline, so the faces lean and break like a cliff's.
            Vector3 M(int i, int j)
            {
                var p = pos[i, j] + outwardOf[i, j] * (float)(Bumps(pos[i, j].X, pos[i, j].Y, 12, 2, seed + 41) * 11 - 3);
                float y = top[i, j] + (under[i, j] - top[i, j]) * (float)(0.35 + 0.3 * Bumps(pos[i, j].X, pos[i, j].Y, 16, 2, seed + 43));
                return new(p.X, y, p.Y);
            }
            for (int k = 0; k < ring.Count; k++)
            {
                var (ai, aj) = ring[k];
                var (bi, bj) = ring[(k + 1) % ring.Count];
                var outward = new Vector3((pos[ai, aj].X + pos[bi, bj].X) / 2 - cx, 0, (pos[ai, aj].Y + pos[bi, bj].Y) / 2 - cz);
                float sa = (k % 5) / 5f, sb = sa + 0.2f;
                Vector3 ta = T(ai, aj), tb = T(bi, bj), ma = M(ai, aj), mb = M(bi, bj), ba = B(ai, aj), bb = B(bi, bj);
                Vector2 uta = Uv(Region.RockSide, sa, V(ta.Y)), utb = Uv(Region.RockSide, sb, V(tb.Y)), uma = Uv(Region.RockSide, sa, V(ma.Y)),
                    umb = Uv(Region.RockSide, sb, V(mb.Y)), uba = Uv(Region.RockSide, sa, V(ba.Y)), ubb = Uv(Region.RockSide, sb, V(bb.Y));
                Triangle(ta, ma, mb, uta, uma, umb, outward);
                Triangle(ta, mb, tb, uta, umb, utb, outward);
                Triangle(ma, ba, bb, uma, uba, ubb, outward);
                Triangle(ma, bb, mb, uma, ubb, umb, outward);
            }
        }
    }

    /// <summary>
    /// The atlas's two textures as TGA files, its rock coloured like <paramref name="rock"/> (the map's own cliffs;
    /// a plain grey-brown when null). Built once for each colour.
    /// </summary>
    public static (byte[] Colour, byte[] Extra) TextureFiles(Rgba? rock = null) => files.GetOrAdd(rock ?? default, _ =>
    {
        var (colour, extra) = Texture.Atlas(rock);
        return (TgaWriter.Write(colour), TgaWriter.Write(extra));
    });

    static readonly System.Collections.Concurrent.ConcurrentDictionary<Rgba, (byte[], byte[])> files = new();

    /// <summary>
    /// Paints the atlas. Texture 1: colour, with alpha 0 everywhere (BAR mixes in team colour by alpha, and a map
    /// feature's team colour is black). Texture 2: red glow, green metalness, blue roughness, as BAR's model shader
    /// reads them for features marked cuspbr (cus_gl4.frag.glsl), and alpha 255 everywhere (alpha cuts holes). Tiled
    /// cells are painted from patterns that repeat exactly once across the cell's used part, so tiles meet without a seam.
    /// </summary>
    internal static partial class Texture
    {
        public static (RgbaImage Colour, RgbaImage Extra) Atlas(Rgba? rock = null)
        {
            // The rock's light and dark: the map's cliff colour brightened and darkened, or a plain grey-brown.
            var tint = rock is Rgba r ? new Vector3(r.R, r.G, r.B) : new Vector3(99, 90, 79);
            var palette = (Light: tint * 1.2f, Dark: tint * 0.8f);
            return Paint((region, s, t) => Paint(region, s, t, palette));
        }

        /// <summary>
        /// An atlas painted cell by cell: texture 1 with alpha 0 (or <paramref name="teamColour"/>'s for a cell: how much of
        /// its owner's colour a feature made for a team wears there), texture 2 with alpha 255.
        /// </summary>
        static (RgbaImage Colour, RgbaImage Extra) Paint(Func<Region, double, double, (Vector3 Colour, Vector3 Extra)> painter, Func<Region, byte>? teamColour = null)
        {
            var colour = new RgbaImage(AtlasSize, AtlasSize);
            var extra = new RgbaImage(AtlasSize, AtlasSize);
            const double span = Cell - 2 * Inset;
            for (int y = 0; y < AtlasSize; y++)
                for (int x = 0; x < AtlasSize; x++)
                {
                    var region = (Region)(y / Cell * 4 + x / Cell);
                    double s = (x % Cell - Inset + 0.5) / span, t = (y % Cell - Inset + 0.5) / span;
                    var (c, e) = painter(region, s, t);
                    colour.SetPixel(x, y, new Rgba(Byte(c.X), Byte(c.Y), Byte(c.Z), teamColour?.Invoke(region) ?? 0));
                    extra.SetPixel(x, y, new Rgba(Byte(e.X), Byte(e.Y), Byte(e.Z), 255));
                }
            return (colour, extra);
        }

        static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);

        /// <summary>Colour (r, g, b) and extra (glow, metalness, roughness) at a point of a cell.</summary>
        static (Vector3 Colour, Vector3 Extra) Paint(Region region, double s, double t, (Vector3 Light, Vector3 Dark) rock)
        {
            switch (region)
            {
                case Region.ConcreteWall: return (Concrete(s, t, 150, formwork: true), Rough);
                case Region.ConcreteTop: return (Concrete(s, t, 164, formwork: false), Rough);
                case Region.MetalWall: return (Steel(s, t, 1), Metal);
                case Region.MetalTop: return (Tread(s, t), Metal);
                case Region.ConcreteDoor: return Door(s, t, metal: false);
                case Region.MetalDoor: return Door(s, t, metal: true);
                case Region.ConcreteEntrance: return Entrance(s, t, metal: false);
                case Region.MetalEntrance: return Entrance(s, t, metal: true);
                case Region.Hazard: return (Stripes(s, t, 4), Paintwork);
                case Region.DarkTrim: return (Grey(48 * (0.86 + 0.14 * Fractal(s, t, 8, 3, 41))), Metal);
                case Region.RockSide: return (RockSide(s, t, rock), Stone);
                case Region.RockTop: return (RockTop(s, t, rock), Stone);
                case Region.ConcretePad: return Pad(s, t, metal: false);
                case Region.MetalPad: return Pad(s, t, metal: true);
                case Region.RepairPanel: return RepairPanel(s, t);
                default: return (Steel(s, t, 1.3), Metal);
            }
        }

        static readonly Vector3 Rough = new(0, 0, 220), Metal = new(0, 200, 110), Paintwork = new(0, 30, 150), Stone = new(0, 0, 235);

        static Vector3 Grey(double v) => new((float)v, (float)v, (float)v);

        static double Frac(double v) => v - Math.Floor(v);

        /// <summary>How close a point is to the cell's edge, in cell widths (0 at the edge), for tiled cells.</summary>
        static double EdgeDistance(double s, double t) => Math.Min(Math.Min(Frac(s), 1 - Frac(s)), Math.Min(Frac(t), 1 - Frac(t)));

        static Vector3 Concrete(double s, double t, double base_, bool formwork)
        {
            double blotch = Fractal(s, t, 4, 4, 11), grain = Fractal(s, t, 32, 2, 12);
            double v = base_ * (0.84 + 0.16 * blotch) + (grain - 0.5) * 20;
            const double px = 1.0 / (Cell - 2 * Inset);
            if (formwork)
            {
                if (EdgeDistance(s, t) < 1.4 * px)
                    v *= 0.72; // the formwork's joints
                else if (Math.Abs(Frac(t) - 0.5) < 0.8 * px)
                    v *= 0.88; // its boards
                foreach (var (hx, hy) in new[] { (0.25, 0.25), (0.75, 0.25), (0.25, 0.75), (0.75, 0.75) })
                {
                    double dx = (Frac(s) - hx) / px, dy = (Frac(t) - hy) / px;
                    if (dx * dx + dy * dy < 5.5)
                        v = 70; // tie holes
                }
            }
            else if (EdgeDistance(s, t) < 1.0 * px)
                v *= 0.9;
            return new((float)v, (float)(v * 0.985), (float)(v * 0.95));
        }

        static Vector3 Steel(double s, double t, double brightness)
        {
            const double px = 1.0 / (Cell - 2 * Inset);
            double brushed = Noise(s, t, 64, 4, 21), stain = Fractal(s, t, 4, 3, 22);
            double v = (92 + (brushed - 0.5) * 14) * brightness * (0.9 + 0.1 * stain);
            double edge = EdgeDistance(s, t);
            if (edge < 1.5 * px)
                v *= 0.55; // the seam between plates
            else if (edge < 3 * px)
                v *= 1.18; // its lit edge
            // Rivets along the plate's edges.
            double ds = Math.Min(Frac(s), 1 - Frac(s)), dt = Math.Min(Frac(t), 1 - Frac(t));
            double along = ds < dt ? Frac(t) : Frac(s), across = Math.Min(ds, dt);
            double rx = (Frac(along * 8) - 0.5) / 8 / px, ry = (across - 7 * px) / px;
            if (rx * rx + ry * ry < 3.2)
                v = rx + ry < 0 ? v * 1.45 : v * 0.7;
            return new((float)(v * 0.94), (float)v, (float)(v * 1.07));
        }

        static Vector3 Tread(double s, double t)
        {
            double v = 76 * (0.9 + 0.1 * Fractal(s, t, 8, 3, 31));
            double gx = Frac(s * 8), gy = Frac(t * 8);
            bool turn = ((int)Math.Floor(s * 8) + (int)Math.Floor(t * 8)) % 2 == 0;
            double a = turn ? Math.Abs(gx - 0.5) * 3 + Math.Abs(gy - 0.5) : Math.Abs(gx - 0.5) + Math.Abs(gy - 0.5) * 3;
            if (a < 0.55)
                v = 118;
            return new((float)(v * 0.95), (float)v, (float)(v * 1.06));
        }

        static Vector3 Stripes(double s, double t, double count)
        {
            bool yellow = Frac((s + t) * count) < 0.5;
            double wear = 0.82 + 0.18 * Fractal(s, t, 8, 3, 51);
            return yellow ? new Vector3((float)(226 * wear), (float)(176 * wear), (float)(34 * wear)) : Grey(34 * wear);
        }

        /// <summary>A gate that drops into the ground: one leaf of horizontal panels, a framed edge, hazard bands along its top and foot.</summary>
        static (Vector3, Vector3) Door(double s, double t, bool metal)
        {
            double frame = 0.05;
            if (s < frame || s > 1 - frame)
                return (Steel(s * 3, t * 3, 0.78), Metal);
            if (t < 0.1 || t > 0.9)
                return (Stripes(s * 2, t * 2, 3), Paintwork);
            var body = metal ? Steel(s * 2, t * 2, 1.05) : Concrete(s * 2, t * 2, 146, formwork: false);
            foreach (double line in new[] { 0.3, 0.5, 0.7 })
            {
                double d = t - line;
                if (Math.Abs(d) < 0.005)
                    body *= 0.6f; // the joint between panels
                else if (d > 0 && d < 0.012)
                    body *= 1.12f; // its lit lip
            }
            return (body, metal ? Metal : Rough);
        }

        static (Vector3, Vector3) Entrance(double s, double t, bool metal)
        {
            double dx = (s - 0.5) / 0.3, top = 0.42;
            bool opening = Math.Abs(dx) < 1 && (t > top || dx * dx + (t - top) / 0.2 * ((t - top) / 0.2) < 1) && t < 0.985;
            double ringX = (s - 0.5) / 0.335;
            bool ring = !opening && Math.Abs(ringX) < 1 && (t > top || ringX * ringX + (t - top) / 0.235 * ((t - top) / 0.235) < 1);
            if (opening)
            {
                // A shutter of steel slats closes the mouth.
                double slat = Frac(t * 22);
                var v = Steel(s * 2, t * 2, 0.7) * (slat < 0.14 ? 0.45f : slat < 0.3 ? 1.2f : 1f);
                return (v, Metal);
            }
            if (ring)
                return (Stripes(s * 3, t * 3, 3), Paintwork);
            var wall = metal ? Steel(s * 2, t * 2, 0.95) : Concrete(s * 2, t * 2, 150, formwork: true);
            if (t > 0.9)
                wall *= (float)(1 - (t - 0.9) * 2.5); // grime at the foot
            return (wall, metal ? Metal : Rough);
        }

        static (Vector3, Vector3) Pad(double s, double t, bool metal)
        {
            double edge = Math.Min(Math.Min(s, 1 - s), Math.Min(t, 1 - t));
            if (edge < 0.06)
                return (Stripes(s * 4, t * 4, 2), Paintwork);
            var v = metal ? Tread(s * 3, t * 3) : Concrete(s * 3, t * 3, 160, formwork: false);
            double ix = Math.Abs(s - 0.5), iy = Math.Abs(t - 0.5);
            double box = Math.Max(ix, iy);
            if (box > 0.22 && box < 0.24 || edge > 0.06 && edge < 0.075)
                v = new Vector3(222, 222, 210); // painted markings: the pad's square, and a line inside the stripes
            return (v, metal ? Metal : Rough);
        }

        static Vector3 RockSide(double s, double t, (Vector3 Light, Vector3 Dark) rock)
        {
            double strata = Noise(s, t * 0.25, 4, 12, 61), mottle = Fractal(s, t, 8, 4, 62);
            double crack = 1 - Math.Abs(2 * Fractal(s, t, 6, 3, 63) - 1);
            var v = Vector3.Lerp(rock.Dark * 0.9f, rock.Light, (float)(0.55 * strata + 0.45 * mottle));
            if (crack > 0.97)
                v *= 0.6f;
            return v;
        }

        static Vector3 RockTop(double s, double t, (Vector3 Light, Vector3 Dark) rock)
        {
            double mottle = Fractal(s, t, 8, 4, 71), dust = Fractal(s, t, 3, 3, 72), speck = Fractal(s, t, 64, 1, 73);
            var v = Vector3.Lerp(rock.Dark, rock.Light * 1.1f, (float)(0.6 * mottle + 0.4 * dust));
            if (speck > 0.8)
                v *= 1.12f;
            double crack = 1 - Math.Abs(2 * Fractal(s, t, 5, 3, 74) - 1);
            if (crack > 0.975)
                v *= 0.65f;
            return v;
        }

        /// <summary>A repair marker's panel, twice: its left half lit green (the structure stands), its right half red (to be repaired).</summary>
        static (Vector3, Vector3) RepairPanel(double s, double t)
        {
            bool red = s >= 0.5;
            s = Math.Clamp(red ? (s - 0.5) * 2 : s * 2, 0, 1);
            double lx = (s - 0.5) / 0.32, ly = (t - 0.22) / 0.1;
            if (lx * lx + ly * ly < 1)
                return (red ? new Vector3(250, 60, 50) : new Vector3(70, 240, 100), new Vector3(255, 0, 90)); // the light, glowing
            if (t < 0.42)
                return (Grey(40 * (0.9 + 0.1 * Fractal(s, t, 8, 2, 81))), Metal);
            if (t < 0.78)
                return (Stripes(s, t, 2), Paintwork);
            return (Steel(s * 2, t * 2, 0.8), Metal);
        }

        /// <summary>Smooth value noise that repeats every 1 in s and t: <paramref name="period"/> bumps across, then finer octaves.</summary>
        internal static double Fractal(double s, double t, int period, int octaves, int seed)
        {
            double sum = 0, amplitude = 1, total = 0;
            for (int o = 0; o < octaves; o++)
            {
                sum += Noise(s, t, period << o, period << o, seed + o * 101) * amplitude;
                total += amplitude;
                amplitude *= 0.5;
            }
            return sum / total;
        }

        /// <summary>Value noise from 0 to 1 that repeats every 1 in s and t, with its own number of bumps each way.</summary>
        internal static double Noise(double s, double t, int periodS, int periodT, int seed)
        {
            double x = s * periodS, y = t * periodT;
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            double fx = x - xi, fy = y - yi;
            double u = fx * fx * (3 - 2 * fx), v = fy * fy * (3 - 2 * fy);
            double a = Lattice(xi, yi, periodS, periodT, seed), b = Lattice(xi + 1, yi, periodS, periodT, seed);
            double c = Lattice(xi, yi + 1, periodS, periodT, seed), d = Lattice(xi + 1, yi + 1, periodS, periodT, seed);
            return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
        }

        /// <summary>A repeatable number from 0 to 1 for a grid point.</summary>
        internal static double Jitter(int x, int y, int seed) => Lattice(x, y, 1 << 20, 1 << 20, seed);

        static double Lattice(int x, int y, int periodS, int periodT, int seed)
        {
            uint h = (uint)(((x % periodS) + periodS) % periodS) * 374761393u + (uint)(((y % periodT) + periodT) % periodT) * 668265263u + (uint)seed * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }
    }
}
