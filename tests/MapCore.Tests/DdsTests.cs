using System.Diagnostics;
using System.Text;
using MapCore.Imaging;
using MapCore.Textures;

namespace MapCore.Tests;

static class DdsTests
{
    static readonly (string File, DdsFormat Format, int Size, int Mips, int Faces)[] MapTextures =
    [
        ("moltenmetal.dds", DdsFormat.Dxt5, 512, 10, 1),
        ("normalmap.dds", DdsFormat.Dxt1, 6144, 13, 1),
        ("raw_iron_metal_spots.dds", DdsFormat.Dxt5, 1024, 11, 1),
        ("rock_holes.dds", DdsFormat.Dxt5, 1024, 11, 1),
        ("rough_sand.dds", DdsFormat.Uncompressed, 1024, 1, 1),
        ("rugged_rock.dds", DdsFormat.Uncompressed, 1024, 1, 1),
        ("sky.dds", DdsFormat.Dxt1, 2048, 1, 6),
        ("specular.dds", DdsFormat.Dxt1, 3072, 1, 1),
        ("splat_distr.dds", DdsFormat.Dxt5, 2048, 12, 1),
    ];

    [Test]
    public static void LoadsEveryMapTexture()
    {
        foreach (var t in MapTextures)
        {
            var dds = DdsFile.Load(TestData.MapTexture(t.File));
            Assert.Equal((t.Format, t.Size, t.Size, t.Mips, t.Faces), (dds.Format, dds.Width, dds.Height, dds.MipCount, dds.FaceCount), t.File);
            Assert.Empty(dds.Warnings.ToList(), $"{t.File} warnings");
        }
    }

    [Test]
    public static void UnchangedSaveIsByteIdentical()
    {
        foreach (var t in MapTextures)
        {
            var original = File.ReadAllBytes(TestData.MapTexture(t.File));
            Assert.True(DdsFile.Load(original).ToBytes().AsSpan().SequenceEqual(original), $"{t.File} differs after load/save");
        }
    }

    [Test]
    public static void HeadersMatchPythonReference()
    {
        foreach (var t in MapTextures)
        {
            var path = TestData.MapTexture(t.File);
            var py = PythonReference.DescribeDds(path);
            var dds = DdsFile.Load(path);
            Assert.Equal(py.GetProperty("width").GetInt32(), dds.Width, $"{t.File} width");
            Assert.Equal(py.GetProperty("height").GetInt32(), dds.Height, $"{t.File} height");
            Assert.Equal(py.GetProperty("mipCount").GetInt32(), dds.MipCount, $"{t.File} mips");
            Assert.Equal(py.GetProperty("faces").GetInt32(), dds.FaceCount, $"{t.File} faces");
            Assert.Equal(py.GetProperty("expectedSize").GetInt64(), py.GetProperty("actualSize").GetInt64(), $"{t.File} python size check");
            for (int m = 0; m < dds.MipCount; m++)
                Assert.Equal(py.GetProperty("levelBytes")[m].GetInt32(), dds.LevelData(m).Length, $"{t.File} mip {m} bytes");
        }
    }

    [Test]
    public static void DecodedPixelsMatchPythonReference()
    {
        const int blocks = 2048;
        foreach (var file in new[] { "specular.dds", "splat_distr.dds", "rugged_rock.dds", "rough_sand.dds", "sky.dds" })
        {
            var path = TestData.MapTexture(file);
            var expected = PythonReference.DecodeDdsBlocks(path, blocks);
            var actual = BlocksOf(DdsFile.Load(path).Decode(), blocks);
            int firstDiff = expected.AsSpan().CommonPrefixLength(actual);
            Assert.True(firstDiff == expected.Length && actual.Length == expected.Length,
                $"{file}: decoded pixels differ from python at byte {firstDiff} (block {firstDiff / 64}, pixel {firstDiff % 64 / 4})");
        }
    }

