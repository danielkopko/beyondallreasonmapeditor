using MapCore.Imaging;
using MapCore.Metal;
using MapCore.Smf;
using MapCore.Textures;

namespace MapCore.Editing;

/// <summary>Metal spots: moving a spot's look with it, and what may stop an extractor being built on a spot.</summary>
public sealed partial class MapEditSession
{
    /// <summary>Radius used when the map's splat map shows no metal look to measure.</summary>
    public const double DefaultLookRadius = 48;

    /// <summary>
    /// BAR's T1 extractor (armmex): maxslope 30, footprint 4x4 squares. The engine allows a building where every
    /// footprint square is within maxHeightDif = 40·tan(maxslope) of the build height (UnitDef.cpp, GameHelper.cpp).
    /// </summary>
    public static readonly double ExtractorMaxHeightDifference = 40 * Math.Tan(30 * Math.PI / 180);
    const int ExtractorFootprint = 4;

    /// <summary>How spots look on this map: the look's radius, how it was found, and the splat channel that marks it (-1: none).</summary>
    sealed record LookInfo(double Radius, string How, int Channel);

    LookInfo? look;

    LookInfo Look => look ??= MeasureLook();

    /// <summary>
    /// The size of a spot's look (game units) and how it was found. Measured around each spot's look centre (see
    /// <see cref="LookCentre"/>): the splat channel that brightens at metal spots (Acidic Quarry: molten metal), and the
    /// texture's patch there; the radius is where the larger of the two profiles fades to a tenth of its peak above the
    /// surroundings. Otherwise <see cref="DefaultLookRadius"/>.
    /// </summary>
    public (double Radius, string How) LookRadius => (Look.Radius, Look.How);

    /// <summary>A splat weight at a world point (nearest pixel), from the session's decoded copy (<c>SplatEdit(out _).Levels[0]</c>).</summary>
    double SplatAt(RgbaImage level0, int channel, double x, double z)
    {
        int px = Math.Clamp((int)(x * level0.Width / MapWidth), 0, level0.Width - 1);
        int py = Math.Clamp((int)(z * level0.Height / MapHeight), 0, level0.Height - 1);
        return level0.Pixels[((level0.Height - 1 - py) * level0.Width + px) * 4 + channel]; // stored upside down
    }

    double TextureLuma(double x, double z)
    {
        var image = TextureImage;
        var p = image.GetPixel(Math.Clamp((int)(x * image.Width / MapWidth), 0, image.Width - 1), Math.Clamp((int)(z * image.Height / MapHeight), 0, image.Height - 1));
        return 0.299 * p.R + 0.587 * p.G + 0.114 * p.B;
    }

    static double RingAverage(Func<double, double, double> value, double x, double z, double r) =>
        r == 0 ? value(x, z) : Enumerable.Range(0, 24).Average(k => value(x + r * Math.Cos(k * Math.PI / 12), z + r * Math.Sin(k * Math.PI / 12)));

    LookInfo MeasureLook()
    {
        var spots = Metal.FindSpots();
        if (spots.Count == 0 || !TrySplat(out _))
            return new LookInfo(DefaultLookRadius, "default (no spots or no splat map to measure)", -1);
        var level0 = SplatEdit(out _).Levels[0];
        double SplatAt(int channel, double x, double z) => this.SplatAt(level0, channel, x, z);
        const double Far = 200;
        int best = -1;
        double bestScore = 0;
        for (int c = 0; c < 4; c++)
        {
            double score = spots.Average(sp => SplatAt(c, sp.X, sp.Z) - RingAverage((x, z) => SplatAt(c, x, z), sp.X, sp.Z, Far));
            if (score > bestScore)
                (best, bestScore) = (c, score);
        }
        if (best < 0 || bestScore < 20)
            return new LookInfo(DefaultLookRadius, "default (no splat channel stands out at the spots)", -1);
        var partial = new LookInfo(DefaultLookRadius, "", best);
        look = partial; // LookCentre below needs the channel
        var centres = spots.Select(LookCentre).ToList();
        // The look's edge ends where its average radial profile flattens out after the steepest drop. (Comparing with
        // the far surroundings fails on small plateaus, whose cliffs and sea are only a little further out.)
        double Radius(Func<double, double, double> value)
        {
            const double Step = 4, Limit = 96;
            var profile = Enumerable.Range(0, (int)(Limit / Step) + 2)
                .Select(i => centres.Average(c => RingAverage(value, c.X, c.Z, i * Step))).ToArray();
            var slope = Enumerable.Range(0, profile.Length - 1).Select(i => Math.Abs(profile[i + 1] - profile[i])).ToArray();
            int steepest = Array.IndexOf(slope, slope.Max());
            for (int i = steepest + 1; i < slope.Length; i++)
                if (slope[i] < slope[steepest] * 0.15)
                    return (i + 1) * Step;
            return Limit;
        }
        double splat = Radius((x, z) => SplatAt(best, x, z)), texture = Radius(TextureLuma);
        double radius = Math.Clamp(Math.Max(splat, texture), 16, 128);
        return new LookInfo(radius, $"measured: splat channel {"RGBA"[best]} ({Info.Resource($"splatDetailNormalTex{best + 1}") ?? "detail texture"}) " +
                                    $"fades by {splat:0} units, the texture patch by {texture:0}", best);
    }

