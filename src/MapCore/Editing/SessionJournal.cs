using System.Text;
using System.Text.Json;

namespace MapCore.Editing;

/// <summary>
/// One thing a session did: apply (with the operation text), undo, redo, begin (a group, with its name), end, save
/// (into the workspace), packed (the workspace written to an archive, with its path; nothing to replay).
/// </summary>
public sealed record JournalEntry(string Kind, string? Text = null);

/// <summary>
/// A running record of everything a session does, one JSON line per event, flushed as it happens. After a crash the
/// session is rebuilt by replaying the journal onto a fresh copy of the source archive — operations are deterministic,
/// so the result is the same session, undo history included. Files an operation reads (imported images) are copied
/// next to the journal first, so the replay doesn't depend on them staying where they were.
/// </summary>
public sealed class SessionJournal : IDisposable
{
    readonly StreamWriter writer;
    int copies;

    /// <param name="path">The journal file (appended to if it exists).</param>
    /// <param name="filesFolder">Where copies of imported files go.</param>
    public SessionJournal(string path, string filesFolder)
    {
        Path = path;
        FilesFolder = filesFolder;
        Directory.CreateDirectory(filesFolder);
        copies = Directory.EnumerateFiles(filesFolder).Count();
        writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
    }

    public string Path { get; }
    public string FilesFolder { get; }

    public void Write(JournalEntry entry)
    {
        writer.WriteLine(JsonSerializer.Serialize(entry));
        writer.Flush(); // to the operating system at once: a crash of the editor loses nothing
    }

    /// <summary>Copies a file an operation reads next to the journal; returns the copy's path.</summary>
    public string KeepCopy(string file)
    {
        var copy = System.IO.Path.Combine(FilesFolder, $"{copies++:D4}{System.IO.Path.GetExtension(file)}");
        File.Copy(file, copy, overwrite: true);
        return copy;
    }

    public void Dispose() => writer.Dispose();

    /// <summary>Writes a whole journal at once (replacing the file).</summary>
    public static void WriteAll(string path, IEnumerable<JournalEntry> entries) =>
        File.WriteAllLines(path, entries.Select(e => JsonSerializer.Serialize(e)), new UTF8Encoding(false));

    /// <summary>The entries of a journal; a last line cut short by a crash is ignored.</summary>
    public static IReadOnlyList<JournalEntry> Read(string path)
    {
        var entries = new List<JournalEntry>();
        using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), Encoding.UTF8);
        while (reader.ReadLine() is string line)
        {
            if (line.Length == 0)
                continue;
            try
            {
                if (JsonSerializer.Deserialize<JournalEntry>(line) is { Kind: not null } entry)
                    entries.Add(entry);
            }
            catch (JsonException)
            {
                break; // torn last line
            }
        }
        return entries;
    }

    /// <summary>Plays entries onto a session (which should not be journaling meanwhile).</summary>
    public static void Replay(MapEditSession session, IReadOnlyList<JournalEntry> entries, IProgress<string>? progress = null)
    {
        session.Prime(); // as the recorded session was when its journal was attached
        for (int i = 0; i < entries.Count; i++)
        {
            if (i % 25 == 0)
                progress?.Report($"Replaying edit {i + 1} of {entries.Count}…");
            var e = entries[i];
            switch (e.Kind)
            {
                // As recorded: a mirrored edit's mirror image is its own entry, so nothing is mirrored again here.
                case "apply": session.ApplyOne(e.Text ?? throw new InvalidDataException("A journal apply entry has no operation.")); break;
                case "undo": session.Undo(); break;
                case "redo": session.Redo(); break;
                case "begin": session.BeginGroup(e.Text ?? "edit"); break;
                case "end": session.EndGroup(); break;
                case "save": session.Save(); break;
                case "packed": break;
                default: throw new InvalidDataException($"Unknown journal entry '{e.Kind}'.");
            }
        }
    }
}
