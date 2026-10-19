using MapCore.Imaging;
using MapCore.Items;
using MapCore.Textures;

namespace MapCore.Assets;

/// <summary>
/// A model ready to draw for a preview: its triangles and textures, read from a folder laid out like a map package
/// (objects3d/, unittextures/): an open map, or an asset's folder in the library.
/// </summary>
public sealed class ModelPreview
{
    /// <summary>Textures are read at most this wide or tall (a smaller mip level of a DDS): plenty for a preview.</summary>
    public const int MaxTextureSize = 1024;

    public required string ModelPath { get; init; }
    public required ModelMesh Mesh { get; init; }

    /// <summary>Texture 1: the colours (its alpha marks where team colour shows, not transparency).</summary>
    public RgbaImage? Colour { get; init; }

    /// <summary>Texture 2: red self-illumination, green reflectivity, alpha transparency.</summary>
    public RgbaImage? Extra { get; init; }

    /// <summary>Whether parts of the model are cut out by texture 2's alpha (leaves, grass, fences).</summary>
    public bool CutOut { get; init; }

    /// <summary>What couldn't be read (a texture the folder lacks, say): the preview is drawn without it.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>
    /// Reads the model at <paramref name="modelPath"/> (a package path, as <see cref="ModelPaths.Find"/> gives) and
    /// the textures it names. Throws <see cref="NotSupportedException"/> for a kind of model the preview doesn't
    /// draw, and <see cref="InvalidDataException"/> for a model that can't be read. <paramref name="textures"/>, when
    /// given, keeps textures read (by file) for the next model: a set of trees shares one. <paramref name="maxTextureSize"/>
    /// scales larger textures down (a thumbnail needs far less than a preview, and keeps them all for a whole run).
    /// </summary>
    public static ModelPreview Load(string root, string modelPath, Func<string, string?>? resolve = null, Dictionary<string, RgbaImage>? textures = null,
        int maxTextureSize = MaxTextureSize)
    {
        if (!modelPath.EndsWith(".s3o", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException($"{Path.GetFileName(modelPath)} is a {Path.GetExtension(modelPath).TrimStart('.').ToUpperInvariant()} model: the preview draws S3O models only.");
        resolve ??= ModelPaths.Resolver(root);
        var mesh = S3oModel.Load(File.ReadAllBytes(Full(root, modelPath))) ??
                   throw new InvalidDataException($"{Path.GetFileName(modelPath)} isn't an S3O model.");
        var notes = new List<string>();
        var colour = Texture(mesh.Texture1);
        var extra = Texture(mesh.Texture2);
        return new ModelPreview
        {
            ModelPath = modelPath, Mesh = mesh, Colour = colour, Extra = extra, CutOut = extra != null && HasCutOut(extra), Notes = notes,
        };

        RgbaImage? Texture(string? name)
        {
            if (name == null)
                return null;
            var entry = TexturePath(name, resolve);
            if (entry == null)
            {
                notes.Add($"texture {name} isn't here (BAR may have it): drawn without it");
                return null;
            }
            try
            {
                var file = Full(root, entry);
                if (textures?.TryGetValue(file, out var known) == true)
                    return known;
                var image = ReadTexture(file);
                if (Math.Max(image.Width, image.Height) > maxTextureSize)
                {
                    double scale = (double)maxTextureSize / Math.Max(image.Width, image.Height);
                    image = image.Resample(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
                }
                if (textures != null)
                    textures[file] = image;
                return image;
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException or ArgumentException)
            {
                notes.Add($"texture {name} couldn't be read ({ex.Message}): drawn without it");
                return null;
            }
        }
    }

    /// <summary>
    /// The model of a feature a folder defines (in its features/**/*.lua), or null when it doesn't define the feature
    /// or the definition names no model. Throws <see cref="FileNotFoundException"/> when the model isn't there.
    /// </summary>
    public static ModelPreview? ForFeature(string root, string feature)
    {
        var def = FeatureDefs.Load(root).SelectMany(f => f.Features).FirstOrDefault(d => d.Name.Equals(feature, StringComparison.OrdinalIgnoreCase));
        if (def?.Object == null)
            return null;
        var resolve = ModelPaths.Resolver(root);
        var model = ModelPaths.Find(def.Object, resolve) ?? throw new FileNotFoundException($"{feature}'s model {def.Object} isn't here (BAR may have it).");
        return Load(root, model, resolve);
    }

    /// <summary>
    /// Several models as one, each moved by its offset (a door or tunnel's walls and centre, say), with what is below
    /// the ground raised to it so the whole stands on the ground as it does in game. Null unless they all use the same
    /// textures, as models built from one atlas do.
    /// </summary>
    public static ModelPreview? Combine(string name, IReadOnlyList<(ModelPreview Model, System.Numerics.Vector3 Offset)> parts)
    {
        if (parts.Count == 0 || parts.Any(p => p.Model.Mesh.Texture1 != parts[0].Model.Mesh.Texture1 || p.Model.Mesh.Texture2 != parts[0].Model.Mesh.Texture2))
            return null;
        var vertices = new List<ModelVertex>();
        var triangles = new List<int>();
        foreach (var (model, offset) in parts)
        {
            int first = vertices.Count;
            foreach (var v in model.Mesh.Vertices)
            {
                var p = v.Position + offset;
                vertices.Add(v with { Position = p with { Y = Math.Max(0, p.Y) } });
            }
            triangles.AddRange(model.Mesh.Triangles.Select(i => i + first));
        }
        var lead = parts[0].Model;
        return new ModelPreview
        {
            ModelPath = name, Mesh = new ModelMesh(vertices, triangles, lead.Mesh.Texture1, lead.Mesh.Texture2, parts.Sum(p => p.Model.Mesh.Pieces)),
            Colour = lead.Colour, Extra = lead.Extra, CutOut = parts.Any(p => p.Model.CutOut), Notes = [.. parts.SelectMany(p => p.Model.Notes).Distinct()],
        };
    }

    /// <summary>Where a texture an S3O names is: under unittextures/, as named or with another image extension.</summary>
    public static string? TexturePath(string name, Func<string, string?> resolve)
    {
        var path = "unittextures/" + name.Replace('\\', '/').TrimStart('/');
        return resolve(path) ?? new[] { ".dds", ".tga", ".png" }.Select(e => resolve(Path.ChangeExtension(path, e))).FirstOrDefault(p => p != null);
    }

    /// <summary>A texture file, top row first: DDS (a mip level no larger than the preview needs), TGA or PNG.</summary>
    public static RgbaImage ReadTexture(string file)
    {
        var data = File.ReadAllBytes(file);
        switch (Path.GetExtension(file).ToLowerInvariant())
        {
            case ".dds":
                var dds = DdsFile.Load(data);
                if (!dds.IsEditable)
                    throw new NotSupportedException($"DDS format {dds.FormatName} isn't one this reads");
                int mip = 0;
                while (mip + 1 < dds.MipCount && Math.Max(dds.MipWidth(mip), dds.MipHeight(mip)) > MaxTextureSize)
                    mip++;
                return dds.Decode(mip);
            case ".tga":
                return TgaReader.Read(data);
            case ".png":
                return PngReader.Read(data).ToRgba();
            default:
                throw new NotSupportedException($"{Path.GetExtension(file)} textures aren't read");
        }
    }

    /// <summary>
    /// Whether an alpha channel cuts shapes out: some texels clear and some solid. One that is all clear (or all
    /// solid) cuts nothing; a model whose texture 2 had no alpha would otherwise vanish.
    /// </summary>
    public static bool HasCutOut(RgbaImage image)
    {
        long clear = 0, solid = 0, n = (long)image.Width * image.Height;
        var p = image.Pixels;
        for (long i = 3; i < p.Length; i += 4)
            if (p[i] < 128)
                clear++;
            else
                solid++;
        return clear > n / 50 && solid > n / 50;
    }

    static string Full(string root, string entry) => Path.Combine(root, entry.Replace('/', Path.DirectorySeparatorChar));
}
