using MapCore.Imaging;
using MapCore.Smf;

namespace MapCore.Heights;

/// <summary>
/// A circular brush in game units. Weight is 1 within <c>Radius × Hardness</c> and eases to 0 at
/// <see cref="Radius"/> along a cosine curve.
/// </summary>
public readonly record struct Brush(double X, double Z, double Radius, double Hardness = 0.5)
{
    public double Weight(double x, double z)
    {
        double d = Math.Sqrt((x - X) * (x - X) + (z - Z) * (z - Z));
        double inner = Radius * Math.Clamp(Hardness, 0, 1);
        if (d >= Radius)
            return 0;
        if (d <= inner)
            return 1;
        return 0.5 * (1 + Math.Cos(Math.PI * (d - inner) / (Radius - inner)));
    }
}

/// <summary>What <see cref="HeightMap.Write"/> stored.</summary>
public sealed record HeightWriteResult(int VerticesWritten, bool RangeChanged, float MinHeight, float MaxHeight);

/// <summary>
/// The terrain heights in game units, edited as floats and stored back as the SMF's 16-bit values.
/// Decoding matches the engine exactly (SMFReadMap / SMFMapFile::ReadHeightmap):
/// <c>height = min + raw × (max − min) / 65536</c>, where mapinfo's smf.minheight and smf.maxheight each replace the
/// SMF header's value when present. Vertices are 8 game units apart; vertex (i, j) is at world (8i, 8j).
/// </summary>
public sealed class HeightMap
{
    public const double Spacing = SmfFile.SquareSize;

    readonly SmfFile smf;
    readonly MapInfo? info;
    float[] original;
    bool[] changed;

    HeightMap(SmfFile smf, MapInfo? info, float min, float max, float[] heights)
    {
        this.smf = smf;
        this.info = info;
        MinHeight = min;
        MaxHeight = max;
        Heights = heights;
        original = (float[])heights.Clone();
        changed = new bool[heights.Length];
    }

    public static HeightMap Load(SmfFile smf, MapInfo? info)
    {
        float min = (float)(info?.MinHeight ?? smf.MinHeight), max = (float)(info?.MaxHeight ?? smf.MaxHeight);
        float mod = (max - min) / 65536.0f;
        var heights = new float[smf.Heightmap.Length];
        for (int i = 0; i < heights.Length; i++)
            heights[i] = min + smf.Heightmap[i] * mod;
        return new HeightMap(smf, info, min, max, heights);
    }

    public int Width => smf.HeightmapWidth;
    public int Height => smf.HeightmapHeight;

    /// <summary>Current heights (game units), row by row.</summary>
    public float[] Heights { get; }

    /// <summary>Heights as loaded (or as last accepted), used to see how the terrain changed.</summary>
    public IReadOnlyList<float> Original => original;

    public float MinHeight { get; private set; }
    public float MaxHeight { get; private set; }

    /// <summary>The height difference between neighbouring raw values.</summary>
    public double Step => (MaxHeight - MinHeight) / 65536.0;

    public bool HasChanges => ChangedRegion is { IsEmpty: false };

    /// <summary>Bounding box (vertex coordinates) of the vertices edited since loading.</summary>
    public PixelRect ChangedRegion { get; private set; }

    public float this[int x, int z] => Heights[Math.Clamp(z, 0, Height - 1) * Width + Math.Clamp(x, 0, Width - 1)];

    /// <summary>Bilinear height at a world position, as the engine interpolates between vertices.</summary>
    public float HeightAt(double worldX, double worldZ) => Sample(Heights, worldX, worldZ);

    public float OriginalHeightAt(double worldX, double worldZ) => Sample(original, worldX, worldZ);

    // ---------------------------------------------------------------- edits

    public PixelRect Raise(Brush brush, double amount) => Apply(brush, (h, w, _, _) => h + amount * w);

    /// <summary>
    /// Pulls heights towards <paramref name="height"/>; at full <paramref name="strength"/> (0-1), fully flat inside
    /// the brush's hard core.
    /// </summary>
    public PixelRect Flatten(Brush brush, double height, double strength = 1) =>
        Apply(brush, (h, w, _, _) => h + (height - h) * w * Math.Clamp(strength, 0, 1));

