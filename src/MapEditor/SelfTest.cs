using System.Drawing.Imaging;
using MapCore;
using MapCore.Editing;
using MapCore.Imaging;
using MapCore.Rendering;
using MapCore.Structures;

namespace MapEditor;

/// <summary>
/// <c>MapEditor --selftest &lt;archive&gt; &lt;out folder&gt;</c>: drives the main window through open, edit, undo/redo, a
/// background layer load, Save As and close, without ever showing it. Writes selftest.txt (PASS/FAIL lines and the
/// window's message log) and view images to the folder; the exit code is 0 only if every check passed.
/// </summary>
static class SelfTest
{
    /// <summary>Step 19: a map left open by a crash is found at the next start and its edits replayed, undo included.</summary>
    static async Task Recovery(MainForm form, string archive, Action<bool, string> check)
    {
        static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        static string State(MapEditSession s) => string.Join("|", Hash(s.Smf.ToBytes()), Hash(s.Texture.TileFiles[0].ToBytes()),
            s.Info.Text, s.Items.Placer?.Document.Text, Hash(s.Heights.Heights.SelectMany(BitConverter.GetBytes).ToArray()),
            s.UndoDescription, s.CanRedo, s.HasUnsavedChanges);

        await form.OpenAsync(archive);
        if (form.Map is not OpenMap map)
        {
            check(false, "the map opens again for the recovery check");
            return;
        }
        var s = map.Session;
        var spot = s.Metal.FindSpots()[2];
        s.Apply("height-raise:2000,2000:200:40");
        s.BeginGroup("paint");
        for (int i = 0; i < 4; i++)
            s.Apply($"texture-paint:{1500 + i * 20},900:40:200,60,30:0.5:0.6");
        s.EndGroup();
        s.Apply(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"metal-move:{spot.X},{spot.Z}:{spot.X + 64},{spot.Z + 32}:look"));
        s.Apply("item-add:placer:agorm_rock1:2500,2500");
        s.Undo();
        var before = State(s);
        check(!SessionRecovery.FindOrphans(Workspaces.Root).Any(o => FileSystemUtil.SamePath(o.Workspace, map.Workspace)),
            "an open map's recovery record is not taken for a crash (its lock is held)");

        form.CloseMap(keepRecord: true); // as if the editor had stopped
        var orphan = SessionRecovery.FindOrphans(Workspaces.Root).FirstOrDefault(o => FileSystemUtil.SamePath(o.Workspace, map.Workspace));
        check(orphan is { UnsavedEdits: 8 } && orphan.Manifest?.ArchiveProblem() == null,
            $"a map left open is found at the next start, with its {orphan?.UnsavedEdits} unsaved edits");
        if (orphan == null)
            return;
        bool recovered = await form.RecoverAsync(orphan);
        check(recovered && form.Map?.Session is MapEditSession r && State(r) == before && FileSystemUtil.SamePath(form.Map.Workspace, map.Workspace),
            "recovery replays the edits: heights, texture, metal, items and undo history as they were");
        if (form.Map?.Session is not MapEditSession again)
            return;
        again.Undo(); // the move with its look
        check(again.UndoDescription == "paint", $"undo carries on through the recovered history (next: \"{again.UndoDescription}\")");
        again.Apply("height-raise:2500,2500:100:10");
        var workspace = form.Map.Workspace;
        form.CloseMap(keepRecord: true);
        var second = SessionRecovery.FindOrphans(Workspaces.Root).FirstOrDefault(o => FileSystemUtil.SamePath(o.Workspace, workspace));
        check(second?.Entries.Count(e => e.Kind == "apply") == 8 && second.Entries[^1].Kind == "apply",
            "the recovered session goes on recording, so a second crash loses nothing either");
        if (second == null || !await form.RecoverAsync(second) || form.Map?.Session is not MapEditSession third)
            return;

