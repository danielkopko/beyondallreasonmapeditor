// Helpers over a square grid of values: masks from outlines, distance to the nearest marked cell (Felzenszwalb and
// Huttenlocher's exact Euclidean transform), and box blur.
static class Grid
{
    /// <summary>Cells (spacing apart, centred on cell * spacing) inside any of the outlines.</summary>
    public static bool[] Mask(int n, double spacing, IEnumerable<Shape> shapes)
    {
        var m = new bool[n * n];
        foreach (var s in shapes)
        {
            int x0 = Math.Max(0, (int)Math.Floor(s.X0 / spacing)), x1 = Math.Min(n - 1, (int)Math.Ceiling(s.X1 / spacing));
            int z0 = Math.Max(0, (int)Math.Floor(s.Z0 / spacing)), z1 = Math.Min(n - 1, (int)Math.Ceiling(s.Z1 / spacing));
            Parallel.For(z0, z1 + 1, z =>
            {
                for (int x = x0; x <= x1; x++)
                    if (!m[z * n + x] && Geo.Inside(new P(x * spacing, z * spacing), s.Pts)) m[z * n + x] = true;
            });
        }
        return m;
    }

    /// <summary>Distance in cells from each cell to the nearest marked one (0 on marked cells).</summary>
    public static float[] Distance(bool[] marked, int n)
    {
        const float Inf = 1e20f;
        var f = new float[n * n];
        for (int i = 0; i < f.Length; i++) f[i] = marked[i] ? 0 : Inf;
        Parallel.For(0, n, x =>
        {
            var col = new float[n]; var outp = new float[n]; var v = new int[n]; var zz = new float[n + 1];
            for (int z = 0; z < n; z++) col[z] = f[z * n + x];
            Pass(col, outp, v, zz, n);
            for (int z = 0; z < n; z++) f[z * n + x] = outp[z];
        });
        Parallel.For(0, n, z =>
        {
            var row = new float[n]; var outp = new float[n]; var v = new int[n]; var zz = new float[n + 1];
            Array.Copy(f, z * n, row, 0, n);
            Pass(row, outp, v, zz, n);
            for (int x = 0; x < n; x++) f[z * n + x] = MathF.Sqrt(outp[x]);
        });
        return f;
    }

    static void Pass(float[] f, float[] d, int[] v, float[] z, int n)
    {
        const float Inf = 1e20f;
        int k = 0;
        v[0] = 0; z[0] = -Inf; z[1] = Inf;
        for (int q = 1; q < n; q++)
        {
            float s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]);
            while (s <= z[k])
            {
                k--;
                s = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]);
            }
            k++;
            v[k] = q; z[k] = s; z[k + 1] = Inf;
        }
        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q) k++;
            d[q] = (q - v[k]) * (q - v[k]) + f[v[k]];
        }
    }

    /// <summary>Box blur, rows then columns, repeated (three passes are close to a Gaussian).</summary>
    public static void Blur(float[] a, int n, int radius, int passes)
    {
        var tmp = new float[n * n];
        for (int pass = 0; pass < passes; pass++)
        {
            Parallel.For(0, n, z =>
            {
                double sum = 0;
                for (int x = -radius; x <= radius; x++) sum += a[z * n + Math.Clamp(x, 0, n - 1)];
                for (int x = 0; x < n; x++)
                {
                    tmp[z * n + x] = (float)(sum / (2 * radius + 1));
                    sum += a[z * n + Math.Min(n - 1, x + radius + 1)] - a[z * n + Math.Max(0, x - radius)];
                }
            });
            Parallel.For(0, n, x =>
            {
                double sum = 0;
                for (int z = -radius; z <= radius; z++) sum += tmp[Math.Clamp(z, 0, n - 1) * n + x];
                for (int z = 0; z < n; z++)
                {
                    a[z * n + x] = (float)(sum / (2 * radius + 1));
                    sum += tmp[Math.Min(n - 1, z + radius + 1) * n + x] - tmp[Math.Max(0, z - radius) * n + x];
                }
            });
        }
    }

    public static double Sample(float[] a, int n, double spacing, double x, double z)
    {
        double fx = x / spacing, fz = z / spacing;
        int ix = Math.Clamp((int)Math.Floor(fx), 0, n - 2), iz = Math.Clamp((int)Math.Floor(fz), 0, n - 2);
        double tx = Math.Clamp(fx - ix, 0, 1), tz = Math.Clamp(fz - iz, 0, 1);
        double p = a[iz * n + ix] + (a[iz * n + ix + 1] - a[iz * n + ix]) * tx;
        double q = a[(iz + 1) * n + ix] + (a[(iz + 1) * n + ix + 1] - a[(iz + 1) * n + ix]) * tx;
        return p + (q - p) * tz;
    }
}
