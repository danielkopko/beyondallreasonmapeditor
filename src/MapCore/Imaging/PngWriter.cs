using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace MapCore.Imaging;

/// <summary>
/// Writes PNG files: 8-bit RGBA, and 8- or 16-bit greyscale. Each row gets the filter that best predicts it
/// (the usual minimum-sum heuristic), so files stay reasonably small. Optional tEXt chunks carry metadata.
/// </summary>
public static class PngWriter
{
    static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static void WriteRgba(string path, RgbaImage image, IReadOnlyDictionary<string, string>? text = null) =>
        Write(path, image.Width, image.Height, colourType: 6, bitDepth: 8, image.Pixels, text);

    public static void WriteGray8(string path, int width, int height, byte[] samples, IReadOnlyDictionary<string, string>? text = null) =>
        Write(path, width, height, colourType: 0, bitDepth: 8, samples, text);

    public static void WriteGray16(string path, int width, int height, ushort[] samples, IReadOnlyDictionary<string, string>? text = null)
    {
        var bytes = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(i * 2), samples[i]);
        Write(path, width, height, colourType: 0, bitDepth: 16, bytes, text);
    }

    /// <param name="pixels">Packed samples, rows top to bottom; 16-bit samples big-endian.</param>
    static void Write(string path, int width, int height, int colourType, int bitDepth, byte[] pixels, IReadOnlyDictionary<string, string>? text)
    {
        int channels = colourType == 6 ? 4 : 1;
        int pixelBytes = channels * bitDepth / 8, stride = width * pixelBytes;
        if (pixels.Length != (long)stride * height)
            throw new ArgumentException($"Expected {(long)stride * height} bytes of samples, got {pixels.Length}.");

        using var output = new MemoryStream();
        output.Write(Signature);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = (byte)bitDepth;
        ihdr[9] = (byte)colourType;
        WriteChunk(output, "IHDR", ihdr);
        foreach (var (keyword, value) in text ?? new Dictionary<string, string>())
            WriteChunk(output, "tEXt", [.. Encoding.Latin1.GetBytes(keyword), 0, .. Encoding.Latin1.GetBytes(value)]);

        // Filter rows in parallel, then compress them in order.
        var filtered = new byte[height][];
        Parallel.For(0, height, y => filtered[y] = FilterRow(pixels.AsSpan(y * stride, stride), y == 0 ? default : pixels.AsSpan((y - 1) * stride, stride), pixelBytes));
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            foreach (var row in filtered)
                z.Write(row);
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        FileSystemUtil.WriteAllBytesAtomic(path, output.ToArray());
    }

    /// <summary>The filter byte plus the filtered row, choosing the filter with the smallest sum of absolute values.</summary>
    static byte[] FilterRow(ReadOnlySpan<byte> row, ReadOnlySpan<byte> previous, int stride)
    {
        var best = new byte[row.Length + 1];
        var candidate = new byte[row.Length + 1];
        long bestScore = long.MaxValue;
        for (byte filter = 0; filter <= 4; filter++)
        {
            candidate[0] = filter;
            long score = 0;
            for (int i = 0; i < row.Length; i++)
            {
                byte left = i >= stride ? row[i - stride] : (byte)0;
                byte up = previous.IsEmpty ? (byte)0 : previous[i];
                byte upLeft = !previous.IsEmpty && i >= stride ? previous[i - stride] : (byte)0;
                byte value = filter switch
                {
                    0 => row[i],
                    1 => (byte)(row[i] - left),
                    2 => (byte)(row[i] - up),
                    3 => (byte)(row[i] - ((left + up) >> 1)),
                    _ => (byte)(row[i] - PngReader.Paeth(left, up, upLeft)),
                };
                candidate[i + 1] = value;
                score += value < 128 ? value : 256 - value;
            }
            if (score < bestScore)
            {
                bestScore = score;
                (best, candidate) = (candidate, best);
            }
        }
        return best;
    }

    static void WriteChunk(Stream s, string type, byte[] data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        s.Write(word);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(word, Crc32.Compute(typeBytes, data));
        s.Write(word);
    }
}

/// <summary>CRC-32 (IEEE 802.3), as used by PNG and zip.</summary>
public static class Crc32
{
    static readonly uint[] Table = BuildTable();

    static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    public static uint Compute(params ReadOnlySpan<byte[]> parts)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var part in parts)
            foreach (byte b in part)
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }
}
