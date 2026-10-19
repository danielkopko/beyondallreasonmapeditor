using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MapCore.Lua;

namespace MapCore.Items;

/// <summary>
/// The gameplay settings of a new feature made from another feature's model (the editor's Experimental: new feature
/// from this model). Only keys the engine reads are here (Recoil's FeatureDefHandler.cpp, checked 2026-10-15), and
/// every one is written out, defaults included: the engine's defaults chain (reclaimable follows indestructible,
/// autoreclaimable follows reclaimable), so a key left out could change another without saying so.
/// </summary>
public sealed record CustomFeatureSettings
{
    public string Description { get; init; } = "";

    /// <summary>Hit points. Written as <c>damage</c>, the spelling maps use; the engine reads <c>health</c>, then <c>damage</c>.</summary>
    public double Health { get; init; } = 100;

    /// <summary>Whether units path around it (over its footprint, not its model).</summary>
    public bool Blocking { get; init; } = true;

    public bool Reclaimable { get; init; } = true;

    /// <summary>
    /// How much reclaiming it takes: each frame a builder takes off reclaimSpeed / 30 of it (Builder.cpp), and
    /// reclaimSpeed defaults to workerTime (UnitDef.cpp), so one builder needs ReclaimTime ÷ its build power seconds.
    /// </summary>
    public double ReclaimTime { get; init; } = 600;

    /// <summary>Whether builders on area-reclaim or patrol take it by themselves.</summary>
    public bool AutoReclaimable { get; init; } = true;

    public double Metal { get; init; }
    public double Energy { get; init; }
    public bool Flammable { get; init; }

    /// <summary>Takes no damage at all (and, by the engine's default, can't be reclaimed unless that is set too).</summary>
    public bool Indestructible { get; init; }

    /// <summary>The blocked area, in footprint squares of 16 game units.</summary>
    public int FootprintX { get; init; } = 1;
    public int FootprintZ { get; init; } = 1;

    /// <summary>
    /// Whether buildings can go on it: 0 no; 1 yes, and units can cross it; 2 yes, but units can't cross it. Not an
    /// engine key: the map's structures gadget takes such features off the blocking map (and, for 2, gives the ground
    /// under them a terrain type with no speed). Without the gadget running they simply block.
    /// </summary>
    public int BuildOn { get; init; }

    /// <summary>
    /// The user's "Ancient Monolith" example over these settings, with only the keys the engine reads:
    /// <c>nodgun</c> and <c>sightDistance</c> are dropped (neither the engine nor BAR's feature post-processing reads
    /// them). The example sets no footprint, so the model's own stays.
    /// </summary>
    public CustomFeatureSettings WithAncientMonolith() => this with
    {
        Description = "Ancient Alien Monolith",
        Blocking = true,
        Health = 900_000,
        Indestructible = false,
        Reclaimable = true,
        ReclaimTime = 36_000,
        AutoReclaimable = false,
        Metal = 5000,
        Energy = 0,
        Flammable = false,
    };

    /// <summary>
    /// The settings a definition has, with the engine's defaults where it says nothing (FeatureDefHandler.cpp:
    /// health 0.1 at least, reclaimTime (metal + energy) × 6 and at least 1, footprint 1).
    /// </summary>
    public static CustomFeatureSettings FromDefinition(FeatureDef def)
    {
        var t = def.Table;
        double Number(string key, double fallback) => LuaValuesRuntime.ToNumber(t?.GetIgnoreCase(key)) ?? fallback;
        bool Flag(string key, bool fallback) => t?.GetIgnoreCase(key) switch
        {
            bool b => b,
            double d => d != 0,
            string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1",
            _ => fallback,
        };
        double metal = Number("metal", def.Metal), energy = Number("energy", def.Energy);
        bool indestructible = Flag("indestructible", false);
        bool reclaimable = Flag("reclaimable", !indestructible);
        return new CustomFeatureSettings
        {
            Description = def.Description ?? "",
            Health = Math.Max(0.1, Number("health", Number("damage", 0))),
            Blocking = Flag("blocking", true),
            Reclaimable = reclaimable,
            ReclaimTime = Math.Max(1, Number("reclaimtime", (metal + energy) * 6)),
            AutoReclaimable = Flag("autoreclaimable", reclaimable),
            Metal = metal,
            Energy = energy,
            Flammable = Flag("flammable", false),
            Indestructible = indestructible,
            FootprintX = (int)Math.Clamp(Number("footprintx", 1), 1, 32),
            FootprintZ = (int)Math.Clamp(Number("footprintz", 1), 1, 32),
        };
    }

