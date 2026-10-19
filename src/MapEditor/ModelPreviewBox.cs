using System.Drawing.Imaging;
using MapCore.Assets;
using MapCore.Rendering;

namespace MapEditor;

/// <summary>
/// A small picture of a model, to check it is the one wanted before placing or adding it: drag to turn it round,
/// double-click to face it again. When there is no model to draw it says why instead.
/// </summary>
sealed class ModelPreviewBox : Control
{
    const double DefaultYaw = 35, DefaultPitch = 20;
    static readonly Color Backdrop = Color.FromArgb(38, 42, 49);
    readonly ToolTip tip = new();
    ModelPreview? model;
    string caption = "", message = "";
    double yaw = DefaultYaw, pitch = DefaultPitch;
    Bitmap? picture;
    byte[]? bgra;
    bool stale = true;
    Point? dragFrom;

    public ModelPreviewBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Height = 190;
        Margin = new Padding(3, 4, 3, 4);
        ShowMessage("");
    }

    /// <summary>Shows a model, facing it the usual way; <paramref name="text"/> goes under it (and, with the notes, in its tooltip).</summary>
    public void ShowModel(ModelPreview preview, string text)
    {
        model = preview;
        caption = text;
        message = "";
        yaw = DefaultYaw;
        pitch = DefaultPitch;
        stale = true;
        tip.SetToolTip(this, string.Join(Environment.NewLine, new[] { text }.Concat(preview.Notes).Append("Drag to turn it; double-click to face it again.")));
        Cursor = Cursors.SizeAll;
        Invalidate();
    }

    /// <summary>Shows a message where the model would be: why there is none to draw.</summary>
    public void ShowMessage(string text)
    {
        model = null;
        message = text;
        caption = "";
        stale = true;
        tip.SetToolTip(this, text);
        Cursor = Cursors.Default;
        Invalidate();
    }

    internal bool HasModel => model != null;

    /// <summary>The model shown, if any (for the self-test).</summary>
    internal MapCore.Assets.ModelPreview? Model => model;

    /// <summary>The text shown: the caption under a model, or the message in its place.</summary>
    internal string Caption => model != null ? caption : message;

    internal ModelRender? LastRender { get; private set; }

    /// <summary>Draws the model now, as painting would (for the self-test, whose window isn't shown).</summary>
    internal ModelRender? RenderNow()
    {
        Render();
        return LastRender;
    }

    int CaptionHeight => Font.Height + 6;

    Size PictureSize => new(Math.Max(16, ClientSize.Width), Math.Max(16, ClientSize.Height - CaptionHeight));

    void Render()
    {
        LastRender = null;
        if (model == null)
            return;
        var size = PictureSize;
        var render = ModelRenderer.Render(model, size.Width, size.Height, yaw, pitch, supersample: dragFrom == null ? 2 : 1);
        if (picture == null || picture.Size != size)
        {
            picture?.Dispose();
            picture = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
            bgra = new byte[size.Width * size.Height * 4];
        }
        ViewPainting.CopyToBitmap(render.Image, bgra!, picture);
        LastRender = render;
        stale = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (model == null)
        {
            g.Clear(BarTheme.Field);
            var area = Rectangle.Inflate(ClientRectangle, -10, -8);
            TextRenderer.DrawText(g, message, Font, area, BarTheme.Dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        if (stale || picture == null || picture.Size != PictureSize)
            Render();
        g.Clear(Backdrop);
        if (picture != null)
            g.DrawImageUnscaled(picture, 0, 0);
        var line = new Rectangle(0, ClientSize.Height - CaptionHeight, ClientSize.Width, CaptionHeight);
        using (var band = new SolidBrush(BarTheme.Panel))
            g.FillRectangle(band, line);
        TextRenderer.DrawText(g, caption, Font, line, BarTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && model != null)
        {
            dragFrom = e.Location;
            Capture = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragFrom is not Point from)
            return;
        yaw = ((yaw - (e.X - from.X) * 0.8) % 360 + 360) % 360;
        pitch = Math.Clamp(pitch + (e.Y - from.Y) * 0.5, -10, 85);
        dragFrom = e.Location;
        stale = true;
        Invalidate();
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
        stale = true; // drawn again smoothly, now it has stopped
        Invalidate();
    }

    protected override void OnDoubleClick(EventArgs e)
    {
        base.OnDoubleClick(e);
        yaw = DefaultYaw;
        pitch = DefaultPitch;
        stale = true;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            picture?.Dispose();
            tip.Dispose();
        }
        base.Dispose(disposing);
    }
}
