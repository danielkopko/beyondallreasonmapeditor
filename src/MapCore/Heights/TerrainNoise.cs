namespace MapCore.Heights;

/// <summary>
/// Smooth, repeatable 2D noise for the noise brush: three octaves of gradient (Perlin) noise, scaled to −1 … 1.
/// The same position always gives the same value, so repeated dabs deepen one pattern rather than adding jitter.
/// </summary>
public static class TerrainNoise
{
    static readonly byte[] Permutation = MakePermutation();

    /// <summary>Noise at (x, z), measured in feature sizes (1 = one bump across). Always within −1 … 1.</summary>
    public static double Sample(double x, double z)
    {
        double sum = 0, amplitude = 1, frequency = 1, total = 0;
        for (int octave = 0; octave < 3; octave++)
        {
            sum += Gradient(x * frequency + octave * 17.31, z * frequency - octave * 9.17) * amplitude;
            total += amplitude;
            amplitude *= 0.5;
            frequency *= 2;
        }
        // 2D gradient noise stays within about ±0.7; stretch that to ±1.
        return Math.Clamp(sum / total * 1.45, -1, 1);
    }

    static double Gradient(double x, double z)
    {
        int xi = (int)Math.Floor(x), zi = (int)Math.Floor(z);
        double xf = x - xi, zf = z - zi;
        double u = Fade(xf), v = Fade(zf);
        double a = Dot(Hash(xi, zi), xf, zf), b = Dot(Hash(xi + 1, zi), xf - 1, zf);
        double c = Dot(Hash(xi, zi + 1), xf, zf - 1), d = Dot(Hash(xi + 1, zi + 1), xf - 1, zf - 1);
        return Lerp(Lerp(a, b, u), Lerp(c, d, u), v);
    }

    static int Hash(int x, int z) => Permutation[(Permutation[x & 255] + z) & 255];

    static double Dot(int hash, double x, double z) => (hash & 7) switch
    {
        0 => x + z,
        1 => x - z,
        2 => -x + z,
        3 => -x - z,
        4 => x,
        5 => -x,
        6 => z,
        _ => -z,
    };

    static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);

    static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>A fixed shuffle (from a fixed seed), so noise is the same in every run and on every machine.</summary>
    static byte[] MakePermutation()
    {
        var p = new byte[256];
        for (int i = 0; i < 256; i++)
            p[i] = (byte)i;
        uint state = 0x9E3779B9;
        for (int i = 255; i > 0; i--)
        {
            state = state * 1664525 + 1013904223;
            int j = (int)(state >> 8) % (i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }
        return p;
    }
}
