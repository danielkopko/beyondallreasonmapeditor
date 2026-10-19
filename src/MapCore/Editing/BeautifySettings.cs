using System.Globalization;
using MapCore.Imaging;

namespace MapCore.Editing;

/// <summary>
/// How the beautify pass roughens cliffs (MapEditSession.CliffDetail.cs): broken rock, ledges following the height and
/// gullies running down them, on ground steeper than <see cref="CliffFrom"/>, and if asked a gentle swell on open
/// ground. Whatever it does, every BAR unit goes where it went and buildings go where they went: where a change would
/// alter that, it is taken back there. Heights are game units; angles degrees; the others 0 to 1.
/// </summary>
public sealed record CliffDetailSettings
{
    /// <summary>Cliffs start at this slope (full detail 15° steeper).</summary>
    public double CliffFrom { get; init; } = 40;

    /// <summary>The size of the rock's lumps and ledges.</summary>
    public double Amount { get; init; } = 10;

    /// <summary>Ledges that follow the height, as rock beds weather (0 none).</summary>
    public double Strata { get; init; } = 0.5;

    /// <summary>Grooves running down the cliffs (0 none).</summary>
    public double Gullies { get; init; } = 0.5;

    /// <summary>A gentle swell on nearly level open ground, under 8° (game units; 0: open ground is left as it is). Slopes, ramps among them, keep their shape.</summary>
    public double Ground { get; init; }

    /// <summary>Set when the map is symmetric: the noise is read at mirror images alike.</summary>
    public MirrorKind? Mirror { get; init; }

    public string? Problem =>
        !(CliffFrom is >= 10 and <= 85) ? "cliffs must start between 10 and 85 degrees"
        : !(Amount is >= 0 and <= 60) ? "the detail's size must be from 0 to 60 game units"
        : !(Ground is >= 0 and <= 20) ? "the swell on open ground must be from 0 to 20 game units"
        : !(Strata is >= 0 and <= 1) || !(Gullies is >= 0 and <= 1) ? "ledges and gullies must be from 0 to 1"
        : null;

    public string Encode()
    {
        static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        var text = $"from={N(CliffFrom)};amount={N(Amount)};strata={N(Strata)};gullies={N(Gullies)};ground={N(Ground)}";
        return Mirror is MirrorKind m ? text + ";mirror=" + MapMirror.CodeOf(m) : text;
    }

    public static CliffDetailSettings Decode(string text)
    {
        var d = new CliffDetailSettings();
        var values = text.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0].Trim(), p => p.Length == 2 ? p[1].Trim() : throw new InvalidOperationException($"'{p[0]}' is not key=value."));
        if (values.Keys.Except(["from", "amount", "strata", "gullies", "ground", "mirror"]).ToList() is { Count: > 0 } unknown)
            throw new InvalidOperationException($"unknown cliff detail setting(s) {string.Join(", ", unknown)}.");
        double Num(string key, double fallback) => values.TryGetValue(key, out var v)
            ? double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : throw new InvalidOperationException($"{key}: '{v}' is not a number.")
            : fallback;
        return new CliffDetailSettings
        {
            CliffFrom = Num("from", d.CliffFrom), Amount = Num("amount", d.Amount), Strata = Num("strata", d.Strata), Gullies = Num("gullies", d.Gullies),
            Ground = Num("ground", d.Ground), Mirror = values.TryGetValue("mirror", out var m) ? MapMirror.ParseKind(m) : null,
        };
    }
}

/// <summary>
/// How the beautify pass paints the map texture by its terrain (MapEditSession.Beautify.cs): each material's colour and
/// how strongly it is laid where it belongs. Rock on cliffs (with strata following the height), dirt and scree on middle
/// slopes and heaped at cliff feet, sand along the shore with a wet edge at the waterline, a darker seabed under water,
/// an optional tint for open ground, shading in hollows, and (if asked) hard edges in the texture softened with dirt.
/// Boundaries wander with noise fixed to the map, and every material keeps the ground's own grain. Amounts are 0 to 1;
/// angles are degrees; heights are game units above the water.
/// </summary>
public sealed record BeautifySettings
{
    /// <summary>How much of it all: every material's amount and the shading are scaled by this (1: as set).</summary>
    public double Strength { get; init; } = 0.75;

    public Rgba Rock { get; init; } = new(118, 112, 104, 255);
    public double RockAmount { get; init; } = 0.85;
    /// <summary>Rock starts at this slope and is full at <see cref="RockTo"/>.</summary>
    public double RockFrom { get; init; } = 38;
    public double RockTo { get; init; } = 56;
    /// <summary>How strongly the rock shows layers that follow the height (0 none).</summary>
    public double Strata { get; init; } = 0.45;

