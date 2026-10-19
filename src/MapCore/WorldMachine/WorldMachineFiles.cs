using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml;
using MapCore.Imaging;

namespace MapCore.WorldMachine;

/// <summary>
/// What the editor writes into a World Machine exchange folder (bar_map.json): the map's size and the height range its
/// heights.png spans, so what World Machine sends back can be mapped onto the map again. One game unit is one metre.
/// </summary>
public sealed record WorldMachineInfo
{
    public required string Map { get; init; }
    /// <summary>The heightmap's vertices across and down (64 per map unit, plus one).</summary>
    public required int HeightsWidth { get; init; }
    public required int HeightsHeight { get; init; }
    /// <summary>The map's size in game units (metres in World Machine).</summary>
    public required double WorldWidth { get; init; }
    public required double WorldHeight { get; init; }
    /// <summary>The heights black and white stand for in heights.png (World Machine has no heights below 0: black is the lowest).</summary>
    public required double MinHeight { get; init; }
    public required double MaxHeight { get; init; }
    public required int TextureWidth { get; init; }
    public required int TextureHeight { get; init; }
    public int SplatWidth { get; init; }
    public int SplatHeight { get; init; }

    /// <summary>The altitude range World Machine should give heights.png: 0 to this, in metres.</summary>
    public double AltitudeRange => MaxHeight - MinHeight;

    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public void Save(string folder) => File.WriteAllText(Path.Combine(folder, WorldMachineFiles.InfoFile), JsonSerializer.Serialize(this, Options), new UTF8Encoding(false));

    /// <summary>The info written when the map was sent, or null if the folder has none.</summary>
    public static WorldMachineInfo? Load(string folder)
    {
        var path = Path.Combine(folder, WorldMachineFiles.InfoFile);
        return File.Exists(path) ? JsonSerializer.Deserialize<WorldMachineInfo>(File.ReadAllText(path)) : null;
    }
}

/// <summary>
/// The files a map and World Machine trade through an exchange folder, and reading what World Machine writes: its
/// heightfield outputs (16-bit PNG, RAW16, 32-bit float RAW, 8-bit PNG), its bitmaps (PNG, BMP), resampled to the map's
/// own sizes. Images have north at the top, as the editor's own exports do.
/// </summary>
public static class WorldMachineFiles
{
    public const string InfoFile = "bar_map.json", Heights = "heights.png", HeightsRaw = "heights.r16", Texture = "texture.png",
        WaterMask = "mask_water.png", SlopeMask = "mask_slope.png", CliffMask = "mask_cliffs.png", KeepMask = "mask_keep.png",
        ReadMe = "README.txt", Script = "build.xml";

    /// <summary>The names World Machine's outputs are given in the folder, for each layer brought back (the first found is used).</summary>
    public static readonly IReadOnlyList<(string Layer, string[] Names)> Outputs =
    [
        ("heights", ["wm_heights.png", "wm_heights.r16", "wm_heights.raw", "wm_heights.r32"]),
        ("texture", ["wm_texture.png", "wm_texture.bmp"]),
        ("splat", ["wm_splat.png"]),
        ("grass", ["wm_grass.png", "wm_grass.r16", "wm_grass.raw"]),
    ];

    /// <summary>Whether a file name is one of World Machine's outputs (what a watch on the folder reacts to).</summary>
    public static bool IsOutput(string fileName) => Outputs.Any(o => o.Names.Contains(Path.GetFileName(fileName), StringComparer.OrdinalIgnoreCase));

    /// <summary>The output World Machine wrote for a layer, if any.</summary>
    public static string? Output(string folder, string layer) =>
        Outputs.First(o => o.Layer == layer).Names.Select(n => Path.Combine(folder, n)).FirstOrDefault(File.Exists);

    /// <summary>A heightfield or mask as read: values 0 to 1 (or heights in metres, when <see cref="Absolute"/>), row by row from the north.</summary>
    public sealed record Field(float[] Values, int Width, int Height, bool Absolute);

