using System.Buffers.Binary;

namespace MapCore.Smf;

/// <summary>
/// A tile file (.smt): DXT1 tiles of 32x32 texels, each stored with mip levels 32, 16, 8 and 4,
/// laid out as the engine reads them (SMFGroundTextures).
/// </summary>
public sealed class SmtFile
{
    public const int TileSize = 32;
    public const int HeaderSize = 32;
    public const int MipCount = 4;
    public const int TileBytes = 680;

    /// <summary>Byte offset and length of each mip level within a tile.</summary>
    public static readonly (int Offset, int Length)[] MipLayout = [(0, 512), (512, 128), (640, 32), (672, 8)];

    static ReadOnlySpan<byte> Magic => "spring tilefile\0"u8;
    const int Version = 1;
    const int CompressionDxt1 = 1;

    byte[] tiles;
    byte[] extraBytes;

    public int TileCount { get; private set; }

    /// <summary>Oddities found while loading that did not prevent it.</summary>
    public IReadOnlyList<string> Warnings { get; }

    public SmtFile() : this([], 0, [], []) { }

    SmtFile(byte[] tiles, int count, byte[] extraBytes, IReadOnlyList<string> warnings)
    {
        this.tiles = tiles;
        TileCount = count;
        this.extraBytes = extraBytes;
        Warnings = warnings;
    }

    public static SmtFile Load(string path) => Load(File.ReadAllBytes(path));

    /// <exception cref="InvalidDataException">The data is not a tile file the engine accepts.</exception>
    public static SmtFile Load(byte[] data)
    {
        if (data.Length < HeaderSize || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("Invalid SMT file: not a tile file (wrong magic text).");
        int version = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16));
        int count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(20));
        int tileSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(24));
        int compression = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(28));
        // The same checks the engine makes before reading tiles.
        if (version != Version || tileSize != TileSize || compression != CompressionDxt1)
            throw new InvalidDataException($"Invalid SMT file: version {version}, tile size {tileSize}, compression {compression} (the engine needs 1, 32, 1).");
        if (count < 0 || HeaderSize + (long)count * TileBytes > data.Length)
            throw new InvalidDataException($"Invalid SMT file: {count} tiles need {HeaderSize + (long)count * TileBytes} bytes; the file has {data.Length}.");

        int end = HeaderSize + count * TileBytes;
        var warnings = new List<string>();
        if (end < data.Length)
            warnings.Add($"{data.Length - end} bytes after the last tile; kept unchanged");
        return new SmtFile(data[HeaderSize..end], count, data[end..], warnings);
    }

    public void Save(string path) => FileSystemUtil.WriteAllBytesAtomic(path, ToBytes());

    public byte[] ToBytes()
    {
        var output = new byte[HeaderSize + TileCount * TileBytes + extraBytes.Length];
        Magic.CopyTo(output);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(16), Version);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(20), TileCount);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(24), TileSize);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(28), CompressionDxt1);
        tiles.AsSpan(0, TileCount * TileBytes).CopyTo(output.AsSpan(HeaderSize));
        extraBytes.CopyTo(output.AsSpan(HeaderSize + TileCount * TileBytes));
        return output;
    }

    /// <summary>All mip levels of one tile (680 bytes); writable.</summary>
    public Memory<byte> Tile(int index)
    {
        if ((uint)index >= (uint)TileCount)
            throw new ArgumentOutOfRangeException(nameof(index), $"Tile {index} does not exist ({TileCount} tiles).");
        return tiles.AsMemory(index * TileBytes, TileBytes);
    }

    /// <summary>Appends a tile and returns its index.</summary>
    public int AddTile(ReadOnlySpan<byte> tile)
    {
        if (tile.Length != TileBytes)
            throw new ArgumentException($"A tile is {TileBytes} bytes, not {tile.Length}.", nameof(tile));
        if ((TileCount + 1) * TileBytes > tiles.Length)
            Array.Resize(ref tiles, Math.Max(TileBytes * 64, tiles.Length * 2));
        tile.CopyTo(tiles.AsSpan(TileCount * TileBytes));
        return TileCount++;
    }

    /// <summary>Sets the tile count: drops tiles beyond it, or adds blank tiles up to it (used by undo/redo).</summary>
    public void Resize(int count)
    {
        if (count <= TileCount)
        {
            Truncate(count);
            return;
        }
        if (count * TileBytes > tiles.Length)
            Array.Resize(ref tiles, Math.Max(count * TileBytes, tiles.Length * 2));
        tiles.AsSpan(TileCount * TileBytes, (count - TileCount) * TileBytes).Clear();
        TileCount = count;
    }

    /// <summary>Drops tiles beyond <paramref name="count"/> (the engine never reads tiles past the SMF's count).</summary>
    public void Truncate(int count)
    {
        if (count < 0 || count > TileCount)
            throw new ArgumentOutOfRangeException(nameof(count));
        TileCount = count;
    }
}
