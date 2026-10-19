using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MapEditor;

/// <summary>
/// The editor's look, after BAR's own interface: its settings overlay and its in-game controls (screenshots of both,
/// 2026-10-16; the colours sampled from them). Near-black panels; warm brown tabs, the chosen one dark with gold-tan
/// text; tan-gold section headings; flat dark fields with a square at their end; toggles lit by a green square; a thin
/// slider with a square knob; dark buttons whose state shows as a bar under them (green on, gold chosen). Every colour
/// the editor's controls use is here.
/// </summary>
static class BarTheme
{
    // The settings overlay.
    public static readonly Color Window = Color.FromArgb(0x10, 0x10, 0x0F);       // behind everything: strips, splitters
    public static readonly Color Panel = Color.FromArgb(0x1A, 0x1A, 0x18);        // the overlay's body
    public static readonly Color Field = Color.FromArgb(0x2D, 0x2D, 0x2B);        // dropdowns, boxes
    public static readonly Color FieldEdge = Color.FromArgb(0x42, 0x42, 0x40);
    public static readonly Color FieldKnob = Color.FromArgb(0x40, 0x40, 0x3D);    // the square at a dropdown's end
    public static readonly Color Text = Color.FromArgb(0xE2, 0xE2, 0xE2);
    public static readonly Color Dim = Color.FromArgb(0x93, 0x93, 0x91);          // sub-labels, hints, units
    public static readonly Color Disabled = Color.FromArgb(0x60, 0x60, 0x5D);
    public static readonly Color Heading = Color.FromArgb(0xE2, 0xC4, 0x9A);      // "Screen", "Effects"
    public static readonly Color Rule = Color.FromArgb(0x33, 0x31, 0x2B);         // a heading's faint line
    public static readonly Color TabTop = Color.FromArgb(0x4A, 0x3F, 0x2E), TabMiddle = Color.FromArgb(0x65, 0x56, 0x3F),
        TabBottom = Color.FromArgb(0x25, 0x1E, 0x12), TabText = Color.FromArgb(0xA6, 0x99, 0x86), TabChosenText = Color.FromArgb(0xD9, 0xB9, 0x8A),
        TabLine = Color.FromArgb(0x2D, 0x2C, 0x27);
    public static readonly Color ToggleBox = Color.FromArgb(0x2F, 0x30, 0x2B), ToggleEdge = Color.FromArgb(0x4A, 0x4A, 0x45),
        ToggleOn = Color.FromArgb(0x8F, 0xE0, 0x7E), ToggleCore = Color.FromArgb(0xE9, 0xFB, 0xE4);
    public static readonly Color SliderTrack = Color.FromArgb(0x4D, 0x4E, 0x49), SliderKnob = Color.FromArgb(0xEB, 0xEC, 0xE7);

    // The in-game controls.
    public static readonly Color Button = Color.FromArgb(0x2B, 0x2B, 0x29), ButtonTop = Color.FromArgb(0x35, 0x35, 0x32),
        ButtonHover = Color.FromArgb(0x3A, 0x39, 0x35), ButtonDown = Color.FromArgb(0x22, 0x22, 0x20), ButtonChosen = Color.FromArgb(0x3A, 0x34, 0x28),
        ButtonEdge = Color.FromArgb(0x3E, 0x3D, 0x39);
    public static readonly Color Gold = Color.FromArgb(0xF0, 0xB9, 0x5A);         // chosen
    public static readonly Color Focus = Color.FromArgb(0x8A, 0x74, 0x4E);        // a focused control's edge: muted gold
    public static readonly Color On = Color.FromArgb(0x5C, 0xC8, 0x4B), Off = Color.FromArgb(0x55, 0x55, 0x51); // a state's bar
    public static readonly Color Warning = Color.FromArgb(0xE8, 0xB0, 0x48), Error = Color.FromArgb(0xE5, 0x5B, 0x4F);
    public static readonly Color Menu = Color.FromArgb(0x1C, 0x1C, 0x1A), MenuHover = Color.FromArgb(0x3A, 0x33, 0x26);

    /// <summary>
    /// The editor's font. It is set on each window only: everything in the window takes it from there, and Windows
    /// rescales it with the screen's DPI. A font set on a control of its own isn't always rescaled (not on a control
    /// whose window doesn't exist yet when the DPI changes), so the theme's other fonts are made from the font a
    /// control has (<see cref="Semibold"/>), and follow it.
    /// </summary>
    public static readonly Font Font = new("Segoe UI", 9f);

    static readonly Dictionary<float, Font> semibold = [];

    /// <summary>Segoe UI Semibold at <paramref name="scale"/> times the size of <paramref name="like"/> (a control's own font, rescaled for its screen).</summary>
    public static Font Semibold(Font like, float scale = 1f)
    {
        float size = MathF.Round(like.SizeInPoints * scale * 4) / 4;
        if (!semibold.TryGetValue(size, out var font))
            semibold[size] = font = new Font("Segoe UI Semibold", size);
        return font;
    }

    /// <summary>A control (a title) in semibold: its font made from the font of whatever it is in, and kept so.</summary>
    public static T Strong<T>(T control) where T : Control
    {
        FollowParent(control, f => Semibold(f));
        return control;
    }

