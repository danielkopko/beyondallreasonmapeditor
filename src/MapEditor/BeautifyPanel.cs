using System.ComponentModel;
using MapCore.Editing;
using MapCore.Imaging;
using static MapEditor.PanelLayout;

namespace MapEditor;

/// <summary>
/// The Beautify panel (View › Beautify panel): makes a map look natural in one go, by its terrain. The texture painted
/// with rock on cliffs, dirt and scree on slopes and at cliff feet, sand and a wet edge along the water, shade in
/// hollows (<see cref="BeautifySettings"/>), and cliffs roughened with lumps, ledges and gullies (<see cref="CliffDetailSettings"/>),
/// all without changing where units or buildings can go, metal, water or anything placed. Colours start as the map's
/// own or a landscape's; every one can be chosen. It only holds the settings and raises events; the main window runs them.
/// </summary>
sealed class BeautifyPanel : UserControl
{
    readonly ComboBox preset = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    readonly NumericUpDown strength = Number(0, 100, 75, 0, 5), detail = Number(0, 100, 50, 0, 5), breakup = Number(0, 100, 60, 0, 5);
    readonly Button both = new() { Text = "Beautify the map", AutoSize = true };
    readonly Button paint = new() { Text = "Paint the texture", AutoSize = true };
    readonly Button roughen = new() { Text = "Roughen the cliffs", AutoSize = true };
    readonly Button exportLayer = new() { Text = "Export layer…", AutoSize = true };
    readonly Button importLayer = new() { Text = "Import layer…", AutoSize = true };
    readonly Dictionary<string, (Button Swatch, NumericUpDown Amount)> materials = [];
    readonly Dictionary<string, Rgba> colours = [];
    readonly NumericUpDown rockFrom = Number(0, 89, 38, 0, 1), rockTo = Number(1, 90, 56, 0, 1), strata = Number(0, 100, 45, 0, 5);
    readonly NumericUpDown dirtFrom = Number(0, 89, 16, 0, 1), dirtTo = Number(1, 90, 32, 0, 1), sandHeight = Number(1, 500, 24, 0, 2);
    readonly NumericUpDown crevices = Number(0, 100, 50, 0, 5), soften = Number(0, 100, 0, 0, 5);
    readonly NumericUpDown cliffFrom = Number(10, 85, 40, 0, 1), cliffSize = Number(0, 60, 10, 0, 1), ledges = Number(0, 100, 50, 0, 5),
        gullies = Number(0, 100, 50, 0, 5), swell = Number(0, 20, 0, 1, 0.5m);
    readonly Label note = Hint("");
    bool filling;

