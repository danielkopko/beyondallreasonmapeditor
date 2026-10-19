using System.Collections.Concurrent;
using System.Numerics;
using MapCore.Assets;
using MapCore.Editing;
using MapCore.Imaging;

namespace MapCore.Structures;

/// <summary>
/// What each type of structure looks like as a new kind of it starts (a door's or tunnel's concrete walls and centre,
/// the ancient guardian tower, the oil pump with its beam, the shield platform), made in memory from the same meshes
/// and atlases a kind writes into the map: for the asset browser's Experimental entries, before any kind exists.
/// </summary>
public static class StructurePreviews
{
    static readonly ConcurrentDictionary<StructureType, ModelPreview> made = new();

    public static ModelPreview For(StructureType type) => made.GetOrAdd(type, Make);

    static ModelPreview Make(StructureType type)
    {
        var name = StructureKind.TypeName(type);
        switch (type)
        {
            case StructureType.Guardian:
                return One(name, StructureModels.Guardian(StructureLook.Ancient), StructureModels.GuardianTexture1, StructureModels.GuardianTexture2,
                    StructureModels.GuardianTextureFiles());
            case StructureType.Pump:
                return One(name, StructureModels.Pump(withBeam: true), StructureModels.PumpTexture1, StructureModels.PumpTexture2, StructureModels.PumpTextureFiles());
            case StructureType.ShieldPad:
            {
                var pad = StructureKindSettings.ShieldPad();
                return One(name, StructureModels.ShieldPad(pad.Gap * 16, pad.Depth * 16), StructureModels.ShieldPadTexture1, StructureModels.ShieldPadTexture2,
                    StructureModels.ShieldPadTextureFiles());
            }
            default:
            {
                // Two walls and the centre between them, as StructureLayout.Parts places them (walls ±(gap + width) × 8 across).
                var s = StructureKindSettings.Built(StructureLook.Concrete, type);
                var roof = type == StructureType.OpenTunnel ? StructureRoof.Cliff : StructureRoof.Flat; // as the Asset tab starts each
                float g = s.Gap * 16, w = s.WallWidth * 16, d = s.Depth * 16, h = (float)s.Height;
                int seed = StructureModels.Seed(name);
                var centre = type switch
                {
                    StructureType.Door => StructureModels.Door(StructureLook.Concrete, g, d, h),
                    StructureType.Tunnel => StructureModels.TunnelTop(StructureLook.Concrete, roof, g, w, d, h, seed),
                    _ => StructureModels.OpenRoof(StructureLook.Concrete, roof, g, w, d, h, seed),
                };
                var files = StructureModels.TextureFiles();
                var wall = One("wall", StructureModels.Wall(StructureLook.Concrete, w, d, h), StructureModels.Texture1, StructureModels.Texture2, files);
                float across = (s.Gap + s.WallWidth) * 8;
                return ModelPreview.Combine(name,
                [
                    (wall, new Vector3(-across, 0, 0)),
                    (One(name, centre, StructureModels.Texture1, StructureModels.Texture2, files), Vector3.Zero),
                    (wall, new Vector3(across, 0, 0)),
                ])!;
            }
        }
    }

    static ModelPreview One(string name, StructureMesh mesh, string texture1, string texture2, (byte[] Colour, byte[] Extra) files)
    {
        var model = S3oModel.Load(mesh.ToS3o(name, texture1, texture2)) ?? throw new InvalidDataException($"{name}'s mesh didn't make a model");
        var colour = TgaReader.Read(files.Colour);
        var extra = TgaReader.Read(files.Extra);
        return new ModelPreview { ModelPath = name, Mesh = model, Colour = colour, Extra = extra, CutOut = ModelPreview.HasCutOut(extra) };
    }
}
