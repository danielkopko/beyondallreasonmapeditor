using System.ComponentModel;
using MapCore.Assets;
using MapCore.Items;
using MapCore.Structures;

namespace MapEditor;

/// <summary>
/// One entry of the asset browser: a feature an asset defines (an asset's file can make many), a name-only asset, or
/// (<see cref="Tool"/>) one of the editor's experimental tools, listed under Experimental.
/// </summary>
sealed record AssetRow(AssetEntry Asset, AssetFeature? Feature, ExperimentalTool? Tool = null)
{
    public string Name => Feature?.Name ?? Asset.Name;
}

/// <summary>
/// An entry of the browser's Experimental group: a type of structure to make a kind of (<see cref="Type"/> alone), a
/// structure kind the open map has (<see cref="Kind"/>, with its type), the new-feature-from-a-model tool, or a new
/// feature the open map has that was made with it (<see cref="Made"/>, its model in the map at <see cref="MapRoot"/>).
/// </summary>
/// <param name="Order">
/// Where it is listed in the group: the types in their own order, then the map's kinds, the tool and the features made
/// with it, each by name. (A band rather than a place, so a chosen entry stays the same when another is added.)
/// </param>
sealed record ExperimentalTool(StructureType? Type, string? Kind, bool NewFeature, int Order, MadeFeature? Made = null, string? MapRoot = null)
{
    public const string NewFeatureId = "experimental:new-feature";

    public static string TypeId(StructureType type) => $"experimental:type:{StructureKind.TypeName(type)}";
    public static string KindId(string kind) => $"experimental:kind:{kind}";
    public static string MadeId(string feature) => $"experimental:feature:{feature}";

    /// <summary>The types, as the Asset tab names them, with what each is.</summary>
    public static readonly IReadOnlyList<(StructureType Type, string Name, string About)> Types =
    [
        (StructureType.Door, "Door", "Two indestructible walls and a gate between them that blocks and can't be built on; destroyed, builders repair it."),
        (StructureType.Tunnel, "Tunnel (collapses)", "Two walls and a roof between them that buildings go on but units can't cross; destroyed, it takes everything on it."),
        (StructureType.OpenTunnel, "Open tunnel", "Two walls and a roof that units pass under; it can't collapse."),
        (StructureType.Pump, "Oil pump", "Derelict at the start: whoever repairs it owns it, and it pumps metal and energy for them while it stands."),
        (StructureType.Guardian, "Guardian tower", "An indestructible tower guarding a path you draw: the unit types it is set to are doomed once they come near."),
        (StructureType.ShieldPad, "Shield platform", "Derelict at the start: whoever repairs it owns it, and their units get a bubble shield as they drive off it."),
    ];

    public const string NewFeatureName = "New feature from a model",
        NewFeatureAbout = "A new feature using a library model, with gameplay settings of your own (only keys the engine reads).";

    /// <summary>
    /// The group's rows: each type, the map's kinds (name, type, how many placed), the new-feature tool, then the new
    /// features the map has that were made with it (and how many placed), whose models are in <paramref name="mapRoot"/>.
    /// </summary>
    public static IEnumerable<AssetRow> Rows(IReadOnlyList<(string Name, StructureType Type, int Placed)> kinds,
        IReadOnlyList<(MadeFeature Feature, int Placed)> made, string? mapRoot)
    {
        int order = 1;
        foreach (var (type, name, about) in Types)
            yield return Row(TypeId(type), name, $"Make a kind of {name.ToLowerInvariant()}: {about}", new(type, null, false, order++));
        foreach (var (kind, type, placed) in kinds)
            yield return Row(KindId(kind), kind, $"This map's {Types.First(t => t.Type == type).Name.ToLowerInvariant()} kind, {placed:N0} placed",
                new(type, kind, false, 10));
        yield return Row(NewFeatureId, NewFeatureName, NewFeatureAbout, new(null, null, true, 20));
        foreach (var (feature, placed) in made)
            yield return Row(MadeId(feature.Name), feature.Name,
                $"This map's new feature from {feature.MadeFrom}'s model, {placed:N0} placed" +
                (feature.Settings.Description.Length > 0 ? $": {feature.Settings.Description}" : ""),
                new(null, null, false, 30, feature, mapRoot));
    }