    /// <summary>
    /// Keeps a control's font made from its parent's (<paramref name="derive"/>), as the parent's changes. When the DPI
    /// changes Windows Forms rescales the parent's font and then this control's own as well, which would scale it
    /// twice: a change to its own font is undone the same way.
    /// </summary>
    static void FollowParent(Control c, Func<Font, Font> derive)
    {
        Control? parent = null;
        bool setting = false;
        void Update(object? sender, EventArgs e)
        {
            if (c.Parent == null || setting)
                return;
            var font = derive(c.Parent.Font);
            if (c.Font.Equals(font))
                return;
            setting = true;
            try
            {
                c.Font = font;
            }
            finally
            {
                setting = false;
            }
        }
        c.FontChanged += Update;
        void Hook()
        {
            if (parent != null)
                parent.FontChanged -= Update;
            parent = c.Parent;
            if (parent != null)
                parent.FontChanged += Update;
            Update(null, EventArgs.Empty);
        }
        c.ParentChanged += (_, _) => Hook();
        if (c.Parent != null)
            Hook();
    }

    static readonly ConditionalWeakTable<Control, object> styled = [];

    /// <summary>Styles a control and everything in it, and whatever is added to them later.</summary>
    public static void Apply(Control control)
    {
        Style(control);
        foreach (Control child in control.Controls)
            Apply(child);
    }

    /// <summary>A button that shows a colour (a swatch): left as it is, only its edge in the theme's.</summary>
    public static T Swatch<T>(T button) where T : ButtonBase
    {
        styled.TryAdd(button, true);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = FieldEdge;
        return button;
    }

    static void Style(Control c)
    {
        if (!styled.TryAdd(c, true))
            return;
        c.ControlAdded += (_, e) => { if (e.Control != null) Apply(e.Control); };
        switch (c)
        {
            case Form form:
                form.BackColor = Panel;
                form.ForeColor = Text;
                WhenCreated(form, DarkTitleBar);
                break;
            case TabPage page:
                page.UseVisualStyleBackColor = false;
                page.BackColor = Panel;
                page.ForeColor = Text;
                break;
            case SplitContainer split:
                split.BackColor = Window;
                break;
            case SplitterPanel panel:
                panel.BackColor = Window;
                break;
            case ToolStrip strip:
                strip.BackColor = Window;
                strip.ForeColor = Text;
                // A strip's font is the system's menu font unless set: the window's instead. Not the menu bar's, which
                // keeps the menu font (Segoe UI 9 too); Windows Forms rescales it in a way that can't be followed.
                if (strip is not MenuStrip)
                    FollowParent(strip, f => f);
                strip.RenderMode = ToolStripRenderMode.ManagerRenderMode;
                break;
            case ButtonBase b when b is Button || b is CheckBox { Appearance: Appearance.Button } || b is RadioButton { Appearance: Appearance.Button }:
                ButtonLook.Attach(b);
                break;
            case CheckBox or RadioButton:
                ToggleLook.Attach((ButtonBase)c);
                break;
            case TextBox text:
                text.BackColor = Field;
                text.ForeColor = Text;
                text.BorderStyle = BorderStyle.FixedSingle;
                break;
            case NumericUpDown number:
                number.BackColor = Field;
                number.ForeColor = Text;
                number.BorderStyle = BorderStyle.FixedSingle;
                SpinLook.Attach(number);
                break;
            case ComboBox combo:
                combo.BackColor = Field;
                combo.ForeColor = Text;
                combo.FlatStyle = FlatStyle.Flat;
                ComboLook.Attach(combo);
                break;
            case ListBox list:
                list.BackColor = Panel;
                list.ForeColor = Text;
                list.BorderStyle = BorderStyle.None;
                WhenCreated(list, l => Dark(l, "DarkMode_Explorer"));
                break;
            case ListView view:
                view.BackColor = Panel;
                view.ForeColor = Text;
                view.BorderStyle = BorderStyle.None;
                ListLook.Attach(view);
                break;
            case TrackBar track:
                track.BackColor = Panel;
                SliderLook.Attach(track);
                break;
            case LinkLabel link:
                link.LinkColor = Gold;
                link.ActiveLinkColor = Text;
                break;
            case Label label:
                if (label.ForeColor == SystemColors.GrayText)
                    label.ForeColor = Dim;
                else if (label.ForeColor == SystemColors.ControlText || label.ForeColor == SystemColors.WindowText)
                    label.ForeColor = Text;
                break;
        }
        if (c is ScrollableControl and not Form)
            WhenCreated(c, s => Dark(s, "DarkMode_Explorer")); // dark scroll bars
        if (c.Parent is ToolStrip)
            FollowParent(c, f => f); // a box on the toolbar: its font as the toolbar's
    }

    static void WhenCreated<T>(T control, Action<T> act) where T : Control
    {
        if (control.IsHandleCreated)
            act(control);
        control.HandleCreated += (_, _) => act(control);
    }

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    static extern int SetWindowTheme(IntPtr hWnd, string? subAppName, string? subIdList);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    // Windows' dark mode for its own controls (as Explorer uses it): not documented, but stable since Windows 10 1903
    // and used widely. Without it a dark theme class gives dark parts but not always dark scroll bars.
    [DllImport("uxtheme.dll", EntryPoint = "#133")]
    static extern bool AllowDarkModeForWindow(IntPtr hWnd, bool allow);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    static extern int SetPreferredAppMode(int mode);

    static bool darkModeAllowed;

    /// <summary>Windows' own dark look for a native part (scroll bars, list headers): best effort, nothing if it isn't there.</summary>
    internal static void Dark(Control c, string theme) => Dark(c.Handle, theme);

