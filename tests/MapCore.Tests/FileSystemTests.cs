namespace MapCore.Tests;

static class FileSystemTests
{
    [Test]
    public static void DeleteTreeRemovesReadOnlyHiddenAndSystemEntries()
    {
        var root = TestData.NewWorkDir("delete");
        var folder = Directory.CreateDirectory(Path.Combine(root, "locked"));
        var file = Path.Combine(folder.FullName, "desktop.ini");
        File.WriteAllText(file, "[.ShellClassInfo]");
        File.SetAttributes(file, FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReadOnly);
        folder.Attributes |= FileAttributes.ReadOnly;

        FileSystemUtil.DeleteTree(root);
        Assert.True(!Directory.Exists(root), "folder tree deleted");
    }

    /// <summary>
    /// Temporary folders (MapTool's unpacked maps and the like) left behind by a run that was killed or crashed are
    /// deleted at the next start: their lock is there and no process holds it. Folders in use are left alone.
    /// </summary>
    [Test]
    public static void TempFoldersLeftByEndedRunsAreSweptAway()
    {
        var root = TestData.NewWorkDir("temp-sweep");
        // This process's own, in use: its lock is beside it, held.
        var mine = FileSystemUtil.NewTempDir("maptool", root);
        File.WriteAllText(Path.Combine(mine, "mapinfo.lua"), "return {}");
        Assert.True(File.Exists(mine + ".lock") && !File.Exists(Path.Combine(mine, ".lock")), "the lock is beside the folder, not in it (a map is packed from it)");
        // Another run's, still going: its lock held (here by a handle of this process's own, as that run would hold it).
        var alive = Path.Combine(root, "collect-alive");
        Directory.CreateDirectory(alive);
        var otherRun = new FileStream(alive + ".lock", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        // A run that was killed: folder and lock left, the lock held by no one (Windows closes it as the process ends).
        var killed = Path.Combine(root, "maptool-killed");
        var readOnly = Directory.CreateDirectory(Path.Combine(killed, "mapconfig", "featureplacer"));
        File.WriteAllText(Path.Combine(readOnly.FullName, "set.lua"), "return {}");
        readOnly.Attributes |= FileAttributes.ReadOnly; // as Acidic Quarry has it
        File.WriteAllText(killed + ".lock", "");
        // Made by an editor from before the locks: none; one old, one just made.
        var old = Directory.CreateDirectory(Path.Combine(root, "collect-old")).FullName;
        Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-2));
        var recent = Directory.CreateDirectory(Path.Combine(root, "collect-recent")).FullName;
        // A lock whose folder was deleted some other way.
        File.WriteAllText(Path.Combine(root, "repack-gone.lock"), "");

        int swept = FileSystemUtil.SweepTempDirs(root);
        Assert.True(swept == 2 && !Directory.Exists(killed) && !File.Exists(killed + ".lock") && !Directory.Exists(old) &&
                    !File.Exists(Path.Combine(root, "repack-gone.lock")),
            $"the killed run's folder and lock, the old unlocked folder and the stray lock go ({swept} folders)");
        Assert.True(Directory.Exists(mine) && Directory.Exists(alive) && File.Exists(alive + ".lock") && Directory.Exists(recent),
            "this process's folder, a running process's and a just-made unlocked one stay");

        FileSystemUtil.DeleteTree(mine);
        Assert.True(!Directory.Exists(mine) && !File.Exists(mine + ".lock"), "deleting its own folder takes its lock with it");
        otherRun.Dispose(); // that run ends without deleting its folder
        Assert.True(FileSystemUtil.SweepTempDirs(root) == 1 && !Directory.Exists(alive) && !File.Exists(alive + ".lock"),
            "once that run has ended, its folder goes at the next sweep");
    }

    [Test]
    public static void AtomicWriteWaitsOutABriefLock()
    {
        // As a virus scanner does just after a write: the file is open, and it can't be replaced until it's closed.
        var root = TestData.NewWorkDir("atomic");
        var file = Path.Combine(root, "mapinfo.lua");
        File.WriteAllText(file, "old");
        var scanner = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        var released = Task.Run(async () => { await Task.Delay(200); scanner.Dispose(); });

        FileSystemUtil.WriteAllBytesAtomic(file, "new"u8.ToArray());
        released.Wait();
        Assert.Equal("new", File.ReadAllText(file), "the file once the lock went");
        Assert.Equal(1, Directory.GetFiles(root).Length, "files left in the folder (no .partial)");
    }

    [Test]
    public static void AtomicWriteReportsALockThatStays()
    {
        var root = TestData.NewWorkDir("atomic");
        var file = Path.Combine(root, "mapinfo.lua");
        File.WriteAllText(file, "old");
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<Exception>(() => FileSystemUtil.WriteAllBytesAtomic(file, "new"u8.ToArray()), "writing over a file held open");
        Assert.Equal("old", File.ReadAllText(file), "the file is untouched");
        Assert.Equal(1, Directory.GetFiles(root).Length, "files left in the folder (no .partial)");
    }
}
