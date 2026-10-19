using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using MapCore.Imaging;

namespace MapCore.Textures;

public enum DdsFormat
{
    Dxt1,
    Dxt5,
    /// <summary>24- or 32-bit pixels described by channel bit masks.</summary>
    Uncompressed,
    /// <summary>Anything else (DXT3, ATI2, volume textures, arrays...): kept byte for byte but not editable.</summary>
    Unsupported,
}

/// <summary>
/// A DirectDraw Surface texture: one image or six cube faces, each with a chain of mip levels.
/// The original header and every untouched level are written back byte for byte.
/// </summary>
public sealed class DdsFile
{
    const uint Magic = 0x20534444; // "DDS "
    const int HeaderStructSize = 124;
    const int BaseHeaderBytes = 4 + HeaderStructSize;
    const int Dx10HeaderBytes = 20;

    const uint FlagCaps = 0x1, FlagHeight = 0x2, FlagWidth = 0x4, FlagPitch = 0x8, FlagPixelFormat = 0x1000;
    const uint FlagMipMapCount = 0x20000, FlagLinearSize = 0x80000;
    const uint PixelAlpha = 0x1, PixelFourCC = 0x4, PixelRgb = 0x40;
    const uint CapsComplex = 0x8, CapsTexture = 0x1000, CapsMipMap = 0x400000;
    const uint Caps2Cubemap = 0x200, Caps2AllFaces = 0xFC00, Caps2Volume = 0x200000;

    readonly byte[] header;
    readonly byte[][][]? levels;   // [face][mip]; null when the layout is not understood
    readonly byte[] extraBytes;     // bytes after the known levels, or the whole payload for unsupported layouts
    readonly int bitsPerPixel;
    readonly uint redMask, greenMask, blueMask, alphaMask;
    readonly List<string> warnings = [];

    public int Width { get; }
    public int Height { get; }
    public int MipCount { get; }
    public int FaceCount { get; }
    public DdsFormat Format { get; }

    /// <summary>Human-readable format, e.g. "DXT1", "32-bit RGBA", "DXT3 (not editable)".</summary>
    public string FormatName { get; }

    public bool IsCubemap => FaceCount == 6;
    public bool IsEditable => Format != DdsFormat.Unsupported;
    public IReadOnlyList<string> Warnings => warnings;

    public int MipWidth(int mip) => Math.Max(1, Width >> mip);
    public int MipHeight(int mip) => Math.Max(1, Height >> mip);

    DdsFile(byte[] header, byte[][][]? levels, byte[] extraBytes, int width, int height, int mipCount, int faceCount,
        DdsFormat format, string formatName, int bitsPerPixel = 0, uint r = 0, uint g = 0, uint b = 0, uint a = 0)
    {
        this.header = header;
        this.levels = levels;
        this.extraBytes = extraBytes;
        Width = width;
        Height = height;
        MipCount = mipCount;
        FaceCount = faceCount;
        Format = format;
        FormatName = formatName;
        this.bitsPerPixel = bitsPerPixel;
        (redMask, greenMask, blueMask, alphaMask) = (r, g, b, a);
    }

    // ---------------------------------------------------------------- reading

    public static DdsFile Load(string path) => Load(File.ReadAllBytes(path));

    /// <exception cref="InvalidDataException">The data is not a readable DDS file.</exception>
    public static DdsFile Load(byte[] data)
    {
        if (data.Length < BaseHeaderBytes || BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic)
            throw Bad("not a DDS file");
        uint U(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset));

        if (U(4) != HeaderStructSize)
            throw Bad($"header size {U(4)}, expected {HeaderStructSize}");
        int height = (int)U(12), width = (int)U(16), mipCount = Math.Max(1, (int)U(28));
        uint pixelFlags = U(80), fourCC = U(84), caps2 = U(112);
        if (width <= 0 || height <= 0 || width > 65536 || height > 65536)
            throw Bad($"implausible size {width}x{height}");
        int maxMips = BitOperations.Log2((uint)Math.Max(width, height)) + 1;
        if (mipCount > maxMips)
            throw Bad($"{mipCount} mip levels for a {width}x{height} texture (at most {maxMips})");

