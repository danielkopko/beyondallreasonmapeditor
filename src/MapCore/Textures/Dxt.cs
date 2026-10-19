using System.Buffers.Binary;

namespace MapCore.Textures;

/// <summary>Encodes and decodes single 4x4 blocks of DXT1 (BC1) and DXT5 (BC3). Pixels are 64 bytes of RGBA.</summary>
/// <remarks>
/// Encoder: endpoints from the extremes along the block's principal colour axis, then least-squares
/// refinement (the approach of stb_dxt), with an exact path for single-colour blocks. Error is plain RGB
/// distance with no perceptual weighting, so normal maps and data textures are treated fairly.
/// Decoder: 5/6-bit endpoints expand by bit replication; interpolated entries use integer arithmetic.
/// Encoder and decoder share the palette code, so the encoder optimises for exactly what is decoded.
/// </remarks>
static class Dxt
{
    public const int Dxt1BlockBytes = 8;
    public const int Dxt5BlockBytes = 16;

    static readonly byte[] Expand5 = new byte[32];
    static readonly byte[] Expand6 = new byte[64];

    // For each 8-bit value v: the endpoint pair (at [2v], [2v+1]) whose 2/3 interpolant best matches v.
    static readonly byte[] SingleColour5 = new byte[512];
    static readonly byte[] SingleColour6 = new byte[512];

    static Dxt()
    {
        for (int i = 0; i < 32; i++)
            Expand5[i] = (byte)(i << 3 | i >> 2);
        for (int i = 0; i < 64; i++)
            Expand6[i] = (byte)(i << 2 | i >> 4);
        BuildSingleColourTable(SingleColour5, Expand5);
        BuildSingleColourTable(SingleColour6, Expand6);
    }

    /// <remarks>Endpoints close together are slightly preferred, so decoders that round differently still agree.</remarks>
    static void BuildSingleColourTable(byte[] table, byte[] expand)
    {
        for (int v = 0; v < 256; v++)
        {
            int bestError = int.MaxValue;
            for (int e0 = 0; e0 < expand.Length; e0++)
                for (int e1 = 0; e1 < expand.Length; e1++)
                {
                    int error = Math.Abs((2 * expand[e0] + expand[e1]) / 3 - v) * 100 + Math.Abs(expand[e0] - expand[e1]) * 3;
                    if (error < bestError)
                    {
                        bestError = error;
                        table[2 * v] = (byte)e0;
                        table[2 * v + 1] = (byte)e1;
                    }
                }
        }
    }

    // ---------------------------------------------------------------- decoding

    public static void DecodeDxt1Block(ReadOnlySpan<byte> block, Span<byte> pixels) => DecodeColour(block, pixels, alwaysFourColour: false);

    public static void DecodeDxt5Block(ReadOnlySpan<byte> block, Span<byte> pixels)
    {
        DecodeColour(block[8..], pixels, alwaysFourColour: true); // DXT5 colour blocks never use the 3-colour mode
        DecodeAlpha(block, pixels);
    }

    static void DecodeColour(ReadOnlySpan<byte> block, Span<byte> pixels, bool alwaysFourColour)
    {
        ushort c0 = BinaryPrimitives.ReadUInt16LittleEndian(block);
        ushort c1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        uint indices = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);

