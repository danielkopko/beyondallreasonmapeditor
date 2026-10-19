namespace MapCore;

/// <summary>Where a field crosses zero on the map, traced on a grid by marching squares: lines for drawing an edge.</summary>
public static class Contour
{
    /// <summary>
    /// The lines where <paramref name="outside"/> (how far outside the edge a point is; negative inside it) crosses
    /// zero within the box, sampled every <paramref name="step"/> units: closed lines round each part, and open ones
    /// where a part runs off the box. Nothing is inside beyond a step outside the box.
    /// </summary>
    public static List<(double X, double Z)[]> Lines(Func<double, double, double> outside, (double MinX, double MinZ, double MaxX, double MaxZ) box, double step)
    {
        var (minX, minZ, maxX, maxZ) = box;
        (minX, minZ) = (minX - step, minZ - step);
        int columns = (int)Math.Ceiling((maxX + step - minX) / step) + 1, rows = (int)Math.Ceiling((maxZ + step - minZ) / step) + 1;
        // How far outside the edge each grid point is (negative inside it), capped so far points interpolate sensibly.
        var f = new double[columns * rows];
        Parallel.For(0, rows, j =>
        {
            for (int i = 0; i < columns; i++)
                f[j * columns + i] = Math.Min(outside(minX + i * step, minZ + j * step), 4 * step);
        });
        (double X, double Z) Point(int edge)
        {
            // Edge ids: 2 (j columns + i) runs from (i, j) to (i + 1, j); that + 1 runs from (i, j) to (i, j + 1).
            int k = edge / 2, i = k % columns, j = k / columns;
            int other = edge % 2 == 0 ? k + 1 : k + columns;
            double a = f[k], b = f[other], t = Math.Clamp(a / (a - b), 0, 1);
            return edge % 2 == 0 ? (minX + (i + t) * step, minZ + j * step) : (minX + i * step, minZ + (j + t) * step);
        }
        // Marching squares: each cell the line passes through joins two of its sides.
        var next = new Dictionary<int, List<int>>();
        void Join(int a, int b)
        {
            (next.TryGetValue(a, out var la) ? la : next[a] = []).Add(b);
            (next.TryGetValue(b, out var lb) ? lb : next[b] = []).Add(a);
        }
        for (int j = 0; j < rows - 1; j++)
            for (int i = 0; i < columns - 1; i++)
            {
                int k = j * columns + i;
                bool a = f[k] < 0, b = f[k + 1] < 0, c = f[k + columns + 1] < 0, d = f[k + columns] < 0; // corners round the cell
                int top = 2 * k, right = 2 * (k + 1) + 1, bottom = 2 * (k + columns), left = 2 * k + 1;
                int index = (a ? 1 : 0) | (b ? 2 : 0) | (c ? 4 : 0) | (d ? 8 : 0);
                switch (index)
                {
                    case 1 or 14: Join(left, top); break;
                    case 2 or 13: Join(top, right); break;
                    case 3 or 12: Join(left, right); break;
                    case 4 or 11: Join(right, bottom); break;
                    case 6 or 9: Join(top, bottom); break;
                    case 7 or 8: Join(left, bottom); break;
                    case 5 or 10:
                        // A saddle: the middle decides which corners join.
                        bool middle = (f[k] + f[k + 1] + f[k + columns + 1] + f[k + columns]) / 4 < 0;
                        if ((index == 5) == middle)
                        {
                            Join(left, bottom);
                            Join(top, right);
                        }
                        else
                        {
                            Join(left, top);
                            Join(right, bottom);
                        }
                        break;
                }
            }
        var lines = new List<(double X, double Z)[]>();
        var used = new HashSet<int>();
        foreach (int start in next.Keys)
        {
            if (used.Contains(start))
                continue;
            var line = new List<(double X, double Z)>();
            int previous = -1, at = start;
            while (at >= 0 && used.Add(at))
            {
                line.Add(Point(at));
                int following = -1;
                foreach (int n in next[at])
                    if (n != previous && !used.Contains(n))
                    {
                        following = n;
                        break;
                    }
                (previous, at) = (at, following);
            }
            if (line.Count > 2)
                lines.Add([.. line]);
        }
        return lines;
    }
}
