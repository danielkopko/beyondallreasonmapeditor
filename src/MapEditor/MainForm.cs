using System.Globalization;
using MapCore;
using MapCore.Assets;
using MapCore.Structures;
using MapCore.Editing;
using MapCore.Rendering;
using HeightBrush = MapCore.Heights.Brush;
using Ramp = MapCore.Heights.Ramp;
using RampPath = MapCore.Heights.RampPath;
using MapCore.Items;

namespace MapEditor;

/// <summary>An open map: its edit session, the view renderer, and the workspace folder it was extracted to.</summary>
/// <param name="Record">The recovery record (journal and lock) beside the workspace; null when not kept.</param>
sealed record OpenMap(MapEditSession Session, MapRenderer Renderer, string Workspace, string Archive, SessionRecord? Record);

/// <summary>Main editor window: menus, toolbar, the map view, a message list and the status bar.</summary>
sealed partial class MainForm : Form
{
    const string AppName = "BAR Map Editor";

    static readonly (MapViewLayer Layer, string Text)[] LayerNames =
    [
        (MapViewLayer.Texture, "Texture"), (MapViewLayer.Heights, "Heights"), (MapViewLayer.Metal, "Metal"),
        (MapViewLayer.Grass, "Grass"), (MapViewLayer.Splat, "Splat"), (MapViewLayer.Normals, "Normals"),
        (MapViewLayer.Minimap, "Minimap"),
    ];

    /// <summary>Layers that can be exported to PNG and imported back: menu text, session layer name, file name, availability.</summary>
    static readonly (string Text, string Layer, string FileName, MapViewLayer? Needs)[] ImageLayers =
    [
        ("&Heights (16-bit grey)…", "heights", "heights.png", null),
        ("&Texture…", "texture", "texture.png", null),
        ("&Metal…", "metal", "metal.png", null),
        ("&Grass…", "grass", "grass.png", null),
        ("Terrain t&ypes…", "types", "types.png", null),
        ("&Splat distribution…", "resource:splatDistrTex", "splat.png", MapViewLayer.Splat),
        ("&Normal map…", "resource:detailNormalTex", "normals.png", MapViewLayer.Normals),
    ];

    /// <summary>Height change per dab at full strength (game units) for raise, lower and noise.</summary>
    const double MaxStepPerDab = 10;

    static readonly (MapOverlays Overlay, string Menu, string Button)[] OverlayNames =
    [
        (MapOverlays.WaterLine, "&Water line", "Water"), (MapOverlays.MetalSpots, "&Metal spots", "Metal"),
        (MapOverlays.Items, "&Items", "Items"), (MapOverlays.StartPositions, "&Start positions", "Starts"),
        (MapOverlays.Grid, "&Grid", "Grid"),
    ];

    readonly string? initialArchive;
    readonly EditorSettings settings = EditorSettings.Load();
    readonly MapViewControl mapView = new() { Dock = DockStyle.Fill };
    readonly HeightToolPanel heightTools = new() { Dock = DockStyle.Fill };
    readonly TextureToolPanel textureTools = new() { Dock = DockStyle.Fill };
    readonly ColourPanel colourPanel = new() { Dock = DockStyle.Fill };
    readonly MetalPanel metalPanel = new() { Dock = DockStyle.Fill };
    readonly ItemsPanel itemsPanel = new() { Dock = DockStyle.Fill };
    readonly AssetsPanel assetsPanel = new() { Dock = DockStyle.Fill };
    readonly AssetBrowser assetBrowser = new() { Dock = DockStyle.Fill };