    static AssetRow Row(string id, string name, string about, ExperimentalTool tool) =>
        new(new AssetEntry { Id = id, Name = name, Defines = [name], Kind = AssetKind.GameFeature, Category = AssetCategories.Experimental, Description = about, BarOnly = true },
            null, tool);
}

/// <summary>Where the asset browser's entries come from: the maps collected, BAR's own catalog, or both.</summary>
enum AssetSource { Both, Maps, Bar }

/// <summary>
/// The asset browser along the bottom of the window: every feature, unit and building collected from the maps
/// opened, and everything BAR itself offers a map (its units, buildings and features, from the catalog shipped with
/// the editor), as thumbnails grouped by category, to see and choose from. What is chosen is shown, and adjusted, on
/// the right (the Asset tab); double-clicking one places it. It only shows the library and raises events; the main
/// window does the work.
/// </summary>
sealed class AssetBrowser : UserControl
{
    const string Loading = "placeholder:loading", NoModel = "placeholder:none", Engine = "placeholder:engine", Bar = "placeholder:bar",
        NotInBar = "placeholder:notinbar", Vent = "placeholder:vent", NewFeature = "placeholder:new";
    const string AllCategories = "All categories";
    readonly Label summary = new() { AutoSize = true, Margin = new Padding(12, 7, 3, 3), ForeColor = BarTheme.Dim };
    readonly ComboBox show = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    readonly ComboBox from = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    readonly TextBox filter = new() { Width = 140, PlaceholderText = "Name or description" };
    readonly CheckBox collectOnOpen = new() { Text = "Collect on open", AutoSize = true, Checked = true, Margin = new Padding(12, 6, 3, 3) };
    readonly Button collectNow = new() { Text = "Collect this map", AutoSize = true };
    readonly Button collectFiles = new() { Text = "Collect map files…", AutoSize = true };
    readonly ToolTip tips = new();
    string summaryText = "";
    readonly ImageList images = new() { ImageSize = new Size(AssetThumbnails.Size, AssetThumbnails.Size), ColorDepth = ColorDepth.Depth32Bit };
    readonly ListView list = new()
    {
        View = View.LargeIcon, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill, ShowGroups = true, ShowItemToolTips = true,
    };
    readonly AssetThumbnails thumbnails = new();
    AssetLibrary? library;
    Func<AssetEntry, bool> inMap = _ => false;
    bool hasMap, syncing;
    IReadOnlyList<AssetEntry>? barEntries;
    readonly Dictionary<string, List<ListViewItem>> itemsByKey = []; // (a structure type's picture is its kinds' too)
    IReadOnlyList<(string Name, StructureType Type, int Placed)> structureKinds = [];
    IReadOnlyList<(MadeFeature Feature, int Placed)> madeFeatures = [];
    string? mapRoot;
    List<AssetRow> experimentalRows = [.. ExperimentalTool.Rows([], [], null)]; // made again only when the map's own change, so a chosen one stays the same row

    // The image list's keys and their places: Windows Forms finds a key by going through every key in turn, which with
    // 2,000-odd pictures in made each refill slow; items are given their image by its place instead.
    readonly Dictionary<string, int> imageIndex = new(StringComparer.Ordinal);
    readonly List<string> imageKeys = [];

    // Typing in the filter refills the list once typing pauses: with BAR's 2,000-odd entries a refill takes a moment.
    readonly System.Windows.Forms.Timer typing = new() { Interval = 250 };
    bool settingFilter;

