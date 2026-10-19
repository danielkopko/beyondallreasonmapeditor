using MapCore;
using MapCore.Editing;
using static MapEditor.PanelLayout;

namespace MapEditor;

/// <summary>A mapinfo.lua value of three numbers changed in the panel.</summary>
sealed record ColourEdit(string Path, double R, double G, double B);

/// <summary>
/// mapinfo.lua's colours and lighting: sky, fog, sun, ground and unit light, water. Each row shows the three numbers
/// and a swatch (click it to pick a colour). Values the file doesn't set show the engine's default. Rows that code
/// in mapconfig/mapinfo may change after mapinfo.lua is read are marked: editing them may not show in game.
/// </summary>
sealed class ColourPanel : UserControl
{
    enum Kind { Colour, Absorb, Direction }

    /// <summary>Path, label, kind, and the engine's default (RecoilEngine MapInfo / water defaults).</summary>
    static readonly (string Path, string Label, Kind Kind, double[] Default)[] Rows =
    [
        ("atmosphere.skyColor", "Sky", Kind.Colour, [0.1, 0.15, 0.7]),
        ("atmosphere.fogColor", "Fog", Kind.Colour, [0.7, 0.7, 0.8]),
        ("atmosphere.sunColor", "Sun", Kind.Colour, [1, 1, 1]),
        ("atmosphere.cloudColor", "Clouds", Kind.Colour, [1, 1, 1]),
        ("lighting.sunDir", "Sun direction", Kind.Direction, [0, 1, 2]),
        ("lighting.groundAmbientColor", "Ground ambient", Kind.Colour, [0.5, 0.5, 0.5]),
        ("lighting.groundDiffuseColor", "Ground diffuse", Kind.Colour, [0.5, 0.5, 0.5]),
        ("lighting.groundSpecularColor", "Ground specular", Kind.Colour, [0.1, 0.1, 0.1]),
        ("lighting.unitAmbientColor", "Unit ambient", Kind.Colour, [0.4, 0.4, 0.4]),
        ("lighting.unitDiffuseColor", "Unit diffuse", Kind.Colour, [0.7, 0.7, 0.7]),
        ("water.surfaceColor", "Water surface", Kind.Colour, [0.75, 0.8, 0.85]),
        ("water.planeColor", "Water plane", Kind.Colour, [0, 0.4, 0]),
        ("water.baseColor", "Sea bed tint", Kind.Colour, [0, 0, 0]),
        ("water.minColor", "Sea bed darkest", Kind.Colour, [0, 0, 0]),
        ("water.absorb", "Water absorb", Kind.Absorb, [0, 0, 0]),
    ];

    readonly Dictionary<string, (NumericUpDown[] Boxes, Button Swatch, Label Note, Kind Kind)> rows = new(StringComparer.OrdinalIgnoreCase);
    readonly ToolTip tips = new();
    bool syncing;

    public ColourPanel()
    {
        AutoScroll = true;
        Padding = new Padding(6);
        var layout = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Top, AutoSize = true };
        Controls.Add(layout);