        // A Save As that dies while packing has saved the workspace but written no archive: still unsaved.
        var locked = Path.Combine(Path.GetTempPath(), $"BarMapEditor-selftest-locked-{Guid.NewGuid():N}.sd7");
        bool savedLocked;
        using (new FileStream(locked, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            savedLocked = await form.SaveAsAsync(third.LoadedName ?? "Map", (third.LoadedVersion ?? "") + "-recovery", locked, overwrite: true);
        File.Delete(locked);
        form.CloseMap(keepRecord: true);
        var failed = SessionRecovery.FindOrphans(Workspaces.Root).FirstOrDefault(o => FileSystemUtil.SamePath(o.Workspace, workspace));
        check(!savedLocked && failed is { UnsavedEdits: > 0, SavedButNotPacked: true },
            $"a Save As that failed while packing leaves its edits to recover ({failed?.UnsavedEdits} unsaved)");
        if (failed == null || !await form.RecoverAsync(failed) || form.Map?.Session is not MapEditSession fourth)
            return;
        check(form.Text.Contains(" * ", StringComparison.Ordinal), $"after recovering it, the title still marks unsaved changes: \"{form.Text}\"");

        var packed = Path.Combine(Path.GetTempPath(), $"BarMapEditor-selftest-packed-{Guid.NewGuid():N}.sd7");
        bool savedPacked = await form.SaveAsAsync(fourth.LoadedName ?? "Map", (fourth.LoadedVersion ?? "") + "-recovery", packed, overwrite: false);
        File.Delete(packed);
        form.CloseMap(keepRecord: true);
        var done = SessionRecovery.FindOrphans(Workspaces.Root).FirstOrDefault(o => FileSystemUtil.SamePath(o.Workspace, workspace));
        check(savedPacked && done is { UnsavedEdits: 0, SavedButNotPacked: false }, "after a Save As that finished, nothing is left to recover");
        if (done != null)
            form.DiscardRecovery(done);
        check(!Directory.Exists(workspace) && !Directory.Exists(SessionRecord.FolderFor(workspace)),
            "discarding a record deletes its workspace and record");
    }

    public static int Run(string archive, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var results = new List<string>();
        void Check(bool condition, string what) => results.Add((condition ? "PASS  " : "FAIL  ") + what);

        // The self-test keeps its own asset library, so it never adds to the user's.
        var assetRoot = FileSystemUtil.NewTempDir("selftest-assets");
        using var form = new MainForm { Quiet = true, AssetRoot = assetRoot };
        _ = form.Handle; // a message target for the background work, without showing the window
        // The maps' assets only until BAR's own are checked (in Assets, with a name filter): drawing pictures for all
        // of BAR's 2,000-odd entries would run behind the whole test.
        form.AssetBrowser.Source = AssetSource.Maps;

        async Task Script()
        {
            archive = Path.GetFullPath(archive);
            await form.OpenAsync(archive);
            Check(form.Map != null, "the map opens");
            if (form.Map is not OpenMap map)
                return;
            form.ShowBeautifyPanel(false, select: false); // as a first run has it (the user's own choice isn't changed: Quiet)
            var s = map.Session;
            Check(FileSystemUtil.IsSameOrUnder(map.Workspace, Workspaces.Root) && Directory.Exists(map.Workspace),
                $"the workspace is under {Workspaces.Root}");
            Check(form.Text.StartsWith($"{s.LoadedName} {s.LoadedVersion} — ", StringComparison.Ordinal), $"the title names the map: \"{form.Text}\"");
            SaveView(form, Path.Combine(outDir, "1_opened.png"));

            const string raise = "height-raise:3072,3072:300:80";
            s.Apply(raise);
            Check(form.Text.Contains(" * ", StringComparison.Ordinal), "the title marks unsaved changes");
            Check(form.UndoText == "&Undo " + raise, $"the Undo menu names the edit: \"{form.UndoText}\"");
            float raised = s.Heights.HeightAt(3072, 3072);
            form.Undo();
            Check(!s.CanUndo && s.CanRedo && s.Heights.HeightAt(3072, 3072) < raised - 50, "Undo puts the heights back");
            form.Redo();
            Check(s.CanUndo && s.Heights.HeightAt(3072, 3072) == raised, "Redo raises them again");

            form.MapView.Overlays = RenderRequest.AllOverlays;
            await form.SelectLayerAsync(MapViewLayer.Heights);
            SaveView(form, Path.Combine(outDir, "2_heights_raised.png"));
            await form.SelectLayerAsync(MapViewLayer.Normals);
            Check(form.MapView.Layer == MapViewLayer.Normals, "the normals layer loads in the background and shows");
            SaveView(form, Path.Combine(outDir, "3_normals_before_save.png"));
            Check(form.MapView.Note?.Contains("saved", StringComparison.Ordinal) == true, $"the normals view says it updates on save: \"{form.MapView.Note}\"");

            Check(SaveAsDialog.Check(s.LoadedName ?? "", s.LoadedVersion ?? "", s.LoadedName, s.LoadedVersion, Path.Combine(outDir, "x.sd7"), archive) != null,
                "Save As refuses the original name and version");
            using (var saveAs = new SaveAsDialog(s.Info.Name, s.Info.Version, s.LoadedName, s.LoadedVersion, archive))
                DialogPicture(saveAs, Path.Combine(outDir, "save_as_dialog.png")); // to see its look
            Check(!await form.SaveAsAsync("Self Test", "1", archive, overwrite: true), "Save As refuses to write over the source archive");
            Check(s.Info.Name == s.LoadedName, "a refused Save As changes nothing");

            var target = Path.Combine(outDir, "selftest_saved.sd7");
            if (File.Exists(target))
                File.Delete(target);
            bool saved = await form.SaveAsAsync(s.LoadedName ?? "Map", (s.LoadedVersion ?? "") + "-selftest", target, overwrite: false);
            Check(saved && File.Exists(target), "Save As writes the new archive");
            Check(!form.Text.Contains(" * ", StringComparison.Ordinal) && form.Text.Contains(Path.GetFileName(target), StringComparison.Ordinal),
                $"after saving, the title names the new archive and shows no unsaved changes: \"{form.Text}\"");
            Check(form.UndoText == "&Undo name and version", $"the name and version change is one undo step: \"{form.UndoText}\"");
            SaveView(form, Path.Combine(outDir, "4_normals_after_save.png"));
            Check(form.MapView.Note == null, "after saving, the normals view has no stale-data note");

            if (saved)
            {
                var check = Path.Combine(outDir, "reopened");
                if (Directory.Exists(check))
                    FileSystemUtil.DeleteTree(check);
                var reopened = MapEditSession.Open(MapPackage.Open(target, check));
                Check(reopened.Info.Version == s.LoadedVersion + "-selftest", $"the saved archive has the new version: {reopened.Info.Version}");
                Check(Math.Abs(reopened.Heights.HeightAt(3072, 3072) - raised) <= reopened.Heights.Step * 2,
                    $"the saved archive has the raised ground: {reopened.Heights.HeightAt(3072, 3072):0.##} vs {raised:0.##}");
                FileSystemUtil.DeleteTree(check);
            }

            // Step 18: copying into BAR's maps folder, which already holds the original map.
            if (saved)
            {
                var maps = Path.Combine(outDir, "maps");
                if (Directory.Exists(maps))
                    FileSystemUtil.DeleteTree(maps);
                Directory.CreateDirectory(maps);
                File.Copy(archive, Path.Combine(maps, "acidicquarry_5.17.sd7"));
                var copied = await form.CopyToMapsAsync(target, maps, s.Info.Name, s.Info.Version);
                Check(copied != null && File.Exists(copied), "Save As copies the new map into the maps folder");
                Check(await form.CopyToMapsAsync(target, maps, s.Info.Name, s.Info.Version) == null &&
                      form.Messages.Last().Contains("already exists", StringComparison.Ordinal), "a second copy doesn't replace the first without asking");
                var lookalike = Path.Combine(outDir, "lookalike.sd7");
                File.Copy(archive, lookalike, overwrite: true);
                Check(await form.CopyToMapsAsync(lookalike, maps, s.LoadedName, s.LoadedVersion) == null &&
                      form.Messages.Any(m => m.Contains("maps folder already has", StringComparison.Ordinal)) && !File.Exists(Path.Combine(maps, "lookalike.sd7")),
                    "a map with the same name and version as one already there is not copied without asking");
            }

            await HeightTools(form, s, outDir, Check);
            RampTool(form, s, outDir, Check);
            await TextureAndColours(form, s, outDir, Check);
            Metal(form, s, Check);
            await Items(form, s, outDir, Check);
            await Assets(form, s, outDir, Check);
            Symmetry(form, s, outDir, Check);
            await Beautify(form, s, outDir, Check);
            await WorldMachine(form, s, outDir, Check);

            // Step 17: checks before saving stop a broken map.
            s.Apply("item-add:placer:agorm_rock1:-40,100");
            var refusedTarget = Path.Combine(outDir, "selftest_checks.sd7");
            if (File.Exists(refusedTarget))
                File.Delete(refusedTarget);
            bool refusedSave = await form.SaveAsAsync(s.LoadedName ?? "Map", (s.LoadedVersion ?? "") + "-checks", refusedTarget, overwrite: true);
            Check(!refusedSave && !File.Exists(refusedTarget) && form.Messages.Any(m => m.Contains("off the map", StringComparison.Ordinal)),
                "Save As refuses a map with an item off the map, and says why");
            form.Undo();
            var window = ThreeD(form, s, outDir, Check);

            var workspace = map.Workspace;
            form.CloseMap();
            Check(window?.IsDisposed == true, "closing the map closes the 3D view");
            Check(form.Map == null && !Directory.Exists(workspace), "closing deletes the workspace");
            Check(form.Text == "BAR Map Editor", "the title resets after closing");
            Check(!Directory.Exists(SessionRecord.FolderFor(workspace)), "closing deletes the recovery record");

            await Recovery(form, archive, Check);
        }

        form.BeginInvoke(new Action(async () =>
        {
            try
            {
                await Script();
            }
            catch (Exception ex)
            {
                results.Add("FAIL  unexpected exception: " + ex);
            }
            finally
            {
                Application.ExitThread();
            }
        }));
        Application.Run();

        var report = results.Concat(["", "Window messages:"]).Concat(form.Messages.Select(m => "  " + m));
        File.WriteAllLines(Path.Combine(outDir, "selftest.txt"), report);
        try
        {
            FileSystemUtil.DeleteTree(assetRoot); // the self-test's own library, made for this run
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return results.Count > 0 && results.All(r => r.StartsWith("PASS", StringComparison.Ordinal)) ? 0 : 1;
    }

    /// <summary>Step 13: the height tools, driven through the map view's real mouse handlers.</summary>
    static async Task HeightTools(MainForm form, MapEditSession s, string outDir, Action<bool, string> Check)
    {
        var view = form.MapView;
        var tools = form.HeightTools;
        view.View = new ViewTransform(1000, 1000, 2);
        Point At(double x, double z)
        {
            var (px, py) = view.View.ToImage(x, z);
            return new Point((int)Math.Floor(px), (int)Math.Floor(py));
        }
        void Mouse(MouseButtons button, string action, double x, double z, Keys keys = Keys.None)
        {
            var p = At(x, z);
            view.SimulateMouse(button, action, p.X, p.Y, keys);
        }
        float H(double x, double z) => s.Heights.HeightAt(x, z);
        int changes = 0;
        s.Changed += (_, _) => changes++;

        tools.Radius = 100;
        tools.Strength = 1;
        tools.Hardness = 0.5;
        tools.Tool = HeightTool.Raise;
        Check(view.Brush is { Radius: 100, RepeatWhileHeld: true }, "choosing Raise gives the map view a 100-unit brush");

        // A 500-unit stroke in ten mouse moves: dabs a quarter radius (25 units) apart, about 21 in all.
        float a0 = H(1200, 1400), mid0 = H(1450, 1400);
        changes = 0;
        Mouse(MouseButtons.Left, "down", 1200, 1400);
        for (int i = 1; i <= 10; i++)
            Mouse(MouseButtons.Left, "move", 1200 + 50 * i, 1400);
        Mouse(MouseButtons.Left, "up", 1700, 1400);
        Check(changes is >= 19 and <= 22, $"a 500-unit stroke with a 100-unit brush makes about 21 dabs: {changes}");
        Check(form.UndoText == "&Undo raise heights", $"the stroke is one undo step named after the tool: \"{form.UndoText}\"");
        Check(H(1200, 1400) > a0 + 5 && H(1450, 1400) > mid0 + 5, "the stroke raised the ground along its path");
        form.Undo();
        Check(H(1200, 1400) == a0 && H(1450, 1400) == mid0, "one undo removes the whole stroke");

        // A stroke cut off by losing the mouse closes its undo step; the next edit is a step of its own.
        Mouse(MouseButtons.Left, "down", 1200, 1400);
        Mouse(MouseButtons.Left, "move", 1400, 1400);
        view.SimulateCaptureLost();
        Check(!view.IsStroking, "losing the mouse ends the stroke");
        Mouse(MouseButtons.Left, "move", 1600, 1400);
        Mouse(MouseButtons.Left, "up", 1600, 1400);
        float cut = H(1200, 1400);
        s.OffsetHeights(1);
        Check(form.UndoText.StartsWith("&Undo height-offset", StringComparison.Ordinal), $"the next edit is its own step: \"{form.UndoText}\"");
        form.Undo();
        Check(H(1200, 1400) == cut, "undoing that edit leaves the cut-off stroke in place");
        form.Undo();
        Check(H(1200, 1400) == a0, "the cut-off stroke undoes as one step");

        // Right-drag still pans while a tool is active, and paints nothing.
        var before = view.View;
        changes = 0;
        view.SimulateMouse(MouseButtons.Right, "down", 100, 100);
        view.SimulateMouse(MouseButtons.Right, "move", 160, 130);
        view.SimulateMouse(MouseButtons.Right, "up", 160, 130);
        Check(view.View.Left == before.Left - 60 * before.UnitsPerPixel && changes == 0, "right-drag pans and does not paint while a tool is active");
        view.View = before;

        // Set height: Ctrl+click picks the target and changes nothing; a click pulls the ground to it.
        tools.Tool = HeightTool.SetHeight;
        string undoBefore = form.UndoText;
        Mouse(MouseButtons.Left, "down", 1300, 1700, Keys.Control);
        Mouse(MouseButtons.Left, "up", 1300, 1700);
        var picked = view.View.ToWorld(At(1300, 1700).X + 0.5, At(1300, 1700).Y + 0.5);
        Check(Math.Abs(tools.TargetHeight - H(picked.X, picked.Z)) <= 0.051 && form.UndoText == undoBefore,
            $"Ctrl+click picks the height under the cursor ({tools.TargetHeight}) and changes nothing");
        tools.TargetHeight = 100;
        Mouse(MouseButtons.Left, "down", 1500, 1700);
        Mouse(MouseButtons.Left, "up", 1500, 1700);
        var dab = view.View.ToWorld(At(1500, 1700).X + 0.5, At(1500, 1700).Y + 0.5);
        Check(Math.Abs(H(dab.X, dab.Z) - 100) < 0.01, $"a Set height click pulls the ground to 100 at the brush centre: {H(dab.X, dab.Z):0.###}");
        Check(form.UndoText == "&Undo set heights to 100", $"named \"{form.UndoText}\"");

        // Flatten pulls towards the height where the stroke starts.
        tools.Tool = HeightTool.Flatten;
        var start = view.View.ToWorld(At(1100, 2000).X + 0.5, At(1100, 2000).Y + 0.5);
        float startHeight = H(start.X, start.Z), end0 = H(1300, 2000);
        Mouse(MouseButtons.Left, "down", 1100, 2000);
        Mouse(MouseButtons.Left, "move", 1300, 2000);
        Mouse(MouseButtons.Left, "up", 1300, 2000);
        Check(Math.Abs(H(1300, 2000) - startHeight) < Math.Abs(end0 - startHeight) || Math.Abs(end0 - startHeight) < 0.01,
            $"Flatten pulls the far end towards the starting height ({end0:0.#} -> {H(1300, 2000):0.#}, target {startHeight:0.#})");

        // Noise changes the ground inside the brush only, as one step.
        tools.Tool = HeightTool.Noise;
        float inside0 = H(2000, 1200), outside0 = H(2300, 1200);
        Mouse(MouseButtons.Left, "down", 2000, 1200);
        Mouse(MouseButtons.Left, "up", 2000, 1200);
        Check(H(2300, 1200) == outside0 && form.UndoText == "&Undo height noise", "Noise stays inside the brush and is one step");

        view.ShowBrushAt(At(1450, 1500));
        await form.SelectLayerAsync(MapCore.Rendering.MapViewLayer.Heights);
        SaveView(form, Path.Combine(outDir, "5_height_tools.png"), keepView: true);
        view.ShowBrushAt(null);

        // Sea level preview: only the view changes until it is applied, as one height shift.
        bool unsaved = s.HasUnsavedChanges;
        undoBefore = form.UndoText;
        tools.SeaLevel = 40;
        Check(form.Map!.Renderer.WaterLevel == 40 && form.UndoText == undoBefore && s.HasUnsavedChanges == unsaved,
            "the sea level preview reaches the view and changes nothing in the map");
        SaveView(form, Path.Combine(outDir, "6_sea_level_40.png"));
        float h0 = H(3000, 3000);
        form.ApplySeaLevel();
        Check(Math.Abs(H(3000, 3000) - (h0 - 40)) < 0.001 && tools.SeaLevel == 0 && form.Map.Renderer.WaterLevel == 0,
            "applying it lowers every height by 40 and resets the preview");
        Check(form.UndoText == "&Undo height-offset:-40", $"as one step: \"{form.UndoText}\"");

        // Heights to PNG and back, each through the background path; the import is one undo step.
        var png = Path.Combine(outDir, "heights_export.png");
        Check(await form.ExportLayerAsync("heights", png) && File.Exists(png), "heights export to PNG");
        Check(await form.ImportLayerAsync("heights", png) && form.UndoText.StartsWith("&Undo import:heights=", StringComparison.Ordinal),
            $"heights import from PNG, as one step: \"{form.UndoText}\"");

        tools.Tool = HeightTool.Pan;
        Check(view.Brush == null, "the Pan tool gives the left button back to panning");
    }

    /// <summary>Step 14: texture brushes, clone, splat painting and the mapinfo colour panel, through the real handlers.</summary>
    static async Task TextureAndColours(MainForm form, MapEditSession s, string outDir, Action<bool, string> Check)
    {
        var view = form.MapView;
        var tools = form.TextureTools;
        form.ShowTab("Texture");
        view.View = new ViewTransform(1000, 3000, 2);
        Point At(double x, double z)
        {
            var (px, py) = view.View.ToImage(x, z);
            return new Point((int)Math.Floor(px), (int)Math.Floor(py));
        }
        (double X, double Z) DabAt(double x, double z) => view.View.ToWorld(At(x, z).X + 0.5, At(x, z).Y + 0.5);
        void Click(double x, double z, Keys keys = Keys.None)
        {
            var p = At(x, z);
            view.SimulateMouse(MouseButtons.Left, "down", p.X, p.Y, keys);
            view.SimulateMouse(MouseButtons.Left, "up", p.X, p.Y);
        }
        Rgba Texel(double x, double z) => s.TextureImage.GetPixel((int)x, (int)z); // 1 texel per game unit here

        tools.Tool = TextureTool.Paint;
        Check(view.Brush is { RepeatWhileHeld: false }, "the Texture tab's tools drive the brush");
        tools.Radius = 40;
        tools.Strength = 1;
        tools.Hardness = 0.8;
        tools.Colour = new Rgba(230, 20, 20, 255);
        var before = Texel(1300, 3300);
        Click(1300, 3300);
        var painted = Texel(DabAt(1300, 3300).X, DabAt(1300, 3300).Z);
        Check(painted.R > 200 && painted.G < 60 && form.UndoText == "&Undo paint texture", $"a Paint click paints the colour, as one step ({painted})");
        form.Undo();
        Check(Texel(1300, 3300) == before, "undo removes it");

        tools.Tool = TextureTool.Tint;
        var picked = DabAt(1500, 3300);
        Click(1500, 3300, Keys.Control);
        Check(tools.Colour == Texel(picked.X, picked.Z) with { A = 255 }, "Ctrl+click picks the colour under the cursor");

        // Clone: refused until a source is chosen; then the first stroke fixes the offset.
        tools.Tool = TextureTool.Clone;
        string undoBefore = form.UndoText;
        Click(1300, 3600);
        Check(form.UndoText == undoBefore, "a clone stroke without a source does nothing");
        Click(1100, 3100, Keys.Alt);
        Check(tools.CloneSource is not null, "Alt+click sets the clone source");
        Click(1700, 3700);
        Check(tools.CloneOffset is (double ox, double oz) && Math.Abs(ox + 600) < 3 && Math.Abs(oz + 600) < 3 && form.UndoText == "&Undo clone texture",
            $"the first clone stroke copies from the source and fixes the offset ({tools.CloneOffset})");
        var source = DabAt(1100, 3100);
        var copied = DabAt(1700, 3700);
        var a = Texel(source.X, source.Z);
        var b = Texel(copied.X, copied.Z);
        Check(Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) < 24, $"the clone shows the source's texture ({a} vs {b})");

        // Splat: choosing the tool shows the splat layer; a click paints the chosen weights.
        tools.Tool = TextureTool.Splat;
        for (int i = 0; i < 100 && view.Layer != MapViewLayer.Splat; i++)
            await Task.Delay(50);
        Check(view.Layer == MapViewLayer.Splat, "choosing the Splat tool shows the splat layer");
        tools.SetSplatChannel(1, 100);
        tools.Strength = 1;
        Click(1300, 3300);
        var (sw, sh) = s.SplatSize;
        var splat = new MapCore.Imaging.RgbaImage(sw, sh);
        var dab = DabAt(1300, 3300);
        int spx = (int)(dab.X * sw / s.MapWidth), spy = (int)(dab.Z * sh / s.MapHeight);
        s.ReadSplat(splat, new PixelRect(spx, spy, 1, 1));
        var weights = splat.GetPixel(spx, spy);
        Check(weights.G > 240 && weights.R < 15 && form.UndoText == "&Undo paint splat", $"a Splat click paints detail texture 2 ({weights})");
        SaveView(form, Path.Combine(outDir, "9_splat_painted.png"), keepView: true);
        tools.Tool = TextureTool.Paint;
        for (int i = 0; i < 100 && view.Layer != MapViewLayer.Texture; i++)
            await Task.Delay(50);
        Check(view.Layer == MapViewLayer.Texture, "going back to Paint shows the texture again");

        // Colours: typing a value is one undo step; undo shows the old value again.
        form.ShowTab("Colours");
        Check(view.Brush == null, "the Colours tab leaves the left button to panning");
        var colours = form.ColourPanel;
        var fog = colours.Values("atmosphere.fogColor");
        colours.SetValue("atmosphere.fogColor", 0, 0.9);
        Check(s.Info.GetNumbers("atmosphere.fogColor")?[0] == 0.9 && form.UndoText.StartsWith("&Undo set:atmosphere.fogColor=0.9,", StringComparison.Ordinal),
            $"editing a colour sets mapinfo, as one step: \"{form.UndoText}\"");
        form.Undo();
        Check(colours.Values("atmosphere.fogColor").SequenceEqual(fog), "after undo the panel shows the old colour");
        Check(colours.Marked("water.baseColor") && !colours.Marked("atmosphere.skyColor"),
            "water colours are marked as possibly overridden by mapconfig/mapinfo (this map's options), the sky is not");
        form.ShowTab("Heights");

        // Pictures of the panels, to check their layout by eye: undocked for the moment, so they draw at full height.
        var heightTool = form.HeightTools.Tool;
        foreach (var (name, panel) in new (string, Control)[] { ("heights", form.HeightTools), ("heights_ramp", form.HeightTools), ("texture", form.TextureTools), ("metal", form.MetalPanel), ("items", form.ItemsPanel), ("colours", form.ColourPanel), ("assets", form.AssetsPanel) })
        {
            form.HeightTools.Tool = name == "heights_ramp" ? HeightTool.Ramp : HeightTool.Raise;
            SavePanelPicture(form, panel, Path.Combine(outDir, $"panel_{name}.png"));
        }
        form.HeightTools.Tool = heightTool;
    }

    /// <summary>A picture of a tool panel, to check its layout by eye: undocked for the moment, so it draws at full height.</summary>
    static void SavePanelPicture(MainForm form, Control panel, string file, int height = 1000, int? width = null)
    {
        var size = new Size(width ?? form.ToolTabs.DisplayRectangle.Width, height); // as wide as in the window
        var dock = panel.Dock;
        panel.Dock = DockStyle.None;
        panel.Size = size;
        panel.PerformLayout();
        CreateHandles(panel); // printing draws only controls that have windows
        using var bitmap = new Bitmap(size.Width, size.Height);
        panel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
        bitmap.Save(file, ImageFormat.Png);
        panel.Dock = dock;
    }

    /// <summary>A picture of a dialog without showing it: its windows made, laid out at its size, printed.</summary>
    static void DialogPicture(Form dialog, string file)
    {
        CreateHandles(dialog);
        dialog.PerformLayout();
        using var bitmap = new Bitmap(dialog.Width, dialog.Height);
        dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
        bitmap.Save(file, ImageFormat.Png);
    }

    static void CreateHandles(Control control)
    {
        _ = control.Handle;
        foreach (Control child in control.Controls)
            CreateHandles(child);
    }

    /// <summary>Whether a preview shows a model: drawn, covering a fair part of the picture, in many colours (textured, lit).</summary>
    static bool ShowsModel(ModelPreviewBox box, out string why)
    {
        var render = box.RenderNow();
        if (!box.HasModel || render == null)
        {
            why = $"no model: \"{box.Caption}\"";
            return false;
        }
        var p = render.Image.Pixels;
        var colours = new HashSet<int>();
        for (int i = 0; i < p.Length; i += 4)
            colours.Add(p[i] << 16 | p[i + 1] << 8 | p[i + 2]);
        why = $"\"{box.Caption}\", covering {render.Coverage:P0} in {colours.Count:N0} colours";
        return render.Coverage > 0.03 && colours.Count > 200;
    }

    /// <summary>Step 21: the ramp tool through the real pointer handlers: place, stretch, widen, move, turn, cancel, apply, undo.</summary>
    static void RampTool(MainForm form, MapEditSession s, string outDir, Action<bool, string> Check)
    {
        var view = form.MapView;
        var tools = form.HeightTools;
        form.ShowTab("Heights");
        view.View = new ViewTransform(1300, 1300, 2);
        Point At(double x, double z)
        {
            var (px, py) = view.View.ToImage(x, z);
            return new Point((int)Math.Round(px), (int)Math.Round(py));
        }
        void Mouse(string action, double x, double z) => view.SimulateMouse(MouseButtons.Left, action, At(x, z).X, At(x, z).Y);
        static double Dist(double ax, double az, double bx, double bz) => Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

        tools.RampLength = 400;
        tools.RampWidth = 160;
        tools.RampDirection = 180; // southwards: from the plateau at (1500, 1475) down into the acid
        tools.RampStartHeight = null;
        tools.RampEndHeight = null;
        tools.RampEdge = null;
        tools.RampLook = RampLook.FromEnds;
        tools.RampRoughness = 0;
        tools.Tool = HeightTool.Ramp;
        Check(view.PointerMode && view.Brush == null && form.CurrentRamp != null && view.Shapes.Count >= 3,
            "the Ramp tool places a ramp outline and puts the left button in place-and-drag mode");

        Mouse("down", 1500, 1675);
        Mouse("up", 1500, 1675);
        var r = form.CurrentRamp!.Value;
        Check(Dist(r.X1, r.Z1, 1500, 1475) < 3 && Dist(r.X2, r.Z2, 1500, 1875) < 3, // a screen pixel is 2 units here
            $"a click places the ramp there: ({r.X1:0}, {r.Z1:0}) -> ({r.X2:0}, {r.Z2:0})");

        Mouse("down", r.X2, r.Z2);
        Mouse("move", 1530, 1915);
        Mouse("move", 1560, 1955);
        Mouse("up", 1560, 1955);
        var stretched = form.CurrentRamp!.Value;
        Check(Dist(stretched.X1, stretched.Z1, r.X1, r.Z1) < 2.5 && Dist(stretched.X2, stretched.Z2, 1560, 1955) < 3 &&
              Math.Abs(tools.RampLength - 484) <= 2 && Math.Abs(tools.RampDirection - 173) <= 1,
            $"dragging the end handle stretches and turns the ramp, the start staying put (length {tools.RampLength}, direction {tools.RampDirection}°)");

        var (sx, sz) = stretched.World(stretched.Length / 2, stretched.Width / 2);
        var (wx, wz) = stretched.World(stretched.Length / 2, 120);
        Mouse("down", sx, sz);
        Mouse("move", wx, wz);
        Mouse("up", wx, wz);
        Check(Math.Abs(tools.RampWidth - 240) <= 3, $"dragging the side handle widens it ({tools.RampWidth})");

        var before = form.CurrentRamp!.Value;
        var (mx, mz) = before.World(before.Length / 2, 0);
        Mouse("down", mx, mz);
        Mouse("move", mx + 40, mz);
        Mouse("up", mx + 40, mz);
        var moved = form.CurrentRamp!.Value;
        Check(Math.Abs(moved.X1 - before.X1 - 40) < 2.5 && Math.Abs(moved.Z1 - before.Z1) < 2.5 && Math.Abs(moved.Length - before.Length) < 0.5,
            "dragging the middle moves it whole");

        double direction = tools.RampDirection;
        tools.HandleKey(Keys.E);
        Check(Math.Abs(tools.RampDirection - (direction + 15) % 360) < 0.5, "E turns it 15°");
        tools.HandleKey(Keys.Q);

        var (cx, cz) = moved.World(moved.Length / 2, 0);
        Mouse("down", cx, cz);
        Mouse("move", cx + 300, cz + 300);
        view.SimulateCaptureLost();
        var kept = form.CurrentRamp!.Value;
        Check(Dist(kept.X1, kept.Z1, moved.X1, moved.Z1) < 0.5 && Dist(kept.X2, kept.Z2, moved.X2, moved.Z2) < 0.5,
            "a drag cut off by losing the mouse leaves the ramp where it was");

        var preview = form.RampPreview!.Value;
        var (g1, g2) = s.RampEndHeights(preview.Ramp);
        Check(Math.Abs(preview.H1 - g1) < 0.06 && Math.Abs(preview.H2 - g2) < 0.06 && tools.RampInfo.StartsWith("Falls", StringComparison.Ordinal),
            $"its heights follow the ground at its ends ({preview.H1:0.#} to {preview.H2:0.#}): \"{tools.RampInfo}\"");
        Check(tools.RampClimb.Contains("✗ Vehicles (27°): too steep", StringComparison.Ordinal) &&
              tools.RampClimb.Contains("✓ Bots and commanders (54°)", StringComparison.Ordinal),
            $"the panel says who can climb it: at {preview.Ramp.SlopeDegrees(preview.H1, preview.H2):0.#}° bots can, vehicles can't");
        SaveView(form, Path.Combine(outDir, "5_ramp_outline.png"), keepView: true);

        var heights = (float[])s.Heights.Heights.Clone();
        bool applied = form.ApplyRamp();
        var (mid1, mid2) = preview.Ramp.World(preview.Ramp.Length / 2, 0);
        Check(applied && form.UndoText.StartsWith("&Undo ramp:", StringComparison.Ordinal) &&
              Math.Abs(s.Heights.HeightAt(mid1, mid2) - (preview.H1 + preview.H2) / 2) < 0.05,
            $"Apply ramp builds it as one step, halfway down at its middle: \"{form.UndoText}\"");
        SaveView(form, Path.Combine(outDir, "5_ramp_built.png"), keepView: true);
        form.Undo();
        Check(s.Heights.Heights.AsSpan().SequenceEqual(heights), "one undo takes the whole ramp away");

        // Rock to dirt: rings show where rock and dirt come from; dragging one picks it elsewhere; Apply uses it.
        tools.RampLook = RampLook.RockToDirt;
        tools.RampRoughness = 0.5;
        var sources = form.RampSources;
        Check(sources?.Rock != null && view.Marks.Any(m => m.Label == "rock"),
            $"rock to dirt marks where its rock comes from ({sources?.Rock?.X:0}, {sources?.Rock?.Z:0})");
        var (rx, rz) = sources!.Value.Rock!.Value;
        Mouse("down", rx, rz);
        Mouse("move", rx + 60, rz + 40);
        Mouse("up", rx + 60, rz + 40);
        var picked = form.RampSources!.Value.Rock!.Value;
        Check(Dist(picked.X, picked.Z, rx + 60, rz + 40) < 3 && Dist(form.CurrentRamp!.Value.X1, form.CurrentRamp.Value.Z1, kept.X1, kept.Z1) < 0.5,
            "dragging the rock ring picks rock from there, and the ramp stays put");
        bool built = form.ApplyRamp();
        Check(built && form.UndoText.Contains(":2:0.5:", StringComparison.Ordinal) &&
              form.UndoText.Contains(FormattableString.Invariant($"{picked.X},{picked.Z}"), StringComparison.Ordinal),
            $"Apply ramp builds rock to dirt with rough sides and the picked rock: \"{form.UndoText}\"");
        SaveView(form, Path.Combine(outDir, "5_ramp_rock_to_dirt.png"), keepView: true);
        form.Undo();
        Check(s.Heights.Heights.AsSpan().SequenceEqual(heights), "undo takes the rock-to-dirt ramp away too");

        // Bending: Shift+click runs the ramp on from its nearer end; Ctrl+click adds a bend on its line (nothing moves
        // until it's dragged); a bend drags; Ctrl+click on a bend takes it away; E turns it all; Swap ends reverses it;
        // Straighten goes back.
        tools.RampLook = RampLook.FromEnds;
        tools.RampRoughness = 0;
        void Press(double x, double z, Keys keys)
        {
            view.SimulateMouse(MouseButtons.Left, "down", At(x, z).X, At(x, z).Y, keys);
            view.SimulateMouse(MouseButtons.Left, "up", At(x, z).X, At(x, z).Y);
        }
        var straight = form.CurrentRamp!.Value;
        double ux = (straight.X2 - straight.X1) / straight.Length, uz = (straight.Z2 - straight.Z1) / straight.Length;
        var (ex, ez) = (straight.X2 + 250 * ux - 200 * uz, straight.Z2 + 250 * uz + 200 * ux);
        Press(ex, ez, Keys.Shift);
        var bent = form.CurrentRamp!.Value;
        var points = form.RampPoints;
        Check(points is { Count: 3 } && Dist(points[2].X, points[2].Z, ex, ez) < 3 && Dist(points[1].X, points[1].Z, straight.X2, straight.Z2) < 1 &&
              bent.Path != null && tools.RampBends && tools.RampInfo.Contains("1 bend", StringComparison.Ordinal),
            $"Shift+click runs the ramp on to there, its old end now a bend: \"{tools.RampInfo}\"");
        var (fx, fz) = ((points![0].X + points[1].X) / 2 - 30 * uz, (points[0].Z + points[1].Z) / 2 + 30 * ux);
        Press(fx, fz, Keys.Control);
        var withBend = form.CurrentRamp!.Value;
        Check(form.RampPoints is { Count: 4 } && Math.Abs(withBend.Length - bent.Length) < 0.5,
            $"Ctrl+click adds a bend on its line, and the ramp doesn't move ({bent.Length:0.#} long, then {withBend.Length:0.#})");
        var added = form.RampPoints![1];
        var (tx, tz) = (added.X - 60 * uz, added.Z + 60 * ux);
        Mouse("down", added.X, added.Z);
        Mouse("move", tx, tz);
        Mouse("up", tx, tz);
        var dragged = form.RampPoints![1];
        Check(form.RampPoints.Count == 4 && Dist(dragged.X, dragged.Z, tx, tz) < 3 && Math.Abs(form.CurrentRamp!.Value.Length - withBend.Length) > 1,
            $"dragging that bend moves it, and the ramp bends there ({form.CurrentRamp!.Value.Length:0.#} long)");
        var held = form.RampPoints.ToList();
        Mouse("down", dragged.X, dragged.Z);
        Mouse("move", dragged.X + 100, dragged.Z + 100);
        view.SimulateCaptureLost();
        Check(form.RampPoints!.SequenceEqual(held), "a bend's drag cut off by losing the mouse leaves every point where it was");
        SaveView(form, Path.Combine(outDir, "5_ramp_bending_outline.png"), keepView: true);
        Press(dragged.X, dragged.Z, Keys.Control);
        Check(form.RampPoints is { Count: 3 }, "Ctrl+click on a bend takes it away");

        var unturned = form.RampPoints!.ToList();
        tools.HandleKey(Keys.E);
        var turned = form.RampPoints!;
        Check(Math.Abs(Dist(turned[0].X, turned[0].Z, turned[2].X, turned[2].Z) - Dist(unturned[0].X, unturned[0].Z, unturned[2].X, unturned[2].Z)) < 2 &&
              Dist(turned[0].X, turned[0].Z, unturned[0].X, unturned[0].Z) > 20,
            "E turns a bending ramp's points together, about its middle");
        tools.HandleKey(Keys.Q);
        var forward = form.RampPoints!.ToList();
        tools.SwapRampEnds();
        Check(form.RampPoints!.SequenceEqual(Enumerable.Reverse(forward)), "Swap ends runs its points the other way");
        tools.SwapRampEnds();

        var flat = (float[])s.Heights.Heights.Clone();
        bool bentBuilt = form.ApplyRamp();
        Check(bentBuilt && form.UndoText.StartsWith("&Undo ramp:", StringComparison.Ordinal) && form.UndoText.Contains(';', StringComparison.Ordinal),
            $"Apply ramp builds the bending ramp as one step, its bend in the operation: \"{form.UndoText}\"");
        SaveView(form, Path.Combine(outDir, "5_ramp_bending_built.png"), keepView: true);
        form.Undo();
        Check(s.Heights.Heights.AsSpan().SequenceEqual(flat), "one undo takes the bending ramp away");
        var ends = (form.RampPoints![0], form.RampPoints[^1]);
        form.StraightenRampForTest();
        var again = form.CurrentRamp!.Value;
        Check(form.RampPoints == null && again.Path == null && !tools.RampBends &&
              Dist(again.X1, again.Z1, ends.Item1.X, ends.Item1.Z) < 1.5 && Dist(again.X2, again.Z2, ends.Item2.X, ends.Item2.Z) < 1.5,
            "Straighten makes it straight again, between the same ends");

        // A ramp wrapped round the south mesa, as a cliff edge would be: its preview keeps up with a drag, and the dashed
        // edge, wider than the bends are round, cuts across inside them instead of looping.
        view.View = new ViewTransform(1650, 2000, 2);
        tools.RampWidth = 140;
        tools.RampEdge = null;
        (double X, double Z)[] mesa = [(2210, 2110), (2600, 2350), (2630, 2830), (2360, 3080), (2160, 2790)];
        form.SetRampPointsForTest(mesa);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        form.SetRampPointsForTest(mesa.Select((p, i) => i == 2 ? (p.X + 10, p.Z) : p));
        timer.Stop();
        var wrapped = form.RampPreview!.Value.Ramp;
        Check(timer.ElapsedMilliseconds < 250 && wrapped.Edge > wrapped.Path!.Tightest().Radius - wrapped.Width / 2,
            $"moving a point of a ramp wrapped round the mesa updates its preview in {timer.ElapsedMilliseconds} ms (edge {wrapped.Edge:0}, tightest bend {wrapped.Path!.Tightest().Radius:0})");
        // One bend round the mesa's south-east corner, tighter than this edge reaches from the line: its dashed edge is one
        // line, on where the blend ends, going round once (360° in all: a loop would add another 360°).
        var bend = MapCore.Heights.Ramp.Through([(2600, 2350), (2630, 2830), (2300, 2830)], wrapped.Width, wrapped.Edge);
        const double traceStep = 12;
        var traced = bend.EdgeContour(traceStep);
        double turning = 0, off = 0;
        if (traced.Count == 1)
        {
            var line = traced[0];
            for (int i = 0; i < line.Length; i++)
            {
                var (a, b, c) = (line[(i + line.Length - 1) % line.Length], line[i], line[(i + 1) % line.Length]);
                turning += Math.IEEERemainder(Math.Atan2(c.Z - b.Z, c.X - b.X) - Math.Atan2(b.Z - a.Z, b.X - a.X), 2 * Math.PI);
                off = Math.Max(off, Math.Abs(bend.DistanceOutside(b.X, b.Z) - bend.Edge));
            }
        }
        Check(traced.Count == 1 && Math.Abs(Math.Abs(turning) - 2 * Math.PI) < 0.1 && off < traceStep && bend.Edge + bend.Width / 2 > bend.Path!.Tightest().Radius &&
              view.Shapes.Count(shape => shape.Dashed && shape.Closed) >= 1,
            $"the dashed edge round a bend tighter than it is one line where the blend ends, without loops ({traced.Count} line, turning {Math.Abs(turning) * 180 / Math.PI:0}° in all, " +
            $"within {off:0.#} of the edge; bend {bend.Path!.Tightest().Radius:0} round, edge {bend.Width / 2 + bend.Edge:0} from the line)");
        SaveView(form, Path.Combine(outDir, "5_ramp_bending_mesa.png"), keepView: true);
        form.StraightenRampForTest();
        view.View = new ViewTransform(1300, 1300, 2);

        tools.Tool = HeightTool.Raise;
        Check(view.Shapes.Count == 0 && !view.PointerMode, "another tool hides the ramp outline");
    }

    /// <summary>
    /// Step 24: the asset library through the asset browser along the bottom (a library of the self-test's own):
    /// thumbnails grouped by category, chosen there and shown, with what can be done with them, on the right.
    /// </summary>
    static async Task Assets(MainForm form, MapEditSession s, string outDir, Action<bool, string> Check)
    {
        var browser = form.AssetBrowser;
        var panel = form.AssetsPanel;
        // Where things really are, in a window the size of a small laptop screen: the map view, then the splitter
        // (so dragging it resizes the browser), then the browser, all left of the tool tabs, which run the full height.
        var oldSize = form.Size;
        form.Size = new Size(1366, 768);
        form.PerformLayout();
        // The bottom panel at a height of the test's own: the editor starts it at the user's last (their settings).
        var bottomSplit = form.BrowserSplit;
        bottomSplit.SplitterDistance = Math.Max(bottomSplit.Panel1MinSize, bottomSplit.Height - 240 - bottomSplit.SplitterWidth);
        static Rectangle OnForm(Control c)
        {
            var r = c.Bounds;
            for (var p = c.Parent; p != null && p is not Form; p = p.Parent)
                r.Offset(p.Location);
            return r;
        }
        var (mapBox, browserBox, tabsBox) = (OnForm(form.MapView), OnForm(form.BottomTabs), OnForm(form.ToolTabs));
        bool stacked = mapBox.Bottom < browserBox.Top && browserBox.Top - mapBox.Bottom <= 8 && mapBox.Left == browserBox.Left && mapBox.Width == browserBox.Width;
        bool beside = browserBox.Right < tabsBox.Left && tabsBox.Left - browserBox.Right <= 8 && tabsBox.Top <= mapBox.Top && tabsBox.Bottom >= browserBox.Bottom;
        // The tool tabs' width follows the screen (336 at 96 DPI; the hidden window may be made on a 150% screen).
        Check(stacked && beside && mapBox.Height >= 250 && browserBox.Height is >= 150 and <= 300 &&
              tabsBox.Width >= form.LogicalToDeviceUnits(300) && tabsBox.Width <= form.LogicalToDeviceUnits(360) && !form.BrowserSplit.Panel2Collapsed,
            $"in a 1366x768 window the bottom panel is under the map view, the tool tabs down the whole right side " +
            $"(map {mapBox}, bottom panel {browserBox}, tabs {tabsBox})");
        var bottom = form.BottomTabs;
        bottom.SelectedIndex = 0;
        int lines = form.Messages.Count();
        await form.CollectAssetsAsync(); // logs a line or two
        var logTitle = bottom.TabPages[1].Text;
        Check(bottom.TabPages[0].Controls.Contains(browser) && form.Messages.Count() > lines && logTitle.StartsWith("Log (", StringComparison.Ordinal),
            $"the log is a tab beside the asset browser, counting lines not yet seen ({bottom.TabPages[0].Text} | {logTitle})");
        form.ShowLog();
        Check(bottom.SelectedIndex == 1 && bottom.TabPages[1].Text == "Log", "showing the log clears the count");
        bottom.SelectedIndex = 0;
        await form.CollectAssetsAsync(); // thumbnails in the picture
        await browser.ThumbnailsDone;
        CreateHandles(form);
        using (var window = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(window, new Rectangle(Point.Empty, form.Size));
            window.Save(Path.Combine(outDir, "window_asset_browser.png"), ImageFormat.Png);
        }
        form.Size = oldSize;
        bool collectedOnOpen = browser.CollectOnOpen;
        var report = await form.CollectAssetsAsync();
        Check(report != null && (collectedOnOpen ? report.Added == 0 && report.AlreadyThere == 5 : report.Added == 5),
            $"collecting Acidic Quarry {(collectedOnOpen ? "again adds nothing new" : "adds its five rocks")}: {report}");
        var rocks = form.Library.Entries.Where(e => e.HasFiles).Select(e => e.Name).Order().ToList();
        Check(string.Join(",", rocks) == "agorm_rock1,agorm_rock2,agorm_rock3,agorm_rock4,agorm_rock5" &&
              browser.GroupRows.Contains("Rocks (5)") && browser.GroupRows.Contains("Trees & plants (16)") && browser.Summary.Contains(" assets from ", StringComparison.Ordinal),
            $"the browser groups them by category: {string.Join(" | ", browser.GroupRows)}");
        await browser.ThumbnailsDone;
        var rock2 = form.Library.Entries.Single(e => e.Name == "agorm_rock2");
        var tree = form.Library.Entries.First(e => e.Kind == MapCore.Assets.AssetKind.EngineFeature && e.Category == MapCore.Assets.AssetCategories.Trees);
        Check(rocks.All(r => browser.HasThumbnail(form.Library.Entries.Single(e => e.Name == r).Id, r)) &&
              File.Exists(form.Library.ThumbnailPath(rock2, "agorm_rock2")),
            "each rock shows a thumbnail of its model, kept in the library");
        var vent = form.Library.Entries.FirstOrDefault(e => e.Name.Contains("geovent", StringComparison.OrdinalIgnoreCase));
        Check(form.BarFeatures != null && browser.HasThumbnail(tree.Id, tree.Name) && File.Exists(form.Library.ThumbnailPath(form.BarFeatures, tree.Name)) &&
              (vent == null || browser.ImageKeyOf(vent.Id, vent.Name) == "placeholder:vent"),
            $"an engine tree shows a thumbnail of BAR's fir tree (shipped in BarFeatures, {form.BarFeatures?.Origin}: {browser.ImageKeyOf(tree.Id, tree.Name)}); " +
            $"the engine's vent, with no model in game, a plain tile ({(vent == null ? "no vent here" : browser.ImageKeyOf(vent.Id, vent.Name))})");

        browser.Select(MapCore.Assets.AssetCategories.Rocks, rock2.Id);
        Check(panel.Selected?.Asset == rock2 && panel.Details.Contains("This map has it.", StringComparison.Ordinal),
            "choosing a rock at the bottom shows its details on the right, and that this map has it");
        Check(ShowsModel(panel.Preview, out var shown) && panel.Preview.Caption.StartsWith("agorm_rock2", StringComparison.Ordinal),
            $"with a preview of its model: {shown}");
        form.ShowTab("Assets");
        SavePanelPicture(form, panel, Path.Combine(outDir, "panel_assets_preview.png"));
        SavePanelPicture(form, browser, Path.Combine(outDir, "panel_asset_browser.png"), height: 260, width: 1100);
        // Experimental: a new feature from rock2's model with the monolith settings, made and placed from the right.
        Check(panel.CustomEnabled && panel.CustomName == "agorm_rock2_custom", $"the Experimental group offers a new feature from this model ({panel.CustomName})");
        panel.ApplyPreset(1);
        Check(panel.HealthNote.Contains("About 10 hit(s)", StringComparison.Ordinal) && panel.ReclaimNote.Contains("180 s", StringComparison.Ordinal) &&
              panel.ReclaimNote.Contains("120 s", StringComparison.Ordinal),
            $"the monolith example: 10 D-gun hits, 180 s at 200 build power, 120 s for a commander ({panel.HealthNote} | {panel.ReclaimNote})");
        panel.CustomName = "selftest_monolith";
        panel.SetBuildOn(2);
        panel.Press("create");
        var made = Path.Combine(s.Package.Root, "features", "selftest_monolith.lua");
        var gadgetFile = Path.Combine(s.Package.Root, "LuaGaia", "Gadgets", "map_structures.lua");
        var madeDef = File.Exists(made) ? MapCore.Items.FeatureDefs.Read("features/selftest_monolith.lua", File.ReadAllText(made)).Features.SingleOrDefault() : null;
        Check(form.UndoText.StartsWith("&Undo feature-custom:selftest_monolith:agorm_rock2:", StringComparison.Ordinal) &&
              madeDef?.Table?.Get("damage") is 900000.0 && madeDef.Table.Get("autoreclaimable") is false && madeDef.Object == "rock2.s3o" &&
              form.ToolTabs.SelectedTab?.Text == "Items" && form.ItemsPanel.FeatureName == "selftest_monolith" && form.ItemsPanel.Mode == ItemsMode.Place,
            $"Create writes the new feature with rock2's model, as one step, and picks it in Items › Place: \"{form.UndoText}\"");
        Check(s.Structures.BuildOn.TryGetValue("selftest_monolith", out bool noCrossing) && noCrossing && File.Exists(gadgetFile),
            "Build on it (units can't cross it) lists it in the structures config, with the gadget");
        // The map's new features are listed under Experimental; the new one is chosen, the right showing what it is and Place.
        string madeId = ExperimentalTool.MadeId("selftest_monolith");
        Check(ShowsModel(panel.Preview, out var madeShown) &&
              browser.Selected?.Tool?.Made is { Name: "selftest_monolith", MadeFrom: "agorm_rock2", Settings.BuildOn: 2 } && panel.SectionsShown == "tool,made" &&
              browser.ListedNames.Contains("selftest_monolith") && panel.Details.Contains($"{900_000:N0}", StringComparison.Ordinal),
            $"the new feature is listed under Experimental and chosen, the right showing its settings and model, and Place ({panel.SectionsShown}; {madeShown})");
        await browser.ThumbnailsDone;
        Check(browser.ImageKeyOf(madeId, "selftest_monolith") is string madeKey && !madeKey.StartsWith("placeholder:", StringComparison.Ordinal),
            $"its tile is its model in the map ({browser.ImageKeyOf(madeId, "selftest_monolith")})");
        form.ShowTab("Assets");
        SavePanelPicture(form, panel, Path.Combine(outDir, "panel_assets_experimental_feature.png"));
        form.ShowTab("Heights");
        form.ItemsPanel.FeatureName = ""; // (Create picked it already)
        browser.Press("activate");
        Check(form.ToolTabs.SelectedTab?.Text == "Items" && form.ItemsPanel.FeatureName == "selftest_monolith" && form.ItemsPanel.Mode == ItemsMode.Place,
            "double-clicking its entry picks it in Items › Place");
        form.ShowTab("Heights");
        form.ItemsPanel.FeatureName = "";
        panel.Press("place-made");
        Check(form.ToolTabs.SelectedTab?.Text == "Items" && form.ItemsPanel.FeatureName == "selftest_monolith", "and so does Place it");
        // Placing one counts it, and its entry stays chosen.
        form.MapView.View = new ViewTransform(2700, 2800, 1);
        var (placeX, placeY) = form.MapView.View.ToImage(2600, 2700);
        form.MapView.SimulateMouse(MouseButtons.Left, "down", (int)Math.Floor(placeX), (int)Math.Floor(placeY), Keys.None);
        form.MapView.SimulateMouse(MouseButtons.Left, "up", (int)Math.Floor(placeX), (int)Math.Floor(placeY), Keys.None);
        Check(s.Items.All.Count(i => i.Name == "selftest_monolith") == 1 && browser.Selected?.Tool?.Made?.Name == "selftest_monolith" &&
              browser.Selected.Asset.Description?.Contains("1 placed", StringComparison.Ordinal) == true,
            $"placing one counts it under Experimental, its entry still chosen ({browser.Selected?.Asset.Description})");
        form.Undo(); // the one placed
        form.Undo();
        Check(!File.Exists(made) && !File.Exists(gadgetFile) && !s.Structures.BuildOn.ContainsKey("selftest_monolith"), "undo takes the new feature, its listing and the gadget away");
        Check(!browser.ListedNames.Contains("selftest_monolith") && browser.ListedNames.Contains(ExperimentalTool.NewFeatureName),
            "and its entry under Experimental");

        // The experimental tools are listed under Experimental in the browser; each shows only its own section on the right.
        const string experimental = MapCore.Assets.AssetCategories.Experimental;
        browser.Select(experimental, ExperimentalTool.TypeId(MapCore.Structures.StructureType.ShieldPad));
        var padFields = panel.StructureFieldsShown;
        Check(panel.SectionsShown == "tool,structure" && padFields.Contains("Shield") && padFields.Contains("Gap") && !padFields.Contains("Wall width") &&
              !padFields.Contains("Kill time") && browser.ListedNames.Contains("Guardian tower") && browser.ListedNames.Contains(ExperimentalTool.NewFeatureName),
            $"Shield platform, under Experimental, shows only a new kind's section with a platform's fields ({string.Join(", ", padFields)})");
        Check(ShowsModel(panel.Preview, out var padShown), $"and the platform as a new kind of it starts ({padShown})");
        browser.Select(experimental, ExperimentalTool.TypeId(MapCore.Structures.StructureType.Door));
        var doorFields = panel.StructureFieldsShown;
        Check(doorFields.Contains("Wall width") && doorFields.Contains("Height") && !doorFields.Contains("Shield") && !doorFields.Contains("Pump metal") &&
              !doorFields.Contains("Walls"),
            $"Door shows a door's fields, the asset models' only for that look ({string.Join(", ", doorFields)})");
        form.ShowTab("Assets");
        SavePanelPicture(form, panel, Path.Combine(outDir, "panel_assets_experimental_door.png"));
        await browser.ThumbnailsDone;
        Check(browser.HasThumbnail(ExperimentalTool.TypeId(MapCore.Structures.StructureType.Door), "Door") is var _ &&
              browser.ImageKeyOf(ExperimentalTool.TypeId(MapCore.Structures.StructureType.Door), "Door") == ExperimentalTool.TypeId(MapCore.Structures.StructureType.Door) &&
              browser.ImageKeyOf(ExperimentalTool.NewFeatureId, ExperimentalTool.NewFeatureName) == "placeholder:new",
            "each type's tile is its built model; the new-feature tool's a plain tile");
        SavePanelPicture(form, browser, Path.Combine(outDir, "panel_asset_browser_experimental.png"), height: 260, width: 1100);
        string undoBeforeTool = form.UndoText;
        form.ShowTab("Heights");
        browser.Press("activate");
        Check(form.ToolTabs.SelectedTab?.Text == "Heights" && form.UndoText == undoBeforeTool, "double-clicking a type places nothing (a kind of it is made first)");
        browser.Select(experimental, ExperimentalTool.NewFeatureId);
        Check(panel.SectionsShown == "tool,custom" && panel.CustomEnabled && panel.CustomSource?.Name == "agorm_rock2" && ShowsModel(panel.Preview, out _),
            $"the new-feature tool shows only its own section, starting from the model last chosen ({panel.CustomSource?.Name})");
        browser.Select(MapCore.Assets.AssetCategories.Rocks, rock2.Id);
        Check(panel.SectionsShown == "asset,library", $"choosing an asset again shows the asset and its library section only ({panel.SectionsShown})");

        // Experimental: a door kind from the rocks' models, placed on the map from Items › Place, picked and deleted.
        panel.SetStructureType(MapCore.Structures.StructureType.Door);
        panel.SetStructureLook(MapCore.Structures.StructureLook.Assets);
        panel.UseModel("wall", "agorm_rock1");
        panel.UseModel("centre", "agorm_rock2");
        panel.UseModel("repair", "agorm_rock3");
        panel.StructureName = "selftest_door";
        panel.Press("create-kind");
        Check(form.UndoText.StartsWith("&Undo structure-kind:selftest_door:door:", StringComparison.Ordinal) && s.Structures.Kinds.ContainsKey("selftest_door") &&
              panel.StructureKinds.Contains("selftest_door") && File.Exists(gadgetFile),
            $"Create kind makes a door kind from three models, with the gadget, as one step: \"{form.UndoText}\"");
        Check(browser.Selected?.Tool is { Kind: "selftest_door" } && panel.SectionsShown == "tool,kind" && browser.ListedNames.Contains("selftest_door"),
            $"the new kind is listed under Experimental and chosen, the right showing only how to place it ({panel.SectionsShown})");
        Check(!browser.ListedNames.Any(n => n.StartsWith("selftest_door_", StringComparison.Ordinal)),
            "its parts, written from the rocks' models as a new feature is, aren't listed as new features");
        form.ShowTab("Assets");
        SavePanelPicture(form, panel, Path.Combine(outDir, "panel_assets_experimental_kind.png"));
        browser.Press("activate");
        Check(form.ToolTabs.SelectedTab?.Text == "Items" && form.ItemsPanel.FeatureName == "structure:selftest_door",
            "double-clicking the kind's entry picks it in Items › Place");
        panel.Press("place-structure");
        Check(form.ToolTabs.SelectedTab?.Text == "Items" && form.ItemsPanel.Mode == ItemsMode.Place && form.ItemsPanel.FeatureName == "structure:selftest_door",
            "Place picks the kind in Items › Place");
        var mapView = form.MapView;
        mapView.View = new ViewTransform(2700, 2800, 1);
        void Click(double x, double z)
        {
            var (px, py) = mapView.View.ToImage(x, z);
            mapView.SimulateMouse(MouseButtons.Left, "down", (int)Math.Floor(px), (int)Math.Floor(py), Keys.None);
            mapView.SimulateMouse(MouseButtons.Left, "up", (int)Math.Floor(px), (int)Math.Floor(py), Keys.None);
        }
        Click(3001, 2999);
        Check(s.Structures.Instances.SingleOrDefault() is { Kind: "selftest_door", X: 3008, Z: 3008 } && form.SelectedStructure != null &&
              form.UndoText.StartsWith("&Undo structure-add:selftest_door:", StringComparison.Ordinal),
            "a click places the door on the 16-unit grid, as one step, and picks it");
        form.ItemsPanel.Mode = ItemsMode.Select;
        Click(2200, 2200);
        Check(form.SelectedStructure == null, "a click elsewhere drops it");
        Click(3010, 3012);
        Check(form.SelectedStructure is { Kind: "selftest_door" } && form.MapView.Shapes.Count >= 3, "a click on its walls or door picks it, outlined");
        form.RemoveSelectedItems();
        Check(s.Structures.Instances.Count == 0 && form.SelectedStructure == null, "Delete removes it");
        form.Undo();
        form.Undo();
        form.Undo();
        Check(!s.Structures.Kinds.ContainsKey("selftest_door") && !File.Exists(gadgetFile) && s.Structures.Instances.Count == 0,
            "three undos take the removal, the door and the kind (and its gadget) away");

        // Built models: a concrete door from the defaults, placed starting destroyed, picked, then changed from Items › Selection.
        form.ShowTab("Assets");
        panel.SetStructureType(MapCore.Structures.StructureType.Door);
        panel.SetStructureLook(MapCore.Structures.StructureLook.Concrete);
        panel.StructureName = "selftest_gate";
        panel.Press("create-kind");
        var gateWall = Path.Combine(s.Package.Root, "objects3d", "selftest_gate_wall.s3o");
        Check(s.Structures.Kinds.TryGetValue("selftest_gate", out var gate) && gate is { Look: MapCore.Structures.StructureLook.Concrete, Height: 80, WallWidth: 3, Depth: 3 } &&
              File.Exists(gateWall) && File.Exists(Path.Combine(s.Package.Root, "unittextures", MapCore.Structures.StructureModels.Texture1)),
            "Create kind builds a concrete door's models and textures: 80 tall, 3 squares thick, taller and thicker than a commander");
        panel.SetPlaceBroken(true);
        panel.Press("place-structure");
        Click(3001, 2999);
        Check(s.Structures.Instances.SingleOrDefault() is { Kind: "selftest_gate", Broken: true } && form.UndoText.EndsWith(":broken", StringComparison.Ordinal),
            $"Start destroyed: placed with its gap open and repair markers out at the start (\"{form.UndoText}\")");
        panel.SetPlaceBroken(false);
        form.ItemsPanel.Mode = ItemsMode.Select;
        Click(2200, 2200);
        Click(3010, 3012);
        var items = form.ItemsPanel;
        Check(form.SelectedStructure is { Kind: "selftest_gate" } && items.StructureShown && items.ApplyText == "Apply to selftest_gate" && !items.ApplyEnabled &&
              items.Preview.HasModel && items.Preview.Caption.Contains("starts destroyed", StringComparison.Ordinal),
            $"picked: its kind's size and look on the right, and the whole of it in the preview: \"{items.Preview.Caption}\"");
        SavePanelPicture(form, items, Path.Combine(outDir, "panel_items_structure.png"), height: 1750);
        items.SetStructureBroken(false);
        Check(s.Structures.Instances.Single() is { Broken: false } && form.UndoText.StartsWith("&Undo structure-broken:", StringComparison.Ordinal) &&
              form.SelectedStructure != null && items.Preview.Caption.Contains("starts destroyed", StringComparison.Ordinal) == false,
            "untick Starts destroyed: it starts standing, as one step, still picked");
        // Drag it east: it moves on the 16-unit grid, as one step; Q turns it a quarter.
        var (gx, gy) = mapView.View.ToImage(3010, 3012);
        var (hx, hy) = mapView.View.ToImage(3080, 3012);
        mapView.SimulateMouse(MouseButtons.Left, "down", (int)Math.Floor(gx), (int)Math.Floor(gy), Keys.None);
        mapView.SimulateMouse(MouseButtons.Left, "move", (int)Math.Floor(hx), (int)Math.Floor(hy), Keys.None);
        mapView.SimulateMouse(MouseButtons.Left, "up", (int)Math.Floor(hx), (int)Math.Floor(hy), Keys.None);
        Check(s.Structures.Instances.Single() is { X: 3072, Z: 3008 } && form.UndoText.StartsWith("&Undo structure-move:", StringComparison.Ordinal) &&
              form.SelectedStructure is { X: 3072 },
            $"dragging it moves it on the 16-unit grid, as one step, still picked ({s.Structures.Instances.Single().X}, {s.Structures.Instances.Single().Z})");
        int facing = s.Structures.Instances.Single().Facing;
        form.TurnSelectedItems(-15);
        Check(s.Structures.Instances.Single().Facing == ((facing + 3) & 3) && form.UndoText.StartsWith("&Undo structure-turn:", StringComparison.Ordinal) &&
              form.SelectedStructure != null,
            "Q turns it a quarter, as one step, still picked");
        form.Undo();
        form.Undo();
        Check(s.Structures.Instances.Single() is { X: 3008, Z: 3008 } && s.Structures.Instances.Single().Facing == facing, "two undos put it back");
        // Repair markers: placed for it with Place repair markers, then picked, dragged and deleted in Select.
        Click(3010, 3012);
        items.Press("place-markers");
        Check(items.Mode == ItemsMode.Place && items.FeatureName.StartsWith("repair-marker:", StringComparison.Ordinal), $"Place repair markers switches to Place for it ({items.FeatureName})");
        Click(3200, 3008);
        Check(s.Structures.Instances.Single().MarkerList.SequenceEqual([(3200.0, 3008.0)]) && form.UndoText.StartsWith("&Undo structure-marker-add:", StringComparison.Ordinal),
            "a click places a repair marker for it, as one step");
        Check(items.Preview.Model is { } shownGate && shownGate.Mesh.Vertices.Max(v => v.Position.X) - shownGate.Mesh.Vertices.Min(v => v.Position.X) <= 200,
            "the preview still shows the gate alone (its markers stand apart, on the map)");
        items.Mode = ItemsMode.Select;
        Click(3201, 3010);
        Check(form.SelectedMarker == (3200, 3008) && items.SelectionText.Contains("repair marker", StringComparison.Ordinal), $"a click on the marker picks it ({items.SelectionText})");
        var (ax, ay) = mapView.View.ToImage(3201, 3010);
        var (bx, by) = mapView.View.ToImage(3265, 3010);
        mapView.SimulateMouse(MouseButtons.Left, "down", (int)Math.Floor(ax), (int)Math.Floor(ay), Keys.None);
        mapView.SimulateMouse(MouseButtons.Left, "move", (int)Math.Floor(bx), (int)Math.Floor(by), Keys.None);
        mapView.SimulateMouse(MouseButtons.Left, "up", (int)Math.Floor(bx), (int)Math.Floor(by), Keys.None);
        Check(s.Structures.Instances.Single().MarkerList.SequenceEqual([(3264.0, 3008.0)]) && form.UndoText.StartsWith("&Undo structure-marker-move:", StringComparison.Ordinal) &&
              form.SelectedMarker == (3264, 3008),
            "dragging it moves it on the 8-unit grid, as one step, still picked");
        form.RemoveSelectedItems();
        Check(s.Structures.Instances.Single().MarkerList.Count == 0 && form.UndoText.StartsWith("&Undo structure-marker-remove:", StringComparison.Ordinal) &&
              s.Structures.Instances.Count == 1,
            "Delete removes the marker, not its gate");
        form.Undo();
        form.Undo();
        form.Undo();
        Check(s.Structures.Instances.Single().MarkerList.Count == 0, "three undos: no marker again");
        Click(3010, 3012);
        var wallBytes = File.ReadAllBytes(gateWall);
        items.SetStructureSize(112, 5, 3, 3);
        Check(items.ApplyEnabled, "a new size can be applied");
        items.Press("apply-structure");
        Check(s.Structures.Kinds["selftest_gate"] is { Height: 112, Gap: 5 } && !File.ReadAllBytes(gateWall).SequenceEqual(wallBytes) &&
              form.UndoText.StartsWith("&Undo structure-change:selftest_gate:", StringComparison.Ordinal) && form.SelectedStructure != null,
            "Apply builds the kind's models again at the new size, as one step");
        form.Undo();
        Check(s.Structures.Kinds["selftest_gate"] is { Height: 80, Gap: 4 } && File.ReadAllBytes(gateWall).SequenceEqual(wallBytes), "undo puts the old size and models back");
        form.Undo();
        form.Undo();
        form.Undo();
        Check(!s.Structures.Kinds.ContainsKey("selftest_gate") && !File.Exists(gateWall) && !File.Exists(gadgetFile) && s.Structures.Instances.Count == 0,
            "three more undos take the toggle, the gate and its kind (models, textures and gadget) away");

        // An oil pump: made in the Asset tab, placed, picked.
        form.ShowTab("Assets");
        panel.SetStructureType(MapCore.Structures.StructureType.Pump);
        panel.StructureName = "selftest_pump";
        panel.Press("create-kind");
        Check(s.Structures.Kinds.TryGetValue("selftest_pump", out var pumpKind) &&
              pumpKind is { Type: MapCore.Structures.StructureType.Pump, MetalIncome: 2, EnergyIncome: 25, Gap: 4, Depth: 6 } &&
              File.Exists(Path.Combine(s.Package.Root, "objects3d", "selftest_pump_beam.s3o")) &&
              File.Exists(Path.Combine(s.Package.Root, "unittextures", MapCore.Structures.StructureModels.PumpTexture1)),
            "Create kind makes an oil pump: its pump, beam and derelict, its atlas; 2 metal and 25 energy a second");
        panel.Press("place-structure");
        Click(3001, 2999);
        Check(s.Structures.Instances.SingleOrDefault() is { Kind: "selftest_pump" }, "and Place puts one on the map");
        form.ItemsPanel.Mode = ItemsMode.Select;
        Click(2200, 2200);
        Click(3008, 3008);
        Check(form.SelectedStructure is { Kind: "selftest_pump" } && items.Preview.HasModel && !items.ApplyEnabled,
            $"picked: its preview (derelict at the start), no size or look to apply ({items.Preview.Caption})");
        form.Undo();
        form.Undo();
        Check(!s.Structures.Kinds.ContainsKey("selftest_pump") && !File.Exists(gadgetFile), "two undos take the pump and its kind away");

        // A shield platform: made in the Asset tab with its shield's strength, price and size, placed, picked.
        form.ShowTab("Assets");
        panel.SetStructureType(StructureType.ShieldPad);
        Check(panel.StructureSettings is { Gap: 6, Depth: 6, ShieldPercent: 50, EnergyPerMetal: 10, BubbleScale: 1, Look: StructureLook.Metal },
            "a shield platform starts 6 by 6 squares, shields half a unit's health for 10 energy a metal");
        panel.StructureName = "selftest_pad";
        panel.Press("create-kind");
        Check(s.Structures.Kinds.TryGetValue("selftest_pad", out var padKind) && padKind is { Type: StructureType.ShieldPad, Gap: 6, Depth: 6, ShieldPercent: 50 } &&
              File.Exists(Path.Combine(s.Package.Root, "objects3d", "selftest_pad_broken.s3o")) && File.Exists(Path.Combine(s.Package.Root, StructureFiles.ShieldEffectsEntry)),
            "Create kind makes the platform, its derelict, its atlas and the shields' effects");
        panel.Press("place-structure");
        Click(3001, 2999);
        Check(s.Structures.Instances.SingleOrDefault() is { Kind: "selftest_pad" }, "and Place puts one on the map");
        form.ItemsPanel.Mode = ItemsMode.Select;
        Click(2200, 2200);
        Click(3008, 3008);
        Check(form.SelectedStructure is { Kind: "selftest_pad" } && items.Preview.HasModel && !items.ApplyEnabled &&
              items.StructureNote.Contains("bubble shield", StringComparison.Ordinal) && items.ApplyText != "Rebuild selftest_pad's models",
            $"picked: its preview (derelict at the start) and what it does ({items.Preview.Caption})");
        SavePanelPicture(form, panel, Path.Combine(outDir, "panel_assets_shield_pad.png"), height: 2300);
        form.Undo();
        form.Undo();
        Check(!s.Structures.Kinds.ContainsKey("selftest_pad") && !File.Exists(gadgetFile) && !File.Exists(Path.Combine(s.Package.Root, StructureFiles.ShieldEffectsEntry)),
            "two undos take the platform and its kind away");

        await GuardianTower(form, s, outDir, Check, gadgetFile);
        form.ShowTab("Assets");
        form.ShowTab("Assets");
        browser.Select(MapCore.Assets.AssetCategories.Rocks, rock2.Id); // (making a kind chose its Experimental entry)
        browser.Filter = "rock2";
        Check(string.Join(",", browser.ListedNames) == "agorm_rock2" && panel.Selected?.Asset == rock2, "the name filter narrows the browser, keeping the choice");
        browser.Filter = "";
        panel.Press("place");
        Check(form.ToolTabs.SelectedTab?.Text == "Items" && form.ItemsPanel.Mode == ItemsMode.Place && form.ItemsPanel.FeatureName == "agorm_rock2" &&
              form.ItemsPanel.PlaceKind == MapCore.Items.PlacedItemKind.Feature,
            "Place picks it in Items › Place, where its heading and spacing are set");
        browser.Select(MapCore.Assets.AssetCategories.Trees, tree.Id);
        Check(browser.ListedNames.Contains(tree.Name) && panel.Details.Contains("Built into the engine", StringComparison.Ordinal),
            $"the engine's trees are in the browser too, built in ({tree.Name})");
        Check(ShowsModel(panel.Preview, out var firShown) && panel.Preview.Caption.Contains("BAR's model", StringComparison.Ordinal) &&
              panel.Details.Contains("fir_tree_", StringComparison.Ordinal),
            $"its preview shows BAR's fir tree, as BAR draws the engine's trees ({firShown}): \"{panel.Preview.Caption}\"");
        string undoBefore = form.UndoText;
        form.ShowTab("Heights");
        browser.Press("activate");
        Check(form.ToolTabs.SelectedTab?.Text == "Items" && form.ItemsPanel.FeatureName == tree.Name && form.UndoText == undoBefore,
            "double-clicking an engine tree places it by name, with nothing copied");
        await BarsOwn(form, outDir, Check);

        // Collecting from map files without opening them: the map saved earlier has the same rocks, and a non-map is skipped.
        int before = form.Library.Entries.Count;
        var notAMap = Path.Combine(Path.GetTempPath(), $"BarMapEditor-selftest-notamap-{Guid.NewGuid():N}.sdz");
        File.WriteAllText(notAMap, "not a zip");
        await form.CollectFromFilesAsync([Path.Combine(outDir, "selftest_saved.sd7"), notAMap]);
        File.Delete(notAMap);
        Check(form.Library.Entries.Count == before && form.Library.Entries.Count(e => e.HasFiles) == 5 &&
              form.Messages.Any(m => m.Contains("selftest_saved.sd7: 0 new assets, 5 already in the library", StringComparison.Ordinal)) &&
              form.Messages.Any(m => m.Contains("skipped", StringComparison.Ordinal) && m.Contains("notamap", StringComparison.Ordinal)),
            "collecting from map files adds nothing already there, and skips what isn't a map");
        await browser.ThumbnailsDone; // nothing left drawing when the library is deleted
    }

    /// <summary>
    /// Step 34: everything BAR itself offers a map, in the browser from the catalog shipped with the editor (not kept in
    /// the library): units and buildings with their build pictures, features with BAR's models; a unit's details and
    /// model on the right; a building placed from the browser goes to Items › Place as a building; a name BAR lacks is
    /// flagged there.
    /// </summary>
    static async Task BarsOwn(MainForm form, string outDir, Action<bool, string> Check)
    {
        var browser = form.AssetBrowser;
        var panel = form.AssetsPanel;
        var bar = form.BarFeatures;
        Check(bar is { Units.Count: 681 } && bar.Units.Count(u => u.Building) == 302,
            $"BAR's units and buildings ship beside the editor ({bar?.Units.Count} units, {bar?.Units.Count(u => u.Building)} of them buildings)");
        if (bar == null)
            return;

        // Everything, from both, in every category: how long the list takes to fill (its pictures not waited for).
        browser.Select("All categories");
        browser.Filter = "";
        var clock = System.Diagnostics.Stopwatch.StartNew();
        browser.Source = AssetSource.Both;
        long fill = clock.ElapsedMilliseconds;
        var groups = browser.GroupRows.ToList();
        int listed = browser.ListedNames.Count();
        browser.Filter = "armpw"; // (and stops pictures being drawn for all of them)
        Check(listed > 2000 && groups.Any(g => g.StartsWith("Units (", StringComparison.Ordinal)) && groups.Any(g => g.StartsWith("Buildings & structures (", StringComparison.Ordinal)) &&
              groups.Any(g => g.StartsWith("Wrecks (", StringComparison.Ordinal)) && browser.Summary.Contains("of BAR's", StringComparison.Ordinal) && fill < 5000,
            $"with Maps and BAR the browser lists the maps' assets and all of BAR's: {listed:N0} entries, filled in {fill} ms ({string.Join(" | ", groups)})");

        browser.Source = AssetSource.Bar;
        await browser.ThumbnailsDone;
        var names = browser.ListedNames.ToList();
        Check(names.Contains("armpw") && names.Contains("armpw_dead") && names.Contains("armpw_heap") && browser.HasThumbnail("unit:armpw", "armpw") &&
              browser.HasThumbnail("gamefeature:armpw_dead", "armpw_dead") && File.Exists(form.Library.ThumbnailPath(bar, "armpw_dead")),
            $"BAR's Pawn is listed with its build picture, its wreck and heap with BAR's models ({string.Join(", ", names)})");
        browser.Select(MapCore.Assets.AssetCategories.Units, "unit:armpw");
        Check(panel.Selected?.Asset is { BarOnly: true } && panel.Details.Contains("Pawn", StringComparison.Ordinal) && panel.Details.Contains("unitlist", StringComparison.Ordinal) &&
              panel.Details.Contains("Gaia", StringComparison.Ordinal) && !panel.LibraryActionsEnabled && !form.Library.Entries.Any(e => e.Id == "unit:armpw"),
            $"chosen, it is shown as BAR's, placed with the featureplacer for Gaia, not in the library (no Move or Remove): {panel.Details.Replace(Environment.NewLine, " | ")}");
        Check(ShowsModel(panel.Preview, out var pawn) && panel.Preview.Caption.Contains("BAR's model", StringComparison.Ordinal),
            $"its preview is BAR's model ({pawn}): \"{panel.Preview.Caption}\"");

        // A building, placed from the browser: Items › Place, as a building, nothing copied.
        browser.Filter = "armllt";
        browser.Select(MapCore.Assets.AssetCategories.Buildings, "building:armllt");
        string undoBefore = form.UndoText;
        form.ShowTab("Heights");
        browser.Press("activate");
        var items = form.ItemsPanel;
        Check(form.ToolTabs.SelectedTab?.Text == "Items" && items.Mode == ItemsMode.Place && items.FeatureName == "armllt" &&
              items.PlaceKind == MapCore.Items.PlacedItemKind.Building && form.UndoText == undoBefore && items.NameWarning.Length == 0,
            $"double-clicking BAR's Sentry picks it in Items › Place as a building, nothing copied (\"{items.NameWarning}\")");
        items.FeatureName = "armasp"; // BAR removed the air repair pad
        Check(items.NameWarning.Contains("BAR has no unit called", StringComparison.Ordinal), $"a name BAR doesn't have is flagged: \"{items.NameWarning}\"");
        items.FeatureName = "armllt";

        // A picture: Armada's units, by their build pictures.
        browser.Filter = "arm";
        browser.Select(MapCore.Assets.AssetCategories.Units);
        await browser.ThumbnailsDone;
        SavePanelPicture(form, browser, Path.Combine(outDir, "panel_asset_browser_bar_units.png"), height: 300, width: 1100);
        browser.Select("All categories");
        browser.Filter = "";
        browser.Source = AssetSource.Maps;
        form.ShowTab("Assets");
    }

    /// <summary>
    /// Step 30: a guardian tower made in the Asset tab with the units it destroys chosen from BAR's catalog (the targets
    /// dialog is made and used but never shown), placed, its path drawn with Shift+click, changed with Ctrl+click and a
    /// drag, its zone, targets and kind changed from Items › Selection, every change one undo step.
    /// </summary>
    static Task GuardianTower(MainForm form, MapEditSession s, string outDir, Action<bool, string> Check, string gadgetFile)
    {
        var panel = form.AssetsPanel;
        var items = form.ItemsPanel;
        var catalog = form.UnitCatalog;
        Check(catalog is { Units.Count: 681 } && catalog.DefaultTargets.Count == 43,
            $"BAR's unit catalog ships beside the editor: {catalog?.Units.Count} units, {catalog?.DefaultTargets.Count} destroyed by default");
        if (catalog == null)
            return Task.CompletedTask;
        form.ShowTab("Assets");
        panel.SetStructureType(StructureType.Guardian);
        panel.StructureName = "selftest_guardian";
        Check(panel.GuardianTargets.Count == 43 && panel.StructureSettings is { Look: StructureLook.Ancient, KillSeconds: 1, RocketSeconds: 2 },
            "a guardian tower starts ancient, destroying the 43 aircraft that attack the ground in a second");
        SavePanelPicture(form, panel, Path.Combine(outDir, "panel_assets_guardian.png"), height: 1900);

        // The targets dialog, made but not shown: filters, presets and the count.
        using (var dialog = new TargetsDialog(catalog, panel.GuardianTargets, "selftest_guardian"))
        {
            dialog.SetFilter("Stormbringer");
            Check(dialog.ShownUnits.SequenceEqual(["armthund"]) && dialog.Chosen.Contains("armthund"), $"the filter finds a unit by what BAR calls it ({string.Join(",", dialog.ShownUnits)})");
            dialog.Preset("unshown");
            Check(dialog.Chosen.Count == 42 && !dialog.Chosen.Contains("armthund"), "Nothing shown unticks only what the filter shows");
            dialog.SetFilter("", factionIndex: 0, showIndex: 1);
            Check(dialog.ShownUnits.Count == catalog.Aircraft.Count && dialog.ShownUnits.All(n => catalog.Find(n)!.CanFly), $"Aircraft only shows the {catalog.Aircraft.Count} that fly");
            dialog.Preset("air");
            Check(dialog.Chosen.Count == catalog.Aircraft.Count, "All aircraft ticks every one");
            dialog.Preset("none");
            dialog.Preset("default");
            Check(dialog.Chosen.SequenceEqual(catalog.DefaultTargets.Order(StringComparer.Ordinal)) && dialog.CountText.StartsWith("43 destroyed of 681", StringComparison.Ordinal),
                $"the default puts back the aircraft that attack the ground ({dialog.CountText})");
            dialog.SetFilter("", factionIndex: 3, showIndex: 0);
            Check(dialog.ShownUnits.Count == 234 && dialog.ShownUnits.All(n => catalog.Find(n)!.Faction == "Legion"), "a faction shows its units alone");
            // A picture of it as it would show (drawn hidden, never shown): Armada's aircraft, the default ticked.
            dialog.SetFilter("", factionIndex: 1, showIndex: 1);
            DialogPicture(dialog, Path.Combine(outDir, "30_targets_dialog.png"));
            panel.GuardianTargets = dialog.Chosen;
        }
        panel.Press("create-kind");
        Check(s.Structures.Kinds.TryGetValue("selftest_guardian", out var kind) && kind is { Type: StructureType.Guardian, Gap: 3, Depth: 3 } && kind.TargetList.Count == 43 &&
              File.Exists(Path.Combine(s.Package.Root, "objects3d", "selftest_guardian_centre.s3o")) && File.Exists(Path.Combine(s.Package.Root, StructureFiles.RocketWeaponEntry)) &&
              File.Exists(gadgetFile),
            "Create kind makes the tower, its rocket and effects, and the gadget, with its 43 targets");
        panel.Press("place-structure");
        var mapView = form.MapView;
        mapView.View = new ViewTransform(2000, 2200, 2);
        void Click(double x, double z, Keys keys = Keys.None)
        {
            var (px, py) = mapView.View.ToImage(x, z);
            mapView.SimulateMouse(MouseButtons.Left, "down", (int)Math.Floor(px), (int)Math.Floor(py), keys);
            mapView.SimulateMouse(MouseButtons.Left, "up", (int)Math.Floor(px), (int)Math.Floor(py), Keys.None);
        }
        // Where a click at a point lands: the middle of its pixel, to the nearest unit (as path points are kept).
        (double X, double Z) At(double x, double z)
        {
            var (px, py) = mapView.View.ToImage(x, z);
            var (wx, wz) = mapView.View.ToWorld(Math.Floor(px) + 0.5, Math.Floor(py) + 0.5);
            return (Math.Round(wx), Math.Round(wz));
        }
        Click(3001, 2999);
        var tower = s.Structures.Instances.SingleOrDefault();
        Check(tower is { Kind: "selftest_guardian", Zone: null }, $"Place puts the tower on the map, guarding nothing yet ({tower?.X}, {tower?.Z})");
        if (tower == null)
            return Task.CompletedTask;
        items.Mode = ItemsMode.Select;
        Click(2100, 2300);
        Click(tower.X + 4, tower.Z + 4);
        Check(form.SelectedStructure is { Kind: "selftest_guardian" } && items.GuardianShown && !items.DoorGroupShown &&
              items.GuardianZoneText.Contains("guards nothing yet", StringComparison.Ordinal) && items.Preview.HasModel,
            $"picked: the Guardian tower group on the right, its model in the preview ({items.Preview.Caption})");

        // The path: two Shift+clicks make it (one step), a third adds a point; it needn't touch the tower.
        string undoBefore = form.UndoText;
        var (p1, p2, p3, moved) = (At(2400, 2600), At(3400, 2600), At(3400, 3400), At(3500, 3300));
        Click(2400, 2600, Keys.Shift);
        Check(form.ZoneFirstPoint == p1 && form.UndoText == undoBefore, "the first Shift+click starts the path, changing nothing yet");
        Click(3400, 2600, Keys.Shift);
        var zone = s.Structures.Instances.Single().Zone;
        Check(zone is { Distance: 200, Closed: false } && zone.Points.SequenceEqual([p1, p2]) &&
              form.UndoText.StartsWith("&Undo structure-zone:", StringComparison.Ordinal) && form.ZoneFirstPoint == null,
            $"the second makes the path, 200 either side, as one step (\"{form.UndoText}\")");
        Click(3400, 3400, Keys.Shift);
        Check(s.Structures.Instances.Single().Zone?.Points.Count == 3, "another Shift+click adds a point at its end");
        Click(2900, 2604, Keys.Control);
        Check(s.Structures.Instances.Single().Zone?.Points is { Count: 4 } p4 && p4[1] == At(2900, 2604), "Ctrl+click on a run puts a point in it");
        Click(2902, 2602, Keys.Control);
        Check(s.Structures.Instances.Single().Zone?.Points.SequenceEqual([p1, p2, p3]) == true, "Ctrl+click on a point removes it");
        var (ax, ay) = mapView.View.ToImage(3400, 3400);
        var (bx, by) = mapView.View.ToImage(3500, 3300);
        mapView.SimulateMouse(MouseButtons.Left, "down", (int)Math.Floor(ax), (int)Math.Floor(ay), Keys.None);
        mapView.SimulateMouse(MouseButtons.Left, "move", (int)Math.Floor(bx), (int)Math.Floor(by), Keys.None);
        Check(mapView.Shapes.Count(sh => sh.Colour.ToArgb() == Color.OrangeRed.ToArgb() && sh.Dashed) > 0, "while a point is dragged, where the zone would go is drawn");
        mapView.SimulateMouse(MouseButtons.Left, "up", (int)Math.Floor(bx), (int)Math.Floor(by), Keys.None);
        Check(s.Structures.Instances.Single().Zone?.Points[2] == moved && form.SelectedStructure != null, "dragging a point moves it, as one step, the tower still picked");
        items.SetGuardianZoneSettings(300, closed: true);
        zone = s.Structures.Instances.Single().Zone;
        string pathText = string.Join(";", new[] { p1, p2, moved }.Select(p => FormattableString.Invariant($"{p.X},{p.Z}")));
        Check(zone is { Distance: 300, Closed: true } && form.UndoText.StartsWith("&Undo structure-zone:", StringComparison.Ordinal) &&
              form.UndoText.EndsWith(":300:1:" + pathText, StringComparison.Ordinal),
            $"the distance and closed loop change the path's zone (\"{form.UndoText}\")");
        Check(mapView.Shapes.Any(sh => sh.Colour.ToArgb() == Color.OrangeRed.ToArgb()) && items.GuardianZoneText.Contains("closed path of 3 points", StringComparison.Ordinal),
            $"its edge is drawn, and the panel says what it guards: \"{items.GuardianZoneText}\"");
        SaveView(form, Path.Combine(outDir, "30_guardian_zone.png"), keepView: true);

        // Its three regions, from Items › Selection. The band starts with the kind's 43; inside and outside with nothing.
        var band = items.RegionEditor(GuardianRegion.Band);
        var inside = items.RegionEditor(GuardianRegion.Inside);
        var outside = items.RegionEditor(GuardianRegion.Outside);
        Check(band.Rule is { AllTeams: true, DelaySeconds: 0 } && band.Rule.Targets.Count == 43 && band.TargetsText.StartsWith("aircraft that attack the ground", StringComparison.Ordinal) &&
              inside.Rule.Targets.Count == 0 && inside.NoteText.Length == 0 && outside.Rule.Targets.Count == 0,
            $"the band destroys the 43 of every team at once; inside and outside nothing yet (\"{band.TargetsText}\")");
        // Inside: every aircraft of lobby teams 2 to 8 after 30 s there (as OK in the targets dialog, then the boxes and the delay).
        inside.SetTargets(catalog.Aircraft);
        Check(form.UndoText.StartsWith(FormattableString.Invariant($"&Undo structure-rule:{tower.X},{tower.Z}:inside:0:all:"), StringComparison.Ordinal),
            $"choosing its targets is one step (\"{form.UndoText[..Math.Min(70, form.UndoText.Length)]}…\")");
        inside.SetTeamsAndDelay([2, 3, 4, 5, 6, 7, 8], 30);
        var insideRule = MapEditSession.RulesOf(s.Structures.Instances.Single(), s.Structures.Kinds["selftest_guardian"]).Inside;
        Check(insideRule is { DelaySeconds: 30 } && insideRule.TeamsText == "2,3,4,5,6,7,8" && insideRule.Targets.Count == catalog.Aircraft.Count &&
              form.UndoText.StartsWith(FormattableString.Invariant($"&Undo structure-rule:{tower.X},{tower.Z}:inside:30:2,3,4,5,6,7,8:"), StringComparison.Ordinal) &&
              inside.TargetsText.Contains("of teams 2, 3, 4, 5, 6, 7, 8, after 30 s there", StringComparison.Ordinal),
            $"inside: every aircraft of teams 2 to 8 after 30 s, as one step (\"{inside.TargetsText}\")");
        // Outside: untick All (no team), then tick team 1: a step each.
        outside.ClickAll();
        Check(form.UndoText.StartsWith(FormattableString.Invariant($"&Undo structure-rule:{tower.X},{tower.Z}:outside:0:none:"), StringComparison.Ordinal), $"unticking All is a step (\"{form.UndoText}\")");
        outside.ClickTeam(1);
        Check(MapEditSession.RulesOf(s.Structures.Instances.Single(), s.Structures.Kinds["selftest_guardian"]).Outside.TeamsText == "1" && outside.Rule.TeamsText == "1",
            "ticking team 1 is another, and the boxes show it");
        SavePanelPicture(form, items, Path.Combine(outDir, "panel_items_guardian.png"), height: 2300);

        // The kind, from Items › Selection: its look and times.
        items.SetGuardianSettings(StructureLook.Metal, 2, 3);
        items.Press("apply-guardian");
        Check(s.Structures.Kinds["selftest_guardian"] is { Look: StructureLook.Metal, KillSeconds: 2, RocketSeconds: 3 } &&
              form.UndoText.StartsWith("&Undo structure-change:selftest_guardian:", StringComparison.Ordinal) &&
              MapEditSession.RulesOf(s.Structures.Instances.Single(), s.Structures.Kinds["selftest_guardian"]).Inside.DelaySeconds == 30,
            "Apply makes it metal, 2 s to kill and a rocket every 3 s, keeping the tower's rules");
        items.Press("clear-zone");
        Check(s.Structures.Instances.Single().Zone == null && form.UndoText.EndsWith(":none", StringComparison.Ordinal), "Clear the path leaves it guarding nothing, as one step");
        // A second tower takes the first's rules.
        panel.Press("place-structure");
        Click(2400, 3200);
        var second = s.Structures.Instances.Count == 2 ? s.Structures.Instances[1] : null;
        Check(second?.Rules is { } secondRules && secondRules.Inside.Same(insideRule) && secondRules.Outside.TeamsText == "1" && secondRules.Band.Targets.Count == 43,
            "a second tower placed takes the first's rules for all three regions");
        // Back to nothing: the second tower, clear, apply, team 1, All, inside teams and delay, inside targets, closed,
        // distance, drag, remove, insert, third point, path, place, kind.
        for (int i = 0; i < 16; i++)
            form.Undo();
        Check(!s.Structures.Kinds.ContainsKey("selftest_guardian") && s.Structures.Instances.Count == 0 && !File.Exists(gadgetFile) &&
              !File.Exists(Path.Combine(s.Package.Root, StructureFiles.RocketWeaponEntry)),
            "sixteen undos take every change, both towers and their kind (its rocket, effects and gadget) away");
        return Task.CompletedTask;
    }

    /// <summary>Step 15: metal spots through the real pointer handlers.</summary>
    static void Metal(MainForm form, MapEditSession s, Action<bool, string> Check)
    {
        var view = form.MapView;
        var panel = form.MetalPanel;
        form.ShowTab("Metal");
        panel.Mode = MetalMode.Select;
        Check(view.PointerMode && view.Brush == null, "the Metal tab puts the left button in pick-and-drag mode");
        view.View = new ViewTransform(3400, 200, 2);
        Point At(double x, double z)
        {
            var (px, py) = view.View.ToImage(x, z);
            return new Point((int)Math.Floor(px), (int)Math.Floor(py));
        }
        void Mouse(string action, double x, double z) => view.SimulateMouse(MouseButtons.Left, action, At(x, z).X, At(x, z).Y);
        string Hash(byte[] b) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b));
        string MetalState() => Hash(s.Smf.MetalMap) + Hash(s.ResourceBytes(MapEditSession.SplatKey));

