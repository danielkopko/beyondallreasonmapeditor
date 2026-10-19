using System.Globalization;
using MapCore.Lua;

namespace MapCore.Items;

/// <summary>One feature a map's definition file makes.</summary>
/// <param name="Name">Its name as the engine knows it: its key in the table the file returns, lower-cased.</param>
/// <param name="Object">The model it names, as written (the engine looks under objects3d/; see <see cref="ModelPaths"/>).</param>
/// <param name="Category">The definition's category, with customparams' category and set: what it says it is.</param>
/// <param name="Dead">The features it turns into (featuredead).</param>
/// <param name="Strings">Every text value in the definition, for finding the files it names (normal maps and the like).</param>
/// <param name="Table">The definition as the file built it; null when the file couldn't be run (its names were read from its text).</param>
public sealed record FeatureDef(string Name, string? Object, string? Description, string? Category, double Metal, double Energy,
    IReadOnlyList<string> Dead, IReadOnlyList<string> Strings, LuaMap? Table = null);

/// <summary>A features/**/*.lua file and the features it defines.</summary>
/// <param name="Entry">Its path in the package ("features/trees.lua").</param>
/// <param name="Ran">Whether the file was run; false when it couldn't be and its names were read from its text instead.</param>
/// <param name="Problem">Why it couldn't be run (or read), if so.</param>
public sealed record FeatureDefFile(string Entry, IReadOnlyList<FeatureDef> Features, bool Ran, string? Problem);

