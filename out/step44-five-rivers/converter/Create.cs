// A new map at any size, from a template map's other files (its detail textures, sky, features and gadgets): the
// SMF and SMT made from scratch as the editor's tests make their synthetic map, mapinfo.lua pointed at them.
using System.Globalization;
using MapCore;
using MapCore.Archives;
using MapCore.Imaging;
using MapCore.Smf;

static class Create
{
    /// <summary>The template's files that belong to its own layout or would override this map's in game.</summary>
    static readonly string[] Dropped =
    [
        "maps/ATQ.smf", "maps/ATQ.smt", "maps/ATQ_grassmap.tga", "maps/ATQ_normals.dds", "maps/ATQ_speculartex.dds",
        "maps/ATQ_splat_distribution.dds", "maps/source", "mapoptions.lua", "mapconfig/mapinfo/0_apply_options.lua",
        // Not used by the template (Simmering Ravine dropped them too), and its sunrise sky, which a new one replaces.
        "maps/StylizedRocks_1k_dnts.tga", "maps/earth_NORM.tga", "maps/GoldSunriseSkybox_TBv1.dds",
    ];

    /// <summary>
    /// Copies the template to <paramref name="root"/> without its own layout, and writes a new SMF and SMT there:
    /// <paramref name="heights"/> per heightmap vertex, <paramref name="paint"/> per texel (rows in parallel).
    /// </summary>
    public static SmfFile Map(string template, string root, int squaresX, int squaresZ, float minHeight, float maxHeight,
        Func<int, int, float> heights, Action<int, Span<Rgba>> paint, string smfName, Action<SmfFile>? beforeSave = null)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
        CopyTree(template, root);
        foreach (var p in Dropped)
        {
            var full = Path.Combine(root, p);
            if (Directory.Exists(full)) Directory.Delete(full, true);
            else if (File.Exists(full)) File.Delete(full);
        }

        var smf = new SmfFile(squaresX, squaresZ) { MinHeight = minHeight, MaxHeight = maxHeight, MapId = 20261023 };
        Parallel.For(0, squaresZ + 1, z =>
        {
            for (int x = 0; x <= squaresX; x++)
                smf.Heightmap[z * smf.HeightmapWidth + x] =
                    (ushort)Math.Clamp(Math.Round((heights(x, z) - minHeight) / (maxHeight - minHeight) * 65535), 0, 65535);
        });

        // Every tile position its own tile, in one file.
        int columns = smf.TileColumns, rows = smf.TileRows;
        var tiles = new SmtFile();
        tiles.Resize(columns * rows);
        smf.TileFiles.Add(new($"{smfName}.smt", tiles.TileCount));
        for (int i = 0; i < columns * rows; i++)
            smf.TileIndex[i] = i;
        var texture = new MapTexture(smf, [tiles]);
        var image = new RgbaImage(texture.Width, texture.Height);
        Parallel.For(0, image.Height, y =>
        {
            var row = new Rgba[image.Width];
            paint(y, row);
            for (int x = 0; x < image.Width; x++)
                image.SetPixel(x, y, row[x]);
        });
        texture.Update(image, texture.Bounds);
        Minimap.Rebuild(smf, image);

        beforeSave?.Invoke(smf);
        Directory.CreateDirectory(Path.Combine(root, "maps"));
        smf.Save(Path.Combine(root, "maps", $"{smfName}.smf"));
        tiles.Save(Path.Combine(root, "maps", $"{smfName}.smt"));

        // An empty feature set for the featureplacer: everything is placed afterwards with MapTool.
        File.WriteAllText(Path.Combine(root, "mapconfig", "featureplacer", "set.lua"),
            "local setcfg = {\n\tunitlist = {\n\t},\n\tbuildinglist = {\n\t},\n\tobjectlist = {\n\t},\n}\nreturn setcfg\n");

        var info = MapInfo.Load(Path.Combine(root, "mapinfo.lua"));
        info.SetFromText("mapfile", $"maps/{smfName}.smf");
        info.SetFromText("smf.smtFileName0", $"maps/{smfName}.smt");
        info.SetFromText("smf.minheight", minHeight.ToString("R", CultureInfo.InvariantCulture));
        info.SetFromText("smf.maxheight", maxHeight.ToString("R", CultureInfo.InvariantCulture));
        info.Save(Path.Combine(root, "mapinfo.lua"));
        return smf;
    }

    public static void Pack(string root, string archive) => ArchiveIO.Pack(root, archive, overwrite: true);

    static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from))
            File.Copy(f, Path.Combine(to, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(from))
            CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
    }
}