        var spot = s.Metal.FindSpots().MinBy(p => Math.Abs(p.X - 3976) + Math.Abs(p.Z - 616))!;
        Mouse("down", spot.X, spot.Z);
        Mouse("up", spot.X, spot.Z);
        Check(form.SelectedSpot == (spot.X, spot.Z) && Math.Abs(form.MetalPanel.ShownIncome - 2.30) < 0.01, "clicking a spot selects it and shows its income");

        var before = MetalState();
        Mouse("down", spot.X, spot.Z);
        Mouse("move", spot.X + 64, spot.Z);
        view.SimulateCaptureLost();
        Mouse("up", spot.X + 64, spot.Z);
        Check(MetalState() == before && view.Marks.Count == 1, "a drag cut off by losing the mouse changes nothing and leaves no ghost");

        var other = s.Metal.FindSpots().Where(p => p.X != spot.X || p.Z != spot.Z).MinBy(p => Math.Abs(p.X - spot.X) + Math.Abs(p.Z - spot.Z))!;
        Mouse("down", spot.X, spot.Z);
        Mouse("move", other.X + 8, other.Z);
        Check(view.Marks.Any(m => m.Colour == Color.Red), "dragging onto other metal shows a red ghost");
        Mouse("up", other.X + 8, other.Z);
        Check(MetalState() == before && form.Messages.Last().Contains("can't go there", StringComparison.Ordinal),
            $"dropping it there is refused, changes nothing, and says why: \"{form.Messages.Last()}\"");