    /// <summary>Blends each vertex towards the average of its 3x3 neighbourhood.</summary>
    public PixelRect Smooth(Brush brush, double strength = 1)
    {
        var before = (float[])Heights.Clone();
        return Apply(brush, (h, w, x, z) =>
        {
            double sum = 0;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    sum += before[Math.Clamp(z + dz, 0, Height - 1) * Width + Math.Clamp(x + dx, 0, Width - 1)];
            return h + (sum / 9 - h) * w * Math.Clamp(strength, 0, 1);
        });
    }

    /// <summary>
    /// Adds smooth noise of up to ±<paramref name="amplitude"/> with bumps about <paramref name="featureSize"/> game
    /// units across, weighted by the brush. The pattern is fixed to the map, so repeated dabs deepen it. With
    /// <paramref name="mirrored"/>, the pattern is read at each point's mirror image: a dab that is another's mirror
    /// image then makes the mirror image of its bumps.
    /// </summary>
    public PixelRect Noise(Brush brush, double amplitude, double featureSize = 64, MapMirror? mirrored = null) =>
        Apply(brush, (h, w, x, z) =>
        {
            var (nx, nz) = mirrored is MapMirror m ? m.Map(x * Spacing, z * Spacing) : (x * Spacing, z * Spacing);
            return h + amplitude * w * TerrainNoise.Sample(nx / featureSize, nz / featureSize);
        });

    /// <summary>
    /// Builds a ramp: heights follow its centre line in a straight line from <paramref name="h1"/> at the first end
    /// to <paramref name="h2"/> at the second, and blend into the ground over its edge (roughened on the sides by
    /// <paramref name="roughness"/>, see <see cref="Ramp.Shaped"/>).
    /// </summary>
    public PixelRect ApplyRamp(Ramp ramp, double h1, double h2, double roughness = 0) =>
        Apply(VertexBounds(ramp), ramp.Weight, (h, _, x, z) => ramp.Shaped(h, x * Spacing, z * Spacing, h1, h2, roughness));

