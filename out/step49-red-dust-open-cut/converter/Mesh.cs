// A model builder for the mine's features: flat-shaded polygons on one atlas (Atlas.cs), every face cut into tiles
// of Tile units so a tiled cell repeats without its texture wrapping (an S3O's coordinates can't wrap inside an atlas
// cell). Model space: x forward (a machine's front), y up from the ground, z across. Triangles are wound as the
// editor's structure models are ((b - a) × (c - a) points out of the model). The site was left only days before the
// war: faces turned up carry red dust, and faces below a model's dirt line are caked in red earth.
using System.Numerics;
using MapCore.Assets;

enum Region
{
    Yellow, YellowDust, Orange, OrangeDust,
    Steel, SteelDust, Lattice, Rust,
    Tyre, Rubber, Window, Grille,
    Chrome, Hazard, Lamp, White,
    Track, Bucket, Ore, Flag,
    Bark, Leaves, Spinifex, Saltbush,
    RedRock, Mound, Corrugated, Concrete,
    Blue, Earth, Dark, Timber,
}

sealed class Mesh
{
    public const int AtlasWidth = 2048, AtlasHeight = 4096, Cell = 512, Inset = 6;
    public const float Tile = 128;

    public readonly List<ModelVertex> V = [];
    public readonly List<int> T = [];
    /// <summary>The box round the solid parts (not plants or thin rails on them): what the footprint and collision box cover.</summary>
    public Vector3 SolidMin = new(float.MaxValue), SolidMax = new(float.MinValue);
    /// <summary>Above zero while plants are being added (they don't count as solid).</summary>
    public int Plant;
    /// <summary>Faces turned up more than this (the normal's y) carry red dust, if dusty.</summary>
    public float DustFrom = 0.55f;
    public bool Dusty = true;
    /// <summary>Faces whose middle is below this height are caked in earth.</summary>
    public float DirtLine = -1e9f;

    public static Vector2 Uv(Region r, float s, float t)
    {
        int col = (int)r % 4, row = (int)r / 4;
        const float span = Cell - 2 * Inset;
        return new((col * Cell + Inset + Math.Clamp(s, 0, 1) * span) / AtlasWidth, (row * Cell + Inset + Math.Clamp(t, 0, 1) * span) / AtlasHeight);
    }

    /// <summary>A model's own rule for a face, before moss and earth (its centre, its normal, its region).</summary>
    public Func<Vector3, Vector3, Region, Region>? Rule;

    Region Weather(Region r, Vector3 n, Vector3 centre)
    {
        if (Rule != null) r = Rule(centre, n, r);
        float y = centre.Y;
        if (y < DirtLine && r is Region.Yellow or Region.YellowDust or Region.Orange or Region.OrangeDust or Region.Steel or Region.SteelDust
                or Region.White or Region.Blue or Region.Track or Region.Bucket)
            return Region.Earth;
        if (Dusty && n.Y > DustFrom)
            return r switch { Region.Yellow => Region.YellowDust, Region.Orange => Region.OrangeDust, Region.Steel => Region.SteelDust, _ => r };
        return r;
    }

