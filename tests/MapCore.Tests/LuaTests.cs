using MapCore.Lua;

namespace MapCore.Tests;

static class LuaTests
{
    [Test]
    public static void LexerHandlesStringsNumbersAndComments()
    {
        const string source = """
            -- line comment with "quotes" and { braces }
            --[[ block comment
                 spanning lines ]]
            --[==[ level-2 comment with ]] inside ]==]
            a = "esc\"aped\\ \n \65\066 \x41"
            b = 'single'
            c = [[long
            string]]
            d = { 0x1F, 1.5e2, .25, 3., 0x.8p1, -7 }
            e = a..b ~= c
            """;
        var tokens = LuaLexer.Tokenize(source);
        var strings = tokens.Where(t => t.Kind == LuaTokenKind.String).Select(t => (string)t.Value!).ToList();
        Assert.Equal("esc\"aped\\ \n AB A", strings[0], "escapes in a double-quoted string");
        Assert.Equal("single", strings[1], "single-quoted string");
        Assert.Equal("long\nstring", strings[2], "long string (newline normalised by the test literal)");
        var numbers = tokens.Where(t => t.Kind == LuaTokenKind.Number).Select(t => (double)t.Value!).ToList();
        Assert.Equal("31,150,0.25,3,1,7", string.Join(",", numbers), "number formats");
        Assert.True(tokens.Any(t => t.Is("..")) && tokens.Any(t => t.Is("~=")), "multi-character symbols");
        Assert.True(!tokens.Any(t => t.Text.Contains("comment")), "comments are skipped");
    }

    [Test]
    public static void LexerReportsTheLineOfAnError()
    {
        var ex = Assert.Throws<LuaSyntaxException>(() => LuaLexer.Tokenize("a = 1\nb = \"unfinished\nc = 3"), "unfinished string");
        Assert.Equal(2, ex.Line, "error line");
    }

    [Test]
    public static void ParserReadsFieldsAndKeepsSourcePositions()
    {
        const string source = """
            t = {
                name = "x",   -- trailing comment
                [0] = { startPos = { x = 1068, z = 5026 } },
                ["quoted key"] = true,
                colour = {0.62, 0.70, -0.18,},
                expr = 1 / 3,
                call = f(1, 2),
                "first positional"; "second positional",
                name = "last wins",
            }
            """;
        var tokens = LuaLexer.Tokenize(source);
        int index = 2;
        var table = LuaTableParser.ParseTable(source, tokens, ref index);

        Assert.Equal("last wins", (table["NAME"] as LuaString)?.Value, "case-insensitive key, last duplicate wins");
        var team = table["0"] as LuaTable;
        Assert.Equal(5026.0, ((team?["startPos"] as LuaTable)?["z"] as LuaNumber)?.Value, "numeric key and nesting");
        Assert.True(table["quoted key"] is LuaBoolean { Value: true }, "string key");
        var colour = (LuaTable)table["colour"]!;
        Assert.Equal("0.62|0.7|-0.18", string.Join("|", colour.Fields.Select(f => ((LuaNumber)f.Value).Value)), "positional numbers incl. negative");
        Assert.Equal("1 / 3", (table["expr"] as LuaExpression)?.Text, "expression kept as text");
        Assert.Equal("f(1, 2)", (table["call"] as LuaExpression)?.Text, "call kept whole despite inner commas");
        Assert.Equal("second positional", (table["2"] as LuaString)?.Value, "positional fields count from 1");

        var x = ((LuaTable)((LuaTable)table["0"]!)["startPos"]!)["x"]!;
        Assert.Equal("1068", source.Substring(x.Start, x.Length), "value position points at its source text");
    }
}
