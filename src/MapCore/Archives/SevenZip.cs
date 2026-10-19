using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace MapCore.Archives;

/// <summary>Drives the installed 7-Zip command-line tool (7z.exe).</summary>
static class SevenZip
{
    public static void Extract(string archivePath, string destDir) =>
        Run(null, "x", archivePath, "-o" + destDir, "-y", "-bso0", "-bsp0");

    /// <summary>Packs everything under <paramref name="sourceDir"/> into a new 7z archive.</summary>
    /// <remarks>
    /// LZMA2 with a 16 MB dictionary ("LZMA2:24", as in published maps). The dictionary is pinned because
    /// 7-Zip's per-level default differs between versions. Non-solid, like the original archives, so each
    /// file can be read without decompressing the ones before it.
    /// </remarks>
    public static void Pack(string sourceDir, string archivePath) =>
        Run(sourceDir, "a", "-t7z", "-m0=LZMA2", "-mx=5", "-md=16m", "-ms=off", "-bso0", "-bsp0", archivePath, "*");

    public static ArchiveListing List(string archivePath) => ParseTechnicalListing(Run(null, "l", "-slt", archivePath));

    /// <summary>One entry's bytes, written by 7-Zip to its output (null if the archive has no such entry).</summary>
    public static byte[]? ReadEntry(string archivePath, string entry)
    {
        var exe = Tools.FindSevenZip() ?? throw new InvalidOperationException(
            "7-Zip is required for .sd7 archives but was not found. Install it from https://www.7-zip.org or use .sdz.");
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "e", archivePath, "-so", "-r-", entry })
            psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start 7-Zip.");
        using var data = new MemoryStream();
        var stderr = process.StandardError.ReadToEndAsync();
        process.StandardOutput.BaseStream.CopyTo(data);
        process.WaitForExit();
        _ = stderr.Result;
        if (process.ExitCode != 0)
            throw new IOException($"7-Zip could not read {entry} from {archivePath} (exit code {process.ExitCode}).");
        return data.Length == 0 ? null : data.ToArray();
    }

    /// <summary>Parses the output of <c>7z l -slt</c>: archive properties, a dashed line, then one block per entry.</summary>
    internal static ArchiveListing ParseTechnicalListing(string output)
    {
        var lines = output.Replace("\r", "").Split('\n');
        int split = Array.FindIndex(lines, l => l.StartsWith("----------", StringComparison.Ordinal));
        if (split < 0)
            throw new InvalidDataException("Unexpected 7-Zip listing format (no entry separator).");

        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        int archiveStart = Array.FindIndex(lines, l => l == "--");
        for (int i = archiveStart + 1; i < split; i++)
            if (TrySplit(lines[i], out var key, out var value))
                properties[key] = value;

        var entries = new List<ArchiveEntry>();
        var block = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = split + 1; i <= lines.Length; i++)
        {
            if (i < lines.Length && TrySplit(lines[i], out var key, out var value))
            {
                block[key] = value;
                continue;
            }
            if (block.ContainsKey("Path"))
                entries.Add(ToEntry(block));
            block.Clear();
        }
        return new ArchiveListing(properties, entries);
    }

    static ArchiveEntry ToEntry(Dictionary<string, string> block)
    {
        var attributes = ParseAttributes(block.GetValueOrDefault("Attributes", ""));
        bool isDir = block.GetValueOrDefault("Folder") == "+" || attributes.HasFlag(FileAttributes.Directory);
        long size = long.TryParse(block.GetValueOrDefault("Size"), NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : 0;
        uint? crc = uint.TryParse(block.GetValueOrDefault("CRC"), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var c) ? c : null;
        if (isDir || size == 0)
            crc = null;
        return new ArchiveEntry(block["Path"].Replace('\\', '/').TrimEnd('/'), isDir, isDir ? 0 : size, crc,
            attributes | (isDir ? FileAttributes.Directory : 0));
    }

    /// <summary>Windows attribute letters as 7-Zip prints them, e.g. "D", "A", "HSA". Unix mode text after a space is ignored.</summary>
    static FileAttributes ParseAttributes(string text)
    {
        var result = (FileAttributes)0;
        foreach (char ch in text.Split(' ')[0])
            result |= ch switch
            {
                'D' => FileAttributes.Directory,
                'R' => FileAttributes.ReadOnly,
                'H' => FileAttributes.Hidden,
                'S' => FileAttributes.System,
                'A' => FileAttributes.Archive,
                _ => 0,
            };
        return result;
    }

    static bool TrySplit(string line, out string key, out string value)
    {
        int eq = line.IndexOf(" = ", StringComparison.Ordinal);
        if (eq < 0 && line.EndsWith(" =", StringComparison.Ordinal))
            eq = line.Length - 2;
        if (eq <= 0)
        {
            key = value = "";
            return false;
        }
        key = line[..eq];
        value = eq + 3 <= line.Length ? line[(eq + 3)..] : "";
        return true;
    }

    static string Run(string? workingDir, params string[] args)
    {
        var exe = Tools.FindSevenZip() ?? throw new InvalidOperationException(
            "7-Zip is required for .sd7 archives but was not found. Install it from https://www.7-zip.org or use .sdz.");

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        psi.ArgumentList.Add("-sccUTF-8");

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start 7-Zip.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new IOException($"7-Zip '{args[0]}' failed (exit code {process.ExitCode}): {stderr.Trim()}");
        return stdout.Result;
    }
}
