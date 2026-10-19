using System.Globalization;
using MapCore.Editing;
using MapCore.Imaging;

namespace MapCore.Tests;

static class JournalTests
{
    static MapEditSession Fresh(string name) =>
        MapEditSession.Open(MapPackage.Open(TestData.PathOf(TestData.AcidicQuarryArchive), TestData.NewWorkDir(name)));

    static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

    /// <summary>Every store, plus what undo would do next and whether there is anything unsaved.</summary>
    static string State(MapEditSession s) => string.Join("|",
        Hash(s.Smf.ToBytes()), Hash(s.Texture.TileFiles[0].ToBytes()), s.Info.Text, s.Items.Placer!.Document.Text,
        Hash(s.Heights.Heights.SelectMany(BitConverter.GetBytes).ToArray()), Hash(s.ResourceBytes(MapEditSession.SplatKey)),
        s.UndoDescription ?? "-", s.CanRedo, s.HasUnsavedChanges);

    static string At(double x, double z) => string.Create(CultureInfo.InvariantCulture, $"{x},{z}");

    /// <summary>
    /// A session is rebuilt from its journal: a scatter, a move with the look, a paint stroke, undo and redo, an import
    /// (whose file is gone by the time of the replay), a save and edits after it come out byte for byte the same, and
    /// the rebuilt session undoes the same way.
    /// </summary>
    [Test]
    public static void ReplayRebuildsTheSession()
    {
        var dir = TestData.NewWorkDir("journal");
        var journalPath = Path.Combine(dir, "session.journal");
        var live = Fresh("journal-live");
        var spot = live.Metal.FindSpots()[3];
        var other = live.Metal.FindSpots()[7];

        var pngDir = TestData.NewWorkDir("journal-png");
        var heightsPng = Path.Combine(pngDir, "heights.png");
        live.ExportLayer("heights", heightsPng);
        var heights = PngReader.Read(heightsPng);
        var raw = heights.ToGray16();
        for (int i = 200 * 769 + 200; i < 200 * 769 + 260; i++)
            raw[i] = (ushort)Math.Min(65535, raw[i] + 2000);
        PngWriter.WriteGray16(heightsPng, heights.Width, heights.Height, raw, heights.Text);

        using (var journal = new SessionJournal(journalPath, Path.Combine(dir, "files")))
        {
            _ = live.LookRadius; // the editor may look at things before the first edit; the replay doesn't
            live.Journal = journal;
            live.BeginGroup("scatter");
            for (int i = 0; i < 12; i++)
                live.Apply($"item-add:placer:agorm_rock1:{At(2000 + i * 37, 2500 + i * 53 % 300)}");
            live.EndGroup();
            // Erasing a spot's splat before its first move with the look: the look is measured from the map as opened.
            live.Apply($"splat-paint:{At(other.X, other.Z)}:90:0,0,0,0:1:1");
            live.Apply($"metal-move:{At(other.X, other.Z)}:{At(other.X + 80, other.Z + 30)}:look");
            live.Apply($"metal-move:{At(spot.X, spot.Z)}:{At(spot.X + 96, spot.Z - 40)}:look");
            live.BeginGroup("paint");
            for (int i = 0; i < 6; i++)
                live.Apply($"texture-paint:{At(1500 + i * 15, 900)}:40:30,120,200:0.5:0.6");
            live.EndGroup();
            live.Apply("ramp:4300,3000:4300,3500:120:auto,auto:auto:1");
            live.Apply("height-raise:3072,3072:200:30");
            live.Undo();
            live.Redo();
            live.Undo();
            live.ImportLayer("heights", heightsPng);
            live.Save();
            journal.Write(new JournalEntry("packed", "somewhere.sd7")); // as the editor writes once the archive is written
            live.Apply("splat-paint:1500,900:120:0,255,0,0:0.8:0.9");
            live.Apply("set:atmosphere.fogColor=0.1,0.2,0.3");
            live.Undo(); // the last thing done is an undo, with a redo waiting
            live.Journal = null;
        }
        File.Delete(heightsPng);

        // A crash mid-write leaves a torn last line, which is ignored.
        File.AppendAllText(journalPath, "{\"Kind\":\"app");
        var entries = SessionJournal.Read(journalPath);
        Assert.Equal(12 + 2 + 3 + 6 + 2 + 1 + 1 + 3 + 1 + 1 + 1 + 2 + 1, entries.Count, "entries read, torn line left out");

        var rebuilt = Fresh("journal-rebuilt");
        var messages = new List<string>();
        SessionJournal.Replay(rebuilt, entries, new Progress<string>(messages.Add));
        Assert.Equal(State(live), State(rebuilt), "the replayed session matches the live one");

        live.Redo();
        rebuilt.Redo();
        Assert.Equal(State(live), State(rebuilt), "the waiting redo is the same");
        for (int i = 0; i < 4; i++)
        {
            live.Undo();
            rebuilt.Undo();
        }
        Assert.Equal(State(live), State(rebuilt), "undoing back through the save, the import and the stroke stays in step");
    }