    /// <summary>
    /// Where a spot's look is centred: the middle of its blob in the splat channel that marks spots (it need not sit on
    /// the metal's own centre); the metal's centre when the map has no such channel.
    /// </summary>
    (double X, double Z) LookCentre(MetalSpot spot)
    {
        int channel = Look.Channel;
        if (channel < 0)
            return (spot.X, spot.Z);
        const double Reach = 48;
        var level0 = SplatEdit(out _).Levels[0];
        double SplatAt(int c, double x, double z) => this.SplatAt(level0, c, x, z);
        double ring = RingAverage((x, z) => SplatAt(channel, x, z), spot.X, spot.Z, Reach + 16);
        double total = 0, sx = 0, sz = 0;
        for (double dz = -Reach; dz <= Reach; dz += 2)
            for (double dx = -Reach; dx <= Reach; dx += 2)
                if (dx * dx + dz * dz <= Reach * Reach)
                {
                    double v = Math.Max(0, SplatAt(channel, spot.X + dx, spot.Z + dz) - ring);
                    (total, sx, sz) = (total + v, sx + v * (spot.X + dx), sz + v * (spot.Z + dz));
                }
        return total <= 0 ? (spot.X, spot.Z) : (sx / total, sz / total);
    }

    /// <summary>The look move for moving the spot near <paramref name="from"/> towards <paramref name="to"/> (as metal-move would place it).</summary>
    bool TryLookPlan(string from, string to, out SpotLook plan)
    {
        plan = null!;
        try
        {
            var spot = PickSpot(from);
            var (x, z) = Point(to);
            if (PlanLook(spot, x, z) is not SpotLook found)
                return false;
            plan = found;
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// The look move for moving <paramref name="spot"/> towards (x, z), computed from the map as it is before the
    /// move (undo capture and the move itself must agree). Null if the move would be refused.
    /// </summary>
    SpotLook? PlanLook(MetalSpot spot, double x, double z)
    {
        var (bx, bz, problem) = Metal.TryMove(spot, x, z);
        if (problem != null)
            return null;
        var (radius, _) = LookRadius;
        var (ax, az) = LookCentre(spot); // the look moves by as much as the metal does
        var plan = SpotLook.Plan(ax, az, ax + bx - spot.X, az + bz - spot.Z, radius, Math.Max(16, radius * 0.6));
        if (!plan.Swap)
            return plan;
        return ChooseFill(spot, plan) is (double fx, double fz) ? plan with { FillX = fx, FillZ = fz } : plan with { FillFromRim = true };
    }

    /// <summary>
    /// Ground to fill the old place with after a far move. Of 16 nearby discs (clear of other spots and the
    /// destination), the one that best continues the old place's surroundings: its rim matches the rim around the old
    /// place (texture brightness, and the look's splat channel), its inside matches its own rim (no look of its own),
    /// and its height is similar. Null when none matches well (on a small plateau every disc that far out is cliff):
    /// the old place is then filled from its own surroundings.
    /// </summary>
    (double X, double Z)? ChooseFill(MetalSpot spot, SpotLook plan)
    {
        double reach = plan.Radius + plan.Feather;
        int channel = Look.Channel;
        var level0 = channel >= 0 ? SplatEdit(out _).Levels[0] : null;
        double Splat(double x, double z) => level0 == null ? 0 : SplatAt(level0, channel, x, z);
        double[] Ring(Func<double, double, double> value, double x, double z, double r) =>
            Enumerable.Range(0, 24).Select(k => value(x + r * Math.Cos(k * Math.PI / 12), z + r * Math.Sin(k * Math.PI / 12))).ToArray();
        static double Difference(double[] a, double[] b) => a.Zip(b).Average(p => Math.Abs(p.First - p.Second));
        double Inside(Func<double, double, double> value, double x, double z) =>
            new[] { 0.0, 0.3, 0.6 }.Average(f => Ring(value, x, z, reach * f).Average());

        var lumaRim = Ring(TextureLuma, plan.AX, plan.AZ, reach + 6);
        var splatRim = Ring(Splat, plan.AX, plan.AZ, reach + 6);
        double height = Heights.HeightAt(plan.AX, plan.AZ);
        var others = Metal.FindSpots().Where(o => o.X != spot.X || o.Z != spot.Z).ToList();
        (double X, double Z)? best = null;
        double bestCost = double.MaxValue;
        for (int k = 0; k < 16; k++)
        {
            double cx = plan.AX + 2.4 * reach * Math.Cos(k * Math.PI / 8), cz = plan.AZ + 2.4 * reach * Math.Sin(k * Math.PI / 8);
            if (cx - reach < 0 || cz - reach < 0 || cx + reach >= MapWidth || cz + reach >= MapHeight)
                continue;
            if (others.Any(o => Math.Sqrt((o.X - cx) * (o.X - cx) + (o.Z - cz) * (o.Z - cz)) < 2 * reach) ||
                Math.Sqrt((plan.BX - cx) * (plan.BX - cx) + (plan.BZ - cz) * (plan.BZ - cz)) < 2 * reach)
                continue;
            var candidateSplatRim = Ring(Splat, cx, cz, reach + 6);
            double luma = Difference(lumaRim, Ring(TextureLuma, cx, cz, reach + 6)), splat = Difference(splatRim, candidateSplatRim);
            double own = Math.Abs(Inside(Splat, cx, cz) - candidateSplatRim.Average()), rise = Math.Abs(Heights.HeightAt(cx, cz) - height);
            if (luma > 20 || splat > 30 || own > 30 || rise > 24)
                continue; // doesn't match well enough to pass for the old place's ground
            double cost = luma + splat + own + rise * 0.5;
            if (cost < bestCost)
                (best, bestCost) = ((cx, cz), cost);
        }
        return best;
    }

    /// <summary>Undo for a move with its look: metal, texture (one state for both areas), minimap, splat and normal map.</summary>
    List<Snapshot> LookSnapshots(SpotLook plan)
    {
        double perUnit = Texture.Width / MapWidth;
        var textureRegions = plan.Areas.Select(a => PixelRect.FromEdges(
            (int)Math.Floor(a.X * perUnit) - 1, (int)Math.Floor(a.Z * perUnit) - 1,
            (int)Math.Ceiling(a.Right * perUnit) + 1, (int)Math.Ceiling(a.Bottom * perUnit) + 1).Intersect(Texture.Bounds)).ToList();
        var list = new List<Snapshot>
        {
            new BytesSnapshot(Smf.MetalMap, MapLayers.Metal),
            new TextureSnapshot(this, textureRegions),
            new MinimapSnapshot(this, textureRegions),
        };
        if (TrySplat(out var splat))
            list.Add(new DdsRegionSnapshot(this, splat, plan.Areas, MapLayers.Splat));
        if (NormalMapEntry() is string normals)
            list.Add(new DdsRegionSnapshot(this, normals, plan.Areas, MapLayers.Resources));
        return list;
    }

    /// <summary>The detail normal map's package entry, if it is a .dds this editor can change.</summary>
    string? NormalMapEntry()
    {
        if (Info.Resource("detailNormalTex") is not string name || Package.ResolveResource(name) is not string entry ||
            !entry.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            ResourceByEntry(entry);
            return entry;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Carries the look from A to B in the texture, the splat map and the normal map.</summary>
    string MoveLookCore(SpotLook plan, string how)
    {
        var done = new List<string>();
        var image = TextureImage;
        foreach (var region in plan.ApplyTo(image, upsideDown: false, MapWidth, MapHeight))
        {
            Texture.Update(image, region);
            RebuildMinimap(region);
            RefreshTexture(region);
        }
        textureChanged = true;
        done.Add("texture");

        if (TrySplat(out _))
        {
            var edit = SplatEdit(out var entry);
            var dds = edit.Dds;
            foreach (var file in plan.ApplyTo(edit.Levels[0], upsideDown: true, MapWidth, MapHeight))
            {
                dds.Encode(edit.Levels[0], 0, 0, file);
                MipChain.Rebuild(edit.Levels[0], dds.MipCount, file, m => edit.Levels[m], (m, level, dirty) => dds.Encode(level, m, 0, dirty));
            }
            changedResources.Add(entry);
            done.Add("splat map");
        }

        if (NormalMapEntry() is string normals)
        {
            var dds = ResourceByEntry(normals);
            var level0 = dds.Decode();
            foreach (var file in plan.ApplyTo(level0, upsideDown: true, MapWidth, MapHeight))
            {
                if (dds.Format == DdsFormat.Dxt1)
                    level0.MakeOpaque(file);
                dds.Encode(level0, 0, 0, file);
                dds.RebuildMips(level0, 0, file);
            }
            changedResources.Add(normals);
            ForgetResourceEdit(normals);
            done.Add("normal map");
        }
        string what = !plan.Swap ? "shifted"
            : plan.FillFromRim ? "carried; the old place filled smoothly from the ground around it (the Clone tool can add texture detail)"
            : plan.FillX == plan.BX && plan.FillZ == plan.BZ ? "swapped with the ground at the new place"
            : $"carried; the old place filled from nearby ground at ({plan.FillX:0}, {plan.FillZ:0})";
        double from = Heights.HeightAt(plan.AX, plan.AZ), to = Heights.HeightAt(plan.BX, plan.BZ);
        string ground = plan.Swap && (Math.Abs(from - to) > 40 || (from < 0) != (to < 0))
            ? $"; the new place is on different ground (height {to:0} vs {from:0}), so the look's rim may need touching up"
            : "";
        return $"look {what} in the {string.Join(", ", done)} (radius {plan.Radius:0}, {how}){ground}";
    }

    /// <summary>
    /// What may stop BAR's T1 extractor from being built on a spot, checked as the engine does: each footprint square
    /// within <see cref="ExtractorMaxHeightDifference"/> of the build height. Notes water depth (allowed, but only
    /// builders that reach it can build there).
    /// </summary>
    public IReadOnlyList<string> SpotNotes(MetalSpot spot)
    {
        var notes = new List<string>();
        var h = Heights;
        // Build height: the engine averages the corners around the centre (GameHelper.cpp GetBuildHeight).
        int cx = (int)((spot.X - SmfFile.SquareSize / 2.0) / SmfFile.SquareSize), cz = (int)((spot.Z - SmfFile.SquareSize / 2.0) / SmfFile.SquareSize);
        double build = (h[cx, cz] + h[cx + 1, cz] + h[cx, cz + 1] + h[cx + 1, cz + 1]) / 4;
        int x0 = (int)Math.Round(spot.X / SmfFile.SquareSize) - ExtractorFootprint / 2;
        int z0 = (int)Math.Round(spot.Z / SmfFile.SquareSize) - ExtractorFootprint / 2;
        double worst = 0;
        for (int z = z0; z < z0 + ExtractorFootprint; z++)
            for (int x = x0; x < x0 + ExtractorFootprint; x++)
                worst = Math.Max(worst, Math.Abs(h[x, z] - build));
        if (worst > ExtractorMaxHeightDifference)
            notes.Add($"too steep for an extractor: the ground varies {worst:0} units under it (BAR allows {ExtractorMaxHeightDifference:0.#})");
        if (build < 0)
            notes.Add($"under water ({-build:0} deep): only builders that reach it can place an extractor");
        foreach (var c in spot.Cells)
            if (c.X == 0 || c.Z == 0 || c.X == Smf.MetalMapWidth - 1 || c.Z == Smf.MetalMapHeight - 1)
            {
                notes.Add("touches the map edge, where BAR's spot finder doesn't look");
                break;
            }
        return notes;
    }
}
