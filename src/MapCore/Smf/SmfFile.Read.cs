using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace MapCore.Smf;

public sealed partial class SmfFile
{
    public static SmfFile Load(string path) => Load(File.ReadAllBytes(path));

    /// <exception cref="InvalidDataException">The data is not a readable SMF file.</exception>
    public static SmfFile Load(byte[] data)
    {
        var r = new Reader(data);
        if (data.Length < HeaderSize)
            throw Bad($"file is {data.Length} bytes, shorter than the {HeaderSize}-byte header");
        if (!data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw Bad("not an SMF file (wrong magic text)");

        int version = r.Int(16), mapId = r.Int(20), mapX = r.Int(24), mapY = r.Int(28);
        int squareSize = r.Int(32), texelsPerSquare = r.Int(36), tileSize = r.Int(40);
        int heightmapPtr = r.Int(52), typeMapPtr = r.Int(56), tilesPtr = r.Int(60), minimapPtr = r.Int(64);
        int metalMapPtr = r.Int(68), featurePtr = r.Int(72), numExtraHeaders = r.Int(76);

        if (version != 1)
            throw Bad($"unsupported version {version}");
        if (squareSize != SquareSize || texelsPerSquare != TexelsPerSquare || tileSize != TileSize)
            throw Bad($"unsupported square/texel/tile size {squareSize}/{texelsPerSquare}/{tileSize} (expected 8/8/32)");
        if (mapX <= 0 || mapY <= 0 || mapX % 4 != 0 || mapY % 4 != 0 || mapX > 1 << 14 || mapY > 1 << 14)
            throw Bad($"implausible map size {mapX}x{mapY}");
        if (numExtraHeaders < 0)
            throw Bad($"negative extra header count {numExtraHeaders}");

        var smf = new SmfFile(mapX, mapY)
        {
            Version = version,
            MapId = mapId,
            MinHeight = r.Float(44),
            MaxHeight = r.Float(48),
        };
        var layout = new List<SmfSectionLayout>();

        // Extra headers sit back to back after the main header; each starts with its size and type.
        int pos = HeaderSize;
        for (int i = 0; i < numExtraHeaders; i++)
        {
            int size = r.Int(pos), type = r.Int(pos + 4);
            if (size < 8)
                throw Bad($"extra header {i} has invalid size {size}");
            r.Check(pos, size, $"extra header {i}");
            if (type == SmfExtraHeader.GrassType)
            {
                if (size < SmfExtraHeader.GrassHeaderSize)
                    throw Bad($"grass header is {size} bytes, expected {SmfExtraHeader.GrassHeaderSize}");
                if (size != SmfExtraHeader.GrassHeaderSize)
                    smf.warnings.Add($"grass header is {size} bytes; it will be written as {SmfExtraHeader.GrassHeaderSize}");
                int grassPtr = r.Int(pos + 8);
                int length = smf.GrassMapWidth * smf.GrassMapHeight;
                smf.GrassMap = r.Slice(grassPtr, length, "grass map").ToArray();
                layout.Add(new(SmfSection.Grass, grassPtr, length));
                smf.ExtraHeaders.Add(new(type, null));
            }
            else
            {
                smf.ExtraHeaders.Add(new(type, data.AsSpan(pos, size).ToArray()));
                smf.warnings.Add($"extra header type {type} is not understood by the editor; kept unchanged");
            }
            pos += size;
        }
        int extraHeadersEnd = pos;

        // SMF data is little-endian, as is every platform the editor runs on.
        CopySection(r, layout, SmfSection.Heightmap, heightmapPtr, MemoryMarshal.AsBytes(smf.Heightmap.AsSpan()));
        CopySection(r, layout, SmfSection.TypeMap, typeMapPtr, smf.TypeMap);
        CopySection(r, layout, SmfSection.Minimap, minimapPtr, smf.Minimap);
        CopySection(r, layout, SmfSection.MetalMap, metalMapPtr, smf.MetalMap);
        layout.Add(ReadTiles(r, smf, tilesPtr));
        layout.Add(ReadFeatures(r, smf, featurePtr));

        layout.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        smf.SectionOrder.Clear();
        smf.SectionOrder.AddRange(layout.Select(s => s.Section));
        smf.SourceLayout = layout;
        CheckCoverage(smf, layout, extraHeadersEnd, data.Length);
        return smf;
    }

    static void CopySection(Reader r, List<SmfSectionLayout> layout, SmfSection section, int offset, Span<byte> target)
    {
        r.Slice(offset, target.Length, section.ToString()).CopyTo(target);
        layout.Add(new(section, offset, target.Length));
    }

    /// <summary>Tile file count, total tile count, then (count, name) per file, then one int per tile slot.</summary>
    static SmfSectionLayout ReadTiles(Reader r, SmfFile smf, int start)
    {
        int numFiles = r.Int(start), declaredTotal = r.Int(start + 4);
        if (numFiles < 0)
            throw Bad($"negative tile file count {numFiles}");
        int p = start + 8;
        for (int i = 0; i < numFiles; i++)
        {
            int count = r.Int(p);
            p += 4;
            smf.TileFiles.Add(new(r.CString(ref p, "tile file name"), count));
        }
        if (declaredTotal != smf.TileCount)
            smf.warnings.Add($"tile section declares {declaredTotal} tiles but its files add up to {smf.TileCount}");

        int indexBytes = smf.TileIndex.Length * 4;
        r.Slice(p, indexBytes, "tile index").CopyTo(MemoryMarshal.AsBytes(smf.TileIndex.AsSpan()));
        p += indexBytes;

        int outOfRange = smf.TileIndex.Count(t => t < 0 || t >= smf.TileCount);
        if (outOfRange > 0)
            smf.warnings.Add($"{outOfRange} tile index entries point outside the {smf.TileCount} available tiles");
        return new(SmfSection.Tiles, start, p - start);
    }

    /// <summary>Type count, feature count, type names, then 24-byte feature records.</summary>
    static SmfSectionLayout ReadFeatures(Reader r, SmfFile smf, int start)
    {
        int numTypes = r.Int(start), numFeatures = r.Int(start + 4);
        if (numTypes < 0 || numFeatures < 0)
            throw Bad($"negative feature counts ({numTypes} types, {numFeatures} features)");
        int p = start + 8;
        for (int i = 0; i < numTypes; i++)
            smf.FeatureTypes.Add(r.CString(ref p, "feature type name"));

        r.Check(p, (long)numFeatures * FeatureRecordSize, "feature records");
        for (int i = 0; i < numFeatures; i++, p += FeatureRecordSize)
            smf.Features.Add(new(r.Int(p), r.Float(p + 4), r.Float(p + 8), r.Float(p + 12), r.Float(p + 16), r.Float(p + 20)));

        int badTypes = smf.Features.Count(f => f.TypeIndex < 0 || f.TypeIndex >= numTypes);
        if (badTypes > 0)
            smf.warnings.Add($"{badTypes} features refer to a feature type that does not exist");
        return new(SmfSection.Features, start, p - start);
    }

    /// <summary>Notes bytes not covered by any section and sections that overlap; neither survives a save.</summary>
    static void CheckCoverage(SmfFile smf, List<SmfSectionLayout> sections, int extraHeadersEnd, int fileLength)
    {
        int cursor = extraHeadersEnd;
        foreach (var s in sections)
        {
            if (s.Offset > cursor)
                smf.warnings.Add($"{s.Offset - cursor} unused bytes before the {s.Section} section; they will be dropped on save");
            else if (s.Offset < cursor)
                smf.warnings.Add($"{s.Section} section overlaps the data before it; it will be written separately on save");
            cursor = Math.Max(cursor, s.Offset + s.Length);
        }
        if (cursor < fileLength)
            smf.warnings.Add($"{fileLength - cursor} trailing bytes after the last section; they will be dropped on save");
    }

    static InvalidDataException Bad(string message) => new($"Invalid SMF file: {message}.");

    /// <summary>Bounds-checked little-endian reads; any out-of-range access becomes an InvalidDataException.</summary>
    readonly struct Reader(byte[] data)
    {
        public int Int(int offset) => BinaryPrimitives.ReadInt32LittleEndian(Slice(offset, 4, "value"));

        public float Float(int offset) => BinaryPrimitives.ReadSingleLittleEndian(Slice(offset, 4, "value"));

        public void Check(long offset, long length, string what)
        {
            if (offset < 0 || length < 0 || offset + length > data.Length)
                throw Bad($"{what} (offset {offset}, {length} bytes) lies outside the {data.Length}-byte file");
        }

        public ReadOnlySpan<byte> Slice(int offset, int length, string what)
        {
            Check(offset, length, what);
            return data.AsSpan(offset, length);
        }

        /// <summary>A NUL-terminated name starting at <paramref name="pos"/>; advances past the NUL.</summary>
        public string CString(ref int pos, string what)
        {
            Check(pos, 0, what);
            int end = Array.IndexOf(data, (byte)0, pos);
            if (end < 0)
                throw Bad($"{what} at offset {pos} is not terminated");
            var text = NameEncoding.GetString(data, pos, end - pos);
            pos = end + 1;
            return text;
        }
    }
}
