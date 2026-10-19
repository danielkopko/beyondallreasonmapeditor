using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace MapCore.Imaging;

/// <summary>
/// A decoded PNG. Samples are stored per pixel, <see cref="Channels"/> per pixel: 1 grey, 2 grey + alpha,
/// 3 RGB, 4 RGBA (palette images are expanded to RGB or RGBA). 1-, 2- and 4-bit greys are scaled to 8 bits.
/// </summary>
public sealed class PngImage
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Channels { get; init; }

    /// <summary>8 or 16.</summary>
    public required int BitDepth { get; init; }

    /// <summary>Samples when <see cref="BitDepth"/> is 8.</summary>
    public byte[]? Samples8 { get; init; }

    /// <summary>Samples when <see cref="BitDepth"/> is 16.</summary>
    public ushort[]? Samples16 { get; init; }

    /// <summary>tEXt chunks, keyword to text.</summary>
    public required IReadOnlyDictionary<string, string> Text { get; init; }

    public bool IsGrey => Channels <= 2;

    int Sample16(int index) => BitDepth == 16 ? Samples16![index] : Samples8![index] * 257;

    int Sample8(int index) => BitDepth == 16 ? Samples16![index] >> 8 : Samples8![index];

    /// <summary>The first channel (grey, or red) at 16-bit precision; 8-bit images are scaled up (×257).</summary>
    public ushort[] ToGray16()
    {
        var result = new ushort[Width * Height];
        for (int i = 0; i < result.Length; i++)
            result[i] = (ushort)Sample16(i * Channels);
        return result;
    }

    /// <summary>The first channel (grey, or red) at 8-bit precision.</summary>
    public byte[] ToGray8()
    {
        var result = new byte[Width * Height];
        for (int i = 0; i < result.Length; i++)
            result[i] = (byte)Sample8(i * Channels);
        return result;
    }

    /// <summary>As 8-bit RGBA; grey becomes RGB, a missing alpha becomes 255.</summary>
    public RgbaImage ToRgba()
    {
        var image = new RgbaImage(Width, Height);
        var p = image.Pixels;
        Parallel.For(0, Height, y =>
        {
            for (int x = 0; x < Width; x++)
            {
                int s = (y * Width + x) * Channels, o = (y * Width + x) * 4;
                byte r = (byte)Sample8(s);
                (byte g, byte b) = Channels >= 3 ? ((byte)Sample8(s + 1), (byte)Sample8(s + 2)) : (r, r);
                byte a = Channels is 2 or 4 ? (byte)Sample8(s + Channels - 1) : (byte)255;
                (p[o], p[o + 1], p[o + 2], p[o + 3]) = (r, g, b, a);
            }
        });
        return image;
    }
}

/// <summary>
/// Reads PNG files: every colour type (grey, RGB, palette, with or without alpha) and bit depth, all five row
/// filters, Adam7 interlacing, tRNS transparency, and tEXt chunks. Chunk CRCs are checked.
/// </summary>
public static class PngReader
{
    static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    static readonly (int X, int Y, int DX, int DY)[] Adam7 =
        [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];

    public static PngImage Read(string path) => Read(File.ReadAllBytes(path));

