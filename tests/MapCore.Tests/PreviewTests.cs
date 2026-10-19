using System.Buffers.Binary;
using System.Numerics;
using MapCore.Assets;
using MapCore.Imaging;
using MapCore.Rendering;

namespace MapCore.Tests;

/// <summary>Model previews: reading S3O models whole, reading TGA textures, and drawing a model the right way round.</summary>
static class PreviewTests
{
    static ModelVertex V(float x, float y, float z, float u = 0, float v = 0) => new(new Vector3(x, y, z), Vector3.UnitZ, new Vector2(u, v));

    [Test]
    public static void S3oPiecesSitWhereTheirParentsPutThem()
    {
        var data = S3oModel.Make("colour.tga", "other.tga",
        [
            (-1, new Vector3(0, 10, 0), [V(0, 0, 0), V(1, 0, 0), V(0, 1, 0)], [0, 1, 2]),
            (0, new Vector3(5, 0, 0), [V(0, 0, 0), V(1, 0, 0), V(0, 1, 0)], [0, 1, 2]),
            (1, new Vector3(0, 0, 3), [V(0, 0, 0), V(1, 0, 0), V(0, 1, 0)], [0, 1, 2]),
        ]);
        var mesh = S3oModel.Load(data)!;
        Assert.True(mesh.Pieces == 3 && mesh.TriangleCount == 3 && mesh.Texture1 == "colour.tga" && mesh.Texture2 == "other.tga", "three pieces, a triangle each");
        var origins = mesh.Triangles.Where((_, i) => i % 3 == 0).Select(i => mesh.Vertices[i].Position).ToList();
        Assert.Equal("<0, 10, 0>|<5, 10, 0>|<5, 10, 3>", string.Join("|", origins.Select(p => p.ToString("0", System.Globalization.CultureInfo.InvariantCulture))),
            "each piece offset from its parent, all the way down");
        Assert.True(S3oModel.Load(S3oModel.Make("a.tga", "b.tga"))!.TriangleCount == 0, "a model with no pieces is empty, not an error");
        Assert.True(S3oModel.Load([1, 2, 3]) == null, "not an S3O");
    }

