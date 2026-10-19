using System.ComponentModel;
using MapCore.Editing;
using static MapEditor.PanelLayout;

namespace MapEditor;

enum HeightTool { Pan, Raise, Lower, Smooth, Flatten, SetHeight, Noise, Ramp }

/// <summary>
/// The height tools: which brush the left mouse button uses and its settings, a sea-level preview, and whole-map
/// raise / lower / scale. It only holds settings and raises events; the main window applies them to the map.
/// </summary>
sealed class HeightToolPanel : UserControl
{
    static readonly (HeightTool Tool, string Text, Keys Key, string Tip)[] Tools =
    [
        (HeightTool.Pan, "Pan", Keys.P, "Left button pans the view (P)"),
        (HeightTool.Raise, "Raise", Keys.R, "Raise the ground; hold still to keep raising (R)"),
        (HeightTool.Lower, "Lower", Keys.L, "Lower the ground; hold still to keep lowering (L)"),
        (HeightTool.Smooth, "Smooth", Keys.S, "Even out bumps (S)"),
        (HeightTool.Flatten, "Flatten", Keys.F, "Flatten towards the height where the stroke starts (F)"),
        (HeightTool.SetHeight, "Set height", Keys.H, "Pull the ground to the target height; Ctrl+click picks it (H)"),
        (HeightTool.Noise, "Noise", Keys.N, "Add natural-looking bumps (N)"),
        (HeightTool.Ramp, "Ramp", Keys.M, "Build a ramp: shape it on the map, then Apply ramp (M)"),
    ];

    readonly Dictionary<HeightTool, RadioButton> toolButtons = [];
    readonly NumericUpDown radius = Number(8, 3000, 150, 0, 10);
    readonly NumericUpDown strength = Number(1, 100, 50, 0, 5);
    readonly NumericUpDown hardness = Number(0, 100, 50, 0, 5);
    readonly NumericUpDown target = Number(-5000, 5000, 0, 1, 5);
    readonly NumericUpDown bumpSize = Number(16, 2048, 96, 0, 16);
    readonly NumericUpDown seaLevel = Number(-1000, 1000, 0, 1, 5);
    readonly TrackBar seaSlider = new() { Minimum = -300, Maximum = 300, TickFrequency = 50, SmallChange = 5, LargeChange = 25, Dock = DockStyle.Fill, AutoSize = false, Height = 28 };
    readonly NumericUpDown offset = Number(-1000, 1000, 10, 1, 5);
    readonly NumericUpDown scale = Number(0.05m, 20, 1, 2, 0.05m);
    readonly NumericUpDown rampLength = Number(16, 4000, 400, 0, 10);
    readonly NumericUpDown rampWidth = Number(16, 2000, 160, 0, 10);
    readonly NumericUpDown rampDirection = Number(-1, 360, 0, 0, 5);
    readonly NumericUpDown rampStart = Number(-5000, 5000, 0, 1, 5);
    readonly NumericUpDown rampEnd = Number(-5000, 5000, 0, 1, 5);
    readonly NumericUpDown rampEdge = Number(0, 512, 48, 0, 8);
    readonly CheckBox startAuto = new() { Text = "ground", Checked = true, AutoSize = true };
    readonly CheckBox endAuto = new() { Text = "ground", Checked = true, AutoSize = true };
    readonly CheckBox edgeAuto = new() { Text = "auto", Checked = true, AutoSize = true };
    readonly ComboBox rampLook = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    readonly NumericUpDown rampRough = Number(0, 100, 50, 0, 10);
    static readonly (string Name, RampLook Look)[] Looks =
        [("Rock to dirt", RampLook.RockToDirt), ("From the ends", RampLook.FromEnds), ("Leave texture", RampLook.None)];
    readonly Label rampInfo = Hint("");
    readonly Label rampClimb = new() { AutoSize = true, MaximumSize = new Size(230, 0), Margin = new Padding(3, 4, 3, 2) };
    readonly List<Control> rampControls = [];
    readonly Button straighten = new() { Text = "Straighten", AutoSize = true };
    bool rampBends;
    readonly GroupBox brushGroup, rampGroup;
    HeightTool tool = HeightTool.Pan;
    bool syncing;

