using System.Numerics;
using MapCore.Assets;
using MapCore.Imaging;
using MapCore.Structures;

namespace MapCore.Tests;

/// <summary>The models and textures the editor builds for doors and tunnels, checked as the engine will read them.</summary>
static class StructureModelTests
{
    const float Gap = 64, Wall = 48, Depth = 48, Height = 80;

    static IEnumerable<(string Name, StructureMesh Mesh)> AllParts()
    {
        foreach (var look in new[] { StructureLook.Concrete, StructureLook.Metal })
        {
            yield return ($"{look} wall", StructureModels.Wall(look, Wall, Depth, Height));
            yield return ($"{look} door", StructureModels.Door(look, Gap, Depth, Height));
            yield return ($"{look} broken door", StructureModels.BrokenDoor(look, Gap, Depth, Height, 7));
            yield return ($"{look} tunnel, flat roof", StructureModels.TunnelTop(look, StructureRoof.Flat, Gap, Wall, Depth, Height, 7));
            yield return ($"{look} tunnel, rock roof", StructureModels.TunnelTop(look, StructureRoof.Cliff, Gap, Wall, Depth, Height, 7));
            yield return ($"{look} open tunnel, rock roof", StructureModels.OpenRoof(look, StructureRoof.Cliff, Gap, Wall, Depth, Height, 7));
            yield return ($"{look} open tunnel, flat roof", StructureModels.OpenRoof(look, StructureRoof.Flat, Gap, Wall, Depth, Height, 7));
            yield return ($"{look} repair marker, red", StructureModels.Repair(look, red: true));
            yield return ($"{look} repair marker, green", StructureModels.Repair(look, red: false));
            yield return ($"{look} broken tunnel, flat roof", StructureModels.BrokenTunnel(look, StructureRoof.Flat, Gap, Depth, Height, 7));
            yield return ($"{look} broken tunnel, rock roof", StructureModels.BrokenTunnel(look, StructureRoof.Cliff, Gap, Depth, Height, 7));
        }
        yield return ("oil pump", StructureModels.Pump(withBeam: false));
        yield return ("oil pump with its beam", StructureModels.Pump(withBeam: true));
        yield return ("oil pump's beam", StructureModels.PumpBeam());
        yield return ("derelict oil pump", StructureModels.DerelictPump(7));
        foreach (var look in new[] { StructureLook.Ancient, StructureLook.Concrete, StructureLook.Metal })
            yield return ($"{look} guardian tower", StructureModels.Guardian(look));
        yield return ("guardian rocket", StructureModels.GuardianRocket());
        yield return ("shield platform", StructureModels.ShieldPad(96, 96));
        yield return ("derelict shield platform", StructureModels.DerelictShieldPad(96, 128, 7));
    }

    /// <summary>
    /// A shield platform fills its footprint, its deck low enough to drive over (only its corner posts stand up); its
    /// atlas lets a platform made for a team wear that team's colour on its stripes, and only there, and its emitters glow.
    /// </summary>
    [Test]
    public static void TheShieldPlatformIsLowAndWearsItsOwnersColour()
    {
        foreach (var (w, d) in new[] { (96f, 96f), (64f, 160f) })
        {
            var pad = StructureModels.ShieldPad(w, d);
            Assert.True(Near(pad.VertexMin, new(-w / 2, -StructureModels.Skirt, -d / 2)) && Math.Abs(pad.VertexMax.X - w / 2) < 1e-3 && Math.Abs(pad.VertexMax.Z - d / 2) < 1e-3 &&
                        pad.VertexMax.Y <= StructureModels.ShieldPadHeight + 1e-3,
                $"{w} by {d}: exactly its footprint, no taller than its posts ({pad.VertexMin}, {pad.VertexMax})");
            var tall = pad.Vertices.Where(v => v.Position.Y > StructureModels.ShieldPadDeck + 2).ToList();
            Assert.True(tall.All(v => Math.Abs(v.Position.X) > w / 2 - 12 && Math.Abs(v.Position.Z) > d / 2 - 12),
                $"{w} by {d}: only the corner posts stand above the deck");
        }
        var (colourFile, extraFile) = StructureModels.ShieldPadTextureFiles();
        var colour = TgaReader.Read(colourFile);
        var extra = TgaReader.Read(extraFile);
        int teamColoured = 0, glowing = 0;
        bool onlyStripes = true, solid = true;
        for (int y = 0; y < colour.Height; y++)
            for (int x = 0; x < colour.Width; x++)
            {
                byte alpha = colour.GetPixel(x, y).A;
                bool paintCell = y / 128 == 1 && x / 128 == 0; // the stripes' paint: row 1, column 0
                if (alpha > 0)
                    teamColoured++;
                onlyStripes &= paintCell ? alpha == 200 : alpha == 0;
                solid &= extra.GetPixel(x, y).A == 255;
                if (extra.GetPixel(x, y).R == 255)
                    glowing++;
            }
        Assert.True(teamColoured == 128 * 128 && onlyStripes && solid && glowing >= 128 * 128,
            $"texture 1's alpha 200 on the stripes, 0 elsewhere; texture 2's 255; the emitters glow ({glowing} pixels)");
    }

