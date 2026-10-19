namespace MapCore;

/// <summary>File-system helpers that treat hidden and system files (such as desktop.ini) like any other file.</summary>
public static class FileSystemUtil
{
    /// <summary>Recursive enumeration including hidden and system files, which .NET skips by default.</summary>
    public static EnumerationOptions AllEntries => new() { RecurseSubdirectories = true, AttributesToSkip = 0 };

    /// <summary>Path of <paramref name="fullPath"/> relative to <paramref name="root"/>, with '/' separators.</summary>
    public static string EntryPath(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace('\\', '/');

    public static bool IsMissingOrEmpty(string dir) =>
        !Directory.Exists(dir) ||
        !Directory.EnumerateFileSystemEntries(dir, "*", new EnumerationOptions { AttributesToSkip = 0 }).Any();

    /// <summary>True if <paramref name="path"/> is <paramref name="root"/> or lies inside it.</summary>
    public static bool IsSameOrUnder(string path, string root)
    {
        var p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var r = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return p.Equals(r, StringComparison.OrdinalIgnoreCase) ||
               p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static bool SamePath(string a, string b) =>
        Path.GetFullPath(a).Equals(Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Deletes a folder tree, first clearing read-only/hidden/system flags on files and folders.
    /// Map archives can carry these (Acidic Quarry stores mapconfig/featureplacer as a read-only folder).
    /// </summary>
    public static void DeleteTree(string dir)
    {
        if (Directory.Exists(dir))
        {
            const FileAttributes blocking = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System;
            foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", AllEntries).Append(new DirectoryInfo(dir)))
                if ((entry.Attributes & blocking) != 0)
                    entry.Attributes &= ~blocking;
            Directory.Delete(dir, recursive: true);
        }
        ReleaseTempLock(dir); // a temporary folder's lock, once the folder is gone
    }

    /// <summary>
    /// Writes a file through a temporary file in the same folder, then swaps it into place, so a failed
    /// write never leaves a half-written file. A read-only flag on the existing file is cleared first.
    /// </summary>
    const int SwapAttempts = 6;

    public static void WriteAllBytesAtomic(string path, byte[] bytes)
    {
        var target = Path.GetFullPath(path);
        var temp = $"{target}.{Guid.NewGuid():N}.partial";
        try
        {
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(target) && File.GetAttributes(target).HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(target, File.GetAttributes(target) & ~FileAttributes.ReadOnly);
            // A virus scanner or the search indexer can hold a file open for a moment after it is written, and
            // Windows refuses to swap it then. Wait briefly (0.75 s at most) before reporting the failure.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(temp, target, overwrite: true);
                    break;
                }
                catch (Exception ex) when ((ex is UnauthorizedAccessException or IOException) && attempt < SwapAttempts)
                {
                    Thread.Sleep(50 * attempt);
                }
            }
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    /// <summary>
    /// Throws a plain-spoken <see cref="IOException"/> if the drive holding <paramref name="dir"/> has less than
    /// <paramref name="bytes"/> free. Drives whose free space cannot be read (network shares) are not checked.
    /// </summary>
    public static void EnsureFreeSpace(string dir, long bytes, string what)
    {
        long free;
        try
        {
            free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))!).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return;
        }
        if (free < bytes)
            throw new IOException($"Not enough disk space for {what}: it needs about {bytes / 1048576.0:N0} MB on " +
                                  $"{Path.GetPathRoot(Path.GetFullPath(dir))}, which has {free / 1048576.0:N0} MB free.");
    }

    /// <summary>Where temporary folders go: %TEMP%\BarMapEditor.</summary>
    public static string TempRoot => Path.Combine(Path.GetTempPath(), "BarMapEditor");

    // Each temporary folder has a lock file beside it (<folder>.lock), held open, unshared, by the process using the
    // folder until DeleteTree deletes it. A process that ends without deleting its folder (killed, stopped with
    // Ctrl+C, crashed) lets go of the lock as it ends, however it ends: so the next start can tell the folder was
    // abandoned (SweepTempDirs). Beside the folder, not in it: a map is unpacked into the folder and packed again
    // from it, and the lock mustn't go with it.
    static readonly Dictionary<string, FileStream> heldLocks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A new, empty folder under <see cref="TempRoot"/> (or <paramref name="root"/>), locked as this process's until <see cref="DeleteTree"/> deletes it.</summary>
    public static string NewTempDir(string purpose, string? root = null)
    {
        root ??= TempRoot;
        Directory.CreateDirectory(root);
        var dir = Path.GetFullPath(Path.Combine(root, $"{purpose}-{Guid.NewGuid():N}"));
        // The lock first: a folder without one is taken for one from an older editor (see SweepTempDirs).
        var held = new FileStream(dir + ".lock", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        lock (heldLocks)
            heldLocks[dir] = held;
        Directory.CreateDirectory(dir);
        return dir;
    }

    static void ReleaseTempLock(string dir)
    {
        FileStream? held;
        var full = Path.GetFullPath(dir);
        lock (heldLocks)
            if (heldLocks.Remove(full, out held))
                held.Dispose();
            else
                return;
        try
        {
            File.Delete(full + ".lock");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for the next sweep.
        }
    }

    /// <summary>
    /// Deletes the temporary folders under <see cref="TempRoot"/> (or <paramref name="root"/>) that processes ended
    /// without deleting: their lock is there and no process holds it. A folder with no lock (made before there were
    /// locks) goes once it is <paramref name="unlockedAge"/> old (an hour unless given). Folders in use are left
    /// alone, and so is anything that can't be deleted now (the next sweep tries again). Returns how many went.
    /// </summary>
    public static int SweepTempDirs(string? root = null, TimeSpan? unlockedAge = null)
    {
        root ??= TempRoot;
        if (!Directory.Exists(root))
            return 0;
        var age = unlockedAge ?? TimeSpan.FromHours(1);
        int deleted = 0;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var full = Path.GetFullPath(dir);
            lock (heldLocks)
                if (heldLocks.ContainsKey(full))
                    continue; // this process's own
            var lockFile = full + ".lock";
            try
            {
                if (File.Exists(lockFile))
                {
                    using (new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) // fails while its process lives
                    {
                    }
                }
                else if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(full) < age)
                    continue;
                DeleteTree(full);
                File.Delete(lockFile);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // In use, or not deletable just now.
            }
        }
        // Locks left without their folder (deleted some other way), held by no one.
        foreach (var lockFile in Directory.EnumerateFiles(root, "*.lock"))
        {
            if (Directory.Exists(lockFile[..^".lock".Length]))
                continue;
            try
            {
                File.Delete(lockFile); // fails while its process holds it
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        return deleted;
    }
}
