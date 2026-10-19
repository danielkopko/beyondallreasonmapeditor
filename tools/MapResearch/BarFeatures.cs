using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MapCore.Assets;
using MapCore.Imaging;
using MapCore.Items;
using MapCore.Lua;
using MapCore.Textures;

namespace MapResearch;

/// <summary>
/// The features BAR defines, and the models of the ones maps use, for the editor to draw them instead of placeholders:
/// read from BAR's repository on GitHub at one commit (the unit catalog's, so the two agree).
/// <list type="bullet">
/// <item>BAR's features/*.lua, run as BAR's loader runs them (gamedata/featuredefs.lua): in MapCore's Lua sandbox, with
/// the functions BAR gives them (lowerkeys, table.copy, VFS.FileExists answered from the repository's file list). Among
/// them enginetrees_override.lua, which makes the engine's treetype0-15 BAR's fir trees.</item>
/// <item>Units' wrecks: each unit file's featuredefs, named unit_key as gamedata/featuredefs_post.lua names them.</item>
/// <item>Units and buildings themselves (the unit catalog's, when given): each unit file's objectname, and whether it
/// moves (a building has no speed and doesn't fly). A map places them through its featureplacer, units and buildings
/// in separate lists.</item>
/// </list>
/// Each chosen model is fetched as it is, then the textures its header names, from unittextures/. Large DDS textures
/// (wreck atlases are 22 MB) are cut to one mip level no larger than <see cref="MaxTexture"/>, fetched by ranged
/// requests; TGA and PNG textures are scaled down to that too and stored as DDS (the editor looks for a texture under
/// any image extension). Features whose model BAR doesn't have are listed with a note: BAR drops them too.
/// </summary>
static class BarFeatures
{
    const string Repo = "beyond-all-reason/Beyond-All-Reason";
    const int MaxTexture = 512;

    /// <summary>A texture many models share (a faction's atlas, which a whole unit only takes a corner of) is kept larger.</summary>
    const int MaxSharedTexture = 1024, SharedBy = 8;
    const int Parallel = 8;

    sealed record Feature(string Name, string? Object, string? Description, string? Category, double Metal, double Energy,
        string File, string? Author, string? FromUnit)
    {
        public string? Path { get; set; }   // the model's path in BAR, when BAR has it
        public bool Shipped { get; set; }
        public string? Note { get; set; }
    }

    sealed record Unit(string Name, string? Object, bool Building, string File, string? Author)
    {
        public string? Path { get; set; }
        public bool Shipped { get; set; }
    }

    /// <param name="wrecks">"none", "all", or a file of feature names (one a line): the wrecks whose models are shipped.</param>
    /// <param name="unitCatalog">The editor's unit catalog (BarUnits/catalog.json): those units' models are shipped too; null for none.</param>
    public static int Run(string outDir, string commit, string wrecks, string? unitCatalog = null)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BarMapEditor-research", "1.0"));
        http.Timeout = TimeSpan.FromMinutes(2);

