using System.Diagnostics;
using MapCore.Editing;
using MapCore.WorldMachine;

namespace MapEditor;

/// <summary>
/// World Machine, from the Beautify panel and File › World Machine: each map's exchange folder (remembered, in
/// Documents\BAR Map Editor\World Machine unless chosen), sending the map there, bringing World Machine's outputs back
/// (by hand; as it writes them, with the folder watched; or after a build the editor starts, with World Machine
/// Professional's automation). Sending and bringing back run in the background; each bring-back is one undo step.
/// </summary>
sealed partial class MainForm
{
    FileSystemWatcher? wmWatcher;
    readonly System.Windows.Forms.Timer wmSettle = new() { Interval = 1500 };
    readonly Dictionary<string, (long Length, DateTime Written)> wmPending = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, DateTime> wmBrought = new(StringComparer.OrdinalIgnoreCase);
    DateTime? wmBuildStarted;
    Process? wmProcess;

    /// <summary>How long a build started from here is waited on for its outputs.</summary>
    static readonly TimeSpan WorldMachineBuildWait = TimeSpan.FromMinutes(30);

    /// <summary>The open map's exchange folder: chosen for it, or Documents\BAR Map Editor\World Machine\&lt;archive&gt;.</summary>
    internal string? WorldMachineFolder
    {
        get
        {
            if (map == null)
                return null;
            var key = Path.GetFileNameWithoutExtension(map.Archive);
            if (settings.WorldMachineFolders.TryGetValue(key, out var chosen))
                return chosen;
            var name = string.Concat(key.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BAR Map Editor", "World Machine", name);
        }
    }

    /// <summary>Sets the open map's exchange folder (remembered, but not by the self-test).</summary>
    internal void SetWorldMachineFolder(string folder)
    {
        if (map == null)
            return;
        settings.WorldMachineFolders[Path.GetFileNameWithoutExtension(map.Archive)] = folder;
        if (!Quiet)
            settings.Save();
        ShowWorldMachine();
        WatchWorldMachine();
    }

    /// <summary>Whether something is running in the background (for the self-test).</summary>
    internal bool Busy => busy;

    /// <summary>Turns bringing outputs back as World Machine writes them on or off, as the panel's box would (for the self-test).</summary>
    internal void SetWorldMachineLive(bool on)
    {
        beautifyPanel.WorldMachineLive = on;
        WatchWorldMachine();
    }

    void InitWorldMachine()
    {
        beautifyPanel.WorldMachineRequested += async (_, what) => await OnWorldMachine(what);
        wmSettle.Tick += async (_, _) =>
        {
            wmSettle.Stop();
            await BringPendingAsync();
        };
    }

    /// <summary>Shows the folder, program and project in the panel.</summary>
    void ShowWorldMachine() => beautifyPanel.ShowWorldMachine(WorldMachineFolder, settings.WorldMachineProgram, settings.WorldMachineProject);

    async Task OnWorldMachine(string what)
    {
        switch (what)
        {
            case "folder" when map != null:
            {
                using var dialog = new FolderBrowserDialog { Description = "The folder the map and World Machine trade files through", UseDescriptionForTitle = true, SelectedPath = WorldMachineFolder ?? "" };
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    SetWorldMachineFolder(dialog.SelectedPath);
                break;
            }
            case "open" when WorldMachineFolder is string folder && Directory.Exists(folder):
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
                break;
            case "send":
                await SendToWorldMachineAsync();
                break;
            case "bring":
                await BringBackFromWorldMachineAsync(null);
                break;
            case "program":
            {
                using var dialog = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "World Machine's program (Professional edition, for automation)" };
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    settings.WorldMachineProgram = dialog.FileName;
                    settings.Save();
                    ShowWorldMachine();
                }
                break;
            }
            case "project":
            {
                using var dialog = new OpenFileDialog { Filter = "World Machine projects (*.tmd)|*.tmd|All files|*.*", Title = "The World Machine project to build (it reads heights.png)" };
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    settings.WorldMachineProject = dialog.FileName;
                    settings.Save();
                    ShowWorldMachine();
                }
                break;
            }
            case "build":
                await BuildInWorldMachineAsync();
                break;
            case "live-on" or "live-off":
                WatchWorldMachine();
                beautifyPanel.ShowWorldMachineNote(what == "live-on"
                    ? "Watching the folder: each output World Machine writes there comes back (one undo step each time)."
                    : "No longer watching the folder.");
                break;
        }
    }

    /// <summary>Sends the open map to its exchange folder (in the background).</summary>
    internal async Task<bool> SendToWorldMachineAsync()
    {
        if (map == null || busy || WorldMachineFolder is not string folder)
            return false;
        var s = map.Session;
        string note = "";
        bool done = await RunBusy("Sending to World Machine", progress => Task.Run(() => note = s.SendToWorldMachine(folder, progress)));
        if (done)
        {
            Log(note);
            beautifyPanel.ShowWorldMachineNote("Sent. README.txt in the folder says how to set World Machine up; build a pass-through first to check it.");
            ShowWorldMachine();
            WatchWorldMachine();
        }
        return done;
    }

    /// <summary>
    /// Brings World Machine's outputs back from the folder, as one undo step: the files given (each with its layer), or
    /// those of the layers ticked in the panel that are there.
    /// </summary>
    internal async Task<bool> BringBackFromWorldMachineAsync(IReadOnlyList<(string Layer, string Path)>? outputs)
    {
        if (map == null || busy || WorldMachineFolder is not string folder)
            return false;
        outputs ??= [.. beautifyPanel.WorldMachineLayers.Select(l => (l, WorldMachineFiles.Output(folder, l))).Where(o => o.Item2 != null).Select(o => (o.l, o.Item2!))];
        if (outputs.Count == 0)
        {
            beautifyPanel.ShowWorldMachineNote($"Nothing to bring back yet: World Machine hasn't written wm_heights.png, wm_texture.png, wm_splat.png or wm_grass.png in {folder}.");
            return false;
        }
        var info = WorldMachineInfo.Load(folder);
        bool stretch = beautifyPanel.WorldMachineStretch;
        if (info == null && !stretch && outputs.Any(o => o.Layer == "heights"))
        {
            beautifyPanel.ShowWorldMachineNote("The heights can't be mapped back: the folder has no bar_map.json. Send the map first, or bring the heights back stretched.");
            return false;
        }
        var options = new WorldMachineImport
        {
            Keep = beautifyPanel.WorldMachineKeep, Stretch = stretch, Range = stretch || info == null ? null : (info.MinHeight, info.MaxHeight),
        };
        var s = map.Session;
        var notes = new List<string>();
        var written = outputs.ToDictionary(o => o.Path, o => File.GetLastWriteTimeUtc(o.Path), StringComparer.OrdinalIgnoreCase);
        bool done = await RunBusy("Bringing back from World Machine", progress => Task.Run(() =>
        {
            s.BeginGroup("bring back from World Machine");
            try
            {
                foreach (var (layer, path) in outputs)
                {
                    progress.Report($"Bringing back {Path.GetFileName(path)}…");
                    notes.Add(s.BringBackFromWorldMachine(layer, path, options));
                }
            }
            finally
            {
                s.EndGroup();
            }
        }));
        foreach (var (path, time) in written)
            wmBrought[path] = time;
        foreach (var n in notes)
            Log(n);
        beautifyPanel.ShowWorldMachineNote(done ? string.Join(Environment.NewLine, notes.Select(n => char.ToUpperInvariant(n[0]) + n[1..] + ".")) : "");
        UpdateTitle();
        UpdateCommands();
        return done;
    }

    /// <summary>Watches the exchange folder for World Machine's outputs, while bringing them back as written is on or a build is waited on.</summary>
    void WatchWorldMachine()
    {
        wmWatcher?.Dispose();
        wmWatcher = null;
        if (map == null || WorldMachineFolder is not string folder || (!beautifyPanel.WorldMachineLive && wmBuildStarted == null))
            return;
        Directory.CreateDirectory(folder);
        wmWatcher = new FileSystemWatcher(folder, "wm_*")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            SynchronizingObject = this,
        };
        wmWatcher.Changed += (_, e) => OnWorldMachineWrote(e.FullPath);
        wmWatcher.Created += (_, e) => OnWorldMachineWrote(e.FullPath);
        wmWatcher.Renamed += (_, e) => OnWorldMachineWrote(e.FullPath);
        wmWatcher.EnableRaisingEvents = true;
    }

    /// <summary>An output was written: brought back once it has settled (World Machine may still be writing it).</summary>
    void OnWorldMachineWrote(string path)
    {
        if (!WorldMachineFiles.IsOutput(path))
            return;
        wmPending[path] = (-1, DateTime.MinValue);
        wmSettle.Stop();
        wmSettle.Start();
    }

    /// <summary>
    /// Brings back what World Machine has written since it was last brought back, once each file has stopped changing
    /// (the same size and time twice running, and it opens for reading alone); later while the editor is busy.
    /// </summary>
    internal async Task BringPendingAsync()
    {
        if (map == null || WorldMachineFolder is null)
        {
            wmPending.Clear();
            return;
        }
        if (busy || mapView.IsStroking)
        {
            wmSettle.Start();
            return;
        }
        if (wmBuildStarted is DateTime started && DateTime.UtcNow - started > WorldMachineBuildWait)
        {
            wmBuildStarted = null;
            beautifyPanel.ShowWorldMachineNote("Stopped waiting for World Machine's build: nothing came in half an hour.");
        }
        var ready = new List<(string Layer, string Path)>();
        foreach (var path in wmPending.Keys.ToList())
        {
            if (!File.Exists(path))
            {
                wmPending.Remove(path);
                continue;
            }
            var file = new FileInfo(path);
            var now = (file.Length, file.LastWriteTimeUtc);
            if (wmPending[path] != now || !OpensAlone(path))
            {
                wmPending[path] = now; // still being written: look again shortly
                continue;
            }
            wmPending.Remove(path);
            if (wmBrought.TryGetValue(path, out var brought) && brought >= now.LastWriteTimeUtc)
                continue; // already brought back as it is
            if (wmBuildStarted is DateTime since && now.LastWriteTimeUtc < since && !beautifyPanel.WorldMachineLive)
                continue; // from before the build
            var layer = WorldMachineFiles.Outputs.First(o => o.Names.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)).Layer;
            if (beautifyPanel.WorldMachineLayers.Contains(layer) && ready.All(r => r.Layer != layer))
                ready.Add((layer, path));
        }
        if (wmPending.Count > 0)
            wmSettle.Start();
        if (ready.Count > 0 && await BringBackFromWorldMachineAsync(ready) && wmBuildStarted != null && wmPending.Count == 0)
        {
            wmBuildStarted = null;
            WatchWorldMachine();
        }

        static bool OpensAlone(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// World Machine Professional: writes build.xml (load the project, build at the heightmap's resolution, write the
    /// outputs), runs World Machine with it, and brings the outputs back as they are written (sending the map first if
    /// the folder has none of it).
    /// </summary>
    internal async Task<bool> BuildInWorldMachineAsync()
    {
        if (map == null || busy || WorldMachineFolder is not string folder || settings.WorldMachineProgram is not string program ||
            settings.WorldMachineProject is not string project)
            return false;
        if (!File.Exists(program) || !File.Exists(project))
        {
            beautifyPanel.ShowWorldMachineNote(!File.Exists(program) ? $"There is no {program}: choose World Machine's program again." : $"There is no {project}: choose the project again.");
            return false;
        }
        if (!File.Exists(Path.Combine(folder, WorldMachineFiles.Heights)) && !await SendToWorldMachineAsync())
            return false;
        var script = Path.Combine(folder, WorldMachineFiles.Script);
        File.WriteAllText(script, WorldMachineFiles.AutomationScript(project, map.Session.Heights.Width));
        wmBuildStarted = DateTime.UtcNow;
        WatchWorldMachine();
        try
        {
            var start = new ProcessStartInfo(program) { WorkingDirectory = folder, UseShellExecute = false };
            start.ArgumentList.Add(script);
            wmProcess = Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            wmBuildStarted = null;
            WatchWorldMachine();
            ShowError("World Machine could not be started", ex);
            return false;
        }
        if (wmProcess != null)
        {
            wmProcess.EnableRaisingEvents = true;
            wmProcess.SynchronizingObject = this;
            wmProcess.Exited += (_, _) =>
            {
                if (wmBuildStarted is DateTime started && !WorldMachineFiles.Outputs.SelectMany(o => o.Names)
                        .Select(n => Path.Combine(folder, n)).Any(p => File.Exists(p) && File.GetLastWriteTimeUtc(p) >= started))
                    beautifyPanel.ShowWorldMachineNote("World Machine closed without writing any output to the folder: check that its File Output devices " +
                                                       "write wm_heights.png (and the others) there.");
            };
        }
        Log($"World Machine started with {script}: building {Path.GetFileName(project)}");
        beautifyPanel.ShowWorldMachineNote("World Machine is building. Its outputs come back as it writes them (one undo step); this waits up to half an hour.");
        return true;
    }
}
