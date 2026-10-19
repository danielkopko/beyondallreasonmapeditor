namespace MapCore;

/// <summary>Locates external tools the editor relies on.</summary>
public static class Tools
{
    static readonly string[] SevenZipCandidates =
    [
        @"C:\Program Files\7-Zip\7z.exe",
        @"C:\Program Files (x86)\7-Zip\7z.exe",
    ];

    /// <summary>Full path to 7z.exe, or null if 7-Zip is not installed.</summary>
    /// <remarks>Needed for .sd7 archives only; .sdz uses the built-in zip support.</remarks>
    public static string? FindSevenZip()
    {
        foreach (var path in SevenZipCandidates)
            if (File.Exists(path))
                return path;

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "7z.exe");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
