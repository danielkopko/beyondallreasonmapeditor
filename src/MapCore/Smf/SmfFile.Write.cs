using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace MapCore.Smf;

public sealed partial class SmfFile
{
    /// <summary>Writes the map to <paramref name="path"/>, replacing any existing file only once the new one is complete.</summary>
    public void Save(string path) => FileSystemUtil.WriteAllBytesAtomic(path, ToBytes());

    /// <summary>
    /// Serialises the map: header, extra headers, then each section in <see cref="SectionOrder"/>,
    /// with every pointer recalculated.
    /// </summary>
    /// <exception cref="InvalidOperationException">The map's contents cannot be saved as they are.</exception>
    public byte[] ToBytes()
    {
        Validate();

        var order = SectionOrder.Where(s => s != SmfSection.Grass || GrassMap != null).ToList();
        if (GrassMap != null && !order.Contains(SmfSection.Grass))
            order.Insert(0, SmfSection.Grass);

        var extras = ExtraHeaders.Where(h => h.Type != SmfExtraHeader.GrassType || GrassMap != null).ToList();
        if (GrassMap != null && !extras.Any(h => h.Type == SmfExtraHeader.GrassType))
            extras.Add(new(SmfExtraHeader.GrassType, null));

        var payloads = order.ToDictionary(s => s, GetSectionBytes);
        var offsets = new Dictionary<SmfSection, int>();
        int offset = HeaderSize + extras.Sum(h => h.Raw?.Length ?? SmfExtraHeader.GrassHeaderSize);
        foreach (var section in order)
        {
            offsets[section] = offset;
            offset = checked(offset + payloads[section].Length);
        }

        var output = new byte[offset];
        var span = output.AsSpan();
        Magic.CopyTo(span);
        WriteInt(span, 16, Version);
        WriteInt(span, 20, MapId);
        WriteInt(span, 24, MapX);
        WriteInt(span, 28, MapY);
        WriteInt(span, 32, SquareSize);
        WriteInt(span, 36, TexelsPerSquare);
        WriteInt(span, 40, TileSize);
        BinaryPrimitives.WriteSingleLittleEndian(span[44..], MinHeight);
        BinaryPrimitives.WriteSingleLittleEndian(span[48..], MaxHeight);
        WriteInt(span, 52, offsets[SmfSection.Heightmap]);
        WriteInt(span, 56, offsets[SmfSection.TypeMap]);
        WriteInt(span, 60, offsets[SmfSection.Tiles]);
        WriteInt(span, 64, offsets[SmfSection.Minimap]);
        WriteInt(span, 68, offsets[SmfSection.MetalMap]);
        WriteInt(span, 72, offsets[SmfSection.Features]);
        WriteInt(span, 76, extras.Count);

        int pos = HeaderSize;
        foreach (var header in extras)
        {
            if (header.Raw != null)
            {
                header.Raw.CopyTo(span[pos..]);
                pos += header.Raw.Length;
                continue;
            }
            WriteInt(span, pos, SmfExtraHeader.GrassHeaderSize);
            WriteInt(span, pos + 4, SmfExtraHeader.GrassType);
            WriteInt(span, pos + 8, offsets[SmfSection.Grass]);
            pos += SmfExtraHeader.GrassHeaderSize;
        }

        foreach (var section in order)
            payloads[section].CopyTo(span[offsets[section]..]);
        return output;
    }

    /// <summary>The bytes a section occupies in a saved file.</summary>
    public byte[] GetSectionBytes(SmfSection section) => section switch
    {
        SmfSection.Grass => (GrassMap ?? throw new InvalidOperationException("The map has no grass map.")).ToArray(),
        SmfSection.Heightmap => MemoryMarshal.AsBytes(Heightmap.AsSpan()).ToArray(),
        SmfSection.TypeMap => TypeMap.ToArray(),
        SmfSection.Minimap => Minimap.ToArray(),
        SmfSection.MetalMap => MetalMap.ToArray(),
        SmfSection.Tiles => SerializeTiles(),
        SmfSection.Features => SerializeFeatures(),
        _ => throw new ArgumentOutOfRangeException(nameof(section)),
    };

    byte[] SerializeTiles()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms); // BinaryWriter is always little-endian
        w.Write(TileFiles.Count);
        w.Write(TileCount);
        foreach (var file in TileFiles)
        {
            w.Write(file.TileCount);
            WriteCString(w, file.FileName);
        }
        w.Write(MemoryMarshal.AsBytes(TileIndex.AsSpan()));
        w.Flush();
        return ms.ToArray();
    }

    byte[] SerializeFeatures()
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(FeatureTypes.Count);
        w.Write(Features.Count);
        foreach (var name in FeatureTypes)
            WriteCString(w, name);
        foreach (var f in Features)
        {
            w.Write(f.TypeIndex);
            w.Write(f.X);
            w.Write(f.Y);
            w.Write(f.Z);
            w.Write(f.Rotation);
            w.Write(f.RelativeSize);
        }
        w.Flush();
        return ms.ToArray();
    }

    void Validate()
    {
        foreach (var section in Enum.GetValues<SmfSection>())
        {
            int count = SectionOrder.Count(s => s == section);
            if (count > 1 || (count == 0 && section != SmfSection.Grass))
                throw Invalid($"section order must list {section} exactly once");
        }
        if (GrassMap != null && GrassMap.Length != GrassMapWidth * GrassMapHeight)
            throw Invalid($"grass map has {GrassMap.Length} bytes, expected {GrassMapWidth * GrassMapHeight}");
        foreach (var file in TileFiles)
        {
            if (string.IsNullOrEmpty(file.FileName) || file.FileName.Contains('\0'))
                throw Invalid($"tile file name '{file.FileName}' is empty or contains a NUL character");
            if (file.TileCount < 0)
                throw Invalid($"tile file '{file.FileName}' has a negative tile count");
        }
        foreach (var name in FeatureTypes)
            if (string.IsNullOrEmpty(name) || name.Contains('\0'))
                throw Invalid($"feature type name '{name}' is empty or contains a NUL character");
        int badFeature = Features.FindIndex(f => f.TypeIndex < 0 || f.TypeIndex >= FeatureTypes.Count);
        if (badFeature >= 0)
            throw Invalid($"feature {badFeature} refers to type {Features[badFeature].TypeIndex}, but there are {FeatureTypes.Count} types");
        foreach (var header in ExtraHeaders.Where(h => h.Type != SmfExtraHeader.GrassType))
            if (header.Raw == null || header.Raw.Length < 8 || BinaryPrimitives.ReadInt32LittleEndian(header.Raw) != header.Raw.Length)
                throw Invalid($"extra header type {header.Type} has malformed raw bytes");
    }

    static void WriteInt(Span<byte> span, int offset, int value) =>
        BinaryPrimitives.WriteInt32LittleEndian(span[offset..], value);

    static void WriteCString(BinaryWriter w, string text)
    {
        w.Write(NameEncoding.GetBytes(text));
        w.Write((byte)0);
    }

    static InvalidOperationException Invalid(string message) => new($"Cannot save SMF: {message}.");
}
