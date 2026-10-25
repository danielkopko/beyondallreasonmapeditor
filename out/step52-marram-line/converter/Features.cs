// Marram Line's features as files in the map: features/marram/<name>.lua, objects3d/<name>.s3o and the atlas in
// unittextures. Every model is centred on its origin (x and z, over what stands above ground), so its footprint and
// collision box sit where it is placed.
using System.Globalization;
using System.Numerics;
using System.Text;

/// <summary>
/// One feature: its model, how it blocks and what it is worth. Landmarks are indestructible with nothing to reclaim;
/// the rest are reclaimable. A footprint may be given (a tree's trunk, not its crown). Crush is the feature's crush
/// resistance: a unit whose movement class's crush strength is at least that drives through it, crushing it (Recoil's
/// MoveMath.cpp, CrushResistant; every feature is crushable, Feature.cpp). Null leaves the engine's default, its mass.
/// </summary>
sealed record FeatureDef(string Name, string Description, Mesh Mesh, bool Landmark, int Metal, int Energy = 0, bool Blocking = true, (int X, int Z)? Base = null, int? Crush = null)
{
    /// <summary>The box of what stands above ground (a little above, so a skirt doesn't count).</summary>
    public (Vector3 Min, Vector3 Max) Box => Mesh.Solid(above: 2);

    /// <summary>Footprint in footprint units (16 game units), over the model's box above ground, at least 1.</summary>
    public (int X, int Z) Footprint
    {
        get
        {
            if (Base is (int bx, int bz)) return (bx, bz);
            var (min, max) = Box;
            return (Math.Max(1, (int)Math.Round((max.X - min.X) / 16)), Math.Max(1, (int)Math.Round((max.Z - min.Z) / 16)));
        }
    }
}

static class Features
{
    static string F(double v) => Math.Round(v, 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>Moves the mesh so what stands above ground is centred on the origin in x and z.</summary>
    public static Mesh Centred(Mesh m, bool alongOnly = false)
    {
        var (min, max) = m.Solid(above: 2);
        var c = new Vector3((min.X + max.X) / 2, 0, alongOnly ? 0 : (min.Z + max.Z) / 2);
        var o = new Mesh();
        o.Add(m, Matrix4x4.CreateTranslation(-c));
        return o;
    }

    public static string Lua(FeatureDef d)
    {
        var (min, max) = d.Box;
        var (fx, fz) = d.Footprint;
        var sb = new StringBuilder();
        sb.Append($"-- {d.Name}: {d.Description}. Model and texture made by the Marram Line converter.\n");
        sb.Append("local featureDef = {\n");
        sb.Append($"\tname = \"{d.Name}\",\n");
        sb.Append($"\tdescription = \"{d.Description}\",\n");
        sb.Append($"\tobject = \"{d.Name}.s3o\",\n");
        sb.Append($"\tblocking = {(d.Blocking ? "true" : "false")},\n");
        sb.Append("\tupright = true,\n");
        sb.Append($"\tfootprintx = {fx},\n");
        sb.Append($"\tfootprintz = {fz},\n");
        if (d.Landmark)
            sb.Append("\tdamage = 1000000000,\n\tindestructible = true,\n\tcrushresistance = 1000000,\n\treclaimable = false,\n\tautoreclaimable = false,\n\tmetal = 0,\n\tenergy = 0,\n");
        else
            sb.Append($"\tdamage = {Math.Max(2000, d.Metal * 12)},\n\treclaimable = true,\n\tautoreclaimable = false,\n\tmetal = {d.Metal},\n\tenergy = {d.Energy},\n\treclaimtime = {Math.Max(400, d.Metal * 6)},\n");
        if (!d.Landmark && d.Crush is int crush)
            sb.Append($"\tcrushresistance = {crush},\n");
        sb.Append("\tflammable = false,\n");
        sb.Append("\tcollisionvolumetype = \"box\",\n");
        sb.Append($"\tcollisionvolumescales = \"{F(max.X - min.X)} {F(max.Y)} {F(max.Z - min.Z)}\",\n");
        sb.Append($"\tcollisionvolumeoffsets = \"{F((min.X + max.X) / 2)} {F(max.Y / 2)} {F((min.Z + max.Z) / 2)}\",\n");
        sb.Append("\tcustomparams = {\n\t\tcuspbr = true,\n\t\tmodel_author = \"Marram Line converter\",\n\t\tcategory = \"Marram Line\",\n\t},\n");
        sb.Append("}\n");
        sb.Append("return lowerkeys({[featureDef.name] = featureDef})\n");
        return sb.ToString();
    }

    /// <summary>Writes the features, their models and the atlas into a map folder.</summary>
    public static void Write(string root, IEnumerable<FeatureDef> defs)
    {
        string fdir = Path.Combine(root, "features", "marram"), odir = Path.Combine(root, "objects3d"), tdir = Path.Combine(root, "unittextures");
        Directory.CreateDirectory(fdir);
        Directory.CreateDirectory(odir);
        Directory.CreateDirectory(tdir);
        foreach (var d in defs)
        {
            File.WriteAllText(Path.Combine(fdir, d.Name + ".lua"), Lua(d));
            File.WriteAllBytes(Path.Combine(odir, d.Name + ".s3o"), d.Mesh.ToS3o(d.Name, Atlas.Texture1, Atlas.Texture2));
        }
        var (c, e) = Atlas.Files();
        File.WriteAllBytes(Path.Combine(tdir, Atlas.Texture1), c);
        File.WriteAllBytes(Path.Combine(tdir, Atlas.Texture2), e);
    }
}
