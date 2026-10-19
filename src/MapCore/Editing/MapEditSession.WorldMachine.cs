using System.Globalization;
using MapCore.Heights;
using MapCore.Imaging;
using MapCore.Smf;
using MapCore.WorldMachine;

namespace MapCore.Editing;

/// <summary>How a World Machine output is brought back (<see cref="MapEditSession.BringBackFromWorldMachine"/>).</summary>
public sealed record WorldMachineImport
{
    /// <summary>
    /// Keep where units and buildings go, metal spots, structures and starts: what would change any of them is blended
    /// back to the map as it was (heights within the map's own range). False: take it as it is.
    /// </summary>
    public bool Keep { get; init; } = true;

    /// <summary>The heights black and white stand for (as sent: bar_map.json's), or null with <see cref="Stretch"/>.</summary>
    public (double Low, double High)? Range { get; init; }

    /// <summary>Spread the heights World Machine sends over the map's own range, whatever its scale.</summary>
    public bool Stretch { get; init; }

    /// <summary>Upside down: null to tell by comparing with the map (the default), or true / false to say.</summary>
    public bool? Flip { get; init; }

    /// <summary>For grass: where the mask is at least this (0 to 1), there is grass.</summary>
    public double GrassThreshold { get; init; } = 0.5;

    /// <summary>As the operation writes it: options, then '|' and the file (paths have ':' in them, never '|').</summary>
    public string Text(string layer, string path) =>
        string.Join(";", new[]
        {
            layer, Keep ? "keep=1" : "keep=0",
            Stretch ? "range=stretch" : Range is var (lo, hi) ? FormattableString.Invariant($"range={lo},{hi}") : null,
            Flip is bool f ? (f ? "flip=1" : "flip=0") : null,
            layer == "grass" ? FormattableString.Invariant($"grass={GrassThreshold}") : null,
        }.OfType<string>()) + "|" + path;

    public static (string Layer, WorldMachineImport Options, string Path) Parse(string text)
    {
        int bar = text.IndexOf('|');
        if (bar <= 0)
            throw new InvalidOperationException("wm-import needs <layer>[;options]|<file>.");
        var parts = text[..bar].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var options = new WorldMachineImport();
        foreach (var part in parts.Skip(1))
        {
            var (key, value) = part.IndexOf('=') is int eq and > 0 ? (part[..eq], part[(eq + 1)..]) : (part, "");
            options = key switch
            {
                "keep" => options with { Keep = value != "0" },
                "range" when value == "stretch" => options with { Stretch = true },
                "range" when value.Split(',') is [var lo, var hi] => options with { Range = (Number(lo), Number(hi)) },
                "flip" => options with { Flip = value == "auto" ? null : value == "1" },
                "grass" => options with { GrassThreshold = Number(value) },
                _ => throw new InvalidOperationException($"wm-import: '{part}' isn't an option (keep, range, flip, grass)."),
            };
        }
        if (parts.Length == 0 || parts[0] is not ("heights" or "texture" or "splat" or "grass"))
            throw new InvalidOperationException("wm-import brings back heights, texture, splat or grass.");
        return (parts[0], options, text[(bar + 1)..]);

        static double Number(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v)
            ? v : throw new InvalidOperationException($"'{s}' is not a number.");
    }
}