    /// <summary>Replaces every height (e.g. from an imported image); only vertices that differ count as edited.</summary>
    /// <returns>The number of vertices that changed.</returns>
    public int SetAll(float[] values)
    {
        if (values.Length != Heights.Length)
            throw new ArgumentException($"Expected {Heights.Length} heights, got {values.Length}.");
        int count = 0, left = Width, top = Height, right = 0, bottom = 0;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] == Heights[i])
                continue;
            Heights[i] = values[i];
            changed[i] = true;
            count++;
            int x = i % Width, z = i / Width;
            (left, top, right, bottom) = (Math.Min(left, x), Math.Min(top, z), Math.Max(right, x + 1), Math.Max(bottom, z + 1));
        }
        if (count > 0)
        {
            var touched = PixelRect.FromEdges(left, top, right, bottom);
            ChangedRegion = ChangedRegion.IsEmpty ? touched : ChangedRegion.Union(touched);
        }
        return count;
    }

    /// <summary>
    /// The current heights as 16-bit values, over the range <see cref="Write"/> would use — widened if edits left
    /// the current one — so nothing is clipped. Changes no state. Unedited vertices keep the SMF's own values
    /// when the range stays the same.
    /// </summary>
    public (ushort[] Values, float MinHeight, float MaxHeight) Quantize()
    {
        var (min, max, widen) = StorageRange();
        double scale = 65536.0 / (max - min);
        var result = new ushort[Heights.Length];
        for (int i = 0; i < Heights.Length; i++)
            result[i] = changed[i] || widen ? (ushort)Math.Clamp(Math.Round((Heights[i] - min) * scale), 0, 65535) : smf.Heightmap[i];
        return (result, min, max);
    }

    /// <summary>The range heights would be stored over: the current one, or widened to whole numbers covering every height.</summary>
    (float Min, float Max, bool Widen) StorageRange()
    {
        if (!HasChanges)
            return (MinHeight, MaxHeight, false);
        float lowest = Heights.Min(), highest = Heights.Max();
        if (lowest >= MinHeight && highest <= MaxHeight - Step)
            return (MinHeight, MaxHeight, false);
        return (MathF.Min(MinHeight, MathF.Floor(lowest)), MathF.Max(MaxHeight, MathF.Ceiling(highest) + 1), true);
    }

    /// <summary>Adds <paramref name="amount"/> to every height.</summary>
    public void Offset(double amount) => ApplyAll(h => h + amount);

    /// <summary>Multiplies every height by <paramref name="factor"/> (sea level, height 0, stays put).</summary>
    public void Scale(double factor) => ApplyAll(h => h * factor);

    PixelRect Apply(Brush brush, Func<float, double, int, int, double> change) => Apply(VertexBounds(brush), brush.Weight, change);

    /// <summary>
    /// Changes the vertices in <paramref name="vertices"/> whose weight (from their world position) is above 0, keeping
    /// the edit marks and <see cref="ChangedRegion"/> that saving relies on. Returns the vertices that changed.
    /// </summary>
    PixelRect Apply(PixelRect vertices, Func<double, double, double> weight, Func<float, double, int, int, double> change)
    {
        vertices = vertices.Intersect(AllVertices);
        var touched = PixelRect.FromEdges(Width, Height, 0, 0);
        for (int z = vertices.Y; z < vertices.Bottom; z++)
            for (int x = vertices.X; x < vertices.Right; x++)
            {
                double w = weight(x * Spacing, z * Spacing);
                if (w <= 0)
                    continue;
                int i = z * Width + x;
                float updated = (float)change(Heights[i], w, x, z);
                if (updated == Heights[i])
                    continue;
                Heights[i] = updated;
                changed[i] = true;
                touched = PixelRect.FromEdges(Math.Min(touched.X, x), Math.Min(touched.Y, z),
                    Math.Max(touched.Right, x + 1), Math.Max(touched.Bottom, z + 1));
            }
        if (touched.IsEmpty)
            return new PixelRect(0, 0, 0, 0);
        ChangedRegion = ChangedRegion.IsEmpty ? touched : ChangedRegion.Union(touched);
        return touched;
    }

    void ApplyAll(Func<float, double> change)
    {
        for (int i = 0; i < Heights.Length; i++)
        {
            Heights[i] = (float)change(Heights[i]);
            changed[i] = true;
        }
        ChangedRegion = new PixelRect(0, 0, Width, Height);
    }

    // ---------------------------------------------------------------- undo support

    /// <summary>A copy of the heights (and edit marks) in a vertex region, for undo.</summary>
    public sealed class State
    {
        internal PixelRect Region { get; init; }
        internal float[] Heights { get; init; } = [];
    }

    /// <summary>The vertices a brush can touch.</summary>
    public PixelRect VertexBounds(Brush brush) => PixelRect.FromEdges(
        Math.Max(0, (int)Math.Floor((brush.X - brush.Radius) / Spacing)),
        Math.Max(0, (int)Math.Floor((brush.Z - brush.Radius) / Spacing)),
        Math.Min(Width, (int)Math.Ceiling((brush.X + brush.Radius) / Spacing) + 1),
        Math.Min(Height, (int)Math.Ceiling((brush.Z + brush.Radius) / Spacing) + 1));

    public PixelRect AllVertices => new(0, 0, Width, Height);

    /// <summary>The vertices a ramp can touch (its rectangle and edge).</summary>
    public PixelRect VertexBounds(Ramp ramp)
    {
        var (minX, minZ, maxX, maxZ) = ramp.Bounds;
        return PixelRect.FromEdges(
            Math.Max(0, (int)Math.Floor(minX / Spacing)), Math.Max(0, (int)Math.Floor(minZ / Spacing)),
            Math.Min(Width, (int)Math.Ceiling(maxX / Spacing) + 1), Math.Min(Height, (int)Math.Ceiling(maxZ / Spacing) + 1));
    }

    public State Capture(PixelRect region)
    {
        region = region.Intersect(AllVertices);
        var heights = new float[region.Width * region.Height];
        for (int z = 0; z < region.Height; z++)
            Array.Copy(Heights, (region.Y + z) * Width + region.X, heights, z * region.Width, region.Width);
        return new State { Region = region, Heights = heights };
    }

    /// <summary>
    /// Puts a captured region back. Edit marks are recomputed against the current baseline, so undoing back to it
    /// leaves nothing to store (and no normal-map rotation), even across a save.
    /// </summary>
    public void Restore(State state)
    {
        var r = state.Region;
        for (int z = 0; z < r.Height; z++)
            Array.Copy(state.Heights, z * r.Width, Heights, (r.Y + z) * Width + r.X, r.Width);
        for (int z = r.Y; z < r.Bottom; z++)
            for (int x = r.X; x < r.Right; x++)
            {
                int i = z * Width + x;
                changed[i] = Heights[i] != original[i];
            }
        int left = Width, top = Height, right = 0, bottom = 0;
        for (int i = 0; i < changed.Length; i++)
            if (changed[i])
                (left, top, right, bottom) = (Math.Min(left, i % Width), Math.Min(top, i / Width), Math.Max(right, i % Width + 1), Math.Max(bottom, i / Width + 1));
        ChangedRegion = right > left ? PixelRect.FromEdges(left, top, right, bottom) : new PixelRect(0, 0, 0, 0);
    }

    // ---------------------------------------------------------------- storing

    /// <summary>
    /// Stores the heights in the SMF. With the range unchanged, only edited vertices are re-quantised, so every
    /// other vertex keeps its exact raw value. If edits left the range, it widens to whole numbers that cover them,
    /// every vertex is re-quantised (each stays within one step of its height), and the new range goes to the SMF
    /// header and — where it already overrides the header — to mapinfo's smf.minheight / smf.maxheight.
    /// </summary>
    public HeightWriteResult Write()
    {
        if (!HasChanges)
            return new HeightWriteResult(0, false, MinHeight, MaxHeight);

        var (min, max, widen) = StorageRange();
        if (widen)
        {
            MinHeight = min;
            MaxHeight = max;
            smf.MinHeight = min;
            smf.MaxHeight = max;
            if (info?.MinHeight != null)
                info.SetNumber("smf.minheight", min);
            if (info?.MaxHeight != null)
                info.SetNumber("smf.maxheight", max);
        }

        double scale = 65536.0 / (MaxHeight - MinHeight);
        int written = 0;
        for (int i = 0; i < Heights.Length; i++)
        {
            if (!widen && !changed[i])
                continue;
            smf.Heightmap[i] = (ushort)Math.Clamp(Math.Round((Heights[i] - MinHeight) * scale), 0, 65535);
            written++;
        }
        return new HeightWriteResult(written, widen, MinHeight, MaxHeight);
    }

    /// <summary>Makes the current heights the new baseline (after everything that depends on the change was saved).</summary>
    public void AcceptChanges()
    {
        original = (float[])Heights.Clone();
        changed = new bool[Heights.Length];
        ChangedRegion = new PixelRect(0, 0, 0, 0);
    }

    // ---------------------------------------------------------------- normals

    /// <summary>
    /// The terrain normal (x east, y up, z south) at a world position: per-vertex normals from central
    /// differences, interpolated bilinearly like the engine's normal texture.
    /// </summary>
    public (double X, double Y, double Z) NormalAt(double worldX, double worldZ, bool original = false)
    {
        var source = original ? this.original : Heights;
        double u = worldX / Spacing, v = worldZ / Spacing;
        int x = (int)Math.Floor(u), z = (int)Math.Floor(v);
        double fx = u - x, fz = v - z;
        var top = Lerp(VertexNormal(source, x, z), VertexNormal(source, x + 1, z), fx);
        var bottom = Lerp(VertexNormal(source, x, z + 1), VertexNormal(source, x + 1, z + 1), fx);
        return Normalize(Lerp(top, bottom, fz));
    }

    (double X, double Y, double Z) VertexNormal(float[] source, int x, int z)
    {
        x = Math.Clamp(x, 0, Width - 1);
        z = Math.Clamp(z, 0, Height - 1);
        int x0 = Math.Max(x - 1, 0), x1 = Math.Min(x + 1, Width - 1), z0 = Math.Max(z - 1, 0), z1 = Math.Min(z + 1, Height - 1);
        double dx = (source[z * Width + x1] - source[z * Width + x0]) / ((x1 - x0) * Spacing);
        double dz = (source[z1 * Width + x] - source[z0 * Width + x]) / ((z1 - z0) * Spacing);
        return Normalize((-dx, 1, -dz));
    }

    float Sample(float[] source, double worldX, double worldZ)
    {
        double u = Math.Clamp(worldX / Spacing, 0, Width - 1), v = Math.Clamp(worldZ / Spacing, 0, Height - 1);
        int x = Math.Min((int)u, Width - 2), z = Math.Min((int)v, Height - 2);
        float fx = (float)(u - x), fz = (float)(v - z);
        float top = source[z * Width + x] * (1 - fx) + source[z * Width + x + 1] * fx;
        float bottom = source[(z + 1) * Width + x] * (1 - fx) + source[(z + 1) * Width + x + 1] * fx;
        return top * (1 - fz) + bottom * fz;
    }

    static (double X, double Y, double Z) Lerp((double X, double Y, double Z) a, (double X, double Y, double Z) b, double t) =>
        (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    static (double X, double Y, double Z) Normalize((double X, double Y, double Z) v)
    {
        double length = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return length == 0 ? (0, 1, 0) : (v.X / length, v.Y / length, v.Z / length);
    }
}
