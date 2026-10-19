using System.Buffers.Binary;
using MapCore.Imaging;
using MapCore.Textures;

namespace MapCore.Tests;

/// <summary>Block-level checks of the DXT codec against the format specification.</summary>
static class DxtTests
{
    [Test]
    public static void DecodesFourColourDxt1BlockPerSpec()
    {
        // c0 = pure red, c1 = pure blue; pixel i uses palette entry i % 4.
        var block = ColourBlock(0xF800, 0x001F, i => i % 4);
        var px = new byte[64];
        Dxt.DecodeDxt1Block(block, px);
        AssertPixel(px, 0, 255, 0, 0, 255);
        AssertPixel(px, 1, 0, 0, 255, 255);
        AssertPixel(px, 2, 170, 0, 85, 255);  // (2*c0 + c1) / 3
        AssertPixel(px, 3, 85, 0, 170, 255);  // (c0 + 2*c1) / 3
        AssertPixel(px, 15, 85, 0, 170, 255);
    }

    [Test]
    public static void DecodesThreeColourDxt1BlockWithTransparency()
    {
        // c0 <= c1 selects 3 colours plus transparent black.
        var block = ColourBlock(0x001F, 0xF800, i => i % 4);
        var px = new byte[64];
        Dxt.DecodeDxt1Block(block, px);
        AssertPixel(px, 2, 127, 0, 127, 255); // (c0 + c1) / 2
        AssertPixel(px, 3, 0, 0, 0, 0);
    }

    [Test]
    public static void DecodesDxt5AlphaInBothModes()
    {
        var px = new byte[64];
        var block = new byte[16];
        ColourBlock(0x001F, 0xF800, i => 3).CopyTo(block, 8); // c0 <= c1, but DXT5 colour is always 4-colour
        WriteAlpha(block, 200, 100, i => i % 8);
        Dxt.DecodeDxt5Block(block, px);
        int[] eightMode = [200, 100, 186, 171, 157, 143, 129, 114];
        for (int i = 0; i < 8; i++)
            Assert.Equal((byte)eightMode[i], px[i * 4 + 3], $"8-value alpha entry {i}");
        AssertPixel(px, 0, 170, 0, 85, 200); // entry 3 = (c0 + 2*c1) / 3 of the 4-colour palette, not transparent

        WriteAlpha(block, 100, 200, i => i % 8);
        Dxt.DecodeDxt5Block(block, px);
        int[] sixMode = [100, 200, 120, 140, 160, 180, 0, 255];
        for (int i = 0; i < 8; i++)
            Assert.Equal((byte)sixMode[i], px[i * 4 + 3], $"6-value alpha entry {i}");
    }

    [Test]
    public static void EncodesSingleColoursClosely()
    {
        var px = new byte[64];
        var block = new byte[8];
        var decoded = new byte[64];
        int worst = 0;
        for (int v = 0; v < 256; v++)
        {
            foreach (var (r, g, b) in new[] { (v, v, v), (v, 255 - v, v / 2), (255 - v, v / 3, v) })
            {
                for (int i = 0; i < 16; i++)
                    (px[i * 4], px[i * 4 + 1], px[i * 4 + 2], px[i * 4 + 3]) = ((byte)r, (byte)g, (byte)b, 255);
                Dxt.EncodeDxt1Block(px, block);
                Dxt.DecodeDxt1Block(block, decoded);
                for (int i = 0; i < 64; i++)
                    worst = Math.Max(worst, Math.Abs(px[i] - decoded[i]));
            }
        }
        Report.Info($"worst single-colour channel error: {worst}");
        Assert.True(worst <= 2, $"single colours should round-trip within 2 levels, worst was {worst}");
    }

    [Test]
    public static void ReproducesOpaqueThreeColourBlocks()
    {
        // Blocks written in 3-colour mode (c0 <= c1) using only entries 0-2, as Acidic Quarry's tiles are.
        var px = new byte[64];
        var block = new byte[8];
        var again = new byte[64];
        var rng = new Random(7);
        int exact = 0, worst = 0;
        const int trials = 5000;
        for (int trial = 0; trial < trials; trial++)
        {
            ushort a = (ushort)rng.Next(0, 65536), b = (ushort)rng.Next(0, 65536);
            var source = ColourBlock(Math.Min(a, b), Math.Max(a, b), i => rng.Next(0, 3));
            Dxt.DecodeDxt1Block(source, px);
            Dxt.EncodeDxt1Block(px, block);
            Dxt.DecodeDxt1Block(block, again);
            if (px.AsSpan().SequenceEqual(again))
                exact++;
            for (int i = 0; i < 64; i++)
                worst = Math.Max(worst, Math.Abs(px[i] - again[i]));
        }
        Report.Info($"random 3-colour blocks: {exact} of {trials} reproduced exactly, worst channel error {worst}");
        // Measured: 4985 of 5000 exact, worst error 4. The misses are blocks whose used entries don't pin down both endpoints.
        Assert.True(exact >= trials * 99 / 100, $"only {exact} of {trials} 3-colour blocks reproduced exactly");
        Assert.True(worst <= 8, $"worst channel error {worst}");
    }