    /// <summary>
    /// Every part as the engine reads it back: wound as real S3O models are (measured on 259 of them: (b−a)×(c−a)
    /// points along the stored normal), unit normals, texture coordinates inside the atlas, and a header whose height
    /// and middle are the model's (the engine centres a collision volume on that middle and never works it out).
    /// </summary>
    [Test]
    public static void PartsAreWoundAndTexturedAsTheEngineReadsThem()
    {
        foreach (var (name, mesh) in AllParts())
        {
            var bytes = mesh.ToS3o("part");
            var read = S3oModel.Load(bytes) ?? throw new TestFailure($"{name}: not read back as an S3O");
            Assert.Equal(mesh.TriangleCount, read.TriangleCount, $"{name}: triangles read back");
            int against = 0;
            for (int i = 0; i < read.Triangles.Count; i += 3)
            {
                var (a, b, c) = (read.Vertices[read.Triangles[i]], read.Vertices[read.Triangles[i + 1]], read.Vertices[read.Triangles[i + 2]]);
                if (Vector3.Dot(Vector3.Cross(b.Position - a.Position, c.Position - a.Position), a.Normal) <= 0)
                    against++;
            }
            Assert.Equal(0, against, $"{name}: triangles wound against their normals");
            Assert.True(read.Vertices.All(v => Math.Abs(v.Normal.Length() - 1) < 1e-4 && v.Uv.X is >= 0 and <= 1 && v.Uv.Y is >= 0 and <= 1),
                $"{name}: unit normals, texture coordinates inside the atlas");
            var min = mesh.VertexMin;
            var max = mesh.VertexMax;
            float headerHeight = BitConverter.ToSingle(bytes, 20), midY = BitConverter.ToSingle(bytes, 28);
            Assert.True(Math.Abs(headerHeight - max.Y) < 1e-3 && Math.Abs(midY - (min.Y + max.Y) / 2) < 1e-3 && BitConverter.ToSingle(bytes, 16) > 0,
                $"{name}: the header's height and middle are the model's");
            Assert.True(mesh.ToS3o("part").SequenceEqual(bytes), $"{name}: the same settings give the same bytes");
        }
    }

    /// <summary>Parts fill their footprints exactly, so walls meet the centre with no gap and no overlap, and stand on the ground.</summary>
    [Test]
    public static void PartsFillTheirFootprints()
    {
        foreach (var look in new[] { StructureLook.Concrete, StructureLook.Metal })
        {
            var wall = StructureModels.Wall(look, Wall, Depth, Height);
            Assert.True(Near(wall.VertexMin, new(-Wall / 2, -StructureModels.Skirt, -Depth / 2)) && Near(wall.VertexMax, new(Wall / 2, Height, Depth / 2)),
                $"{look} wall: exactly its footprint, its full height, and its skirt below ground ({wall.VertexMin}, {wall.VertexMax})");
            var door = StructureModels.Door(look, Gap, Depth, Height);
            Assert.True(Math.Abs(door.VertexMin.X + Gap / 2) < 1e-3 && Math.Abs(door.VertexMax.X - Gap / 2) < 1e-3 && door.VertexMax.Y <= Height &&
                        door.VertexMin.Z >= -Depth / 2 && door.VertexMax.Z <= Depth / 2,
                $"{look} door: from wall to wall, no taller than the walls, within its depth");
            var top = StructureModels.TunnelTop(look, StructureRoof.Flat, Gap, Wall, Depth, Height, 7);
            Assert.True(Near(top.VertexMin, new(-Gap / 2, -StructureModels.Skirt, -Depth / 2)) && Near(top.VertexMax, new(Gap / 2, Height, Depth / 2)),
                $"{look} tunnel top: its footprint, its flat roof level with the walls' tops");
            var roofTop = top.Vertices.Where(v => v.Normal.Y > 0.99f).Max(v => v.Position.Y);
            Assert.True(Math.Abs(roofTop - Height) < 1e-3, "the roof buildings are lifted onto is at the kind's height");
        }
    }