        var info = JsonNode.Parse(Get(http, $"https://api.github.com/repos/{Repo}/commits/{commit}"))!;
        string sha = info["sha"]!.GetValue<string>();
        string date = info["commit"]?["committer"]?["date"]?.GetValue<string>() ?? "";
        Console.WriteLine($"BAR at {sha[..12]} ({date})");
        var tree = JsonNode.Parse(Get(http, $"https://api.github.com/repos/{Repo}/git/trees/{sha}?recursive=1"))!;
        if (tree["truncated"]?.GetValue<bool>() == true)
            throw new InvalidOperationException("GitHub truncated the file list");
        var files = tree["tree"]!.AsArray().Where(e => e!["type"]!.GetValue<string>() == "blob")
            .ToDictionary(e => e!["path"]!.GetValue<string>(), e => e!["size"]?.GetValue<long>() ?? 0);
        var byLower = files.Keys.GroupBy(p => p.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        string? InBar(string path) => byLower.GetValueOrDefault(path.Replace('\\', '/').TrimStart('/').ToLowerInvariant());
        string Raw(string path) => $"https://raw.githubusercontent.com/{Repo}/{sha}/{Uri.EscapeDataString(path).Replace("%2F", "/")}";
        var problems = new List<string>();

        // ---- BAR's own feature files
        var features = new List<Feature>();
        foreach (var file in files.Keys.Where(p => p.StartsWith("features/", StringComparison.Ordinal) && p.EndsWith(".lua", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            var read = FeatureDefs.Read(file, Get(http, Raw(file)), setup: lua => BarEnvironment(lua, path => InBar(path) != null));
            if (read.Problem != null)
                problems.Add($"{file}: {read.Problem}");
            foreach (var def in read.Features)
            {
                var custom = def.Table?.GetIgnoreCase("customparams") as LuaMap;
                string? author = (custom?.GetIgnoreCase("model_author") ?? custom?.GetIgnoreCase("author")) as string;
                features.Add(new Feature(def.Name, def.Object, def.Description, def.Category, def.Metal, def.Energy, file, author, null));
            }
            Console.WriteLine($"{file}: {read.Features.Count} features{(read.Ran ? "" : " (read from its text)")}");
        }

        // ---- units, and their wrecks
        var unitFiles = files.Keys.Where(p => p.StartsWith("units/", StringComparison.Ordinal) && p.EndsWith(".lua", StringComparison.Ordinal)).ToList();
        var wreckList = new List<Feature>();
        var unitList = new List<Unit>();
        var gate = new SemaphoreSlim(Parallel);
        Task.WhenAll(unitFiles.Select(async file =>
        {
            await gate.WaitAsync();
            try
            {
                var (found, units) = ReadUnitFile(file, await http.GetStringAsync(Raw(file)));
                lock (wreckList)
                {
                    wreckList.AddRange(found);
                    unitList.AddRange(units);
                }
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
        })).Wait();
        Console.WriteLine($"{unitList.Count} units, {wreckList.Count} wrecks and heaps from {unitFiles.Count} unit files");
        var known = features.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        features.AddRange(wreckList.Where(w => known.Add(w.Name)));
        features.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        // ---- which models to ship
        HashSet<string>? wreckNames = wrecks switch
        {
            "none" => [],
            "all" => null,
            _ => File.ReadAllLines(wrecks).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToHashSet(StringComparer.OrdinalIgnoreCase),
        };
        foreach (var f in features)
        {
            f.Path = f.Object == null ? null : ModelPaths.Find(f.Object, InBar);
            if (f.Object != null && f.Path == null)
                f.Note = $"BAR doesn't have its model ({f.Object}), and drops it as it loads";
        }
        var wanted = features.Where(f => f.Path != null && (f.FromUnit == null || wreckNames == null || wreckNames.Contains(f.Name))).ToList();
        if (wreckNames != null)
            foreach (var name in wreckNames.Where(n => features.All(f => !f.Name.Equals(n, StringComparison.OrdinalIgnoreCase))))
                problems.Add($"{name}: asked for, but BAR defines no such feature");

        // The catalog's units (Armada, Cortex, Legion), with what it says of them.
        var catalogUnits = unitCatalog == null ? []
            : JsonNode.Parse(File.ReadAllText(unitCatalog))!["units"]!.AsArray().Select(u => u!).ToDictionary(u => u["name"]!.GetValue<string>(), StringComparer.OrdinalIgnoreCase);
        var units = unitList.Where(u => catalogUnits.ContainsKey(u.Name)).GroupBy(u => u.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .OrderBy(u => u.Name, StringComparer.Ordinal).ToList();
        foreach (var name in catalogUnits.Keys.Where(n => units.All(u => !u.Name.Equals(n, StringComparison.OrdinalIgnoreCase))))
            problems.Add($"{name}: in the unit catalog, but no unit file defines it");
        foreach (var u in units)
        {
            u.Path = u.Object == null ? null : ModelPaths.Find(u.Object, InBar);
            if (u.Path == null)
                problems.Add($"{u.Name}: its model ({u.Object ?? "none named"}) isn't in BAR");
        }

        // ---- models, then the textures they name
        Directory.CreateDirectory(outDir);
        var models = wanted.Select(f => f.Path!).Concat(units.Where(u => u.Path != null).Select(u => u.Path!))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();
        var textureNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // and how many models use each
        long modelBytes = 0;
        var shipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var batch in models.Chunk(Parallel))
        {
            Task.WhenAll(batch.Select(async path =>
            {
                try
                {
                    var data = await http.GetByteArrayAsync(Raw(path));
                    var names = MapCore.Assets.S3oModel.ReadTextures(data) ?? throw new InvalidDataException("not an S3O model");
                    Write(outDir, path, data);
                    lock (shipped)
                    {
                        shipped.Add(path);
                        modelBytes += data.Length;
                        foreach (var t in new[] { names.Texture1, names.Texture2 })
                            if (!string.IsNullOrEmpty(t))
                                textureNames[t] = textureNames.GetValueOrDefault(t) + 1;
                    }
                }
                catch (Exception e) when (e is InvalidDataException or HttpRequestException)
                {
                    lock (problems)
                        problems.Add($"{path}: {e.Message}");
                }
            })).Wait();
        }
        foreach (var f in wanted)
            f.Shipped = shipped.Contains(f.Path!);
        foreach (var u in units)
            u.Shipped = u.Path != null && shipped.Contains(u.Path);
        Console.WriteLine($"{shipped.Count} models, {modelBytes / 1024.0 / 1024:0.0} MB");

        long textureBytes = 0;
        foreach (var (name, users) in textureNames.OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase))
        {
            var path = ModelPreview.TexturePath(name, InBar);
            if (path == null)
            {
                problems.Add($"texture {name}: not in BAR's unittextures/");
                continue;
            }
            try
            {
                textureBytes += FetchTexture(http, Raw(path), path, files[path], outDir, users >= SharedBy ? MaxSharedTexture : MaxTexture);
            }
            catch (Exception e) when (e is InvalidDataException or HttpRequestException or NotSupportedException)
            {
                problems.Add($"texture {path}: {e.Message}");
            }
        }
        Console.WriteLine($"{textureNames.Count} textures, {textureBytes / 1024.0 / 1024:0.0} MB");

        // ---- the catalog
        var catalog = new JsonObject
        {
            ["source"] = new JsonObject { ["repository"] = Repo, ["commit"] = sha, ["date"] = date, ["built"] = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
            ["features"] = new JsonArray(features.Select(f => (JsonNode)new JsonObject
            {
                ["name"] = f.Name,
                ["object"] = f.Path ?? f.Object,
                ["model"] = f.Shipped ? f.Path : null,
                ["description"] = f.Description,
                ["category"] = f.Category,
                ["metal"] = f.Metal,
                ["energy"] = f.Energy,
                ["file"] = f.File,
                ["author"] = f.Author,
                ["fromUnit"] = f.FromUnit,
                ["note"] = f.Note,
            }).ToArray()),
            ["units"] = new JsonArray(units.Select(u => (JsonNode)new JsonObject
            {
                ["name"] = u.Name,
                ["displayName"] = catalogUnits[u.Name]["displayName"]?.GetValue<string>(),
                ["description"] = catalogUnits[u.Name]["description"]?.GetValue<string>(),
                ["faction"] = catalogUnits[u.Name]["faction"]?.GetValue<string>(),
                ["building"] = u.Building,
                ["object"] = u.Path ?? u.Object,
                ["model"] = u.Shipped ? u.Path : null,
                ["file"] = u.File,
                ["author"] = u.Author,
            }).ToArray()),
        };
        File.WriteAllText(Path.Combine(outDir, BarFeatureCatalog.FileName), catalog.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        var credits = new StringBuilder();
        credits.AppendLine("BAR's own unit, building and feature models and their textures, shipped so the editor can show them (textures cut to one smaller mip level).");
        credits.AppendLine($"From {Repo} at {sha} ({date}): https://github.com/{Repo}/tree/{sha}");
        credits.AppendLine("They are their authors' work, under the licences BAR's repository gives them; see the repository for those.");
        credits.AppendLine();
        credits.AppendLine("Authors, as BAR's definitions credit them (features shipped here):");
        foreach (var group in wanted.Where(f => f.Shipped).GroupBy(f => f.FromUnit != null ? $"{f.Author ?? "not credited"} (unit wrecks)" : f.Author ?? "not credited")
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            credits.AppendLine($"  {group.Key}: {group.Count()} ({string.Join(", ", group.Select(f => f.Name).Take(4))}{(group.Count() > 4 ? ", ..." : "")})");
        credits.AppendLine();
        credits.AppendLine("Units and buildings shipped here, by their models' authors as BAR's unit files credit them:");
        foreach (var group in units.Where(u => u.Shipped).GroupBy(u => u.Author ?? "not credited").OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            credits.AppendLine($"  {group.Key}: {group.Count()} ({string.Join(", ", group.Select(u => u.Name).Take(4))}{(group.Count() > 4 ? ", ..." : "")})");
        credits.AppendLine();
        credits.AppendLine("The fir trees BAR draws for the engine's treetype0-15 are credited to Beherith and 0 A.D. (Wildfire Games), whose art is published under CC BY-SA 3.0 (https://creativecommons.org/licenses/by-sa/3.0/).");
        File.WriteAllText(Path.Combine(outDir, "CREDITS.txt"), credits.ToString(), new UTF8Encoding(false));
        foreach (var problem in problems.Order(StringComparer.Ordinal))
            Console.WriteLine("  " + problem);
        Console.WriteLine($"wrote {outDir}: {features.Count} features, {wanted.Count(f => f.Shipped)} with their models; " +
                          $"{units.Count} units ({units.Count(u => u.Building)} buildings), {units.Count(u => u.Shipped)} with their models " +
                          $"({(modelBytes + textureBytes) / 1024.0 / 1024:0.0} MB), {problems.Count} problems");
        return 0;
    }

    /// <summary>
    /// What BAR's loader gives a feature file beyond the sandbox (which has lowerkeys already): table.copy (BAR's
    /// table functions: a deep copy), and VFS.FileExists, answered from BAR's own files.
    /// </summary>
    static void BarEnvironment(LuaInterpreter lua, Func<string, bool> exists)
    {
        var g = lua.Globals;
        if (g.Get("table") is LuaMap table)
            table.Set("copy", LuaInterpreter.Function("table.copy", a => [a.Length > 0 && a[0] is LuaMap t ? Copy(t, new()) : a.FirstOrDefault()]));
        var vfs = new LuaMap();
        vfs.Set("FileExists", LuaInterpreter.Function("VFS.FileExists", a => [a.Length > 0 && a[0] is string p && exists(p)]));
        g.Set("VFS", vfs);
    }

    static LuaMap Copy(LuaMap t, Dictionary<LuaMap, LuaMap> done)
    {
        if (done.TryGetValue(t, out var already))
            return already;
        var copy = new LuaMap();
        done[t] = copy;
        foreach (var (key, value) in t.Entries)
            copy.Set(key, value is LuaMap nested ? Copy(nested, done) : value);
        return copy;
    }

    /// <summary>
    /// A unit file's units (each with its model and whether it is a building) and their wrecks and heaps: its
    /// featuredefs, each named unit_key (gamedata/featuredefs_post.lua).
    /// </summary>
    static (List<Feature> Wrecks, List<Unit> Units) ReadUnitFile(string file, string text)
    {
        var root = LuaTables.ParseReturnedTable(text) ?? throw new InvalidDataException("no table returned");
        var wrecks = new List<Feature>();
        var units = new List<Unit>();
        foreach (var (unitName, value) in root.Keyed)
        {
            if (value is not LuaTables.Table unit)
                continue;
            var unitAuthor = (unit.Get("customparams") as LuaTables.Table)?.Get("model_author") as string;
            double UnitNumber(string k) => unit.Get(k) switch
            {
                double d => d,
                string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) => v,
                _ => 0,
            };
            // A building: it doesn't fly and has no speed. (BAR gives its nano turrets a movement class, NANO, so the
            // engine pathfinds for them, but they never move: a player sees them as buildings.)
            bool moves = unit.Get("canfly") is true || UnitNumber("speed") > 0 || UnitNumber("maxvelocity") > 0;
            units.Add(new Unit(unitName.ToLowerInvariant(), unit.Get("objectname") as string, !moves, file, unitAuthor));
            if (unit.Get("featuredefs") is not LuaTables.Table defs)
                continue;
            foreach (var (key, defValue) in defs.Keyed)
            {
                if (defValue is not LuaTables.Table def)
                    continue;
                double Number(string k) => def.Get(k) switch
                {
                    double d => d,
                    string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) => v,
                    _ => 0,
                };
                wrecks.Add(new Feature($"{unitName.ToLowerInvariant()}_{key.ToLowerInvariant()}", def.Get("object") as string, def.Get("description") as string,
                    def.Get("category") as string, Number("metal"), Number("energy"), file, unitAuthor, unitName.ToLowerInvariant()));
            }
        }
        return (wrecks, units);
    }

    static void Write(string outDir, string path, byte[] data)
    {
        var target = Path.Combine(outDir, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, data);
    }

    /// <summary>
    /// One texture into the output: a DDS as its largest mip level no bigger than <see cref="MaxTexture"/> (a one-level
    /// DDS of BAR's own bytes, by ranged requests); any other image whole, or scaled down to a PNG when larger.
    /// Returns the bytes written.
    /// </summary>
    static long FetchTexture(HttpClient http, string url, string path, long size, string outDir, int maxSize)
    {
        if (path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
        {
            var start = Range(http, url, 0, Math.Min(size, 148) - 1);
            var whole = DdsFile.LevelSpan(start, 0);
            int mip = 0;
            while (Math.Max(whole.Width >> mip, whole.Height >> mip) > maxSize)
                mip++;
            DdsFile dds;
            try
            {
                var span = DdsFile.LevelSpan(start, mip);
                dds = DdsFile.FromLevel(start, mip, Range(http, url, span.Offset, span.Offset + span.Length - 1));
            }
            catch (InvalidDataException) when (mip > 0)
            {
                // No such level (a file without mipmaps): the whole picture, scaled down.
                var full = DdsFile.Load(http.GetByteArrayAsync(url).GetAwaiter().GetResult()).Decode();
                dds = DdsFile.Create(DdsFormat.Dxt5, Fit(full, maxSize), mipmaps: false);
            }
            var bytes = dds.ToBytes();
            Write(outDir, path, bytes);
            return bytes.Length;
        }
        // A TGA or PNG: decoded (top row first, as the preview reads every format) and stored as a DDS beside where it
        // was, DXT1 when it is opaque throughout: a quarter or an eighth of the size. The preview finds it by its
        // other extension.
        var image = Fit(ModelPreviewRead(path, http.GetByteArrayAsync(url).GetAwaiter().GetResult()), maxSize);
        bool opaque = true;
        for (int i = 3; i < image.Pixels.Length && opaque; i += 4)
            opaque = image.Pixels[i] == 255;
        var encoded = DdsFile.Create(opaque ? DdsFormat.Dxt1 : DdsFormat.Dxt5, image, mipmaps: false).ToBytes();
        Write(outDir, Path.ChangeExtension(path, ".dds"), encoded);
        return encoded.Length;
    }

    static RgbaImage ModelPreviewRead(string path, byte[] data) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".tga" => TgaReader.Read(data),
        ".png" => PngReader.Read(data).ToRgba(),
        var other => throw new NotSupportedException($"{other} textures aren't read"),
    };

    static RgbaImage Fit(RgbaImage image, int maxSize)
    {
        double scale = Math.Min(1.0, (double)maxSize / Math.Max(image.Width, image.Height));
        return scale >= 1 ? image : image.Resample(Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
    }

    static string Get(HttpClient http, string url) => http.GetStringAsync(url).GetAwaiter().GetResult();

    static byte[] Range(HttpClient http, string url, long from, long to)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(from, to);
        using var response = http.SendAsync(request).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        var data = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        if (response.StatusCode == HttpStatusCode.PartialContent)
            return data;
        if (data.Length > 64L * 1024 * 1024)
            throw new InvalidOperationException($"{url} ignored the range request: stopping rather than fetch whole files");
        return data[(int)from..(int)Math.Min(data.Length, to + 1)];
    }
}
