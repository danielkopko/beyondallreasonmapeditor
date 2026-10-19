namespace MapCore.Imaging;

public static class ImageMetrics
{
    /// <summary>Peak signal-to-noise ratio in dB between two same-sized images; infinity when identical.</summary>
    /// <param name="includeAlpha">Whether the alpha channel counts towards the error.</param>
    public static double Psnr(RgbaImage a, RgbaImage b, bool includeAlpha = false)
    {
        if (a.Width != b.Width || a.Height != b.Height)
            throw new ArgumentException("Images must be the same size.");
        int channels = includeAlpha ? 4 : 3;
        long sum = 0;
        var pa = a.Pixels;
        var pb = b.Pixels;
        for (int i = 0; i < pa.Length; i += 4)
            for (int c = 0; c < channels; c++)
            {
                int d = pa[i + c] - pb[i + c];
                sum += d * d;
            }
        if (sum == 0)
            return double.PositiveInfinity;
        double mse = (double)sum / ((long)a.Width * a.Height * channels);
        return 10 * Math.Log10(255.0 * 255.0 / mse);
    }
}