    [Test]
    public static void ReencodingKeepsQualityAndSpeed()
    {
        // The "original's quality": blocks that already exist in DXT form. A perfect encoder would
        // reproduce them exactly (infinite PSNR); a good one comes close.
        var normal = DdsFile.Load(TestData.MapTexture("normalmap.dds"));
        var sw = Stopwatch.StartNew();
        var decoded = normal.Decode();
        long decodeMs = sw.ElapsedMilliseconds;
        var data = new byte[normal.LevelData().Length];
        sw.Restart();
        BlockCompression.Encode(BlockFormat.Dxt1, decoded, data);
        long encodeMs = sw.ElapsedMilliseconds;
        double normalPsnr = ImageMetrics.Psnr(decoded, BlockCompression.Decode(BlockFormat.Dxt1, data, decoded.Width, decoded.Height));
        Report.Info($"normalmap 6144x6144 DXT1: decode {decodeMs} ms, full re-encode {encodeMs} ms on {Environment.ProcessorCount} threads, PSNR {normalPsnr:F2} dB");

        var splat = DdsFile.Load(TestData.MapTexture("splat_distr.dds"));
        var splatDecoded = splat.Decode();
        var splatData = new byte[splat.LevelData().Length];
        BlockCompression.Encode(BlockFormat.Dxt5, splatDecoded, splatData);
        double splatPsnr = ImageMetrics.Psnr(splatDecoded, BlockCompression.Decode(BlockFormat.Dxt5, splatData, 2048, 2048), includeAlpha: true);
        Report.Info($"splat_distr 2048x2048 DXT5: PSNR incl. alpha {splatPsnr:F2} dB");

        Assert.True(normalPsnr >= 40, $"normal map re-encode PSNR {normalPsnr:F2} dB is below 40 dB");
        Assert.True(splatPsnr >= 40, $"splat re-encode PSNR {splatPsnr:F2} dB is below 40 dB");
        Assert.True(encodeMs < 30_000, $"full 6144x6144 encode took {encodeMs} ms");
    }

    [Test]
    public static void RegionEditRewritesOnlyTouchedBlocks()
    {
        var path = TestData.MapTexture("splat_distr.dds");
        var original = DdsFile.Load(path);
        var dds = DdsFile.Load(path);
        var region = new PixelRect(101, 203, 80, 60);
        var color = new Rgba(10, 200, 30, 255);

        var level0 = dds.Decode();
        level0.Fill(region, color);
        dds.Encode(level0, 0, 0, region);
        dds.RebuildMips(level0, 0, region);

        var dirty = region;
        for (int m = 0; m < dds.MipCount; m++)
        {
            if (m > 0)
                dirty = dirty.ToNextMip(dds.MipWidth(m), dds.MipHeight(m));
            var blocks = dirty.ToBlockBounds(dds.MipWidth(m), dds.MipHeight(m));
            var (outside, inside) = CompareBlocks(original.LevelData(m).Span, dds.LevelData(m).Span, dds.MipWidth(m), blocks);
            Assert.Equal(0, outside, $"mip {m}: changed blocks outside the edited area");
            if (m == 0)
                Assert.True(inside > 0, "mip 0: the edited area should change");
        }

        var after = dds.Decode();
        var painted = after.GetPixel(region.X + 40, region.Y + 30);
        Assert.True(Math.Abs(painted.R - color.R) <= 4 && Math.Abs(painted.G - color.G) <= 4 && Math.Abs(painted.B - color.B) <= 4,
            $"painted pixel decodes as {painted}");
        Assert.Equal(color.A, painted.A, "painted alpha");
    }

    [Test]
    public static void UncompressedRegionEditIsLossless()
    {
        var path = TestData.MapTexture("rugged_rock.dds");
        var original = File.ReadAllBytes(path);
        var dds = DdsFile.Load(original);
        var image = dds.Decode();
        var region = new PixelRect(10, 20, 30, 40);
        for (int y = region.Y; y < region.Bottom; y++)
            for (int x = region.X; x < region.Right; x++)
                image.SetPixel(x, y, new Rgba((byte)x, (byte)y, (byte)(x ^ y), (byte)(x + y)));
        dds.Encode(image, region: region);

        var reloaded = DdsFile.Load(dds.ToBytes()).Decode();
        for (int y = region.Y; y < region.Bottom; y++)
            for (int x = region.X; x < region.Right; x++)
                Assert.Equal(new Rgba((byte)x, (byte)y, (byte)(x ^ y), (byte)(x + y)), reloaded.GetPixel(x, y), $"pixel {x},{y}");
        var untouched = DdsFile.Load(original).Decode();
        Assert.Equal(untouched.GetPixel(500, 500), reloaded.GetPixel(500, 500), "pixel outside the edit");
    }

    [Test]
    public static void CreatedTexturesAreReadableByBothParsers()
    {
        var image = new RgbaImage(256, 128);
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 256; x++)
                image.SetPixel(x, y, new Rgba((byte)x, (byte)(y * 2), (byte)(255 - x), (byte)(x / 2 + y)));

