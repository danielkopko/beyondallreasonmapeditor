using MapCore.Movement;
using MapCore.Rendering;

namespace MapEditor;

/// <summary>
/// The 3D view's own window, beside the main one: view only. It follows every edit, so height strokes made in the
/// 2D view show here as they are painted.
/// </summary>
sealed class TerrainWindow : Form
{
    readonly TerrainViewControl view = new() { Dock = DockStyle.Fill };
    readonly ToolStripButton shadows = new("Shadows") { CheckOnClick = true, Checked = true, ToolTipText = "Terrain shadows from mapinfo's sun" };
    readonly ToolStripButton water = new("Water") { CheckOnClick = true, Checked = true, ToolTipText = "Water colours below sea level" };
    readonly ToolStripButton assets = new("Assets")
    {
        CheckOnClick = true, Checked = true,
        ToolTipText = "Draw the placed trees, rocks, buildings and props as their models (BAR's own features have no model here: turn on the Items markers to see them)",
    };
    readonly NumericUpDown exaggeration = new()
    {
        Minimum = 0.5m, Maximum = 5, Increment = 0.5m, DecimalPlaces = 1, Value = 1, Width = 55, TextAlign = HorizontalAlignment.Right,
    };
    readonly Dictionary<MapOverlays, ToolStripButton> markers = [];
    readonly ToolStripComboBox passable = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        AutoSize = false,
        Width = 105,
        ToolTipText = "Tint the ground by where a kind of unit can go, by the engine's rules",
    };

    /// <summary>The choices of the Passable for box: what each tints for, and its legend.</summary>
    static readonly (string Name, MoveClass? Class, string Legend)[] PassabilityChoices =
    [
        ("Off", null, ""),
        ("Hovercraft", BarMoveClasses.Hover, "Hovercraft:  cyan over water (any slope)  ·  green land up to 33°  ·  red too steep"),
        ("Vehicles", BarMoveClasses.Vehicle, "Vehicles:  green up to 27°  ·  red too steep  ·  purple water deeper than 20"),
        ("Bots", BarMoveClasses.Bot, "Bots:  green up to 54°  ·  red too steep  ·  purple water deeper than 20 (amphibious bots and commanders go on)"),
    ];
    readonly ToolStripStatusLabel help = new("Left drag: rotate  ·  Right or middle drag: move  ·  Wheel: zoom  ·  Arrows: rotate  ·  Home: overview");
    readonly ToolStripStatusLabel legend = new() { Available = false };

    /// <param name="lookAt2D">Gives the 2D view's centre and the width it shows (game units), for "Look at 2D view".</param>
    public TerrainWindow(Func<(double X, double Z, double Width)?> lookAt2D)
    {
        Text = "3D view";
        Font = BarTheme.Font;
        ClientSize = new Size(960, 640);
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;

        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        toolbar.Items.Add(new ToolStripButton("Overview", null, (_, _) => view.ResetCamera()) { ToolTipText = "The whole map from the south (Home)" });
        this.lookAt2D = lookAt2D;
        toolbar.Items.Add(new ToolStripButton("Look at 2D view", null, (_, _) => LookAt2D()) { ToolTipText = "Centre on what the 2D view shows" });
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(new ToolStripLabel("Height ×"));
        toolbar.Items.Add(new ToolStripControlHost(exaggeration) { ToolTipText = "Vertical exaggeration (1 is true to the game)" });
        toolbar.Items.AddRange([shadows, water, assets, new ToolStripSeparator(), new ToolStripLabel("Markers:")]);
        foreach (var (overlay, text) in new[] { (MapOverlays.MetalSpots, "Metal"), (MapOverlays.StartPositions, "Starts"), (MapOverlays.Items, "Items") })
        {
            var button = new ToolStripButton(text) { CheckOnClick = true, Checked = view.Options.Markers.HasFlag(overlay) };
            button.CheckedChanged += (_, _) => UpdateOptions();
            markers[overlay] = button;
            toolbar.Items.Add(button);
        }
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(new ToolStripLabel("Passable for:"));
        toolbar.Items.Add(passable);
        foreach (var (name, _, _) in PassabilityChoices)
            passable.Items.Add(name);
        passable.SelectedIndex = 0;
        passable.SelectedIndexChanged += (_, _) => UpdateOptions();
        var status = new StatusStrip();
        status.Items.AddRange([legend, help]);

        Controls.Add(view);
        Controls.Add(status);
        Controls.Add(toolbar);

        shadows.CheckedChanged += (_, _) => UpdateOptions();
        water.CheckedChanged += (_, _) => UpdateOptions();
        assets.CheckedChanged += (_, _) => UpdateOptions();
        exaggeration.ValueChanged += (_, _) => UpdateOptions();
        BarTheme.Apply(this);
    }

    readonly Func<(double X, double Z, double Width)?> lookAt2D;

    public TerrainViewControl View => view;

    /// <summary>Centres the camera on the 2D view, far enough away to show about the same width.</summary>
    void LookAt2D()
    {
        if (lookAt2D() is (double x, double z, double width))
            view.Camera = view.Camera with
            {
                TargetX = x,
                TargetZ = z,
                Distance = Math.Max(200, width / 2 / Math.Tan(view.Camera.FieldOfView * Math.PI / 360)),
            };
    }

    internal void LookAt2DForTest() => LookAt2D();

    void UpdateOptions()
    {
        var shown = MapOverlays.None;
        foreach (var (overlay, button) in markers)
            if (button.Checked)
                shown |= overlay;
        view.Options = view.Options with
        {
            Exaggeration = (double)exaggeration.Value,
            Shadows = shadows.Checked,
            Water = water.Checked,
            Models = assets.Checked,
            Markers = shown,
            Passability = Choice.Class,
        };
        legend.Available = Choice.Class != null; // Available, not Visible: Visible also reads false while the window is hidden
        legend.Text = Choice.Class != null ? Choice.Legend + "    " : "";
    }

    (string Name, MoveClass? Class, string Legend) Choice => PassabilityChoices[Math.Max(0, passable.SelectedIndex)];

    /// <summary>For the self-test: the Passable for box and the legend shown with it.</summary>
    internal ToolStripComboBox PassableBox => passable;
    internal ToolStripButton AssetsButton => assets;
    internal string Legend => legend.Available ? legend.Text ?? "" : "";
}
