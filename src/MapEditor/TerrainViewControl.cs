using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using MapCore.Editing;
using MapCore.Imaging;
using MapCore.Rendering;

namespace MapEditor;

/// <summary>
/// The 3D view: MapCore's <see cref="TerrainView"/> draws the terrain, this control shows it and moves the camera.
/// Left drag orbits, right or middle drag moves the camera over the map, the wheel zooms. While the camera moves or
/// edits keep arriving it draws quick frames (every 4th vertex, half resolution); once things are still for a
/// moment it draws the full-detail frame.
/// </summary>
sealed class TerrainViewControl : Control
{
    const int QuickStep = 4, SettleMilliseconds = 220;

    TerrainView? view;
    MapEditSession? session;
    OrbitCamera camera;
    TerrainOptions options = new() { Markers = MapOverlays.StartPositions | MapOverlays.MetalSpots, Models = true };
    RgbaImage? frame;
    byte[]? bgra;
    Bitmap? bitmap;
    RenderResult? last;
    bool dirty = true, quick, frozen;
    double lastScale = 1;
    Point? dragFrom;
    MouseButtons dragButton;
    OrbitCamera dragCamera;
    readonly System.Windows.Forms.Timer settle = new() { Interval = SettleMilliseconds };

    public TerrainViewControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Color.FromArgb(MapRenderer.Background.R, MapRenderer.Background.G, MapRenderer.Background.B);
        Font = new Font("Segoe UI", 8.25f);
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            quick = false;
            Redraw(still: true);
        };
    }

    public event EventHandler? CameraChanged;

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public OrbitCamera Camera
    {
        get => camera;
        set
        {
            camera = value;
            Moved();
            CameraChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TerrainOptions Options
    {
        get => options;
        set
        {
            options = value;
            Redraw(still: true);
        }
    }

    /// <summary>While true the view shows its last frame and leaves the session alone (background work owns it).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Frozen
    {
        get => frozen;
        set
        {
            frozen = value;
            if (!value)
                Redraw(still: true);
        }
    }

    public bool HasMap => view != null;

    public void Attach(TerrainView terrain, MapEditSession mapSession)
    {
        view = terrain;
        session = mapSession;
        camera = OrbitCamera.Overview(mapSession.MapWidth, mapSession.MapHeight, Math.Max(1, Width) / (double)Math.Max(1, Height));
        Redraw(still: true);
    }

    public void Detach()
    {
        settle.Stop();
        view = null;
        session = null;
        last = null;
        Invalidate();
    }

    /// <summary>The whole map from the south.</summary>
    public void ResetCamera()
    {
        if (session != null)
            Camera = OrbitCamera.Overview(session.MapWidth, session.MapHeight, Math.Max(1, Width) / (double)Math.Max(1, Height));
    }

    /// <summary>The map changed: quick frames while edits keep coming, then a full one.</summary>
    public void MapChanged() => Moved();

    void Moved()
    {
        quick = true;
        Redraw(still: false);
        settle.Stop();
        settle.Start();
    }

    void Redraw(bool still)
    {
        if (still)
            quick = false;
        dirty = true;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e) => Draw(e.Graphics, ClientSize);

    /// <summary>Renders if needed and draws the frame and its labels; also used to save the view as an image.</summary>
    public void Draw(Graphics g, Size size)
    {
        g.Clear(BackColor);
        if (view == null)
        {
            TextRenderer.DrawText(g, "Open a map to see it in 3D.", Font, new Rectangle(Point.Empty, size), Color.Gainsboro,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        if (size.Width <= 0 || size.Height <= 0)
            return;
        double scale = quick ? 0.5 : 1;
        if (!frozen && (dirty || bitmap == null || lastScale != scale || frame == null ||
                        frame.Width != Math.Max(1, (int)(size.Width * scale)) || frame.Height != Math.Max(1, (int)(size.Height * scale))))
            Render(size, scale);
        if (bitmap == null)
            return;
        g.InterpolationMode = lastScale < 1 ? InterpolationMode.Bilinear : InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(bitmap, new Rectangle(0, 0, size.Width, size.Height), new Rectangle(0, 0, bitmap.Width, bitmap.Height), GraphicsUnit.Pixel);
        g.PixelOffsetMode = PixelOffsetMode.Default;
        if (last != null)
            ViewPainting.DrawLabels(g, Font, last.Labels, size, 1 / lastScale);
    }

    void Render(Size size, double scale)
    {
        int w = Math.Max(1, (int)(size.Width * scale)), h = Math.Max(1, (int)(size.Height * scale));
        if (frame == null || frame.Width != w || frame.Height != h)
        {
            frame = new RgbaImage(w, h);
            bgra = new byte[frame.Pixels.Length];
            bitmap?.Dispose();
            bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        }
        last = view!.Render(frame, camera, options with { MeshStep = scale < 1 ? QuickStep : 1 });
        ViewPainting.CopyToBitmap(frame, bgra!, bitmap!);
        lastScale = scale;
        dirty = false;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (view == null || frozen || dragFrom != null)
            return;
        dragFrom = e.Location;
        dragButton = e.Button;
        dragCamera = camera;
        Capture = true;
        Cursor = e.Button == MouseButtons.Left ? Cursors.Hand : Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragFrom is not Point from || session == null)
            return;
        int dx = e.X - from.X, dy = e.Y - from.Y;
        Camera = dragButton == MouseButtons.Left
            ? dragCamera.Orbit(dx * 0.3, dy * 0.25)
            : dragCamera.Pan(dx, dy, Math.Max(1, Height), session.MapWidth, session.MapHeight);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        EndDrag();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        EndDrag();
    }

    void EndDrag()
    {
        if (dragFrom == null)
            return;
        dragFrom = null;
        Capture = false;
        Cursor = Cursors.Default;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (view != null && !frozen)
            Camera = camera.Zoom(Math.Pow(1.15, e.Delta / 120.0));
    }

    /// <summary>For the self-test: runs the real mouse handlers ("down", "move", "up", "wheel").</summary>
    internal void SimulateMouse(MouseButtons button, string action, int x, int y, int wheelDelta = 0)
    {
        var e = new MouseEventArgs(button, 1, x, y, wheelDelta);
        switch (action)
        {
            case "down": OnMouseDown(e); break;
            case "move": OnMouseMove(e); break;
            case "up": OnMouseUp(e); break;
            case "wheel": OnMouseWheel(e); break;
            default: throw new ArgumentException($"Unknown mouse action '{action}'.", nameof(action));
        }
    }

    /// <summary>For the self-test: the view loses the mouse in the middle of a drag.</summary>
    internal void SimulateCaptureLost() => OnMouseCaptureChanged(EventArgs.Empty);

    /// <summary>For the self-test: where a world point shows in the last frame, in control pixels.</summary>
    internal (double X, double Y)? ProjectForTest(double x, double y, double z) =>
        view?.Project(x, y, z) is (double px, double py, _) ? (px / lastScale, py / lastScale) : null;

    /// <summary>For the self-test: the ground shown at a control pixel in the last frame.</summary>
    internal (double X, double Z)? GroundForTest(int px, int py) => view?.GroundAt((int)(px * lastScale), (int)(py * lastScale));

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (view == null || frozen)
            return;
        switch (e.KeyCode)
        {
            case Keys.Left: Camera = camera.Orbit(-10, 0); break;
            case Keys.Right: Camera = camera.Orbit(10, 0); break;
            case Keys.Up: Camera = camera.Orbit(0, 5); break;
            case Keys.Down: Camera = camera.Orbit(0, -5); break;
            case Keys.Add or Keys.Oemplus: Camera = camera.Zoom(1.3); break;
            case Keys.Subtract or Keys.OemMinus: Camera = camera.Zoom(1 / 1.3); break;
            case Keys.Home: ResetCamera(); break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            settle.Dispose();
            bitmap?.Dispose();
        }
        base.Dispose(disposing);
    }
}