    public HeightToolPanel()
    {
        AutoScroll = true;
        Padding = new Padding(6);
    
        var layout = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Top, AutoSize = true };
        Controls.Add(layout);

        var toolGrid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        toolGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        toolGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var tips = new ToolTip();
        foreach (var (t, text, _, tip) in Tools)
        {
            var button = new RadioButton
            {
                Text = text,
                Appearance = Appearance.Button,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                Height = 28,
                Checked = t == tool,
            };
            tips.SetToolTip(button, tip);
            button.CheckedChanged += (_, _) =>
            {
                if (button.Checked && !syncing)
                    Tool = t;
            };
            toolButtons[t] = button;
            toolGrid.Controls.Add(button);
        }
        layout.Controls.Add(Group("Height tool", toolGrid));

        var brush = Grid();
        AddRow(brush, "Radius", radius, "units");
        AddRow(brush, "Strength", strength, "%");
        AddRow(brush, "Hardness", hardness, "%");
        AddRow(brush, "Target height", target, "");
        AddRow(brush, "Bump size", bumpSize, "units");
        brush.Controls.Add(Hint("Hardness: the share of the radius at full strength. Set height: Ctrl+click picks the target. [ and ] change the radius; right or middle button drags the view."), 0, brush.RowCount++);
        brush.SetColumnSpan(brush.Controls[^1], 3);
        brushGroup = Group("Brush", brush);
        layout.Controls.Add(brushGroup);

        var ramp = Grid();
        AddRow(ramp, "Length", rampLength, "units");
        AddRow(ramp, "Width", rampWidth, "units");
        AddRow(ramp, "Direction", rampDirection, "° (0 north)");
        AddRow(ramp, "Start height", rampStart, startAuto);
        AddRow(ramp, "End height", rampEnd, endAuto);
        AddRow(ramp, "Edge", rampEdge, edgeAuto);
        foreach (var (name, _) in Looks)
            rampLook.Items.Add(name);
        rampLook.SelectedIndex = 0;
        AddRow(ramp, "Look", rampLook, "");
        AddRow(ramp, "Rough sides", rampRough, "%");
        AddWide(ramp, rampInfo);
        AddWide(ramp, rampClimb);
        var swapEnds = new Button { Text = "Swap ends", AutoSize = true };
        var applyRamp = new Button { Text = "Apply ramp", AutoSize = true };
        var autoSources = new Button { Text = "Auto rock, dirt", AutoSize = true };
        var rampButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        rampButtons.Controls.AddRange([applyRamp, swapEnds, straighten, autoSources]);
        AddWide(ramp, rampButtons);
        AddWide(ramp, Hint("Put each end on the ground the ramp should join: its height and texture come from there. " +
            "Click the map to place the ramp and drag it to move it. Drag a round end handle to stretch and turn it, " +
            "the side handle to widen it; Q / E turn it, [ and ] change its width. A height marked \"ground\" follows the ground at that end; " +
            "the edge is where the ramp blends into the ground. Rock to dirt copies rock and dirt from the map (the brown and sandy rings): " +
            "drag them to take them from elsewhere. Enter or Apply ramp builds it (one undo step)."));
        AddWide(ramp, Hint("To bend it, as round the edge of a cliff: Shift+click adds a point beyond the nearer end, Ctrl+click adds one " +
            "on the line between its points (drag it out), and Ctrl+click on a bend takes it away. The ramp runs from point to point, " +
            "rounding each bend as much as the runs beside it allow, and climbs evenly along its length; closely spaced points make it " +
            "follow the line they're drawn along. Drag any point to move it. Straighten goes back to a straight ramp between its ends."));
        rampGroup = Group("Ramp", ramp);
        layout.Controls.Add(rampGroup);
        rampControls.AddRange([rampLength, rampWidth, rampDirection, rampStart, rampEnd, rampEdge, startAuto, endAuto, edgeAuto, rampLook, rampRough, swapEnds, applyRamp, autoSources]);
        tips.SetToolTip(straighten, "Make the bending ramp straight again, between its ends");
        straighten.Click += (_, _) => StraightenRamp?.Invoke(this, EventArgs.Empty);
        tips.SetToolTip(rampLook, "Rock to dirt: rock on the upper ramp and its steep sides, sand or dirt lower down, blending into the ground at its foot. " +
                                  "From the ends: the ground beyond each end copied along it. Leave texture: heights only");
        tips.SetToolTip(rampRough, "Breaks up the sides like broken rock; the ramp itself and its ends stay smooth");
        tips.SetToolTip(autoSources, "Take rock and dirt from where the editor finds them near the ramp again");
        autoSources.Click += (_, _) => AutoSources?.Invoke(this, EventArgs.Empty);
        rampLook.SelectedIndexChanged += (_, _) => OnRampChanged();
        rampRough.ValueChanged += (_, _) => OnRampChanged();
        tips.SetToolTip(edgeAuto, "As wide as the largest height step beside the ramp, so the blend into the ground averages about 45°");
        tips.SetToolTip(swapEnds, "Turn the ramp round: the start becomes the end");