/// <summary>
/// World Machine: the map sent to an exchange folder (heights, texture, masks and what they mean:
/// <see cref="WorldMachineFiles"/>), and World Machine's outputs brought back as undoable operations:
/// <c>wm-import:&lt;heights|texture|splat|grass&gt;[;keep=1|0][;range=lo,hi|stretch][;flip=0|1][;grass=0.5]|&lt;file&gt;</c>.
/// Sizes are resampled to the map's. Heights are compared with the map's to tell an upside-down image (and report a
/// scale that doesn't match what was sent). With keep, what would change where any unit or building goes is blended
/// back to the map as it was, as cliff detail's check sees it.
/// </summary>
public sealed partial class MapEditSession
{
    /// <summary>
    /// Writes what World Machine works from into <paramref name="folder"/> (made if need be): heights.png and .r16
    /// (the map's own height range, recorded in bar_map.json), texture.png, masks (water, slope, cliffs, what is kept)
    /// and README.txt with the set-up. Not an edit.
    /// </summary>
    public string SendToWorldMachine(string folder, IProgress<string>? progress = null)
    {
        Directory.CreateDirectory(folder);
        var h = Heights;
        int w = h.Width, hh = h.Height;
        var values = h.Heights;
        float min = values.Min(), max = Math.Max(values.Max(), values.Min() + 1);
        var grey = new ushort[values.Length];
        for (int i = 0; i < grey.Length; i++)
            grey[i] = (ushort)Math.Clamp(Math.Round((values[i] - min) / (max - min) * 65535), 0, 65535);
        PngWriter.WriteGray16(Path.Combine(folder, WorldMachineFiles.Heights), w, hh, grey);
        WorldMachineFiles.WriteRaw16(Path.Combine(folder, WorldMachineFiles.HeightsRaw), grey);

        var slope = new byte[w * hh];
        var cliffs = new byte[w * hh];
        var water = new byte[w * hh];
        for (int z = 0; z < hh; z++)
            for (int x = 0; x < w; x++)
            {
                int i = z * w + x;
                int x0 = Math.Max(0, x - 1), x1 = Math.Min(w - 1, x + 1), z0 = Math.Max(0, z - 1), z1 = Math.Min(hh - 1, z + 1);
                double gx = (values[z * w + x1] - values[z * w + x0]) / ((x1 - x0) * HeightMap.Spacing);
                double gz = (values[z1 * w + x] - values[z0 * w + x]) / ((z1 - z0) * HeightMap.Spacing);
                double degrees = Math.Atan(Math.Sqrt(gx * gx + gz * gz)) * 180 / Math.PI;
                slope[i] = (byte)Math.Round(degrees / 90 * 255);
                cliffs[i] = (byte)Math.Round(Smooth(40, 55, degrees) * 255);
                water[i] = values[i] < 0 ? (byte)255 : (byte)0;
            }
        var keep = VertexProtection(w, hh).Select(v => (byte)Math.Round(v * 255)).ToArray();
        PngWriter.WriteGray8(Path.Combine(folder, WorldMachineFiles.SlopeMask), w, hh, slope);
        PngWriter.WriteGray8(Path.Combine(folder, WorldMachineFiles.CliffMask), w, hh, cliffs);
        PngWriter.WriteGray8(Path.Combine(folder, WorldMachineFiles.WaterMask), w, hh, water);
        PngWriter.WriteGray8(Path.Combine(folder, WorldMachineFiles.KeepMask), w, hh, keep);
        progress?.Report("Writing the texture…");
        PngWriter.WriteRgba(Path.Combine(folder, WorldMachineFiles.Texture), TextureImage);
        int splatWidth = 0, splatHeight = 0;
        if (TrySplat(out _))
            (splatWidth, splatHeight) = SplatSize;
        var info = new WorldMachineInfo
        {
            Map = $"{Info.Name} {Info.Version}".Trim(), HeightsWidth = w, HeightsHeight = hh, WorldWidth = MapWidth, WorldHeight = MapHeight,
            MinHeight = min, MaxHeight = max, TextureWidth = TextureImage.Width, TextureHeight = TextureImage.Height, SplatWidth = splatWidth, SplatHeight = splatHeight,
        };
        info.Save(folder);
        File.WriteAllText(Path.Combine(folder, WorldMachineFiles.ReadMe), WorldMachineFiles.ReadMeText(info).Replace("\r\n", "\n").Replace("\n", "\r\n"));
        return string.Create(CultureInfo.InvariantCulture,
            $"sent to World Machine in {folder}: heights {w}x{hh} spanning {min:0.##} to {max:0.##} (0 to {max - min:0.##} m there), the texture, and masks; README.txt says how to set World Machine up");
    }

