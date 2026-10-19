using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MapCore.Editing;
using MapCore.Imaging;
using MapCore.Rendering;

namespace MapEditor;

/// <summary>A brush tool's footprint on the map (game units), and whether holding still keeps applying it.</summary>
sealed record BrushCursor(double Radius, double Hardness, bool RepeatWhileHeld);

/// <summary>A ring drawn over the map (game units), e.g. the selected metal spot or where a dragged one would land.</summary>
sealed record ViewMark(double X, double Z, double Radius, Color Colour, bool Dashed = false, string? Label = null);

/// <summary>A line through map points (game units) drawn over the map; closed shapes return to the first point.</summary>
sealed record ViewShape(IReadOnlyList<(double X, double Z)> Points, Color Colour, bool Closed = true, bool Dashed = false);

/// <summary>A point of a brush stroke, in game units. A handler of the stroke's start may cancel the stroke.</summary>
sealed class StrokeEventArgs(double x, double z, Keys modifiers) : EventArgs
{
    public double X { get; } = x;
    public double Z { get; } = z;
    public Keys Modifiers { get; } = modifiers;
    public bool Cancel { get; set; }
}

/// <summary>
/// The top-down map view: MapCore renders the visible part into an <see cref="RgbaImage"/>, this control copies it
/// to a bitmap and draws the labels. Wheel zooms about the cursor; dragging with the right or middle button pans.
/// With a <see cref="Brush"/> set, the left button paints strokes, cut into dabs a quarter of the radius apart
/// (and repeated while held still, for tools that build up); without one, the left button pans too.
/// </summary>
sealed class MapViewControl : Control
{
    const int RepeatMilliseconds = 100;

    const double ZoomStep = 1.25;
    const double MaxZoomIn = 1.0 / 16; // game units per pixel: 16 screen pixels per texel

    static readonly Color Backdrop = Color.FromArgb(MapRenderer.Background.R, MapRenderer.Background.G, MapRenderer.Background.B);

    MapEditSession? session;
    MapRenderer? renderer;
    ViewTransform view = new(0, 0, 1);
    MapViewLayer layer = MapViewLayer.Texture;
    MapOverlays overlays = MapOverlays.MetalSpots | MapOverlays.Items | MapOverlays.StartPositions;
    RgbaImage? frame;
    byte[]? bgra;
    Bitmap? bitmap;
    RenderResult? last;
    bool dirty = true;
    Size lastSize;
    Point? dragFrom;
    ViewTransform dragView;
    BrushCursor? brush;
    Point? mouse;
    bool stroking, movedSinceTick, frozen, pointing;
    IReadOnlyList<ViewMark> marks = [];
    IReadOnlyList<ViewShape> shapes = [];
    (double X, double Z) lastDab, cursorWorld;
    Keys? simulatedModifiers;
    readonly System.Windows.Forms.Timer repeat = new() { Interval = RepeatMilliseconds };