        var sea = Grid();
        AddRow(sea, "Sea level", seaLevel, "preview");
        sea.Controls.Add(seaSlider, 0, sea.RowCount++);
        sea.SetColumnSpan(seaSlider, 3);
        var applySea = new Button { Text = "Apply", AutoSize = true };
        var resetSea = new Button { Text = "Reset", AutoSize = true };
        var seaButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        seaButtons.Controls.AddRange([applySea, resetSea]);
        sea.Controls.Add(seaButtons, 0, sea.RowCount++);
        sea.SetColumnSpan(seaButtons, 3);
        sea.Controls.Add(Hint("The game's sea level is always 0. This shows where water would be after shifting every height; nothing changes until you apply it (one height shift)."), 0, sea.RowCount++);
        sea.SetColumnSpan(sea.Controls[^1], 3);
        layout.Controls.Add(Group("Sea level", sea));

        var whole = Grid();
        var applyOffset = new Button { Text = "Apply", AutoSize = true };
        var applyScale = new Button { Text = "Apply", AutoSize = true };
        AddRow(whole, "Raise all by", offset, applyOffset);
        AddRow(whole, "Scale all by", scale, applyScale);
        whole.Controls.Add(Hint("Negative values lower. Scaling keeps sea level (height 0) where it is."), 0, whole.RowCount++);
        whole.SetColumnSpan(whole.Controls[^1], 3);
        layout.Controls.Add(Group("Whole map", whole));

