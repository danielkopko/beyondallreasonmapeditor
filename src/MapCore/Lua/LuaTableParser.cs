namespace MapCore.Lua;

/// <summary>
/// Parses table constructors. Literal values (numbers, strings, booleans, nil) and nested tables are
/// understood; anything else is kept as an expression with its source text.
/// </summary>
public static class LuaTableParser
{
    /// <summary>Parses the table constructor whose '{' is at <paramref name="index"/>; leaves index after its '}'.</summary>
    public static LuaTable ParseTable(string source, IReadOnlyList<LuaToken> tokens, ref int index)
    {
        var open = Expect(source, tokens, index++, "{");
        var fields = new List<LuaField>();
        int position = 0;
        while (true)
        {
            if (index >= tokens.Count)
                throw new LuaSyntaxException("table is not closed with '}'", LuaLexer.LineOf(source, open.Start));
            if (tokens[index].Is("}"))
                break;

            var first = tokens[index];
            if (first.Is("["))
            {
                index++;
                var key = ParseValue(source, tokens, ref index, "]");
                Expect(source, tokens, index++, "]");
                Expect(source, tokens, index++, "=");
                fields.Add(new(null, key, null, ParseValue(source, tokens, ref index), first.Start));
            }
            else if (first.Kind == LuaTokenKind.Name && index + 1 < tokens.Count && tokens[index + 1].Is("="))
            {
                index += 2;
                fields.Add(new(first.Text, null, null, ParseValue(source, tokens, ref index), first.Start));
            }
            else
            {
                fields.Add(new(null, null, ++position, ParseValue(source, tokens, ref index), first.Start));
            }

            if (index < tokens.Count && (tokens[index].Is(",") || tokens[index].Is(";")))
                index++;
            else if (index < tokens.Count && !tokens[index].Is("}"))
                throw new LuaSyntaxException($"expected ',' or '}}' but found '{tokens[index].Text}'", LuaLexer.LineOf(source, tokens[index].Start));
        }
        var close = tokens[index++];
        return new LuaTable(fields, open.Start, close.End - open.Start);
    }

    /// <summary>A value up to the next ',' ';' '}' (or <paramref name="stopAt"/>) outside brackets.</summary>
    static LuaValue ParseValue(string source, IReadOnlyList<LuaToken> tokens, ref int index, string? stopAt = null)
    {
        if (index < tokens.Count && tokens[index].Is("{") && IsValueEnd(tokens, FindTableEnd(tokens, index), stopAt))
            return ParseTable(source, tokens, ref index);

        int start = index, depth = 0;
        while (index < tokens.Count)
        {
            var t = tokens[index];
            if (depth == 0 && (t.Is(",") || t.Is(";") || t.Is("}") || (stopAt != null && t.Is(stopAt))))
                break;
            if (t.Is("(") || t.Is("{") || t.Is("["))
                depth++;
            else if (t.Is(")") || t.Is("}") || t.Is("]"))
                depth--;
            index++;
        }
        if (index == start)
            throw new LuaSyntaxException("missing value", LuaLexer.LineOf(source, index < tokens.Count ? tokens[index].Start : source.Length));

        var firstToken = tokens[start];
        var lastToken = tokens[index - 1];
        int length = lastToken.End - firstToken.Start;
        int count = index - start;
        if (count == 1)
        {
            switch (firstToken.Kind)
            {
                case LuaTokenKind.Number:
                    return new LuaNumber((double)firstToken.Value!, firstToken.Start, length);
                case LuaTokenKind.String:
                    return new LuaString((string)firstToken.Value!, source[firstToken.Start], firstToken.Start, length);
                case LuaTokenKind.Name when firstToken.Text is "true" or "false":
                    return new LuaBoolean(firstToken.Text == "true", firstToken.Start, length);
                case LuaTokenKind.Name when firstToken.Text == "nil":
                    return new LuaNil(firstToken.Start, length);
            }
        }
        if (count == 2 && firstToken.Is("-") && lastToken.Kind == LuaTokenKind.Number)
            return new LuaNumber(-(double)lastToken.Value!, firstToken.Start, length);
        return new LuaExpression(source.Substring(firstToken.Start, length), firstToken.Start, length);
    }

    /// <summary>Index just after the '}' matching the '{' at <paramref name="open"/>.</summary>
    static int FindTableEnd(IReadOnlyList<LuaToken> tokens, int open)
    {
        int depth = 0;
        for (int i = open; i < tokens.Count; i++)
        {
            if (tokens[i].Is("{"))
                depth++;
            else if (tokens[i].Is("}") && --depth == 0)
                return i + 1;
        }
        return tokens.Count;
    }

    /// <summary>True if the table is the whole value (not the start of an expression like <c>{...}[1]</c>).</summary>
    static bool IsValueEnd(IReadOnlyList<LuaToken> tokens, int after, string? stopAt) =>
        after >= tokens.Count || tokens[after].Is(",") || tokens[after].Is(";") || tokens[after].Is("}") ||
        (stopAt != null && tokens[after].Is(stopAt));

    static LuaToken Expect(string source, IReadOnlyList<LuaToken> tokens, int index, string symbol)
    {
        if (index >= tokens.Count || !tokens[index].Is(symbol))
            throw new LuaSyntaxException($"expected '{symbol}'", LuaLexer.LineOf(source, index < tokens.Count ? tokens[index].Start : source.Length));
        return tokens[index];
    }
}
