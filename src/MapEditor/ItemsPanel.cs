using System.ComponentModel;
using System.Globalization;
using MapCore.Assets;
using MapCore.Editing;
using MapCore.Items;
using MapCore.Structures;
using static MapEditor.PanelLayout;

namespace MapEditor;

enum ItemsMode { Select, Place, Scatter, Grass, Pan }

/// <summary>
/// The item tools: select (click, Ctrl+click, drag a box), move and turn placed features, place one, scatter many,
/// paint grass, and drag start positions. It holds the settings and raises events; the main window applies them.
/// </summary>
sealed class ItemsPanel : UserControl
{
    static readonly (ItemsMode Mode, string Text, Keys Key, string Tip)[] Modes =
    [
        (ItemsMode.Select, "Select / move", Keys.S, "Click an item or start position; Ctrl+click adds to the selection; drag on empty ground to select a box; drag a selected item to move them all (S)"),
        (ItemsMode.Place, "Place", Keys.A, "Click to place the feature named below (A)"),
        (ItemsMode.Scatter, "Scatter", Keys.C, "Paint to scatter the feature, at least the spacing apart (C)"),
        (ItemsMode.Grass, "Grass", Keys.G, "Paint grass, or erase it (G)"),
        (ItemsMode.Pan, "Pan", Keys.P, "Left button pans the view (P)"),
    ];