    /// <summary>Brings a World Machine output back (one undo step): see the class summary.</summary>
    public string BringBackFromWorldMachine(string layer, string path, WorldMachineImport options) => Apply("wm-import:" + options.Text(layer, path));

    string WorldMachineImportCore(string rest)
    {
        var (layer, options, path) = WorldMachineImport.Parse(rest);
        if (!File.Exists(path))
            throw new InvalidOperationException($"There is no {path}.");
        string file = Path.GetFileName(path);
        try
        {
            return layer switch
            {
                "heights" => ImportWmHeights(path, file, options),
                "texture" => ImportWmTexture(path, file, options),
                "splat" => ImportWmSplat(path, file, options),
                _ => ImportWmGrass(path, file, options),
            };
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException($"{file}: {ex.Message}", ex);
        }
    }

    string ImportWmHeights(string path, string file, WorldMachineImport options)
    {
        var field = WorldMachineFiles.ReadField(path);
        var h = Heights;
        int w = h.Width, hh = h.Height;
        var before = (float[])h.Heights.Clone();
        float low = before.Min(), high = before.Max();
        var values = WorldMachineFiles.ResampleCorners(field.Values, field.Width, field.Height, w, hh);
        var mapped = new float[values.Length];
        if (options.Stretch)
        {
            float vmin = values.Min(), vmax = Math.Max(values.Max(), values.Min() + 1e-6f);
            for (int i = 0; i < mapped.Length; i++)
                mapped[i] = low + (values[i] - vmin) / (vmax - vmin) * (high - low);
        }
        else
        {
            var (lo, hi) = options.Range ?? throw new InvalidOperationException("wm-import heights needs range=lo,hi (the heights sent: bar_map.json) or range=stretch.");
            for (int i = 0; i < mapped.Length; i++)
                mapped[i] = (float)(field.Absolute ? lo + values[i] : lo + values[i] * (hi - lo));
        }
        var notes = new List<string>();
        // Edits made since the map was sent: World Machine's result is built from what was sent, so they would be replaced.
        var sentPath = Path.Combine(Path.GetDirectoryName(path)!, WorldMachineFiles.Heights);
        if (options.Range is var (fromLow, fromHigh) && File.Exists(sentPath) && WorldMachineFiles.ReadField(sentPath) is { } sent && sent.Width == w && sent.Height == hh)
        {
            double tolerance = 2 * (fromHigh - fromLow) / 65535;
            int differ = 0;
            for (int i = 0; i < before.Length; i++)
                if (Math.Abs(fromLow + sent.Values[i] * (fromHigh - fromLow) - before[i]) > tolerance)
                    differ++;
            if (differ > 50)
                notes.Add($"note: the map has changed since it was sent ({differ:N0} heights): World Machine's result is built from what was sent, so those " +
                          "edits are replaced where it is taken (Ctrl+Z takes it back); send again to work from the map as it is now");
        }
        // Which way up: the one that matches the map better, unless said. (A new terrain matches neither: as it is.)
        double straight = Correlation(before, mapped, w, hh, flip: false), flipped = Correlation(before, mapped, w, hh, flip: true);
        bool flip = options.Flip ?? (flipped > straight + 0.2 && flipped > 0.5);
        if (flip)
        {
            FlipRows(mapped, w, hh);
            notes.Add(options.Flip == null ? "upside down, so turned the right way up (World Machine wrote it with north at the bottom)" : "turned upside down as asked");
        }
        double match = Math.Max(flip ? flipped : straight, -1);
        var (scale, offset) = Fit(before, mapped);
        if (match > 0.9 && !options.Stretch && (Math.Abs(scale - 1) > 0.03 || Math.Abs(offset) > Math.Max(2, (high - low) * 0.02)))
        {
            double range = options.Range is var (lo, hi) ? hi - lo : high - low;
            notes.Add(string.Create(CultureInfo.InvariantCulture,
                $"its heights look {1 / scale:0.##} times those sent (offset {-offset / scale:0.#}): if World Machine's height scale isn't 0 to {range:0.#} m, set it so, or bring it back stretched"));
        }

        string kept = "";
        float[] result = mapped;
        if (options.Keep)
        {
            int clipped = 0;
            var wanted = new float[mapped.Length];
            for (int i = 0; i < wanted.Length; i++)
            {
                wanted[i] = Math.Clamp(mapped[i], low, high);
                if (wanted[i] != mapped[i])
                    clipped++;
            }
            result = KeepAccess(before, wanted, VertexProtection(w, hh), w, hh, out int held, out int rounds, out var found, out double retained);
            kept = $"; where units and buildings go kept ({retained:P0} of the change taken; {held:N0} heights held back some or all, in {rounds} passes, for " +
                   $"{found.Slopes:N0} slopes that would have crossed a unit's limit, {found.Water:N0} squares the water's edge or a depth limit, " +
                   $"{found.Buildings:N0} building footprints that would have lost their flat ground" +
                   (clipped > 0 ? $"; {clipped:N0} clipped to the map's range {low:0.#} to {high:0.#}" : "") + "), metal spots, structures and starts as they were";
        }
        int changed = h.SetAll(result);
        double most = 0;
        for (int i = 0; i < result.Length; i++)
            most = Math.Max(most, Math.Abs(result[i] - before[i]));
        string size = field.Width == w && field.Height == hh ? $"{w}x{hh}" : $"{field.Width}x{field.Height} resampled to {w}x{hh}";
        double step = (options.Range is var (sentLow, sentHigh) ? sentHigh - sentLow : high - low) / 65535;
        string amount = most <= step * 0.51 ? "no height moved by more than a 16-bit step: it came back as it went"
            : string.Create(CultureInfo.InvariantCulture, $"{changed:N0} heights changed, by up to {most:0.##}");
        if (most <= step * 0.51)
            kept = ""; // the set-up check: nothing to keep
        return $"World Machine heights from {file} ({size}): {amount}" +
               (notes.Count > 0 ? "; " + string.Join("; ", notes) : "") + kept;
    }