    internal static void Dark(IntPtr handle, string theme)
    {
        try
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
            {
                if (!darkModeAllowed)
                {
                    darkModeAllowed = true;
                    SetPreferredAppMode(1); // allow dark: only windows that ask for it
                }
                AllowDarkModeForWindow(handle, true);
            }
            SetWindowTheme(handle, theme, null);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    /// <summary>A dark title bar (Windows 10 2004 and later), the window's own colour on Windows 11: best effort.</summary>
    static void DarkTitleBar(Form form)
    {
        try
        {
            int on = 1, caption = ColorTranslator.ToWin32(Window), text = ColorTranslator.ToWin32(Text);
            DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int));        // DWMWA_USE_IMMERSIVE_DARK_MODE
            DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));   // DWMWA_CAPTION_COLOR
            DwmSetWindowAttribute(form.Handle, 36, ref text, sizeof(int));      // DWMWA_TEXT_COLOR
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    /// <summary>A rectangle with rounded corners (all four, or only the top two).</summary>
    public static GraphicsPath Rounded(RectangleF r, float radius, bool topOnly = false)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        if (topOnly)
        {
            path.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
        }
        else
        {
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        }
        path.CloseFigure();
        return path;
    }

    /// <summary>The colour behind a control: its parent's (a button or toggle paints over its own corners with it).</summary>
    public static Color Behind(Control c) => c.Parent?.BackColor ?? Panel;
}

/// <summary>
/// A button as BAR's in-game command buttons are: dark, rounded a little, lighter at the top, small light text. A
/// button that stays down (a tool, a choice) shows it with a gold bar under its text; a focused one has a muted gold edge.
/// </summary>
sealed class ButtonLook
{
    bool hover, down;

