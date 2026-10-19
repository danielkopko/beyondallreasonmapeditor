using System.Globalization;
using MapCore.Lua;

namespace MapCore;

/// <summary>A team's start position from mapinfo's <c>teams</c> table (game units).</summary>
public sealed record StartPosition(int Team, double X, double Z);

/// <summary>
/// A place where Lua code may change a mapinfo value after the table is built.
/// <see cref="Path"/> uses '.' between keys; keys computed at run time appear as "[name]".
/// </summary>
public sealed record MapInfoOverride(string File, string Path, string Kind);

/// <summary>
/// mapinfo.lua: typed access to the returned table, edited in place through <see cref="LuaDocument"/>.
/// Paths use '.' between keys, e.g. "smf.minheight", "atmosphere.fogColor", "teams.0.startPos.x".
/// </summary>
public sealed class MapInfo
{
    MapInfo(LuaDocument document) => Document = document;

    /// <summary>The underlying file, for edits beyond the typed helpers.</summary>
    public LuaDocument Document { get; }

    public string Text => Document.Text;
    public LuaTable Table => Document.Root;

    /// <summary>Name of the variable holding the table ("mapinfo"), or null when the table is returned directly.</summary>
    public string? TableName => Document.RootName;

    /// <exception cref="LuaSyntaxException">The text is not Lua this reader understands.</exception>
    /// <exception cref="InvalidDataException">No returned table was found.</exception>
    public static MapInfo Parse(string text) => new(LuaDocument.Parse(text));

    public static MapInfo Load(string path) => new(LuaDocument.Load(path));

    public static MapInfo FromBytes(byte[] bytes) => new(LuaDocument.FromBytes(bytes));

    public byte[] ToBytes() => Document.ToBytes();

    public void Save(string path) => Document.Save(path);

    // ---------------------------------------------------------------- reading

    public LuaValue? Get(string path) => Document.Get(path);

    public string? GetString(string path) => (Get(path) as LuaString)?.Value;

    public double? GetNumber(string path) => (Get(path) as LuaNumber)?.Value;

    /// <summary>A table of plain numbers, such as a colour.</summary>
    public double[]? GetNumbers(string path) =>
        Get(path) is LuaTable t && t.Fields.All(f => f.Position != null && f.Value is LuaNumber)
            ? t.Fields.Select(f => ((LuaNumber)f.Value).Value).ToArray()
            : null;

    /// <summary>A string, or a number written as text (some maps write <c>version = 1.2</c>).</summary>
    public string? GetText(string path) => Get(path) switch
    {
        LuaString s => s.Value,
        LuaNumber n => LuaDocument.FormatNumber(n.Value),
        _ => null,
    };

    public string? Name => GetText("name");
    public string? Version => GetText("version");
    public string? Description => GetText("description");
    public string? Author => GetText("author");
    public string? MapFile => GetString("mapfile");
    public double? MaxMetal => GetNumber("maxMetal");
    public double? ExtractorRadius => GetNumber("extractorRadius");

    /// <summary>smf.minheight: when present, the engine uses it instead of the SMF header's minimum height.</summary>
    public double? MinHeight => GetNumber("smf.minHeight");

    /// <summary>smf.maxheight: when present, the engine uses it instead of the SMF header's maximum height.</summary>
    public double? MaxHeight => GetNumber("smf.maxHeight");

    /// <summary>smf.smtFileName0, 1, ... up to the first missing number, as the engine reads them.</summary>
    public IReadOnlyList<string> SmtFileNames
    {
        get
        {
            var names = new List<string>();
            while (Get($"smf.smtFileName{names.Count}") is LuaValue value)
                names.Add(value is LuaString s ? s.Value : ".smt"); // the engine's default for a non-string
            return names;
        }
    }

    /// <summary>
    /// smf.minimapTex / metalmapTex / typemapTex / grassmapTex that are set. The engine then uses that image
    /// instead of the matching SMF section, so edits to the SMF's copy would not show in game.
    /// </summary>
    public IReadOnlyDictionary<string, string> TextureOverrides =>
        new[] { "minimapTex", "metalmapTex", "typemapTex", "grassmapTex" }
            .Select(key => (Key: key, Value: GetString($"smf.{key}")))
            .Where(p => !string.IsNullOrEmpty(p.Value))
            .ToDictionary(p => p.Key, p => p.Value!);

    /// <summary>A resources texture name, e.g. "detailNormalTex" or "splatDistrTex".</summary>
    public string? Resource(string key) => GetString($"resources.{key}");

    /// <summary>Start positions from <c>teams[n].startPos</c>; the engine numbers teams from 0.</summary>
    public IReadOnlyList<StartPosition> StartPositions =>
        (Get("teams") as LuaTable)?.Fields
            .Select(f => (Team: f.Key is LuaNumber n ? (int)n.Value : f.Position, Start: (f.Value as LuaTable)?["startPos"] as LuaTable))
            .Where(t => t.Team != null && t.Start?["x"] is LuaNumber && t.Start["z"] is LuaNumber)
            .Select(t => new StartPosition(t.Team!.Value, ((LuaNumber)t.Start!["x"]!).Value, ((LuaNumber)t.Start["z"]!).Value))
            .OrderBy(s => s.Team)
            .ToList() ?? [];

    // ---------------------------------------------------------------- editing

    public void SetNumber(string path, double value) => Document.SetNumber(path, value);
    public void SetString(string path, string value) => Document.SetString(path, value);
    public void SetNumbers(string path, IReadOnlyList<double> values) => Document.SetNumbers(path, values);