    /// <summary>How alike two height grids are (Pearson's r over every 4th vertex), the second read upside down if asked.</summary>
    static double Correlation(float[] a, float[] b, int w, int h, bool flip)
    {
        double sa = 0, sb = 0, saa = 0, sbb = 0, sab = 0;
        int n = 0;
        for (int z = 0; z < h; z += 4)
            for (int x = 0; x < w; x += 4)
            {
                double va = a[z * w + x], vb = b[(flip ? h - 1 - z : z) * w + x];
                (sa, sb, saa, sbb, sab, n) = (sa + va, sb + vb, saa + va * va, sbb + vb * vb, sab + va * vb, n + 1);
            }
        double cov = sab / n - sa / n * (sb / n), va2 = saa / n - sa / n * (sa / n), vb2 = sbb / n - sb / n * (sb / n);
        return va2 <= 1e-9 || vb2 <= 1e-9 ? 0 : cov / Math.Sqrt(va2 * vb2);
    }

    /// <summary>The line best fitting a ≈ scale × b + offset (least squares).</summary>
    static (double Scale, double Offset) Fit(float[] a, float[] b)
    {
        double sa = 0, sb = 0, sbb = 0, sab = 0;
        for (int i = 0; i < a.Length; i += 3)
            (sa, sb, sbb, sab) = (sa + a[i], sb + b[i], sbb + (double)b[i] * b[i], sab + (double)a[i] * b[i]);
        int n = (a.Length + 2) / 3;
        double denominator = n * sbb - sb * sb;
        if (Math.Abs(denominator) < 1e-9)
            return (1, 0);
        double scale = (n * sab - sa * sb) / denominator;
        return (scale, (sa - scale * sb) / n);
    }