    /// <summary>
    /// What a destroyed door or tunnel leaves lies low at the edges of its gap (it doesn't block: units walk over it),
    /// no wider than the gap, its middle clear.
    /// </summary>
    [Test]
    public static void ABrokenDoorLiesLowInItsGap()
    {
        foreach (var (look, name, broken) in new[] { StructureLook.Concrete, StructureLook.Metal }.SelectMany(l => new[]
                 {
                     (l, "door", StructureModels.BrokenDoor(l, Gap, Depth, Height, 7)),
                     (l, "flat tunnel", StructureModels.BrokenTunnel(l, StructureRoof.Flat, Gap, Depth, Height, 7)),
                     (l, "rock tunnel", StructureModels.BrokenTunnel(l, StructureRoof.Cliff, Gap, Depth, Height, 7)),
                 }))
        {
            var door = StructureModels.Door(look, Gap, Depth, Height);
            Assert.True(broken.VertexMax.Y <= Height * 0.42f + 1e-3 && broken.VertexMax.Y < door.VertexMax.Y / 2 + 1,
                $"{look} {name}: under half the door's height ({broken.VertexMax.Y} of {door.VertexMax.Y})");
            Assert.True(broken.VertexMin.X >= -Gap / 2 - 3 && broken.VertexMax.X <= Gap / 2 + 3, $"{look} {name}: within the gap ({broken.VertexMin.X}, {broken.VertexMax.X})");
            float clear = Gap / 2 * StructureModels.BrokenDoorClear;
            var inMiddle = broken.Vertices.Where(v => Math.Abs(v.Position.X) < clear - 0.01f && v.Position.Y > 0.6f).ToList();
            Assert.True(inMiddle.Count == 0 && clear * 2 > 28,
                $"{look} {name}: the middle {clear * 2:0} across (wider than a commander's 28) is clear but for anything flush with the ground ({inMiddle.Count} points above it)");
        }
    }

    /// <summary>
    /// An open tunnel with a rock roof leaves its passage clear: nothing in the gap below the vault's springing (55%
    /// of the height), nothing where a commander (28 across, 52 tall) walks down the middle, and the vault's crown
    /// above that. The vault's triangles are flat between its points, so the test is on vertices and triangle middles.
    /// </summary>
    [Test]
    public static void AnOpenTunnelsPassageIsClear()
    {
        var roof = StructureModels.OpenRoof(StructureLook.Concrete, StructureRoof.Cliff, Gap, Wall, Depth, Height, 7);
        float spring = Height * 0.55f;
        bool InPassage(Vector3 p) => Math.Abs(p.X) < Gap / 2 - 0.5f && Math.Abs(p.Z) < Depth / 2 - 0.5f && p.Y < spring - 0.5f;
        bool InCommander(Vector3 p) => Math.Abs(p.X) < 14 && Math.Abs(p.Z) < Depth / 2 - 0.5f && p.Y < 52;
        var points = roof.Vertices.Select(v => v.Position).Concat(Enumerable.Range(0, roof.TriangleCount).Select(t =>
            (roof.Vertices[roof.Triangles[t * 3]].Position + roof.Vertices[roof.Triangles[t * 3 + 1]].Position + roof.Vertices[roof.Triangles[t * 3 + 2]].Position) / 3)).ToList();
        Assert.True(!points.Any(InPassage), "nothing hangs into the passage below the vault's springing");
        Assert.True(!points.Any(InCommander), "a commander walking down the middle clears the vault");
        var middle = roof.Vertices.Where(v => Math.Abs(v.Position.X) < 2 && Math.Abs(v.Position.Z) < Depth / 2 - 1).Min(v => v.Position.Y);
        Assert.True(middle >= Height - 18 - 1e-3, $"under the middle of the roof is the vault's crown, nothing lower ({middle})");
    }

