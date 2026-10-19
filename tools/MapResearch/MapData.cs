using MapCore;
using MapCore.Smf;

namespace MapResearch;

/// <summary>A loaded map with heights in game units, using mapinfo's height range when it overrides the SMF's.</summary>
sealed class MapData
{
    public required MapPackage Package { get; init; }
    public required SmfFile Smf { get; init; }
    public required MapInfo Info { get; init; }
    public required float MinHeight { get; init; }
    public required float MaxHeight { get; init; }

    /// <summary>Heights at the (MapX+1) x (MapY+1) heightmap vertices, 8 game units apart.</summary>
    public required float[] Heights { get; init; }

    public int VerticesX => Smf.HeightmapWidth;
    public int VerticesZ => Smf.HeightmapHeight;

    public static MapData Load(MapPackage package)
    {
        var smf = package.LoadSmf();
        var info = package.LoadMapInfo();
        float min = (float)(info.MinHeight ?? smf.MinHeight);
        float max = (float)(info.MaxHeight ?? smf.MaxHeight);
        var heights = smf.Heightmap.Select(raw => min + (max - min) * raw / 65535f).ToArray();
        return new MapData { Package = package, Smf = smf, Info = info, MinHeight = min, MaxHeight = max, Heights = heights };
    }

    public float Height(int x, int z) =>
        Heights[Math.Clamp(z, 0, VerticesZ - 1) * VerticesX + Math.Clamp(x, 0, VerticesX - 1)];

    /// <summary>Bilinear height at a world position in game units.</summary>
    public float HeightAt(double worldX, double worldZ)
    {
        double u = worldX / 8, v = worldZ / 8;
        int x = (int)Math.Floor(u), z = (int)Math.Floor(v);
        float fx = (float)(u - x), fz = (float)(v - z);
        float top = Height(x, z) * (1 - fx) + Height(x + 1, z) * fx;
        float bottom = Height(x, z + 1) * (1 - fx) + Height(x + 1, z + 1) * fx;
        return top * (1 - fz) + bottom * fz;
    }

    /// <summary>Path of a mapinfo resources texture, e.g. "detailNormalTex" (looked up under maps/).</summary>
    public string ResourcePath(string key)
    {
        var name = Info.Resource(key) ?? throw new InvalidDataException($"mapinfo.lua sets no resources.{key}.");
        return Path.Combine(Package.Root, "maps", name);
    }
}