    static void FlipRows<T>(T[] values, int w, int h)
    {
        var row = new T[w];
        for (int z = 0; z < h / 2; z++)
        {
            Array.Copy(values, z * w, row, 0, w);
            Array.Copy(values, (h - 1 - z) * w, values, z * w, w);
            Array.Copy(row, 0, values, (h - 1 - z) * w, w);
        }
    }

    /// <summary>
    /// Heights as near <paramref name="wanted"/> as keep where every unit and building goes (<see cref="AccessGuard"/>)
    /// and leave what is protected as it was. Where the change would alter anything, it is halved there, again and again
    /// (taking back only as much as needed, so the edge of what is taken back makes no new step), and after eight
    /// halvings taken out. That ends: once taken out, a height stays so, and what alters something always has a height
    /// not yet taken out (one with all of them taken out is as it was), so every pass takes out one more at least.
    /// </summary>
    /// <param name="retained">How much of the change was kept, all told (0 to 1).</param>
    float[] KeepAccess(float[] before, float[] wanted, float[] protect, int w, int h, out int held, out int rounds,
        out (int Slopes, int Water, int Buildings) first, out double retained)
    {
        var guard = new AccessGuard(before, w, h);
        first = default;
        var keep = new float[w * h];
        for (int i = 0; i < keep.Length; i++)
            keep[i] = 1 - protect[i];
        var candidate = new float[w * h];
        for (rounds = 1; ; rounds++)
        {
            for (int i = 0; i < candidate.Length; i++)
                candidate[i] = before[i] + keep[i] * (wanted[i] - before[i]);
            var wrong = guard.Violations(candidate);
            if (rounds == 1)
                first = guard.Found;
            if (wrong.Count == 0)
                break;
            foreach (int v in wrong)
                keep[v] = rounds < 8 ? keep[v] * 0.5f : 0;
        }
        held = keep.Count(k => k < 1);
        double asked = 0, kept = 0;
        for (int i = 0; i < before.Length; i++)
        {
            asked += Math.Abs(wanted[i] - before[i]);
            kept += Math.Abs(candidate[i] - before[i]);
        }
        retained = asked > 0 ? kept / asked : 1;
        return candidate;
    }

    string ImportWmTexture(string path, string file, WorldMachineImport options)
    {
        var current = TextureImage;
        var source = WorldMachineFiles.ReadImage(path);
        var image = WorldMachineFiles.ResampleCentres(source, current.Width, current.Height); // (the file's own image when the size is right)
        if (options.Keep)
        {
            var mask = ProtectionMask(current);
            Parallel.For(0, image.Height, y =>
            {
                for (int x = 0; x < image.Width; x++)
                {
                    double keep = mask.At(x, y);
                    if (keep <= 0)
                        continue;
                    var (a, b) = (image.GetPixel(x, y), current.GetPixel(x, y));
                    image.SetPixel(x, y, new Rgba(Byte(a.R + (b.R - a.R) * keep), Byte(a.G + (b.G - a.G) * keep), Byte(a.B + (b.B - a.B) * keep), 255));
                }
            });
        }
        for (int i = 3; i < image.Pixels.Length; i += 4)
            image.Pixels[i] = 255; // the map texture is opaque
        var (update, changed) = Texture.UpdateFrom(image);
        if (!changed.IsEmpty)
        {
            RebuildMinimap(changed);
            RefreshTexture(changed);
            textureChanged = true;
        }
        string size = source.Width == current.Width && source.Height == current.Height ? $"{current.Width}x{current.Height}" : $"{source.Width}x{source.Height} resampled to {current.Width}x{current.Height}";
        return $"World Machine texture from {file} ({size}): {update.TilesRewritten:N0} tiles re-encoded" + (options.Keep ? "; metal spots' looks and structures kept" : "");
    }