    /// <summary>The atlas as the engine needs it: texture 1's alpha 0 (or BAR mixes in a feature's black team colour), texture 2's alpha 255 (or holes).</summary>
    [Test]
    public static void TheAtlasIsReadyForTheEngine()
    {
        var (colourFile, extraFile) = StructureModels.TextureFiles();
        var colour = TgaReader.Read(colourFile);
        var extra = TgaReader.Read(extraFile);
        Assert.True(colour is { Width: 512, Height: 512 } && extra is { Width: 512, Height: 512 }, "512 square");
        bool colourClear = true, extraSolid = true, someGlow = false, someMetal = false;
        for (int i = 0; i < colour.Pixels.Length; i += 4)
        {
            colourClear &= colour.Pixels[i + 3] == 0;
            extraSolid &= extra.Pixels[i + 3] == 255;
            someGlow |= extra.Pixels[i] == 255;
            someMetal |= extra.Pixels[i + 1] >= 200;
        }
        Assert.True(colourClear && extraSolid, "texture 1 alpha 0 everywhere, texture 2 alpha 255 everywhere");
        Assert.True(someGlow && someMetal, "the repair light glows, and metal is metal");
        Assert.True(StructureModels.TextureFiles().Colour.SequenceEqual(colourFile), "the same bytes each time");

        // A map's cliff colour tints the rock, and only the rock.
        var red = TgaReader.Read(StructureModels.TextureFiles(new Rgba(170, 60, 50, 255)).Colour);
        double Redness(RgbaImage image, int cell)
        {
            int x0 = cell % 4 * 128, y0 = cell / 4 * 128;
            double r = 0, g = 0;
            for (int y = y0 + 8; y < y0 + 120; y++)
                for (int x = x0 + 8; x < x0 + 120; x++)
                {
                    var p = image.GetPixel(x, y);
                    (r, g) = (r + p.R, g + p.G);
                }
            return r / g;
        }
        const int rockTop = 11, concreteWall = 0;
        Assert.True(Redness(red, rockTop) > Redness(colour, rockTop) + 0.2 && Math.Abs(Redness(red, concreteWall) - Redness(colour, concreteWall)) < 1e-9,
            "a red cliff colour reddens the rock, not the concrete");
    }

    /// <summary>
    /// The oil pump stands within its footprint (the derelict's scattered staves aside), its beam pivots at the post's
    /// bearing, and its atlas lets a pump made for a team wear that team's colour on its paint, and only there.
    /// </summary>
    [Test]
    public static void TheOilPumpFitsAndWearsItsOwnersColour()
    {
        var pump = StructureModels.Pump(withBeam: true);
        float hw = StructureModels.PumpWidth / 2, hl = StructureModels.PumpLength / 2;
        Assert.True(pump.VertexMin.X >= -hw - 1e-3 && pump.VertexMax.X <= hw + 1e-3 && pump.VertexMin.Z >= -hl - 1e-3 && pump.VertexMax.Z <= hl + 1e-3,
            $"within its {StructureModels.PumpWidth} by {StructureModels.PumpLength} footprint ({pump.VertexMin}, {pump.VertexMax})");
        var beam = StructureModels.PumpBeam();
        Assert.True(beam.VertexMin.Y < 0 && beam.VertexMax.Y > 0 && beam.VertexMin.Z < 0 && beam.VertexMax.Z > 0, "the beam's own origin is its pivot, inside it");
        var body = StructureModels.Pump(withBeam: false);
        Assert.True(body.Vertices.Any(v => Vector3.Distance(v.Position, StructureModels.PumpPivot) < 6), "the post's bearing is where the beam pivots");

        var (colourFile, extraFile) = StructureModels.PumpTextureFiles();
        var colour = TgaReader.Read(colourFile);
        var extra = TgaReader.Read(extraFile);
        int teamColoured = 0;
        bool onlyPaint = true, solid = true;
        for (int y = 0; y < colour.Height; y++)
            for (int x = 0; x < colour.Width; x++)
            {
                byte alpha = colour.GetPixel(x, y).A;
                bool paintCell = y / 128 == 0 && x / 128 == 1; // the pump's paint: row 0, column 1
                if (alpha > 0)
                    teamColoured++;
                onlyPaint &= paintCell ? alpha == 200 : alpha == 0;
                solid &= extra.GetPixel(x, y).A == 255;
            }
        Assert.True(teamColoured == 128 * 128 && onlyPaint && solid, "texture 1's alpha 200 over its paint and 0 elsewhere; texture 2's 255 everywhere");
    }