    public static void Attach(ButtonBase b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.UseVisualStyleBackColor = false;
        b.BackColor = BarTheme.Button;
        b.ForeColor = BarTheme.Text;
        var look = new ButtonLook();
        b.MouseEnter += (_, _) => { look.hover = true; b.Invalidate(); };
        b.MouseLeave += (_, _) => { look.hover = look.down = false; b.Invalidate(); };
        b.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) { look.down = true; b.Invalidate(); } };
        b.MouseUp += (_, _) => { look.down = false; b.Invalidate(); };
        b.GotFocus += (_, _) => b.Invalidate();
        b.LostFocus += (_, _) => b.Invalidate();
        b.EnabledChanged += (_, _) => b.Invalidate();
        if (b is CheckBox check)
            check.CheckedChanged += (_, _) => b.Invalidate();
        if (b is RadioButton radio)
            radio.CheckedChanged += (_, _) => b.Invalidate();
        b.Paint += (_, e) => look.Paint(b, e.Graphics);
    }

    void Paint(ButtonBase b, Graphics g)
    {
        g.Clear(BarTheme.Behind(b));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        bool chosen = b is CheckBox { Checked: true } or RadioButton { Checked: true };
        var top = !b.Enabled ? BarTheme.Button : down ? BarTheme.ButtonDown : hover ? BarTheme.ButtonHover : chosen ? BarTheme.ButtonChosen : BarTheme.ButtonTop;
        var bottom = !b.Enabled ? BarTheme.Button : down ? BarTheme.ButtonDown : hover ? BarTheme.ButtonHover : chosen ? BarTheme.ButtonChosen : BarTheme.Button;
        var r = new RectangleF(0.5f, 0.5f, b.Width - 1.5f, b.Height - 1.5f);
        float radius = b.LogicalToDeviceUnits(3);
        using (var path = BarTheme.Rounded(r, radius))
        {
            using (var fill = new LinearGradientBrush(new RectangleF(0, 0, 1, Math.Max(1, b.Height)), top, bottom, LinearGradientMode.Vertical))
                g.FillPath(fill, path);
            using var edge = new Pen(b.Focused ? BarTheme.Focus : BarTheme.ButtonEdge);
            g.DrawPath(edge, path);
        }
        if (chosen)
        {
            int inset = b.LogicalToDeviceUnits(6), bar = b.LogicalToDeviceUnits(2);
            using var gold = new SolidBrush(BarTheme.Gold);
            g.FillRectangle(gold, inset, b.Height - bar - b.LogicalToDeviceUnits(3), Math.Max(1, b.Width - 2 * inset), bar);
        }
        g.SmoothingMode = SmoothingMode.Default;
        TextRenderer.DrawText(g, b.Text, b.Font, new Rectangle(2, 0, b.Width - 4, b.Height), b.Enabled ? (chosen ? BarTheme.Gold : BarTheme.Text) : BarTheme.Disabled,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>A tick box or choice as BAR's settings toggles are: a small dark square, lit by a green square when on (a round one for a choice).</summary>
static class ToggleLook
{
    public static void Attach(ButtonBase b)
    {
        b.UseVisualStyleBackColor = false;
        b.ForeColor = BarTheme.Text;
        b.EnabledChanged += (_, _) => b.Invalidate();
        b.Paint += (_, e) => Paint(b, e.Graphics);
    }

    /// <summary>
    /// The box's size, from the font's height rather than the DPI: Windows lays a tick box out for a glyph about this
    /// size beside its text, and a box any larger would push the text into the next control.
    /// </summary>
    public static int BoxSize(Font font) => Math.Max(9, (int)Math.Round(font.Height * 0.8));

    static void Paint(ButtonBase b, Graphics g)
    {
        g.Clear(BarTheme.Behind(b));
        bool on = b is CheckBox { Checked: true } or RadioButton { Checked: true };
        int s = BoxSize(b.Font), gap = Math.Max(3, s / 3);
        DrawBox(g, new Rectangle(0, (b.Height - s) / 2, s, s), on, b.Enabled, b is RadioButton, b.Focused);
        var flags = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding |
            (b.AutoSize ? TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine : TextFormatFlags.WordBreak);
        TextRenderer.DrawText(g, b.Text, b.Font, new Rectangle(s + gap, 0, Math.Max(1, b.Width - s - gap), b.Height), b.Enabled ? b.ForeColor : BarTheme.Disabled, flags);
    }

    /// <summary>A toggle's box in <paramref name="area"/> (a square): dark, edged, lit by a green square (or dot) when on.</summary>
    public static void DrawBox(Graphics g, Rectangle area, bool on, bool enabled, bool round, bool focused)
    {
        int s = area.Width;
        var box = new Rectangle(area.X, area.Y, s - 1, s - 1);
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = round ? SmoothingMode.AntiAlias : SmoothingMode.None;
        using (var fill = new SolidBrush(BarTheme.ToggleBox))
        using (var edge = new Pen(focused ? BarTheme.Focus : BarTheme.ToggleEdge))
        {
            if (round)
            {
                g.FillEllipse(fill, box);
                g.DrawEllipse(edge, box);
            }
            else
            {
                g.FillRectangle(fill, box);
                g.DrawRectangle(edge, box);
            }
        }
        if (on)
        {
            int inner = Math.Max(3, s / 2), core = Math.Max(1, inner / 2);
            var lit = new Rectangle(area.X + (s - inner) / 2, area.Y + (s - inner) / 2, inner, inner);
            using var green = new SolidBrush(enabled ? BarTheme.ToggleOn : BarTheme.Disabled);
            using var light = new SolidBrush(enabled ? BarTheme.ToggleCore : BarTheme.Disabled);
            if (round)
            {
                g.FillEllipse(green, lit);
                g.FillEllipse(light, lit.X + (inner - core) / 2, lit.Y + (inner - core) / 2, core, core);
            }
            else
            {
                g.FillRectangle(green, lit);
                g.FillRectangle(light, lit.X + (inner - core) / 2, lit.Y + (inner - core) / 2, core, core);
            }
        }
        g.SmoothingMode = smoothing;
    }

    /// <summary>A list's tick boxes (its state images: unticked, ticked), drawn as the toggles are.</summary>
    public static ImageList ListBoxes(Font font)
    {
        int size = Math.Max(12, font.Height), s = BoxSize(font);
        var images = new ImageList { ImageSize = new Size(size, size), ColorDepth = ColorDepth.Depth32Bit };
        foreach (bool on in new[] { false, true })
        {
            var picture = new Bitmap(size, size);
            using (var g = Graphics.FromImage(picture))
                DrawBox(g, new Rectangle((size - s) / 2, (size - s) / 2, s, s), on, enabled: true, round: false, focused: false);
            images.Images.Add(picture);
        }
        return images;
    }
}

/// <summary>
/// A dropdown as BAR's settings dropdowns are: a flat dark field, its text light, a square at its end. Drawn over the
/// native one after it paints (and when printed); an editable one keeps its own text box, only its frame and square drawn.
/// </summary>
sealed class ComboLook : NativeWindow
{
    const int WM_PAINT = 0x000F, WM_PRINTCLIENT = 0x0318;
    readonly ComboBox box;

    ComboLook(ComboBox box)
    {
        this.box = box;
        box.HandleCreated += (_, _) => AssignHandle(box.Handle);
        box.HandleDestroyed += (_, _) => ReleaseHandle();
        box.GotFocus += (_, _) => box.Invalidate();
        box.LostFocus += (_, _) => box.Invalidate();
        if (box.IsHandleCreated)
            AssignHandle(box.Handle);
    }

    public static void Attach(ComboBox box) => _ = new ComboLook(box);

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_PAINT)
        {
            using var g = Graphics.FromHwnd(Handle);
            Draw(g);
        }
        else if (m.Msg == WM_PRINTCLIENT)
        {
            using var g = Graphics.FromHdc(m.WParam);
            Draw(g);
        }
    }

    void Draw(Graphics g)
    {
        var r = box.ClientRectangle;
        if (r.Width <= 2 || r.Height <= 2)
            return;
        int square = Math.Min(r.Height - 2, box.LogicalToDeviceUnits(16));
        // An editable box's own text box is a window of its own, left out of this drawing: only round it is filled.
        using (var fill = new SolidBrush(BarTheme.Field))
            g.FillRectangle(fill, r);
        if (box.DropDownStyle != ComboBoxStyle.DropDown)
        {
            TextRenderer.DrawText(g, box.Text, box.Font, new Rectangle(box.LogicalToDeviceUnits(4), 0, r.Width - square - box.LogicalToDeviceUnits(8), r.Height),
                box.Enabled ? BarTheme.Text : BarTheme.Disabled, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        var knob = new Rectangle(r.Right - square - 2, r.Top + (r.Height - square) / 2, square, square);
        using (var fill = new SolidBrush(BarTheme.FieldKnob))
            g.FillRectangle(fill, knob);
        // A small chevron in the square.
        int c = Math.Max(2, square / 5);
        var mid = new Point(knob.X + knob.Width / 2, knob.Y + knob.Height / 2);
        using (var arrow = new SolidBrush(box.Enabled ? BarTheme.Dim : BarTheme.Disabled))
            g.FillPolygon(arrow, new[] { new Point(mid.X - c, mid.Y - c / 2), new Point(mid.X + c + 1, mid.Y - c / 2), new Point(mid.X, mid.Y + c / 2 + 1) });
        using var edge = new Pen(box.Focused ? BarTheme.Focus : BarTheme.FieldEdge);
        g.DrawRectangle(edge, 0, 0, r.Width - 1, r.Height - 1);
    }
}

/// <summary>A slider as BAR's settings sliders are: a thin dark track, a small light square knob. Drawn over the native one after it paints.</summary>
sealed class SliderLook : NativeWindow
{
    const int WM_PAINT = 0x000F, WM_PRINTCLIENT = 0x0318, TBM_GETTHUMBRECT = 0x0419, TBM_GETCHANNELRECT = 0x041A;
    readonly TrackBar track;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref RECT lParam);

    SliderLook(TrackBar track)
    {
        this.track = track;
        track.HandleCreated += (_, _) => AssignHandle(track.Handle);
        track.HandleDestroyed += (_, _) => ReleaseHandle();
        if (track.IsHandleCreated)
            AssignHandle(track.Handle);
    }

    public static void Attach(TrackBar track) => _ = new SliderLook(track);

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_PAINT)
        {
            using var g = Graphics.FromHwnd(Handle);
            Draw(g);
        }
        else if (m.Msg == WM_PRINTCLIENT)
        {
            using var g = Graphics.FromHdc(m.WParam);
            Draw(g);
        }
    }

    void Draw(Graphics g)
    {
        var channel = new RECT();
        var thumb = new RECT();
        SendMessage(Handle, TBM_GETCHANNELRECT, IntPtr.Zero, ref channel);
        SendMessage(Handle, TBM_GETTHUMBRECT, IntPtr.Zero, ref thumb);
        using (var fill = new SolidBrush(track.BackColor))
            g.FillRectangle(fill, track.ClientRectangle);
        int line = Math.Max(1, track.LogicalToDeviceUnits(2));
        int cy = (thumb.Top + thumb.Bottom) / 2;
        using (var rail = new SolidBrush(BarTheme.SliderTrack))
            g.FillRectangle(rail, channel.Left, cy - line / 2, Math.Max(1, channel.Right - channel.Left), line);
        int knob = track.LogicalToDeviceUnits(9);
        int cx = (thumb.Left + thumb.Right) / 2;
        var square = new Rectangle(cx - knob / 2, cy - knob / 2, knob, knob);
        using (var fill = new SolidBrush(track.Enabled ? BarTheme.SliderKnob : BarTheme.Disabled))
            g.FillRectangle(fill, square);
        if (track.Focused)
        {
            using var edge = new Pen(BarTheme.Focus);
            g.DrawRectangle(edge, square.X - 2, square.Y - 2, square.Width + 3, square.Height + 3);
        }
    }
}