    string ImportWmSplat(string path, string file, WorldMachineImport options)
    {
        var dds = Resource(SplatKey, out var entry);
        var source = WorldMachineFiles.ReadImage(path);
        var image = WorldMachineFiles.ResampleCentres(source, dds.Width, dds.Height);
        if (options.Keep)
        {
            // The splat map as it is (north at the top, as the image), kept round metal spots (their molten look) and structures.
            var current = dds.Decode();
            current.FlipVertical();
            var mask = ProtectionMask(dds.Width, dds.Height);
            Parallel.For(0, image.Height, y =>
            {
                for (int x = 0; x < image.Width; x++)
                {
                    double keep = mask.At(x, y);
                    if (keep <= 0)
                        continue;
                    var (a, b) = (image.GetPixel(x, y), current.GetPixel(x, y));
                    image.SetPixel(x, y, new Rgba(Byte(a.R + (b.R - a.R) * keep), Byte(a.G + (b.G - a.G) * keep), Byte(a.B + (b.B - a.B) * keep), Byte(a.A + (b.A - a.A) * keep)));
                }
            });
        }
        image.FlipVertical(); // the .dds is stored from the south
        var changed = dds.EncodeChanged(image);
        if (!changed.IsEmpty)
        {
            changedResources.Add(entry);
            ForgetResourceEdit(entry);
        }
        string size = source.Width == dds.Width && source.Height == dds.Height ? $"{dds.Width}x{dds.Height}" : $"{source.Width}x{source.Height} resampled to {dds.Width}x{dds.Height}";
        return $"World Machine splat map from {file} ({size}) into {entry}: red, green, blue and alpha are detail textures 1 to 4" +
               (changed.IsEmpty ? "; nothing differed" : "") + (options.Keep ? "; metal spots' looks and structures kept" : "");
    }

    string ImportWmGrass(string path, string file, WorldMachineImport options)
    {
        if (Info.TextureOverrides.TryGetValue("grassmapTex", out var image))
            throw new InvalidOperationException($"mapinfo.lua sets smf.grassmapTex = \"{image}\"; the game takes grass from that image, so the SMF grass map is not used.");
        var field = WorldMachineFiles.ReadField(path);
        int w = Smf.GrassMapWidth, h = Smf.GrassMapHeight;
        var values = WorldMachineFiles.ResampleCentres(field.Values, field.Width, field.Height, w, h);
        double threshold = options.GrassThreshold;
        Smf.GrassMap ??= new byte[w * h];
        int changed = 0;
        for (int i = 0; i < values.Length; i++)
        {
            byte value = values[i] >= threshold ? (byte)1 : (byte)0;
            if (Smf.GrassMap[i] != value)
            {
                Smf.GrassMap[i] = value;
                changed++;
            }
        }
        return string.Create(CultureInfo.InvariantCulture, $"World Machine grass from {file}: {changed:N0} cells changed; {GrassLayer.CellsWithGrass(Smf):N0} cells have grass (where the mask is at least {threshold:0.##})");
    }

    /// <summary>The snapshots a World Machine import needs (what its layer can change).</summary>
    List<Snapshot> WorldMachineSnapshots(string rest)
    {
        try
        {
            var (layer, _, _) = WorldMachineImport.Parse(rest);
            return layer switch
            {
                "heights" => [new HeightSnapshot(Heights, Heights.AllVertices)],
                "texture" => [new TextureSnapshot(this, Texture.Bounds), new BytesSnapshot(Smf.Minimap, MapLayers.Minimap)],
                "splat" when TrySplat(out var entry) => [new ResourceSnapshot(this, entry)],
                "grass" => [new GrassSnapshot(Smf)],
                _ => [],
            };
        }
        catch (InvalidOperationException)
        {
            return []; // the operation itself will refuse, with the reason
        }
    }
}
