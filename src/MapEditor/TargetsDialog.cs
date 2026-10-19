using MapCore.Structures;

namespace MapEditor;

/// <summary>
/// A guardian tower's targets: every standard BAR unit with its picture, grouped by faction and kind. Ticked units are
/// destroyed once they come into the tower's zone (their Scavengers variants too); unticked ones pass. Names on the
/// list that the catalog doesn't know (units added to BAR since, or a mod's) are kept, ticked, at the end.
/// </summary>
sealed class TargetsDialog : Form
{
    const int IconSize = 64;

    readonly UnitCatalog catalog;
    readonly HashSet<string> chosen;
    readonly List<string> unknown;
    readonly ListView list = new()
    {
        View = View.LargeIcon, CheckBoxes = true, ShowItemToolTips = true, Dock = DockStyle.Fill, MultiSelect = true,
        HideSelection = false,
    };
    readonly TextBox filter = new() { Width = 200, PlaceholderText = "Filter: name, or what it is" };
    readonly ComboBox faction = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    readonly ComboBox show = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    readonly Label count = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 7, 3, 3) };
    readonly ToolTip tips = new();
    bool filling;

    /// <summary>The pictures, read once (681 of them) and shared by every opening of the dialog.</summary>
    static ImageList? images;
    static UnitCatalog? imagesFor;

    public TargetsDialog(UnitCatalog catalog, IEnumerable<string> targets, string kindName)
    {
        this.catalog = catalog;
        chosen = new HashSet<string>(targets, StringComparer.Ordinal);
        unknown = chosen.Where(n => catalog.Find(n) == null).Order(StringComparer.Ordinal).ToList();

        Text = $"Units {kindName} destroys";
        Font = BarTheme.Font;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        Size = new Size(980, 720);
        MinimumSize = new Size(640, 420);
        Padding = new Padding(8);

        list.LargeImageList = Images(catalog);
        faction.Items.AddRange(["All factions", .. catalog.Units.Select(u => u.Faction).Distinct()]);
        faction.SelectedIndex = 0;
        show.Items.AddRange(["Every unit", "Aircraft only", "Ticked only", "Unticked only"]);
        show.SelectedIndex = 0;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        top.Controls.AddRange([filter, faction, show, count]);
        var presets = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        presets.Controls.Add(new Label { Text = "Tick:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) });
        foreach (var (text, tip, preset) in new (string, string, string)[]
                 {
                     ("Aircraft that attack the ground", "Bombers, gunships, drones, torpedo planes and armed transports: the default. Fighters, scouts, transports and constructors pass.", "default"),
                     ("All aircraft", "Every unit that flies", "air"),
                     ("Everything shown", "Tick every unit the filter shows", "shown"),
                     ("Nothing shown", "Untick every unit the filter shows", "unshown"),
                     ("Nothing", "Untick every unit: the tower destroys nothing", "none"),
                 })
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += (_, _) => Preset(preset);
            tips.SetToolTip(button, tip);
            presets.Controls.Add(button);
        }
        var hint = new Label
        {
            Dock = DockStyle.Bottom, AutoSize = true, ForeColor = BarTheme.Dim, Padding = new Padding(0, 6, 0, 0),
            MaximumSize = new Size(940, 0),
            Text = "Ticked units that come within the zone's distance of its path are doomed: destroyed in the kind's kill time wherever they go " +
                   "next, healed or not. Their Scavengers variants (name_scav) are destroyed too. A unit carried by a transport is judged by the transport.",
        };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange([cancel, ok]);
        Controls.Add(list);
        Controls.Add(presets);
        Controls.Add(top);
        Controls.Add(hint);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;

        filter.TextChanged += (_, _) => Fill();
        faction.SelectedIndexChanged += (_, _) => Fill();
        show.SelectedIndexChanged += (_, _) => Fill();
        list.ItemChecked += (_, e) =>
        {
            if (filling || e.Item.Tag is not string name)
                return;
            if (e.Item.Checked)
                chosen.Add(name);
            else
                chosen.Remove(name);
            Count();
        };
        Fill();
        BarTheme.Apply(this);
    }

    /// <summary>The ticked units' names, in order.</summary>
    public IReadOnlyList<string> Chosen => chosen.Order(StringComparer.Ordinal).ToList();

    /// <summary>The units the filter shows now (for the self-test).</summary>
    internal IReadOnlyList<string> ShownUnits => list.Items.Cast<ListViewItem>().Select(i => (string)i.Tag!).ToList();

    internal string CountText => count.Text;

    internal void SetFilter(string text, int factionIndex = 0, int showIndex = 0)
    {
        filter.Text = text;
        faction.SelectedIndex = factionIndex;
        show.SelectedIndex = showIndex;
        Fill();
    }

    /// <summary>default (aircraft that attack the ground), air, shown, unshown or none.</summary>
    internal void Preset(string preset)
    {
        switch (preset)
        {
            case "default":
                chosen.RemoveWhere(n => catalog.Find(n) != null);
                chosen.UnionWith(catalog.DefaultTargets);
                break;
            case "air":
                chosen.UnionWith(catalog.Aircraft);
                break;
            case "shown":
                chosen.UnionWith(ShownUnits);
                break;
            case "unshown":
                chosen.ExceptWith(ShownUnits);
                break;
            case "none":
                chosen.Clear();
                break;
        }
        Fill();
    }

    void Fill()
    {
        filling = true;
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            list.Groups.Clear();
            string text = filter.Text.Trim();
            string? onlyFaction = faction.SelectedIndex > 0 ? faction.SelectedItem as string : null;
            bool Wanted(string name, bool flies, params string[] about) =>
                (text.Length == 0 || about.Append(name).Any(a => a.Contains(text, StringComparison.OrdinalIgnoreCase))) &&
                show.SelectedIndex switch { 1 => flies, 2 => chosen.Contains(name), 3 => !chosen.Contains(name), _ => true };
            foreach (var (factionName, group, units) in catalog.Grouped())
            {
                if (onlyFaction != null && factionName != onlyFaction)
                    continue;
                var shown = units.Where(u => Wanted(u.Name, u.CanFly, u.DisplayName, u.Description, u.Group)).ToList();
                if (shown.Count == 0)
                    continue;
                var listGroup = new ListViewGroup($"{factionName}: {group}") { CollapsedState = ListViewGroupCollapsedState.Expanded };
                list.Groups.Add(listGroup);
                foreach (var u in shown)
                    list.Items.Add(new ListViewItem(u.DisplayName.Length > 0 ? u.DisplayName : u.Name, list.LargeImageList?.Images.IndexOfKey(u.Name) ?? -1, listGroup)
                    {
                        Tag = u.Name,
                        Checked = chosen.Contains(u.Name),
                        ToolTipText = $"{u.Title}\n{u.Description}" + (u.DefaultDestroy ? "\nDestroyed by default: it can fly and attack the ground." : ""),
                    });
            }
            var stillUnknown = unknown.Where(n => onlyFaction == null && Wanted(n, false)).ToList();
            if (stillUnknown.Count > 0)
            {
                var listGroup = new ListViewGroup("Not in the catalog (kept as they are)");
                list.Groups.Add(listGroup);
                foreach (var n in stillUnknown)
                    list.Items.Add(new ListViewItem(n, listGroup) { Tag = n, Checked = chosen.Contains(n), ToolTipText = $"{n}: not one of BAR's standard units as the editor knows them" });
            }
        }
        finally
        {
            list.EndUpdate();
            filling = false;
        }
        Count();
    }

    void Count()
    {
        int known = chosen.Count(n => catalog.Find(n) != null);
        count.Text = $"{chosen.Count} destroyed of {catalog.Units.Count + unknown.Count}" + (known < chosen.Count ? $" ({chosen.Count - known} not in the catalog)" : "") +
                     $"; {list.Items.Count} shown";
    }

    static ImageList Images(UnitCatalog catalog)
    {
        if (images != null && ReferenceEquals(imagesFor, catalog))
            return images;
        var made = new ImageList { ImageSize = new Size(IconSize, IconSize), ColorDepth = ColorDepth.Depth32Bit };
        foreach (var u in catalog.Units)
        {
            if (catalog.IconPath(u) is not string path)
                continue;
            try
            {
                using var stream = File.OpenRead(path);
                using var image = Image.FromStream(stream);
                made.Images.Add(u.Name, new Bitmap(image));
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                // A missing or broken picture: the unit is listed without one.
            }
        }
        (images, imagesFor) = (made, catalog);
        return made;
    }
}