        Mouse("down", spot.X, spot.Z);
        Mouse("move", spot.X + 30, spot.Z + 10);
        Mouse("move", spot.X + 64, spot.Z + 32);
        Mouse("up", spot.X + 64, spot.Z + 32);
        var moved = s.Metal.SpotNear(spot.X + 64, spot.Z + 32, 24);
        Check(moved != null && form.UndoText.StartsWith("&Undo metal-move:", StringComparison.Ordinal) && form.UndoText.EndsWith(":look", StringComparison.Ordinal),
            $"a drag moves the spot and its look as one step: \"{form.UndoText}\"");
        Check(moved != null && form.SelectedSpot == (moved.X, moved.Z), "the moved spot stays selected");
        form.Undo();
        Check(MetalState() == before, "one undo puts the spot and its look back exactly");

        panel.Mode = MetalMode.Add;
        int count = s.Metal.FindSpots().Count;
        var (ax, az, problem) = s.Metal.TryAdd(spot.X - 400, spot.Z + 500);
        Mouse("down", spot.X - 400, spot.Z + 500);
        Mouse("up", spot.X - 400, spot.Z + 500);
        Check(problem == null && s.Metal.FindSpots().Count == count + 1 && form.SelectedSpot is (double nx, double nz) && Math.Abs(nx - ax) < 1 && Math.Abs(nz - az) < 1,
            "Add mode: a click adds a spot and selects it");
        form.DeleteSelectedSpot();
        Check(s.Metal.FindSpots().Count == count && form.SelectedSpot == null, "Delete removes the selected spot");