    /// <summary>
    /// Reads a heightfield or mask: PNG (16- or 8-bit, the first channel), RAW16 (.r16/.raw: little-endian, square) or
    /// 32-bit float RAW (.r32: square; values above 1.5 are taken as metres, else 0 to 1).
    /// </summary>
    /// <exception cref="InvalidDataException">Not a format it reads, or a RAW file that isn't square.</exception>
    public static Field ReadField(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".png")
        {
            var png = PngReader.Read(path);
            var grey = png.ToGray16();
            return new Field([.. grey.Select(v => v / 65535f)], png.Width, png.Height, false);
        }
        var bytes = File.ReadAllBytes(path);
        int size = ext == ".r32" ? 4 : 2;
        int n = (int)Math.Round(Math.Sqrt(bytes.Length / (double)size));
        if (ext is not (".r16" or ".raw" or ".r32") || n * n * size != bytes.Length)
            throw new InvalidDataException(ext is ".r16" or ".raw" or ".r32"
                ? $"{Path.GetFileName(path)} isn't a square RAW heightfield ({bytes.Length:N0} bytes)."
                : $"{Path.GetFileName(path)}: World Machine heightfields are read as PNG (16-bit), RAW16 (.r16, .raw) or 32-bit float RAW (.r32).");
        var values = new float[n * n];
        if (size == 2)
            for (int i = 0; i < values.Length; i++)
                values[i] = BitConverter.ToUInt16(bytes, i * 2) / 65535f;
        else
            for (int i = 0; i < values.Length; i++)
                values[i] = BitConverter.ToSingle(bytes, i * 4);
        bool absolute = size == 4 && values.Max() > 1.5f;
        return new Field(values, n, n, absolute);
    }

    /// <summary>Reads a bitmap: PNG (any kind) or an uncompressed BMP (24 or 32 bits).</summary>
    public static RgbaImage ReadImage(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => PngReader.Read(path).ToRgba(),
        ".bmp" => ReadBmp(File.ReadAllBytes(path)),
        _ => throw new InvalidDataException($"{Path.GetFileName(path)}: World Machine bitmaps are read as PNG or BMP."),
    };

    static RgbaImage ReadBmp(byte[] data)
    {
        if (data.Length < 54 || data[0] != 'B' || data[1] != 'M')
            throw new InvalidDataException("not a BMP file");
        int offset = BitConverter.ToInt32(data, 10), width = BitConverter.ToInt32(data, 18), height = BitConverter.ToInt32(data, 22);
        int bits = BitConverter.ToUInt16(data, 28), compression = BitConverter.ToInt32(data, 30);
        if (bits is not (24 or 32) || compression is not (0 or 3))
            throw new InvalidDataException($"BMP of {bits} bits (compression {compression}): only uncompressed 24- and 32-bit ones are read; save World Machine's bitmap as PNG.");
        bool topDown = height < 0;
        height = Math.Abs(height);
        int stride = (width * bits / 8 + 3) & ~3, step = bits / 8;
        var image = new RgbaImage(width, height);
        for (int y = 0; y < height; y++)
        {
            int row = offset + (topDown ? y : height - 1 - y) * stride;
            for (int x = 0; x < width; x++)
            {
                int i = row + x * step;
                image.SetPixel(x, y, new Rgba(data[i + 2], data[i + 1], data[i], 255));
            }
        }
        return image;
    }

    /// <summary>
    /// A vertex grid resampled to another (corners on corners: vertex i of the result is at i × (source − 1) /
    /// (result − 1)), bicubic (Catmull-Rom), kept within the four nearest heights so cliff edges don't ring.
    /// </summary>
    public static float[] ResampleCorners(float[] source, int sw, int sh, int dw, int dh)
    {
        if (sw == dw && sh == dh)
            return (float[])source.Clone();
        var result = new float[dw * dh];
        Parallel.For(0, dh, y =>
        {
            double sy = dh == 1 ? 0 : y * (sh - 1) / (double)(dh - 1);
            for (int x = 0; x < dw; x++)
                result[y * dw + x] = (float)Bicubic(source, sw, sh, dw == 1 ? 0 : x * (sw - 1) / (double)(dw - 1), sy);
        });
        return result;
    }

    static double Bicubic(float[] f, int w, int h, double x, double y)
    {
        int x1 = Math.Min((int)Math.Floor(x), w - 1), y1 = Math.Min((int)Math.Floor(y), h - 1);
        double fx = x - x1, fy = y - y1;
        float At(int ax, int ay) => f[Math.Clamp(ay, 0, h - 1) * w + Math.Clamp(ax, 0, w - 1)];
        static double CatmullRom(double p0, double p1, double p2, double p3, double t) =>
            p1 + 0.5 * t * (p2 - p0 + t * (2 * p0 - 5 * p1 + 4 * p2 - p3 + t * (3 * (p1 - p2) + p3 - p0)));
        double Row(int ay) => CatmullRom(At(x1 - 1, ay), At(x1, ay), At(x1 + 1, ay), At(x1 + 2, ay), fx);
        double v = CatmullRom(Row(y1 - 1), Row(y1), Row(y1 + 1), Row(y1 + 2), fy);
        float a = At(x1, y1), b = At(x1 + 1, y1), c = At(x1, y1 + 1), d = At(x1 + 1, y1 + 1);
        return Math.Clamp(v, Math.Min(Math.Min(a, b), Math.Min(c, d)), Math.Max(Math.Max(a, b), Math.Max(c, d)));
    }

    /// <summary>A pixel grid resampled to another (centres on centres).</summary>
    public static float[] ResampleCentres(float[] source, int sw, int sh, int dw, int dh)
    {
        if (sw == dw && sh == dh)
            return (float[])source.Clone();
        var result = new float[dw * dh];
        Parallel.For(0, dh, y =>
        {
            double sy = (y + 0.5) * sh / dh - 0.5;
            for (int x = 0; x < dw; x++)
                result[y * dw + x] = (float)Bilinear(source, sw, sh, (x + 0.5) * sw / dw - 0.5, sy);
        });
        return result;
    }

    /// <summary>An image resampled to another size (centres on centres, bilinear).</summary>
    public static RgbaImage ResampleCentres(RgbaImage source, int dw, int dh)
    {
        if (source.Width == dw && source.Height == dh)
            return source;
        var result = new RgbaImage(dw, dh);
        int sw = source.Width, sh = source.Height;
        var p = source.Pixels;
        Parallel.For(0, dh, y =>
        {
            double sy = Math.Clamp((y + 0.5) * sh / dh - 0.5, 0, sh - 1);
            int y0 = Math.Min((int)sy, sh - 2 < 0 ? 0 : sh - 2), y1 = Math.Min(y0 + 1, sh - 1);
            double fy = sy - y0;
            for (int x = 0; x < dw; x++)
            {
                double sx = Math.Clamp((x + 0.5) * sw / dw - 0.5, 0, sw - 1);
                int x0 = Math.Min((int)sx, sw - 2 < 0 ? 0 : sw - 2), x1 = Math.Min(x0 + 1, sw - 1);
                double fx = sx - x0;
                byte C(int c)
                {
                    double top = p[(y0 * sw + x0) * 4 + c] * (1 - fx) + p[(y0 * sw + x1) * 4 + c] * fx;
                    double bottom = p[(y1 * sw + x0) * 4 + c] * (1 - fx) + p[(y1 * sw + x1) * 4 + c] * fx;
                    return (byte)Math.Clamp(Math.Round(top + (bottom - top) * fy), 0, 255);
                }
                result.SetPixel(x, y, new Rgba(C(0), C(1), C(2), C(3)));
            }
        });
        return result;
    }

    static double Bilinear(float[] f, int w, int h, double x, double y)
    {
        x = Math.Clamp(x, 0, w - 1);
        y = Math.Clamp(y, 0, h - 1);
        int x0 = Math.Min((int)x, Math.Max(0, w - 2)), y0 = Math.Min((int)y, Math.Max(0, h - 2));
        int x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
        double fx = x - x0, fy = y - y0;
        double top = f[y0 * w + x0] * (1 - fx) + f[y0 * w + x1] * fx, bottom = f[y1 * w + x0] * (1 - fx) + f[y1 * w + x1] * fx;
        return top + (bottom - top) * fy;
    }

    /// <summary>A RAW16 file (little-endian), as World Machine reads and writes them.</summary>
    public static void WriteRaw16(string path, ushort[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++)
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), values[i]);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>
    /// World Machine Professional's automation script (XML): load the project, build at the heightmap's resolution and
    /// write its outputs. The project's own extents are left as they are.
    /// </summary>
    public static string AutomationScript(string project, int resolution)
    {
        var text = new StringBuilder();
        using (var writer = XmlWriter.Create(text, new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8, OmitXmlDeclaration = true }))
        {
            writer.WriteStartElement("automation");
            writer.WriteAttributeString("version", "WMP2");
            writer.WriteComment(" Written by BAR Map Editor: build the project at the map's heightmap resolution, then write its outputs. ");
            writer.WriteStartElement("load");
            writer.WriteAttributeString("file", project);
            writer.WriteEndElement();
            writer.WriteStartElement("modify");
            writer.WriteStartElement("world");
            writer.WriteAttributeString("res", resolution.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteStartElement("build");
            writer.WriteAttributeString("mode", "normal");
            writer.WriteEndElement();
            writer.WriteElementString("output", null);
            writer.WriteEndElement();
        }
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n" + text + "\n";
    }

    /// <summary>The set-up notes written beside the files: what each is, and how to wire World Machine to them.</summary>
    public static string ReadMeText(WorldMachineInfo info) => string.Create(CultureInfo.InvariantCulture, $"""
        BAR Map Editor <-> World Machine: {info.Map}

        One game unit is one metre. The map is {info.WorldWidth:0} x {info.WorldHeight:0} game units: in World Machine,
        0 to {info.WorldWidth / 1000:0.###} km by 0 to {info.WorldHeight / 1000:0.###} km. Its heightmap is {info.HeightsWidth} x {info.HeightsHeight}.
        heights.png spans the map's heights {info.MinHeight:0.###} (black) to {info.MaxHeight:0.###} (white): an altitude range of
        0 to {info.AltitudeRange:0.###} m in World Machine, which has no heights below 0. Sea level (height 0 in the game) is
        at {-info.MinHeight:0.###} m.

        Files written here
          heights.png     16-bit heightmap ({info.HeightsWidth} x {info.HeightsHeight}); heights.r16 is the same as RAW16
          texture.png     the map texture ({info.TextureWidth} x {info.TextureHeight})
          mask_water.png  white under water
          mask_slope.png  slope: black level, white vertical
          mask_cliffs.png white on cliffs (steeper than 40 to 55 degrees)
          mask_keep.png   white where the editor keeps the map as it is: metal spots, structures, start positions
          bar_map.json    these numbers, for bringing the results back

        In World Machine
          1. Set the project's extents to the map's size above and its build resolution to {info.HeightsWidth}.
             Make sure its height scale covers {info.AltitudeRange:0.###} m.
          2. Add a File Input device and load heights.png. Place it to fill the render extents, and set its elevations
             to Specify, from 0 to {info.AltitudeRange:0.###} m.
          3. Add a File Output device after it: format PNG (16-bit), filename wm_heights.png in this folder, with
             "Save file every build" ticked. (RAW16 as wm_heights.r16 works too.)
          4. Check the set-up first: wire the File Input straight to the File Output, build, and bring it back in the
             editor. It should report no change to speak of. If it reports a scale or an upside-down image, it says what to set.
          5. Then put your devices (erosion and the rest) between them, and build.
        Optional outputs, brought back the same way:
          wm_texture.png  a Bitmap Output (PNG) of the colours: becomes the map texture (best built at the texture's size)
          wm_splat.png    a Texture Weightmap (Splatmap) device with 4 inputs, packed, into a Bitmap Output: red, green,
                          blue and alpha become the map's detail textures 1 to 4
          wm_grass.png    a mask into a File Output: grass where it is white

        Back in the editor
          Beautify panel > World Machine > Bring back. With "Keep where units and buildings go", what would change where
          any unit can drive or a building can stand is blended back to the map as it was; metal spots, structures and
          start positions stay as they are. With "Bring back as World Machine writes", the editor brings each output back
          as soon as World Machine writes it.
          World Machine's result is built from the heights sent: edits made in the editor since then are replaced where
          it is taken (the editor says so when it brings it back). Send again before working on the map in World Machine.
          World Machine Professional: "Build in World Machine" writes build.xml (an automation script: load the
          project, build, write the outputs) and runs it, then brings the outputs back.
        """);
}
