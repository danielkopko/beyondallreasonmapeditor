namespace MapEditor;

/// <summary>Small layout helpers shared by the tool panels.</summary>
static class PanelLayout
{
    public static decimal Clamp(NumericUpDown box, double value) =>
        Math.Clamp(Math.Round((decimal)value, box.DecimalPlaces), box.Minimum, box.Maximum);

    public static NumericUpDown Number(decimal min, decimal max, decimal value, int decimals, decimal step) => new()
    {
        Minimum = min,
        Maximum = max,
        Value = value,
        DecimalPlaces = decimals,
        Increment = step,
        Width = 80,
        TextAlign = HorizontalAlignment.Right,
    };

    /// <summary>A three-column grid: label, input, and a note or button.</summary>
    public static TableLayoutPanel Grid()
    {
        var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return grid;
    }

    public static void AddRow(TableLayoutPanel grid, string label, Control input, string unit) =>
        AddRow(grid, label, input, new Label { Text = unit, AutoSize = true, ForeColor = BarTheme.Dim, Anchor = AnchorStyles.Left });

    public static void AddRow(TableLayoutPanel grid, string label, Control input, Control after)
    {
        int row = grid.RowCount++;
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        grid.Controls.Add(input, 1, row);
        after.Anchor = AnchorStyles.Left;
        grid.Controls.Add(after, 2, row);
    }

    /// <summary>A control across all three columns, on a new row.</summary>
    public static void AddWide(TableLayoutPanel grid, Control control)
    {
        grid.Controls.Add(control, 0, grid.RowCount++);
        grid.SetColumnSpan(control, 3);
    }

    public static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(220, 0),
        ForeColor = BarTheme.Dim,
        Margin = new Padding(3, 4, 3, 2),
    };

    /// <summary>A section of a panel: a heading as BAR's settings have them, its contents below.</summary>
    public static GroupBox Group(string title, Control content)
    {
        var box = new BarGroupBox { Text = title, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(6) };
        content.Dock = DockStyle.Fill;
        box.Controls.Add(content);
        return box;
    }

    /// <summary>Radio buttons drawn as buttons, two per row.</summary>
    public static TableLayoutPanel ButtonGrid()
    {
        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        return grid;
    }

    public static RadioButton ToolButton(string text, bool selected) => new()
    {
        Text = text,
        Appearance = Appearance.Button,
        TextAlign = ContentAlignment.MiddleCenter,
        Dock = DockStyle.Fill,
        Height = 28,
        Checked = selected,
    };
}