    [Test]
    public static void Dxt1KeepsTransparentPixels()
    {
        var px = new byte[64];
        for (int i = 0; i < 16; i++)
            (px[i * 4], px[i * 4 + 1], px[i * 4 + 2], px[i * 4 + 3]) = ((byte)(i * 16), 100, (byte)(255 - i * 16), (byte)(i % 2 == 0 ? 255 : 0));
        var block = new byte[8];
        var decoded = new byte[64];
        Dxt.EncodeDxt1Block(px, block);
        Dxt.DecodeDxt1Block(block, decoded);
        for (int i = 0; i < 16; i++)
            Assert.Equal(px[i * 4 + 3], decoded[i * 4 + 3], $"alpha of pixel {i}");
    }

    [Test]
    public static void EncoderBeatsBoundingBoxBaselineOnRealTexture()
    {
        // rugged_rock.dds is stored uncompressed, so it is a true original to measure against.
        var original = DdsFile.Load(TestData.MapTexture("rugged_rock.dds")).Decode();
        var ours = RoundTrip(original, (format, image, data, region) => BlockCompression.Encode(format, image, data, region));
        var baseline = RoundTrip(original, BoundingBoxEncode);

        double psnrOurs = ImageMetrics.Psnr(original, ours), psnrBaseline = ImageMetrics.Psnr(original, baseline);
        Report.Info($"rugged_rock 1024x1024 as DXT1: ours {psnrOurs:F2} dB, bounding-box baseline {psnrBaseline:F2} dB");
        Assert.True(psnrOurs > psnrBaseline + 0.5, "encoder should clearly beat the naive baseline");
        // Measured 28.88 dB on this noisy rock texture. A slower ±1-step endpoint search starting from our
        // endpoints gained only 0.15 dB (29.03 dB), so the encoder sits at a local optimum. The floor guards
        // against regressions.
        Assert.True(psnrOurs > 28, "encoder quality on a natural texture");
    }

    static RgbaImage RoundTrip(RgbaImage image, Action<BlockFormat, RgbaImage, Memory<byte>, PixelRect?> encode)
    {
        var data = new byte[BlockCompression.LevelBytes(BlockFormat.Dxt1, image.Width, image.Height)];
        encode(BlockFormat.Dxt1, image, data, null);
        return BlockCompression.Decode(BlockFormat.Dxt1, data, image.Width, image.Height);
    }

    /// <summary>Naive encoder for comparison: per-channel min/max endpoints, nearest palette entry.</summary>
    static void BoundingBoxEncode(BlockFormat format, RgbaImage image, Memory<byte> destination, PixelRect? region)
    {
        int across = image.Width / 4;
        var palette = new int[12];
        for (int by = 0; by < image.Height / 4; by++)
            for (int bx = 0; bx < across; bx++)
            {
                int[] lo = [255, 255, 255], hi = [0, 0, 0];
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        var p = image.GetPixel(bx * 4 + x, by * 4 + y);
                        int[] c = [p.R, p.G, p.B];
                        for (int k = 0; k < 3; k++)
                            (lo[k], hi[k]) = (Math.Min(lo[k], c[k]), Math.Max(hi[k], c[k]));
                    }
                ushort c0 = Pack(hi), c1 = Pack(lo);
                if (c0 == c1)
                    c1 = (ushort)(c0 == 0 ? 0 : c0 - 1);
                var probe = new byte[64];
                Dxt.DecodeDxt1Block(ColourBlock(c0, c1, i => i % 4), probe);
                for (int e = 0; e < 4; e++)
                    for (int k = 0; k < 3; k++)
                        palette[e * 3 + k] = probe[e * 4 + k];
                uint indices = 0;
                for (int i = 0; i < 16; i++)
                {
                    var p = image.GetPixel(bx * 4 + i % 4, by * 4 + i / 4);
                    int best = 0, bestD = int.MaxValue;
                    for (int e = 0; e < 4; e++)
                    {
                        int dr = p.R - palette[e * 3], dg = p.G - palette[e * 3 + 1], db = p.B - palette[e * 3 + 2];
                        int d = dr * dr + dg * dg + db * db;
                        if (d < bestD)
                            (bestD, best) = (d, e);
                    }
                    indices |= (uint)best << (2 * i);
                }
                var block = destination.Span.Slice((by * across + bx) * 8, 8);
                BinaryPrimitives.WriteUInt16LittleEndian(block, c0);
                BinaryPrimitives.WriteUInt16LittleEndian(block[2..], c1);
                BinaryPrimitives.WriteUInt32LittleEndian(block[4..], indices);
            }

        static ushort Pack(int[] c) => (ushort)((c[0] * 31 + 127) / 255 << 11 | (c[1] * 63 + 127) / 255 << 5 | (c[2] * 31 + 127) / 255);
    }

    static byte[] ColourBlock(ushort c0, ushort c1, Func<int, int> indexOf)
    {
        var block = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(block, c0);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(2), c1);
        uint indices = 0;
        for (int i = 0; i < 16; i++)
            indices |= (uint)indexOf(i) << (2 * i);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4), indices);
        return block;
    }

    static void WriteAlpha(byte[] block, byte a0, byte a1, Func<int, int> indexOf)
    {
        block[0] = a0;
        block[1] = a1;
        ulong indices = 0;
        for (int i = 0; i < 16; i++)
            indices |= (ulong)indexOf(i) << (3 * i);
        for (int i = 0; i < 6; i++)
            block[2 + i] = (byte)(indices >> (8 * i));
    }

    static void AssertPixel(byte[] px, int i, int r, int g, int b, int a) =>
        Assert.Equal((r, g, b, a), ((int)px[i * 4], (int)px[i * 4 + 1], (int)px[i * 4 + 2], (int)px[i * 4 + 3]), $"pixel {i}");
}
