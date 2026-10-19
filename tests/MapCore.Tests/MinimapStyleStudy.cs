using MapCore.Editing;
using MapCore.Imaging;
using MapCore.Smf;

namespace MapCore.Tests;

/// <summary>
/// Research for Step 17: how the map's own minimap relates to its texture, so a redrawn patch matches the rest.
/// Fits per-channel linear models (least squares) of increasing richness and reports how close each gets.
/// </summary>
static class MinimapStyleStudy
{
    [Test]
    public static void HowTheMinimapRelatesToTheTexture()
    {
        var s = MapEditSession.Open(MapPackage.OpenFolder(TestData.AcidicQuarry.Root));
        var minimap = Minimap.Decode(s.Smf);
        var texture = s.TextureImage.Resample(Minimap.Size, Minimap.Size);
        int n = Minimap.Size * Minimap.Size;
        double unit = s.MapWidth / Minimap.Size;
        var (lx, ly, lz) = MapCore.Rendering.SceneLighting.From(s.Info).SunDirection;
        var height = new double[n];
        var light = new double[n];
        for (int y = 0; y < Minimap.Size; y++)
            for (int x = 0; x < Minimap.Size; x++)
            {
                double wx = (x + 0.5) * unit, wz = (y + 0.5) * unit;
                height[y * Minimap.Size + x] = s.Heights.HeightAt(wx, wz);
                var (nx, ny, nz) = s.Heights.NormalAt(wx, wz);
                light[y * Minimap.Size + x] = Math.Max(0, nx * lx + ny * ly + nz * lz);
            }

        double Psnr(Func<int, int, double> predict)
        {
            double se = 0;
            for (int i = 0; i < n; i++)
                for (int c = 0; c < 3; c++)
                {
                    double d = Math.Clamp(predict(i, c), 0, 255) - minimap.Pixels[i * 4 + c];
                    se += d * d;
                }
            return 10 * Math.Log10(255.0 * 255 / (se / (n * 3)));
        }

        // features(i, c) -> regressors; fits a separate model per channel and per group (land / water).
        Func<int, int, double> Fit(Func<int, int, double[]> features, bool split)
        {
            var models = new Dictionary<(int Channel, bool Water), double[]>();
            for (int c = 0; c < 3; c++)
                foreach (bool water in split ? new[] { false, true } : new[] { false })
                {
                    int k = features(0, c).Length;
                    var ata = new double[k, k];
                    var atb = new double[k];
                    for (int i = 0; i < n; i++)
                    {
                        if (split && (height[i] < 0) != water)
                            continue;
                        var f = features(i, c);
                        double target = minimap.Pixels[i * 4 + c];
                        for (int a = 0; a < k; a++)
                        {
                            atb[a] += f[a] * target;
                            for (int b = 0; b < k; b++)
                                ata[a, b] += f[a] * f[b];
                        }
                    }
                    models[(c, water)] = Solve(ata, atb);
                }
            return (i, c) =>
            {
                var w = models[(c, split && height[i] < 0)];
                var f = features(i, c);
                return w.Zip(f).Sum(p => p.First * p.Second);
            };
        }

        double T(int i, int c) => texture.Pixels[i * 4 + c];
        double depth(int i) => Math.Max(0, -height[i]);
        Report.Info($"minimap vs texture as is: {Psnr((i, c) => T(i, c)):0.0} dB");
        Report.Info($"linear per channel: {Psnr(Fit((i, c) => [T(i, c), 1], false)):0.0} dB");
        Report.Info($"linear, land and water apart: {Psnr(Fit((i, c) => [T(i, c), 1], true)):0.0} dB");
        Report.Info($"+ sun lighting: {Psnr(Fit((i, c) => [T(i, c), 1, light[i], T(i, c) * light[i]], true)):0.0} dB");
        Report.Info($"+ water depth: {Psnr(Fit((i, c) => [T(i, c), 1, light[i], T(i, c) * light[i], depth(i), T(i, c) * depth(i)], true)):0.0} dB");
        foreach (int r in new[] { 2, 4, 8 })
        {
            var blurred = texture.Resample(Minimap.Size / r, Minimap.Size / r).Resample(Minimap.Size, Minimap.Size);
            double B(int i, int c) => blurred.Pixels[i * 4 + c];
            Report.Info($"linear with a {r}x blurred texture too, land and water apart: {Psnr(Fit((i, c) => [T(i, c), B(i, c), 1], true)):0.0} dB");
        }
    }

    static double[] Solve(double[,] a, double[] b)
    {
        int k = b.Length;
        var m = new double[k, k + 1];
        for (int r = 0; r < k; r++)
        {
            for (int c = 0; c < k; c++)
                m[r, c] = a[r, c] + (r == c ? 1e-6 : 0);
            m[r, k] = b[r];
        }
        for (int col = 0; col < k; col++)
        {
            int pivot = Enumerable.Range(col, k - col).MaxBy(r => Math.Abs(m[r, col]));
            for (int c = 0; c <= k; c++)
                (m[col, c], m[pivot, c]) = (m[pivot, c], m[col, c]);
            for (int r = 0; r < k; r++)
            {
                if (r == col || m[col, col] == 0)
                    continue;
                double f = m[r, col] / m[col, col];
                for (int c = col; c <= k; c++)
                    m[r, c] -= f * m[col, c];
            }
        }
        return Enumerable.Range(0, k).Select(r => m[r, k] / m[r, r]).ToArray();
    }
}