    public Rgba Dirt { get; init; } = new(132, 112, 86, 255);
    public double DirtAmount { get; init; } = 0.6;
    /// <summary>Dirt starts on slopes this steep, full at <see cref="DirtTo"/>; it also gathers at cliff feet.</summary>
    public double DirtFrom { get; init; } = 16;
    public double DirtTo { get; init; } = 32;

    public Rgba Sand { get; init; } = new(200, 184, 140, 255);
    public double SandAmount { get; init; } = 0.7;
    /// <summary>How far above the water the shore's sand reaches.</summary>
    public double SandHeight { get; init; } = 24;

    /// <summary>The wet edge just at the waterline: mud, or wet sand.</summary>
    public Rgba Mud { get; init; } = new(92, 80, 64, 255);
    public double MudAmount { get; init; } = 0.5;

    /// <summary>The ground under the water, deepening with depth.</summary>
    public Rgba Seabed { get; init; } = new(60, 74, 70, 255);
    public double SeabedAmount { get; init; } = 0.4;

    /// <summary>A tint for open, level ground (off unless set): the map's own ground is usually best left.</summary>
    public Rgba Ground { get; init; } = new(92, 112, 58, 255);
    public double GroundAmount { get; init; }

    /// <summary>Darkens hollows, cracks and cliff feet, and lightens ridges a little (0 none).</summary>
    public double Crevices { get; init; } = 0.5;

    /// <summary>
    /// Grain and tone made up where the texture has little of its own (a freshly painted map): fine speckle and cracks,
    /// and broad patches a little lighter, darker, warmer or cooler (0 none). Texture that has grain gets less.
    /// </summary>
    public double Detail { get; init; } = 0.5;

    /// <summary>How much the boundaries between materials wander (0: they follow slope and height exactly).</summary>
    public double Breakup { get; init; } = 0.6;

    /// <summary>Softens hard edges already in the texture (painted roads, plates) with a dirty blend (0: off).</summary>
    public double SoftenEdges { get; init; }

    /// <summary>Set when the map is symmetric: the noise is read at mirror images alike, so both halves come out the same.</summary>
    public MirrorKind? Mirror { get; init; }

    /// <summary>
    /// The colours are the map's own, read from it as the pass starts (<see cref="MapEditSession.SuggestBeautify"/>):
    /// written <c>colours=map</c>, for the command line. The editor reads them first and writes them out instead.
    /// </summary>
    public bool FromMap { get; init; }

    /// <summary>What is wrong with these settings, or null.</summary>
    public string? Problem
    {
        get
        {
            foreach (var (name, v) in new[] { ("strength", Strength), ("rock", RockAmount), ("strata", Strata), ("dirt", DirtAmount), ("sand", SandAmount), ("mud", MudAmount),
                         ("seabed", SeabedAmount), ("ground", GroundAmount), ("crevices", Crevices), ("detail", Detail), ("breakup", Breakup), ("soften", SoftenEdges) })
                if (double.IsNaN(v) || v is < 0 or > 1)
                    return $"{name} must be from 0 to 1";
            if (!(RockFrom is >= 0 and <= 89) || !(RockTo > RockFrom && RockTo <= 90))
                return "rock's angles must rise from 0 to 90 degrees";
            if (!(DirtFrom is >= 0 and <= 89) || !(DirtTo > DirtFrom && DirtTo <= 90))
                return "dirt's angles must rise from 0 to 90 degrees";
            if (!(SandHeight is >= 1 and <= 500))
                return "the sand's height must be from 1 to 500 game units";
            return null;
        }
    }

    // ---------------------------------------------------------------- the op's text

    /// <summary>The settings as one op argument: key=value pairs separated by ';' (colours as r,g,b).</summary>
    public string Encode()
    {
        static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        static string C(Rgba c) => FormattableString.Invariant($"{c.R},{c.G},{c.B}");
        var parts = new List<string>
        {
            "strength=" + N(Strength), "rock=" + C(Rock), "rockamount=" + N(RockAmount), "rockfrom=" + N(RockFrom), "rockto=" + N(RockTo), "strata=" + N(Strata),
            "dirt=" + C(Dirt), "dirtamount=" + N(DirtAmount), "dirtfrom=" + N(DirtFrom), "dirtto=" + N(DirtTo),
            "sand=" + C(Sand), "sandamount=" + N(SandAmount), "sandheight=" + N(SandHeight),
            "mud=" + C(Mud), "mudamount=" + N(MudAmount), "seabed=" + C(Seabed), "seabedamount=" + N(SeabedAmount),
            "ground=" + C(Ground), "groundamount=" + N(GroundAmount), "crevices=" + N(Crevices), "detail=" + N(Detail), "breakup=" + N(Breakup), "soften=" + N(SoftenEdges),
        };
        if (Mirror is MirrorKind m)
            parts.Add("mirror=" + MapMirror.CodeOf(m));
        if (FromMap)
            parts.Add("colours=map");
        return string.Join(";", parts);
    }