/// <summary>
/// Tabs as BAR's settings overlay has them: across the top, rounded at their upper corners, warm brown and lighter
/// through the middle, their text tan; the chosen one dark as the page below it, its text gold-tan. With
/// <see cref="FillStrip"/> the tabs share the strip's width evenly.
/// </summary>
sealed class BarTabControl : TabControl
{
    public BarTabControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Padding = new Point(8, 5);
    }

    /// <summary>Whether the tabs share the strip's width evenly (the tool tabs), rather than each fitting its text.</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool FillStrip { get; init; }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Fit();
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        Fit();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Fit();
    }

    const int SetMinTabWidth = 0x1331, SetItemSize = 0x1329; // TCM_SETMINTABWIDTH, TCM_SETITEMSIZE

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (FillStrip)
            BeginInvoke(Fit); // once it is made: fitting while Windows makes it would make it again
    }

    /// <summary>
    /// Shares the strip out again, after a tab is added or removed: while a page is being added the control still counts
    /// the tabs it had, so tabs sized then are too wide and scroll off the end.
    /// </summary>
    public void FitTabs() => Fit();

    bool fitting;

    void Fit()
    {
        // Setting the tabs' size nudges the control's own size to make Windows lay them out again: no fitting then.
        if (TabCount == 0 || Width <= 0 || fitting)
            return;
        fitting = true;
        try
        {
            if (!FillStrip)
            {
                // Each tab as wide as Windows measures its text in the plain font, with room for it in semibold.
                var padding = new Point(LogicalToDeviceUnits(12), LogicalToDeviceUnits(5));
                if (Padding != padding)
                    Padding = padding;
                return;
            }
            SizeMode = TabSizeMode.Fixed;
            int height = TextRenderer.MeasureText("Ag", BarTheme.Semibold(Font)).Height + LogicalToDeviceUnits(12);
            // An even share of the strip: never more, or the last tabs scroll off the end.
            var size = new Size(Math.Max(16, (Width - LogicalToDeviceUnits(6)) / TabCount), height);
            if (ItemSize != size)
                ItemSize = size;
            // Windows keeps fixed-width tabs at least about 54 pixels wide unless told otherwise: seven of those
            // overflow the tool tabs' strip, scrolling the first ones out of sight. Then the size again, to lay them out.
            if (IsHandleCreated)
            {
                SendMessage(Handle, SetMinTabWidth, IntPtr.Zero, (IntPtr)LogicalToDeviceUnits(24));
                SendMessage(Handle, SetItemSize, IntPtr.Zero, (IntPtr)((size.Height << 16) | (size.Width & 0xFFFF)));
            }
        }
        finally
        {
            fitting = false;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BarTheme.Window);
        if (TabCount == 0)
            return;
        var body = DisplayRectangle;
        body.Inflate(LogicalToDeviceUnits(3), LogicalToDeviceUnits(3));
        using (var fill = new SolidBrush(BarTheme.Panel))
            g.FillRectangle(fill, body);
        var first = GetTabRect(0);
        using (var line = new Pen(BarTheme.TabLine))
            g.DrawLine(line, 0, first.Bottom, Width, first.Bottom);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float radius = LogicalToDeviceUnits(4);
        for (int i = 0; i < TabCount; i++)
        {
            var t = GetTabRect(i);
            bool chosen = i == SelectedIndex;
            var r = new RectangleF(t.X + 1, t.Y + (chosen ? 0 : 2), t.Width - 2, t.Height - (chosen ? -2 : 2));
            using var path = BarTheme.Rounded(r, radius, topOnly: true);
            if (chosen)
            {
                using var fill = new SolidBrush(BarTheme.Panel);
                g.FillPath(fill, path);
            }
            else
            {
                using var fill = new LinearGradientBrush(new RectangleF(0, r.Top - 1, 1, r.Height + 2), BarTheme.TabTop, BarTheme.TabBottom, LinearGradientMode.Vertical)
                {
                    InterpolationColors = new ColorBlend { Colors = [BarTheme.TabTop, BarTheme.TabMiddle, BarTheme.TabBottom], Positions = [0f, 0.42f, 1f] },
                };
                g.FillPath(fill, path);
            }
            TextRenderer.DrawText(g, TabPages[i].Text, BarTheme.Semibold(Font), Rectangle.Round(r), chosen ? BarTheme.TabChosenText : BarTheme.TabText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}

/// <summary>
/// A section as BAR's settings overlay has them: no box, a tan-gold heading ("Screen", "Effects") with a faint line
/// running on from it, its contents below. Its heading's room is taken from where its contents start, and added to
/// its size, so the layout around it holds.
/// </summary>
class BarGroupBox : GroupBox
{
    public BarGroupBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    /// <summary>A section within a section: its heading smaller.</summary>
    [System.ComponentModel.Browsable(false), System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Minor { get; set; }

    Font HeadingFont => BarTheme.Semibold(Font, Minor ? 1f : 10.5f / 9f);

    int HeadingHeight => TextRenderer.MeasureText("Ag", HeadingFont).Height + LogicalToDeviceUnits(2);

    /// <summary>How much more room the heading takes than the group's own title would.</summary>
    int Extra => Math.Max(0, HeadingHeight - FontHeight);

    public override Rectangle DisplayRectangle
    {
        get
        {
            var r = base.DisplayRectangle;
            return new Rectangle(r.X, r.Y + Extra, r.Width, Math.Max(0, r.Height - Extra));
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var size = base.GetPreferredSize(proposedSize);
        return new Size(size.Width, size.Height + Extra);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        int x = LogicalToDeviceUnits(2);
        var text = TextRenderer.MeasureText(g, Text, HeadingFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Text, HeadingFont, new Point(x, 0), Enabled ? BarTheme.Heading : BarTheme.Disabled, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        int y = text.Height / 2 + 1;
        using var rule = new Pen(BarTheme.Rule);
        g.DrawLine(rule, x + text.Width + LogicalToDeviceUnits(8), y, Width - LogicalToDeviceUnits(2), y);
    }
}

/// <summary>
/// Menus, the toolbar and the status bar: dark strips; the toolbar's buttons light text, a hover behind them, and a
/// state bar under the ones that switch something on or off (green on, grey off, as BAR's command buttons show it);
/// drop-down menus dark, the item under the pointer warm.
/// </summary>
sealed class BarToolStripRenderer : ToolStripProfessionalRenderer
{
    public BarToolStripRenderer() : base(new Colours())
    {
        RoundedEdges = false;
    }

    sealed class Colours : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => BarTheme.Menu;
        public override Color ImageMarginGradientBegin => BarTheme.Menu;
        public override Color ImageMarginGradientMiddle => BarTheme.Menu;
        public override Color ImageMarginGradientEnd => BarTheme.Menu;
        public override Color MenuBorder => BarTheme.ButtonEdge;
        public override Color MenuItemBorder => BarTheme.MenuHover;
        public override Color MenuItemSelected => BarTheme.MenuHover;
        public override Color MenuItemSelectedGradientBegin => BarTheme.MenuHover;
        public override Color MenuItemSelectedGradientEnd => BarTheme.MenuHover;
        public override Color MenuItemPressedGradientBegin => BarTheme.Menu;
        public override Color MenuItemPressedGradientMiddle => BarTheme.Menu;
        public override Color MenuItemPressedGradientEnd => BarTheme.Menu;
        public override Color MenuStripGradientBegin => BarTheme.Window;
        public override Color MenuStripGradientEnd => BarTheme.Window;
        public override Color ToolStripGradientBegin => BarTheme.Window;
        public override Color ToolStripGradientMiddle => BarTheme.Window;
        public override Color ToolStripGradientEnd => BarTheme.Window;
        public override Color ToolStripBorder => BarTheme.Window;
        public override Color StatusStripGradientBegin => BarTheme.Window;
        public override Color StatusStripGradientEnd => BarTheme.Window;
        public override Color SeparatorDark => BarTheme.ButtonEdge;
        public override Color SeparatorLight => BarTheme.Window;
        public override Color ButtonSelectedHighlight => BarTheme.ButtonHover;
        public override Color ButtonSelectedBorder => BarTheme.ButtonEdge;
        public override Color ButtonSelectedGradientBegin => BarTheme.ButtonHover;
        public override Color ButtonSelectedGradientMiddle => BarTheme.ButtonHover;
        public override Color ButtonSelectedGradientEnd => BarTheme.ButtonHover;
        public override Color ButtonPressedGradientBegin => BarTheme.ButtonDown;
        public override Color ButtonPressedGradientMiddle => BarTheme.ButtonDown;
        public override Color ButtonPressedGradientEnd => BarTheme.ButtonDown;
        public override Color ButtonCheckedGradientBegin => BarTheme.Button;
        public override Color ButtonCheckedGradientMiddle => BarTheme.Button;
        public override Color ButtonCheckedGradientEnd => BarTheme.Button;
        public override Color CheckBackground => BarTheme.MenuHover;
        public override Color CheckSelectedBackground => BarTheme.MenuHover;
        public override Color CheckPressedBackground => BarTheme.MenuHover;
        public override Color GripDark => BarTheme.ButtonEdge;
        public override Color GripLight => BarTheme.Window;
        public override Color OverflowButtonGradientBegin => BarTheme.Window;
        public override Color OverflowButtonGradientMiddle => BarTheme.Window;
        public override Color OverflowButtonGradientEnd => BarTheme.Window;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // An item given a colour of its own (a warning) keeps it.
        var own = e.Item.ForeColor;
        e.TextColor = !e.Item.Enabled ? BarTheme.Disabled
            : own != BarTheme.Text && own != SystemColors.ControlText && own != SystemColors.MenuText ? own
            : e.Item is ToolStripMenuItem { Selected: true } ? BarTheme.Heading
            : BarTheme.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        var g = e.Graphics;
        var r = new Rectangle(Point.Empty, e.Item.Size);
        if (e.Item is ToolStripButton b)
        {
            if (b.Pressed || b.Selected)
            {
                using var fill = new SolidBrush(b.Pressed ? BarTheme.ButtonDown : BarTheme.ButtonHover);
                g.FillRectangle(fill, r);
            }
            if (b.CheckOnClick && b.Enabled)
            {
                // A switch's state, as under BAR's command buttons: green on, grey off.
                int inset = e.Item.Owner?.LogicalToDeviceUnits(5) ?? 5, bar = e.Item.Owner?.LogicalToDeviceUnits(2) ?? 2;
                using var state = new SolidBrush(b.Checked ? BarTheme.On : BarTheme.Off);
                g.FillRectangle(state, inset, r.Height - bar - 1, Math.Max(1, r.Width - 2 * inset), bar);
            }
            return;
        }
        base.OnRenderButtonBackground(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled == false ? BarTheme.Disabled : BarTheme.Dim;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        TextRenderer.DrawText(e.Graphics, "✓", BarTheme.Semibold(e.Item.Font), r, BarTheme.Gold, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is ToolStripDropDown)
        {
            using var edge = new Pen(BarTheme.ButtonEdge);
            e.Graphics.DrawRectangle(edge, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }
        // the toolbar, menu bar and status bar: no line
    }
}

/// <summary>
/// A number box as BAR's settings fields are: the theme's dark field and edge, its up and down buttons two dark squares
/// with small arrows. Windows Forms draws the edge and the buttons itself, in the system's light colours, so both are
/// drawn over after they paint.
/// </summary>
static class SpinLook
{
    public static void Attach(NumericUpDown number)
    {
        Overpaint.Attach(number, DrawEdge);
        // Its two parts: the text box, and the buttons.
        if (number.Controls.Cast<Control>().FirstOrDefault(c => c is not TextBoxBase) is Control buttons)
        {
            int pressed = 0; // 1 the up button, 2 the down one
            buttons.MouseDown += (_, e) => { pressed = e.Y < buttons.Height / 2 ? 1 : 2; buttons.Invalidate(); };
            buttons.MouseUp += (_, _) => { pressed = 0; buttons.Invalidate(); };
            buttons.MouseLeave += (_, _) => { pressed = 0; buttons.Invalidate(); };
            Overpaint.Attach(buttons, (c, g) => DrawButtons(c, g, number.Enabled, pressed));
        }
    }

    static void DrawEdge(Control number, Graphics g)
    {
        var r = number.ClientRectangle;
        if (r.Width <= 2 || r.Height <= 2)
            return;
        // All round the text box and the buttons (windows of their own, left out of this drawing, or printed after it).
        using (var fill = new SolidBrush(BarTheme.Field))
            g.FillRectangle(fill, r);
        using var edge = new Pen(number.ContainsFocus ? BarTheme.Focus : BarTheme.FieldEdge);
        g.DrawRectangle(edge, 0, 0, r.Width - 1, r.Height - 1);
    }

    static void DrawButtons(Control buttons, Graphics g, bool enabled, int pressed)
    {
        var r = buttons.ClientRectangle;
        if (r.Width <= 0 || r.Height <= 0)
            return;
        int half = r.Height / 2;
        var up = new Rectangle(0, 0, r.Width, half);
        var down = new Rectangle(0, half, r.Width, r.Height - half);
        using (var back = new SolidBrush(BarTheme.Field))
            g.FillRectangle(back, r);
        using var knob = new SolidBrush(BarTheme.FieldKnob);
        using var held = new SolidBrush(BarTheme.ButtonDown);
        using var arrow = new SolidBrush(enabled ? BarTheme.Text : BarTheme.Disabled);
        foreach (var (part, isUp, which) in new[] { (up, true, 1), (down, false, 2) })
        {
            var face = Rectangle.Inflate(part, -1, 0);
            face.Height -= 1;
            if (!isUp)
                face.Y += 1;
            g.FillRectangle(pressed == which ? held : knob, face);
            int w = Math.Max(2, Math.Min(face.Width, face.Height * 2) / 4), cx = face.X + face.Width / 2, cy = face.Y + face.Height / 2;
            int h = Math.Max(1, w / 2);
            Point[] tri = isUp
                ? [new(cx - w, cy + h / 2 + 1), new(cx + w + 1, cy + h / 2 + 1), new(cx, cy - h)]
                : [new(cx - w, cy - h / 2), new(cx + w + 1, cy - h / 2), new(cx, cy + h + 1)];
            g.FillPolygon(arrow, tri);
        }
    }
}

/// <summary>Draws over a control after it has painted itself (and when it is printed): for the parts of Windows' own controls the theme can't set.</summary>
sealed class Overpaint : NativeWindow
{
    const int WM_PAINT = 0x000F, WM_PRINTCLIENT = 0x0318;
    readonly Control control;
    readonly Action<Control, Graphics> draw;

    Overpaint(Control control, Action<Control, Graphics> draw)
    {
        this.control = control;
        this.draw = draw;
        control.HandleCreated += (_, _) => AssignHandle(control.Handle);
        control.HandleDestroyed += (_, _) => ReleaseHandle();
        control.GotFocus += (_, _) => control.Invalidate();
        control.LostFocus += (_, _) => control.Invalidate();
        control.EnabledChanged += (_, _) => control.Invalidate();
        if (control.IsHandleCreated)
            AssignHandle(control.Handle);
    }

    public static void Attach(Control control, Action<Control, Graphics> draw) => _ = new Overpaint(control, draw);

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_PAINT)
        {
            using var g = Graphics.FromHwnd(Handle);
            draw(control, g);
        }
        else if (m.Msg == WM_PRINTCLIENT)
        {
            using var g = Graphics.FromHdc(m.WParam);
            draw(control, g);
        }
    }
}

/// <summary>
/// A list (the asset browser, the units, the metal spots) dark: Windows' dark Explorer look for its scroll bars, the
/// dark items look for its column headings, and its group headings drawn here as the panels' headings are (Explorer's
/// dark ones are a blue that is hard to read on the dark).
/// </summary>
sealed class ListLook : NativeWindow
{
    const int WM_REFLECT_NOTIFY = 0x2000 + 0x004E, NM_CUSTOMDRAW = -12, CDDS_PREPAINT = 1, CDDS_ITEMPREPAINT = 0x10001,
        CDRF_NOTIFYITEMDRAW = 0x20, CDRF_SKIPDEFAULT = 4, LVCDI_GROUP = 1, LVM_GETHEADER = 0x101F, LVM_GETGROUPRECT = 0x1062,
        LVM_GETGROUPINFO = 0x1095, LVGF_HEADER = 1, LVGGR_HEADER = 1;
    readonly ListView list;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NMHDR
    {
        public IntPtr From, Id;
        public int Code;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NMLVCUSTOMDRAW
    {
        public NMHDR Header;
        public int Stage;
        public IntPtr Hdc;
        public RECT Bounds;
        public IntPtr ItemSpec;
        public int ItemState;
        public IntPtr ItemParam;
        public int TextColour, TextBackColour, SubItem, ItemType;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct LVGROUP
    {
        public int Size, Mask;
        public IntPtr Header;
        public int HeaderLength;
        public IntPtr Footer;
        public int FooterLength, GroupId, StateMask, State, Align;
    }

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref RECT lParam);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref LVGROUP lParam);

    ListLook(ListView list)
    {
        this.list = list;
        list.HandleCreated += (_, _) => Hook();
        list.HandleDestroyed += (_, _) => ReleaseHandle();
        if (list.IsHandleCreated)
            Hook();
    }

    public static void Attach(ListView list)
    {
        if (list.CheckBoxes && list.StateImageList == null)
            list.StateImageList = ToggleLook.ListBoxes(list.Font); // in place of Windows' light tick boxes
        _ = new ListLook(list);
    }

    void Hook()
    {
        AssignHandle(list.Handle);
        BarTheme.Dark(list, "DarkMode_Explorer");
        var header = SendMessage(list.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
        if (header != IntPtr.Zero)
            BarTheme.Dark(header, "DarkMode_ItemsView");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_REFLECT_NOTIFY && list.ShowGroups && Marshal.PtrToStructure<NMHDR>(m.LParam).Code == NM_CUSTOMDRAW)
        {
            var draw = Marshal.PtrToStructure<NMLVCUSTOMDRAW>(m.LParam);
            // A group comes as a paint of its own (stage "pre-paint", its type "group"), not as an item.
            if (draw.ItemType == LVCDI_GROUP && draw.Stage is CDDS_PREPAINT or CDDS_ITEMPREPAINT && DrawGroup(draw))
            {
                m.Result = CDRF_SKIPDEFAULT;
                return;
            }
            base.WndProc(ref m);
            if (draw.Stage == CDDS_PREPAINT)
                m.Result = (IntPtr)((long)m.Result | CDRF_NOTIFYITEMDRAW); // ask for the groups too
            return;
        }
        base.WndProc(ref m);
    }

    /// <summary>A group's heading: its text in the headings' colour, a faint line on from it. False if it can't be found.</summary>
    bool DrawGroup(NMLVCUSTOMDRAW draw)
    {
        int id = (int)draw.ItemSpec;
        var bounds = new RECT { Top = LVGGR_HEADER };
        if (SendMessage(Handle, LVM_GETGROUPRECT, (IntPtr)id, ref bounds) == IntPtr.Zero)
            return false;
        var text = new char[256];
        var handle = GCHandle.Alloc(text, GCHandleType.Pinned);
        string title;
        try
        {
            var group = new LVGROUP { Size = Marshal.SizeOf<LVGROUP>(), Mask = LVGF_HEADER, Header = handle.AddrOfPinnedObject(), HeaderLength = text.Length };
            if (SendMessage(Handle, LVM_GETGROUPINFO, (IntPtr)id, ref group) == (IntPtr)(-1))
                return false;
            title = new string(text, 0, Math.Max(0, Array.IndexOf(text, '\0')));
        }
        finally
        {
            handle.Free();
        }
        using var g = Graphics.FromHdc(draw.Hdc);
        var r = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        using (var fill = new SolidBrush(list.BackColor))
            g.FillRectangle(fill, r);
        var font = BarTheme.Semibold(list.Font);
        int x = r.X + list.LogicalToDeviceUnits(10);
        var size = TextRenderer.MeasureText(g, title, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, title, font, new Rectangle(x, r.Y, Math.Max(1, r.Width - x), r.Height), BarTheme.Heading,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        int y = r.Y + r.Height / 2;
        using var rule = new Pen(BarTheme.Rule);
        g.DrawLine(rule, x + size.Width + list.LogicalToDeviceUnits(8), y, r.Right - list.LogicalToDeviceUnits(10), y);
        return true;
    }
}
