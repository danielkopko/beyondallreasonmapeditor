using MapCore.Items;
using MapCore.Lua;

namespace MapCore.Tests;

/// <summary>
/// The sandboxed Lua interpreter that runs maps' feature definitions: the language as Lua 5.1 has it, the patterns
/// real maps use to build their features (loops, concatenation, lowerkeys, tables of names), and the limits that keep
/// a broken or hostile file from hanging or crashing the editor.
/// </summary>
static class LuaInterpreterTests
{
    static object?[] Run(string source, LuaLimits? limits = null) => new LuaInterpreter(limits).Run(source);

    static object? One(string source) => Run(source)[0];

    [Test]
    public static void NumbersPrintAsLua51Does()
    {
        Assert.Equal("tree_1", One("return 'tree_' .. 1"), "a whole number has no decimal point");
        Assert.Equal("1.5|0.3|1e+15|-2|99999999999999|3.1415926535898", One(
            "return 1.5 .. '|' .. (0.1 + 0.2) .. '|' .. 1e15 .. '|' .. -2 .. '|' .. 99999999999999 .. '|' .. math.pi"), "%.14g");
        Assert.Equal("1e+14", LuaValuesRuntime.NumberToString(1e14), "at 15 digits Lua switches to an exponent");
        Assert.Equal(16.0, One("return tonumber('0x10')"), "hex text");
        Assert.Equal(11.0, One("return '10' + 1"), "text in arithmetic");
        Assert.Equal("1|2|3.5", One("return 7 % 3 .. '|' .. -7 % 3 .. '|' .. 7 / 2"), "floored modulo");
    }

    [Test]
    public static void TheLanguageWorks()
    {
        Assert.Equal("abc|3|x-y-z|B|lo", One("""
            local s = ("ABC"):lower()
            local t = {}
            table.insert(t, "x"); table.insert(t, "y"); table.insert(t, "z")
            local function join(sep, ...)
              local parts = {...}
              return table.concat(parts, sep)
            end
            return s .. '|' .. #t .. '|' .. join('-', unpack(t)) .. '|' .. string.sub('ABC', -2, -2) .. '|' .. ('hello'):sub(-2)
            """), "strings, tables, varargs");
        Assert.Equal("a1b2c3", One("""
            local order = {}
            local t = { c = 3 }
            t.a = 1
            t.b = 2
            local out = ""
            for k, v in pairs({a = 1, b = 2, c = 3}) do out = out .. k .. v end
            return out
            """), "pairs goes in the order keys were set (the same on every run)");
        Assert.Equal(20.0, One("local i = 3; local a = {}; i, a[i] = i + 1, 20; return a[3]"), "targets are worked out before assigning");
        Assert.Equal("6|ok|boom", One("""
            local function counter()
              local n = 0
              return function() n = n + 1; return n end
            end
            local c = counter(); c(); c()
            local total = 0
            for i = 1, 3 do total = total + i end
            local ok, message = pcall(function() error("boom") end)
            return total .. '|' .. (ok and 'bad' or 'ok') .. '|' .. message
            """), "closures, loops, pcall and error");
        Assert.Equal("3 1 2", One("""
            local t = setmetatable({}, { __index = { x = 1, y = 2 } })
            t.x = 3
            local i = 0
            repeat i = i + 1 until i >= 2
            return t.x .. ' ' .. (i - 1) .. ' ' .. t.y
            """), "__index, repeat-until");
    }

    [Test]
    public static void StringFormatWritesAsCDoes()
    {
        Assert.Equal("rocks30_def_07|rocks30_snow_21", One("return string.format('rocks30_%s_%02d', 'def', 7) .. '|' .. string.format('rocks30_%s_%02d', 'snow', 21)"),
            "BAR's rock names (features/rocks30.lua)");
        Assert.Equal("[   42]|[42   ]|[+42]|[-0042]|[ff]|[0XFF]|[%]|[2]", One("return string.format('[%5d]|[%-5d]|[%+d]|[%05d]|[%x]|[%#X]|[%%]|[%d]', 42, 42, 42, -42, 255, 255, 2.7)"),
            "widths, flags, hexadecimal, and a number truncated as Lua 5.1 does");
        Assert.Equal("3.14|1.234568e+04|0.0001|1e+20|100|2.5", One("return string.format('%.2f|%e|%g|%g|%g|%g', 3.14159, 12345.678, 0.0001, 1e20, 100, 2.5)"),
            "real numbers as C writes them");
        Assert.Equal("tree|abc|   ab|\"say \\\"hi\\\"\"", One("return string.format('%s|%.3s|%5s|%q', 'tree', 'abcdef', 'ab', 'say \"hi\"')"),
            "strings, cut, padded and quoted");
        Assert.Equal("false", One("local ok = pcall(string.format, '%d'); return tostring(ok)"), "a missing value is an error");
    }

