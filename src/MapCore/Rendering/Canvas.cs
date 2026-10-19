using MapCore.Imaging;

namespace MapCore.Rendering;

/// <summary>Anti-aliased shapes drawn over an <see cref="RgbaImage"/>, blended by the colour's alpha.</summary>
static class Canvas
{
    /// <summary>Mixes <paramref name="colour"/> into one pixel, weighted by its alpha × <paramref name="coverage"/> (0-1).</summary>
    public static void Blend(RgbaImage image, int x, int y, Rgba colour, double coverage)
    {
        if ((uint)x >= (uint)image.Width || (uint)y >= (uint)image.Height || coverage <= 0)
            return;
        int a = (int)Math.Round(colour.A * Math.Min(coverage, 1));
        if (a == 0)
            return;
        int o = (y * image.Width + x) * 4;
        var p = image.Pixels;
        p[o] = (byte)((p[o] * (255 - a) + colour.R * a + 127) / 255);
        p[o + 1] = (byte)((p[o + 1] * (255 - a) + colour.G * a + 127) / 255);
        p[o + 2] = (byte)((p[o + 2] * (255 - a) + colour.B * a + 127) / 255);
        p[o + 3] = 255;
    }

    public static void FillCircle(RgbaImage image, double cx, double cy, double radius, Rgba colour) =>
        Shape(image, cx - radius - 1, cy - radius - 1, cx + radius + 1, cy + radius + 1, colour,
            (x, y) => radius + 0.5 - Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)));

    public static void StrokeCircle(RgbaImage image, double cx, double cy, double radius, double width, Rgba colour)
    {
        double outer = radius + width / 2 + 1;
        double inner = Math.Max(0, radius - width / 2 - 1);
        // Visit only the ring: rows outside it are skipped by the bounding box, and inside each row the hollow middle.
        int top = (int)Math.Max(0, Math.Floor(cy - outer)), bottom = (int)Math.Min(image.Height - 1, Math.Ceiling(cy + outer));
        for (int y = top; y <= bottom; y++)
        {
            double dy = y + 0.5 - cy;
            double half = Math.Sqrt(Math.Max(0, outer * outer - dy * dy));
            double hole = dy * dy < inner * inner ? Math.Sqrt(inner * inner - dy * dy) : -1;
            Span(y, cx - half, hole < 0 ? cx + half : cx - hole);
            if (hole >= 0)
                Span(y, cx + hole, cx + half);
        }

        void Span(int y, double from, double to)
        {
            int x0 = (int)Math.Max(0, Math.Floor(from)), x1 = (int)Math.Min(image.Width - 1, Math.Ceiling(to));
            for (int x = x0; x <= x1; x++)
            {
                double d = Math.Sqrt((x + 0.5 - cx) * (x + 0.5 - cx) + (y + 0.5 - cy) * (y + 0.5 - cy));
                Blend(image, x, y, colour, width / 2 + 0.5 - Math.Abs(d - radius));
            }
        }
    }

    public static void Line(RgbaImage image, double x0, double y0, double x1, double y1, double width, Rgba colour)
    {
        double dx = x1 - x0, dy = y1 - y0, length2 = dx * dx + dy * dy;
        double pad = width / 2 + 1;
        Shape(image, Math.Min(x0, x1) - pad, Math.Min(y0, y1) - pad, Math.Max(x0, x1) + pad, Math.Max(y0, y1) + pad, colour, (x, y) =>
        {
            double t = length2 == 0 ? 0 : Math.Clamp(((x - x0) * dx + (y - y0) * dy) / length2, 0, 1);
            double px = x0 + t * dx - x, py = y0 + t * dy - y;
            return width / 2 + 0.5 - Math.Sqrt(px * px + py * py);
        });
    }

    /// <summary>A rectangle filled with a colour (blended by its alpha), corners in any order.</summary>
    public static void FillRect(RgbaImage image, double x0, double y0, double x1, double y1, Rgba colour)
    {
        int left = (int)Math.Max(0, Math.Round(Math.Min(x0, x1))), right = (int)Math.Min(image.Width, Math.Round(Math.Max(x0, x1)));
        int top = (int)Math.Max(0, Math.Round(Math.Min(y0, y1))), bottom = (int)Math.Min(image.Height, Math.Round(Math.Max(y0, y1)));
        for (int y = top; y < bottom; y++)
            for (int x = left; x < right; x++)
                Blend(image, x, y, colour, 1);
    }

    /// <summary>A horizontal or vertical line one pixel wide, across the whole span given.</summary>
    public static void HorizontalLine(RgbaImage image, int y, int x0, int x1, Rgba colour)
    {
        for (int x = Math.Max(0, x0); x <= Math.Min(image.Width - 1, x1); x++)
            Blend(image, x, y, colour, 1);
    }

    public static void VerticalLine(RgbaImage image, int x, int y0, int y1, Rgba colour)
    {
        for (int y = Math.Max(0, y0); y <= Math.Min(image.Height - 1, y1); y++)
            Blend(image, x, y, colour, 1);
    }

    /// <summary>Visits the pixels of a bounding box; <paramref name="coverage"/> gets pixel centres and returns 0-1 (clamped).</summary>
    static void Shape(RgbaImage image, double left, double top, double right, double bottom, Rgba colour, Func<double, double, double> coverage)
    {
        int x0 = (int)Math.Max(0, Math.Floor(left)), x1 = (int)Math.Min(image.Width - 1, Math.Ceiling(right));
        int y0 = (int)Math.Max(0, Math.Floor(top)), y1 = (int)Math.Min(image.Height - 1, Math.Ceiling(bottom));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                Blend(image, x, y, colour, Math.Clamp(coverage(x + 0.5, y + 0.5), 0, 1));
    }
}
