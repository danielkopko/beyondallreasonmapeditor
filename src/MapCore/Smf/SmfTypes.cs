namespace MapCore.Smf;

/// <summary>Data blocks of an SMF file that are located through pointers in the header.</summary>
public enum SmfSection { Grass, Heightmap, TypeMap, Minimap, MetalMap, Tiles, Features }

/// <summary>Where a section sits in a file, in bytes.</summary>
public readonly record struct SmfSectionLayout(SmfSection Section, int Offset, int Length);

/// <summary>A texture tile file (.smt) used by the map, and how many tiles it contributes to the tile index.</summary>
public sealed record SmtReference(string FileName, int TileCount);

/// <summary>A feature placed by the SMF itself (engine trees, geothermal vents). Mirrors the engine's MapFeatureStruct.</summary>
/// <param name="TypeIndex">Index into <see cref="SmfFile.FeatureTypes"/>.</param>
/// <param name="RelativeSize">Stored by the format but unused by the engine.</param>
public record struct SmfFeature(int TypeIndex, float X, float Y, float Z, float Rotation, float RelativeSize);

/// <summary>An extra header following the main header. Grass headers are rebuilt on write; other types are kept as raw bytes.</summary>
/// <param name="Raw">The complete header (size and type fields included) for types the editor does not interpret; null for grass.</param>
public sealed record SmfExtraHeader(int Type, byte[]? Raw)
{
    public const int GrassType = 1;
    public const int GrassHeaderSize = 12; // size, type, pointer to grass map
}