    readonly Dictionary<ItemsMode, RadioButton> modeButtons = [];
    readonly ComboBox featureName = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 170 };
    readonly Label nameWarning = Hint("");
    readonly RadioButton toPlacer = new() { Text = "featureplacer (set.lua)", AutoSize = true, Checked = true };
    readonly RadioButton toSmf = new() { Text = "SMF feature list", AutoSize = true };
    readonly RadioButton toUnits = new() { Text = "a unit (featureplacer unitlist)", AutoSize = true };
    readonly RadioButton toBuildings = new() { Text = "a building (featureplacer buildinglist)", AutoSize = true };
    readonly NumericUpDown heading = Number(0, 359, 0, 0, 15);
    readonly CheckBox randomHeading = new() { Text = "random", AutoSize = true, Checked = true };
    readonly NumericUpDown radius = Number(16, 2000, 150, 0, 10);
    readonly NumericUpDown spacing = Number(8, 1000, 60, 0, 5);
    readonly NumericUpDown grassValue = Number(1, 254, 1, 0, 1);
    readonly CheckBox eraseGrass = new() { Text = "Erase grass", AutoSize = true };
    readonly Label selection = BarTheme.Strong(new Label { AutoSize = true, MaximumSize = new Size(270, 0) });
    readonly Label selectionDetail = Hint("");
    readonly NumericUpDown selectedHeading = Number(0, 359, 0, 0, 15);
    readonly Button turnLeft = new() { Text = "↺ 15°", AutoSize = true };
    readonly Button turnRight = new() { Text = "↻ 15°", AutoSize = true };
    readonly Button remove = new() { Text = "Remove", AutoSize = true };
    readonly ModelPreviewBox preview = new() { Dock = DockStyle.Fill };
    readonly Label startNote = Hint("Start positions (white-ringed circles) are dragged in Select mode.");

    // A door or tunnel picked in Select mode: its kind's size and look (for every one of that kind), and whether this one starts destroyed.
    readonly Label structureInfo = new() { AutoSize = true, MaximumSize = new Size(270, 0) };
    readonly ComboBox structureLook = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, DropDownWidth = 200 };
    readonly ComboBox structureRoof = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, DropDownWidth = 220 };
    readonly NumericUpDown structureHeight = Number(16, 512, 80, 0, 8);
    readonly NumericUpDown structureGap = Number(1, 32, 4, 0, 1), structureWall = Number(1, 32, 3, 0, 1), structureDepth = Number(1, 32, 3, 0, 1);
    readonly Button applyStructure = new() { Text = "Apply", AutoSize = true };
    readonly CheckBox structureBroken = new() { Text = "This one starts destroyed", AutoSize = true };
    readonly Label structureMarkers = Hint("");
    readonly Button placeMarkers = new() { Text = "Place repair markers", AutoSize = true };
    readonly GroupBox structureGroup;

    // A guardian tower picked in Select mode: its kind's look, times and targets (for every one of that kind), and this one's zone.
    readonly Label guardianInfo = new() { AutoSize = true, MaximumSize = new Size(270, 0) };
    readonly ComboBox guardianLook = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, DropDownWidth = 200 };
    readonly NumericUpDown killSeconds = Number(0.2m, 60, 1, 1, 0.2m), rocketSeconds = Number(0.5m, 60, 2, 1, 0.5m);
    readonly Button applyGuardian = new() { Text = "Apply", AutoSize = true };
    readonly GuardianRegionEditor[] regionEditors = [new(GuardianRegion.Band), new(GuardianRegion.Inside), new(GuardianRegion.Outside)];
    readonly Label zoneInfo = new() { AutoSize = true, MaximumSize = new Size(270, 0) };
    readonly NumericUpDown zoneDistance = Number((decimal)GuardianZone.MinDistance, (decimal)GuardianZone.MaxDistance, 200, 0, 16);
    readonly CheckBox zoneClosed = new() { Text = "Closed loop (back to the first point)", AutoSize = true };
    readonly Button clearZone = new() { Text = "Clear the path", AutoSize = true };
    readonly GroupBox guardianGroup;
    GuardianZone? shownZone;

    StructureKind? shownKind;
    StructureKindSettings? shownSettings;
    bool rebuild;
    readonly HashSet<string> known = new(StringComparer.OrdinalIgnoreCase);
    ItemsMode mode = ItemsMode.Select;
    bool syncing;

    public ItemsPanel()
    {
        AutoScroll = true;
        Padding = new Padding(6);
        var layout = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Top, AutoSize = true };
        Controls.Add(layout);

        var tips = new ToolTip();
        var modeGrid = ButtonGrid();
        foreach (var (m, text, _, tip) in Modes)
        {
            var button = ToolButton(text, m == mode);
            tips.SetToolTip(button, tip);
            button.CheckedChanged += (_, _) =>
            {
                if (button.Checked && !syncing)
                    Mode = m;
            };
            modeButtons[m] = button;
            modeGrid.Controls.Add(button);
        }
        layout.Controls.Add(Group("Items tool", modeGrid));

        var place = Grid();
        AddWide(place, new Label { Text = "Feature", AutoSize = true });
        AddWide(place, featureName);
        AddWide(place, nameWarning);
        AddWide(place, new Label { Text = "Add to", AutoSize = true });
        AddWide(place, toPlacer);
        AddWide(place, toSmf);
        AddWide(place, toUnits);
        AddWide(place, toBuildings);
        AddRow(place, "Heading", heading, randomHeading);
        AddRow(place, "Radius", radius, "units");
        AddRow(place, "Spacing", spacing, "units");
        AddWide(place, Hint("Heading: 0° faces south, 90° east. Radius is the scatter and grass brush; spacing keeps scattered items apart. New items go to the featureplacer when the map has one: the map's own gadget places them when a game starts."));
        layout.Controls.Add(Group("Place and scatter", place));

        var grass = Grid();
        AddRow(grass, "Value", grassValue, "");
        AddWide(grass, Hint("1 is grass; 2-254 set the blade size in BAR."));
        AddWide(grass, eraseGrass);
        layout.Controls.Add(Group("Grass", grass));

        var selected = Grid();
        AddWide(selected, selection);
        AddWide(selected, selectionDetail);
        AddWide(selected, preview);
        AddRow(selected, "Heading", selectedHeading, "degrees");
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        buttons.Controls.AddRange([turnLeft, turnRight, remove]);
        AddWide(selected, buttons);
        AddWide(selected, Hint("Q / E turn the selection, Delete removes it. Buildings turn by quarters."));
        AddWide(selected, startNote);
        layout.Controls.Add(Group("Selection", selected));

        var structure = Grid();
        AddWide(structure, structureInfo);
        structureLook.Items.AddRange(["Concrete (built)", "Metal (built)", "Models from assets"]);
        structureRoof.Items.AddRange(["Flat (build on it)", "Rock on a concrete vault"]);
        AddRow(structure, "Look", structureLook, "");
        AddRow(structure, "Roof", structureRoof, "");
        AddRow(structure, "Height", structureHeight, "game units");
        AddRow(structure, "Gap", structureGap, "squares");
        AddRow(structure, "Wall width", structureWall, "squares");
        AddRow(structure, "Depth", structureDepth, "squares");
        AddWide(structure, applyStructure);
        AddWide(structure, structureBroken);
        AddWide(structure, structureMarkers);
        AddWide(structure, placeMarkers);
        AddWide(structure, Hint("Drag it to move it (it stays on a 16-unit grid). Q / E, the turn buttons or Heading turn it a quarter at a time: " +
            "the engine turns a feature's blocked footprint only by quarters, so doors and tunnels align with it."));
        AddWide(structure, Hint("Size and look belong to the kind: Apply changes every structure of it (one undo step) and builds its models again. " +
            "Squares are 16 game units; a commander is 52 tall and 28 across. Models from assets keep their models: only the footprints change. " +
            "Starting destroyed is this one's own: the game begins with its gap open and its repair markers out."));
        structureGroup = Group("Door or tunnel", structure);
        structureGroup.Visible = false;
        layout.Controls.Add(structureGroup);

        var guardian = Grid();
        AddWide(guardian, guardianInfo);
        guardianLook.Items.AddRange(["Ancient (bronze and stone)", "Concrete (built)", "Metal (built)"]);
        AddRow(guardian, "Look", guardianLook, "");
        AddRow(guardian, "Kill time", killSeconds, "s");
        AddRow(guardian, "Rocket every", rocketSeconds, "s");
        AddWide(guardian, applyGuardian);
        AddWide(guardian, zoneInfo);
        AddRow(guardian, "Distance", zoneDistance, "units");
        AddWide(guardian, zoneClosed);
        AddWide(guardian, clearZone);
        AddWide(guardian, Hint("The path: Shift+click adds a point at its end (two points make it), drag a point to move it, Ctrl+click a point " +
            "to remove it or a run to put a point in. It needn't touch the tower."));
        foreach (var editor in regionEditors)
        {
            AddWide(guardian, editor);
            editor.RuleChanged += (_, rule) =>
            {
                if (!syncing && shownKind?.Type == StructureType.Guardian)
                    GuardianRuleChanged?.Invoke(this, (editor.Which, rule));
            };
        }
        AddWide(guardian, Hint("In each region, units of the types it destroys, of the teams ticked (BAR's lobby teams; All takes in Scavengers " +
            "and Raptors too), are doomed once they have been there the delay without leaving: gone the kill time later wherever they go, " +
            "healed or not, however many come. A unit carried by a transport is judged by the transport. Look and times belong to the kind; " +
            "the path and what it does in each region are this tower's own (a new tower copies the last one placed)."));
        guardianGroup = Group("Guardian tower", guardian);
        guardianGroup.Visible = false;
        layout.Controls.Add(guardianGroup);

        featureName.TextChanged += (_, _) => CheckName();
        toUnits.CheckedChanged += (_, _) => CheckName();
        toBuildings.CheckedChanged += (_, _) => CheckName();
        radius.ValueChanged += (_, _) => BrushChanged?.Invoke(this, EventArgs.Empty);
        selectedHeading.ValueChanged += (_, _) =>
        {
            if (!syncing)
                HeadingChanged?.Invoke(this, (double)selectedHeading.Value);
        };
        turnLeft.Click += (_, _) => TurnRequested?.Invoke(this, structureShown ? -90 : -15);
        turnRight.Click += (_, _) => TurnRequested?.Invoke(this, structureShown ? 90 : 15);
        remove.Click += (_, _) => RemoveRequested?.Invoke(this, EventArgs.Empty);
        structureLook.SelectedIndexChanged += (_, _) => UpdateStructureControls();
        foreach (var control in new Control[] { structureLook, structureRoof, structureHeight, structureGap, structureWall, structureDepth })
            control.TextChanged += (_, _) => UpdateStructureControls();
        applyStructure.Click += (_, _) => Press("apply-structure");
        placeMarkers.Click += (_, _) => Press("place-markers");
        structureBroken.CheckedChanged += (_, _) =>
        {
            if (!syncing && shownKind != null)
                StructureBrokenChanged?.Invoke(this, structureBroken.Checked);
        };
        foreach (var control in new Control[] { guardianLook, killSeconds, rocketSeconds })
            control.TextChanged += (_, _) => UpdateStructureControls();
        guardianLook.SelectedIndexChanged += (_, _) => UpdateStructureControls();
        applyGuardian.Click += (_, _) => Press("apply-guardian");
        zoneDistance.ValueChanged += (_, _) => ZoneSettingsChanged();
        zoneClosed.CheckedChanged += (_, _) => ZoneSettingsChanged();
        clearZone.Click += (_, _) => Press("clear-zone");
        ShowSelection([], null);
    }

    /// <summary>BAR's standard units, for a guardian's targets (null when the editor has no catalog beside it).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal UnitCatalog? Catalog
    {
        get => catalog;
        set
        {
            catalog = value;
            foreach (var editor in regionEditors)
                editor.Catalog = value;
        }
    }

    UnitCatalog? catalog;

    /// <summary>What the shown guardian tower does in one region changed (one undo step).</summary>
    public event EventHandler<(GuardianRegion Region, GuardianRule Rule)>? GuardianRuleChanged;

    /// <summary>The shown guardian's editor for a region (for the self-test).</summary>
    internal GuardianRegionEditor RegionEditor(GuardianRegion region) => regionEditors[(int)region];

    /// <summary>The shown guardian's zone distance or closed loop was changed (for its path, or the path still to be drawn).</summary>
    public event EventHandler<(double Distance, bool Closed)>? GuardianZoneSettingsChanged;

    /// <summary>Clear the path: the shown guardian guards nothing.</summary>
    public event EventHandler? GuardianZoneCleared;

    /// <summary>The distance and loop a guardian's path is drawn with.</summary>
    internal double GuardianDistance => (double)zoneDistance.Value;
    internal bool GuardianClosed => zoneClosed.Checked;
    // Which group shows, kept apart from Visible: in a window not yet shown, every control reads as hidden.
    bool guardianShown, doorGroupShown;
    internal bool GuardianShown => guardianShown;
    internal bool DoorGroupShown => doorGroupShown;
    internal string GuardianZoneText => zoneInfo.Text;

    void ZoneSettingsChanged()
    {
        if (!syncing && shownKind?.Type == StructureType.Guardian)
            GuardianZoneSettingsChanged?.Invoke(this, ((double)zoneDistance.Value, zoneClosed.Checked));
    }

    /// <summary>For the self-test: sets the distance and loop as a user would (each raising its change).</summary>
    internal void SetGuardianZoneSettings(double distance, bool closed)
    {
        zoneDistance.Value = Clamp(zoneDistance, distance);
        zoneClosed.Checked = closed;
    }

    internal void SetGuardianSettings(StructureLook look, double kill, double rocket)
    {
        guardianLook.SelectedIndex = look switch { StructureLook.Concrete => 1, StructureLook.Metal => 2, _ => 0 };
        killSeconds.Value = Clamp(killSeconds, kill);
        rocketSeconds.Value = Clamp(rocketSeconds, rocket);
    }


    /// <summary>Apply: the shown structure's kind with the size and look as set.</summary>
    public event EventHandler<(string Kind, StructureKindSettings Settings)>? StructureChangeRequested;

    /// <summary>The shown structure's "starts destroyed" box was changed.</summary>
    public event EventHandler<bool>? StructureBrokenChanged;

    /// <summary>Place repair markers: each click in Place now puts one down for the shown structure.</summary>
    public event EventHandler? PlaceMarkersRequested;

    /// <summary>
    /// Shows a picked door or tunnel: what it is and where, its kind's size and look (with how many share them), and
    /// whether it starts destroyed. Null hides the group.
    /// </summary>
    /// <param name="partsMissing">A built kind made by an earlier editor, without some of the parts it now has (its green
    /// markers, what it leaves when destroyed): Apply then rebuilds it as it is.</param>
    public void ShowStructure(StructureInstance? placed, StructureKind? kind, StructureKindSettings? settings, int ofKind, (double X, double Z)? marker = null,
        bool partsMissing = false)
    {
        rebuild = partsMissing;
        syncing = true;
        shownKind = placed != null ? kind : null;
        shownSettings = settings;
        structureShown = shownKind != null;
        bool guardian = shownKind?.Type == StructureType.Guardian;
        structureGroup.Visible = doorGroupShown = structureShown && !guardian;
        guardianGroup.Visible = guardianShown = guardian;
        if (guardian && placed != null && kind != null && settings != null)
        {
            selection.Text = kind.Name;
            selectionDetail.Text = string.Create(CultureInfo.CurrentCulture, $"a guardian tower at ({placed.X:0}, {placed.Z:0})");
            selectedHeading.Value = Clamp(selectedHeading, placed.Facing * 90);
            selectedHeading.Enabled = turnLeft.Enabled = turnRight.Enabled = remove.Enabled = true;
            (turnLeft.Text, turnRight.Text) = ("↺ 90°", "↻ 90°");
            guardianInfo.Text = $"{kind.Name}: a guardian tower kind, {(ofKind == 1 ? "placed once" : $"placed {ofKind} times")}.";
            guardianLook.SelectedIndex = settings.Look switch { StructureLook.Concrete => 1, StructureLook.Metal => 2, _ => 0 };
            killSeconds.Value = Clamp(killSeconds, settings.KillSeconds);
            rocketSeconds.Value = Clamp(rocketSeconds, settings.RocketSeconds);
            var rules = MapEditSession.RulesOf(placed, kind);
            foreach (var editor in regionEditors)
            {
                editor.TowerName = kind.Name;
                editor.Show(rules[editor.Which], editor.Which == GuardianRegion.Inside && placed.Zone is not { Closed: true }
                    ? "It has no inside until its path is closed (three points or more, Closed loop ticked)." : null);
            }
            shownZone = placed.Zone;
            if (placed.Zone is GuardianZone zone)
            {
                zoneDistance.Value = Clamp(zoneDistance, zone.Distance);
                zoneClosed.Checked = zone.Closed;
                zoneInfo.Text = string.Create(CultureInfo.CurrentCulture,
                    $"It guards a {(zone.Closed ? "closed " : "")}path of {zone.Points.Count} points, {zone.Length:N0} long, {zone.Distance:0} either side.");
            }
            else
                zoneInfo.Text = "It guards nothing yet: Shift+click on the map to start its path.";
            zoneClosed.Enabled = placed.Zone is not { Points.Count: < 3 };
            clearZone.Enabled = placed.Zone != null;
            applyGuardian.Text = ofKind == 1 ? $"Apply to {kind.Name}" : $"Apply to every {kind.Name} ({ofKind})";
        }
        else if (placed != null && kind != null && settings != null)
        {
            string type = StructureKind.TypeName(kind.Type).Replace('_', ' ');
            selection.Text = kind.Name;
            selectionDetail.Text = string.Create(CultureInfo.CurrentCulture,
                $"a {type} at ({placed.X:0}, {placed.Z:0}), its passage running {(placed.Facing % 2 == 0 ? "north-south" : "east-west")}");
            // Doors and tunnels turn by quarters only: the engine turns a feature's blocked footprint only by quarters.
            selectedHeading.Value = Clamp(selectedHeading, placed.Facing * 90);
            selectedHeading.Enabled = turnLeft.Enabled = turnRight.Enabled = remove.Enabled = true;
            (turnLeft.Text, turnRight.Text) = ("↺ 90°", "↻ 90°");
            structureInfo.Text = $"{kind.Name}: a {type} kind, {(ofKind == 1 ? "placed once" : $"placed {ofKind} times")}.";
            structureLook.SelectedIndex = settings.Look switch { StructureLook.Metal => 1, StructureLook.Assets => 2, _ => 0 };
            structureRoof.SelectedIndex = settings.Roof == StructureRoof.Cliff ? 1 : 0;
            structureHeight.Value = Clamp(structureHeight, settings.Look == StructureLook.Assets ? 80 : settings.Height);
            structureGap.Value = Clamp(structureGap, settings.Gap);
            structureWall.Value = Clamp(structureWall, settings.WallWidth);
            structureDepth.Value = Clamp(structureDepth, settings.Depth);
            structureBroken.Checked = placed.Broken;
            structureBroken.Enabled = kind.Repair != null && kind.Type != StructureType.Pump;
            int markers = placed.MarkerList.Count;
            structureMarkers.Text = kind.Type == StructureType.Pump
                ? string.Create(CultureInfo.CurrentCulture,
                    $"An oil pump: derelict at the start. Whoever finishes repairing it (reclaiming the derelict) owns it and gets {kind.MetalIncome:0.#} metal and {kind.EnergyIncome:0} energy a second while it stands.")
                : kind.Type == StructureType.ShieldPad
                ? string.Create(CultureInfo.CurrentCulture,
                    $"A shield platform: derelict at the start. Whoever finishes repairing it (reclaiming the derelict) owns it; their units and their allies' get a bubble shield of {kind.ShieldPercent:0}% of their health as they drive off it, for {kind.EnergyPerMetal:0.#} energy a metal they cost (topped up when they drive over again). ") +
                  string.Create(CultureInfo.CurrentCulture, $"Bubbles are their units' size ×{kind.BubbleScale:0.##}, and take hits for allies within them too.")
                : partsMissing
                ? $"Made by an earlier editor: press Rebuild to add its green and red repair markers and what it leaves when destroyed. Until then it puts two markers beside its walls when destroyed."
                : kind.Marker == null
                ? (kind.Repair == null ? "An open tunnel isn't repaired." : "Made from assets: when destroyed, it puts two repair markers beside its walls.")
                : (markers == 0 ? "No repair markers: when destroyed, it is repaired by reclaiming what is left of it in its gap."
                    : string.Create(CultureInfo.CurrentCulture, $"{markers} repair marker{(markers == 1 ? "" : "s")}: green while it stands, red while destroyed; reclaim one, or what is left of it, to repair it.")) +
                  " In Select, drag a marker to move it; Delete removes it.";
            placeMarkers.Enabled = kind.Marker != null && !partsMissing;
            if (marker is var (mx, mz))
            {
                selection.Text = kind.Name + ": repair marker";
                selectionDetail.Text = string.Create(CultureInfo.CurrentCulture, $"at ({mx:0}, {mz:0}), for the {type} at ({placed.X:0}, {placed.Z:0}); drag it to move it, Delete removes it");
            }
            applyStructure.Text = partsMissing ? $"Rebuild {kind.Name}'s models{(ofKind == 1 ? "" : $" ({ofKind} placed)")}"
                : ofKind == 1 ? $"Apply to {kind.Name}" : $"Apply to every {kind.Name} ({ofKind})";
        }
        syncing = false;
        UpdateStructureControls();
    }

    bool structureShown;
    internal bool StructureShown => structureShown;

    void UpdateStructureControls()
    {
        if (shownKind == null)
            return;
        if (shownKind.Type == StructureType.Guardian)
        {
            applyGuardian.Enabled = !SameAsShown();
            return;
        }
        bool assets = structureLook.SelectedIndex == 2, pump = shownKind.Owned; // an oil pump's or a shield platform's size and look are set when it is made
        structureHeight.Enabled = !assets && !pump;
        structureRoof.Enabled = !assets && shownKind.Type is StructureType.Tunnel or StructureType.OpenTunnel;
        structureLook.Enabled = structureGap.Enabled = structureWall.Enabled = structureDepth.Enabled = !pump; // a pump's size and look are its model's
        // Models from assets can be chosen only where the kind already has them (the asset tab picks them): Apply checks below.
        applyStructure.Enabled = !(assets && string.IsNullOrEmpty(shownSettings?.Wall)) && (rebuild || !SameAsShown());
    }

    bool SameAsShown() => shownSettings != null && StructureSettings is var now && now == shownSettings;

    /// <summary>The shown kind's settings with the size and look as the group has them (its gameplay numbers kept).</summary>
    internal StructureKindSettings? StructureSettings
    {
        get
        {
            if (shownSettings is not StructureKindSettings s)
                return null;
            if (shownKind?.Type == StructureType.Guardian)
                return s with
                {
                    Look = guardianLook.SelectedIndex switch { 1 => StructureLook.Concrete, 2 => StructureLook.Metal, _ => StructureLook.Ancient },
                    KillSeconds = (double)killSeconds.Value, RocketSeconds = (double)rocketSeconds.Value,
                };
            var look = structureLook.SelectedIndex switch { 1 => StructureLook.Metal, 2 => StructureLook.Assets, _ => StructureLook.Concrete };
            return s with
            {
                Look = look,
                Height = look == StructureLook.Assets ? 0 : (double)structureHeight.Value,
                Roof = structureRoof.SelectedIndex == 1 ? StructureRoof.Cliff : StructureRoof.Flat,
                Gap = (int)structureGap.Value, WallWidth = (int)structureWall.Value, Depth = (int)structureDepth.Value,
            };
        }
    }

    /// <summary>For the self-test: sets the group's size.</summary>
    internal void SetStructureSize(double height, int gap, int wallWidth, int depth)
    {
        structureHeight.Value = Clamp(structureHeight, height);
        structureGap.Value = Clamp(structureGap, gap);
        structureWall.Value = Clamp(structureWall, wallWidth);
        structureDepth.Value = Clamp(structureDepth, depth);
    }

    internal void SetStructureLook(StructureLook look) => structureLook.SelectedIndex = look switch { StructureLook.Metal => 1, StructureLook.Assets => 2, _ => 0 };
    internal void SetStructureBroken(bool broken) => structureBroken.Checked = broken;
    internal string ApplyText => applyStructure.Text;
    internal string StructureNote => structureMarkers.Text;
    internal bool ApplyEnabled => applyStructure.Enabled;

    /// <summary>Presses a button (for the self-test), when it is enabled: PerformClick does nothing in a window that isn't shown.</summary>
    internal void Press(string what)
    {
        if (what == "apply-structure" && applyStructure.Enabled && shownKind != null && StructureSettings is StructureKindSettings settings)
            StructureChangeRequested?.Invoke(this, (shownKind.Name, settings));
        if (what == "place-markers" && placeMarkers.Enabled && shownKind != null)
            PlaceMarkersRequested?.Invoke(this, EventArgs.Empty);
        if (what == "apply-guardian" && applyGuardian.Enabled && shownKind?.Type == StructureType.Guardian && StructureSettings is StructureKindSettings guardian)
            StructureChangeRequested?.Invoke(this, (shownKind.Name, guardian));
        if (what == "clear-zone" && clearZone.Enabled && shownKind?.Type == StructureType.Guardian)
            GuardianZoneCleared?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ModeChanged;
    public event EventHandler? BrushChanged;
    public event EventHandler<double>? HeadingChanged;
    public event EventHandler<double>? TurnRequested;
    public event EventHandler? RemoveRequested;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ItemsMode Mode
    {
        get => mode;
        set
        {
            mode = value;
            syncing = true;
            foreach (var (m, button) in modeButtons)
                button.Checked = m == value;
            syncing = false;
            ModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string FeatureName { get => featureName.Text.Trim(); set => featureName.Text = value; }

    public ItemSource Store => toSmf.Checked ? ItemSource.Smf : ItemSource.FeaturePlacer;

    /// <summary>What Place and Scatter add: a feature, or (to the featureplacer) a unit or a building.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PlacedItemKind PlaceKind
    {
        get => toUnits.Checked ? PlacedItemKind.Unit : toBuildings.Checked ? PlacedItemKind.Building : PlacedItemKind.Feature;
        set
        {
            if (value == PlacedItemKind.Unit && toUnits.Enabled)
                toUnits.Checked = true;
            else if (value == PlacedItemKind.Building && toBuildings.Enabled)
                toBuildings.Checked = true;
            else if (toUnits.Checked || toBuildings.Checked)
                (toPlacer.Enabled ? toPlacer : toSmf).Checked = true;
        }
    }

    /// <summary>The heading for a new item: the box, or random.</summary>
    public double NewHeading(Random random) => randomHeading.Checked ? random.Next(0, 360) : (double)heading.Value;

    /// <summary>The Heading box itself (random or not): doors and tunnels are placed by it, never at random.</summary>
    public double HeadingBox => (double)heading.Value;

    public bool RandomHeading => randomHeading.Checked;

    public void SetNewHeading(double degrees, bool random)
    {
        heading.Value = Clamp(heading, degrees);
        randomHeading.Checked = random;
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Radius { get => (double)radius.Value; set => radius.Value = Clamp(radius, value); }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Spacing { get => (double)spacing.Value; set => spacing.Value = Clamp(spacing, value); }

    /// <summary>The grass value to paint (0 when erasing).</summary>
    public int GrassValue => eraseGrass.Checked ? 0 : (int)grassValue.Value;

    public void SetGrass(int value, bool erase)
    {
        grassValue.Value = Clamp(grassValue, Math.Max(1, value));
        eraseGrass.Checked = erase;
    }

    /// <summary>Brings the name list up to date (after assets are added or taken away), leaving everything else as it is.</summary>
    public void SetNames(IReadOnlyList<string> names)
    {
        known.Clear();
        known.UnionWith(names);
        var text = featureName.Text;
        featureName.Items.Clear();
        featureName.Items.AddRange([.. names.Order(StringComparer.OrdinalIgnoreCase)]);
        featureName.Text = text;
        CheckName();
    }

    /// <summary>The names the map defines or uses, for the name box; a featureplacer is offered only when the map has one.</summary>
    public void ShowMap(IReadOnlyList<string> names, bool hasPlacer, string? startBoxes)
    {
        known.Clear();
        known.UnionWith(names);
        featureName.Items.Clear();
        featureName.Items.AddRange([.. names.Order(StringComparer.OrdinalIgnoreCase)]);
        if (featureName.Text.Length == 0 && names.Count > 0)
            featureName.Text = names[0];
        toPlacer.Enabled = toUnits.Enabled = toBuildings.Enabled = hasPlacer;
        (hasPlacer ? toPlacer : toSmf).Checked = true;
        startNote.Text = startBoxes == null
            ? "Start positions (white-ringed circles) are dragged in Select mode."
            : $"Start positions (white-ringed circles) are dragged in Select mode. This map also has start boxes ({startBoxes}); dragging a start position doesn't move them.";
        CheckName();
    }

    /// <summary>BAR's own features, units and buildings, to say whether BAR has a name the map doesn't define (null: not known).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public BarFeatureCatalog? BarFeatures { get; set; }

    /// <summary>What is said about the name to place (empty: nothing), for the self-test.</summary>
    internal string NameWarning => nameWarning.Text;

    void CheckName()
    {
        var name = FeatureName;
        bool unit = PlaceKind is PlacedItemKind.Unit or PlacedItemKind.Building;
        nameWarning.Text = name.Length == 0 ? "Type or pick a feature name." :
            known.Contains(name) || name.StartsWith("repair-marker:", StringComparison.Ordinal) ? "" :
            BarFeatures == null ? $"\"{name}\" isn't defined by this map: it must exist in BAR, or the item will simply be missing in game." :
            unit ? (BarFeatures.FindUnit(name) != null ? "" : $"BAR has no unit called \"{name}\" ({BarFeatures.Origin}): it will be missing in game.") :
            BarFeatures.Find(name) is { Note: null } ? "" :
            BarFeatures.Find(name) != null ? $"BAR drops \"{name}\" as it loads (its model is missing): it will be missing in game." :
            $"Neither this map nor BAR defines \"{name}\" ({BarFeatures.Origin}): it will be missing in game.";
        nameWarning.ForeColor = BarTheme.Warning;
    }

    public void ShowSelection(IReadOnlyList<MapItem> items, double? headingDegrees)
    {
        syncing = true;
        selection.Text = items.Count switch
        {
            0 => "Nothing selected",
            1 => $"{items[0].Name}",
            _ => $"{items.Count} items",
        };
        selectionDetail.Text = items.Count == 0 ? "" : items.Count == 1
            ? $"at ({items[0].X:0}, {items[0].Z:0}) in the {(items[0].Source == ItemSource.Smf ? "SMF feature list" : "featureplacer")}"
            : string.Join(", ", items.GroupBy(i => i.Name).Select(g => g.Count() == 1 ? g.Key : $"{g.Count()}× {g.Key}"));
        selectedHeading.Enabled = turnLeft.Enabled = turnRight.Enabled = remove.Enabled = items.Count > 0;
        (turnLeft.Text, turnRight.Text) = ("↺ 15°", "↻ 15°");
        if (headingDegrees is double h)
            selectedHeading.Value = Clamp(selectedHeading, Math.Round(h) % 360);
        syncing = false;
    }

    /// <summary>For the self-test: types a heading for the selection.</summary>
    internal void SetSelectedHeading(double degrees) => selectedHeading.Value = Clamp(selectedHeading, degrees);
    internal string SelectionText => selection.Text;

    /// <summary>The picture of the selected item's model.</summary>
    internal ModelPreviewBox Preview => preview;

    /// <summary>Mode keys (S, A, C, G, P); true if the key was used.</summary>
    public bool HandleKey(Keys key)
    {
        foreach (var (m, _, k, _) in Modes)
            if (k == key)
            {
                Mode = m;
                return true;
            }
        return false;
    }
}