    public AssetBrowser()
    {
        list.LargeImageList = images;
        AddImage(Loading, AssetThumbnails.Placeholder("…", Color.FromArgb(150, 156, 166)));
        AddImage(NoModel, AssetThumbnails.Placeholder("no model", Color.FromArgb(120, 112, 104)));
        AddImage(Engine, AssetThumbnails.Placeholder("engine", Color.FromArgb(70, 110, 150)));
        AddImage(Bar, AssetThumbnails.Placeholder("BAR", Color.FromArgb(150, 92, 60)));
        AddImage(NotInBar, AssetThumbnails.Placeholder("not in BAR", Color.FromArgb(128, 44, 40)));
        AddImage(Vent, AssetThumbnails.Placeholder("vent", Color.FromArgb(84, 80, 74)));
        AddImage(NewFeature, AssetThumbnails.Placeholder("new", Color.FromArgb(150, 118, 52)));

        // One row: what to show on the left, collecting on the right of it, and a short count at the end.
        var header = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(2, 2, 2, 0) };
        show.Items.Add(AllCategories);
        foreach (var c in AssetCategories.All)
            show.Items.Add(c);
        show.SelectedIndex = 0;
        from.Items.AddRange(["Maps and BAR", "Maps", "BAR"]); // in AssetSource's order
        from.SelectedIndex = 0;
        header.Controls.AddRange([from, show, filter, collectOnOpen, collectNow, collectFiles, summary]);
        // Sized to their text in their own font, so they fit at any DPI (the font follows the window's).
        foreach (var box in new[] { from, show })
        {
            FitWidth(box);
            box.FontChanged += (_, _) => FitWidth(box);
        }
        filter.FontChanged += (_, _) => filter.Width = TextRenderer.MeasureText("Name or description", filter.Font).Width + 16;
        Controls.Add(list);
        Controls.Add(header);

        tips.SetToolTip(from, "What maps have used (collected into the library), everything BAR itself offers a map (its units, buildings and features), or both");
        tips.SetToolTip(show, "Show one category, or all of them");
        tips.SetToolTip(filter, "Show only assets whose name or description contains this (Pawn finds armpw)");
        tips.SetToolTip(collectOnOpen, "Look through each map as it opens and add what is new to the library (the map itself isn't changed)");
        tips.SetToolTip(collectNow, "Add the open map's assets to the library");
        tips.SetToolTip(collectFiles, "Add the assets of map files (.sd7, .sdz) without opening them, e.g. a whole BAR maps folder");