    void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc, Vector3 outward)
    {
        var n = Vector3.Cross(b - a, c - a);
        if (n.LengthSquared() < 1e-8f)
            return;
        if (Vector3.Dot(n, outward) < 0)
        {
            (b, c) = (c, b);
            (ub, uc) = (uc, ub);
            n = -n;
        }
        n = Vector3.Normalize(n);
        if (Plant == 0)
            foreach (var p in new[] { a, b, c }) { SolidMin = Vector3.Min(SolidMin, p); SolidMax = Vector3.Max(SolidMax, p); }
        int i = V.Count;
        V.Add(new(a, n, ua));
        V.Add(new(b, n, ub));
        V.Add(new(c, n, uc));
        T.AddRange([i, i + 1, i + 2]);
    }

    /// <summary>
    /// A flat convex polygon facing <paramref name="outward"/>. Tiled: cut into Tile-unit squares of the face's own
    /// grid (texture "down" is down a wall, south on a floor), each square the whole cell. Stretched: the cell over the
    /// polygon's extent once.
    /// </summary>
    public void Poly(IReadOnlyList<Vector3> pts, Vector3 outward, Region region, bool stretch = false)
    {
        if (pts.Count < 3)
            return;
        var n = Vector3.Normalize(Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]));
        for (int k = 3; n.LengthSquared() < 0.5f && k < pts.Count; k++)
            n = Vector3.Normalize(Vector3.Cross(pts[1] - pts[0], pts[k] - pts[0]));
        if (float.IsNaN(n.X))
            return;
        if (Vector3.Dot(n, outward) < 0)
            n = -n;
        var centre = pts.Aggregate(Vector3.Zero, (s, p) => s + p) / pts.Count;
        var r = Weather(region, n, centre);
        Vector3 v = Math.Abs(n.Y) < 0.7f ? -Vector3.UnitY : Vector3.UnitZ;
        v = Vector3.Normalize(v - n * Vector3.Dot(v, n));
        var u = Vector3.Cross(n, v);
        float off = Vector3.Dot(n, pts[0]);
        var flat = pts.Select(p => new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v))).ToList();
        Vector3 Back(Vector2 q) => u * q.X + v * q.Y + n * off;
        float minA = flat.Min(q => q.X), maxA = flat.Max(q => q.X), minB = flat.Min(q => q.Y), maxB = flat.Max(q => q.Y);
        if (stretch)
        {
            float wa = Math.Max(1e-3f, maxA - minA), wb = Math.Max(1e-3f, maxB - minB);
            Vector2 U(Vector2 q) => Uv(r, (q.X - minA) / wa, (q.Y - minB) / wb);
            for (int k = 1; k + 1 < flat.Count; k++)
                Tri(Back(flat[0]), Back(flat[k]), Back(flat[k + 1]), U(flat[0]), U(flat[k]), U(flat[k + 1]), n);
            return;
        }
        for (int i = (int)Math.Floor(minA / Tile); i * Tile < maxA; i++)
            for (int j = (int)Math.Floor(minB / Tile); j * Tile < maxB; j++)
            {
                var piece = Clip(flat, i * Tile, (i + 1) * Tile, j * Tile, (j + 1) * Tile);
                if (piece.Count < 3)
                    continue;
                Vector2 U(Vector2 q) => Uv(r, (q.X - i * Tile) / Tile, (q.Y - j * Tile) / Tile);
                for (int k = 1; k + 1 < piece.Count; k++)
                    Tri(Back(piece[0]), Back(piece[k]), Back(piece[k + 1]), U(piece[0]), U(piece[k]), U(piece[k + 1]), n);
            }
    }

    /// <summary>A convex polygon clipped to a rectangle (Sutherland-Hodgman).</summary>
    static List<Vector2> Clip(List<Vector2> poly, float a0, float a1, float b0, float b1)
    {
        var p = poly;
        p = ClipEdge(p, q => q.X - a0, (s, e) => Cut(s, e, (s.X - a0) / (s.X - e.X)));
        p = ClipEdge(p, q => a1 - q.X, (s, e) => Cut(s, e, (s.X - a1) / (s.X - e.X)));
        p = ClipEdge(p, q => q.Y - b0, (s, e) => Cut(s, e, (s.Y - b0) / (s.Y - e.Y)));
        p = ClipEdge(p, q => b1 - q.Y, (s, e) => Cut(s, e, (s.Y - b1) / (s.Y - e.Y)));
        return p;
    }

    static Vector2 Cut(Vector2 s, Vector2 e, float t) => s + (e - s) * t;

    static List<Vector2> ClipEdge(List<Vector2> poly, Func<Vector2, float> inside, Func<Vector2, Vector2, Vector2> cut)
    {
        var o = new List<Vector2>();
        for (int k = 0; k < poly.Count; k++)
        {
            var s = poly[k];
            var e = poly[(k + 1) % poly.Count];
            bool si = inside(s) >= 0, ei = inside(e) >= 0;
            if (si) o.Add(s);
            if (si != ei) o.Add(cut(s, e));
        }
        return o;
    }

    /// <summary>A quad (four corners in order round it), split into two triangles if it isn't flat.</summary>
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Region region, bool stretch = false)
    {
        var n = Vector3.Cross(b - a, c - a);
        if (n.LengthSquared() > 1e-8f && Math.Abs(Vector3.Dot(Vector3.Normalize(n), d - a)) < 0.05f)
            Poly([a, b, c, d], outward, region, stretch);
        else
        {
            Poly([a, b, c], outward, region, stretch);
            Poly([a, c, d], outward, region, stretch);
        }
    }

    /// <summary>A box between two corners: its sides, its top (if given), never its bottom (it is below ground or unseen).</summary>
    public void Box(Vector3 min, Vector3 max, Region sides, Region? top = null, bool bottom = false)
    {
        var c = (min + max) / 2;
        Vector3 P(int i) => new((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
        Quad(P(0), P(4), P(6), P(2), -Vector3.UnitX, sides);
        Quad(P(1), P(3), P(7), P(5), Vector3.UnitX, sides);
        Quad(P(0), P(2), P(3), P(1), -Vector3.UnitZ, sides);
        Quad(P(4), P(5), P(7), P(6), Vector3.UnitZ, sides);
        Quad(P(2), P(6), P(7), P(3), Vector3.UnitY, top ?? sides);
        if (bottom) Quad(P(0), P(1), P(5), P(4), -Vector3.UnitY, sides);
    }

    /// <summary>A box turned and tilted: centre, its three half-axes (any directions, at right angles).</summary>
    public void Block(Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, Region sides, Region? top = null)
    {
        Vector3 P(int i) => c + ((i & 1) == 0 ? -ax : ax) + ((i & 2) == 0 ? -ay : ay) + ((i & 4) == 0 ? -az : az);
        Quad(P(0), P(4), P(6), P(2), -ax, sides);
        Quad(P(1), P(3), P(7), P(5), ax, sides);
        Quad(P(0), P(2), P(3), P(1), -az, sides);
        Quad(P(4), P(5), P(7), P(6), az, sides);
        Quad(P(2), P(6), P(7), P(3), ay, top ?? sides);
        Quad(P(0), P(1), P(5), P(4), -ay, sides);
    }

    /// <summary>A plate: a box (centre, half-axes) whose outer face (+az) shows <paramref name="face"/>, its other faces <paramref name="edge"/>.</summary>
    public void Plate(Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, Region face, Region edge, bool stretchFace = false)
    {
        Vector3 P(int i) => c + ((i & 1) == 0 ? -ax : ax) + ((i & 2) == 0 ? -ay : ay) + ((i & 4) == 0 ? -az : az);
        Quad(P(0), P(4), P(6), P(2), -ax, edge);
        Quad(P(1), P(3), P(7), P(5), ax, edge);
        Quad(P(0), P(2), P(3), P(1), -az, edge);
        Quad(P(4), P(5), P(7), P(6), az, face, stretchFace);
        Quad(P(2), P(6), P(7), P(3), ay, edge);
        Quad(P(0), P(1), P(5), P(4), -ay, edge);
    }

    /// <summary>A beam from p0 to p1, <paramref name="halfWide"/> to each side and <paramref name="halfHigh"/> across the other way.</summary>
    public void Beam(Vector3 p0, Vector3 p1, float halfWide, float halfHigh, Region region)
    {
        var along = p1 - p0;
        var dir = Vector3.Normalize(along);
        var up = Math.Abs(dir.Y) > 0.9f ? Vector3.UnitX : Vector3.UnitY;
        var side = Vector3.Normalize(Vector3.Cross(dir, up));
        var over = Vector3.Cross(side, dir);
        Block((p0 + p1) / 2, along / 2, over * halfHigh, side * halfWide, region);
    }

    /// <summary>A glowing seam: a thin strip from a to b, <paramref name="width"/> across, lifted off the surface along <paramref name="outward"/>.</summary>
    public void Seam(Vector3 a, Vector3 b, Vector3 outward, float width, Region region = Region.Lamp)
    {
        var n = Vector3.Normalize(outward);
        var across = Vector3.Normalize(Vector3.Cross(n, b - a)) * (width / 2);
        var lift = n * 1.5f;
        Quad(a - across + lift, b - across + lift, b + across + lift, a + across + lift, n, region, stretch: true);
    }

    /// <summary>
    /// A slab: a convex outline (in the plane through <paramref name="origin"/> along <paramref name="u"/> and
    /// <paramref name="v"/>) given <paramref name="depth"/> along their normal: two faces and the edges round them.
    /// </summary>
    public void Slab(Vector3 origin, Vector3 u, Vector3 v, IReadOnlyList<Vector2> outline, float depth, Region face, Region edge, bool stretchFace = false)
    {
        var w = Vector3.Normalize(Vector3.Cross(u, v)) * depth;
        var a = outline.Select(q => origin + u * q.X + v * q.Y).ToList();
        var b = a.Select(p => p + w).ToList();
        var mid = (a.Aggregate(Vector3.Zero, (s, p) => s + p) / a.Count) + w / 2;
        Poly(a, -w, face, stretchFace);
        Poly(b, w, face, stretchFace);
        for (int k = 0; k < a.Count; k++)
        {
            int m = (k + 1) % a.Count;
            var side = (a[k] + a[m]) / 2 + w / 2 - mid;
            Quad(a[k], a[m], b[m], b[k], side, edge);
        }
    }

    /// <summary>
    /// A hull: cross-sections (closed convex outlines in z and y) at stations along x, joined edge to edge. Each edge
    /// of the outline has its own region; the two ends are capped (or left open).
    /// </summary>
    public void Loft(IReadOnlyList<(float X, Vector2[] Ring)> stations, Func<int, Region> edgeRegion, Region? startCap, Region? endCap, bool stretchCaps = false)
    {
        int m = stations[0].Ring.Length;
        Vector3 At(int s, int k) => new(stations[s].X, stations[s].Ring[k].Y, stations[s].Ring[k].X);
        for (int s = 0; s + 1 < stations.Count; s++)
        {
            var axis = new Vector3((stations[s].X + stations[s + 1].X) / 2, 0, 0);
            for (int k = 0; k < m; k++)
            {
                int k2 = (k + 1) % m;
                var a = At(s, k); var b = At(s, k2); var c = At(s + 1, k2); var d = At(s + 1, k);
                var mid = (a + b + c + d) / 4;
                var centreY = (stations[s].Ring.Average(q => q.Y) + stations[s + 1].Ring.Average(q => q.Y)) / 2;
                Quad(a, b, c, d, mid - new Vector3(mid.X, (float)centreY, 0), edgeRegion(k));
            }
        }
        if (startCap is Region sc)
            Poly(Enumerable.Range(0, m).Select(k => At(0, k)).ToList(), -Vector3.UnitX, sc, stretchCaps);
        if (endCap is Region ec)
            Poly(Enumerable.Range(0, m).Select(k => At(stations.Count - 1, k)).ToList(), Vector3.UnitX, ec, stretchCaps);
    }

    /// <summary>A tube or cone from p0 (radius r0) to p1 (r1), its outside in <paramref name="side"/>, capped if asked.</summary>
    public void Tube(Vector3 p0, Vector3 p1, float r0, float r1, Region side, int sides = 12, Region? cap0 = null, Region? cap1 = null, bool inside = false, bool stretch = false)
    {
        var along = Vector3.Normalize(p1 - p0);
        var up = Math.Abs(along.Y) > 0.95f ? Vector3.UnitX : Vector3.UnitY;
        var e1 = Vector3.Normalize(Vector3.Cross(along, up));
        var e2 = Vector3.Cross(e1, along);
        Vector3 Ring(Vector3 c, float r, int k) => c + (e1 * MathF.Cos(2 * MathF.PI * k / sides) + e2 * MathF.Sin(2 * MathF.PI * k / sides)) * r;
        for (int k = 0; k < sides; k++)
        {
            Vector3 a0 = Ring(p0, r0, k), a1 = Ring(p0, r0, k + 1), b0 = Ring(p1, r1, k), b1 = Ring(p1, r1, k + 1);
            var outward = (a0 + a1 + b0 + b1) / 4 - (p0 + p1) / 2 - along * Vector3.Dot((a0 + a1 + b0 + b1) / 4 - (p0 + p1) / 2, along);
            Quad(a0, a1, b1, b0, inside ? -outward : outward, side, stretch);
        }
        if (cap0 is Region c0)
            Poly(Enumerable.Range(0, sides).Select(k => Ring(p0, r0, k)).ToList(), -along, c0, stretch: true);
        if (cap1 is Region c1)
            Poly(Enumerable.Range(0, sides).Select(k => Ring(p1, r1, k)).ToList(), along, c1, stretch: true);
    }

    /// <summary>
    /// A thin strip along a line (vines, cables, reed blades), <paramref name="width"/> across at its start and
    /// narrowing to <paramref name="tip"/> times that, seen from both sides; the cell runs along it.
    /// </summary>
    public void Ribbon(IReadOnlyList<Vector3> line, Vector3 across, float width, float tip, Region region)
    {
        Plant++;
        try { RibbonSolidless(line, across, width, tip, region); }
        finally { Plant--; }
    }

    void RibbonSolidless(IReadOnlyList<Vector3> line, Vector3 across, float width, float tip, Region region)
    {
        var x = Vector3.Normalize(across);
        float total = 0;
        var run = new float[line.Count];
        for (int i = 1; i < line.Count; i++) run[i] = total += (line[i] - line[i - 1]).Length();
        for (int i = 0; i + 1 < line.Count; i++)
        {
            float w0 = width * (1 + (tip - 1) * run[i] / total) / 2, w1 = width * (1 + (tip - 1) * run[i + 1] / total) / 2;
            Vector3 a = line[i] - x * w0, b = line[i] + x * w0, c = line[i + 1] + x * w1, d = line[i + 1] - x * w1;
            float t0 = run[i] / total, t1 = run[i + 1] / total;
            var n = Vector3.Cross(c - a, b - a);
            if (n.LengthSquared() < 1e-8f) continue;
            var r = region;
            Vector2 ua = Uv(r, 0, t0), ub = Uv(r, 1, t0), uc = Uv(r, 1, t1), ud = Uv(r, 0, t1);
            foreach (var side in new[] { n, -n })
            {
                Tri(a, b, c, ua, ub, uc, side);
                Tri(a, c, d, ua, uc, ud, side);
            }
        }
    }

    /// <summary>
    /// A lumpy ellipsoid (an icosphere, subdivided, each point pushed out or in by up to <paramref name="lumps"/> of its
    /// radius): boulders, leaf clumps, bushes. Faces wholly below <paramref name="cutY"/> are left off (in the ground).
    /// </summary>
    public void Blob(Vector3 c, Vector3 radii, Region region, int seed, float lumps = 0.2f, int subdivide = 2, bool stretch = false, float cutY = float.MinValue)
    {
        float g = (1 + MathF.Sqrt(5)) / 2;
        var pts = new List<Vector3>
        {
            new(-1, g, 0), new(1, g, 0), new(-1, -g, 0), new(1, -g, 0), new(0, -1, g), new(0, 1, g),
            new(0, -1, -g), new(0, 1, -g), new(g, 0, -1), new(g, 0, 1), new(-g, 0, -1), new(-g, 0, 1),
        };
        var faces = new List<(int, int, int)>
        {
            (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
            (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
        };
        for (int i = 0; i < pts.Count; i++) pts[i] = Vector3.Normalize(pts[i]);
        for (int d = 0; d < subdivide; d++)
        {
            var mid = new Dictionary<(int, int), int>();
            int Mid(int a, int b)
            {
                var k = a < b ? (a, b) : (b, a);
                if (!mid.TryGetValue(k, out int m)) { pts.Add(Vector3.Normalize(pts[a] + pts[b])); m = pts.Count - 1; mid[k] = m; }
                return m;
            }
            var next = new List<(int, int, int)>();
            foreach (var (a, b, cc) in faces)
            {
                int ab = Mid(a, b), bc = Mid(b, cc), ca = Mid(cc, a);
                next.AddRange([(a, ab, ca), (b, bc, ab), (cc, ca, bc), (ab, bc, ca)]);
            }
            faces = next;
        }
        var rnd = new Random(seed);
        var push = pts.Select(_ => 1 + lumps * (float)(rnd.NextDouble() * 2 - 1)).ToArray();
        var world = pts.Select((p, i) => c + p * radii * push[i]).ToArray();
        foreach (var (a, b, cc) in faces)
        {
            if (Math.Max(world[a].Y, Math.Max(world[b].Y, world[cc].Y)) < cutY) continue;
            var outward = (world[a] + world[b] + world[cc]) / 3 - c;
            Poly([world[a], world[b], world[cc]], outward, region, stretch);
        }
    }

    /// <summary>Another mesh moved by <paramref name="m"/>, added to this one.</summary>
    public void Add(Mesh other, Matrix4x4 m)
    {
        if (other.SolidMax.X >= other.SolidMin.X)
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? other.SolidMin.X : other.SolidMax.X, (i & 2) == 0 ? other.SolidMin.Y : other.SolidMax.Y, (i & 4) == 0 ? other.SolidMin.Z : other.SolidMax.Z);
                var p = Vector3.Transform(corner, m);
                SolidMin = Vector3.Min(SolidMin, p); SolidMax = Vector3.Max(SolidMax, p);
            }
        int first = V.Count;
        foreach (var v in other.V)
            V.Add(new(Vector3.Transform(v.Position, m), Vector3.Normalize(Vector3.TransformNormal(v.Normal, m)), v.Uv));
        T.AddRange(other.T.Select(i => i + first));
    }

    /// <summary>This mesh's mirror image across z = 0 (its triangles turned the other way, so they still face out).</summary>
    public Mesh Mirrored()
    {
        var m = new Mesh { SolidMin = SolidMin with { Z = -SolidMax.Z }, SolidMax = SolidMax with { Z = -SolidMin.Z } };
        foreach (var v in V)
            m.V.Add(new(v.Position with { Z = -v.Position.Z }, v.Normal with { Z = -v.Normal.Z }, v.Uv));
        for (int i = 0; i < T.Count; i += 3)
            m.T.AddRange([T[i], T[i + 2], T[i + 1]]);
        return m;
    }

    public (Vector3 Min, Vector3 Max) Solid(float above = float.MinValue) =>
        SolidMax.X >= SolidMin.X ? (SolidMin with { Y = Math.Max(SolidMin.Y, above) }, SolidMax) : Bounds(above);

    public (Vector3 Min, Vector3 Max) Bounds(float above = float.MinValue)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in V)
        {
            var p = v.Position;
            if (p.Y < above) p = p with { Y = above };
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return (min, max);
    }

    public byte[] ToS3o(string piece, string tex1, string tex2) => S3oModel.Write(tex1, tex2, piece, V, T);
}