    public MapViewControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Backdrop;
        Font = new Font("Segoe UI", 8.25f);
        repeat.Tick += (_, _) =>
        {
            if (stroking && brush?.RepeatWhileHeld == true && !movedSinceTick)
                RaiseDab(cursorWorld.X, cursorWorld.Z);
            movedSinceTick = false;
        };
    }

    /// <summary>A stroke begins (left button down with a brush). Set Cancel to use the click for something else.</summary>
    public event EventHandler<StrokeEventArgs>? StrokeStarted;

    /// <summary>One dab of the current stroke.</summary>
    public event EventHandler<StrokeEventArgs>? Dab;

    /// <summary>The stroke is over: the button was released, or the view lost the mouse or the focus.</summary>
    public event EventHandler? StrokeEnded;

    /// <summary>The active brush (null: no tool, the left button pans). Changing it ends a stroke in progress.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public BrushCursor? Brush
    {
        get => brush;
        set
        {
            if (stroking && value != brush)
                EndStroke();
            brush = value;
            Invalidate();
        }
    }

    public bool IsStroking => stroking;

    /// <summary>
    /// With no brush: the left button picks and drags things (Pointer events) instead of panning. Right or middle
    /// drag still pans.
    /// </summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool PointerMode { get; set; }

    /// <summary>Left button pressed in pointer mode (game units). Set Cancel to ignore the rest of this press.</summary>
    public event EventHandler<StrokeEventArgs>? PointerDown;

    /// <summary>The pointer moved while the left button is held.</summary>
    public event EventHandler<StrokeEventArgs>? PointerDrag;

    /// <summary>The left button was released.</summary>
    public event EventHandler<StrokeEventArgs>? PointerUp;

    /// <summary>The press ended without a release (the view lost the mouse or the focus): apply nothing.</summary>
    public event EventHandler? PointerCancelled;

    /// <summary>A dashed rectangle between two map points (box selection), or null.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public (double X1, double Z1, double X2, double Z2)? SelectionBox
    {
        get => selectionBox;
        set
        {
            selectionBox = value;
            Invalidate();
        }
    }

    (double X1, double Z1, double X2, double Z2)? selectionBox;

    /// <summary>Rings drawn over the map.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<ViewMark> Marks
    {
        get => marks;
        set
        {
            marks = value;
            Invalidate();
        }
    }

    /// <summary>Outlines drawn over the map, under the rings.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<ViewShape> Shapes
    {
        get => shapes;
        set
        {
            shapes = value;
            Invalidate();
        }
    }

    /// <summary>
    /// The symmetry edits are made with (null: none): its mirror line is drawn over the map (a mark at the centre for a
    /// half turn), and a brush's outline at the cursor's mirror image too.
    /// </summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MapCore.MapMirror? Mirror
    {
        get => mirror;
        set
        {
            mirror = value;
            Invalidate();
        }
    }

    MapCore.MapMirror? mirror;

    /// <summary>Raised when the cursor moves over the view: the world position, or null when it leaves.</summary>
    public event EventHandler<(double X, double Z)?>? CursorMoved;

    /// <summary>Raised after zooming or panning, and after each render (the layer note may change).</summary>
    public event EventHandler? ViewChanged;

    public bool HasMap => renderer != null;

    /// <summary>While true the view shows its last frame and leaves the session alone (background work owns it).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Frozen
    {
        get => frozen;
        set
        {
            if (value && stroking)
                EndStroke();
            frozen = value;
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ViewTransform View
    {
        get => view;
        set
        {
            view = value;
            Redraw();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MapViewLayer Layer
    {
        get => layer;
        set
        {
            layer = value;
            Redraw();
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MapOverlays Overlays
    {
        get => overlays;
        set
        {
            overlays = value;
            Redraw();
        }
    }

    /// <summary>What the renderer said about the current layer last time, if anything.</summary>
    public string? Note => last?.Note;

    public void Attach(MapEditSession mapSession, MapRenderer mapRenderer)
    {
        session = mapSession;
        renderer = mapRenderer;
        last = null;
        ZoomToFit();
    }

    public void Detach()
    {
        EndStroke();
        session = null;
        renderer = null;
        last = null;
        bitmap?.Dispose();
        bitmap = null;
        frame = null;
        Invalidate();
    }

    /// <summary>The map changed: render again on the next paint.</summary>
    public void Redraw()
    {
        dirty = true;
        Invalidate();
    }

    public void ZoomToFit()
    {
        if (session != null)
            View = ViewTransform.Fit(session.MapWidth, session.MapHeight, Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
    }

    /// <summary>Zooms about the middle of the view (above 1 zooms in).</summary>
    public void ZoomBy(double factor) => ZoomAt(ClientSize.Width / 2.0, ClientSize.Height / 2.0, factor);

    void ZoomAt(double x, double y, double factor)
    {
        if (session == null)
            return;
        double fit = ViewTransform.Fit(session.MapWidth, session.MapHeight, Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height)).UnitsPerPixel;
        double units = Math.Clamp(view.UnitsPerPixel / factor, MaxZoomIn, fit * 4);
        View = view.ZoomAbout(x, y, view.UnitsPerPixel / units);
    }

    protected override void OnPaint(PaintEventArgs e) => Draw(e.Graphics, ClientSize);

    /// <summary>Renders if needed and draws the map and its labels; also used to save the view as an image.</summary>
    public void Draw(Graphics g, Size size)
    {
        g.Clear(Backdrop);
        if (renderer == null)
        {
            TextRenderer.DrawText(g, "Open a map with File > Open, or drop a .sd7 / .sdz file here.", Font,
                new Rectangle(Point.Empty, size), Color.Gainsboro, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        if (size.Width <= 0 || size.Height <= 0)
            return;
        if (!Frozen && (dirty || size != lastSize || bitmap == null))
            Render(size);
        if (bitmap != null)
        {
            // Pixel rectangles on both sides: DrawImageUnscaled would scale by the bitmap's DPI on high-DPI screens.
            var pixels = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.DrawImage(bitmap, pixels, pixels, GraphicsUnit.Pixel);
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
        }
        if (last != null)
            ViewPainting.DrawLabels(g, Font, last.Labels, size);
        DrawMarks(g);
        if (mirror is MapCore.MapMirror symmetry)
            DrawMirror(g, symmetry);
        if (brush != null && mouse is Point m && new Rectangle(Point.Empty, size).Contains(m))
        {
            DrawBrush(g, m, brush);
            if (mirror is MapCore.MapMirror copy)
            {
                // Where the brush's mirror image lands (a dashed outline): what it does is done there too.
                var (wx, wz) = view.ToWorld(m.X + 0.5, m.Y + 0.5);
                var (ix, iz) = copy.Map(wx, wz);
                var (sx, sy) = view.ToImage(ix, iz);
                DrawBrush(g, new Point((int)Math.Round(sx), (int)Math.Round(sy)), brush, mirrored: true);
            }
        }
    }

    /// <summary>The mirror line across the map, dashed; for a half turn, a mark at the centre it turns about.</summary>
    void DrawMirror(Graphics g, MapCore.MapMirror symmetry)
    {
        var (w, h) = (symmetry.Width, symmetry.Height);
        (double X, double Z) a, b;
        switch (symmetry.Kind)
        {
            case MapCore.MirrorKind.LeftRight: (a, b) = ((w / 2, 0), (w / 2, h)); break;
            case MapCore.MirrorKind.TopBottom: (a, b) = ((0, h / 2), (w, h / 2)); break;
            case MapCore.MirrorKind.Diagonal: (a, b) = ((0, 0), (w, h)); break;
            case MapCore.MirrorKind.AntiDiagonal: (a, b) = ((w, 0), (0, h)); break;
            default:
            {
                var (cx, cy) = view.ToImage(w / 2, h / 2);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var ring = new Pen(MirrorColour, 2);
                using var under = new Pen(Color.FromArgb(170, 0, 0, 0), 4);
                const float r = 11;
                foreach (var pen in new[] { under, ring })
                {
                    g.DrawEllipse(pen, (float)cx - r, (float)cy - r, 2 * r, 2 * r);
                    g.DrawLine(pen, (float)cx - r - 6, (float)cy, (float)cx + r + 6, (float)cy);
                    g.DrawLine(pen, (float)cx, (float)cy - r - 6, (float)cx, (float)cy + r + 6);
                }
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
                return;
            }
        }
        var (x1, y1) = view.ToImage(a.X, a.Z);
        var (x2, y2) = view.ToImage(b.X, b.Z);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var shadow = new Pen(Color.FromArgb(150, 0, 0, 0), 4))
            g.DrawLine(shadow, (float)x1, (float)y1, (float)x2, (float)y2);
        using (var line = new Pen(MirrorColour, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
            g.DrawLine(line, (float)x1, (float)y1, (float)x2, (float)y2);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
    }

    /// <summary>The mirror line's colour: BAR's amber, apart from the white of brushes and selections.</summary>
    static readonly Color MirrorColour = Color.FromArgb(240, 186, 74);

    void DrawMarks(Graphics g)
    {
        if (selectionBox is (double bx1, double bz1, double bx2, double bz2))
        {
            var (x1, y1) = view.ToImage(Math.Min(bx1, bx2), Math.Min(bz1, bz2));
            var (x2, y2) = view.ToImage(Math.Max(bx1, bx2), Math.Max(bz1, bz2));
            using var dashed = new Pen(Color.White, 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            g.DrawRectangle(dashed, (float)x1, (float)y1, (float)(x2 - x1), (float)(y2 - y1));
        }
        if (marks.Count == 0 && shapes.Count == 0)
            return;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var shadow = new Pen(Color.FromArgb(170, 0, 0, 0), 4);
        foreach (var shape in shapes)
        {
            if (shape.Points.Count < 2)
                continue;
            var points = shape.Points.Select(p => view.ToImage(p.X, p.Z)).Select(p => new PointF((float)p.X, (float)p.Y)).ToArray();
            using var pen = new Pen(shape.Colour, 2) { DashStyle = shape.Dashed ? System.Drawing.Drawing2D.DashStyle.Dash : System.Drawing.Drawing2D.DashStyle.Solid };
            if (shape.Closed)
            {
                g.DrawPolygon(shadow, points);
                g.DrawPolygon(pen, points);
            }
            else
            {
                g.DrawLines(shadow, points);
                g.DrawLines(pen, points);
            }
        }
        foreach (var mark in marks)
        {
            var (x, y) = view.ToImage(mark.X, mark.Z);
            float r = (float)Math.Max(7, mark.Radius / view.UnitsPerPixel);
            using var pen = new Pen(mark.Colour, 2) { DashStyle = mark.Dashed ? System.Drawing.Drawing2D.DashStyle.Dash : System.Drawing.Drawing2D.DashStyle.Solid };
            g.DrawEllipse(shadow, (float)x - r, (float)y - r, 2 * r, 2 * r);
            g.DrawEllipse(pen, (float)x - r, (float)y - r, 2 * r, 2 * r);
            if (mark.Label != null)
                ViewPainting.DrawLabels(g, Font, [new MapLabel(x, y + r + 3, mark.Label, new MapCore.Imaging.Rgba(mark.Colour.R, mark.Colour.G, mark.Colour.B, 255))], ClientSize.IsEmpty ? new Size(10000, 10000) : ClientSize);
        }
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
    }

    /// <summary>The brush outline at the cursor, with its full-strength core (radius × hardness) dashed; its mirror image's dashed, in the mirror line's colour.</summary>
    void DrawBrush(Graphics g, Point at, BrushCursor cursor, bool mirrored = false)
    {
        float r = (float)(cursor.Radius / view.UnitsPerPixel), core = r * (float)Math.Clamp(cursor.Hardness, 0, 1);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var shadow = new Pen(Color.FromArgb(160, 0, 0, 0), 3);
        using var outline = mirrored ? new Pen(MirrorColour, 1.2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash } : new Pen(Color.White, 1.2f);
        using var dashed = new Pen(Color.FromArgb(220, 255, 255, 255), 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        g.DrawEllipse(shadow, at.X - r, at.Y - r, 2 * r, 2 * r);
        g.DrawEllipse(outline, at.X - r, at.Y - r, 2 * r, 2 * r);
        if (core > 2 && core < r - 2)
            g.DrawEllipse(dashed, at.X - core, at.Y - core, 2 * core, 2 * core);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;
    }

    /// <summary>For the self-test: where the brush outline is drawn (null hides it).</summary>
    internal void ShowBrushAt(Point? at) => mouse = at;

    void Render(Size size)
    {
        if (frame == null || frame.Width != size.Width || frame.Height != size.Height)
        {
            frame = new RgbaImage(size.Width, size.Height);
            bgra = new byte[frame.Pixels.Length];
            bitmap?.Dispose();
            bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        }
        last = renderer!.Render(frame, view, layer, overlays);
        ViewPainting.CopyToBitmap(frame, bgra!, bitmap!);
        lastSize = size;
        dirty = false;
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (renderer != null && lastSize.Width > 0 && ClientSize.Width > 0 && ClientSize.Height > 0)
        {
            view = view.Resize(lastSize.Width, lastSize.Height, ClientSize.Width, ClientSize.Height);
            lastSize = ClientSize;
            Redraw();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ZoomAt(e.X, e.Y, Math.Pow(ZoomStep, e.Delta / 120.0));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (renderer == null || frozen || stroking || pointing || dragFrom != null)
            return;
        if (e.Button == MouseButtons.Left && brush != null)
            BeginStroke(e.Location);
        else if (e.Button == MouseButtons.Left && PointerMode)
        {
            var (x, z) = view.ToWorld(e.X + 0.5, e.Y + 0.5);
            var press = new StrokeEventArgs(x, z, simulatedModifiers ?? ModifierKeys);
            PointerDown?.Invoke(this, press);
            if (!press.Cancel)
            {
                pointing = true;
                Capture = true;
            }
        }
        else if (e.Button is MouseButtons.Left or MouseButtons.Middle or MouseButtons.Right)
        {
            dragFrom = e.Location;
            dragView = view;
            Capture = true;
            Cursor = Cursors.SizeAll;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        mouse = e.Location;
        if (renderer == null)
            return;
        var world = view.ToWorld(e.X + 0.5, e.Y + 0.5);
        if (dragFrom is Point from)
            View = dragView.Pan(e.X - from.X, e.Y - from.Y);
        else if (stroking)
            StrokeTo(world);
        else if (pointing)
            PointerDrag?.Invoke(this, new StrokeEventArgs(world.X, world.Z, simulatedModifiers ?? ModifierKeys));
        else if (brush != null)
            Invalidate(); // move the brush outline
        CursorMoved?.Invoke(this, world);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (stroking && e.Button == MouseButtons.Left)
            EndStroke();
        else if (pointing && e.Button == MouseButtons.Left)
        {
            pointing = false; // first: releasing the capture calls OnMouseCaptureChanged
            Capture = false;
            var (x, z) = view.ToWorld(e.X + 0.5, e.Y + 0.5);
            PointerUp?.Invoke(this, new StrokeEventArgs(x, z, simulatedModifiers ?? ModifierKeys));
        }
        else if (dragFrom != null)
            EndDrag();
    }

    /// <summary>Losing the mouse (another window, a message box) ends a stroke, so it never stays open.</summary>
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (stroking)
            EndStroke();
        CancelPointer();
        if (dragFrom != null)
            EndDrag();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        if (stroking)
            EndStroke();
        CancelPointer();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        mouse = null;
        Invalidate();
        CursorMoved?.Invoke(this, null);
    }

    void BeginStroke(Point at)
    {
        var (x, z) = view.ToWorld(at.X + 0.5, at.Y + 0.5);
        var start = new StrokeEventArgs(x, z, simulatedModifiers ?? ModifierKeys);
        StrokeStarted?.Invoke(this, start);
        if (start.Cancel)
            return;
        stroking = true;
        Capture = true;
        lastDab = cursorWorld = (x, z);
        movedSinceTick = false;
        RaiseDab(x, z);
        if (brush?.RepeatWhileHeld == true)
            repeat.Start();
    }

    /// <summary>Dabs along the way from the last dab, a quarter of the brush radius apart (at least one pixel).</summary>
    void StrokeTo((double X, double Z) to)
    {
        cursorWorld = to;
        movedSinceTick = true;
        if (brush == null)
            return;
        double spacing = Math.Max(brush.Radius / 4, view.UnitsPerPixel);
        double dx = to.X - lastDab.X, dz = to.Z - lastDab.Z, distance = Math.Sqrt(dx * dx + dz * dz);
        int steps = (int)(distance / spacing);
        for (int i = 1; i <= steps && stroking; i++)
        {
            lastDab = (lastDab.X + dx / distance * spacing, lastDab.Z + dz / distance * spacing);
            RaiseDab(lastDab.X, lastDab.Z);
        }
    }

    void RaiseDab(double x, double z) => Dab?.Invoke(this, new StrokeEventArgs(x, z, simulatedModifiers ?? ModifierKeys));

    void EndStroke()
    {
        if (!stroking)
            return;
        stroking = false; // first: releasing the capture below calls OnMouseCaptureChanged
        repeat.Stop();
        Capture = false;
        StrokeEnded?.Invoke(this, EventArgs.Empty);
    }

    void CancelPointer()
    {
        if (!pointing)
            return;
        pointing = false;
        Capture = false;
        PointerCancelled?.Invoke(this, EventArgs.Empty);
    }

    void EndDrag()
    {
        dragFrom = null;
        Capture = false;
        Cursor = Cursors.Default;
    }

    /// <summary>For the self-test: runs the real mouse handlers with a given button, position and modifier keys.</summary>
    internal void SimulateMouse(MouseButtons button, string action, int x, int y, Keys modifiers = Keys.None)
    {
        simulatedModifiers = modifiers;
        try
        {
            var e = new MouseEventArgs(button, 1, x, y, 0);
            switch (action)
            {
                case "down": OnMouseDown(e); break;
                case "move": OnMouseMove(e); break;
                case "up": OnMouseUp(e); break;
                default: throw new ArgumentException($"Unknown mouse action '{action}'.", nameof(action));
            }
        }
        finally
        {
            simulatedModifiers = null;
        }
    }

    /// <summary>For the self-test: the view loses the mouse (as when a message box opens mid-stroke).</summary>
    internal void SimulateCaptureLost() => OnMouseCaptureChanged(EventArgs.Empty);

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (renderer == null)
            return;
        int step = Math.Max(ClientSize.Width, ClientSize.Height) / 8;
        switch (e.KeyCode)
        {
            case Keys.Left: View = view.Pan(step, 0); break;
            case Keys.Right: View = view.Pan(-step, 0); break;
            case Keys.Up: View = view.Pan(0, step); break;
            case Keys.Down: View = view.Pan(0, -step); break;
            case Keys.Home: ZoomToFit(); break;
            case Keys.Add or Keys.Oemplus: ZoomBy(ZoomStep * ZoomStep); break;
            case Keys.Subtract or Keys.OemMinus: ZoomBy(1 / (ZoomStep * ZoomStep)); break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            repeat.Dispose();
            bitmap?.Dispose();
        }
        base.Dispose(disposing);
    }
}