    [Test]
    public static void S3oStripsAndQuadsBecomeTriangles()
    {
        ModelVertex[] square = [V(0, 0, 0), V(1, 0, 0), V(0, 1, 0), V(1, 1, 0)];
        byte[] Shaped(int primitive, int[] indices)
        {
            var data = S3oModel.Make("a.tga", "b.tga", [(-1, Vector3.Zero, square, indices)]);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(36)) + 24), primitive);
            return data;
        }
        Assert.Equal(2, S3oModel.Load(Shaped(1, [0, 1, 2, 3]))!.TriangleCount, "a strip of four makes two");
        Assert.Equal(2, S3oModel.Load(Shaped(1, [0, 1, 2, -1, 1, 3, 2]))!.TriangleCount, "-1 starts a new strip");
        Assert.Equal(2, S3oModel.Load(Shaped(2, [0, 1, 3, 2]))!.TriangleCount, "a quad makes two");
        Assert.Equal(1, S3oModel.Load(Shaped(0, [0, 1, 2, 0, 0, 1, 0, 1, 9]))!.TriangleCount, "degenerate triangles and indices past the end draw nothing");
    }

    [Test]
    public static void BrokenS3oFilesAreRefused()
    {
        var data = S3oModel.Make("a.tga", "b.tga", [(-1, Vector3.Zero, [V(0, 0, 0), V(1, 0, 0), V(0, 1, 0)], [0, 1, 2])]);
        int piece = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(36));
        var looped = (byte[])data.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(looped.AsSpan(piece + 4), 1); // one child...
        int table = looped.Length;
        Array.Resize(ref looped, looped.Length + 4);
        BinaryPrimitives.WriteInt32LittleEndian(looped.AsSpan(piece + 8), table);
        BinaryPrimitives.WriteInt32LittleEndian(looped.AsSpan(table), piece); // ...which is itself
        Assert.Throws<InvalidDataException>(() => S3oModel.Load(looped), "a piece that is its own child");
        var outside = (byte[])data.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(outside.AsSpan(piece + 16), 1 << 30);
        Assert.Throws<InvalidDataException>(() => S3oModel.Load(outside), "vertices outside the file");
        Assert.Throws<InvalidDataException>(() => S3oModel.Load(data[..(piece + 20)]), "a file cut short");
    }

    static byte[] Tga(int type, int bits, int width, int height, bool topFirst, byte[] pixels)
    {
        var header = new byte[18];
        header[2] = (byte)type;
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), (ushort)height);
        header[16] = (byte)bits;
        header[17] = (byte)((topFirst ? 0x20 : 0) | (bits == 32 ? 8 : 0));
        return [.. header, .. pixels];
    }

    [Test]
    public static void TgaImagesReadEitherWayUp()
    {
        // 2x2, stored bottom row first (the TGA default): bottom blue, white; top red, green. B, G, R, A per pixel.
        var plain = TgaReader.Read(Tga(2, 32, 2, 2, false, [255, 0, 0, 255, 255, 255, 255, 128, 0, 0, 255, 255, 0, 255, 0, 255]));
        Assert.True(plain.GetPixel(0, 0) == new Rgba(255, 0, 0, 255) && plain.GetPixel(1, 0) == new Rgba(0, 255, 0, 255) &&
                    plain.GetPixel(0, 1) == new Rgba(0, 0, 255, 255) && plain.GetPixel(1, 1) == new Rgba(255, 255, 255, 128), "bottom-up, 32 bits");
        // The same picture top row first, 24 bits, run-length encoded: a run of two reds, then two raw pixels.
        var rle = TgaReader.Read(Tga(10, 24, 2, 2, true, [0x81, 0, 0, 255, 0x01, 255, 0, 0, 255, 255, 255]));
        Assert.True(rle.GetPixel(0, 0) == new Rgba(255, 0, 0, 255) && rle.GetPixel(1, 0) == new Rgba(255, 0, 0, 255) &&
                    rle.GetPixel(0, 1) == new Rgba(0, 0, 255, 255) && rle.GetPixel(1, 1) == new Rgba(255, 255, 255, 255), "top-down, 24 bits, RLE");
        var grey = TgaReader.Read(Tga(3, 8, 2, 1, true, [10, 200]));
        Assert.True(grey.GetPixel(1, 0) == new Rgba(200, 200, 200, 255), "grey");
        Assert.Throws<InvalidDataException>(() => TgaReader.Read(Tga(2, 32, 4, 4, true, [1, 2, 3])), "cut short");
    }

    /// <summary>
    /// A square facing the viewer, textured with four colours: drawn the right way round (S3O's v = 0 is the
    /// texture's top row, as the engine has it; checked by eye on real models too), and texture 2's alpha cutting
    /// out where it is clear.
    /// </summary>
    [Test]
    public static void ModelsAreDrawnTheRightWayRound()
    {
        var colour = new RgbaImage(2, 2);
        colour.SetPixel(0, 0, new Rgba(255, 0, 0, 0));
        colour.SetPixel(1, 0, new Rgba(0, 255, 0, 0));
        colour.SetPixel(0, 1, new Rgba(0, 0, 255, 0));
        colour.SetPixel(1, 1, new Rgba(255, 255, 255, 0));
        var mesh = new ModelMesh([V(-1, 0, 0, 0, 1), V(1, 0, 0, 1, 1), V(1, 2, 0, 1, 0), V(-1, 2, 0, 0, 0)], [0, 1, 2, 0, 2, 3], "c.tga", null, 1);
        var model = new ModelPreview { ModelPath = "objects3d/square.s3o", Mesh = mesh, Colour = colour };
        var empty = ModelRenderer.Render(new ModelPreview { ModelPath = "none", Mesh = new ModelMesh([], [], null, null, 0) }, 64, 64, yaw: 0, pitch: 0);
        var drawn = ModelRenderer.Render(model, 64, 64, yaw: 0, pitch: 0);
        var covered = Enumerable.Range(0, 64 * 64).Where(i => drawn.Image.GetPixel(i % 64, i / 64) != empty.Image.GetPixel(i % 64, i / 64)).ToList();
        int left = covered.Min(i => i % 64), right = covered.Max(i => i % 64), top = covered.Min(i => i / 64), bottom = covered.Max(i => i / 64);
        Rgba At(double fx, double fy) => drawn.Image.GetPixel((int)(left + fx * (right - left)), (int)(top + fy * (bottom - top)));
        string Main(Rgba c) => c.R > 2 * c.G && c.R > 2 * c.B ? "red" : c.G > 2 * c.R && c.G > 2 * c.B ? "green" : c.B > 2 * c.R && c.B > 2 * c.G ? "blue"
            : c.R > 100 && c.G > 100 && c.B > 100 ? "white" : $"{c}";
        Assert.Equal("red green blue white", $"{Main(At(0.25, 0.25))} {Main(At(0.75, 0.25))} {Main(At(0.25, 0.75))} {Main(At(0.75, 0.75))}",
            "top left, top right, bottom left, bottom right");
        Assert.True(drawn.Coverage > 0.1, $"the square fills the picture ({drawn.Coverage:P0})");

        var extra = new RgbaImage(2, 2);
        extra.SetPixel(1, 0, new Rgba(0, 0, 0, 255));
        extra.SetPixel(1, 1, new Rgba(0, 0, 0, 255)); // the right half solid, the left clear
        Assert.True(ModelPreview.HasCutOut(extra) && !ModelPreview.HasCutOut(new RgbaImage(2, 2)), "texture 2's alpha cuts out only when it is partly clear");
        var cut = ModelRenderer.Render(new ModelPreview { ModelPath = "x", Mesh = mesh, Colour = colour, Extra = extra, CutOut = true }, 64, 64, yaw: 0, pitch: 0);
        Rgba CutAt(double fx, double fy) => cut.Image.GetPixel((int)(left + fx * (right - left)), (int)(top + fy * (bottom - top)));
        Assert.True(CutAt(0.15, 0.5) == empty.Image.GetPixel((int)(left + 0.15 * (right - left)), (int)(top + 0.5 * (bottom - top))) &&
                    Main(CutAt(0.85, 0.25)) == "green", "the clear half is cut out, the solid half drawn");
    }

    /// <summary>Acidic Quarry's rocks, read and drawn from the map: TGA textures, and the picture is mostly rock.</summary>
    [Test]
    public static void AcidicQuarrysRocksPreview()
    {
        var root = TestData.AcidicQuarry.Root;
        var preview = ModelPreview.ForFeature(root, "agorm_rock3")!;
        Assert.True(preview.Colour != null && preview.Extra != null && !preview.CutOut && preview.Notes.Count == 0 && preview.Mesh.TriangleCount > 100,
            $"{preview.ModelPath}: {preview.Mesh.TriangleCount} triangles, textures {preview.Colour?.Width}x{preview.Colour?.Height}");
        var render = ModelRenderer.Render(preview, 160, 140);
        Assert.True(render.Coverage is > 0.05 and < 0.9, $"the rock fills part of the picture ({render.Coverage:P0})");
        Assert.True(ModelPreview.ForFeature(root, "no_such_feature") == null, "a feature the map doesn't define has no preview");
    }
}
