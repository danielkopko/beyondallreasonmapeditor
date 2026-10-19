using System.Buffers.Binary;

namespace MapCore.Imaging;

/// <summary>
/// Reads Truevision TGA images (the usual format of model textures in maps): true-colour (16, 24 or 32 bits),
/// grey and colour-mapped, plain or run-length encoded, from either corner. The result is top row first.
/// </summary>
public static class TgaReader
{
    const int HeaderSize = 18;
    const long MaxPixels = 64L * 1024 * 1024;

    public static RgbaImage Read(byte[] data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException("not a TGA image: too short");
        int idLength = data[0], mapType = data[1], type = data[2];
        int mapFirst = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(3)), mapLength = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(5));
        int mapBits = data[7];
        int width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(12)), height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(14));
        int bits = data[16], descriptor = data[17];
        bool rle = type is 9 or 10 or 11;
        int baseType = rle ? type - 8 : type;
        if (baseType is not (1 or 2 or 3))
            throw new InvalidDataException($"not a TGA image this reads (type {type})");
        if (width == 0 || height == 0 || (long)width * height > MaxPixels)
            throw new InvalidDataException($"TGA image size {width}x{height} isn't usable");
        if (baseType == 1 ? bits != 8 || mapType != 1 : baseType == 3 ? bits is not (8 or 16) : bits is not (15 or 16 or 24 or 32))
            throw new InvalidDataException($"TGA image of type {type} with {bits} bits per pixel isn't supported");

        int at = HeaderSize + idLength;
        Rgba[]? palette = null;
        if (mapType == 1)
        {
            int entryBytes = (mapBits + 7) / 8;
            if (at + (long)mapLength * entryBytes > data.Length)
                throw new InvalidDataException("the TGA colour map is cut short");
            palette = new Rgba[mapFirst + mapLength];
            for (int i = 0; i < mapLength; i++)
                palette[mapFirst + i] = Colour(data, at + i * entryBytes, mapBits, alpha: mapBits == 32);
            at += mapLength * entryBytes;
        }

        int bytesPerPixel = (bits + 7) / 8;
        bool hasAlpha = bits == 32 || (bits == 16 && baseType == 2 && (descriptor & 0x0F) > 0) || (bits == 16 && baseType == 3);
        var image = new RgbaImage(width, height);
        bool topFirst = (descriptor & 0x20) != 0, rightToLeft = (descriptor & 0x10) != 0;
        long count = (long)width * height;
        var p = image.Pixels;
        void Put(long n, Rgba c)
        {
            int row = (int)(n / width), col = (int)(n % width);
            int y = topFirst ? row : height - 1 - row, x = rightToLeft ? width - 1 - col : col;
            int o = (y * width + x) * 4;
            (p[o], p[o + 1], p[o + 2], p[o + 3]) = (c.R, c.G, c.B, c.A);
        }
        Rgba Pixel(int offset)
        {
            if (offset + bytesPerPixel > data.Length)
                throw new InvalidDataException("the TGA image is cut short");
            if (baseType == 1)
            {
                int index = data[offset];
                return palette != null && index < palette.Length ? palette[index] : throw new InvalidDataException("a TGA pixel is outside its colour map");
            }
            if (baseType == 3)
                return new Rgba(data[offset], data[offset], data[offset], bits == 16 ? data[offset + 1] : (byte)255);
            return Colour(data, offset, bits, hasAlpha);
        }

        if (!rle)
        {
            if (at + count * bytesPerPixel > data.Length)
                throw new InvalidDataException("the TGA image is cut short");
            for (long n = 0; n < count; n++)
                Put(n, Pixel(at + (int)(n * bytesPerPixel)));
            return image;
        }
        for (long n = 0; n < count;)
        {
            if (at >= data.Length)
                throw new InvalidDataException("the TGA image is cut short");
            int header = data[at++];
            int run = (header & 0x7F) + 1;
            if ((header & 0x80) != 0)
            {
                var c = Pixel(at);
                at += bytesPerPixel;
                for (int i = 0; i < run && n < count; i++)
                    Put(n++, c);
            }
            else
            {
                for (int i = 0; i < run && n < count; i++)
                {
                    Put(n++, Pixel(at));
                    at += bytesPerPixel;
                }
            }
        }
        return image;
    }

    /// <summary>A true-colour pixel: B, G, R(, A) bytes, or 16 bits as A1 R5 G5 B5.</summary>
    static Rgba Colour(byte[] data, int offset, int bits, bool alpha)
    {
        if (bits is 15 or 16)
        {
            int v = data[offset] | data[offset + 1] << 8;
            static byte Scale(int five) => (byte)((five << 3) | (five >> 2));
            return new Rgba(Scale((v >> 10) & 31), Scale((v >> 5) & 31), Scale(v & 31), alpha ? ((v & 0x8000) != 0 ? (byte)255 : (byte)0) : (byte)255);
        }
        return new Rgba(data[offset + 2], data[offset + 1], data[offset], bits == 32 && alpha ? data[offset + 3] : (byte)255);
    }
}
