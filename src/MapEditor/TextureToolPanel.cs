using System.ComponentModel;
using MapCore.Imaging;
using static MapEditor.PanelLayout;

namespace MapEditor;

enum TextureTool { Pan, Paint, Tint, Adjust, Clone, Splat }

/// <summary>
/// The texture tools: paint a colour, tint (recolour keeping the texture's detail), adjust brightness / saturation /
/// hue, clone from elsewhere on the map, and paint the splat map (which detail texture shows where). It holds the
/// settings and raises events; the main window applies the strokes.
/// </summary>
sealed class TextureToolPanel : UserControl
{
    static readonly (TextureTool Tool, string Text, Keys Key, string Tip)[] Tools =
    [
        (TextureTool.Pan, "Pan", Keys.P, "Left button pans the view (P)"),
        (TextureTool.Paint, "Paint", Keys.B, "Paint the colour; Ctrl+click picks it from the map (B)"),
        (TextureTool.Tint, "Tint", Keys.T, "Recolour but keep the texture's light and dark detail; Ctrl+click picks the colour (T)"),
        (TextureTool.Adjust, "Adjust", Keys.A, "Brightness, saturation and hue; each dab applies part of it (A)"),
        (TextureTool.Clone, "Clone", Keys.C, "Copy texture from elsewhere: Alt+click the source first (C)"),
        (TextureTool.Splat, "Splat", Keys.D, "Paint which detail texture shows where (D)"),
    ];

    readonly Dictionary<TextureTool, RadioButton> toolButtons = [];
    readonly NumericUpDown radius = Number(4, 3000, 80, 0, 5);
    readonly NumericUpDown strength = Number(1, 100, 60, 0, 5);
    readonly NumericUpDown hardness = Number(0, 100, 50, 0, 5);
    readonly Button swatch = BarTheme.Swatch(new Button { Width = 80, Height = 24 });
    readonly NumericUpDown brightness = Number(-90, 200, 15, 0, 5);
    readonly NumericUpDown saturation = Number(-100, 200, 0, 0, 5);
    readonly NumericUpDown hue = Number(-180, 180, 0, 0, 5);
    readonly Label cloneSource = Hint("No source yet: Alt+click on the map.");
    readonly RadioButton[] channels = new RadioButton[4];
    readonly NumericUpDown splatAmount = Number(0, 100, 100, 0, 5);
    readonly CheckBox erase = new() { Text = "Erase (all channels to 0)", AutoSize = true };
    TextureTool tool = TextureTool.Pan;
    Rgba colour = new(120, 110, 90, 255);
    bool syncing;

    public TextureToolPanel()
    {
        AutoScroll = true;
        Padding = new Padding(6);
        var layout = new TableLayoutPanel { ColumnCount = 1, Dock = DockStyle.Top, AutoSize = true };
        Controls.Add(layout);

        var tips = new ToolTip();
        var toolGrid = ButtonGrid();
        foreach (var (t, text, _, tip) in Tools)
        {
            var button = ToolButton(text, t == tool);
            tips.SetToolTip(button, tip);
            button.CheckedChanged += (_, _) =>
            {
                if (button.Checked && !syncing)
                    Tool = t;
            };
            toolButtons[t] = button;
            toolGrid.Controls.Add(button);
        }
        layout.Controls.Add(Group("Texture tool", toolGrid));

        var brush = Grid();
        AddRow(brush, "Radius", radius, "units");
        AddRow(brush, "Strength", strength, "% per dab");
        AddRow(brush, "Hardness", hardness, "%");
        AddWide(brush, Hint("Hardness: the share of the radius at full strength. [ and ] change the radius; right or middle button drags the view."));
        layout.Controls.Add(Group("Brush", brush));

        var paint = Grid();
        AddRow(paint, "Colour", swatch, "");
        AddWide(paint, Hint("Click the swatch to choose, or Ctrl+click on the map to pick a colour from the texture."));
        swatch.Click += (_, _) => PickColour();
        layout.Controls.Add(Group("Paint and tint", paint));

        var adjust = Grid();
        AddRow(adjust, "Brightness", brightness, "%");
        AddRow(adjust, "Saturation", saturation, "%");
        AddRow(adjust, "Hue", hue, "degrees");
        AddWide(adjust, Hint("Each dab applies the strength's share of the change, so going over an area builds it up."));
        layout.Controls.Add(Group("Adjust", adjust));

        var clone = Grid();
        AddWide(clone, cloneSource);
        layout.Controls.Add(Group("Clone", clone));

        var splat = Grid();
        for (int i = 0; i < 4; i++)
        {
            channels[i] = new RadioButton { Text = $"{"RGBA"[i]}: detail texture {i + 1}", AutoSize = true, Checked = i == 0 };
            AddWide(splat, channels[i]);
        }
        AddRow(splat, "Amount", splatAmount, "% of full weight");
        AddWide(splat, erase);
        AddWide(splat, Hint("Shows in game as the detail textures blend; the 3D view does not draw them. Use the Splat layer (Ctrl+5) to see the weights."));
        layout.Controls.Add(Group("Splat", splat));

        foreach (var box in new[] { radius, hardness })
            box.ValueChanged += (_, _) => BrushChanged?.Invoke(this, EventArgs.Empty);
        ShowColour();
        UpdateEnabled();
    }

