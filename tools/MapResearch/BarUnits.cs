using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MapCore.Imaging;
using MapCore.Textures;

namespace MapResearch;

/// <summary>
/// The catalog of BAR's standard units that the editor offers for a guardian tower's targets, built from BAR's
/// repository on GitHub at one commit: every unit file outside <c>units/Scavengers</c> and <c>units/other</c>, its
/// name and description from <c>language/en/units.json</c>, what kind of unit it is, its weapons' target categories,
/// and its build-menu picture as a 64-pixel PNG.
/// <para>
/// Pictures are large DDS files (a quarter to over a megabyte each), so only the one mip level that is 64 pixels
/// across is fetched, by ranged requests: the file's start (to find where that level lies), then the level itself.
/// If the server ignores ranges the tool stops rather than download hundreds of megabytes.
/// </para>
/// </summary>
static class BarUnits
{
    const string Repo = "beyond-all-reason/Beyond-All-Reason";
    const int IconSize = 64;
    const int Parallel = 8;

    sealed record Weapon(string Def, string[] Targets);

    sealed record Unit(string Name, string File, string Folder, string Faction, string Group, string DisplayName, string Description,
        bool CanFly, bool Builder, bool Transport, bool Commander, IReadOnlyList<Weapon> Weapons, string? Picture)
    {
        /// <summary>
        /// Has a weapon that can hit something other than aircraft: no target category given means anything, and a
        /// weapon limited to "VTOL" alone is an anti-air weapon.
        /// </summary>
        public bool AttacksSurface => Weapons.Any(w => w.Targets.Length == 0 || w.Targets.Any(t => !t.Equals("VTOL", StringComparison.OrdinalIgnoreCase)));

        /// <summary>What a new guardian kind destroys: aircraft that can attack the ground (bombers, gunships, drones, armed transports).</summary>
        public bool DefaultDestroy => CanFly && AttacksSurface;
    }

    public static int Run(string outDir)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BarMapEditor-research", "1.0"));
        http.Timeout = TimeSpan.FromMinutes(2);

