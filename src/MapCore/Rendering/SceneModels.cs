using System.Globalization;
using System.Numerics;
using MapCore.Assets;
using MapCore.Editing;
using MapCore.Imaging;
using MapCore.Items;

namespace MapCore.Rendering;

/// <summary>A placed feature's model in the 3D view: its triangles in model space (y up, front +z), and how to colour it.</summary>
sealed class SceneModel
{
    public required Vector3[] Positions;
    public required Vector3[] Normals;
    public required Vector2[] Uvs;
    public required int[] Triangles;

    /// <summary>The middle of its bounding sphere (model space) and the sphere's radius.</summary>
    public required Vector3 Centre;
    public required float Radius;

    public RgbaImage? Colour, Extra;
    public bool CutOut;

    /// <summary>Each triangle's colour (0-1) when there is no texture: the engine's trees, drawn as simple shapes.</summary>
    public Vector3[]? FaceColours;

    /// <summary>Its colour seen from far away (0-1): far features are drawn as single dots of it.</summary>
    public Vector3 Average;

    public static SceneModel From(ModelPreview preview)
    {
        var mesh = preview.Mesh;
        var used = mesh.Triangles.Distinct().Select(i => mesh.Vertices[i].Position).ToList();
        var min = used.Count > 0 ? used.Aggregate(Vector3.Min) : Vector3.Zero;
        var max = used.Count > 0 ? used.Aggregate(Vector3.Max) : Vector3.Zero;
        var centre = (min + max) / 2;
        return new SceneModel
        {
            Positions = [.. mesh.Vertices.Select(v => v.Position)],
            Normals = [.. mesh.Vertices.Select(v => v.Normal)],
            Uvs = [.. mesh.Vertices.Select(v => v.Uv)],
            Triangles = [.. mesh.Triangles],
            Centre = centre,
            Radius = used.Count > 0 ? used.Max(p => (p - centre).Length()) : 0,
            Colour = preview.Colour,
            Extra = preview.Extra,
            CutOut = preview.CutOut,
            Average = AverageColour(preview),
        };
    }

    /// <summary>The mean colour of texture 1 where texture 2 doesn't cut it out (grey without a texture).</summary>
    static Vector3 AverageColour(ModelPreview preview)
    {
        if (preview.Colour is not RgbaImage colour)
            return new Vector3(0.6f);
        var sum = Vector3.Zero;
        int n = 0;
        for (int j = 0; j < 32; j++)
            for (int i = 0; i < 32; i++)
            {
                var uv = new Vector2((i + 0.5f) / 32, (j + 0.5f) / 32);
                if (preview.CutOut && preview.Extra != null && ModelRenderer.Sample(preview.Extra, uv).W < 0.5f)
                    continue;
                var t = ModelRenderer.Sample(colour, uv);
                sum += new Vector3(t.X, t.Y, t.Z);
                n++;
            }
        return n > 0 ? sum / n : new Vector3(0.6f);
    }

    /// <summary>
    /// The engine's default trees (treetype0-16) have no model file in a map: a simple trunk and cone of leaves,
    /// about their size, so a forest of them still shows.
    /// </summary>
    public static SceneModel EngineTree { get; } = MakeTree();

    static SceneModel MakeTree()
    {
        const int Sides = 8;
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var triangles = new List<int>();
        var colours = new List<Vector3>();
        var bark = new Vector3(0.36f, 0.26f, 0.17f);
        var leaves = new Vector3(0.20f, 0.40f, 0.17f);
        void Cone(float baseY, float topY, float baseRadius, float topRadius, Vector3 colour)
        {
            for (int s = 0; s < Sides; s++)
            {
                double a0 = 2 * Math.PI * s / Sides, a1 = 2 * Math.PI * (s + 1) / Sides;
                var b0 = new Vector3((float)Math.Sin(a0) * baseRadius, baseY, (float)Math.Cos(a0) * baseRadius);
                var b1 = new Vector3((float)Math.Sin(a1) * baseRadius, baseY, (float)Math.Cos(a1) * baseRadius);
                var t0 = new Vector3((float)Math.Sin(a0) * topRadius, topY, (float)Math.Cos(a0) * topRadius);
                var t1 = new Vector3((float)Math.Sin(a1) * topRadius, topY, (float)Math.Cos(a1) * topRadius);
                foreach (var (p, q, r) in new[] { (b0, b1, t1), (b0, t1, t0) })
                {
                    var normal = Vector3.Normalize(Vector3.Cross(q - p, r - p));
                    if (float.IsNaN(normal.X))
                        continue;
                    triangles.AddRange([positions.Count, positions.Count + 1, positions.Count + 2]);
                    positions.AddRange([p, q, r]);
                    normals.AddRange([normal, normal, normal]);
                    colours.Add(colour);
                }
            }
        }
        Cone(0, 20, 3, 2.5f, bark);
        Cone(14, 80, 22, 0.01f, leaves);
        return new SceneModel
        {
            Positions = [.. positions],
            Normals = [.. normals],
            Uvs = new Vector2[positions.Count],
            Triangles = [.. triangles],
            Centre = new Vector3(0, 40, 0),
            Radius = 45,
            FaceColours = [.. colours],
            Average = leaves,
        };
    }
}