    public static BeautifySettings Decode(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
                throw new InvalidOperationException($"'{pair}' is not key=value.");
            values[pair[..eq].Trim()] = pair[(eq + 1)..].Trim();
        }
        var d = new BeautifySettings();
        var known = new[] { "strength", "rock", "rockamount", "rockfrom", "rockto", "strata", "dirt", "dirtamount", "dirtfrom", "dirtto", "sand", "sandamount", "sandheight",
            "mud", "mudamount", "seabed", "seabedamount", "ground", "groundamount", "crevices", "detail", "breakup", "soften", "mirror", "colours" };
        if (values.Keys.Except(known).ToList() is { Count: > 0 } unknown)
            throw new InvalidOperationException($"unknown beautify setting(s) {string.Join(", ", unknown)}.");
        double Num(string key, double fallback) => values.TryGetValue(key, out var v)
            ? double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : throw new InvalidOperationException($"{key}: '{v}' is not a number.")
            : fallback;
        Rgba Col(string key, Rgba fallback)
        {
            if (!values.TryGetValue(key, out var v))
                return fallback;
            var c = v.Split(',');
            if (c.Length != 3 || !c.All(p => byte.TryParse(p.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
                throw new InvalidOperationException($"{key}: '{v}' is not a colour (r,g,b from 0 to 255).");
            return new Rgba(byte.Parse(c[0].Trim(), CultureInfo.InvariantCulture), byte.Parse(c[1].Trim(), CultureInfo.InvariantCulture),
                byte.Parse(c[2].Trim(), CultureInfo.InvariantCulture), 255);
        }
        return new BeautifySettings
        {
            Strength = Num("strength", d.Strength),
            Rock = Col("rock", d.Rock), RockAmount = Num("rockamount", d.RockAmount), RockFrom = Num("rockfrom", d.RockFrom), RockTo = Num("rockto", d.RockTo),
            Strata = Num("strata", d.Strata),
            Dirt = Col("dirt", d.Dirt), DirtAmount = Num("dirtamount", d.DirtAmount), DirtFrom = Num("dirtfrom", d.DirtFrom), DirtTo = Num("dirtto", d.DirtTo),
            Sand = Col("sand", d.Sand), SandAmount = Num("sandamount", d.SandAmount), SandHeight = Num("sandheight", d.SandHeight),
            Mud = Col("mud", d.Mud), MudAmount = Num("mudamount", d.MudAmount), Seabed = Col("seabed", d.Seabed), SeabedAmount = Num("seabedamount", d.SeabedAmount),
            Ground = Col("ground", d.Ground), GroundAmount = Num("groundamount", d.GroundAmount),
            Crevices = Num("crevices", d.Crevices), Detail = Num("detail", d.Detail), Breakup = Num("breakup", d.Breakup), SoftenEdges = Num("soften", d.SoftenEdges),
            Mirror = values.TryGetValue("mirror", out var mirror) ? MapMirror.ParseKind(mirror) : null,
            FromMap = values.TryGetValue("colours", out var from) && (from == "map" ? true : throw new InvalidOperationException($"colours: '{from}' isn't map (the map's own colours).")),
        };
    }

    // ---------------------------------------------------------------- presets

    /// <summary>The colour sets offered: the map's own (worked out from it), then a few landscapes.</summary>
    public static IReadOnlyList<string> PresetNames { get; } = ["From this map", "Temperate", "Desert", "Volcanic", "Snow and ice"];

    /// <summary>A landscape's colours (the map's own are <see cref="MapEditSession.SuggestBeautify"/>).</summary>
    public BeautifySettings WithPreset(string name) => name switch
    {
        "Temperate" => this with
        {
            Rock = new(118, 112, 104, 255), Dirt = new(132, 112, 86, 255), Sand = new(200, 184, 140, 255), Mud = new(92, 80, 64, 255),
            Seabed = new(60, 74, 70, 255), Ground = new(92, 112, 58, 255),
        },
        "Desert" => this with
        {
            Rock = new(160, 116, 82, 255), Dirt = new(196, 158, 110, 255), Sand = new(224, 200, 152, 255), Mud = new(140, 110, 80, 255),
            Seabed = new(120, 112, 92, 255), Ground = new(206, 172, 122, 255),
        },
        "Volcanic" => this with
        {
            Rock = new(56, 50, 48, 255), Dirt = new(92, 74, 62, 255), Sand = new(66, 60, 58, 255), Mud = new(46, 40, 36, 255),
            Seabed = new(38, 44, 46, 255), Ground = new(82, 72, 62, 255),
        },
        "Snow and ice" => this with
        {
            Rock = new(108, 112, 120, 255), Dirt = new(150, 150, 154, 255), Sand = new(226, 230, 236, 255), Mud = new(120, 128, 136, 255),
            Seabed = new(70, 92, 104, 255), Ground = new(228, 232, 238, 255),
        },
        _ => this,
    };
}