    [Test]
    public static void ALoaderCanAddToTheSandbox()
    {
        // BAR's loader gives its feature files table.copy and VFS (tools/MapResearch barfeatures does the same).
        const string source = """
            local base = { description = "Rock", metal = 5 }
            local defs = {}
            for i = 1, 2 do
                local def = table.copy(base)
                def.object = string.format("rocks/rock_%02d.s3o", i)
                defs[string.format("rock_%02d", i)] = def
            end
            if VFS.FileExists("unittextures/moss.dds") then defs.rock_01.description = "Mossy rock" end
            return defs
            """;
        Assert.True(!FeatureDefs.Read("features/rocks.lua", source).Ran, "without them the file can't be run");
        var read = FeatureDefs.Read("features/rocks.lua", source, setup: lua =>
        {
            if (lua.Globals.Get("table") is LuaMap table)
                table.Set("copy", LuaInterpreter.Function("table.copy", a =>
                {
                    var copy = new LuaMap();
                    foreach (var (k, v) in ((LuaMap)a[0]!).Entries)
                        copy.Set(k, v);
                    return [copy];
                }));
            var vfs = new LuaMap();
            vfs.Set("FileExists", LuaInterpreter.Function("VFS.FileExists", a => [a[0] as string == "unittextures/moss.dds"]));
            lua.Globals.Set("VFS", vfs);
        });
        Assert.True(read.Ran && read.Features.Count == 2 && read.Features[0] is { Name: "rock_01", Object: "rocks/rock_01.s3o", Description: "Mossy rock", Metal: 5 } &&
                    read.Features[1] is { Name: "rock_02", Description: "Rock" },
            $"with them it runs ({string.Join(", ", read.Features.Select(f => $"{f.Name} {f.Object} {f.Description}"))})");
    }

    [Test]
    public static void LowerkeysLowersEveryLevel()
    {
        var t = (LuaMap)One("""
            local featureDef = { name = "Agorm_Rock1", footprintX = 2, customParams = { NormalTex = "x.dds" } }
            return lowerkeys({[featureDef.name] = featureDef})
            """)!;
        var def = (LuaMap)t.Get("agorm_rock1")!;
        Assert.True(def.Get("footprintx") is 2.0 && ((LuaMap)def.Get("customparams")!).Get("normaltex") is "x.dds", "keys lower-cased all the way down");
        Assert.Equal("Agorm_Rock1", def.Get("name"), "values left as they are");
    }

    /// <summary>The ways real maps build their features: each must give the names the game sees.</summary>
    [Test]
    public static void FeatureFilesGiveTheNamesTheGameSees()
    {
        // All That Simmers' palms: a nested loop over sizes.
        var palms = FeatureDefs.Read("features/palms.lua", """
            local featureDef = { world = "All Worlds", category = "Vegetation", description = "Palm Tree", energy = 250,
              customparams = { normaltex = "unittextures/palm_normal.tga", category = "Plants" } }
            local palms = {}
            for i = 1, 3 do
              for _, size in pairs({'', '_large'}) do
                local name = 'palm_' .. i .. size
                local def = {}
                for k, v in pairs(featureDef) do def[k] = v end
                def.name = name
                def.object = name .. ".s3o"
                palms[name] = def
              end
            end
            return palms
            """);
        Assert.True(palms.Ran && palms.Problem == null, $"it runs: {palms.Problem}");
        Assert.Equal("palm_1,palm_1_large,palm_2,palm_2_large,palm_3,palm_3_large", string.Join(",", palms.Features.Select(f => f.Name)), "six palms");
        var palm = palms.Features[2];
        Assert.True(palm.Object == "palm_2.s3o" && palm.Description == "Palm Tree" && palm.Energy == 250 && palm.Category == "Vegetation Plants" &&
                    palm.Strings.Contains("unittextures/palm_normal.tga"), "each with its model, description, category and the files it names");

        // Crimson Bay's firs: a table of names, a name field that isn't the name, models in a subfolder.
        var firs = FeatureDefs.Read("features/fir.lua", """
            local list = { "fir_small_1", "fir_tall_2" }
            for i, name in ipairs(list) do
              list[name] = { name = "Fir Tree", object = "fir/" .. name .. ".s3o",
                customparams = { normaltex = "unittextures/" .. "fir" .. "_normal.tga" } }
            end
            return list
            """);
        Assert.Equal("fir_small_1,fir_tall_2", string.Join(",", firs.Features.Select(f => f.Name)), "named by their keys; the list of names isn't features");
        Assert.True(firs.Features[1].Object == "fir/fir_tall_2.s3o" && firs.Features[0].Strings.Contains("unittextures/fir_normal.tga"),
            "built paths come out whole");

        // Ferns: if/elseif inside the loop; models named differently from the features.
        var ferns = FeatureDefs.Read("features/ferns.lua", """
            local ferns = {}
            for i = 1, 3 do
              local def = { object = 'fern' .. i .. '.s3o', description = "Ferns" }
              if i == 1 then def.description = "Ferns and Stones" elseif i == 2 then def.description = "Fern" end
              ferns["cycas" .. i] = def
            end
            return ferns
            """);
        Assert.Equal("cycas1:fern1.s3o:Ferns and Stones,cycas2:fern2.s3o:Fern,cycas3:fern3.s3o:Ferns",
            string.Join(",", ferns.Features.Select(f => $"{f.Name}:{f.Object}:{f.Description}")), "each fern as the loop made it");

        // A plain table, and a single definition with lowerkeys.
        Assert.Equal("pipe_long_1,smothdist", string.Join(",", FeatureDefs.Read("features/p.lua",
            "return { pipe_long_1 = { object = 'p.s3o' }, smothdist = { object = 's.s3o' } }").Features.Select(f => f.Name)), "a returned table");
        Assert.Equal("crateblue", FeatureDefs.Read("features/c.lua", """
            local objectname = "CrateBlue"
            local featureDef = { name = "crateblue", object = "crateblue.s3o" }
            return lowerkeys({[objectname] = featureDef})
            """).Features.Single().Name, "lowerkeys");
    }

