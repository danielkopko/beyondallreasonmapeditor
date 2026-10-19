using MapCore.Imaging;
using MapCore.Metal;
using MapCore.Smf;

namespace MapCore.Editing;

/// <summary>What is at one point of the map: for the editor's status bar.</summary>
/// <param name="ExtractorIncome">Metal/s a T1 extractor placed here would earn.</param>
public sealed record MapPoint(double X, double Z, bool OnMap, float Height, byte MetalRaw, double ExtractorIncome, byte Grass);

/// <summary>Read-only questions about the map, for the editor.</summary>
public sealed partial class MapEditSession
{
    /// <summary>Map width in game units (x).</summary>
    public double MapWidth => Smf.MapX * (double)SmfFile.SquareSize;

    /// <summary>Map height in game units (z).</summary>
    public double MapHeight => Smf.MapY * (double)SmfFile.SquareSize;

    /// <summary>mapinfo's name when the map was opened; Save As must not keep both it and <see cref="LoadedVersion"/>.</summary>
    public string? LoadedName { get; private init; }

    /// <summary>mapinfo's version when the map was opened.</summary>
    public string? LoadedVersion { get; private init; }

    public MapPoint Probe(double x, double z)
    {
        if (x < 0 || z < 0 || x >= MapWidth || z >= MapHeight)
            return new MapPoint(x, z, false, 0, 0, 0, 0);
        int mx = Math.Min((int)(x / MetalLayer.CellSize), Smf.MetalMapWidth - 1);
        int mz = Math.Min((int)(z / MetalLayer.CellSize), Smf.MetalMapHeight - 1);
        return new MapPoint(x, z, true, Heights.HeightAt(x, z), Smf.MetalMap[mz * Smf.MetalMapWidth + mx],
            Metal.Income(x, z), GrassLayer.ValueAt(Smf, x, z));
    }

    /// <summary>A mapinfo resource texture (e.g. "splatDistrTex") decoded in map orientation (north at the top).</summary>
    /// <exception cref="InvalidOperationException">The map has no such resource, or it cannot be read.</exception>
    public RgbaImage ResourceImage(string key)
    {
        var image = Resource(key, out _).Decode();
        image.FlipVertical();
        return image;
    }
}
