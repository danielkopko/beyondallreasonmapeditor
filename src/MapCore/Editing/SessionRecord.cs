using System.Text.Json;

namespace MapCore.Editing;

/// <summary>The map a session started from, as a recovery needs it: the archive, and how to tell if it changed since.</summary>
public sealed record SessionManifest(string Archive, long ArchiveLength, DateTime ArchiveWriteTimeUtc, string? MapName, string? MapVersion, DateTime OpenedUtc)
{
    public static SessionManifest For(string archive, string? mapName, string? mapVersion)
    {
        var info = new FileInfo(archive);
        return new SessionManifest(info.FullName, info.Length, info.LastWriteTimeUtc, mapName, mapVersion, DateTime.UtcNow);
    }

    /// <summary>Null if the archive is still there and unchanged; otherwise why the edits cannot be replayed onto it.</summary>
    public string? ArchiveProblem()
    {
        var info = new FileInfo(Archive);
        if (!info.Exists)
            return $"{Archive} is no longer there.";
        if (info.Length != ArchiveLength || info.LastWriteTimeUtc != ArchiveWriteTimeUtc)
            return $"{Archive} has changed since the edits were made, and they only replay onto the map they were made on.";
        return null;
    }
}

/// <summary>
/// The recovery record of an open map, kept beside its workspace in "&lt;workspace&gt;.session": the manifest, the
/// journal, copies of imported files and a lock file that stays open while the map does. When the editor stops without
/// closing the map, the lock goes with it and the record is left behind for <see cref="SessionRecovery.FindOrphans"/>.
/// </summary>
public sealed class SessionRecord : IDisposable
{
    internal const string ManifestName = "manifest.json", JournalName = "session.journal", FilesName = "files", LockName = "lock";
    readonly FileStream lockFile;

    SessionRecord(string workspace, FileStream lockFile)
    {
        Workspace = workspace;
        this.lockFile = lockFile;
        var folder = FolderFor(workspace);
        Journal = new SessionJournal(Path.Combine(folder, JournalName), Path.Combine(folder, FilesName));
    }

    public string Workspace { get; }
    public string Folder => FolderFor(Workspace);
    public SessionJournal Journal { get; }

    public static string FolderFor(string workspace) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace)) + ".session";

    /// <summary>Starts the record of a map just opened into <paramref name="workspace"/>.</summary>
    public static SessionRecord Start(string workspace, SessionManifest manifest)
    {
        var folder = FolderFor(workspace);
        Directory.CreateDirectory(folder);
        var lockFile = TakeLock(folder) ?? throw new IOException($"{folder} is in use by another copy of the editor.");
        try
        {
            File.WriteAllText(Path.Combine(folder, ManifestName), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            File.Delete(Path.Combine(folder, JournalName));
            return new SessionRecord(workspace, lockFile);
        }
        catch
        {
            lockFile.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Takes over the record of a session being recovered. The journal is rewritten as it was read (without a last
    /// line torn by the crash, which would hide everything written after it) and the recovered session continues it.
    /// </summary>
    public static SessionRecord Resume(OrphanSession orphan)
    {
        var folder = FolderFor(orphan.Workspace);
        var lockFile = TakeLock(folder) ?? throw new IOException($"{folder} is in use by another copy of the editor.");
        try
        {
            var journal = Path.Combine(folder, JournalName);
            SessionJournal.WriteAll(journal + ".tmp", orphan.Entries);
            File.Move(journal + ".tmp", journal, overwrite: true);
            return new SessionRecord(orphan.Workspace, lockFile);
        }
        catch
        {
            lockFile.Dispose();
            throw;
        }
    }

    /// <summary>Releases the journal and the lock; the files stay (the caller deletes them when the map is closed).</summary>
    public void Dispose()
    {
        Journal.Dispose();
        lockFile.Dispose();
    }

    /// <summary>The lock of a record folder, or null if another editor holds it.</summary>
    internal static FileStream? TakeLock(string folder)
    {
        try
        {
            return new FileStream(Path.Combine(folder, LockName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>A session record no running editor holds: a map that was open when the editor stopped.</summary>
/// <param name="Manifest">Null when the manifest could not be read (then the session cannot be recovered).</param>
public sealed record OrphanSession(string Workspace, SessionManifest? Manifest, IReadOnlyList<JournalEntry> Entries, DateTime LastWriteUtc)
{
    public string Folder => SessionRecord.FolderFor(Workspace);

    /// <summary>
    /// Edits, undos and redos since the map was last written to an archive: what is lost unless the session is
    /// recovered. A save into the workspace doesn't count until the archive is written ("packed"), as packing can fail.
    /// </summary>
    public int UnsavedEdits => Entries.Skip(LastIndexOf("packed") + 1).Count(e => e.Kind is "apply" or "undo" or "redo");

    /// <summary>The workspace was saved after the last archive was written: a Save As that didn't finish packing.</summary>
    public bool SavedButNotPacked => LastIndexOf("save") > LastIndexOf("packed");

    int LastIndexOf(string kind)
    {
        for (int i = Entries.Count - 1; i >= 0; i--)
            if (Entries[i].Kind == kind)
                return i;
        return -1;
    }
}

public static class SessionRecovery
{
    /// <summary>
    /// Session records under <paramref name="root"/> whose lock can be taken, newest first. A record without a
    /// manifest is left alone for its first minutes, in case an editor is only now starting it.
    /// </summary>
    public static IReadOnlyList<OrphanSession> FindOrphans(string root)
    {
        var found = new List<OrphanSession>();
        if (!Directory.Exists(root))
            return found;
        foreach (var folder in Directory.EnumerateDirectories(root, "*.session"))
        {
            using var lockFile = SessionRecord.TakeLock(folder);
            if (lockFile == null)
                continue; // an editor has this map open
            var workspace = folder[..^".session".Length];
            var manifestPath = Path.Combine(folder, SessionRecord.ManifestName);
            var journalPath = Path.Combine(folder, SessionRecord.JournalName);
            SessionManifest? manifest = null;
            try
            {
                manifest = JsonSerializer.Deserialize<SessionManifest>(File.ReadAllText(manifestPath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                if (DateTime.UtcNow - Directory.GetCreationTimeUtc(folder) < TimeSpan.FromMinutes(5))
                    continue;
            }
            IReadOnlyList<JournalEntry> entries = [];
            try
            {
                if (File.Exists(journalPath))
                    entries = SessionJournal.Read(journalPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            var lastWrite = File.Exists(journalPath) ? File.GetLastWriteTimeUtc(journalPath) : Directory.GetLastWriteTimeUtc(folder);
            found.Add(new OrphanSession(workspace, manifest, entries, lastWrite));
        }
        return found.OrderByDescending(o => o.LastWriteUtc).ToList();
    }
}