    /// <exception cref="InvalidDataException">The data is not a PNG this reader can decode.</exception>
    public static PngImage Read(byte[] data)
    {
        if (data.Length < 8 || !data.AsSpan(0, 8).SequenceEqual(Signature))
            throw Bad("not a PNG file");

        int width = 0, height = 0, bitDepth = 0, colourType = -1, interlace = 0;
        byte[]? palette = null, transparency = null;
        var text = new Dictionary<string, string>();
        using var compressed = new MemoryStream();
        int pos = 8;
        bool ended = false;
        while (pos + 12 <= data.Length && !ended)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(pos));
            if (length < 0 || pos + 12 + length > data.Length)
                throw Bad("chunk runs past the end of the file");
            var type = Encoding.ASCII.GetString(data, pos + 4, 4);
            var body = data.AsSpan(pos + 8, length);
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 8 + length));
            if (Crc32.Compute(data[(pos + 4)..(pos + 8 + length)]) != crc)
                throw Bad($"{type} chunk is damaged (CRC mismatch)");

            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(body);
                    height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                    (bitDepth, colourType, interlace) = (body[8], body[9], body[12]);
                    if (body[10] != 0 || body[11] != 0)
                        throw Bad("unknown compression or filter method");
                    break;
                case "PLTE":
                    palette = body.ToArray();
                    break;
                case "tRNS":
                    transparency = body.ToArray();
                    break;
                case "tEXt":
                    int zero = body.IndexOf((byte)0);
                    if (zero > 0)
                        text[Encoding.Latin1.GetString(body[..zero])] = Encoding.Latin1.GetString(body[(zero + 1)..]);
                    break;
                case "IDAT":
                    compressed.Write(body);
                    break;
                case "IEND":
                    ended = true;
                    break;
            }
            pos += 12 + length;
        }

        if (width <= 0 || height <= 0 || width > 1 << 16 || height > 1 << 16)
            throw Bad($"implausible size {width}x{height}");
        int fileChannels = colourType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw Bad($"unknown colour type {colourType}") };
        bool validDepth = colourType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            _ => bitDepth is 8 or 16,
        };
        if (!validDepth)
            throw Bad($"bit depth {bitDepth} is not allowed for colour type {colourType}");
        if (colourType == 3 && palette == null)
            throw Bad("palette image without a PLTE chunk");
        if (interlace > 1)
            throw Bad($"unknown interlace method {interlace}");

        compressed.Position = 0;
        using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        inflater.CopyTo(raw);
        var filtered = raw.GetBuffer();
        int available = (int)raw.Length;

        // Output layout: palette expands to RGB(A); a tRNS colour key adds an alpha channel.
        bool keyAlpha = transparency != null && colourType is 0 or 2;
        bool paletteAlpha = colourType == 3 && transparency != null;
        int outChannels = colourType == 3 ? (paletteAlpha ? 4 : 3) : fileChannels + (keyAlpha ? 1 : 0);
        int outDepth = bitDepth == 16 ? 16 : 8;
        var samples8 = outDepth == 8 ? new byte[(long)width * height * outChannels] : null;
        var samples16 = outDepth == 16 ? new ushort[(long)width * height * outChannels] : null;

        int bitsPerPixel = fileChannels * bitDepth;
        int filterStride = Math.Max(1, bitsPerPixel / 8);
        int offset = 0;
        var passes = interlace == 1 ? Adam7 : [(0, 0, 1, 1)];
        foreach (var (px, py, dx, dy) in passes)
        {
            int passWidth = (width - px + dx - 1) / dx, passHeight = (height - py + dy - 1) / dy;
            if (passWidth <= 0 || passHeight <= 0)
                continue;
            int rowBytes = (passWidth * bitsPerPixel + 7) / 8;
            var previous = new byte[rowBytes];
            var row = new byte[rowBytes];
            for (int r = 0; r < passHeight; r++)
            {
                if (offset + 1 + rowBytes > available)
                    throw Bad("image data ends early");
                int filter = filtered[offset];
                Array.Copy(filtered, offset + 1, row, 0, rowBytes);
                offset += 1 + rowBytes;
                Unfilter(filter, row, previous, filterStride);

                int y = py + r * dy;
                for (int c = 0; c < passWidth; c++)
                {
                    int x = px + c * dx;
                    long o = ((long)y * width + x) * outChannels;
                    for (int ch = 0; ch < fileChannels; ch++)
                    {
                        int v = ReadSample(row, c * fileChannels + ch, bitDepth);
                        if (colourType == 3)
                        {
                            if (v * 3 + 2 >= palette!.Length)
                                throw Bad($"palette index {v} is out of range");
                            samples8![o] = palette[v * 3];
                            samples8[o + 1] = palette[v * 3 + 1];
                            samples8[o + 2] = palette[v * 3 + 2];
                            if (paletteAlpha)
                                samples8[o + 3] = v < transparency!.Length ? transparency[v] : (byte)255;
                        }
                        else if (outDepth == 16)
                            samples16![o + ch] = (ushort)v;
                        else
                            samples8![o + ch] = (byte)(bitDepth < 8 ? v * 255 / ((1 << bitDepth) - 1) : v);
                    }
                    if (keyAlpha)
                    {
                        bool transparent = true;
                        for (int ch = 0; ch < fileChannels; ch++)
                            transparent &= ReadSample(row, c * fileChannels + ch, bitDepth) == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(ch * 2));
                        if (outDepth == 16)
                            samples16![o + fileChannels] = transparent ? (ushort)0 : ushort.MaxValue;
                        else
                            samples8![o + fileChannels] = transparent ? (byte)0 : (byte)255;
                    }
                }
                (previous, row) = (row, previous);
            }
        }

        return new PngImage
        {
            Width = width, Height = height, Channels = outChannels, BitDepth = outDepth,
            Samples8 = samples8, Samples16 = samples16, Text = text,
        };
    }

    static int ReadSample(byte[] row, int index, int bitDepth) => bitDepth switch
    {
        16 => row[index * 2] << 8 | row[index * 2 + 1],
        8 => row[index],
        _ => row[index * bitDepth / 8] >> (8 - bitDepth - index * bitDepth % 8) & ((1 << bitDepth) - 1),
    };

    static void Unfilter(int filter, byte[] row, byte[] previous, int stride)
    {
        switch (filter)
        {
            case 0:
                return;
            case 1:
                for (int i = stride; i < row.Length; i++)
                    row[i] += row[i - stride];
                return;
            case 2:
                for (int i = 0; i < row.Length; i++)
                    row[i] += previous[i];
                return;
            case 3:
                for (int i = 0; i < row.Length; i++)
                    row[i] += (byte)(((i >= stride ? row[i - stride] : 0) + previous[i]) >> 1);
                return;
            case 4:
                for (int i = 0; i < row.Length; i++)
                    row[i] += Paeth(i >= stride ? row[i - stride] : (byte)0, previous[i], i >= stride ? previous[i - stride] : (byte)0);
                return;
            default:
                throw Bad($"unknown row filter {filter}");
        }
    }

    internal static byte Paeth(byte a, byte b, byte c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    static InvalidDataException Bad(string message) => new($"Invalid PNG: {message}.");
}