        foreach (var box in new[] { radius, hardness })
            box.ValueChanged += (_, _) => BrushChanged?.Invoke(this, EventArgs.Empty);
        seaLevel.ValueChanged += (_, _) =>
        {
            if (!syncing)
            {
                syncing = true;
                seaSlider.Value = (int)Math.Clamp(seaLevel.Value, seaSlider.Minimum, seaSlider.Maximum);
                syncing = false;
            }
            SeaLevelChanged?.Invoke(this, EventArgs.Empty);
        };
        seaSlider.ValueChanged += (_, _) =>
        {
            if (!syncing)
                seaLevel.Value = seaSlider.Value;
        };
        applySea.Click += (_, _) => ApplySeaLevel?.Invoke(this, EventArgs.Empty);
        resetSea.Click += (_, _) => SeaLevel = 0;
        applyOffset.Click += (_, _) => OffsetAll?.Invoke(this, (double)offset.Value);
        applyScale.Click += (_, _) => ScaleAll?.Invoke(this, (double)scale.Value);
        foreach (var box in new[] { rampLength, rampWidth, rampDirection, rampStart, rampEnd, rampEdge })
            box.ValueChanged += (_, _) =>
            {
                if (box == rampDirection && rampDirection.Value is < 0 or >= 360)
                {
                    RampDirection = (double)rampDirection.Value; // wraps round, raising this again
                    return;
                }
                OnRampChanged();
            };
        foreach (var check in new[] { startAuto, endAuto, edgeAuto })
            check.CheckedChanged += (_, _) =>
            {
                UpdateEnabled();
                OnRampChanged();
            };
        applyRamp.Click += (_, _) => ApplyRamp?.Invoke(this, EventArgs.Empty);
        swapEnds.Click += (_, _) => SwapRampEnds();
        UpdateEnabled();
    }

    public event EventHandler? ToolChanged;

    /// <summary>The radius or hardness changed (the brush outline follows).</summary>
    public event EventHandler? BrushChanged;

    public event EventHandler? SeaLevelChanged;
    public event EventHandler? ApplySeaLevel;
    public event EventHandler<double>? OffsetAll;
    public event EventHandler<double>? ScaleAll;

    /// <summary>A ramp setting changed (the outline on the map follows).</summary>
    public event EventHandler? RampChanged;

    /// <summary>Apply ramp (or Enter on the map) was pressed.</summary>
    public event EventHandler? ApplyRamp;

    /// <summary>Auto rock, dirt was pressed: take them from where the editor finds them again.</summary>
    public event EventHandler? AutoSources;

    /// <summary>Q or E with a bending ramp: turn its points by this many degrees (clockwise on the map).</summary>
    public event EventHandler<double>? RampTurned;

    /// <summary>Swap ends was pressed (after the heights have swapped): a bending ramp's points run the other way.</summary>
    public event EventHandler? RampEndsSwapped;

    /// <summary>Straighten was pressed: the bending ramp goes straight between its ends.</summary>
    public event EventHandler? StraightenRamp;

    /// <summary>
    /// Whether the ramp bends through points: its length and direction then follow them (the boxes are off), Q / E turn
    /// its points, and Straighten is on.
    /// </summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool RampBends
    {
        get => rampBends;
        set
        {
            if (rampBends == value)
                return;
            rampBends = value;
            UpdateEnabled();
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public HeightTool Tool
    {
        get => tool;
        set
        {
            tool = value;
            syncing = true;
            foreach (var (t, button) in toolButtons)
                button.Checked = t == value;
            syncing = false;
            UpdateEnabled();
            ToolChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Radius
    {
        get => (double)radius.Value;
        set => radius.Value = Clamp(radius, value);
    }

    /// <summary>0-1.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Strength
    {
        get => (double)strength.Value / 100;
        set => strength.Value = Clamp(strength, value * 100);
    }

    /// <summary>0-1.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Hardness
    {
        get => (double)hardness.Value / 100;
        set => hardness.Value = Clamp(hardness, value * 100);
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double TargetHeight
    {
        get => (double)target.Value;
        set => target.Value = Clamp(target, value);
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double BumpSize
    {
        get => (double)bumpSize.Value;
        set => bumpSize.Value = Clamp(bumpSize, value);
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double SeaLevel
    {
        get => (double)seaLevel.Value;
        set => seaLevel.Value = Clamp(seaLevel, value);
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double RampLength
    {
        get => (double)rampLength.Value;
        set => rampLength.Value = Clamp(rampLength, value);
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double RampWidth
    {
        get => (double)rampWidth.Value;
        set => rampWidth.Value = Clamp(rampWidth, value);
    }

    /// <summary>Where the ramp runs from its start to its end, in degrees: 0 north (up the map), 90 east.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double RampDirection
    {
        get => (double)rampDirection.Value;
        set => rampDirection.Value = Clamp(rampDirection, (Math.Round(value) % 360 + 360) % 360);
    }

    /// <summary>The start's height, or null to follow the ground there.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double? RampStartHeight
    {
        get => startAuto.Checked ? null : (double)rampStart.Value;
        set => SetSetting(rampStart, startAuto, value);
    }

    /// <summary>The end's height, or null to follow the ground there.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double? RampEndHeight
    {
        get => endAuto.Checked ? null : (double)rampEnd.Value;
        set => SetSetting(rampEnd, endAuto, value);
    }

    /// <summary>The edge width, or null for automatic.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double? RampEdge
    {
        get => edgeAuto.Checked ? null : (double)rampEdge.Value;
        set => SetSetting(rampEdge, edgeAuto, value);
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RampLook RampLook
    {
        get => Looks[Math.Max(0, rampLook.SelectedIndex)].Look;
        set => rampLook.SelectedIndex = Array.FindIndex(Looks, l => l.Look == value);
    }

    /// <summary>How rough the ramp's sides are, 0-1.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double RampRoughness
    {
        get => (double)rampRough.Value / 100;
        set => rampRough.Value = Clamp(rampRough, value * 100);
    }

    /// <summary>The slope and heights, shown under the ramp settings.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string RampInfo
    {
        get => rampInfo.Text;
        set => rampInfo.Text = value;
    }

    /// <summary>Which units can go up the ramp (BAR's movement classes), shown under the slope.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string RampClimb
    {
        get => rampClimb.Text;
        set => rampClimb.Text = value;
    }

    /// <summary>Shows the values used where a setting follows the ground or is automatic, without raising RampChanged.</summary>
    public void ShowRampAuto(double start, double end, double edge)
    {
        syncing = true;
        if (startAuto.Checked)
            rampStart.Value = Clamp(rampStart, start);
        if (endAuto.Checked)
            rampEnd.Value = Clamp(rampEnd, end);
        if (edgeAuto.Checked)
            rampEdge.Value = Clamp(rampEdge, edge);
        syncing = false;
    }

    void SetSetting(NumericUpDown box, CheckBox auto, double? value)
    {
        if (value is double v)
            box.Value = Clamp(box, v);
        auto.Checked = value == null;
    }

    /// <summary>Turns the ramp round: the start becomes the end, with its height setting.</summary>
    public void SwapRampEnds()
    {
        syncing = true;
        (decimal start, decimal end) = (rampStart.Value, rampEnd.Value);
        (bool startGround, bool endGround) = (startAuto.Checked, endAuto.Checked);
        rampStart.Value = end;
        rampEnd.Value = start;
        startAuto.Checked = endGround;
        endAuto.Checked = startGround;
        RampDirection += 180;
        syncing = false;
        UpdateEnabled();
        RampEndsSwapped?.Invoke(this, EventArgs.Empty);
        OnRampChanged();
    }

    void OnRampChanged()
    {
        if (!syncing)
            RampChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Tool keys (P, R, L, S, F, H, N, M) and [ ] for the radius; with the Ramp tool, [ ] change its width and Q / E
    /// turn it. True if the key was used.
    /// </summary>
    public bool HandleKey(Keys key)
    {
        if (tool == HeightTool.Ramp)
            switch (key)
            {
                case Keys.OemOpenBrackets:
                    RampWidth /= 1.25;
                    return true;
                case Keys.OemCloseBrackets:
                    RampWidth *= 1.25;
                    return true;
                case Keys.Q when rampBends:
                    RampTurned?.Invoke(this, -15);
                    return true;
                case Keys.E when rampBends:
                    RampTurned?.Invoke(this, 15);
                    return true;
                case Keys.Q:
                    RampDirection -= 15;
                    return true;
                case Keys.E:
                    RampDirection += 15;
                    return true;
            }
        switch (key)
        {
            case Keys.OemOpenBrackets:
                Radius /= 1.25;
                return true;
            case Keys.OemCloseBrackets:
                Radius *= 1.25;
                return true;
        }
        foreach (var (t, _, k, _) in Tools)
            if (k == key)
            {
                Tool = t;
                return true;
            }
        return false;
    }

    void UpdateEnabled()
    {
        // The Ramp tool's settings take the brush's place, so Apply ramp is in view without scrolling.
        brushGroup.Visible = tool != HeightTool.Ramp;
        rampGroup.Visible = tool == HeightTool.Ramp;
        target.Enabled = tool == HeightTool.SetHeight;
        bumpSize.Enabled = tool == HeightTool.Noise;
        radius.Enabled = strength.Enabled = hardness.Enabled = tool is not (HeightTool.Pan or HeightTool.Ramp);
        foreach (var control in rampControls)
            control.Enabled = tool == HeightTool.Ramp;
        rampStart.Enabled = tool == HeightTool.Ramp && !startAuto.Checked;
        rampEnd.Enabled = tool == HeightTool.Ramp && !endAuto.Checked;
        rampEdge.Enabled = tool == HeightTool.Ramp && !edgeAuto.Checked;
        rampLength.Enabled = rampDirection.Enabled = tool == HeightTool.Ramp && !rampBends;
        straighten.Enabled = tool == HeightTool.Ramp && rampBends;
    }
}
