using MapCore.Structures;

namespace MapResearch;

/// <summary>
/// Writes the oil pump's models into a folder laid out like a map (features/, objects3d/, unittextures/), so that
/// <c>MapTool preview &lt;folder&gt; &lt;feature&gt; &lt;out.png&gt;</c> can draw each one to look at.
/// </summary>
static class ModelStudy
{
    public static void Run(string outDir)
    {
        foreach (var dir in new[] { "features", "objects3d", "unittextures" })
            Directory.CreateDirectory(Path.Combine(outDir, dir));
        var (colour, extra) = StructureModels.PumpTextureFiles();
        File.WriteAllBytes(Path.Combine(outDir, "unittextures", StructureModels.PumpTexture1), colour);
        File.WriteAllBytes(Path.Combine(outDir, "unittextures", StructureModels.PumpTexture2), extra);
        foreach (var (name, mesh) in new[]
                 {
                     ("pump", StructureModels.Pump(withBeam: true)),
                     ("pump_body", StructureModels.Pump(withBeam: false)),
                     ("pump_beam", StructureModels.PumpBeam()),
                     ("pump_derelict", StructureModels.DerelictPump(7)),
                 })
        {
            File.WriteAllBytes(Path.Combine(outDir, "objects3d", name + ".s3o"), mesh.ToS3o(name, StructureModels.PumpTexture1, StructureModels.PumpTexture2));
            File.WriteAllText(Path.Combine(outDir, "features", name + ".lua"), $"return {{ {name} = {{ object = \"{name}.s3o\" }} }}\n");
            Console.WriteLine($"{name}: {mesh.TriangleCount} triangles, {mesh.VertexMin} to {mesh.VertexMax}");
        }
    }
}