/// <summary>
/// The models of the features placed on a map, by name, for the 3D view: each read once (textures shared between
/// models that use the same file), from the map's own definitions. The definitions are run again only when the
/// map's features/, objects3d/ or unittextures/ change (an asset added or undone). Features the map doesn't define
/// are drawn with BAR's own models when they are shipped (<paramref name="bar"/>: the engine's trees as BAR's firs,
/// BAR's rocks, wrecks); otherwise they have none, and the engine's trees get a simple stand-in. Units and buildings
/// the map's featureplacer places are drawn with BAR's models too (<see cref="ForUnit"/>).
/// </summary>
sealed class SceneModels(MapEditSession session, BarFeatureCatalog? bar = null)
{
    static readonly string[] Watched = ["features", "objects3d", "unittextures"];
    readonly Dictionary<string, SceneModel?> loaded = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, SceneModel?> units = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, RgbaImage> textures = [];
    Dictionary<string, string?> modelOf = new(StringComparer.OrdinalIgnoreCase);
    Func<string, string?> resolve = _ => null;
    string? fingerprint;
    bool check = true;

    /// <summary>Notes that the map's files may have changed (an items change): looked at again before the next use.</summary>
    public void FilesMayHaveChanged() => check = true;

    /// <summary>How many models were read (for tests).</summary>
    public int Reads { get; private set; }

    public SceneModel? For(string name)
    {
        if (check)
            Refresh();
        if (loaded.TryGetValue(name, out var model))
            return model;
        model = null;
        if (modelOf.TryGetValue(name, out var path) && path != null && path.EndsWith(".s3o", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                model = SceneModel.From(ModelPreview.Load(session.Package.Root, path, resolve, textures));
                Reads++;
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                model = null; // drawn as a marker only
            }
        }
        else if (!modelOf.ContainsKey(name) && bar?.ModelOf(name) is string barModel)
        {
            try
            {
                model = SceneModel.From(ModelPreview.Load(bar.Folder, barModel, bar.Resolve, textures));
                Reads++;
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                model = null;
            }
        }
        if (model == null && !modelOf.ContainsKey(name) && name.Contains("treetype", StringComparison.OrdinalIgnoreCase))
            model = SceneModel.EngineTree;
        loaded[name] = model;
        return model;
    }

    /// <summary>A unit's or building's model (a map's featureplacer places them): BAR's, when shipped; else none (a marker only).</summary>
    public SceneModel? ForUnit(string name)
    {
        if (units.TryGetValue(name, out var model))
            return model;
        model = null;
        if (bar?.UnitModelOf(name) is string path)
        {
            try
            {
                model = SceneModel.From(ModelPreview.Load(bar.Folder, path, bar.Resolve, textures));
                Reads++;
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException or UnauthorizedAccessException)
            {
                model = null;
            }
        }
        units[name] = model;
        return model;
    }

    void Refresh()
    {
        check = false;
        var root = session.Package.Root;
        var now = string.Join("|", Watched.Select(d => Path.Combine(root, d)).Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*", FileSystemUtil.AllEntries))
            .Select(f => new FileInfo(f)).OrderBy(f => f.FullName, StringComparer.Ordinal)
            .Select(f => string.Create(CultureInfo.InvariantCulture, $"{f.FullName}:{f.Length}:{f.LastWriteTimeUtc.Ticks}")));
        if (now == fingerprint)
            return;
        fingerprint = now;
        resolve = ModelPaths.Resolver(root);
        modelOf = new(StringComparer.OrdinalIgnoreCase);
        foreach (var file in FeatureDefs.Load(root))
            foreach (var def in file.Features)
                modelOf.TryAdd(def.Name, def.Object == null ? null : ModelPaths.Find(def.Object, resolve));
        loaded.Clear();
        textures.Clear();
    }
}