        panel.Mode = MetalMode.Select;
        Mouse("down", spot.X, spot.Z);
        Mouse("up", spot.X, spot.Z);
        panel.SetIncome(1.5);
        Check(Math.Abs(s.Metal.Income(s.Metal.SpotNear(spot.X, spot.Z, 24)!) - 1.5) < 0.02 && form.UndoText.StartsWith("&Undo metal-income:", StringComparison.Ordinal),
            "typing an income rescales the spot, as one step");
        panel.SetSize(4);
        Check(s.Metal.SpotNear(spot.X, spot.Z, 24)?.Cells.Count == 16 && form.UndoText.StartsWith("&Undo metal-resize:", StringComparison.Ordinal),
            "typing a size reshapes it to 4x4, as one step");
        form.Undo();
        form.Undo();
        Check(MetalState() == before && form.MetalPanel.Rows == 24, "undo restores the spot; the list shows all 24 spots");
        form.ShowTab("Heights");
    }

    /// <summary>Step 16: items, scatter, grass and start positions through the real pointer and brush handlers.</summary>
    static async Task Items(MainForm form, MapEditSession s, string outDir, Action<bool, string> Check)
    {
        var view = form.MapView;
        var panel = form.ItemsPanel;
        form.ShowTab("Items");
        panel.Mode = ItemsMode.Select;
        Check(view.PointerMode && view.Brush == null, "the Items tab puts the left button in pick-and-drag mode");
        view.View = new ViewTransform(1900, 2300, 1);
        Point At(double x, double z)
        {
            var (px, py) = view.View.ToImage(x, z);
            return new Point((int)Math.Floor(px), (int)Math.Floor(py));
        }
        void Mouse(string action, double x, double z, Keys keys = Keys.None) => view.SimulateMouse(MouseButtons.Left, action, At(x, z).X, At(x, z).Y, keys);
        string Hash(byte[] b) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b));
        string ItemState() => Hash(s.Smf.ToBytes()) + Hash(System.Text.Encoding.UTF8.GetBytes(s.Items.Placer!.Document.Text));
        var placer = () => s.Items.All.Where(i => i.Source == MapCore.Items.ItemSource.FeaturePlacer).ToList();

        // A box around three rocks that sit together around (2200, 2580): f2, f7 and f47.
        var rocks = placer().Where(i => i.X is > 2000 and < 2350 && i.Z is > 2450 and < 2700).ToList();
        double x1 = rocks.Min(i => i.X) - 15, z1 = rocks.Min(i => i.Z) - 15, x2 = rocks.Max(i => i.X) + 15, z2 = rocks.Max(i => i.Z) + 15;
        Mouse("down", x1, z1);
        Mouse("move", (x1 + x2) / 2, (z1 + z2) / 2);
        Mouse("move", x2, z2);
        Mouse("up", x2, z2);
        var selected = form.SelectedItems;
        Check(rocks.Count >= 2 && selected.Count == rocks.Count && rocks.All(r => selected.Any(i => i.X == r.X && i.Z == r.Z)),
            $"a box selects the {rocks.Count} items inside it ({selected.Count} selected)");

        var before = ItemState();
        var setBefore = s.Items.Placer!.Document.Text;
        var first = selected[0];
        Mouse("down", first.X, first.Z);
        Mouse("move", first.X + 30, first.Z + 10);
        view.SimulateCaptureLost();
        Mouse("up", first.X + 30, first.Z + 10);
        Check(ItemState() == before, "a drag cut off by losing the mouse changes nothing");

        Mouse("down", first.X, first.Z);
        Mouse("move", first.X + 20, first.Z + 10);
        Mouse("move", first.X + 40, first.Z + 20);
        Mouse("up", first.X + 40, first.Z + 20);
        var moved = form.SelectedItems;
        Check(moved.Count == rocks.Count && rocks.All(r => moved.Any(i => i.X == r.X + 40 && i.Z == r.Z + 20)) &&
              form.UndoText.StartsWith("&Undo items-move:40,20:", StringComparison.Ordinal),
            $"dragging one moves the whole selection as one step: \"{form.UndoText}\"");
        var changedLines = setBefore.Split('\n').Zip(s.Items.Placer!.Document.Text.Split('\n')).Count(p => p.First != p.Second);
        Check(changedLines == rocks.Count, $"set.lua changed only the moved items' lines ({changedLines} lines for {rocks.Count} items)");
        form.Undo();
        Check(ItemState() == before && form.SelectedItems.Count == 0, "one undo restores set.lua and the SMF exactly (the moved selection is gone)");

        // Select two by Ctrl+click, remove them in one step.
        Mouse("down", rocks[0].X, rocks[0].Z);
        Mouse("up", rocks[0].X, rocks[0].Z);
        Mouse("down", rocks[1].X, rocks[1].Z, Keys.Control);
        Mouse("up", rocks[1].X, rocks[1].Z);
        int count = s.Items.All.Count;
        Check(form.SelectedItems.Count == 2, "click then Ctrl+click selects two");
        form.RemoveSelectedItems();
        Check(s.Items.All.Count == count - 2 && form.UndoText.StartsWith("&Undo items-remove:", StringComparison.Ordinal), "Delete removes both as one step");
        form.Undo();
        Check(ItemState() == before, "undo brings them back exactly");

        // Turning: features by the degree, a building by quarters.
        Mouse("down", rocks[0].X, rocks[0].Z);
        Mouse("up", rocks[0].X, rocks[0].Z);
        bool drawn = ShowsModel(panel.Preview, out var shown);
        Check(form.SelectedItems.Count == 1 && drawn && panel.Preview.Caption.StartsWith(form.SelectedItems[0].Name, StringComparison.OrdinalIgnoreCase),
            $"the selected {form.SelectedItems[0].Name} shows its model: {shown}");
        SavePanelPicture(form, panel, Path.Combine(outDir, "panel_items_selected.png"));
        double heading = MapEditSession.HeadingDegrees(form.SelectedItems[0]);
        form.TurnSelectedItems(15);
        Check(Math.Abs((MapEditSession.HeadingDegrees(form.SelectedItems[0]) - heading + 360) % 360 - 15) < 0.1, "E turns a feature 15°");
        form.Undo();
        s.Items.Add(MapCore.Items.ItemSource.FeaturePlacer, MapCore.Items.PlacedItemKind.Building, "armwin", 1500, 1500, 0);
        Mouse("down", 1500, 1500);
        Mouse("up", 1500, 1500);
        form.TurnSelectedItems(15);
        Check(form.SelectedItems.Single().Rotation == 1, "a building turns a whole quarter (east)");
        s.Items.Remove(s.Items.All.Single(i => i.Kind == MapCore.Items.PlacedItemKind.Building));

        // Place one.
        panel.Mode = ItemsMode.Place;
        panel.FeatureName = "agorm_rock1";
        panel.SetNewHeading(90, random: false);
        count = s.Items.All.Count;
        Mouse("down", 1700, 900);
        Mouse("up", 1700, 900);
        var placed = form.SelectedItems.SingleOrDefault();
        Check(s.Items.All.Count == count + 1 && placed?.Name == "agorm_rock1" && Math.Abs(MapEditSession.HeadingDegrees(placed) - 90) < 0.1 &&
              form.UndoText.StartsWith("&Undo item-add:placer:agorm_rock1:", StringComparison.Ordinal),
            "Place adds the feature to the featureplacer, facing east, and selects it");
        form.Undo();

        // Scatter: one step, nothing closer than the spacing.
        panel.Mode = ItemsMode.Scatter;
        panel.FeatureName = "agorm_rock2";
        panel.Radius = 150;
        panel.Spacing = 60;
        count = s.Items.All.Count;
        var oldPositions = s.Items.All.Select(i => (i.X, i.Z)).ToList();
        Mouse("down", 2000, 1300);
        for (int i = 1; i <= 8; i++)
            Mouse("move", 2000 + 50 * i, 1300);
        Mouse("up", 2400, 1300);
        var added = s.Items.All.Skip(0).Select(i => (i.X, i.Z)).Except(oldPositions).ToList();
        var everything = s.Items.All.Select(i => (i.X, i.Z)).ToList();
        bool spaced = added.All(a => everything.Count(b => (a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z) < 60 * 60) == 1);
        Check(s.Items.All.Count - count >= 4 && spaced && form.UndoText == "&Undo scatter agorm_rock2",
            $"a scatter stroke adds {s.Items.All.Count - count} rocks as one step, none closer than 60 units to another");
        view.View = new ViewTransform(1850, 1000, 1);
        SaveView(form, Path.Combine(outDir, "10_scatter.png"), keepView: true);
        form.Undo();
        Check(s.Items.All.Count == count, "one undo removes the whole scatter");

        // Grass: the layer follows, a stroke paints.
        panel.Mode = ItemsMode.Grass;
        for (int i = 0; i < 100 && view.Layer != MapViewLayer.Grass; i++)
            await Task.Delay(50);
        Check(view.Layer == MapViewLayer.Grass, "the grass brush shows the grass layer");
        panel.SetGrass(1, erase: false);
        int grass = MapCore.Smf.GrassLayer.CellsWithGrass(s.Smf);
        Mouse("down", 2000, 2000);
        Mouse("move", 2300, 2000);
        Mouse("up", 2300, 2000);
        Check(MapCore.Smf.GrassLayer.CellsWithGrass(s.Smf) > grass + 20 && form.UndoText == "&Undo paint grass", "a grass stroke paints grass, as one step");
        form.Undo();
        panel.Mode = ItemsMode.Select;

        // Start positions drag in Select mode.
        var start = s.Info.StartPositions.Single(p => p.Team == 3);
        Mouse("down", start.X, start.Z);
        Mouse("move", start.X + 50, start.Z);
        Mouse("move", start.X + 100, start.Z + 40);
        Mouse("up", start.X + 100, start.Z + 40);
        var now = s.Info.StartPositions.Single(p => p.Team == 3);
        Check(Math.Abs(now.X - start.X - 100) < 1 && Math.Abs(now.Z - start.Z - 40) < 1 && form.UndoText.StartsWith("&Undo start-move:3:", StringComparison.Ordinal),
            $"dragging start 3 moves it ({start.X}, {start.Z}) -> ({now.X}, {now.Z}), as one step");
        form.Undo();
        form.ShowTab("Heights");
    }

    /// <summary>Step 13b: the 3D window follows edits and the 2D view, and draws without being shown.</summary>
    static TerrainWindow? ThreeD(MainForm form, MapEditSession s, string outDir, Action<bool, string> Check)
    {
        var window = form.OpenTerrainWindow(show: false);
        Check(window?.View.HasMap == true, "the 3D view opens for the map");
        if (window == null)
            return null;
        var view = window.View;
        var size = new Size(900, 600);
        byte[] Still(string? png = null)
        {
            view.Options = view.Options; // asks for a full-detail frame
            using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
                view.Draw(g, size);
            if (png != null)
                bitmap.Save(png, ImageFormat.Png);
            var data = bitmap.LockBits(new Rectangle(Point.Empty, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var bytes = new byte[data.Stride * data.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            bitmap.UnlockBits(data);
            return bytes;
        }

        view.Camera = new OrbitCamera(3072, 3600, 20, 35, 3200);
        var before = Still(Path.Combine(outDir, "7_3d_before.png"));
        s.RaiseHeights(new MapCore.Heights.Brush(3072, 3000, 300, 0.6), 300);
        var after = Still(Path.Combine(outDir, "8_3d_after_raise.png"));
        int changed = 0;
        for (int i = 0; i < before.Length; i += 4)
            if (before[i] != after[i] || before[i + 1] != after[i + 1] || before[i + 2] != after[i + 2])
                changed++;
        Check(changed > 2000, $"a height edit shows in the 3D view ({changed:N0} pixels changed)");
        s.Undo();
        Check(Still().AsSpan().SequenceEqual(before), "after undo the 3D view is exactly as before");

        // Passable for: each choice tints where that kind of unit can go, with its legend; Off puts the view back.
        var box = window.PassableBox;
        _ = box.ComboBox.Handle; // a combo box without a window raises no SelectedIndexChanged
        var frames = new List<byte[]>();
        foreach (var (index, name, moveClass, limit) in new[]
                 {
                     (1, "Hovercraft", MapCore.Movement.BarMoveClasses.Hover, "cyan over water"),
                     (2, "Vehicles", MapCore.Movement.BarMoveClasses.Vehicle, "up to 27°"),
                     (3, "Bots", MapCore.Movement.BarMoveClasses.Bot, "up to 54°"),
                 })
        {
            box.SelectedIndex = index;
            var frame = Still(Path.Combine(outDir, $"8_3d_passable_{name.ToLowerInvariant()}.png"));
            int tinted = 0;
            for (int i = 0; i < before.Length; i += 4)
                if (before[i] != frame[i] || before[i + 1] != frame[i + 1] || before[i + 2] != frame[i + 2])
                    tinted++;
            Check((string?)box.SelectedItem == name && view.Options.Passability == moveClass &&
                  window.Legend.StartsWith(name, StringComparison.Ordinal) && window.Legend.Contains(limit, StringComparison.Ordinal) &&
                  tinted > size.Width * size.Height / 2 && frames.All(f => !f.AsSpan().SequenceEqual(frame)),
                $"Passable for {name} tints the 3D view its own way ({tinted:N0} pixels): \"{window.Legend.Trim()}\"");
            frames.Add(frame);
        }
        box.SelectedIndex = 0;
        Check(view.Options.Passability == null && window.Legend.Length == 0 && Still().AsSpan().SequenceEqual(before),
            "Off takes the tint and the legend away");

        // Assets: the placed features drawn as their models, on by default; off leaves the bare ground.
        var assets = window.AssetsButton;
        Check(assets.Checked && view.Options.Models, "the 3D view draws the placed assets by default");
        var camera = view.Camera;
        view.Camera = new OrbitCamera(2200, 2580, 30, 35, 600); // Acidic Quarry's rocks around (2200, 2580)
        var withModels = Still(Path.Combine(outDir, "9_3d_assets.png"));
        assets.Checked = false;
        var bare = Still();
        int differ = 0;
        for (int i = 0; i < bare.Length; i += 4)
            if (bare[i] != withModels[i] || bare[i + 1] != withModels[i + 1] || bare[i + 2] != withModels[i + 2])
                differ++;
        Check(!view.Options.Models && differ > 300, $"turning Assets off takes the rocks' models away ({differ:N0} pixels)");
        assets.Checked = true;
        Check(Still().AsSpan().SequenceEqual(withModels), "and turning it on draws them the same again");
        view.Camera = camera;

        // The real input handlers.
        var area = view.ClientSize;
        Check(area.Width > 100 && area.Height > 100, $"the 3D view fills its window: {area}");
        void Frame()
        {
            view.Options = view.Options;
            using var bitmap = new Bitmap(Math.Max(1, area.Width), Math.Max(1, area.Height), PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bitmap);
            view.Draw(g, area);
        }
        view.Camera = new OrbitCamera(2600, 3300, 40, 45, 3000);
        var c0 = view.Camera;
        view.SimulateMouse(MouseButtons.Left, "down", 300, 300);
        view.SimulateMouse(MouseButtons.Left, "move", 360, 330);
        view.SimulateMouse(MouseButtons.Left, "up", 360, 330);
        var c1 = view.Camera;
        Check(c1 with { Yaw = c0.Yaw, Pitch = c0.Pitch } == c0 && Math.Abs(c1.Yaw - (c0.Yaw + 18)) < 1e-9 && Math.Abs(c1.Pitch - (c0.Pitch + 7.5)) < 1e-9,
            $"left drag only rotates (yaw {c0.Yaw} -> {c1.Yaw:0.#}, pitch {c0.Pitch} -> {c1.Pitch:0.#})");

        Frame();
        int mx = area.Width / 2, my = area.Height / 2;
        var grabbed = view.GroundForTest(mx, my);
        view.SimulateMouse(MouseButtons.Right, "down", mx, my);
        view.SimulateMouse(MouseButtons.Right, "move", mx + 80, my + 50);
        view.SimulateMouse(MouseButtons.Right, "up", mx + 80, my + 50);
        Frame();
        if (grabbed is (double gx, double gz) && view.ProjectForTest(gx, s.Heights.HeightAt(gx, gz), gz) is (double px, double py))
        {
            double miss = Math.Sqrt((px - mx - 80) * (px - mx - 80) + (py - my - 50) * (py - my - 50));
            Check(miss < 15 && view.Camera.Yaw == c1.Yaw && view.Camera.Distance == c1.Distance,
                $"right drag moves the camera so the ground follows the mouse (off by {miss:0.#} px at yaw {c1.Yaw:0.#})");
        }
        else
            Check(false, "right drag: the grabbed ground point could not be found again");

        var c2 = view.Camera;
        view.SimulateMouse(MouseButtons.None, "wheel", mx, my, 120);
        Check(view.Camera == c2 with { Distance = c2.Distance / 1.15 }, "the wheel only zooms");

        var c3 = view.Camera;
        view.SimulateMouse(MouseButtons.Left, "down", 300, 300);
        view.SimulateCaptureLost();
        view.SimulateMouse(MouseButtons.Left, "move", 420, 300);
        view.SimulateMouse(MouseButtons.Left, "up", 420, 300);
        Check(view.Camera == c3, "losing the mouse ends a drag");

        form.MapView.View = new ViewTransform(1000, 4000, 2);
        window.LookAt2DForTest();
        var (cx, cz) = form.MapView.View.ToWorld(form.MapView.ClientSize.Width / 2.0, form.MapView.ClientSize.Height / 2.0);
        Check(Math.Abs(view.Camera.TargetX - cx) < 1e-6 && Math.Abs(view.Camera.TargetZ - cz) < 1e-6, "Look at 2D view centres the 3D camera on the 2D view");
        return window;
    }

    /// <summary>
    /// Symmetry on the toolbar: a raise stroke with Mirror (left | right) is made at its mirror image too, as one undo
    /// step, with the line drawn; Mirror + flip places an item's twin at the opposite corner, turned half round; off again.
    /// </summary>
    static void Symmetry(MainForm form, MapEditSession s, string outDir, Action<bool, string> Check)
    {
        var view = form.MapView;
        view.View = new ViewTransform(1000, 1500, 2);
        Point At(double x, double z)
        {
            var (px, py) = view.View.ToImage(x, z);
            return new Point((int)Math.Floor(px), (int)Math.Floor(py));
        }
        void Click(double x, double z)
        {
            var p = At(x, z);
            view.SimulateMouse(MouseButtons.Left, "down", p.X, p.Y);
            view.SimulateMouse(MouseButtons.Left, "up", p.X, p.Y);
        }

        Check(s.Mirror == null && view.Mirror == null, "symmetry starts off");
        form.SetSymmetry(MirrorKind.LeftRight);
        Check(s.Mirror == MirrorKind.LeftRight && view.Mirror is { Kind: MirrorKind.LeftRight },
            "Symmetry: Mirror, Left | right gives the map its mirror and draws the line");
        form.ShowTab("Heights");
        var tools = form.HeightTools;
        var tool = tools.Tool;
        tools.Tool = HeightTool.Raise;
        tools.Radius = 160;
        var before = (float[])s.Heights.Heights.Clone();
        string undoBefore = form.UndoText;
        Click(1400, 2000);
        var dab = view.View.ToWorld(At(1400, 2000).X + 0.5, At(1400, 2000).Y + 0.5);
        double here = s.Heights.HeightAt(dab.X, dab.Z) - Sample(before, s, dab.X, dab.Z);
        double there = s.Heights.HeightAt(s.MapWidth - dab.X, dab.Z) - Sample(before, s, s.MapWidth - dab.X, dab.Z);
        Check(here > 1 && Math.Abs(here - there) < 0.01 && form.UndoText == "&Undo raise heights",
            $"a raise stroke rises alike at its mirror image, as one step (+{here:0.##} and +{there:0.##}; \"{form.UndoText}\")");
        // A picture: the line, and the brush with its dashed copy.
        view.ShowBrushAt(At(1400, 2000));
        view.View = ViewTransform.Fit(s.MapWidth, s.MapHeight, 900, 700);
        view.ShowBrushAt(At(1400, 2000));
        SaveView(form, Path.Combine(outDir, "symmetry_mirror.png"), keepView: true);
        view.ShowBrushAt(null);
        form.Undo();
        Check(s.Heights.Heights.AsSpan().SequenceEqual(before) && form.UndoText == undoBefore, "one undo takes the stroke and its mirror image away");

        // Mirror + flip: a half turn; an item placed is placed again at the opposite corner, facing the other way.
        form.SetSymmetry(MirrorKind.HalfTurn);
        Check(s.Mirror == MirrorKind.HalfTurn && view.Mirror is { Kind: MirrorKind.HalfTurn }, "Mirror + flip turns the copy half round");
        form.ShowTab("Items");
        var panel = form.ItemsPanel;
        panel.Mode = ItemsMode.Place;
        panel.PlaceKind = MapCore.Items.PlacedItemKind.Feature;
        panel.FeatureName = "agorm_rock1";
        panel.SetNewHeading(90, random: false);
        view.View = new ViewTransform(1000, 1500, 2);
        int count = s.Items.All.Count;
        Click(1700, 1900);
        var placed = form.SelectedItems.SingleOrDefault();
        var twin = placed == null ? null : s.Items.All.FirstOrDefault(i => i.Name == "agorm_rock1" && Math.Abs(i.X - (s.MapWidth - placed.X)) < 0.01 &&
                                                                         Math.Abs(i.Z - (s.MapHeight - placed.Z)) < 0.01);
        Check(s.Items.All.Count == count + 2 && twin != null && Math.Abs(MapEditSession.HeadingDegrees(twin) - 270) < 0.1,
            $"Place puts its twin at the opposite corner, facing west ({twin?.X:0}, {twin?.Z:0})");
        SaveView(form, Path.Combine(outDir, "symmetry_flip.png"));
        form.Undo();
        Check(s.Items.All.Count == count, "one undo removes both");

        form.SetSymmetry(null);
        Check(s.Mirror == null && view.Mirror == null, "Off: edits are made once again");
        tools.Tool = tool;
        form.ShowTab("Heights");
    }

    /// <summary>
    /// The Beautify panel: opened and closed from View; "Beautify the map" roughens the cliffs and paints the texture in
    /// the map's own colours as one undo step, leaving where every unit goes, the metal and the water as they were.
    /// </summary>
    static async Task Beautify(MainForm form, MapEditSession s, string outDir, Action<bool, string> Check)
    {
        form.ShowBeautifyPanel(false, select: false);
        Check(!form.BeautifyOpen, "the Beautify panel can be closed (its tab goes)");
        form.ShowBeautifyPanel(true, select: true);
        Check(form.BeautifyOpen && form.ToolTabs.SelectedTab?.Text == "Beautify", "and opened from View › Beautify panel, as a tab beside the tools");
        var panel = form.BeautifyPanel;
        var defaultRock = new MapCore.Editing.BeautifySettings().Rock;
        var access = new MapCore.Movement.AccessMap(s.Heights);
        var heights = (float[])s.Heights.Heights.Clone();
        var metal = (byte[])s.Smf.MetalMap.Clone();
        var texture = (byte[])s.TextureImage.Pixels.Clone();
        string undoBefore = form.UndoText;
        bool ran = await form.BeautifyAsync(cliffs: true, texture: true);
        int heightsChanged = s.Heights.Heights.Zip(heights).Count(p => p.First != p.Second);
        bool textureChanged = !s.TextureImage.Pixels.AsSpan().SequenceEqual(texture);
        var differences = access.Differences(new MapCore.Movement.AccessMap(s.Heights), 5);
        Check(ran && form.UndoText == "&Undo beautify the map" && heightsChanged > 10_000 && textureChanged,
            $"Beautify the map roughens the cliffs and paints the texture, as one step ({heightsChanged:N0} heights; \"{form.UndoText}\")");
        Check(differences.Count == 0 && s.Smf.MetalMap.AsSpan().SequenceEqual(metal),
            $"every movement class goes where it went, and the metal is as it was ({string.Join("; ", differences)})");
        Check(panel.Settings.Rock != defaultRock, $"From this map: the panel shows the colours read from the map (rock {panel.Settings.Rock})");
        form.ShowTab("Beautify");
        SavePanelPicture(form, panel, Path.Combine(outDir, "panel_beautify.png"), height: 1400);
        SaveView(form, Path.Combine(outDir, "beautify_after.png"));
        form.Undo();
        Check(s.Heights.Heights.AsSpan().SequenceEqual(heights) && s.TextureImage.Pixels.AsSpan().SequenceEqual(texture) && form.UndoText == undoBefore,
            "one undo takes it all back exactly");
        form.ShowBeautifyPanel(false, select: false);
        form.ShowTab("Heights");
    }

    /// <summary>
    /// World Machine through the editor (World Machine itself isn't here: its outputs are written by the test): Send
    /// writes the files; a pass-through comes back as it went, as one step; with the folder watched, an output written
    /// there comes back by itself.
    /// </summary>
    static async Task WorldMachine(MainForm form, MapEditSession s, string outDir, Action<bool, string> Check)
    {
        var folder = Path.Combine(outDir, "worldmachine");
        if (Directory.Exists(folder))
            FileSystemUtil.DeleteTree(folder);
        form.SetWorldMachineFolder(folder);
        Check(await form.SendToWorldMachineAsync() && File.Exists(Path.Combine(folder, MapCore.WorldMachine.WorldMachineFiles.Heights)) &&
              File.Exists(Path.Combine(folder, MapCore.WorldMachine.WorldMachineFiles.InfoFile)) && File.Exists(Path.Combine(folder, MapCore.WorldMachine.WorldMachineFiles.ReadMe)),
            "Send to World Machine writes the heights, the texture, the masks, bar_map.json and README.txt into the map's folder");
        var sent = Path.Combine(folder, MapCore.WorldMachine.WorldMachineFiles.Heights);
        var output = Path.Combine(folder, "wm_heights.png");
        File.Copy(sent, output, overwrite: true);
        var heights = (float[])s.Heights.Heights.Clone();
        string undoBefore = form.UndoText;
        bool brought = await form.BringBackFromWorldMachineAsync(null);
        Check(brought && form.UndoText == "&Undo bring back from World Machine" && form.BeautifyPanel.WorldMachineNote.Contains("came back as it went", StringComparison.Ordinal),
            $"a pass-through comes back as it went, as one step: \"{form.BeautifyPanel.WorldMachineNote.Split('\n')[0]}\"");
        form.Undo();
        Check(s.Heights.Heights.AsSpan().SequenceEqual(heights) && form.UndoText == undoBefore, "undo takes it back");

        // Watched: World Machine writes an output (here, a raised patch), and it comes back by itself.
        File.Delete(output);
        form.SetWorldMachineLive(true);
        var png = MapCore.Imaging.PngReader.Read(sent);
        var grey = png.ToGray16();
        for (int z = 300; z < 340; z++)
            for (int x = 300; x < 340; x++)
                grey[z * png.Width + x] = (ushort)Math.Min(65535, grey[z * png.Width + x] + 600);
        MapCore.Imaging.PngWriter.WriteGray16(output, png.Width, png.Height, grey);
        for (int i = 0; i < 80 && (form.UndoText == undoBefore || form.Busy); i++)
            await Task.Delay(250); // until it has come back and the editor is free again
        double raised = 0;
        for (int z = 300; z < 340; z++)
            for (int x = 300; x < 340; x++)
                raised = Math.Max(raised, s.Heights[x, z] - heights[z * s.Heights.Width + x]);
        Check(form.UndoText == "&Undo bring back from World Machine" && raised > 1,
            $"watching the folder, an output World Machine writes comes back by itself (the patch raised {raised:0.#})");
        form.SetWorldMachineLive(false);
        form.Undo();
        form.ShowBeautifyPanel(true, select: true);
        SavePanelPicture(form, form.BeautifyPanel, Path.Combine(outDir, "panel_beautify_worldmachine.png"), height: 2600);
        form.ShowBeautifyPanel(false, select: false);
        form.ShowTab("Heights");
    }

    /// <summary>A height from a copy of the heights (as HeightMap samples them: bilinear between vertices 8 units apart).</summary>
    static double Sample(float[] heights, MapEditSession s, double x, double z)
    {
        int w = s.Heights.Width, h = s.Heights.Height;
        double gx = Math.Clamp(x / 8, 0, w - 1), gz = Math.Clamp(z / 8, 0, h - 1);
        int x0 = (int)Math.Floor(gx), z0 = (int)Math.Floor(gz), x1 = Math.Min(x0 + 1, w - 1), z1 = Math.Min(z0 + 1, h - 1);
        double fx = gx - x0, fz = gz - z0;
        double top = heights[z0 * w + x0] * (1 - fx) + heights[z0 * w + x1] * fx, bottom = heights[z1 * w + x0] * (1 - fx) + heights[z1 * w + x1] * fx;
        return top * (1 - fz) + bottom * fz;
    }

    static void SaveView(MainForm form, string png, bool keepView = false)
    {
        using var bitmap = new Bitmap(900, 700, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            var view = form.MapView;
            if (view.HasMap && !keepView)
                view.View = ViewTransform.Fit(form.Map!.Session.MapWidth, form.Map.Session.MapHeight, bitmap.Width, bitmap.Height);
            view.Draw(g, bitmap.Size);
        }
        bitmap.Save(png, ImageFormat.Png);
    }
}