    /// <summary>
    /// A record is an orphan only once its editor lets go of the lock; it counts the edits since the last save, notices
    /// a changed archive, and a resumed record drops a torn last line so what follows can be read.
    /// </summary>
    [Test]
    public static void OrphanedRecordsAreFoundOnlyWhenNoEditorHoldsThem()
    {
        var root = TestData.NewWorkDir("journal-orphans");
        var archive = Path.Combine(root, "map.sd7");
        File.WriteAllBytes(archive, [1, 2, 3]);
        var workspace = Path.Combine(root, "abc");
        Directory.CreateDirectory(workspace);

        var record = SessionRecord.Start(workspace, SessionManifest.For(archive, "Map", "1"));
        foreach (var kind in new[] { "apply", "apply", "save", "apply", "undo", "begin", "apply", "end" })
            record.Journal.Write(new JournalEntry(kind, kind == "apply" ? "set:name=\"x\"" : null));
        Assert.Equal(0, SessionRecovery.FindOrphans(root).Count, "orphans while the lock is held");
        record.Dispose();

        File.AppendAllText(Path.Combine(record.Folder, "session.journal"), "{\"Kind\":\"ap");
        var orphan = SessionRecovery.FindOrphans(root).Single();
        Assert.True(FileSystemUtil.SamePath(orphan.Workspace, workspace), "the orphan names its workspace");
        Assert.Equal(5, orphan.UnsavedEdits, "every apply and undo: the save was never packed into an archive");
        Assert.True(orphan.Manifest?.ArchiveProblem() == null, "the archive is unchanged");

        using (var resumed = SessionRecord.Resume(orphan))
        {
            resumed.Journal.Write(new JournalEntry("redo"));
            Assert.Equal(0, SessionRecovery.FindOrphans(root).Count, "orphans once a resumed record holds the lock again");
        }
        var entries = SessionJournal.Read(Path.Combine(record.Folder, "session.journal"));
        Assert.Equal(9, entries.Count, "the torn line is gone and the entry written after it reads");
        Assert.Equal("redo", entries[^1].Kind, "the last entry");

        File.WriteAllBytes(archive, [1, 2, 3, 4]);
        Assert.True(SessionRecovery.FindOrphans(root).Single().Manifest!.ArchiveProblem() is string p && p.Contains("changed", StringComparison.Ordinal),
            "a changed archive is noticed");
        File.Delete(archive);
        Assert.True(SessionRecovery.FindOrphans(root).Single().Manifest!.ArchiveProblem()?.Contains("no longer", StringComparison.Ordinal) == true,
            "a missing archive is noticed");

        Directory.CreateDirectory(Path.Combine(root, "starting.session"));
        Assert.Equal(1, SessionRecovery.FindOrphans(root).Count, "a record just created, without a manifest yet, is left alone");
    }

    /// <summary>Saving into the workspace isn't enough: edits count as saved once an archive is written ("packed").</summary>
    [Test]
    public static void OnlyAWrittenArchiveCountsAsSaved()
    {
        static OrphanSession With(params string[] kinds) => new("w", null, kinds.Select(k => new JournalEntry(k)).ToList(), DateTime.UtcNow);
        Assert.Equal(2, With("apply", "begin", "apply", "end").UnsavedEdits, "never saved");
        Assert.Equal(2, With("apply", "apply", "save").UnsavedEdits, "saved into the workspace, but packing never finished");
        Assert.True(With("apply", "save").SavedButNotPacked, "a save without its archive is noticed");
        Assert.Equal(0, With("apply", "save", "packed").UnsavedEdits, "saved and packed");
        Assert.True(!With("apply", "save", "packed").SavedButNotPacked, "and nothing is pending");
        Assert.Equal(1, With("apply", "save", "packed", "undo").UnsavedEdits, "an undo after packing");
    }

    [Test]
    public static void AnEmptyOrTornJournalReadsAsFarAsItCan()
    {
        var dir = TestData.NewWorkDir("journal-torn");
        var path = Path.Combine(dir, "j.journal");
        File.WriteAllText(path, "");
        Assert.Equal(0, SessionJournal.Read(path).Count, "an empty journal has no entries");
        using (var j = new SessionJournal(path, Path.Combine(dir, "files")))
        {
            j.Write(new JournalEntry("begin", "a \"quoted\" name: with, colons"));
            j.Write(new JournalEntry("end"));
        }
        File.AppendAllText(path, "{\"Kind\":\"undo\"}\n{not json\n{\"Kind\":\"redo\"}\n");
        var entries = SessionJournal.Read(path);
        Assert.Equal(3, entries.Count, "reading stops at the first broken line");
        Assert.Equal("a \"quoted\" name: with, colons", entries[0].Text, "text survives the round trip");
    }
}