/// <summary>
/// The features a map package defines, as the game's loader (gamedata/featuredefs.lua) finds them: every .lua under
/// features/, subfolders included, is run and each table it returns under a string key is one feature, named by
/// that key (lower-cased; a <c>name</c> field inside doesn't change it). Map files build their features with loops
/// and concatenation (<c>"ad0_senegal_" .. i</c>), so they are run in <see cref="LuaInterpreter"/>'s sandbox. A
/// file that can't be run there (it calls something the sandbox doesn't have) falls back to reading the names
/// written in its text, and says so.
/// </summary>
public static class FeatureDefs
{
    public static IReadOnlyList<FeatureDefFile> Load(string packageRoot)
    {
        var folder = Path.Combine(packageRoot, "features");
        if (!Directory.Exists(folder))
            return [];
        return Directory.EnumerateFiles(folder, "*", FileSystemUtil.AllEntries)
            .Where(f => f.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            .Select(f => FileSystemUtil.EntryPath(packageRoot, f))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(entry => Read(entry, File.ReadAllText(Path.Combine(packageRoot, entry.Replace('/', Path.DirectorySeparatorChar)))))
            .ToList();
    }

    /// <summary>
    /// The features one definition file makes. <paramref name="setup"/>, when given, adds to the sandbox before the
    /// file runs (the functions BAR's own loader gives its feature files, say).
    /// </summary>
    public static FeatureDefFile Read(string entry, string source, LuaLimits? limits = null, Action<LuaInterpreter>? setup = null)
    {
        try
        {
            var lua = new LuaInterpreter(limits);
            setup?.Invoke(lua);
            var results = lua.Run(source, entry);
            if (results.Length == 0 || results[0] is not LuaMap table)
                return new FeatureDefFile(entry, [], true, "returns no table of features");
            var features = table.Entries
                .Where(e => e.Key is string && e.Value is LuaMap)
                .Select(e => FromTable(((string)e.Key).ToLowerInvariant(), (LuaMap)e.Value))
                .GroupBy(f => f.Name, StringComparer.Ordinal).Select(g => g.Last()) // keys differing only in case: the engine keeps one
                .OrderBy(f => f.Name, StringComparer.Ordinal)
                .ToList();
            return new FeatureDefFile(entry, features, true, features.Count == 0 ? "defines no features" : null);
        }
        catch (LuaSyntaxException ex)
        {
            return new FeatureDefFile(entry, [], false, $"isn't Lua ({ex.Message})");
        }
        catch (LuaRuntimeException ex)
        {
            return FromText(entry, source, $"couldn't be run ({ex.Message}); its names were read from its text");
        }
    }

    static FeatureDef FromTable(string name, LuaMap def)
    {
        string? Text(LuaMap? t, string key) => t?.GetIgnoreCase(key) switch
        {
            string s => s,
            double d => LuaValuesRuntime.NumberToString(d),
            _ => null,
        };
        double Number(string key) => LuaValuesRuntime.ToNumber(def.GetIgnoreCase(key)) ?? 0;
        var custom = def.GetIgnoreCase("customparams") as LuaMap;
        var category = string.Join(" ", new[] { Text(def, "category"), Text(custom, "category"), Text(custom, "set") }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var dead = Text(def, "featuredead");
        var strings = new List<string>();
        CollectStrings(def, strings, 0, []);
        return new FeatureDef(name, Text(def, "object"), Text(def, "description"), category.Length == 0 ? null : category,
            Number("metal"), Number("energy"), dead == null ? [] : [dead.ToLowerInvariant()], strings, def);
    }

    static void CollectStrings(LuaMap table, List<string> into, int depth, HashSet<LuaMap> seen)
    {
        if (depth > 8 || !seen.Add(table))
            return;
        foreach (var (_, value) in table.Entries)
        {
            if (value is string s)
                into.Add(s);
            else if (value is LuaMap nested)
                CollectStrings(nested, into, depth + 1, seen);
        }
    }

    /// <summary>
    /// The features a file names in its text (<c>name = "…"</c>, with the <c>object</c>, <c>featuredead</c> and
    /// numbers written alongside): for files that can't be run. It can miss names built by code.
    /// </summary>
    static FeatureDefFile FromText(string entry, string source, string problem)
    {
        List<LuaToken> tokens;
        try
        {
            tokens = LuaLexer.Tokenize(source);
        }
        catch (LuaSyntaxException ex)
        {
            return new FeatureDefFile(entry, [], false, $"isn't Lua ({ex.Message})");
        }
        var names = Assigned(tokens, "name");
        var objects = Assigned(tokens, "object");
        var dead = Assigned(tokens, "featuredead").Select(d => d.ToLowerInvariant()).ToList();
        var strings = tokens.Where(t => t.Kind == LuaTokenKind.String).Select(t => (string)t.Value!).ToList();
        var category = Assigned(tokens, "category").FirstOrDefault();
        var description = Assigned(tokens, "description").FirstOrDefault();
        var features = names.Select((n, i) => new FeatureDef(n.ToLowerInvariant(), i < objects.Count ? objects[i] : null, description, category,
                FirstNumber(tokens, "metal"), FirstNumber(tokens, "energy"), dead, strings))
            .GroupBy(f => f.Name, StringComparer.Ordinal).Select(g => g.First())
            .OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
        return new FeatureDefFile(entry, features, false, problem);
    }

    /// <summary>Values of every <c>key = "text"</c> in the file, in order.</summary>
    static List<string> Assigned(List<LuaToken> tokens, string key)
    {
        var values = new List<string>();
        for (int i = 0; i + 2 < tokens.Count; i++)
            if (tokens[i].Kind == LuaTokenKind.Name && tokens[i].Text.Equals(key, StringComparison.OrdinalIgnoreCase) &&
                tokens[i + 1].Is("=") && tokens[i + 2].Kind == LuaTokenKind.String)
                values.Add((string)tokens[i + 2].Value!);
        return values;
    }

    /// <summary>The first <c>key = number</c> (or a number written as text) in the file; 0 if none.</summary>
    static double FirstNumber(List<LuaToken> tokens, string key)
    {
        for (int i = 0; i + 2 < tokens.Count; i++)
            if (tokens[i].Kind == LuaTokenKind.Name && tokens[i].Text.Equals(key, StringComparison.OrdinalIgnoreCase) && tokens[i + 1].Is("="))
                return tokens[i + 2].Value switch
                {
                    double d => d,
                    string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) => v,
                    _ => 0,
                };
        return 0;
    }
}

/// <summary>Where the engine finds a model a definition names, and a quick case-insensitive lookup of a package's files.</summary>
public static class ModelPaths
{
    static readonly string[] Extensions = [".s3o", ".3do", ".dae", ".obj"];

    /// <summary>
    /// The package entry of the model <paramref name="obj"/> names, or null: the engine looks under objects3d/ (unless
    /// the name already starts there), with the name as written and then with each model extension added.
    /// </summary>
    public static string? Find(string obj, Func<string, string?> resolve)
    {
        var o = obj.Replace('\\', '/').TrimStart('/');
        if (o.Length == 0)
            return null;
        var path = o.StartsWith("objects3d/", StringComparison.OrdinalIgnoreCase) ? o : "objects3d/" + o;
        return resolve(path) ?? Extensions.Select(e => resolve(path + e)).FirstOrDefault(p => p != null);
    }

    /// <summary>Looks entry paths up in a package folder, ignoring case ('/' or '\' separators); null when absent.</summary>
    public static Func<string, string?> Resolver(string packageRoot)
    {
        var files = Directory.Exists(packageRoot)
            ? Directory.EnumerateFiles(packageRoot, "*", FileSystemUtil.AllEntries)
                .Select(f => FileSystemUtil.EntryPath(packageRoot, f))
                .GroupBy(e => e, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
            : [];
        return path => files.TryGetValue(path.Replace('\\', '/').TrimStart('/'), out var entry) ? entry : null;
    }
}