        show.SelectedIndexChanged += (_, _) => { if (!syncing) FillList(); };
        from.SelectedIndexChanged += (_, _) =>
        {
            if (syncing)
                return;
            ShowSummary();
            FillList();
        };
        filter.TextChanged += (_, _) =>
        {
            if (settingFilter)
                return;
            typing.Stop();
            typing.Start();
        };
        typing.Tick += (_, _) =>
        {
            typing.Stop();
            FillList();
        };
        list.SelectedIndexChanged += (_, _) => { if (!syncing) SelectionChanged?.Invoke(this, Selected); };
        list.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left && Selected is AssetRow row) Picked?.Invoke(this, row); };
        list.ItemActivate += (_, _) => { if (Selected is AssetRow row) PlaceRequested?.Invoke(this, row); };
        collectNow.Click += (_, _) => CollectRequested?.Invoke(this, EventArgs.Empty);
        collectFiles.Click += (_, _) => CollectFilesRequested?.Invoke(this, EventArgs.Empty);
        collectOnOpen.CheckedChanged += (_, _) => { if (!syncing) CollectOnOpenChanged?.Invoke(this, collectOnOpen.Checked); };
    }

    void AddImage(string key, Image image)
    {
        images.Images.Add(key, image);
        imageIndex[key] = imageKeys.Count;
        imageKeys.Add(key);
    }

    /// <summary>A dropdown as wide as its longest choice, with room for the square at its end (about as wide as the box is high).</summary>
    static void FitWidth(ComboBox box) =>
        box.Width = box.Items.Cast<object>().Max(i => TextRenderer.MeasureText(i.ToString(), box.Font).Width) + box.Height + 10;

    public event EventHandler? CollectRequested;
    public event EventHandler? CollectFilesRequested;
    public event EventHandler<bool>? CollectOnOpenChanged;

    /// <summary>The chosen entry changed (null: none), however it happened.</summary>
    public event EventHandler<AssetRow?>? SelectionChanged;

    /// <summary>The user clicked an entry: its details are wanted on the right.</summary>
    public event EventHandler<AssetRow>? Picked;

    /// <summary>The user double-clicked an entry (or pressed Enter on it): place it.</summary>
    public event EventHandler<AssetRow>? PlaceRequested;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool CollectOnOpen
    {
        get => collectOnOpen.Checked;
        set
        {
            syncing = true;
            collectOnOpen.Checked = value;
            syncing = false;
        }
    }

    /// <summary>Where the entries shown come from: the maps collected, BAR's own, or both (the default).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AssetSource Source
    {
        get => (AssetSource)Math.Max(0, from.SelectedIndex);
        set
        {
            if (from.SelectedIndex == (int)value)
                return;
            syncing = true;
            from.SelectedIndex = (int)value;
            syncing = false;
            ShowSummary();
            FillList();
        }
    }

    /// <summary>BAR's units and buildings, for their build pictures (the tiles players know them by).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public UnitCatalog? Units { get; set; }

    /// <summary>Shows only entries whose name or description contains this (empty: all).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Filter
    {
        get => filter.Text;
        set
        {
            typing.Stop();
            settingFilter = true;
            filter.Text = value;
            settingFilter = false;
            FillList();
        }
    }

    /// <summary>The entry chosen, if any. (A list without a window yet, in a window never shown, keeps it in its items.)</summary>
    public AssetRow? Selected =>
        (list.IsHandleCreated ? list.SelectedItems.Cast<ListViewItem>() : list.Items.Cast<ListViewItem>().Where(i => i.Selected)).FirstOrDefault()?.Tag as AssetRow;

    /// <summary>Whether the open map has the chosen entry's asset (every file, byte for byte; name-only assets always).</summary>
    public bool SelectedInMap => Selected is AssetRow row && hasMap && inMap(row.Asset);

    /// <summary>The thumbnails being made for what is shown; done once they are all in.</summary>
    internal Task ThumbnailsDone { get; private set; } = Task.CompletedTask;

    /// <summary>The entries an asset makes: one per feature it defines, or the asset itself for a name-only one.</summary>
    static IEnumerable<AssetRow> Rows(AssetEntry a) =>
        a.Features.Count > 0 ? a.Features.Select(f => new AssetRow(a, f)) : [new AssetRow(a, null)];

    /// <summary>
    /// A row's picture: its own; a structure kind's is its type's; a new feature of the map's, its model there (the
    /// map's folder and the model in the key: undone and made again from another model, it is another picture).
    /// </summary>
    static string ThumbnailKey(AssetRow row) => row.Tool switch
    {
        { Type: StructureType type } => ExperimentalTool.TypeId(type),
        { Made: { Model: string model } made, MapRoot: string root } => $"{row.Asset.Id}|{root}|{model}|{made.MadeFrom}",
        _ => $"{row.Asset.Id}/{row.Name}",
    };

    /// <summary>
    /// The open map's own experimental entries, listed under Experimental to place: its structure kinds (name, type, how
    /// many placed) and the new features made with the editor it has (how many placed), their models in <paramref name="root"/>.
    /// </summary>
    public void ShowMapsOwn(IReadOnlyList<(string Name, StructureType Type, int Placed)> kinds, IReadOnlyList<(MadeFeature Feature, int Placed)> made, string? root)
    {
        if (kinds.SequenceEqual(structureKinds) && made.SequenceEqual(madeFeatures) && root == mapRoot)
            return;
        (structureKinds, madeFeatures, mapRoot) = (kinds, made, root);
        experimentalRows = [.. ExperimentalTool.Rows(kinds, made, root)];
        FillList();
    }

    /// <summary>Shows the library (null: none yet); <paramref name="isInMap"/> tells which assets the open map already has.</summary>
    public void ShowLibrary(AssetLibrary? assets, Func<AssetEntry, bool> isInMap, bool mapOpen)
    {
        library = assets;
        inMap = isInMap;
        hasMap = mapOpen;
        ShowSummary();
        collectNow.Enabled = mapOpen;
        FillList();
    }

    /// <summary>BAR's own entries (made once from the catalog), or none.</summary>
    IReadOnlyList<AssetEntry> BarEntries => BarFeatures == null ? [] : barEntries ??= BarFeatures.AssetEntries();

    /// <summary>
    /// The entries shown, by <see cref="Source"/>: the library's; BAR's (the library's own entry for a name a map has
    /// used, keeping where it was seen); or the library's and the rest of BAR's.
    /// </summary>
    IEnumerable<AssetEntry> Entries()
    {
        var mine = library?.Entries ?? [];
        if (Source == AssetSource.Maps || BarFeatures == null)
            return mine;
        var byId = mine.GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        return Source == AssetSource.Bar
            ? BarEntries.Select(e => byId.GetValueOrDefault(e.Id) ?? e)
            : mine.Concat(BarEntries.Where(e => !byId.ContainsKey(e.Id)));
    }

    void ShowSummary()
    {
        var entries = library?.Entries ?? [];
        int maps = entries.SelectMany(e => e.SeenIn).Select(s => (s.Map, s.Version)).Distinct().Count();
        int made = entries.Count(e => e.HasFiles), features = entries.Where(e => e.HasFiles).Sum(e => Math.Max(1, e.Features.Count));
        var bar = BarEntries;
        string ofBar = bar.Count == 0 ? "" : $"{bar.Count:N0} of BAR's ({bar.Count(e => e.Kind == AssetKind.Unit):N0} units, " +
            $"{bar.Count(e => e.Kind == AssetKind.Building):N0} buildings, {bar.Count(e => e.Kind is AssetKind.GameFeature or AssetKind.EngineFeature):N0} features, {BarFeatures!.Origin})";
        string fromMaps = entries.Count == 0 ? "No assets from maps yet: open a map, or collect from map files." :
            $"{AssetsPanel.Plural(entries.Count, "asset")} from {AssetsPanel.Plural(maps, "map")}: {made} made by maps " +
            $"({AssetsPanel.Plural(features, "feature")}), {entries.Count(e => !e.HasFiles)} built into BAR or the engine";
        summaryText = Source switch
        {
            AssetSource.Maps => fromMaps,
            AssetSource.Bar => ofBar.Length > 0 ? ofBar : "BAR's catalog isn't beside the editor.",
            _ => ofBar.Length > 0 ? fromMaps + Environment.NewLine + ofBar : fromMaps,
        };
        string mapsShort = entries.Count == 0 ? "No assets from maps yet" : $"{AssetsPanel.Plural(entries.Count, "asset")} · {AssetsPanel.Plural(maps, "map")}";
        string barShort = $"{bar.Count:N0} of BAR's";
        summary.Text = Source switch
        {
            AssetSource.Maps => mapsShort,
            AssetSource.Bar => barShort,
            _ => bar.Count > 0 ? $"{mapsShort} · {barShort}" : mapsShort,
        };
        tips.SetToolTip(summary, summaryText);
    }

    /// <summary>Selects an entry by its id, in the category shown if it is there (else under its own), as a click would.</summary>
    public void Choose(string id)
    {
        if (list.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Tag is AssetRow r && r.Asset.Id == id) is ListViewItem item)
        {
            syncing = true;
            foreach (ListViewItem i in list.SelectedItems)
                i.Selected = false;
            item.Selected = true;
            item.EnsureVisible();
            syncing = false;
            SelectionChanged?.Invoke(this, Selected);
            return;
        }
        if (filter.Text.Length > 0)
            Filter = "";
        Select(id.StartsWith("experimental:", StringComparison.Ordinal) ? AssetCategories.Experimental : show.SelectedItem as string ?? AllCategories, id);
    }

    /// <summary>Selects an asset's entry (its feature named, or its first), showing its category; for the self-test and after an action.</summary>
    public void Select(string category, string? id = null, string? feature = null)
    {
        syncing = true;
        show.SelectedItem = show.Items.Contains(category) ? category : AllCategories;
        syncing = false;
        FillList();
        if (id != null)
        {
            var item = list.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Tag is AssetRow r && r.Asset.Id == id &&
                                                                         (feature == null || r.Name.Equals(feature, StringComparison.OrdinalIgnoreCase)));
            syncing = true;
            foreach (ListViewItem i in list.SelectedItems)
                i.Selected = false;
            if (item != null)
            {
                item.Selected = true;
                item.EnsureVisible();
            }
            syncing = false;
        }
        SelectionChanged?.Invoke(this, Selected);
    }

    void FillList()
    {
        var keep = Selected;
        var category = show.SelectedItem as string ?? AllCategories;
        var text = filter.Text.Trim();
        // The editor's own experimental tools are listed whatever the source: they are neither a map's nor BAR's.
        var rows = Entries().Where(e => category == AllCategories || e.Category == category).SelectMany(Rows)
            .Concat(category is AllCategories or AssetCategories.Experimental ? experimentalRows : [])
            .Where(r => text.Length == 0 || r.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                        (r.Feature?.Description ?? r.Asset.Description)?.Contains(text, StringComparison.OrdinalIgnoreCase) == true) // "Pawn" finds armpw
            .OrderBy(r => r.Tool?.Order ?? 0).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Asset.Id, StringComparer.Ordinal).ToList();
        syncing = true;
        list.BeginUpdate();
        list.Items.Clear();
        list.Groups.Clear();
        itemsByKey.Clear();
        var groups = new Dictionary<string, ListViewGroup>();
        foreach (var c in AssetCategories.All)
        {
            int n = rows.Count(r => r.Asset.Category == c);
            if (n > 0)
                list.Groups.Add(groups[c] = new ListViewGroup(c, $"{c} ({n:N0})"));
        }
        ListViewItem? kept = null;
        foreach (var row in rows)
        {
            var a = row.Asset;
            bool here = hasMap && a.HasFiles && inMap(a);
            var item = new ListViewItem((here ? "✓ " : "") + row.Name + (a.Variant ? " *" : ""))
            {
                Tag = row,
                Group = groups.GetValueOrDefault(a.Category),
                ImageIndex = imageIndex[ImageFor(row)],
                ToolTipText = $"{row.Name}\n{a.Category} · " + a.Kind switch
                {
                    _ when row.Tool != null => a.Description ?? "",
                    AssetKind.MapFeature => $"from {string.Join(", ", a.SeenIn.Select(s => s.Map).Distinct())}",
                    _ when a.HasFiles => "",
                    AssetKind.Unit or AssetKind.Building when BarFeatures?.FindUnit(row.Name) is BarUnit unit =>
                        $"BAR's {(unit.Building ? "building" : "unit")} ({unit.Faction}): {unit.DisplayName}, {unit.Description}",
                    AssetKind.Unit or AssetKind.Building when BarFeatures != null => "BAR has no unit called this: it won't appear in game",
                    _ when row.Name.Contains("geovent", StringComparison.OrdinalIgnoreCase) => "the engine's geothermal vent: no model, in game either",
                    _ when BarFeatures?.ModelOf(row.Name) != null => a.Kind == AssetKind.EngineFeature ? "built into the engine; BAR draws it with this model" : "built into BAR; BAR's model",
                    _ when BarFeatures != null && a.Kind == AssetKind.GameFeature && BarFeatures.Find(row.Name) == null => "BAR doesn't define it: it won't appear in game",
                    AssetKind.EngineFeature => "built into the engine",
                    _ => "built into BAR",
                } + (here ? "\nThis map has it." : "") + (a.Variant ? "\n* another map has a different one" : ""),
            };
            list.Items.Add(item);
            var key = ThumbnailKey(row);
            if (!itemsByKey.TryGetValue(key, out var same))
                itemsByKey[key] = same = [];
            same.Add(item);
            if (keep != null && a.Id == keep.Asset.Id && row.Name == keep.Name && row.Tool == keep.Tool)
                kept = item;
        }
        if (kept != null)
            kept.Selected = true;
        list.EndUpdate();
        syncing = false;
        if (keep != Selected)
            SelectionChanged?.Invoke(this, Selected);
        MakeThumbnails(rows);
    }

    /// <summary>BAR's own units, buildings and features with their models: listed, and drawn for names maps only place (null: none shipped).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public BarFeatureCatalog? BarFeatures
    {
        get => barFeatures;
        set
        {
            barFeatures = value;
            barEntries = null;
        }
    }

    BarFeatureCatalog? barFeatures;

    /// <summary>
    /// Whether a row has a picture to make: its own model in the library; for a unit or building, BAR's build picture
    /// or model; for a name-only feature, BAR's model.
    /// </summary>
    bool HasModel(AssetRow row) => row.Asset.Kind switch
    {
        // A structure type or kind: the type's built model; a new feature of the map's, its model there; the new-feature tool has none.
        _ when row.Tool != null => row.Tool.Type != null || row.Tool.Made?.Model != null,
        AssetKind.MapFeature => row.Feature?.Model != null,
        AssetKind.Unit or AssetKind.Building => AssetThumbnails.UnitIcon(Units, row.Name) != null || BarFeatures?.UnitModelOf(row.Name) != null,
        _ => BarFeatures?.ModelOf(row.Name) != null,
    };

    string ImageFor(AssetRow row)
    {
        if (row.Tool is { NewFeature: true })
            return NewFeature;
        if (row.Tool != null && !HasModel(row))
            return NoModel; // (an Experimental entry is neither BAR's nor the engine's)
        if (!row.Asset.HasFiles && !HasModel(row))
            return row.Name.Contains("geovent", StringComparison.OrdinalIgnoreCase) ? Vent
                : row.Asset.Kind == AssetKind.EngineFeature ? Engine
                : BarFeatures != null && row.Asset.Kind == AssetKind.GameFeature && BarFeatures.Find(row.Name) == null ? NotInBar
                : BarFeatures != null && row.Asset.Kind is AssetKind.Unit or AssetKind.Building && BarFeatures.FindUnit(row.Name) == null ? NotInBar
                : Bar;
        if (!HasModel(row))
            return NoModel;
        var key = ThumbnailKey(row);
        return imageIndex.ContainsKey(key) ? key : Loading;
    }

    void MakeThumbnails(List<AssetRow> rows)
    {
        if (library == null)
        {
            ThumbnailsDone = Task.CompletedTask;
            return;
        }
        // In the order they are shown (by category, then name), so the ones in view come first.
        var order = AssetCategories.All.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);
        var missing = rows.Where(r => HasModel(r) && !imageIndex.ContainsKey(ThumbnailKey(r))).DistinctBy(ThumbnailKey)
            .OrderBy(r => order.GetValueOrDefault(r.Asset.Category, order.Count)).ToList();
        ThumbnailsDone = missing.Count == 0 ? Task.CompletedTask : thumbnails.Make(library, BarFeatures, Units, missing, (row, bitmap) =>
        {
            var key = ThumbnailKey(row);
            if (bitmap != null && !imageIndex.ContainsKey(key))
                AddImage(key, bitmap);
            if (itemsByKey.TryGetValue(key, out var items))
                foreach (var item in items)
                    item.ImageIndex = imageIndex[bitmap != null ? key : NoModel];
        });
    }

    /// <summary>The image an entry shows: its own thumbnail's key, or a placeholder's (for the self-test).</summary>
    internal string? ImageKeyOf(string id, string name) => list.Items.Cast<ListViewItem>()
        .FirstOrDefault(i => i.Tag is AssetRow r && r.Asset.Id == id && r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { ImageIndex: >= 0 } item
        ? imageKeys[item.ImageIndex] : null;

    internal bool HasThumbnail(string id, string name) => imageIndex.ContainsKey($"{id}/{name}") && ImageKeyOf(id, name) == $"{id}/{name}";

    /// <summary>The names listed, in order (for the self-test).</summary>
    internal IEnumerable<string> ListedNames => list.Items.Cast<ListViewItem>().Select(i => ((AssetRow)i.Tag!).Name);

    internal IEnumerable<string> GroupRows => list.Groups.Cast<ListViewGroup>().Select(g => g.Header);
    internal string Summary => summaryText;

    /// <summary>
    /// Does what a click would (for the self-test; PerformClick does nothing in a window that isn't shown): "collect",
    /// or "activate" (a double-click on the chosen entry).
    /// </summary>
    internal void Press(string what)
    {
        switch (what)
        {
            case "collect" when collectNow.Enabled: CollectRequested?.Invoke(this, EventArgs.Empty); break;
            case "activate" when Selected is AssetRow row: PlaceRequested?.Invoke(this, row); break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            thumbnails.Dispose();
            typing.Dispose();
            images.Dispose();
            tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