    /// <summary>What is wrong with these settings, or null.</summary>
    public string? Problem
    {
        get
        {
            static bool Bad(double v, double min, double max) => double.IsNaN(v) || v < min || v > max;
            if (Description.Length > 200)
                return "the description is longer than 200 characters";
            if (Bad(Health, 0.1, 1e9))
                return "health must be from 0.1 to 1,000,000,000";
            if (Bad(ReclaimTime, 1, 1e9))
                return "reclaim time must be from 1 to 1,000,000,000";
            if (Bad(Metal, 0, 1e7) || Bad(Energy, 0, 1e7))
                return "metal and energy must be from 0 to 10,000,000";
            if (FootprintX is < 1 or > 32 || FootprintZ is < 1 or > 32)
                return "the footprint must be from 1 to 32 squares each way";
            if (BuildOn is < 0 or > 2)
                return "build-on must be 0 (no), 1 (units can cross) or 2 (units can't cross)";
            return null;
        }
    }

    // ---------------------------------------------------------------- the op's text

    /// <summary>
    /// The settings as one op argument: key=value pairs separated by ';', every value percent-encoded, so ':', ';',
    /// '=', '%' and '&amp;' in a description are safe (and no '&amp;' reaches a menu as a mnemonic).
    /// </summary>
    public string Encode()
    {
        static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        static string B(bool v) => v ? "1" : "0";
        return string.Join(";",
            "description=" + Uri.EscapeDataString(Description),
            "damage=" + N(Health), "blocking=" + B(Blocking), "reclaimable=" + B(Reclaimable), "reclaimtime=" + N(ReclaimTime),
            "autoreclaimable=" + B(AutoReclaimable), "metal=" + N(Metal), "energy=" + N(Energy), "flammable=" + B(Flammable),
            "indestructible=" + B(Indestructible), "footprintx=" + FootprintX.ToString(CultureInfo.InvariantCulture),
            "footprintz=" + FootprintZ.ToString(CultureInfo.InvariantCulture)) + (BuildOn != 0 ? ";buildon=" + BuildOn.ToString(CultureInfo.InvariantCulture) : "");
    }

    public static CustomFeatureSettings Decode(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
                throw new InvalidOperationException($"'{pair}' is not key=value.");
            values[pair[..eq]] = Uri.UnescapeDataString(pair[(eq + 1)..]);
        }
        var unknown = values.Keys.Except(["description", "damage", "blocking", "reclaimable", "reclaimtime", "autoreclaimable", "metal", "energy",
            "flammable", "indestructible", "footprintx", "footprintz", "buildon"]).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException($"unknown setting(s) {string.Join(", ", unknown)}: only keys the engine reads can be set.");
        var d = new CustomFeatureSettings();
        string? Get(string key) => values.TryGetValue(key, out var v) ? v : null;
        double Num(string key, double fallback) => Get(key) is string v
            ? double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : throw new InvalidOperationException($"{key}: '{v}' is not a number.")
            : fallback;
        bool Flag(string key, bool fallback) => Get(key) switch
        {
            null => fallback,
            "1" or "true" => true,
            "0" or "false" => false,
            var v => throw new InvalidOperationException($"{key}: '{v}' is not 1 or 0."),
        };
        return new CustomFeatureSettings
        {
            Description = Get("description") ?? d.Description,
            Health = Num("damage", d.Health),
            Blocking = Flag("blocking", d.Blocking),
            Reclaimable = Flag("reclaimable", d.Reclaimable),
            ReclaimTime = Num("reclaimtime", d.ReclaimTime),
            AutoReclaimable = Flag("autoreclaimable", d.AutoReclaimable),
            Metal = Num("metal", d.Metal),
            Energy = Num("energy", d.Energy),
            Flammable = Flag("flammable", d.Flammable),
            Indestructible = Flag("indestructible", d.Indestructible),
            FootprintX = (int)Num("footprintx", d.FootprintX),
            FootprintZ = (int)Num("footprintz", d.FootprintZ),
            BuildOn = (int)Num("buildon", 0),
        };
    }
}

/// <summary>
/// A new feature made with the editor (New feature from a model) that a map has: its name, the feature whose model it
/// was made from, its model's path in the map (null: not there), and its settings as its file has them.
/// </summary>
public sealed record MadeFeature(string Name, string MadeFrom, string? Model, CustomFeatureSettings Settings);

/// <summary>Writing a new feature's definition file (features/&lt;name&gt;.lua) that uses another feature's model.</summary>
public static class CustomFeature
{
    /// <summary>A feature name the editor will make: lower-case (the engine lower-cases names anyway), letters, digits and '_'.</summary>
    public static readonly Regex NamePattern = new("^[a-z][a-z0-9_]{0,63}$", RegexOptions.CultureInvariant);

