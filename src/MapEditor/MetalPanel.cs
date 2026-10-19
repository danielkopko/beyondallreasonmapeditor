using System.ComponentModel;
using System.Globalization;
using static MapEditor.PanelLayout;

namespace MapEditor;

enum MetalMode { Select, Add, Pan }

/// <summary>One row of the spot list.</summary>
sealed record SpotRow(double X, double Z, int Cells, double Income, IReadOnlyList<string> Notes);

/// <summary>
/// The metal tools: select and drag spots, add spots, and the selected spot's income, size and removal; the list of
/// every spot with what an extractor there would make and anything that may stop one being built. The main window
/// applies the edits.
/// </summary>
sealed class MetalPanel : UserControl
{
    static readonly (MetalMode Mode, string Text, Keys Key, string Tip)[] Modes =
    [
        (MetalMode.Select, "Select / move", Keys.S, "Click a spot to select it; drag it to move it (S)"),
        (MetalMode.Add, "Add", Keys.A, "Click to add a spot shaped like this map's usual one (A)"),
        (MetalMode.Pan, "Pan", Keys.P, "Left button pans the view (P)"),
    ];

    readonly Label warnings = new() { AutoSize = true, MaximumSize = new Size(290, 0), ForeColor = BarTheme.Warning, Margin = new Padding(3, 3, 3, 6) };
    readonly Dictionary<MetalMode, RadioButton> modeButtons = [];
    readonly CheckBox moveLook = new() { Text = "Move the look with the spot", AutoSize = true, Checked = true };
    readonly Label selectedTitle = BarTheme.Strong(new Label { AutoSize = true, MaximumSize = new Size(260, 0) });
    readonly NumericUpDown income = Number(0.01m, 50, 2.3m, 2, 0.1m);
    readonly NumericUpDown size = Number(1, 9, 3, 0, 1);
    readonly Button remove = new() { Text = "Remove spot", AutoSize = true };
    readonly Label notes = Hint("");
    readonly ListView list = new()
    {
        View = System.Windows.Forms.View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false,
        Height = 300, Dock = DockStyle.Fill, HeaderStyle = ColumnHeaderStyle.Nonclickable,
    };
    readonly Label totals = Hint("");
    readonly GroupBox selectedBox;
    MetalMode mode = MetalMode.Select;
    bool syncing;

    public MetalPanel()
    {
        AutoScroll = true;
        Padding = new Padding(6);
        var layout = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Top, AutoSize = true };
        Controls.Add(layout);
        layout.Controls.Add(warnings);

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
        layout.Controls.Add(Group("Metal tool", modeGrid));
        tips.SetToolTip(moveLook, "Carry the spot's look (molten-metal splat, texture patch, normal-map outline) to the new place");
        layout.Controls.Add(moveLook);

        var selected = Grid();
        AddWide(selected, selectedTitle);
        AddRow(selected, "Income", income, "metal/s (T1)");
        AddRow(selected, "Size", size, "cells across");
        AddWide(selected, remove);
        AddWide(selected, notes);
        selectedBox = Group("Selected spot", selected);
        layout.Controls.Add(selectedBox);

        list.Columns.Add("#", 32);
        list.Columns.Add("x", 52, HorizontalAlignment.Right);
        list.Columns.Add("z", 52, HorizontalAlignment.Right);
        list.Columns.Add("metal/s", 58, HorizontalAlignment.Right);
        list.Columns.Add("notes", 100);
        var listPanel = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
        listPanel.Controls.Add(list);
        listPanel.Controls.Add(totals);
        layout.Controls.Add(Group("Spots", listPanel));
        layout.Controls.Add(Hint("Delete removes the selected spot. Moves the game would refuse (touching other metal, the map edge) show red while dragging and are not made."));

        income.ValueChanged += (_, _) =>
        {
            if (!syncing)
                IncomeChanged?.Invoke(this, (double)income.Value);
        };
        size.ValueChanged += (_, _) =>
        {
            if (!syncing)
                SpotSizeChanged?.Invoke(this, (int)size.Value);
        };
        remove.Click += (_, _) => RemoveRequested?.Invoke(this, EventArgs.Empty);
        list.SelectedIndexChanged += (_, _) =>
        {
            if (!syncing && list.SelectedIndices.Count == 1)
                SpotChosen?.Invoke(this, list.SelectedIndices[0]);
        };
        ShowSelected(null);
    }

    public event EventHandler? ModeChanged;
    public event EventHandler<int>? SpotChosen;
    public event EventHandler<double>? IncomeChanged;
    public event EventHandler<int>? SpotSizeChanged;
    public event EventHandler? RemoveRequested;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MetalMode Mode
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
    public bool MoveLook { get => moveLook.Checked; set => moveLook.Checked = value; }

    /// <summary>Notes about the whole metal layer; read-only maps disable editing.</summary>
    public void ShowWarnings(IReadOnlyList<string> lines, bool readOnly)
    {
        warnings.Text = string.Join("\n", lines.Select(l => "⚠ " + l));
        warnings.Visible = lines.Count > 0;
        foreach (var button in modeButtons.Values)
            button.Enabled = !readOnly;
        moveLook.Enabled = selectedBox.Enabled = !readOnly;
    }

    public void ShowSpots(IReadOnlyList<SpotRow> rows, int selected)
    {
        syncing = true;
        try
        {
            list.BeginUpdate();
            list.Items.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var item = new ListViewItem([(i + 1).ToString(CultureInfo.CurrentCulture), r.X.ToString("0"), r.Z.ToString("0"), r.Income.ToString("0.00"),
                    r.Notes.Count == 0 ? "" : "⚠ " + string.Join("; ", r.Notes)]);
                if (r.Notes.Count > 0)
                    item.ForeColor = BarTheme.Warning;
                list.Items.Add(item);
            }
            list.EndUpdate();
            if (selected >= 0 && selected < rows.Count)
            {
                list.Items[selected].Selected = true;
                list.EnsureVisible(selected);
            }
            totals.Text = $"{rows.Count} spots; {rows.Sum(r => r.Income):0.00} metal/s with a T1 extractor on every one";
            ShowSelected(selected >= 0 && selected < rows.Count ? rows[selected] : null);
        }
        finally
        {
            syncing = false;
        }
    }

    void ShowSelected(SpotRow? row)
    {
        bool wasSyncing = syncing;
        syncing = true;
        selectedTitle.Text = row == null ? "None: click a spot on the map or in the list" : $"Spot at ({row.X:0}, {row.Z:0}), {row.Cells} cells";
        income.Enabled = size.Enabled = remove.Enabled = row != null;
        if (row != null)
        {
            income.Value = Clamp(income, row.Income);
            size.Value = Clamp(size, Math.Round(Math.Sqrt(row.Cells)));
        }
        notes.Text = row == null ? "" : row.Notes.Count == 0 ? "A T1 extractor can be built here." : "⚠ " + string.Join("\n⚠ ", row.Notes);
        syncing = wasSyncing;
    }

    /// <summary>For the self-test: types a value as the user would.</summary>
    internal void SetIncome(double value) => income.Value = Clamp(income, value);
    internal void SetSize(int cells) => size.Value = Clamp(size, cells);
    internal double ShownIncome => (double)income.Value;
    internal int Rows => list.Items.Count;

    /// <summary>Mode keys (S, A, P); true if the key was used.</summary>
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