        Span<int> palette = stackalloc int[16];
        bool fourColour = c0 > c1 || alwaysFourColour;
        BuildPalette(c0, c1, fourColour, palette);
        for (int i = 0; i < 16; i++)
        {
            int p = (int)(indices >> (2 * i) & 3) * 4;
            pixels[i * 4] = (byte)palette[p];
            pixels[i * 4 + 1] = (byte)palette[p + 1];
            pixels[i * 4 + 2] = (byte)palette[p + 2];
            pixels[i * 4 + 3] = (byte)palette[p + 3];
        }
    }

    static void DecodeAlpha(ReadOnlySpan<byte> block, Span<byte> pixels)
    {
        Span<int> palette = stackalloc int[8];
        BuildAlphaPalette(block[0], block[1], palette);
        ulong indices = 0;
        for (int i = 0; i < 6; i++)
            indices |= (ulong)block[2 + i] << (8 * i);
        for (int i = 0; i < 16; i++)
            pixels[i * 4 + 3] = (byte)palette[(int)(indices >> (3 * i) & 7)];
    }

    /// <summary>RGBA palette for a colour block: 4 opaque colours, or 3 colours plus transparent black.</summary>
    static void BuildPalette(ushort c0, ushort c1, bool fourColour, Span<int> palette)
    {
        int r0 = Expand5[c0 >> 11], g0 = Expand6[c0 >> 5 & 63], b0 = Expand5[c0 & 31];
        int r1 = Expand5[c1 >> 11], g1 = Expand6[c1 >> 5 & 63], b1 = Expand5[c1 & 31];
        Set(palette, 0, r0, g0, b0, 255);
        Set(palette, 1, r1, g1, b1, 255);
        if (fourColour)
        {
            Set(palette, 2, (2 * r0 + r1) / 3, (2 * g0 + g1) / 3, (2 * b0 + b1) / 3, 255);
            Set(palette, 3, (r0 + 2 * r1) / 3, (g0 + 2 * g1) / 3, (b0 + 2 * b1) / 3, 255);
        }
        else
        {
            Set(palette, 2, (r0 + r1) / 2, (g0 + g1) / 2, (b0 + b1) / 2, 255);
            Set(palette, 3, 0, 0, 0, 0);
        }

        static void Set(Span<int> p, int entry, int r, int g, int b, int a) =>
            (p[entry * 4], p[entry * 4 + 1], p[entry * 4 + 2], p[entry * 4 + 3]) = (r, g, b, a);
    }

    /// <summary>8 interpolated values when a0 &gt; a1; otherwise 6 values plus 0 and 255.</summary>
    static void BuildAlphaPalette(int a0, int a1, Span<int> palette)
    {
        palette[0] = a0;
        palette[1] = a1;
        if (a0 > a1)
        {
            for (int i = 1; i <= 6; i++)
                palette[i + 1] = ((7 - i) * a0 + i * a1 + 3) / 7;
        }
        else
        {
            for (int i = 1; i <= 4; i++)
                palette[i + 1] = ((5 - i) * a0 + i * a1 + 2) / 5;
            palette[6] = 0;
            palette[7] = 255;
        }
    }

    // ---------------------------------------------------------------- encoding

    /// <summary>
    /// Pixels with alpha below 128 become transparent (DXT1's 3-colour mode). Opaque blocks try both the
    /// 4-colour mode and the 3-colour mode without its transparent entry, keeping whichever is closer;
    /// some tools write nearly every block in 3-colour mode, which 4 colours alone cannot reproduce.
    /// </summary>
    public static void EncodeDxt1Block(ReadOnlySpan<byte> pixels, Span<byte> block)
    {
        int opaque = 0;
        for (int i = 0; i < 16; i++)
            if (pixels[i * 4 + 3] >= 128)
                opaque |= 1 << i;
        if (opaque == 0xFFFF)
            EncodeOpaqueColour(pixels, block, tryThreeColour: true);
        else
            EncodeColourWithTransparency(pixels, opaque, block);
    }

    public static void EncodeDxt5Block(ReadOnlySpan<byte> pixels, Span<byte> block)
    {
        EncodeAlpha(pixels, block);
        EncodeOpaqueColour(pixels, block[8..], tryThreeColour: false); // DXT5 colour is always decoded as 4 colours
    }

    // Weight of endpoint 0 for each palette entry: 4-colour mode (1, 0, 2/3, 1/3) and 3-colour mode (1, 0, 1/2).
    static ReadOnlySpan<float> FourColourWeights => [1f, 0f, 2f / 3f, 1f / 3f];
    static ReadOnlySpan<float> ThreeColourWeights => [1f, 0f, 0.5f, 0f];

    static void EncodeOpaqueColour(ReadOnlySpan<byte> px, Span<byte> block, bool tryThreeColour)
    {
        if (IsSingleColour(px))
        {
            int r = px[0], g = px[1], b = px[2];
            ushort s0 = (ushort)(SingleColour5[2 * r] << 11 | SingleColour6[2 * g] << 5 | SingleColour5[2 * b]);
            ushort s1 = (ushort)(SingleColour5[2 * r + 1] << 11 | SingleColour6[2 * g + 1] << 5 | SingleColour5[2 * b + 1]);
            WriteFourColourBlock(block, s0, s1, 0xAAAAAAAA); // every pixel uses entry 2 = (2*c0 + c1) / 3
            return;
        }

        PrincipalAxisEndpoints(px, 0xFFFF, out ushort start0, out ushort start1);
        int error = FitEndpoints(px, start0, start1, fourColour: true, out ushort c0, out ushort c1, out uint indices);
        if (tryThreeColour && error > 0)
        {
            int threeError = FitEndpoints(px, start0, start1, fourColour: false, out ushort t0, out ushort t1, out uint threeIndices);
            if (threeError < error)
            {
                WriteThreeColourBlock(block, t0, t1, threeIndices);
                return;
            }
        }
        WriteFourColourBlock(block, c0, c1, indices);
    }

    /// <summary>Matches pixels to the palette, then refines the endpoints by least squares (two passes).</summary>
    static int FitEndpoints(ReadOnlySpan<byte> px, ushort start0, ushort start1, bool fourColour,
        out ushort c0, out ushort c1, out uint indices)
    {
        (c0, c1) = (start0, start1);
        var weights = fourColour ? FourColourWeights : ThreeColourWeights;
        int error = MatchPalette(px, c0, c1, fourColour, out indices);
        for (int pass = 0; pass < 2 && error > 0; pass++)
        {
            if (!RefineEndpoints(px, indices, weights, out ushort n0, out ushort n1) || (n0 == c0 && n1 == c1))
                break;
            int refined = MatchPalette(px, n0, n1, fourColour, out uint refinedIndices);
            if (refined >= error)
                break;
            (c0, c1, indices, error) = (n0, n1, refinedIndices, refined);
        }
        return error;
    }

    /// <summary>Writes endpoints in the order that selects 3-colour mode (c0 &lt;= c1), remapping indices if swapped.</summary>
    static void WriteThreeColourBlock(Span<byte> block, ushort c0, ushort c1, uint indices)
    {
        if (c0 > c1)
        {
            (c0, c1) = (c1, c0);
            indices ^= ~(indices >> 1) & 0x55555555; // swaps entries 0<->1; entry 2 (the midpoint) stays
        }
        BinaryPrimitives.WriteUInt16LittleEndian(block, c0);
        BinaryPrimitives.WriteUInt16LittleEndian(block[2..], c1);
        BinaryPrimitives.WriteUInt32LittleEndian(block[4..], indices);
    }

    /// <summary>Writes endpoints in the order that selects 4-colour mode (c0 &gt; c1), remapping indices if swapped.</summary>
    static void WriteFourColourBlock(Span<byte> block, ushort c0, ushort c1, uint indices)
    {
        if (c0 < c1)
        {
            (c0, c1) = (c1, c0);
            indices ^= 0x55555555; // swaps entries 0<->1 and 2<->3
        }
        else if (c0 == c1)
        {
            indices = 0; // equal endpoints would select 3-colour mode, where entry 3 is transparent
        }
        BinaryPrimitives.WriteUInt16LittleEndian(block, c0);
        BinaryPrimitives.WriteUInt16LittleEndian(block[2..], c1);
        BinaryPrimitives.WriteUInt32LittleEndian(block[4..], indices);
    }

    static void EncodeColourWithTransparency(ReadOnlySpan<byte> px, int opaque, Span<byte> block)
    {
        ushort c0 = 0, c1 = 0;
        uint indices = 0;
        if (opaque != 0)
        {
            PrincipalAxisEndpoints(px, opaque, out c0, out c1);
            if (c0 > c1)
                (c0, c1) = (c1, c0); // c0 <= c1 selects 3-colour mode
        }

        Span<int> palette = stackalloc int[16];
        BuildPalette(c0, c1, fourColour: false, palette);
        for (int i = 0; i < 16; i++)
        {
            int best = 3;
            if ((opaque >> i & 1) != 0)
            {
                int bestDistance = int.MaxValue;
                for (int p = 0; p < 3; p++)
                {
                    int d = ColourDistance(px, i, palette, p);
                    if (d < bestDistance)
                        (bestDistance, best) = (d, p);
                }
            }
            indices |= (uint)best << (2 * i);
        }
        BinaryPrimitives.WriteUInt16LittleEndian(block, c0);
        BinaryPrimitives.WriteUInt16LittleEndian(block[2..], c1);
        BinaryPrimitives.WriteUInt32LittleEndian(block[4..], indices);
    }

    static bool IsSingleColour(ReadOnlySpan<byte> px)
    {
        for (int i = 4; i < 64; i += 4)
            if (px[i] != px[0] || px[i + 1] != px[1] || px[i + 2] != px[2])
                return false;
        return true;
    }

    /// <summary>Endpoints at the two pixels furthest apart along the principal axis of the (masked) pixels' colours.</summary>
    static void PrincipalAxisEndpoints(ReadOnlySpan<byte> px, int mask, out ushort c0, out ushort c1)
    {
        int n = 0, sumR = 0, sumG = 0, sumB = 0;
        int minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
        for (int i = 0; i < 16; i++)
        {
            if ((mask >> i & 1) == 0)
                continue;
            int r = px[i * 4], g = px[i * 4 + 1], b = px[i * 4 + 2];
            n++;
            sumR += r; sumG += g; sumB += b;
            minR = Math.Min(minR, r); minG = Math.Min(minG, g); minB = Math.Min(minB, b);
            maxR = Math.Max(maxR, r); maxG = Math.Max(maxG, g); maxB = Math.Max(maxB, b);
        }
        float meanR = sumR / (float)n, meanG = sumG / (float)n, meanB = sumB / (float)n;

        float rr = 0, rg = 0, rb = 0, gg = 0, gb = 0, bb = 0;
        for (int i = 0; i < 16; i++)
        {
            if ((mask >> i & 1) == 0)
                continue;
            float r = px[i * 4] - meanR, g = px[i * 4 + 1] - meanG, b = px[i * 4 + 2] - meanB;
            rr += r * r; rg += r * g; rb += r * b;
            gg += g * g; gb += g * b; bb += b * b;
        }

        // Power iteration from the bounding-box diagonal converges on the covariance's main axis.
        float vr = maxR - minR, vg = maxG - minG, vb = maxB - minB;
        for (int iteration = 0; iteration < 4; iteration++)
        {
            float r = vr * rr + vg * rg + vb * rb;
            float g = vr * rg + vg * gg + vb * gb;
            float b = vr * rb + vg * gb + vb * bb;
            (vr, vg, vb) = (r, g, b);
        }
        if (MathF.Max(MathF.Abs(vr), MathF.Max(MathF.Abs(vg), MathF.Abs(vb))) < 4f)
            (vr, vg, vb) = (299f, 587f, 114f); // no clear axis: fall back to luminance

        float minDot = float.MaxValue, maxDot = float.MinValue;
        int minIndex = 0, maxIndex = 0;
        for (int i = 0; i < 16; i++)
        {
            if ((mask >> i & 1) == 0)
                continue;
            float dot = px[i * 4] * vr + px[i * 4 + 1] * vg + px[i * 4 + 2] * vb;
            if (dot < minDot)
                (minDot, minIndex) = (dot, i);
            if (dot > maxDot)
                (maxDot, maxIndex) = (dot, i);
        }
        c0 = Pack565(px[maxIndex * 4], px[maxIndex * 4 + 1], px[maxIndex * 4 + 2]);
        c1 = Pack565(px[minIndex * 4], px[minIndex * 4 + 1], px[minIndex * 4 + 2]);
    }

    /// <summary>
    /// Nearest palette entry per pixel (4 colours, or 3 without the transparent entry); returns the summed
    /// squared RGB error. The palette is order-independent, so endpoints may be in either order here.
    /// </summary>
    static int MatchPalette(ReadOnlySpan<byte> px, ushort c0, ushort c1, bool fourColour, out uint indices)
    {
        Span<int> palette = stackalloc int[16];
        BuildPalette(c0, c1, fourColour, palette);
        int entries = fourColour ? 4 : 3;
        int total = 0;
        uint result = 0;
        for (int i = 0; i < 16; i++)
        {
            int best = 0, bestDistance = int.MaxValue;
            for (int p = 0; p < entries; p++)
            {
                int d = ColourDistance(px, i, palette, p);
                if (d < bestDistance)
                    (bestDistance, best) = (d, p);
            }
            total += bestDistance;
            result |= (uint)best << (2 * i);
        }
        indices = result;
        return total;
    }

    static int ColourDistance(ReadOnlySpan<byte> px, int pixel, ReadOnlySpan<int> palette, int entry)
    {
        int dr = px[pixel * 4] - palette[entry * 4];
        int dg = px[pixel * 4 + 1] - palette[entry * 4 + 1];
        int db = px[pixel * 4 + 2] - palette[entry * 4 + 2];
        return dr * dr + dg * dg + db * db;
    }

    /// <summary>Least-squares endpoints for the given index assignment. False if all pixels share one index.</summary>
    /// <param name="weight0">Weight of endpoint 0 for each palette entry.</param>
    static bool RefineEndpoints(ReadOnlySpan<byte> px, uint indices, ReadOnlySpan<float> weight0, out ushort c0, out ushort c1)
    {
        float aa = 0, bb = 0, ab = 0, ar = 0, ag = 0, ab2 = 0, br = 0, bg = 0, bb2 = 0;
        for (int i = 0; i < 16; i++)
        {
            float a = weight0[(int)(indices >> (2 * i) & 3)], b = 1 - a;
            int r = px[i * 4], g = px[i * 4 + 1], bl = px[i * 4 + 2];
            aa += a * a; bb += b * b; ab += a * b;
            ar += a * r; ag += a * g; ab2 += a * bl;
            br += b * r; bg += b * g; bb2 += b * bl;
        }
        float det = aa * bb - ab * ab;
        if (MathF.Abs(det) < 1e-6f)
        {
            c0 = c1 = 0;
            return false;
        }
        float inv = 1f / det;
        c0 = Pack565((ar * bb - br * ab) * inv, (ag * bb - bg * ab) * inv, (ab2 * bb - bb2 * ab) * inv);
        c1 = Pack565((br * aa - ar * ab) * inv, (bg * aa - ag * ab) * inv, (bb2 * aa - ab2 * ab) * inv);
        return true;
    }

    static ushort Pack565(int r, int g, int b) =>
        (ushort)((r * 31 + 127) / 255 << 11 | (g * 63 + 127) / 255 << 5 | (b * 31 + 127) / 255);

    static ushort Pack565(float r, float g, float b) => Pack565(ToByte(r), ToByte(g), ToByte(b));

    static int ToByte(float v) => (int)Math.Clamp(MathF.Round(v), 0f, 255f);

    /// <summary>Tries both alpha modes (8 interpolated values, or 6 plus exact 0 and 255) and keeps the better.</summary>
    static void EncodeAlpha(ReadOnlySpan<byte> px, Span<byte> block)
    {
        int min = 255, max = 0, innerMin = 255, innerMax = 0;
        for (int i = 0; i < 16; i++)
        {
            int a = px[i * 4 + 3];
            min = Math.Min(min, a);
            max = Math.Max(max, a);
            if (a != 0 && a != 255)
            {
                innerMin = Math.Min(innerMin, a);
                innerMax = Math.Max(innerMax, a);
            }
        }
        if (min == max)
        {
            block[0] = block[1] = (byte)min;
            block[2..8].Clear();
            return;
        }
        if (innerMin > innerMax)
            innerMin = innerMax = 0; // only 0 and 255 present: mode 2 represents them exactly

        int eightError = MatchAlpha(px, max, min, out ulong eightIndices);
        int sixError = MatchAlpha(px, innerMin, innerMax, out ulong sixIndices);
        bool useEight = eightError <= sixError;
        block[0] = (byte)(useEight ? max : innerMin);
        block[1] = (byte)(useEight ? min : innerMax);
        ulong indices = useEight ? eightIndices : sixIndices;
        for (int i = 0; i < 6; i++)
            block[2 + i] = (byte)(indices >> (8 * i));
    }

    static int MatchAlpha(ReadOnlySpan<byte> px, int a0, int a1, out ulong indices)
    {
        Span<int> palette = stackalloc int[8];
        BuildAlphaPalette(a0, a1, palette);
        int total = 0;
        ulong result = 0;
        for (int i = 0; i < 16; i++)
        {
            int a = px[i * 4 + 3], best = 0, bestDistance = int.MaxValue;
            for (int p = 0; p < 8; p++)
            {
                int d = (a - palette[p]) * (a - palette[p]);
                if (d < bestDistance)
                    (bestDistance, best) = (d, p);
            }
            total += bestDistance;
            result |= (ulong)best << (3 * i);
        }
        indices = result;
        return total;
    }
}
