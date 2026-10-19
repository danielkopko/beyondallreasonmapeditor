namespace MapResearch;

/// <summary>Angle histogram in 0.25-degree bins (0-90 degrees), mergeable across threads.</summary>
sealed class AngleHistogram
{
    readonly long[] bins = new long[361];
    public long Count { get; private set; }
    double sum;

    public void Add(double degrees)
    {
        bins[Math.Clamp((int)(degrees * 4), 0, bins.Length - 1)]++;
        sum += degrees;
        Count++;
    }

    public void Merge(AngleHistogram other)
    {
        lock (this)
        {
            for (int i = 0; i < bins.Length; i++)
                bins[i] += other.bins[i];
            sum += other.sum;
            Count += other.Count;
        }
    }

    public double Mean => Count == 0 ? double.NaN : sum / Count;

    public double Percentile(double p)
    {
        long target = (long)(Count * p), seen = 0;
        for (int i = 0; i < bins.Length; i++)
            if ((seen += bins[i]) > target)
                return i / 4.0;
        return 90;
    }

    public override string ToString() =>
        Count == 0 ? "(no samples)" : $"mean {Mean,5:F1}°  median {Percentile(0.5),5:F1}°  p90 {Percentile(0.9),5:F1}°  (n={Count:N0})";
}

/// <summary>Running sums for Pearson correlations between named variables.</summary>
sealed class Correlations(params string[] names)
{
    readonly double[] sum = new double[names.Length];
    readonly double[,] products = new double[names.Length, names.Length];
    long count;

    public void Add(ReadOnlySpan<double> values)
    {
        for (int i = 0; i < values.Length; i++)
        {
            sum[i] += values[i];
            for (int j = i; j < values.Length; j++)
                products[i, j] += values[i] * values[j];
        }
        count++;
    }

    public void Merge(Correlations other)
    {
        lock (this)
        {
            for (int i = 0; i < sum.Length; i++)
            {
                sum[i] += other.sum[i];
                for (int j = i; j < sum.Length; j++)
                    products[i, j] += other.products[i, j];
            }
            count += other.count;
        }
    }

    public double Pearson(string a, string b)
    {
        int i = Array.IndexOf(names, a), j = Array.IndexOf(names, b);
        if (i > j)
            (i, j) = (j, i);
        double cov = products[i, j] / count - sum[i] / count * (sum[j] / count);
        double vi = products[i, i] / count - Math.Pow(sum[i] / count, 2), vj = products[j, j] / count - Math.Pow(sum[j] / count, 2);
        return cov / Math.Sqrt(vi * vj);
    }

    public double Mean(string a) => sum[Array.IndexOf(names, a)] / count;
}