        // DXT1 treats alpha below 128 as transparent black, so it gets an opaque copy.
        var opaque = new RgbaImage(256, 128);
        image.Pixels.CopyTo(opaque.Pixels, 0);
        for (int i = 3; i < opaque.Pixels.Length; i += 4)
            opaque.Pixels[i] = 255;

        foreach (var format in new[] { DdsFormat.Dxt1, DdsFormat.Dxt5, DdsFormat.Uncompressed })
        {
            var source = format == DdsFormat.Dxt1 ? opaque : image;
            var path = Path.Combine(TestData.NewWorkDir("dds-create"), $"created_{format}.dds");
            DdsFile.Create(format, source, mipmaps: true).Save(path);

            var dds = DdsFile.Load(path);
            Assert.Equal((format, 256, 128, 9), (dds.Format, dds.Width, dds.Height, dds.MipCount), $"{format} layout");
            Assert.Equal(1, dds.MipWidth(8), $"{format} last mip width");
            var py = PythonReference.DescribeDds(path);
            Assert.Equal(py.GetProperty("expectedSize").GetInt64(), py.GetProperty("actualSize").GetInt64(), $"{format} size per python");
            Assert.Equal(9, py.GetProperty("mipCount").GetInt32(), $"{format} mips per python");

            double psnr = ImageMetrics.Psnr(source, dds.Decode(), includeAlpha: true);
            Report.Info($"created {format}: PSNR {psnr:F2} dB");
            Assert.True(psnr > 35, $"{format} created texture quality");
            if (format == DdsFormat.Uncompressed)
                Assert.True(double.IsPositiveInfinity(psnr), "uncompressed texture should be lossless");

            var pyBlocks = PythonReference.DecodeDdsBlocks(path, 64);
            Assert.True(pyBlocks.AsSpan().SequenceEqual(BlocksOf(dds.Decode(), 64)), $"{format}: python decodes the created texture identically");
        }
    }

    [Test]
    public static void UnsupportedFormatsAreKeptButNotEditable()
    {
        var bytes = DdsFile.Create(DdsFormat.Dxt1, new RgbaImage(8, 8), mipmaps: false).ToBytes();
        Encoding.ASCII.GetBytes("DXT3").CopyTo(bytes, 84);
        var dds = DdsFile.Load(bytes);
        Assert.Equal(DdsFormat.Unsupported, dds.Format, "format");
        Assert.True(dds.ToBytes().AsSpan().SequenceEqual(bytes), "unsupported texture kept byte for byte");
        Assert.Throws<NotSupportedException>(() => dds.Decode(), "decoding DXT3");
    }

    [Test]
    public static void RejectsDamagedFiles()
    {
        Assert.Throws<InvalidDataException>(() => DdsFile.Load(Encoding.ASCII.GetBytes("not a texture at all, just some text....")), "not a DDS");
        var truncated = File.ReadAllBytes(TestData.MapTexture("specular.dds"))[..5000];
        Assert.Throws<InvalidDataException>(() => DdsFile.Load(truncated), "truncated DDS");
    }

    /// <summary>The first <paramref name="count"/> 4x4 blocks as RGBA, 64 bytes each, in the python tool's order.</summary>
    static byte[] BlocksOf(RgbaImage image, int count)
    {
        int across = image.Width / 4;
        var result = new byte[count * 64];
        for (int k = 0; k < count; k++)
            for (int i = 0; i < 16; i++)
            {
                int x = Math.Min(k % across * 4 + i % 4, image.Width - 1), y = Math.Min(k / across * 4 + i / 4, image.Height - 1);
                Array.Copy(image.Pixels, (y * image.Width + x) * 4, result, k * 64 + i * 4, 4);
            }
        return result;
    }

    /// <summary>Counts changed 16-byte (DXT5) blocks outside and inside <paramref name="area"/> (pixel coordinates).</summary>
    static (int Outside, int Inside) CompareBlocks(ReadOnlySpan<byte> before, ReadOnlySpan<byte> after, int width, PixelRect area)
    {
        int across = BlockCompression.BlocksAcross(width), outside = 0, inside = 0;
        for (int b = 0; b < before.Length / 16; b++)
        {
            if (before.Slice(b * 16, 16).SequenceEqual(after.Slice(b * 16, 16)))
                continue;
            int px = b % across * 4, py = b / across * 4;
            bool inArea = px >= area.X && px < area.Right && py >= area.Y && py < area.Bottom;
            if (inArea)
                inside++;
            else
                outside++;
        }
        return (outside, inside);
    }
}