    [Test]
    public static void FilesThatCantBeRunAreReadFromTheirText()
    {
        var engine = FeatureDefs.Read("features/odd.lua", """
            local opts = Spring.GetModOptions()
            return { rock_a = { name = "rock_a", object = "rock_a.s3o" } }
            """);
        Assert.True(!engine.Ran && engine.Problem!.Contains("couldn't be run", StringComparison.Ordinal) && engine.Problem.Contains("Spring", StringComparison.Ordinal) &&
                    engine.Features.Single() is { Name: "rock_a", Object: "rock_a.s3o" },
            $"a call to the engine falls back to the names written in the file, and says why: {engine.Problem}");
        var junk = FeatureDefs.Read("features/page.lua", "<!DOCTYPE html><html>");
        Assert.True(junk.Features.Count == 0 && junk.Problem!.StartsWith("isn't Lua", StringComparison.Ordinal), $"a web page isn't Lua: {junk.Problem}");
        Assert.True(FeatureDefs.Read("features/s.lua", "return { size = 3 }").Problem == "defines no features", "a table of settings defines nothing");
    }

    [Test]
    public static void HostileFilesAreStopped()
    {
        var small = new LuaLimits(MaxSteps: 200_000);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var endless = Assert.Throws<LuaLimitException>(() => Run("while true do end", small), "an endless loop");
        Assert.True(endless.Message.Contains("ran too long", StringComparison.Ordinal) && sw.ElapsedMilliseconds < 5000, $"stopped quickly: {endless.Message}");
        Assert.Throws<LuaLimitException>(() => Run("local function f(n) return f(n + 1) + 1 end return f(1)"), "endless recursion");
        Assert.Throws<LuaLimitException>(() => Run("local t = {} for i = 1, 1e9 do t[i] = i end", new LuaLimits(MaxSteps: 50_000_000, MaxTableEntries: 10_000)), "a huge table");
        Assert.Throws<LuaLimitException>(() => Run("local s = 'x' while true do s = s .. s end"), "a huge string");
        Assert.Throws<LuaLimitException>(() => Run("return pcall(function() while true do end end)", small), "pcall doesn't catch a limit");
        Assert.Throws<LuaSyntaxException>(() => Run("return " + new string('(', 5000) + "1" + new string(')', 5000)), "source nested 5000 deep is refused, not a crash");
        Assert.True(Run("return io, os, load, loadstring, require, dofile, debug, setfenv, getfenv").All(v => v == null), "nothing that reaches files or the system is there");
        var fallback = FeatureDefs.Read("features/loop.lua", "while true do end return { a = { name = 'a' } }", small);
        Assert.True(!fallback.Ran && fallback.Features.Single().Name == "a", "a definition file that never ends is read from its text");
    }
}
