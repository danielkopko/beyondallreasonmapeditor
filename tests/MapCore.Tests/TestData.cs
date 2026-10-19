using MapCore.Archives;

namespace MapCore.Tests;

/// <summary>Locates the repository and the test map copies under testdata/, and manages scratch folders.</summary>
static class TestData
{
    public const string AcidicQuarryArchive = "acidicquarry_5.17.sd7";

    /// <summary>Repository root: the nearest parent folder holding PLAN.md.</summary>
    public static string RepoRoot { get; } = FindRepoRoot();

    public static string Dir => Path.Combine(RepoRoot, "testdata");

    public static string PathOf(string fileName) => Path.Combine(Dir, fileName);

    static readonly List<string> WorkDirs = [];

    static readonly Lazy<MapPackage> acidicQuarry =
        new(() => MapPackage.Open(PathOf(AcidicQuarryArchive), NewWorkDir("aq-shared")));

    static readonly Lazy<IReadOnlyList<ArchiveEntry>> acidicQuarryListing =
        new(() => ArchiveIO.List(PathOf(AcidicQuarryArchive)));

    /// <summary>The test map extracted once per run. Read-only: tests must not modify it.</summary>
    public static MapPackage AcidicQuarry => acidicQuarry.Value;

    /// <summary>A texture from the extracted test map's maps/ folder (read-only).</summary>
    public static string MapTexture(string fileName) => Path.Combine(AcidicQuarry.Root, "maps", fileName);

    /// <summary>7-Zip's own listing of the original test archive.</summary>
    public static IReadOnlyList<ArchiveEntry> AcidicQuarryListing => acidicQuarryListing.Value;

    static TestData() =>
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            foreach (var dir in WorkDirs)
            {
                try
                {
                    FileSystemUtil.DeleteTree(dir);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"warning: could not delete test folder {dir}: {ex.Message}");
                }
            }
        };

    /// <summary>A new empty folder under out/tests, deleted when the test run ends.</summary>
    public static string NewWorkDir(string name)
    {
        var dir = Path.Combine(RepoRoot, "out", "tests", $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        lock (WorkDirs)
            WorkDirs.Add(dir);
        return dir;
    }

    static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "PLAN.md")))
                return dir.FullName;
        throw new InvalidOperationException("Could not find the repository root (PLAN.md) above " + AppContext.BaseDirectory);
    }
}