    // The window's middle: the map view over the asset browser on the left, the tool tabs down the right (like
    // Blender: choose at the bottom, see and adjust on the right). Split containers keep that arrangement whatever
    // order the windows are made in; docked siblings would follow their order, which Windows can change.
    readonly SplitContainer mainSplit = new() { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, SplitterWidth = 5 };
    readonly SplitContainer browserSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2, SplitterWidth = 5 };
    const int ToolTabsWidth = 336; // at 96 DPI; scaled with the screen (FitToolTabs)
    const int BeautifyTabWidth = 48; // more while the Beautify panel's tab is open: seven tabs as wide as six were

    /// <summary>
    /// The tool tabs' width for this screen: <see cref="ToolTabsWidth"/> at 96 DPI, scaled with the window's DPI (the
    /// split container keeps a fixed panel's width in pixels, so on a 150% screen it would be two thirds as wide).
    /// </summary>
    void FitToolTabs()
    {
        if (mainSplit.Width > 0)
            mainSplit.SplitterDistance = Math.Max(mainSplit.Panel1MinSize,
                mainSplit.Width - LogicalToDeviceUnits(ToolTabsWidth + (BeautifyOpen ? BeautifyTabWidth : 0)) - mainSplit.SplitterWidth);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        FitToolTabs(); // the window's own DPI is known now
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        FitToolTabs();
    }
    readonly ToolStripMenuItem browserItem = new("&Bottom panel (assets, log)", null, null, Keys.F4) { CheckOnClick = true };

    // The Beautify panel: a tab beside the tools while it is open (View › Beautify panel, or the toolbar's Beautify).
    readonly BeautifyPanel beautifyPanel = new() { Dock = DockStyle.Fill };
    readonly TabPage beautifyPage = new("Beautify");
    readonly ToolStripMenuItem beautifyItem = new("Beauti&fy panel") { CheckOnClick = true };
    readonly ToolStripButton beautifyButton = new("Beautify")
    {
        CheckOnClick = true, ToolTipText = "Open the Beautify panel: paint the texture and roughen the cliffs by the terrain, keeping where units and buildings go",
    };

    /// <summary>The map whose own colours the Beautify panel shows (read once a map, when they are wanted).</summary>
    MapEditSession? coloursReadFrom;

    // The bottom panel: the asset browser, and the log in a tab beside it.
    readonly TabControl bottomTabs = new BarTabControl { Dock = DockStyle.Fill };
    readonly TabPage assetsPage = new("Assets"), logPage = new("Log");
    int unreadLog;
    readonly ToolStripStatusLabel lastLogLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft, IsLink = false };
    readonly ModelPreviews previews = new();
    HashSet<string>? assetsHere;
    int assetsHereVersion = -1;
    AssetRow? previewRow;
    readonly FeatureLookup features = new();
    string? itemPreviewKey;
    AssetLibrary? library;

    /// <summary>Where the asset library is kept (the self-test points it at a folder of its own).</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal string AssetRoot { get; set; } = AssetLibrary.DefaultRoot;

    /// <summary>The asset library, opened on first use.</summary>
    internal AssetLibrary Library
    {
        get
        {
            if (library == null)
            {
                library = new AssetLibrary(AssetRoot);
                if (library.LoadProblem != null)
                    Log(library.LoadProblem);
            }
            return library;
        }
    }

    internal AssetsPanel AssetsPanel => assetsPanel;
    internal AssetBrowser AssetBrowser => assetBrowser;
    internal SplitContainer BrowserSplit => browserSplit;
    readonly TabControl toolTabs = new BarTabControl { Dock = DockStyle.Fill, FillStrip = true };
    readonly Dictionary<string, (ToolStripMenuItem Import, ToolStripMenuItem Export)> layerFileItems = [];
    readonly ListBox messages = new() { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
    readonly ToolStripStatusLabel cursorLabel = new() { TextAlign = ContentAlignment.MiddleLeft };
    readonly ToolStripStatusLabel noteLabel = new() { ForeColor = BarTheme.Warning };
    readonly ToolStripStatusLabel zoomLabel = new() { BorderSides = ToolStripStatusLabelBorderSides.Left };
    readonly ToolStripStatusLabel busyLabel = new() { Visible = false };
    readonly ToolStripProgressBar progress = new() { Style = ProgressBarStyle.Marquee, Visible = false, Width = 120 };
    readonly ToolStripComboBox layerBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };

    // Symmetry: off, a mirror across the axis chosen, or mirrored and flipped (a half turn). Not kept between runs: a
    // mirror forgotten on would quietly double the edits on the next map.
    readonly ToolStripComboBox symmetryBox = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Width = 105,
        ToolTipText = "Off; Mirror: every edit is made again across the axis chosen; Mirror + flip: at the opposite side and corner (a half turn)",
    };
    readonly ToolStripComboBox axisBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100, ToolTipText = "The line Mirror makes its copies across" };

    static readonly (MirrorKind Kind, string Text)[] Axes =
        [(MirrorKind.LeftRight, "Left | right"), (MirrorKind.TopBottom, "Top | bottom"), (MirrorKind.Diagonal, "Diagonal ╲"), (MirrorKind.AntiDiagonal, "Diagonal ╱")];
    readonly Dictionary<MapViewLayer, ToolStripMenuItem> layerItems = [];
    readonly Dictionary<MapOverlays, (ToolStripMenuItem Menu, ToolStripButton Button)> overlayItems = [];
    readonly List<ToolStripItem> needMap = [];
    readonly ToolStripMenuItem undoItem, redoItem;
    readonly ToolStripButton undoButton, redoButton;

    OpenMap? map;
    TerrainWindow? terrainWindow;
    TerrainView? terrainView;
    bool busy, syncing, strokeFailed;
    double flattenTarget;

    // Metal tab: the spots as found after the last change, the selected one (by position), and a drag in progress.
    List<MapCore.Metal.MetalSpot> spots = [];
    (double X, double Z)? selectedSpot;
    MapCore.Metal.MetalSpot? draggedSpot;
    (double X, double Z) dragOrigin;
    ViewMark? dropGhost;
    bool luaLayoutConfirmed;

    // Items tab: the selection (found again after every change by store, list, name and position), and a drag.
    sealed record ItemKey(ItemSource Source, PlacedItemKind Kind, string Name, double X, double Z);
    enum ItemDrag { None, Items, Start, Box, Structure, Marker, ZonePoint }
    List<ItemKey> selectedItems = [];
    ItemDrag itemDrag;
    (double X, double Z) itemDragFrom, itemDragNow, startOrigin;
    int draggedTeam;
    bool boxAdds;
    readonly Random random = new();
    readonly List<(double X, double Z)> scattered = [];

    /// <summary>Set when the workspace was saved but packing the archive failed: the edits exist only here.</summary>
    bool notPacked;

    /// <summary>The archive last saved by Save As, if any (the title shows it; the next Save As starts there).</summary>
    string? savedAs;

    public MainForm(string? initialArchive = null)
    {
        this.initialArchive = initialArchive;
        Text = AppName;
        Font = BarTheme.Font;
        ClientSize = new Size(1280, 860);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;

        // ---- menus
        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(new ToolStripMenuItem("&Open…", null, (_, _) => OpenWithDialog(), Keys.Control | Keys.O));
        file.DropDownItems.Add(NeedsMap(new ToolStripMenuItem("Save &As…", null, async (_, _) => await SaveAsAsync(), Keys.Control | Keys.Shift | Keys.S)));
        file.DropDownItems.Add(NeedsMap(new ToolStripMenuItem("&Close", null, (_, _) => CloseWithConfirm(), Keys.Control | Keys.W)));
        file.DropDownItems.Add(new ToolStripSeparator());
        var importMenu = NeedsMap(new ToolStripMenuItem("&Import layer"));
        var exportMenu = NeedsMap(new ToolStripMenuItem("&Export layer"));
        foreach (var (text, layer, fileName, _) in ImageLayers)
        {
            var import = new ToolStripMenuItem(text, null, async (_, _) => await ImportWithDialogAsync(layer));
            var export = new ToolStripMenuItem(text, null, async (_, _) => await ExportWithDialogAsync(layer, fileName));
            importMenu.DropDownItems.Add(import);
            exportMenu.DropDownItems.Add(export);
            layerFileItems[layer] = (import, export);
        }
        var worldMachine = NeedsMap(new ToolStripMenuItem("&World Machine"));
        worldMachine.DropDownItems.AddRange(
        [
            new ToolStripMenuItem("&Send to World Machine", null, async (_, _) => await OnWorldMachine("send")),
            new ToolStripMenuItem("&Bring back from World Machine", null, async (_, _) => await OnWorldMachine("bring")),
            new ToolStripMenuItem("B&uild in World Machine (Professional)", null, async (_, _) => await OnWorldMachine("build")),
            new ToolStripSeparator(),
            new ToolStripMenuItem("&Open the exchange folder", null, async (_, _) => await OnWorldMachine("open")),
            new ToolStripMenuItem("&Choose the exchange folder…", null, async (_, _) => await OnWorldMachine("folder")),
        ]);
        file.DropDownItems.AddRange([importMenu, exportMenu, worldMachine, new ToolStripSeparator()]);
        file.DropDownItems.Add(new ToolStripMenuItem("E&xit", null, (_, _) => Close()) { ShortcutKeyDisplayString = "Alt+F4" });

        var edit = new ToolStripMenuItem("&Edit");
        undoItem = new ToolStripMenuItem("&Undo", null, (_, _) => Undo(), Keys.Control | Keys.Z);
        redoItem = new ToolStripMenuItem("&Redo", null, (_, _) => Redo(), Keys.Control | Keys.Y);
        edit.DropDownItems.AddRange([undoItem, redoItem]);

        var viewMenu = new ToolStripMenuItem("&View");
        for (int i = 0; i < LayerNames.Length; i++)
        {
            var (layer, text) = LayerNames[i];
            var item = NeedsMap(new ToolStripMenuItem(text, null, async (_, _) => await SelectLayerAsync(layer), Keys.Control | (Keys.D1 + i)));
            layerItems[layer] = item;
            viewMenu.DropDownItems.Add(item);
            layerBox.Items.Add(text);
        }
        viewMenu.DropDownItems.Add(new ToolStripSeparator());
        foreach (var (overlay, menuText, buttonText) in OverlayNames)
        {
            var item = NeedsMap(new ToolStripMenuItem(menuText) { CheckOnClick = true });
            var button = NeedsMap(new ToolStripButton(buttonText) { CheckOnClick = true, ToolTipText = $"Show {menuText.Replace("&", "").ToLowerInvariant()}" });
            item.CheckedChanged += (_, _) => SetOverlay(overlay, item.Checked);
            button.CheckedChanged += (_, _) => SetOverlay(overlay, button.Checked);
            overlayItems[overlay] = (item, button);
            viewMenu.DropDownItems.Add(item);
        }
        viewMenu.DropDownItems.Add(new ToolStripSeparator());
        viewMenu.DropDownItems.Add(NeedsMap(new ToolStripMenuItem("&3D view", null, (_, _) => OpenTerrainWindow(), Keys.F3)));
        viewMenu.DropDownItems.Add(browserItem);
        viewMenu.DropDownItems.Add(beautifyItem);
        viewMenu.DropDownItems.Add(new ToolStripSeparator());
        viewMenu.DropDownItems.Add(NeedsMap(new ToolStripMenuItem("Zoom to &fit", null, (_, _) => mapView.ZoomToFit()) { ShortcutKeyDisplayString = "Home" }));
        viewMenu.DropDownItems.Add(NeedsMap(new ToolStripMenuItem("Zoom &in", null, (_, _) => mapView.ZoomBy(1.5625)) { ShortcutKeyDisplayString = "+" }));
        viewMenu.DropDownItems.Add(NeedsMap(new ToolStripMenuItem("Zoom &out", null, (_, _) => mapView.ZoomBy(0.64)) { ShortcutKeyDisplayString = "-" }));

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(new ToolStripMenuItem("&Controls…", null, (_, _) => ShowControls()));
        menu.Items.AddRange([file, edit, viewMenu, help]);

        // ---- toolbar
        var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        toolbar.Items.Add(new ToolStripButton("Open", null, (_, _) => OpenWithDialog()) { ToolTipText = "Open a map (Ctrl+O)" });
        toolbar.Items.Add(NeedsMap(new ToolStripButton("Save As", null, async (_, _) => await SaveAsAsync()) { ToolTipText = "Save as a new map (Ctrl+Shift+S)" }));
        toolbar.Items.Add(new ToolStripSeparator());
        undoButton = new ToolStripButton("Undo", null, (_, _) => Undo());
        redoButton = new ToolStripButton("Redo", null, (_, _) => Redo());
        toolbar.Items.AddRange([undoButton, redoButton, new ToolStripSeparator(), new ToolStripLabel("Layer:"), NeedsMap(layerBox), new ToolStripSeparator()]);
        foreach (var (overlay, _, _) in OverlayNames)
            toolbar.Items.Add(overlayItems[overlay].Button);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(NeedsMap(new ToolStripButton("Fit", null, (_, _) => mapView.ZoomToFit()) { ToolTipText = "Zoom to fit (Home)" }));
        toolbar.Items.Add(NeedsMap(new ToolStripButton("3D", null, (_, _) => OpenTerrainWindow()) { ToolTipText = "Open the 3D view (F3)" }));
        toolbar.Items.Add(beautifyButton);
        symmetryBox.Items.AddRange(["Off", "Mirror", "Mirror + flip"]);
        symmetryBox.SelectedIndex = 0;
        foreach (var (_, text) in Axes)
            axisBox.Items.Add(text);
        axisBox.SelectedIndex = 0;
        axisBox.Enabled = false;
        toolbar.Items.AddRange([new ToolStripSeparator(), new ToolStripLabel("Symmetry:"), symmetryBox, axisBox]);
        symmetryBox.SelectedIndexChanged += (_, _) => { if (!syncing) ApplySymmetry(announce: true); };
        axisBox.SelectedIndexChanged += (_, _) => { if (!syncing) ApplySymmetry(announce: true); };
        layerBox.SelectedIndexChanged += async (_, _) =>
        {
            if (!syncing && layerBox.SelectedIndex >= 0)
                await SelectLayerAsync(LayerNames[layerBox.SelectedIndex].Layer);
        };

        // ---- status bar
        var status = new StatusStrip();
        status.Items.AddRange([cursorLabel, lastLogLabel, noteLabel, busyLabel, progress, zoomLabel]);
        lastLogLabel.ToolTipText = "The latest message; click to see the log";
        lastLogLabel.Click += (_, _) => ShowLog();

        // ---- tool tabs
        foreach (var (title, panel) in new (string, Control)[] { ("Heights", heightTools), ("Texture", textureTools), ("Metal", metalPanel), ("Items", itemsPanel), ("Colours", colourPanel), ("Assets", assetsPanel) })
        {
            var page = new TabPage(title);
            page.Controls.Add(panel);
            toolTabs.TabPages.Add(page);
        }

        beautifyPage.Controls.Add(beautifyPanel);

        // Docking runs in reverse order of addition: the middle, added first, takes what is left.
        assetsPage.Controls.Add(assetBrowser);
        logPage.Controls.Add(messages);
        bottomTabs.TabPages.AddRange([assetsPage, logPage]);
        bottomTabs.SelectedIndexChanged += (_, _) =>
        {
            if (bottomTabs.SelectedTab == logPage)
                MarkLogRead();
        };
        browserSplit.Panel1.Controls.Add(mapView);
        browserSplit.Panel2.Controls.Add(bottomTabs);
        mainSplit.Panel1.Controls.Add(browserSplit);
        mainSplit.Panel2.Controls.Add(toolTabs);
        Controls.Add(mainSplit);
        Controls.Add(status);
        Controls.Add(toolbar);
        Controls.Add(menu);
        MainMenuStrip = menu;
        BarTheme.Apply(this); // before the layout below: the theme's fonts set the strips' and tabs' sizes

        mapView.CursorMoved += (_, position) => ShowCursor(position);
        mapView.ViewChanged += (_, _) => ShowViewState();
        mapView.StrokeStarted += OnStrokeStarted;
        mapView.Dab += OnDab;
        mapView.StrokeEnded += (_, _) => OnStrokeEnded();
        heightTools.ToolChanged += (_, _) => OnHeightToolChanged();
        heightTools.RampChanged += (_, _) => UpdateRampPreview();
        heightTools.ApplyRamp += (_, _) => ApplyRamp();
        heightTools.RampTurned += (_, degrees) => TurnRampPath(degrees);
        heightTools.RampEndsSwapped += (_, _) => ReverseRampPath();
        heightTools.StraightenRamp += (_, _) => StraightenRamp();
        // The fixed panels' sizes, once docking has sized the split containers; they keep them as the window resizes.
        PerformLayout();
        FitToolTabs();
        browserSplit.SplitterDistance = Math.Max(browserSplit.Panel1MinSize,
            browserSplit.Height - Math.Clamp(settings.AssetBrowserHeight, 120, 900) - browserSplit.SplitterWidth);
        browserSplit.Panel2Collapsed = !(browserItem.Checked = settings.AssetBrowser);
        ShowBeautifyPanel(settings.BeautifyPanel, select: false);
        beautifyItem.CheckedChanged += (_, _) => { if (!syncing) ShowBeautifyPanel(beautifyItem.Checked, select: true); };
        beautifyButton.CheckedChanged += (_, _) => { if (!syncing) ShowBeautifyPanel(beautifyButton.Checked, select: true); };
        beautifyPanel.PaintRequested += async (_, _) => await BeautifyAsync(cliffs: false, texture: true);
        beautifyPanel.RoughenRequested += async (_, _) => await BeautifyAsync(cliffs: true, texture: false);
        beautifyPanel.BothRequested += async (_, _) => await BeautifyAsync(cliffs: true, texture: true);
        beautifyPanel.PresetChosen += async (_, _) => await ReadMapColoursAsync();
        beautifyPanel.LayerMenuRequested += (_, request) => ShowLayerMenu(request.Anchor, request.Import);
        InitWorldMachine();
        browserItem.CheckedChanged += (_, _) =>
        {
            browserSplit.Panel2Collapsed = !(settings.AssetBrowser = browserItem.Checked);
            settings.Save();
        };
        browserSplit.SplitterMoved += (_, _) =>
        {
            if (Quiet)
                return; // the self-test resizes things; the user's settings stay as they are
            settings.AssetBrowserHeight = browserSplit.Panel2.Height;
            settings.Save();
        };
        assetBrowser.CollectOnOpen = settings.CollectAssets;
        assetBrowser.CollectOnOpenChanged += (_, on) =>
        {
            settings.CollectAssets = on;
            settings.Save();
        };
        assetBrowser.SelectionChanged += (_, row) => ShowAssetDetails(row);
        assetBrowser.Picked += (_, _) => ShowTab("Assets"); // its details and what can be done with it, on the right
        assetBrowser.PlaceRequested += (_, row) =>
        {
            // An Experimental entry: a map's structure kind or new feature is placed as the Asset tab's Place places it;
            // a type, or the new-feature tool, has nothing to place yet.
            if (row.Tool is { Kind: string kind })
                PlaceStructureKind(kind, broken: false);
            else if (row.Tool is { Made: MadeFeature made })
                PlaceAsset(row.Asset, made.Name);
            else if (row.Tool == null)
                PlaceAsset(row.Asset, row.Feature?.Name);
        };
        assetBrowser.CollectRequested += async (_, _) => await CollectAssetsAsync();
        assetBrowser.CollectFilesRequested += async (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "Spring maps (*.sd7;*.sdz)|*.sd7;*.sdz",
                Title = "Collect assets from map files",
                Multiselect = true,
                InitialDirectory = settings.MapsFolder ?? "",
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                await CollectFromFilesAsync(dialog.FileNames);
        };
        assetsPanel.AddRequested += (_, asset) => AddAssetToMap(asset);
        assetsPanel.PlaceRequested += (_, row) => PlaceAsset(row.Asset, row.Tool?.Made?.Name ?? row.Feature?.Name);
        assetsPanel.CreateCustomRequested += (_, request) => CreateCustomFeature(request.Row, request.Name, request.Settings);
        // BAR's standard units (BarUnits beside the editor), for guardian towers' targets.
        try
        {
            unitCatalog = UnitCatalog.Shipped(AppContext.BaseDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            unitCatalog = null;
        }
        assetsPanel.Catalog = itemsPanel.Catalog = unitCatalog;
        // BAR's own features and the models of the ones maps use (BarFeatures beside the editor), to draw them.
        try
        {
            barFeatures = BarFeatureCatalog.Shipped(AppContext.BaseDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            barFeatures = null;
        }
        assetBrowser.BarFeatures = assetsPanel.BarFeatures = previews.BarFeatures = itemsPanel.BarFeatures = barFeatures;
        assetBrowser.Units = unitCatalog;
        assetsPanel.CreateStructureKindRequested += (_, request) =>
        {
            if (map != null && !busy && Edit(s => request.Type == StructureType.Guardian
                    ? s.AddGuardianKind(request.Name, request.Settings, request.Targets)
                    : s.AddStructureKind(request.Name, request.Type, request.Settings)))
            {
                ShowAssets();
                assetBrowser.Choose(ExperimentalTool.KindId(request.Name)); // the new kind's entry: its Place section
            }
        };
        assetsPanel.PlaceStructureRequested += (_, request) => PlaceStructureKind(request.Kind, request.Broken);
        assetsPanel.CustomModelChosen += (_, source) =>
        {
            var definition = MapEditSession.LibraryDefinition(Library, source.Asset, source.Name);
            assetsPanel.SetCustomDefaults(source, definition == null ? null : CustomFeatureSettings.FromDefinition(definition));
            if (assetBrowser.Selected is { Tool: ExperimentalTool tool } shown)
                ShowToolPreview(shown, tool);
        };
        assetsPanel.NewFeatureFromRequested += (_, _) => assetBrowser.Choose(ExperimentalTool.NewFeatureId);
        itemsPanel.GuardianRuleChanged += (_, request) =>
        {
            // One region's rule for the picked tower (one undo step); the panel shows what the map now has either way.
            if (map != null && !busy && CurrentStructure() is StructureInstance picked)
                Edit(s => s.SetGuardianRule(picked.X, picked.Z, request.Region, request.Rule));
            ShowItemSelection();
        };
        itemsPanel.GuardianZoneSettingsChanged += (_, request) =>
        {
            // A path already drawn takes the new distance and loop (one undo step); one still to be drawn starts with them.
            if (map != null && !busy && CurrentStructure() is { Zone: GuardianZone zone } picked &&
                (zone.Distance != request.Distance || zone.Closed != request.Closed))
                SetZone(picked, zone with { Distance = request.Distance, Closed = request.Closed && zone.Points.Count > 2 });
        };
        itemsPanel.GuardianZoneCleared += (_, _) =>
        {
            if (map != null && !busy && CurrentStructure() is { Zone: not null } picked)
                SetZone(picked, null);
        };
        itemsPanel.StructureChangeRequested += (_, request) =>
        {
            if (map != null && !busy && Edit(s => s.ChangeStructureKind(request.Kind, request.Settings)))
                ShowItemSelection();
        };
        itemsPanel.PlaceMarkersRequested += (_, _) =>
        {
            if (CurrentStructure() is not StructureInstance picked)
                return;
            itemsPanel.Mode = ItemsMode.Place;
            itemsPanel.FeatureName = MarkerPrefix + string.Create(CultureInfo.InvariantCulture, $"{picked.X},{picked.Z}");
            Log($"Place repair markers for the {picked.Kind} at ({picked.X}, {picked.Z}): click where each goes. Back in Select, drag one to move it.");
        };
        itemsPanel.StructureBrokenChanged += (_, broken) =>
        {
            if (map != null && !busy && CurrentStructure() is StructureInstance picked)
                Edit(s => s.SetStructureBroken(picked.X, picked.Z, broken));
            ShowItemSelection();
        };
        assetsPanel.MoveRequested += (_, move) =>
        {
            Library.SetCategory(move.Asset, move.Category);
            Log($"{move.Asset.Name} moved to {move.Category}");
            ShowAssets();
        };
        assetsPanel.RemoveRequested += (_, asset) =>
        {
            if (!Quiet && MessageBox.Show(this, $"Take {asset.Name} out of the asset library?\n\nMaps that already use it keep their own copy.", AppName,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            Library.Remove(asset);
            Log($"{asset.Name} taken out of the asset library");
            ShowAssets();
        };
        heightTools.AutoSources += (_, _) =>
        {
            (rampRockPick, rampDirtPick) = (null, null);
            UpdateRampPreview();
        };
        heightTools.BrushChanged += (_, _) => UpdateBrush();
        textureTools.ToolChanged += async (_, _) =>
        {
            UpdateBrush();
            await FollowTextureTool();
        };
        textureTools.BrushChanged += (_, _) => UpdateBrush();
        toolTabs.SelectedIndexChanged += (_, _) =>
        {
            UpdateBrush();
            if (ActiveTab == ToolTab.Metal && !mapView.Overlays.HasFlag(MapOverlays.MetalSpots))
            {
                mapView.Overlays |= MapOverlays.MetalSpots;
                SyncOverlays();
            }
            if (ActiveTab == ToolTab.Items && (mapView.Overlays & (MapOverlays.Items | MapOverlays.StartPositions)) != (MapOverlays.Items | MapOverlays.StartPositions))
            {
                mapView.Overlays |= MapOverlays.Items | MapOverlays.StartPositions;
                SyncOverlays();
            }
            UpdateRampPreview(); // shows or hides the ramp outline too
        };
        itemsPanel.ModeChanged += async (_, _) =>
        {
            UpdateBrush();
            await FollowItemsMode();
        };
        itemsPanel.BrushChanged += (_, _) => UpdateBrush();
        itemsPanel.HeadingChanged += (_, degrees) =>
        {
            if (CurrentStructure() != null)
                TurnStructureTo((int)Math.Round(degrees / 90) & 3);
            else
                EditSelectedItems((s, items) => s.SetItemHeading(items, degrees));
        };
        itemsPanel.TurnRequested += (_, degrees) => TurnSelectedItems(degrees);
        itemsPanel.RemoveRequested += (_, _) => RemoveSelectedItems();
        metalPanel.ModeChanged += (_, _) => UpdateBrush();
        metalPanel.SpotChosen += (_, index) => ChooseSpot(index);
        metalPanel.IncomeChanged += (_, income) => EditSelectedSpot((s, x, z) => s.SetSpotIncome(x, z, income));
        metalPanel.SpotSizeChanged += (_, cells) => EditSelectedSpot((s, x, z) => s.ResizeSpot(x, z, cells));
        metalPanel.RemoveRequested += (_, _) => DeleteSelectedSpot();
        mapView.PointerDown += OnPointerDown;
        mapView.PointerDrag += OnPointerDrag;
        mapView.PointerUp += OnPointerUp;
        mapView.PointerCancelled += (_, _) =>
        {
            CancelRampDrag();
            CancelSpotDrag();
            CancelItemDrag();
        };
        colourPanel.ColourChanged += (_, e) => Edit(s => s.SetColour(e.Path, e.R, e.G, e.B));
        heightTools.SeaLevelChanged += (_, _) =>
        {
            if (map == null)
                return;
            map.Renderer.WaterLevel = heightTools.SeaLevel;
            mapView.Redraw();
            terrainWindow?.View.MapChanged();
        };
        heightTools.ApplySeaLevel += (_, _) => ApplySeaLevel();
        heightTools.OffsetAll += (_, amount) => Edit(s => s.OffsetHeights(amount));
        heightTools.ScaleAll += (_, factor) => Edit(s => s.ScaleHeights(factor));
        ShowViewState();
        SyncLayer();
        SyncOverlays();
        UpdateCommands();
    }

    T NeedsMap<T>(T item) where T : ToolStripItem
    {
        needMap.Add(item);
        return item;
    }

    /// <summary>The map view, for saving it as an image.</summary>
    public MapViewControl MapView => mapView;

    /// <summary>For the self-test: errors are logged instead of shown, and "discard changes?" is answered yes.</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal bool Quiet { get; init; }

    internal OpenMap? Map => map;
    internal HeightToolPanel HeightTools => heightTools;
    internal TextureToolPanel TextureTools => textureTools;
    internal ColourPanel ColourPanel => colourPanel;
    internal MetalPanel MetalPanel => metalPanel;
    internal ItemsPanel ItemsPanel => itemsPanel;
    internal IReadOnlyList<MapItem> SelectedItems => map == null ? [] : ResolveSelection(map.Session.Items.All);

    /// <summary>Brings a tool tab to the front by its title.</summary>
    internal void ShowTab(string title)
    {
        _ = toolTabs.Handle; // a tab control without a window raises no SelectedIndexChanged
        toolTabs.SelectedTab = toolTabs.TabPages.Cast<TabPage>().Single(p => p.Text == title);
    }
    internal (double X, double Z)? SelectedSpot => selectedSpot;
    internal TabControl ToolTabs => toolTabs;
    internal IEnumerable<string> Messages => messages.Items.Cast<string>();
    internal string UndoText => undoItem.Text ?? "";

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ShowAssets(); // the library is in the browser from the start, before any map is opened
        await OfferRecoveryAsync();
        if (initialArchive != null)
            await OpenAsync(initialArchive);
    }

    // ---------------------------------------------------------------- recovery after a crash

    /// <summary>
    /// Looks for maps that were open when the editor last stopped without closing them, and offers to replay their
    /// unsaved edits. Records with nothing unsaved are cleared away quietly.
    /// </summary>
    async Task OfferRecoveryAsync()
    {
        IReadOnlyList<OrphanSession> orphans;
        try
        {
            orphans = SessionRecovery.FindOrphans(Workspaces.Root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"Could not look for unsaved work from last time: {ex.Message}");
            return;
        }
        foreach (var orphan in orphans)
        {
            if (orphan.Manifest is not SessionManifest m || orphan.UnsavedEdits == 0)
            {
                DiscardRecovery(orphan);
                continue;
            }
            string what = $"The editor stopped without saving {m.MapName ?? "a map"} ({Path.GetFileName(m.Archive)}): " +
                          $"{orphan.UnsavedEdits} edit{(orphan.UnsavedEdits == 1 ? "" : "s")} since it was last saved, " +
                          $"the last at {orphan.LastWriteUtc.ToLocalTime():g}.";
            if (map != null)
            {
                Log(what + " Kept for the next start, as a map is already open.");
                continue;
            }
            if (m.ArchiveProblem() is string problem)
            {
                Log(what + " " + problem);
                if (MessageBox.Show(this, what + "\n\nThey cannot be recovered: " + problem +
                        "\n\nDiscard them? (No keeps them, in case the map file comes back.)", AppName,
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                    DiscardRecovery(orphan);
                continue;
            }
            var choice = MessageBox.Show(this, what + "\n\nRecover them?\n\nYes: open the map with those edits (undo works as before).\n" +
                "No: discard them.\nCancel: decide next time.", AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.Yes)
                await RecoverAsync(orphan);
            else if (choice == DialogResult.No)
                DiscardRecovery(orphan);
        }
    }

    /// <summary>Opens the map an orphaned session started from and replays its journal onto it.</summary>
    internal async Task<bool> RecoverAsync(OrphanSession orphan)
    {
        if (busy || orphan.Manifest is not SessionManifest m || !ConfirmDiscard("recover another"))
            return false;
        CloseMap();
        OpenMap? opened = null;
        await RunBusy($"Recovering {m.MapName ?? Path.GetFileName(m.Archive)}",
            async report => opened = await Task.Run(() => LoadMap(m.Archive, report, recover: orphan, assets: Library)));
        if (opened == null)
            return false;
        notPacked = orphan.SavedButNotPacked; // its last Save As never reached the archive
        Attach(opened);
        Log($"Recovered {orphan.UnsavedEdits} unsaved edit{(orphan.UnsavedEdits == 1 ? "" : "s")} from {orphan.LastWriteUtc.ToLocalTime():g}; " +
            "use Save As to keep them.");
        return true;
    }

    internal void DiscardRecovery(OrphanSession orphan)
    {
        foreach (var dir in new[] { orphan.Workspace, orphan.Folder })
            if (Directory.Exists(dir) && Workspaces.Delete(dir) is string problem)
                Log(problem);
    }

    // ---------------------------------------------------------------- open, save, close

    void OpenWithDialog()
    {
        if (busy)
            return;
        using var dialog = new OpenFileDialog
        {
            Filter = "Spring maps (*.sd7;*.sdz)|*.sd7;*.sdz|All files (*.*)|*.*",
            Title = "Open a map",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _ = OpenAsync(dialog.FileName);
    }

    internal async Task OpenAsync(string archive)
    {
        if (busy || !ConfirmDiscard("open another map"))
            return;
        CloseMap();
        OpenMap? opened = null;
        var assets = Library;
        await RunBusy($"Opening {Path.GetFileName(archive)}", async report => opened = await Task.Run(() => LoadMap(archive, report, assets: assets)));
        if (opened == null)
            return;
        Attach(opened);
        if (settings.CollectAssets)
            await CollectAssetsAsync();
    }

    // ---------------------------------------------------------------- assets

    /// <summary>Adds the open map's assets to the library (in the background; the map isn't changed) and logs what was new.</summary>
    internal async Task<CollectReport?> CollectAssetsAsync()
    {
        if (map == null || busy)
            return null;
        var s = map.Session;
        var assets = Library;
        CollectReport? report = null;
        await RunBusy("Collecting the map's assets", _ => Task.Run(() => report = assets.Collect(s.Package.Root, s.Smf, s.Info, s.Items.Placer)));
        if (report != null)
        {
            Log($"Asset library: {report}");
            foreach (var note in report.Notes)
                Log("Asset library note: " + note);
        }
        ShowAssets();
        return report;
    }

    /// <summary>
    /// Adds the assets of map files without opening them in the editor: each is unpacked to a temporary folder,
    /// collected and the folder deleted. Files that can't be read are logged and skipped.
    /// </summary>
    internal async Task CollectFromFilesAsync(IReadOnlyList<string> archives)
    {
        if (busy)
            return;
        var assets = Library;
        var lines = new List<string>();
        await RunBusy("Collecting assets from map files", report => Task.Run(() =>
        {
            for (int i = 0; i < archives.Count; i++)
            {
                var archive = archives[i];
                report.Report($"Collecting {Path.GetFileName(archive)} ({i + 1} of {archives.Count})…");
                var temp = FileSystemUtil.NewTempDir("collect");
                try
                {
                    var package = MapPackage.Open(archive, Path.Combine(temp, "map"));
                    var result = assets.Collect(package.Root, package.LoadSmf(), package.LoadMapInfo(), FeaturePlacerConfig.Load(package.Root));
                    lock (lines)
                        lines.Add($"Asset library, {Path.GetFileName(archive)}: {result}");
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException
                                               or NotSupportedException or MapCore.Lua.LuaSyntaxException)
                {
                    lock (lines)
                        lines.Add($"Asset library: {Path.GetFileName(archive)} skipped: {ex.Message}");
                }
                finally
                {
                    try
                    {
                        FileSystemUtil.DeleteTree(temp);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                    }
                }
            }
        }));
        foreach (var line in lines)
            Log(line);
        ShowAssets();
    }

    /// <summary>
    /// Shows the library in the asset browser, and the chosen asset on the right. Which assets the map has is worked
    /// out again when the library changed (<paramref name="libraryChanged"/>) or the map's own files did.
    /// </summary>
    void ShowAssets(bool libraryChanged = true)
    {
        if (map == null)
            assetsHere = null;
        else
        {
            features.Get(map.Session);
            if (libraryChanged || assetsHere == null || assetsHereVersion != features.Version)
            {
                assetsHere = map.Session.AssetsPresent(Library.Entries);
                assetsHereVersion = features.Version;
            }
        }
        var here = assetsHere ?? [];
        // The library's models, for the new-feature tool and for structures made from assets.
        assetsPanel.SetModelChoices(Library.Entries.Where(e => e.HasFiles)
            .SelectMany(e => e.Features.Where(f => f.Model != null).Select(f => new AssetRow(e, f)))
            .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList());
        ShowStructureKinds();
        assetBrowser.ShowLibrary(Library, a => here.Contains(a.Id), map != null);
        ShowAssetDetails(assetBrowser.Selected);
    }

    /// <summary>Items › Place takes this prefix and a structure kind to place a door or tunnel.</summary>
    const string StructurePrefix = "structure:";

    /// <summary>Items › Place takes this prefix and a structure's centre (x,z) to place repair markers for it.</summary>
    const string MarkerPrefix = "repair-marker:";
    StructureInstance? selectedStructure;

    /// <summary>A picked repair marker (where it stands); its structure is <see cref="selectedStructure"/>.</summary>
    (double X, double Z)? selectedMarker;
    internal (double X, double Z)? SelectedMarker => selectedMarker;
    internal StructureInstance? SelectedStructure => CurrentStructure();

    /// <summary>Whether a door or tunnel placed from the Asset tab starts the game destroyed.</summary>
    bool placeStructureBroken;

    /// <summary>The picked door or tunnel as the map now has it (found again by place: an edit makes a new record), or null when it is gone.</summary>
    StructureInstance? CurrentStructure()
    {
        if (map == null || selectedStructure is not StructureInstance picked)
            return null;
        var structures = map.Session.Structures;
        selectedStructure = structures.Instances.FirstOrDefault(i => i.X == picked.X && i.Z == picked.Z && i.Kind == picked.Kind && structures.Kinds.ContainsKey(i.Kind));
        return selectedStructure;
    }

    /// <summary>A picked door or tunnel in Items › Selection: what it is, its kind's size and look, and the whole of it in the preview.</summary>
    void ShowStructureSelection()
    {
        var picked = CurrentStructure();
        var structures = map?.Session.Structures;
        if (picked == null || structures == null || !structures.Kinds.TryGetValue(picked.Kind, out var kind))
        {
            itemsPanel.ShowStructure(null, null, null, 0);
            return;
        }
        var s = map!.Session;
        var settings = s.KindSettings(kind);
        if (selectedMarker is var (mx, mz) && !picked.MarkerList.Contains((mx, mz)))
            selectedMarker = null; // gone (removed, or undone)
        // A built kind made by an earlier editor lacks some parts it now has: offer to rebuild it.
        bool partsMissing = kind.Look is StructureLook.Concrete or StructureLook.Metal &&
            (kind.Type switch
            {
                StructureType.Pump => new[] { kind.Centre, kind.Beam, kind.Debris },
                StructureType.ShieldPad => [kind.Centre, kind.Debris],
                StructureType.Guardian => [kind.Centre], // a tower is its centre alone (it has no walls to find)
                _ => [kind.Wall, kind.Centre, kind.Repair, kind.Marker, kind.Debris],
            })
            .OfType<string>().Any(f =>
                !File.Exists(Path.Combine(s.Package.Root, "features", f + ".lua")) || !File.Exists(Path.Combine(s.Package.Root, "objects3d", f + ".s3o")));
        itemsPanel.ShowStructure(picked, kind, settings, structures.Instances.Count(i => i.Kind == kind.Name), selectedMarker, partsMissing);
        features.Get(s); // notices models built again
        var key = $"{features.Version}|structure|{picked.Kind}|{picked.Broken}|{picked.MarkerList.Count}|{settings.Encode()}";
        if (key == itemPreviewKey)
            return;
        itemPreviewKey = key;
        var result = previews.ForStructure(kind, picked, s, features, library ?? Library);
        if (result.Model != null)
            itemsPanel.Preview.ShowModel(result.Model, result.Text);
        else
            itemsPanel.Preview.ShowMessage(result.Text);
    }

    /// <summary>Brings the Items › Place name list up to date (assets added or undone), with the map's structure kinds.</summary>
    void RefreshItemNames()
    {
        if (map == null)
            return;
        var s = map.Session;
        var (definitions, _) = features.Get(s);
        var kinds = s.Structures.Kinds.Keys;
        itemsPanel.SetNames(definitions.Select(d => d.Name).Concat(s.Items.All.Select(i => i.Name)).Concat(kinds.Select(k => StructurePrefix + k))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        ShowStructureKinds();
    }

    /// <summary>Shows the asset chosen in the browser on the right: its details, and its model in the preview.</summary>
    void ShowAssetDetails(AssetRow? row)
    {
        bool here = row != null && (assetsHere?.Contains(row.Asset.Id) ?? false);
        assetsPanel.ShowAsset(row, here, map != null, Library.Entries.Count == 0);
        if (row == previewRow && row != null)
            return; // the same model: keep the way it is turned
        previewRow = row;
        if (row?.Tool is ExperimentalTool tool)
        {
            ShowToolPreview(row, tool);
            return;
        }
        // An asset with a model becomes the new-feature tool's model (until another is chosen there).
        var definition = row?.Feature?.Model != null && row.Asset.HasFiles ? MapEditSession.LibraryDefinition(Library, row.Asset, row.Name) : null;
        if (definition != null)
            assetsPanel.SetCustomDefaults(row, CustomFeatureSettings.FromDefinition(definition));
        if (row == null)
        {
            assetsPanel.Preview.ShowMessage(Library.Entries.Count > 0 ? "Choose an asset in the browser below the map to see its model." : "");
            return;
        }
        var result = previews.ForAsset(Library, row.Asset, row.Feature);
        if (result.Model != null)
            assetsPanel.Preview.ShowModel(result.Model, result.Text);
        else
            assetsPanel.Preview.ShowMessage(result.Text);
    }

    /// <summary>
    /// An Experimental entry's model: a structure type as a new kind of it starts, a map's kind as it stands (made from
    /// its own files), the model the new-feature tool starts from, or a new feature's model in the map.
    /// </summary>
    void ShowToolPreview(AssetRow row, ExperimentalTool tool)
    {
        var box = assetsPanel.Preview;
        if (tool.Made is MadeFeature made)
        {
            var result = map == null ? new ModelPreviews.Result(null, $"{made.Name} isn't in the open map.")
                : previews.ForPlaced(made.Name, PlacedItemKind.Feature, map.Session, features, library ?? Library);
            if (result.Model != null)
                box.ShowModel(result.Model, result.Text + $" · made from {made.MadeFrom}'s model");
            else
                box.ShowMessage(result.Text);
            return;
        }
        if (tool.NewFeature)
        {
            if (assetsPanel.CustomSource is AssetRow source && previews.ForAsset(Library, source.Asset, source.Feature) is { Model: ModelPreview model } shown)
                box.ShowModel(model, shown.Text + " · the model it starts from");
            else
                box.ShowMessage("Choose the model the new feature uses (below).");
            return;
        }
        if (tool.Kind is string kind)
        {
            if (map?.Session.Structures.Kinds.TryGetValue(kind, out var placing) != true)
            {
                box.ShowMessage($"{kind} isn't in the open map.");
                return;
            }
            var result = previews.ForStructure(placing!, new StructureInstance(kind, 0, 0, 0), map!.Session, features, library ?? Library);
            if (result.Model != null)
                box.ShowModel(result.Model, result.Text);
            else
                box.ShowMessage(result.Text);
            return;
        }
        if (tool.Type is StructureType type)
            box.ShowModel(StructurePreviews.For(type), type is StructureType.Door or StructureType.Tunnel or StructureType.OpenTunnel
                ? $"{row.Name} · as a new kind starts (concrete)" : $"{row.Name} · as a new kind starts");
    }

    /// <summary>
    /// The map's structure kinds (in the Asset tab's list to place) and the new features made with the editor it has:
    /// both under Experimental in the browser, with how many are placed.
    /// </summary>
    void ShowStructureKinds()
    {
        var s = map?.Session;
        var structures = s?.Structures;
        assetsPanel.ShowStructureKinds(structures?.Kinds.Keys ?? (IEnumerable<string>)[]);
        if (s == null)
            madeFeatures = [];
        else
        {
            features.Get(s); // notices a feature file written or undone
            if (madeFeaturesOf != (s.Package.Root, features.Version))
            {
                madeFeatures = s.MadeFeatures();
                madeFeaturesOf = (s.Package.Root, features.Version);
            }
        }
        var placed = s?.Items.All.Where(i => i.Kind == PlacedItemKind.Feature).GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        assetBrowser.ShowMapsOwn(structures == null ? []
                : structures.Kinds.Values.Select(k => (k.Name, k.Type, structures.Instances.Count(i => i.Kind == k.Name))).ToList(),
            madeFeatures.Select(f => (f, placed?.GetValueOrDefault(f.Name) ?? 0)).ToList(), s?.Package.Root);
    }

    /// <summary>The open map's new features made with the editor, read again only when its feature files change.</summary>
    IReadOnlyList<MadeFeature> madeFeatures = [];
    (string Root, int Version)? madeFeaturesOf;

    /// <summary>Picks a structure kind in Items › Place (standing, or destroyed for a door or tunnel), and says how it is placed.</summary>
    void PlaceStructureKind(string kind, bool broken)
    {
        ShowTab("Items");
        itemsPanel.Mode = ItemsMode.Place;
        itemsPanel.PlaceKind = PlacedItemKind.Feature;
        itemsPanel.FeatureName = StructurePrefix + kind;
        StructureKind? placing = null;
        map?.Session.Structures.Kinds.TryGetValue(kind, out placing);
        placeStructureBroken = broken && placing?.Repair != null; // only a door or tunnel can start destroyed
        Log(placing?.Type switch
        {
            StructureType.Guardian => $"Place {kind}: click the map where the tower goes. Then, in Select, pick it and Shift+click to draw the path it guards.",
            StructureType.ShieldPad => $"Place {kind}: click the map where the platform goes. It starts derelict: whoever repairs it owns it, and their units get shields driving off it.",
            StructureType.Pump => $"Place {kind}: click the map where the pump goes. It starts derelict: whoever repairs it owns it.",
            _ => $"Place {kind}{(broken ? " (starting destroyed)" : "")}: click the map where its centre goes. Its heading (snapped to a quarter turn) is the way the passage runs.",
        });
    }

    /// <summary>Shows the selected item's model in the Items tab (when the selection is all one kind of item).</summary>
    void ShowItemPreview(IReadOnlyList<MapItem> items)
    {
        var names = items.Select(i => i.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var box = itemsPanel.Preview;
        if (map == null || items.Count == 0)
        {
            itemPreviewKey = null;
            box.ShowMessage("Select an item to see its model.");
            return;
        }
        if (names.Count > 1)
        {
            itemPreviewKey = null;
            box.ShowMessage($"{items.Count} items of {names.Count} kinds: select one kind to see its model.");
            return;
        }
        features.Get(map.Session); // notices an asset added or undone
        var key = $"{features.Version}|{items[0].Kind}|{names[0]}";
        if (key == itemPreviewKey)
            return; // the same model as shown: keep the way it is turned
        itemPreviewKey = key;
        var result = previews.ForPlaced(names[0], items[0].Kind, map.Session, features, library ?? Library);
        if (result.Model != null)
            box.ShowModel(result.Model, result.Text);
        else
            box.ShowMessage(result.Text);
    }

    internal bool AddAssetToMap(AssetEntry asset)
    {
        bool done = Edit(s => s.AddAsset(asset));
        ShowAssets();
        return done;
    }

    /// <summary>
    /// Experimental: writes a new feature using the asset's model with the given settings (one undo step; the model
    /// is copied from the library if the map lacks it), chooses its entry under Experimental, then picks it in Items › Place.
    /// </summary>
    internal bool CreateCustomFeature(AssetRow row, string name, CustomFeatureSettings settings)
    {
        if (map == null || busy)
            return false;
        if (!Edit(s => s.AddCustomFeature(name, row.Name, settings)))
            return false;
        ShowAssets();
        assetBrowser.Choose(ExperimentalTool.MadeId(name));
        ShowTab("Items");
        itemsPanel.Mode = ItemsMode.Place;
        itemsPanel.PlaceKind = PlacedItemKind.Feature;
        itemsPanel.FeatureName = name;
        Log($"Place {name}: click the map where it goes.");
        return true;
    }

    /// <summary>Adds the asset if the map lacks it, then picks the feature (its first, unless named) in Items › Place.</summary>
    internal bool PlaceAsset(AssetEntry asset, string? feature = null)
    {
        if (map == null || busy)
            return false;
        if (asset.HasFiles && !map.Session.HasAsset(asset) && !AddAssetToMap(asset))
            return false;
        var kind = asset.Kind switch
        {
            AssetKind.Unit => PlacedItemKind.Unit,
            AssetKind.Building => PlacedItemKind.Building,
            _ => PlacedItemKind.Feature,
        };
        if (kind != PlacedItemKind.Feature && map.Session.Items.Placer == null)
        {
            Log($"{asset.Name} is a {kind.ToString().ToLowerInvariant()}: this map has no featureplacer to place it with.");
            return false;
        }
        var name = feature ?? asset.Features.FirstOrDefault()?.Name ?? asset.Name;
        ShowTab("Items");
        itemsPanel.Mode = ItemsMode.Place;
        itemsPanel.PlaceKind = kind;
        itemsPanel.FeatureName = name;
        Log($"Place {name}: click the map where it goes.");
        return true;
    }

    /// <summary>
    /// Extracts and loads a map, and prepares the texture and height views. Runs off the UI thread.
    /// With <paramref name="keepRecord"/>, every edit is journaled beside the workspace for recovery after a crash;
    /// with <paramref name="recover"/>, that orphaned session's workspace is refilled and its journal replayed first.
    /// </summary>
    public static OpenMap LoadMap(string archive, IProgress<string>? report, bool keepRecord = true, OrphanSession? recover = null, AssetLibrary? assets = null)
    {
        var workspace = recover?.Workspace ?? Workspaces.Create();
        SessionRecord? record = null;
        try
        {
            if (recover != null && Directory.Exists(workspace))
            {
                report?.Report("Clearing the old working copy…");
                if (Workspaces.Delete(workspace) is string problem)
                    throw new IOException(problem);
            }
            report?.Report("Extracting the archive…");
            var package = MapPackage.Open(archive, workspace);
            report?.Report("Reading the map…");
            var session = MapEditSession.Open(package);
            session.Assets = assets; // before any replay: asset-add copies from it
            if (recover != null)
            {
                record = SessionRecord.Resume(recover);
                SessionJournal.Replay(session, recover.Entries, report);
                session.Journal = record.Journal;
                session.EndGroup(); // a stroke the crash cut short ends here
            }
            else if (keepRecord)
            {
                record = SessionRecord.Start(workspace, SessionManifest.For(archive, session.Info.Name, session.Info.Version));
                session.Journal = record.Journal;
            }
            report?.Report("Decoding the texture…");
            _ = session.TextureImage;
            report?.Report("Preparing the views…");
            var renderer = new MapRenderer(session);
            renderer.Prepare(MapViewLayer.Texture);
            renderer.Prepare(MapViewLayer.Heights);
            return new OpenMap(session, renderer, workspace, Path.GetFullPath(archive), record);
        }
        catch
        {
            record?.Dispose();
            if (recover == null) // a failed recovery keeps its record, to try again
            {
                Workspaces.Delete(workspace);
                if (Directory.Exists(SessionRecord.FolderFor(workspace)))
                    Workspaces.Delete(SessionRecord.FolderFor(workspace));
            }
            throw;
        }
    }

    void Attach(OpenMap opened)
    {
        map = opened;
        var s = opened.Session;
        s.Changed += OnSessionChanged;
        mapView.Attach(s, opened.Renderer);
        mapView.Focus();
        Log($"Opened {s.Info.Name} {s.Info.Version} ({Path.GetFileName(opened.Archive)}): " +
            $"{s.Smf.MapX / 64}x{s.Smf.MapY / 64} map units, heights {s.Heights.MinHeight} to {s.Heights.MaxHeight}, " +
            $"{s.Metal.FindSpots().Count} metal spots, {s.Items.All.Count} placed items");
        foreach (var warning in s.Metal.Warnings.Concat(s.Smf.Warnings))
            Log("Note: " + warning);
        foreach (var (layer, _) in LayerNames)
            if (opened.Renderer.Unavailable(layer) is string why)
                layerItems[layer].ToolTipText = why;
        if (opened.Renderer.Unavailable(mapView.Layer) != null)
            mapView.Layer = MapViewLayer.Texture;
        colourPanel.ShowValues(s);
        textureTools.SetSplatChannelNames(s.SplatChannelNames);
        metalPanel.ShowWarnings(s.Metal.Warnings, s.Metal.ReadOnlyReason != null);
        var (definitions, _) = features.Get(s);
        var names = definitions.Select(d => d.Name).Concat(s.Items.All.Select(i => i.Name)).Concat(s.Structures.Kinds.Keys.Select(k => StructurePrefix + k))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        assetsPanel.ShowStructureKinds(s.Structures.Kinds.Keys);
        selectedStructure = null;
        var startBoxes = File.Exists(Path.Combine(s.Package.Root, "mapconfig", "map_startboxes.lua")) ? "mapconfig/map_startboxes.lua" : null;
        itemsPanel.ShowMap(names, s.Items.Placer != null, startBoxes);
        selectedItems = [];
        ShowItemSelection();
        ShowAssets();
        luaLayoutConfirmed = false;
        selectedSpot = null;
        RefreshSpots();
        SyncLayer();
        ApplySymmetry(announce: symmetryBox.SelectedIndex != 0); // the toolbar's, set after any recovery (whose replay never mirrors)
        ShowWorldMachine();
        WatchWorldMachine();
        UpdateTitle();
        UpdateCommands();
    }

    internal BeautifyPanel BeautifyPanel => beautifyPanel;

    /// <summary>Whether the Beautify panel is open (its tab beside the tools).</summary>
    internal bool BeautifyOpen => toolTabs.TabPages.Contains(beautifyPage);

    /// <summary>Opens or closes the Beautify panel (a tab beside the tools), remembering the choice; <paramref name="select"/> brings it to the front.</summary>
    internal void ShowBeautifyPanel(bool open, bool select)
    {
        syncing = true;
        beautifyItem.Checked = beautifyButton.Checked = open;
        syncing = false;
        if (open && !toolTabs.TabPages.Contains(beautifyPage))
            toolTabs.TabPages.Add(beautifyPage);
        else if (!open && toolTabs.TabPages.Contains(beautifyPage))
            toolTabs.TabPages.Remove(beautifyPage);
        FitToolTabs(); // wider with the seventh tab
        ((BarTabControl)toolTabs).FitTabs(); // seven tabs share the strip, or six
        if (open && select)
            toolTabs.SelectedTab = beautifyPage;
        if (settings.BeautifyPanel != open && !Quiet)
        {
            settings.BeautifyPanel = open;
            settings.Save();
        }
    }

    /// <summary>
    /// Beautify: the cliffs roughened and the texture painted by the terrain (either or both, the cliffs first), as one
    /// undo step, in the background. With "From this map", the map's own colours are read first (once a map).
    /// </summary>
    internal async Task<bool> BeautifyAsync(bool cliffs, bool texture)
    {
        if (map == null || busy)
            return false;
        var s = map.Session;
        var look = beautifyPanel.Settings with { Mirror = s.Mirror };
        var shape = beautifyPanel.CliffSettings with { Mirror = s.Mirror };
        bool readColours = texture && beautifyPanel.FromThisMap && coloursReadFrom != s;
        string name = cliffs && texture ? "beautify the map" : cliffs ? "roughen the cliffs" : "paint the texture by its terrain";
        var notes = new List<string>();
        BeautifySettings? read = null;
        bool done = await RunBusy(cliffs && texture ? "Beautifying the map" : cliffs ? "Roughening the cliffs" : "Painting the texture", progress => Task.Run(() =>
        {
            s.BeginGroup(name);
            try
            {
                if (cliffs)
                {
                    progress.Report("Roughening the cliffs…");
                    notes.Add(s.BeautifyHeights(shape));
                }
                if (texture)
                {
                    if (readColours)
                    {
                        progress.Report("Reading the map's own colours…");
                        read = s.SuggestBeautify(look);
                        look = look with { Rock = read.Rock, Dirt = read.Dirt, Sand = read.Sand, Mud = read.Mud, Seabed = read.Seabed, Ground = read.Ground };
                    }
                    progress.Report("Painting the texture…");
                    notes.Add(s.BeautifyTexture(look));
                }
            }
            finally
            {
                s.EndGroup();
            }
        }));
        if (read != null)
        {
            beautifyPanel.SetColours(read);
            coloursReadFrom = s;
        }
        foreach (var n in notes)
            Log(n);
        beautifyPanel.ShowNote(done ? string.Join(Environment.NewLine, notes.Select(n => char.ToUpperInvariant(n[0]) + n[1..] + ".")) + " Ctrl+Z takes it all back." : "");
        UpdateTitle();
        UpdateCommands();
        return done;
    }

    /// <summary>"From this map": shows the open map's own colours in the Beautify panel.</summary>
    async Task ReadMapColoursAsync()
    {
        if (map == null || busy)
            return;
        var s = map.Session;
        var current = beautifyPanel.Settings;
        BeautifySettings? read = null;
        if (await RunBusy("Reading the map's own colours", _ => Task.Run(() => read = s.SuggestBeautify(current))) && read != null)
        {
            beautifyPanel.SetColours(read);
            coloursReadFrom = s;
        }
    }

    /// <summary>The layers to import or export, as a menu under a button (the File menu's, for the Beautify panel).</summary>
    void ShowLayerMenu(Control anchor, bool import)
    {
        var menu = new ContextMenuStrip();
        foreach (var (text, layer, fileName, needs) in ImageLayers)
        {
            bool available = map != null && !busy && (needs is not MapViewLayer v || map.Renderer.Unavailable(v) == null);
            menu.Items.Add(new ToolStripMenuItem(text.Replace("&", ""), null, async (_, _) =>
            {
                if (import)
                    await ImportWithDialogAsync(layer);
                else
                    await ExportWithDialogAsync(layer, fileName);
            }) { Enabled = available });
        }
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    /// <summary>The symmetry chosen on the toolbar: none, a mirror across the axis chosen, or mirrored and flipped (a half turn).</summary>
    MirrorKind? ChosenSymmetry => symmetryBox.SelectedIndex switch
    {
        1 => Axes[Math.Max(0, axisBox.SelectedIndex)].Kind,
        2 => MirrorKind.HalfTurn,
        _ => null,
    };

    /// <summary>The open map's symmetry (null: none).</summary>
    internal MirrorKind? Symmetry => map?.Session.Mirror;

    /// <summary>Chooses a symmetry on the toolbar, as the user would (for the self-test).</summary>
    internal void SetSymmetry(MirrorKind? kind)
    {
        syncing = true;
        symmetryBox.SelectedIndex = kind == null ? 0 : kind == MirrorKind.HalfTurn ? 2 : 1;
        if (kind is MirrorKind k && k != MirrorKind.HalfTurn)
            axisBox.SelectedIndex = Array.FindIndex(Axes, a => a.Kind == k);
        syncing = false;
        ApplySymmetry(announce: true);
    }

    /// <summary>
    /// Makes the toolbar's symmetry the open map's, and draws its line on the map. A diagonal on a map that isn't
    /// square can't be: symmetry goes off, saying why.
    /// </summary>
    void ApplySymmetry(bool announce)
    {
        axisBox.Enabled = symmetryBox.SelectedIndex == 1; // a half turn is the same whichever axis
        var kind = ChosenSymmetry;
        if (map == null)
        {
            mapView.Mirror = null;
            return;
        }
        var s = map.Session;
        if (kind is MirrorKind wanted && !MapMirror.Fits(wanted, s.MapWidth, s.MapHeight))
        {
            Log($"A diagonal mirror needs a square map, and this one is {s.Smf.MapX / 64}x{s.Smf.MapY / 64}: symmetry is off.");
            syncing = true;
            symmetryBox.SelectedIndex = 0;
            syncing = false;
            axisBox.Enabled = false;
            kind = null;
        }
        if (s.Mirror == kind && mapView.Mirror?.Kind == kind)
            return;
        s.Mirror = kind;
        mapView.Mirror = kind is MirrorKind on ? new MapMirror(on, s.MapWidth, s.MapHeight) : null;
        if (announce)
            Log(kind switch
            {
                MirrorKind.LeftRight => "Symmetry: each edit is made again mirrored across the line down the middle (left and right), as one undo step.",
                MirrorKind.TopBottom => "Symmetry: each edit is made again mirrored across the line across the middle (top and bottom), as one undo step.",
                MirrorKind.Diagonal => "Symmetry: each edit is made again mirrored across the diagonal from the top-left corner, as one undo step.",
                MirrorKind.AntiDiagonal => "Symmetry: each edit is made again mirrored across the diagonal from the top-right corner, as one undo step.",
                MirrorKind.HalfTurn => "Symmetry: each edit is made again at the opposite side and corner, turned half round (mirrored and flipped), as one undo step.",
                _ => "Symmetry off: edits are made once.",
            });
    }

    async Task SaveAsAsync()
    {
        if (map == null || busy)
            return;
        var s = map.Session;
        using var dialog = new SaveAsDialog(s.Info.Name, s.Info.Version, s.LoadedName, s.LoadedVersion, map.Archive, savedAs,
            settings.MapsFolder ?? MapInstaller.CandidateFolders().FirstOrDefault(), settings.CopyToMaps);
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        var path = Path.GetFullPath(dialog.ArchivePath);
        bool overwrite = false;
        if (File.Exists(path))
        {
            if (MessageBox.Show(this, $"{path} already exists.\n\nReplace it?", "Save As", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            overwrite = true;
        }
        settings.CopyToMaps = dialog.CopyTo != null;
        if (dialog.MapsFolder.Length > 0)
            settings.MapsFolder = dialog.MapsFolder;
        settings.Save();
        await SaveAsAsync(dialog.MapName, dialog.Version, path, overwrite, dialog.CopyTo);
    }

    /// <summary>
    /// Sets the name and version (one undo step), saves the workspace and packs it into <paramref name="path"/>;
    /// then, with <paramref name="copyTo"/>, copies the archive into that maps folder (see <see cref="CopyToMapsAsync"/>).
    /// </summary>
    internal async Task<bool> SaveAsAsync(string name, string version, string path, bool overwrite, string? copyTo = null)
    {
        if (map == null || busy)
            return false;
        var s = map.Session;
        if (SaveAsDialog.Check(name, version, s.LoadedName, s.LoadedVersion, path, map.Archive) is string refused)
        {
            ShowError("Save As refused", new InvalidOperationException(refused));
            return false;
        }
        // Checks: errors stop the save, warnings ask first, notes go to the log.
        var checks = s.CheckBeforeSave(checkName: false);
        foreach (var check in checks)
            Log($"{check.Level}: {check.Message}");
        var errors = checks.Where(c => c.Level == CheckLevel.Error).Select(c => c.Message).ToList();
        if (errors.Count > 0)
        {
            ShowError("Not saved", new InvalidOperationException(string.Join(Environment.NewLine, errors)));
            return false;
        }
        var warnings = checks.Where(c => c.Level == CheckLevel.Warning).Select(c => "• " + c.Message).ToList();
        string paragraph = Environment.NewLine + Environment.NewLine;
        if (warnings.Count > 0 && !Quiet &&
            MessageBox.Show(this, "Before saving:" + paragraph + string.Join(paragraph, warnings) + paragraph + "Save anyway?", "Save As",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1) != DialogResult.Yes)
            return false;
        try
        {
            s.BeginGroup("name and version");
            if (name != (s.Info.Name ?? ""))
                s.Apply($"set:name=\"{name}\"");
            if (version != (s.Info.Version ?? ""))
                s.Apply($"set:version=\"{version}\"");
        }
        catch (InvalidOperationException ex)
        {
            ShowError("The name or version could not be set", ex);
            return false;
        }
        finally
        {
            s.EndGroup();
        }

        IReadOnlyList<string> notes = [];
        bool saved = await RunBusy($"Saving {Path.GetFileName(path)}", report => Task.Run(() =>
        {
            report.Report("Writing the map files…");
            notes = s.Save();
            notPacked = true;
            report.Report("Packing the archive…");
            s.Package.SaveAs(path, overwrite);
            notPacked = false;
            s.Journal?.Write(new JournalEntry("packed", path)); // from here a crash loses nothing
        }));
        foreach (var note in notes)
            Log(note);
        UpdateTitle();
        if (!saved)
            return false;
        Log($"Saved {s.Info.Name} {s.Info.Version} as {path}");
        savedAs = path;
        UpdateTitle();
        if (copyTo != null)
            await CopyToMapsAsync(path, copyTo, s.Info.Name, s.Info.Version);
        return true;
    }

    /// <summary>
    /// Copies a saved archive into BAR's maps folder: first looks there for another archive with the same name and
    /// version (BAR would list two maps under one name), and never replaces a file without asking.
    /// Returns where it was copied, or null.
    /// </summary>
    internal async Task<string?> CopyToMapsAsync(string archive, string folder, string? name, string? version)
    {
        var target = Path.Combine(folder, Path.GetFileName(archive));
        IReadOnlyList<string> same = [];
        if (!await RunBusy("Checking BAR's maps folder", report => Task.Run(() => same = MapInstaller.FindSameMap(folder, name, version, except: target, report))))
            return null;
        if (same.Count > 0)
        {
            string list = string.Join(", ", same.Select(Path.GetFileName));
            Log($"BAR's maps folder already has \"{name} {version}\": {list}");
            if (Quiet || MessageBox.Show(this, $"BAR's maps folder already has a map called \"{name} {version}\" ({list}). BAR would list two maps under that name." +
                    Environment.NewLine + Environment.NewLine + "Copy anyway?", "Copy into BAR's maps folder", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                Log("Not copied into the maps folder.");
                return null;
            }
        }
        bool replace = false;
        if (File.Exists(target) && !MapCore.FileSystemUtil.SamePath(target, archive))
        {
            if (Quiet || MessageBox.Show(this, $"{target} already exists." + Environment.NewLine + Environment.NewLine + "Replace it?",
                    "Copy into BAR's maps folder", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                Log($"Not copied: {target} already exists.");
                return null;
            }
            replace = true;
        }
        string? copied = null;
        if (!await RunBusy("Copying into BAR's maps folder", _ => Task.Run(() => copied = MapInstaller.Install(archive, folder, replace))))
            return null;
        Log($"Copied into BAR's maps folder: {copied}");
        return copied;
    }

    void CloseWithConfirm()
    {
        if (!busy && ConfirmDiscard("close it"))
            CloseMap();
    }

    /// <param name="keepRecord">Leave the workspace and its recovery record behind, as a crash would (for tests).</param>
    internal void CloseMap(bool keepRecord = false)
    {
        if (map == null)
            return;
        if (terrainWindow != null) // its renderer reads the session, so it goes first
        {
            var window = terrainWindow;
            terrainWindow = null;
            window.Close(); // a window never shown raises no FormClosed, so clean up here
            window.Dispose();
            terrainView?.Dispose();
            terrainView = null;
        }
        map.Session.Changed -= OnSessionChanged;
        map.Session.Journal = null;
        map.Record?.Dispose();
        map.Renderer.Dispose();
        mapView.Detach();
        if (!keepRecord)
            foreach (var dir in new[] { map.Workspace, SessionRecord.FolderFor(map.Workspace) })
                if (Directory.Exists(dir) && Workspaces.Delete(dir) is string problem)
                    Log(problem);
        map = null;
        wmWatcher?.Dispose();
        wmWatcher = null;
        wmPending.Clear();
        wmBuildStarted = null;
        ShowWorldMachine();
        notPacked = false;
        savedAs = null;
        heightTools.SeaLevel = 0;
        colourPanel.ShowValues(null);
        rampCentre = null;
        rampPoints = null;
        rampDrag = RampDrag.None;
        (rampRockPick, rampDirtPick) = (null, null);
        selectedSpot = null;
        draggedSpot = null;
        dropGhost = null;
        RefreshSpots();
        selectedItems = [];
        itemDrag = ItemDrag.None;
        ShowItemSelection();
        if (library != null)
            ShowAssets(); // nothing is "in this map" now
        ShowCursor(null);
        UpdateTitle();
        UpdateCommands();
    }

    bool HasUnpackedChanges => map != null && (map.Session.HasUnsavedChanges || notPacked);

    bool ConfirmDiscard(string action)
    {
        if (map == null || !HasUnpackedChanges || Quiet)
            return true;
        return MessageBox.Show(this, $"{map.Session.Info.Name} has changes that are not saved.\n\nDiscard them and {action}?",
            AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (busy)
        {
            e.Cancel = true;
            MessageBox.Show(this, "Please wait until the current task finishes.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!ConfirmDiscard("exit"))
        {
            e.Cancel = true;
            return;
        }
        CloseMap();
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        if (!busy && DroppedArchive(e) != null)
            e.Effect = DragDropEffects.Copy;
    }

    protected override async void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        if (DroppedArchive(e) is string archive)
            await OpenAsync(archive);
    }

    static string? DroppedArchive(DragEventArgs e) =>
        e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files &&
        Path.GetExtension(files[0]).ToLowerInvariant() is ".sd7" or ".sdz" ? files[0] : null;

    /// <summary>
    /// Runs background work with the progress bar showing. Meanwhile the view shows its last frame and every
    /// command is disabled, because the session is not thread-safe. Returns false (after reporting) if it failed.
    /// </summary>
    async Task<bool> RunBusy(string what, Func<IProgress<string>, Task> work)
    {
        busy = true;
        mapView.Frozen = true;
        if (terrainWindow != null)
            terrainWindow.View.Frozen = true;
        busyLabel.Text = what + "…";
        busyLabel.Visible = progress.Visible = true;
        UseWaitCursor = true;
        UpdateCommands();
        try
        {
            await work(new Progress<string>(text => busyLabel.Text = text));
            return true;
        }
        catch (Exception ex)
        {
            ShowError(what + " failed", ex);
            return false;
        }
        finally
        {
            busy = false;
            mapView.Frozen = false;
            if (terrainWindow != null)
                terrainWindow.View.Frozen = false;
            busyLabel.Visible = progress.Visible = false;
            UseWaitCursor = false;
            UpdateCommands();
            mapView.Redraw();
        }
    }

    // ---------------------------------------------------------------- editing

    internal void Undo()
    {
        if (map == null || busy || mapView.IsStroking || !map.Session.CanUndo)
            return;
        var what = map.Session.UndoDescription;
        map.Session.Undo();
        Log($"Undid {what}");
    }

    internal void Redo()
    {
        if (map == null || busy || mapView.IsStroking || !map.Session.CanRedo)
            return;
        var what = map.Session.RedoDescription;
        map.Session.Redo();
        Log($"Redid {what}");
    }

    // ---------------------------------------------------------------- height tools

    /// <summary>Which tool tab is in front: its tool drives the left mouse button.</summary>
    enum ToolTab { Heights, Texture, Metal, Items, Colours, Assets }

    ToolTab ActiveTab => (ToolTab)Math.Max(0, toolTabs.SelectedIndex);

    void UpdateBrush()
    {
        mapView.PointerMode = (ActiveTab == ToolTab.Metal && metalPanel.Mode != MetalMode.Pan) ||
                              (ActiveTab == ToolTab.Items && itemsPanel.Mode is ItemsMode.Select or ItemsMode.Place) ||
                              (ActiveTab == ToolTab.Heights && heightTools.Tool == HeightTool.Ramp);
        mapView.Brush = BrushForTab();
    }

    BrushCursor? BrushForTab() => ActiveTab switch
    {
        ToolTab.Heights when heightTools.Tool is not (HeightTool.Pan or HeightTool.Ramp) => new BrushCursor(heightTools.Radius, heightTools.Hardness,
            RepeatWhileHeld: heightTools.Tool is HeightTool.Raise or HeightTool.Lower or HeightTool.Smooth or HeightTool.Noise),
        ToolTab.Texture when textureTools.Tool != TextureTool.Pan => new BrushCursor(textureTools.Radius, textureTools.Hardness, RepeatWhileHeld: false),
        ToolTab.Items when itemsPanel.Mode is ItemsMode.Scatter or ItemsMode.Grass => new BrushCursor(itemsPanel.Radius, 1, RepeatWhileHeld: false),
        _ => null,
    };

    /// <summary>The splat tool shows the splat layer; the other texture tools show the texture.</summary>
    async Task FollowTextureTool()
    {
        if (map == null || busy)
            return;
        if (textureTools.Tool == TextureTool.Splat && mapView.Layer != MapViewLayer.Splat && map.Renderer.Unavailable(MapViewLayer.Splat) == null)
            await SelectLayerAsync(MapViewLayer.Splat);
        else if (textureTools.Tool is TextureTool.Paint or TextureTool.Tint or TextureTool.Adjust or TextureTool.Clone && mapView.Layer == MapViewLayer.Splat)
            await SelectLayerAsync(MapViewLayer.Texture);
    }

    static string StrokeName(HeightTool tool, double target) => tool switch
    {
        HeightTool.Raise => "raise heights",
        HeightTool.Lower => "lower heights",
        HeightTool.Smooth => "smooth heights",
        HeightTool.Flatten => "flatten heights",
        HeightTool.SetHeight => string.Create(CultureInfo.CurrentCulture, $"set heights to {target:0.#}"),
        _ => "height noise",
    };

    void OnStrokeStarted(object? sender, StrokeEventArgs e)
    {
        if (map == null || busy)
        {
            e.Cancel = true;
            return;
        }
        if (ActiveTab == ToolTab.Texture)
        {
            TextureStrokeStarted(e);
            return;
        }
        if (ActiveTab == ToolTab.Items)
        {
            ItemsStrokeStarted(e);
            return;
        }
        var heights = map.Session.Heights;
        if (heightTools.Tool == HeightTool.SetHeight && e.Modifiers.HasFlag(Keys.Control))
        {
            heightTools.TargetHeight = heights.HeightAt(e.X, e.Z);
            Log($"Target height set to {heightTools.TargetHeight:0.#} from ({e.X:0}, {e.Z:0})");
            e.Cancel = true;
            return;
        }
        flattenTarget = heights.HeightAt(e.X, e.Z);
        strokeFailed = false;
        map.Session.BeginGroup(StrokeName(heightTools.Tool, heightTools.TargetHeight));
    }

    void OnDab(object? sender, StrokeEventArgs e)
    {
        if (map == null || strokeFailed)
            return;
        if (ActiveTab == ToolTab.Texture)
        {
            TextureDab(e);
            return;
        }
        if (ActiveTab == ToolTab.Items)
        {
            ItemsDab(e);
            return;
        }
        var s = map.Session;
        var brush = new HeightBrush(e.X, e.Z, heightTools.Radius, heightTools.Hardness);
        double strength = heightTools.Strength;
        try
        {
            switch (heightTools.Tool)
            {
                case HeightTool.Raise: s.RaiseHeights(brush, MaxStepPerDab * strength); break;
                case HeightTool.Lower: s.RaiseHeights(brush, -MaxStepPerDab * strength); break;
                case HeightTool.Smooth: s.SmoothHeights(brush, strength); break;
                case HeightTool.Flatten: s.FlattenHeights(brush, flattenTarget, strength); break;
                case HeightTool.SetHeight: s.FlattenHeights(brush, heightTools.TargetHeight, strength); break;
                case HeightTool.Noise: s.AddHeightNoise(brush, MaxStepPerDab * strength, heightTools.BumpSize); break;
            }
        }
        catch (InvalidOperationException ex)
        {
            strokeFailed = true;
            ShowError("The brush could not be applied", ex);
        }
    }

    void TextureStrokeStarted(StrokeEventArgs e)
    {
        var s = map!.Session;
        var tool = textureTools.Tool;
        if (tool is TextureTool.Paint or TextureTool.Tint && e.Modifiers.HasFlag(Keys.Control))
        {
            var image = s.TextureImage;
            int px = (int)Math.Clamp(e.X * image.Width / s.MapWidth, 0, image.Width - 1);
            int py = (int)Math.Clamp(e.Z * image.Height / s.MapHeight, 0, image.Height - 1);
            textureTools.Colour = image.GetPixel(px, py);
            Log($"Colour picked at ({e.X:0}, {e.Z:0}): {textureTools.Colour.R}, {textureTools.Colour.G}, {textureTools.Colour.B}");
            e.Cancel = true;
            return;
        }
        if (tool == TextureTool.Clone && e.Modifiers.HasFlag(Keys.Alt))
        {
            textureTools.SetCloneSource(e.X, e.Z);
            Log($"Clone source set at ({e.X:0}, {e.Z:0})");
            e.Cancel = true;
            return;
        }
        if (tool == TextureTool.Clone)
        {
            if (textureTools.CloneSource is not (double sx, double sz))
            {
                Log("Clone: Alt+click on the map first to choose where to copy from.");
                e.Cancel = true;
                return;
            }
            textureTools.CloneOffset ??= (sx - e.X, sz - e.Z); // fixed by the first stroke, so later strokes stay aligned
        }
        if (tool == TextureTool.Splat && map.Renderer.Unavailable(MapViewLayer.Splat) is string why)
        {
            Log(why);
            e.Cancel = true;
            return;
        }
        strokeFailed = false;
        s.BeginGroup(tool switch
        {
            TextureTool.Paint => "paint texture",
            TextureTool.Tint => "tint texture",
            TextureTool.Adjust => "adjust texture",
            TextureTool.Clone => "clone texture",
            _ => "paint splat",
        });
    }

    void TextureDab(StrokeEventArgs e)
    {
        var s = map!.Session;
        var t = textureTools;
        var brush = new HeightBrush(e.X, e.Z, t.Radius, t.Hardness);
        try
        {
            switch (t.Tool)
            {
                case TextureTool.Paint: s.PaintTexture(brush, t.Colour, t.Strength); break;
                case TextureTool.Tint: s.TintTexture(brush, t.Colour, t.Strength); break;
                case TextureTool.Adjust: s.AdjustTexture(brush, t.Brightness, t.Saturation, t.Hue, t.Strength); break;
                case TextureTool.Clone when t.CloneOffset is (double dx, double dz): s.CloneTexture(brush, dx, dz, t.Strength); break;
                case TextureTool.Splat: s.PaintSplat(brush, t.SplatWeights, t.Strength); break;
            }
        }
        catch (InvalidOperationException ex)
        {
            strokeFailed = true;
            ShowError("The brush could not be applied", ex);
        }
    }

    void OnStrokeEnded()
    {
        map?.Session.EndGroup();
        UpdateTitle();
        UpdateCommands();
    }

    internal void ApplySeaLevel()
    {
        double level = heightTools.SeaLevel;
        if (map == null || busy || level == 0)
            return;
        if (Edit(s => s.OffsetHeights(-level)))
            heightTools.SeaLevel = 0;
    }

    /// <summary>Applies one edit to the session, logging its result or error. Returns whether it worked.</summary>
    bool Edit(Func<MapEditSession, string> edit)
    {
        if (map == null || busy || mapView.IsStroking)
            return false;
        try
        {
            Log(edit(map.Session));
            return true;
        }
        catch (InvalidOperationException ex)
        {
            ShowError("The edit could not be applied", ex);
            return false;
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Tool keys only while the map has the focus, so typing in the panel's boxes is never taken over.
        if (mapView.Focused && map != null && !busy && (keyData & Keys.Modifiers) == Keys.None)
        {
            if (ActiveTab == ToolTab.Metal && keyData == Keys.Delete)
            {
                DeleteSelectedSpot();
                return true;
            }
            if (RampActive && keyData == Keys.Enter)
            {
                ApplyRamp();
                return true;
            }
            if (ActiveTab == ToolTab.Items && keyData is Keys.Delete or Keys.Q or Keys.E)
            {
                if (keyData == Keys.Delete)
                    RemoveSelectedItems();
                else
                    TurnSelectedItems(keyData == Keys.Q ? -15 : 15);
                return true;
            }
            if (ActiveTab switch
                {
                    ToolTab.Heights => heightTools.HandleKey(keyData),
                    ToolTab.Texture => textureTools.HandleKey(keyData),
                    ToolTab.Metal => metalPanel.HandleKey(keyData),
                    ToolTab.Items => itemsPanel.HandleKey(keyData),
                    _ => false,
                })
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---------------------------------------------------------------- ramp

    enum RampDrag { None, Move, Start, End, Side, Rock, Dirt, Point }

    (double X, double Z)? rampCentre;
    List<(double X, double Z)>? rampPoints; // a bending ramp's points, first end to last, in whole units (null: straight, by centre, length and direction)
    RampPath? rampPath; // the line through rampPoints, kept while they don't change (it keeps the lookups the preview needs)
    (double X, double Z)? rampRockPick, rampDirtPick; // where rock and dirt come from when picked on the map (null: found automatically)
    RampDrag rampDrag;
    int rampPoint; // the point being dragged
    (double X, double Z) rampDragFrom, rampCentreFrom, rampFixedEnd, rampPointFrom;
    List<(double X, double Z)> rampPointsFrom = [];
    (double Length, double Width, double Direction, (double X, double Z)? Centre, List<(double X, double Z)>? Points) rampBefore;

    bool RampActive => map != null && ActiveTab == ToolTab.Heights && heightTools.Tool == HeightTool.Ramp;

    /// <summary>A bending ramp's points, first end to last; null for a straight ramp.</summary>
    internal IReadOnlyList<(double X, double Z)>? RampPoints => rampPoints;

    void OnHeightToolChanged()
    {
        UpdateBrush();
        if (RampActive && rampCentre == null && rampPoints == null)
        {
            // First use: in the middle of the view.
            var (x, z) = mapView.View.ToWorld(mapView.ClientSize.Width / 2.0, mapView.ClientSize.Height / 2.0);
            rampCentre = (Math.Round(Math.Clamp(x, 0, map!.Session.MapWidth)), Math.Round(Math.Clamp(z, 0, map.Session.MapHeight)));
        }
        UpdateRampPreview();
    }

    /// <summary>
    /// The ramp as placed and shaped now (no edge yet): a straight one's ends to a tenth of a unit, or a bending one
    /// through its points; null before it is placed.
    /// </summary>
    internal Ramp? CurrentRamp
    {
        get
        {
            if (map == null)
                return null;
            if (rampPoints != null)
            {
                if (rampPath == null || !rampPath.Points.SequenceEqual(rampPoints))
                    rampPath = new RampPath(rampPoints);
                var (first, last) = (rampPoints[0], rampPoints[^1]);
                return new Ramp(first.X, first.Z, last.X, last.Z, heightTools.RampWidth, 0) { Path = rampPath };
            }
            if (rampCentre is not (double cx, double cz))
                return null;
            double a = heightTools.RampDirection * Math.PI / 180, half = heightTools.RampLength / 2;
            double dx = Math.Sin(a), dz = -Math.Cos(a); // 0° runs north, up the map
            return new Ramp(Math.Round(cx - dx * half, 1), Math.Round(cz - dz * half, 1), Math.Round(cx + dx * half, 1),
                Math.Round(cz + dz * half, 1), heightTools.RampWidth, 0);
        }
    }

    /// <summary>The ramp as Apply ramp would build it: heights from the ground where set to follow it, and its edge.</summary>
    internal (Ramp Ramp, double H1, double H2)? RampPreview
    {
        get
        {
            if (CurrentRamp is not Ramp r)
                return null;
            var s = map!.Session;
            var (g1, g2) = s.RampEndHeights(r);
            double h1 = Math.Round(heightTools.RampStartHeight ?? g1, 1), h2 = Math.Round(heightTools.RampEndHeight ?? g2, 1);
            return (r with { Edge = heightTools.RampEdge ?? s.RampAutoEdge(r, h1, h2) }, h1, h2);
        }
    }

    /// <summary>
    /// Where rock to dirt would take rock and dirt from: picked on the map, or found near the ramp; null when the look
    /// isn't rock to dirt or the ramp is level.
    /// </summary>
    internal ((double X, double Z)? Rock, (double X, double Z)? Dirt)? RampSources
    {
        get
        {
            if (heightTools.RampLook != RampLook.RockToDirt || RampPreview is not (Ramp r, double h1, double h2) ||
                Math.Abs(h2 - h1) < MapEditSession.LevelRampRise)
                return null;
            var found = rampRockPick != null && rampDirtPick != null ? (null, null) : map!.Session.RampMaterialSources(r, h1, h2);
            return (rampRockPick ?? found.Rock, rampDirtPick ?? found.Dirt);
        }
    }

    /// <summary>
    /// Why the ramp as shaped can't be built (an end or point off the map, a bend too tight, two parts too near), or
    /// null if it can.
    /// </summary>
    string? RampProblem(Ramp r)
    {
        var s = map!.Session;
        if (r.Points.Any(p => p.X < 0 || p.Z < 0 || p.X > s.MapWidth || p.Z > s.MapHeight))
            return r.Path == null ? "An end is off the map: move the ramp in before applying it." : "A point is off the map: move it in before applying the ramp.";
        return MapEditSession.RampShapeProblem(r);
    }

    /// <summary>Shows the heights and edge the ramp would use, its slope, and its outline on the map.</summary>
    void UpdateRampPreview()
    {
        heightTools.RampBends = rampPoints != null;
        if (RampActive && RampPreview is (Ramp r, double h1, double h2))
        {
            heightTools.ShowRampAuto(h1, h2, r.Edge);
            double rise = h2 - h1;
            string? problem = RampProblem(r);
            string bends = r.Path == null ? ""
                : r.TightestBend(h1, h2) is var (radius, _, _, inside)
                    ? string.Create(CultureInfo.CurrentCulture, $" {r.Bends.Count} bend{(r.Bends.Count == 1 ? "" : "s")}: the tightest turns on {radius:0}, {inside:0.#}° on its inside.")
                    : string.Create(CultureInfo.CurrentCulture, $" {r.Bends.Count} bend{(r.Bends.Count == 1 ? "" : "s")}, in line.");
            heightTools.RampInfo = problem ?? string.Create(CultureInfo.CurrentCulture,
                $"{(rise >= 0 ? "Rises" : "Falls")} {Math.Abs(rise):0.#} over {r.Length:0}: {r.SlopeDegrees(h1, h2):0.#}° ({100 * Math.Abs(rise) / r.Length:0.#}% grade). Edge {r.Edge:0}.") + bends;
            var (startLip, endLip) = problem != null ? (0, 0) : map!.Session.RampLipDegrees(r, h1, h2, heightTools.RampRoughness);
            heightTools.RampClimb = problem != null ? "" : MapCore.Movement.RampPassability.Describe(r.Length, r.Width, h1, h2, startLip, endLip);
        }
        else
        {
            heightTools.RampInfo = map == null ? "" : "Choose the Ramp tool and click the map to place a ramp.";
            heightTools.RampClimb = "";
        }
        UpdateMarks();
    }

    /// <summary>Builds the ramp as previewed (one undo step); false if there is none or it could not be built.</summary>
    internal bool ApplyRamp()
    {
        if (!RampActive || RampPreview is not (Ramp r, double h1, double h2))
            return false;
        var sources = RampSources;
        bool done = Edit(s => s.BuildRamp(r, h1, h2, heightTools.RampLook, heightTools.RampRoughness, Rounded(sources?.Rock), Rounded(sources?.Dirt)));
        UpdateRampPreview();
        return done;
    }

    static (double X, double Z)? Rounded((double X, double Z)? p) => p is (double x, double z) ? (Math.Round(x), Math.Round(z)) : null;

    void RampPointerDown(StrokeEventArgs e)
    {
        if (map == null || busy)
        {
            e.Cancel = true;
            return;
        }
        rampBefore = (heightTools.RampLength, heightTools.RampWidth, heightTools.RampDirection, rampCentre, rampPoints?.ToList());
        rampDragFrom = (e.X, e.Z);
        bool shift = e.Modifiers.HasFlag(Keys.Shift), control = e.Modifiers.HasFlag(Keys.Control);
        if (CurrentRamp is Ramp r)
        {
            // Handles are picked in screen pixels, so they stay easy to grab at any zoom.
            double reach = 10 * mapView.View.UnitsPerPixel;
            bool Near(double x, double z) => Math.Sqrt((x - e.X) * (x - e.X) + (z - e.Z) * (z - e.Z)) <= reach;
            if (shift)
            {
                ExtendRamp(r, e.X, e.Z);
                return;
            }
            var (sx, sz) = r.World(r.Length / 2, r.Width / 2);
            var sources = RampSources;
            var points = r.Points;
            // The ends first (as a straight ramp has them), then the bends.
            int hit = Near(r.X2, r.Z2) ? points.Count - 1 : Near(r.X1, r.Z1) ? 0 : -1;
            for (int i = 1; i < points.Count - 1 && hit < 0; i++)
                if (Near(points[i].X, points[i].Z))
                    hit = i;
            if (!control && sources?.Rock is (double rx, double rz) && Near(rx, rz))
                (rampDrag, rampRockPick) = (RampDrag.Rock, (rx, rz));
            else if (!control && sources?.Dirt is (double dx, double dz) && Near(dx, dz))
                (rampDrag, rampDirtPick) = (RampDrag.Dirt, (dx, dz));
            else if (control && hit > 0 && hit < points.Count - 1)
            {
                RemoveRampBend(hit);
                rampDrag = RampDrag.None;
                return;
            }
            else if (control)
            {
                InsertRampBend(r, e.X, e.Z);
                return;
            }
            else if (hit >= 0 && rampPoints != null)
                (rampDrag, rampPoint, rampPointFrom) = (RampDrag.Point, hit, rampPoints[hit]);
            else if (hit == points.Count - 1)
                (rampDrag, rampFixedEnd) = (RampDrag.End, (r.X1, r.Z1));
            else if (hit == 0)
                (rampDrag, rampFixedEnd) = (RampDrag.Start, (r.X2, r.Z2));
            else if (Near(sx, sz))
                rampDrag = RampDrag.Side;
            else if (r.DistanceOutside(e.X, e.Z) <= 0)
                (rampDrag, rampCentreFrom, rampPointsFrom) = (RampDrag.Move, rampCentre ?? (0, 0), rampPoints?.ToList() ?? []);
            else
                rampDrag = RampDrag.None;
            if (rampDrag != RampDrag.None || rampPoints != null)
                return; // a click off a bending ramp leaves it be: it would take all its points to put it back
        }
        // Anywhere else: the straight ramp jumps there, and dragging moves it on.
        rampCentre = (Math.Round(e.X), Math.Round(e.Z));
        (rampDrag, rampCentreFrom) = (RampDrag.Move, rampCentre.Value);
        UpdateRampPreview();
    }

    /// <summary>The points of a ramp as they are, to bend it: its two ends for a straight one.</summary>
    static List<(double X, double Z)> PointsOf(Ramp r) => [.. r.Points.Select(p => (Math.Round(p.X), Math.Round(p.Z)))];

    static double Apart((double X, double Z) a, (double X, double Z) b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    /// <summary>Shift+click: the ramp runs on from its nearer end to here (that end becomes a bend), and dragging moves the new end.</summary>
    void ExtendRamp(Ramp r, double x, double z)
    {
        var points = PointsOf(r);
        var at = (Math.Round(x), Math.Round(z));
        bool atStart = Apart(at, points[0]) < Apart(at, points[^1]);
        if (Apart(at, atStart ? points[0] : points[^1]) < MapEditSession.MinRampPointGap)
        {
            rampDrag = RampDrag.None;
            return;
        }
        if (atStart)
            points.Insert(0, at);
        else
            points.Add(at);
        rampPoints = points;
        (rampDrag, rampPoint, rampPointFrom) = (RampDrag.Point, atStart ? 0 : points.Count - 1, at);
        UpdateRampPreview();
    }

    /// <summary>
    /// Ctrl+click: a bend on the line between the ramp's points nearest the click, where the ramp runs straight along
    /// that line (past the arcs at either end of it), so the ramp doesn't change until it's dragged.
    /// </summary>
    void InsertRampBend(Ramp r, double x, double z)
    {
        var points = PointsOf(r);
        var tangents = r.Path?.Tangents;
        int best = -1;
        (double X, double Z) bestAt = default;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < points.Count - 1; i++)
        {
            var (a, b) = (points[i], points[i + 1]);
            double dx = b.X - a.X, dz = b.Z - a.Z, length = Math.Sqrt(dx * dx + dz * dz);
            double lo = tangents?[i] ?? 0, hi = Math.Max(lo, length - (tangents?[i + 1] ?? 0));
            double along = Math.Clamp(((x - a.X) * dx + (z - a.Z) * dz) / length, lo, hi);
            var at = (Math.Round(a.X + along * dx / length), Math.Round(a.Z + along * dz / length));
            double distance = Apart(at, (x, z));
            if (distance < bestDistance && Apart(at, a) >= MapEditSession.MinRampPointGap && Apart(at, b) >= MapEditSession.MinRampPointGap)
                (best, bestAt, bestDistance) = (i, at, distance);
        }
        if (best < 0 || points.Count >= MapEditSession.MaxRampPoints)
        {
            rampDrag = RampDrag.None;
            Log(points.Count >= MapEditSession.MaxRampPoints
                ? $"A ramp runs through at most {MapEditSession.MaxRampPoints} points."
                : $"No room for a bend there: its points must be at least {MapEditSession.MinRampPointGap} units apart.");
            return;
        }
        points.Insert(best + 1, bestAt);
        rampPoints = points;
        (rampDrag, rampPoint, rampPointFrom) = (RampDrag.Point, best + 1, bestAt);
        UpdateRampPreview();
    }

    /// <summary>Ctrl+click on a bend takes it out; with only its ends left, the ramp is straight again.</summary>
    void RemoveRampBend(int index)
    {
        if (rampPoints == null)
            return;
        rampPoints.RemoveAt(index);
        if (rampPoints.Count == 2)
            StraightenRamp();
        else
            UpdateRampPreview();
    }

    /// <summary>Straighten, as the panel's button does it.</summary>
    internal void StraightenRampForTest() => StraightenRamp();

    /// <summary>Gives the ramp these points (a bending ramp through them), as clicking them in would.</summary>
    internal void SetRampPointsForTest(IEnumerable<(double X, double Z)> points)
    {
        rampPoints = [.. points];
        UpdateRampPreview();
    }

    /// <summary>The ramp straight again, between its ends as they are.</summary>
    void StraightenRamp()
    {
        if (rampPoints == null)
            return;
        var (first, last) = (rampPoints[0], rampPoints[^1]);
        rampPoints = null;
        double dx = last.X - first.X, dz = last.Z - first.Z;
        rampCentre = (Math.Round((first.X + last.X) / 2), Math.Round((first.Z + last.Z) / 2));
        heightTools.RampLength = Math.Max(MapEditSession.MinRampSize, Math.Round(Math.Sqrt(dx * dx + dz * dz)));
        if (dx != 0 || dz != 0)
            heightTools.RampDirection = Math.Atan2(dx, -dz) * 180 / Math.PI;
        UpdateRampPreview();
    }

    /// <summary>Q / E on a bending ramp: turns all its points about its middle.</summary>
    void TurnRampPath(double degrees)
    {
        if (rampPoints == null || CurrentRamp is not Ramp r)
            return;
        var (cx, cz) = r.World(r.Length / 2, 0);
        double a = degrees * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        // 0° runs north and angles grow clockwise on the map, as the Direction box has them.
        rampPoints = [.. rampPoints.Select(p => (Math.Round(cx + (p.X - cx) * cos - (p.Z - cz) * sin), Math.Round(cz + (p.X - cx) * sin + (p.Z - cz) * cos)))];
        UpdateRampPreview();
    }

    /// <summary>Swap ends on a bending ramp: its points run the other way.</summary>
    void ReverseRampPath()
    {
        if (rampPoints == null)
            return;
        rampPoints.Reverse();
        UpdateRampPreview();
    }

    void RampPointerDrag(StrokeEventArgs e)
    {
        if (map == null || CurrentRamp is not Ramp r)
            return;
        switch (rampDrag)
        {
            case RampDrag.Move when rampPoints != null:
            {
                double dx = Math.Round(e.X - rampDragFrom.X), dz = Math.Round(e.Z - rampDragFrom.Z);
                rampPoints = [.. rampPointsFrom.Select(p => (p.X + dx, p.Z + dz))];
                break;
            }
            case RampDrag.Move:
                rampCentre = (Math.Round(rampCentreFrom.X + e.X - rampDragFrom.X), Math.Round(rampCentreFrom.Z + e.Z - rampDragFrom.Z));
                break;
            case RampDrag.Point when rampPoints != null:
            {
                // By how far the mouse has moved, so grabbing a handle off its centre doesn't make it jump.
                var at = (Math.Round(rampPointFrom.X + e.X - rampDragFrom.X), Math.Round(rampPointFrom.Z + e.Z - rampDragFrom.Z));
                bool room = (rampPoint == 0 || Apart(at, rampPoints[rampPoint - 1]) >= MapEditSession.MinRampPointGap) &&
                            (rampPoint == rampPoints.Count - 1 || Apart(at, rampPoints[rampPoint + 1]) >= MapEditSession.MinRampPointGap);
                if (room)
                    rampPoints[rampPoint] = at;
                break;
            }
            case RampDrag.Start or RampDrag.End:
            {
                // The other end stays put; the dragged end follows the mouse.
                var (fx, fz) = rampFixedEnd;
                double dx = rampDrag == RampDrag.End ? e.X - fx : fx - e.X, dz = rampDrag == RampDrag.End ? e.Z - fz : fz - e.Z;
                double length = Math.Max(MapEditSession.MinRampSize, Math.Sqrt(dx * dx + dz * dz));
                if (dx == 0 && dz == 0)
                    break;
                double degrees = Math.Atan2(dx, -dz) * 180 / Math.PI;
                double a = Math.Round(degrees) * Math.PI / 180;
                double sign = rampDrag == RampDrag.End ? 1 : -1;
                heightTools.RampDirection = degrees;
                heightTools.RampLength = Math.Round(length);
                rampCentre = (Math.Round(fx + sign * Math.Sin(a) * Math.Round(length) / 2), Math.Round(fz - sign * Math.Cos(a) * Math.Round(length) / 2));
                break;
            }
            case RampDrag.Side:
                heightTools.RampWidth = Math.Round(Math.Max(MapEditSession.MinRampSize, 2 * Math.Abs(r.Local(e.X, e.Z).V)));
                break;
            case RampDrag.Rock:
                rampRockPick = (Math.Round(e.X), Math.Round(e.Z));
                break;
            case RampDrag.Dirt:
                rampDirtPick = (Math.Round(e.X), Math.Round(e.Z));
                break;
            default:
                return;
        }
        UpdateRampPreview();
    }

    void CancelRampDrag()
    {
        if (rampDrag == RampDrag.None)
            return;
        rampDrag = RampDrag.None;
        rampCentre = rampBefore.Centre;
        rampPoints = rampBefore.Points;
        heightTools.RampLength = rampBefore.Length;
        heightTools.RampWidth = rampBefore.Width;
        heightTools.RampDirection = rampBefore.Direction;
        UpdateRampPreview();
    }

    // ---------------------------------------------------------------- metal spots

    /// <summary>Finds the spots again, keeps the selection on the nearest one, and updates the list and the rings.</summary>
    void RefreshSpots()
    {
        if (map == null)
        {
            spots = [];
            metalPanel.ShowSpots([], -1);
            UpdateMarks();
            return;
        }
        var s = map.Session;
        spots = [.. s.Metal.FindSpots()];
        int selected = -1;
        if (selectedSpot is (double sx, double sz))
        {
            var nearest = spots.Select((p, i) => (Index: i, Distance: Math.Sqrt((p.X - sx) * (p.X - sx) + (p.Z - sz) * (p.Z - sz))))
                .Where(p => p.Distance < 40).OrderBy(p => p.Distance).FirstOrDefault((-1, 0));
            selected = nearest.Index;
        }
        selectedSpot = selected >= 0 ? (spots[selected].X, spots[selected].Z) : null;
        metalPanel.ShowSpots(spots.Select(p => new SpotRow(p.X, p.Z, p.Cells.Count, s.Metal.Income(p), s.SpotNotes(p))).ToList(), selected);
        UpdateMarks();
    }

    void UpdateMarks()
    {
        var marks = new List<ViewMark>();
        var shapes = new List<ViewShape>();
        if (RampActive && RampPreview is (Ramp r, double h1, double h2))
        {
            bool problem = RampProblem(r) != null;
            shapes.Add(new ViewShape(r.Outline(), problem ? Color.Red : Color.White));
            if (r.Edge > 0 && r.Path == null)
                shapes.Add(new ViewShape(r.Outline(r.Edge), Color.FromArgb(190, 255, 255, 255), Dashed: true));
            else if (r.Edge > 0)
            {
                // Traced where the blend ends: grown from the curve, the edge would loop inside bends tighter than it.
                var (minX, minZ, maxX, maxZ) = r.Bounds;
                foreach (var line in r.EdgeContour(Math.Max(8, Math.Max(maxX - minX, maxZ - minZ) / 160)))
                    shapes.Add(new ViewShape(line, Color.FromArgb(190, 255, 255, 255), Dashed: true));
            }
            // A bending ramp's points, joined: it runs along these lines, rounding each bend.
            if (r.Path != null)
                shapes.Add(new ViewShape(r.Points, Color.FromArgb(150, 255, 215, 0), Closed: false, Dashed: true));
            // An arrow from the start to the end down the middle (along its curve), its head sized to fit.
            double head = Math.Min(r.Width / 3, r.Length * 0.2), tip = r.Length * 0.85;
            var middle = new List<(double X, double Z)>();
            for (double u = r.Length * 0.15; u < tip; u += Math.Max(4, r.Length / 200))
                middle.Add(r.World(u, 0));
            middle.Add(r.World(tip, 0));
            shapes.Add(new ViewShape(middle, Color.Gold, Closed: false));
            shapes.Add(new ViewShape([r.World(tip - head, -head * 0.6), r.World(tip, 0), r.World(tip - head, head * 0.6)], Color.Gold, Closed: false));
            marks.Add(new ViewMark(r.X1, r.Z1, 0, Color.DeepSkyBlue, Label: string.Create(CultureInfo.CurrentCulture, $"start {h1:0.#}")));
            marks.Add(new ViewMark(r.X2, r.Z2, 0, Color.Orange, Label: string.Create(CultureInfo.CurrentCulture, $"end {h2:0.#}")));
            foreach (var (bx, bz) in r.Bends)
                marks.Add(new ViewMark(bx, bz, 0, Color.Gold));
            var (sx, sz) = r.World(r.Length / 2, r.Width / 2);
            marks.Add(new ViewMark(sx, sz, 0, Color.White));
            if (RampSources is var (rock, dirt))
            {
                if (rock is (double rx, double rz))
                    marks.Add(new ViewMark(rx, rz, MapEditSession.MaterialPatch / 2, Color.FromArgb(150, 110, 80), Label: "rock"));
                if (dirt is (double dx, double dz))
                    marks.Add(new ViewMark(dx, dz, MapEditSession.MaterialPatch / 2, Color.FromArgb(230, 200, 140), Label: "dirt"));
            }
        }
        mapView.Shapes = shapes;
        if (ActiveTab == ToolTab.Metal)
        {
            if (selectedSpot is (double x, double z))
                marks.Add(new ViewMark(x, z, 40, Color.White));
            if (dropGhost != null)
                marks.Add(dropGhost);
        }
        if (ActiveTab == ToolTab.Items && map != null)
        {
            double dx = itemDrag == ItemDrag.Items ? Math.Round(itemDragNow.X - itemDragFrom.X) : 0;
            double dz = itemDrag == ItemDrag.Items ? Math.Round(itemDragNow.Z - itemDragFrom.Z) : 0;
            foreach (var item in ResolveSelection(map.Session.Items.All))
            {
                marks.Add(new ViewMark(item.X, item.Z, 14, Color.White));
                if (dx != 0 || dz != 0)
                    marks.Add(new ViewMark(item.X + dx, item.Z + dz, 14, Color.LimeGreen, Dashed: true));
            }
            if (itemDrag == ItemDrag.Start)
                marks.Add(new ViewMark(startOrigin.X + itemDragNow.X - itemDragFrom.X, startOrigin.Z + itemDragNow.Z - itemDragFrom.Z, 40, Color.LimeGreen, Dashed: true, $"Start {draggedTeam}"));
            var structures = map.Session.Structures;
            if (CurrentStructure() is StructureInstance picked && structures.Kinds.TryGetValue(picked.Kind, out var kind))
            {
                foreach (var part in StructureLayout.Parts(kind, picked).Where(p => p.Role is "wall" or "centre"))
                    shapes.Add(new ViewShape([(part.X1, part.Z1), (part.X2, part.Z1), (part.X2, part.Z2), (part.X1, part.Z2)], Color.White));
                // Where a drag would put it (on the 16-unit grid).
                if (itemDrag == ItemDrag.Structure && StructureDragTarget(picked) is var (tx, tz) && (tx != picked.X || tz != picked.Z))
                    foreach (var part in StructureLayout.Parts(kind, picked with { X = tx, Z = tz }).Where(p => p.Role is "wall" or "centre"))
                        shapes.Add(new ViewShape([(part.X1, part.Z1), (part.X2, part.Z1), (part.X2, part.Z2), (part.X1, part.Z2)], Color.LimeGreen, Dashed: true));
                // Its repair markers, the picked one brighter, and where a drag would put that one (on the 8-unit grid).
                foreach (var (mx, mz) in picked.MarkerList)
                    shapes.Add(new ViewShape([(mx - 9, mz - 9), (mx + 9, mz - 9), (mx + 9, mz + 9), (mx - 9, mz + 9)],
                        selectedMarker == (mx, mz) ? Color.White : Color.FromArgb(150, 255, 255, 255), Dashed: selectedMarker != (mx, mz)));
                if (itemDrag == ItemDrag.Marker && selectedMarker is var (sx, sz) && MarkerDragTarget(sx, sz) is var (ux, uz) && (ux != sx || uz != sz))
                    shapes.Add(new ViewShape([(ux - 9, uz - 9), (ux + 9, uz - 9), (ux + 9, uz + 9), (ux - 9, uz + 9)], Color.LimeGreen, Dashed: true));
            }
            // The picked guardian tower's zone (the map draws every zone's edge and path): its edge brighter, where a drag
            // would put it, its path's points, and a line from the tower to its path.
            var current = CurrentStructure();
            if (current is { Zone: GuardianZone placedZone } guardian && IsGuardian(current))
            {
                var zone = ZoneDragTarget(guardian) ?? placedZone;
                foreach (var line in ZoneOutline(zone))
                    shapes.Add(new ViewShape(line, Color.OrangeRed, Dashed: zone != placedZone));
                shapes.Add(new ViewShape(zone.Points, Color.Gold, Closed: zone.Closed));
                for (int i = 0; i < zone.Points.Count; i++)
                    marks.Add(new ViewMark(zone.Points[i].X, zone.Points[i].Z, 0, i == 0 ? Color.DeepSkyBlue : Color.Gold,
                        Label: i == 0 ? "path start" : i == zone.Points.Count - 1 && !zone.Closed ? "end" : null));
                var nearest = zone.Points.MinBy(p => (p.X - guardian.X) * (p.X - guardian.X) + (p.Z - guardian.Z) * (p.Z - guardian.Z));
                shapes.Add(new ViewShape([(guardian.X, guardian.Z), nearest], Color.FromArgb(160, 255, 215, 0), Closed: false, Dashed: true));
            }
            if (zoneFirstPoint is var (zx, zz) && current != null && IsGuardian(current) && current.Zone == null)
            {
                marks.Add(new ViewMark(zx, zz, 0, Color.DeepSkyBlue, Label: "path start: Shift+click the next point"));
                shapes.Add(new ViewShape([(current.X, current.Z), (zx, zz)], Color.FromArgb(160, 255, 215, 0), Closed: false, Dashed: true));
            }
            mapView.Shapes = shapes;
        }
        mapView.Marks = marks;
        mapView.SelectionBox = itemDrag == ItemDrag.Box ? (itemDragFrom.X, itemDragFrom.Z, itemDragNow.X, itemDragNow.Z) : null;
    }

    void ChooseSpot(int index)
    {
        if (index < 0 || index >= spots.Count)
            return;
        var spot = spots[index];
        selectedSpot = (spot.X, spot.Z);
        // Bring it to the middle of the view at the same zoom.
        var v = mapView.View;
        mapView.View = v with { Left = spot.X - mapView.ClientSize.Width / 2.0 * v.UnitsPerPixel, Top = spot.Z - mapView.ClientSize.Height / 2.0 * v.UnitsPerPixel };
        RefreshSpots();
    }

    /// <summary>
    /// Before the first metal edit on a map whose metal comes from mapconfig/map_metal_layout.lua (its metal map is
    /// empty), warn that adding metal makes BAR ignore that file.
    /// </summary>
    bool ConfirmMetalEdit()
    {
        var s = map!.Session;
        if (luaLayoutConfirmed || Quiet || s.Smf.MetalMap.Any(v => v != 0) ||
            !File.Exists(Path.Combine(s.Package.Root, "mapconfig", "map_metal_layout.lua")))
            return true;
        luaLayoutConfirmed = MessageBox.Show(this,
            "This map's metal comes from mapconfig/map_metal_layout.lua, because its metal map is empty.\n\n" +
            "Adding metal to the map makes BAR ignore that file: every spot it lists would disappear in game, " +
            "unless you add them all here.\n\nAdd metal anyway?",
            AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        return luaLayoutConfirmed;
    }

    void EditSelectedSpot(Func<MapEditSession, double, double, string> edit)
    {
        if (map == null || selectedSpot is not (double x, double z) || !ConfirmMetalEdit())
        {
            RefreshSpots();
            return;
        }
        if (!Edit(s => edit(s, x, z)))
            RefreshSpots(); // put the panel back to what the map has
    }

    internal void DeleteSelectedSpot()
    {
        if (map == null || selectedSpot is not (double x, double z) || !ConfirmMetalEdit())
            return;
        if (Edit(s => s.RemoveSpot(x, z)))
            selectedSpot = null;
        RefreshSpots();
    }

    void OnPointerDown(object? sender, StrokeEventArgs e)
    {
        if (RampActive)
        {
            RampPointerDown(e);
            return;
        }
        if (map != null && !busy && ActiveTab == ToolTab.Items)
        {
            ItemsPointerDown(e);
            return;
        }
        if (map == null || busy || ActiveTab != ToolTab.Metal)
        {
            e.Cancel = true;
            return;
        }
        var s = map.Session;
        if (metalPanel.Mode == MetalMode.Add)
        {
            e.Cancel = true; // a click, not a drag
            var (x, z, problem) = s.Metal.TryAdd(e.X, e.Z);
            if (problem != null)
            {
                Log("A spot can't go there: " + problem);
                return;
            }
            if (!ConfirmMetalEdit())
                return;
            selectedSpot = (x, z);
            Edit(session => session.AddSpot(e.X, e.Z));
            RefreshSpots();
            return;
        }
        // Prefer the selected spot under the cursor, so a nearby neighbour can't be grabbed by mistake.
        double reach = Math.Max(64, 14 * mapView.View.UnitsPerPixel);
        double Distance(MapCore.Metal.MetalSpot p) => Math.Sqrt((p.X - e.X) * (p.X - e.X) + (p.Z - e.Z) * (p.Z - e.Z));
        var hit = spots.FirstOrDefault(p => selectedSpot is (double sx, double sz) && p.X == sx && p.Z == sz && Distance(p) <= reach)
                  ?? spots.Where(p => Distance(p) <= reach).MinBy(Distance);
        selectedSpot = hit == null ? null : (hit.X, hit.Z);
        RefreshSpots();
        if (hit == null || s.Metal.ReadOnlyReason != null)
        {
            e.Cancel = true;
            return;
        }
        draggedSpot = hit;
        dragOrigin = (e.X, e.Z);
    }

    (double X, double Z) DragTarget(StrokeEventArgs e) =>
        (draggedSpot!.X + e.X - dragOrigin.X, draggedSpot.Z + e.Z - dragOrigin.Z);

    void OnPointerDrag(object? sender, StrokeEventArgs e)
    {
        if (RampActive)
        {
            RampPointerDrag(e);
            return;
        }
        if (ActiveTab == ToolTab.Items)
        {
            ItemsPointerDrag(e);
            return;
        }
        if (map == null || draggedSpot == null)
            return;
        var (tx, tz) = DragTarget(e);
        var (bx, bz, problem) = map.Session.Metal.TryMove(draggedSpot, tx, tz);
        dropGhost = new ViewMark(bx, bz, 40, problem == null ? Color.LimeGreen : Color.Red, Dashed: true, Label: problem == null ? null : "can't go here");
        UpdateMarks();
    }

    void OnPointerUp(object? sender, StrokeEventArgs e)
    {
        if (RampActive)
        {
            RampPointerDrag(e);
            rampDrag = RampDrag.None;
            return;
        }
        if (ActiveTab == ToolTab.Items)
        {
            ItemsPointerUp(e);
            return;
        }
        if (map == null || draggedSpot == null)
            return;
        var spot = draggedSpot;
        var (tx, tz) = DragTarget(e);
        CancelSpotDrag();
        var (bx, bz, problem) = map.Session.Metal.TryMove(spot, tx, tz);
        if (bx == spot.X && bz == spot.Z)
            return; // a click: just the selection
        if (problem != null)
        {
            Log("The spot can't go there: " + problem);
            return;
        }
        if (!ConfirmMetalEdit())
            return;
        selectedSpot = (bx, bz);
        Edit(s => s.MoveSpot(spot.X, spot.Z, tx, tz, metalPanel.MoveLook));
        RefreshSpots();
    }

    void CancelSpotDrag()
    {
        draggedSpot = null;
        dropGhost = null;
        UpdateMarks();
    }

    // ---------------------------------------------------------------- items

    static ItemKey KeyOf(MapItem item) => new(item.Source, item.Kind, item.Name, item.X, item.Z);

    /// <summary>The selected items as they are now: each key matched once, by store, list, name and position (within half a unit).</summary>
    List<MapItem> ResolveSelection(IReadOnlyList<MapItem> all)
    {
        var found = new List<MapItem>();
        foreach (var key in selectedItems)
            if (all.FirstOrDefault(i => !found.Contains(i) && i.Source == key.Source && i.Kind == key.Kind &&
                                        string.Equals(i.Name, key.Name, StringComparison.Ordinal) &&
                                        Math.Abs(i.X - key.X) < 0.5 && Math.Abs(i.Z - key.Z) < 0.5) is MapItem item)
                found.Add(item);
        return found;
    }

    void ShowItemSelection()
    {
        if (map == null)
        {
            itemsPanel.ShowSelection([], null);
            ShowItemPreview([]);
            UpdateMarks();
            return;
        }
        var items = ResolveSelection(map.Session.Items.All);
        selectedItems = items.Select(KeyOf).ToList(); // forget what is gone
        itemsPanel.ShowSelection(items, items.Count > 0 ? MapEditSession.HeadingDegrees(items[0]) : null);
        if (items.Count == 0 && CurrentStructure() != null)
            ShowStructureSelection();
        else
        {
            itemsPanel.ShowStructure(null, null, null, 0);
            ShowItemPreview(items);
        }
        UpdateMarks();
    }

    void EditSelectedItems(Func<MapEditSession, IReadOnlyList<MapItem>, string> edit)
    {
        if (map == null)
            return;
        var items = ResolveSelection(map.Session.Items.All);
        if (items.Count == 0)
            return;
        // Keys follow the edit: rotations keep positions, so the selection is found again as it is.
        Edit(s => edit(s, items));
        ShowItemSelection();
    }

    /// <summary>Turns the selected items (as Q / E do).</summary>
    /// <summary>Turns the selection (as Q / E do): items by the degrees, a picked door or tunnel a quarter that way.</summary>
    internal void TurnSelectedItems(double degrees)
    {
        if (CurrentStructure() is StructureInstance picked)
            TurnStructureTo((picked.Facing + Math.Sign(degrees) + 4) & 3);
        else
            EditSelectedItems((s, items) => s.RotateItems(items, degrees));
    }

    /// <summary>Turns the picked door or tunnel to a facing (one undo step), keeping it picked.</summary>
    void TurnStructureTo(int facing)
    {
        if (map == null || busy || CurrentStructure() is not StructureInstance picked || picked.Facing == facing)
        {
            ShowItemSelection();
            return;
        }
        if (Edit(s => s.TurnStructure(picked.X, picked.Z, facing)))
            selectedStructure = picked with { Facing = facing };
        ShowItemSelection();
    }

    internal void RemoveSelectedItems()
    {
        if (map == null)
            return;
        if (selectedMarker is var (mx, mz) && selectedStructure != null)
        {
            if (Edit(s => s.RemoveStructureMarker(mx, mz)))
                selectedMarker = null;
            ShowItemSelection();
            return;
        }
        if (selectedStructure is StructureInstance structure)
        {
            if (Edit(s => s.RemoveStructure(structure.X, structure.Z)))
                selectedStructure = null;
            ShowItemSelection();
            return;
        }
        var items = ResolveSelection(map.Session.Items.All);
        if (items.Count > 0 && Edit(s => s.RemoveItems(items)))
            selectedItems = [];
        ShowItemSelection();
    }

    // ---------------------------------------------------------------- guardian zones

    UnitCatalog? unitCatalog;
    internal UnitCatalog? UnitCatalog => unitCatalog;
    BarFeatureCatalog? barFeatures;
    internal BarFeatureCatalog? BarFeatures => barFeatures;

    /// <summary>A picked guardian's path being started: its first point, until a second Shift+click makes the path.</summary>
    (double X, double Z)? zoneFirstPoint;
    internal (double X, double Z)? ZoneFirstPoint => zoneFirstPoint;

    /// <summary>The point of the picked guardian's path being dragged.</summary>
    int zonePoint = -1;

    readonly Dictionary<GuardianZone, List<(double X, double Z)[]>> zoneOutlines = [];

    bool IsGuardian(StructureInstance s) => map?.Session.Structures.Kinds.TryGetValue(s.Kind, out var k) == true && k.Type == StructureType.Guardian;

    /// <summary>Gives the picked guardian a zone (or none), one undo step, keeping it picked.</summary>
    bool SetZone(StructureInstance picked, GuardianZone? zone)
    {
        bool done = Edit(s => s.SetGuardianZone(picked.X, picked.Z, zone));
        ShowItemSelection();
        return done;
    }

    /// <summary>The zone's edge for drawing, traced once for each zone as the map has it.</summary>
    List<(double X, double Z)[]> ZoneOutline(GuardianZone zone)
    {
        if (zoneOutlines.TryGetValue(zone, out var lines))
            return lines;
        var (minX, minZ, maxX, maxZ) = zone.Bounds;
        lines = zone.Outline(Math.Max(8, Math.Max(maxX - minX, maxZ - minZ) / 200));
        if (itemDrag != ItemDrag.ZonePoint)
        {
            if (zoneOutlines.Count > 64)
                zoneOutlines.Clear();
            zoneOutlines[zone] = lines;
        }
        return lines;
    }

    /// <summary>Where dragging a point of the picked guardian's path puts it: moved as far as the pointer, to the nearest unit.</summary>
    GuardianZone? ZoneDragTarget(StructureInstance picked) =>
        itemDrag == ItemDrag.ZonePoint && picked.Zone is GuardianZone zone && zonePoint >= 0 && zonePoint < zone.Points.Count
            ? zone.WithPoint(zonePoint, (Math.Round(zone.Points[zonePoint].X + itemDragNow.X - itemDragFrom.X), Math.Round(zone.Points[zonePoint].Z + itemDragNow.Z - itemDragFrom.Z)))
            : null;

    /// <summary>
    /// Path editing for a picked guardian, in Select: Shift+click adds a point at the end (the first two make the path),
    /// Ctrl+click on a point removes it and on a run puts one in, and a point is dragged to move it. True when it took the click.
    /// </summary>
    bool ZonePointerDown(StrokeEventArgs e)
    {
        if (CurrentStructure() is not StructureInstance picked || !IsGuardian(picked))
        {
            zoneFirstPoint = null;
            return false;
        }
        double reach = Math.Max(10 * mapView.View.UnitsPerPixel, 8);
        var at = (X: Math.Round(e.X), Z: Math.Round(e.Z));
        var zone = picked.Zone;
        int hit = -1;
        if (zone != null)
        {
            double best = reach;
            for (int i = 0; i < zone.Points.Count; i++)
                if (Math.Sqrt((zone.Points[i].X - e.X) * (zone.Points[i].X - e.X) + (zone.Points[i].Z - e.Z) * (zone.Points[i].Z - e.Z)) is double d && d <= best)
                    (hit, best) = (i, d);
        }
        if (e.Modifiers.HasFlag(Keys.Shift))
        {
            e.Cancel = true; // a click, not a drag
            if (zone != null)
                SetZone(picked, zone.Inserted(zone.Points.Count, at));
            else if (zoneFirstPoint is var (fx, fz))
            {
                if (SetZone(picked, new GuardianZone([(fx, fz), at], itemsPanel.GuardianDistance, false)))
                    zoneFirstPoint = null;
            }
            else
            {
                zoneFirstPoint = at;
                Log($"{picked.Kind}'s path starts at ({at.X:0}, {at.Z:0}): Shift+click where it goes next.");
                UpdateMarks();
            }
            return true;
        }
        if (e.Modifiers.HasFlag(Keys.Control) && zone != null)
        {
            if (hit >= 0)
            {
                e.Cancel = true;
                if (zone.Points.Count > 2)
                    SetZone(picked, zone.Removed(hit));
                else if (SetZone(picked, null))
                {
                    // Down to one point: it stays as the start of a path, for the next Shift+click.
                    zoneFirstPoint = zone.Points[1 - hit];
                    Log($"{picked.Kind}'s path needs two points: Shift+click to go on from ({zoneFirstPoint.Value.X:0}, {zoneFirstPoint.Value.Z:0}).");
                    UpdateMarks();
                }
                return true;
            }
            var (run, distance) = zone.NearestRun(e.X, e.Z);
            if (run >= 0 && distance <= reach)
            {
                e.Cancel = true;
                SetZone(picked, zone.Inserted(run + 1, at));
                return true;
            }
            return false;
        }
        if (hit >= 0)
        {
            itemDragFrom = itemDragNow = (e.X, e.Z);
            (itemDrag, zonePoint) = (ItemDrag.ZonePoint, hit);
            UpdateMarks();
            return true;
        }
        zoneFirstPoint = null;
        return false;
    }

    void ItemsPointerDown(StrokeEventArgs e)
    {
        var s = map!.Session;
        if (itemsPanel.Mode == ItemsMode.Place)
        {
            e.Cancel = true; // a click, not a drag
            PlaceItem(Math.Round(e.X), Math.Round(e.Z));
            return;
        }
        if (ZonePointerDown(e))
            return;
        double perPixel = mapView.View.UnitsPerPixel;
        double Distance(double x, double z) => Math.Sqrt((x - e.X) * (x - e.X) + (z - e.Z) * (z - e.Z));
        itemDragFrom = itemDragNow = (e.X, e.Z);
        // Start positions first (their markers are the biggest), then the selection, then any item.
        if (s.Info.StartPositions.Where(p => Distance(p.X, p.Z) <= 12 * perPixel).MinBy(p => Distance(p.X, p.Z)) is MapCore.StartPosition start)
        {
            itemDrag = ItemDrag.Start;
            draggedTeam = start.Team;
            startOrigin = (start.X, start.Z);
            UpdateMarks();
            return;
        }
        var all = s.Items.All;
        var selected = ResolveSelection(all);
        double reach = Math.Max(8 * perPixel, 6);
        var hit = selected.Where(i => Distance(i.X, i.Z) <= reach).MinBy(i => Distance(i.X, i.Z))
                  ?? all.Where(i => Distance(i.X, i.Z) <= reach).MinBy(i => Distance(i.X, i.Z));
        bool control = e.Modifiers.HasFlag(Keys.Control);
        // A repair marker before the structures (it may stand beside one): a click picks it, a drag moves it.
        if (hit == null && s.StructureMarkerAt(e.X, e.Z, Math.Max(10 * perPixel, 10)) is var (owner, markerX, markerZ))
        {
            selectedStructure = owner;
            selectedMarker = (markerX, markerZ);
            itemDrag = ItemDrag.Marker;
            selectedItems = [];
            ShowItemSelection();
            return;
        }
        selectedMarker = null;
        selectedStructure = hit == null ? s.StructureAt(e.X, e.Z) : null;
        if (selectedStructure != null)
        {
            itemDrag = ItemDrag.Structure; // a click picks the door or tunnel; a drag moves it
            selectedItems = [];
            ShowItemSelection();
            Log(IsGuardian(selectedStructure)
                ? $"{selectedStructure.Kind} at ({selectedStructure.X}, {selectedStructure.Z}): Shift+click to draw the path it guards; its look, times, targets and zone are under Guardian tower on the right; Delete removes it."
                : $"{selectedStructure.Kind} at ({selectedStructure.X}, {selectedStructure.Z}): its size and look are under Door or tunnel on the right; Delete removes it.");
            return;
        }
        if (hit == null)
        {
            itemDrag = ItemDrag.Box; // drag out a box to select
            boxAdds = control;
            UpdateMarks();
            return;
        }
        if (control)
        {
            var key = KeyOf(hit);
            if (!selectedItems.Remove(key))
                selectedItems.Add(key);
            e.Cancel = true;
            ShowItemSelection();
            return;
        }
        if (!selected.Contains(hit))
            selectedItems = [KeyOf(hit)];
        itemDrag = ItemDrag.Items;
        ShowItemSelection();
    }

    void ItemsPointerDrag(StrokeEventArgs e)
    {
        if (itemDrag == ItemDrag.None)
            return;
        itemDragNow = (e.X, e.Z);
        UpdateMarks();
    }

    void ItemsPointerUp(StrokeEventArgs e)
    {
        if (map == null || itemDrag == ItemDrag.None)
            return;
        var drag = itemDrag;
        itemDragNow = (e.X, e.Z);
        double dx = Math.Round(itemDragNow.X - itemDragFrom.X), dz = Math.Round(itemDragNow.Z - itemDragFrom.Z);
        var s = map.Session;
        switch (drag)
        {
            case ItemDrag.Items when dx != 0 || dz != 0:
            {
                var items = ResolveSelection(s.Items.All);
                selectedItems = items.Select(i => KeyOf(i) with { X = i.X + dx, Z = i.Z + dz }).ToList();
                if (!Edit(session => session.MoveItems(items, dx, dz)))
                    selectedItems = items.Select(KeyOf).ToList();
                break;
            }
            case ItemDrag.Structure when CurrentStructure() is StructureInstance picked && StructureDragTarget(picked) is var (tx, tz) && (tx != picked.X || tz != picked.Z):
            {
                if (Edit(session => session.MoveStructure(picked.X, picked.Z, tx, tz)))
                    selectedStructure = picked with { X = tx, Z = tz };
                break;
            }
            case ItemDrag.Marker when selectedMarker is var (mx, mz) && MarkerDragTarget(mx, mz) is var (tx, tz) && (tx != mx || tz != mz):
            {
                if (Edit(session => session.MoveStructureMarker(mx, mz, tx, tz)))
                    selectedMarker = (tx, tz);
                break;
            }
            case ItemDrag.ZonePoint when CurrentStructure() is StructureInstance picked && ZoneDragTarget(picked) is GuardianZone moved && moved.Points[zonePoint] != picked.Zone!.Points[zonePoint]:
            {
                itemDrag = ItemDrag.None;
                SetZone(picked, moved);
                return;
            }
            case ItemDrag.Start when dx != 0 || dz != 0:
            {
                double x = Math.Clamp(Math.Round(startOrigin.X + dx), 0, s.MapWidth), z = Math.Clamp(Math.Round(startOrigin.Z + dz), 0, s.MapHeight);
                Edit(session => session.MoveStartPosition(draggedTeam, x, z));
                break;
            }
            case ItemDrag.Box:
            {
                double x1 = Math.Min(itemDragFrom.X, itemDragNow.X), x2 = Math.Max(itemDragFrom.X, itemDragNow.X);
                double z1 = Math.Min(itemDragFrom.Z, itemDragNow.Z), z2 = Math.Max(itemDragFrom.Z, itemDragNow.Z);
                var inside = s.Items.All.Where(i => i.X >= x1 && i.X <= x2 && i.Z >= z1 && i.Z <= z2).Select(KeyOf);
                selectedItems = boxAdds ? [.. selectedItems.Union(inside)] : [.. inside];
                break;
            }
        }
        itemDrag = ItemDrag.None;
        ShowItemSelection();
    }

    /// <summary>Where dragging the picked door or tunnel puts its centre: moved as far as the pointer, on the 16-unit grid.</summary>
    (double X, double Z) StructureDragTarget(StructureInstance picked) =>
        (Math.Round((picked.X + itemDragNow.X - itemDragFrom.X) / 16) * 16, Math.Round((picked.Z + itemDragNow.Z - itemDragFrom.Z) / 16) * 16);

    /// <summary>Where dragging the picked repair marker puts it: moved as far as the pointer, on the 8-unit grid.</summary>
    (double X, double Z) MarkerDragTarget(double x, double z) =>
        (Math.Round((x + itemDragNow.X - itemDragFrom.X) / 8) * 8, Math.Round((z + itemDragNow.Z - itemDragFrom.Z) / 8) * 8);

    void CancelItemDrag()
    {
        if (itemDrag == ItemDrag.None)
            return;
        itemDrag = ItemDrag.None;
        UpdateMarks();
    }

    void PlaceItem(double x, double z)
    {
        var name = itemsPanel.FeatureName;
        if (name.Length == 0)
        {
            Log("Place: type or pick a feature name first.");
            return;
        }
        if (name.StartsWith(MarkerPrefix, StringComparison.Ordinal))
        {
            var at = name[MarkerPrefix.Length..].Split(',');
            if (at.Length == 2 && double.TryParse(at[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double sx) &&
                double.TryParse(at[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double sz) && Edit(s => s.AddStructureMarker(sx, sz, x, z)))
            {
                selectedStructure = map!.Session.Structures.Instances.FirstOrDefault(i => i.X == sx && i.Z == sz);
                selectedMarker = null;
            }
            ShowItemSelection();
            return;
        }
        if (name.StartsWith(StructurePrefix, StringComparison.Ordinal))
        {
            // By the Heading box, never at random, snapped to a quarter turn (the engine turns blocked footprints only by quarters).
            int facing = (int)Math.Round(itemsPanel.HeadingBox / 90) & 3;
            if (Edit(s => s.PlaceStructure(name[StructurePrefix.Length..], x, z, facing, placeStructureBroken)))
            {
                selectedStructure = map!.Session.StructureAt(x, z);
                if (itemsPanel.RandomHeading)
                    Log($"Doors and tunnels are placed by the Heading box ({facing * 90}°), not at random: turn one with Q / E in Select.");
            }
            ShowItemSelection();
            return;
        }
        var store = itemsPanel.Store;
        double heading = itemsPanel.NewHeading(random);
        var kind = itemsPanel.PlaceKind;
        if (Edit(s => s.AddItem(store, name, x, z, heading, kind)) && map!.Session.Items.All.LastOrDefault(i => i.Source == store && i.Name == name && i.X == x && i.Z == z) is MapItem added)
            selectedItems = [KeyOf(added)];
        ShowItemSelection();
    }

    void ItemsStrokeStarted(StrokeEventArgs e)
    {
        var s = map!.Session;
        if (itemsPanel.Mode == ItemsMode.Scatter && itemsPanel.FeatureName.Length == 0)
        {
            Log("Scatter: type or pick a feature name first.");
            e.Cancel = true;
            return;
        }
        if (itemsPanel.Mode == ItemsMode.Scatter && (itemsPanel.FeatureName.StartsWith(StructurePrefix, StringComparison.Ordinal) ||
                                                     itemsPanel.FeatureName.StartsWith(MarkerPrefix, StringComparison.Ordinal)))
        {
            Log("Scatter places features: doors, tunnels and repair markers are placed one at a time, with Place.");
            e.Cancel = true;
            return;
        }
        strokeFailed = false;
        scattered.Clear();
        s.BeginGroup(itemsPanel.Mode == ItemsMode.Grass
            ? itemsPanel.GrassValue == 0 ? "erase grass" : "paint grass"
            : $"scatter {itemsPanel.FeatureName}");
    }

    /// <summary>
    /// Scatter: a few random points in the brush, each kept only if it is at least the spacing from every item
    /// (including those placed earlier in this stroke). Grass: paints the brush.
    /// </summary>
    void ItemsDab(StrokeEventArgs e)
    {
        var s = map!.Session;
        double radius = itemsPanel.Radius;
        try
        {
            if (itemsPanel.Mode == ItemsMode.Grass)
            {
                s.PaintGrass(e.X, e.Z, radius, itemsPanel.GrassValue);
                return;
            }
            double spacing = itemsPanel.Spacing;
            var near = s.Items.All.Where(i => Math.Abs(i.X - e.X) <= radius + spacing && Math.Abs(i.Z - e.Z) <= radius + spacing)
                .Select(i => (i.X, i.Z)).Concat(scattered).ToList();
            int attempts = Math.Clamp((int)Math.Ceiling(Math.PI * radius * radius / (spacing * spacing) * 0.3), 1, 24);
            for (int k = 0; k < attempts; k++)
            {
                double r = radius * Math.Sqrt(random.NextDouble()), a = random.NextDouble() * 2 * Math.PI;
                double x = Math.Round(e.X + r * Math.Cos(a)), z = Math.Round(e.Z + r * Math.Sin(a));
                if (x < 0 || z < 0 || x >= s.MapWidth || z >= s.MapHeight || near.Any(p => (p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z) < spacing * spacing))
                    continue;
                s.AddItem(itemsPanel.Store, itemsPanel.FeatureName, x, z, itemsPanel.NewHeading(random), itemsPanel.PlaceKind);
                near.Add((x, z));
                scattered.Add((x, z));
            }
        }
        catch (InvalidOperationException ex)
        {
            strokeFailed = true;
            ShowError("The brush could not be applied", ex);
        }
    }

    /// <summary>The grass brush shows the grass layer.</summary>
    async Task FollowItemsMode()
    {
        if (map == null || busy)
            return;
        if (itemsPanel.Mode == ItemsMode.Grass && mapView.Layer != MapViewLayer.Grass)
            await SelectLayerAsync(MapViewLayer.Grass);
        else if (itemsPanel.Mode != ItemsMode.Grass && mapView.Layer == MapViewLayer.Grass)
            await SelectLayerAsync(MapViewLayer.Texture);
    }

    // ---------------------------------------------------------------- layer images

    async Task ImportWithDialogAsync(string layer)
    {
        if (map == null || busy)
            return;
        using var dialog = new OpenFileDialog { Filter = "PNG images (*.png)|*.png", Title = "Import layer" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            await ImportLayerAsync(layer, dialog.FileName);
    }

    async Task ExportWithDialogAsync(string layer, string fileName)
    {
        if (map == null || busy)
            return;
        using var dialog = new SaveFileDialog { Filter = "PNG images (*.png)|*.png", Title = "Export layer", FileName = fileName };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            await ExportLayerAsync(layer, dialog.FileName);
    }

    /// <summary>Reads a layer from PNG in the background, as one undoable step; logs what changed.</summary>
    internal async Task<bool> ImportLayerAsync(string layer, string png)
    {
        if (map == null || busy)
            return false;
        var s = map.Session;
        string note = "";
        if (!await RunBusy($"Importing {Path.GetFileName(png)}", _ => Task.Run(() => note = s.ImportLayer(layer, png))))
            return false;
        Log("Imported " + note);
        return true;
    }

    internal async Task<bool> ExportLayerAsync(string layer, string png)
    {
        if (map == null || busy)
            return false;
        var s = map.Session;
        string note = "";
        if (!await RunBusy($"Exporting {Path.GetFileName(png)}", _ => Task.Run(() => note = s.ExportLayer(layer, png))))
            return false;
        Log($"Exported {note} to {png}");
        return true;
    }

    void OnSessionChanged(object? sender, MapChange change)
    {
        // Saving runs in the background and reports what it changed from there.
        if (InvokeRequired)
        {
            BeginInvoke(() => OnSessionChanged(sender, change));
            return;
        }
        mapView.Redraw();
        terrainWindow?.View.MapChanged();
        if (change.Layers.HasFlag(MapLayers.MapInfo) && map != null)
            colourPanel.ShowValues(map.Session); // after undo, or a Save As name change
        if (change.Layers.HasFlag(MapLayers.Metal) || change.Layers.HasFlag(MapLayers.Heights))
            RefreshSpots(); // spot notes depend on the ground too
        if (change.Layers.HasFlag(MapLayers.Items))
            ShowItemSelection(); // drops selected items that no longer exist (removed, or undone)
        if (change.Layers.HasFlag(MapLayers.Items))
        {
            int version = features.Version;
            RefreshItemNames(); // an asset added or undone changes what can be placed
            if (features.Version != version)
                ShowAssets(libraryChanged: false); // and what the map has
        }
        if (RampActive && change.Layers.HasFlag(MapLayers.Heights))
            UpdateRampPreview(); // heights that follow the ground follow it after edits and undo
        UpdateTitle();
        UpdateCommands();
    }

    // ---------------------------------------------------------------- 3D view

    /// <summary>Opens (or brings forward) the 3D view of the open map. With <paramref name="show"/> false it is created hidden (self-test).</summary>
    internal TerrainWindow? OpenTerrainWindow(bool show = true)
    {
        if (map == null || busy)
            return null;
        if (terrainWindow == null)
        {
            terrainView = new TerrainView(map.Renderer) { BarFeatures = barFeatures };
            var window = new TerrainWindow(LookAt2D);
            window.FormClosed += (_, _) =>
            {
                if (terrainWindow == window)
                {
                    terrainWindow = null;
                    terrainView?.Dispose();
                    terrainView = null;
                }
            };
            terrainWindow = window;
            var screen = Screen.FromControl(this).WorkingArea;
            window.Location = new Point(Math.Max(screen.Left, Math.Min(Right - window.Width - 40, screen.Right - window.Width)), Math.Max(screen.Top, Top + 90));
            window.View.Attach(terrainView, map.Session);
        }
        terrainWindow.Text = $"3D view — {map.Session.Info.Name} {map.Session.Info.Version}";
        if (show)
        {
            if (!terrainWindow.Visible)
                terrainWindow.Show(this);
            terrainWindow.Activate();
        }
        return terrainWindow;
    }

    /// <summary>What the 2D view shows: its centre and width in game units.</summary>
    (double X, double Z, double Width)? LookAt2D()
    {
        if (map == null)
            return null;
        var (x, z) = mapView.View.ToWorld(mapView.ClientSize.Width / 2.0, mapView.ClientSize.Height / 2.0);
        return (x, z, Math.Max(1, mapView.ClientSize.Width) * mapView.View.UnitsPerPixel);
    }

    // ---------------------------------------------------------------- view

    internal async Task SelectLayerAsync(MapViewLayer layer)
    {
        if (map == null || busy)
        {
            SyncLayer();
            return;
        }
        if (map.Renderer.Unavailable(layer) is string why)
        {
            Log(why);
            SyncLayer();
            return;
        }
        // The .dds layers are large: decode them in the background.
        if (layer is MapViewLayer.Splat or MapViewLayer.Normals)
        {
            var renderer = map.Renderer;
            if (!await RunBusy($"Loading the {layer.ToString().ToLowerInvariant()} view", _ => Task.Run(() => renderer.Prepare(layer))))
            {
                SyncLayer();
                return;
            }
        }
        mapView.Layer = layer;
        SyncLayer();
    }

    void SetOverlay(MapOverlays overlay, bool on)
    {
        if (syncing)
            return;
        mapView.Overlays = on ? mapView.Overlays | overlay : mapView.Overlays & ~overlay;
        SyncOverlays();
    }

    void SyncLayer()
    {
        syncing = true;
        foreach (var (layer, item) in layerItems)
            item.Checked = layer == mapView.Layer;
        layerBox.SelectedIndex = Array.FindIndex(LayerNames, l => l.Layer == mapView.Layer);
        syncing = false;
    }

    void SyncOverlays()
    {
        syncing = true;
        foreach (var (overlay, (item, button)) in overlayItems)
            item.Checked = button.Checked = mapView.Overlays.HasFlag(overlay);
        syncing = false;
    }

    void ShowCursor((double X, double Z)? position)
    {
        if (map == null || busy || position is not (double x, double z))
        {
            cursorLabel.Text = "";
            return;
        }
        var p = map.Session.Probe(x, z);
        cursorLabel.Text = !p.OnMap
            ? $"x {x:0}   z {z:0}   (off the map)"
            : $"x {x:0}   z {z:0}      height {p.Height:0.0}      metal {p.MetalRaw}" +
              $" (an extractor here makes {p.ExtractorIncome:0.00}/s)      grass {p.Grass}";
    }

    void ShowViewState()
    {
        zoomLabel.Text = mapView.HasMap ? $"Zoom {100 / mapView.View.UnitsPerPixel:0.#}%" : "";
        noteLabel.Text = mapView.HasMap ? mapView.Note ?? "" : "";
    }

    void UpdateTitle()
    {
        if (map == null)
        {
            Text = AppName;
            return;
        }
        var s = map.Session;
        Text = $"{s.Info.Name} {s.Info.Version}{(HasUnpackedChanges ? " *" : "")} — {Path.GetFileName(savedAs ?? map.Archive)} — {AppName}";
    }

    void UpdateCommands()
    {
        bool ready = map != null && !busy;
        foreach (var item in needMap)
            item.Enabled = ready;
        foreach (var (layer, item) in layerItems)
            item.Enabled = ready && map!.Renderer.Unavailable(layer) == null;
        foreach (var (_, layer, _, needs) in ImageLayers)
        {
            bool available = ready && (needs is not MapViewLayer v || map!.Renderer.Unavailable(v) == null);
            layerFileItems[layer].Import.Enabled = layerFileItems[layer].Export.Enabled = available;
        }
        heightTools.Enabled = textureTools.Enabled = colourPanel.Enabled = metalPanel.Enabled = itemsPanel.Enabled = ready;
        beautifyPanel.SetEnabled(ready);
        var s = map?.Session;
        undoItem.Enabled = undoButton.Enabled = ready && s!.CanUndo;
        redoItem.Enabled = redoButton.Enabled = ready && s!.CanRedo;
        undoItem.Text = ready && s!.CanUndo ? $"&Undo {Short(s.UndoDescription)}" : "&Undo";
        redoItem.Text = ready && s!.CanRedo ? $"&Redo {Short(s.RedoDescription)}" : "&Redo";
        undoButton.ToolTipText = undoItem.Text.Replace("&", "") + " (Ctrl+Z)";
        redoButton.ToolTipText = redoItem.Text.Replace("&", "") + " (Ctrl+Y)";
    }

    /// <summary>An operation as the Edit menu names it: cut short when it lists many things (a guardian's 86 unit names).</summary>
    static string Short(string? description) =>
        description is { Length: > 120 } ? description[..117] + "…" : description ?? "";

    /// <summary>
    /// Adds a line to the log (the bottom panel's Log tab). The newest line also shows in the status bar, and the
    /// tab counts lines not yet seen.
    /// </summary>
    void Log(string text)
    {
        messages.Items.Add($"{DateTime.Now:HH:mm:ss}  {text}");
        messages.TopIndex = Math.Max(0, messages.Items.Count - 1);
        lastLogLabel.Text = text.Length > 160 ? text[..157] + "…" : text;
        if (bottomTabs.SelectedTab != logPage || browserSplit.Panel2Collapsed)
            logPage.Text = $"Log ({++unreadLog})";
    }

    void MarkLogRead()
    {
        unreadLog = 0;
        logPage.Text = "Log";
    }

    /// <summary>Shows the log: the bottom panel, on its Log tab.</summary>
    internal void ShowLog()
    {
        browserItem.Checked = true;
        bottomTabs.SelectedTab = logPage;
        MarkLogRead();
    }

    internal TabControl BottomTabs => bottomTabs;

    void ShowError(string what, Exception ex)
    {
        if (ex is OutOfMemoryException or InsufficientExecutionStackException)
            ex = new InvalidOperationException("The editor ran out of memory. The map is as it was before this step, and every " +
                "edit so far is in the recovery journal. Closing other programs (or the 3D view) may help; then save with Save As.", ex);
        bool expected = ex is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException
            or FormatException or NotSupportedException or MapCore.Lua.LuaSyntaxException;
        Log($"{what}: {(expected ? ex.Message : ex.ToString())}");
        if (!Quiet)
            MessageBox.Show(this, $"{what}.\n\n{ex.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    void ShowControls() => MessageBox.Show(this,
        "Mouse wheel: zoom about the cursor\n" +
        "Left button: the tool of the Heights or Texture tab (with Pan, it drags the view)\n" +
        "Right or middle button drag: pan\n" +
        "Arrow keys: pan     + / -: zoom     Home: whole map\n" +
        "Tools (with the map focused): P pan, R raise, L lower, S smooth, F flatten, H set height, N noise, M ramp\n" +
        "Ramp: click to place it, drag it to move it, drag an end handle to stretch and turn it, the side handle to widen it;\n" +
        "    Q / E turn it, [ / ] change its width, Enter applies it\n" +
        "Texture tools: P pan, B paint, T tint, A adjust, C clone, D splat\n" +
        "[ / ]: smaller / larger brush     Ctrl+click: pick the height (Set height) or colour (Paint, Tint)\n" +
        "Alt+click with Clone: choose where to copy from\n" +
        "Metal tab: S select / move (drag a spot), A add, P pan; Delete removes the selected spot\n" +
        "Items tab: S select / move (Ctrl+click adds, drag a box), A place, C scatter, G grass, P pan; Q / E turn, Delete removes\n" +
        "Ctrl+1 … Ctrl+7: texture, heights, metal, grass, splat, normals, minimap     F3: 3D view\n" +
        "Ctrl+Z / Ctrl+Y: undo / redo (a whole stroke is one step)\n" +
        "Symmetry (toolbar): Mirror makes every edit again across the axis chosen; Mirror + flip, at the opposite side and corner\n" +
        "    (a half turn). The line is drawn on the map, and the brush's copy dashed; an edit and its copy undo together",
        "Controls", MessageBoxButtons.OK, MessageBoxIcon.Information);
}