    static readonly Regex Header = new(@"^-- ([a-z][a-z0-9_]{0,63}): a feature made with BAR Map Editor from (.+)'s model\.\r?$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The name and base a definition file's first line gives when <see cref="Lua"/> wrote it without a comment: a new
    /// feature, or a part of a structure kind made from models (told apart by its name only). Null for any other file.
    /// </summary>
    public static (string Name, string BaseName)? MadeBy(string firstLine) =>
        Header.Match(firstLine) is { Success: true } m ? (m.Groups[1].Value, m.Groups[2].Value) : null;

    /// <summary>What it keeps of the model's own definition: how it looks and how it is hit. Gameplay comes from the settings.</summary>
    static readonly string[] Kept = ["height", "upright", "noselect", "hitdensity", "collisionvolumetype", "collisionvolumescales", "collisionvolumeoffsets",
        "collisionvolumetest", "usepiececollisionvolumes"];

    /// <summary>Of its customparams, only how it is drawn and who made the model (not lod_master and the like, which name the original).</summary>
    static readonly string[] KeptCustom = ["normaltex", "normalmaps", "treeshader", "cuspbr", "model_author", "author"];

    /// <summary>
    /// The definition file's text: the model as the base names it (BAR checks objects3d/ + object exists), the
    /// base's look and collision, then every gameplay setting, wrapped in lowerkeys as maps do. The same input always
    /// gives the same bytes. <paramref name="comment"/>, when given, says what it is instead of "made from baseName's model".
    /// </summary>
    public static string Lua(string name, string baseName, string obj, LuaMap? baseTable, string? category, CustomFeatureSettings settings, string? comment = null)
    {
        var text = new StringBuilder();
        if (comment != null)
            text.Append("-- ").Append(name).Append(": ").Append(comment).Append(".\n");
        else
            text.Append("-- ").Append(name).Append(": a feature made with BAR Map Editor from ").Append(baseName).Append("'s model.\n");
        text.Append("-- Only keys the engine reads are set; every one is written out.\n");
        text.Append("local featureDef = {\n");
        void Line(string key, object value) => text.Append('\t').Append(key).Append(" = ").Append(Value(value)).Append(",\n");
        Line("name", name);
        Line("description", settings.Description);
        Line("object", obj);
        if (!string.IsNullOrWhiteSpace(category))
            Line("category", category);
        Line("blocking", settings.Blocking);
        Line("damage", settings.Health);
        Line("indestructible", settings.Indestructible);
        Line("reclaimable", settings.Reclaimable);
        Line("reclaimtime", settings.ReclaimTime);
        Line("autoreclaimable", settings.AutoReclaimable);
        Line("metal", settings.Metal);
        Line("energy", settings.Energy);
        Line("flammable", settings.Flammable);
        Line("footprintx", settings.FootprintX);
        Line("footprintz", settings.FootprintZ);
        foreach (var key in Kept)
            if (baseTable?.GetIgnoreCase(key) is object v && Writable(v))
                Line(key, v);
        text.Append("\tcustomparams = {\n");
        text.Append("\t\tmade_from = ").Append(Value(baseName)).Append(",\n");
        if (baseTable?.GetIgnoreCase("customparams") is LuaMap custom)
            foreach (var key in KeptCustom)
                if (custom.GetIgnoreCase(key) is object v && Writable(v))
                    text.Append("\t\t").Append(key).Append(" = ").Append(Value(v)).Append(",\n");
        text.Append("\t},\n");
        text.Append("}\n");
        text.Append("return lowerkeys({[featureDef.name] = featureDef})\n");
        return text.ToString();
    }

    static bool Writable(object value) => value is string or bool || value is double d && double.IsFinite(d);

    static string Value(object value) => value switch
    {
        string s => Quote(s),
        bool b => b ? "true" : "false",
        double d => LuaValuesRuntime.NumberToString(d),
        int i => i.ToString(CultureInfo.InvariantCulture),
        _ => throw new ArgumentException($"can't write {value.GetType().Name} to Lua"),
    };

    /// <summary>A Lua string literal: backslash, quote and control characters escaped (\ddd), the rest as written.</summary>
    static string Quote(string s)
    {
        var text = new StringBuilder("\"");
        foreach (char c in s)
        {
            if (c is '\\' or '"')
                text.Append('\\').Append(c);
            else if (c < 32 || c == 127)
                text.Append('\\').Append(((int)c).ToString("000", CultureInfo.InvariantCulture));
            else
                text.Append(c);
        }
        return text.Append('"').ToString();
    }
}
