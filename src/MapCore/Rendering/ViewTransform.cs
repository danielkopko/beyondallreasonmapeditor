namespace MapCore.Rendering;

/// <summary>
/// Places the map on an output image: the image's top-left corner is at world (<see cref="Left"/>, <see cref="Top"/>)
/// and each pixel spans <see cref="UnitsPerPixel"/> game units. Pixel (i, j) covers world
/// [Left + i·u, Left + (i+1)·u) × [Top + j·u, Top + (j+1)·u). North (z = 0) is at the top; z grows downwards.
/// </summary>
public readonly record struct ViewTransform(double Left, double Top, double UnitsPerPixel)
{
    /// <summary>The world position of an image position (pixel centres are at i + 0.5).</summary>
    public (double X, double Z) ToWorld(double imageX, double imageY) => (Left + imageX * UnitsPerPixel, Top + imageY * UnitsPerPixel);

    /// <summary>The image position of a world position.</summary>
    public (double X, double Y) ToImage(double worldX, double worldZ) => ((worldX - Left) / UnitsPerPixel, (worldZ - Top) / UnitsPerPixel);

    /// <summary>The whole map, centred in a <paramref name="width"/> × <paramref name="height"/> image with a small margin.</summary>
    public static ViewTransform Fit(double mapWidth, double mapHeight, int width, int height, double margin = 0.03)
    {
        double units = Math.Max(mapWidth / Math.Max(1, width), mapHeight / Math.Max(1, height)) * (1 + 2 * margin);
        return new ViewTransform(mapWidth / 2 - width * units / 2, mapHeight / 2 - height * units / 2, units);
    }

    /// <summary>Zooms by <paramref name="factor"/> (above 1 zooms in) keeping the world point under the image position fixed.</summary>
    public ViewTransform ZoomAbout(double imageX, double imageY, double factor)
    {
        var (x, z) = ToWorld(imageX, imageY);
        double units = UnitsPerPixel / factor;
        return new ViewTransform(x - imageX * units, z - imageY * units, units);
    }

    /// <summary>Moves the view so the map follows a drag of (dx, dy) image pixels.</summary>
    public ViewTransform Pan(double dx, double dy) => this with { Left = Left - dx * UnitsPerPixel, Top = Top - dy * UnitsPerPixel };

    /// <summary>The same view for an image whose size changed, keeping the centre where it was.</summary>
    public ViewTransform Resize(int oldWidth, int oldHeight, int newWidth, int newHeight) => this with
    {
        Left = Left + (oldWidth - newWidth) * UnitsPerPixel / 2,
        Top = Top + (oldHeight - newHeight) * UnitsPerPixel / 2,
    };
}
