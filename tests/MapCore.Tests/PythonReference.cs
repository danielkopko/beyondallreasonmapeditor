using System.Diagnostics;
using System.Text.Json;

namespace MapCore.Tests;

/// <summary>Runs the independent Python parsers in tools/ and returns their JSON output.</summary>
static class PythonReference
{
    public static JsonElement DescribeSmf(string smfPath) => RunJson("smf_reference.py", smfPath);

    /// <summary>Metal spots as BAR's resource spot finder would report them (Python port): x, z, raw.</summary>
    public static IReadOnlyList<(double X, double Z, int Raw)> BarMetalSpots(string smfPath) =>
        RunJson("smf_reference.py", smfPath, "--metal-spots").GetProperty("metalSpots").EnumerateArray()
            .Select(s => (s.GetProperty("x").GetDouble(), s.GetProperty("z").GetDouble(), s.GetProperty("raw").GetInt32()))
            .ToList();

    public static JsonElement DescribeDds(string ddsPath) => RunJson("dds_reference.py", ddsPath);

    /// <summary>Mip 0 of the texture tile at grid position (column, row) as 32x32 RGBA, row by row.</summary>
    public static byte[] DecodeTile(string smfPath, int column, int row)
    {
        var output = Path.Combine(TestData.NewWorkDir("py-tile"), "tile.bin");
        RunJson("smf_reference.py", smfPath, "--tile", column.ToString(), row.ToString(), output);
        return File.ReadAllBytes(output);
    }

    /// <summary>The first <paramref name="blocks"/> 4x4 blocks of mip 0 as RGBA, 64 bytes per block.</summary>
    public static byte[] DecodeDdsBlocks(string ddsPath, int blocks)
    {
        var output = Path.Combine(TestData.NewWorkDir("py-dds"), "blocks.bin");
        RunJson("dds_reference.py", ddsPath, "--decode-blocks", blocks.ToString(), "--out", output);
        return File.ReadAllBytes(output);
    }

    static JsonElement RunJson(string script, params string[] args)
    {
        var psi = new ProcessStartInfo("python")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-I");
        psi.ArgumentList.Add(Path.Combine(TestData.RepoRoot, "tools", script));
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new TestFailure("could not start python");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new TestFailure($"{script} failed (exit {process.ExitCode}): {stderr.Trim()}");
        using var doc = JsonDocument.Parse(stdout.Result);
        return doc.RootElement.Clone();
    }
}
