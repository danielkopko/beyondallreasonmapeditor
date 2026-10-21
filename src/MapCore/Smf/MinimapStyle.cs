using MapCore.Imaging;

namespace MapCore.Smf;

/// <summary>
/// How a map's own minimap relates to its texture, so redrawn parts match the rest. Map makers often make the minimap
/// from an in-game picture, with the water surface over the sea bed; a minimap redrawn from the bare texture then
/// stands out. Per colour channel, minimap ≈ a·texture + b, fitted by least squares separately for land and for ground
/// under water. (Measured on Acidic Quarry: 24.9 dB as is, 27.6 dB with this; lighting, depth or blur add nothing.)
/// </summary>
public sealed record MinimapStyle(double[] Land, double[] Water)
{
    /// <summary>Unchanged colours.</summary>
    public static MinimapStyle Identity { get; } = new([1, 0, 1, 0, 1, 0], [1, 0, 1, 0, 1, 0]);

    /// <summary>
    /// On land, the least share of the minimap's variation the texture must explain (r², in each colour channel) for
    /// the fit to be used: a minimap that isn't a picture of this texture (another layout's, a placeholder) would fit as
    /// a flat wash. (BAR's own maps fit with 0.78 or more: Acidic Quarry, All That Glitters, All That Simmers,
    /// Ascendancy, Crimson Bay, Folsom Dam, Industrial Revolution.)
    /// </summary>
    public const double LandFitFloor = 0.3;

    /// <summary>
    /// Fits the style from the minimap and the texture resampled to its size; <paramref name="underWater"/>(x, y) in
    /// minimap pixels. Where the land fails to fit in a channel (see <see cref="LandFitFloor"/>), the land's colours are
    /// left as they are, all three (one channel fitted and another not would cast a colour). Under water a flat fit
    /// stands: minimaps often show the water's surface, whatever the bed below.
    /// </summary>
    public static MinimapStyle Fit(RgbaImage minimap, RgbaImage texture, Func<int, int, bool> underWater)
    {
        var land = new double[6];
        var water = new double[6];
        bool landFits = true;
        for (int c = 0; c < 3; c++)
            foreach (bool wet in new[] { false, true })
            {
                double sxx = 0, sx = 0, sxy = 0, sy = 0, syy = 0, n = 0;
                for (int y = 0; y < minimap.Height; y++)
                    for (int x = 0; x < minimap.Width; x++)
                    {
                        if (underWater(x, y) != wet)
                            continue;
                        int i = (y * minimap.Width + x) * 4 + c;
                        double t = texture.Pixels[i], m = minimap.Pixels[i];
                        (sxx, sx, sxy, sy, syy, n) = (sxx + t * t, sx + t, sxy + t * m, sy + m, syy + m * m, n + 1);
                    }
                var target = wet ? water : land;
                double det = n * sxx - sx * sx, spread = n * syy - sy * sy, covariance = n * sxy - sx * sy;
                if (n < 64 || Math.Abs(det) < 1e-9)
                {
                    (target[c * 2], target[c * 2 + 1]) = (1, 0); // too little of this kind of ground: leave it as is
                    continue;
                }
                if (!wet && (spread < 1e-9 || covariance * covariance / (det * spread) < LandFitFloor))
                    landFits = false; // the minimap doesn't show this texture
                double a = covariance / det;
                (target[c * 2], target[c * 2 + 1]) = (a, (sy - a * sx) / n);
            }
        return new MinimapStyle(landFits ? land : [1, 0, 1, 0, 1, 0], water);
    }

    public Rgba Apply(Rgba texture, bool underWater)
    {
        var k = underWater ? Water : Land;
        return new Rgba(Channel(texture.R, k[0], k[1]), Channel(texture.G, k[2], k[3]), Channel(texture.B, k[4], k[5]), 255);

        static byte Channel(byte v, double a, double b) => (byte)Math.Clamp(Math.Round(a * v + b), 0, 255);
    }
}
