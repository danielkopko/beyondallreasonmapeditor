using System.Globalization;
using MapCore.Assets;
using MapCore.Editing;
using MapCore.Items;
using MapCore.Structures;
using static MapEditor.PanelLayout;

namespace MapEditor;

/// <summary>
/// The Asset tab on the right: the asset chosen in the asset browser (along the bottom), with a preview of its
/// model, what it is and where it came from, and what can be done with it: add it to the map, place it (heading and
/// spacing are then set in Items › Place), put it in another category, or take it out of the library. It only shows
/// the asset and raises events; the main window does the work.
/// </summary>
sealed class AssetsPanel : UserControl
{
    readonly Label title = BarTheme.Strong(new Label { AutoSize = true, MaximumSize = new Size(290, 0) });
    readonly ModelPreviewBox preview = new() { Dock = DockStyle.Fill, Height = 240 };
    readonly Label details = new() { AutoSize = true, MaximumSize = new Size(290, 0), Margin = new Padding(3, 4, 3, 2) };
    readonly Button add = new() { Text = "Add to this map", AutoSize = true };
    readonly Button place = new() { Text = "Place", AutoSize = true };
    readonly Button newFromThis = new() { Text = "New feature from it…", AutoSize = true };
    readonly FlowLayoutPanel assetButtons = new() { AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
    readonly GroupBox assetGroup, libraryGroup, structureGroup, kindGroup, madeGroup;
    readonly Label assetHint = Hint("Choose an asset in the asset browser below the map (View › Bottom panel); double-click one to place it. " +
        "Add to this map copies a map-made asset's files in (one undo step): a file that makes several features brings them all. " +
        "Place adds it if needed and picks it in Items › Place, where its heading, scatter radius and spacing are set. " +
        "Assets built into BAR or the engine are placed by name and nothing is copied. The editor's own experimental tools are under Experimental.");
    string sectionsShown = "";
    readonly ComboBox moveTo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    readonly Button move = new() { Text = "Move", AutoSize = true };
    readonly Button remove = new() { Text = "Remove from library", AutoSize = true };

    // Experimental: a new feature from a library model (the model chosen, and the library's models to choose from).
    readonly ComboBox modelChoice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, DropDownWidth = 260 };
    readonly List<AssetRow> modelRows = [];
    AssetRow? customSource;
    readonly TextBox customName = new() { Width = 150 };
    readonly ComboBox preset = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, DropDownWidth = 240 };
    readonly TextBox customDescription = new() { Width = 150 };
    readonly NumericUpDown health = Number(1, 1_000_000_000, 100, 0, 1000);
    readonly CheckBox reclaimable = new() { Text = "Reclaimable", AutoSize = true };
    readonly NumericUpDown reclaimTime = Number(1, 1_000_000_000, 600, 0, 1000);
    readonly CheckBox autoReclaim = new() { Text = "Auto-reclaimed (area reclaim, patrol)", AutoSize = true };
    readonly NumericUpDown metal = Number(0, 10_000_000, 0, 0, 100);
    readonly NumericUpDown energy = Number(0, 10_000_000, 0, 0, 100);
    readonly CheckBox blocking = new() { Text = "Blocking: units path round it", AutoSize = true };
    readonly CheckBox indestructible = new() { Text = "Indestructible", AutoSize = true };
    readonly CheckBox flammable = new() { Text = "Flammable", AutoSize = true };
    readonly NumericUpDown footprintX = Number(1, 32, 1, 0, 1), footprintZ = Number(1, 32, 1, 0, 1);
    readonly ComboBox buildOn = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, DropDownWidth = 260 };
    readonly Label healthNote = Hint(""), reclaimNote = Hint(""), footprintNote = Hint("");
    readonly Button create = new() { Text = "Create feature", AutoSize = true };
    readonly GroupBox customGroup;

    // Experimental: a door or tunnel made of several features, worked by a map gadget.
    readonly ComboBox structureType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    readonly TextBox structureName = new() { Width = 150 };
    readonly ComboBox structureLook = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, DropDownWidth = 200 };
    readonly ComboBox structureRoof = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, DropDownWidth = 220 };
    readonly NumericUpDown structureHeight = Number(16, 512, 80, 0, 8);
    readonly ComboBox wallChoice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, DropDownWidth = 260 },
        centreChoice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, DropDownWidth = 260 },
        repairChoice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, DropDownWidth = 260 };
    readonly List<(string Name, int Row, Func<StructureType, StructureLook, bool> Shown)> structureRows = [];
    readonly NumericUpDown gap = Number(1, 32, 4, 0, 1), wallWidth = Number(1, 32, 3, 0, 1), depth = Number(1, 32, 3, 0, 1);
    readonly NumericUpDown centreHealth = Number(1, 1_000_000_000, 20_000, 0, 1000), work = Number(1, 1_000_000_000, 36_000, 0, 1000);
    readonly NumericUpDown repairMetal = Number(0, 10_000_000, 1200, 0, 100), repairEnergy = Number(0, 10_000_000, 3000, 0, 100);
    readonly NumericUpDown metalIncome = Number(0, 1000, 2, 1, 0.5m), energyIncome = Number(0, 10_000, 25, 0, 5);
    readonly CheckBox openWhenBroken = new() { Text = "The gap can be crossed while broken", AutoSize = true, Checked = true };
    readonly NumericUpDown killSeconds = Number(0.2m, 60, 1, 1, 0.2m), rocketSeconds = Number(0.5m, 60, 2, 1, 0.5m);
    readonly Button chooseTargets = new() { Text = "Choose…", AutoSize = true };
    readonly Label targetsText = new() { AutoSize = true, Anchor = AnchorStyles.Left, MaximumSize = new Size(270, 0) };
    IReadOnlyList<string>? guardianTargets; // null until chosen: the catalog's default then
    readonly NumericUpDown shieldPercent = Number(1, 500, 50, 0, 5), energyPerMetal = Number(0, 1000, 10, 1, 1), bubbleScale = Number(0.25m, 4, 1, 2, 0.05m);
    readonly Label structureNote = Hint("");
    readonly Button createKind = new() { Text = "Create kind", AutoSize = true };
    readonly ComboBox placeKind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    readonly Button placeStructure = new() { Text = "Place it", AutoSize = true };
    readonly CheckBox placeBroken = new() { Text = "Start destroyed (repaired in game)", AutoSize = true };
    readonly Button placeMade = new() { Text = "Place it", AutoSize = true };
    readonly HashSet<string> kindNames = [];
    string? wallFeature, centreFeature, repairFeature;
    CustomFeatureSettings customDefaults = new();
    bool filling;
    AssetRow? row;
    bool inMap, hasMap;

    public AssetsPanel()
    {
        AutoScroll = true;
        Padding = new Padding(6);
        var layout = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Top, AutoSize = true };
        Controls.Add(layout);

        // What is chosen: its name, model and what it is. For an asset, what can be done with it; for an
        // experimental tool (a structure type, a map's kind, the new-feature tool), its own section below instead.
        var asset = Grid();
        AddWide(asset, title);
        AddWide(asset, preview);
        AddWide(asset, details);
        assetButtons.Controls.AddRange([add, place, newFromThis]);
        AddWide(asset, assetButtons);
        assetGroup = Group("Asset", asset);
        layout.Controls.Add(assetGroup);

        var library = Grid();
        foreach (var c in AssetCategories.All.Where(c => c != AssetCategories.Experimental)) // the browser's group for tools, never an asset's
            moveTo.Items.Add(c);
        var moveRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        moveRow.Controls.AddRange([new Label { Text = "Category", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) }, moveTo, move]);
        AddWide(library, moveRow);
        AddWide(library, remove);
        libraryGroup = Group("In the library", library);
        layout.Controls.Add(libraryGroup);

        // The new-feature tool: gameplay settings for a new feature that uses a library model (only keys the engine reads).
        var custom = Grid();
        preset.Items.AddRange(["Its own settings", "Ancient monolith choke"]);
        AddRow(custom, "Model", modelChoice, "");
        AddRow(custom, "Name", customName, "");
        AddRow(custom, "Start from", preset, "");
        AddRow(custom, "Description", customDescription, "");
        AddRow(custom, "Health", health, "");
        AddWide(custom, healthNote);
        AddWide(custom, indestructible);
        AddWide(custom, reclaimable);
        AddRow(custom, "Reclaim time", reclaimTime, "");
        AddWide(custom, reclaimNote);
        AddWide(custom, autoReclaim);
        AddRow(custom, "Metal", metal, "");
        AddRow(custom, "Energy", energy, "");
        AddWide(custom, Hint("Metal and energy are what reclaiming it gives."));
        AddWide(custom, blocking);
        AddWide(custom, flammable);
        AddRow(custom, "Footprint E-W", footprintX, "squares");
        AddRow(custom, "Footprint N-S", footprintZ, "squares");
        AddWide(custom, footprintNote);
        buildOn.Items.AddRange(["No", "Yes, units can cross it", "Yes, units can't cross it"]);
        buildOn.SelectedIndex = 0;
        AddRow(custom, "Build on it", buildOn, "");
        AddWide(custom, Hint("Buildings on it need the map's structures gadget, which Create adds: it takes the feature off the blocking map " +
            "(and keeps units off it for the last choice). Factories can't go on one units can't cross."));
        AddWide(custom, create);
        AddWide(custom, Hint("Writes features/<name>.lua using the model, with only keys the engine reads, every one written out. " +
            "Not offered: nodgun and sightDistance, which neither the engine nor BAR reads for features. " +
            "Create adds it to the map as one undo step (the model comes from the library if the map lacks it), then picks it in Items › Place."));
        customGroup = Group("New feature", custom);
        layout.Controls.Add(customGroup);

        // A structure type: a new kind of it. Only the fields that type has are shown. (The type comes from the browser's
        // Experimental entry chosen; this box, not shown, holds it.)
        structureType.Items.AddRange(["Door", "Tunnel (collapses)", "Open tunnel", "Oil pump", "Guardian tower", "Shield platform"]); // StructureType's order
        structureType.SelectedIndex = 0;
        var structure = Grid();
        void Field(string name, Func<StructureType, StructureLook, bool> shown) => structureRows.Add((name, structure.RowCount - 1, shown));
        static bool Walled(StructureType t) => t is StructureType.Door or StructureType.Tunnel or StructureType.OpenTunnel;
        AddRow(structure, "Name", structureName, "");
        Field("Name", (_, _) => true);
        structureLook.Items.AddRange(["Concrete (built)", "Metal (built)", "Models from assets", "Ancient (bronze and stone)"]);
        structureLook.SelectedIndex = 0;
        AddRow(structure, "Look", structureLook, "");
        Field("Look", (t, _) => t is not (StructureType.Pump or StructureType.ShieldPad));
        structureRoof.Items.AddRange(["Flat (build on it)", "Rock on a concrete vault"]);
        structureRoof.SelectedIndex = 0;
        AddRow(structure, "Roof", structureRoof, "");
        Field("Roof", (t, l) => t is StructureType.Tunnel or StructureType.OpenTunnel && l != StructureLook.Assets);
        AddRow(structure, "Height", structureHeight, "game units");
        Field("Height", (t, l) => Walled(t) && l != StructureLook.Assets);
        AddWide(structure, Hint("Models from assets: choose a library model for each part."));
        Field("Models", (t, l) => Walled(t) && l == StructureLook.Assets);
        AddRow(structure, "Walls", wallChoice, "");
        Field("Walls", (t, l) => Walled(t) && l == StructureLook.Assets);
        AddRow(structure, "Centre", centreChoice, "");
        Field("Centre", (t, l) => Walled(t) && l == StructureLook.Assets);
        AddRow(structure, "Repair marker", repairChoice, "");
        Field("Repair marker", (t, l) => t is StructureType.Door or StructureType.Tunnel && l == StructureLook.Assets);
        AddRow(structure, "Gap", gap, "squares");
        Field("Gap", (t, _) => Walled(t) || t == StructureType.ShieldPad);
        AddRow(structure, "Wall width", wallWidth, "squares");
        Field("Wall width", (t, _) => Walled(t));
        AddRow(structure, "Depth", depth, "squares");
        Field("Depth", (t, _) => Walled(t) || t == StructureType.ShieldPad);
        foreach (var (label, box) in new[] { ("Centre health", centreHealth), ("Repair work", work), ("Repair metal", repairMetal), ("Repair energy", repairEnergy) })
        {
            AddRow(structure, label, box, "");
            Field(label, (t, _) => t is StructureType.Door or StructureType.Tunnel or StructureType.Pump or StructureType.ShieldPad);
        }
        AddRow(structure, "Pump metal", metalIncome, "a second");
        Field("Pump metal", (t, _) => t == StructureType.Pump);
        AddRow(structure, "Pump energy", energyIncome, "a second");
        Field("Pump energy", (t, _) => t == StructureType.Pump);
        AddRow(structure, "Kill time", killSeconds, "s");
        Field("Kill time", (t, _) => t == StructureType.Guardian);
        AddRow(structure, "Rocket every", rocketSeconds, "s");
        Field("Rocket every", (t, _) => t == StructureType.Guardian);
        AddRow(structure, "Crossing", chooseTargets, "first tower");
        Field("Crossing", (t, _) => t == StructureType.Guardian);
        AddWide(structure, targetsText);
        Field("Targets", (t, _) => t == StructureType.Guardian);
        AddRow(structure, "Shield", shieldPercent, "% of health");
        Field("Shield", (t, _) => t == StructureType.ShieldPad);
        AddRow(structure, "Price", energyPerMetal, "energy a metal");
        Field("Price", (t, _) => t == StructureType.ShieldPad);
        AddRow(structure, "Bubble size", bubbleScale, "× the unit's");
        Field("Bubble size", (t, _) => t == StructureType.ShieldPad);
        AddWide(structure, openWhenBroken);
        Field("Crossable broken", (t, _) => t is StructureType.Door or StructureType.Tunnel);
        AddWide(structure, structureNote);
        AddWide(structure, createKind);
        AddWide(structure, Hint("Create kind writes its models and settings into the map (one undo step). The kind then appears in the browser " +
            "under Experimental, to place."));
        structureRowsGrid = structure;
        structureGroup = Group("New kind", structure);
        layout.Controls.Add(structureGroup);

        // A kind the map has: place it.
        var kindGrid = Grid();
        AddWide(kindGrid, placeStructure);
        AddWide(kindGrid, placeBroken);
        AddWide(kindGrid, Hint("Place picks it in Items › Place: click the map where it goes. To change a placed one's size or look, or remove it, " +
            "click it on the map in Items › Select (Delete removes it)."));
        kindGroup = Group("Place", kindGrid);
        layout.Controls.Add(kindGroup);

        // A new feature the map has, made with the new-feature tool: place it.
        var madeGrid = Grid();
        AddWide(madeGrid, placeMade);
        AddWide(madeGrid, Hint("Place picks it in Items › Place: click the map where it goes (its heading, scatter radius and spacing are set there). " +
            "Its settings are in features/<name>.lua: to change them, undo it, or make another with New feature from a model."));
        madeGroup = Group("Place", madeGrid);
        layout.Controls.Add(madeGroup);

        layout.Controls.Add(assetHint);

        var tips = new ToolTip();
        tips.SetToolTip(add, "Copy this asset's definition, models and textures into the map, at the paths they had in their own map");
        tips.SetToolTip(place, "Add it to the map if needed, then place this feature on the map (Items tab, Place)");
        tips.SetToolTip(remove, "Take this asset and its files out of the library (maps that use it keep their copy)");
        tips.SetToolTip(newFromThis, "Experimental: a new feature using this model, with gameplay settings of your own");
        newFromThis.Click += (_, _) => { if (row != null) NewFeatureFromRequested?.Invoke(this, row); };
        modelChoice.SelectedIndexChanged += (_, _) =>
        {
            if (!filling && modelChoice.SelectedIndex >= 0 && modelChoice.SelectedIndex < modelRows.Count)
                CustomModelChosen?.Invoke(this, modelRows[modelChoice.SelectedIndex]);
        };
        foreach (var (choice, part) in new[] { (wallChoice, "wall"), (centreChoice, "centre"), (repairChoice, "repair") })
            choice.SelectedIndexChanged += (_, _) =>
            {
                if (!filling && choice.SelectedItem is string feature)
                    UseModel(part, feature);
            };

        add.Click += (_, _) => { if (row != null) AddRequested?.Invoke(this, row.Asset); };
        place.Click += (_, _) => { if (row != null) PlaceRequested?.Invoke(this, row); };
        move.Click += (_, _) =>
        {
            if (row != null && moveTo.SelectedItem is string c && c != row.Asset.Category)
                MoveRequested?.Invoke(this, (row.Asset, c));
        };
        remove.Click += (_, _) => { if (row != null) RemoveRequested?.Invoke(this, row.Asset); };
        preset.SelectedIndexChanged += (_, _) => { if (!filling) ApplyPreset(preset.SelectedIndex); };
        foreach (var control in new Control[] { health, reclaimTime, footprintX, footprintZ })
            control.TextChanged += (_, _) => UpdateNotes();
        foreach (var box in new[] { reclaimable, indestructible })
            box.CheckedChanged += (_, _) => UpdateNotes();
        create.Click += (_, _) => Press("create");
        structureType.SelectedIndexChanged += (_, _) =>
        {
            structureRoof.SelectedIndex = Type == StructureType.OpenTunnel ? 1 : 0;
            // A guardian starts ancient; ancient is a guardian's alone.
            if (Type == StructureType.Guardian)
                SetStructureLook(StructureLook.Ancient);
            else if (Look == StructureLook.Ancient)
                SetStructureLook(StructureLook.Concrete);
            if (Type == StructureType.ShieldPad)
                SetStructureLook(StructureLook.Metal);
            // An oil pump and a shield platform start from their own numbers; the others from a door's.
            bool own = Type is StructureType.Pump or StructureType.ShieldPad;
            var start = Type == StructureType.Pump ? StructureKindSettings.Pump() : Type == StructureType.ShieldPad ? StructureKindSettings.ShieldPad()
                : StructureKindSettings.Built(StructureLook.Concrete, Type);
            centreHealth.Value = (decimal)(own ? start.Health : 20_000);
            work.Value = (decimal)(own ? start.Work : 36_000);
            repairMetal.Value = (decimal)(own ? start.Metal : 1200);
            repairEnergy.Value = (decimal)(own ? start.Energy : 3000);
            if (Type == StructureType.ShieldPad)
                (gap.Value, depth.Value) = (start.Gap, start.Depth);
            else if (gap.Value == 6 && depth.Value == 6)
                (gap.Value, depth.Value) = (4, 3);
            SuggestName();
            UpdateStructureNote();
            UpdateStructureRows();
        };
        structureLook.SelectedIndexChanged += (_, _) =>
        {
            UpdateStructureNote();
            UpdateStructureRows();
        };
        foreach (var control in new Control[] { gap, wallWidth, depth, work, structureHeight, metalIncome, energyIncome, killSeconds, rocketSeconds, shieldPercent, energyPerMetal, bubbleScale })
            control.TextChanged += (_, _) => UpdateStructureNote();
        chooseTargets.Click += (_, _) => ChooseTargets();
        structureName.TextChanged += (_, _) => { if (!suggesting) autoName = false; };
        createKind.Click += (_, _) => Press("create-kind");
        placeStructure.Click += (_, _) => Press("place-structure");
        placeMade.Click += (_, _) => Press("place-made");
        SuggestName();
        UpdateStructureNote();
        UpdateStructureRows();
        tips.SetToolTip(create, "Write the new feature into the map (one undo step), then place it");
        ShowAsset(null, false, false, false);
        SetCustomDefaults(null, null);
    }

    public event EventHandler<AssetEntry>? AddRequested;
    public event EventHandler<AssetRow>? PlaceRequested;
    public event EventHandler<(AssetEntry Asset, string Category)>? MoveRequested;
    public event EventHandler<AssetEntry>? RemoveRequested;

    /// <summary>Create kind: a new structure kind (and for a guardian tower, the units it destroys).</summary>
    public event EventHandler<(string Name, StructureType Type, StructureKindSettings Settings, IReadOnlyList<string> Targets)>? CreateStructureKindRequested;

    /// <summary>BAR's standard units, for a guardian's targets; null when the editor has no catalog beside it.</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal UnitCatalog? Catalog
    {
        get => catalog;
        set
        {
            catalog = value;
            UpdateStructureNote();
        }
    }

    UnitCatalog? catalog;

    /// <summary>The units a new guardian kind destroys: as chosen, or the catalog's default (aircraft that attack the ground).</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal IReadOnlyList<string> GuardianTargets
    {
        get => guardianTargets ?? catalog?.DefaultTargets ?? [];
        set
        {
            guardianTargets = value;
            UpdateStructureNote();
        }
    }

    void ChooseTargets()
    {
        if (catalog == null)
            return;
        using var dialog = new TargetsDialog(catalog, GuardianTargets, StructureName.Length > 0 ? StructureName : "the new kind");
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
            GuardianTargets = dialog.Chosen;
    }

    /// <summary>A short account of a guardian's targets, e.g. "43 units: Stormbringer, Hound, …".</summary>
    internal static string TargetsSummary(IReadOnlyList<string> targets, UnitCatalog? catalog)
    {
        if (targets.Count == 0)
            return "nothing yet";
        var names = targets.Take(3).Select(n => catalog?.Find(n)?.DisplayName is { Length: > 0 } d ? d : n);
        bool isDefault = catalog != null && targets.Order(StringComparer.Ordinal).SequenceEqual(catalog.DefaultTargets.Order(StringComparer.Ordinal));
        return (isDefault ? "aircraft that attack the ground: " : "") + $"{targets.Count} unit type{(targets.Count == 1 ? "" : "s")} ({string.Join(", ", names)}{(targets.Count > 3 ? ", …" : "")})";
    }

    /// <summary>Place: put a structure of this kind on the map (Items › Place), standing or destroyed at the start.</summary>
    public event EventHandler<(string Kind, bool Broken)>? PlaceStructureRequested;

    /// <summary>The map's structure kinds, to place.</summary>
    public void ShowStructureKinds(IEnumerable<string> kinds)
    {
        var keep = placeKind.SelectedItem as string;
        placeKind.Items.Clear();
        kindNames.Clear();
        foreach (var k in kinds)
        {
            placeKind.Items.Add(k);
            kindNames.Add(k);
        }
        SuggestName();
        if (keep != null && placeKind.Items.Contains(keep))
            placeKind.SelectedItem = keep;
        else if (placeKind.Items.Count > 0)
            placeKind.SelectedIndex = placeKind.Items.Count - 1;
        placeStructure.Enabled = placeKind.Items.Count > 0;
    }

    /// <summary>Makes a library model (a feature's name) the given part's (wall, centre or repair) for the next kind.</summary>
    internal void UseModel(string part, string feature)
    {
        var choice = part switch { "wall" => wallChoice, "centre" => centreChoice, _ => repairChoice };
        switch (part)
        {
            case "wall": wallFeature = feature; break;
            case "centre": centreFeature = feature; break;
            default: repairFeature = feature; break;
        }
        filling = true;
        if (!choice.Items.Contains(feature))
            choice.Items.Add(feature);
        choice.SelectedItem = feature;
        filling = false;
        if (structureName.Text.Length == 0 || autoName)
            structureName.Text = MakeName((Type == StructureType.Door ? "door_" : "tunnel_") + feature);
        autoName = false;
    }

    bool autoName, suggesting;

    /// <summary>Fills in a name not yet used (door_1, tunnel_1, open_tunnel_1, …) while the box is empty or still holds one it suggested.</summary>
    void SuggestName()
    {
        if (structureName.Text.Length > 0 && !autoName)
            return;
        var stem = StructureKind.TypeName(Type) + "_";
        int n = 1;
        while (kindNames.Contains(stem + n))
            n++;
        suggesting = true;
        structureName.Text = stem + n;
        suggesting = false;
        autoName = true;
    }

    StructureType Type => structureType.SelectedIndex switch
    {
        1 => StructureType.Tunnel, 2 => StructureType.OpenTunnel, 3 => StructureType.Pump, 4 => StructureType.Guardian, 5 => StructureType.ShieldPad,
        _ => StructureType.Door,
    };

    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string StructureName { get => structureName.Text.Trim(); set => structureName.Text = value; }
    internal void SetStructureType(StructureType type) => structureType.SelectedIndex = (int)type;
    internal IEnumerable<string> StructureKinds => placeKind.Items.Cast<string>();

    StructureLook Look => structureLook.SelectedIndex switch { 1 => StructureLook.Metal, 2 => StructureLook.Assets, 3 => StructureLook.Ancient, _ => StructureLook.Concrete };

    internal void SetStructureLook(StructureLook look) =>
        structureLook.SelectedIndex = look switch { StructureLook.Metal => 1, StructureLook.Assets => 2, StructureLook.Ancient => 3, _ => 0 };
    internal void SetGuardianTimes(double kill, double rocket) => (killSeconds.Value, rocketSeconds.Value) = ((decimal)kill, (decimal)rocket);
    internal void SetStructureRoof(StructureRoof roof) => structureRoof.SelectedIndex = roof == StructureRoof.Cliff ? 1 : 0;
    internal void SetPlaceBroken(bool broken) => placeBroken.Checked = broken;

    internal StructureKindSettings StructureSettings
    {
        get
        {
            if (Type == StructureType.Guardian)
                return StructureKindSettings.Guardian() with { Look = Look, KillSeconds = (double)killSeconds.Value, RocketSeconds = (double)rocketSeconds.Value };
            if (Type == StructureType.ShieldPad)
                return StructureKindSettings.ShieldPad() with
                {
                    Gap = (int)gap.Value, Depth = (int)depth.Value,
                    Health = (double)centreHealth.Value, Work = (double)work.Value, Metal = (double)repairMetal.Value, Energy = (double)repairEnergy.Value,
                    ShieldPercent = (double)shieldPercent.Value, EnergyPerMetal = (double)energyPerMetal.Value, BubbleScale = (double)bubbleScale.Value,
                };
            if (Type == StructureType.Pump)
                return StructureKindSettings.Pump() with
                {
                    Health = (double)centreHealth.Value, Work = (double)work.Value, Metal = (double)repairMetal.Value, Energy = (double)repairEnergy.Value,
                    MetalIncome = (double)metalIncome.Value, EnergyIncome = (double)energyIncome.Value,
                };
            bool assets = Look == StructureLook.Assets;
            return new(assets ? wallFeature ?? "" : null, assets ? centreFeature ?? "" : null, assets && Type != StructureType.OpenTunnel ? repairFeature : null,
                (int)gap.Value, (int)wallWidth.Value, (int)depth.Value, (double)centreHealth.Value, (double)work.Value,
                (double)repairMetal.Value, (double)repairEnergy.Value, openWhenBroken.Checked, Look, assets ? 0 : (double)structureHeight.Value,
                structureRoof.SelectedIndex == 1 ? StructureRoof.Cliff : StructureRoof.Flat);
        }
    }

    void UpdateStructureNote()
    {
        bool open = Type == StructureType.OpenTunnel, pump = Type == StructureType.Pump, guardian = Type == StructureType.Guardian, pad = Type == StructureType.ShieldPad;
        bool assets = Look == StructureLook.Assets && !pump && !guardian && !pad;
        structureLook.Enabled = !pump && !pad;
        wallChoice.Enabled = centreChoice.Enabled = assets;
        repairChoice.Enabled = assets && !open;
        structureHeight.Enabled = !assets && !pump && !guardian && !pad;
        structureRoof.Enabled = !assets && Type is StructureType.Tunnel or StructureType.OpenTunnel;
        foreach (var control in new Control[] { gap, depth })
            control.Enabled = !pump && !guardian;
        wallWidth.Enabled = !pump && !guardian && !pad;
        foreach (var control in new Control[] { centreHealth, work, repairMetal, repairEnergy, openWhenBroken })
            control.Enabled = !open && !guardian;
        openWhenBroken.Enabled = !open && !pump && !guardian && !pad;
        metalIncome.Enabled = energyIncome.Enabled = pump;
        killSeconds.Enabled = rocketSeconds.Enabled = guardian;
        shieldPercent.Enabled = energyPerMetal.Enabled = bubbleScale.Enabled = pad;
        chooseTargets.Enabled = guardian && catalog != null;
        targetsText.Text = guardian ? TargetsSummary(GuardianTargets, catalog) : "";
        double t = (double)work.Value;
        if (pad)
        {
            structureNote.Text = string.Create(CultureInfo.CurrentCulture,
                    $"A shield platform: a low pad {gap.Value * 16:N0} by {depth.Value * 16:N0} game units that units drive over, derelict at the start. ") +
                string.Create(CultureInfo.CurrentCulture, $"Whoever finishes repairing it (reclaim the derelict: {Seconds(t / 300)} for a commander) owns it, in its colour. ") +
                string.Create(CultureInfo.CurrentCulture,
                    $"Their units and their allies' get a bubble shield as they drive off it: {shieldPercent.Value:0}% of the unit's health, for {energyPerMetal.Value:0.#} energy ") +
                "for each metal the unit costs (topped up, for the part missing, when it drives over again). The bubble surrounds the unit by its size " +
                string.Create(CultureInfo.CurrentCulture, $"(×{bubbleScale.Value:0.##}) and takes hits for it and for allies within it, so a titan's covers the grunts at its feet; ") +
                "when nothing is left it is gone. Destroyed, the platform is derelict again.";
            return;
        }
        if (guardian)
        {
            structureNote.Text = (Look is StructureLook.Assets ? "A guardian tower is built, not made from assets: choose Ancient, Concrete or Metal. " : "") +
                string.Create(CultureInfo.CurrentCulture,
                    $"A guardian tower: indestructible, {StructureModels.GuardianSize:0} by {StructureModels.GuardianSize:0} game units (3 squares), rocket pods pointing up. ") +
                "After placing it, select it (Items › Select) and draw the path it guards: Shift+click adds points. " +
                "Each tower then sets what it destroys crossing the path, inside a closed path and outside it, of which teams and after how long; " +
                "Crossing here is what the first tower of the kind destroys crossing its path (later ones copy the last placed). " +
                string.Create(CultureInfo.CurrentCulture,
                    $"A unit it dooms is gone {killSeconds.Value:0.#} s later wherever it goes, healed or not, however many come. Its rockets are for show.") +
                (catalog == null ? " (The unit list, BarUnits, isn't beside the editor: it destroys nothing until one is added.)" : "");
            return;
        }
        if (pump)
        {
            structureNote.Text = string.Create(CultureInfo.CurrentCulture,
                $"An oil pump, {StructureModels.PumpWidth:0} by {StructureModels.PumpLength:0} game units: derelict at the start. Whoever finishes repairing it ") +
                string.Create(CultureInfo.CurrentCulture, $"(reclaim the derelict: {Seconds(t / 300)} for a commander) owns it, and gets {metalIncome.Value:0.#} metal and ") +
                string.Create(CultureInfo.CurrentCulture, $"{energyIncome.Value:0} energy a second while it stands, its beam nodding, in its colour. Destroyed, it is derelict again.");
            return;
        }
        structureNote.Text = string.Create(CultureInfo.CurrentCulture,
            $"The opening is {gap.Value * 16:N0} game units wide; the whole thing {(gap.Value + 2 * wallWidth.Value) * 16:N0} by {depth.Value * 16:N0}") +
            (assets ? "." : string.Create(CultureInfo.CurrentCulture, $", {structureHeight.Value:N0} tall (a commander is 52 tall and 28 across).")) +
            (open ? "" : $" Repairing takes one builder {Seconds(t / 200)} at 200 build power, {Seconds(t / 300)} for a commander (300).");
    }

    /// <summary>Create feature: a new feature named so, from the shown asset's model, with these settings.</summary>
    public event EventHandler<(AssetRow Row, string Name, CustomFeatureSettings Settings)>? CreateCustomRequested;

    /// <summary>
    /// Makes a library model the new-feature tool's source (<paramref name="defaults"/>: its own definition's
    /// settings): chosen in its Model box, or the asset last chosen in the browser that has one. Without one the
    /// tool is off until a model is chosen.
    /// </summary>
    public void SetCustomDefaults(AssetRow? source, CustomFeatureSettings? defaults)
    {
        customSource = source?.Feature?.Model != null && defaults != null ? source : null;
        customGroup.Enabled = customSource != null;
        customDefaults = defaults ?? new CustomFeatureSettings();
        filling = true;
        customName.Text = customSource == null ? "" : MakeName(customSource.Name + "_custom");
        preset.SelectedIndex = 0;
        int at = customSource == null ? -1 : modelRows.FindIndex(r => r.Asset.Id == customSource.Asset.Id && r.Name == customSource.Name);
        if (customSource != null && at < 0)
        {
            modelRows.Add(customSource);
            modelChoice.Items.Add(ModelLabel(customSource));
            at = modelRows.Count - 1;
        }
        modelChoice.SelectedIndex = at;
        filling = false;
        Fill(customDefaults);
    }

    /// <summary>The library's features that have a model, to make a new feature or a structure's parts from.</summary>
    public void SetModelChoices(IReadOnlyList<AssetRow> rows)
    {
        filling = true;
        modelRows.Clear();
        modelRows.AddRange(rows);
        modelChoice.Items.Clear();
        foreach (var r in rows)
            modelChoice.Items.Add(ModelLabel(r));
        modelChoice.SelectedIndex = customSource == null ? -1 : modelRows.FindIndex(r => r.Asset.Id == customSource.Asset.Id && r.Name == customSource.Name);
        var names = rows.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var (choice, chosen) in new[] { (wallChoice, wallFeature), (centreChoice, centreFeature), (repairChoice, repairFeature) })
        {
            choice.Items.Clear();
            choice.Items.AddRange(names);
            if (chosen != null)
            {
                if (!choice.Items.Contains(chosen))
                    choice.Items.Add(chosen);
                choice.SelectedItem = chosen;
            }
        }
        filling = false;
    }

    static string ModelLabel(AssetRow r) => r.Asset.Features.Count > 1 ? $"{r.Name} ({r.Asset.Name})" : r.Name;

    /// <summary>The model the new-feature tool makes a feature from, if one is chosen.</summary>
    internal AssetRow? CustomSource => customSource;

    /// <summary>The new-feature tool's Model box changed: the main window works out that model's own settings (<see cref="SetCustomDefaults"/>).</summary>
    public event EventHandler<AssetRow>? CustomModelChosen;

    /// <summary>"New feature from it…" on an asset with a model: show the new-feature tool, with this model.</summary>
    public event EventHandler<AssetRow>? NewFeatureFromRequested;

    static string MakeName(string text)
    {
        var name = new string(text.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());
        if (name.Length == 0 || !char.IsAsciiLetter(name[0]))
            name = "f_" + name;
        return name.Length > 64 ? name[..64] : name;
    }

    /// <summary>Starts the settings from a preset: 0 the model's own, 1 the Ancient monolith choke example.</summary>
    internal void ApplyPreset(int index)
    {
        filling = true;
        preset.SelectedIndex = Math.Clamp(index, 0, preset.Items.Count - 1);
        filling = false;
        Fill(index == 1 ? customDefaults.WithAncientMonolith() : customDefaults);
    }

    void Fill(CustomFeatureSettings s)
    {
        filling = true;
        customDescription.Text = s.Description;
        health.Value = Math.Clamp((decimal)Math.Round(s.Health), health.Minimum, health.Maximum);
        reclaimable.Checked = s.Reclaimable;
        reclaimTime.Value = Math.Clamp((decimal)Math.Round(s.ReclaimTime), reclaimTime.Minimum, reclaimTime.Maximum);
        autoReclaim.Checked = s.AutoReclaimable;
        metal.Value = Math.Clamp((decimal)Math.Round(s.Metal), metal.Minimum, metal.Maximum);
        energy.Value = Math.Clamp((decimal)Math.Round(s.Energy), energy.Minimum, energy.Maximum);
        blocking.Checked = s.Blocking;
        indestructible.Checked = s.Indestructible;
        flammable.Checked = s.Flammable;
        footprintX.Value = s.FootprintX;
        footprintZ.Value = s.FootprintZ;
        buildOn.SelectedIndex = Math.Clamp(s.BuildOn, 0, 2);
        filling = false;
        UpdateNotes();
    }

    /// <summary>The settings as the group shows them.</summary>
    internal CustomFeatureSettings CustomSettings => new()
    {
        Description = customDescription.Text.Trim(),
        Health = (double)health.Value,
        Reclaimable = reclaimable.Checked,
        ReclaimTime = (double)reclaimTime.Value,
        AutoReclaimable = autoReclaim.Checked,
        Metal = (double)metal.Value,
        Energy = (double)energy.Value,
        Blocking = blocking.Checked,
        Indestructible = indestructible.Checked,
        Flammable = flammable.Checked,
        FootprintX = (int)footprintX.Value,
        FootprintZ = (int)footprintZ.Value,
        BuildOn = Math.Max(0, buildOn.SelectedIndex),
    };

    internal void SetBuildOn(int choice) => buildOn.SelectedIndex = Math.Clamp(choice, 0, 2);

    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string CustomName { get => customName.Text.Trim(); set => customName.Text = value; }

    /// <summary>
    /// What the numbers mean in game, from checked sources only: a commander's D-gun does 99,999 a hit (BAR's
    /// armcom, 2026-10-15); one builder reclaims in reclaim time ÷ its build power seconds (Recoil's Builder.cpp and
    /// UnitDef.cpp; a commander has 300); the footprint is 16 game units a square.
    /// </summary>
    void UpdateNotes()
    {
        double hp = (double)health.Value, time = (double)reclaimTime.Value;
        healthNote.Text = indestructible.Checked ? "Indestructible: nothing can damage it, the D-gun included."
            : string.Create(CultureInfo.CurrentCulture, $"About {Math.Ceiling(hp / 99_999):N0} hit(s) from a commander's D-gun (99,999 a hit in BAR); one shot can hit more than once, and BAR's gadgets may change damage.");
        reclaimNote.Text = !reclaimable.Checked ? "Not reclaimable."
            : string.Create(CultureInfo.CurrentCulture, $"One builder: {Seconds(time / 200)} at 200 build power, {Seconds(time / 300)} for a commander (300). Several builders share it.");
        footprintNote.Text = string.Create(CultureInfo.CurrentCulture,
            $"Blocks {footprintX.Value * 16:N0} × {footprintZ.Value * 16:N0} game units: units path round the footprint, not the model.");
    }

    static string Seconds(double s) => s < 120 ? string.Create(CultureInfo.CurrentCulture, $"{s:0.#} s")
        : string.Create(CultureInfo.CurrentCulture, $"{s:0} s ({s / 60:0.#} min)");

    internal string HealthNote => healthNote.Text;
    internal string ReclaimNote => reclaimNote.Text;
    internal bool CustomEnabled => customGroup.Enabled;

    /// <summary>The asset shown, if any.</summary>
    public AssetRow? Selected => row;

    public ModelPreviewBox Preview => preview;

    /// <summary>BAR's own features, to say what BAR has for a name a map only places (null: none shipped).</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public BarFeatureCatalog? BarFeatures { get; set; }

    /// <summary>Shows an asset (null: none chosen); <paramref name="libraryEmpty"/> changes what is said when there is none.</summary>
    public void ShowAsset(AssetRow? shown, bool isInMap, bool mapOpen, bool libraryEmpty)
    {
        row = shown;
        inMap = isInMap;
        hasMap = mapOpen;
        if (shown?.Tool is ExperimentalTool tool)
        {
            ShowTool(shown, tool);
            return;
        }
        assetGroup.Text = "Asset";
        ShowSections(shown == null ? "asset" : shown.Asset.BarOnly ? "asset" : "asset,library");
        add.Enabled = place.Enabled = move.Enabled = remove.Enabled = moveTo.Enabled = shown != null;
        newFromThis.Enabled = shown is { Asset.HasFiles: true, Feature.Model: not null };
        if (shown == null)
        {
            title.Text = "No asset chosen";
            details.Text = libraryEmpty ? "Open a map to start the library: its trees, rocks and buildings appear in the asset browser below the map."
                : "Choose an asset in the asset browser below the map.";
            return;
        }
        var a = shown.Asset;
        place.Enabled = hasMap;
        add.Enabled = hasMap && a.HasFiles && !inMap;
        move.Enabled = remove.Enabled = moveTo.Enabled = !a.BarOnly; // BAR's own, listed from its catalog: not in the library
        moveTo.SelectedItem = a.Category;
        var unitOfBar = a.Kind is AssetKind.Unit or AssetKind.Building ? BarFeatures?.FindUnit(shown.Name) : null;
        title.Text = unitOfBar?.Title ?? shown.Name;
        long kb = (a.Files.Sum(f => f.Size) + 1023) / 1024;
        var lines = new List<string>
        {
            a.Kind switch
            {
                AssetKind.MapFeature when a.Features.Count > 1 =>
                    $"One of {a.Features.Count} features {a.Source ?? a.Name} makes; adding it brings them all ({Plural(a.Files.Count, "file")}, {kb:N0} KB).",
                AssetKind.MapFeature => $"Made by a map ({Plural(a.Files.Count, "file")}, {kb:N0} KB).",
                AssetKind.EngineFeature => "Built into the engine.",
                AssetKind.Unit or AssetKind.Building when unitOfBar != null =>
                    $"A BAR {(a.Kind == AssetKind.Unit ? "unit" : "building")} ({unitOfBar.Faction}), placed with the map's featureplacer " +
                    $"({(a.Kind == AssetKind.Unit ? "unitlist" : "buildinglist")}): the featureplacer gadgets of the maps tried make it Gaia's, neutral and always visible.",
                AssetKind.Unit => "A BAR unit.",
                AssetKind.Building => "A BAR building.",
                _ => "A BAR feature (placed by name).",
            },
            $"Category: {a.Category}{(a.CategoryChosen ? " (your choice)" : "")}",
        };
        var bar = a.HasFiles || a.Kind is not (AssetKind.GameFeature or AssetKind.EngineFeature) ? null : BarFeatures?.Find(shown.Name);
        if ((shown.Feature?.Description ?? a.Description ?? bar?.Description) is string description)
            lines.Add(description);
        if (a.HasFiles)
            lines.Add(shown.Feature?.Model is string model ? "Model " + model : "Its model wasn't in its map (BAR may have it).");
        else if (shown.Name.Contains("geovent", StringComparison.OrdinalIgnoreCase))
            lines.Add("The engine's geothermal vent: it has no model, in game either.");
        else if (bar != null)
            lines.Add((BarFeatures!.ModelOf(shown.Name) != null ? $"BAR's model {bar.Object}" : $"BAR's model {bar.Object ?? "(none)"}, not shipped with the editor") +
                      (bar.Author is string author ? $", by {author}" : "") + $" ({BarFeatures.Origin}{(bar.FromUnit is string unit ? $"; the wreck of {unit}" : "")}).");
        else if (unitOfBar != null)
            lines.Add((BarFeatures!.UnitModelOf(shown.Name) != null ? $"BAR's model {unitOfBar.Object}" : $"BAR's model {unitOfBar.Object ?? "(none)"}, not shipped with the editor") +
                      (unitOfBar.Author is string author ? $", by {author}" : "") + $" ({BarFeatures.Origin}).");
        else if (BarFeatures != null && a.Kind is AssetKind.Unit or AssetKind.Building)
            lines.Add($"BAR has no unit called this ({BarFeatures.Origin}): placed on a map, it won't appear in game.");
        else if (BarFeatures != null && a.Kind == AssetKind.GameFeature)
            lines.Add($"BAR doesn't define it ({BarFeatures.Origin}): placed on a map, it won't appear in game.");
        if (a.Variant)
            lines.Add("* Another map has a different " + a.Name + "; a map can hold only one of them.");
        lines.Add(a.SeenIn.Count > 0 ? "Seen in " + string.Join(", ", a.SeenIn.Select(s => $"{s.Map} {s.Version}".Trim()))
            : "From BAR's own files: no map collected so far uses it.");
        if (hasMap)
            lines.Add(inMap ? (a.HasFiles ? "This map has it." : "Placeable here by name.") : "Not in this map yet.");
        lines.AddRange(a.Notes.Select(n => "Note: " + n));
        details.Text = string.Join(Environment.NewLine, lines);
    }

    internal string Details => title.Text + Environment.NewLine + details.Text;

    /// <summary>
    /// An Experimental entry: a structure type (a new kind of it, only that type's fields), a kind the map has (place
    /// it), the new-feature tool (from a library model), or a new feature the map has that was made with it (what it is,
    /// and place it). Only its own section is shown.
    /// </summary>
    void ShowTool(AssetRow shown, ExperimentalTool tool)
    {
        assetGroup.Text = "Experimental";
        title.Text = shown.Name;
        if (tool.NewFeature)
        {
            ShowSections("tool,custom");
            details.Text = ExperimentalTool.NewFeatureAbout + " Choose the model below (or choose an asset with a model in the browser, then " +
                "New feature from it…); the new feature is written into the map and placed from Items › Place.";
            return;
        }
        if (tool.Made is MadeFeature made)
        {
            ShowSections("tool,made");
            placeMade.Enabled = hasMap;
            details.Text = MadeDetails(shown, made);
            return;
        }
        var type = tool.Type!.Value;
        var about = ExperimentalTool.Types.First(t => t.Type == type).About;
        if (tool.Kind is string kind)
        {
            ShowSections("tool,kind");
            filling = true;
            if (placeKind.Items.Contains(kind))
                placeKind.SelectedItem = kind;
            filling = false;
            bool repairable = type is StructureType.Door or StructureType.Tunnel; // only a door or tunnel can start destroyed
            placeBroken.Visible = repairable;
            if (!repairable)
                placeBroken.Checked = false;
            placeStructure.Enabled = hasMap && placeKind.SelectedItem as string == kind;
            details.Text = $"{shown.Asset.Description}. {about}";
            return;
        }
        ShowSections("tool,structure");
        if (Type != type)
        {
            // Another type: its own look to start from, as its preview shows (a choice made for this type is kept).
            SetStructureType(type);
            SetStructureLook(type switch
            {
                StructureType.Guardian => StructureLook.Ancient,
                StructureType.Pump or StructureType.ShieldPad => StructureLook.Metal,
                _ => StructureLook.Concrete,
            });
        }
        createKind.Enabled = hasMap;
        details.Text = about + (hasMap ? "" : " Open a map to make a kind of it.");
    }

    /// <summary>What a new feature of the map's is: where it came from, and its settings as its file has them.</summary>
    static string MadeDetails(AssetRow shown, MadeFeature made)
    {
        var s = made.Settings;
        var c = CultureInfo.CurrentCulture;
        var lines = new List<string> { $"{shown.Asset.Description}.", $"Written by the editor as features/{made.Name}.lua." };
        lines.Add(string.Create(c, $"Health {s.Health:N0}{(s.Indestructible ? " (indestructible)" : "")}; ") +
                  (s.Reclaimable ? string.Create(c, $"reclaim time {s.ReclaimTime:N0}{(s.AutoReclaimable ? "" : ", not auto-reclaimed")}") : "not reclaimable") +
                  string.Create(c, $"; reclaiming it gives {s.Metal:N0} metal and {s.Energy:N0} energy."));
        lines.Add(string.Create(c, $"Footprint {s.FootprintX} × {s.FootprintZ} squares; ") + (s.Blocking ? "blocking" : "not blocking") +
                  (s.Flammable ? "; flammable" : "") + s.BuildOn switch
                  {
                      1 => "; buildings can go on it.",
                      2 => "; buildings can go on it, units can't cross it.",
                      _ => ".",
                  });
        lines.Add(made.Model is string model ? "Model " + model : "Its model isn't in this map: BAR drops a feature without its model.");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Shows only the sections named: asset, library (In the library), tool (the Experimental heading), custom, structure, kind, made.</summary>
    void ShowSections(string names)
    {
        var shown = names.Split(',');
        sectionsShown = names;
        assetGroup.Visible = shown.Contains("asset") || shown.Contains("tool");
        assetButtons.Visible = shown.Contains("asset");
        assetHint.Visible = shown.Contains("asset");
        libraryGroup.Visible = shown.Contains("library");
        customGroup.Visible = shown.Contains("custom");
        structureGroup.Visible = shown.Contains("structure");
        kindGroup.Visible = shown.Contains("kind");
        madeGroup.Visible = shown.Contains("made");
    }

    /// <summary>The sections shown (for the self-test; Visible reads false in a window never shown).</summary>
    internal string SectionsShown => sectionsShown;

    /// <summary>A new kind's fields: only those of the type (and look) chosen.</summary>
    void UpdateStructureRows()
    {
        var (type, look) = (Type, Look);
        foreach (var (_, at, shown) in structureRows)
        {
            bool visible = shown(type, look);
            for (int column = 0; column < 3; column++)
                if (structureRowsGrid?.GetControlFromPosition(column, at) is Control c)
                    c.Visible = visible;
        }
    }

    TableLayoutPanel? structureRowsGrid;

    /// <summary>The fields a new kind of the chosen type shows (for the self-test).</summary>
    internal IReadOnlyList<string> StructureFieldsShown => structureRows.Where(r => r.Shown(Type, Look)).Select(r => r.Name).ToList();

    /// <summary>Whether Move and Remove (the library's own actions) are offered for what is shown.</summary>
    internal bool LibraryActionsEnabled => move.Enabled && remove.Enabled;

    /// <summary>
    /// Presses a button (for the self-test): what its click does, when it is enabled. PerformClick does nothing in a
    /// window that isn't shown.
    /// </summary>
    internal void Press(string what)
    {
        switch (what)
        {
            case "add" when add.Enabled && row != null: AddRequested?.Invoke(this, row.Asset); break;
            case "place" when place.Enabled && row != null: PlaceRequested?.Invoke(this, row); break;
            case "remove" when remove.Enabled && row != null: RemoveRequested?.Invoke(this, row.Asset); break;
            case "create" when customGroup.Enabled && customSource != null: CreateCustomRequested?.Invoke(this, (customSource, CustomName, CustomSettings)); break;
            case "create-kind": CreateStructureKindRequested?.Invoke(this, (StructureName, Type, StructureSettings, Type == StructureType.Guardian ? GuardianTargets : [])); break;
            case "place-structure" when placeKind.SelectedItem is string kind: PlaceStructureRequested?.Invoke(this, (kind, placeBroken.Checked)); break;
            case "place-made" when placeMade.Enabled && row?.Tool?.Made != null: PlaceRequested?.Invoke(this, row); break;
        }
    }

    internal static string Plural(int n, string word) => string.Create(CultureInfo.CurrentCulture, $"{n:N0} {word}{(n == 1 ? "" : "s")}");
}