        // Two lines per value: its name, swatch and override mark; then its three numbers.
        var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill };
        foreach (var (path, label, kind, _) in Rows)
        {
            var header = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
            var name = BarTheme.Strong(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 5, 6, 0) });
            tips.SetToolTip(name, path);
            var swatch = BarTheme.Swatch(new Button { Width = 34, Height = 20, Visible = kind == Kind.Colour, Margin = new Padding(0, 2, 6, 0) });
            swatch.Click += (_, _) => PickColour(path);
            tips.SetToolTip(swatch, "Pick a colour");
            var note = new Label { AutoSize = true, ForeColor = BarTheme.Warning, Margin = new Padding(0, 5, 0, 0) };
            header.Controls.AddRange([name, swatch, note]);
            AddWide(grid, header);

            int row = grid.RowCount++;
            var boxes = new NumericUpDown[3];
            for (int i = 0; i < 3; i++)
            {
                boxes[i] = kind switch
                {
                    Kind.Absorb => Number(0, 1, 0, 4, 0.0005m),
                    Kind.Direction => Number(-10, 10, 0, 3, 0.05m),
                    _ => Number(0, 4, 0, 3, 0.02m),
                };
                boxes[i].Width = 84;
                boxes[i].ValueChanged += (_, _) => Changed(path);
                grid.Controls.Add(boxes[i], i, row);
            }
            rows[path] = (boxes, swatch, note, kind);
        }
        layout.Controls.Add(Group("mapinfo.lua colours", grid));
        layout.Controls.Add(Hint("Values are 0-1 (red, green, blue). Grey numbers are the engine's defaults: this map doesn't set them, and changing one adds it to mapinfo.lua. ⚠ marks values that mapconfig/mapinfo code may change after mapinfo.lua is read (hover for details), so an edit may not show in game. The 3D view shows sky, sun, ground light and water changes at once."));
    }

    /// <summary>A row was edited by the user (each change is one undo step).</summary>
    public event EventHandler<ColourEdit>? ColourChanged;

    /// <summary>Shows the session's values, and marks rows that overrides may change.</summary>
    public void ShowValues(MapEditSession? session)
    {
        syncing = true;
        try
        {
            var overrides = session?.Info.FindOverrides(session.Package.Root) ?? [];
            foreach (var (path, _, _, fallback) in Rows)
            {
                var (boxes, swatch, note, kind) = rows[path];
                var values = session?.Info.GetNumbers(path);
                bool set = values is { Length: >= 3 };
                var shown = set ? values! : fallback;
                for (int i = 0; i < 3; i++)
                {
                    boxes[i].Value = Clamp(boxes[i], shown[i]);
                    boxes[i].ForeColor = set ? BarTheme.Text : BarTheme.Dim;
                    boxes[i].Enabled = session != null;
                }
                swatch.BackColor = kind == Kind.Colour ? ToColor(shown) : BarTheme.Panel;
                swatch.Enabled = session != null;
                var affecting = MapInfo.Affecting(overrides, path).ToList();
                note.Text = affecting.Count > 0 ? "⚠ may be overridden" : "";
                tips.SetToolTip(note, string.Join("\n", affecting.Select(o => $"{o.File}: {o.Path} ({o.Kind})")));
            }
        }
        finally
        {
            syncing = false;
        }
    }

    /// <summary>For the self-test: the numbers a row shows.</summary>
    internal double[] Values(string path) => rows[path].Boxes.Select(b => (double)b.Value).ToArray();

    /// <summary>For the self-test: types a value into one box of a row, as the user would.</summary>
    internal void SetValue(string path, int index, double value) => rows[path].Boxes[index].Value = Clamp(rows[path].Boxes[index], value);

    /// <summary>For the self-test: whether a row is marked as possibly overridden.</summary>
    internal bool Marked(string path) => rows[path].Note.Text.Length > 0;

    void Changed(string path)
    {
        if (syncing)
            return;
        var (boxes, swatch, _, kind) = rows[path];
        var v = boxes.Select(b => (double)b.Value).ToArray();
        if (kind == Kind.Colour)
            swatch.BackColor = ToColor(v);
        foreach (var box in boxes)
            box.ForeColor = BarTheme.Text;
        ColourChanged?.Invoke(this, new ColourEdit(path, v[0], v[1], v[2]));
    }

    void PickColour(string path)
    {
        var (boxes, _, _, _) = rows[path];
        var current = boxes.Select(b => (double)b.Value).ToArray();
        using var dialog = new ColorDialog { Color = ToColor(current), FullOpen = true };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        syncing = true; // three boxes, one edit
        boxes[0].Value = Clamp(boxes[0], dialog.Color.R / 255.0);
        boxes[1].Value = Clamp(boxes[1], dialog.Color.G / 255.0);
        boxes[2].Value = Clamp(boxes[2], dialog.Color.B / 255.0);
        syncing = false;
        Changed(path);
    }

    static Color ToColor(double[] v) => Color.FromArgb(
        (int)Math.Clamp(Math.Round(v[0] * 255), 0, 255), (int)Math.Clamp(Math.Round(v[1] * 255), 0, 255), (int)Math.Clamp(Math.Round(v[2] * 255), 0, 255));
}