    /// <summary>
    /// A guardian tower stands in its 3 by 3 squares, its pods' mouths where the gadget launches its rockets; its looks
    /// are one shape, coloured differently; and its atlas glows and never takes a team colour.
    /// </summary>
    [Test]
    public static void TheGuardianStandsInItsSquaresWithItsPodsWhereRocketsLeave()
    {
        var ancient = StructureModels.Guardian(StructureLook.Ancient);
        float h = StructureModels.GuardianSize / 2;
        Assert.True(Near(ancient.VertexMin, new(-h, -StructureModels.Skirt, -h)) && Math.Abs(ancient.VertexMax.X - h) < 1e-3 && Math.Abs(ancient.VertexMax.Z - h) < 1e-3 &&
                    Math.Abs(ancient.VertexMax.Y - StructureModels.GuardianHeight) < 1e-3,
            $"3 squares a side, {StructureModels.GuardianHeight} tall, its plinth reaching below the ground ({ancient.VertexMin}, {ancient.VertexMax})");
        foreach (var (x, z) in new[] { (0f, 1f), (1f, 0f), (0f, -1f), (-1f, 0f) })
        {
            var mouth = new Vector3(x * StructureModels.GuardianPodOut, StructureModels.GuardianPodUp, z * StructureModels.GuardianPodOut);
            var nearest = ancient.Vertices.Min(v => Vector3.Distance(v.Position, mouth));
            Assert.True(nearest < 6, $"a pod's mouths around {mouth} ({nearest:0.0} away)");
        }
        foreach (var look in new[] { StructureLook.Concrete, StructureLook.Metal })
        {
            var other = StructureModels.Guardian(look);
            Assert.True(other.Vertices.Select(v => v.Position).SequenceEqual(ancient.Vertices.Select(v => v.Position)) &&
                        !other.Vertices.Select(v => v.Uv).SequenceEqual(ancient.Vertices.Select(v => v.Uv)),
                $"{look}: the ancient tower's shape, coloured differently");
        }

        var (colourFile, extraFile) = StructureModels.GuardianTextureFiles();
        var colour = TgaReader.Read(colourFile);
        var extra = TgaReader.Read(extraFile);
        bool clear = true, solid = true;
        int glowing = 0;
        for (int i = 0; i < colour.Pixels.Length; i += 4)
        {
            clear &= colour.Pixels[i + 3] == 0;
            solid &= extra.Pixels[i + 3] == 255;
            if (extra.Pixels[i] == 255)
                glowing++;
        }
        Assert.True(colour is { Width: 512, Height: 512 } && clear && solid, "512 square, no team colour, no holes");
        Assert.True(glowing >= 2 * 128 * 128, $"the teal and amber glow cells glow, and the runes' strokes ({glowing} pixels)");
    }

    [Test]
    public static void TgaImagesAreWrittenAsTheyAreRead()
    {
        var image = new RgbaImage(37, 21);
        var random = new Random(5);
        random.NextBytes(image.Pixels);
        var back = TgaReader.Read(TgaWriter.Write(image));
        Assert.True(back.Width == 37 && back.Height == 21 && back.Pixels.SequenceEqual(image.Pixels), "every pixel back, top row first, alpha kept");
    }

    /// <summary>A door or tunnel's preview: its parts as one model standing on the ground; parts with different textures aren't joined.</summary>
    [Test]
    public static void PartsJoinIntoOnePreview()
    {
        static ModelPreview Preview(StructureMesh mesh, string texture = StructureModels.Texture1) => new()
        {
            ModelPath = "part", Mesh = new ModelMesh(mesh.Vertices, mesh.Triangles, texture, StructureModels.Texture2, 1),
        };
        var wall = Preview(StructureModels.Wall(StructureLook.Concrete, Wall, Depth, Height));
        var door = Preview(StructureModels.Door(StructureLook.Concrete, Gap, Depth, Height));
        var whole = ModelPreview.Combine("gate", [(wall, new Vector3(-56, 0, 0)), (wall, new Vector3(56, 0, 0)), (door, Vector3.Zero)]);
        Assert.True(whole != null && whole.Mesh.TriangleCount == wall.Mesh.TriangleCount * 2 + door.Mesh.TriangleCount, "three parts as one");
        Assert.True(whole!.Mesh.Vertices.Min(v => v.Position.Y) >= 0 && Math.Abs(whole.Mesh.Vertices.Min(v => v.Position.X) + 80) < 1e-3,
            "standing on the ground (the skirt raised to it), walls either side");
        Assert.True(ModelPreview.Combine("mixed", [(wall, Vector3.Zero), (Preview(StructureModels.Repair(StructureLook.Concrete), "rock.dds"), Vector3.Zero)]) == null,
            "parts with different textures aren't joined");
    }

    static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-3;
}