        int headerLength = BaseHeaderBytes;
        int faces = (caps2 & Caps2Cubemap) != 0 ? BitOperations.PopCount(caps2 & Caps2AllFaces) : 1;
        bool volume = (caps2 & Caps2Volume) != 0;
        DdsFormat format;
        string name;
        int bpp = 0;
        uint r = 0, g = 0, b = 0, a = 0;

        if ((pixelFlags & PixelFourCC) != 0)
        {
            name = Encoding.ASCII.GetString(data, 84, 4).TrimEnd('\0');
            (format, name) = name switch
            {
                "DXT1" => (DdsFormat.Dxt1, "DXT1"),
                "DXT5" => (DdsFormat.Dxt5, "DXT5"),
                "DX10" => ReadDx10(data, ref headerLength, ref faces, ref bpp, ref r, ref g, ref b, ref a),
                _ => (DdsFormat.Unsupported, name),
            };
        }
        else if ((pixelFlags & PixelRgb) != 0 && U(88) is 24 or 32)
        {
            (format, bpp) = (DdsFormat.Uncompressed, (int)U(88));
            (r, g, b) = (U(92), U(96), U(100));
            a = (pixelFlags & PixelAlpha) != 0 ? U(104) : 0;
            name = $"{bpp}-bit {(a != 0 ? "RGBA" : "RGB")}";
        }
        else
        {
            (format, name) = (DdsFormat.Unsupported, $"pixel format flags 0x{pixelFlags:X}");
        }

        if (volume || faces == 0)
            format = DdsFormat.Unsupported;
        if (data.Length < headerLength)
            throw Bad("file ends inside the header");
        var headerBytes = data[..headerLength];

        if (format == DdsFormat.Unsupported)
        {
            var unsupported = new DdsFile(headerBytes, null, data[headerLength..], width, height, mipCount, Math.Max(faces, 1),
                format, $"{name} (not editable)");
            unsupported.warnings.Add($"{name} textures are kept unchanged but cannot be edited");
            return unsupported;
        }

        var faceLevels = new byte[faces][][];
        int offset = headerLength;
        for (int f = 0; f < faces; f++)
        {
            faceLevels[f] = new byte[mipCount][];
            for (int m = 0; m < mipCount; m++)
            {
                int size = LevelBytes(format, bpp, Math.Max(1, width >> m), Math.Max(1, height >> m));
                if ((long)offset + size > data.Length)
                    throw Bad($"file ends inside face {f}, mip {m} ({data.Length} bytes)");
                faceLevels[f][m] = data[offset..(offset + size)];
                offset += size;
            }
        }