        var commit = JsonNode.Parse(Get(http, $"https://api.github.com/repos/{Repo}/commits/master"))!;
        string sha = commit["sha"]!.GetValue<string>();
        string date = commit["commit"]?["committer"]?["date"]?.GetValue<string>() ?? "";
        Console.WriteLine($"BAR at {sha[..12]} ({date})");
        var tree = JsonNode.Parse(Get(http, $"https://api.github.com/repos/{Repo}/git/trees/{sha}?recursive=1"))!;
        if (tree["truncated"]?.GetValue<bool>() == true)
            throw new InvalidOperationException("GitHub truncated the file list");
        var paths = tree["tree"]!.AsArray().Select(e => e!["path"]!.GetValue<string>()).ToList();
        var pictures = paths.Where(p => p.StartsWith("unitpics/", StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => p.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var unitFiles = paths.Where(p => p.StartsWith("units/", StringComparison.Ordinal) && p.EndsWith(".lua", StringComparison.Ordinal) &&
                                         !p.StartsWith("units/Scavengers/", StringComparison.Ordinal) && !p.StartsWith("units/other/", StringComparison.Ordinal))
            .ToList();
        string Raw(string path) => $"https://raw.githubusercontent.com/{Repo}/{sha}/{Uri.EscapeDataString(path).Replace("%2F", "/")}";

        var language = JsonNode.Parse(Get(http, Raw("language/en/units.json")))!["units"]!;
        string Text(string section, string name) => language[section]?[name]?.GetValue<string>() ?? "";

        var units = new List<Unit>();
        var problems = new List<string>();
        var gate = new SemaphoreSlim(Parallel);
        var parsed = Task.WhenAll(unitFiles.Select(async file =>
        {
            await gate.WaitAsync();
            try
            {
                string text = await http.GetStringAsync(Raw(file));
                var unit = Parse(file, text, Text);
                lock (units)
                    units.Add(unit);
            }
            catch (Exception e) when (e is InvalidDataException or HttpRequestException)
            {
                lock (problems)
                    problems.Add($"{file}: {e.Message}");
            }
            finally
            {
                gate.Release();
            }
        }));
        parsed.Wait();
        units.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        Console.WriteLine($"{units.Count} units read from {unitFiles.Count} files");

        string iconDir = Path.Combine(outDir, "icons");
        Directory.CreateDirectory(iconDir);
        int fetched = 0;
        long bytes = 0;
        bool rangesChecked = false;
        var icons = new Dictionary<string, string>();
        foreach (var batch in units.Chunk(Parallel))
        {
            var work = batch.Select(async unit =>
            {
                if (unit.Picture == null || !pictures.TryGetValue("unitpics/" + unit.Picture.ToLowerInvariant(), out var picturePath))
                {
                    lock (problems)
                        problems.Add($"{unit.Name}: no picture '{unit.Picture}'");
                    return;
                }
                try
                {
                    var (image, size) = await FetchIcon(http, Raw(picturePath), () => rangesChecked);
                    lock (icons)
                    {
                        rangesChecked = true;
                        string iconFile = unit.Name + ".png";
                        PngWriter.WriteRgba(Path.Combine(iconDir, iconFile), image);
                        icons[unit.Name] = "icons/" + iconFile;
                        fetched++;
                        bytes += size;
                    }
                }
                catch (Exception e) when (e is InvalidDataException or HttpRequestException)
                {
                    lock (problems)
                        problems.Add($"{unit.Name}: picture {picturePath}: {e.Message}");
                }
            }).ToList();
            Task.WhenAll(work).Wait();
        }
        Console.WriteLine($"{fetched} pictures, {bytes / 1024.0 / 1024:0.0} MB fetched");

        var catalog = new JsonObject
        {
            ["source"] = new JsonObject { ["repository"] = Repo, ["commit"] = sha, ["date"] = date, ["built"] = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
            ["units"] = new JsonArray(units.Select(u => (JsonNode)new JsonObject
            {
                ["name"] = u.Name,
                ["displayName"] = u.DisplayName,
                ["description"] = u.Description,
                ["faction"] = u.Faction,
                ["group"] = u.Group,
                ["canFly"] = u.CanFly,
                ["builder"] = u.Builder,
                ["transport"] = u.Transport,
                ["commander"] = u.Commander,
                ["attacksSurface"] = u.AttacksSurface,
                ["defaultDestroy"] = u.DefaultDestroy,
                ["weapons"] = new JsonArray(u.Weapons.Select(w => (JsonNode)new JsonObject { ["def"] = w.Def, ["targets"] = string.Join(" ", w.Targets) }).ToArray()),
                ["icon"] = icons.GetValueOrDefault(u.Name),
            }).ToArray()),
        };
        File.WriteAllText(Path.Combine(outDir, "catalog.json"), catalog.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        foreach (var problem in problems.Order())
            Console.WriteLine("  " + problem);
        Console.WriteLine($"wrote {Path.Combine(outDir, "catalog.json")}: {units.Count} units, {units.Count(u => u.DefaultDestroy)} destroyed by default, {problems.Count} problems");
        return 0;
    }

    static string Get(HttpClient http, string url) => http.GetStringAsync(url).GetAwaiter().GetResult();

    /// <summary>The picture's 64-pixel level (or the nearest larger one, scaled down), by two ranged requests.</summary>
    static async Task<(RgbaImage Image, long Bytes)> FetchIcon(HttpClient http, string url, Func<bool> rangesChecked)
    {
        var start = await Range(http, url, 0, 147, mustBePartial: !rangesChecked());
        var whole = DdsFile.LevelSpan(start, 0);
        int mip = 0;
        while (Math.Max(whole.Width >> (mip + 1), 1) >= IconSize)
            mip++;
        (long Offset, int Length, int Width, int Height) span;
        try
        {
            span = DdsFile.LevelSpan(start, mip);
        }
        catch (InvalidDataException)
        {
            mip = 0; // no mip levels: the whole picture, scaled down
            span = whole;
        }
        var level = await Range(http, url, span.Offset, span.Offset + span.Length - 1, mustBePartial: false);
        var image = DdsFile.FromLevel(start, mip, level).Decode();
        if (image.Width != IconSize || image.Height != IconSize)
            image = image.Resample(IconSize, IconSize);
        return (image, start.Length + level.Length);
    }

    static async Task<byte[]> Range(HttpClient http, string url, long from, long to, bool mustBePartial)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(from, to);
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            if (mustBePartial)
                throw new InvalidOperationException($"{url} ignored the range request (status {(int)response.StatusCode}): stopping rather than fetch whole pictures");
            var all = await response.Content.ReadAsByteArrayAsync();
            return all[(int)from..(int)Math.Min(all.Length, to + 1)];
        }
        return await response.Content.ReadAsByteArrayAsync();
    }

    // ---------------------------------------------------------------- reading unit files

    /// <summary>A unit file's unit: the first entry of the table it returns.</summary>
    static Unit Parse(string file, string text, Func<string, string, string> language)
    {
        var root = LuaTables.ParseReturnedTable(text) ?? throw new InvalidDataException("no table returned");
        var (name, unitTable) = root.Keyed.FirstOrDefault(e => e.Value is LuaTables.Table) is { Key: not null } entry
            ? (entry.Key, (LuaTables.Table)entry.Value)
            : throw new InvalidDataException("no unit table");
        name = name.ToLowerInvariant();
        string folder = Path.GetDirectoryName(file)!.Replace('\\', '/')["units".Length..].TrimStart('/');
        // By the name's prefix (BAR keeps a few Legion units in Cortex folders), then by BAR's folder for a name
        // without one of the prefixes (it misspells one, "leegmech").
        string faction = name.StartsWith("arm", StringComparison.Ordinal) ? "Armada"
            : name.StartsWith("cor", StringComparison.Ordinal) ? "Cortex"
            : name.StartsWith("leg", StringComparison.Ordinal) ? "Legion"
            : folder.StartsWith("Arm", StringComparison.Ordinal) ? "Armada"
            : folder.StartsWith("Cor", StringComparison.Ordinal) ? "Cortex"
            : folder.StartsWith("Legion", StringComparison.Ordinal) ? "Legion" : "Other";
        bool Flag(string key) => unitTable.Get(key) is true;
        double Number(string key) => unitTable.Get(key) is double d ? d : 0;
        var weapons = new List<Weapon>();
        if (unitTable.Get("weapons") is LuaTables.Table weaponTable)
            foreach (var value in weaponTable.Positional.Concat(weaponTable.Keyed.Values))
                if (value is LuaTables.Table w)
                    weapons.Add(new Weapon(w.Get("def") as string ?? "?",
                        (w.Get("onlytargetcategory") as string ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        var customParams = unitTable.Get("customparams") as LuaTables.Table;
        bool commander = customParams?.Get("iscommander") is true || folder.Contains("EvoCom", StringComparison.Ordinal) ||
                         name is "armcom" or "corcom" or "legcom";
        return new Unit(name, file, folder, faction, Group(folder, Flag("canfly")), language("names", name), language("descriptions", name),
            Flag("canfly"), Flag("builder") || Number("workertime") > 0, Number("transportcapacity") > 0, commander, weapons,
            unitTable.Get("buildpic") as string);
    }

    /// <summary>The unit's group in the editor's list, from the folder BAR keeps it in.</summary>
    static string Group(string folder, bool canFly)
    {
        string f = folder.ToLowerInvariant();
        if (f.Contains("seaplane"))
            return "Seaplanes";
        if (canFly || f.Contains("aircraft") || f.EndsWith("/air", StringComparison.Ordinal) || f.Contains("t2 air"))
            return "Aircraft";
        if (f.Contains("hover"))
            return "Hovercraft";
        if (f.Contains("ship") || f.Contains("seautility"))
            return "Ships";
        if (f.Contains("bot"))
            return "Bots";
        if (f.Contains("vehicle"))
            return "Vehicles";
        if (f.Contains("gantry") || f.EndsWith("/t3", StringComparison.Ordinal))
            return "Experimental";
        if (f.Contains("evocom") || f.Length == 0)
            return "Commanders and drones";
        if (f.Contains("constructor"))
            return "Constructors";
        return "Buildings";
    }
}

/// <summary>
/// Just enough Lua to read BAR's unit files: the table a file returns, with string, number and boolean values and
/// nested tables; anything else (an expression, a function) is skipped.
/// </summary>
static class LuaTables
{
    public sealed class Table
    {
        public Dictionary<string, object?> Keyed { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<object?> Positional { get; } = [];
        public object? Get(string key) => Keyed.GetValueOrDefault(key);
    }

    enum Kind { Name, String, Number, Symbol, End }

    readonly record struct Token(Kind Kind, string Text);

    public static Table? ParseReturnedTable(string text)
    {
        var tokens = Tokenize(text);
        // The last "return {" at the top level (files may define locals and functions first).
        int depth = 0, at = -1;
        for (int i = 0; i < tokens.Count - 1; i++)
        {
            var t = tokens[i];
            if (t.Kind == Kind.Symbol && t.Text is "{" or "(")
                depth++;
            else if (t.Kind == Kind.Symbol && t.Text is "}" or ")")
                depth--;
            else if (depth == 0 && t is { Kind: Kind.Name, Text: "return" } && tokens[i + 1] is { Kind: Kind.Symbol, Text: "{" })
                at = i + 1;
        }
        if (at < 0)
            return null;
        int position = at;
        return ParseTable(tokens, ref position);
    }

    static Table ParseTable(List<Token> tokens, ref int i)
    {
        var table = new Table();
        i++; // {
        while (i < tokens.Count && !(tokens[i] is { Kind: Kind.Symbol, Text: "}" }))
        {
            if (tokens[i] is { Kind: Kind.Symbol, Text: "," or ";" })
            {
                i++;
                continue;
            }
            string? key = null;
            if (tokens[i].Kind == Kind.Name && i + 1 < tokens.Count && tokens[i + 1] is { Kind: Kind.Symbol, Text: "=" })
            {
                key = tokens[i].Text;
                i += 2;
            }
            else if (tokens[i] is { Kind: Kind.Symbol, Text: "[" } && i + 3 < tokens.Count && tokens[i + 2] is { Kind: Kind.Symbol, Text: "]" } &&
                     tokens[i + 3] is { Kind: Kind.Symbol, Text: "=" })
            {
                key = tokens[i + 1].Text;
                i += 4;
            }
            var value = ParseValue(tokens, ref i);
            if (key != null)
                table.Keyed[key] = value;
            else
                table.Positional.Add(value);
        }
        i++; // }
        return table;
    }

    static object? ParseValue(List<Token> tokens, ref int i)
    {
        var t = tokens[i];
        bool simple = i + 1 < tokens.Count && tokens[i + 1] is { Kind: Kind.Symbol, Text: "," or ";" or "}" };
        if (t is { Kind: Kind.Symbol, Text: "{" })
            return ParseTable(tokens, ref i);
        if (simple)
        {
            i++;
            return t.Kind switch
            {
                Kind.String => t.Text,
                Kind.Number => double.TryParse(t.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null,
                Kind.Name when t.Text == "true" => true,
                Kind.Name when t.Text == "false" => false,
                _ => null,
            };
        }
        // An expression: skip to the end of this field.
        int depth = 0;
        while (i < tokens.Count)
        {
            var s = tokens[i];
            if (s.Kind == Kind.Symbol && s.Text is "{" or "(" or "[")
                depth++;
            else if (s.Kind == Kind.Symbol && s.Text is "}" or ")" or "]")
            {
                if (depth == 0)
                    break;
                depth--;
            }
            else if (depth == 0 && s.Kind == Kind.Symbol && s.Text is "," or ";")
                break;
            i++;
        }
        return null;
    }

    static List<Token> Tokenize(string s)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }
            if (c == '-' && i + 1 < s.Length && s[i + 1] == '-')
            {
                int level = LongBracket(s, i + 2);
                if (level >= 0)
                    i = CloseLongBracket(s, i + 2, level);
                else
                {
                    int end = s.IndexOf('\n', i);
                    i = end < 0 ? s.Length : end + 1;
                }
                continue;
            }
            if (c is '"' or '\'')
            {
                var sb = new StringBuilder();
                int j = i + 1;
                while (j < s.Length && s[j] != c)
                {
                    if (s[j] == '\\' && j + 1 < s.Length)
                    {
                        j++;
                        sb.Append(s[j] switch { 'n' => '\n', 't' => '\t', _ => s[j] });
                    }
                    else
                        sb.Append(s[j]);
                    j++;
                }
                tokens.Add(new Token(Kind.String, sb.ToString()));
                i = j + 1;
                continue;
            }
            if (c == '[' && LongBracket(s, i) is int level2 and >= 0)
            {
                int open = i + level2 + 2, close = CloseLongBracket(s, i, level2);
                tokens.Add(new Token(Kind.String, s[open..Math.Max(open, close - level2 - 2)]));
                i = close;
                continue;
            }
            if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
            {
                int j = i;
                while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '.' || ((s[j] is '+' or '-') && s[j - 1] is 'e' or 'E')))
                    j++;
                tokens.Add(new Token(Kind.Number, s[i..j]));
                i = j;
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int j = i;
                while (j < s.Length && (char.IsLetterOrDigit(s[j]) || s[j] == '_'))
                    j++;
                tokens.Add(new Token(Kind.Name, s[i..j]));
                i = j;
                continue;
            }
            tokens.Add(new Token(Kind.Symbol, c.ToString()));
            i++;
        }
        tokens.Add(new Token(Kind.End, ""));
        return tokens;
    }

    /// <summary>The level of a long bracket ([[, [=[, ...) starting at i, or -1.</summary>
    static int LongBracket(string s, int i)
    {
        if (i >= s.Length || s[i] != '[')
            return -1;
        int j = i + 1, level = 0;
        while (j < s.Length && s[j] == '=')
        {
            level++;
            j++;
        }
        return j < s.Length && s[j] == '[' ? level : -1;
    }

    /// <summary>Just past the long bracket opened at i with the given level.</summary>
    static int CloseLongBracket(string s, int i, int level)
    {
        string close = "]" + new string('=', level) + "]";
        int end = s.IndexOf(close, i + level + 2, StringComparison.Ordinal);
        return end < 0 ? s.Length : end + close.Length;
    }
}
