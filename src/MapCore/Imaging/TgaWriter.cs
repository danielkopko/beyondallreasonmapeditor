namespace MapCore.Imaging;

/// <summary>
/// Writes Truevision TGA images as model textures are usually stored: uncompressed 32-bit BGRA, rows from the
/// bottom up (the format every loader reads, the engine's included). <see cref="TgaReader"/> reads them back.
/// </summary>
public static class TgaWriter
{
    const int HeaderSize = 18;

    public static byte[] Write(RgbaImage image)
    {
        if (image.Width > ushort.MaxValue || image.Height > ushort.MaxValue)
            throw new ArgumentException($"A TGA image can't be {image.Width}x{image.Height}.", nameof(image));
        var data = new byte[HeaderSize + image.Width * image.Height * 4];
        data[2] = 2; // uncompressed true colour
        data[12] = (byte)image.Width;
        data[13] = (byte)(image.Width >> 8);
        data[14] = (byte)image.Height;
        data[15] = (byte)(image.Height >> 8);
        data[16] = 32;
        data[17] = 8; // 8 alpha bits; origin bottom-left
        var p = image.Pixels;
        int at = HeaderSize;
        for (int y = image.Height - 1; y >= 0; y--)
            for (int x = 0; x < image.Width; x++)
            {
                int o = (y * image.Width + x) * 4;
                data[at++] = p[o + 2];
                data[at++] = p[o + 1];
                data[at++] = p[o];
                data[at++] = p[o + 3];
            }
        return data;
    }
}