        var dds = new DdsFile(headerBytes, faceLevels, data[offset..], width, height, mipCount, faces, format, name, bpp, r, g, b, a);
        if (offset < data.Length)
            dds.warnings.Add($"{data.Length - offset} extra bytes after the last mip level; kept unchanged");
        return dds;
    }

    /// <summary>The DX10 extension header: only single images/cubes of BC1, BC3 and 8-bit RGBA/BGRA are editable.</summary>
    static (DdsFormat, string) ReadDx10(byte[] data, ref int headerLength, ref int faces,
        ref int bpp, ref uint r, ref uint g, ref uint b, ref uint a)
    {
        headerLength = BaseHeaderBytes + Dx10HeaderBytes;
        if (data.Length < headerLength)
            throw Bad("file ends inside the DX10 header");
        uint dxgi = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(128));
        uint misc = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(136));
        uint arraySize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(140));
        faces = (misc & 0x4) != 0 ? 6 : 1;
        if (arraySize > 1)
            return (DdsFormat.Unsupported, $"DX10 texture array (format {dxgi})");
        switch (dxgi)
        {
            case 71 or 72:
                return (DdsFormat.Dxt1, "DXT1 (DX10 header)");
            case 77 or 78:
                return (DdsFormat.Dxt5, "DXT5 (DX10 header)");
            case 28 or 29:
                (bpp, r, g, b, a) = (32, 0x000000FF, 0x0000FF00, 0x00FF0000, 0xFF000000);
                return (DdsFormat.Uncompressed, "32-bit RGBA (DX10 header)");
            case 87 or 91:
                (bpp, r, g, b, a) = (32, 0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000);
                return (DdsFormat.Uncompressed, "32-bit BGRA (DX10 header)");
            default:
                return (DdsFormat.Unsupported, $"DX10 format {dxgi}");
        }
    }

    /// <summary>
    /// Where one mip level of the first face lies in a DDS file, worked out from the file's start alone (148 bytes is
    /// always enough): its byte offset and length, and its size. For fetching one level of a large file.
    /// </summary>
    /// <exception cref="InvalidDataException">The start isn't a DDS header, the format isn't readable, or the file has no such level.</exception>
    public static (long Offset, int Length, int Width, int Height) LevelSpan(byte[] start, int mip)
    {
        var (headerLength, format, bpp, width, height, mipCount) = ReadLayout(start);
        if (mip < 0 || mip >= mipCount)
            throw Bad($"no mip level {mip} (the file has {mipCount})");
        long offset = headerLength;
        for (int m = 0; m < mip; m++)
            offset += LevelBytes(format, bpp, Math.Max(1, width >> m), Math.Max(1, height >> m));
        int w = Math.Max(1, width >> mip), h = Math.Max(1, height >> mip);
        return (offset, LevelBytes(format, bpp, w, h), w, h);
    }

    /// <summary>A one-level DDS file made from a file's start and the bytes of one of its levels (see <see cref="LevelSpan"/>).</summary>
    public static DdsFile FromLevel(byte[] start, int mip, byte[] level)
    {
        var (headerLength, _, _, _, _, _) = ReadLayout(start);
        var (_, length, width, height) = LevelSpan(start, mip);
        if (level.Length != length)
            throw Bad($"level {mip} is {length} bytes, not {level.Length}");
        var data = new byte[headerLength + length];
        Array.Copy(start, data, headerLength);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), (uint)height);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(28), 1);
        level.CopyTo(data, headerLength);
        return Load(data);
    }

    /// <summary>The header's length, readable pixel format, size and mip count (unsupported formats throw).</summary>
    static (int HeaderLength, DdsFormat Format, int Bpp, int Width, int Height, int MipCount) ReadLayout(byte[] start)
    {
        if (start.Length < BaseHeaderBytes || BinaryPrimitives.ReadUInt32LittleEndian(start) != Magic)
            throw Bad("not a DDS file");
        uint U(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(start.AsSpan(offset));
        int height = (int)U(12), width = (int)U(16), mipCount = Math.Max(1, (int)U(28));
        uint pixelFlags = U(80);
        int headerLength = BaseHeaderBytes, faces = 1, bpp = 0;
        uint r = 0, g = 0, b = 0, a = 0;
        DdsFormat format;
        if ((pixelFlags & PixelFourCC) != 0)
            (format, _) = Encoding.ASCII.GetString(start, 84, 4).TrimEnd('\0') switch
            {
                "DXT1" => (DdsFormat.Dxt1, ""),
                "DXT5" => (DdsFormat.Dxt5, ""),
                "DX10" => ReadDx10(start, ref headerLength, ref faces, ref bpp, ref r, ref g, ref b, ref a),
                var other => (DdsFormat.Unsupported, other),
            };
        else if ((pixelFlags & PixelRgb) != 0 && U(88) is 24 or 32)
            (format, bpp) = (DdsFormat.Uncompressed, (int)U(88));
        else
            format = DdsFormat.Unsupported;
        if (format == DdsFormat.Unsupported)
            throw Bad("the pixel format can't be read");
        return (headerLength, format, bpp, width, height, mipCount);
    }

    static int LevelBytes(DdsFormat format, int bpp, int width, int height) => format switch
    {
        DdsFormat.Dxt1 => BlockCompression.LevelBytes(BlockFormat.Dxt1, width, height),
        DdsFormat.Dxt5 => BlockCompression.LevelBytes(BlockFormat.Dxt5, width, height),
        _ => checked((width * bpp + 7) / 8 * height),
    };

    // ---------------------------------------------------------------- writing

    public void Save(string path) => FileSystemUtil.WriteAllBytesAtomic(path, ToBytes());

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        ms.Write(header);
        if (levels != null)
            foreach (var face in levels)
                foreach (var level in face)
                    ms.Write(level);
        ms.Write(extraBytes);
        return ms.ToArray();
    }

    /// <summary>A new texture from <paramref name="image"/>, optionally with a full mip chain made by box filtering.</summary>
    public static DdsFile Create(DdsFormat format, RgbaImage image, bool mipmaps)
    {
        if (format is not (DdsFormat.Dxt1 or DdsFormat.Dxt5 or DdsFormat.Uncompressed))
            throw new ArgumentException($"Cannot create {format} textures.", nameof(format));
        int w = image.Width, h = image.Height;
        int mips = mipmaps ? BitOperations.Log2((uint)Math.Max(w, h)) + 1 : 1;
        // Uncompressed textures are written as A8R8G8B8, the most widely read layout.
        (uint r, uint g, uint b, uint a) = (0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000);

        var header = new byte[BaseHeaderBytes];
        void Put(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(offset), value);
        Put(0, Magic);
        Put(4, HeaderStructSize);
        bool compressed = format != DdsFormat.Uncompressed;
        Put(8, FlagCaps | FlagHeight | FlagWidth | FlagPixelFormat | (compressed ? FlagLinearSize : FlagPitch) | (mips > 1 ? FlagMipMapCount : 0));
        Put(12, (uint)h);
        Put(16, (uint)w);
        Put(20, (uint)(compressed ? LevelBytes(format, 32, w, h) : w * 4));
        Put(28, (uint)mips);
        Put(76, 32); // pixel format struct size
        if (compressed)
        {
            Put(80, PixelFourCC);
            Encoding.ASCII.GetBytes(format == DdsFormat.Dxt1 ? "DXT1" : "DXT5").CopyTo(header, 84);
        }
        else
        {
            Put(80, PixelRgb | PixelAlpha);
            Put(88, 32);
            Put(92, r);
            Put(96, g);
            Put(100, b);
            Put(104, a);
        }
        Put(108, CapsTexture | (mips > 1 ? CapsComplex | CapsMipMap : 0));

        var faceLevels = new[] { new byte[mips][] };
        for (int m = 0; m < mips; m++)
            faceLevels[0][m] = new byte[LevelBytes(format, 32, Math.Max(1, w >> m), Math.Max(1, h >> m))];

        string name = format switch { DdsFormat.Dxt1 => "DXT1", DdsFormat.Dxt5 => "DXT5", _ => "32-bit RGBA" };
        var dds = new DdsFile(header, faceLevels, [], w, h, mips, 1, format, name, 32, r, g, b, a);
        dds.Encode(image);
        dds.RebuildMips(image);
        return dds;
    }

    // ---------------------------------------------------------------- pixels

    /// <exception cref="NotSupportedException">The texture's format cannot be decoded.</exception>
    public RgbaImage Decode(int mip = 0, int face = 0)
    {
        var data = Level(mip, face);
        int w = MipWidth(mip), h = MipHeight(mip);
        return Format switch
        {
            DdsFormat.Dxt1 => BlockCompression.Decode(BlockFormat.Dxt1, data, w, h),
            DdsFormat.Dxt5 => BlockCompression.Decode(BlockFormat.Dxt5, data, w, h),
            _ => UnpackUncompressed(data, w, h),
        };
    }

    /// <summary>
    /// Stores <paramref name="image"/> as the given level. With a region, only that region is rewritten
    /// (for compressed formats: the 4x4 blocks it touches); the rest keeps its original bytes.
    /// </summary>
    public void Encode(RgbaImage image, int mip = 0, int face = 0, PixelRect? region = null)
    {
        var data = Level(mip, face);
        if (image.Width != MipWidth(mip) || image.Height != MipHeight(mip))
            throw new ArgumentException($"Image is {image.Width}x{image.Height}; mip {mip} is {MipWidth(mip)}x{MipHeight(mip)}.");
        switch (Format)
        {
            case DdsFormat.Dxt1:
                BlockCompression.Encode(BlockFormat.Dxt1, image, data, region);
                break;
            case DdsFormat.Dxt5:
                BlockCompression.Encode(BlockFormat.Dxt5, image, data, region);
                break;
            default:
                PackUncompressed(image, data, (region ?? image.Bounds).Intersect(image.Bounds));
                break;
        }
    }

    /// <summary>
    /// Regenerates mip levels 1 and below from <paramref name="level0"/> with a box filter. With a region
    /// (in level-0 pixels), only the parts of each level that depend on it are rewritten.
    /// </summary>
    public void RebuildMips(RgbaImage level0, int face = 0, PixelRect? region = null) =>
        MipChain.Rebuild(level0, MipCount, region, m => Decode(m, face), (m, image, dirty) => Encode(image, m, face, dirty));

    /// <summary>
    /// Brings level 0 of a face in line with <paramref name="image"/>, re-encoding only the blocks (for
    /// uncompressed formats: pixels) that differ, then rebuilds the mips over the changed area. Importing an
    /// unedited export therefore changes nothing. Written pixels of DXT1 textures are made opaque.
    /// Returns the changed area in file coordinates (empty when nothing differs).
    /// </summary>
    public PixelRect EncodeChanged(RgbaImage image, int face = 0)
    {
        if (image.Width != Width || image.Height != Height)
            throw new ArgumentException($"Image is {image.Width}x{image.Height}; the texture is {Width}x{Height}.");
        var current = Decode(0, face);
        int block = Format == DdsFormat.Uncompressed ? 1 : 4;
        var changed = new PixelRect(0, 0, 0, 0);
        var blocks = new List<PixelRect>();
        for (int y = 0; y < Height; y += block)
            for (int x = 0; x < Width; x += block)
            {
                var rect = new PixelRect(x, y, block, block).Intersect(image.Bounds);
                bool same = true;
                for (int row = rect.Y; row < rect.Bottom && same; row++)
                    same = current.Pixels.AsSpan((row * Width + rect.X) * 4, rect.Width * 4)
                        .SequenceEqual(image.Pixels.AsSpan((row * Width + rect.X) * 4, rect.Width * 4));
                if (same)
                    continue;
                blocks.Add(rect);
                changed = changed.Union(rect);
            }
        if (changed.IsEmpty)
            return changed;

        foreach (var rect in blocks)
        {
            if (Format == DdsFormat.Dxt1)
                image.MakeOpaque(rect);
            Encode(image, 0, face, rect);
        }
        RebuildMips(image, face, changed);
        return changed;
    }

    /// <summary>A copy of every level of every face, for undo.</summary>
    public byte[][][] CaptureLevels() =>
        (levels ?? throw new NotSupportedException($"{FormatName} textures cannot be edited."))
            .Select(face => face.Select(level => (byte[])level.Clone()).ToArray()).ToArray();

    public void RestoreLevels(byte[][][] captured)
    {
        for (int f = 0; f < captured.Length; f++)
            for (int m = 0; m < captured[f].Length; m++)
                captured[f][m].CopyTo(levels![f][m], 0);
    }

    /// <summary>The stored bytes of a level-0 region and of everything the mips derive from it, for undo.</summary>
    public sealed class RegionState
    {
        internal List<(int Mip, PixelRect Area)> Chain { get; init; } = [];
        internal List<byte[]> Rows { get; } = [];
        internal int Face { get; init; }
        public long Bytes => Rows.Sum(r => (long)r.Length);
    }

    /// <summary>
    /// Copies the bytes that editing <paramref name="region"/> (level-0 pixels, file coordinates) can change: the
    /// blocks it touches, and at each mip the blocks that depend on it. Much smaller than <see cref="CaptureLevels"/>.
    /// </summary>
    public RegionState CaptureRegion(PixelRect region, int face = 0)
    {
        var state = new RegionState { Chain = RegionChain(region).ToList(), Face = face };
        ForEachRegionRow(state.Chain, face, (data, offset, length) => state.Rows.Add(data.AsSpan(offset, length).ToArray()));
        return state;
    }

    public void RestoreRegion(RegionState state)
    {
        int row = 0;
        ForEachRegionRow(state.Chain, state.Face, (data, offset, _) => state.Rows[row++].CopyTo(data, offset));
    }

    /// <summary>The level areas a region's edit can change: the region (and its mip chain) grown to whole blocks.</summary>
    public IEnumerable<(int Mip, PixelRect Area)> RegionChain(PixelRect region)
    {
        var area = region.Intersect(new PixelRect(0, 0, Width, Height));
        for (int m = 0; m < MipCount && !area.IsEmpty; m++)
        {
            if (m > 0)
                area = area.ToNextMip(MipWidth(m), MipHeight(m));
            yield return (m, Format == DdsFormat.Uncompressed ? area : area.ToBlockBounds(MipWidth(m), MipHeight(m)));
        }
    }

    /// <summary>Visits the stored byte runs (one per block row, or pixel row) of a region chain: (level data, offset, length).</summary>
    void ForEachRegionRow(IEnumerable<(int Mip, PixelRect Area)> chain, int face, Action<byte[], int, int> visit)
    {
        foreach (var (mip, area) in chain)
        {
            var data = Level(mip, face);
            int w = MipWidth(mip);
            if (Format == DdsFormat.Uncompressed)
            {
                int bytesPerPixel = bitsPerPixel / 8, stride = (w * bitsPerPixel + 7) / 8;
                for (int y = area.Y; y < area.Bottom; y++)
                    visit(data, y * stride + area.X * bytesPerPixel, area.Width * bytesPerPixel);
            }
            else
            {
                int blockBytes = Format == DdsFormat.Dxt1 ? 8 : 16, across = BlockCompression.BlocksAcross(w);
                for (int by = area.Y / 4; by < (area.Bottom + 3) / 4; by++)
                    visit(data, (by * across + area.X / 4) * blockBytes, ((area.Right + 3) / 4 - area.X / 4) * blockBytes);
            }
        }
    }

    /// <summary>The stored (possibly compressed) bytes of one level.</summary>
    public ReadOnlyMemory<byte> LevelData(int mip = 0, int face = 0) => Level(mip, face);

    byte[] Level(int mip, int face)
    {
        if (levels == null)
            throw new NotSupportedException($"{FormatName} textures cannot be decoded or edited.");
        if (face < 0 || face >= FaceCount || mip < 0 || mip >= MipCount)
            throw new ArgumentOutOfRangeException(nameof(mip), $"Face {face}, mip {mip} does not exist ({FaceCount} faces, {MipCount} mips).");
        return levels[face][mip];
    }

    RgbaImage UnpackUncompressed(byte[] data, int w, int h)
    {
        var image = new RgbaImage(w, h);
        int bytesPerPixel = bitsPerPixel / 8, stride = (w * bitsPerPixel + 7) / 8;
        Parallel.For(0, h, y =>
        {
            for (int x = 0; x < w; x++)
            {
                int s = y * stride + x * bytesPerPixel;
                uint v = bytesPerPixel == 4 ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(s)) : (uint)(data[s] | data[s + 1] << 8 | data[s + 2] << 16);
                int o = (y * w + x) * 4;
                image.Pixels[o] = Extract(v, redMask);
                image.Pixels[o + 1] = Extract(v, greenMask);
                image.Pixels[o + 2] = Extract(v, blueMask);
                image.Pixels[o + 3] = alphaMask == 0 ? (byte)255 : Extract(v, alphaMask);
            }
        });
        return image;
    }

    void PackUncompressed(RgbaImage image, byte[] data, PixelRect region)
    {
        int bytesPerPixel = bitsPerPixel / 8, stride = (image.Width * bitsPerPixel + 7) / 8;
        for (int y = region.Y; y < region.Bottom; y++)
            for (int x = region.X; x < region.Right; x++)
            {
                int o = (y * image.Width + x) * 4, s = y * stride + x * bytesPerPixel;
                uint v = Insert(image.Pixels[o], redMask) | Insert(image.Pixels[o + 1], greenMask) |
                         Insert(image.Pixels[o + 2], blueMask) | Insert(image.Pixels[o + 3], alphaMask);
                if (bytesPerPixel == 4)
                {
                    // Bits outside the channel masks (e.g. X8R8G8B8 padding) keep their stored value.
                    uint other = ~(redMask | greenMask | blueMask | alphaMask);
                    v |= BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(s)) & other;
                    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(s), v);
                }
                else
                {
                    (data[s], data[s + 1], data[s + 2]) = ((byte)v, (byte)(v >> 8), (byte)(v >> 16));
                }
            }
    }

    /// <summary>A channel scaled to 0-255, whatever the mask's bit width.</summary>
    static byte Extract(uint value, uint mask)
    {
        if (mask == 0)
            return 0;
        int shift = BitOperations.TrailingZeroCount(mask);
        uint max = mask >> shift, v = (value & mask) >> shift;
        return max == 255 ? (byte)v : (byte)((v * 255 + max / 2) / max);
    }

    static uint Insert(byte value, uint mask)
    {
        if (mask == 0)
            return 0;
        int shift = BitOperations.TrailingZeroCount(mask);
        uint max = mask >> shift;
        uint v = max == 255 ? value : ((uint)value * max + 127) / 255;
        return v << shift & mask;
    }

    static InvalidDataException Bad(string message) => new($"Invalid DDS file: {message}.");
}
