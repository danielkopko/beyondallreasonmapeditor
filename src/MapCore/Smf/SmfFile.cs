using System.Text;

namespace MapCore.Smf;

/// <summary>
/// A Spring/Recoil map file (.smf): heightmap, type map, minimap, metal map, texture tile index,
/// features and optional grass map.
/// </summary>
/// <remarks>
/// Values are held exactly as stored. Physical heights come from the height range, which mapinfo.lua
/// may override (smf.minheight / smf.maxheight). Saving rebuilds every offset; an unedited file is
/// written back byte for byte because the original section order is kept.
/// </remarks>
public sealed partial class SmfFile
{
    public const int HeaderSize = 80;
    /// <summary>DXT1 1024x1024 with 9 mip levels (down to 4x4), the only minimap size the engine reads.</summary>
    public const int MinimapSize = 699048;
    public const int SquareSize = 8;
    public const int TexelsPerSquare = 8;
    public const int TileSize = 32;
    const int FeatureRecordSize = 24;

    static ReadOnlySpan<byte> Magic => "spring map file\0"u8;
    static readonly Encoding NameEncoding = Encoding.Latin1;

    /// <summary>Section order used when a file has no order of its own to preserve.</summary>
    public static readonly IReadOnlyList<SmfSection> DefaultSectionOrder =
    [
        SmfSection.Grass, SmfSection.Heightmap, SmfSection.TypeMap, SmfSection.Minimap,
        SmfSection.MetalMap, SmfSection.Tiles, SmfSection.Features,
    ];

    public int Version { get; set; } = 1;
    public int MapId { get; set; }

    /// <summary>Width in heightmap squares (8 game units each).</summary>
    public int MapX { get; }

    /// <summary>Height (north-south extent) in heightmap squares.</summary>
    public int MapY { get; }

    /// <summary>Height of raw value 0. mapinfo.lua's smf.minheight overrides this in game.</summary>
    public float MinHeight { get; set; }

    /// <summary>Height of raw value 65535. mapinfo.lua's smf.maxheight overrides this in game.</summary>
    public float MaxHeight { get; set; }

    /// <summary>(MapX+1) x (MapY+1) raw heights, row by row from the north-west corner.</summary>
    public ushort[] Heightmap { get; }

    /// <summary>Terrain type per 2x2 squares; indexes mapinfo terrainTypes.</summary>
    public byte[] TypeMap { get; }

    /// <summary>Raw DXT1 minimap with mip levels, <see cref="MinimapSize"/> bytes.</summary>
    public byte[] Minimap { get; }

    /// <summary>Metal density per 2x2 squares (16 game units), 0-255.</summary>
    public byte[] MetalMap { get; }

    /// <summary>Grass density per 4x4 squares, or null when the map has no grass header.</summary>
    public byte[]? GrassMap { get; set; }

    /// <summary>Tile files in order; together they define the tile numbers used by <see cref="TileIndex"/>.</summary>
    public List<SmtReference> TileFiles { get; } = [];

    /// <summary>Tile number per 32x32-texel block of the map texture, row by row.</summary>
    public int[] TileIndex { get; }

    public List<string> FeatureTypes { get; } = [];
    public List<SmfFeature> Features { get; } = [];

    /// <summary>Extra headers in file order (grass entries carry no raw bytes).</summary>
    public List<SmfExtraHeader> ExtraHeaders { get; } = [];

    /// <summary>Order in which sections are written after the headers.</summary>
    public List<SmfSection> SectionOrder { get; } = [.. DefaultSectionOrder];

    /// <summary>Section positions in the file this instance was loaded from; empty for a new map.</summary>
    public IReadOnlyList<SmfSectionLayout> SourceLayout { get; private set; } = [];

    /// <summary>Oddities found while loading that did not prevent it (gaps, unknown headers, ...).</summary>
    public IReadOnlyList<string> Warnings => warnings;
    readonly List<string> warnings = [];

    public int HeightmapWidth => MapX + 1;
    public int HeightmapHeight => MapY + 1;
    public int MetalMapWidth => MapX / 2;
    public int MetalMapHeight => MapY / 2;
    public int TileColumns => MapX * TexelsPerSquare / TileSize;
    public int TileRows => MapY * TexelsPerSquare / TileSize;
    public int GrassMapWidth => MapX / 4;
    public int GrassMapHeight => MapY / 4;

    /// <summary>Total tiles across all tile files.</summary>
    public int TileCount => TileFiles.Sum(f => f.TileCount);

    /// <summary>
    /// The index of a feature type, adding it to <see cref="FeatureTypes"/> if missing. Names compare
    /// case-insensitively, as the engine lowercases them.
    /// </summary>
    public int FeatureTypeIndex(string name)
    {
        int index = FeatureTypes.FindIndex(t => t.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            return index;
        FeatureTypes.Add(name);
        return FeatureTypes.Count - 1;
    }

    /// <summary>
    /// Places a feature. The engine drops SMF features onto the ground, so no height is needed (stored as 0);
    /// <paramref name="rotation"/> is a heading in 65536ths of a turn (engine FeatureHandler).
    /// </summary>
    public void AddFeature(string typeName, float x, float z, float rotation) =>
        Features.Add(new SmfFeature(FeatureTypeIndex(typeName), x, 0, z, rotation, 1));

    /// <summary>A blank map of the given size in squares (multiples of 4).</summary>
    public SmfFile(int mapX, int mapY)
    {
        if (mapX <= 0 || mapY <= 0 || mapX % 4 != 0 || mapY % 4 != 0)
            throw new ArgumentException($"Map size {mapX}x{mapY} squares must be positive multiples of 4.");
        MapX = mapX;
        MapY = mapY;
        Heightmap = new ushort[HeightmapWidth * HeightmapHeight];
        TypeMap = new byte[MetalMapWidth * MetalMapHeight];
        Minimap = new byte[MinimapSize];
        MetalMap = new byte[MetalMapWidth * MetalMapHeight];
        TileIndex = new int[TileColumns * TileRows];
    }
}