    /// <inheritdoc cref="LuaDocument.SetFromText"/>
    public void SetFromText(string path, string input) => Document.SetFromText(path, input);

    /// <summary>Moves a team's start position, adding the team (as <c>[n] = { startPos = ... }</c>) if it is new.</summary>
    public void SetStartPosition(int team, double x, double z)
    {
        string prefix = $"teams.{team.ToString(CultureInfo.InvariantCulture)}.startPos";
        SetNumber($"{prefix}.x", x);
        SetNumber($"{prefix}.z", z);
    }

    public void RemoveStartPosition(int team) => Document.Remove($"teams.{team.ToString(CultureInfo.InvariantCulture)}");

    // ---------------------------------------------------------------- overrides

    /// <summary>
    /// Code that may change values after the table is built: assignments like <c>mapinfo.water.damage = 50</c>
    /// later in mapinfo.lua, and — when mapinfo.lua merges the mapconfig/mapinfo folder, as the standard template
    /// does — assignments and returned tables in mapconfig/mapinfo/*.lua. These may be conditional (map options),
    /// so they are reported, not applied.
    /// </summary>
    public IReadOnlyList<MapInfoOverride> FindOverrides(string? packageRoot)
    {
        var result = new List<MapInfoOverride>();
        if (TableName != null)
            result.AddRange(Assignments(MapPackage.MapInfoFileName, Text, Document.Tokens, TableName, Table));

        var folder = packageRoot == null ? null : Path.Combine(packageRoot, "mapconfig", "mapinfo");
        if (folder == null || !Directory.Exists(folder) || !Text.Contains("mapconfig/mapinfo", StringComparison.OrdinalIgnoreCase))
            return result;

        var files = Directory.EnumerateFiles(folder, "*.lua", new EnumerationOptions { AttributesToSkip = 0 })
            .Order(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var entry = FileSystemUtil.EntryPath(packageRoot!, file);
            var source = File.ReadAllText(file);
            List<LuaToken> fileTokens;
            try
            {
                fileTokens = LuaLexer.Tokenize(source);
            }
            catch (LuaSyntaxException)
            {
                result.Add(new(entry, "*", "unreadable Lua; may change anything"));
                continue;
            }
            result.AddRange(Assignments(entry, source, fileTokens, "mapinfo", null));

            int r = fileTokens.FindLastIndex(t => t.Kind == LuaTokenKind.Name && t.Text == "return");
            if (r >= 0 && r + 1 < fileTokens.Count && fileTokens[r + 1].Is("{"))
            {
                int start = r + 1;
                var returned = LuaTableParser.ParseTable(source, fileTokens, ref start);
                result.AddRange(LeafPaths(returned, "").Select(p => new MapInfoOverride(entry, p, "returned table (merged)")));
            }
        }
        return result;
    }

    /// <summary>Overrides that touch <paramref name="path"/>: the same key, a table above it, or a key inside it.</summary>
    public static IEnumerable<MapInfoOverride> Affecting(IEnumerable<MapInfoOverride> overrides, string path) =>
        overrides.Where(o => PathsOverlap(o.Path, path));

    static bool PathsOverlap(string a, string b)
    {
        var x = a.Split('.');
        var y = b.Split('.');
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++)
            if (!(x[i] == "*" || y[i] == "*" || x[i].StartsWith('[') || y[i].StartsWith('[') ||
                  x[i].Equals(y[i], StringComparison.OrdinalIgnoreCase)))
                return false;
        return true;
    }

    static IEnumerable<MapInfoOverride> Assignments(string file, string source, IReadOnlyList<LuaToken> tokens, string variable, LuaTable? skip)
    {
        for (int i = 0; i + 1 < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind != LuaTokenKind.Name || t.Text != variable || (i > 0 && (tokens[i - 1].Is(".") || tokens[i - 1].Is(":"))))
                continue;
            if (skip != null && t.Start >= skip.Start && t.Start < skip.End)
                continue;

            var keys = new List<string>();
            int j = i + 1;
            while (j < tokens.Count)
            {
                if (tokens[j].Is(".") && j + 1 < tokens.Count && tokens[j + 1].Kind == LuaTokenKind.Name)
                {
                    keys.Add(tokens[j + 1].Text);
                    j += 2;
                }
                else if (tokens[j].Is("["))
                {
                    int depth = 0, k = j;
                    for (; k < tokens.Count; k++)
                    {
                        if (tokens[k].Is("["))
                            depth++;
                        else if (tokens[k].Is("]") && --depth == 0)
                            break;
                    }
                    var inner = tokens.Skip(j + 1).Take(k - j - 1).ToList();
                    keys.Add(inner.Count == 1 && inner[0].Kind is LuaTokenKind.String or LuaTokenKind.Number
                        ? Convert.ToString(inner[0].Value, CultureInfo.InvariantCulture)!
                        : "[" + source[tokens[j].End..(k < tokens.Count ? tokens[k].Start : source.Length)].Trim() + "]");
                    j = k + 1;
                }
                else
                {
                    break;
                }
            }
            if (keys.Count > 0 && j < tokens.Count && tokens[j].Is("="))
                yield return new(file, string.Join('.', keys), "assignment");
        }
    }

    static IEnumerable<string> LeafPaths(LuaTable t, string prefix) =>
        t.Fields.SelectMany(f => f.Value is LuaTable child
            ? LeafPaths(child, prefix + f.KeyText + ".")
            : [prefix + f.KeyText]);
}