    public event EventHandler? ToolChanged;
    public event EventHandler? BrushChanged;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TextureTool Tool
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
    public double Radius { get => (double)radius.Value; set => radius.Value = Clamp(radius, value); }

    /// <summary>0-1, per dab.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Strength { get => (double)strength.Value / 100; set => strength.Value = Clamp(strength, value * 100); }

    /// <summary>0-1.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Hardness { get => (double)hardness.Value / 100; set => hardness.Value = Clamp(hardness, value * 100); }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Rgba Colour
    {
        get => colour;
        set
        {
            colour = value with { A = 255 };
            ShowColour();
        }
    }

    /// <summary>Brightness factor (1 = unchanged).</summary>
    public double Brightness => 1 + (double)brightness.Value / 100;

    /// <summary>Saturation factor (1 = unchanged, 0 = grey).</summary>
    public double Saturation => 1 + (double)saturation.Value / 100;

    public double Hue => (double)hue.Value;

    public void SetAdjust(double brightnessPercent, double saturationPercent, double hueDegrees)
    {
        brightness.Value = Clamp(brightness, brightnessPercent);
        saturation.Value = Clamp(saturation, saturationPercent);
        hue.Value = Clamp(hue, hueDegrees);
    }

    /// <summary>Where cloning copies from (game units), and the offset once the first stroke fixed it.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public (double X, double Z)? CloneSource { get; private set; }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public (double X, double Z)? CloneOffset { get; set; }

    public void SetCloneSource(double x, double z)
    {
        CloneSource = (x, z);
        CloneOffset = null; // fixed by the next stroke, then kept: later strokes stay aligned with the source
        cloneSource.Text = $"Source at ({x:0}, {z:0}). Your next stroke starts copying from there.";
    }

    /// <summary>The splat weights to paint towards (R, G, B, A = detail textures 1-4).</summary>
    public Rgba SplatWeights
    {
        get
        {
            if (erase.Checked)
                return new Rgba(0, 0, 0, 0);
            byte v = (byte)Math.Round((double)splatAmount.Value * 2.55);
            int c = Array.FindIndex(channels, b => b.Checked);
            return new Rgba(c == 0 ? v : (byte)0, c == 1 ? v : (byte)0, c == 2 ? v : (byte)0, c == 3 ? v : (byte)0);
        }
    }

    public void SetSplatChannel(int channel, double amountPercent, bool clear = false)
    {
        channels[Math.Clamp(channel, 0, 3)].Checked = true;
        splatAmount.Value = Clamp(splatAmount, amountPercent);
        erase.Checked = clear;
    }

    /// <summary>Labels the splat channels with the map's detail textures (null: none set for that channel).</summary>
    public void SetSplatChannelNames(IReadOnlyList<string?> names)
    {
        for (int i = 0; i < 4; i++)
            channels[i].Text = $"{"RGBA"[i]}: {(i < names.Count && names[i] is string n ? Path.GetFileNameWithoutExtension(n) : "(no detail texture)")}";
    }

    /// <summary>Tool keys (P, B, T, A, C, D) and [ ] for the radius; true if the key was used.</summary>
    public bool HandleKey(Keys key)
    {
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

    void PickColour()
    {
        using var dialog = new ColorDialog { Color = Color.FromArgb(colour.R, colour.G, colour.B), FullOpen = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            Colour = new Rgba(dialog.Color.R, dialog.Color.G, dialog.Color.B, 255);
    }

    void ShowColour()
    {
        swatch.BackColor = Color.FromArgb(colour.R, colour.G, colour.B);
        swatch.Text = $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";
        swatch.ForeColor = 0.299 * colour.R + 0.587 * colour.G + 0.114 * colour.B > 128 ? Color.Black : Color.White;
    }

    void UpdateEnabled()
    {
        swatch.Enabled = tool is TextureTool.Paint or TextureTool.Tint;
        brightness.Enabled = saturation.Enabled = hue.Enabled = tool == TextureTool.Adjust;
        splatAmount.Enabled = erase.Enabled = tool == TextureTool.Splat;
        foreach (var c in channels)
            c.Enabled = tool == TextureTool.Splat;
    }
}
