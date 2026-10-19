using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MapCore.Imaging;
using MapCore.Rendering;

namespace MapEditor;

/// <summary>Shared by the 2D and 3D views: copying MapCore images to GDI bitmaps, and drawing labels.</summary>
static class ViewPainting
{
    /// <summary>GDI's 32-bit pixels are B, G, R, A in memory; MapCore's are R, G, B, A.</summary>
    public static void CopyToBitmap(RgbaImage image, byte[] bgra, Bitmap bitmap)
    {
        var src = image.Pixels;
        Parallel.For(0, image.Height, y =>
        {
            int start = y * image.Width * 4, end = start + image.Width * 4;
            for (int i = start; i < end; i += 4)
            {
                bgra[i] = src[i + 2];
                bgra[i + 1] = src[i + 1];
                bgra[i + 2] = src[i];
                bgra[i + 3] = src[i + 3];
            }
        });
        var data = bitmap.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int row = image.Width * 4;
            if (data.Stride == row)
                Marshal.Copy(bgra, 0, data.Scan0, bgra.Length);
            else
                for (int y = 0; y < image.Height; y++)
                    Marshal.Copy(bgra, y * row, data.Scan0 + y * data.Stride, row);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>Labels centred on their X, top at their Y, outlined in black so they read on any ground.</summary>
    public static void DrawLabels(Graphics g, Font font, IReadOnlyList<MapLabel> labels, Size area, double scale = 1)
    {
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        foreach (var label in labels)
        {
            var size = TextRenderer.MeasureText(g, label.Text, font, Size.Empty, flags);
            var at = new Point((int)Math.Round(label.X * scale - size.Width / 2.0), (int)Math.Round(label.Y * scale));
            if (at.X > area.Width || at.Y > area.Height || at.X + size.Width < 0 || at.Y + size.Height < 0)
                continue;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (dx != 0 || dy != 0)
                        TextRenderer.DrawText(g, label.Text, font, new Point(at.X + dx, at.Y + dy), Color.Black, flags);
            TextRenderer.DrawText(g, label.Text, font, at, Color.FromArgb(label.Colour.R, label.Colour.G, label.Colour.B), flags);
        }
    }
}
