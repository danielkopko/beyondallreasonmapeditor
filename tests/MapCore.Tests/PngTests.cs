using System.Buffers.Binary;
using System.Diagnostics;
using MapCore.Imaging;

namespace MapCore.Tests;

static class PngTests
{
    [Test]
    public static void ReadsPngsFromAnIndependentEncoder()
    {
        var dir = TestData.NewWorkDir("png-ref");
        var psi = new ProcessStartInfo("python") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-I", Path.Combine(TestData.RepoRoot, "tools", "png_reference.py"), dir })
            psi.ArgumentList.Add(arg);
        using (var p = Process.Start(psi)!)
        {
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            Assert.Equal(0, p.ExitCode, $"png_reference.py: {err}");
        }

        foreach (var name in new[] { "rgb8_filters", "palette4_trns", "gray2", "gray8_key", "rgba8_adam7" })
        {
            var image = PngReader.Read(Path.Combine(dir, name + ".png")).ToRgba();
            var expected = File.ReadAllBytes(Path.Combine(dir, name + ".rgba"));
            int diff = image.Pixels.AsSpan().CommonPrefixLength(expected);
            Assert.True(diff == expected.Length && image.Pixels.Length == expected.Length, $"{name}: first difference at byte {diff}");
        }

        var gray = PngReader.Read(Path.Combine(dir, "gray16.png"));
        var expected16 = File.ReadAllBytes(Path.Combine(dir, "gray16.gray16"));
        var values = gray.ToGray16();
        Assert.Equal(16, gray.BitDepth, "16-bit kept");
        for (int i = 0; i < values.Length; i++)
            Assert.Equal(BinaryPrimitives.ReadUInt16BigEndian(expected16.AsSpan(i * 2)), values[i], $"gray16 sample {i}");
    }

    [Test]
    public static void WriterRoundTripsEveryFormat()
    {
        var dir = TestData.NewWorkDir("png-rt");
        var rgba = new RgbaImage(37, 23);
        new Random(3).NextBytes(rgba.Pixels);
        PngWriter.WriteRgba(Path.Combine(dir, "a.png"), rgba, new Dictionary<string, string> { ["Key"] = "value 1 2" });
        var back = PngReader.Read(Path.Combine(dir, "a.png"));
        Assert.True(back.ToRgba().Pixels.AsSpan().SequenceEqual(rgba.Pixels), "RGBA round trip");
        Assert.Equal("value 1 2", back.Text["Key"], "text chunk");

        var g8 = new byte[31 * 17];
        new Random(4).NextBytes(g8);
        PngWriter.WriteGray8(Path.Combine(dir, "b.png"), 31, 17, g8);
        Assert.True(PngReader.Read(Path.Combine(dir, "b.png")).ToGray8().AsSpan().SequenceEqual(g8), "grey 8 round trip");

        var g16 = Enumerable.Range(0, 29 * 19).Select(i => (ushort)(i * 2654435761 >> 16)).ToArray();
        PngWriter.WriteGray16(Path.Combine(dir, "c.png"), 29, 19, g16);
        Assert.True(PngReader.Read(Path.Combine(dir, "c.png")).ToGray16().AsSpan().SequenceEqual(g16), "grey 16 round trip");
    }

    [Test]
    public static void RejectsDamagedPngs()
    {
        var dir = TestData.NewWorkDir("png-bad");
        var path = Path.Combine(dir, "ok.png");
        PngWriter.WriteGray8(path, 4, 4, new byte[16]);
        var bytes = File.ReadAllBytes(path);
        var corrupt = (byte[])bytes.Clone();
        corrupt[20] ^= 0xFF; // inside IHDR, so its CRC no longer matches
        Assert.Throws<InvalidDataException>(() => PngReader.Read(corrupt), "CRC mismatch");
        Assert.Throws<InvalidDataException>(() => PngReader.Read(bytes[..30]), "truncated");
        Assert.Throws<InvalidDataException>(() => PngReader.Read(System.Text.Encoding.ASCII.GetBytes("GIF89a....")), "not a PNG");
    }
}