    // World Machine: the exchange folder, sending, bringing back (by hand, as World Machine writes, or after a build).
    readonly Label wmFolder = new() { AutoSize = true, MaximumSize = new Size(240, 0), Margin = new Padding(3, 4, 3, 2) };
    readonly Button wmChooseFolder = new() { Text = "Folder…", AutoSize = true }, wmOpenFolder = new() { Text = "Open", AutoSize = true };
    readonly Button wmSend = new() { Text = "Send to World Machine", AutoSize = true }, wmBring = new() { Text = "Bring back", AutoSize = true };
    readonly CheckBox wmHeights = new() { Text = "Heights", AutoSize = true, Checked = true }, wmTexture = new() { Text = "Texture", AutoSize = true, Checked = true },
        wmSplat = new() { Text = "Splat", AutoSize = true, Checked = true }, wmGrass = new() { Text = "Grass", AutoSize = true, Checked = true };
    readonly CheckBox wmKeep = new() { Text = "Keep where units and buildings go", AutoSize = true, Checked = true };
    readonly ComboBox wmRange = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, DropDownWidth = 220 };
    readonly CheckBox wmLive = new() { Text = "Bring back as World Machine writes", AutoSize = true };
    readonly Label wmProgram = new() { AutoSize = true, MaximumSize = new Size(240, 0), Margin = new Padding(3, 4, 3, 2) };
    readonly Label wmProject = new() { AutoSize = true, MaximumSize = new Size(240, 0), Margin = new Padding(3, 4, 3, 2) };
    readonly Button wmChooseProgram = new() { Text = "Program…", AutoSize = true }, wmChooseProject = new() { Text = "Project…", AutoSize = true };
    readonly Button wmBuild = new() { Text = "Build in World Machine", AutoSize = true };
    readonly Label wmNote = Hint("");

    /// <summary>The materials: name in the settings, label, what it is.</summary>
    static readonly (string Key, string Label, string Tip)[] Materials =
    [
        ("rock", "Cliff rock", "Laid on cliffs, with layers that follow the height"),
        ("dirt", "Dirt, scree", "On middle slopes, and heaped at the feet of cliffs"),
        ("sand", "Shore sand", "Along the water, up to the sand height"),
        ("mud", "Wet edge", "Just at the waterline: mud, or wet sand"),
        ("seabed", "Seabed", "Under the water, darker as it deepens"),
        ("ground", "Open ground", "A tint for level ground (0%: the map's own ground is left)"),
    ];

    public BeautifyPanel()
    {
        AutoScroll = true;
        Padding = new Padding(6);
        var layout = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Top, AutoSize = true };
        Controls.Add(layout);
        var tips = new ToolTip();

        var look = Grid();
        AddWide(look, Hint("Makes the map look natural in one go, by its terrain: rock on the cliffs, dirt and scree on slopes and at cliff feet, " +
                           "sand and a wet edge along the water, shade in hollows, and rougher cliffs. Metal spots, structures, water and where every " +
                           "unit and building can go are kept. Each run is one undo step."));
        preset.Items.AddRange([.. BeautifySettings.PresetNames]);
        preset.SelectedIndex = 0;
        AddRow(look, "Colours", preset, "");
        AddRow(look, "Strength", strength, "%");
        AddRow(look, "Made-up detail", detail, "%");
        AddRow(look, "Wandering edges", breakup, "%");
        AddWide(look, both);
        AddWide(look, note);
        layout.Controls.Add(Group("Beautify", look));
        tips.SetToolTip(preset, "From this map: its own rock and ground colours, read from its steep and level ground. The others: a landscape's.");
        tips.SetToolTip(strength, "How much of everything below: 100% lays each material fully; lower keeps more of the map's own texture");
        tips.SetToolTip(detail, "Grain and tone made up where the texture is plain (a freshly painted map); texture with grain of its own gets less");
        tips.SetToolTip(breakup, "How much the edges between materials wander, as on real ground");
        tips.SetToolTip(both, "Roughen the cliffs, then paint the texture, as one undo step");

        var paints = Grid();
        foreach (var (key, label, tip) in Materials)
        {
            var swatch = BarTheme.Swatch(new Button { Width = 80, Height = 24 });
            var amount = Number(0, 100, 50, 0, 5);
            amount.Width = 60;
            var after = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
            after.Controls.AddRange([amount, new Label { Text = "%", AutoSize = true, ForeColor = BarTheme.Dim, Margin = new Padding(3, 7, 3, 3) }]);
            AddRow(paints, label, swatch, after);
            tips.SetToolTip(swatch, tip + ". Click to choose its colour.");
            tips.SetToolTip(amount, "How strongly it is laid where it belongs");
            swatch.Click += (_, _) => PickColour(key);
            materials[key] = (swatch, amount);
        }
        AddRow(paints, "Rock from", rockFrom, "° slope");
        AddRow(paints, "Rock full at", rockTo, "°");
        AddRow(paints, "Rock layers", strata, "%");
        AddRow(paints, "Dirt from", dirtFrom, "° slope");
        AddRow(paints, "Dirt full at", dirtTo, "°");
        AddRow(paints, "Sand height", sandHeight, "above water");
        AddRow(paints, "Shade hollows", crevices, "%");
        AddRow(paints, "Soften hard edges", soften, "%");
        AddWide(paints, Hint("Soften hard edges blends sharp edges already painted (roads, plates) with dirt; it is off unless set."));
        AddWide(paints, paint);
        layout.Controls.Add(Group("Texture", paints));

        var cliffs = Grid();
        AddRow(cliffs, "Cliffs from", cliffFrom, "° slope");
        AddRow(cliffs, "Size", cliffSize, "game units");
        AddRow(cliffs, "Ledges", ledges, "%");
        AddRow(cliffs, "Gullies", gullies, "%");
        AddRow(cliffs, "Swell on open ground", swell, "game units");
        AddWide(cliffs, Hint("Broken rock, ledges and gullies on the cliffs; nothing that changes where units or buildings go. Every slope stays on its " +
                             "side of each unit's limit (vehicles 27°, heavy tanks and hovercraft 33°, bots 54°), the water's edge and depth limits stay, " +
                             "and ground flat enough for any building stays so. Where a change would cross one, it is softened or left out. " +
                             "Run it before painting the texture, so the paint follows the new rock."));
        AddWide(cliffs, roughen);
        layout.Controls.Add(Group("Cliff shape", cliffs));

        var layers = Grid();
        AddWide(layers, Hint("Each layer to and from PNG, to work on elsewhere: heights (16-bit), texture, metal, grass, terrain types, splat distribution, " +
                             "normal map. The same as File › Import layer and Export layer."));
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
        buttons.Controls.AddRange([exportLayer, importLayer]);
        AddWide(layers, buttons);
        layout.Controls.Add(Group("Layers", layers));

        var wm = Grid();
        AddWide(wm, Hint("Work on the map in World Machine and bring the results back. Send writes the heights (16-bit), the texture, masks " +
                         "(water, slope, cliffs, what is kept) and README.txt, which says how to set World Machine up, into the folder. " +
                         "World Machine's outputs written there as wm_heights.png, wm_texture.png, wm_splat.png and wm_grass.png come back " +
                         "resampled to the map's sizes, the right way up, each as one undo step."));
        AddWide(wm, wmFolder);
        var folderButtons = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
        folderButtons.Controls.AddRange([wmChooseFolder, wmOpenFolder]);
        AddWide(wm, folderButtons);
        AddWide(wm, wmSend);
        var layerChecks = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = true, MaximumSize = new Size(280, 0) };
        layerChecks.Controls.AddRange([wmHeights, wmTexture, wmSplat, wmGrass]);
        AddWide(wm, layerChecks);
        wmRange.Items.AddRange(["As sent", "Stretch to the map's range"]);
        wmRange.SelectedIndex = 0;
        AddRow(wm, "Heights", wmRange, "");
        AddWide(wm, wmKeep);
        AddWide(wm, Hint("Keep: what would change where any unit can drive or a building can stand is blended back to the map as it was, " +
                         "and metal spots, structures and start positions stay as they are."));
        AddWide(wm, wmBring);
        AddWide(wm, wmLive);
        AddWide(wm, Hint("World Machine Professional runs automation scripts: Build in World Machine writes build.xml (load the project, " +
                         "build at the heightmap's resolution, write the outputs), runs it, and brings the outputs back as they are written."));
        AddWide(wm, wmProgram);
        AddWide(wm, wmProject);
        var buildButtons = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
        buildButtons.Controls.AddRange([wmChooseProgram, wmChooseProject]);
        AddWide(wm, buildButtons);
        AddWide(wm, wmBuild);
        AddWide(wm, wmNote);
        layout.Controls.Add(Group("World Machine", wm));
        tips.SetToolTip(wmSend, "Write the map's heights, texture and masks, and the set-up notes, into the folder");
        tips.SetToolTip(wmBring, "Bring World Machine's outputs back from the folder, the ones ticked, as one undo step");
        tips.SetToolTip(wmLive, "Watch the folder: each time World Machine writes an output, bring it back (one undo step each time)");
        tips.SetToolTip(wmRange, "As sent: World Machine's heights map onto the range sent (bar_map.json). Stretch: whatever its scale, onto the map's own range");
        wmChooseFolder.Click += (_, _) => WorldMachineRequested?.Invoke(this, "folder");
        wmOpenFolder.Click += (_, _) => WorldMachineRequested?.Invoke(this, "open");
        wmSend.Click += (_, _) => WorldMachineRequested?.Invoke(this, "send");
        wmBring.Click += (_, _) => WorldMachineRequested?.Invoke(this, "bring");
        wmChooseProgram.Click += (_, _) => WorldMachineRequested?.Invoke(this, "program");
        wmChooseProject.Click += (_, _) => WorldMachineRequested?.Invoke(this, "project");
        wmBuild.Click += (_, _) => WorldMachineRequested?.Invoke(this, "build");
        wmLive.CheckedChanged += (_, _) => { if (!filling) WorldMachineRequested?.Invoke(this, wmLive.Checked ? "live-on" : "live-off"); };
        ShowWorldMachine(null, null, null);

        preset.SelectedIndexChanged += (_, _) =>
        {
            if (filling)
                return;
            if (preset.SelectedIndex == 0)
                PresetChosen?.Invoke(this, EventArgs.Empty);
            else
                Settings = Settings.WithPreset((string)preset.SelectedItem!);
        };
        paint.Click += (_, _) => Press("paint");
        roughen.Click += (_, _) => Press("roughen");
        both.Click += (_, _) => Press("both");
        exportLayer.Click += (_, _) => LayerMenuRequested?.Invoke(this, (exportLayer, false));
        importLayer.Click += (_, _) => LayerMenuRequested?.Invoke(this, (importLayer, true));
        Settings = new BeautifySettings();
    }

    /// <summary>Paint the texture.</summary>
    public event EventHandler? PaintRequested;

    /// <summary>Roughen the cliffs.</summary>
    public event EventHandler? RoughenRequested;

    /// <summary>Both, as one undo step: the cliffs first, then the texture.</summary>
    public event EventHandler? BothRequested;

    /// <summary>"From this map" chosen: the main window reads the map's colours and sets them.</summary>
    public event EventHandler? PresetChosen;

    /// <summary>
    /// A World Machine button: folder (choose), open, send, bring, program, project, build, live-on, live-off.
    /// </summary>
    public event EventHandler<string>? WorldMachineRequested;

    /// <summary>Shows the exchange folder, World Machine's program and the project (null: none chosen).</summary>
    public void ShowWorldMachine(string? folder, string? program, string? project)
    {
        wmFolder.Text = folder == null ? "Folder: none (open a map)" : "Folder: " + folder;
        wmOpenFolder.Enabled = folder != null && Directory.Exists(folder);
        wmProgram.Text = "Program: " + (program ?? "not chosen (World Machine.exe, Professional edition)");
        wmProject.Text = "Project: " + (project ?? "not chosen (a .tmd that reads heights.png)");
        wmBuild.Enabled = program != null && project != null;
    }

    /// <summary>The layers ticked to bring back.</summary>
    public IReadOnlyList<string> WorldMachineLayers =>
        [.. new[] { ("heights", wmHeights), ("texture", wmTexture), ("splat", wmSplat), ("grass", wmGrass) }.Where(c => c.Item2.Checked).Select(c => c.Item1)];

    /// <summary>Keep where units and buildings go (and metal spots, structures, starts) when bringing back.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool WorldMachineKeep { get => wmKeep.Checked; set => wmKeep.Checked = value; }

    /// <summary>Stretch World Machine's heights over the map's range, rather than map them as sent.</summary>
    public bool WorldMachineStretch => wmRange.SelectedIndex == 1;

    /// <summary>Whether outputs are brought back as World Machine writes them (setting it doesn't raise the event).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool WorldMachineLive
    {
        get => wmLive.Checked;
        set
        {
            filling = true;
            wmLive.Checked = value;
            filling = false;
        }
    }

    /// <summary>What World Machine's last exchange said.</summary>
    public void ShowWorldMachineNote(string text) => wmNote.Text = text;

    internal string WorldMachineNote => wmNote.Text;

    /// <summary>A layer menu wanted under this button: import (true) or export.</summary>
    public event EventHandler<(Control Anchor, bool Import)>? LayerMenuRequested;

    /// <summary>Whether the colours are the map's own (to be read from it before painting, if not read yet).</summary>
    public bool FromThisMap => preset.SelectedIndex == 0;

    /// <summary>The texture settings as the panel shows them (setting them shows them, the preset as it is).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public BeautifySettings Settings
    {
        get
        {
            double A(string key) => (double)materials[key].Amount.Value / 100;
            return new BeautifySettings
            {
                Strength = (double)strength.Value / 100, Detail = (double)detail.Value / 100, Breakup = (double)breakup.Value / 100,
                Rock = colours["rock"], RockAmount = A("rock"), RockFrom = (double)rockFrom.Value, RockTo = Math.Max((double)rockTo.Value, (double)rockFrom.Value + 1),
                Strata = (double)strata.Value / 100,
                Dirt = colours["dirt"], DirtAmount = A("dirt"), DirtFrom = (double)dirtFrom.Value, DirtTo = Math.Max((double)dirtTo.Value, (double)dirtFrom.Value + 1),
                Sand = colours["sand"], SandAmount = A("sand"), SandHeight = (double)sandHeight.Value,
                Mud = colours["mud"], MudAmount = A("mud"), Seabed = colours["seabed"], SeabedAmount = A("seabed"),
                Ground = colours["ground"], GroundAmount = A("ground"),
                Crevices = (double)crevices.Value / 100, SoftenEdges = (double)soften.Value / 100,
            };
        }
        set
        {
            filling = true;
            strength.Value = Clamp(strength, value.Strength * 100);
            detail.Value = Clamp(detail, value.Detail * 100);
            breakup.Value = Clamp(breakup, value.Breakup * 100);
            foreach (var (key, colour, amount) in new[]
                     {
                         ("rock", value.Rock, value.RockAmount), ("dirt", value.Dirt, value.DirtAmount), ("sand", value.Sand, value.SandAmount),
                         ("mud", value.Mud, value.MudAmount), ("seabed", value.Seabed, value.SeabedAmount), ("ground", value.Ground, value.GroundAmount),
                     })
            {
                colours[key] = colour;
                materials[key].Amount.Value = Clamp(materials[key].Amount, amount * 100);
                ShowColour(key);
            }
            rockFrom.Value = Clamp(rockFrom, value.RockFrom);
            rockTo.Value = Clamp(rockTo, value.RockTo);
            strata.Value = Clamp(strata, value.Strata * 100);
            dirtFrom.Value = Clamp(dirtFrom, value.DirtFrom);
            dirtTo.Value = Clamp(dirtTo, value.DirtTo);
            sandHeight.Value = Clamp(sandHeight, value.SandHeight);
            crevices.Value = Clamp(crevices, value.Crevices * 100);
            soften.Value = Clamp(soften, value.SoftenEdges * 100);
            filling = false;
        }
    }

    /// <summary>The cliff settings as the panel shows them.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public CliffDetailSettings CliffSettings
    {
        get => new()
        {
            CliffFrom = (double)cliffFrom.Value, Amount = (double)cliffSize.Value, Strata = (double)ledges.Value / 100, Gullies = (double)gullies.Value / 100,
            Ground = (double)swell.Value,
        };
        set
        {
            cliffFrom.Value = Clamp(cliffFrom, value.CliffFrom);
            cliffSize.Value = Clamp(cliffSize, value.Amount);
            ledges.Value = Clamp(ledges, value.Strata * 100);
            gullies.Value = Clamp(gullies, value.Gullies * 100);
            swell.Value = Clamp(swell, value.Ground);
        }
    }

    /// <summary>Shows the map's own colours (read from it), keeping the amounts and angles.</summary>
    public void SetColours(BeautifySettings colouredFrom)
    {
        var s = Settings;
        Settings = s with
        {
            Rock = colouredFrom.Rock, Dirt = colouredFrom.Dirt, Sand = colouredFrom.Sand, Mud = colouredFrom.Mud, Seabed = colouredFrom.Seabed, Ground = colouredFrom.Ground,
        };
    }

    /// <summary>Chooses a colour set by name, as the box would (for the self-test).</summary>
    internal void SetPreset(string name)
    {
        filling = true;
        preset.SelectedItem = name;
        filling = false;
        if (preset.SelectedIndex > 0)
            Settings = Settings.WithPreset(name);
    }

    /// <summary>What the last run said (shown under the button).</summary>
    public void ShowNote(string text) => note.Text = text;

    /// <summary>Whether the buttons can be used (a map open, nothing running).</summary>
    public void SetEnabled(bool enabled) =>
        both.Enabled = paint.Enabled = roughen.Enabled = exportLayer.Enabled = importLayer.Enabled = wmSend.Enabled = wmBring.Enabled = wmChooseFolder.Enabled = enabled;

    /// <summary>Does what a button would (for the self-test: PerformClick does nothing in a window that isn't shown).</summary>
    internal void Press(string what)
    {
        switch (what)
        {
            case "paint": PaintRequested?.Invoke(this, EventArgs.Empty); break;
            case "roughen": RoughenRequested?.Invoke(this, EventArgs.Empty); break;
            case "both": BothRequested?.Invoke(this, EventArgs.Empty); break;
        }
    }

    void PickColour(string key)
    {
        var c = colours[key];
        using var dialog = new ColorDialog { Color = Color.FromArgb(c.R, c.G, c.B), FullOpen = true };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        colours[key] = new Rgba(dialog.Color.R, dialog.Color.G, dialog.Color.B, 255);
        ShowColour(key);
    }

    void ShowColour(string key)
    {
        var (swatch, _) = materials[key];
        var c = colours[key];
        swatch.BackColor = Color.FromArgb(c.R, c.G, c.B);
        swatch.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        swatch.ForeColor = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 128 ? Color.Black : Color.White;
    }
}
